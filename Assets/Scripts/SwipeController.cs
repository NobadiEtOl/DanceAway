using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Common.Enums;

public class SwipeController : MonoBehaviour, IControlModule
{
    [SerializeField] private Player player;
    [SerializeField] private GameController gameController;
    [SerializeField] private float minSwipeDistance = 50f;
    [SerializeField] private RectTransform swipeArea;
    [SerializeField] private bool manualSwipesAnywhere = true;

    private RectTransform _swipeAreaRt;
    private Camera        _swipeCamera;

    // ── Gesture state (Idle → Armed → Consumed → Idle) ────────────────────────
    private enum GestureState { Idle, Armed, Consumed }
    private GestureState _gesture = GestureState.Idle;

    private Vector2   _touchStart;
    private float     _touchStartTime;
    private BeatState _capturedBeatState;
    private int       _lastMoveBeat = int.MinValue;
    // Locked after any valid swipe; cleared at the next beat to prevent mid-animation inputs.
    private bool      _moveLocked   = false;
    // ── Hold / auto-move state ─────────────────────────────────────────────
    // Immediately set when a touch is armed; cleared when the touch lifts.
    private bool       _isHolding    = false;
    private Vector2    _holdPos      = Vector2.zero;   // live screen-space finger pos
    private Vector2Int _holdDirection = Vector2Int.zero;
    // True after any move (swipe or auto) fires this cycle; cleared by OnBeatUnlock.
    private bool       _movedThisBeat = false;
    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        _swipeAreaRt = swipeArea != null ? swipeArea : GetComponent<RectTransform>();
        RefreshCamera();
    }

    private void OnEnable()
    {
        _moveLocked    = false;
        _movedThisBeat = false;
        _isHolding     = false;
        if (GameController.beatTimer != null)
        {
            GameController.beatTimer.OnBeat  += OnBeatUnlock;
            GameController.beatTimer.OffBeat += OnOffBeatAutoMove;
        }
    }

    private void OnDisable()
    {
        if (GameController.beatTimer != null)
        {
            GameController.beatTimer.OnBeat  -= OnBeatUnlock;
            GameController.beatTimer.OffBeat -= OnOffBeatAutoMove;
        }
        _isHolding = false;
    }

    private void OnBeatUnlock()
    {
        _moveLocked    = false;
        _movedThisBeat = false;
    }

    public void Initialize(GameController gc)
    {
        if (gc != null)
        {
            gameController = gc;
            player = gc.player;
        }
    }

    public void SetSwipeArea(RectTransform area)
    {
        swipeArea    = area;
        _swipeAreaRt = area != null ? area : GetComponent<RectTransform>();
        RefreshCamera();
    }

    void Update() => DetectSwipe();

    // ── Input routing ─────────────────────────────────────────────────────────

    private void DetectSwipe()
    {
        if (Input.touchCount > 0)
        {
            ProcessTouch(Input.GetTouch(0));
            return;
        }
        ProcessMouse();
    }

    private void ProcessTouch(Touch t)
    {
        switch (t.phase)
        {
            case TouchPhase.Began:
                TryArm(t.position);
                break;

            case TouchPhase.Stationary:
                CheckExpiry();
                if (_isHolding) UpdateHoldPos(t.position);
                break;

            case TouchPhase.Moved:
                CheckExpiry();
                if (_gesture == GestureState.Armed)
                    TryFire(t.position);
                if (_isHolding) UpdateHoldPos(t.position);
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                _gesture   = GestureState.Idle;
                _isHolding = false;
                break;
        }
    }

    private void ProcessMouse()
    {
        // Up resets first — a same-frame down+up correctly produces no move.
        if (Input.GetMouseButtonUp(0))
        {
            _gesture   = GestureState.Idle;
            _isHolding = false;
            return;
        }

        if (Input.GetMouseButtonDown(0))
            TryArm(Input.mousePosition);

        // Re-evaluate state after TryArm; also covers frames where button was
        // already held from a previous frame.
        if (Input.GetMouseButton(0))
        {
            if (_gesture == GestureState.Armed)
            {
                CheckExpiry();
                if (_gesture == GestureState.Armed)   // still armed after expiry check
                    TryFire(Input.mousePosition);
            }
            if (_isHolding) UpdateHoldPos(Input.mousePosition);
        }
    }

    // ── Gesture helpers ───────────────────────────────────────────────────────

    /// Arms the gesture. Only transitions from Idle.
    private void TryArm(Vector2 pos)
    {
        if (_gesture != GestureState.Idle)
            return;

        // Allow swipe gestures to start from any screen position by default.
        if (!manualSwipesAnywhere && !IsWithinSwipeArea(pos))
            return;

        _touchStart        = pos;
        _touchStartTime    = Time.time;
        _capturedBeatState = GameController.beatTimer.state;
        _gesture           = GestureState.Armed;
        _isHolding         = true;                           // immediately enable hold tracking
        _holdPos           = pos;
        _holdDirection     = ComputeDirectionFromPosition(pos);
    }

    /// Expires the gesture if it has lived too long. Transitions Armed → Consumed.
    private void CheckExpiry()
    {
        if (_gesture == GestureState.Armed &&
            Time.time - _touchStartTime > GameController.beatTimer.beatInterval * 0.5f)
        {
            _gesture = GestureState.Consumed;
        }
    }

    /// Fires a move when the swipe distance threshold is met.
    /// Sets Consumed BEFORE dispatching — impossible to fire twice per gesture.
    private void TryFire(Vector2 currentPos)
    {
        if (Vector2.Distance(_touchStart, currentPos) < minSwipeDistance)
            return;

        bool inCalibration = gameController != null && gameController.inCalibration;

        _gesture = GestureState.Consumed; // ← dead from this moment, even if Move throws

        if (_moveLocked && !inCalibration) return;           // block input while movement animation is playing

        if (!inCalibration && GameController.beatTimer.beatCounter == _lastMoveBeat)
            return; // already moved this beat

        Vector2 swipeDir = currentPos - _touchStart;
        float   angle    = Mathf.Atan2(swipeDir.y, swipeDir.x) * Mathf.Rad2Deg;
        int     sector   = (int)Mathf.Round(angle / 45f);
        sector = ((sector % 8) + 8) % 8;

        Vector2Int dir = SectorDirections[sector];
        _holdDirection = dir;  // lock hold direction to confirmed swipe so auto-move stays consistent
        player.SetFacingDirection(dir);
        player.Move(dir, overrideState: _capturedBeatState);
        if (!inCalibration)
        {
            _moveLocked    = true;
            _movedThisBeat = true;
            _lastMoveBeat  = GameController.beatTimer.beatCounter;
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Auto-move handler for hold mode. Fires once per beat cycle at OffBeat
    /// while the finger is held. Awards a flat auto-move score, matching the
    /// behaviour of ArrowKeysController and JoystickBeatController.
    /// </summary>
    private void OnOffBeatAutoMove()
    {
        if (gameController != null && gameController.GetPlayYourMusicMode()) return;  // PYM mode: manual input only, no auto-move
        if (!_isHolding)    return;
        if (_movedThisBeat) return;
        if (_gesture == GestureState.Armed) return;  // intent unknown mid-swipe; avoid moving in the wrong direction

        // Recompute from the live finger position for responsiveness.
        Vector2Int dir = ComputeDirectionFromPosition(_holdPos);
        if (dir == Vector2Int.zero) return;

        _holdDirection = dir;
        player.SetFacingDirection(dir);
        player.Move(dir, autoMove: true);
        _movedThisBeat = true;
        _moveLocked    = true;
    }

    /// <summary>
    /// Updates the tracked finger position and the player's facing direction
    /// while the finger is held on the swipe area.
    /// </summary>
    private void UpdateHoldPos(Vector2 screenPos)
    {
        _holdPos = screenPos;
        Vector2Int hd = ComputeDirectionFromPosition(screenPos);
        _holdDirection = hd;
        if (hd != Vector2Int.zero) player.SetFacingDirection(hd);
    }

    /// <summary>
    /// Maps a screen-space finger position to one of 8 grid directions using the
    /// swipe area as an invisible direction pad.
    ///
    /// The area is normalised by its half-extents so all 8 zones scale correctly
    /// with any aspect ratio. Diagonal zones are intentionally wider than cardinal
    /// zones (≈53° vs ≈37° each) to compensate for the mismatch between a
    /// rectangular pad and a square movement grid.
    /// </summary>
    private Vector2Int ComputeDirectionFromPosition(Vector2 screenPos)
    {
        if (_swipeAreaRt == null) return Vector2Int.zero;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _swipeAreaRt, screenPos, _swipeCamera, out Vector2 localPos);

        Rect r = _swipeAreaRt.rect;
        // Re-centre so (0,0) is the visual middle regardless of pivot placement.
        localPos -= new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

        // Normalise by half-extents so the logical zones are equal regardless of
        // how wide or tall the rectangle is.
        float nx = localPos.x / (r.width  * 0.5f);
        float ny = localPos.y / (r.height * 0.5f);

        float ax = Mathf.Abs(nx);
        float ay = Mathf.Abs(ny);

        // cardinalThreshold > 2.41 → diagonal zones wider than cardinal zones.
        // 3.0 gives cardinals ~37° each and diagonals ~53° each.
        const float cardinalThreshold = 3f;

        bool isRight = nx >= 0f;
        bool isUp    = ny >= 0f;

        if (ax > cardinalThreshold * ay) return new Vector2Int(isRight ? 1 : -1, 0);   // E / W
        if (ay > cardinalThreshold * ax) return new Vector2Int(0, isUp    ? 1 : -1);   // N / S
        return new Vector2Int(isRight ? 1 : -1, isUp ? 1 : -1);                        // diagonals
    }

    private bool IsWithinSwipeArea(Vector2 screenPos)
    {
        if (_swipeAreaRt == null) return true;
        return RectTransformUtility.RectangleContainsScreenPoint(_swipeAreaRt, screenPos, _swipeCamera);
    }

    private void RefreshCamera()
    {
        Canvas c = _swipeAreaRt != null ? _swipeAreaRt.GetComponentInParent<Canvas>() : null;
        _swipeCamera = (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay)
            ? c.worldCamera
            : null;
    }

    private static readonly Vector2Int[] SectorDirections =
    {
        new Vector2Int( 1,  0),  // 0: E
        new Vector2Int( 1,  1),  // 1: NE
        new Vector2Int( 0,  1),  // 2: N
        new Vector2Int(-1,  1),  // 3: NW
        new Vector2Int(-1,  0),  // 4: W
        new Vector2Int(-1, -1),  // 5: SW
        new Vector2Int( 0, -1),  // 6: S
        new Vector2Int( 1, -1),  // 7: SE
    };
}