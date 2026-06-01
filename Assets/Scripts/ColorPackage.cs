using UnityEngine;

/// <summary>
/// A color package is a named preset that assigns one color to each of the three
/// customizable console regions: Region 1, Region 2, Region 3.
///
/// Button colors are managed separately in ColorCustomizationPanelController.buttonColors[].
///
/// Create assets via: right-click in Project → Create → DanceAway/Color Package
/// Assign as many packages as you like to ColorCustomizationPanelController.packages[].
/// </summary>
[CreateAssetMenu(fileName = "ColorPackage", menuName = "DanceAway/Color Package")]
public class ColorPackage : ScriptableObject
{
    [Tooltip("Display name shown on the column header button in the customization panel.")]
    public string packageName = "Package";

    [Tooltip("Color applied to console mask region 1 (_Color1).")]
    public Color region1Color = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Tooltip("Color applied to console mask region 2 (_Color2).")]
    public Color region2Color = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Tooltip("Color applied to console mask region 3 (_Color3).")]
    public Color region3Color = new Color(0.6f, 0.6f, 0.6f, 1f);
}
