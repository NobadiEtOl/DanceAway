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
    private float tolerance;
    // Difficulty: divisor applied to beatInterval to derive tolerance. Easy=12, Normal=20, Hard=35.
    [SerializeField] public float toleranceDivisor = 20f;
    // Fixed lookahead for audio scheduling — decoupled from tolerance so difficulty doesn't affect audio timing.
    [SerializeField] private float audioLookaheadSeconds = 0.12f;
    // Color squares (right-aligned, right=perfect): wire in Inspector
    [SerializeField] private RectTransform perfectSquare;
    [SerializeField] private RectTransform closeSquare;
    [SerializeField] private RectTransform middleSquare;
    [SerializeField] private RectTransform farSquare;
    private float barWidth;
    // ---- Centralized Beat Track ----
    // nextBeatDsp: DSP timestamp when the next beat will fire.
    // Advances forward each beat — never reset, never modulo.
    private double nextBeatDsp;
    private double lastBeatDsp;  // DSP timestamp of the most recent actual beat
    private bool trackStarted = false; // latches true on the first active FixedUpdate
    public float trackPitch = 0.8333f; // centralized pitch — one value controls all sources
    // ---------------------------------
    public BeatState state { get; set; }
    public bool play = false;
    public int beatCounter;
    private bool beatFired = false;
    [SerializeField]private SnapController snapController;

    void Awake()
    {

    }

    private Vector3 beatIndLeftPos;
    void Start()
    {
        backGround = GameObject.Find("BackGround").GetComponent<SpriteRenderer>();
        gameController = GetComponent<GameController>();
        tolerance = beatInterval / toleranceDivisor;
        audioDelay = beatInterval * 0.9f;
        RectTransform rectTransform = beatIndicator.GetComponent<RectTransform>();
        barWidth = rectTransform.rect.width * rectTransform.lossyScale.x;
        beatIndLeftPos = rectTransform.position - new Vector3(barWidth / 2, 0, 0);
        beatIndicatorCurrent.position = new Vector3(beatIndLeftPos.x, beatIndicatorCurrent.position.y, 5f);
        ResizeColorSquares();
        StartAfterDelay();
    }

    public bool begin = false;
    public void StartAfterDelay()
    {
        beatCounter = -1;
        play = true;
        state = BeatState.OffBeat;
        trackStarted = false; // force re-anchor of nextBeatDsp on the next active frame
    }


    void FixedUpdate()
    {
        if (begin)
        {
            if (timeS != 1) Time.timeScale = 1 * timeS;

            // Recompute tolerance and audioDelay every frame so they stay accurate after SetTempo
            tolerance  = beatInterval / toleranceDivisor;
            audioDelay = beatInterval * 0.9f;

            double dsp = AudioSettings.dspTime;

            // Anchor the track to DSP time on the very first active frame
            if (!trackStarted)
            {
                nextBeatDsp  = dsp + beatInterval;
                trackStarted = true;
            }

            // Lookahead: fire the beat event audioLookaheadSeconds before the actual DSP beat time,
            // giving enough lead time for PlayScheduled calls to the audio hardware.
            // Fixed and decoupled from tolerance so difficulty changes don't affect audio scheduling.
            double lookahead = dsp + audioLookaheadSeconds;

            if (lookahead >= nextBeatDsp && !beatFired)
            {
                beatFired = true;
                double thisBeatDsp = nextBeatDsp; // exact DSP time of this beat for audio scheduling
                lastBeatDsp = thisBeatDsp;         // record for distance calculation
                nextBeatDsp += beatInterval;       // advance track — never reset

                beatCounter++;
                OnBeat?.Invoke();

                if (beatCounter % 16 == 0)
                {
                    if (play)
                    {
                        gameController.PlayBackScheduled(thisBeatDsp);
                        play = false;
                    }
                    gameController.PlayHandScheduled(thisBeatDsp);
                }
            }
            else if (lookahead < nextBeatDsp - audioLookaheadSeconds)
            {
                beatFired = false; // reset so the next beat can fire
            }

            if(backText) backText.text = backSlider.value.ToString();

            // Symmetric distance from the nearest beat, in seconds.
            // 0 = right on the beat, beatInterval/2 = furthest from any beat.
            // Continuous and jump-free because both sides equal 0 at the beat moment.
            double timeSinceBeat = dsp - lastBeatDsp;
            double timeUntilBeat = nextBeatDsp - dsp;
            double distFromBeat  = System.Math.Min(System.Math.Abs(timeSinceBeat), timeUntilBeat);
            distFromBeat         = System.Math.Max(0.0, distFromBeat);

            UpdateBeatState(distFromBeat);
            UpdateIndicator((float)distFromBeat);
        }
    }

    // Returns the DSP timestamp N beats from now (0 = next beat, 1 = beat after next, etc.)
    public double GetBeatDsp(int beatsFromNow = 0) => nextBeatDsp + beatsFromNow * beatInterval;

    // Atomically snaps tempo at a beat boundary.
    // Only beatInterval and pitch change — nextBeatDsp is untouched so the track stays stable.
    public void SetTempo(float newInterval, float newPitch)
    {
        beatInterval = newInterval;
        trackPitch   = newPitch;
        for (int i = 0; i < GameController.audioSources.Length; i++)
            GameController.audioSources[i].pitch = newPitch;
        // nextBeatDsp is already correct — the track continues from the next queued beat
    }

    // Pauses the track, preserving its position
    private double pausedAtDsp;
    public void PauseTrack()
    {
        pausedAtDsp = AudioSettings.dspTime;
        begin = false;
    }

    // Resumes the track by shifting nextBeatDsp forward over the paused gap
    public void ResumeTrack()
    {
        double offset = AudioSettings.dspTime - pausedAtDsp;
        nextBeatDsp += offset;
        begin = true;
    }

    private BeatState preState;
    // distFromBeat: 0 = on the beat (RIGHT/PerfectBeat), beatInterval/2 = midway (LEFT/OffBeat)
    private void UpdateBeatState(double distFromBeat)
    {
        if (distFromBeat <= tolerance)
        {
            backGround.color = new Color(1f, 0f, 0f, 0.01f);
            state = BeatState.PerfectBeat;
        }
        else if (distFromBeat <= 2 * tolerance)
        {
            backGround.color = new Color(0f, 0f, 1f, 0.01f);
            state = BeatState.CloseBeat;
        }
        else if (distFromBeat <= 3 * tolerance)
        {
            backGround.color = new Color(0f, 1f, 0f, 0.01f);
            state = BeatState.MiddleBeat;
        }
        else if (distFromBeat <= 4 * tolerance)
        {
            backGround.color = new Color(1f, 1f, 0f, 0.01f);
            state = BeatState.FarBeat;
        }
        else
        {
            backGround.color = new Color(0f, 0f, 0f, 0.01f);
            if (state == BeatState.FarBeat) OffBeat?.Invoke();
            state = BeatState.OffBeat;
        }

        if (preState != state)
        {
            preState = state;
            snapController.ChangeSnap(state);
        }
    }

    // distFromBeat: 0 = on the beat → indicator at RIGHT, beatInterval/2 = furthest → indicator at LEFT
    private void UpdateIndicator(float distFromBeat)
    {
        // 1 when on the beat (RIGHT), 0 when beatInterval/2 away (LEFT). Smooth, no jumps.
        float yoyo01 = Mathf.Clamp01(1f - distFromBeat / (beatInterval * 0.5f));
        float x = beatIndLeftPos.x + barWidth * yoyo01;
        beatIndicatorCurrent.position = new Vector3(x, beatIndicatorCurrent.position.y, 5f);
    }

    // Repositions and resizes the 4 colored beat-zone squares to match the current toleranceDivisor.
    // squareWorldWidth = (2 / toleranceDivisor) * barWidth, matching exactly 1 tolerance step each.
    private void ResizeColorSquares()
    {
        if (perfectSquare == null || closeSquare == null || middleSquare == null || farSquare == null) return;

        float squareWorldWidth = (2f / toleranceDivisor) * barWidth;
        float rightEdge = beatIndLeftPos.x + barWidth;

        RectTransform[] squares = { perfectSquare, closeSquare, middleSquare, farSquare };
        for (int i = 0; i < squares.Length; i++)
        {
            float squareLocalWidth = squareWorldWidth / squares[i].lossyScale.x;
            squares[i].SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, squareLocalWidth);
            float posX = rightEdge - squareWorldWidth * (i + 0.5f);
            squares[i].position = new Vector3(posX, squares[i].position.y, squares[i].position.z);
        }
    }

    // Sets difficulty by changing toleranceDivisor and refreshing the indicator squares.
    // Called by GameController before the game session starts; locked in once play begins.
    public void SetDifficulty(float divisor)
    {
        toleranceDivisor = divisor;
        ResizeColorSquares();
    }

    public void ResetBeatCounter()
    {
        beatCounter = -1;
    }
}
