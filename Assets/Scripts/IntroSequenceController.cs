using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Beat-synced intro walk sequence that plays before the start-screen buttons appear.
///
/// Flow:
///   1. GameController.Start() initialises all actors at their game-start (final) positions.
///   2. This script's Start() (runs after GameController via Script Execution Order) captures
///      those positions, then teleports actors to intro positions before the first frame renders.
///   3. BeginController.GameReady() calls OnLoadingComplete() → beat timer starts → walk plays.
///   4. Player walks upward one tile per beat (reusing the existing crowd-push move path).
///   5. Bottom crowd lerps continuously toward its game-start position proportional to walk progress.
///   6. Camera follows the player and zooms out over the walk duration.
///   7. After _totalBeats steps RunFinalTransition() smoothly returns all actors to their captured
///      game-start positions, then invokes the callback → BeginController shows start/tutorial buttons.
///
/// IMPORTANT — Script Execution Order:
///   Set GameController to -100 and IntroSequenceController to -50 in
///   Project Settings → Script Execution Order so GameController.Start() always runs first.
/// </summary>
public class IntroSequenceController : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR
    // =========================================================================

    [Header("Scene References")]
    [SerializeField] private Player player;
    [SerializeField] private CrowdController crowdController;
    [SerializeField] private Camera cam;
    [SerializeField] private GameController gameController;
    [SerializeField] private GridController gridController;
    [SerializeField] private BeatTimer beatTimer;

    [Header("Intro Settings")]
    [Tooltip("World units per second the bottom crowd moves toward the player. Should be faster than the player's tile-per-beat speed so it visually catches up.")]
    [SerializeField] private float crowdChaseSpeed = 25f;

    [Tooltip("Lerp speed used to smooth camera follow during the walk. Higher = snappier.")]
    [SerializeField] private float cameraFollowSpeed = 5f;

    [Tooltip("Fraction of the camera half-height to offset the camera downward during the intro walk, positioning the player above centre. 0 = centred, 0.33 = upper third of the screen.")]
    [SerializeField] private float introCameraVerticalOffsetFraction = 0.33f;

    [Tooltip("Duration in seconds for the smooth snap of all actors back to their game-start positions.")]
    [SerializeField] private float finalTransitionDuration = 1.2f;

    [Tooltip("Easing curve applied to the final snap transition (x = normalised time, y = normalised value).")]
    [SerializeField] private AnimationCurve snapCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Debug")]
    [Tooltip("Skip all teleporting and walking; actors stay at game-start positions. Useful for fast iteration without waiting for the walk.")]
    [SerializeField] private bool skipIntro = false;

    // =========================================================================
    // RUNTIME STATE
    // =========================================================================

    // Final (game-ready collapsed) targets captured/computed at startup.
    private Vector3    _targetPlayerPos;
    private Vector2Int _targetPlayerGridPos;
    private Vector3    _targetCameraPos;
    private float      _targetCameraOrthoSize;
    private Vector3    _targetLeftCrowdPos;
    private Vector3    _targetRightCrowdPos;
    private Vector3    _targetBottomCrowdPos;
    private Vector3    _targetTopCrowdPos;

    // Collapsed gameplay bounds used after intro and for target computation.
    private int _targetMinX;
    private int _targetMaxX;
    private int _targetMinY;
    private int _targetMaxY;

    // Intro positions (computed, no scene empties required).
    private Vector3 _bottomCrowdIntroStartPos;
    private int _roadGridX;

    // Per-side crowd speeds derived from intro duration so each side
    // arrives at its collapsed target exactly when the player does.
    private float _leftCrowdIntroSpeed;
    private float _rightCrowdIntroSpeed;
    private float _bottomCrowdIntroSpeed;
    private float _topCrowdIntroSpeed;

    private class DemoTriangleEntry
    {
        public GameObject  go;
        public Rigidbody2D rb;
        public Animator    anim;
        public float       worldY; // logical Y — updated each beat so snap target is always exact
    }

    private List<GameObject>        _roadTiles     = new List<GameObject>();
    private List<DemoTriangleEntry> _demoTriangles = new List<DemoTriangleEntry>();
    [SerializeField]private int  _totalBeats;      // random 8-16
    private int  _introBeatCount;  // beats completed so far
    private bool _introActive;     // drives Update logic
    private Action _onDone;        // callback to reveal start screen

    [Header("Demo Gameplay")]
    [Tooltip("Triangle prefab to use for the scripted dodge demo during the intro walk. Assign Triangle_New or Triangle_Old.")]
    [SerializeField] private GameObject demoTrianglePrefab;

    private Rigidbody2D _playerRb;
    private float _introStartCameraOrthoSize;

    /// <summary>Survives scene reloads (static). True after the intro walk has played once,
    /// so that Retry skips straight to the start screen without replaying the walk.</summary>
    private static bool _hasPlayedIntro = false;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================


    void Start()
    {        // Skip the walk on any reload after the first play (e.g. after Retry).
        if (_hasPlayedIntro) skipIntro = true;
        _playerRb = player.GetComponent<Rigidbody2D>();
        _introStartCameraOrthoSize = cam.orthographicSize; // Respect the camera size configured in-scene as intro start.
        //_totalBeats = UnityEngine.Random.Range(5, 11); // 4-8 inclusive

        // ------------------------------------------------------------------
        // 1. Compute game-ready collapsed targets.
        //    GameController.Start() has already run and placed every actor.
        // ------------------------------------------------------------------
        ComputeCollapsedTargets();
        ApplyCollapsedGridBounds();
        _roadGridX = _targetPlayerGridPos.x; // same column as the player target

        // ------------------------------------------------------------------
        // Derive per-side crowd speeds so every side finishes moving at the
        // same time the player completes the walk.
        //   introDuration = total steps × beat interval
        //   speed         = distance to travel / introDuration
        // ------------------------------------------------------------------
        float beatInterval   = gameController.SendBeatInterval();
        float introDuration  = _totalBeats * beatInterval;
        float ts             = gameController.tileSize;
        int   xInset         = gameController.width  - _targetMaxX;
        int   yInset         = gameController.height - _targetMaxY;

        _leftCrowdIntroSpeed   = (xInset * ts)       / introDuration;
        _rightCrowdIntroSpeed  = (xInset * ts)       / introDuration;
        _topCrowdIntroSpeed    = (yInset * ts)       / introDuration;
        // Bottom speed is computed after _bottomCrowdIntroStartPos is set (below).

        if (skipIntro)
            return; // Actors stay at game-start positions; OnLoadingComplete handles the rest.

        // ------------------------------------------------------------------
        // 2. Compute intro start positions (no scene empties needed).
        //    Player starts _totalBeats tiles directly below its final position.
        // ------------------------------------------------------------------
        float tileSize = gameController.tileSize;

        int roadLeftGridX = _roadGridX - 1;
        Vector3 playerIntroStart = new Vector3(
            roadLeftGridX * tileSize, // Bottom-left tile of the 3-lane intro road.
            _targetPlayerPos.y - _totalBeats * tileSize,
            _targetPlayerPos.z
        );

        // Compute initYOffset here so it can be shared by the crowd start pos and the camera teleport.
        float initYOffset = _introStartCameraOrthoSize * introCameraVerticalOffsetFraction;

        // Bottom crowd starts just below the camera's bottom screen edge at intro start,
        // so it is invisible and travels on-screen as the player walks in.
        float screenBottomY = playerIntroStart.y - initYOffset - _introStartCameraOrthoSize;
        _bottomCrowdIntroStartPos = new Vector3(
            _targetBottomCrowdPos.x,
            screenBottomY - tileSize, // one tile below the screen edge so it enters smoothly
            _targetBottomCrowdPos.z
        );
        // Speed = total distance the crowd must travel / intro duration.
        _bottomCrowdIntroSpeed = (_targetBottomCrowdPos.y - _bottomCrowdIntroStartPos.y) / introDuration;

        // ------------------------------------------------------------------
        // 3. Teleport actors before the first frame renders — no visible pop.
        // ------------------------------------------------------------------
        player.transform.position = playerIntroStart;
        _playerRb.position        = playerIntroStart;
        player.position           = new Vector2Int(
            _targetPlayerGridPos.x - 1, // LEFT column
            _targetPlayerGridPos.y - _totalBeats
        );

        // Keep camera centred on the road (centre column), not the player's offset X.
        cam.transform.position = new Vector3(_targetPlayerPos.x, playerIntroStart.y - initYOffset, _targetCameraPos.z);

        crowdController.crowdParentBottom.transform.position = _bottomCrowdIntroStartPos;

        // ------------------------------------------------------------------
        // 4. Generate road tiles below the arena using the same prefab.
        //    They fill the gap between the player's intro start and the arena
        //    floor (the existing grid starts at y = 0).
        // ------------------------------------------------------------------
        SpawnRoadTiles(tileSize);
        SpawnDemoTriangles(tileSize);

        // ------------------------------------------------------------------
        // 5. Subscribe to the beat — walking begins once BeatTimerBegin() fires.
        // ------------------------------------------------------------------
        beatTimer.OnBeat += OnIntroBeat;
    }

    void Update()
    {
        if (!_introActive) return;

        float t = Mathf.Clamp01((float)_introBeatCount / _totalBeats);

        // Camera follows player smoothly during the walk, offset downward so the player sits in the upper third.
        // X is locked to the road centre so the camera doesn't oscillate with the player's diagonal zigzag.
        float yOffset = cam.orthographicSize * introCameraVerticalOffsetFraction;
        Vector3 followTarget = new Vector3(
            _targetPlayerPos.x,
            player.transform.position.y - yOffset,
            _targetCameraPos.z
        );
        cam.transform.position = Vector3.Lerp(
            cam.transform.position,
            followTarget,
            Time.deltaTime * cameraFollowSpeed
        );

        // Camera zooms out progressively toward the scene-default ortho size.
        cam.orthographicSize = Mathf.Lerp(_introStartCameraOrthoSize, _targetCameraOrthoSize, t);

        // All crowd sides move toward their collapsed targets, each at a speed
        // calibrated so they all arrive exactly when the player reaches the centre.
        crowdController.crowdParentBottom.transform.position = Vector3.MoveTowards(
            crowdController.crowdParentBottom.transform.position,
            _targetBottomCrowdPos,
            _bottomCrowdIntroSpeed * Time.deltaTime
        );

        crowdController.crowdParentLeft.transform.position = Vector3.MoveTowards(
            crowdController.crowdParentLeft.transform.position,
            _targetLeftCrowdPos,
            _leftCrowdIntroSpeed * Time.deltaTime
        );

        crowdController.crowdParentRight.transform.position = Vector3.MoveTowards(
            crowdController.crowdParentRight.transform.position,
            _targetRightCrowdPos,
            _rightCrowdIntroSpeed * Time.deltaTime
        );

        crowdController.crowdParentTop.transform.position = Vector3.MoveTowards(
            crowdController.crowdParentTop.transform.position,
            _targetTopCrowdPos,
            _topCrowdIntroSpeed * Time.deltaTime
        );
    }

    void OnDestroy()
    {
        beatTimer.OnBeat -= OnIntroBeat;
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>
    /// Called by BeginController when the loading phase is complete.
    /// Starts the beat timer (music + beats) and stores the callback that will
    /// be invoked once the intro walk and final transition have finished.
    /// </summary>
    public void OnLoadingComplete(Action onDone)
    {
        _onDone = onDone;

        // skipIntro: no walk needed — start game immediately and reveal the start screen.
        if (skipIntro)
        {
            ApplyCollapsedGameReadyStateInstant();
            gameController.introRunning = false;
            gameController.OnIntroComplete();
            gameController.BeatTimerBegin();
            // StartGame() is deferred until the player presses Start.
            _onDone?.Invoke();
            return;
        }

        _introActive = true;
        gameController.BeatTimerBegin(); // music starts, beats fire → OnIntroBeat is called
    }

    // =========================================================================
    // BEAT HANDLER
    // =========================================================================

    // Extra beats after the player reaches the centre during which triangles keep moving.
    private const int ExtraTriangleBeats = 2;

    private void OnIntroBeat()
    {
        // All beats walked AND extra triangle tail beats done — start final transition.
        if (_introBeatCount >= _totalBeats + ExtraTriangleBeats)
        {
            beatTimer.OnBeat -= OnIntroBeat;
            StartCoroutine(RunFinalTransition());
            return;
        }

        // Player only moves during the main walk phase.
        if (_introBeatCount < _totalBeats)
        {
            // Traverse all 3 road lanes during the preview:
            //   left -> middle -> right -> middle -> ... while always moving upward.
            //   pushed=true bypasses beat-timing and grid-bounds checks.
            int laneStep = _introBeatCount % 4;
            Vector2Int moveDir = (laneStep == 0 || laneStep == 1)
                ? new Vector2Int(1, 1)
                : new Vector2Int(-1, 1);
            player.Move(moveDir, pushed: true);
        }

        // Triangles keep moving for the full walk PLUS the extra tail beats.
        float ts            = gameController.tileSize;
        float moveDuration  = gameController.SendBeatInterval() / 3f; // matches Triangle's speedMult = 3
        float destroyBelowY = _targetPlayerPos.y - _totalBeats * ts - ts;
        for (int i = _demoTriangles.Count - 1; i >= 0; i--)
        {
            DemoTriangleEntry entry = _demoTriangles[i];
            if (entry.go == null) { _demoTriangles.RemoveAt(i); continue; }
            entry.worldY -= ts;
            if (entry.worldY < destroyBelowY)
            {
                Destroy(entry.go);
                _demoTriangles.RemoveAt(i);
                continue;
            }
            StartCoroutine(MoveDemoTriangle(entry, ts, moveDuration));
        }

        _introBeatCount++;
    }

    // =========================================================================
    // FINAL TRANSITION
    // =========================================================================

    private IEnumerator RunFinalTransition()
    {
        _introActive = false; // Stop Update overrides

        // Capture current world state as lerp start points.
        Vector3 startPlayerPos      = player.transform.position;
        Vector3 startCamPos         = cam.transform.position;
        float   startOrthoSize      = cam.orthographicSize;
        Vector3 startLeftCrowdPos   = crowdController.crowdParentLeft.transform.position;
        Vector3 startRightCrowdPos  = crowdController.crowdParentRight.transform.position;
        Vector3 startBottomCrowdPos = crowdController.crowdParentBottom.transform.position;
        Vector3 startTopCrowdPos    = crowdController.crowdParentTop.transform.position;

        float elapsed = 0f;

        while (elapsed < finalTransitionDuration)
        {
            float t = snapCurve.Evaluate(elapsed / finalTransitionDuration);

            // Player — drive both transform and Rigidbody2D so physics stays in sync.
            Vector3 newPlayerPos = Vector3.Lerp(startPlayerPos, _targetPlayerPos, t);
            player.transform.position = newPlayerPos;
            _playerRb.position        = newPlayerPos;

            // Camera position and zoom.
            cam.transform.position = Vector3.Lerp(startCamPos, _targetCameraPos, t);
            cam.orthographicSize   = Mathf.Lerp(startOrthoSize, _targetCameraOrthoSize, t);

            // All crowd sides converge directly to collapsed game-ready targets.
            crowdController.crowdParentLeft.transform.position = Vector3.Lerp(startLeftCrowdPos, _targetLeftCrowdPos, t);
            crowdController.crowdParentRight.transform.position = Vector3.Lerp(startRightCrowdPos, _targetRightCrowdPos, t);
            crowdController.crowdParentBottom.transform.position = Vector3.Lerp(startBottomCrowdPos, _targetBottomCrowdPos, t);
            crowdController.crowdParentTop.transform.position = Vector3.Lerp(startTopCrowdPos, _targetTopCrowdPos, t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Snap every actor to exact final values — no floating-point drift.
        player.transform.position = _targetPlayerPos;
        _playerRb.position        = _targetPlayerPos;
        player.position           = _targetPlayerGridPos;

        cam.transform.position = _targetCameraPos;
        cam.orthographicSize   = _targetCameraOrthoSize;

        crowdController.SetCrowdWorldPositions(
            _targetLeftCrowdPos,
            _targetRightCrowdPos,
            _targetBottomCrowdPos,
            _targetTopCrowdPos
        );

        ApplyCollapsedGridBounds();

        // Clean up road tiles and demo triangles.
        foreach (GameObject tile in _roadTiles)
        {
            if (tile != null) Destroy(tile);
        }
        _roadTiles.Clear();

        foreach (DemoTriangleEntry entry in _demoTriangles)
        {
            if (entry.go != null) Destroy(entry.go);
        }
        _demoTriangles.Clear();

        // Hand control back to normal game systems.
        _hasPlayedIntro = true;         // future scene reloads (Retry) will skip this walk
        gameController.introRunning = false;
        gameController.OnIntroComplete();
        // StartGame() is deferred until the player presses Start, so the first wave
        // uses whichever difficulty the player selects on the start screen.

        // Reveal the start-screen buttons (callback set by BeginController).
        _onDone?.Invoke();
    }

    // =========================================================================
    // ROAD TILE SPAWNING
    // =========================================================================

    /// <summary>
    /// Spawns a 3-tile-wide decorative road using the same tile prefab
    /// and checkerboard colouring as the disco floor.
    /// </summary>
    private void SpawnRoadTiles(float tileSize)
    {
        GameObject roadParent = new GameObject("IntroRoad");

        // Road spans from the player's intro start row up to (but not including)
        // the arena's bottom row (y = 0), and also extends by the same amount
        // in the opposite direction for decoration.
        int playerIntroStartGridY = _targetPlayerGridPos.y - _totalBeats;
        int endGridY = -1;
        int upwardLength = Mathf.Max(0, endGridY - playerIntroStartGridY);
        int startGridY = playerIntroStartGridY - upwardLength;

        // 3-tile road: center lane plus one extra column on each side.
        for (int gy = startGridY; gy <= endGridY; gy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = _roadGridX + dx;
                float worldX = gx * tileSize;
                float worldY = gy * tileSize;

                GameObject tile = Instantiate(
                    gridController.tilePrefab,
                    new Vector3(worldX, worldY, 0f),
                    Quaternion.identity,
                    roadParent.transform
                );

                tile.transform.localScale = new Vector3(tileSize, tileSize, 1f);
                tile.name = $"RoadTile_{gx}_{gy}";

                // Match the arena's checkerboard pattern.
                if ((gx + gy) % 2 == 1)
                    tile.GetComponent<SpriteRenderer>().color = Color.cyan;

                _roadTiles.Add(tile);
            }
        }
    }

    /// <summary>
    /// Spawns demo enemy triangles on the intro road using all 3 lanes,
    /// spaced 1 tile apart starting 1 tile above the player's start position.
    /// The Triangle AI component is disabled immediately — these are purely visual props.
    /// They move downward one tile per beat in OnIntroBeat().
    /// </summary>
    private void SpawnDemoTriangles(float tileSize)
    {
        if (demoTrianglePrefab == null) return;

        GameObject triParent = new GameObject("IntroDemoTriangles");
        int playerIntroStartGridY = _targetPlayerGridPos.y - _totalBeats;
        // One triangle per row of the intro road — fills the entire path from start to centre.
        int numEnemies = _totalBeats;

        int roadLeftGridX = _roadGridX - 1;
        for (int i = 0; i < numEnemies; i++)
        {
            // Ping-pong lane order: left -> middle -> right -> middle -> left -> ...
            int lane = (i % 4 == 0) ? 0 : (i % 4 == 1) ? 1 : (i % 4 == 2) ? 2 : 1;
            int gridX = roadLeftGridX + lane;
            // Each subsequent triangle is 1 row above the previous — no gaps.
            int gridY = playerIntroStartGridY + 1 + i*2;
            float worldX = gridX * tileSize;
            float worldY = gridY * tileSize;

            GameObject tri = Instantiate(
                demoTrianglePrefab,
                new Vector3(worldX, worldY, 0f),
                Quaternion.Euler(0f, 0f, 180f), // face downward to match movement direction
                triParent.transform
            );
            tri.name = $"DemoTriangle_{i}";

            // Disable Triangle AI — Initialize() was never called so beatTimer/gameController
            // are null; disabling prevents FixedUpdate from crashing.
            Triangle triComp = tri.GetComponent<Triangle>();
            if (triComp != null) triComp.enabled = false;

            // Disable all colliders so demo triangles never interact with the player.
            foreach (Collider2D col in tri.GetComponentsInChildren<Collider2D>())
                col.enabled = false;

            // Set up Rigidbody2D for kinematic smooth movement driven by the coroutine.
            Rigidbody2D triRb = tri.GetComponent<Rigidbody2D>();
            if (triRb != null)
            {
                triRb.gravityScale = 0f;
                triRb.isKinematic  = true;
            }

            // Start animators in their idle states.
            Animator triAnim = tri.GetComponent<Animator>();
            if (triAnim != null) triAnim.Play("Triangle_Idle");
            if (tri.transform.childCount > 0)
            {
                Animator childAnim = tri.transform.GetChild(0).GetComponent<Animator>();
                if (childAnim != null) childAnim.Play("Triangle_Damage_Idle");
            }

            _demoTriangles.Add(new DemoTriangleEntry { go = tri, rb = triRb, anim = triAnim, worldY = worldY });
        }
    }

    /// <summary>
    /// Smoothly moves a demo triangle one tile downward using Rigidbody2D.MovePosition,
    /// playing the Triangle_Moving animation during the move and Triangle_Idle afterward.
    /// Movement duration matches Triangle's normal speedMult=3 (one-third of a beat).
    /// </summary>
    private IEnumerator MoveDemoTriangle(DemoTriangleEntry entry, float tileSize, float moveDuration)
    {
        if (entry.go == null) yield break;
        entry.anim?.Play("Triangle_Moving");

        float elapsed = 0f;
        float speed   = tileSize / moveDuration;

        while (elapsed < moveDuration)
        {
            if (entry.go == null) yield break;
            entry.rb.MovePosition(entry.rb.position + Vector2.down * (speed * Time.fixedDeltaTime));
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        if (entry.go == null) yield break;

        // Snap to the exact logical Y to prevent float drift accumulating across beats.
        Vector2 snapped = new Vector2(entry.rb.position.x, entry.worldY);
        entry.rb.position = snapped;
        entry.go.transform.position = new Vector3(snapped.x, snapped.y, entry.go.transform.position.z);

        entry.anim?.Play("Triangle_Idle");
    }

    private void ComputeCollapsedTargets()
    {
        float tileSize = gameController.tileSize;

        // Keep the same arena intent as the previous intro end-state.
        _targetMinX = 2;
        _targetMaxX = gameController.width - 3;
        _targetMinY = 2;
        _targetMaxY = gameController.height - 3;

        // Use the true center of the collapsed disco bounds so intro end-state
        // lands exactly at arena center on both axes.
        int centerX = Mathf.RoundToInt((_targetMinX + _targetMaxX) * 0.5f);
        int centerY = Mathf.RoundToInt((_targetMinY + _targetMaxY) * 0.5f);
        _targetPlayerGridPos = new Vector2Int(centerX, centerY);
        _targetPlayerPos = new Vector3(centerX * tileSize, centerY * tileSize, player.transform.position.z);

        _targetCameraOrthoSize = gameController.CalculateOrthoSizeForBounds(_targetMinX, _targetMaxX);
        // Final intro camera target: arena centre shifted down by the gameplay offset fraction
        // so the arena sits slightly above screen centre, leaving room at the bottom for input.
        float downwardShift = _targetCameraOrthoSize * gameController.gameplayCameraVerticalOffsetFraction;
        _targetCameraPos = new Vector3(
            _targetPlayerPos.x,
            _targetPlayerPos.y - downwardShift,
            cam.transform.position.z
        );

        Vector3 baseLeftCrowdPos = crowdController.crowdParentLeft.transform.position;
        Vector3 baseRightCrowdPos = crowdController.crowdParentRight.transform.position;
        Vector3 baseBottomCrowdPos = crowdController.crowdParentBottom.transform.position;
        Vector3 baseTopCrowdPos = crowdController.crowdParentTop.transform.position;

        int xInsetTiles = gameController.width - _targetMaxX;
        int yInsetTiles = gameController.height - _targetMaxY;
        _targetLeftCrowdPos = baseLeftCrowdPos + Vector3.right * (xInsetTiles * tileSize);
        _targetRightCrowdPos = baseRightCrowdPos + Vector3.left * (xInsetTiles * tileSize);
        _targetBottomCrowdPos = baseBottomCrowdPos + Vector3.up * (yInsetTiles * tileSize);
        _targetTopCrowdPos = baseTopCrowdPos + Vector3.down * (yInsetTiles * tileSize);
    }

    private void ApplyCollapsedGridBounds()
    {
        GameController.gridBounds[0] = _targetMinX;
        GameController.gridBounds[1] = _targetMaxX;
        GameController.gridBounds[2] = _targetMinY;
        GameController.gridBounds[3] = _targetMaxY;
        player.ChangeGridBounds();
    }

    private void ApplyCollapsedGameReadyStateInstant()
    {
        player.transform.position = _targetPlayerPos;
        _playerRb.position = _targetPlayerPos;
        player.position = _targetPlayerGridPos;

        cam.transform.position = _targetCameraPos;
        cam.orthographicSize = _targetCameraOrthoSize;

        crowdController.SetCrowdWorldPositions(
            _targetLeftCrowdPos,
            _targetRightCrowdPos,
            _targetBottomCrowdPos,
            _targetTopCrowdPos
        );

        ApplyCollapsedGridBounds();
    }
}
