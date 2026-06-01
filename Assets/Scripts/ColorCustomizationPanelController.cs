using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedurally builds a swatch grid inside the customization panel at runtime.
///
/// LAYOUT:
///   [Pkg A]  [Pkg B]  [Pkg C]   ← column header buttons (tap = apply whole package to regions)
///   [ ■ ]    [ ■ ]    [ ■ ]     ← Region 1 swatches (driven by packages)
///   [ ■ ]    [ ■ ]    [ ■ ]     ← Region 2 swatches (driven by packages)
///   [ ■ ]    [ ■ ]    [ ■ ]     ← Region 3 swatches (driven by packages)
///   [ ■ ]    [ ■ ]    [ ■ ]     ← Button color swatches (driven by buttonColors[], independent of packages)
///
/// Tapping a column header applies that package's colors to the 3 region rows only.
/// Tapping an individual swatch overrides only that row.
/// Button colors are a flat independent list; selecting one overrides the button row only.
/// Every selection is applied immediately — no Save button needed.
/// Colors survive sessions via ColorCustomizationManager → PlayerPrefs.
///
/// REQUIRED INSPECTOR SETUP:
///   packages[]              — assign ColorPackage ScriptableObject assets (3 region colors each)
///   buttonColors[]          — flat list of Color values for the button color row
///   columnButtonContainer   — HorizontalLayoutGroup for the header row
///   rowContainers[0..3]     — HorizontalLayoutGroup for each color row
///   swatchTilePrefab        — Image + Button on root; optional child Image "SelectionIndicator"
///   columnButtonPrefab      — Button + TMP_Text on root
/// </summary>
public class ColorCustomizationPanelController : MonoBehaviour
{
    [Header("Packages")]
    [Tooltip("Each ColorPackage asset becomes one column of swatches for the 3 region rows.")]
    [SerializeField] private ColorPackage[] packages;

    [Tooltip("Flat list of colors that populate the button color row (independent of packages).")]
    [SerializeField] private Color[] buttonColors = new Color[0];

    [Header("Containers — build these in the Editor")]
    [Tooltip("HorizontalLayoutGroup that receives one column-header button per package.")]
    [SerializeField] private Transform columnButtonContainer;

    [Tooltip("Four HorizontalLayoutGroup containers: index 0=Region1, 1=Region2, 2=Region3, 3=Buttons.")]
    [SerializeField] private Transform[] rowContainers = new Transform[4];

    [Header("Prefabs")]
    [Tooltip("Swatch tile prefab. Root needs Image + Button. " +
             "Add an optional child Image named 'SelectionIndicator' for a visible outline when selected.")]
    [SerializeField] private GameObject swatchTilePrefab;

    [Tooltip("Column header button prefab. Root needs Button + TMP_Text child.")]
    [SerializeField] private GameObject columnButtonPrefab;

    [Header("Highlight")]
    [Tooltip("Scale factor applied to the selected swatch tile.")]
    [SerializeField] private float selectedScale = 1.15f;

    // [row][col] → the swatch Image for that cell
    private List<List<Image>> _tileImages = new List<List<Image>>();

    // Which column is currently selected per row. -1 = none.
    private int[] _selectedCol = { -1, -1, -1, -1 };

    // The last applied color per row; used to recombine when only one row changes.
    private Color[] _appliedColors = new Color[4];

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        BuildGrid();
    }

    private void OnEnable()
    {
        // Skip before BuildGrid has run (panel starts inactive in most setups).
        if (_tileImages.Count == 0) return;
        if (ColorCustomizationManager.Instance == null) return;

        SyncAppliedColorsFromManager();
        TrySyncSelectedCols();
        RefreshHighlights();
    }

    // ── Grid Construction ─────────────────────────────────────────────────────

    private void BuildGrid()
    {
        if (packages == null || packages.Length == 0)
        {
            Debug.LogWarning("[ColorCustomizationPanelController] No ColorPackage assets assigned.");
            return;
        }

        if (rowContainers == null || rowContainers.Length < 4)
        {
            Debug.LogError("[ColorCustomizationPanelController] Assign all 4 rowContainers in the Inspector.");
            return;
        }

        _tileImages.Clear();
        for (int r = 0; r < 4; r++)
            _tileImages.Add(new List<Image>());

        // --- Package columns: populate region rows 0-2 only ---
        for (int col = 0; col < packages.Length; col++)
        {
            ColorPackage pkg = packages[col];
            if (pkg == null) continue;

            int capturedCol = col; // closure capture

            // Column header button
            if (columnButtonContainer != null && columnButtonPrefab != null)
            {
                GameObject colBtn = Instantiate(columnButtonPrefab, columnButtonContainer);
                TMP_Text label = colBtn.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = pkg.packageName;

                Button btn = colBtn.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => OnColumnButtonClicked(capturedCol));
            }

            // Swatch tiles for region rows 0-2
            Color[] pkgColors = PackageToColorArray(pkg);
            for (int row = 0; row < 3; row++)
            {
                if (rowContainers[row] == null) continue;

                int capturedRow = row; // closure capture

                GameObject tile = Instantiate(swatchTilePrefab, rowContainers[row]);
                Image img = tile.GetComponent<Image>();
                if (img != null) img.color = pkgColors[row];
                _tileImages[row].Add(img);

                Button btn = tile.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => OnSwatchClicked(capturedRow, capturedCol));
            }
        }

        // --- Button color row (row 3) — independent flat list ---
        if (buttonColors != null && rowContainers[3] != null)
        {
            for (int col = 0; col < buttonColors.Length; col++)
            {
                int capturedCol = col;

                GameObject tile = Instantiate(swatchTilePrefab, rowContainers[3]);
                Image img = tile.GetComponent<Image>();
                if (img != null) img.color = buttonColors[col];
                _tileImages[3].Add(img);

                Button btn = tile.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => OnButtonSwatchClicked(capturedCol));
            }
        }

        // Sync highlights with whatever colors are currently applied.
        if (ColorCustomizationManager.Instance != null)
            SyncAppliedColorsFromManager();

        TrySyncSelectedCols();
        RefreshHighlights();
    }

    // ── Click Handlers ────────────────────────────────────────────────────────

    /// <summary>Applies the whole column package to the three region rows (not the button row).</summary>
    private void OnColumnButtonClicked(int col)
    {
        for (int row = 0; row < 3; row++)
            SelectSwatch(row, col);

        ColorCustomizationManager.Instance.ApplyConsoleColors(
            _appliedColors[0], _appliedColors[1], _appliedColors[2]);
        RefreshHighlights();
    }

    /// <summary>Applies a single region swatch (rows 0-2) to its row only.</summary>
    private void OnSwatchClicked(int row, int col)
    {
        SelectSwatch(row, col);
        ColorCustomizationManager.Instance.ApplyConsoleColors(
            _appliedColors[0], _appliedColors[1], _appliedColors[2]);
        RefreshHighlights();
    }

    /// <summary>Applies a button color swatch (row 3) independently of packages.</summary>
    private void OnButtonSwatchClicked(int col)
    {
        _selectedCol[3] = col;
        _appliedColors[3] = buttonColors[col];
        ColorCustomizationManager.Instance.ApplyButtonColor(_appliedColors[3]);
        RefreshHighlights();
    }

    // ── Selection + Apply Logic ───────────────────────────────────────────────

    private void SelectSwatch(int row, int col)
    {
        _selectedCol[row] = col;
        _appliedColors[row] = PackageToColorArray(packages[col])[row];
    }

    // ── Highlight Refresh ─────────────────────────────────────────────────────

    private void RefreshHighlights()
    {
        for (int row = 0; row < _tileImages.Count; row++)
        {
            for (int col = 0; col < _tileImages[row].Count; col++)
            {
                Image img = _tileImages[row][col];
                if (img == null) continue;

                bool isSelected = _selectedCol[row] == col;

                // Scale the tile up slightly when selected.
                img.transform.localScale = Vector3.one * (isSelected ? selectedScale : 1f);

                // If the prefab has a child Image named "SelectionIndicator", toggle it.
                Transform indicator = img.transform.Find("SelectionIndicator");
                if (indicator != null && indicator.TryGetComponent<Image>(out Image indicatorImg))
                    indicatorImg.enabled = isSelected;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SyncAppliedColorsFromManager()
    {
        _appliedColors[0] = ColorCustomizationManager.Instance.RegionColor1;
        _appliedColors[1] = ColorCustomizationManager.Instance.RegionColor2;
        _appliedColors[2] = ColorCustomizationManager.Instance.RegionColor3;
        _appliedColors[3] = ColorCustomizationManager.Instance.ButtonColor;
    }

    /// <summary>
    /// After loading saved colors, tries to find which column in the packages array
    /// matches each row's current applied color, so highlight state is restored
    /// correctly when the panel is reopened.
    /// </summary>
    private void TrySyncSelectedCols()
    {
        // Sync region rows 0-2 against packages
        if (packages != null)
        {
            for (int row = 0; row < 3; row++)
            {
                _selectedCol[row] = -1;
                for (int col = 0; col < packages.Length; col++)
                {
                    if (packages[col] == null) continue;
                    if (ColorsApproxEqual(PackageToColorArray(packages[col])[row], _appliedColors[row]))
                    {
                        _selectedCol[row] = col;
                        break;
                    }
                }
            }
        }

        // Sync button row (row 3) against the independent buttonColors list
        _selectedCol[3] = -1;
        if (buttonColors != null)
        {
            for (int col = 0; col < buttonColors.Length; col++)
            {
                if (ColorsApproxEqual(buttonColors[col], _appliedColors[3]))
                {
                    _selectedCol[3] = col;
                    break;
                }
            }
        }
    }

    private static Color[] PackageToColorArray(ColorPackage pkg) => new[]
    {
        pkg.region1Color,
        pkg.region2Color,
        pkg.region3Color,
    };

    private static bool ColorsApproxEqual(Color a, Color b, float threshold = 0.01f) =>
        Mathf.Abs(a.r - b.r) < threshold &&
        Mathf.Abs(a.g - b.g) < threshold &&
        Mathf.Abs(a.b - b.b) < threshold;
}
