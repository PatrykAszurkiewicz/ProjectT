using UnityEngine;

public class EnemyController : MonoBehaviour
{
    private Transform target;
    private EnemyStats stats;
    private Rigidbody2D rb;

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
        if (target == null) return;

        Vector2 direction = (target.position - transform.position).normalized;
        rb.MovePosition(rb.position + direction * stats.MoveSpeed * Time.fixedDeltaTime);
    }
}
