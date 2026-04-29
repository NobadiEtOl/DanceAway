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
    [Tooltip("Orthographic size at the very start of the intro (zoomed in on the player). Zooms out to the scene default over the walk.")]
    [SerializeField] private float introOrthoSize = 20f;

    [Tooltip("How many world units below its game-start position the bottom crowd parent begins.")]
    [SerializeField] private float crowdIntroOffset = 80f;

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

    // Final (game-start) positions captured from the already-initialised components.
    private Vector3    _finalPlayerPos;
    private Vector2Int _finalPlayerGridPos;
    private Vector3    _finalCameraPos;
    private float      _finalCameraOrthoSize;
    private Vector3    _finalBottomCrowdPos;

    // Intro positions (computed, no scene empties required).
    private Vector3 _bottomCrowdIntroStartPos;

    private List<GameObject> _roadTiles = new List<GameObject>();
    private int  _totalBeats;      // random 8-16
    private int  _introBeatCount;  // beats completed so far
    private bool _introActive;     // drives Update logic
    private Action _onDone;        // callback to reveal start screen

    private Rigidbody2D _playerRb;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    void Start()
    {
        _playerRb = player.GetComponent<Rigidbody2D>();
        _totalBeats = UnityEngine.Random.Range(8, 17); // 8-16 inclusive

        // ------------------------------------------------------------------
        // 1. Capture game-start (final) positions.
        //    GameController.Start() has already run and placed every actor.
        // ------------------------------------------------------------------
        _finalPlayerPos       = player.transform.position;
        _finalPlayerGridPos   = player.position;
        _finalCameraPos       = cam.transform.position;
        _finalCameraOrthoSize = cam.orthographicSize;
        _finalBottomCrowdPos  = crowdController.crowdParentBottom.transform.position;

        if (skipIntro)
            return; // Actors stay at game-start positions; OnLoadingComplete handles the rest.

        // ------------------------------------------------------------------
        // 2. Compute intro start positions (no scene empties needed).
        //    Player starts _totalBeats tiles directly below its final position.
        // ------------------------------------------------------------------
        float tileSize = gameController.tileSize;

        Vector3 playerIntroStart = new Vector3(
            _finalPlayerPos.x,
            _finalPlayerPos.y - _totalBeats * tileSize,
            _finalPlayerPos.z
        );

        _bottomCrowdIntroStartPos = _finalBottomCrowdPos + Vector3.down * crowdIntroOffset;

        // ------------------------------------------------------------------
        // 3. Teleport actors before the first frame renders — no visible pop.
        // ------------------------------------------------------------------
        player.transform.position = playerIntroStart;
        _playerRb.position        = playerIntroStart;
        player.position           = new Vector2Int(
            _finalPlayerGridPos.x,
            _finalPlayerGridPos.y - _totalBeats
        );

        float initYOffset = introOrthoSize * introCameraVerticalOffsetFraction;
        cam.transform.position = new Vector3(playerIntroStart.x, playerIntroStart.y - initYOffset, _finalCameraPos.z);
        cam.orthographicSize   = introOrthoSize;

        crowdController.crowdParentBottom.transform.position = _bottomCrowdIntroStartPos;

        // ------------------------------------------------------------------
        // 4. Generate road tiles below the arena using the same prefab.
        //    They fill the gap between the player's intro start and the arena
        //    floor (the existing grid starts at y = 0).
        // ------------------------------------------------------------------
        SpawnRoadTiles(tileSize);

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
        float yOffset = cam.orthographicSize * introCameraVerticalOffsetFraction;
        Vector3 followTarget = new Vector3(
            player.transform.position.x,
            player.transform.position.y - yOffset,
            _finalCameraPos.z
        );
        cam.transform.position = Vector3.Lerp(
            cam.transform.position,
            followTarget,
            Time.deltaTime * cameraFollowSpeed
        );

        // Camera zooms out progressively toward the scene-default ortho size.
        cam.orthographicSize = Mathf.Lerp(introOrthoSize, _finalCameraOrthoSize, t);

        // Bottom crowd moves continuously at crowdChaseSpeed — independent of beat count.
        // Faster than the player (~1 tile/beat) so it visually catches up from behind.
        crowdController.crowdParentBottom.transform.position = Vector3.MoveTowards(
            crowdController.crowdParentBottom.transform.position,
            _finalBottomCrowdPos,
            crowdChaseSpeed * Time.deltaTime
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
            gameController.introRunning = false;
            gameController.BeatTimerBegin();
            gameController.StartGame();
            _onDone?.Invoke();
            return;
        }

        _introActive = true;
        gameController.BeatTimerBegin(); // music starts, beats fire → OnIntroBeat is called
    }

    // =========================================================================
    // BEAT HANDLER
    // =========================================================================

    private void OnIntroBeat()
    {
        // All beats walked — start the final transition on this beat.
        if (_introBeatCount >= _totalBeats)
        {
            beatTimer.OnBeat -= OnIntroBeat;
            StartCoroutine(RunFinalTransition());
            return;
        }

        // Reuse the existing crowd-push move path:
        //   pushed=true bypasses beat-timing and grid-bounds checks, triggers
        //   the walk animation, and applies the physics force — exactly what
        //   happens when a crowd member pushes the player in normal gameplay.
        player.Move(Vector2Int.up, pushed: true);
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
        Vector3 startBottomCrowdPos = crowdController.crowdParentBottom.transform.position;

        float elapsed = 0f;

        while (elapsed < finalTransitionDuration)
        {
            float t = snapCurve.Evaluate(elapsed / finalTransitionDuration);

            // Player — drive both transform and Rigidbody2D so physics stays in sync.
            Vector3 newPlayerPos = Vector3.Lerp(startPlayerPos, _finalPlayerPos, t);
            player.transform.position = newPlayerPos;
            _playerRb.position        = newPlayerPos;

            // Camera position and zoom.
            cam.transform.position = Vector3.Lerp(startCamPos, _finalCameraPos, t);
            cam.orthographicSize   = Mathf.Lerp(startOrthoSize, _finalCameraOrthoSize, t);

            // Bottom crowd.
            crowdController.crowdParentBottom.transform.position = Vector3.Lerp(
                startBottomCrowdPos, _finalBottomCrowdPos, t
            );

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Snap every actor to exact final values — no floating-point drift.
        player.transform.position = _finalPlayerPos;
        _playerRb.position        = _finalPlayerPos;
        player.position           = _finalPlayerGridPos; // restore logical grid position

        cam.transform.position = _finalCameraPos;
        cam.orthographicSize   = _finalCameraOrthoSize;

        crowdController.crowdParentBottom.transform.position = _finalBottomCrowdPos;

        // Close all 4 crowd sides to a 3×3 arena before the game starts.
        // bound = gc.width - gridBounds[1] = 10 - 6 = 4 → each side moves 4 tiles inward.
        GameController.gridBounds[0] = 2;
        GameController.gridBounds[1] = gameController.width  - 3;
        GameController.gridBounds[2] = 2;
        GameController.gridBounds[3] = gameController.height - 3;
        crowdController.ResizeCrowd();
        yield return new WaitForSeconds(crowdController.cameraTransitionDuration);

        // Clean up road tiles.
        foreach (GameObject tile in _roadTiles)
        {
            if (tile != null) Destroy(tile);
        }
        _roadTiles.Clear();

        // Hand control back to normal game systems.
        gameController.introRunning = false;
        gameController.StartGame();     // enemies begin spawning, game state = Play

        // Reveal the start-screen buttons (callback set by BeginController).
        _onDone?.Invoke();
    }

    // =========================================================================
    // ROAD TILE SPAWNING
    // =========================================================================

    /// <summary>
    /// Spawns a single-column road below the arena using the same tile prefab
    /// and checkerboard colouring as the disco floor.
    /// </summary>
    private void SpawnRoadTiles(float tileSize)
    {
        GameObject roadParent = new GameObject("IntroRoad");

        // Road spans from the player's intro start row up to (but not including)
        // the arena's bottom row (y = 0). The arena itself already provides tiles
        // for y = 0 through y = (height-1).
        int startGridY = _finalPlayerGridPos.y - _totalBeats;
        int endGridY   = -1;

        for (int gy = startGridY; gy <= endGridY; gy++)
        {
            float worldX = _finalPlayerGridPos.x * tileSize;
            float worldY = gy * tileSize;

            GameObject tile = Instantiate(
                gridController.tilePrefab,
                new Vector3(worldX, worldY, 0f),
                Quaternion.identity,
                roadParent.transform
            );

            tile.transform.localScale = new Vector3(tileSize, tileSize, 1f);
            tile.name = $"RoadTile_{gy}";

            // Match the arena's checkerboard pattern.
            if ((_finalPlayerGridPos.x + gy) % 2 == 1)
                tile.GetComponent<SpriteRenderer>().color = Color.cyan;

            _roadTiles.Add(tile);
        }
    }
}
