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
    [SerializeField]private Slider loadSlider;
    [SerializeField]private GameObject danceAway;
    [SerializeField]private GameObject startButton;
    [SerializeField]private GameObject tutorialButton;
    [SerializeField]private Image background;
    private static bool alreadyStarted = false;
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
        loadSlider.gameObject.SetActive(false);
    }

    [SerializeField]private bool readyToPlay=false;
    void Update()
    {
        beginTimer+= Time.deltaTime;
        if((beginTimer>=loadTime && isClicked) || readyToPlay || alreadyStarted)
        {
            alreadyStarted=true;
            clickToBegin.SetActive(false);
            GameReady();
        }
        else
        {
            loadSlider.value = beginTimer/loadTime;
            Color color = Color.black;
            color.a = 1-beginTimer/loadTime;
            background.color = color;
        }

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
        loadSlider.gameObject.SetActive(true);
        isClicked=true;
        danceAway.SetActive(true);
        clickToBegin.SetActive(false);
    }

    private void GameReady()
    {
        danceAway.SetActive(true);
        startButton.SetActive(true);
        tutorialButton.SetActive(true);
        loadSlider.gameObject.SetActive(false);
        gameController.BeatTimerBegin();
        
    } 
}
