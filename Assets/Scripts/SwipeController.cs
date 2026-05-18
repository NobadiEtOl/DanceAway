using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Common.Enums;

public class SwipeController : MonoBehaviour, IControlModule
{
    [SerializeField] private Player player;
    [SerializeField] private float minSwipeDistance = 50f;
    [SerializeField] private RectTransform swipeArea;

    private RectTransform _swipeAreaRt;
    private Camera        _swipeCamera;

    // ── Gesture state (Idle → Armed → Consumed → Idle) ────────────────────────
    private enum GestureState { Idle, Armed, Consumed }
    private GestureState _gesture = GestureState.Idle;

    private Vector2   _touchStart;
    private float     _touchStartTime;
    private BeatState _capturedBeatState;
    private int       _lastMoveBeat = int.MinValue;
    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        _swipeAreaRt = swipeArea != null ? swipeArea : GetComponent<RectTransform>();
        RefreshCamera();
    }

    public void Initialize(GameController gc)
    {
        if (gc != null) player = gc.player;
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
                break;

            case TouchPhase.Moved:
                CheckExpiry();
                if (_gesture == GestureState.Armed)
                    TryFire(t.position);
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                _gesture = GestureState.Idle;
                break;
        }
    }

    private void ProcessMouse()
    {
        // Up resets first — a same-frame down+up correctly produces no move.
        if (Input.GetMouseButtonUp(0))
        {
            _gesture = GestureState.Idle;
            return;
        }

        if (Input.GetMouseButtonDown(0))
            TryArm(Input.mousePosition);

        // Re-evaluate state after TryArm; also covers frames where button was
        // already held from a previous frame.
        if (Input.GetMouseButton(0) && _gesture == GestureState.Armed)
        {
            CheckExpiry();
            if (_gesture == GestureState.Armed)   // still armed after expiry check
                TryFire(Input.mousePosition);
        }
    }

    // ── Gesture helpers ───────────────────────────────────────────────────────

    /// Arms the gesture. Only transitions from Idle.
    private void TryArm(Vector2 pos)
    {
        if (_gesture != GestureState.Idle || !IsWithinSwipeArea(pos))
            return;

        _touchStart        = pos;
        _touchStartTime    = Time.time;
        _capturedBeatState = GameController.beatTimer.state;
        _gesture           = GestureState.Armed;
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

        _gesture = GestureState.Consumed; // ← dead from this moment, even if Move throws

        if (GameController.beatTimer.beatCounter == _lastMoveBeat)
            return; // already moved this beat

        Vector2 swipeDir = currentPos - _touchStart;
        float   angle    = Mathf.Atan2(swipeDir.y, swipeDir.x) * Mathf.Rad2Deg;
        int     sector   = (int)Mathf.Round(angle / 45f);
        sector = ((sector % 8) + 8) % 8;

        Vector2Int dir = SectorDirections[sector];
        player.SetFacingDirection(dir);
        player.Move(dir, overrideState: _capturedBeatState);
        _lastMoveBeat = GameController.beatTimer.beatCounter;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

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