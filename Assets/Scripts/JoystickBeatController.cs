using UnityEngine;
using Common.Enums;

/// <summary>
/// Split-screen joystick + beat-button controller ("Split Control" mode).
///
/// The prefab hierarchy is:
///   JoystickController (this script)
///   ├── Beatbutton       ← beatButtonRt   — tap anywhere here to execute a move
///   └── JoystickBase     ← joystickBaseRt — fixed base visual; knob tracks the finger
///       └── JoystickKnob ← joystickKnobRt — moves within the base radius
///
/// Default layout (localScale 1,1,1): joystick on the RIGHT, beat button on the LEFT.
/// To flip sides, negate the parent's X scale — no logic changes are required.
///
/// The "joystick side" is determined at runtime by the screen-space position of
/// JoystickBase, so flipping the parent automatically flips which half is which.
/// </summary>
public class JoystickBeatController : MonoBehaviour, IControlModule
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [SerializeField] private Player         player;
    [SerializeField] private GameController gameController;

    [Header("Joystick Side")]
    [Tooltip("The fixed base visual — child of the prefab root named JoystickBase.")]
    [SerializeField] private RectTransform joystickBaseRt;
    [Tooltip("The knob that tracks the finger — child of JoystickBase named JoystickKnob.")]
    [SerializeField] private RectTransform joystickKnobRt;
    [Tooltip("Minimum canvas-space displacement from centre before a direction is registered.")]
    [SerializeField] private float joystickDeadzone = 20f;

    [Header("Beat Button Side")]
    [Tooltip("RectTransform of the Beatbutton child. Any tap on this side executes a move.")]
    [SerializeField] private RectTransform beatButtonRt;

    [Header("Beat Zone Ripple")]
    [Tooltip("Sprite for the expanding ripple ring spawned on each beat tick while this mode is active.")]
    [SerializeField] private Sprite beatZoneSprite;
    [SerializeField] private Color  beatZoneColor          = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private float  beatZoneExpandScale    = 2.5f;
    [SerializeField, Range(0f, 1f)]
    private float                   beatZoneStartAlpha     = 0.6f;
    [SerializeField] private float  beatZoneRippleDuration = 0.35f;

    // =========================================================================
    // Runtime state
    // =========================================================================

    private BeatZonePulse beatZonePulse;

    private int        joystickFingerId   = -1;
    private Vector2Int currentDirection   = Vector2Int.zero;
    private BeatState  capturedBeatState;

    // Tracks whether a move was already executed in the current beat cycle.
    // Prevents the OffBeat auto-move from firing after the beat button was already pressed.
    private bool moveExecutedThisCycle;

    // True from the moment any valid move fires until the next OnBeat.
    // Blocks all beat-button input while the player is animating to the new tile,
    // preventing mid-animation presses from causing a tile/model position desync.
    private bool _moveLocked;

    // PC mouse / keyboard emulation
    private const int MouseJoystickId    = 97;
    private bool      mouseJoystickActive;
    private bool      keyboardJoystickActive;

    // =========================================================================
    // IControlModule
    // =========================================================================

    /// <inheritdoc/>
    public void Initialize(GameController gc)
    {
        if (gc == null)
        {
            Debug.LogError("[JoystickBeatController] GameController is null.", this);
            return;
        }
        gameController = gc;
        player         = gc.player;
    }

    // =========================================================================
    // Lifecycle
    // =========================================================================

    private void Awake()
    {
        // Add BeatZonePulse to the beat button so it pulses on every beat tick.
        if (beatButtonRt != null)
        {
            beatZonePulse         = beatButtonRt.gameObject.AddComponent<BeatZonePulse>();
            beatZonePulse.enabled = false;
            beatZonePulse.Initialize(beatZoneSprite, beatZoneColor, 0f,
                                     beatZoneExpandScale, beatZoneStartAlpha, beatZoneRippleDuration);
        }
    }

    private void OnEnable()
    {
        _moveLocked = false;  // ensure fresh state when the module is enabled
        ResetJoystick();
        if (beatZonePulse != null) beatZonePulse.enabled = true;
        if (GameController.beatTimer != null)
        {
            GameController.beatTimer.OnBeat  += OnBeatCycleStart;
            GameController.beatTimer.OffBeat += OnOffBeatAutoMove;
        }
    }

    private void OnDisable()
    {
        ResetJoystick();
        if (beatZonePulse != null) beatZonePulse.enabled = false;
        if (GameController.beatTimer != null)
        {
            GameController.beatTimer.OnBeat  -= OnBeatCycleStart;
            GameController.beatTimer.OffBeat -= OnOffBeatAutoMove;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ResetJoystick();
    }

    private void Update()
    {
        // Guard: gameController is injected by IControlModule.Initialize; skip if not yet set.
        if (gameController == null) return;
        if (!gameController.canStart || gameController.introRunning) return;

        ProcessTouches();

        if (!Application.isMobilePlatform)
        {
            ProcessMouseInput();
            ProcessKeyboardInput();
        }
    }

    // =========================================================================
    // Side detection
    // =========================================================================

    /// <summary>
    /// Returns true when <paramref name="screenPos"/> is on the same screen half as JoystickBase.
    /// Works correctly when the parent is X-flipped to swap sides.
    /// </summary>
    private bool IsJoystickSide(Vector2 screenPos)
    {
        bool joystickIsOnLeft = joystickBaseRt != null
            ? joystickBaseRt.position.x < Screen.width * 0.5f
            : screenPos.x >= Screen.width * 0.5f; // fallback: default right side
        bool touchIsOnLeft = screenPos.x < Screen.width * 0.5f;
        return touchIsOnLeft == joystickIsOnLeft;
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

    private void ProcessContact(int fingerId, Vector2 position, TouchPhase phase)
    {
        bool inCalibration = gameController != null && gameController.inCalibration;

        switch (phase)
        {
            case TouchPhase.Began:
                if (IsJoystickSide(position))
                {
                    // Only one joystick finger at a time.
                    if (joystickFingerId == -1)
                    {
                        joystickFingerId = fingerId;
                        UpdateDirection(position);
                    }
                }
                else
                {
                    // Beat button side: tap executes the current move.
                    if (currentDirection != Vector2Int.zero && (!_moveLocked || inCalibration))
                    {
                        capturedBeatState = GameController.beatTimer.state;
                        ExecuteMove();
                    }
                }
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (fingerId == joystickFingerId)
                    UpdateDirection(position);
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                if (fingerId == joystickFingerId)
                    ResetJoystick();
                break;
        }
    }

    // =========================================================================
    // Direction — knob tracks finger, clamped to base radius
    // =========================================================================

    private void UpdateDirection(Vector2 touchPos)
    {
        if (joystickBaseRt == null || joystickKnobRt == null) return;

        // Convert screen-space touch to the local coordinate space of JoystickBase.
        // (0,0) in local space = pivot = centre of the base, regardless of canvas scale.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            joystickBaseRt, touchPos, null, out Vector2 localPos);

        if (localPos.magnitude < joystickDeadzone)
        {
            currentDirection                = Vector2Int.zero;
            joystickKnobRt.anchoredPosition = Vector2.zero;
            return;
        }

        float radius = joystickBaseRt.rect.width * 0.5f;
        joystickKnobRt.anchoredPosition = Vector2.ClampMagnitude(localPos, radius);

        float angle  = Mathf.Atan2(localPos.y, localPos.x) * Mathf.Rad2Deg;
        int   sector = (int)Mathf.Round(angle / 45f);
        sector = ((sector % 8) + 8) % 8;

        currentDirection = SectorDirections[sector];
        if (currentDirection != Vector2Int.zero)
            player.SetFacingDirection(currentDirection);
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
    // Move execution
    // =========================================================================

    private void ExecuteMove(bool isAutoMove = false)
    {
        if (currentDirection == Vector2Int.zero) return;
        bool inCalibration = gameController != null && gameController.inCalibration;

        Vector2Int dir = currentDirection;
        Debug.Log($"[PlayerAnimationChecks] Joystick ExecuteMove — dir={dir} isAutoMove={isAutoMove} moveExecutedThisCycle={moveExecutedThisCycle}");
        player.SetFacingDirection(dir);

        // If the player already executed a manual move this beat, treat any additional
        // press as OffBeat: player.Move still runs (so "F" feedback + bounce fire) but
        // validMove will be false inside Player so the tile position never changes.
        if (!inCalibration && !isAutoMove && moveExecutedThisCycle)
        {
            player.Move(dir, overrideState: BeatState.OffBeat);
            return;
        }

        if (!inCalibration)
        {
            moveExecutedThisCycle = true;
            _moveLocked           = true;  // lock until next OnBeat so mid-animation input is ignored
        }
        player.Move(dir, overrideState: isAutoMove ? (BeatState?)null : capturedBeatState, autoMove: isAutoMove);
    }

    // =========================================================================
    // Beat-driven auto-move
    // =========================================================================

    /// <summary>
    /// Resets the per-cycle move flag at the start of each beat, opening the window
    /// for a new beat-button press or a new OffBeat auto-move.
    /// </summary>
    private void OnBeatCycleStart()
    {
        moveExecutedThisCycle = false;
        _moveLocked           = false;  // unlock beat-button input for the new beat window
    }

    /// <summary>
    /// Fires when the beat state transitions from FarBeat → OffBeat (the scoring window closes).
    /// If the player is still holding a direction and hasn't already executed a move this cycle,
    /// auto-move in that direction with an OffBeat rating.
    /// </summary>
    private void OnOffBeatAutoMove()
    {
        if (gameController == null || !gameController.canStart || gameController.introRunning) return;
        if (gameController.GetPlayYourMusicMode()) return;  // PYM mode: manual input only, no auto-move
        if (moveExecutedThisCycle) return;
        if (currentDirection == Vector2Int.zero) return;

        ExecuteMove(isAutoMove: true);
    }

    // =========================================================================
    // State reset
    // =========================================================================

    private void ResetJoystick()
    {
        joystickFingerId       = -1;
        currentDirection       = Vector2Int.zero;
        mouseJoystickActive    = false;
        keyboardJoystickActive = false;
        // moveExecutedThisCycle is intentionally NOT reset here.
        // It must only be cleared by OnBeatCycleStart() so that releasing and
        // re-engaging the joystick within the same beat cannot trigger a second
        // auto-move via OnOffBeatAutoMove and cause a tile/model desync.
        if (joystickKnobRt != null)
            joystickKnobRt.anchoredPosition = Vector2.zero;
    }

    // =========================================================================
    // PC mouse + keyboard fallback (non-mobile platforms)
    // =========================================================================

    private void ProcessMouseInput()
    {
        bool inCalibration = gameController != null && gameController.inCalibration;

        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mpos = Input.mousePosition;
            if (IsJoystickSide(mpos))
            {
                if (joystickFingerId == -1 && !keyboardJoystickActive)
                {
                    joystickFingerId    = MouseJoystickId;
                    mouseJoystickActive = true;
                    UpdateDirection(mpos);
                }
            }
            else
            {
                // Click on beat button side.
                if (currentDirection != Vector2Int.zero && (!_moveLocked || inCalibration))
                {
                    capturedBeatState = GameController.beatTimer.state;
                    ExecuteMove();
                }
            }
        }
        else if (Input.GetMouseButton(0) && mouseJoystickActive)
        {
            UpdateDirection(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0) && mouseJoystickActive)
        {
            ResetJoystick();
        }

        // Right mouse button: beat press shortcut.
        if (Input.GetMouseButtonDown(1) && joystickFingerId != -1 && currentDirection != Vector2Int.zero && !_moveLocked)
        {
            capturedBeatState = GameController.beatTimer.state;
            ExecuteMove();
        }
    }

    /// <summary>
    /// Keyboard fallback for JoystickBeat on PC:
    ///   WASD (held) = set joystick direction.
    ///   Space (down) = beat press — execute the move.
    /// Touch/mouse input takes priority; keyboard is ignored while they are active.
    /// </summary>
    private void ProcessKeyboardInput()
    {
        if (joystickFingerId != -1)
        {
            if (keyboardJoystickActive)
                keyboardJoystickActive = false;
            return;
        }

        int kx = 0, ky = 0;
        if (Input.GetKey(KeyCode.A)) kx--;
        if (Input.GetKey(KeyCode.D)) kx++;
        if (Input.GetKey(KeyCode.W)) ky++;
        if (Input.GetKey(KeyCode.S)) ky--;

        bool hasKeyDir = (kx != 0 || ky != 0);

        if (hasKeyDir)
        {
            keyboardJoystickActive = true;
            currentDirection       = new Vector2Int(kx, ky);
            player.SetFacingDirection(currentDirection);
        }
        else if (keyboardJoystickActive)
        {
            keyboardJoystickActive = false;
            currentDirection       = Vector2Int.zero;
            if (joystickKnobRt != null)
                joystickKnobRt.anchoredPosition = Vector2.zero;
        }

        // Space bar = beat press.
        if (Input.GetKeyDown(KeyCode.Space) && keyboardJoystickActive && currentDirection != Vector2Int.zero && !_moveLocked)
        {
            capturedBeatState = GameController.beatTimer.state;
            ExecuteMove();
        }
    }
}
