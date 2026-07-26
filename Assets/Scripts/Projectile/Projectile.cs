using UnityEngine;

public class Projectile : MonoBehaviour
{
    [Header("Projectile Properties")]
    public float speed = 10f;
    public float damage = 10f;
    public float lifeTime = 3f;
    public float maxRange = 10f;

    [Header("Visual Settings")]
    public bool rotateTowardsTarget = true;
    public GameObject impactEffectPrefab;

    private GameObject target;
    private Vector3 startPosition;
    private Rigidbody2D rb;
    private bool hasHit = false;

    private float freezeChance = 0f;

    // Cached so a POOLED projectile can wipe its trail on respawn. A TrailRenderer
    // draws a ribbon from the object's movement history; when the pool teleports a
    // recycled projectile to a new spawn point, an uncleared trail streaks a line
    // across the whole map. Clearing it on spawn removes that artifact. Null (and
    // therefore skipped) if the prefab has no trail — costs nothing in that case.
    private TrailRenderer trail;

    // Pooling: lifetime is now tracked with a timer that counts down in Update and
    // returns the projectile to the pool, instead of Destroy(gameObject, lifeTime).
    // Destroy-based lifetime can't work with pooling because a recycled object's
    // Start() never runs again. Seeded in ResetForSpawn (and in Awake, so an object
    // that is somehow used without Initialize still self-retires).
    private float lifeTimer;

    public void SetFreezeChance(float chance)
    {
        freezeChance = Mathf.Clamp01(chance);
        Debug.Log($"Projectile freeze chance set to: {freezeChance}");
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }

        // Configure rigidbody
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        startPosition = transform.position;

        // Add collider if not present
        if (GetComponent<Collider2D>() == null)
        {
            CircleCollider2D collider = gameObject.AddComponent<CircleCollider2D>();
            collider.radius = 0.1f;
            collider.isTrigger = true;
        }

        // Ensure projectile renders above grass Y-sort range (400-1600)
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = 2500;

        // Cache the trail (if any) for pool-respawn clearing. Include inactive so a
        // trail on a disabled child is still found.
        trail = GetComponentInChildren<TrailRenderer>(true);

        // Defensive default so a projectile that is spawned without going through
        // Initialize() still self-retires after `lifeTime` rather than living forever.
        lifeTimer = lifeTime;
    }

    // Resets every piece of per-shot mutable state so a RECYCLED projectile behaves
    // exactly like a freshly-Instantiated one. Called from both Initialize overloads
    // (the spawn entry points used by the tower). Without this, a pooled projectile
    // would inherit the previous shot's hasHit / freezeChance / velocity — e.g. a
    // shot fired by a non-freeze tower could still freeze because the last user of
    // this instance was a freeze tower. Resetting here closes that gap.
    private void ResetForSpawn()
    {
        hasHit = false;
        freezeChance = 0f;                 // tower re-applies via SetFreezeChance only when > 0
        startPosition = transform.position;
        lifeTimer = lifeTime;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        // Wipe any leftover trail ribbon from the previous use. Safe here because
        // PrefabPool.Get has already repositioned us before Initialize() runs, so
        // the trail restarts cleanly at the new spawn point.
        if (trail != null) trail.Clear();
    }

    void Start()
    {
        // Lifetime is handled by lifeTimer in Update now (pool-safe); nothing to do
        // here. Kept so existing prefab wiring / execution-order expectations are
        // undisturbed.
    }

    void Update()
    {
        if (hasHit) return;

        // Lifetime (pool-safe replacement for Destroy(gameObject, lifeTime)).
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            DestroyProjectile();
            return;
        }

        // Check if projectile has traveled too far
        float distanceTraveled = Vector3.Distance(startPosition, transform.position);
        if (distanceTraveled > maxRange)
        {
            DestroyProjectile();
            return;
        }

        MoveProjectile();
    }

    void MoveProjectile()
    {
        Vector3 direction;

        if (target != null)
        {
            // Homing projectile - follow target
            direction = (target.transform.position - transform.position).normalized;
        }
        else
        {
            // Straight projectile - continue in initial direction
            direction = transform.up;
        }

        // Move projectile
        rb.linearVelocity = direction * speed;

        // Rotate to face movement direction
        if (rotateTowardsTarget)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle - 90f, Vector3.forward);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;

        // Check if we hit our target or any valid enemy
        if (IsValidTarget(other.gameObject))
        {
            HitTarget(other.gameObject);
        }
    }

    bool IsValidTarget(GameObject hitObject)
    {
        // Hit specific target if we have one
        if (target != null)
        {
            return hitObject == target;
        }

        // Otherwise hit any object on enemy layer
        return hitObject.layer == LayerMask.NameToLayer("Enemy");
    }

    void HitTarget(GameObject hitTarget)
    {
        hasHit = true;

        // Deal damage
        EnemyStats targetStats = hitTarget.GetComponent<EnemyStats>();
        if (targetStats != null)
        {
            // Augments 335/341/342 — this is a TOWER projectile (the player's
            // ranged weapons use WeaponProjectile, not this class), so credit the
            // hit to towers for tower-kill attribution. No-op unless an augment
            // that cares is active.
            TowerKillAttribution.MarkTowerHit(hitTarget);

            targetStats.TakeDamage(damage);

            // Combat telemetry: tower (ranged) damage dealt.
            CombatStats.ReportTowerDamageDealt(damage);

            // Apply freeze effect if chance > 0
            if (freezeChance > 0f && Random.Range(0f, 1f) <= freezeChance)
            {
                var enemyController = hitTarget.GetComponent<EnemyController>();
                if (enemyController != null)
                {
                    enemyController.ApplyFreeze(2f); // Freeze for 2 seconds
                    Debug.Log($"Projectile froze enemy {hitTarget.name}! (chance was {freezeChance})");
                }
                else
                {
                    Debug.LogError($"No EnemyController found on {hitTarget.name} for freeze effect!");
                }
            }
        }

        // Spawn impact effect
        if (impactEffectPrefab != null)
        {
            Instantiate(impactEffectPrefab, transform.position, Quaternion.identity);
        }

        DestroyProjectile();
    }

    void DestroyProjectile()
    {
        // Return to the pool instead of destroying. PrefabPool.Release is safe on
        // non-pooled instances too (it just Destroys them), so a projectile placed
        // in the scene by hand still behaves correctly.
        PrefabPool.Release(gameObject);
    }

    public void Initialize(GameObject targetEnemy, float projectileDamage, float projectileRange)
    {
        ResetForSpawn();           // clear recycled state before applying this shot's values
        target = targetEnemy;
        damage = projectileDamage;
        maxRange = projectileRange;
        startPosition = transform.position;
    }

    public void Initialize(Vector3 direction, float projectileDamage, float projectileRange)
    {
        ResetForSpawn();           // clear recycled state before applying this shot's values
        target = null;
        damage = projectileDamage;
        maxRange = projectileRange;
        startPosition = transform.position;

        // Set initial direction
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle - 90f, Vector3.forward);
    }
}

