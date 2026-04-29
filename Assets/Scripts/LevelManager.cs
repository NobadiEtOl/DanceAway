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
        gc.canSpawn = false;

        float oldPitch = beatTimer.trackPitch;
        GetNewValues(gc.levelNo, out float newPitch, out float newInterval);
        int transitionBeats = 4;

        // Approximate wall-clock duration for smooth visual fades (Time.deltaTime is fine here)
        float approxDuration = beatTimer.beatInterval * transitionBeats;

        // Pitch-only lerp: never touches beatInterval, so the beat track stays perfectly stable
        StartCoroutine(LerpPitchOnly(oldPitch, newPitch, approxDuration));
        StartCoroutine(AnimateLevelText(approxDuration));

        // Melody crossfade only at levels where an active layer changes
        if (gc.levelNo == 10 || gc.levelNo == 20 || gc.levelNo == 30)
            StartCoroutine(CrossfadeMelodyLayer(gc.levelNo, approxDuration));

        // Wait for exactly transitionBeats beats on the centralized track — no WaitForSeconds
        int targetBeat = beatTimer.beatCounter + transitionBeats;
        yield return new WaitUntil(() => beatTimer.beatCounter >= targetBeat);

        // Atomic tempo snap at a beat boundary — nextBeatDsp is unaffected, no phase jump
        beatTimer.SetTempo(newInterval, newPitch);
        gc.canSpawn = true;
    }

    private void GetNewValues(int levelNo, out float pitch, out float interval)
    {
        if      (levelNo == 5)  { pitch = 0.875f;   interval = 0.571f;  }
        else if (levelNo == 10) { pitch = 0.9167f;  interval = 0.545f;  }
        else if (levelNo == 15) { pitch = 0.9583f;  interval = 0.5217f; }
        else if (levelNo == 20) { pitch = 1f;        interval = 0.5f;    }
        else if (levelNo == 25) { pitch = 1.0417f;  interval = 0.48f;   }
        else if (levelNo == 30) { pitch = 1.0833f;  interval = 0.4615f; }
        else if (levelNo == 35) { pitch = 1.125f;   interval = 0.4444f; }
        else                    { pitch = 1.1667f;  interval = 0.4286f; }
    }

    // Lerps AudioSource.pitch only — never touches beatInterval
    private IEnumerator LerpPitchOnly(float oldPitch, float newPitch, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float p = Mathf.Lerp(oldPitch, newPitch, elapsed / duration);
            for (int i = 0; i < 6; i++)
                audioSources[i].pitch = p;
            elapsed += Time.deltaTime;
            yield return null;
        }
        // SetTempo() will apply the final exact pitch atomically after WaitUntil completes
    }

    // Crossfades the active melody layer out and the next one in, scheduled on the beat track.
    // Level 10: no outgoing layer — fade in [3].
    // Level 20: fade out [3], fade in [4].
    // Level 30: fade out [4], fade in [5].
    private IEnumerator CrossfadeMelodyLayer(int levelNo, float duration)
    {
        int outIdx    = levelNo == 20 ? 3 : levelNo == 30 ? 4 : -1;
        int inIdx     = levelNo == 10 ? 3 : levelNo == 20 ? 4 : 5;
        float targetVol = levelNo == 20 ? 0.4f : 0.5f;

        float startOutVol = outIdx >= 0 ? audioSources[outIdx].volume : 0f;

        // Schedule the incoming layer to start at the next beat on the track
        audioSources[inIdx].volume = 0f;
        audioSources[inIdx].PlayScheduled(beatTimer.GetBeatDsp(0));

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            if (outIdx >= 0)
                audioSources[outIdx].volume = Mathf.Lerp(startOutVol, 0f, t);
            audioSources[inIdx].volume = Mathf.Lerp(0f, targetVol, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (outIdx >= 0)
        {
            audioSources[outIdx].volume = 0f;
            audioSources[outIdx].Stop();
        }
        audioSources[inIdx].volume = targetVol;
    }

    private IEnumerator AnimateLevelText(float duration)
    {
        levelText.text = "Level " + gc.levelNo;
        levelText.gameObject.SetActive(true);
        Color c = levelText.color;
        float half = duration / 2f;
        float elapsed = 0f;

        while (elapsed < half)
        {
            c.a = Mathf.Lerp(0f, 1f, elapsed / half);
            levelText.color = c;
            elapsed += Time.deltaTime;
            yield return null;
        }
        c.a = 1f;
        levelText.color = c;
        elapsed = 0f;

        while (elapsed < half)
        {
            c.a = Mathf.Lerp(1f, 0f, elapsed / half);
            levelText.color = c;
            elapsed += Time.deltaTime;
            yield return null;
        }
        c.a = 0f;
        levelText.color = c;
        levelText.gameObject.SetActive(false);
    }
}
