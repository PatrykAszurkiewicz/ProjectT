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

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();

        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
            coreTarget = core.transform;

        currentTarget = coreTarget;

        InvokeRepeating(nameof(UpdateTarget), 0f, 0.5f);
    }
    private void FixedUpdate()
    {
        // Validate current target before moving
        if (currentTarget == null || isKnockedBack || !IsValidTarget(currentTarget))
        {
            // Force immediate target update if current target is invalid
            if (!isKnockedBack && !IsValidTarget(currentTarget))
            {
                UpdateTarget();
            }
            return;
        }

        float distance = Vector2.Distance(transform.position, currentTarget.position);

        if (distance <= attackRange)
        {
            // Stop if close enough to attack
            if (!isKnockedBack)
            {
                rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        // Move toward target
        Vector2 direction = (currentTarget.position - transform.position).normalized;

        //Raycast foward
        Collider2D obstacle = Physics2D.OverlapCircle(transform.position, avoidDistance, obstacleLayer);

        if (obstacle != null)
        {
            // add vector
            Vector2 awayFromObstacle = (rb.position - (Vector2)obstacle.transform.position).normalized;
            direction = (direction + awayFromObstacle).normalized;
        }
        rb.linearVelocity = direction.normalized * stats.MoveSpeed;
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

        // Handle attacking
        if (currentTarget != null)
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
        var stats = target.GetComponent<CharacterStats>();
        if (stats != null)
        {
            stats.TakeDamage(this.stats.Damage);

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
                // Immediately clear target and switch to Central Core
                currentTarget = coreTarget;
                // Force immediate movement toward core by clearing velocity
                if (rb != null && !isKnockedBack)
                {
                    rb.linearVelocity = Vector2.zero;
                }
            }
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
        //avoid obstacle
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, avoidDistance);
    }
}