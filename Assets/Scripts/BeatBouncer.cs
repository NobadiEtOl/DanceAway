using UnityEngine;

/// <summary>
/// Attach to any RectTransform UI element to animate it vertically in sync with the beat.
///
/// Behaviour:
///   • PerfectBeat (distFromBeat = 0)      → element sits at its original authored position.
///   • Midpoint between beats (OffBeat)    → element is displaced upward by the full amplitude.
///   • The transition follows a power curve so the landing on the beat is sharp and snappy,
///     while the rise away from the beat is gentle — or vice-versa, depending on the exponent.
///
/// Amplitude is expressed as a fraction of the physical screen height so it scales
/// consistently across all portrait-mode resolutions.
///
/// Power curve shapes (exponent field):
///   exponent < 1  (e.g. 0.5) → fast departure, slow final approach  (snappy landing)
///   exponent = 1              → linear
///   exponent > 1  (e.g. 2.0) → slow departure, fast final approach  (heavy drop)
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BeatBouncer : MonoBehaviour
{
    [Tooltip("Amplitude as a fraction of screen height. 0.03 = 3 % of screen height.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float amplitudeScreenFraction = 0.03f;

    [Tooltip("Power-curve exponent that shapes the bounce.\n" +
             "< 1 : snappy landing (fast rise, slow descent to beat).\n" +
             "> 1 : heavy drop    (slow rise, fast descent to beat).\n" +
             "  1 : linear.")]
    [Range(0f, 1f)]
    private float exponent = 0.6f;

    [Tooltip("Direction the element travels away from the beat. Default is up (+Y).")]
    [SerializeField] private Vector2 bounceDirection = Vector2.up;

    // -------------------------------------------------------------------------

    private RectTransform rt;
    private Vector2       originalAnchoredPosition;
    private Canvas        rootCanvas;

    // -------------------------------------------------------------------------

    private void Start()
    {
        rt = GetComponent<RectTransform>();
        originalAnchoredPosition = rt.anchoredPosition;

        // Walk up the hierarchy to the root Canvas so we can read its scaleFactor.
        Canvas c = GetComponentInParent<Canvas>();
        if (c != null) rootCanvas = c.rootCanvas;
    }

    private void FixedUpdate()
    {
        // GameController.beatTimer is the authoritative static reference used throughout the project.
        BeatTimer beatTimer = GameController.beatTimer;
        if (beatTimer == null || !beatTimer.begin) return;

        // normalizedPhase: 0 = exactly on the beat, 1 = furthest from any beat.
        float normalizedPhase = beatTimer.GetNormalizedBeatPhase();

        // Apply power curve — shapes how aggressively the element moves.
        float offset01 = Mathf.Pow(normalizedPhase, exponent);

        float amplitude = ComputeAmplitudeInCanvasUnits();
        rt.anchoredPosition = originalAnchoredPosition + bounceDirection.normalized * (offset01 * amplitude);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts the desired screen-space pixel amplitude into canvas local units,
    /// respecting the root Canvas's scaleFactor (set by Canvas Scaler).
    /// </summary>
    private float ComputeAmplitudeInCanvasUnits()
    {
        float pixelAmplitude = Screen.height * amplitudeScreenFraction;
        float scaleFactor    = (rootCanvas != null && rootCanvas.scaleFactor > 0f)
                               ? rootCanvas.scaleFactor
                               : 1f;
        return pixelAmplitude / scaleFactor;
    }

    /// <summary>
    /// Call this if the element is repositioned at runtime and the new position
    /// should become the new rest position.
    /// </summary>
    public void RefreshOriginalPosition()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        originalAnchoredPosition = rt.anchoredPosition;
    }
}
