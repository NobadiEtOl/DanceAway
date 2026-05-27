using UnityEngine;
using Common.Enums;

/// <summary>
/// Touch-zone controller for the ArrowKeysControl prefab.
///
/// The entire prefab RectTransform acts as one directional input zone.
/// The angle from the zone centre to the finger determines one of 8 directions.
///
/// Two input modes coexist:
///
///   Tap  — When a finger first touches the zone the direction is computed and
///           <c>player.Move()</c> is called immediately with normal beat-timing
///           scoring (S / A / B / C / F based on BeatTimer.state).
///
///   Hold — While the finger is held the direction is tracked continuously.
///           On every OnBeat event the player auto-moves in the held direction
///           for a flat 25 points with no beat-grade text shown.
///
/// Both modes call <c>player.Move()</c>; only the <c>autoMove</c> flag differs.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ArrowKeysController : MonoBehaviour, IControlModule
{
    [Header("Settings")]
    [Tooltip("Canvas-space radius around the zone centre that counts as 'no direction'.")]
    [SerializeField] private float deadzone = 20f;

    // =========================================================================
    // Runtime state
    // =========================================================================

    private GameController _gc;
    private Player         _player;
    private RectTransform  _rt;

    private int        _activeFingerId  = -1;
    private bool       _isHolding       = false;
    private Vector2Int _heldDirection   = Vector2Int.zero;
    // Raw screen position of the held finger — resampled every beat for responsiveness.
    private Vector2    _heldScreenPos   = Vector2.zero;
    // Set by a tap so the very next HandleOnBeat skips auto-move (prevents double-moving).
    private bool       _movedThisBeat   = false;
    // Locked after any valid move (tap or auto); cleared by HandleOnBeat at the next beat.
    // Prevents mid-animation tap inputs from firing a second move.
    private bool       _moveLocked      = false;

    // PC mouse fallback
    private const int MouseFingerId = 97;
    private bool      _mouseActive   = false;

    // =========================================================================
    // IControlModule
    // =========================================================================

    public void Initialize(GameController gc)
    {
        if (gc == null)
        {
            Debug.LogError("[ArrowKeysController] GameController is null — input will not work.", this);
            return;
        }
        _gc     = gc;
        _player = gc.player;
    }

    // =========================================================================
    // Unity lifecycle
    // =========================================================================

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        _moveLocked = false;
        if (GameController.beatTimer != null)
        {
            GameController.beatTimer.OnBeat  += HandleOnBeat;
            GameController.beatTimer.OffBeat += HandleOffBeat;
        }
    }

    private void OnDisable()
    {
        if (GameController.beatTimer != null)
        {
            GameController.beatTimer.OnBeat  -= HandleOnBeat;
            GameController.beatTimer.OffBeat -= HandleOffBeat;
        }
        ResetTouch();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ResetTouch();
    }

    private void Update()
    {
        if (_gc == null || !_gc.canStart || _gc.introRunning) return;

        ProcessTouches();
        if (!Application.isMobilePlatform) ProcessMouseInput();
    }

    // =========================================================================
    // Beat handlers — cycle reset on OnBeat, auto-move on OffBeat
    // =========================================================================

    /// <summary>Resets per-cycle state at the start of each beat, opening the window for new input.</summary>
    private void HandleOnBeat()
    {
        _movedThisBeat = false;
        _moveLocked    = false;
    }

    /// <summary>
    /// Fires when the beat state transitions from FarBeat → OffBeat (the scoring window closes).
    /// If the player is still holding a direction and hasn't already moved this cycle,
    /// auto-move in that direction — matching the joystick's OffBeat auto-move behaviour.
    /// </summary>
    private void HandleOffBeat()
    {
        if (_gc == null || !_gc.canStart || _gc.introRunning) return;
        if (_gc.GetPlayYourMusicMode()) return;  // PYM mode: manual input only, no auto-move
        if (!_isHolding) return;
        if (_movedThisBeat) return;

        // Recompute direction from the live finger position at beat time so the
        // move always reflects where the finger actually is, not a cached value.
        Vector2Int dir = ComputeDirection(_heldScreenPos);
        _heldDirection = dir;
        if (dir != Vector2Int.zero)
            ExecuteAutoMove(dir);
    }

    // =========================================================================
    // Touch processing
    // =========================================================================

    private void ProcessTouches()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            ProcessContact(t.fingerId, t.position, t.phase);
        }
    }

    private void ProcessContact(int fingerId, Vector2 screenPos, TouchPhase phase)
    {
        bool inCalibration = _gc != null && _gc.inCalibration;

        switch (phase)
        {
            case TouchPhase.Began:
                if (_activeFingerId == -1 && IsInsideControl(screenPos))
                {
                    _activeFingerId = fingerId;
                    _isHolding      = true;
                    _heldScreenPos  = screenPos;
                    Vector2Int dir  = ComputeDirection(screenPos);
                    _heldDirection  = dir;
                    if (dir != Vector2Int.zero && (!_moveLocked || inCalibration))
                        ExecuteImmediate(dir);
                }
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (fingerId == _activeFingerId)
                {
                    _heldScreenPos = screenPos;
                    Vector2Int dir = ComputeDirection(screenPos);
                    _heldDirection = dir;
                    if (dir != Vector2Int.zero)
                        _player.SetFacingDirection(dir);
                }
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                if (fingerId == _activeFingerId)
                    ResetTouch();
                break;
        }
    }

    // =========================================================================
    // Move execution
    // =========================================================================

    /// <summary>Immediate tap — goes through the regular beat-timing scoring flow.</summary>
    private void ExecuteImmediate(Vector2Int dir)
    {
        bool inCalibration = _gc != null && _gc.inCalibration;

        // Guard: if the player already moved in this beat window (e.g. a previous tap
        // whose ResetTouch did NOT clear _movedThisBeat), swallow the input so only
        // one move ever fires per beat.
        if (_movedThisBeat && !inCalibration) return;
        Debug.Log($"[PlayerAnimationChecks] ArrowKeys ExecuteImmediate — dir={dir}");
        if (!inCalibration)
        {
            _movedThisBeat = true; // Prevent HandleOnBeat from also moving on this beat.
            _moveLocked    = true; // Block further taps until the next beat unlocks input.
        }
        _player.SetFacingDirection(dir);
        _player.Move(dir);
    }

    /// <summary>Beat-timed auto-move — always succeeds, awards a flat 25 points.</summary>
    private void ExecuteAutoMove(Vector2Int dir)
    {
        Debug.Log($"[PlayerAnimationChecks] ArrowKeys ExecuteAutoMove — dir={dir}");
        _moveLocked = true; // Lock until the next beat so tap input during animation is ignored.
        _player.SetFacingDirection(dir);
        _player.Move(dir, autoMove: true);
    }

    // =========================================================================
    // Direction computation — 8 sectors of 45°
    // =========================================================================

    private Vector2Int ComputeDirection(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rt, screenPos, null, out Vector2 localPos);

        // Shift so (0,0) is the visual centre of the ArrowKeys rect,
        // regardless of where the pivot is placed on the prefab.
        Rect r = _rt.rect;
        localPos -= new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

        if (localPos.magnitude < deadzone)
            return Vector2Int.zero;

        float angle  = Mathf.Atan2(localPos.y, localPos.x) * Mathf.Rad2Deg;
        int   sector = (int)Mathf.Round(angle / 45f);
        sector = ((sector % 8) + 8) % 8;
        return SectorDirections[sector];
    }

    private static readonly Vector2Int[] SectorDirections =
    {
        new Vector2Int( 1,  0),  // 0: E  (right)
        new Vector2Int( 1,  1),  // 1: NE
        new Vector2Int( 0,  1),  // 2: N  (up)
        new Vector2Int(-1,  1),  // 3: NW
        new Vector2Int(-1,  0),  // 4: W  (left)
        new Vector2Int(-1, -1),  // 5: SW
        new Vector2Int( 0, -1),  // 6: S  (down)
        new Vector2Int( 1, -1),  // 7: SE
    };

    // =========================================================================
    // Helpers
    // =========================================================================

    private bool IsInsideControl(Vector2 screenPos)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(_rt, screenPos);
    }

    private void ResetTouch()
    {
        _activeFingerId = -1;
        _isHolding      = false;
        _heldDirection  = Vector2Int.zero;
        _heldScreenPos  = Vector2.zero;
        // _movedThisBeat is intentionally NOT reset here.
        // It must only be cleared by HandleOnBeat() at the start of the next beat
        // so that lifting and re-tapping within the same beat window cannot fire a
        // second move and desync the logical tile from the physics position.
        _mouseActive    = false;
    }

    // =========================================================================
    // PC mouse fallback (Editor / desktop builds)
    // =========================================================================

    private void ProcessMouseInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mpos = Input.mousePosition;
            if (_activeFingerId == -1 && IsInsideControl(mpos))
            {
                _activeFingerId = MouseFingerId;
                _mouseActive    = true;
                _isHolding      = true;
                _heldScreenPos  = mpos;
                Vector2Int dir  = ComputeDirection(mpos);
                _heldDirection  = dir;
                if (dir != Vector2Int.zero && !_moveLocked)
                    ExecuteImmediate(dir);
            }
        }
        else if (Input.GetMouseButton(0) && _mouseActive)
        {
            _heldScreenPos = Input.mousePosition;
            Vector2Int dir = ComputeDirection(_heldScreenPos);
            _heldDirection = dir;
            if (dir != Vector2Int.zero)
                _player.SetFacingDirection(dir);
        }
        else if (Input.GetMouseButtonUp(0) && _mouseActive)
        {
            ResetTouch();
        }
    }
}
