using System.Collections;
using UnityEngine;

public class WeaponProjectile : MonoBehaviour
{
    private float damage;
    private Vector2 direction;
    private float speed;
    private float knockBackForce;
    [SerializeField] private float bulletDespawnTime = 5f;

    public void Initialize(Vector2 dir, float dmg, float spd, float knockback)
    {
        direction = dir.normalized;
        damage = dmg;
        speed = spd;
        knockBackForce = knockback;
    }

    private void Start()
    {
        StartCoroutine(DespawnAfterTime(bulletDespawnTime));
    }

    private IEnumerator DespawnAfterTime(float time)
    {
        yield return new WaitForSeconds(time);
        Destroy(gameObject);
    }

    private void Update()
    {
        transform.Translate(direction * speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Ignore certain trigger types that shouldn't destroy bullets
        if (other.name.Contains("EnergyCollectionTrigger") ||
            other.name.Contains("Energy") ||
            other.CompareTag("Player") ||
            other.GetComponent<PlayerMovement>() != null)
        {
            return;
        }

        if (other.CompareTag("Enemy"))
        {
            // Try IDamageable first for Gremlin
            IDamageable damageable = other.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, gameObject);
            }
            else
            {
                // Fallback to EnemyStats for regular enemies
                EnemyStats enemy = other.GetComponent<EnemyStats>();
                if (enemy != null)
                {
                    enemy.TakeDamage(damage);

                    // Knockback
                    EnemyController enemyController = enemy.GetComponent<EnemyController>();
                    if (enemyController != null)
                    {
                        Vector2 dir = (enemy.transform.position - transform.position).normalized;
                        enemyController.ApplyKnockback(dir, knockBackForce);
                    }
                }
            }
        }

        Destroy(gameObject);
    }
}