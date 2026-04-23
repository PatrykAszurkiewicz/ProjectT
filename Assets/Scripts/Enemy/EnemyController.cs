using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyController : MonoBehaviour
{
    private EnemyStats stats;
    private Rigidbody2D rb;

    private bool isKnockedBack = false;
    private float knockbackTimer = 0f;

    [SerializeField] private float detectRange = 5f;
    private Transform coreTarget;
    private Transform currentTarget;

    [SerializeField] private float attackRange = 1.7f;
    [SerializeField] private float attackCooldown = 1f;
    private float attackTimer = 0f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float avoidDistance = 1f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Stuck Prevention")]
    [SerializeField] private float stuckCheckTime = 0.5f;
    [SerializeField] private float minMovementThreshold = 0.2f;

    [Header("Grappling")]
    private bool isBeingGrappled = false;
    private float grapplingEndTime = 0f;

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

    private bool isAttackingCycle = false;
    private EnemyAnimationController animController;

    // Knockback direct velocity
    private Vector2 knockbackVelocity;

    //  Attack timing state 
    private float attackCycleStartTime = -999f; // Time.time when current attack cycle began

    //  Resolved frame config (from EnemyData) 
    private int resolvedHitFrame;
    private int resolvedParryStart;
    private int resolvedParryEnd;

    /// The Time.time when the current attack cycle started.
    /// Used by ParryIndicator to synchronize timing with IsInParryWindow().
    public float AttackCycleStartTime => attackCycleStartTime;
    public bool IsAttacking => isAttackingCycle;

    //  Decoy lure system 
    private Transform decoyTarget;
    private bool isLuredByDecoy = false;

    // How close to the decoy the enemy will walk before stopping
    private const float DECOY_STOP_DISTANCE = 0.6f;

    public float GetAttackCooldown() => attackCooldown;

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        animController = GetComponent<EnemyAnimationController>();

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;

        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;

            bool isBoss = GetComponent<Boss1>() != null || GetComponent<BaseBossStats>() != null;
            ysort.sortYOffset = isBoss ? -1.0f : -0.2f;
        }

        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
            coreTarget = core.transform;

        currentTarget = coreTarget;
        InvokeRepeating(nameof(UpdateTarget), 0f, 0.5f);

        // Resolve frame config once at start
        ResolveFrameConfig();
    }

    private void OnDestroy()
    {
    }

    /// Reads frame config from EnemyData. All hit/parry frame configuration lives on the EnemyData ScriptableObject — one place, no duplication.
    private void ResolveFrameConfig()
    {
        EnemyData data = stats?.enemyData;

        if (data != null)
        {
            resolvedHitFrame = Mathf.Max(data.hitFrame, 0);
            resolvedParryStart = Mathf.Max(data.parryFrameStart, 0);
            resolvedParryEnd = Mathf.Max(data.parryFrameEnd, 0);
        }
        else
        {
            resolvedHitFrame = 0;
            resolvedParryStart = 0;
            resolvedParryEnd = 0;
        }

        if (resolvedParryEnd < resolvedParryStart)
            resolvedParryEnd = resolvedParryStart;
    }

    //  Decoy target API (called by DecoyDevice) 
    // Called by DecoyDevice to lure this enemy towards the decoy.
    // While lured, normal target selection is overridden.

    public void SetDecoyTarget(Transform decoy)
    {
        decoyTarget = decoy;
        isLuredByDecoy = true;
        currentTarget = decoy;
    }

    // Called by DecoyDevice when the decoy expires or is replaced.
    // Enemy returns to normal target selection.
    public void ClearDecoyTarget()
    {
        decoyTarget = null;
        isLuredByDecoy = false;
        currentTarget = coreTarget;
        UpdateTarget();
    }

    public bool IsLuredByDecoy() => isLuredByDecoy;

    public void SetGrapplingState(bool isGrappled, float duration = 2f)
    {
        isBeingGrappled = isGrappled;
        if (isGrappled)
            grapplingEndTime = Time.time + duration;
    }

    private void FixedUpdate()
    {
        if (isBeingGrappled && Time.time > grapplingEndTime)
            isBeingGrappled = false;

        if (isFrozen)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        // Parry stun — completely freeze movement
        if (GetComponent<ParryStunEffect>() != null)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        if (EnergyManager.Instance != null && EnergyManager.Instance.IsGameOver())
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        var boss1 = GetComponent<Boss1>();
        if (boss1 != null)
        {
            if (animController != null && animController.IsPlayingLaserAttack())
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                return;
            }
        }

        if (isBeingGrappled)
        {
            if (rb != null) rb.linearVelocity *= 0.95f;
            return;
        }

        // Knockback direct velocity with decay
        if (isKnockedBack)
        {
            if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                knockbackTimer -= Time.fixedDeltaTime;
                knockbackVelocity *= 0.82f;
                rb.linearVelocity = knockbackVelocity;

                if (knockbackTimer <= 0f)
                {
                    isKnockedBack = false;
                    rb.linearVelocity = Vector2.zero;
                }
            }
            else
            {
                isKnockedBack = false;
            }
            return;
        }

        // If lured by decoy, validate the decoy still exists
        if (isLuredByDecoy)
        {
            if (decoyTarget == null || decoyTarget.gameObject == null || !decoyTarget.gameObject.activeInHierarchy)
            {
                ClearDecoyTarget();
            }
            else
            {
                currentTarget = decoyTarget;
            }
        }

        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            if (!IsValidTarget(currentTarget))
                UpdateTarget();
            return;
        }

        float distance = Vector2.Distance(transform.position, currentTarget.position);

        // When lured by decoy, use a much tighter stop distance so they cluster around it
        if (isLuredByDecoy && currentTarget == decoyTarget)
        {
            if (distance <= DECOY_STOP_DISTANCE)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }
        }
        else if (distance <= attackRange || isAttackingCycle)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        HandleStuckDetection();
        Vector2 direction = (currentTarget.position - transform.position).normalized;
        direction = GetOptimalMovementDirection(direction);

        // Lured enemies move slightly slower (confused)
        float speedMultiplier = isLuredByDecoy ? 0.8f : 1f;
        rb.linearVelocity = direction.normalized * stats.MoveSpeed * speedMultiplier;
    }

    public bool IsBeingGrappled() => isBeingGrappled;

    private void HandleStuckDetection()
    {
        float distanceMoved = Vector2.Distance(transform.position, lastKnownPosition);

        if (distanceMoved > minMovementThreshold)
        {
            timeSinceLastMovement = 0f;
            lastKnownPosition = transform.position;
            isInStuckMode = false;
        }
        else
        {
            timeSinceLastMovement += Time.fixedDeltaTime;
            if (timeSinceLastMovement > stuckCheckTime && !isInStuckMode)
                EnterStuckMode();
        }

        if (isInStuckMode)
        {
            stuckModeTimer -= Time.fixedDeltaTime;
            if (stuckModeTimer <= 0f)
                isInStuckMode = false;
        }
    }

    private void EnterStuckMode()
    {
        isInStuckMode = true;
        stuckModeTimer = 2f;
        Vector2 toTarget = (currentTarget.position - transform.position).normalized;
        Vector2 perpendicular = new Vector2(-toTarget.y, toTarget.x);
        Vector2 leftSide = perpendicular;
        Vector2 rightSide = -perpendicular;

        bool leftClear = !Physics2D.OverlapCircle(
            transform.position + (Vector3)(leftSide * avoidDistance), 0.3f, obstacleLayer);
        bool rightClear = !Physics2D.OverlapCircle(
            transform.position + (Vector3)(rightSide * avoidDistance), 0.3f, obstacleLayer);

        if (leftClear && !rightClear) stuckAvoidanceDirection = leftSide;
        else if (rightClear && !leftClear) stuckAvoidanceDirection = rightSide;
        else stuckAvoidanceDirection = leftSide;
    }

    private Vector2 GetOptimalMovementDirection(Vector2 desiredDirection)
    {
        if (isInStuckMode) return stuckAvoidanceDirection;

        Collider2D obstacle = Physics2D.OverlapCircle(
            transform.position, avoidDistance, obstacleLayer);

        if (obstacle != null)
        {
            var tower = obstacle.GetComponent<Tower>();
            if (tower != null && tower.IsDestroyed())
                return desiredDirection;

            Vector2 obstaclePos = obstacle.transform.position;
            Vector2 toObstacle = (obstaclePos - (Vector2)transform.position).normalized;
            Vector2 perpLeft = new Vector2(-toObstacle.y, toObstacle.x);
            Vector2 perpRight = new Vector2(toObstacle.y, -toObstacle.x);
            float leftDot = Vector2.Dot(perpLeft, desiredDirection);
            float rightDot = Vector2.Dot(perpRight, desiredDirection);
            Vector2 chosenDirection = (leftDot > rightDot) ? perpLeft : perpRight;
            return Vector2.Lerp(chosenDirection, desiredDirection, 0.3f);
        }

        return desiredDirection;
    }

    private void UpdateTarget()
    {
        // If lured by decoy, don't change target
        if (isLuredByDecoy && decoyTarget != null && decoyTarget.gameObject != null && decoyTarget.gameObject.activeInHierarchy)
        {
            currentTarget = decoyTarget;
            return;
        }

        // If decoy reference went stale, clear it
        if (isLuredByDecoy)
            ClearDecoyTarget();

        if (currentTarget != null &&
            (currentTarget.gameObject == null || !currentTarget.gameObject.activeInHierarchy))
            currentTarget = null;

        if (coreTarget == null) return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player && Vector2.Distance(transform.position, player.transform.position) < detectRange)
        {
            currentTarget = player.transform;
            return;
        }

        GameObject[] towers = GameObject.FindGameObjectsWithTag("Tower");
        float closestDist = Mathf.Infinity;
        Transform closestTower = null;

        foreach (var tower in towers)
        {
            if (tower == null || !tower.activeInHierarchy) continue;
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

        currentTarget = coreTarget;
    }

    private void Update()
    {
        if (isFrozen)
        {
            freezeTimeRemaining -= Time.deltaTime;
            if (freezeTimeRemaining <= 0f)
                UnfreezeEnemy();
        }

        if (isKnockedBack)
        {
            knockbackTimer -= Time.deltaTime;
            if (knockbackTimer <= 0f)
                isKnockedBack = false;
        }

        if (currentTarget != null && !IsValidTarget(currentTarget))
        {
            currentTarget = coreTarget;
            return;
        }

        if (currentTarget != null && !isFrozen && !isAttackingCycle
            && GetComponent<ParryStunEffect>() == null
            && (EnergyManager.Instance == null || !EnergyManager.Instance.IsGameOver()))
        {
            float distance = Vector2.Distance(transform.position, currentTarget.position);

            if (distance <= attackRange)
            {
                // Don't attack the decoy — just mill around it
                if (isLuredByDecoy && currentTarget == decoyTarget)
                    return;

                attackTimer -= Time.deltaTime;
                if (attackTimer <= 0f)
                {
                    StartCoroutine(AttackCycle(currentTarget));
                }
            }
            else
            {
                attackTimer = 0f;
            }
        }
    }

    private IEnumerator AttackCycle(Transform target)
    {
        isAttackingCycle = true;
        attackCycleStartTime = Time.time;
        bool hitDelivered = false;

        // Calculate attack timing from EnemyData
        float animDuration = 0f;
        float animSpeed = 0f;
        if (stats != null && stats.enemyData != null)
        {
            animDuration = stats.enemyData.AttackDuration;
            animSpeed = stats.enemyData.AttackAnimSpeed;
        }

        if (animController != null && resolvedHitFrame > 0)
        {
            // Frame-driven hit: the animation coroutine calls PerformHit
            // synchronously when it reaches the hit frame. One coroutine,
            // one timeline, no drift between sprite and damage.
            animController.PlayMeleeAttackAnimation(resolvedHitFrame, () =>
            {
                if (!hitDelivered && target != null)
                {
                    hitDelivered = true;
                    PerformHit(target);
                }
            });

            // Safety fallback: if animation gets interrupted (freeze, stun, death)
            // and the callback never fired, deliver hit at the expected time via timer.
            float hitDelay = animSpeed * resolvedHitFrame;
            yield return new WaitForSeconds(hitDelay);

            if (!hitDelivered && target != null)
            {
                hitDelivered = true;
                PerformHit(target);
            }

            // Wait remaining time
            //float remainingWait = Mathf.Max(animDuration, attackCooldown) - hitDelay;
            float remainingWait = animDuration - hitDelay;

            if (remainingWait > 0f)
                yield return new WaitForSeconds(remainingWait);
        }
        else
        {
            // Instant damage at animation start (hitFrame = 0)
            if (animController != null)
                animController.PlayMeleeAttackAnimation();
            PerformHit(target);
            hitDelivered = true;

            float waitTime = Mathf.Max(animDuration, attackCooldown);
            yield return new WaitForSeconds(waitTime);
        }

        if (animController != null)
            animController.StopMeleeAttackAnimation();

        isAttackingCycle = false;
        attackTimer = attackCooldown;

    }

    // Returns true if the player's parry attempt overlaps this enemy's parry window for the current attack cycle.
    // A parry succeeds if EITHER: The shield was RAISED (right-click pressed) during the parry frames, OR The shield is currently held AND the hit lands during the parry frames.
    // Called by ShieldSystem.TryBlockOrParry().

    public bool IsInParryWindow(float shieldRaiseTime)
    {
        if (!isAttackingCycle || attackCycleStartTime < 0f) return false;
        if (stats == null || stats.enemyData == null) return false;

        float animSpeed = stats.enemyData.AttackAnimSpeed;
        if (animSpeed <= 0f) return false;

        int pStart = resolvedParryStart;
        int pEnd = resolvedParryEnd;
        int hit = resolvedHitFrame;

        // If nothing configured, use a default 0.2s window before hit
        if (pStart == 0 && pEnd == 0 && hit == 0)
        {
            // Fallback: parry if shield was raised within 0.2s
            return (Time.time - shieldRaiseTime) <= 0.2f;
        }

        // Calculate absolute times for parry window
        float parryWindowStart = attackCycleStartTime + pStart * animSpeed;
        float parryWindowEnd = attackCycleStartTime + (pEnd + 1) * animSpeed;

        // (a) Shield was pressed (raised) during the parry window
        bool raisedDuringWindow = shieldRaiseTime >= parryWindowStart && shieldRaiseTime <= parryWindowEnd;

        // Parry = shield was PRESSED (raised) during the parry window.
        // Holding shield from before the window is just a block, not a parry.
        bool isParry = raisedDuringWindow;

        Debug.Log($"[PARRY CHECK] {gameObject.name}: shieldRaise={shieldRaiseTime:F3} now={Time.time:F3} " +
                  $"parryWindow=[{parryWindowStart:F3}-{parryWindowEnd:F3}] " +
                  $"frames={pStart}-{pEnd} hit={hit} " +
                  $"raisedDuring={raisedDuringWindow} => {(isParry ? "PARRY!" : "BLOCK")}");

        return isParry;
    }

    // Returns true if this enemy is currently mid-attack and the current time falls within its parry frames. 

    public bool IsCurrentlyInParryFrames()
    {
        if (!isAttackingCycle || attackCycleStartTime < 0f) return false;
        if (stats == null || stats.enemyData == null) return false;

        float animSpeed = stats.enemyData.AttackAnimSpeed;
        if (animSpeed <= 0f) return false;

        int pStart = resolvedParryStart;
        int pEnd = resolvedParryEnd;
        int hit = resolvedHitFrame;

        if (pStart == 0 && pEnd == 0 && hit == 0) return false;

        float parryWindowStart = attackCycleStartTime + pStart * animSpeed;
        float parryWindowEnd = attackCycleStartTime + (pEnd + 1) * animSpeed;

        return Time.time >= parryWindowStart && Time.time <= parryWindowEnd;
    }

    private void PerformHit(Transform target)
    {
        PlayAttackSound();

        // Boss1 plays an additional ground-hit sound on melee connect
        var boss1 = GetComponent<Boss1>();
        if (boss1 != null)
            boss1.PlayGroundHitSound();

        ApplyDamageToTarget(target);
    }

    public void ApplyDamageToTarget(Transform target)
    {
        if (target == null) return;

        //  Shield block / parry check 
        // If the target is the player and they have an active shield, check blocking.
        var playerStats = target.GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            // Check for parry stun — if this enemy is stunned, skip the attack entirely
            var parryStun = GetComponent<ParryStunEffect>();
            if (parryStun != null) return;

            var weapon = target.GetComponentInChildren<Weapon>();
            if (weapon != null)
            {
                var shield = weapon.GetShieldSystem();
                if (shield != null && shield.TryBlockOrParry(gameObject))
                    return; // Attack was blocked or parried — no damage applied
            }
        }

        var stats = target.GetComponent<CharacterStats>();
        if (stats != null)
        {
            float damageAmount = this.stats.Damage;
            stats.TakeDamage(damageAmount);

            if (playerStats != null)
            {
                CombatFeel.OnPlayerHurt();

                var reflectionEffect = playerStats.GetComponent<DamageReflectionEffect>();
                if (reflectionEffect != null)
                    reflectionEffect.ReflectDamage(damageAmount, gameObject);

                var iceArmorEffect = playerStats.GetComponent<IceArmorEffect>();
                if (iceArmorEffect != null)
                    iceArmorEffect.FreezeAttacker(gameObject);
            }

            if (stats.IsDead())
                currentTarget = coreTarget;

            return;
        }

        var consumer = target.GetComponent<IEnergyConsumer>();
        if (consumer != null && EnergyManager.Instance != null)
        {
            bool wasDestroyed = EnergyManager.Instance.DamageEnergyConsumer(
                consumer, this.stats.Damage, gameObject);

            if (wasDestroyed)
            {
                currentTarget = coreTarget;
                if (rb != null && !isKnockedBack)
                    rb.linearVelocity = Vector2.zero;
            }
        }
    }

    private void PlayAttackSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(
                FMODEvents.instance.enemyAttack, transform.position);
    }

    public void ApplyFreeze(float duration)
    {
        isFrozen = true;
        freezeTimeRemaining = duration;
        if (spriteRenderer != null)
            spriteRenderer.color = Color.cyan;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    private void UnfreezeEnemy()
    {
        isFrozen = false;
        freezeTimeRemaining = 0f;
        if (spriteRenderer != null)
            spriteRenderer.color = originalColor;
    }

    private bool IsValidTarget(Transform target)
    {
        if (target == null || target.gameObject == null || !target.gameObject.activeInHierarchy)
            return false;
        var tower = target.GetComponent<Tower>();
        if (tower != null && tower.IsDestroyed())
            return false;
        return true;
    }

    /// SAFE knockback — checks rigidbody type before setting velocity.
    /// Bosses with static/kinematic bodies won't crash.
    public void ApplyKnockback(Vector2 direction, float force, float duration = 0.25f)
    {
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
        {
            //Debug.Log($"[CombatFeel] KNOCKBACK SKIPPED on {gameObject.name} (non-dynamic rigidbody)");
            return;
        }

        isKnockedBack = true;
        knockbackTimer = duration;
        knockbackVelocity = direction.normalized * force;
        rb.linearVelocity = knockbackVelocity;
        //Debug.Log($"[CombatFeel] KNOCKBACK {gameObject.name} dir={direction} force={force}");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (currentTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, avoidDistance);
    }

}
