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

    [Header("PYM Info Screen")]
    [Tooltip("Root object for the Play Your Music info popup shown from the start screen.")]
    [SerializeField] private GameObject pymInfoScreen;
    [Tooltip("Accept button on the PYM info screen. Starts PYM game flow.")]
    [SerializeField] private Button pymInfoStartButton;
    [Tooltip("Close button on the PYM info screen.")]
    [SerializeField] private Button pymInfoCloseButton;

    // -------------------------------------------------------------------------
    // Algorithm constants
    // -------------------------------------------------------------------------
    private const int   MinTaps            = 6;      // minimum taps before auto-complete is eligible
    private const float MinBpm             = 60f;
    private const float MaxBpm             = 220f;
    private const float HalfTempoCutoff   = 80f;    // below this raw BPM, assume half-tempo and double
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

        if (pymInfoCloseButton != null)
        {
            pymInfoCloseButton.onClick.RemoveListener(ClosePymInfoScreen);
            pymInfoCloseButton.onClick.AddListener(ClosePymInfoScreen);
        }

        if (pymInfoScreen != null)
            pymInfoScreen.SetActive(false);
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

        // Always stop the beat timer during calibration so the beat state stops cycling
        // and enemies remain still. For mid-game, also freeze scaled time.
        _bt.PauseTrack();
        if (isMidGame) Time.timeScale = 0f;

        if (calibrationPanel != null) calibrationPanel.SetActive(true);
        UpdateUI();

        Debug.Log("[TapCalibration] Calibration started. Tap to the beat of your music.");
    }

    /// <summary>Record a tap. Call from the tap button or GameController context menu.</summary>
    public void OnTapInput()
    {
        if (State == CalibrationState.CountingDown) return;

        _tapTimes.Add(AudioSettings.dspTime);

        if (State == CalibrationState.Idle)
            State = CalibrationState.Collecting;

        if (_tapTimes.Count >= 2)
            ComputeAndUpdate();

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

        UpdateUI();
    }

    /// <summary>Apply the calibrated tempo and begin a 3-beat countdown before gameplay.</summary>
    public void ConfirmCalibration()
    {
        if (_snappedBeatInterval <= 0f)
        {
            Debug.LogWarning("[TapCalibration] Cannot confirm — no valid interval yet. Keep tapping.");
            return;
        }

        State          = CalibrationState.CountingDown;
        _countdownValue = 3;

        // Apply new tempo atomically
        _bt.SetTempo(_snappedBeatInterval, _bt.trackPitch);

        // Anchor phase to the last tap so the first beat fires one interval after it
        double lastTapDsp = _tapTimes[_tapTimes.Count - 1];
        _bt.SetPhase(lastTapDsp);

        // Unfreeze time if mid-game; inCalibration keeps enemies blocked until FinishCalibration
        if (_isMidGame)
            Time.timeScale = 1f;

        // Start the beat track (SetPhase sets trackStarted=true so FixedUpdate won't re-anchor)
        _bt.begin = true;

        // Subscribe for the countdown
        _bt.OnBeat += OnCountdownBeat;

        StartMetronome();
        UpdateUI();

        Debug.Log($"[TapCalibration] Confirmed {_currentBpm:F1} BPM ({_snappedBeatInterval * 1000f:F0} ms). Counting in…");
    }

    /// <summary>Discard calibration and restore the previous beat interval.</summary>
    public void CancelCalibration()
    {
        _bt.OnBeat -= OnCountdownBeat;
        StopMetronome();

        _bt.SetTempo(_savedBeatInterval, _bt.trackPitch);

        if (_isMidGame)
        {
            Time.timeScale = 1f;
            _bt.ResumeTrack();
        }

        _gc.inCalibration = false;
        State             = CalibrationState.Idle;

        if (calibrationPanel != null) calibrationPanel.SetActive(false);
        Debug.Log("[TapCalibration] Cancelled. Original tempo restored.");
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
        string label = _countdownValue > 0 ? _countdownValue.ToString() : "GO!";
        if (countdownText != null) countdownText.text = label;
        Debug.Log($"[TapCalibration] Countdown: {label}");

        PunchMetronome();

        if (_countdownValue <= 0)
        {
            _bt.OnBeat -= OnCountdownBeat;
            StopMetronome();
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

        // BPM snapping: clamp → half-tempo correction → round to nearest 0.5 BPM
        float rawBpm  = 60f / (float)rawInterval;
        rawBpm        = Mathf.Clamp(rawBpm, MinBpm, MaxBpm);
        if (rawBpm < HalfTempoCutoff) rawBpm *= 2f;                  // likely tapping on half-beats
        float snapped = Mathf.Round(rawBpm * 2f) / 2f;               // nearest 0.5 BPM
        snapped       = Mathf.Clamp(snapped, MinBpm, MaxBpm);

        _currentBpm          = snapped;
        _snappedBeatInterval = 60f / snapped;

        CheckAutoComplete();
    }

    private void CheckAutoComplete()
    {
        bool enoughTaps = _tapTimes.Count >= MinTaps;
        bool confident  = _currentSdMs < AutoCompleteSdMs;

        if (enoughTaps && confident)
        {
            State = CalibrationState.Ready;
            ConfirmCalibration(); // auto-confirm — no button press required
        }
        else if (State == CalibrationState.Ready)
            State = CalibrationState.Collecting; // confidence dropped (e.g. after undo)
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
                confidenceText.text = "Tap to the beat of your music";
            else if (State == CalibrationState.Ready)
                confidenceText.text = "Ready!";
            else if (_currentSdMs < 40f)
                confidenceText.text = "Almost ready — keep tapping";
            else
                confidenceText.text = "Keep tapping\u2026";
        }

        if (countdownText != null)
            countdownText.gameObject.SetActive(State == CalibrationState.CountingDown);
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
