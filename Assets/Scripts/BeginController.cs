using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BeginController : MonoBehaviour
{
    private AudioClip[] gameAudioClips;
    public string[] audioNames = { "01", "2", "3", "4", "5", "6" }; // Replace with your audio file names
    [SerializeField]private GameController gameController;
    [SerializeField]private GameObject clickToBegin;
    [SerializeField]private float loadTime=15;
    private float beginTimer;
    private bool isClicked;
    [SerializeField]private Text loadingText;
    [SerializeField]private GameObject danceAway;
    [SerializeField]private GameObject startButton;
    [SerializeField]private GameObject tutorialButton;
    [SerializeField]private Image background;
    [SerializeField]private IntroSequenceController introSequenceController;
    private static bool alreadyStarted = false;
    private bool _gameReadyCalled;
    private readonly string[] _loadingStates = { "YÜKLENİYOR", "YÜKLENİYOR.", "YÜKLENİYOR..", "YÜKLENİYOR..." };
    private const float LoadingTextStepDuration = 0.35f;
    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(PreloadAudio());
        gameController = GameObject.Find("GameController").GetComponent<GameController>();
        beginTimer=0;
        isClicked=false;
        danceAway.SetActive(false);
        startButton.SetActive(false);
        tutorialButton.SetActive(false);
        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(true);
            loadingText.text = "DANS ET";
        }
    }

    [SerializeField]private bool readyToPlay=false;
    /// <summary>0–1 progress value driven by the load timer. Used by IntroSequenceController.</summary>
    public float LoadProgress => Mathf.Clamp01(beginTimer / loadTime);
    void Update()
    {
        if (_gameReadyCalled) return;

        beginTimer+= Time.deltaTime;
        if(beginTimer>=loadTime || readyToPlay || alreadyStarted)
        {
            alreadyStarted=true;
            _gameReadyCalled = true;
            GameReady();
        }
        else
        {
            Color color = Color.black;
            color.a = 1-beginTimer/loadTime;
            background.color = color;
            UpdateLoadingText();
        }

    }

    private void UpdateLoadingText()
    {
        if (!isClicked || loadingText == null) return;

        int loadingStateIndex = Mathf.FloorToInt(beginTimer / LoadingTextStepDuration) % _loadingStates.Length;
        loadingText.text = _loadingStates[loadingStateIndex];
    }

    // Coroutine to load audio clips from Resources folder asynchronously
    IEnumerator PreloadAudio()
    {
        gameAudioClips = new AudioClip[audioNames.Length];

        for (int i = 0; i < audioNames.Length; i++)
        {
            ResourceRequest request = Resources.LoadAsync<AudioClip>($"Audio/{audioNames[i]}");
            yield return request;  // Wait for each audio clip to load
            gameAudioClips[i] = request.asset as AudioClip;
            Debug.Log($"Loaded {audioNames[i]}");
        }

        // Optionally, you could display a loading screen here and wait until all audio files are loaded
        Debug.Log("All audio clips loaded. Ready to start the game.");
    }

    // Function to start the game when preloading is complete
    public void BeginGame()
    {
        isClicked=true;
        danceAway.SetActive(true);
        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(true);
            loadingText.text = _loadingStates[0];
        }
    }

    private void GameReady()
    {
        danceAway.SetActive(true);
        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(true);
        }

        if (introSequenceController != null)
        {
            // Intro controller starts the beat timer and calls ShowStartScreen when the walk finishes.
            introSequenceController.OnLoadingComplete(ShowStartScreen);
        }
        else
        {
            // Fallback when no intro controller is assigned.
            gameController.BeatTimerBegin();
            ShowStartScreen();
        }
    }

    private void ShowStartScreen()
    {
        clickToBegin.SetActive(false);
        startButton.SetActive(true);
        tutorialButton.SetActive(true);
    }
}
