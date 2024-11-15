using UnityEngine;
using System;
using Common.Enums;
using System.Collections;
using UnityEngine.UI;
public class BeatTimer : MonoBehaviour
{
    [Range(0,1)]
    [SerializeField] private float timeS; // To control time scale for debugging
    [SerializeField] public float beatInterval = 0.6f; // Time between beats in seconds
    [SerializeField]private Slider backSlider;
    [SerializeField]private Text backText;
    [SerializeField]private Image beatIndicator;
    [SerializeField]private Transform beatIndicatorCurrent;
    [SerializeField]public float audioDelay;
    //[SerializeField]private Text dpsText;
    private GameController gameController;
    public event Action OnBeat;
    public event Action OffBeat;
    private SpriteRenderer backGround;
    private double timer;
    private float tolerance;
    private float beatTolerance;
    public BeatState state { get; set; }
    public bool play = false;
    public int beatCounter;
    private int beatCheckCounter = 0;
    [SerializeField]private SnapController snapController;

    void Awake()
    {

    }

    private float beatIndicatorSpeed;
    private Vector3 beatIndLeftPos;
    void Start()
    {
        backGround = GameObject.Find("BackGround").GetComponent<SpriteRenderer>();
        gameController = GetComponent<GameController>();
        tolerance = beatInterval / 20;
        beatTolerance = beatInterval / 15;
        audioDelay = beatInterval*0.9f;
        beatIndicatorSpeed = beatIndicator.rectTransform.rect.width/(beatInterval/2);
        RectTransform rectTransform = beatIndicator.GetComponent<RectTransform>();
        beatIndLeftPos = rectTransform.position - new Vector3(rectTransform.rect.width * rectTransform.lossyScale.x / 2, 0, 0);
        StartAfterDelay();
    }

    public bool begin= false;
    public void StartAfterDelay()
    {
        beatCounter = -1;
        beatCheckCounter = -1;
        play = true;
        state = BeatState.OffBeat;
    }


    void FixedUpdate()
    {
        if (begin)
        {
            timer = (double)AudioSettings.dspTime + tolerance*4; // Use dspTime for accurate timing
            if (timeS != 1) Time.timeScale = 1 * timeS;
            CheckAction();
            if(backText)backText.text = backSlider.value.ToString();
            float beatIndTimer = (float)timer % beatInterval;
            if(beatIndTimer < beatInterval/2)
            {
                beatIndicatorCurrent.position = new Vector3(200+beatIndLeftPos.x-beatIndicatorSpeed*beatIndTimer,beatIndicatorCurrent.position.y,5);
            }
            else if(beatIndTimer >= beatInterval/2)
            {
                beatIndicatorCurrent.position = new Vector3(beatIndLeftPos.x+beatIndicatorSpeed*(beatIndTimer%(beatInterval/2)),beatIndicatorCurrent.position.y,5);      
            }
        }

        //dpsText.text = timer.ToString();
    }

    private bool beatFlag = true;

    void CheckAction()
    {
        double modTimer = timer % beatInterval;
        UpdateBeatState(modTimer);
    }

    private BeatState preState;
    private void UpdateBeatState(double modTimer)
    {
        // Adjust beat state color based on proximity to the beat
        if (modTimer <= tolerance || modTimer >= beatInterval - tolerance)
        {
            if(beatFlag)
            {
                beatCounter++;
                OnBeat?.Invoke();
                beatFlag = false; // Ensure the beat is only triggered once per interval
                
                if (beatCounter % 16 == 0)
                {
                    if (play)
                    {
                        PlayBack(); // Now delayed wi   th coroutine
                        play = false;
                    }
                    PlayHand(); // Now delayed with coroutine
                }
            }

            backGround.color = Color.red;
            backGround.color = new Color(backGround.color.r, backGround.color.g, backGround.color.b, 0.01f);
            state = BeatState.PerfectBeat;
        }

        else
        {
            // Reset beatFlag to allow the next beat
            beatFlag = true;
            UpdateBeatVisuals(modTimer);  // Adjust colors for intermediate states
        }

        if(preState != state)
        {
            preState=state;
            snapController.ChangeSnap(state);
        }
    }

    private void UpdateBeatVisuals(double modTimer)
    {
        if (modTimer <= 2 * tolerance || (modTimer >= (beatInterval - 2 * tolerance)))
        {
            backGround.color = Color.blue;
            backGround.color = new Color(backGround.color.r, backGround.color.g, backGround.color.b, 0.01f);
            state = BeatState.CloseBeat;
        }
        else if (modTimer <= 3 * tolerance || (modTimer >= (beatInterval - 3 * tolerance)))
        {
            backGround.color = Color.green;
            backGround.color = new Color(backGround.color.r, backGround.color.g, backGround.color.b, 0.01f);
            state = BeatState.MiddleBeat;
        }
        else if (modTimer <= 4 * tolerance || (modTimer >= (beatInterval - 4 * tolerance)))
        {
            backGround.color = Color.yellow;
            backGround.color = new Color(backGround.color.r, backGround.color.g, backGround.color.b, 0.01f);
            state = BeatState.FarBeat;
        }
        else
        {
            backGround.color = Color.black;
            backGround.color = new Color(backGround.color.r, backGround.color.g, backGround.color.b, 0.01f);
            if(state == BeatState.FarBeat) OffBeat?.Invoke();
            state = BeatState.OffBeat;
            if (!beatFlag) beatFlag = true;
        }

    }

    // Use coroutine for PlayBack to delay its execution
    private void PlayBack()
    {
        StartCoroutine(DelayedPlayBack(audioDelay));
    }

    private IEnumerator DelayedPlayBack(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gameController) gameController.PlayBack();
    }

    // Use coroutine for PlayHand to delay its execution
    void PlayHand()
    {
        StartCoroutine(DelayedPlayHand(audioDelay));
    }

    private IEnumerator DelayedPlayHand(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gameController) gameController.PlayHand();
    }

    public void ResetBeatCounter()
    {
        beatCounter = -1;
    }
}
