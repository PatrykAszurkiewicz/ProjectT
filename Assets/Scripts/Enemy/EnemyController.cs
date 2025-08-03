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
        Debug.Log($"Nowy cel: {currentTarget.name}");
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
        if (currentTarget == null || isKnockedBack) return;

        Vector2 direction = (currentTarget.position - transform.position).normalized;
        rb.linearVelocity = direction * stats.MoveSpeed;
    }

    private void Update()
    {
        if (isKnockedBack)
        {
            knockbackTimer -= Time.deltaTime;
            if (knockbackTimer <= 0f)
            {
                isKnockedBack = false;
            }
        }
    }

    public void ApplyKnockback(Vector2 direction, float force, float duration = 0.2f)
    {
        isKnockedBack = true;
        knockbackTimer = duration;

        rb.linearVelocity = direction * force;
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        //Gizmos.color = Color.red;
        //Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
