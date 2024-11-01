using System.Collections;
using System.Collections.Generic;
using Common.Enums;
using UnityEngine;

public class SnapController : MonoBehaviour
{
    [SerializeField]private Sprite yellowSprite;
    [SerializeField]private Sprite greenSprite;
    [SerializeField]private Sprite blueSprite;
    [SerializeField]private Sprite redSprite;
    private SpriteRenderer spriteRenderer;
    private BeatState beatState;
    private BeatState currentState;

    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void ChangeSnap(BeatState beatState)
    {
        switch(beatState)
        {
            case BeatState.OffBeat:
            spriteRenderer.sprite = null;
            break;
            case BeatState.FarBeat:
            spriteRenderer.sprite = yellowSprite;
            break;
            case BeatState.MiddleBeat:
            spriteRenderer.sprite = greenSprite;
            break;
            case BeatState.CloseBeat:
            spriteRenderer.sprite = blueSprite;
            break;
            case BeatState.PerfectBeat:
            spriteRenderer.sprite = redSprite;
            break;
        }
    }
}
