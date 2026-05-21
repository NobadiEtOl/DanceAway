using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a Canvas UI speaker GameObject. On every beat, spawns a UI Image
/// that floats upward while oscillating sideways (wave), fades out, and is
/// automatically destroyed.
///
/// Spawned notes are named "SpeakerNote" and are parented directly under the
/// root Canvas so they always render on top of other UI.
///
/// Configuration:
///   noteSprites              — Pool of sprites; one is picked at random on each beat.
///   noteSize                 — Width and height of each note in canvas units.
///   startColor               — Tint + starting alpha for each note.
///   spawnOffset              — Offset in canvas units from the speaker where notes first appear.
///   spawnRadiusScreenFraction — Radius of the spawn circle as a fraction of screen height.
///   minNotesPerBeat          — Minimum notes spawned per beat.
///   maxNotesPerBeat          — Maximum notes spawned per beat.
///   riseScreenFraction       — Travel distance along each note's radial line, as a fraction of screen height.
///   waveScreenFraction       — Sine-wave oscillation perpendicular to travel line, as a fraction of screen width.
///   waveFrequency            — Oscillation cycles per second.
///   lifetime                 — Seconds from spawn to destruction.
///   holdFraction             — Fraction of lifetime at full opacity before the fade starts.
///   maxSimultaneous          — Hard cap on live notes at one time (0 = unlimited).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SpeakerBeatEmitter : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector configuration
    // -------------------------------------------------------------------------

    [Header("Note Appearance")]
    [Tooltip("Pool of sprites to choose from randomly on each beat. Assign one or more.")]
    [SerializeField] private Sprite[] noteSprites;

    [Tooltip("Width and height of each spawned note in canvas units.")]
    [SerializeField] private float noteSize = 60f;

    [Tooltip("Tint colour applied to every spawned note. Alpha sets the starting opacity.")]
    [SerializeField] private Color startColor = Color.white;

    // -------------------------------------------------------------------------

    [Header("Spawn Position")]
    [Tooltip("Centre of the spawn circle, offset in canvas units from the speaker's position.\n" +
             "(0, 0) = centred on the speaker. Positive Y shifts the circle upward.")]
    [SerializeField] private Vector2 spawnOffset = new Vector2(0f, 0f);

    [Tooltip("Radius of the circle around the spawn centre from which notes launch.\n" +
             "Expressed as a fraction of screen height. 0.05 = 5 % of screen height.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float spawnRadiusScreenFraction = 0.05f;

    // -------------------------------------------------------------------------

    [Header("Spawn Count")]
    [Tooltip("Minimum number of notes spawned on each beat.")]
    [SerializeField] private int minNotesPerBeat = 1;

    [Tooltip("Maximum number of notes spawned on each beat.")]
    [SerializeField] private int maxNotesPerBeat = 3;

    // -------------------------------------------------------------------------

    [Header("Movement")]
    [Tooltip("How far each note travels along its radial line, as a fraction of screen height.\n" +
             "The line runs from the speaker centre through each note's spawn point.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float riseScreenFraction = 0.10f;

    [Tooltip("Sine-wave oscillation amplitude perpendicular to the travel line, as a fraction of screen width.\n" +
             "0.02 = 2 % of screen width. Set to 0 for perfectly straight radial paths.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float waveScreenFraction = 0.02f;

    [Tooltip("Horizontal oscillation cycles per second.")]
    [SerializeField] private float waveFrequency = 1.5f;

    // -------------------------------------------------------------------------

    [Header("Timing & Fade")]
    [Tooltip("Seconds from spawn until the note is fully transparent and destroyed.")]
    [SerializeField] private float lifetime = 0.8f;

    [Tooltip("Fraction of the lifetime the note stays fully opaque before fading begins.\n" +
             "0 = fade immediately. 0.15 = hold for 15 % of the lifetime, then fade.")]
    [Range(0f, 0.9f)]
    [SerializeField] private float holdFraction = 0.15f;

    // -------------------------------------------------------------------------

    [Header("Limits")]
    [Tooltip("Maximum number of notes alive simultaneously. 0 = unlimited.")]
    [SerializeField] private int maxSimultaneous = 8;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private Canvas rootCanvas;
    private int    activeCount;
    private bool   subscribed;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Resolve the root Canvas as early as possible so it is ready before
        // any beat events could fire.
        Canvas c = GetComponentInParent<Canvas>();
        if (c != null) rootCanvas = c.rootCanvas;
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        // Second attempt: beatTimer may not have been assigned yet during OnEnable.
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (subscribed && GameController.beatTimer != null)
            GameController.beatTimer.OnBeat -= HandleBeat;
        subscribed = false;
    }

    private void TrySubscribe()
    {
        if (subscribed) return;
        if (GameController.beatTimer == null) return;
        GameController.beatTimer.OnBeat += HandleBeat;
        subscribed = true;
    }

    // -------------------------------------------------------------------------
    // Beat handler
    // -------------------------------------------------------------------------

    private void HandleBeat()
    {
        BeatTimer bt = GameController.beatTimer;
        if (bt == null || !bt.begin) return;
        if (noteSprites == null || noteSprites.Length == 0) return;

        float scaleFactor            = (rootCanvas != null && rootCanvas.scaleFactor > 0f) ? rootCanvas.scaleFactor : 1f;
        float spawnRadiusCanvasUnits = Screen.height * spawnRadiusScreenFraction / scaleFactor;

        int count = Random.Range(minNotesPerBeat, maxNotesPerBeat + 1);

        // Stratified angle sampling: divide the full 360° into 'count' equal sectors
        // and pick one random angle within each sector. This guarantees notes fan out
        // evenly in all directions instead of clumping together.
        float sectorDeg = 360f / Mathf.Max(count, 1);
        for (int i = 0; i < count; i++)
        {
            if (maxSimultaneous > 0 && activeCount >= maxSimultaneous) break;

            Sprite chosen = noteSprites[Random.Range(0, noteSprites.Length)];
            if (chosen == null) continue;

            float angle = i * sectorDeg + Random.Range(0f, sectorDeg);
            StartCoroutine(AnimateNote(chosen, angle, spawnRadiusCanvasUnits));
        }
    }

    // -------------------------------------------------------------------------
    // Per-note animation
    // -------------------------------------------------------------------------

    private IEnumerator AnimateNote(Sprite sprite, float angleDeg, float spawnRadiusCanvasUnits)
    {
        if (rootCanvas == null) yield break;

        activeCount++;

        // Travel direction derived from angle; perpendicular used for wave oscillation.
        float   angleRad  = angleDeg * Mathf.Deg2Rad;
        Vector2 travelDir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        Vector2 perpDir   = new Vector2(-travelDir.y, travelDir.x);

        // --- Create UI Image note ---
        var note = new GameObject("SpeakerNote", typeof(RectTransform), typeof(Image));

        // Parent to the root Canvas so notes render on top of all other UI.
        note.transform.SetParent(rootCanvas.transform, false);
        note.transform.SetAsLastSibling();

        RectTransform noteRt = note.GetComponent<RectTransform>();
        noteRt.anchorMin = noteRt.anchorMax = noteRt.pivot = new Vector2(0.5f, 0.5f);
        noteRt.sizeDelta = new Vector2(noteSize, noteSize);

        // Place note on the spawn circle: speaker position + circle centre offset + radial offset.
        noteRt.position         = transform.position;
        noteRt.anchoredPosition += spawnOffset + travelDir * spawnRadiusCanvasUnits;

        Image img = note.GetComponent<Image>();
        img.sprite        = sprite;
        img.color         = startColor;
        img.raycastTarget = false;

        // --- Compute movement in canvas units (resolution-independent) ---
        float scaleFactor       = (rootCanvas.scaleFactor > 0f) ? rootCanvas.scaleFactor : 1f;
        float travelCanvasUnits = Screen.height * riseScreenFraction / scaleFactor;
        float waveCanvasUnits   = Screen.width  * waveScreenFraction / scaleFactor;

        Vector2 startAnchoredPos = noteRt.anchoredPosition;
        float   holdTime         = lifetime * holdFraction;
        float   fadeTime         = lifetime - holdTime;
        float   elapsed          = 0f;

        // --- Animate ---
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);

            // Travel along the radial line
            float travel = t * travelCanvasUnits;

            // Sine wave perpendicular to the travel line
            float wave = Mathf.Sin(elapsed * waveFrequency * 2f * Mathf.PI) * waveCanvasUnits;

            noteRt.anchoredPosition = startAnchoredPos + travelDir * travel + perpDir * wave;

            // Alpha: hold then fade
            float alpha;
            if (elapsed < holdTime)
            {
                alpha = startColor.a;
            }
            else
            {
                float fadeProgress = (elapsed - holdTime) / fadeTime;
                alpha = Mathf.Lerp(startColor.a, 0f, fadeProgress);
            }

            Color c = startColor;
            c.a       = alpha;
            img.color = c;

            yield return null;
        }

        activeCount--;
        Destroy(note);
    }
}
