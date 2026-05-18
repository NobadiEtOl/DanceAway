using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Common.Enums;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


namespace Common.Enums
{
    public enum BeatState
    {
        PerfectBeat,
        CloseBeat,
        MiddleBeat,
        FarBeat,
        OffBeat
    }

    public enum Difficulty
    {
        Easy,
        Normal,
        Hard
    }

    public enum MovementMode
    {
        Swipe,
        ArrowKeys,
        JoystickBeat
    }
}


public class GameController : MonoBehaviour
{
    
    [HideInInspector]public BeatState State;
    [SerializeField]public int levelNo=1;
    [SerializeField]private float spotlightSpawnChance = 0.4f;
    [SerializeField]private float heartSpawnChance = 0.05f;    
    [SerializeField]private CrowdController crowdController;
    [SerializeField]public int width;
    [SerializeField]public int height;
    [SerializeField]public static List<int> gridBounds = new List<int>();// width lower(0)/upper(1), height lower(2)/upper(3) 
    public float tileSize = 8.0f;
    [SerializeField]public static AudioSource[] audioSources;  
    public static BeatTimer beatTimer;
    private GameState currentState;
    public static GameObject[,] grid;
    [SerializeField]private GameObject spotLightPrefab;
    [SerializeField]private GameObject heartPrefab;
    [SerializeField]private GameObject endScreen;
    public Player player;
    [HideInInspector]public List<Triangle> enemies = new List<Triangle>();
    [HideInInspector]public List<SpotlightSquare> spotlights = new List<SpotlightSquare>();
    private List<GameObject> hearts = new List<GameObject>();
    public int totalTrianglesToSpawn;
    public int trianglesSpawned;
    private int beatCounter = 0;
    public bool isSpawningEnemies = false; // Flag to track enemy spawning
    [Header("Enemy Movement")]
    [SerializeField] private bool freezeEnemies = false;
    private LevelManager levelManager;
    private EnemySpawner enemySpawner;
    private GridController gridController;
    public bool canStart=false;
    /// <summary>Set true by default so all gameplay logic is blocked until the intro sequence finishes.</summary>
    public bool introRunning = true;
    [SerializeField]private GameObject startScreen;
    [SerializeField]private GameObject settingScreen;
    [SerializeField]private GameObject settingsButtonsContainer;
    [SerializeField]private GameObject healthBar;
    [SerializeField]private GameObject scoreObj;
    [SerializeField]private Text endScore;
    [SerializeField]private Text endLevelText; 
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private AudioMixer audioMixer;
    
    // Difficulty System
    [SerializeField] private float easyTolerance   = 12f;
    [SerializeField] private float normalTolerance  = 20f;
    [SerializeField] private float hardTolerance    = 35f;
    [SerializeField] private UnityEngine.UI.Button easyButton;
    [SerializeField] private UnityEngine.UI.Button normalButton;
    [SerializeField] private UnityEngine.UI.Button hardButton;
    [SerializeField] private UnityEngine.UI.Button retryButton;
    private Difficulty currentDifficulty = Difficulty.Easy;
    private bool difficultyLocked = false;

    // Difficulty Unlock Progression
    [SerializeField] private int normalUnlockLevel = 30; // Easy level required to unlock Normal
    [SerializeField] private int hardUnlockLevel   = 30; // Normal level required to unlock Hard
    [SerializeField] private GameObject normalLockIcon; // "Lock" child of the Normal button
    [SerializeField] private GameObject hardLockIcon;   // "Lock" child of the Hard button
    private bool normalUnlocked = false;
    private bool hardUnlocked   = false;

    // Movement Mode
    [Header("Movement Mode")]
    [SerializeField] private MovementMode currentMovementMode;
    [SerializeField] private ControlPlacementController controlPlacement;

    [SerializeField] private int performanceThresholdHand1 = 75;      // Below this, hand beat 1 removed
    [SerializeField] private int performanceThresholdHand2 = 130;     // Below this, hand beat 2 removed
    [SerializeField] private int performanceThresholdDrums = 50;      // Below this, drum layers removed
    private bool[] activeAudioLayers = new bool[6];                   // Track which layers are currently playing
    private bool[] skipNextSchedule = new bool[6];                    // Set by LevelManager crossfade to prevent PlayHandScheduled from restarting the new layer
    private int lastRecordedPerformance = 0;                           // Track last known performance level
    private bool dynamicMusicActive = false;                          // Only apply dynamic adjustments after game starts

    public void StartHandleBeatCor()
    {
        canStart = true;
        player.score = 0;
        scoreObj.SetActive(true);
        player.PlaceScore();
        healthBar.SetActive(true);
        startScreen.SetActive(false);
        if(!beatTimer.begin)beatTimer.begin=true;
        player.EnableBeatStateText();
        InitializeDynamicMusic(); // Reset layer tracking
        // Pre-seed avarage so the first scored cycle starts at max (200)
        avarage = 200 * 16;
        lockAvarageAtMax = false;  // Allow avarage to change from now on
        dynamicMusicActive = true; // Dynamic music now responds to performance
        difficultyLocked = true;   // Lock difficulty for the rest of this session
        if (controlPlacement != null) controlPlacement.ShowMode(currentMovementMode); // Show controls now that gameplay begins
        StartGame();               // Spawn first wave now, using the difficulty the player chose
        RefreshCameraForGameplay();
    }

    public void BeatTimerBegin()
    {
        beatTimer.begin=true;
    }
    void Start()
    {
        // Ensure the game is unpaused when the scene loads (e.g. after Retry from settings)
        Time.timeScale = 1;

        // Initializing the arena grid and other components
        InitializeComponents();

        //Initializng the player before the start of the game. 
        player.StartPlayer();

        //Subscribing to beat events
        beatTimer.OnBeat += HandleBeat;
        beatTimer.OffBeat += HandleOffBeat;

        // Sync slider with current mixer value so UI reflects real volume on scene load.
        SyncVolumeSliderWithMixer();

        //Initializin onValueChanged for the volume slider
        volumeSlider.onValueChanged.AddListener(AdjustVolume);

        CenterCamera();

        // Setting initial audio pitches
        for(int i = 0; i < 6; i++)
        {
            audioSources[i].Stop();
            audioSources[i].pitch = 0.8333f;
        }

        settingScreen.SetActive(false);
        endScreen.SetActive(false);
        scoreObj.SetActive(false);

        crowdController.GetCrowdParents();
        crowdController.MaxNodders(); // Match max-average lock: all layers playing, all crowd nodding

        // Load and apply saved difficulty (default: Easy on first launch)
        Difficulty saved = (Difficulty)PlayerPrefs.GetInt("Difficulty", (int)Difficulty.Easy);

        // Load unlock flags before applying difficulty so the safety fallback works
        normalUnlocked = PlayerPrefs.GetInt("NormalUnlocked", 0) == 1;
        hardUnlocked   = PlayerPrefs.GetInt("HardUnlocked",   0) == 1;

        // Safety fallback: don't start on a mode that isn't unlocked
        if (saved == Difficulty.Hard   && !hardUnlocked)   saved = Difficulty.Easy;
        if (saved == Difficulty.Normal && !normalUnlocked) saved = Difficulty.Easy;

        ApplyDifficulty(saved);
        UpdateLockIcons();

        currentMovementMode = (MovementMode)PlayerPrefs.GetInt("MovementMode", (int)MovementMode.ArrowKeys);

        // Guarded: IntroSequenceController will call these after the intro walk finishes.
        if (!introRunning)
        {
            gridController.ResetGridBounds();
            crowdController.ResizeCrowd();
            // StartGame() is deferred until the player presses Start (StartHandleBeatCor).
        }
    }

    private void InitializeComponents()
    {
        grid = new GameObject[width, height];
        beatTimer = GetComponent<BeatTimer>();
        audioSources = GetComponents<AudioSource>();
        levelManager = GetComponent<LevelManager>();
        levelManager.Initialize();
        enemySpawner = GetComponent<EnemySpawner>();
        enemySpawner.Initialize();  
        gridController = GetComponent<GridController>();
        gridController.Initialize();
        crowdController.Initialize(beatTimer,width,height,tileSize);
        gridController.InitializeGrid();
    }
    public bool canSpawn = true;
    private bool perfectBeatTileColorsActive = false;
    private readonly Color perfectBeatTileColor = Color.HSVToRGB(0.33f, 0.65f, 1f);

    void HandleBeat()// Handles all the checks happening once per beat
    {

        beatCounter++;

        // During intro: allow tile color-switching so the arena looks alive, but block all gameplay.
        if (introRunning) { SwitchColor(); return; }

        if(canStart)StartCoroutine(HandleBeatCoroutine());
        
        foreach (var spotlight in spotlights)
        {
            if(beatCounter%2==0)spotlight.Move();// Spotlights move once per 2 beats since their speed is halved
        }

        if (isSpawningEnemies && canSpawn)
        {
            enemySpawner.SpawnRemainingEnemies();
        }

        if(canStart && enemies.Count == 0 && !isSpawningEnemies)
        {
            levelManager.LoadLevel();// Load level if only there are no enemies present and none will be spawned
            gridController.ResetGridBounds();
            crowdController.ResizeCrowd();
        }
        //Switches the colors of the tiles each beat
        SwitchColor();

        // Flash the upcoming boundary tiles so the player can anticipate the crowd closing in
        if (gridBoundsFlag && enemiesKilled >= 5 && enemies.Count > 1)
        {
            gridController.FlashBoundaryTiles();
        }
    }

    public int enemiesKilled = 0;
    IEnumerator HandleBeatCoroutine()
    {
        if (freezeEnemies)
        {
            yield break;
        }

        List<Triangle> trianglesToRemove = new List<Triangle>();

        // Iterate over a copy of the list to avoid modifying the collection during iteration
        var enemiesCopy = new List<Triangle>(enemies);

        foreach (var enemy in enemiesCopy)
        {
            if (enemy != null)
            {
                enemy.Move(); // Make enemies move with the beat
            }
            yield return null;
        }

    }

    // Runs at OffBeat (after the scoring window closes, before the next beat)
    void HandleOffBeat()
    {
        // Grid contraction fires here so it never overlaps with the player's scoring window
        if (gridBoundsFlag && enemiesKilled >= 5 && enemies.Count > 1)
        {
            gridController.ChangeGridBounds();
            crowdController.ResizeCrowd();
            gridBoundsFlag = false;
        }
    }


    // Updates the triangle that is chaing the player according to the powe level of the triangle
    public void UpdateChasingTriangle()
    {
        Triangle highestPowerTriangle = null;

        foreach (var triangle in enemies)
        {
            if (highestPowerTriangle == null || triangle.powerLevel > highestPowerTriangle.powerLevel)
            {
                if (highestPowerTriangle != null)
                    highestPowerTriangle.isChasingPlayer = false; // Stop the previous chaser

                highestPowerTriangle = triangle;
            }
        }

        if (highestPowerTriangle != null)
            highestPowerTriangle.isChasingPlayer = true;
    }

    public int avarage=0;// To keep track of how good the player is doing
    /// <summary>When true, avarage is locked at max (200) so all music layers play — active from Click to Begin until Start is pressed.</summary>
    public bool lockAvarageAtMax = true;
    
    /// <summary>Called by LevelManager when a crossfade schedules a layer, so PlayHandScheduled skips one restart cycle for that layer.</summary>
    public void SkipNextSchedule(int index)
    {
        if (index >= 0 && index < skipNextSchedule.Length)
            skipNextSchedule[index] = true;
    }

    /// <summary>Initializes the dynamic music system by playing all audio layers at full power.</summary>
    private void InitializeDynamicMusic()
    {
        // Start with all layers active
        for (int i = 0; i < 6; i++)
        {
            activeAudioLayers[i] = true;
        }
        lastRecordedPerformance = 100; // Start at max performance assumption
        dynamicMusicActive = false;     // Don't calculate adjustments yet—wait until game truly starts
    }

    public void PlayHandScheduled(double dspTime)
    {   
        avarage = avarage / 16;

        // Keep avarage at max while locked (pre-game practice)
        if (lockAvarageAtMax) avarage = 200;
        
        // Track performance, but only apply dynamic adjustments after game starts
        if (dynamicMusicActive)
        {
            lastRecordedPerformance = avarage;
        }
    
        // Always play back beat (foundation layer)
        audioSources[0].volume = 0.6f;
        audioSources[0].PlayScheduled(dspTime);
        
        // Hand beat 1: Active by default, removed if performance drops (only if dynamic music is active)
        if (!dynamicMusicActive || avarage >= performanceThresholdHand1)
        {
            if (!activeAudioLayers[1])
            {
                activeAudioLayers[1] = true;
                crowdController.MoreNodders(20);
            }
            audioSources[1].volume = 0.3f;
            audioSources[1].PlayScheduled(dspTime);
        }
        else
        {
            if (activeAudioLayers[1])
            {
                activeAudioLayers[1] = false; // Layer removed due to poor performance
            }
            audioSources[1].Stop();
        }
        
        // Hand beat 2: Active by default, removed if performance drops significantly (only if dynamic music is active)
        if (!dynamicMusicActive || avarage >= performanceThresholdHand2)
        {
            if (!activeAudioLayers[2])
            {
                activeAudioLayers[2] = true;
                crowdController.MoreNodders(50);
            }
            audioSources[2].volume = 0.5f;
            audioSources[2].PlayScheduled(dspTime);
        }
        else
        {
            if (activeAudioLayers[2])
            {
                activeAudioLayers[2] = false; // Layer removed due to poor performance
            }
            audioSources[2].Stop();
        }
        
        // Level-based drum layers: Start at full power, remove if performance is terrible (only if dynamic music is active)
        if (levelNo >= 30)
        {
            if (!dynamicMusicActive || avarage >= performanceThresholdDrums || avarage == 0)
            {
                if (!activeAudioLayers[5])
                    activeAudioLayers[5] = true;
                if (!skipNextSchedule[5])
                {
                    audioSources[5].volume = 0.5f;
                    audioSources[5].PlayScheduled(dspTime);
                }
                skipNextSchedule[5] = false;
            }
            else
            {
                if (activeAudioLayers[5])
                    activeAudioLayers[5] = false;
                audioSources[5].Stop();
            }
        }
        else if (levelNo >= 20)
        {
            if (!dynamicMusicActive || avarage >= performanceThresholdDrums || avarage == 0)
            {
                if (!activeAudioLayers[4])
                    activeAudioLayers[4] = true;
                if (!skipNextSchedule[4])
                {
                    audioSources[4].volume = 0.4f;
                    audioSources[4].PlayScheduled(dspTime);
                }
                skipNextSchedule[4] = false;
            }
            else
            {
                if (activeAudioLayers[4])
                    activeAudioLayers[4] = false;
                audioSources[4].Stop();
            }
        }
        else if (levelNo >= 10)
        {
            if (!dynamicMusicActive || avarage >= performanceThresholdDrums || avarage == 0)
            {
                if (!activeAudioLayers[3])
                    activeAudioLayers[3] = true;
                if (!skipNextSchedule[3])
                {
                    audioSources[3].volume = 0.5f;
                    audioSources[3].PlayScheduled(dspTime);
                }
                skipNextSchedule[3] = false;
            }
            else
            {
                if (activeAudioLayers[3])
                    activeAudioLayers[3] = false;
                audioSources[3].Stop();
            }
        }
        
        avarage = 0;
    }

    public void PlayBackScheduled(double dspTime)
    {
        audioSources[0].volume = 0.6f;
        audioSources[0].PlayScheduled(dspTime);
    }

    /// <summary>Gets the number of active audio layers currently playing.</summary>
    public int GetActiveAudioLayerCount()
    {
        int count = 0;
        for (int i = 0; i < activeAudioLayers.Length; i++)
        {
            if (activeAudioLayers[i]) count++;
        }
        return count;
    }

    /// <summary>Gets the current performance/average score.</summary>
    public int GetCurrentPerformance()
    {
        return lastRecordedPerformance;
    }

    /// <summary>Returns the enemy count multiplier for the current difficulty (Easy=1, Normal=2, Hard=4).</summary>
    public int GetDifficultyMultiplier()
    {
        return currentDifficulty == Difficulty.Hard   ? 4
             : currentDifficulty == Difficulty.Normal ? 2
             : 1;
    }

    public void LessNodders(int no)
    {
        crowdController.LessNodders(no);
    }
    public void MoreNodders(int no)
    {
        crowdController.MoreNodders(no);
    }


    void SwitchColor()// To chage the arena grid's color randomly each beat
    {
        for(int x = 0; x<width; x++)
        {
            for (int y = 0; y<height; y++)
            {
                grid[x,y].GetComponent<SpriteRenderer>().color =  GetRandomColor();
            }
        }
    }

    private void SetAllTileColors(Color color)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y].GetComponent<SpriteRenderer>().color = color;
            }
        }
    }

    private void UpdatePerfectBeatTileColors()
    {
        if (beatTimer == null) return;

        bool isPerfectBeat = beatTimer.state == BeatState.PerfectBeat;

        if (isPerfectBeat)
        {
            if (!perfectBeatTileColorsActive)
            {
                SetAllTileColors(perfectBeatTileColor);
                perfectBeatTileColorsActive = true;
            }
            return;
        }

        if (perfectBeatTileColorsActive)
        {
            perfectBeatTileColorsActive = false;
            SwitchColor();
        }
    }
    
    private Color GetRandomColor()
    {
        // Define a list of predetermined colors
        Color[] colors = new Color[]
        {
            Color.HSVToRGB(0f, 0.65f, 1f),         // Red
            Color.HSVToRGB(0.67f, 0.65f, 1f),      // Blue
            Color.HSVToRGB(0.33f, 0.65f, 1f),      // Green
            Color.HSVToRGB(0.17f, 0.65f, 1f),      // Yellow
            Color.HSVToRGB(0.5f, 0.65f, 1f),       // Cyan
            Color.HSVToRGB(0.83f, 0.65f, 1f),      // Magenta
            Color.HSVToRGB(0.08f, 0.65f, 1f),      // Orange
            Color.HSVToRGB(0.75f, 0.65f, 1f)       // Purple
        };

        // Choose a random index from the color array
        int randomIndex = UnityEngine.Random.Range(0, colors.Length);

        return colors[randomIndex];
    }

    public void MergeTriangles(List<Triangle> mergeCandidates)
    {
        if(mergeCandidates[0].powerLevel == mergeCandidates[1].powerLevel)
        {
            int numberOfTriangles = mergeCandidates.Count;
            Triangle baseTriangle = mergeCandidates[0];

            int healthSum=0;//how much health triangle lost
            foreach (var triangle in mergeCandidates)
            {
                healthSum+=(triangle.baseHealth*triangle.powerLevel)-triangle.health;
                if (triangle != baseTriangle)
                {
                    RemoveEnemy(triangle);
                    UpdateChasingTriangle();
                    //var temp = triangle.powerLevel;// does not work correctly if this comment is not here
                    Destroy(triangle.gameObject);
                }
            }

            baseTriangle.MergeTriangles(numberOfTriangles,healthSum);
        }
    }

    public void MergeSpotlights(List<SpotlightSquare> mergeCandidates)
    {
        if(mergeCandidates[0].powerLevel == mergeCandidates[1].powerLevel)
        {
            int numberOfSpotlights = mergeCandidates.Count;
            SpotlightSquare baseSpotlight = mergeCandidates[0];

            foreach (var spotlight in mergeCandidates)
            {
                if (spotlight != baseSpotlight)
                {
                    RemoveSpotlight(spotlight);
                    Destroy(spotlight.gameObject);
                }
            }

            baseSpotlight.MergeSpotlights(numberOfSpotlights);
        }
    }

    private float screenAspect;
    private float arenaWidth;
    [SerializeField] private float visibleCrowdTiles = 0f;
    /// <summary>Fraction of orthographic half-height to shift the camera downward once gameplay begins.
    /// 0 = centred on the arena. 0.2 = shift down 20% of the ortho size, giving more space at the bottom
    /// for player input. Device-independent.</summary>
    [SerializeField] public float gameplayCameraVerticalOffsetFraction = 0f;

    /// <summary>Calculates the orthographic size needed to frame the active arena bounds
    /// plus <see cref="visibleCrowdTiles"/> rows of crowd on each side. Device-independent.
    /// screenAspect must already be set (it is, from CenterCamera which runs in Start).</summary>
    public float CalculateOrthoSizeForBounds(int minX, int maxX)
    {
        return ((maxX - minX) / 2f + visibleCrowdTiles) * tileSize / screenAspect;
    }

    void CenterCamera()
    {
        // Initial camera placement at Start() — grid bounds are not set yet so just
        // center neutrally and capture screenAspect. RefreshCameraForGameplay() applies
        // the gameplay offset and correct ortho size once start is pressed.
        float centerX = (width * tileSize - tileSize) / 2.0f;
        float centerY = (height * tileSize - tileSize) / 2.0f;
        screenAspect = (float)Screen.width / (float)Screen.height;
        arenaWidth = width * height;
        Camera.main.transform.position = new Vector3(centerX, centerY, Camera.main.transform.position.z);
    }

    /// <summary>Called when the player presses Start. Recalculates screenAspect,
    /// derives the correct ortho size from live gridBounds, snaps the camera
    /// to the gameplay-offset position, and lerps to the target size.</summary>
    public void RefreshCameraForGameplay()
    {
        screenAspect = (float)Screen.width / (float)Screen.height;
        float targetOrtho = CalculateOrthoSizeForBounds(gridBounds[0], gridBounds[1]);
        float centerX = (width * tileSize - tileSize) / 2.0f;
        float centerY = (height * tileSize - tileSize) / 2.0f;
        float downwardShift = targetOrtho * gameplayCameraVerticalOffsetFraction;
        Camera.main.transform.position = new Vector3(centerX, centerY - downwardShift, Camera.main.transform.position.z);
        StartCoroutine(ChangeCameraOrthoSize(targetOrtho));
    }




    public float cameraTransitionDuration = 3f;
    public IEnumerator ChangeCameraOrthoSize(float f)
    {
        float timePassed = 0;
        float initialSize = Camera.main.orthographicSize;
        float targetSize = f;

        while (timePassed < cameraTransitionDuration)
        {
            Camera.main.orthographicSize = Mathf.Lerp(initialSize,targetSize,timePassed/cameraTransitionDuration);
            timePassed += Time.deltaTime;
            yield return null;
        }

        Camera.main.orthographicSize = targetSize;
    }
    public IEnumerator ChangeCameraOrthoSize(int i)
    {
        float timePassed = 0;
        float initialSize = Camera.main.orthographicSize;
        float targetSize = i;

        while (timePassed < cameraTransitionDuration)
        {
            Camera.main.orthographicSize = Mathf.Lerp(initialSize,targetSize,timePassed/cameraTransitionDuration);
            timePassed += Time.deltaTime;
            yield return null;
        }

        //Camera.main.orthographicSize = targetSize;
    }

    public void StartGame()
    {
        ChangeState(GameState.Play);
        //beatTimer.StartAfterDelay();
        int difficultyMultiplier = currentDifficulty == Difficulty.Hard   ? 4
                                 : currentDifficulty == Difficulty.Normal ? 2
                                 : 1;
        totalTrianglesToSpawn = levelNo * difficultyMultiplier;
        trianglesSpawned = 0;
        isSpawningEnemies = true;
        dynamicMusicActive = true; // NOW start calculating dynamic music adjustments
        enemySpawner.SpawnRemainingEnemies(); // Ensure enemies are spawned when the game starts
    }



    void ChangeState(GameState newState)
    {
        currentState = newState;
        Debug.Log("Game State Changed to: " + newState);
    }

    void PauseGame()
    {
        ChangeState(GameState.Pause);
        Time.timeScale = 0;
    }

    void ResumeGame()
    {
        ChangeState(GameState.Play);
        Time.timeScale = 1;
    }

    void EndGame()
    {
        ChangeState(GameState.End);
    }

    [SerializeField]private bool resizeFlag=false;
    [SerializeField]public bool gridBoundsFlag=false;
    private bool tutorialWindowOpen = false;
    private bool backPressedOnce = false;
    private float backPressTime = 0f;
    private const float backPressWindow = 3.5f; // Matches Android LENGTH_LONG toast duration

    void Update()
    {
        UpdatePerfectBeatTileColors();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleBackButton();
        }
    }

    private void HandleBackButton()
    {
        // If settings screen is open, close it
        /*if (settingScreen != null && settingScreen.activeSelf)
        {
            CloseSettingScreen();
            return;
        }

        // If gameplay is running, open settings
        if (canStart && currentState == GameState.Play)
        {
            OpenSettingScreen();
            return;
        }*/

        // Double-press to minimize app to background
        if (backPressedOnce && Time.unscaledTime - backPressTime < backPressWindow)
        {
            MinimizeApp();
            return;
        }

        backPressedOnce = true;
        backPressTime = Time.unscaledTime;
        ShowAndroidToast("Oyundan çıkmak için bir daha bas");
        StartCoroutine(ResetBackPress());
    }

    private IEnumerator ResetBackPress()
    {
        yield return new WaitForSecondsRealtime(backPressWindow);
        backPressedOnce = false;
    }

    private void ShowAndroidToast(string message)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        AndroidJavaClass toastClass = new AndroidJavaClass("android.widget.Toast");
        AndroidJavaObject toast = toastClass.CallStatic<AndroidJavaObject>("makeText", currentActivity, message, toastClass.GetStatic<int>("LENGTH_LONG"));
        toast.Call("show");
#endif
    }

    private void MinimizeApp()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        currentActivity.Call<bool>("moveTaskToBack", true);
#endif
    }

    private void SpawnSpotlight_Heart(Vector2Int position, int powerLevel)
    {
        // Generate a random value between 0 and 1
        float randomValue = UnityEngine.Random.Range(0f, 1f);

        // Check if the random value is less than the spawn chance
        if (randomValue <= spotlightSpawnChance)
        {
            GameObject spotlightObject = Instantiate(spotLightPrefab, new Vector2(position.x * tileSize, position.y * tileSize), Quaternion.identity);
            SpotlightSquare spotlight = spotlightObject.GetComponent<SpotlightSquare>();
            spotlight.Initialize(position, this, beatTimer, powerLevel);
            addSpotlight(spotlight);
        }

        if (randomValue >= 1-heartSpawnChance)
        {
            GameObject heartObject = Instantiate(heartPrefab, new Vector2(position.x * tileSize, position.y * tileSize), Quaternion.identity);
            hearts.Add(heartObject);
        }
    }

    // Function to handle collecting the heart
    public void CollectHeart(GameObject heart)
    {
        player.TakeHeart(10);
        // Remove the heart from the list and destroy the heart object
        hearts.Remove(heart);
        Destroy(heart);
    }


    public void addSpotlight(SpotlightSquare spotlight)
    {
        spotlights.Add(spotlight);
    }



    public void RemoveEnemy(Triangle enemy)
    {
        if (enemies.Contains(enemy))
        {
            SpawnSpotlight_Heart(enemy.position, enemy.powerLevel);
            enemies.Remove(enemy);
            UpdateChasingTriangle();
        }
    }

    public void RemoveSpotlight(SpotlightSquare spotlight)
    {
        if (spotlights.Contains(spotlight))
        {
            spotlights.Remove(spotlight);
        }
    }

    public void Retry()
    {
        SceneManager.LoadScene("Game");
    }

    
    public void OpenEndScreen()
    {
        SaveRunProgress();
        endScore.text = player.score.ToString();
        endLevelText.text = "Level " + levelNo.ToString();
        endScreen.SetActive(true);
    }

    private void SaveRunProgress()
    {
        string diffKey = currentDifficulty.ToString();

        // Persist max level reached on this difficulty
        int savedMaxLevel = PlayerPrefs.GetInt("MaxLevel_" + diffKey, 0);
        if (levelNo > savedMaxLevel)
            PlayerPrefs.SetInt("MaxLevel_" + diffKey, levelNo);

        // Persist max score reached on this difficulty
        int savedMaxScore = PlayerPrefs.GetInt("MaxScore_" + diffKey, 0);
        if (player.score > savedMaxScore)
            PlayerPrefs.SetInt("MaxScore_" + diffKey, player.score);

        // Check unlock conditions
        if (!normalUnlocked && currentDifficulty == Difficulty.Easy && levelNo >= normalUnlockLevel)
        {
            normalUnlocked = true;
            PlayerPrefs.SetInt("NormalUnlocked", 1);
        }

        if (!hardUnlocked && currentDifficulty == Difficulty.Normal && levelNo >= hardUnlockLevel)
        {
            hardUnlocked = true;
            PlayerPrefs.SetInt("HardUnlocked", 1);
        }

        PlayerPrefs.Save();
        UpdateLockIcons();
        UpdateDifficultyButtonVisuals();
    }

    public List<int> ReturnGridbounds()
    {
        List<int> tempBounds = new List<int>();
        for(int i=0; i<4; i++)
        {
            tempBounds.Add(gridBounds[i]);
        }
        return tempBounds;
    }

    public void  OpenSettingScreen()
    {
        settingScreen.SetActive(true);
        SetSettingsButtonsVisible(false);
        if (retryButton != null) retryButton.gameObject.SetActive(canStart);
        if (canStart)
        {
            Time.timeScale = 0;
            beatTimer.PauseTrack();
        }
        else if (startScreen != null)
        {
            startScreen.SetActive(false);
        }
    }

    public void CloseSettingScreen()
    {
        settingScreen.SetActive(false);
        if (!tutorialWindowOpen)
        {
            SetSettingsButtonsVisible(true);
        }
        if (canStart)
        {
            Time.timeScale = 1;
            beatTimer.ResumeTrack();
        }
        else if (startScreen != null)
        {
            startScreen.SetActive(true);
        }
    }

    public void OnTutorialWindowOpened()
    {
        tutorialWindowOpen = true;
        SetSettingsButtonsVisible(false);
    }

    public void OnTutorialWindowClosed()
    {
        tutorialWindowOpen = false;
        if (settingScreen == null || !settingScreen.activeSelf)
        {
            SetSettingsButtonsVisible(true);
        }
    }

    private void SetSettingsButtonsVisible(bool isVisible)
    {
        if (settingsButtonsContainer != null)
        {
            settingsButtonsContainer.SetActive(isVisible);
        }
    }
    /// <summary>
    /// Maps a slider value [0,1] to decibels:
    /// 0.0 → -80 dB (silence), 0.5 → 0 dB (default), 1.0 → +6 dB (2× amplitude).
    /// </summary>
    private float SliderToDb(float sliderValue)
    {
        if (sliderValue <= 0.5f)
        {
            // Lower half: logarithmic fade from silence to unity
            float normalized = sliderValue / 0.5f;
            return Mathf.Log10(Mathf.Clamp(normalized, 0.0001f, 1f)) * 20f;
        }
        else
        {
            // Upper half: linear boost from 0 dB to +6 dB (2× amplitude)
            float t = (sliderValue - 0.5f) / 0.5f;
            return t * 6.02f;
        }
    }

    private void AdjustVolume(float volume)
    {
        audioMixer.SetFloat("Volume", SliderToDb(volume));

        // Persist the player's choice so it is restored on next game start.
        PlayerPrefs.SetFloat("Volume", volume);
        PlayerPrefs.Save();
    }

    private void SyncVolumeSliderWithMixer()
    {
        if (volumeSlider == null || audioMixer == null) return;

        // Default is 0.5 so the slider rests at unity gain (0 dB).
        float savedVolume = PlayerPrefs.GetFloat("Volume", 0.5f);
        savedVolume = Mathf.Clamp01(savedVolume);

        // Apply to mixer and update slider without firing the onValueChanged callback.
        audioMixer.SetFloat("Volume", SliderToDb(savedVolume));
        volumeSlider.SetValueWithoutNotify(savedVolume);
    }

    public float SendBeatInterval()
    {
        return beatTimer.beatInterval;
    }

    // --- Difficulty ---

    public void SetDifficultyEasy()   { if (!difficultyLocked) ApplyDifficulty(Difficulty.Easy); }
    public void SetDifficultyNormal() { if (!difficultyLocked && normalUnlocked) ApplyDifficulty(Difficulty.Normal); }
    public void SetDifficultyHard()   { if (!difficultyLocked && hardUnlocked)   ApplyDifficulty(Difficulty.Hard); }

    private void ApplyDifficulty(Difficulty d)
    {
        currentDifficulty = d;
        float divisor = d == Difficulty.Easy ? easyTolerance
                      : d == Difficulty.Hard  ? hardTolerance
                      : normalTolerance;
        beatTimer.SetDifficulty(divisor);
        PlayerPrefs.SetInt("Difficulty", (int)d);
        PlayerPrefs.Save();
        UpdateDifficultyButtonVisuals();
    }

    private void UpdateDifficultyButtonVisuals()
    {
        if (easyButton   != null) easyButton.interactable   = currentDifficulty != Difficulty.Easy;
        if (normalButton != null) normalButton.interactable = currentDifficulty != Difficulty.Normal;
        if (hardButton   != null) hardButton.interactable   = currentDifficulty != Difficulty.Hard;
        UpdateLockIcons();
    }

    private void UpdateLockIcons()
    {
        if (normalLockIcon != null) normalLockIcon.SetActive(!normalUnlocked);
        if (hardLockIcon   != null) hardLockIcon.SetActive(!hardUnlocked);
    }

    // -------------------------------------------------------------------------
    // Movement Mode
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by IntroSequenceController once the loading/intro sequence finishes.
    /// Shows the arrow keys UI if ArrowKeys mode is active, and ensures SwipeController
    /// state matches the current mode.
    /// </summary>
    public void OnIntroComplete()
    {
        // Controls stay hidden until an info window previews them or gameplay starts.
        if (controlPlacement != null) controlPlacement.HideControls();
    }

    /// <summary>
    /// Switch the active movement mode at runtime. Safe to call from the settings UI.
    /// Persists the choice to PlayerPrefs so it survives scene reloads.
    /// </summary>
    public void SetMovementMode(MovementMode mode)
    {
        currentMovementMode = mode;
        PlayerPrefs.SetInt("MovementMode", (int)mode);
        PlayerPrefs.Save();
        // Only apply visually after the intro has finished.
        if (!introRunning)
            ApplyMovementMode(mode);
    }

    /// <summary>Returns the currently active movement mode.</summary>
    public MovementMode GetMovementMode() => currentMovementMode;

    /// <summary>
    /// Called by Unity whenever a serialized field changes in the Inspector (edit mode and play mode).
    /// Ensures the active input controller always matches currentMovementMode.
    /// </summary>
    private void OnValidate()
    {
        ApplyMovementMode(currentMovementMode);
    }

    /// <summary>
    /// Applies the visual and functional state for the given mode:
    ///   Swipe        → SwipeController enabled, arrow keys UI hidden, joystick disabled.
    ///   ArrowKeys    → SwipeController disabled, arrow keys UI shown, joystick disabled.
    ///   JoystickBeat → SwipeController disabled, arrow keys UI hidden, joystick enabled.
    /// WASD/keyboard input (HandleInput) remains available in all modes.
    /// </summary>
    private void ApplyMovementMode(MovementMode mode)
    {
        // Each mode's controller lives on its prefab — instantiation IS enabling.
        // Destruction (on mode switch) IS disabling. No explicit enable/disable needed.
        // Guard: never show controls while the intro is still running or before the game starts.
        if (Application.isPlaying && controlPlacement != null && !introRunning && canStart)
            controlPlacement.ShowMode(mode);
    }

    // -------------------------------------------------------------------------
    // Arrow input handlers (public so ArrowKeysController can wire them as delegates)
    // -------------------------------------------------------------------------

    public void OnArrowUp()
    {
        if (!canStart || introRunning) return;
        player.SetFacingDirection(Vector2Int.up);
        player.Move(Vector2Int.up);
    }

    public void OnArrowDown()
    {
        if (!canStart || introRunning) return;
        player.SetFacingDirection(Vector2Int.down);
        player.Move(Vector2Int.down);
    }

    public void OnArrowLeft()
    {
        if (!canStart || introRunning) return;
        player.SetFacingDirection(Vector2Int.left);
        player.Move(Vector2Int.left);
    }

    public void OnArrowRight()
    {
        if (!canStart || introRunning) return;
        player.SetFacingDirection(Vector2Int.right);
        player.Move(Vector2Int.right);
    }

    public void OnArrowUpLeft()
    {
        if (!canStart || introRunning) return;
        Vector2Int dir = new Vector2Int(-1, 1);
        player.SetFacingDirection(dir);
        player.Move(dir);
    }

    public void OnArrowUpRight()
    {
        if (!canStart || introRunning) return;
        Vector2Int dir = new Vector2Int(1, 1);
        player.SetFacingDirection(dir);
        player.Move(dir);
    }

    public void OnArrowDownLeft()
    {
        if (!canStart || introRunning) return;
        Vector2Int dir = new Vector2Int(-1, -1);
        player.SetFacingDirection(dir);
        player.Move(dir);
    }

    public void OnArrowDownRight()
    {
        if (!canStart || introRunning) return;
        Vector2Int dir = new Vector2Int(1, -1);
        player.SetFacingDirection(dir);
        player.Move(dir);
    }



    //keeps making the crowd smaller if it keeps going


    /*void ResizeGrid(int newWidth, int newHeight)
    {
        // Calculate the grid center based on the original width and height
        float centerX = (width * tileSize) / 2.0f;
        float centerY = (height * tileSize) / 2.0f;

        // Calculate the boundaries of the new grid dimensions
        float newCenterX = (newWidth * tileSize) / 2.0f;
        float newCenterY = (newHeight * tileSize) / 2.0f;

        // Iterate through the grid to activate/deactivate tiles
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Calculate the current tile's position relative to the center
                float tileXPos = (x * tileSize) + tileSize / 2.0f;
                float tileYPos = (y * tileSize) + tileSize / 2.0f;

                // Determine if this tile is within the new width and height bounds
                bool isWithinBoundsX = Mathf.Abs(tileXPos - centerX) <= newCenterX;
                bool isWithinBoundsY = Mathf.Abs(tileYPos - centerY) <= newCenterY;

                if (isWithinBoundsX && isWithinBoundsY)
                {
                    // Activate the tile
                    grid[x, y].SetActive(true);
                }
                else
                {
                    // Deactivate the tile
                    grid[x, y].SetActive(false);
                }
            }
        }

        width = newWidth;
        height = newHeight;
    }*/
    /*void HandleMerging()// Maybe change it so that it works with colliders instead?????????
    {
        var mergeGroups = new Dictionary<Vector2Int, List<Triangle>>();

        // Group triangles by their current and previous positions
        foreach (var enemy in enemies)
        {
            if (!mergeGroups.ContainsKey(enemy.position))
            {
                mergeGroups[enemy.position] = new List<Triangle>();
            }
            mergeGroups[enemy.position].Add(enemy);

            if (enemy.previousPosition != enemy.position) // If the triangle moved, consider its previous position too
            {
                if (!mergeGroups.ContainsKey(enemy.previousPosition))
                {
                    mergeGroups[enemy.previousPosition] = new List<Triangle>();
                }
                mergeGroups[enemy.previousPosition].Add(enemy);
            }
        }

        // Merge triangles that have crossed paths or are on the same tile with the same power level and move count greater than 0
        foreach (var group in mergeGroups.Values)
        {
            if (group.Count > 1)
            {
                var mergeCandidates = new List<Triangle>();
                int powerLevel = group[0].powerLevel;

                foreach (var triangle in group)
                {
                    if (triangle.powerLevel == powerLevel && triangle.moveCount > 0)
                    {
                        mergeCandidates.Add(triangle);
                    }
                }

                if (mergeCandidates.Count > 1)
                {
                    MergeTriangles(mergeCandidates);
                }
            }
        }
    }*/

    /*void HandleSpotlightMerging()// Maybe change it so that it works with colliders instead?????????
    {
        var mergeGroups = new Dictionary<Vector2Int, List<SpotlightSquare>>();

        // Group spotlights by their current and previous positions
        foreach (var spotlight in spotlights)
        {
            if (!mergeGroups.ContainsKey(spotlight.position))
            {
                mergeGroups[spotlight.position] = new List<SpotlightSquare>();
            }
            mergeGroups[spotlight.position].Add(spotlight);

            if (spotlight.previousPosition != spotlight.position) // If the spotlight moved, consider its previous position too
            {
                if (!mergeGroups.ContainsKey(spotlight.previousPosition))
                {
                    mergeGroups[spotlight.previousPosition] = new List<SpotlightSquare>();
                }
                mergeGroups[spotlight.previousPosition].Add(spotlight);
            }
        }

        // Merge spotlights that have crossed paths or are on the same tile with the same power level and move count greater than 0
        foreach (var group in mergeGroups.Values)
        {
            if (group.Count > 1)
            {
                var mergeCandidates = new List<SpotlightSquare>();
                int powerLevel = group[0].powerLevel;

                foreach (var spotlight in group)
                {
                    if (spotlight.powerLevel == powerLevel && spotlight.moveCount > 0)
                    {
                        mergeCandidates.Add(spotlight);
                    }
                }

                if (mergeCandidates.Count > 1)
                {
                    MergeSpotlights(mergeCandidates);
                }
            }
        }
    }*/

}
