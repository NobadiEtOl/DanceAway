using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    public void OpenFirst()
    {
        print("openfirst");
        CloseStartScreen();
        tutorialScreen.SetActive(true);
        if (gameController != null)
        {
            gameController.OnTutorialWindowOpened();
        }
        firstPage.SetActive(true);
        secondPage.SetActive(false);
        thirdPage.SetActive(false);
        fourthPage.SetActive(false);
        score.SetActive(false);
        print("openfirstend");
    }

    private void CloseStartScreen()
    {
        if(startScreen == null)
        {
            Debug.LogError("noluyo");
        }
        else startScreen.SetActive(false);
    }

    public void OpenSecond()
    {
        secondPage.SetActive(true);
        firstPage.SetActive(false);
        score.SetActive(true);
    }

    public void OpenThird()
    {
        thirdPage.SetActive(true);
        secondPage.SetActive(false);
    }

    public void OpenFourth()
    {
        fourthPage.SetActive(true);
        thirdPage.SetActive(false);
    }

    public void CloseTutorial()
    {
        startScreen.SetActive(true);
        tutorialScreen.SetActive(false);
        score.SetActive(false);
        if (gameController != null)
        {
            gameController.OnTutorialWindowClosed();
        }
    }
}
