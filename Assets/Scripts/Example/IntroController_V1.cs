/*using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Controls the intro entrance sequence in MainScreenScene.
/// Orchestrates: first click → camera to entrance → door opens + leaver exits → 
/// camera moves inside → chair drops → reveal main menu UI.
/// 
/// RESPONSIBILITIES:
/// - Sequence state machine for entrance flow
/// - Camera transitions between anchors (Transform-based)
/// - Door opening control (Animator first, scripted fallback)
/// - Prefab instantiation for leaver and chair
/// - Main menu UI reveal timing
/// 
/// Does NOT handle:
/// - Menu button logic (MainScreen_V1 owns that)
/// - Gameplay or network (stays pure frontend orchestration)
/// - Future room navigation (locker, settings, etc. — added later)
/// </summary>
public class IntroController_V1 : MonoBehaviour
{
    [Serializable]
    public class CameraPathPoint
    {
        [Tooltip("Target transform the camera should move to for this step.")]
        public Transform target;

        [Tooltip("Optional explicit duration in seconds for the move into this target. Use 0 or a negative value to fall back to weighted timing.")]
        public float moveDuration = 0f;

        [Tooltip("Optional wait in seconds after reaching this target before moving to the next one.")]
        public float waitAfterArrival = 0f;
    }

    // =============================================================================
    // STATE MACHINE
    // =============================================================================
    
    public enum IntroState
    {
        WaitingForAuth,
        WaitingForFirstClick,
        MovingToEntrance,
        OpeningDoorAndSpawningLeaver,
        MovingInside,
        SpawningChair,
        RevealingMainScreen,
        MovingToPlaySelection,
        Idle
    }
    
    [Header("Current State (Read Only)")]
    [SerializeField] private IntroState currentState = IntroState.WaitingForAuth;

    // =============================================================================
    // STARTUP LOADING (FIRST SEQUENCE)
    // =============================================================================

    [Header("Startup Loading Sequence")]
    [Tooltip("Optional minimum startup loading duration in seconds. If 0, intro waits only for auth/setup completion.")]
    [SerializeField] private float startupLoadingDuration = 0f;

    [Tooltip("Camera FOV at the beginning of startup loading.")]
    [SerializeField] private float startupLoadingStartFov = 20f;

    [Tooltip("Camera FOV at the end of startup loading.")]
    [SerializeField] private float startupLoadingEndFov = 100f;

    [Tooltip("Optional dedicated text for startup loading and click-ready status.")]
    [SerializeField] private TMPro.TextMeshProUGUI startupLoadingStatusText;

    [Tooltip("Base loading text shown during startup loading. Dots are appended in a loop.")]
    [SerializeField] private string startupLoadingText = "YÜKLENİYOR";

    [Tooltip("Seconds between loading text dot updates.")]
    [SerializeField] private float startupLoadingDotInterval = 0.35f;

    [Tooltip("Maximum number of animated dots appended to loading text.")]
    [SerializeField] private int startupLoadingMaxDots = 3;

    [Tooltip("Duration in seconds to transition camera to Camera Start Pos after loading completes. Use 0 for instant snap.")]
    [SerializeField] private float startupLoadingCameraTransitionDuration = 0.8f;

    [Header("Startup Camera Lock")]
    [Tooltip("If true, the camera world rotation is locked during startup loading so it can stay parented to the car for position follow without inheriting car rotation animation.")]
    [SerializeField] private bool lockCameraRotationDuringStartupLoading = true;

    [Tooltip("World-space Euler start rotation for the camera while startup loading is active.")]
    [SerializeField] private Vector3 startupLoadingCameraLockedEuler = Vector3.zero;

    [Tooltip("World-space Euler end rotation reached as startup loading progresses.")]
    [SerializeField] private Vector3 startupLoadingCameraLockedEndEuler = Vector3.zero;
    
    // =============================================================================
    // CAMERA REFERENCES
    // =============================================================================
    
    [Header("Camera")]
    [Tooltip("The camera to move during the intro sequence. If null, uses Camera.main.")]
    [SerializeField] private Camera introCamera;

    [Tooltip("Optional camera transform to snap to after startup loading completes.")]
    [SerializeField] private Transform cameraStartPos;

    [Header("Startup Car Drift")]
    [Tooltip("Car root GameObject that owns (or contains) the animator used during startup loading.")]
    [SerializeField] private GameObject startupCar;

    [Tooltip("Animator trigger parameter fired when startup loading completes to start the drift animation.")]
    [SerializeField] private string startupCarDriftTriggerName = "DriftTrigger";

    [Tooltip("If true, waits for the drift animation to complete before moving the camera to Camera Start Pos.")]
    [SerializeField] private bool waitForStartupCarDriftToFinish = true;

    [Tooltip("Safety timeout in seconds while waiting for the startup car drift animation to finish.")]
    [SerializeField] private float startupCarDriftTimeout = 6f;

    [Tooltip("Optional camera transform to move to when the startup car drift begins, before transitioning to Camera Start Pos.")]
    [SerializeField] private Transform startupCarDriftCameraPos;

    [Tooltip("Delay in seconds before the camera starts moving to the drift position after the drift begins.")]
    [SerializeField] private float startupCarDriftCameraTransitionStartDelay = 0f;

    [Tooltip("Duration in seconds to transition camera to the drift position when the startup car drift begins. Use 0 for instant snap.")]
    [SerializeField] private float startupCarDriftCameraTransitionDuration = 0.45f;

    [Tooltip("Ordered camera targets for the entrance move (outside looking at door). Each target can optionally override its move duration and add a wait before the next target.")]
    [SerializeField] private List<CameraPathPoint> entrancePath = new List<CameraPathPoint>();

    [Tooltip("Ordered camera targets for the move into the building. Each target can optionally override its move duration and add a wait before the next target.")]
    [SerializeField] private List<CameraPathPoint> enteredPath = new List<CameraPathPoint>();

    [Tooltip("Ordered camera targets for the play-button move. Each target can optionally override its move duration and add a wait before the next target.")]
    [SerializeField] private List<CameraPathPoint> playPath = new List<CameraPathPoint>();

    [FormerlySerializedAs("entrancePositions")]
    [SerializeField, HideInInspector] private List<Transform> legacyEntrancePositions = new List<Transform>();

    [FormerlySerializedAs("enteredPositions")]
    [SerializeField, HideInInspector] private List<Transform> legacyEnteredPositions = new List<Transform>();

    [FormerlySerializedAs("playPositions")]
    [SerializeField, HideInInspector] private List<Transform> legacyPlayPositions = new List<Transform>();

    [FormerlySerializedAs("entrancePos")]
    [SerializeField, HideInInspector] private Transform legacyEntrancePosition;

    [FormerlySerializedAs("enteredPos")]
    [SerializeField, HideInInspector] private Transform legacyEnteredPosition;
    
    // =============================================================================
    // DOOR REFERENCES
    // =============================================================================
    
    [Header("Door")]
    [Tooltip("The door transform to rotate open on the Y axis (fallback when no Animator is configured).")]
    [SerializeField] private Transform doorToOpen;

    [Tooltip("Optional second door transform to rotate open on the Y axis at the same time as the main door (fallback when no Animator is configured).")]
    [SerializeField] private Transform secondDoorToOpen;

    [Tooltip("Animator for the main door. If assigned, trigger-based opening is used during the final entrance camera segment.")]
    [SerializeField] private Animator doorAnimator;

    [Tooltip("Animator for the second door. Optional. Uses trigger-based opening during the final entrance camera segment.")]
    [SerializeField] private Animator secondDoorAnimator;

    [Tooltip("Trigger parameter fired on the main door animator to start door opening.")]
    [SerializeField] private string doorOpenTriggerName = "Open";

    [Tooltip("Trigger parameter fired on the second door animator to start door opening.")]
    [SerializeField] private string secondDoorOpenTriggerName = "Open";
    
    [Tooltip("How many degrees to rotate the door on Y axis when opening (positive = counterclockwise from above).")]
    [SerializeField] private float doorOpenAngle = 90f;

    [Tooltip("How many degrees to rotate the second door on Y axis when opening (positive = counterclockwise from above).")]
    [SerializeField] private float secondDoorOpenAngle = -90f;
    
    [Tooltip("Duration in seconds for the door to rotate open.")]
    [SerializeField] private float doorOpenDuration = 1.0f;

    [Tooltip("Duration in seconds for the second door to rotate open.")]
    [SerializeField] private float secondDoorOpenDuration = 1.0f;
    
    // =============================================================================
    // LEAVER (NPC EXITING) REFERENCES
    // =============================================================================
    
    [Header("Leaver (Exiting NPC)")]
    [Tooltip("Prefab of the NPC that exits through the door. Should have its own animation.")]
    [SerializeField] private GameObject leaverPrefab;
    
    [Tooltip("World position/rotation where the leaver prefab spawns.")]
    [SerializeField] private Transform leaverStart;
    
    [Tooltip("Time in seconds after leaver spawns before it auto-destroys (0 = never auto-destroy).")]
    [SerializeField] private float leaverLifetime = 5f;
    
    // =============================================================================
    // CHAIR REFERENCES
    // =============================================================================
    
    [Header("Chair (Armchair Drop)")]
    [Tooltip("Prefab of the armchair that drops from ceiling. Should have its own drop animation.")]
    [SerializeField] private GameObject chairPrefab;
    
    [Tooltip("World position/rotation where the chair prefab spawns.")]
    [SerializeField] private Transform armchairStart;
    
    [Tooltip("How long to wait after chair spawns before revealing UI (allows animation to settle).")]
    [SerializeField] private float chairSettleDelay = 1.5f;

    [Tooltip("If true, waits for the chair intro animation to finish once, then disables the chair animator so code can take over its transform.")]
    [SerializeField] private bool disableChairAnimatorAfterIntroAnimation = true;

    [Tooltip("Safety timeout in seconds for disabling the chair animator if its intro animation state never reports completion.")]
    [SerializeField] private float chairAnimatorDisableTimeout = 5f;

    [Tooltip("How many degrees to rotate the spawned chair around Y when Play is pressed. Positive values rotate anticlockwise from above.")]
    [SerializeField] private float playSelectionChairRotationAngle = 180f;
    
    // =============================================================================
    // UI REFERENCES
    // =============================================================================
    
    [Header("UI References")]
    [Tooltip("Root GameObject of the main menu UI. Will be hidden on start and revealed after sequence.")]
    [SerializeField] private GameObject mainMenuRoot;
    
    [Tooltip("Optional: Click-to-start overlay shown before first click. Hidden when sequence starts.")]
    [SerializeField] private GameObject clickToStartOverlay;
    
    [Tooltip("Text to show while waiting for authentication.")]
    [SerializeField] private string connectingText = "Connecting...";
    
    [Tooltip("Text to show when authentication is complete and player can click.")]
    [SerializeField] private string clickToStartText = "TIKLA";
    
    [Tooltip("Text to show if authentication fails.")]
    [SerializeField] private string connectionFailedText = "Connection Failed";
    
    [Tooltip("Optional: Reference to MainScreen_V1 if explicit enable/disable is needed.")]
    [SerializeField] private MainScreen_V1 mainScreenController;

    [Header("Background Music")]
    [Tooltip("Optional persistent music controller used to transition the soundtrack during intro beats.")]
    [SerializeField] private BackgroundMusicController_V1 backgroundMusicController;

    [Tooltip("Transition duration when switching to the in-car radio sound.")]
    [SerializeField] private float carRadioMusicTransitionDuration = 0.25f;

    [Tooltip("Transition duration when the player exits the car and the music becomes quieter/outdoor.")]
    [SerializeField] private float outsideMusicTransitionDuration = 0.75f;

    [Tooltip("Transition duration when the player moves into the building and the music becomes full again.")]
    [SerializeField] private float interiorMusicTransitionDuration = 1.0f;
    
    // =============================================================================
    // TIMING SETTINGS
    // =============================================================================
    
    [Header("Timing")]
    [Tooltip("Duration in seconds to move camera from start to entrance position.")]
    [SerializeField] private float moveToEntranceDuration = 2.0f;
    
    [Tooltip("Delay after reaching entrance before door starts opening.")]
    [SerializeField] private float delayBeforeDoorOpens = 0.3f;
    
    [Tooltip("Delay after door opens before camera moves inside.")]
    [SerializeField] private float delayBeforeMoveInside = 0.5f;
    
    [Tooltip("Duration in seconds to move camera from entrance to entered position.")]
    [SerializeField] private float moveInsideDuration = 2.0f;

    [Tooltip("Duration in seconds to move camera through the play-button path.")]
    [SerializeField] private float moveToPlayDuration = 1.5f;
    
    [Tooltip("Delay after entering before chair spawns.")]
    [SerializeField] private float delayBeforeChairSpawns = 0.3f;
    
    [Header("Easing")]
    [Tooltip("Animation curve for camera movement easing. Default: ease in-out.")]
    [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("How much a full 360-degree camera rotation contributes to path duration when distributing movement time across anchors. Higher values make rotation-only moves take longer.")]
    [SerializeField] private float fullRotationDurationWeight = 3f;
    
    [Tooltip("Animation curve for door rotation easing.")]
    [SerializeField] private AnimationCurve doorCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    // =============================================================================
    // DEBUG SETTINGS
    // =============================================================================
    
    [Header("Debug")]
    [Tooltip("If true, sequence auto-starts on play (skips waiting for click). For editor testing.")]
    [SerializeField] private bool autoStartInEditor = false;
    
    [Tooltip("If true, logs state transitions to console.")]
    [SerializeField] private bool logStateChanges = true;
    
    // =============================================================================
    // RUNTIME STATE
    // =============================================================================
    
    private Vector3 cameraStartPosition;
    private Quaternion cameraStartRotation;
    private Quaternion doorClosedRotation;
    private Quaternion secondDoorClosedRotation;
    private float cameraStartFov;
    private GameObject spawnedLeaver;
    private GameObject spawnedChair;
    private Animator spawnedChairAnimator;
    private Animator startupCarAnimator;
    private Coroutine activeSequence;
    private Coroutine activePlaySequence;
    private Coroutine activeStartupLoadingCompletionSequence;
    private Coroutine activeChairAnimatorDisableSequence;
    private Coroutine activeChairPlayRotationSequence;
    private bool sequenceStarted = false;
    private float startupLoadingStartTime;
    private bool startupLoadingInitialized = false;
    private bool startupLoadingCompletionStarted = false;
    private bool isStartupCameraRotationLocked = false;
    private bool shouldDetachCameraFromStartupCar = false;
    private Transform initialCameraParent;
    
    // =============================================================================
    // UNITY LIFECYCLE
    // =============================================================================
    
    private void Awake()
    {
        clickToStartText = "TIKLA";

        // Get camera reference if not assigned
        if (introCamera == null)
        {
            introCamera = Camera.main;
        }

        if (introCamera != null)
        {
            initialCameraParent = introCamera.transform.parent;
        }

        ResolveBackgroundMusicControllerReference();
        CacheStartupCarAnimator();
    }
    
    private void Start()
    {
        InitializeSequence();
        
        // Auto-start for editor testing
        if (autoStartInEditor && Application.isEditor)
        {
            StartEntranceSequence();
        }
    }
    
    private void Update()
    {
        // Handle auth waiting state
        if (currentState == IntroState.WaitingForAuth)
        {
            UpdateAuthWaitingState();
            return;
        }
        
        // Once loading is ready, the first click continues into car drift + entrance.
        if (currentState == IntroState.WaitingForFirstClick && !sequenceStarted && !startupLoadingCompletionStarted)
        {
            if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
            {
                ContinueFromStartupLoadingIntoIntro();
            }
        }
    }

    private void LateUpdate()
    {
        ApplyStartupCameraRotationLock();
    }
    
    /// <summary>
    /// Checks authentication status and transitions to WaitingForFirstClick when ready.
    /// </summary>
    private void UpdateAuthWaitingState()
    {
        if (!startupLoadingInitialized)
        {
            BeginStartupLoadingSequence();
        }

        float elapsed = Mathf.Max(0f, Time.time - startupLoadingStartTime);
        UpdateStartupLoadingFov(elapsed);

        var services = UnityServicesInitializer_V1.Instance;

        // Check for errors
        if (services != null && services.HasError)
        {
            SetStartupLoadingText(connectionFailedText);
            // Optionally could allow offline play here in the future
            return;
        }

        bool authReady = services != null && services.IsReady;
        bool minimumDurationSatisfied = startupLoadingDuration <= 0f || elapsed >= startupLoadingDuration;

        if (!authReady || !minimumDurationSatisfied)
        {
            UpdateStartupLoadingText(elapsed);
            return;
        }

        SetStartupLoadingText(clickToStartText);
        SetState(IntroState.WaitingForFirstClick);
    }

    private void BeginStartupLoadingSequence()
    {
        startupLoadingStartTime = Time.time;
        startupLoadingInitialized = true;

        shouldDetachCameraFromStartupCar = introCamera != null && startupCar != null && introCamera.transform.IsChildOf(startupCar.transform);
        StartStartupCameraRotationLock();

        if (introCamera != null)
        {
            introCamera.fieldOfView = startupLoadingStartFov;
        }

        UpdateStartupLoadingText(0f);
    }

    private void UpdateStartupLoadingFov(float elapsed)
    {
        if (introCamera == null)
        {
            return;
        }

        if (startupLoadingDuration <= 0f)
        {
            return;
        }

        float t = Mathf.Clamp01(elapsed / startupLoadingDuration);
        float eased = movementCurve != null ? movementCurve.Evaluate(t) : t;
        introCamera.fieldOfView = Mathf.LerpUnclamped(startupLoadingStartFov, startupLoadingEndFov, eased);
    }

    private void UpdateStartupLoadingText(float elapsed)
    {
        if (string.IsNullOrEmpty(startupLoadingText))
        {
            SetStartupLoadingText(connectingText);
            return;
        }

        float safeInterval = Mathf.Max(0.05f, startupLoadingDotInterval);
        int dotCycle = Mathf.Max(1, startupLoadingMaxDots + 1);
        int dotCount = Mathf.FloorToInt(elapsed / safeInterval) % dotCycle;
        SetStartupLoadingText(startupLoadingText + new string('.', dotCount));
    }

    private void SetStartupLoadingText(string text)
    {
        if (startupLoadingStatusText != null)
        {
            startupLoadingStatusText.text = text;
        }
    }

    private void ContinueFromStartupLoadingIntoIntro()
    {
        if (startupLoadingCompletionStarted)
        {
            return;
        }

        startupLoadingCompletionStarted = true;

        if (clickToStartOverlay != null)
        {
            clickToStartOverlay.SetActive(false);
        }

        SetStartupLoadingText(string.Empty);

        activeStartupLoadingCompletionSequence = StartCoroutine(CompleteStartupLoadingSequence(UnityServicesInitializer_V1.Instance));
    }

    private IEnumerator CompleteStartupLoadingSequence(UnityServicesInitializer_V1 services)
    {
        if (introCamera != null)
        {
            introCamera.fieldOfView = startupLoadingEndFov;
        }

        yield return StartCoroutine(PlayStartupCarDriftSequence());

        yield return StartCoroutine(TransitionCameraToPostLoadingStartPosition());

        SetStartupLoadingText(string.Empty);

        string playerId = services != null ? services.PlayerId : "unknown";
        Debug.Log($"[IntroController] Auth complete, player ID: {playerId}");

        activeStartupLoadingCompletionSequence = null;
        StartEntranceSequence();
    }

    private void StartStartupCameraRotationLock()
    {
        if (!lockCameraRotationDuringStartupLoading || introCamera == null)
        {
            isStartupCameraRotationLocked = false;
            return;
        }

        isStartupCameraRotationLocked = true;
        ApplyStartupCameraRotationLock();
    }

    private void ApplyStartupCameraRotationLock()
    {
        if (!isStartupCameraRotationLocked || introCamera == null)
        {
            return;
        }

        float t = GetStartupLoadingRotationProgress();
        float x = Mathf.LerpAngle(startupLoadingCameraLockedEuler.x, startupLoadingCameraLockedEndEuler.x, t);
        float y = Mathf.LerpAngle(startupLoadingCameraLockedEuler.y, startupLoadingCameraLockedEndEuler.y, t);
        float z = Mathf.LerpAngle(startupLoadingCameraLockedEuler.z, startupLoadingCameraLockedEndEuler.z, t);

        introCamera.transform.rotation = Quaternion.Euler(x, y, z);
    }

    private float GetStartupLoadingRotationProgress()
    {
        if (!startupLoadingInitialized || startupLoadingDuration <= 0f)
        {
            return 0f;
        }

        float elapsed = Mathf.Max(0f, Time.time - startupLoadingStartTime);
        float normalized = Mathf.Clamp01(elapsed / startupLoadingDuration);
        return movementCurve != null ? movementCurve.Evaluate(normalized) : normalized;
    }

    private void StopStartupCameraRotationLock()
    {
        isStartupCameraRotationLocked = false;
    }

    private void DetachCameraFromStartupCarIfNeeded()
    {
        if (!shouldDetachCameraFromStartupCar || introCamera == null)
        {
            return;
        }

        introCamera.transform.SetParent(null, true);
        shouldDetachCameraFromStartupCar = false;
    }

    private IEnumerator TransitionCameraToStartupDriftPosition()
    {
        if (startupCarDriftCameraPos == null)
        {
            yield break;
        }

        if (startupCarDriftCameraTransitionStartDelay > 0f)
        {
            yield return new WaitForSeconds(startupCarDriftCameraTransitionStartDelay);
        }

        yield return StartCoroutine(TransitionCameraToAnchor(startupCarDriftCameraPos, startupCarDriftCameraTransitionDuration));
    }

    private IEnumerator TransitionCameraToPostLoadingStartPosition()
    {
        StopStartupCameraRotationLock();
        DetachCameraFromStartupCarIfNeeded();

        yield return StartCoroutine(TransitionCameraToAnchor(cameraStartPos, startupLoadingCameraTransitionDuration));
    }

    private IEnumerator TransitionCameraToAnchor(Transform targetAnchor, float duration)
    {
        if (introCamera == null || targetAnchor == null)
        {
            yield break;
        }

        float safeDuration = Mathf.Max(0f, duration);
        bool canUseLocalSpace = introCamera.transform.parent != null && introCamera.transform.parent == targetAnchor.parent;

        if (safeDuration <= 0.001f)
        {
            if (canUseLocalSpace)
            {
                introCamera.transform.localPosition = targetAnchor.localPosition;
                introCamera.transform.localRotation = targetAnchor.localRotation;
            }
            else
            {
                introCamera.transform.position = targetAnchor.position;
                introCamera.transform.rotation = targetAnchor.rotation;
            }

            yield break;
        }

        Vector3 startPosition = canUseLocalSpace ? introCamera.transform.localPosition : introCamera.transform.position;
        Quaternion startRotation = canUseLocalSpace ? introCamera.transform.localRotation : introCamera.transform.rotation;
        Vector3 endPosition = canUseLocalSpace ? targetAnchor.localPosition : targetAnchor.position;
        Quaternion endRotation = canUseLocalSpace ? targetAnchor.localRotation : targetAnchor.rotation;

        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            float eased = movementCurve != null ? movementCurve.Evaluate(t) : t;

            if (canUseLocalSpace)
            {
                introCamera.transform.localPosition = Vector3.LerpUnclamped(startPosition, endPosition, eased);
                introCamera.transform.localRotation = Quaternion.SlerpUnclamped(startRotation, endRotation, eased);
            }
            else
            {
                introCamera.transform.position = Vector3.LerpUnclamped(startPosition, endPosition, eased);
                introCamera.transform.rotation = Quaternion.SlerpUnclamped(startRotation, endRotation, eased);
            }

            yield return null;
        }

        if (canUseLocalSpace)
        {
            introCamera.transform.localPosition = endPosition;
            introCamera.transform.localRotation = endRotation;
        }
        else
        {
            introCamera.transform.position = endPosition;
            introCamera.transform.rotation = endRotation;
        }
    }

    private IEnumerator PlayStartupCarDriftSequence()
    {
        if (string.IsNullOrWhiteSpace(startupCarDriftTriggerName))
        {
            yield return StartCoroutine(TransitionCameraToStartupDriftPosition());
            yield break;
        }

        CacheStartupCarAnimator();
        Animator carAnimator = startupCarAnimator;
        if (carAnimator == null || !carAnimator.isActiveAndEnabled)
        {
            yield return StartCoroutine(TransitionCameraToStartupDriftPosition());
            yield break;
        }

        AnimatorStateInfo initialStateInfo = carAnimator.GetCurrentAnimatorStateInfo(0);
        int initialStateHash = initialStateInfo.fullPathHash;

        carAnimator.ResetTrigger(startupCarDriftTriggerName);
        carAnimator.SetTrigger(startupCarDriftTriggerName);

        Coroutine driftCameraTransition = null;
        if (startupCarDriftCameraPos != null)
        {
            driftCameraTransition = StartCoroutine(TransitionCameraToStartupDriftPosition());
        }

        if (!waitForStartupCarDriftToFinish)
        {
            if (driftCameraTransition != null)
            {
                yield return driftCameraTransition;
            }

            yield break;
        }

        float timeout = Mathf.Max(0.1f, startupCarDriftTimeout);
        float elapsed = 0f;
        bool driftStarted = false;
        int driftStateHash = 0;

        while (elapsed < timeout)
        {
            if (carAnimator == null || !carAnimator.isActiveAndEnabled)
            {
                if (driftCameraTransition != null)
                {
                    yield return driftCameraTransition;
                }

                yield break;
            }

            bool isTransitioning = carAnimator.IsInTransition(0);
            AnimatorStateInfo stateInfo = carAnimator.GetCurrentAnimatorStateInfo(0);
            int currentStateHash = stateInfo.fullPathHash;

            if (!driftStarted)
            {
                if (currentStateHash != initialStateHash)
                {
                    driftStarted = true;
                    driftStateHash = currentStateHash;

                    if (logStateChanges)
                    {
                        Debug.Log("[IntroController_V1] Startup drift animation started.");
                    }
                }
            }
            else
            {
                bool finishedByClipEnd = !stateInfo.loop && currentStateHash == driftStateHash && stateInfo.normalizedTime >= 1f && !isTransitioning;
                bool finishedByStateExit = currentStateHash != driftStateHash && !isTransitioning;

                if (finishedByClipEnd || finishedByStateExit)
                {
                    if (logStateChanges)
                    {
                        Debug.Log("[IntroController_V1] Startup drift animation finished.");
                    }

                    if (driftCameraTransition != null)
                    {
                        yield return driftCameraTransition;
                    }

                    yield break;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (driftCameraTransition != null)
        {
            yield return driftCameraTransition;
        }

        Debug.LogWarning("[IntroController_V1] Startup drift wait timed out. Continuing with camera transition.");
    }

    private void CacheStartupCarAnimator()
    {
        startupCarAnimator = null;

        if (startupCar == null)
        {
            return;
        }

        startupCarAnimator = startupCar.GetComponent<Animator>();
        if (startupCarAnimator == null)
        {
            startupCarAnimator = startupCar.GetComponentInChildren<Animator>();
        }
    }
    

    private void ResolveBackgroundMusicControllerReference()
    {
        if (backgroundMusicController == null)
        {
            backgroundMusicController = FindObjectOfType<BackgroundMusicController_V1>();
        }
    }

    private void ApplyBackgroundMusicForIntroState(IntroState state)
    {
        ResolveBackgroundMusicControllerReference();

        if (backgroundMusicController == null)
        {
            return;
        }

        switch (state)
        {
            case IntroState.WaitingForAuth:
            case IntroState.WaitingForFirstClick:
                backgroundMusicController.SetMusicState(BackgroundMusicController_V1.MusicState.CarRadio, carRadioMusicTransitionDuration);
                break;

            case IntroState.MovingToEntrance:
            case IntroState.OpeningDoorAndSpawningLeaver:
                backgroundMusicController.SetMusicState(BackgroundMusicController_V1.MusicState.Outside, outsideMusicTransitionDuration);
                break;

            case IntroState.MovingInside:
            case IntroState.SpawningChair:
            case IntroState.RevealingMainScreen:
            case IntroState.MovingToPlaySelection:
            case IntroState.Idle:
                backgroundMusicController.SetMusicState(BackgroundMusicController_V1.MusicState.Interior, interiorMusicTransitionDuration);
                break;
        }
    }
    
    private void OnDestroy()
    {
        // Cleanup any spawned objects
        CleanupSpawnedObjects();
    }
    
    // =============================================================================
    // INITIALIZATION
    // =============================================================================
    
    /// <summary>
    /// Sets up initial state for the intro sequence.
    /// Call this on scene load or when resetting the intro.
    /// </summary>
    public void InitializeSequence()
    {
        UpgradeLegacyCameraPathsIfNeeded();
        CacheStartupCarAnimator();
        StopStartupCameraRotationLock();

        if (introCamera != null && initialCameraParent != null && introCamera.transform.parent != initialCameraParent)
        {
            introCamera.transform.SetParent(initialCameraParent, true);
        }

        if (activeStartupLoadingCompletionSequence != null)
        {
            StopCoroutine(activeStartupLoadingCompletionSequence);
            activeStartupLoadingCompletionSequence = null;
        }

        // Cache starting transforms
        if (introCamera != null)
        {
            cameraStartPosition = introCamera.transform.position;
            cameraStartRotation = introCamera.transform.rotation;
            cameraStartFov = introCamera.fieldOfView;
        }
        
        if (doorToOpen != null)
        {
            doorClosedRotation = doorToOpen.localRotation;
        }

        if (secondDoorToOpen != null)
        {
            secondDoorClosedRotation = secondDoorToOpen.localRotation;
        }
        
        // Hide main menu
        if (mainMenuRoot != null)
        {
            mainMenuRoot.SetActive(false);
        }
        
        // Show click-to-start overlay
        if (clickToStartOverlay != null)
        {
            clickToStartOverlay.SetActive(true);
        }
        
        // Cleanup any leftover spawned objects from previous runs
        CleanupSpawnedObjects();
        
        // Reset state
        sequenceStarted = false;
        startupLoadingCompletionStarted = false;
        shouldDetachCameraFromStartupCar = false;

        // Startup always begins with loading visuals, then transitions to click-to-start.
        BeginStartupLoadingSequence();
        SetState(IntroState.WaitingForAuth);
    }
    
    /// <summary>
    /// Resets the intro to initial state (for scene reload or restart).
    /// </summary>
    public void ResetSequence()
    {
        // Stop any running sequence
        if (activeSequence != null)
        {
            StopCoroutine(activeSequence);
            activeSequence = null;
        }

        if (activePlaySequence != null)
        {
            StopCoroutine(activePlaySequence);
            activePlaySequence = null;
        }

        if (activeStartupLoadingCompletionSequence != null)
        {
            StopCoroutine(activeStartupLoadingCompletionSequence);
            activeStartupLoadingCompletionSequence = null;
        }

        StopChairAnimatorDisableSequence();
        StopChairPlayRotationSequence();
        StopStartupCameraRotationLock();
        
        // Reset camera to start position
        if (introCamera != null)
        {
            introCamera.transform.position = cameraStartPosition;
            introCamera.transform.rotation = cameraStartRotation;
            introCamera.fieldOfView = cameraStartFov;
        }
        
        // Reset door to closed
        if (doorToOpen != null)
        {
            doorToOpen.localRotation = doorClosedRotation;
        }

        if (secondDoorToOpen != null)
        {
            secondDoorToOpen.localRotation = secondDoorClosedRotation;
        }
        
        InitializeSequence();
    }
    
    // =============================================================================
    // SEQUENCE CONTROL
    // =============================================================================
    
    /// <summary>
    /// Starts the entrance sequence. Called on first click or auto-start.
    /// </summary>
    public void StartEntranceSequence()
    {
        if (sequenceStarted)
        {
            Debug.LogWarning("[IntroController_V1] Sequence already started, ignoring duplicate start request.");
            return;
        }
        
        sequenceStarted = true;
        
        // Hide click-to-start overlay
        if (clickToStartOverlay != null)
        {
            clickToStartOverlay.SetActive(false);
        }
        
        // Start the sequence coroutine
        activeSequence = StartCoroutine(RunEntranceSequence());
    }

    /// <summary>
    /// Starts the play-button camera transition and invokes a callback after the move completes.
    /// </summary>
    public bool StartPlaySelectionSequence(Action onComplete = null)
    {
        if (activePlaySequence != null)
        {
            Debug.LogWarning("[IntroController_V1] Play transition already running, ignoring duplicate request.");
            return false;
        }

        if (activeSequence != null || currentState != IntroState.Idle)
        {
            Debug.LogWarning("[IntroController_V1] Play transition can only run after the intro sequence is complete.");
            return false;
        }

        if (!HasAnyValidAnchors(playPath))
        {
            onComplete?.Invoke();
            return false;
        }

        activePlaySequence = StartCoroutine(RunPlaySelectionSequence(onComplete));
        return true;
    }
    
    /// <summary>
    /// Main sequence coroutine that orchestrates all entrance beats.
    /// </summary>
    private IEnumerator RunEntranceSequence()
    {
        Coroutine doorCoroutine = null;
        Coroutine secondDoorCoroutine = null;
        bool doorOpenStarted = false;

        void BeginDoorOpenBeat()
        {
            if (doorOpenStarted)
            {
                return;
            }

            doorOpenStarted = true;
            SetState(IntroState.OpeningDoorAndSpawningLeaver);

            bool triggeredMainDoorAnimator = TriggerDoorAnimator(doorAnimator, doorOpenTriggerName, "main");
            bool triggeredSecondDoorAnimator = TriggerDoorAnimator(secondDoorAnimator, secondDoorOpenTriggerName, "second");

            if (!triggeredMainDoorAnimator && doorToOpen != null)
            {
                doorCoroutine = StartCoroutine(RotateDoorOpen(doorToOpen, doorClosedRotation, doorOpenAngle, doorOpenDuration));
            }

            if (!triggeredSecondDoorAnimator && secondDoorToOpen != null)
            {
                secondDoorCoroutine = StartCoroutine(RotateDoorOpen(secondDoorToOpen, secondDoorClosedRotation, secondDoorOpenAngle, secondDoorOpenDuration));
            }

            SpawnLeaver();
        }

        // =================================================================
        // BEAT 1: Move camera to entrance position
        // =================================================================
        SetState(IntroState.MovingToEntrance);
        
        if (introCamera != null)
        {
            yield return StartCoroutine(MoveCameraAlongPath(entrancePath, moveToEntranceDuration, (segmentIndex, segmentCount) =>
            {
                if (segmentCount > 0 && segmentIndex == segmentCount - 1)
                {
                    BeginDoorOpenBeat();
                }
            }));
        }

        // Fallback if path had no valid anchors or camera reference was missing.
        if (!doorOpenStarted)
        {
            BeginDoorOpenBeat();
        }
        
        // Small delay after door starts opening
        if (delayBeforeDoorOpens > 0f)
        {
            yield return new WaitForSeconds(delayBeforeDoorOpens);
        }
        
        // Wait for fallback scripted rotation only. Animator-driven doors continue independently.
        if (doorCoroutine != null)
        {
            yield return doorCoroutine;
        }

        if (secondDoorCoroutine != null)
        {
            yield return secondDoorCoroutine;
        }
        
        // Delay before moving inside
        if (delayBeforeMoveInside > 0f)
        {
            yield return new WaitForSeconds(delayBeforeMoveInside);
        }
        
        // =================================================================
        // BEAT 3: Move camera inside
        // =================================================================
        SetState(IntroState.MovingInside);
        
        if (introCamera != null)
        {
            yield return StartCoroutine(MoveCameraAlongPath(enteredPath, moveInsideDuration));
        }
        
        // Delay before chair spawns
        if (delayBeforeChairSpawns > 0f)
        {
            yield return new WaitForSeconds(delayBeforeChairSpawns);
        }
        
        // =================================================================
        // BEAT 4: Spawn chair
        // =================================================================
        SetState(IntroState.SpawningChair);
        
        SpawnChair();
        
        // Wait for chair to settle
        if (chairSettleDelay > 0f)
        {
            yield return new WaitForSeconds(chairSettleDelay);
        }
        
        // =================================================================
        // BEAT 5: Reveal main menu
        // =================================================================
        SetState(IntroState.RevealingMainScreen);
        
        RevealMainMenu();
        
        // =================================================================
        // DONE
        // =================================================================
        SetState(IntroState.Idle);
        activeSequence = null;
    }

    /// <summary>
    /// Moves the camera through the play path at constant speed so the entire move reads as one motion.
    /// </summary>
    private IEnumerator RunPlaySelectionSequence(Action onComplete)
    {
        SetState(IntroState.MovingToPlaySelection);

        float chairRotationDuration = GetPlaySelectionChairRotationDuration();
        if (spawnedChair != null && Mathf.Abs(playSelectionChairRotationAngle) > 0.01f && chairRotationDuration > 0f)
        {
            StopChairPlayRotationSequence();
            activeChairPlayRotationSequence = StartCoroutine(RotateChairForPlaySelection(spawnedChair.transform, playSelectionChairRotationAngle, chairRotationDuration));
        }

        yield return StartCoroutine(MoveCameraAlongLinearPath(playPath, moveToPlayDuration));

        if (activeChairPlayRotationSequence != null)
        {
            yield return activeChairPlayRotationSequence;
        }

        SetState(IntroState.Idle);
        activePlaySequence = null;
        onComplete?.Invoke();
    }
    
    // =============================================================================
    // CAMERA MOVEMENT
    // =============================================================================
    
    /// <summary>
    /// Upgrades old single-anchor fields into the new path lists so existing scene data keeps working.
    /// </summary>
    private void UpgradeLegacyCameraPathsIfNeeded()
    {
        UpgradeLegacyPathIfNeeded(entrancePath, legacyEntrancePositions, legacyEntrancePosition);
        UpgradeLegacyPathIfNeeded(enteredPath, legacyEnteredPositions, legacyEnteredPosition);
        UpgradeLegacyPathIfNeeded(playPath, legacyPlayPositions, null);
    }

    private void UpgradeLegacyPathIfNeeded(List<CameraPathPoint> targetPath, List<Transform> legacyTransforms, Transform legacySingleTransform)
    {
        if (targetPath == null || targetPath.Count > 0)
        {
            return;
        }

        if (legacyTransforms != null)
        {
            for (int i = 0; i < legacyTransforms.Count; i++)
            {
                if (legacyTransforms[i] != null)
                {
                    targetPath.Add(new CameraPathPoint { target = legacyTransforms[i] });
                }
            }
        }

        if (targetPath.Count == 0 && legacySingleTransform != null)
        {
            targetPath.Add(new CameraPathPoint { target = legacySingleTransform });
        }
    }

    /// <summary>
    /// Smoothly moves the camera through an ordered list of anchor transforms over the specified total duration.
    /// </summary>
    private IEnumerator MoveCameraAlongPath(List<CameraPathPoint> pathPoints, float totalDuration, Action<int, int> onBeforeSegmentMove = null)
    {
        if (introCamera == null || pathPoints == null || pathPoints.Count == 0)
        {
            yield break;
        }

        List<CameraPathPoint> validPoints = GetValidPathPoints(pathPoints);

        if (validPoints.Count == 0)
        {
            yield break;
        }

        Vector3 segmentStartPos = introCamera.transform.position;
        Quaternion segmentStartRot = introCamera.transform.rotation;

        if (HasAnyCustomDurations(validPoints))
        {
            yield return StartCoroutine(MoveCameraPathWithCustomTiming(validPoints, segmentStartPos, segmentStartRot, totalDuration, onBeforeSegmentMove));
            yield break;
        }

        if (validPoints.Count == 1)
        {
            onBeforeSegmentMove?.Invoke(0, 1);
            yield return StartCoroutine(MoveCameraSegment(segmentStartPos, segmentStartRot, validPoints[0].target, totalDuration));

            if (validPoints[0].waitAfterArrival > 0f)
            {
                yield return new WaitForSeconds(validPoints[0].waitAfterArrival);
            }

            yield break;
        }

        float totalDistance = 0f;
        Vector3 previousPoint = segmentStartPos;
        Quaternion previousRotation = segmentStartRot;

        for (int i = 0; i < validPoints.Count; i++)
        {
            totalDistance += GetCameraTransitionWeight(previousPoint, previousRotation, validPoints[i].target.position, validPoints[i].target.rotation);
            previousPoint = validPoints[i].target.position;
            previousRotation = validPoints[i].target.rotation;
        }

        if (totalDistance <= 0.001f)
        {
            for (int i = 0; i < validPoints.Count; i++)
            {
                introCamera.transform.position = validPoints[i].target.position;
                introCamera.transform.rotation = validPoints[i].target.rotation;

                if (validPoints[i].waitAfterArrival > 0f)
                {
                    yield return new WaitForSeconds(validPoints[i].waitAfterArrival);
                }
            }

            yield break;
        }

        for (int i = 0; i < validPoints.Count; i++)
        {
            CameraPathPoint targetPoint = validPoints[i];
            Transform targetAnchor = targetPoint.target;
            float segmentDistance = GetCameraTransitionWeight(segmentStartPos, segmentStartRot, targetAnchor.position, targetAnchor.rotation);
            float segmentDuration = totalDuration * (segmentDistance / totalDistance);

            onBeforeSegmentMove?.Invoke(i, validPoints.Count);
            yield return StartCoroutine(MoveCameraSegment(segmentStartPos, segmentStartRot, targetAnchor, segmentDuration));

            if (targetPoint.waitAfterArrival > 0f)
            {
                yield return new WaitForSeconds(targetPoint.waitAfterArrival);
            }

            segmentStartPos = targetAnchor.position;
            segmentStartRot = targetAnchor.rotation;
        }
    }

    /// <summary>
    /// Moves the camera through all anchors as a single constant-speed path instead of easing per segment.
    /// </summary>
    private IEnumerator MoveCameraAlongLinearPath(List<CameraPathPoint> pathPoints, float totalDuration)
    {
        if (introCamera == null || pathPoints == null || pathPoints.Count == 0)
        {
            yield break;
        }

        List<CameraPathPoint> validPoints = GetValidPathPoints(pathPoints);
        if (validPoints.Count == 0)
        {
            yield break;
        }

        if (HasAnyCustomDurations(validPoints) || HasAnyWaits(validPoints))
        {
            yield return StartCoroutine(MoveCameraPathWithCustomTiming(validPoints, introCamera.transform.position, introCamera.transform.rotation, totalDuration));
            yield break;
        }

        List<Vector3> positions = new List<Vector3>(validPoints.Count + 1)
        {
            introCamera.transform.position
        };

        List<Quaternion> rotations = new List<Quaternion>(validPoints.Count + 1)
        {
            introCamera.transform.rotation
        };

        for (int i = 0; i < validPoints.Count; i++)
        {
            positions.Add(validPoints[i].target.position);
            rotations.Add(validPoints[i].target.rotation);
        }

        float[] cumulativeWeights = new float[positions.Count];
        float totalDistance = 0f;

        for (int i = 1; i < positions.Count; i++)
        {
            totalDistance += GetCameraTransitionWeight(positions[i - 1], rotations[i - 1], positions[i], rotations[i]);
            cumulativeWeights[i] = totalDistance;
        }

        if (totalDistance <= 0.001f)
        {
            introCamera.transform.position = positions[positions.Count - 1];
            introCamera.transform.rotation = rotations[rotations.Count - 1];
            yield break;
        }

        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.001f, totalDuration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / safeDuration);
            float distanceAlongPath = normalizedTime * totalDistance;
            int segmentIndex = GetSegmentIndexForDistance(cumulativeWeights, distanceAlongPath);
            float segmentStartDistance = cumulativeWeights[segmentIndex];
            float segmentEndDistance = cumulativeWeights[segmentIndex + 1];
            float segmentT = Mathf.InverseLerp(segmentStartDistance, segmentEndDistance, distanceAlongPath);

            introCamera.transform.position = Vector3.LerpUnclamped(positions[segmentIndex], positions[segmentIndex + 1], segmentT);
            introCamera.transform.rotation = Quaternion.SlerpUnclamped(rotations[segmentIndex], rotations[segmentIndex + 1], segmentT);

            yield return null;
        }

        introCamera.transform.position = positions[positions.Count - 1];
        introCamera.transform.rotation = rotations[rotations.Count - 1];
    }

    private IEnumerator MoveCameraPathWithCustomTiming(List<CameraPathPoint> validPoints, Vector3 startPosition, Quaternion startRotation, float fallbackTotalDuration, Action<int, int> onBeforeSegmentMove = null)
    {
        float totalWeight = 0f;
        Vector3 previousPosition = startPosition;
        Quaternion previousRotation = startRotation;

        for (int i = 0; i < validPoints.Count; i++)
        {
            totalWeight += GetCameraTransitionWeight(previousPosition, previousRotation, validPoints[i].target.position, validPoints[i].target.rotation);
            previousPosition = validPoints[i].target.position;
            previousRotation = validPoints[i].target.rotation;
        }

        previousPosition = startPosition;
        previousRotation = startRotation;

        for (int i = 0; i < validPoints.Count; i++)
        {
            CameraPathPoint point = validPoints[i];
            float segmentWeight = GetCameraTransitionWeight(previousPosition, previousRotation, point.target.position, point.target.rotation);
            float segmentDuration = ResolveSegmentDuration(point, fallbackTotalDuration, totalWeight, segmentWeight);

            onBeforeSegmentMove?.Invoke(i, validPoints.Count);
            yield return StartCoroutine(MoveCameraSegment(previousPosition, previousRotation, point.target, segmentDuration));

            if (point.waitAfterArrival > 0f)
            {
                yield return new WaitForSeconds(point.waitAfterArrival);
            }

            previousPosition = point.target.position;
            previousRotation = point.target.rotation;
        }
    }

    private List<CameraPathPoint> GetValidPathPoints(List<CameraPathPoint> pathPoints)
    {
        List<CameraPathPoint> validPoints = new List<CameraPathPoint>();

        if (pathPoints == null)
        {
            return validPoints;
        }

        for (int i = 0; i < pathPoints.Count; i++)
        {
            CameraPathPoint point = pathPoints[i];
            if (point != null && point.target != null)
            {
                validPoints.Add(point);
            }
        }

        return validPoints;
    }

    private bool HasAnyValidAnchors(List<CameraPathPoint> pathPoints)
    {
        if (pathPoints == null)
        {
            return false;
        }

        for (int i = 0; i < pathPoints.Count; i++)
        {
            CameraPathPoint point = pathPoints[i];
            if (point != null && point.target != null)
            {
                return true;
            }
        }

        return false;
    }

    private int GetSegmentIndexForDistance(float[] cumulativeDistances, float distanceAlongPath)
    {
        for (int i = 0; i < cumulativeDistances.Length - 1; i++)
        {
            if (distanceAlongPath <= cumulativeDistances[i + 1])
            {
                return i;
            }
        }

        return Mathf.Max(0, cumulativeDistances.Length - 2);
    }

    private bool HasAnyCustomDurations(List<CameraPathPoint> pathPoints)
    {
        for (int i = 0; i < pathPoints.Count; i++)
        {
            if (pathPoints[i].moveDuration > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyWaits(List<CameraPathPoint> pathPoints)
    {
        for (int i = 0; i < pathPoints.Count; i++)
        {
            if (pathPoints[i].waitAfterArrival > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private float ResolveSegmentDuration(CameraPathPoint point, float fallbackTotalDuration, float totalWeight, float segmentWeight)
    {
        if (point.moveDuration > 0f)
        {
            return point.moveDuration;
        }

        if (totalWeight <= 0.001f)
        {
            return fallbackTotalDuration;
        }

        return fallbackTotalDuration * (segmentWeight / totalWeight);
    }

    private float GetCameraTransitionWeight(Vector3 startPos, Quaternion startRot, Vector3 endPos, Quaternion endRot)
    {
        float positionDistance = Vector3.Distance(startPos, endPos);
        float rotationWeight = GetRotationWeight(startRot, endRot);
        return positionDistance + rotationWeight;
    }

    private float GetRotationWeight(Quaternion startRot, Quaternion endRot)
    {
        if (fullRotationDurationWeight <= 0f)
        {
            return 0f;
        }

        float angle = Quaternion.Angle(startRot, endRot);
        return (angle / 360f) * fullRotationDurationWeight;
    }

    /// <summary>
    /// Smoothly moves the camera from a start transform snapshot to a target anchor.
    /// </summary>
    private IEnumerator MoveCameraSegment(Vector3 startPos, Quaternion startRot, Transform targetAnchor, float duration)
    {
        if (introCamera == null || targetAnchor == null)
        {
            yield break;
        }

        Vector3 endPos = targetAnchor.position;
        Quaternion endRot = targetAnchor.rotation;
        
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.001f, duration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            float eased = movementCurve.Evaluate(t);
            
            introCamera.transform.position = Vector3.LerpUnclamped(startPos, endPos, eased);
            introCamera.transform.rotation = Quaternion.SlerpUnclamped(startRot, endRot, eased);
            
            yield return null;
        }
        
        // Snap to final position
        introCamera.transform.position = endPos;
        introCamera.transform.rotation = endRot;
    }
    
    // =============================================================================
    // DOOR CONTROL
    // =============================================================================

    private bool TriggerDoorAnimator(Animator targetAnimator, string triggerName, string doorLabel)
    {
        if (targetAnimator == null || !targetAnimator.isActiveAndEnabled || string.IsNullOrWhiteSpace(triggerName))
        {
            return false;
        }

        targetAnimator.ResetTrigger(triggerName);
        targetAnimator.SetTrigger(triggerName);

        if (logStateChanges)
        {
            Debug.Log($"[IntroController_V1] Triggered {doorLabel} door animator with '{triggerName}'.");
        }

        return true;
    }
    
    /// <summary>
    /// Rotates the door open on Y axis by doorOpenAngle degrees.
    /// </summary>
    private IEnumerator RotateDoorOpen(Transform doorTransform, Quaternion closedRotation, float openAngle, float duration)
    {
        if (doorTransform == null)
        {
            yield break;
        }

        Vector3 closedEuler = closedRotation.eulerAngles;
        Vector3 startEuler = doorTransform.localEulerAngles;
        float startY = startEuler.y;
        float targetY = closedEuler.y + openAngle;
        
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.001f, duration);
        
        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            float eased = doorCurve.Evaluate(t);

            float currentY = Mathf.LerpAngle(startY, targetY, eased);
            doorTransform.localEulerAngles = new Vector3(closedEuler.x, currentY, closedEuler.z);
            
            yield return null;
        }

        // Snap to final rotation while preserving original X/Z axes.
        doorTransform.localEulerAngles = new Vector3(closedEuler.x, targetY, closedEuler.z);
    }
    
    // =============================================================================
    // PREFAB SPAWNING
    // =============================================================================
    
    /// <summary>
    /// Instantiates the leaver prefab at the designated start position.
    /// </summary>
    private void SpawnLeaver()
    {
        if (leaverPrefab == null)
        {
            Debug.LogWarning("[IntroController_V1] Leaver prefab not assigned, skipping spawn.");
            return;
        }
        
        Vector3 spawnPos = leaverStart != null ? leaverStart.position : Vector3.zero;
        Quaternion spawnRot = leaverStart != null ? leaverStart.rotation : Quaternion.identity;
        
        spawnedLeaver = Instantiate(leaverPrefab, spawnPos, spawnRot);
        spawnedLeaver.name = "IntroLeaver_Instance";
        
        if (logStateChanges)
        {
            Debug.Log($"[IntroController_V1] Spawned leaver at {spawnPos}");
        }
        
        // Auto-destroy after lifetime if specified
        if (leaverLifetime > 0f && spawnedLeaver != null)
        {
            Destroy(spawnedLeaver, leaverLifetime);
        }
    }
    
    /// <summary>
    /// Instantiates the chair prefab at the designated start position.
    /// </summary>
    private void SpawnChair()
    {
        if (chairPrefab == null)
        {
            Debug.LogWarning("[IntroController_V1] Chair prefab not assigned, skipping spawn.");
            return;
        }
        
        Vector3 spawnPos = armchairStart != null ? armchairStart.position : Vector3.zero;
        Quaternion spawnRot = armchairStart != null ? armchairStart.rotation : Quaternion.identity;
        
        spawnedChair = Instantiate(chairPrefab, spawnPos, spawnRot);
        spawnedChair.name = "IntroChair_Instance";
        spawnedChairAnimator = spawnedChair.GetComponentInChildren<Animator>();

        if (disableChairAnimatorAfterIntroAnimation && spawnedChairAnimator != null)
        {
            StopChairAnimatorDisableSequence();
            activeChairAnimatorDisableSequence = StartCoroutine(DisableChairAnimatorAfterIntroAnimation(spawnedChairAnimator));
        }
        
        if (logStateChanges)
        {
            Debug.Log($"[IntroController_V1] Spawned chair at {spawnPos}");
        }
    }
    
    /// <summary>
    /// Cleans up any spawned intro objects.
    /// </summary>
    private void CleanupSpawnedObjects()
    {
        StopChairAnimatorDisableSequence();
        StopChairPlayRotationSequence();

        if (spawnedLeaver != null)
        {
            Destroy(spawnedLeaver);
            spawnedLeaver = null;
        }
        
        if (spawnedChair != null)
        {
            Destroy(spawnedChair);
            spawnedChair = null;
        }

        spawnedChairAnimator = null;
    }

    private IEnumerator DisableChairAnimatorAfterIntroAnimation(Animator chairAnimator)
    {
        if (chairAnimator == null)
        {
            activeChairAnimatorDisableSequence = null;
            yield break;
        }

        float elapsed = 0f;
        bool observedPlayableState = false;

        while (elapsed < chairAnimatorDisableTimeout)
        {
            if (chairAnimator == null)
            {
                activeChairAnimatorDisableSequence = null;
                yield break;
            }

            if (!chairAnimator.enabled || !chairAnimator.isActiveAndEnabled)
            {
                activeChairAnimatorDisableSequence = null;
                yield break;
            }

            AnimatorStateInfo stateInfo = chairAnimator.GetCurrentAnimatorStateInfo(0);
            bool isTransitioning = chairAnimator.IsInTransition(0);

            if (!isTransitioning && stateInfo.length > 0f)
            {
                observedPlayableState = true;

                if (!stateInfo.loop && stateInfo.normalizedTime >= 1f)
                {
                    break;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (chairAnimator != null && chairAnimator.enabled)
        {
            chairAnimator.enabled = false;

            if (logStateChanges)
            {
                string reason = observedPlayableState ? "after intro animation completed" : "after timeout safeguard";
                Debug.Log($"[IntroController_V1] Disabled chair animator {reason}.");
            }
        }

        activeChairAnimatorDisableSequence = null;
    }

    private void StopChairAnimatorDisableSequence()
    {
        if (activeChairAnimatorDisableSequence != null)
        {
            StopCoroutine(activeChairAnimatorDisableSequence);
            activeChairAnimatorDisableSequence = null;
        }
    }

    private float GetPlaySelectionChairRotationDuration()
    {
        if (moveToPlayDuration <= 0f)
        {
            return 0f;
        }

        List<CameraPathPoint> validPoints = GetValidPathPoints(playPath);
        if (validPoints.Count == 0 || introCamera == null)
        {
            return 0f;
        }

        for (int i = 0; i < validPoints.Count; i++)
        {
            if (validPoints[i].moveDuration > 0f)
            {
                return validPoints[i].moveDuration;
            }
        }

        List<CameraPathPoint> validAnchors = validPoints;
        if (validAnchors.Count == 0 || introCamera == null)
        {
            return 0f;
        }

        Vector3 startPosition = introCamera.transform.position;
        Quaternion startRotation = introCamera.transform.rotation;
        float totalWeight = 0f;
        float firstSegmentWeight = GetCameraTransitionWeight(startPosition, startRotation, validAnchors[0].target.position, validAnchors[0].target.rotation);

        Vector3 previousPosition = startPosition;
        Quaternion previousRotation = startRotation;
        for (int i = 0; i < validAnchors.Count; i++)
        {
            totalWeight += GetCameraTransitionWeight(previousPosition, previousRotation, validAnchors[i].target.position, validAnchors[i].target.rotation);
            previousPosition = validAnchors[i].target.position;
            previousRotation = validAnchors[i].target.rotation;
        }

        if (totalWeight <= 0.001f)
        {
            return moveToPlayDuration;
        }

        return moveToPlayDuration * (firstSegmentWeight / totalWeight);
    }

    private IEnumerator RotateChairForPlaySelection(Transform chairTransform, float rotationAngle, float duration)
    {
        if (chairTransform == null)
        {
            activeChairPlayRotationSequence = null;
            yield break;
        }

        Quaternion startRotation = chairTransform.rotation;
        Quaternion endRotation = startRotation * Quaternion.Euler(0f, rotationAngle, 0f);
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.001f, duration);

        while (elapsed < safeDuration)
        {
            if (chairTransform == null)
            {
                activeChairPlayRotationSequence = null;
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            float eased = movementCurve.Evaluate(t);
            chairTransform.rotation = Quaternion.SlerpUnclamped(startRotation, endRotation, eased);
            yield return null;
        }

        if (chairTransform != null)
        {
            chairTransform.rotation = endRotation;
        }

        activeChairPlayRotationSequence = null;
    }

    private void StopChairPlayRotationSequence()
    {
        if (activeChairPlayRotationSequence != null)
        {
            StopCoroutine(activeChairPlayRotationSequence);
            activeChairPlayRotationSequence = null;
        }
    }
    
    // =============================================================================
    // UI CONTROL
    // =============================================================================
    
    /// <summary>
    /// Reveals the main menu UI after the entrance sequence completes.
    /// </summary>
    private void RevealMainMenu()
    {
        if (mainMenuRoot != null)
        {
            mainMenuRoot.SetActive(true);
            
            if (logStateChanges)
            {
                Debug.Log("[IntroController_V1] Main menu revealed");
            }
        }
        
        // Optionally activate the MainScreen controller if it was disabled
        if (mainScreenController != null && !mainScreenController.enabled)
        {
            mainScreenController.enabled = true;
        }
    }
    
    // =============================================================================
    // STATE MANAGEMENT
    // =============================================================================
    
    private void SetState(IntroState newState)
    {
        if (currentState == newState)
        {
            ApplyBackgroundMusicForIntroState(newState);
            return;
        }
        
        IntroState oldState = currentState;
        currentState = newState;

        ApplyBackgroundMusicForIntroState(newState);
        
        if (logStateChanges)
        {
            Debug.Log($"[IntroController_V1] State: {oldState} → {newState}");
        }
    }
    
    /// <summary>
    /// Returns the current intro state (for external queries).
    /// </summary>
    public IntroState GetCurrentState()
    {
        return currentState;
    }
    
    /// <summary>
    /// Returns true if the intro sequence has completed and menu is active.
    /// </summary>
    public bool IsSequenceComplete()
    {
        return currentState == IntroState.Idle;
    }
    
    // =============================================================================
    // EDITOR HELPERS
    // =============================================================================
    
#if UNITY_EDITOR
    /// <summary>
    /// Editor-only: Skip to the end state for testing menu without waiting for sequence.
    /// </summary>
    [ContextMenu("Skip To Menu (Editor Only)")]
    public void EditorSkipToMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[IntroController_V1] Must be in play mode to skip.");
            return;
        }
        
        // Stop any running sequence
        if (activeSequence != null)
        {
            StopCoroutine(activeSequence);
            activeSequence = null;
        }
        
        // Move camera to entered position
        if (introCamera != null)
        {
            UpgradeLegacyCameraPathsIfNeeded();

            Transform finalEnteredAnchor = GetLastValidAnchor(enteredPath);
            if (finalEnteredAnchor != null)
            {
                introCamera.transform.position = finalEnteredAnchor.position;
                introCamera.transform.rotation = finalEnteredAnchor.rotation;
            }
        }
        
        // Open door
        if (doorToOpen != null)
        {
            Vector3 closedEuler = doorClosedRotation.eulerAngles;
            doorToOpen.localEulerAngles = new Vector3(closedEuler.x, closedEuler.y + doorOpenAngle, closedEuler.z);
        }

        if (secondDoorToOpen != null)
        {
            Vector3 closedEuler = secondDoorClosedRotation.eulerAngles;
            secondDoorToOpen.localEulerAngles = new Vector3(closedEuler.x, closedEuler.y + secondDoorOpenAngle, closedEuler.z);
        }
        
        // Spawn chair if not already
        if (spawnedChair == null && chairPrefab != null)
        {
            SpawnChair();
        }
        
        // Reveal menu
        sequenceStarted = true;
        RevealMainMenu();
        SetState(IntroState.Idle);
        
        Debug.Log("[IntroController_V1] Skipped to menu state");
    }

    private Transform GetLastValidAnchor(List<CameraPathPoint> pathPoints)
    {
        if (pathPoints == null)
        {
            return null;
        }

        for (int i = pathPoints.Count - 1; i >= 0; i--)
        {
            CameraPathPoint point = pathPoints[i];
            if (point != null && point.target != null)
            {
                return point.target;
            }
        }

        return null;
    }
#endif
}*/
