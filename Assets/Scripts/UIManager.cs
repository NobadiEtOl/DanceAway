using System.Collections.Generic;
using Common.Enums;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [Header("Movement Mode — Main Buttons")]
    [SerializeField] private GameController              gameController;
    [SerializeField] private ControlPlacementController controlPlacementController;
    [SerializeField] private Button swipeModeButton;
    [SerializeField] private Button arrowKeysModeButton;
    [SerializeField] private Button joystickBeatModeButton;

    [Header("Movement Mode — Info Windows")]
    [SerializeField] private GameObject swipeInfoWindow;
    [SerializeField] private GameObject arrowKeysInfoWindow;
    [SerializeField] private GameObject joystickBeatInfoWindow;

    [Header("Movement Mode — Window Buttons")]
    [SerializeField] private Button swipeAcceptButton;
    [SerializeField] private Button swipeReturnButton;
    [SerializeField] private Button arrowKeysAcceptButton;
    [SerializeField] private Button arrowKeysReturnButton;
    [SerializeField] private Button joystickBeatAcceptButton;
    [SerializeField] private Button joystickBeatReturnButton;
    [SerializeField] private Button joystickFlipButton;

    void Start()
    {
        // Wire main mode buttons
        if (swipeModeButton        != null) swipeModeButton.onClick.AddListener(OnSwipeModePressed);
        if (arrowKeysModeButton    != null) arrowKeysModeButton.onClick.AddListener(OnArrowKeysModePressed);
        if (joystickBeatModeButton != null) joystickBeatModeButton.onClick.AddListener(OnJoystickBeatModePressed);

        // Wire window buttons
        if (swipeAcceptButton        != null) swipeAcceptButton.onClick.AddListener(OnSwipeAccept);
        if (swipeReturnButton        != null) swipeReturnButton.onClick.AddListener(OnSwipeReturn);
        if (arrowKeysAcceptButton    != null) arrowKeysAcceptButton.onClick.AddListener(OnArrowKeysAccept);
        if (arrowKeysReturnButton    != null) arrowKeysReturnButton.onClick.AddListener(OnArrowKeysReturn);
        if (joystickBeatAcceptButton != null) joystickBeatAcceptButton.onClick.AddListener(OnJoystickBeatAccept);
        if (joystickBeatReturnButton != null) joystickBeatReturnButton.onClick.AddListener(OnJoystickBeatReturn);
        if (joystickFlipButton       != null) joystickFlipButton.onClick.AddListener(OnJoystickFlipPressed);

        // Ensure info windows start closed
        if (swipeInfoWindow        != null) swipeInfoWindow.SetActive(false);
        if (arrowKeysInfoWindow    != null) arrowKeysInfoWindow.SetActive(false);
        if (joystickBeatInfoWindow != null) joystickBeatInfoWindow.SetActive(false);

        // Sync button interactable state with saved mode — read from PlayerPrefs directly
        // so Start() ordering between UIManager and GameController doesn't matter.
        MovementMode savedMode = (MovementMode)PlayerPrefs.GetInt("MovementMode", (int)MovementMode.ArrowKeys);
        RefreshMainButtonVisuals(savedMode);
        // Flip button intentionally not touched here — it keeps its Editor-assigned color.
    }

    void Update()
    {
        // Outside-click to close: only run when at least one info window is open.
        bool swipeOpen        = swipeInfoWindow        != null && swipeInfoWindow.activeSelf;
        bool arrowKeysOpen    = arrowKeysInfoWindow    != null && arrowKeysInfoWindow.activeSelf;
        bool joystickBeatOpen = joystickBeatInfoWindow != null && joystickBeatInfoWindow.activeSelf;
        if (!swipeOpen && !arrowKeysOpen && !joystickBeatOpen) return;

        bool pointerDown = Input.GetMouseButtonDown(0) ||
                           (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        if (!pointerDown) return;

        // Raycast all UI elements under the pointer position.
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = Input.touchCount > 0
            ? (Vector2)Input.GetTouch(0).position
            : (Vector2)Input.mousePosition;

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        // If the tap hit anything inside an open window, keep the window open.
        foreach (RaycastResult result in results)
        {
            if (swipeOpen        && IsChildOf(result.gameObject, swipeInfoWindow))        return;
            if (arrowKeysOpen    && IsChildOf(result.gameObject, arrowKeysInfoWindow))    return;
            if (joystickBeatOpen && IsChildOf(result.gameObject, joystickBeatInfoWindow)) return;
        }

        // If the tap landed on a mode-selection button, let that button's onClick handle
        // the transition directly — prevents a flash of the default prefab between windows.
        foreach (RaycastResult result in results)
        {
            if (swipeModeButton        != null && IsChildOf(result.gameObject, swipeModeButton.gameObject))        return;
            if (arrowKeysModeButton    != null && IsChildOf(result.gameObject, arrowKeysModeButton.gameObject))    return;
            if (joystickBeatModeButton != null && IsChildOf(result.gameObject, joystickBeatModeButton.gameObject)) return;
        }

        // Tap landed outside all open windows — close them.
        CloseAllWindows();
    }

    // -------------------------------------------------------------------------
    // Main mode button handlers
    // -------------------------------------------------------------------------

    private void OnSwipeModePressed()
    {
        if (arrowKeysInfoWindow    != null) arrowKeysInfoWindow.SetActive(false);
        if (joystickBeatInfoWindow != null) joystickBeatInfoWindow.SetActive(false);
        if (swipeInfoWindow        != null) swipeInfoWindow.SetActive(true);
        controlPlacementController?.PreviewMode(MovementMode.Swipe);
    }

    private void OnArrowKeysModePressed()
    {
        if (swipeInfoWindow        != null) swipeInfoWindow.SetActive(false);
        if (joystickBeatInfoWindow != null) joystickBeatInfoWindow.SetActive(false);
        if (arrowKeysInfoWindow    != null) arrowKeysInfoWindow.SetActive(true);
        controlPlacementController?.PreviewMode(MovementMode.ArrowKeys);
    }

    private void OnJoystickBeatModePressed()
    {
        if (swipeInfoWindow     != null) swipeInfoWindow.SetActive(false);
        if (arrowKeysInfoWindow != null) arrowKeysInfoWindow.SetActive(false);
        if (joystickBeatInfoWindow != null) joystickBeatInfoWindow.SetActive(true);
        controlPlacementController?.PreviewMode(MovementMode.JoystickBeat);
    }

    // -------------------------------------------------------------------------
    // Accept / Return handlers
    // -------------------------------------------------------------------------

    private void OnSwipeAccept()
    {
        gameController.SetMovementMode(MovementMode.Swipe);
        controlPlacementController?.UpdateCurrentMode(MovementMode.Swipe);
        if (swipeInfoWindow != null) swipeInfoWindow.SetActive(false);
        RefreshMainButtonVisuals();
        RestoreOrHideControls();
    }

    private void OnSwipeReturn()
    {
        if (swipeInfoWindow != null) swipeInfoWindow.SetActive(false);
        RestoreOrHideControls();
    }

    private void OnArrowKeysAccept()
    {
        gameController.SetMovementMode(MovementMode.ArrowKeys);
        controlPlacementController?.UpdateCurrentMode(MovementMode.ArrowKeys);
        if (arrowKeysInfoWindow != null) arrowKeysInfoWindow.SetActive(false);
        RefreshMainButtonVisuals();
        RestoreOrHideControls();
    }

    private void OnArrowKeysReturn()
    {
        if (arrowKeysInfoWindow != null) arrowKeysInfoWindow.SetActive(false);
        RestoreOrHideControls();
    }

    private void OnJoystickBeatAccept()
    {
        gameController.SetMovementMode(MovementMode.JoystickBeat);
        controlPlacementController?.UpdateCurrentMode(MovementMode.JoystickBeat);
        if (joystickBeatInfoWindow != null) joystickBeatInfoWindow.SetActive(false);
        RefreshMainButtonVisuals();
        RestoreOrHideControls();
    }

    private void OnJoystickBeatReturn()
    {
        if (joystickBeatInfoWindow != null) joystickBeatInfoWindow.SetActive(false);
        RestoreOrHideControls();
    }

    private void OnJoystickFlipPressed()
    {
        if (controlPlacementController == null) return;
        bool newFlip = !controlPlacementController.GetFlipJoystickSides();
        controlPlacementController.SetFlipJoystickSides(newFlip);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void CloseAllWindows()
    {
        if (swipeInfoWindow        != null) swipeInfoWindow.SetActive(false);
        if (arrowKeysInfoWindow    != null) arrowKeysInfoWindow.SetActive(false);
        if (joystickBeatInfoWindow != null) joystickBeatInfoWindow.SetActive(false);
        RestoreOrHideControls();
    }

    /// <summary>
    /// Restores the active control prefab when gameplay is running, or hides controls
    /// entirely when the game has not yet started (controls only appear with an info window
    /// or once gameplay begins).
    /// </summary>
    private void RestoreOrHideControls()
    {
        if (gameController != null && gameController.canStart)
            controlPlacementController?.RestoreCurrentMode();
        else
            controlPlacementController?.HideControls();
    }

    private void RefreshMainButtonVisuals()
    {
        RefreshMainButtonVisuals(gameController.GetMovementMode());
    }

    private void RefreshMainButtonVisuals(MovementMode mode)
    {
        SetButtonSelectedVisual(swipeModeButton,        mode == MovementMode.Swipe);
        SetButtonSelectedVisual(arrowKeysModeButton,    mode == MovementMode.ArrowKeys);
        SetButtonSelectedVisual(joystickBeatModeButton, mode == MovementMode.JoystickBeat);
    }

    /// <summary>
    /// Keeps the button always interactable (so it can be clicked), but applies the
    /// disabled color tint when <paramref name="isSelected"/> is true so it looks inactive.
    /// </summary>
    private void SetButtonSelectedVisual(Button btn, bool isSelected)
    {
        if (btn == null) return;
        btn.interactable = true;
        if (btn.targetGraphic != null)
            btn.targetGraphic.color = isSelected ? btn.colors.disabledColor : btn.colors.normalColor;
    }

    /// <summary>Walks the Transform hierarchy to check whether obj is obj or a descendant of parent.</summary>
    private bool IsChildOf(GameObject obj, GameObject parent)
    {
        Transform t = obj.transform;
        while (t != null)
        {
            if (t.gameObject == parent) return true;
            t = t.parent;
        }
        return false;
    }
}
