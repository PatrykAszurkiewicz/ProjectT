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

        // Subtle warm tracer light so the shot reads in dark biomes.
        ProjectileGlow.Attach(transform, new Color(1f, 0.95f, 0.7f), worldRadius: 0.55f,
                              alpha: 0.5f, pulse: true, pulseSpeed: 10f, pulseAmount: 0.18f);

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
                PlayHitSfx();

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
                PlayHitSfx();

                Destroy(gameObject);
                return;
            }
        }

        // When hitting something that isn't an enemy (wall, obstacle, etc.) - destroy bullet
        // Destroy(gameObject);
        if (!other.isTrigger)
            Destroy(gameObject);
    }

    // Ranged impact SFX. Fired on any enemy hit (CharacterStats or IDamageable),
    // before the bullet is destroyed. Mortar shells use their own explosion path,
    // so this stays exclusive to the direct ranged weapon.
    private void PlayHitSfx()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.rangedHit.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.rangedHit, transform.position);
        }
    }
}
