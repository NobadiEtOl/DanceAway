using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Beat-synced ripple effect for the Split Control beat-press zone.
/// Added dynamically by JoystickBeatController — call Initialize() immediately after AddComponent.
///
/// On each OnBeat event, spawns an expanding ring that fades out over rippleDuration seconds.
/// Multiple rings can be in-flight simultaneously (one per beat).
/// </summary>
public class BeatZonePulse : MonoBehaviour
{
    // All data provided via Initialize() — nothing serialized here.
    private Sprite rippleSprite;
    private Color  rippleColor;
    private float  rippleDiameter;
    private float  expandScale    = 2.5f;
    private float  startAlpha     = 0.6f;
    private float  rippleDuration = 0.35f;

    /// <summary>
    /// Must be called once after AddComponent, before the GameObject is first activated.
    /// </summary>
    public void Initialize(Sprite sprite, Color color, float diameter,
                           float expand, float alpha, float duration)
    {
        rippleSprite  = sprite;
        rippleColor   = color;
        rippleDiameter = diameter;
        expandScale   = expand;
        startAlpha    = alpha;
        rippleDuration = duration;
    }

    private void OnEnable()
    {
        if (GameController.beatTimer != null)
            GameController.beatTimer.OnBeat += OnBeat;
    }

    private void OnDisable()
    {
        if (GameController.beatTimer != null)
            GameController.beatTimer.OnBeat -= OnBeat;
    }

    private void OnBeat() => StartCoroutine(RippleCoroutine());

    private IEnumerator RippleCoroutine()
    {
        if (rippleSprite == null) yield break;

        // Spawn ring as sibling, rendered on top of the persistent zone indicator
        var ring = new GameObject("BeatRipple", typeof(RectTransform), typeof(Image));
        ring.transform.SetParent(transform.parent, false);
        ring.transform.SetSiblingIndex(transform.GetSiblingIndex() + 1);

        RectTransform rt  = ring.GetComponent<RectTransform>();
        Image         img = ring.GetComponent<Image>();

        rt.anchorMin  = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.position   = transform.position;   // same screen position as the zone indicator
        // When diameter is 0, derive the ring's starting size from this panel's own rect.
        RectTransform ownRt   = GetComponent<RectTransform>();
        Vector2       ownSize = (ownRt != null) ? ownRt.rect.size : new Vector2(100f, 100f);
        rt.sizeDelta = (rippleDiameter > 0f) ? new Vector2(rippleDiameter, rippleDiameter) : ownSize;
        rt.localScale = Vector3.one;

        img.sprite        = rippleSprite;
        img.type          = Image.Type.Simple;
        img.raycastTarget = false;

        Color c = rippleColor;
        c.a       = startAlpha;
        img.color = c;

        float elapsed = 0f;
        while (elapsed < rippleDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / rippleDuration);
            rt.localScale = Vector3.one * Mathf.Lerp(1f, expandScale, t);
            c.a       = Mathf.Lerp(startAlpha, 0f, t);
            img.color = c;
            yield return null;
        }

        Destroy(ring);
    }
}
