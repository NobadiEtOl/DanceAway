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
    private Animator multTextAnimator;
    private Animator comboTextAnimator;
    private Animator scoreIncTextAnimator;
    private Animator beatStateTextAnimator;
    private Rigidbody2D rb;
    public BeatState State { get; set; }
    private float tileSize;
    public LayerMask spotlightLayer;
    private Vector2Int currentDirection;
    //[SerializeField]private List<int> gridBounds = new List<int>();// width lower(0)/upper(1), height lower(2)/upper(3)
    [SerializeField]private List<int> gridBoundsPlayer = new List<int>();// width lower(0)/upper(1), height lower(2)/upper(3)
    public float rotationSpeed = 5f; // Adjust speed as needed
    private Quaternion targetRotation;

    private void PlayPlayerAnimation(string animationName)
    {
        Debug.LogError($"{{Moving aniamtion}} {animationName}");
        animator.Play(animationName);
    }

    // Tracks the single active move-animation coroutine.
    // Cancelling it before starting a new one prevents a stale coroutine
    // from forcing "idle" over a freshly started move animation.
    private Coroutine _moveAnimCoroutine;

    private void StartMoveAnimation()
    {
        Debug.Log("Move() called — starting move animation");
        if (_moveAnimCoroutine != null)
            StopCoroutine(_moveAnimCoroutine);
        _moveAnimCoroutine = StartCoroutine(ResetAnimation("Player_Moving"));
    }

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
        multTextAnimator = multText.GetComponent<Animator>();
        scoreIncTextAnimator = scoreIncText.GetComponent<Animator>();
        comboTextAnimator = comboText.GetComponent<Animator>();
        PlayPlayerAnimation("idle");

        beatStateText.text = "";
        multText.text="";
        scoreIncText.text="";
        beatStateText.gameObject.SetActive(false);
        beatStateTextAnimator = beatStateText.GetComponent<Animator>();

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
    public void Move(Vector2Int direction, bool pushed=false, BeatState? overrideState = null, bool autoMove = false)
    {
        Debug.Log($"[PlayerAnimationChecks] Move() called — dir={direction} pushed={pushed} autoMove={autoMove} overrideState={overrideState}");

        // ── State ─────────────────────────────────────────────────────────────
        State            = (overrideState.HasValue && !pushed) ? overrideState.Value : beatTimer.state;
        currentDirection = direction;
        crowdPushFlag    = pushed;

        // Pushed and auto-moves always succeed; normal moves require good timing
        // and a reasonable move count within the beat window.
        bool validMove = pushed || autoMove || !(State == BeatState.OffBeat || moveCount > 2);

        Debug.Log($"[PlayerAnimationChecks] Move() state — State={State} validMove={validMove} moveCount={moveCount} takingDamage={takingDamage}");

        // ── Target position ───────────────────────────────────────────────────
        Vector2Int newPosition = validMove ? position + direction : position;

        bool inBounds = newPosition.x >= gridBoundsPlayer[0] && newPosition.x < gridBoundsPlayer[1]
                     && newPosition.y >= gridBoundsPlayer[2] && newPosition.y < gridBoundsPlayer[3];

        // ── Path A: target is inside the playfield ────────────────────────────
        if (crowdPushFlag || inBounds)
        {
            if (validMove)
            {
                position = newPosition;
                // Clamp so no movement — including crowd pushes — can leave the
                // logical position outside the allowed playfield.
                position.x = Mathf.Clamp(position.x, gridBoundsPlayer[0], gridBoundsPlayer[1] - 1);
                position.y = Mathf.Clamp(position.y, gridBoundsPlayer[2], gridBoundsPlayer[3] - 1);
                rb.AddForce((Vector2)direction * (200 * tileSize));
            }

            moveCount++;

            // Pushed moves are silent — skip all score and UI logic.
            if (!pushed)
            {
                int scoreIncrement = 0;
                CheckForSpotlightCollision();

                if (autoMove)
                {
                    scoreIncrement     = 25;
                    beatStateText.text = " ";
                }
                else
                {
                    moveCombo++;
                    switch (State)
                    {
                        case BeatState.PerfectBeat:
                            scoreIncrement = 200; beatStateText.text = "S"; break;
                        case BeatState.CloseBeat:
                            scoreIncrement = 150; beatStateText.text = "A"; break;
                        case BeatState.MiddleBeat:
                            scoreIncrement = 100; beatStateText.text = "B"; break;
                        case BeatState.FarBeat:
                            scoreIncrement =  50; beatStateText.text = "C"; break;
                        case BeatState.OffBeat:
                            scoreIncrement     = 0;
                            beatStateText.text = "F";
                            StartCoroutine(WrongMove(direction));
                            moveCombo          = 0;
                            break;
                        default:
                            scoreIncrement     = 0;
                            beatStateText.text = "?";
                            break;
                    }
                    beatStateTextAnimator.Play("BeatStateText", -1, 0f);
                    scoreIncrement *= gameController.GetDifficultyMultiplier();
                }

                canHit = scoreIncrement * mult * (gameController.canStart ? 1 : 0);
                HitWeakestTriangle(canHit);

                if (autoMove)
                {
                    if (validMove) score += 25;
                    scoreIncText.text = "+25";
                }
                else
                {
                    if (validMove) score += (scoreIncrement + moveCombo) * mult;
                    scoreIncText.text = "+" + (scoreIncrement * mult).ToString();
                }

                scoreText.text = score.ToString();
                multTextAnimator.Play("MultText", -1, 0f);
                scoreIncTextAnimator.Play("ScoreIncText", -1, 0f);
                comboTextAnimator.Play("ComboText", -1, 0f);
                if (!gameController.lockAvarageAtMax) gameController.avarage += scoreIncrement;
                multText.text  = "x" + mult.ToString();
                comboText.text = moveCombo != 0 ? "x" + moveCombo.ToString() : "";
            }
        }
        // ── Path B: player is already outside the grid — nudge toward centre ──
        else if (position.x < gridBoundsPlayer[0] || position.x >= gridBoundsPlayer[1]
              || position.y < gridBoundsPlayer[2]  || position.y >= gridBoundsPlayer[3])
        {
            float currentDist = Vector2.Distance(position, new Vector2(4, 4));
            float newDist     = Vector2.Distance(newPosition, new Vector2(4, 4));
            if (newDist <= currentDist)
                Move(direction, pushed: true);
        }
        // ── Path C: valid timing but direction leads out of bounds ─────────────
        else
        {
            // Reflect the rebound off the crowd wall that was hit:
            // flip only the axis (or axes) that went out of bounds.
            // WrongMove will additionally clip the rebound if it would strike a
            // second wall from the player's current position.
            bool xOut = newPosition.x < gridBoundsPlayer[0] || newPosition.x >= gridBoundsPlayer[1];
            bool yOut = newPosition.y < gridBoundsPlayer[2] || newPosition.y >= gridBoundsPlayer[3];
            Vector2 reflectDir = new Vector2(xOut ? -direction.x : direction.x,
                                             yOut ? -direction.y : direction.y);
            StartCoroutine(WrongMove((Vector2)direction, reflectDir));
        }

        // ── Animation — single authority, always reached ──────────────────────
        // StartMoveAnimation() cancels any stale reset coroutine before starting
        // a fresh one, so an older coroutine can never force "idle" over a newer
        // move animation.
        Debug.Log("sagfadf");
        StartMoveAnimation();
        Debug.Log($"[PlayerAnimationChecks] StartMoveAnimation() called at end of Move()");
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

    private IEnumerator WrongMove(Vector2 direction, Vector2? reboundDirection = null)
    {
        //Making the player move back and forth for a wrong move
        rb.AddForce(direction * (200 * tileSize));

        yield return new WaitForSeconds(beatTimer.beatInterval/4);

        // Clip the rebound so it never pushes toward a second crowd wall.
        // This check uses the logical grid position, so it covers all callers
        // (crowd-bounce, off-beat penalty, etc.) without going through Move().
        Vector2 rebound = reboundDirection ?? -direction;
        Vector2Int reboundTarget = position + new Vector2Int(Mathf.RoundToInt(rebound.x), Mathf.RoundToInt(rebound.y));
        if (reboundTarget.x < gridBoundsPlayer[0] || reboundTarget.x >= gridBoundsPlayer[1]) rebound.x = 0;
        if (reboundTarget.y < gridBoundsPlayer[2] || reboundTarget.y >= gridBoundsPlayer[3]) rebound.y = 0;
        rb.AddForce(rebound * (200 * tileSize));
    }

    private IEnumerator ResetAnimation(string animationName)
    {
        Debug.Log($"[PlayerAnimationChecks] ResetAnimation('{animationName}') — takingDamage={takingDamage} animator={(animator == null ? "NULL" : animator.name)}");
        if(!takingDamage)PlayPlayerAnimation(animationName);
        else Debug.Log("[PlayerAnimationChecks] Animation SKIPPED — takingDamage is true");

        float animationLength = animationLengths.ContainsKey(animationName) ? animationLengths[animationName] : 0.4f;

        yield return new WaitForSeconds(animationLength);

        if(!takingDamage)PlayPlayerAnimation("idle");
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
        PlayPlayerAnimation("Player_Damage");
        takingDamage = true;

        yield return new WaitForSeconds(beatTimer.beatInterval);

        PlayPlayerAnimation("idle");
        takingDamage=false;
    }
    private IEnumerator HealTaken()
    {
        PlayPlayerAnimation("Player_Heal");
        takingDamage = true;

        yield return new WaitForSeconds(beatTimer.beatInterval);

        PlayPlayerAnimation("idle");
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

    /// <summary>
    /// Updates the player's facing direction in real-time (e.g. from a held joystick)
    /// without triggering a move.  The smooth rotation lerp in FixedUpdate handles the visual.
    /// </summary>
    public void SetFacingDirection(Vector2Int dir)
    {
        if      (dir == new Vector2Int( 0,  1)) targetRotation = Quaternion.Euler(0, 0,   0);
        else if (dir == new Vector2Int( 1,  1)) targetRotation = Quaternion.Euler(0, 0, 315);
        else if (dir == new Vector2Int( 1,  0)) targetRotation = Quaternion.Euler(0, 0, 270);
        else if (dir == new Vector2Int( 1, -1)) targetRotation = Quaternion.Euler(0, 0, 225);
        else if (dir == new Vector2Int( 0, -1)) targetRotation = Quaternion.Euler(0, 0, 180);
        else if (dir == new Vector2Int(-1, -1)) targetRotation = Quaternion.Euler(0, 0, 135);
        else if (dir == new Vector2Int(-1,  0)) targetRotation = Quaternion.Euler(0, 0,  90);
        else if (dir == new Vector2Int(-1,  1)) targetRotation = Quaternion.Euler(0, 0,  45);
    }

    public void HandleInput()
    {
        if (hasDied) return;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            SetFacingDirection(Vector2Int.up);
            Move(Vector2Int.up);
        }
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            SetFacingDirection(Vector2Int.down);
            Move(Vector2Int.down);
        }
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            SetFacingDirection(Vector2Int.left);
            Move(Vector2Int.left);
        }
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            SetFacingDirection(Vector2Int.right);
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
