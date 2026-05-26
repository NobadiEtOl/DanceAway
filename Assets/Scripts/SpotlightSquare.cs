using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

public class SpotlightSquare: MonoBehaviour
{
    [HideInInspector]public Vector2Int position;
    [HideInInspector]public Vector2Int previousPosition; // To track the previous position
    private GameController gameController; // Reference to GameController
    private BeatTimer beatTimer;
    private Vector2Int nextPosition; // Make nextPosition public
    private Vector2Int currentDirection; // Make currentDirection public
    [HideInInspector]public int powerLevel; // Starting power level for spotlights
    [HideInInspector]public int moveCount = 0; // Move counter starting at 0
    [SerializeField] private float speed;
    private SpriteRenderer spriteRenderer;
    private List<Vector2Int> initialMoves = new List<Vector2Int>();
    private Rigidbody2D rb;
    [HideInInspector]public static Dictionary<GameObject, SpotlightSquare> cachedSpotlights = new Dictionary<GameObject, SpotlightSquare>();
    [SerializeField]private List<int> gridBoundsSpotlight = new List<int>();

    public void Initialize(Vector2Int initialPosition, GameController controller, BeatTimer timer, int initialPowerLevel)
    {
        // Setting initial parameters.
        position = initialPosition;
        gameController = controller;
        beatTimer = timer;
        powerLevel = initialPowerLevel; // Set power level from the triangle it spawned from
        // Setting initial position
        transform.position = new Vector2(position.x * gameController.tileSize, position.y * gameController.tileSize);
        nextPosition = position;
        gridBoundsSpotlight = GameController.gridBounds;
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        speed = gameController.tileSize / beatTimer.beatInterval;
        // Adding each triangle to a static cache to use during collision checks in the player script
        if (!cachedSpotlights.ContainsKey(gameObject))
        {
            cachedSpotlights[gameObject] = this;
        }
        // Set initial color based on power level
        UpdateColor();
    }

    void Start()
    {
    }

    void FixedUpdate()
    {
        // Spotlight move half as slow to give the player chance to be on the spotlight
        float moveDistance = speed * Time.fixedDeltaTime / 2; // Distance to move in one frame
        rb.MovePosition(rb.position + new Vector2(currentDirection.x * moveDistance, currentDirection.y * moveDistance));
    }

    void OnDestroy()
    {
        if(cachedSpotlights.ContainsKey(gameObject))
        {
            cachedSpotlights.Remove(gameObject);
        }
    }

    public void DecideNextMove()
    {
        if (initialMoves.Count > 0)
        {
            currentDirection = initialMoves[0];
            initialMoves.RemoveAt(0);
        }
        else if(IsOutside())
        {
            currentDirection = GetDirectionTowardsGrid();
        }
        else
        {
            currentDirection = GetRandomValidDirection();
        }

        nextPosition = position + currentDirection;
        RotateTowardsDirection(currentDirection);
    }

    public void Move()
    {
        DecideNextMove();
        previousPosition = position; // Store the current position as the previous position
        position = nextPosition;     // Update the current position to the next position
        moveCount++; // Increment move counter
    }


    public void MergeSpotlights(int numberOfSpotlights)
    {
        powerLevel = powerLevel * (int)Mathf.Pow(2, numberOfSpotlights - 1);
        UpdateColor(); // Update the color based on the new power level
    }

    private bool IsOutside()
    {
        if (position.x < gridBoundsSpotlight[0] || position.x >= gridBoundsSpotlight[1] || position.y < gridBoundsSpotlight[2] || position.y >= gridBoundsSpotlight[3])
        {
            return true; // Out of bounds
        }
        else return false;
    }

    private Vector2Int GetDirectionTowardsGrid()
    {
        if (position.x < gridBoundsSpotlight[0]) // Coming from the left
        {
            return Vector2Int.right;
        }
        else if (position.x >= gridBoundsSpotlight[1]) // Coming from the right
        {
            return Vector2Int.left;
        }
        else if (position.y >= gridBoundsSpotlight[3]) // Coming from above
        {
            return Vector2Int.down;
        }
        else if (position.y < gridBoundsSpotlight[2]) // Coming from below
        {
            return Vector2Int.up;
        }
        else return Vector2Int.zero;
    }

    void UpdateColor()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        SpriteRenderer childSpriteRenderer = null;

        // Find the child with SpriteRenderer but skip those with SpriteMask
        foreach (Transform child in transform)
        {
            if (child.TryGetComponent(out SpriteRenderer sr) && !child.TryGetComponent<SpriteMask>(out _))
            {
                childSpriteRenderer = sr;
                break; // Stop once the correct child is found
            }
        }

        Color color = Color.white; // Default to white
        switch (powerLevel)
        {
            case 2:
                color = Color.white;
                break;
            case 4:
                color = Color.yellow;
                break;
            case 8:
                color = Color.green;
                break;
            case 16:
                color = Color.blue;
                break;
            case 32:
                color = Color.red;
                break;
            case 64:
                color = Color.gray;
                break;
            default:
                color = Color.black;
                break;
        }

        // Set parent sprite's color
        color.a = 0.5f;
        spriteRenderer.color = color;

        color = Color.white;
        color.a = 0.05f; // Set alpha transparency for child

        childSpriteRenderer.color = color;
    }

    // Same as Triangle.
    private Vector2Int GetRandomValidDirection()
    {
        List<Vector2Int> directions = new List<Vector2Int>
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        while (directions.Count > 0)
        {
            int index = Random.Range(0, directions.Count);
            Vector2Int dir = directions[index];
            Vector2Int nextPos = position + dir;

            if (IsValidMove(nextPos))
            {
                return dir;
            }

            directions.RemoveAt(index);
        }

        return Vector2Int.zero; // Stay in place if no valid moves
    }

    private bool IsValidMove(Vector2Int nextPosition)
    {
        if (nextPosition.x < gridBoundsSpotlight[0] || nextPosition.x >= gridBoundsSpotlight[1] || nextPosition.y < gridBoundsSpotlight[2] || nextPosition.y >= gridBoundsSpotlight[3])
        {
            return false; // Out of bounds
        }

        foreach (var spotlight in gameController.spotlights)
        {
            if (spotlight.powerLevel != powerLevel && spotlight.nextPosition == nextPosition)
            {
                return false;
            }
        }
        return true;
    }

    public void RotateTowardsDirection(Vector2Int direction)
    {
        Vector3 directionVector = new Vector3(direction.x, direction.y, 0);
        transform.up = directionVector;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if(other.CompareTag("Spotlight"))
        {
             List<SpotlightSquare> spotlightMergeList=new List<SpotlightSquare>{this,other.gameObject.GetComponent<SpotlightSquare>()};
            gameController.MergeSpotlights(spotlightMergeList);
        }
    }
}
