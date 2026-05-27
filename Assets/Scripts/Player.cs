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
    /// <summary>Set to true by IntroSequenceController during the walk so bounds-clamping is skipped.</summary>
    public bool introMode = false;
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
    // Tracks the active WrongMove coroutine so its pending rebound can be cancelled
    // when a new move fires — prevents a stale rebound from landing on a fresh force.
    private Coroutine _wrongMoveCoroutine;

    // ── Force lock ────────────────────────────────────────────────────────────
    // Only one rb.AddForce displacement is allowed at a time. Once Move() begins
    // executing, _forceLocked is true for beatInterval/2. Any concurrent Move()
    // call returns immediately (position unchanged, no score, no force).
    private bool _forceLocked = false;
    private Coroutine _forceLockCoroutine;
    [SerializeField] private float calibrationForceLockSeconds = 0.08f;

    private void StartForceLock(float duration)
    {
        if (_forceLockCoroutine != null) StopCoroutine(_forceLockCoroutine);
        _forceLockCoroutine = StartCoroutine(ForceLockTimer(duration));
        _forceLocked = true;
    }

    private IEnumerator ForceLockTimer(float duration)
    {
        yield return new WaitForSecondsRealtime(duration);
        _forceLocked = false;
    }
    // ─────────────────────────────────────────────────────────────────────────

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

        // Velocity-idle drift correction: once no force is in flight, the rigidbody
        // is nearly at rest, and the visual position has drifted from the logical grid
        // tile, snap back silently. Animations always finish first because the snap
        // only fires after physics has settled — no timer needed.
        // Threshold is tight (0.01 sqrMag = 0.1 units/s) because the pre-move snap in
        // Move() is now the primary corrector — this is the safety net.
        if (rb != null && !_forceLocked && !takingDamage && !introMode)
        {
            Vector2 logicalPos = new Vector2(position.x * tileSize, position.y * tileSize);
            if (rb.velocity.sqrMagnitude < 0.01f && Vector2.Distance(rb.position, logicalPos) > 0.05f)
            {
                rb.velocity = Vector2.zero;
                rb.position = logicalPos;
            }
        }
    }

    private int moveCount=0;  
    private int mult;
    private int canHit;
    private int moveCombo=0;
    public void Move(Vector2Int direction, bool pushed=false, BeatState? overrideState = null, bool autoMove = false)
    {
        // Force lock: drop this call entirely if a displacement is already in progress.
        // Prevents stacking forces from multiple enemies, crowd bounces, and player input.
        // During calibration use a short lock to absorb duplicate touch/mouse callbacks
        // from a single press while still allowing rapid free movement.
        if (_forceLocked) return;
        float lockDuration = gameController.inCalibration
            ? calibrationForceLockSeconds
            : beatTimer.beatInterval / 2f;
        StartForceLock(lockDuration);

        // ── Pre-move rb reset ─────────────────────────────────────────────────
        // Zero velocity and snap the Rigidbody to the current logical grid tile before
        // applying any new force. Prevents physics overshoots from compounding into
        // visible desync across rapid-input sessions. Skipped during introMode so the
        // intro walk controller stays in charge of the Rigidbody.
        // Also cancels any pending WrongMove rebound — the new move supersedes it.
        if (_wrongMoveCoroutine != null) { StopCoroutine(_wrongMoveCoroutine); _wrongMoveCoroutine = null; }
        if (!introMode && rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.position = new Vector2(position.x * tileSize, position.y * tileSize);
        }
        // ─────────────────────────────────────────────────────────────────────

        Debug.Log($"[PlayerAnimationChecks] Move() called — dir={direction} pushed={pushed} autoMove={autoMove} overrideState={overrideState}");

        // ── State ─────────────────────────────────────────────────────────────
        State            = (overrideState.HasValue && !pushed) ? overrideState.Value : beatTimer.state;
        currentDirection = direction;
        crowdPushFlag    = pushed;

        // Drift correction: record the signed beat offset for voluntary, non-pushed moves
        if (!pushed && !autoMove)
        {
            gameController.RecordMoveOffset(beatTimer.GetSignedBeatOffset(), State);
            // During PYM calibration every voluntary move counts as a calibration tap
            if (gameController.inCalibration)
                gameController.RecordCalibrationTap();
        }

        // Pushed and auto-moves always succeed; normal moves require good timing
        // and a reasonable move count within the beat window.
        bool validMove = pushed || autoMove || gameController.inCalibration || !(State == BeatState.OffBeat || moveCount > 2);

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
                Vector2Int preMovePosition = position;
                position = newPosition;
                // Clamp so no movement — including crowd pushes — can leave the
                // logical position outside the allowed playfield.
                // Skip clamping during the intro walk (player is outside the arena).
                if (!introMode)
                {
                    position.x = Mathf.Clamp(position.x, gridBoundsPlayer[0], gridBoundsPlayer[1] - 1);
                    position.y = Mathf.Clamp(position.y, gridBoundsPlayer[2], gridBoundsPlayer[3] - 1);
                }
                // Phase 3: only apply force on axes that actually moved after clamping,
                // so a crowd-push that gets clamped never fires force toward the wall.
                Vector2Int actualDelta = position - preMovePosition;
                Vector2 forceDir = new Vector2(
                    actualDelta.x != 0 ? direction.x : 0,
                    actualDelta.y != 0 ? direction.y : 0);
                rb.AddForce(forceDir * (200 * tileSize));
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
                            _wrongMoveCoroutine = StartCoroutine(WrongMove(direction));
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
            // Previously called Move(direction, pushed:true) here, which was always silently
            // dropped — _forceLocked was set at the top of this very Move() call. Now uses
            // ApplyInternalPush which bypasses the lock and actually fires the rescue push.
            if (newDist <= currentDist)
                ApplyInternalPush(direction);
        }
        // ── Path C: valid timing but direction leads out of bounds ─────────────
        else
        {
            bool xOut = newPosition.x < gridBoundsPlayer[0] || newPosition.x >= gridBoundsPlayer[1];
            bool yOut = newPosition.y < gridBoundsPlayer[2] || newPosition.y >= gridBoundsPlayer[3];
            // Slide visually toward the crowd the player tried to enter.
            rb.AddForce((Vector2)direction * (200 * tileSize));
            if (xOut && yOut)
            {
                // True corner hit: both axes lead into crowd.
                // Fire a full rebound force on both axes (same timing as single-wall)
                // so the player visually bounces off the corner.
                Vector2 reboundForce = new Vector2(
                    -direction.x * (200 * tileSize),
                    -direction.y * (200 * tileSize));
                StartCoroutine(DelayedForce(reboundForce, beatTimer.beatInterval / 4f, bypassLock: true));
            }
            else
            {
                // Single-wall hit: only rebound the axis that was blocked.
                // The unobstructed axis gets zero rebound so the bounce never carries
                // the player into a perpendicular crowd wall.
                // Update the logical position on the free axis so it matches where
                // the player actually ends up after the visual slide.
                if (!xOut) position.x = Mathf.Clamp(position.x + direction.x, gridBoundsPlayer[0], gridBoundsPlayer[1] - 1);
                if (!yOut) position.y = Mathf.Clamp(position.y + direction.y, gridBoundsPlayer[2], gridBoundsPlayer[3] - 1);

                Vector2 reboundForce = new Vector2(
                    xOut ? -direction.x * (200 * tileSize) : 0f,
                    yOut ? -direction.y * (200 * tileSize) : 0f);
                if (reboundForce != Vector2.zero)
                    StartCoroutine(DelayedForce(reboundForce, beatTimer.beatInterval / 4f, bypassLock: true));
            }
        }

        // ── Animation — single authority, always reached ──────────────────────
        // StartMoveAnimation() cancels any stale reset coroutine before starting
        // a fresh one, so an older coroutine can never force "idle" over a newer
        // move animation.
        Debug.Log("sagfadf");
        StartMoveAnimation();
        Debug.Log($"[PlayerAnimationChecks] StartMoveAnimation() called at end of Move()");
        // Debug visualizer: keep the black tile in sync with the logical position.
        gameController?.UpdateDebugTilePos(position);
    }

    private void ResetMove()
    {
        moveCount = 0;
    }

    /// <summary>Updates logical position one tile in <paramref name="dir"/> (clamped to
    /// the playfield), snaps the Rigidbody to the new tile, and fires a displacement
    /// force — all without touching the force lock. For internal rescues only
    /// (Path B and CheckIfOutside). Never call from player input paths.</summary>
    private void ApplyInternalPush(Vector2Int dir)
    {
        position += dir;
        position.x = Mathf.Clamp(position.x, gridBoundsPlayer[0], gridBoundsPlayer[1] - 1);
        position.y = Mathf.Clamp(position.y, gridBoundsPlayer[2], gridBoundsPlayer[3] - 1);
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.position = new Vector2(position.x * tileSize, position.y * tileSize);
            rb.AddForce((Vector2)dir * (200 * tileSize));
        }
        gameController?.UpdateDebugTilePos(position);
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
        // Don't fight an ongoing knockback push — the push force is already in flight
        // and a rebound here would cancel it, leaving the rb at the old tile while
        // the logical position has already moved to the pushed tile.
        if (takingDamage) yield break;

        // Always apply force in the full attempted direction — even toward the crowd —
        // so the player visually bumps in that direction before rebounding back,
        // matching the feel of a valid crowd-wall hit (Path C).
        rb.AddForce((Vector2)direction * (200 * tileSize));

        yield return new WaitForSeconds(beatTimer.beatInterval / 4f);

        // Clip the rebound so it never pushes toward a second crowd wall.
        Vector2 rebound = reboundDirection ?? -direction;
        Vector2Int reboundTarget = position + new Vector2Int(Mathf.RoundToInt(rebound.x), Mathf.RoundToInt(rebound.y));
        if (reboundTarget.x < gridBoundsPlayer[0] || reboundTarget.x >= gridBoundsPlayer[1]) rebound.x = 0;
        if (reboundTarget.y < gridBoundsPlayer[2] || reboundTarget.y >= gridBoundsPlayer[3]) rebound.y = 0;
        if (rebound != Vector2.zero)
            rb.AddForce(rebound * (200 * tileSize));
    }

    /// <summary>Waits <paramref name="delay"/> seconds then applies a raw physics force.
    /// Used for crowd-wall bounce-back — logical position is intentionally NOT changed
    /// so the player stays on their current tile after the visual bounce.
    /// Pass <paramref name="bypassLock"/> = true for the second phase of a crowd bounce
    /// so the rebound fires even while the force lock is active.</summary>
    private IEnumerator DelayedForce(Vector2 force, float delay, bool bypassLock = false)
    {
        yield return new WaitForSeconds(delay);
        if (!bypassLock && _forceLocked) yield break;
        rb.AddForce(force);
    }

    /// <summary>Waits <paramref name="delay"/> seconds then zeroes velocity and snaps the
    /// Rigidbody to <paramref name="targetPos"/>. Used for corner bounces where force-based
    /// reflection would push the player away from the corner tile.</summary>
    private IEnumerator DelayedSnap(Vector2 targetPos, float delay)
    {
        yield return new WaitForSeconds(delay);
        rb.velocity = Vector2.zero;
        rb.position = targetPos; // direct set — takes effect immediately, not deferred to FixedUpdate
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
    // Phase 4: hitDir is the direction to push the player away from the enemy;
    // falls back to -currentDirection when not provided (e.g. non-triangle damage).
    public void TakeDamage(int damage)
    {
        if(!takingDamage)
        {
            health -= damage;
            gameController.LessNodders(40);
            healthSlider.value = health;
            if (health <= 0 && !hasDied)
            {
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

    // Triangles that have already dealt damage this contact; cleared when they separate.
    private HashSet<Triangle> _immuneTriangles = new HashSet<Triangle>();

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
                // Only deal damage if this triangle hasn't hit the player since they last
                // separated — prevents repeated hits while they are overlapping.
                if (!_immuneTriangles.Contains(triangle))
                {
                    _immuneTriangles.Add(triangle);
                    TakeDamage(triangle.powerLevel);
                }
            }
        }

        if(other.CompareTag("Heart"))
        {
            gameController.CollectHeart(other.gameObject);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if(other.CompareTag("Triangle"))
        {
            Triangle triangle;
            if(Triangle.cachedTriangles.TryGetValue(other.gameObject, out triangle))
            {
                // Player and triangle have separated — allow damage again on re-contact.
                _immuneTriangles.Remove(triangle);
            }
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
        // Previously: loops of Move(dir, pushed:true) — only the first iteration ever
        // fired because _forceLocked was already set by the outer Move() that triggered
        // ChangeGridBounds. Now: compute the clamped target in one step, snap the rb
        // directly, and fire a single visual impulse. Handles multi-tile corrections.
        int targetX = Mathf.Clamp(position.x, gridBoundsPlayer[0], gridBoundsPlayer[1] - 1);
        int targetY = Mathf.Clamp(position.y, gridBoundsPlayer[2], gridBoundsPlayer[3] - 1);
        if (targetX == position.x && targetY == position.y) return;

        // Unit-vector push direction so the visual impulse always has the same magnitude
        // regardless of how many tiles needed correcting.
        Vector2Int pushDir = new Vector2Int(
            targetX != position.x ? (int)Mathf.Sign(targetX - position.x) : 0,
            targetY != position.y ? (int)Mathf.Sign(targetY - position.y) : 0);

        position = new Vector2Int(targetX, targetY);
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.position = new Vector2(position.x * tileSize, position.y * tileSize);
            rb.AddForce((Vector2)pushDir * (200 * tileSize));
        }
        gameController?.UpdateDebugTilePos(position);
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
