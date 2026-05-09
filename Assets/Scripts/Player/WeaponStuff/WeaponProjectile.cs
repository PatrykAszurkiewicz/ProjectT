using System.Collections;
using UnityEngine;

public class WeaponProjectile : MonoBehaviour
{
    private float damage;
    private Vector2 direction;
    private float speed;
    private float knockBackForce;
    [SerializeField] private float bulletDespawnTime = 5f;
    public float GetDamage() => damage;

    public void Initialize(Vector2 dir, float dmg, float spd, float knockback)
    {
        direction = dir.normalized;
        damage = dmg;
        speed = spd;
        knockBackForce = knockback;
    }

    private void Start()
    {
        // Ensure projectile renders above grass Y-sort range (400-1600)
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = 2500;

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
        // Ignore triggers that should not destroy bullets
        if (other.name.Contains("EnergyCollectionTrigger") ||
            other.name.Contains("Energy") ||
            other.CompareTag("Player") ||
            other.GetComponent<PlayerMovement>() != null)
        {
            return;
        }

        if (other.CompareTag("Enemy"))
        {
            // CharacterStats (covers EnemyStats, Boss1, all bosses) 
            // GetComponent returns the most-derived type (virtual TakeDamage)
            CharacterStats stats = other.GetComponent<CharacterStats>();
            if (stats != null)
            {
                stats.TakeDamage(damage);

                //  COMBAT FEEL — ranged hit 
                CombatJuice.OnPlayerHitEnemy(other.gameObject, isMelee: false);

                // Knockback (only for enemies that have a controller)
                EnemyController enemyController = other.GetComponent<EnemyController>();
                if (enemyController != null)
                {
                    Vector2 dir = (other.transform.position - transform.position).normalized;
                    enemyController.ApplyKnockback(dir, knockBackForce);
                }

                Destroy(gameObject);
                return;
            }

            // IDamageable for Gremlin and other non-CharacterStats enemies
            IDamageable damageable = other.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, gameObject);

                //  COMBAT FEEL  ranged hit on IDamageable 
                CombatJuice.OnPlayerHitEnemy(other.gameObject, isMelee: false);

                Destroy(gameObject);
                return;
            }
        }

        // When hitting something that isn't an enemy (wall, obstacle, etc.) - destroy bullet
        // Destroy(gameObject);
        if (!other.isTrigger)
            Destroy(gameObject);
    }
}
