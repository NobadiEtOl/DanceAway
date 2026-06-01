using UnityEngine;

/// <summary>
/// Singleton that owns the console and button colors, applies them to shared
/// materials, and persists them across sessions via PlayerPrefs.
///
/// Attach this component to any persistent GameObject (e.g. GameController).
/// In the Inspector, assign TopConsoleMat and BottomConsoleMat — the two
/// Material assets that use the Custom/ColorRegionSwap shader.
///
/// For button tinting, assign ButtonSharedMat (rectangular buttons) and
/// TriangleLeftButtonMat / TriangleRightButtonMat (triangle buttons). All three
/// receive the same global button color.
///
/// LIVE PREVIEW: change any of the three Region Color fields in the Inspector
/// at any time (Edit or Play mode) to see the result immediately on screen.
/// Right-click the component header → "Apply Preview as Saved Colors" to persist.
///
/// Call ApplyConsoleColors() from the settings screen whenever the player
/// picks new color swatches. Call ApplyButtonColor() for global button tint.
/// </summary>
public class ColorCustomizationManager : MonoBehaviour
{
    public static ColorCustomizationManager Instance { get; private set; }

    // Read-only accessors so the customization panel can read the currently applied colors.
    public Color RegionColor1 => regionColor1;
    public Color RegionColor2 => regionColor2;
    public Color RegionColor3 => regionColor3;
    public Color ButtonColor  => buttonColor;

    [Header("Console Materials")]
    [Tooltip("Material on the top-half console Image (uses Custom/ColorRegionSwap).")]
    [SerializeField] private Material topConsoleMat;

    [Tooltip("Material on the bottom-half console Image (uses Custom/ColorRegionSwap).")]
    [SerializeField] private Material bottomConsoleMat;

    [Tooltip("Shared material assigned to all bottom-half button Images (uses Custom/ColorRegionSwap).")]
    [SerializeField] private Material buttonSharedMat;

    [Tooltip("Material for the left triangle button (uses Custom/ColorRegionSwap).")]
    [SerializeField] private Material triangleLeftButtonMat;

    [Tooltip("Material for the right triangle button (uses Custom/ColorRegionSwap).")]
    [SerializeField] private Material triangleRightButtonMat;

    [Tooltip("Material for the joystick knob (uses Custom/ColorRegionSwap). Tinted with the same button color.")]
    [SerializeField] private Material knobMat;

    [Header("Live Preview — Region Colors")]
    [Tooltip("Color for mask region 1 (_Color1). Change live in the Inspector; does NOT auto-save to PlayerPrefs.")]
    [SerializeField] private Color regionColor1 = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Tooltip("Color for mask region 2 (_Color2). Change live in the Inspector; does NOT auto-save to PlayerPrefs.")]
    [SerializeField] private Color regionColor2 = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Tooltip("Color for mask region 3 (_Color3). Change live in the Inspector; does NOT auto-save to PlayerPrefs.")]
    [SerializeField] private Color regionColor3 = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Header("Live Preview - Button Color")]
    [Tooltip("Global button tint for mask region 1 (_Color1). Change live in the Inspector; does NOT auto-save to PlayerPrefs.")]
    [SerializeField] private Color buttonColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    // Cached shader property IDs — faster than string lookups.
    private static readonly int Color1Id = Shader.PropertyToID("_Color1");
    private static readonly int Color2Id = Shader.PropertyToID("_Color2");
    private static readonly int Color3Id = Shader.PropertyToID("_Color3");

    // PlayerPrefs keys — one set per region.
    private const string Pref1R = "ConsoleColor1_R"; private const string Pref1G = "ConsoleColor1_G"; private const string Pref1B = "ConsoleColor1_B";
    private const string Pref2R = "ConsoleColor2_R"; private const string Pref2G = "ConsoleColor2_G"; private const string Pref2B = "ConsoleColor2_B";
    private const string Pref3R = "ConsoleColor3_R"; private const string Pref3G = "ConsoleColor3_G"; private const string Pref3B = "ConsoleColor3_B";
    private const string PrefBtnR = "ButtonColor_R"; private const string PrefBtnG = "ButtonColor_G"; private const string PrefBtnB = "ButtonColor_B";

    // Called by Unity whenever any Inspector value changes (edit mode + play mode).
    private void OnValidate()
    {
        PushColorsToMaterials(regionColor1, regionColor2, regionColor3);
        PushButtonColorToMaterial(buttonColor);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Restore saved colors. If the player has never customised, the materials
        // keep whatever _Color1/2/3 values were set in the Inspector.
        if (PlayerPrefs.HasKey(Pref1R))
        {
            regionColor1 = LoadSavedColor(Pref1R, Pref1G, Pref1B);
            regionColor2 = LoadSavedColor(Pref2R, Pref2G, Pref2B);
            regionColor3 = LoadSavedColor(Pref3R, Pref3G, Pref3B);
            PushColorsToMaterials(regionColor1, regionColor2, regionColor3);
        }

        if (PlayerPrefs.HasKey(PrefBtnR))
        {
            buttonColor = LoadSavedColor(PrefBtnR, PrefBtnG, PrefBtnB);
            PushButtonColorToMaterial(buttonColor);
        }
    }

    /// <summary>
    /// Applies three independent region colors to both materials and saves them
    /// to PlayerPrefs. Call this from the settings screen color pickers.
    /// </summary>
    public void ApplyConsoleColors(Color c1, Color c2, Color c3)
    {
        regionColor1 = c1;
        regionColor2 = c2;
        regionColor3 = c3;
        PushColorsToMaterials(c1, c2, c3);

        PlayerPrefs.SetFloat(Pref1R, c1.r); PlayerPrefs.SetFloat(Pref1G, c1.g); PlayerPrefs.SetFloat(Pref1B, c1.b);
        PlayerPrefs.SetFloat(Pref2R, c2.r); PlayerPrefs.SetFloat(Pref2G, c2.g); PlayerPrefs.SetFloat(Pref2B, c2.b);
        PlayerPrefs.SetFloat(Pref3R, c3.r); PlayerPrefs.SetFloat(Pref3G, c3.g); PlayerPrefs.SetFloat(Pref3B, c3.b);
        PlayerPrefs.Save();
    }

    /// <summary>Convenience overload: applies the same color to all three regions.</summary>
    public void ApplyConsoleColor(Color color) => ApplyConsoleColors(color, color, color);

    /// <summary>
    /// Applies one global button tint color to the shared button material and
    /// persists it to PlayerPrefs.
    /// </summary>
    public void ApplyButtonColor(Color color)
    {
        buttonColor = color;
        PushButtonColorToMaterial(color);

        PlayerPrefs.SetFloat(PrefBtnR, color.r);
        PlayerPrefs.SetFloat(PrefBtnG, color.g);
        PlayerPrefs.SetFloat(PrefBtnB, color.b);
        PlayerPrefs.Save();
    }

    /// <summary>Pushes each color to its own region slot on both materials without touching PlayerPrefs.</summary>
    private void PushColorsToMaterials(Color c1, Color c2, Color c3)
    {
        if (topConsoleMat != null)
        {
            topConsoleMat.SetColor(Color1Id, c1);
            topConsoleMat.SetColor(Color2Id, c2);
            topConsoleMat.SetColor(Color3Id, c3);
        }
        if (bottomConsoleMat != null)
        {
            bottomConsoleMat.SetColor(Color1Id, c1);
            bottomConsoleMat.SetColor(Color2Id, c2);
            bottomConsoleMat.SetColor(Color3Id, c3);
        }
    }

    /// <summary>Pushes the global button tint to all button materials without touching PlayerPrefs.</summary>
    private void PushButtonColorToMaterial(Color color)
    {
        if (buttonSharedMat != null)
            buttonSharedMat.SetColor(Color1Id, color);
        if (triangleLeftButtonMat != null)
            triangleLeftButtonMat.SetColor(Color1Id, color);
        if (triangleRightButtonMat != null)
            triangleRightButtonMat.SetColor(Color1Id, color);
        if (knobMat != null)
            knobMat.SetColor(Color1Id, color);
    }

    /// <summary>Saves the current Inspector preview colors to PlayerPrefs.</summary>
    [ContextMenu("Apply Preview as Saved Colors")]
    public void SavePreviewColors()
    {
        ApplyConsoleColors(regionColor1, regionColor2, regionColor3);
        ApplyButtonColor(buttonColor);
        Debug.Log($"[ColorCustomizationManager] Saved - R1:{regionColor1}  R2:{regionColor2}  R3:{regionColor3}  Btn:{buttonColor}");
    }

    /// <summary>Clears all saved region colors from PlayerPrefs.</summary>
    [ContextMenu("Clear Saved Colors")]
    public void ClearSavedColors()
    {
        PlayerPrefs.DeleteKey(Pref1R); PlayerPrefs.DeleteKey(Pref1G); PlayerPrefs.DeleteKey(Pref1B);
        PlayerPrefs.DeleteKey(Pref2R); PlayerPrefs.DeleteKey(Pref2G); PlayerPrefs.DeleteKey(Pref2B);
        PlayerPrefs.DeleteKey(Pref3R); PlayerPrefs.DeleteKey(Pref3G); PlayerPrefs.DeleteKey(Pref3B);
        PlayerPrefs.DeleteKey(PrefBtnR); PlayerPrefs.DeleteKey(PrefBtnG); PlayerPrefs.DeleteKey(PrefBtnB);
        PlayerPrefs.Save();
        Debug.Log("[ColorCustomizationManager] Saved colors cleared.");
    }

    private Color LoadSavedColor(string keyR, string keyG, string keyB)
    {
        return new Color(
            PlayerPrefs.GetFloat(keyR, 0.6f),
            PlayerPrefs.GetFloat(keyG, 0.6f),
            PlayerPrefs.GetFloat(keyB, 0.6f)
        );
    }
}
