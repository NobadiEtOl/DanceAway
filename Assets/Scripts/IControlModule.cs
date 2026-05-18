/// <summary>
/// Implemented by every control-mode MonoBehaviour that lives on its mode's prefab root.
///
/// ControlPlacementController calls <see cref="Initialize"/> synchronously right after
/// instantiation and FitToZone, injecting the scene-level GameController before the
/// first frame's Update runs on the new object.
///
/// Implementors:
///   ArrowKeysController    — ArrowKeysControl.prefab root
///   SwipeController        — SwipeControl.prefab root
///   JoystickBeatController — JoystickBeatControl.prefab root
/// </summary>
public interface IControlModule
{
    /// <summary>
    /// Inject the scene-level <see cref="GameController"/> (and any dependencies
    /// it exposes, such as Player) into the freshly instantiated control prefab.
    /// Called before Start() runs on the new object.
    /// </summary>
    void Initialize(GameController gc);
}
