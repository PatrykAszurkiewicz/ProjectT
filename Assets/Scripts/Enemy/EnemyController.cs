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

    // Cached reference to animation controller
    private EnemyAnimationController animController;

    public float GetAttackCooldown() => attackCooldown;

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        animController = GetComponent<EnemyAnimationController>();

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;

        // Y-Sort: dynamically sort against grass based on Y position
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;

            // Bosses are larger sprites — need bigger offset to avoid grass protruding
            bool isBoss = GetComponent<Boss1>() != null || GetComponent<BaseBossStats>() != null;
            ysort.sortYOffset = isBoss ? -1.0f : -0.2f;
        }

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
        if (EnergyManager.Instance != null && EnergyManager.Instance.IsGameOver())
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }
        // Don't move while Boss1 is performing laser attack
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

        if (currentTarget == null || isKnockedBack || !IsValidTarget(currentTarget))
        {
            if (!isKnockedBack && !IsValidTarget(currentTarget))
                UpdateTarget();
            return;
        }

        float distance = Vector2.Distance(transform.position, currentTarget.position);

        // Stop moving when in attack range or mid-cycle
        if (distance <= attackRange || isAttackingCycle)
        {
            if (!isKnockedBack) rb.linearVelocity = Vector2.zero;
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
    && (EnergyManager.Instance == null || !EnergyManager.Instance.IsGameOver()))
        {
            float distance = Vector2.Distance(transform.position, currentTarget.position);

            if (distance <= attackRange)
            {
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

    // Owns the entire attack cycle start to finish.
    // No Attack() can fire again until this coroutine completes.
    private IEnumerator AttackCycle(Transform target)
    {
        isAttackingCycle = true;

        // Tell animation controller to play the melee attack animation
        if (animController != null)
            animController.PlayMeleeAttackAnimation();

        var boss1 = GetComponent<Boss1>();

        if (boss1 != null)
        {
            // Boss1: delay both damage AND sound until the ground-hit frame.
            // Boss1.OnMeleeAttackFired() handles sound and damage at correct frame
            boss1.OnMeleeAttackFired(target);
        }
        else
        {
            // Regular enemies: hit + sound immediately
            PerformHit(target);
        }

        // Wait for the full attack animation to finish.
        // The animation plays at enemyData.animationSpeed per frame
        // for enemyData.attack.frameCount frames.
        float animDuration = 0f;
        if (stats != null && stats.enemyData != null)
            animDuration = stats.enemyData.animationSpeed * stats.enemyData.attack.frameCount;

        // Wait the longer of: animation duration or attackCooldown.
        // This guarantees the animation finishes AND the cooldown is respected.
        float waitTime = Mathf.Max(animDuration, attackCooldown);
        yield return new WaitForSeconds(waitTime);

        // Tell animation controller the melee attack is done
        if (animController != null)
            animController.StopMeleeAttackAnimation();

        isAttackingCycle = false;

        // Reset the cooldown timer so Update() waits a full cooldown period

        attackTimer = attackCooldown;
    }

    /// Perform hit immediately, used by regular non-boss enemies.
    private void PerformHit(Transform target)
    {
        PlayAttackSound();
        ApplyDamageToTarget(target);
    }

    /// Apply damage to target without playing any sound.
    public void ApplyDamageToTarget(Transform target)
    {
        if (target == null) return;

        var stats = target.GetComponent<CharacterStats>();
        if (stats != null)
        {
            float damageAmount = this.stats.Damage;
            stats.TakeDamage(damageAmount);

            var playerStats = stats as PlayerStats;
            if (playerStats != null)
            {
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

        if (currentTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, avoidDistance);
    }
}
