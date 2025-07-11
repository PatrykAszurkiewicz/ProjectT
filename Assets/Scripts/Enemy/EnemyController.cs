using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public Transform target;
    private EnemyStats stats;
    private Rigidbody2D rb;

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        if (target == null) return;

        Vector2 direction = (target.position - transform.position).normalized;
        rb.MovePosition(rb.position + direction * stats.MoveSpeed * Time.fixedDeltaTime);
    }
}
