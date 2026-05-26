using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TutorialController : MonoBehaviour
{
    [SerializeField]private GameController gameController;
    [SerializeField]private GameObject tutorialScreen;
    [SerializeField]private GameObject firstPage;
    [SerializeField]private GameObject secondPage;
    [SerializeField]private GameObject thirdPage;
    [SerializeField]private GameObject fourthPage;
    [SerializeField]private GameObject score;
    [SerializeField]private GameObject startScreen;
    [SerializeField]private ConsoleBottomHalfController consoleBottomHalf;
    [SerializeField]private GameObject backButton;
    [SerializeField]private GameObject forwardButton;

    private int currentPage = 1;
    private const int totalPages = 4;

    // Start is called before the first frame update
    void Start()
    {
        if (gameController == null)
        {
            GameObject controller = GameObject.Find("GameController");
            if (controller != null)
            {
                gameController = controller.GetComponent<GameController>();
            }
        }

        this.gameObject.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OpenTutorial()
    {
        CloseStartScreen();
        tutorialScreen.SetActive(true);
        if (gameController != null)
        {
            gameController.OnTutorialWindowOpened();
        }
        consoleBottomHalf?.ShowTutorial();
        ShowPage(1);
    }

    public void NavigateForward()
    {
        if (currentPage < totalPages)
            ShowPage(currentPage + 1);
    }

    public void NavigateBack()
    {
        if (currentPage > 1)
            ShowPage(currentPage - 1);
    }

    private void ShowPage(int page)
    {
        currentPage = page;
        firstPage.SetActive(page == 1);
        secondPage.SetActive(page == 2);
        thirdPage.SetActive(page == 3);
        fourthPage.SetActive(page == 4);
        score.SetActive(page >= 2);

        SetTutorialButtonVisible(backButton, page > 1);
        SetTutorialButtonVisible(forwardButton, page < totalPages);
    }

    private void SetTutorialButtonVisible(GameObject buttonObj, bool visible)
    {
        if (buttonObj == null) return;

        // Activation/deactivation is owned by ConsoleBottomHalfController.
        // Here we only control visibility and clickability per tutorial page.
        var cg = buttonObj.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = buttonObj.AddComponent<CanvasGroup>();

        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;

        var btn = buttonObj.GetComponent<Button>();
        if (btn != null)
            btn.interactable = visible;
    }

    private void CloseStartScreen()
    {
        if(startScreen == null)
        {
            Debug.LogError("noluyo");
        }
        else startScreen.SetActive(false);
    }

    public void CloseTutorial()
    {
        startScreen.SetActive(true);
        tutorialScreen.SetActive(false);
        score.SetActive(false);
        consoleBottomHalf?.ShowStartScreen();
        if (gameController != null)
        {
            gameController.OnTutorialWindowClosed();
        }
    }
}
