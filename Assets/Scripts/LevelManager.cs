using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using static GameController;
public class LevelManager : MonoBehaviour
{
    private GameController gc;
    [SerializeField]private Text levelText;
    [SerializeField]private int levelLoad5Wait=1;
    public void Initialize()
    {
        gc = GameObject.Find("GameController").GetComponent<GameController>();
        levelText.gameObject.SetActive(false);
    }   

    // Update is called once per frame
    void Update()
    {
        
    }

    private bool flag5=true;
    public void LoadLevel()
    {
        gc.levelNo++;
        if((gc.levelNo%5)==0 && flag5)
        {   
            flag5 = false;
            StartCoroutine(Load5());
            flag5 = true;
        }
        gc.totalTrianglesToSpawn = gc.levelNo;
        gc.trianglesSpawned = 0;
        gc.isSpawningEnemies = true;
        gc.enemiesKilled = 0;
    }

    private IEnumerator Load5()
    {
        gc.canSpawn=false;
        OpenLevelText();
        StopMusic();
        yield return new WaitForSeconds(gc.SendBeatInterval()*2);
        ChangePitch();
        CloseLevelText();
        StartMusic();
        yield return new WaitForSeconds(gc.SendBeatInterval());
        gc.canSpawn=true;
    }

    private void StartMusic()
    {
        beatTimer.play=true;
        beatTimer.beatCounter=-1;
    }

    private void StopMusic()
    {
        for (int i = 0; i < 6; i++)
        {
            audioSources[i].Stop();
        }
    }

    private void ChangePitch()
    {
        // Different beat intervals and pitches are set according to the level no.
        if(gc.levelNo == 5)
        {
            beatTimer.beatInterval = 0.571f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 0.875f;
            }
        }
        else if(gc.levelNo == 10)
        {
            beatTimer.beatInterval = 0.545f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 0.9167f;
            }
        }
        else if(gc.levelNo == 15)
        {
            beatTimer.beatInterval = 0.5217f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 0.9583f;
            }
        }
        else if(gc.levelNo == 20)
        {
            beatTimer.beatInterval = 0.5f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 1f;
            }
        }
        else if(gc.levelNo == 25)
        {
            beatTimer.beatInterval = 0.48f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 1.0417f;
            }
        }
        else if(gc.levelNo == 30)
        {
            beatTimer.beatInterval = 0.4615f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 1.0833f;
            }
        }
        else if(gc.levelNo == 35 )
        {
            beatTimer.beatInterval = 0.4444f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 1.125f;
            }
        }
        else
        {
            beatTimer.beatInterval = 0.4286f;
            for (int i = 0; i < 6; i++)
            {
                audioSources[i].pitch = 1.1667f;
            }
        }

        beatTimer.audioDelay =  beatTimer.beatInterval*0.9f;//adjusting the delay according to the new beatInterval
    }

    private void OpenLevelText()
    {
        levelText.text = "Level " + gc.levelNo;
        levelText.gameObject.SetActive(true);
    }

    private void CloseLevelText()
    {
        levelText.gameObject.SetActive(false);
    }
}
