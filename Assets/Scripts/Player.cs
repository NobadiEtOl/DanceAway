using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using Common.Enums;
//using UnityEditor.Animations;
using System;
using System.Data.Common;

public class Player : MonoBehaviour
{
    [SerializeField]private Text scoreText;
    [SerializeField]private Text beatStateText;
    [SerializeField]private Text multText;
    [SerializeField]private Text scoreIncText;
    [SerializeField]private Text comboText;
    [SerializeField]private Slider healthSlider;
    public int score;
    private int maxHealth=50;
    public int health; // Player starting health
    public Vector2Int position;
    private GameController gameController; // Reference to GameController
    private BeatTimer beatTimer;
    private Animator animator;
    private Dictionary<string, float> animationLengths;
    private Animator beatStateAnimator;
    private Animator multTextAnimator;
    private Animator comboTextAnimator;
    private Animator scoreIncTextAnimator;
    private Rigidbody2D rb;
    public BeatState State { get; set; }
    private float tileSize;
    public LayerMask spotlightLayer;
    private Vector2Int currentDirection;
    //[SerializeField]private List<int> gridBounds = new List<int>();// width lower(0)/upper(1), height lower(2)/upper(3)
    [SerializeField]private List<int> gridBoundsPlayer = new List<int>();// width lower(0)/upper(1), height lower(2)/upper(3)
    public float rotationSpeed = 5f; // Adjust speed as needed
    private Quaternion targetRotation;
    void Start()
    {
        targetRotation = transform.rotation;
    }
    public void StartPlayer()
    {
        // GameController and BeatTimer scripts
        gameController = GameObject.Find("GameController").GetComponent<GameController>();
        beatTimer = GameObject.Find("GameController").GetComponent<BeatTimer>();
        // Setting the initial bounds for movement.
        tileSize = gameController.tileSize;
        gridBoundsPlayer = gameController.ReturnGridbounds();
        //Nasıl deep coy olmadan kopşyalıyıcam
        // Starting position at the bottom-middle tile
        position = new Vector2Int((gameController.width-1)/2,(gameController.height-1)/2);
        transform.position = new Vector2(position.x * tileSize, position.y * tileSize);
        // Starting animation
        animator = GetComponent<Animator>();
        beatStateAnimator = beatStateText.GetComponent<Animator>();
        multTextAnimator = multText.GetComponent<Animator>();
        scoreIncTextAnimator = scoreIncText.GetComponent<Animator>();
        comboTextAnimator = comboText.GetComponent<Animator>();
        animator.Play("idle");

        beatStateText.text = "";
        multText.text="";
        scoreIncText.text="";
        beatStateText.gameObject.SetActive(false);

        rb = GetComponent<Rigidbody2D>();
        health = maxHealth;
        healthSlider.maxValue = maxHealth;
        healthSlider.value = maxHealth;

        GetAnimationLenghts();

        beatTimer.OffBeat += ResetMove;

    }

    public void EnableBeatStateText()
    {
        beatStateText.gameObject.SetActive(true);
    }

    void FixedUpdate()
    {
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
    }

    private int moveCount=0;  
    private int mult;
    private int canHit;
    private int moveCombo=0;
    public void Move(Vector2Int direction, bool pushed=false, BeatState? overrideState = null)
    {
        Vector2Int newPosition;
        State = (overrideState.HasValue && !pushed) ? overrideState.Value : beatTimer.state;
        currentDirection = direction;// To use later if the player walks into a triangle.
        crowdPushFlag = pushed;

        bool validMove=true;

        // A crowd push always succeeds — bypass beat-timing and move-count checks
        if (pushed)
        {
            validMove = true;
        }
        else if (State == BeatState.OffBeat || moveCount > 2)
        {
            validMove=false; // Ignore movement if in OffBeat
        }

        // Update newPosition if move is valid
        if(validMove)newPosition = position + direction;
        else newPosition = position;

        // Check if the new position is within the grid bounds
        if (crowdPushFlag || (newPosition.x >= gridBoundsPlayer[0] && newPosition.x < gridBoundsPlayer[1] && newPosition.y >= gridBoundsPlayer[2] && newPosition.y < gridBoundsPlayer[3]))
        {
            // Update logical position and apply force only when the move is valid
            if (validMove)
            {
                position = newPosition;
                rb.AddForce(direction*(int)(200*tileSize));
            }

            // Pushed moves are silent — no score, no UI, no combo change
            if (pushed)
            {
                moveCount++;
                StartCoroutine(ResetAnimation("Player_Moving"));
                return;
            }

            int scoreIncrement = 0;
            CheckForSpotlightCollision();// Find out how much mult is.
            
            moveCount++;// Only count moves if there are enemies.
            moveCombo++;
            if (State == BeatState.PerfectBeat)
            {
                scoreIncrement = 200; // Perfect score threshold
                beatStateText.text = "S";
            }
            else if (State == BeatState.CloseBeat)
            {
                scoreIncrement = 150; // Close score threshold
                beatStateText.text = "A";
            }
            else if (State == BeatState.MiddleBeat)
            {
                scoreIncrement = 100; // Middle score threshold
                beatStateText.text = "B";
            }
            else if (State == BeatState.FarBeat)
            {
                scoreIncrement = 50; // Far score threshold
                beatStateText.text = "C";
            }
            else if (State == BeatState.OffBeat)
            {
                // Moving during off beat is punishing.
                scoreIncrement=0;// Make a large score deduction.
                beatStateText.text = "F";
                StartCoroutine(WrongMove(direction));
                moveCombo=0;
            }
            else
            {
                scoreIncrement = 0;
                beatStateText.text = "WTF";
            }

            int diffMult = gameController.GetDifficultyMultiplier();
            scoreIncrement *= diffMult;
            //Only one triangle with the highest powerLevel gets hit.
            canHit = scoreIncrement*mult*(gameController.canStart ? 1 : 0);
            HitWeakestTriangle(canHit);

            // Score only applies to valid (on-beat) moves
            if(validMove)score += (scoreIncrement+ moveCombo)*mult;
            scoreIncText.text = "+" + (scoreIncrement*mult).ToString();

            // Updating score and avarage
            scoreText.text = score.ToString();
            beatStateAnimator.Play("BeatStateText",-1,0f);
            multTextAnimator.Play("MultText",-1,0f);
            scoreIncTextAnimator.Play("ScoreIncText",-1,0f);
            comboTextAnimator.Play("ComboText",-1,0f);
            if (!gameController.lockAvarageAtMax) gameController.avarage += scoreIncrement;
            multText.text = "x" + mult.ToString();
            if(moveCombo!=0)comboText.text = "x" + moveCombo.ToString();
            else comboText.text = "";
        }
        
        //correction method so that the player does not get stuck outside of the current grid.
        else if(position.x < gridBoundsPlayer[0] || position.x >= gridBoundsPlayer[1] || position.y < gridBoundsPlayer[2] || position.y >= gridBoundsPlayer[3])
        {
            var currentDistance = Vector2.Distance(position,new Vector2(4,4));
            var newDistance = Vector2.Distance(newPosition,new Vector2(4,4));

            if(newDistance <= currentDistance)
            {
                Move(direction,true);
            }
        }

        else
        {
            StartCoroutine(WrongMove(direction));
        }

        StartCoroutine(ResetAnimation("Player_Moving"));
    }

    private void ResetMove()
    {
        moveCount = 0;
    }


    private void CheckForSpotlightCollision()
    {
        // Check for nearby colliders in the spotlight layer
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 5f, spotlightLayer);

        // mult start with one in each check
        mult = 1;

        foreach (Collider2D collider in colliders)
        {
            SpotlightSquare spotlight;

            if (SpotlightSquare.cachedSpotlights.TryGetValue(collider.gameObject, out spotlight) && spotlight != null)
            {
                // mult gets multiplied if there is a spotlight overlapping with the player
                mult *= spotlight.powerLevel;
            }
        }
    }

    //for spotlight coliision debugging
    private void OnDrawGizmosSelected()
    {
        // Set the color of the Gizmos
        Gizmos.color = Color.yellow;
        
        // Draw a wire sphere at the player's position with the specified radius
        Gizmos.DrawWireSphere(transform.position, 6f);
    }

    private IEnumerator WrongMove(Vector2 direction)
    {
        //Making the player move back and forth for a wrong move
        rb.AddForce(direction*(int)(200*tileSize));

        yield return new WaitForSeconds(beatTimer.beatInterval/4);

        rb.AddForce(-direction*(int)(200*tileSize));
    }

    private IEnumerator ResetAnimation(string animationName)
    {
        if(!takingDamage)animator.Play(animationName);

        float animationLength = animationLengths.ContainsKey(animationName) ? animationLengths[animationName] : 0.4f;

        yield return new WaitForSeconds(animationLength);

        if(!takingDamage)animator.Play("idle");
    }

    private void HitWeakestTriangle(int damage)
    {
        if (gameController.enemies.Count == 0)
        {
            return; // No enemies to hit
        }

        // Calculate the maximum power level among all triangles
        int minPower = gameController.enemies.Min(t => t.powerLevel);

        // Find the first triangle with the maximum power level
        var weakestTriangle = gameController.enemies.FirstOrDefault(t => t.powerLevel == minPower);

        // If a triangle is found, deal damage to it
        if (weakestTriangle != null)
        {
            weakestTriangle.TakeDamage(damage);
        }
    }

    private bool hasDied=false;
    public void TakeDamage(int damage)
    {
        if(!takingDamage)
        {
            health -= damage;
            gameController.LessNodders(40);// Decrease the number of cTriangles Nodding.
            healthSlider.value = health;
            Move(-currentDirection);
            if (health <= 0 && !hasDied)
            {
                // Handle player death
                Debug.Log("Player has died");
                gameController.OpenEndScreen();
                hasDied=true;
            }

            StartCoroutine(DamageTaken());
        }
    }
    public void TakeHeart(int heal)
    {

        health += heal;
        gameController.MoreNodders(20);
        healthSlider.value = health;
        if (health > maxHealth)
        {
            health = maxHealth;
            gameController.MoreNodders(50);
        }

        StartCoroutine(HealTaken());
        
    }

    private bool takingDamage = false;
    private IEnumerator DamageTaken()
    {
        animator.Play("Player_Damage");
        takingDamage = true;

        yield return new WaitForSeconds(beatTimer.beatInterval);

        animator.Play("idle");
        takingDamage=false;
    }
    private IEnumerator HealTaken()
    {
        animator.Play("Player_Heal");
        takingDamage = true;

        yield return new WaitForSeconds(beatTimer.beatInterval);

        animator.Play("idle");
        takingDamage=false;
    }


    private bool crowdPushFlag=true;
    void OnTriggerEnter2D(Collider2D other)
    {
        if(other.CompareTag("Triangle"))
        {
            Triangle triangle;
            if(Triangle.cachedTriangles.TryGetValue(other.gameObject, out triangle))
            {
                TakeDamage(triangle.powerLevel);
            }
        }

        if(other.CompareTag("Heart"))
        {
            gameController.CollectHeart(other.gameObject);
        }

    }

    public void HandleInput()
    {
        if (hasDied) return;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            targetRotation = Quaternion.Euler(0, 0, 0);
            Move(Vector2Int.up);
        }
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            targetRotation = Quaternion.Euler(0, 0, 180);
            Move(Vector2Int.down);
        }
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            targetRotation = Quaternion.Euler(0, 0, 90);
            Move(Vector2Int.left);
        }
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            targetRotation = Quaternion.Euler(0, 0, 270);
            Move(Vector2Int.right);
        }
    }

    public void ChangeGridBounds()
    {
        if(gameController == null)print("offfff");
        gridBoundsPlayer = gameController.ReturnGridbounds();
        CheckIfOutside();
    }

    private void CheckIfOutside()
    {
        if(position.x < gridBoundsPlayer[0])
        {
            for(int i = gridBoundsPlayer[0]-position.x; i > 0; i--)
            {
                Move(Vector2Int.right,true);
            }
        }

        if(position.x >= gridBoundsPlayer[1])
        {
            for(int i = position.x-gridBoundsPlayer[1]+1; i > 0; i--)
            {
                Move(Vector2Int.left,true);
            }
        }

        if(position.y < gridBoundsPlayer[2])
        {
            for(int i = gridBoundsPlayer[2]-position.y; i > 0; i--)
            {
                Move(Vector2Int.up,true);
            }
        }
        if(position.y >= gridBoundsPlayer[3])
        {
            for(int i = position.y-gridBoundsPlayer[3]+1; i > 0; i--)
            {
                Move(Vector2Int.down,true);
            }
        }
    }

    public void PlaceScore()
    {
        scoreText.text = "0";
    }

    private void GetAnimationLenghts()
    {
        animationLengths = new Dictionary<string, float>();
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            animationLengths[clip.name] = clip.length;
        }
    }
}
