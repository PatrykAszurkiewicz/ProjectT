using UnityEngine;

public class EnemyController : MonoBehaviour
{
    private EnemyStats stats;
    private Rigidbody2D rb;

    private bool isKnockedBack = false;
    private float knockbackTimer = 0f;
    //Target finder
    [SerializeField] private float detectRange = 5f;
    private Transform coreTarget;
    private Transform currentTarget;
    //Attack
    [SerializeField] private float attackRange = 1.7f;
    [SerializeField] private float attackCooldown = 1f;
    private float attackTimer = 0f;
    // Obstacle avoidance
    [Header("Obstacle Avoidance")]
    [SerializeField] private float avoidDistance = 1f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Stuck Prevention")]
    [SerializeField] private float stuckCheckTime = 0.5f;
    [SerializeField] private float minMovementThreshold = 0.2f;

    [Header("Grappling")]
    private bool isBeingGrappled = false;
    private float grapplingEndTime = 0f;

    // Private fields for stuck detection
    private Vector2 lastKnownPosition;
    private float timeSinceLastMovement = 0f;
    private bool isInStuckMode = false;
    private float stuckModeTimer = 0f;
    private Vector2 stuckAvoidanceDirection;


    [Header("Freeze System")]
    private bool isFrozen = false;
    private float freezeTimeRemaining = 0f;
    private Color originalColor;
    private SpriteRenderer spriteRenderer;



    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();

        // Freeze system
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;

        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
            coreTarget = core.transform;

        currentTarget = coreTarget;

        InvokeRepeating(nameof(UpdateTarget), 0f, 0.5f);
    }

    public void SetGrapplingState(bool isGrappled, float duration = 2f)
    {
        isBeingGrappled = isGrappled;
        if (isGrappled)
        {
            grapplingEndTime = Time.time + duration;
            //Debug.Log($"{gameObject.name} is now being grappled for {duration} seconds");
        }
        else
        {
            //Debug.Log($"{gameObject.name} grappling ended");
        }
    }


    private void FixedUpdate()
    {
        // Check if grappling has ended
        if (isBeingGrappled && Time.time > grapplingEndTime)
        {
            isBeingGrappled = false;
        }

        // ADD FREEZE CHECK - Don't move while frozen
        if (isFrozen)
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero; // Stop movement while frozen
            }
            return;
        }

        // Don't apply pathfinding movement while being grappled
        if (isBeingGrappled)
        {
            if (rb != null)
            {
                rb.linearVelocity *= 0.95f;
            }
            return;
        }

        // Existing movement logic continues unchanged...
        if (currentTarget == null || isKnockedBack || !IsValidTarget(currentTarget))
        {
            if (!isKnockedBack && !IsValidTarget(currentTarget))
            {
                UpdateTarget();
            }
            return;
        }

        float distance = Vector2.Distance(transform.position, currentTarget.position);

        if (distance <= attackRange)
        {
            if (!isKnockedBack)
            {
                rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        HandleStuckDetection();
        Vector2 direction = (currentTarget.position - transform.position).normalized;
        direction = GetOptimalMovementDirection(direction);

        rb.linearVelocity = direction.normalized * stats.MoveSpeed;
    }

    public bool IsBeingGrappled() => isBeingGrappled;
    private void HandleStuckDetection()
    {
        // Check if we've moved significantly
        float distanceMoved = Vector2.Distance(transform.position, lastKnownPosition);

        if (distanceMoved > minMovementThreshold)
        {
            // If moving, reset stuck detection
            timeSinceLastMovement = 0f;
            lastKnownPosition = transform.position;
            isInStuckMode = false;
        }
        else
        {
            timeSinceLastMovement += Time.fixedDeltaTime;
            if (timeSinceLastMovement > stuckCheckTime && !isInStuckMode)
            {
                EnterStuckMode();
            }
        }

        // Handle stuck mode timer
        if (isInStuckMode)
        {
            stuckModeTimer -= Time.fixedDeltaTime;
            if (stuckModeTimer <= 0f)
            {
                isInStuckMode = false;
            }
        }
    }

    private void EnterStuckMode()
    {
        isInStuckMode = true;
        stuckModeTimer = 2f; // Try to unstuck for 2 seconds
        // Pick a direction perpendicular to our current target direction
        Vector2 toTarget = (currentTarget.position - transform.position).normalized;
        Vector2 perpendicular = new Vector2(-toTarget.y, toTarget.x); // 90 degrees
        // Choose left or right based on which side is more clear
        Vector2 leftSide = perpendicular;
        Vector2 rightSide = -perpendicular;

        bool leftClear = !Physics2D.OverlapCircle(transform.position + (Vector3)(leftSide * avoidDistance), 0.3f, obstacleLayer);
        bool rightClear = !Physics2D.OverlapCircle(transform.position + (Vector3)(rightSide * avoidDistance), 0.3f, obstacleLayer);

        if (leftClear && !rightClear)
            stuckAvoidanceDirection = leftSide;
        else if (rightClear && !leftClear)
            stuckAvoidanceDirection = rightSide;
        else
            stuckAvoidanceDirection = leftSide; // Default to left if both or neither are clear
        //Debug.Log($"Enemy entering stuck mode, trying direction: {stuckAvoidanceDirection}");
    }

    private Vector2 GetOptimalMovementDirection(Vector2 desiredDirection)
    {
        if (isInStuckMode)
        {
            return stuckAvoidanceDirection;
        }

        Collider2D obstacle = Physics2D.OverlapCircle(transform.position, avoidDistance, obstacleLayer);

        if (obstacle != null)
        {
            // Additional obstacle check for destroyed towers
            var tower = obstacle.GetComponent<Tower>();
            if (tower != null && tower.IsDestroyed())
            {
                // Move through destroyed towers
                return desiredDirection;
            }
            Vector2 obstaclePos = obstacle.transform.position;
            Vector2 toObstacle = (obstaclePos - (Vector2)transform.position).normalized;
            // Create two perpendicular directions
            Vector2 perpLeft = new Vector2(-toObstacle.y, toObstacle.x);
            Vector2 perpRight = new Vector2(toObstacle.y, -toObstacle.x);
            // Choose the perpendicular direction that's more aligned with our desired direction
            float leftDot = Vector2.Dot(perpLeft, desiredDirection);
            float rightDot = Vector2.Dot(perpRight, desiredDirection);
            Vector2 chosenDirection = (leftDot > rightDot) ? perpLeft : perpRight;
            // Blend with desired direction for smoother movement
            return Vector2.Lerp(chosenDirection, desiredDirection, 0.3f);
        }

        return desiredDirection;
    }


    private void UpdateTarget()
    {
        // Clear invalid targets first
        if (currentTarget != null && (currentTarget.gameObject == null || !currentTarget.gameObject.activeInHierarchy))
        {
            currentTarget = null;
        }

        if (coreTarget == null) return;

        // Find Player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player && Vector2.Distance(transform.position, player.transform.position) < detectRange)
        {
            currentTarget = player.transform;
            return;
        }

        // Find closest turret (with validation)
        GameObject[] towers = GameObject.FindGameObjectsWithTag("Tower");
        float closestDist = Mathf.Infinity;
        Transform closestTower = null;

        foreach (var tower in towers)
        {
            // Validate tower exists and is operational
            if (tower == null || !tower.activeInHierarchy) continue;

            // Check if tower is actually destroyed (has no energy)
            var towerComponent = tower.GetComponent<Tower>();
            if (towerComponent != null && towerComponent.IsDestroyed()) continue;

            float dist = Vector2.Distance(transform.position, tower.transform.position);
            if (dist < closestDist && dist < detectRange)
            {
                closestDist = dist;
                closestTower = tower.transform;
            }
        }
        if (closestTower != null)
        {
            currentTarget = closestTower;
            return;
        }

        // Default target (Central Core)
        currentTarget = coreTarget;
    }


    private void Update()
    {

        if (isFrozen)
        {
            freezeTimeRemaining -= Time.deltaTime;
            if (freezeTimeRemaining <= 0f)
            {
                UnfreezeEnemy();
            }
        }

        // Handle knockback timer
        if (isKnockedBack)
        {
            knockbackTimer -= Time.deltaTime;
            if (knockbackTimer <= 0f)
            {
                isKnockedBack = false;
            }
        }

        // Validate target before attacking
        if (currentTarget != null && !IsValidTarget(currentTarget))
        {
            currentTarget = coreTarget; // Fallback to core
            return;
        }

        // Handle attacking - DON'T attack while frozen
        if (currentTarget != null && !isFrozen)
        {
            float distance = Vector2.Distance(transform.position, currentTarget.position);

            if (distance <= attackRange)
            {
                attackTimer -= Time.deltaTime;

                if (attackTimer <= 0f)
                {
                    Attack(currentTarget);
                    attackTimer = attackCooldown;
                }

                return;
            }
        }
        attackTimer = 0f;
    }

    public void ApplyFreeze(float duration)
    {
        isFrozen = true;
        freezeTimeRemaining = duration;

        // Visual feedback - blue tint for frozen
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.cyan;
        }

        // Stop movement immediately
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        Debug.Log($"Enemy {gameObject.name} frozen for {duration} seconds");
    }

    private void UnfreezeEnemy()
    {
        isFrozen = false;
        freezeTimeRemaining = 0f;

        // Restore original color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        Debug.Log($"Enemy {gameObject.name} unfrozen");
    }


    // Helper method to validate if a target is still valid
    private bool IsValidTarget(Transform target)
    {
        if (target == null || target.gameObject == null || !target.gameObject.activeInHierarchy)
            return false;

        // Special validation for towers only
        var tower = target.GetComponent<Tower>();
        if (tower != null && tower.IsDestroyed())
            return false;

        return true;
    }


    private void Attack(Transform target)
    {
        PlayAttackSound();

        var stats = target.GetComponent<CharacterStats>();
        if (stats != null)
        {
            // Store damage amount before applying
            float damageAmount = this.stats.Damage;

            stats.TakeDamage(damageAmount);

            // Check for damage reflection on player
            var playerStats = stats as PlayerStats;
            if (playerStats != null)
            {
                var reflectionEffect = playerStats.GetComponent<DamageReflectionEffect>();
                if (reflectionEffect != null)
                {
                    reflectionEffect.ReflectDamage(damageAmount, gameObject);
                }

                // Check for ice armor on player
                var iceArmorEffect = playerStats.GetComponent<IceArmorEffect>();
                if (iceArmorEffect != null)
                {
                    iceArmorEffect.FreezeAttacker(gameObject);
                }
            }

            if (stats.IsDead())
            {
                currentTarget = coreTarget;
            }
            return;
        }

        var consumer = target.GetComponent<IEnergyConsumer>();
        if (consumer != null && EnergyManager.Instance != null)
        {
            bool wasDestroyed = EnergyManager.Instance.DamageEnergyConsumer(consumer, this.stats.Damage, gameObject);

            if (wasDestroyed)
            {
                currentTarget = coreTarget;
                if (rb != null && !isKnockedBack)
                {
                    rb.linearVelocity = Vector2.zero;
                }
            }
        }
    }

    private void PlayAttackSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.enemyAttack, transform.position);
        }
    }

    public void ApplyKnockback(Vector2 direction, float force, float duration = 0.2f)
    {
        isKnockedBack = true;
        knockbackTimer = duration;
        rb.AddForce(direction.normalized * force, ForceMode2D.Impulse);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Draw line to the current target
        if (currentTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }
        // Avoid obstacle
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, avoidDistance);
    }
}