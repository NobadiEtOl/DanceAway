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
    private float beatTolerance;
    // ---- Centralized Beat Track ----
    // nextBeatDsp: DSP timestamp when the next beat will fire.
    // Advances forward each beat — never reset, never modulo.
    private double nextBeatDsp;
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
        tolerance = beatInterval / 20f;
        beatTolerance = beatInterval / 15f;
        audioDelay = beatInterval * 0.9f;
        RectTransform rectTransform = beatIndicator.GetComponent<RectTransform>();
        beatIndLeftPos = rectTransform.position - new Vector3(rectTransform.rect.width * rectTransform.lossyScale.x / 2, 0, 0);
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
            tolerance  = beatInterval / 20f;
            audioDelay = beatInterval * 0.9f;

            double dsp = AudioSettings.dspTime;

            // Anchor the track to DSP time on the very first active frame
            if (!trackStarted)
            {
                nextBeatDsp  = dsp + beatInterval;
                trackStarted = true;
            }

            // Lookahead: fire the beat event tolerance*4 seconds before the actual DSP beat time,
            // giving enough lead time for PlayScheduled calls to the audio hardware.
            double lookahead = dsp + tolerance * 4.0;

            if (lookahead >= nextBeatDsp && !beatFired)
            {
                beatFired = true;
                double thisBeatDsp = nextBeatDsp; // exact DSP time of this beat for audio scheduling
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
            else if (lookahead < nextBeatDsp - tolerance)
            {
                beatFired = false; // reset so the next beat can fire
            }

            if(backText) backText.text = backSlider.value.ToString();

            // Beat phase: seconds elapsed since the last beat (0 → beatInterval), used for
            // scoring windows, background color, and the beat indicator position.
            double beatPhase = dsp - (nextBeatDsp - beatInterval);
            beatPhase = System.Math.Max(0.0, beatPhase);

            UpdateBeatState(beatPhase);
            UpdateIndicator((float)beatPhase);
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
    private void UpdateBeatState(double beatPhase)
    {
        if (beatPhase <= tolerance || beatPhase >= beatInterval - tolerance)
        {
            backGround.color = new Color(1f, 0f, 0f, 0.01f);
            state = BeatState.PerfectBeat;
        }
        else
        {
            UpdateBeatVisuals(beatPhase);
        }

        if (preState != state)
        {
            preState = state;
            snapController.ChangeSnap(state);
        }
    }

    private void UpdateBeatVisuals(double beatPhase)
    {
        if (beatPhase <= 2 * tolerance || beatPhase >= beatInterval - 2 * tolerance)
        {
            backGround.color = new Color(0f, 0f, 1f, 0.01f);
            state = BeatState.CloseBeat;
        }
        else if (beatPhase <= 3 * tolerance || beatPhase >= beatInterval - 3 * tolerance)
        {
            backGround.color = new Color(0f, 1f, 0f, 0.01f);
            state = BeatState.MiddleBeat;
        }
        else if (beatPhase <= 4 * tolerance || beatPhase >= beatInterval - 4 * tolerance)
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
    }

    private void UpdateIndicator(float beatPhase)
    {
        float beatIndicatorSpeed = beatIndicator.rectTransform.rect.width / (beatInterval / 2f);
        float tInd = Mathf.Clamp(beatPhase, 0f, beatInterval);
        if (tInd < beatInterval / 2f)
        {
            beatIndicatorCurrent.position = new Vector3(200 + beatIndLeftPos.x - beatIndicatorSpeed * tInd, beatIndicatorCurrent.position.y, 5);
        }
        else
        {
            beatIndicatorCurrent.position = new Vector3(beatIndLeftPos.x + beatIndicatorSpeed * (tInd - beatInterval / 2f), beatIndicatorCurrent.position.y, 5);
        }
    }

    public void ResetBeatCounter()
    {
        beatCounter = -1;
    }
}
