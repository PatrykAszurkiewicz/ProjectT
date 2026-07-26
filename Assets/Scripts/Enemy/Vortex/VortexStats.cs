using UnityEngine;

// VORTEX STATS
// Subclasses EnemyStats for exactly one reason, the same one BaseBossStats and
// SplitterStats exist for: Die() is virtual, so the collapse VFX can fire before
// the base class tears the GameObject down.
// (maxHealth / currentHealth are PUBLIC fields on CharacterStats, so the spawner
// reads them directly — no accessor needed here.)
[RequireComponent(typeof(VortexVisual))]
public class VortexStats : EnemyStats
{
    [Header("Vortex Feedback")]
    [Tooltip("Disk brightness surge when the vortex is hit. It's a star; hitting " +
             "it should make it flare, not flash white.")]
    [Range(0f, 1f)][SerializeField] private float hitFlare = 0.35f;

    private VortexVisual visual;

    protected override void Awake()
    {
        base.Awake();
        visual = GetComponent<VortexVisual>();

        // No sprite, so the sprite-shatter death has nothing to shatter — and
        // EnemyDeathVFX.Trigger destroys the enemy outright when sprite == null.
        // VortexVisual.Collapse() is our death VFX.
        ConfigureDeathVfx(0f);
    }

    public override void TakeDamage(float amount)
    {
        base.TakeDamage(amount);
        if (visual != null && hitFlare > 0f) visual.Flare(hitFlare);
    }

    public override void Die()
    {
        // Before base.Die() destroys us — the VFX spawns as its own root object and
        // outlives this GameObject.
        if (visual != null) visual.Collapse();
        base.Die();
    }
}

