using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Common.Enums;

public class SwipeController : MonoBehaviour
{
    [SerializeField]private Player player;
    private Vector2 startTouchPosition, endTouchPosition;
    private bool isSwipe;
    private BeatState capturedBeatState;
    private float swipeStartTime;
    
    [SerializeField] private float minSwipeDistance = 50f; // Minimum swipe distance in pixels
    [SerializeField] private RectTransform swipeArea;
    private RectTransform swipeAreaRectTransform;
    private Camera swipeEventCamera;

    void Start()
    {
        // Use an explicitly assigned area if provided; otherwise use this object's RectTransform.
        swipeAreaRectTransform = swipeArea != null ? swipeArea : GetComponent<RectTransform>();

        Canvas parentCanvas = swipeAreaRectTransform != null ? swipeAreaRectTransform.GetComponentInParent<Canvas>() : null;
        if (parentCanvas != null)
        {
            // Overlay canvases must use null camera for correct screen-point checks.
            swipeEventCamera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        }
        else
        {
            swipeEventCamera = Camera.main;
        }
    }

    void Update()
    {
        DetectSwipe();
    }

    private void DetectSwipe()
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    // Check if the touch started within the swipe area (UI RectTransform)
                    if (IsWithinSwipeArea(touch.position))
                    {
                        startTouchPosition = touch.position;
                        isSwipe = true;
                        capturedBeatState = GameController.beatTimer.state;
                        swipeStartTime = Time.time;
                    }
                    break;

                case TouchPhase.Moved:
                    if (isSwipe)
                    {
                        endTouchPosition = touch.position;
                        if (Vector2.Distance(startTouchPosition, endTouchPosition) >= minSwipeDistance)
                        {
                            DetectSwipeDirection();
                            isSwipe = false;
                        }
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    isSwipe = false;
                    break;
            }

            return;
        }

        DetectMouseSwipe();
    }

    private void DetectMouseSwipe()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (IsWithinSwipeArea(Input.mousePosition))
            {
                startTouchPosition = Input.mousePosition;
                isSwipe = true;
                capturedBeatState = GameController.beatTimer.state;
                swipeStartTime = Time.time;
            }
        }
        else if (Input.GetMouseButton(0) && isSwipe)
        {
            endTouchPosition = Input.mousePosition;
            if (Vector2.Distance(startTouchPosition, endTouchPosition) >= minSwipeDistance)
            {
                DetectSwipeDirection();
                isSwipe = false;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isSwipe = false;
        }
    }

    private bool IsWithinSwipeArea(Vector2 screenPosition)
    {
        if (swipeAreaRectTransform == null)
        {
            return true;
        }

        return RectTransformUtility.RectangleContainsScreenPoint(swipeAreaRectTransform, screenPosition, swipeEventCamera);
    }

    private void DetectSwipeDirection()
    {
        // Use the beat state captured at swipe start. If the gesture took longer than half
        // a beat interval the capture is stale — fall back to the live state instead.
        bool stale = (Time.time - swipeStartTime) > (GameController.beatTimer.beatInterval * 0.5f);
        BeatState? stateOverride = stale ? (BeatState?)null : capturedBeatState;

        Vector2 swipeDirection = endTouchPosition - startTouchPosition;
        float x = swipeDirection.x;
        float y = swipeDirection.y;

        float angleInDegrees = Mathf.Atan2(y, x) * Mathf.Rad2Deg;

        if(20 <= Mathf.Abs(angleInDegrees) && Mathf.Abs(angleInDegrees) <= 70)
        {
            if(angleInDegrees > 0)
            {
                if (isSwipe)
                {
                    player.Move(Vector2Int.right, overrideState: stateOverride);
                    player.Move(Vector2Int.up, overrideState: stateOverride);
                    player.transform.eulerAngles = new Vector3(0,0,0);
                }
            }
            else
            {
                if (isSwipe)
                {
                    player.Move(Vector2Int.right, overrideState: stateOverride);
                    player.Move(Vector2Int.down, overrideState: stateOverride);
                    player.transform.eulerAngles = new Vector3(0,0,180);
                }
            }
        }
        else if(110 <= Mathf.Abs(angleInDegrees) && Mathf.Abs(angleInDegrees) <= 160)
        {
            if(angleInDegrees > 0)
            {
                if (isSwipe)
                {
                    player.Move(Vector2Int.left, overrideState: stateOverride);
                    player.Move(Vector2Int.up, overrideState: stateOverride);
                    player.transform.eulerAngles = new Vector3(0,0,0);
                }
            }
            else
            {
                if (isSwipe)
                {
                    player.Move(Vector2Int.left, overrideState: stateOverride);
                    player.Move(Vector2Int.down, overrideState: stateOverride);
                    player.transform.eulerAngles = new Vector3(0,0,180);
                }
            }
        }

        else
        {
            if (Mathf.Abs(x) > Mathf.Abs(y))
            {
                if (x > 0)
                {
                    //Debug.Log("Swipe Right");
                    if (isSwipe)
                    {
                        player.Move(Vector2Int.right, overrideState: stateOverride);
                        player.transform.eulerAngles = new Vector3(0,0,270);
                    }
                }
                else
                {
                    //Debug.Log("Swipe Left");
                    if (isSwipe)
                    {
                        player.Move(Vector2Int.left, overrideState: stateOverride);
                        player.transform.eulerAngles = new Vector3(0,0,90);
                    }
                }
            }
            else
            {
                if (y > 0)
                {
                    //Debug.Log("Swipe Up");
                    if (isSwipe)
                    {
                        player.Move(Vector2Int.up, overrideState: stateOverride);
                        player.transform.eulerAngles = new Vector3(0,0,0);
                    }
                }
                else
                {
                    //Debug.Log("Swipe Down");
                    if (isSwipe)
                    {
                        player.Move(Vector2Int.down, overrideState: stateOverride);
                        player.transform.eulerAngles = new Vector3(0,0,180);
                    }
                }
            }
        }
    }
}
