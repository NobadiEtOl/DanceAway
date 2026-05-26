using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the bottom-half console UI: which element group is visible and the
/// slide-in / slide-out animation that plays on every state transition.
///
/// The bottom-half image (bottomHalfRect) and the active element group move
/// together by the same anchoredPosition delta, so the hierarchy never needs
/// to be restructured. Positions are cached at Awake so every element always
/// returns to its exact original canvas position after a transition.
///
/// Call the Show* methods from GameController, BeginController, and
/// TutorialController.
/// </summary>
public class ConsoleBottomHalfController : MonoBehaviour
{
    // ── Element Groups ────────────────────────────────────────────────────────
    [Header("Bottom Half Elements")]
    [Tooltip("Elements shown while the game is loading (cartridge/CD slot visuals, etc.)")]
    [SerializeField] private List<GameObject> loadingElements;

    [Tooltip("Elements shown on the start screen (Start and Tutorial buttons)")]
    [SerializeField] private List<GameObject> startScreenElements;

    [Tooltip("Elements shown while the tutorial is open (Back and Forward buttons)")]
    [SerializeField] private List<GameObject> tutorialElements;

    [Header("Gameplay Controls")]
    [Tooltip("The GameObject that contains the spawned input-control prefab. Activated only during gameplay.")]
    [SerializeField] private GameObject inputControlHolder;
    [Tooltip("Handles spawning and resizing of the correct control prefab inside inputControlHolder.")]
    [SerializeField] private ControlPlacementController controlPlacement;

    // ── Bottom Half Visual ────────────────────────────────────────────────────
    [Header("Bottom Half Image")]
    [Tooltip("RectTransform of the bottom-half console image that slides in and out.")]
    [SerializeField] private RectTransform bottomHalfRect;
    [Tooltip("Image component on bottomHalfRect whose sprite is swapped per state.")]
    [SerializeField] private Image bottomHalfImage;
    [Tooltip("Sprite shown on the bottom half during the loading state.")]
    [SerializeField] private Sprite loadingSprite;
    [Tooltip("Sprite shown on the bottom half during the start-screen state.")]
    [SerializeField] private Sprite startScreenSprite;
    [Tooltip("Sprite shown on the bottom half during the tutorial state.")]
    [SerializeField] private Sprite tutorialSprite;
    [Tooltip("Sprite shown on the bottom half during gameplay.")]
    [SerializeField] private Sprite gameplaySprite;

    // ── Animation ─────────────────────────────────────────────────────────────
    [Header("Animation")]
    [Tooltip("How far (canvas units) the bottom half slides down to go off-screen.")]
    [SerializeField] private float slideDistance = 400f;
    [Tooltip("Duration in seconds of each slide-in or slide-out animation.")]
    [SerializeField] private float slideDuration = 0.35f;
    [Tooltip("Easing curve applied to the slide. Defaults to ease-in-out when left empty.")]
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── Private State ─────────────────────────────────────────────────────────
    private enum BottomHalfState { None, Loading, StartScreen, Tutorial, Gameplay }

    private BottomHalfState _currentState = BottomHalfState.None;
    private Coroutine _transition;
    private Dictionary<RectTransform, Vector2> _cachedPositions;
    private Dictionary<RectTransform, Transform> _originalParents;
    private Vector2 _bottomHalfRestPos;
    private Vector2 _originalBottomHalfPivot;

    // ── Control-preview state (info-window overlay) ────────────────────────────
    /// <summary>True while the bottom half is temporarily showing a control prefab
    /// in response to an info window opening (not actual gameplay).</summary>
    private bool _isPreviewingControl = false;
    /// <summary>The state to return to once the info window closes.</summary>
    private BottomHalfState _stateBeforePreview = BottomHalfState.None;
    /// <summary>When true, HideControls() is called at the end of the next transition
    /// so the prefab remains visible during the slide-out animation.</summary>
    private bool _cleanupControlAfterTransition = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        CachePositions();
        if (bottomHalfRect != null)
        {
            _bottomHalfRestPos = bottomHalfRect.anchoredPosition;
            _originalBottomHalfPivot = bottomHalfRect.pivot;
        }
    }

    void Start()
    {
        // Begin off-screen; ShowLoading will animate it into place.
        if (bottomHalfRect != null)
            bottomHalfRect.anchoredPosition = _bottomHalfRestPos + Vector2.down * slideDistance;
        DeactivateAll();
        ShowLoading();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ShowLoading()     => TransitionTo(BottomHalfState.Loading);

    public void ShowStartScreen()
    {
        // If a control showcase is active, just update the return-to state so
        // HideControlPreview() arrives at StartScreen instead of Loading.
        // Do not interrupt the showcase — it will transition to StartScreen on its own.
        if (_isPreviewingControl)
        {
            _stateBeforePreview = BottomHalfState.StartScreen;
            return;
        }
        TransitionTo(BottomHalfState.StartScreen);
    }

    public void ShowTutorial()
    {
        Debug.Log("[CBHC] ShowTutorial called");
        TransitionTo(BottomHalfState.Tutorial);
    }

    /// <summary>
    /// Slides the bottom half off-screen and leaves it there for the rest of the session.
    /// Any in-progress transition is cancelled first.
    /// </summary>
    public void ShowEndScreen()
    {
        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(SlideOutPermanently());
    }

    private IEnumerator SlideOutPermanently()
    {
        // If elements are currently visible, reparent them so they follow the panel during the slide.
        if (_currentState != BottomHalfState.None)
        {
            ActivateState(_currentState);
            var rts = GetRTs(_currentState);
            SetBeatBouncersEnabled(rts, false);
            ReparentAll(rts, bottomHalfRect, false);
        }

        if (bottomHalfRect != null)
        {
            Vector2 startPos = bottomHalfRect.anchoredPosition;
            Vector2 endPos   = _bottomHalfRestPos + Vector2.down * slideDistance;
            for (float t = 0f; t < slideDuration; t += Time.unscaledDeltaTime)
            {
                float eval = EvaluateCurve(Mathf.Clamp01(t / slideDuration));
                bottomHalfRect.anchoredPosition = Vector2.Lerp(startPos, endPos, eval);
                yield return null;
            }
            bottomHalfRect.anchoredPosition = endPos;
        }

        DeactivateAll();
        _currentState = BottomHalfState.None;
        _transition   = null;
    }

    /// <summary>
    /// Slides the input control holder into view and restores the last-used
    /// control prefab. All button groups are hidden during gameplay.
    /// Clears any pending control-preview state so actual gameplay always wins.
    /// </summary>
    public void ShowGameplay()
    {
        _isPreviewingControl           = false;
        _cleanupControlAfterTransition = false;
        _stateBeforePreview            = BottomHalfState.None;
        TransitionTo(BottomHalfState.Gameplay);
    }

    /// <summary>
    /// Temporarily slides the bottom half to show the control prefab while the
    /// player reads an input-mode info window.  Has no effect when actual
    /// gameplay is already active or a preview is already showing.
    /// Call this AFTER ControlPlacementController.PreviewMode() so the prefab
    /// is already spawned when the animation begins.
    /// </summary>
    public void ShowControlPreview()
    {
        if (_isPreviewingControl || _currentState == BottomHalfState.Gameplay) return;
        _stateBeforePreview  = _currentState != BottomHalfState.None
            ? _currentState
            : BottomHalfState.StartScreen;
        _isPreviewingControl = true;
        TransitionTo(BottomHalfState.Gameplay);
    }

    /// <summary>
    /// Slides the bottom half back to the state it had before
    /// <see cref="ShowControlPreview"/> was called.  The control prefab is
    /// destroyed only after the slide-out animation completes so it remains
    /// visible throughout the transition.
    /// </summary>
    /// <returns>
    /// <c>true</c> if a preview was active and cleanup has been deferred —
    /// the caller should skip an immediate HideControls call.
    /// <c>false</c> if no preview was running — the caller handles cleanup.
    /// </returns>
    public bool HideControlPreview()
    {
        if (!_isPreviewingControl) return false;
        _isPreviewingControl           = false;
        _cleanupControlAfterTransition = true;
        BottomHalfState target = _stateBeforePreview != BottomHalfState.None
            ? _stateBeforePreview
            : BottomHalfState.StartScreen;
        _stateBeforePreview = BottomHalfState.None;
        TransitionTo(target);
        return true;
    }

    // ── Transition ────────────────────────────────────────────────────────────

    private void TransitionTo(BottomHalfState next)
    {
        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(DoTransition(next));
    }

    private IEnumerator DoTransition(BottomHalfState next)
    {
        // ── Snap any interrupted animation to a clean baseline ────────────────
        // If a previous transition was stopped mid-slide, restore everything to
        // its exact rest position and original parent before starting a new one.
        if (_currentState != BottomHalfState.None)
        {
            var snapRTs = GetRTs(_currentState);
            foreach (var rt in snapRTs)
                if (_cachedPositions.TryGetValue(rt, out Vector2 rest))
                    rt.anchoredPosition = rest;
            RestoreParents(snapRTs);
        }

        if (bottomHalfRect != null)
        {
            bottomHalfRect.pivot = _originalBottomHalfPivot;
            bottomHalfRect.localEulerAngles = Vector3.zero;
            bottomHalfRect.anchoredPosition = _bottomHalfRestPos;
        }

        // ── Slide OUT (skipped on the very first transition) ──────────────────
        // Declared outside the if-block so the swap section can reference outRTs.
        List<RectTransform> outRTs = new List<RectTransform>();
        if (_currentState != BottomHalfState.None)
        {
            // Re-activate current state elements so they remain visible during
            // the slide-out — external code may have deactivated them early.
            ActivateState(_currentState);

            outRTs = GetRTs(_currentState);
            // Disable BeatBouncers so the bounce animation cannot fight the slide.
            SetBeatBouncersEnabled(outRTs, false);
            // Parent to bottomHalfRect — elements follow it through both the slide
            // and the rotation automatically. worldPositionStays=false is safe
            // because both are stretch-fill with the same coordinate space at rest.
            ReparentAll(outRTs, bottomHalfRect, false);
            for (float t = 0f; t < slideDuration; t += Time.unscaledDeltaTime)
            {
                float eval = EvaluateCurve(Mathf.Clamp01(t / slideDuration));
                if (bottomHalfRect != null)
                    bottomHalfRect.anchoredPosition =
                        Vector2.Lerp(_bottomHalfRestPos, _bottomHalfRestPos + Vector2.down * slideDistance, eval);
                yield return null;   // outRTs follow bottomHalfRect as children
            }
            // Snap fully off-screen.
            if (bottomHalfRect != null)
                bottomHalfRect.anchoredPosition = _bottomHalfRestPos + Vector2.down * slideDistance;
            // outRTs are children — no per-element snap needed.
        }

        // ── Swap ──────────────────────────────────────────────────────────────
        // Elements are now fully off-screen — safe to deactivate and swap.
        DeactivateAll();

        // Restore old elements to rest positions and back to TopHalf while invisible.
        if (_currentState != BottomHalfState.None)
        {
            foreach (var rt in outRTs)
                if (_cachedPositions.TryGetValue(rt, out Vector2 rest))
                    rt.anchoredPosition = rest;
            RestoreParents(outRTs);
        }

        // Swap the bottom-half sprite.
        if (bottomHalfImage != null)
            bottomHalfImage.sprite = GetSpriteForState(next);

        // Cache incoming RTs now so the same list drives pre-shifting,
        // BeatBouncer management, animation, and re-enabling.
        List<RectTransform> inRTs = GetRTs(next);

        // Set inRTs to their rest positions in bottomHalfRect's coordinate space.
        // While bottomHalfRect is off-screen, children are off-screen too;
        // when it slides to rest they automatically arrive at the correct position.
        foreach (var rt in inRTs)
            if (_cachedPositions.TryGetValue(rt, out Vector2 rest))
                rt.anchoredPosition = rest;
        if (bottomHalfRect != null) ReparentAll(inRTs, bottomHalfRect, false);

        // Disable BeatBouncers before activation so they cannot override the
        // pre-shifted position on the very first FixedUpdate after going active.
        SetBeatBouncersEnabled(inRTs, false);

        // Prepare bottomHalfRect for the enter sequence.
        if (bottomHalfRect != null)
        {
            if (_currentState == BottomHalfState.None)
            {
                // Very first transition: slide in from below.
                bottomHalfRect.pivot = _originalBottomHalfPivot;
                bottomHalfRect.localEulerAngles = Vector3.zero;
                bottomHalfRect.anchoredPosition = _bottomHalfRestPos + Vector2.down * slideDistance;
            }
        }

        // Activate incoming elements (they are off-screen at this point).
        ActivateState(next);

        // For gameplay, spawn the control prefab now so it is ready when the
        // holder slides into view.  Skip when a control preview is active —
        // UIManager has already called PreviewMode() with the correct prefab.
        if (next == BottomHalfState.Gameplay && !_isPreviewingControl)
            controlPlacement?.RestoreCurrentMode();

        // ── Slide IN ──────────────────────────────────────────────────────────

        for (float t = 0f; t < slideDuration; t += Time.unscaledDeltaTime)
        {
            float eval = EvaluateCurve(Mathf.Clamp01(t / slideDuration));
            if (bottomHalfRect != null)
                bottomHalfRect.anchoredPosition =
                    Vector2.Lerp(_bottomHalfRestPos + Vector2.down * slideDistance, _bottomHalfRestPos, eval);
            yield return null;   // inRTs follow bottomHalfRect as children
        }

        // Snap bottomHalfRect to exact rest position — no floating-point drift.
        // inRTs are children with anchoredPosition=rest, so they are correct too.
        if (bottomHalfRect != null)
            bottomHalfRect.anchoredPosition = _bottomHalfRestPos;

        // Return elements to TopHalf. Since bottomHalfRect is at rest and both
        // share the same coordinate space, worldPositionStays=false is correct.
        RestoreParents(inRTs);

        // Elements are now at their true rest positions. Refresh BeatBouncer
        // origins before re-enabling so they bounce from the correct baseline.
        RefreshBeatBouncers(inRTs);
        SetBeatBouncersEnabled(inRTs, true);

        _currentState = next;
        _transition   = null;

        // Deferred cleanup: destroy the control prefab now that it is fully
        // off-screen.  Called here (end of transition) so the prefab stays
        // visible throughout the slide-out animation.
        if (_cleanupControlAfterTransition)
        {
            _cleanupControlAfterTransition = false;
            controlPlacement?.HideControls();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Caches the anchoredPosition of every managed RectTransform at scene load.</summary>
    private void CachePositions()
    {
        _cachedPositions = new Dictionary<RectTransform, Vector2>();
        _originalParents  = new Dictionary<RectTransform, Transform>();
        CacheList(loadingElements);
        CacheList(startScreenElements);
        CacheList(tutorialElements);
        if (inputControlHolder != null)
        {
            var rt = inputControlHolder.GetComponent<RectTransform>();
            if (rt != null && !_cachedPositions.ContainsKey(rt))
            {
                _cachedPositions[rt] = rt.anchoredPosition;
                _originalParents[rt]  = rt.parent;
            }
        }
    }

    private void CacheList(List<GameObject> list)
    {
        if (list == null) return;
        foreach (var obj in list)
        {
            if (obj == null) continue;
            var rt = obj.GetComponent<RectTransform>();
            if (rt != null && !_cachedPositions.ContainsKey(rt))
            {
                _cachedPositions[rt] = rt.anchoredPosition;
                _originalParents[rt]  = rt.parent;
            }
        }
    }

    /// <summary>Returns the RectTransforms that slide together for a given state.</summary>
    private List<RectTransform> GetRTs(BottomHalfState state)
    {
        var rts = new List<RectTransform>();
        List<GameObject> objs;
        switch (state)
        {
            case BottomHalfState.Loading:     objs = loadingElements;     break;
            case BottomHalfState.StartScreen: objs = startScreenElements; break;
            case BottomHalfState.Tutorial:    objs = tutorialElements;     break;
            case BottomHalfState.Gameplay:
                if (inputControlHolder != null)
                {
                    var rt = inputControlHolder.GetComponent<RectTransform>();
                    if (rt != null) rts.Add(rt);
                }
                return rts;
            default: return rts;
        }
        if (objs == null) return rts;
        foreach (var obj in objs)
        {
            if (obj == null) continue;
            var rt = obj.GetComponent<RectTransform>();
            if (rt != null) rts.Add(rt);
        }
        return rts;
    }

    private Sprite GetSpriteForState(BottomHalfState state) => state switch
    {
        BottomHalfState.Loading     => loadingSprite,
        BottomHalfState.StartScreen => startScreenSprite,
        BottomHalfState.Tutorial    => tutorialSprite,
        BottomHalfState.Gameplay    => gameplaySprite,
        _                           => null
    };

    private void ActivateState(BottomHalfState state)
    {
        switch (state)
        {
            case BottomHalfState.Loading:
                foreach (var obj in loadingElements)     if (obj != null) obj.SetActive(true);
                break;
            case BottomHalfState.StartScreen:
                foreach (var obj in startScreenElements) if (obj != null) obj.SetActive(true);
                break;
            case BottomHalfState.Tutorial:
                foreach (var obj in tutorialElements)    if (obj != null) obj.SetActive(true);
                break;
            case BottomHalfState.Gameplay:
                if (inputControlHolder != null) inputControlHolder.SetActive(true);
                break;
        }
    }

    private void DeactivateAll()
    {
        Deactivate(loadingElements);
        Deactivate(startScreenElements);
        Deactivate(tutorialElements);
        if (inputControlHolder != null) inputControlHolder.SetActive(false);
    }

    private void Deactivate(List<GameObject> list)
    {
        if (list == null) return;
        foreach (var obj in list)
            if (obj != null) obj.SetActive(false);
    }

    private float EvaluateCurve(float t)
    {
        return (slideCurve != null && slideCurve.length > 0)
            ? slideCurve.Evaluate(t)
            : Mathf.SmoothStep(0f, 1f, t);
    }

    /// <summary>
    /// Changes <paramref name="rt"/>'s pivot to <paramref name="newPivot"/> while
    /// adjusting anchoredPosition so the visual position does not jump.
    /// Only accurate when the transform's local rotation is 0°.
    /// </summary>
    private void SetPivotWithPositionCompensation(RectTransform rt, Vector2 newPivot)
    {
        Vector2 deltaPivot = newPivot - rt.pivot;
        rt.anchoredPosition += new Vector2(deltaPivot.x * rt.rect.width, deltaPivot.y * rt.rect.height);
        rt.pivot = newPivot;
    }

    /// <summary>
    /// Moves all RectTransforms to <paramref name="newParent"/>.
    /// Use worldPositionStays=false when parent coordinate spaces are identical
    /// (TopHalf stretched to fill Canvas), so no position conversion is needed.
    /// </summary>
    private void ReparentAll(List<RectTransform> rts, Transform newParent, bool worldPositionStays)
    {
        foreach (var rt in rts)
            rt.SetParent(newParent, worldPositionStays);
    }

    /// <summary>
    /// Returns each RectTransform to the parent it had at scene load.
    /// The element's anchoredPosition must already be correct for the
    /// original parent's coordinate space before this is called.
    /// </summary>
    private void RestoreParents(List<RectTransform> rts)
    {
        foreach (var rt in rts)
            if (_originalParents.TryGetValue(rt, out Transform parent))
                rt.SetParent(parent, false);
    }

    /// <summary>Enables or disables every BeatBouncer on the supplied RectTransforms.</summary>
    private void SetBeatBouncersEnabled(List<RectTransform> rts, bool enabled)
    {
        foreach (var rt in rts)
        {
            var bb = rt.GetComponent<BeatBouncer>();
            if (bb != null) bb.enabled = enabled;
        }
    }

    /// <summary>
    /// Calls RefreshOriginalPosition on every BeatBouncer in the list so that
    /// the bouncer's stored rest position matches the element's current anchoredPosition.
    /// </summary>
    private void RefreshBeatBouncers(List<RectTransform> rts)
    {
        foreach (var rt in rts)
        {
            var bb = rt.GetComponent<BeatBouncer>();
            if (bb != null) bb.RefreshOriginalPosition();
        }
    }
}
