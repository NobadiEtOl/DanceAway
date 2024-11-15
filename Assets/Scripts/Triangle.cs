using UnityEngine;
using System.Collections.Generic;
//using UnityEditor.Experimental.GraphView;
using System.Collections;
using UnityEngine.UI;

public class Triangle : MonoBehaviour
{
    [SerializeField]private GameObject spotlightPrefab;
    [SerializeField]public int baseHealth=1;
    public int health;
    [SerializeField]private float speed;
    [SerializeField]private List<int> gridBounds = new List<int>();// width lower(0)/upper(1), height lower(2)/upper(3)    
    [HideInInspector]public Vector2Int position;
    [HideInInspector]public Vector2Int previousPosition; // To track the previous position
    [HideInInspector]public GameController gameController; // Reference to GameController
    private BeatTimer beatTimer;
    public AudioClip moveSound; // Sound to play when the triangle moves
    [HideInInspector]public Vector2Int nextPosition; // Make nextPosition public
    [HideInInspector]public Vector2Int currentDirection; // Make currentDirection public
    public int powerLevel = 2; // Starting power level for triangles
    [HideInInspector]public int moveCount = 0; // Move counter starting at 0
    private SpriteRenderer spriteRenderer;
    private AudioSource audioSource;
    private List<Vector2Int> initialMoves = new List<Vector2Int>();
    private Rigidbody2D rb;
    [HideInInspector]public bool isChasingPlayer = false;
    [HideInInspector]public static Dictionary<GameObject, Triangle> cachedTriangles = new Dictionary<GameObject, Triangle>();
    private int speedMult = 3;
    public void  Initialize(Vector2Int initialPosition, GameController controller, BeatTimer timer)
    {
        // Giving initial parameters
        position = initialPosition;
        gameController = controller;
        beatTimer = timer;
        // Positioning in the correct initial position
        transform.position = new Vector2(position.x * gameController.tileSize, position.y * gameController.tileSize);
        nextPosition = position;
        // Set initial grid bounds
        gridBounds = GameController.gridBounds;

        audioSource = GetComponent<AudioSource>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        // Adjusting speed for correct movement
        speed = gameController.tileSize/(beatTimer.beatInterval/speedMult);
        // Resetin health to full in each Initializetion while multiplying with the power level
        health = powerLevel * baseHealth;
        // Initial animation 
        animator = GetComponent<Animator>();
        childAnimator = transform.GetChild(0).GetComponent<Animator>();
        animator.Play("Triangle_Idle");
        childAnimator.Play("Triangle_Damage_Idle");
        // Adding each triangle to a static cache to use during collision checks in the player script
        if (!cachedTriangles.ContainsKey(gameObject))
        {
            cachedTriangles[gameObject] = this;
        }

        // Set the color based on power level
        UpdateColor();
    }

    void Start()
    {

    }

    private float moveTimer=0;
    private bool canMove=true;
    void FixedUpdate()
    {
        // Snap to the position
        if (moveTimer >= beatTimer.beatInterval / speedMult && canMove)
        {
            transform.position=new Vector3(nextPosition.x*gameController.tileSize,nextPosition.y*gameController.tileSize,0);
            canMove = false;
            DecideNextMove();
        }

        // Move to the next position
        if (canMove && moveTimer < beatTimer.beatInterval / speedMult)
        {
            float moveDistance = speed * Time.fixedDeltaTime; // Distance to move in one frame
            rb.MovePosition(rb.position + new Vector2(currentDirection.x * moveDistance, currentDirection.y * moveDistance));
        }
        else
        {
            // Stop the movement after half-beat
            rb.velocity = Vector2.zero;
            if(position.y == 0)
            {
                Move();
            }
        }

        moveTimer += Time.deltaTime;
    }


    void OnDestroy()
    {
        if (cachedTriangles.ContainsKey(gameObject))
        {
            cachedTriangles.Remove(gameObject);
        }
    }

    // A function to make sure the triangle initially moves to the inside of the arena 
    public void SetInitialMoves()
    {
        if (position.x <= gridBounds[0]-1) // Coming from the left
        {
            initialMoves.Add(Vector2Int.right);
            //initialMoves.Add(Vector2Int.right);
        }
        else if (position.x >= gridBounds[1]) // Coming from the right
        {
            initialMoves.Add(Vector2Int.left);
            //initialMoves.Add(Vector2Int.left);
        }
        else if (position.y <= gridBounds[2]-1) // Coming from below
        {
            initialMoves.Add(Vector2Int.up);
            initialMoves.Add(Vector2Int.up);
        }
        else if (position.y >=  gridBounds[3]) // Coming from above
        {
            initialMoves.Add(Vector2Int.down);
            initialMoves.Add(Vector2Int.down);
        }
    }
    public void DecideNextMove()
    {
        // Makes sure the triangles leave the area from the bottom
        if(position.y == gridBounds[2])
        {
            currentDirection = Vector2Int.down;
        }
        else if (initialMoves.Count > 0)
        {
            currentDirection = initialMoves[0];
            initialMoves.RemoveAt(0);
        }
        else if(IsOutside())
        {
            currentDirection = GetDirectionTowardsGrid();
        }
        else if (isChasingPlayer)
        {
            currentDirection = GetDirectionTowardsPlayer();
        }
        else
        {
            currentDirection = GetRandomValidDirectionDown();
        }

        nextPosition = position + currentDirection;
        RotateTowardsDirection(currentDirection);
    }

    private int chasingNo = 0;
    [SerializeField]private bool chaseBreak=true;// To control if triangle can take a chase break
    // Method to get direction towards the player
    private Vector2Int GetDirectionTowardsPlayer()
    {
        chasingNo++;
        if(chaseBreak && chasingNo%2==0)
        {
            return Vector2Int.down;// Only chase player every 2 moves once.
        }

        Vector2Int playerPos = gameController.player.position;
        Vector2Int direction = Vector2Int.zero;

        // Triangle checks the horizantal position fisrt to get the correct column then moves downward
        if (position.x < playerPos.x) direction = Vector2Int.right;
        else if (position.x > playerPos.x) direction = Vector2Int.left;
        else if (position.y < playerPos.y) direction = Vector2Int.down;// To prevent reverse chasing
        else if (position.y > playerPos.y) direction = Vector2Int.down;

        if (IsValidMove(position + direction))
            return direction;

        return GetRandomValidDirectionDown(); // Fallback to random move if the direct path is blocked
    }

    private Vector2Int GetDirectionTowardsGrid()
    {
        if (position.x <= gridBounds[0]-1) // Coming from the left
        {
            return Vector2Int.right;
        }
        else if (position.x >= gridBounds[1]) // Coming from the right
        {
            return Vector2Int.left;
        }
        else if (position.y >=  gridBounds[3]) // Coming from above
        {
            return Vector2Int.down;
        }
        else return Vector2Int.zero;
    }


    public void Move()
    {
        // Teleports triangle to the top of the column if they reach the bottom
        if(position.y <= gridBounds[2])
        {
            nextPosition.y = gridBounds[3]-1;
        }

        canMove=true;
        moveTimer=0;
        previousPosition = position;
        position = nextPosition;

        moveCount++; // Increment move counter

        StartCoroutine(ResetAnimation("Triangle_Moving"));
    }

    private IEnumerator ResetAnimation(string animationName)
    {
        animator.Play(animationName);

        yield return new WaitForSeconds(0.45f);

        animator.Play("Triangle_Idle");
    }

    public void MergeTriangles(int numberOfTriangles, int combinedHealth)
    {
        powerLevel = powerLevel * (int)Mathf.Pow(2, numberOfTriangles - 1);
        health = (powerLevel*baseHealth)-combinedHealth;
        UpdateColor(); // Update the color based on the new power level
    }

    void UpdateColor()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        switch (powerLevel)
        {
            case 2:
                spriteRenderer.color = Color.white;
                break;
            case 4:
                spriteRenderer.color = Color.yellow;
                break;
            case 8:
                spriteRenderer.color = Color.green;
                break;
            case 16:
                spriteRenderer.color = Color.blue;
                break;
            case 32:
                spriteRenderer.color = Color.red;
                break;
            case 64:
                spriteRenderer.color = Color.gray;
                break;
            default:
                spriteRenderer.color = Color.black; // Default color if power level exceeds 16
                break;
        }
    }

    public void TakeDamage(int damage)
    {
        health -= damage;

        StartCoroutine(DamageTaken());
        if (health <= 0)
        {
            gameController.RemoveEnemy(this);
            gameController.gridBoundsFlag = true;
            gameController.enemiesKilled++;
            Destroy(gameObject);
        }
    }

    private Animator animator;
    private Animator childAnimator;
    private IEnumerator DamageTaken()
    {
        childAnimator.Play("Triangle_Damage");

        yield return new WaitForSeconds(beatTimer.beatInterval/2
        );
        
        childAnimator.Play("Triangle_Damage_Idle");
    }
    // Random direction with a more tendecy to go down.
    private Vector2Int GetRandomValidDirectionDown()
    {
        List<Vector2Int> directions = new List<Vector2Int>
        {
            Vector2Int.down,
            Vector2Int.down,
            Vector2Int.zero,
            Vector2Int.zero,
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
    // Checks if the move is valid
    private bool IsValidMove(Vector2Int nextPosition)
    {
        if (nextPosition.x < gridBounds[0] || nextPosition.x >= gridBounds[1] || nextPosition.y < gridBounds[2] || nextPosition.y >= gridBounds[3])
        {
            return false; // Out of bounds
        }

        foreach (var triangle in gameController.enemies)
        {
            if (triangle.powerLevel != powerLevel && triangle.nextPosition == nextPosition)
            {
                return false;
            }
        }
        return true;
    }
    private bool IsOutside()
    {
        if (position.x < gridBounds[0] || position.x >= gridBounds[1] || position.y < gridBounds[2] || position.y >= gridBounds[3])
        {
            return true; // Out of bounds
        }
        else return false;
    }

    public void RotateTowardsDirection(Vector2Int direction)
    {
        if(direction == Vector2.zero)
        {
            direction = Vector2Int.down;
        }
        Vector3 directionVector = new Vector3(direction.x, direction.y, 0);
        transform.up = directionVector;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if(other.CompareTag("Triangle"))
        {
            List<Triangle> triangleMergeList=new List<Triangle>{this,other.gameObject.GetComponent<Triangle>()};
            gameController.MergeTriangles(triangleMergeList);
        }
    }

}
