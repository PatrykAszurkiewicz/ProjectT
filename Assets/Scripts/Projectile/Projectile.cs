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
    }

    void Start()
    {
        // Destroy projectile after lifetime
        Destroy(gameObject, lifeTime);
    }

    void Update()
    {
        if (hasHit) return;

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
        Health targetHealth = hitTarget.GetComponent<Health>();
        if (targetHealth != null)
        {
            targetHealth.TakeDamage(damage);
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
        Destroy(gameObject);
    }

    public void Initialize(GameObject targetEnemy, float projectileDamage, float projectileRange)
    {
        target = targetEnemy;
        damage = projectileDamage;
        maxRange = projectileRange;
        startPosition = transform.position;
    }

    public void Initialize(Vector3 direction, float projectileDamage, float projectileRange)
    {
        target = null;
        damage = projectileDamage;
        maxRange = projectileRange;
        startPosition = transform.position;

        // Set initial direction
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle - 90f, Vector3.forward);
    }
}