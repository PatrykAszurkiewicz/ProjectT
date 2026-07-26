using UnityEngine;

// SPLITTER STATS
//
// Subclasses EnemyStats for exactly one reason: Die() is virtual, and splitting
// has to happen at the moment of death, before the base class tears the object
// down. Same idiom BaseBossStats uses for its armour pool.
//
// It also routes damage feedback into the blob: EnemyStats.StartDamageFlash()
// only knows how to tint a SpriteRenderer, and the Splitter's body is a mesh.
[RequireComponent(typeof(SplitterController))]
public class SplitterStats : EnemyStats
{
    [Header("Blob Feedback")]
    [Tooltip("How hard an incoming hit dents the membrane. The pressure term " +
             "makes the opposite flank bulge out on its own.")]
    [SerializeField] private float hitImpulse = 5f;

    [Tooltip("Recoil squash across the axis of an incoming blow. The pressure term " +
             "makes the opposite flank bulge on its own.")]
    [Range(0f, 0.5f)][SerializeField] private float hitSquash = 0.18f;

    private SplitterController splitter;
    private ProceduralBlob blob;

    // Direction the last hit pushed us — away from the attacker. Reused as the
    // blast direction when the membrane tears apart.
    private Vector2 lastHitDirection = Vector2.up;

    // Die() can be re-entered (base.Die() → PerformDeath, and CharacterStats may
    // call Die more than once if two hits land the same frame). Split exactly once.
    private bool hasSplit;

    protected override void Awake()
    {
        base.Awake();
        splitter = GetComponent<SplitterController>();
        blob = GetComponentInChildren<ProceduralBlob>();
        if (blob == null) blob = GetComponent<ProceduralBlob>();
    }

    /// Called by SplitterController on a freshly instantiated child, AFTER
    /// EnemyStats.Awake has seeded maxHealth from the (already per-instance
    /// cloned) EnemyData, but BEFORE Start() builds the health bar — so the bar
    /// picks up the reduced maximum with no extra wiring.
    public void SetupAsChild(float healthFraction)
    {
        maxHealth *= Mathf.Max(0.05f, healthFraction);
        currentHealth = maxHealth;
    }

    public override void TakeDamage(float amount)
    {
        base.TakeDamage(amount);

        if (blob != null)
        {
            blob.Flash();

            // CharacterStats.TakeDamage carries no attacker, so aim the dent at the
            // nearest player when there is one — that's the source ~always.
            Vector2 dir = Random.insideUnitCircle.normalized;
            var pr = PlayerRegistry.Instance;
            if (pr != null)
            {
                var nearest = pr.NearestAlive(transform.position, includeCloaked: true);
                if (nearest != null)
                {
                    Vector2 d = (Vector2)(transform.position - nearest.transform.position);
                    if (d.sqrMagnitude > 0.0001f) dir = d.normalized;
                }
            }

            lastHitDirection = dir;
            blob.Impulse(dir, hitImpulse);

            // Recoil: squash across the axis of the blow. Reads as a real impact
            // rather than a colour flash.
            blob.Stretch(dir, -hitSquash);
        }
    }

    public override void Die()
    {
        if (!hasSplit)
        {
            hasSplit = true;

            // Children first, disintegration second: the goo should read as the
            // burst that threw them, not as a separate effect.
            if (splitter != null) splitter.SpawnChildren();

            if (blob != null)
            {
                // Tear the membrane apart along the direction the killing blow came
                // from. lastHitDirection points away from the attacker, so the debris
                // flies off the far side, the way it should.
                float force = splitter != null ? splitter.DisintegrateForce : 3.2f;
                blob.Disintegrate(lastHitDirection, force);
            }
        }

        base.Die();
    }
}

