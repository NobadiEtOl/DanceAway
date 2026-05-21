using Common.Enums;
using UnityEngine;

/// <summary>
/// Single owner of the control UI zone.
///
/// Responsibilities:
///   1. Instantiate the prefab for the active control mode under <see cref="controlRoot"/>.
///   2. Scale it to fill the zone without changing the prefab's designed aspect ratio
///      (portrait-first: zone height clamps first when the zone is taller than wide).
///   3. Centre it inside <see cref="controlRoot"/>.
///   4. Call <see cref="IControlModule.Initialize"/> on the prefab root so each prefab's
///      own controller receives its scene dependencies before the first frame.
///
/// Every control prefab carries its own controller on its root GameObject
/// (ArrowKeysController / SwipeController / JoystickBeatController) and implements
/// IControlModule — no per-mode wiring code lives here.
///
/// Call <see cref="ShowMode"/> from GameController.ApplyMovementMode / OnIntroComplete.
///
/// NOTE — Landscape support: the FitToZone method uses a Contain rule (Mathf.Min)
/// which is orientation-agnostic. If a separate landscape layout is ever needed,
/// add a FitLandscape() branch gated on Screen.width > Screen.height.
/// </summary>
public class ControlPlacementController : MonoBehaviour
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("Zone")]
    [Tooltip("The single RectTransform that defines where controls live on screen. " +
             "Position and size this in the Editor; the script handles everything inside it.")]
    [SerializeField] private RectTransform controlRoot;

    [Header("Control Prefabs")]
    [SerializeField] private GameObject arrowKeysControlPrefab;
    [SerializeField] private GameObject swipeControlPrefab;
    [SerializeField] private GameObject joystickBeatControlPrefab;

    [Header("Joystick Options")]
    [Tooltip("Mirror the joystick prefab horizontally: right side becomes joystick, left becomes beat-press.")]
    [SerializeField] private bool flipJoystickSides;

    [Header("Scene References")]
    [SerializeField] private GameController gameController;

    // =========================================================================
    // Runtime state
    // =========================================================================

    private GameObject    _active;
    private MovementMode  _currentMode;
    private bool          _isPreviewing;
    private MovementMode  _previewMode;
    private const string  FlipPrefKey = "JoystickFlipSides";

    // =========================================================================
    // Unity lifecycle
    // =========================================================================

    private void Start()
    {
        flipJoystickSides = PlayerPrefs.GetInt(FlipPrefKey, 0) == 1;
        _currentMode      = (MovementMode)PlayerPrefs.GetInt("MovementMode", (int)MovementMode.ArrowKeys);

        // Destroy any control children that may have been left in the scene hierarchy
        // (e.g. a prefab placed by hand in the Editor) so the scene always starts clean.
        if (controlRoot != null)
        {
            for (int i = controlRoot.childCount - 1; i >= 0; i--)
                Destroy(controlRoot.GetChild(i).gameObject);
        }
        _active = null;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Toggles which side the joystick sits on, persists the choice, and
    /// re-instantiates the prefab if JoystickBeat mode is currently displayed (preview or active).
    /// </summary>
    public void SetFlipJoystickSides(bool flip)
    {
        flipJoystickSides = flip;
        PlayerPrefs.SetInt(FlipPrefKey, flip ? 1 : 0);
        PlayerPrefs.Save();
        MovementMode displayed = _isPreviewing ? _previewMode : _currentMode;
        if (_active != null && displayed == MovementMode.JoystickBeat)
            ShowModeVisual(displayed);
    }

    /// <summary>
    /// Shows the prefab for <paramref name="mode"/> without changing the saved current mode.
    /// Used by UIManager to preview a control style inside an info window.
    /// </summary>
    public void PreviewMode(MovementMode mode)
    {
        _isPreviewing = true;
        _previewMode  = mode;
        ShowModeVisual(mode);
    }

    /// <summary>
    /// Restores the previously active control prefab, discarding any preview.
    /// Call this when the user dismisses an info window without accepting a new mode.
    /// </summary>
    public void RestoreCurrentMode()
    {
        _isPreviewing = false;
        ShowModeVisual(_currentMode);
    }

    /// <summary>Returns the current joystick-flip state.</summary>
    public bool GetFlipJoystickSides() => flipJoystickSides;

    /// <summary>
    /// Records <paramref name="mode"/> as the saved current mode without instantiating
    /// or destroying any prefab. Use when accepting a new mode before hiding or restoring.
    /// </summary>
    public void UpdateCurrentMode(MovementMode mode)
    {
        _currentMode  = mode;
        _isPreviewing = false;
    }

    /// <summary>
    /// Destroys the currently displayed control prefab without showing a replacement.
    /// Call when no info window is open and gameplay has not yet begun.
    /// </summary>
    public void HideControls()
    {
        _isPreviewing = false;
        if (_active != null)
        {
            Destroy(_active);
            _active = null;
        }
    }

    /// <summary>
    /// Tears down the current control prefab instance (if any), instantiates the one
    /// matching <paramref name="mode"/> under <see cref="controlRoot"/>, fits it to the
    /// zone, and wires it to its controller. Clears any active preview state.
    /// </summary>
    public void ShowMode(MovementMode mode)
    {
        _currentMode  = mode;
        _isPreviewing = false;
        ShowModeVisual(mode);
    }

    // =========================================================================
    // Internal visual swap (shared by ShowMode, PreviewMode, RestoreCurrentMode)
    // =========================================================================

    private void ShowModeVisual(MovementMode mode)
    {
        if (_active != null)
        {
            Destroy(_active);
            _active = null;
        }

        GameObject prefab = mode switch
        {
            MovementMode.ArrowKeys    => arrowKeysControlPrefab,
            MovementMode.Swipe        => swipeControlPrefab,
            MovementMode.JoystickBeat => joystickBeatControlPrefab,
            _                         => null
        };

        if (prefab == null || controlRoot == null)
        {
            if (controlRoot == null) Debug.LogWarning("[ControlPlacementController] controlRoot is not assigned.", this);
            return;
        }

        _active = Instantiate(prefab, controlRoot);

        var rt = _active.GetComponent<RectTransform>();
        if (rt == null)
        {
            Debug.LogError("[ControlPlacementController] Instantiated prefab has no RectTransform on root.", this);
            return;
        }

        if (mode == MovementMode.Swipe)
            FitToZoneStretch(rt);
        else
            FitToZone(rt);

        // Apply joystick side-flip after FitToZone so the magnitude stays correct.
        if (mode == MovementMode.JoystickBeat && flipJoystickSides)
        {
            Vector3 s = rt.localScale;
            rt.localScale = new Vector3(-s.x, s.y, s.z);
        }

        // Each prefab root carries its own IControlModule component.
        // Inject the scene-level GameController now — before Start() runs on the new object.
        var module = _active.GetComponent<IControlModule>();
        if (module != null)
            module.Initialize(gameController);
        else
            Debug.LogWarning($"[ControlPlacementController] The {mode} prefab root has no IControlModule component. " +
                             "Add ArrowKeysController / SwipeController / JoystickBeatController to the root.", this);
    }

    // =========================================================================
    // Fit algorithm
    // =========================================================================

    private void FitToZone(RectTransform rt)
    {
        // Anchor + pivot to centre so anchoredPosition = zero means centred.
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        Vector2 zone   = controlRoot.rect.size;
        Vector2 design = rt.sizeDelta;

        if (design.x <= 0f || design.y <= 0f)
        {
            Debug.LogWarning("[ControlPlacementController] Prefab root sizeDelta is zero — cannot scale to fit. " +
                             "Set the RectTransform size in the prefab to your design resolution.", this);
            return;
        }

        // "Contain" rule: uniform scale so neither axis overflows the zone.
        // In portrait (zone.y > zone.x) the height axis will clamp first, which is desired.
        // TODO landscape: gate on Screen.width > Screen.height and call FitLandscape() instead.
        float scale = Mathf.Min(zone.x / design.x, zone.y / design.y);
        rt.localScale = Vector3.one * scale;
    }

    private void FitToZoneStretch(RectTransform rt)
    {
        // Swipe controller is intentionally stretched to fill both axes of the zone.
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        Vector2 zone   = controlRoot.rect.size;
        Vector2 design = rt.sizeDelta;

        if (design.x <= 0f || design.y <= 0f)
        {
            Debug.LogWarning("[ControlPlacementController] Prefab root sizeDelta is zero — cannot stretch to fit. " +
                             "Set the RectTransform size in the prefab to your design resolution.", this);
            return;
        }

        rt.localScale = new Vector3(zone.x / design.x, zone.y / design.y, 1f);
    }
}
