using UnityEngine;

public class WeaponProjectile : MonoBehaviour
{
    private float damage;
    private Vector2 direction;
    private float speed;
    private float knockBackForce;

    public void Initialize(Vector2 dir, float dmg, float spd, float knockback)
    {
        direction = dir.normalized;
        damage = dmg;
        speed = spd;
        knockBackForce = knockback;
    }

    private void Update()
    {
        transform.Translate(direction * speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Enemy"))
        {
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

        Destroy(gameObject);
    }

}
