using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TutorialController : MonoBehaviour
{
    [SerializeField]private GameObject tutorialScreen;
    [SerializeField]private GameObject firstPage;
    [SerializeField]private GameObject secondPage;
    [SerializeField]private GameObject thirdPage;
    [SerializeField]private GameObject fourthPage;
    [SerializeField]private GameObject startButton;
    [SerializeField]private GameObject tutorialButon;
    [SerializeField]private GameObject score;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OpenFirst()
    {
        startButton.SetActive(false);
        tutorialScreen.SetActive(true);
        firstPage.SetActive(true);
        secondPage.SetActive(false);
        thirdPage.SetActive(false);
        fourthPage.SetActive(false);
        tutorialButon.SetActive(false);
        score.SetActive(false);
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
        startButton.SetActive(true);
        tutorialScreen.SetActive(false);
        tutorialButon.SetActive(true);
        score.SetActive(false);
    }
}
