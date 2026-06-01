using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Common.Enums;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the "Play Your Music" tap-tempo calibration session.
///
/// Flow:
///   1. BeginCalibration() — shows overlay if assigned, pauses game if mid-play.
///   2. Player calls OnTapInput() repeatedly to the beat of their music.
///   3. Algorithm computes beat interval via median + outlier-rejected tap intervals,
///      snaps to nearest 0.5 BPM, and tracks confidence via standard deviation.
///   4. Auto-complete fires when >= MinTaps taps with stdDev < 20 ms are collected,
///      or the player presses Done (ConfirmCalibration).
///   5. A 3-beat countdown plays on the newly calibrated beat, then
///      GameController.FinishCalibration() is called to resume gameplay.
///   6. CancelCalibration() restores the original tempo and resumes the game.
///
/// All UI references are optional — the controller works without them.
/// Test via the ContextMenu helpers on GameController.
/// </summary>
public class TapCalibrationController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Public state
    // -------------------------------------------------------------------------
    public enum CalibrationState { Idle, Collecting, Ready, CountingDown }
    public CalibrationState State { get; private set; } = CalibrationState.Idle;

    // -------------------------------------------------------------------------
    // Optional UI references (assign in Inspector once the panel is built)
    // -------------------------------------------------------------------------
    [Header("Calibration Panel (assign after building UI)")]
    [Tooltip("Root panel to show/hide. Leave empty while UI is not yet built.")]
    [SerializeField] private GameObject calibrationPanel;
    [Tooltip("Text showing the detected BPM, e.g. '128 BPM'.")]
    [SerializeField] private Text bpmText;
    [Tooltip("Text showing confidence state: 'Keep tapping…' / 'Almost ready…' / 'Ready!'.")]
    [SerializeField] private Text confidenceText;
    [Tooltip("Text showing countdown value: '3', '2', '1', 'GO!'.")]
    [SerializeField] private Text countdownText;
    [Tooltip("Object that scale-punches on each beat once BPM is detected (visual metronome).")]
    [SerializeField] private GameObject metronomeVisual;

    [Header("Calibration References")]
    [Tooltip("Bottom half controller — switches to calibration panel and back to gameplay.")]
    [SerializeField] private ConsoleBottomHalfController consoleBottomHalf;
    [Tooltip("Crowd controller — stops nodding during calibration, restored on finish.")]
    [SerializeField] private CrowdController crowdController;

    [Header("PYM Info Screen")]
    [Tooltip("Root object for the Play Your Music info popup shown from the start screen.")]
    [SerializeField] private GameObject pymInfoScreen;
    [Tooltip("Accept button on the PYM info screen. Starts PYM game flow.")]
    [SerializeField] private Button pymInfoStartButton;

    // -------------------------------------------------------------------------
    // Algorithm constants
    // -------------------------------------------------------------------------
    private const int   MinTaps            = 6;      // minimum taps before auto-complete is eligible
    private const float MinBpm             = 60f;
    private const float MaxBpm             = 220f;
    private const float AutoCompleteSdMs  = 35f;    // stddev threshold (ms) for auto-complete
    private const float OutlierFraction   = 0.25f;  // intervals >25% off the median are discarded
    private const float MetronomeScale    = 1.35f;  // punch scale for the visual metronome
    private const int   WindowSize         = 8;     // rolling window: only the last N intervals are used
    private const float IterativeSigmaMult = 1.5f;  // iterative pass: discard intervals > N*σ from mean
    private const int   MinValidIntervals  = 4;     // never prune below this many intervals

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------
    private GameController   _gc;
    private BeatTimer        _bt;

    private readonly List<double> _tapTimes = new List<double>();
    private float  _snappedBeatInterval = 0f;
    private float  _currentBpm          = 0f;
    private float  _currentSdMs         = float.MaxValue;
    private bool   _isMidGame           = false;
    private float  _savedBeatInterval   = 0f;   // restored on Cancel
    private int    _countdownValue      = 3;
    private Coroutine _metronomeCor;
    private Coroutine _fillCor;      // animates fill during tapping and drains it during countdown
    private float     _smoothedFill; // displayed fill value — lags behind computed target

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------
    private void Awake()
    {
        _gc = GetComponent<GameController>();
        _bt = GetComponent<BeatTimer>();

        if (pymInfoStartButton != null)
        {
            pymInfoStartButton.onClick.RemoveListener(OnPymInfoStartPressed);
            pymInfoStartButton.onClick.AddListener(OnPymInfoStartPressed);
        }

        if (pymInfoScreen != null)
            pymInfoScreen.SetActive(false);

        if (calibrationPanel != null)
            calibrationPanel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Begins a calibration session.
    /// Pass isMidGame=true to pause live gameplay first.</summary>
    public void BeginCalibration(bool isMidGame)
    {
        ClosePymInfoScreen();
        _isMidGame           = isMidGame;
        _savedBeatInterval   = _bt.beatInterval;
        _tapTimes.Clear();
        _snappedBeatInterval = 0f;
        _currentBpm          = 0f;
        _currentSdMs         = float.MaxValue;
        State                = CalibrationState.Idle;
        _gc.inCalibration    = true;
        _smoothedFill        = 0f;
        if (_fillCor != null) { StopCoroutine(_fillCor); _fillCor = null; }

        consoleBottomHalf?.ShowCalibration();
        crowdController?.LessNodders(9999);
        _bt.SetCalibrationMode(true);
        _bt.SetCalibrationFill(0f);

        // For mid-game: snapshot and evacuate enemies BEFORE pausing so they can animate out
        if (isMidGame) _gc.StartEnemyEvacuation();

        // Pause the beat track after evacuation is triggered
        _bt.PauseTrack();

        _gc.SetPymRecalibrationButtonVisible(false);

        if (calibrationPanel != null) calibrationPanel.SetActive(true);
        UpdateUI();

        Debug.Log("[TapCalibration] Calibration started. Tap to the beat of your music.");
    }

    /// <summary>Record a tap. Call from the tap button or GameController context menu.</summary>
    public void OnTapInput()
    {
        if (State == CalibrationState.CountingDown)
        {
            // Refine the calibration with taps made during the countdown.
            // Phase is already anchored — only the interval is updated.
            _tapTimes.Add(AudioSettings.dspTime);
            ComputeAndUpdate();
            if (_snappedBeatInterval > 0f)
                _bt.SetTempo(_snappedBeatInterval, _bt.trackPitch);
            UpdateUI();
            Debug.Log($"[TapCalibration] Countdown tap #{_tapTimes.Count} — {_currentBpm:F1} BPM, σ={_currentSdMs:F1} ms");
            return;
        }

        _tapTimes.Add(AudioSettings.dspTime);

        if (State == CalibrationState.Idle)
            State = CalibrationState.Collecting;

        if (_tapTimes.Count >= 2)
        {
            ComputeAndUpdate(); // may auto-confirm and set State = CountingDown
            if (State != CalibrationState.CountingDown)
            {
                // Limit each tap's contribution to MaxFillJumpPerTap to prevent sudden jumps.
                // Also allow a small decrease if confidence drops (e.g. badly timed tap).
                const float MaxFillJumpPerTap = 0.12f;
                float target  = ComputeCalibrationFill();
                float delta   = target - _smoothedFill;
                _smoothedFill = Mathf.Clamp01(_smoothedFill + Mathf.Clamp(delta, -0.05f, MaxFillJumpPerTap));
                _bt.SetCalibrationFill(_smoothedFill);
            }
        }

        UpdateUI();
        Debug.Log($"[TapCalibration] Tap #{_tapTimes.Count} — {_currentBpm:F1} BPM, σ={_currentSdMs:F1} ms");
    }

    /// <summary>Remove the most recent tap and recompute.</summary>
    public void UndoLastTap()
    {
        if (_tapTimes.Count == 0 || State == CalibrationState.CountingDown) return;

        _tapTimes.RemoveAt(_tapTimes.Count - 1);

        if (_tapTimes.Count == 0)
        {
            State                = CalibrationState.Idle;
            _snappedBeatInterval = 0f;
            _currentBpm          = 0f;
            _currentSdMs         = float.MaxValue;
        }
        else if (_tapTimes.Count == 1)
        {
            State                = CalibrationState.Collecting;
            _snappedBeatInterval = 0f;
            _currentBpm          = 0f;
            _currentSdMs         = float.MaxValue;
        }
        else
        {
            ComputeAndUpdate();
        }

        // Sync displayed fill downward on undo — never exceed the newly-lowered target.
        float undoTarget = ComputeCalibrationFill();
        _smoothedFill = Mathf.Min(_smoothedFill, undoTarget);
        _bt.SetCalibrationFill(_smoothedFill);

        UpdateUI();
    }

    /// <summary>Apply the calibrated tempo and begin a 3-beat countdown before gameplay.</summary>
    public void ConfirmCalibration()
    {
        if (_snappedBeatInterval <= 0f || State == CalibrationState.CountingDown)
        {
            if (_snappedBeatInterval <= 0f)
                Debug.LogWarning("[TapCalibration] Cannot confirm — no valid interval yet. Keep tapping.");
            return;
        }

        State           = CalibrationState.CountingDown;
        _countdownValue = 4; // fires 3, 2, 1, GO!

        // Apply new tempo atomically
        _bt.SetTempo(_snappedBeatInterval, _bt.trackPitch);

        // Anchor phase to the last tap so the first beat fires one interval after it
        double lastTapDsp = _tapTimes[_tapTimes.Count - 1];
        _bt.SetPhase(lastTapDsp);

        // Fill indicator to full — the drain coroutine will empty it over the countdown.
        _smoothedFill = 1f;
        _bt.SetCalibrationFill(1f);
        // Calibration mode stays ACTIVE — SetCalibrationMode(false) happens in OnCountdownBeat.

        // Start the beat track (SetPhase sets trackStarted=true so FixedUpdate won't re-anchor)
        _bt.begin = true;

        // Subscribe for the countdown
        _bt.OnBeat += OnCountdownBeat;

        // Drain over beatInterval/2: ends at the midpoint between last tap and first beat,
        // which is exactly distFromBeat=max (indicator naturally at 0). The indicator then
        // grows from 0 → full over the remaining beatInterval/2, landing on the first in-phase beat.
        if (_fillCor != null) StopCoroutine(_fillCor);
        _fillCor = StartCoroutine(DrainFill(_bt.beatInterval * 0.5f));

        StartMetronome();
        UpdateUI();

        Debug.Log($"[TapCalibration] Confirmed {_currentBpm:F1} BPM ({_snappedBeatInterval * 1000f:F0} ms). Counting in…");
    }

    /// <summary>Discard calibration and restore the previous beat interval.</summary>
    public void CancelCalibration()
    {
        if (State == CalibrationState.CountingDown) return;

        _bt.OnBeat -= OnCountdownBeat; // safety unsubscribe
        StopMetronome();

        // Restore saved tempo
        _bt.SetTempo(_savedBeatInterval, _bt.trackPitch);
        // Re-anchor phase to now (no last tap available)
        _bt.SetPhase(AudioSettings.dspTime);

        State           = CalibrationState.CountingDown;
        _countdownValue = 4; // fires 3, 2, 1, GO!

        // Calibration mode stays ACTIVE — drain from whatever fill is currently shown.
        // SetCalibrationMode(false) happens in OnCountdownBeat when countdown completes.

        // Start the beat track
        _bt.begin = true;

        // Subscribe for the countdown
        _bt.OnBeat += OnCountdownBeat;

        // SetPhase is anchored to now, so beatInterval/2 lands at the natural darkest point.
        if (_fillCor != null) StopCoroutine(_fillCor);
        _fillCor = StartCoroutine(DrainFill(_bt.beatInterval * 0.5f));

        StartMetronome();
        UpdateUI();

        Debug.Log("[TapCalibration] Cancelled. Original tempo restored. Counting in…");
    }

    public void OpenPymInfoScreen()
    {
        if (pymInfoScreen != null) pymInfoScreen.SetActive(true);
    }

    public void ClosePymInfoScreen()
    {
        if (pymInfoScreen != null) pymInfoScreen.SetActive(false);
    }

    public bool IsPymInfoScreenOpen()
    {
        return pymInfoScreen != null && pymInfoScreen.activeSelf;
    }

    private void OnPymInfoStartPressed()
    {
        if (_gc != null)
            _gc.StartPYMGameFromInfo();
        else
            Debug.LogWarning("[TapCalibration] GameController reference missing.");
    }

    // -------------------------------------------------------------------------
    // Countdown
    // -------------------------------------------------------------------------
    private void OnCountdownBeat()
    {
        _countdownValue--;
        string label = _countdownValue > 0 ? _countdownValue.ToString() : "!!!";
        if (countdownText != null) countdownText.text = label;
        Debug.Log($"[TapCalibration] Countdown: {label}");

        PunchMetronome();

        if (_countdownValue <= 0)
        {
            _bt.OnBeat -= OnCountdownBeat;
            StopMetronome();
            // Stop drain coroutine and ensure fill reaches exactly 0
            if (_fillCor != null) { StopCoroutine(_fillCor); _fillCor = null; }
            _smoothedFill = 0f;
            _bt.SetCalibrationFill(0f);
            _bt.SetCalibrationMode(false); // safety net — DrainFill already called this
            // Slide input controls in now that the countdown is fully done
            consoleBottomHalf?.ShowGameplay();
            State = CalibrationState.Idle;
            if (calibrationPanel != null) calibrationPanel.SetActive(false);
            _gc.FinishCalibration(_isMidGame);
        }
    }

    // -------------------------------------------------------------------------
    // Algorithm
    // -------------------------------------------------------------------------
    private void ComputeAndUpdate()
    {
        // Rolling window: only the last WindowSize intervals so early bad taps age out quickly
        int startTapIdx = Mathf.Max(0, _tapTimes.Count - 1 - WindowSize);
        var intervals = new List<double>();
        for (int i = startTapIdx + 1; i < _tapTimes.Count; i++)
            intervals.Add(_tapTimes[i] - _tapTimes[i - 1]);

        // Pass 1 — median-based rejection: catches extreme pauses / accidental double-taps
        var sorted = new List<double>(intervals);
        sorted.Sort();
        double median = sorted.Count % 2 == 0
            ? (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) * 0.5
            : sorted[sorted.Count / 2];

        var valid = new List<double>(intervals.Count);
        foreach (double iv in intervals)
        {
            if (System.Math.Abs(iv - median) / median <= OutlierFraction)
                valid.Add(iv);
        }
        if (valid.Count == 0) valid = new List<double>(intervals);

        // Pass 2 — iterative σ-based rejection: converges on the stable core of the window
        bool pruned = true;
        while (pruned && valid.Count > MinValidIntervals)
        {
            double m   = valid.Average();
            double sq  = valid.Sum(v => (v - m) * (v - m));
            double sd  = System.Math.Sqrt(sq / valid.Count);
            var    next = valid.Where(v => System.Math.Abs(v - m) <= IterativeSigmaMult * sd).ToList();
            if (next.Count >= MinValidIntervals)
            {
                pruned = next.Count < valid.Count;
                valid  = next;
            }
            else
            {
                break; // pruning further would drop below the minimum — stop here
            }
        }

        // BPM from median of the final valid set
        var validSorted = new List<double>(valid);
        validSorted.Sort();
        double rawInterval = validSorted.Count % 2 == 0
            ? (validSorted[validSorted.Count / 2 - 1] + validSorted[validSorted.Count / 2]) * 0.5
            : validSorted[validSorted.Count / 2];

        // Standard deviation (ms) — used for confidence display and auto-complete
        double mean  = valid.Average();
        double sumSq = valid.Sum(v => (v - mean) * (v - mean));
        _currentSdMs = (float)(System.Math.Sqrt(sumSq / valid.Count) * 1000.0);

        // BPM snapping: clamp → half-tempo correction → round to nearest whole BPM
        float rawBpm  = 60f / (float)rawInterval;
        rawBpm        = Mathf.Clamp(rawBpm, MinBpm, MaxBpm);
        float snapped = Mathf.Round(rawBpm);                          // nearest whole BPM
        snapped       = Mathf.Clamp(snapped, MinBpm, MaxBpm);

        _currentBpm          = snapped;
        _snappedBeatInterval = 60f / snapped;

        CheckAutoComplete();
    }

    private void CheckAutoComplete()
    {
        bool enoughTaps = _tapTimes.Count >= MinTaps;
        bool confident  = _currentSdMs < AutoCompleteSdMs;

        if (State == CalibrationState.CountingDown) return; // already confirmed — countdown taps only refine

        if (enoughTaps && confident)
        {
            State = CalibrationState.Ready;
            ConfirmCalibration(); // auto-confirm — no button press required
        }
        else if (State == CalibrationState.Ready)
            State = CalibrationState.Collecting; // confidence dropped (e.g. after undo)
    }

    /// <summary>Returns a 0–1 fill value for the beat indicator based on tap count and σ confidence.
    /// 0→0.5 driven by tap count progress toward MinTaps; 0.5→1 driven by σ dropping toward AutoCompleteSdMs.</summary>
    private float ComputeCalibrationFill()
    {
        // 60% of the bar grows linearly as tap count approaches MinTaps.
        float tapProgress = Mathf.Clamp01((float)Mathf.Max(0, _tapTimes.Count - 1) / Mathf.Max(1, MinTaps - 1));
        // 40% grows as σ drops from 100 ms (noisy) toward 15 ms (locked-in).
        // Contributes from tap 2 onwards so it builds gradually alongside tap count.
        float sdRaw = _tapTimes.Count >= 2
            ? Mathf.Clamp01(1f - Mathf.InverseLerp(15f, 100f, _currentSdMs))
            : 0f;
        return tapProgress * 0.6f + sdRaw * 0.4f;
    }

    // -------------------------------------------------------------------------
    // UI helpers (all null-safe)
    // -------------------------------------------------------------------------
    private void Update()
    {
        if (pymInfoScreen == null || !pymInfoScreen.activeSelf) return;

        bool clicked = Input.GetMouseButtonDown(0) ||
                       (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        if (!clicked) return;

        Vector2 screenPos = Input.touchCount > 0
            ? (Vector2)Input.GetTouch(0).position
            : (Vector2)Input.mousePosition;

        var rt = pymInfoScreen.GetComponent<RectTransform>();
        if (rt != null && !RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
            ClosePymInfoScreen();
    }

    // -------------------------------------------------------------------------
    // UI helpers (all null-safe)
    // -------------------------------------------------------------------------
    private void UpdateUI()
    {
        if (bpmText != null)
            bpmText.text = _currentBpm > 0f ? $"{_currentBpm:F0} BPM" : "--- BPM";

        if (confidenceText != null)
        {
            if (_tapTimes.Count < 2)
                confidenceText.text = "MÜZİKLE BERABER BUTONA TIKLA";
            else if (State == CalibrationState.Ready)
                confidenceText.text = "KALİBRE EDİLDİ!";
            else if (_currentSdMs < 40f)
                confidenceText.text = "NEREDEYSE KALİBRE EDİLDİ...";
            else
                confidenceText.text = "MÜZİKLE BERABER DEVAM ET";
        }

        if (countdownText != null)
            countdownText.gameObject.SetActive(State == CalibrationState.CountingDown);
    }

    // -------------------------------------------------------------------------
    // Fill drain coroutine — smoothly empties the calibration fill indicator
    // -------------------------------------------------------------------------
    private IEnumerator DrainFill(float duration)
    {
        float startFill = _smoothedFill;
        float elapsed   = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t    = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float fill = Mathf.Lerp(startFill, 0f, t);
            _smoothedFill = fill;
            _bt.SetCalibrationFill(fill);
            yield return null;
        }
        _smoothedFill = 0f;
        _bt.SetCalibrationFill(0f);
        // Hand off to live beat-tracking now — indicator is at 0 (midpoint between beats)
        // and will grow to full over the remaining beatInterval/2 until the first in-phase beat.
        _bt.SetCalibrationMode(false);
        _fillCor = null;
    }

    // -------------------------------------------------------------------------
    // Visual metronome (null-safe, unscaled time so it works at timeScale=0)
    // -------------------------------------------------------------------------
    private void StartMetronome()
    {
        StopMetronome();
        if (metronomeVisual == null) return;
        _metronomeCor = StartCoroutine(MetronomePulse());
    }

    private void StopMetronome()
    {
        if (_metronomeCor != null) { StopCoroutine(_metronomeCor); _metronomeCor = null; }
        if (metronomeVisual != null) metronomeVisual.transform.localScale = Vector3.one;
    }

    private void PunchMetronome()
    {
        if (metronomeVisual == null) return;
        StopMetronome();
        _metronomeCor = StartCoroutine(MetronomePulse());
    }

    private IEnumerator MetronomePulse()
    {
        if (metronomeVisual == null) yield break;
        const float Duration = 0.12f;
        float elapsed = 0f;
        while (elapsed < Duration)
        {
            float t = elapsed / Duration;
            float s = Mathf.Lerp(MetronomeScale, 1f, t);
            metronomeVisual.transform.localScale = new Vector3(s, s, 1f);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        metronomeVisual.transform.localScale = Vector3.one;
    }
}
