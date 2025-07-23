using UnityEngine;

public class EnemyController : MonoBehaviour
{
    private Transform target;
    private EnemyStats stats;
    private Rigidbody2D rb;

    private bool isKnockedBack = false;
    private float knockbackTimer = 0f;

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            target = player.transform;
        }
        else
        {
            Debug.LogWarning("EnemyController: Nie znaleziono obiektu z tagiem 'Player'!");
        }
    }

    private void FixedUpdate()
    {
        if (target == null || isKnockedBack) return;

        Vector2 direction = (target.position - transform.position).normalized;
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
}
