using UnityEngine;

// Pitcher ranged enemy. It reuses EnemyController for everything (target
// acquisition, movement, obstacle avoidance, stuck handling, the attack
// cycle and the attack animation timing) and only swaps out what happens at
// the moment the attack lands: instead of an instant melee hit, it throws a
// homing projectile at the current target.

[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(EnemyStats))]
public class PitcherController : MonoBehaviour
{
    [Header("Projectile")]
    [Tooltip("Prefab carrying an EnemyProjectile component. Tip: duplicate the " +
             "tower's projectile prefab for the visuals and swap its script for " +
             "EnemyProjectile so the art is reused but the damage targets the " +
             "player / towers / core instead of enemies.")]
    [SerializeField] private GameObject projectilePrefab;

    [Tooltip("Travel speed of the thrown projectile in units/second.")]
    [SerializeField] private float projectileSpeed = 8f;

    [Tooltip("Local offset from the Pitcher's position where projectiles spawn " +
             "(e.g. slightly up so they leave the 'hands' rather than the feet).")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.3f, 0f);

    [Tooltip("Safety cap (seconds) before an in-flight projectile self-destructs, " +
             "so a projectile can never leak if its target vanishes mid-flight.")]
    [SerializeField] private float projectileMaxLifetime = 5f;

    private EnemyController enemyController;
    private EnemyStats stats;

    private void Awake()
    {
        // Assign in Awake so the override is in place before the first attack
        // cycle can run — same ordering rationale as InsectController.
        enemyController = GetComponent<EnemyController>();
        stats = GetComponent<EnemyStats>();

        if (enemyController != null)
            enemyController.AttackHandlerOverride = ThrowProjectile;
    }

    private void OnDestroy()
    {
        // Drop the delegate so nothing holds a stale reference to this
        // (now destroyed) component.
        if (enemyController != null)
            enemyController.AttackHandlerOverride = null;
    }

    // Invoked by EnemyController.PerformHit() at the configured hit frame of the
    // attack animation. 'target' is whatever the controller currently has
    // locked: player, a tower, or the core.
    private void ThrowProjectile(Transform target)
    {
        if (target == null) return;

        if (projectilePrefab == null)
        {
            Debug.LogWarning($"[Pitcher] {name} has no projectilePrefab assigned — no shot fired.");
            return;
        }

        Vector3 spawn = transform.position + spawnOffset;
        Vector3 dir = (target.position - spawn);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (float.IsNaN(angle) || float.IsInfinity(angle)) angle = 0f;

        GameObject projObj = Instantiate(
            projectilePrefab, spawn, Quaternion.AngleAxis(angle, Vector3.forward));

        var projectile = projObj.GetComponent<EnemyProjectile>();
        if (projectile != null)
        {
            // Hand the firing controller to the projectile so that, on impact,
            // it can reuse EnemyController.ApplyDamageToTarget 
            float damage = stats != null ? stats.Damage : 0f;
            // homing:false → the shot commits to its launch heading instead of
            // tracking the player, so the player can side-step it. (A parried
            // shot still homes back into the Pitcher; see EnemyProjectile.)
            projectile.Initialize(enemyController, target, damage,
                                  projectileSpeed, projectileMaxLifetime, homing: false);
        }
        else
        {
            Debug.LogWarning($"[Pitcher] projectilePrefab '{projectilePrefab.name}' " +
                             $"has no EnemyProjectile component.");
        }
    }
}

