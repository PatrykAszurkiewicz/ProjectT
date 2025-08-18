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
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private float attackCooldown = 1f;
    private float attackTimer = 0f;

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

    private void UpdateTarget()
    {
        //Find Player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player && Vector2.Distance(transform.position, player.transform.position) < detectRange)
        {
            currentTarget = player.transform;
            return;
        }
        //Find closest turret
        GameObject[] towers = GameObject.FindGameObjectsWithTag("Tower");
        float closestDist = Mathf.Infinity;
        Transform closestTower = null;

        foreach (var tower in towers)
        {
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
        //Default target (core)
        currentTarget = coreTarget;
    }

    private void FixedUpdate()
    {
        // WAŻNE: Nie ruszaj velocity podczas knockbacka!
        if (currentTarget == null || isKnockedBack) return;

        float distance = Vector2.Distance(transform.position, currentTarget.position);

        if (distance <= attackRange)
        {
            // Zatrzymaj się tylko jeśli nie ma knockbacka
            if (!isKnockedBack)
            {
                rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        // Poruszaj się tylko jeśli nie ma knockbacka
        Vector2 direction = (currentTarget.position - transform.position).normalized;
        rb.linearVelocity = direction * stats.MoveSpeed;
    }

    private void Update()
    {
        // Obsługa knockback timera
        if (isKnockedBack)
        {
            knockbackTimer -= Time.deltaTime;
            if (knockbackTimer <= 0f)
            {
                isKnockedBack = false;
            }
        }

        // Obsługa ataku
        if (currentTarget != null)
        {
            float distance = Vector2.Distance(transform.position, currentTarget.position);
            if (distance <= attackRange)
            {
                attackTimer -= Time.deltaTime;
                
                // NIE zeruj velocity tutaj - to przeszkadza w knockbacku!
                // rb.linearVelocity = Vector2.zero; // ← USUNIĘTE

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
            EnergyManager.Instance.DamageEnergyConsumer(consumer, this.stats.Damage, gameObject);

            if (target == null)
            {
                currentTarget = coreTarget;
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
    }
}