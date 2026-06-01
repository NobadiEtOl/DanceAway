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
    [SerializeField]public float audioDelay;
    [SerializeField] private Sprite[] barSprites;          // Assign sliced bar sprites L→R in Inspector
    [SerializeField] private RectTransform beatIndicatorBackground; // The empty-bars background Image RectTransform
    // How long (seconds) the bar holds at 100% either side of the beat.
    // Widens the peak so it is never skipped by the render framerate.
    [SerializeField] private float peakHoldSeconds = 0.05f;
    private Image[] _barImages;                             // Created at runtime by PositionFilledBars
    private GameController gameController;
    public event Action OnBeat;
    public event Action OffBeat;
    private SpriteRenderer backGround;
    private float tolerance;
    // Difficulty: divisor applied to beatInterval to derive tolerance. Easy=12, Normal=20, Hard=35.
    [SerializeField] public float toleranceDivisor = 20f;
    // Fixed lookahead for audio scheduling — decoupled from tolerance so difficulty doesn't affect audio timing.
    [SerializeField] private float audioLookaheadSeconds = 0.12f;
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

    // ── Calibration Mode ─────────────────────────────────────────────────────
    [Header("Calibration Mode")]
    [Tooltip("Empty RectTransform placed in the Canvas at the soundbar's desired position during calibration.")]
    [SerializeField] private RectTransform calibrationIndicatorAnchor;
    private bool      _calibrationMode   = false;
    private float     _calibrationFill01 = 0f;
    private Vector2   _normalIndicatorPos;
    private Coroutine _indicatorMoveCor;

    void Awake()
    {

    }

    void Start()
    {
        backGround = GameObject.Find("BackGround").GetComponent<SpriteRenderer>();
        gameController = GetComponent<GameController>();
        tolerance = beatInterval / toleranceDivisor;
        audioDelay = beatInterval * 0.9f;
        StartCoroutine(PositionFilledBarsNextFrame());
        StartAfterDelay();
        if (beatIndicatorBackground != null)
            _normalIndicatorPos = beatIndicatorBackground.anchoredPosition;
    }

    // -------------------------------------------------------------------------
    // Beat Indicator Bar Positioning
    // -------------------------------------------------------------------------

    // Waits one frame so the Canvas has finished its layout pass before reading rect sizes.
    private IEnumerator PositionFilledBarsNextFrame()
    {
        yield return null;
        PositionFilledBars();
    }

    /// <summary>
    /// Creates one Image GameObject per sprite in barSprites, parents them all under
    /// beatIndicatorBackground, and positions each one over its matching pixel slot.
    ///
    /// Source image: 512 × 64 px
    ///   5 px left pad | 42 × (10 px bar + 2 px gap) | 5 px right pad
    /// </summary>
    private void PositionFilledBars()
    {
        if (barSprites == null || barSprites.Length == 0 || beatIndicatorBackground == null) return;

        float totalW = beatIndicatorBackground.rect.width;

        // Pixel constants from the source image (512 px wide)
        const float IMG_W  = 512f;
        const float PAD    = 5f;
        const float BAR_W  = 10f;
        const float STRIDE = 12f; // 10 px bar + 2 px gap

        float scale = totalW / IMG_W;

        _barImages = new Image[barSprites.Length];

        for (int i = 0; i < barSprites.Length; i++)
        {
            GameObject go = new GameObject($"Bar_{i:D2}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(beatIndicatorBackground, false);

            Image img = go.GetComponent<Image>();
            img.sprite        = barSprites[i];
            img.raycastTarget = false;
            go.SetActive(false); // hidden until the beat brings it in

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);

            // Center-out layout: even sprites fill the left side, odd sprites fill the right side,
            // both expanding outward from center. Sprite 0/1 (red) sit closest to center;
            // sprite 40/41 (green) sit at the outer edges.
            int barIndex;
            if (i % 2 == 0)
                barIndex = 20 - (i / 2);       // left side: slot 0 → bar 20, slot 20 → bar 0
            else
                barIndex = 21 + ((i - 1) / 2); // right side: slot 0 → bar 21, slot 20 → bar 41
            float centerX = (PAD + barIndex * STRIDE + BAR_W * 0.5f) * scale - totalW * 0.5f;
            rt.anchoredPosition = new Vector2(centerX, 0f);

            // Width = scaled bar; height = 0 → full parent height via Y anchors
            rt.sizeDelta = new Vector2(BAR_W * scale, 0f);

            _barImages[i] = img;
        }
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

            // Symmetric distance from the nearest beat, in seconds.
            // 0 = right on the beat, beatInterval/2 = furthest from any beat.
            // Continuous and jump-free because both sides equal 0 at the beat moment.
            double timeSinceBeat = dsp - lastBeatDsp;
            double timeUntilBeat = nextBeatDsp - dsp;
            double distFromBeat  = System.Math.Min(System.Math.Abs(timeSinceBeat), timeUntilBeat);
            distFromBeat         = System.Math.Max(0.0, distFromBeat);

            UpdateBeatState(distFromBeat);
            // UpdateIndicator runs in Update() at render frame-rate instead.
        }
    }

    // Visual-only: runs every rendered frame for finer DSP sampling.
    void Update()
    {
        if (_calibrationMode)
        {
            UpdateIndicatorWithFill(_calibrationFill01);
            return;
        }
        if (!begin) return;
        double dsp           = AudioSettings.dspTime;
        double timeSinceBeat = dsp - lastBeatDsp;
        double timeUntilBeat = nextBeatDsp - dsp;
        double distFromBeat  = System.Math.Min(System.Math.Abs(timeSinceBeat), timeUntilBeat);
        distFromBeat         = System.Math.Max(0.0, distFromBeat);
        UpdateIndicator((float)distFromBeat);
    }

    // Returns the DSP timestamp N beats from now (0 = next beat, 1 = beat after next, etc.)
    public double GetBeatDsp(int beatsFromNow = 0) => nextBeatDsp + beatsFromNow * beatInterval;

    // Returns a 0–1 value representing how far the current moment is from the nearest beat.
    // 0 = exactly on the beat (PerfectBeat), 1 = midpoint between beats (OffBeat).
    // Safe to call from Update() every frame.
    public float GetNormalizedBeatPhase()
    {
        if (!begin || !trackStarted) return 0f;
        double dsp           = AudioSettings.dspTime;
        double timeSinceBeat = dsp - lastBeatDsp;
        double timeUntilBeat = nextBeatDsp - dsp;
        double distFromBeat  = System.Math.Min(System.Math.Abs(timeSinceBeat), timeUntilBeat);
        distFromBeat         = System.Math.Max(0.0, distFromBeat);
        float halfInterval   = beatInterval * 0.5f;
        return halfInterval > 0f ? Mathf.Clamp01((float)(distFromBeat / halfInterval)) : 0f;
    }

    // Atomically snaps tempo at a beat boundary.
    // Only beatInterval and pitch change — nextBeatDsp is untouched so the track stays stable.
    public void SetTempo(float newInterval, float newPitch)
    {
        float prevInterval = beatInterval;
        float prevPitch    = trackPitch;

        beatInterval = newInterval;
        trackPitch   = newPitch;
        for (int i = 0; i < GameController.audioSources.Length; i++)
            GameController.audioSources[i].pitch = newPitch;
        // nextBeatDsp is already correct — the track continues from the next queued beat

        if (newInterval > 0f && prevInterval > 0f)
        {
            float prevBpm = 60f / prevInterval;
            float newBpm  = 60f / newInterval;
            Debug.Log($"[tap] Tempo change: {prevBpm:F2} -> {newBpm:F2} BPM ({prevInterval * 1000f:F1} -> {newInterval * 1000f:F1} ms), pitch {prevPitch:F3} -> {newPitch:F3}");
        }
        else
        {
            Debug.Log($"[tap] Tempo change: interval {prevInterval * 1000f:F1} -> {newInterval * 1000f:F1} ms, pitch {prevPitch:F3} -> {newPitch:F3}");
        }
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

    // distFromBeat: 0 = on the beat → all bars lit, beatInterval/2 = furthest from beat → no bars lit.
    // A flat-top hold of peakHoldSeconds either side of the beat guarantees 100% is visible
    // even when the render framerate cannot sample the exact zero-crossing.
    private void UpdateIndicator(float distFromBeat)
    {
        if (_barImages == null || _barImages.Length == 0) return;

        float halfInterval = beatInterval * 0.5f;
        float hold         = Mathf.Clamp(peakHoldSeconds, 0f, halfInterval - 0.001f);

        float yoyo01;
        if (distFromBeat <= hold)
        {
            // Inside the hold window — always show fully-filled bars.
            yoyo01 = 1f;
        }
        else
        {
            // Ramp from 1 (at hold boundary) down to 0 (at halfInterval).
            yoyo01 = Mathf.Clamp01(1f - (distFromBeat - hold) / (halfInterval - hold));
        }

        int barsToShow = Mathf.RoundToInt(yoyo01 * _barImages.Length);
        for (int i = 0; i < _barImages.Length; i++)
            if (_barImages[i] != null) _barImages[i].gameObject.SetActive(i < barsToShow);
    }

    // Called by GameController before the game session starts; locked in once play begins.
    public void SetDifficulty(float divisor)
    {
        toleranceDivisor = divisor;
    }

    public void ResetBeatCounter()
    {
        beatCounter = -1;
    }

    // -------------------------------------------------------------------------
    // Play Your Music / Tap Calibration API
    // -------------------------------------------------------------------------

    /// <summary>Returns the signed DSP offset (seconds) of the current moment from the nearest beat.
    /// Positive = tap landed after the beat (late); negative = tap landed before the next beat (early).
    /// Returns 0 when the track is not running.</summary>
    public double GetSignedBeatOffset()
    {
        if (!begin || !trackStarted) return 0.0;
        double dsp          = AudioSettings.dspTime;
        double timeSinceBeat = dsp - lastBeatDsp;
        double timeUntilBeat = nextBeatDsp - dsp;
        return timeSinceBeat < timeUntilBeat ? timeSinceBeat : -timeUntilBeat;
    }

    /// <summary>Re-anchors the beat track to a specific DSP timestamp (typically the last
    /// calibration tap). Sets nextBeatDsp = anchorDspTime + beatInterval so the first
    /// post-calibration beat fires exactly one interval after the anchor.
    /// Sets trackStarted = true so FixedUpdate does not override the anchor.</summary>
    public void SetPhase(double anchorDspTime)
    {
        lastBeatDsp  = anchorDspTime;
        nextBeatDsp  = anchorDspTime + beatInterval;
        trackStarted = true;
        beatFired    = false;
    }

    /// <summary>Shifts nextBeatDsp by deltaSeconds. Positive shifts beats later;
    /// negative shifts them earlier. Used by the drift corrector for small in-game nudges.</summary>
    public void ShiftPhase(double deltaSeconds)
    {
        nextBeatDsp += deltaSeconds;
    }

    // ── Calibration Mode API ─────────────────────────────────────────────────

    /// <summary>Enables or disables calibration fill mode for the beat indicator.
    /// In calibration mode the indicator shows a fill driven by SetCalibrationFill()
    /// instead of the live DSP beat distance. Also animates the indicator to/from
    /// its calibration anchor position.</summary>
    public void SetCalibrationMode(bool active)
    {
        _calibrationMode = active;
        if (_indicatorMoveCor != null) StopCoroutine(_indicatorMoveCor);
        _indicatorMoveCor = StartCoroutine(AnimateIndicatorPos(active));
        if (!active && _barImages != null)
            UpdateIndicatorWithFill(0f); // clear fill immediately on exit
    }

    /// <summary>Sets the calibration fill fraction (0–1) shown on the beat indicator.
    /// Call after each tap to reflect confidence growth.</summary>
    public void SetCalibrationFill(float fill01)
    {
        _calibrationFill01 = Mathf.Clamp01(fill01);
        if (_calibrationMode && _barImages != null)
            UpdateIndicatorWithFill(_calibrationFill01);
    }

    private void UpdateIndicatorWithFill(float fill01)
    {
        if (_barImages == null || _barImages.Length == 0) return;
        int barsToShow = Mathf.FloorToInt(fill01 * _barImages.Length);
        for (int i = 0; i < _barImages.Length; i++)
            if (_barImages[i] != null)
                _barImages[i].gameObject.SetActive(i < barsToShow);
    }

    private IEnumerator AnimateIndicatorPos(bool toCalibration)
    {
        if (beatIndicatorBackground == null) yield break;
        Vector2 target = toCalibration && calibrationIndicatorAnchor != null
            ? calibrationIndicatorAnchor.anchoredPosition
            : _normalIndicatorPos;
        Vector2 start = beatIndicatorBackground.anchoredPosition;
        const float Duration = 0.3f;
        for (float t = 0f; t < Duration; t += Time.unscaledDeltaTime)
        {
            beatIndicatorBackground.anchoredPosition =
                Vector2.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / Duration));
            yield return null;
        }
        beatIndicatorBackground.anchoredPosition = target;
        _indicatorMoveCor = null;
    }
}
