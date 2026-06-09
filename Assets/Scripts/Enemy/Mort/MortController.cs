using UnityEngine;

// Mort - a ranged artillery enemy
[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(EnemyStats))]
public class MortController : MonoBehaviour
{
    [Header("Mortar Shell")]
    [Tooltip("Prefab carrying a MortarProjectile component. Tip: duplicate any " +
             "existing projectile prefab for the visuals and put MortarProjectile " +
             "on it so the art is reused but it arcs + explodes on the player / " +
             "towers / core instead of homing.")]
    [SerializeField] private GameObject mortarPrefab;

    [Tooltip("Seconds the shell spends in the air before it lands. This IS the " +
             "dodge window — longer = easier to escape, shorter = harder. The " +
             "blast lands wherever the target was when the shell was released.")]
    [SerializeField] private float flightTime = 1.1f;

    [Tooltip("Local offset from the Mort's position where shells spawn (e.g. " +
             "slightly up so they leave the muzzle rather than the feet).")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.4f, 0f);

    [Tooltip("Safety cap (seconds) before an in-flight shell self-destructs " +
             "(detonating where it is), so a shell can never leak.")]
    [SerializeField] private float shellMaxLifetime = 6f;

    [Tooltip("Optional aim lead. 0 = aim exactly where the target is now " +
             "(most dodge-able). 1 = aim where the target would be after the " +
             "full flight if it kept its current velocity (much harder to dodge, " +
             "needs a Rigidbody2D on the target to read velocity).")]
    [Range(0f, 1f)]
    [SerializeField] private float aimLead = 0f;

    private EnemyController enemyController;
    private EnemyStats stats;

    private void Awake()
    {
        // Assign in Awake so the override is in place before the first attack
        // cycle can run — same ordering rationale as PitcherController.
        enemyController = GetComponent<EnemyController>();
        stats = GetComponent<EnemyStats>();

        if (enemyController != null)
            enemyController.AttackHandlerOverride = FireMortar;
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
    private void FireMortar(Transform target)
    {
        if (target == null) return;

        if (mortarPrefab == null)
        {
            Debug.LogWarning($"[Mort] {name} has no mortarPrefab assigned — no shell fired.");
            return;
        }

        Vector3 spawn = transform.position + spawnOffset;

        // Capture the landing spot once
        Vector3 landing = target.position;
        if (aimLead > 0f)
        {
            var targetRb = target.GetComponent<Rigidbody2D>();
            if (targetRb != null)
                landing += (Vector3)(targetRb.linearVelocity * (flightTime * aimLead));
        }

        // Spawn pointing roughly at the landing spot (cosmetic; the shell
        // re-orients itself along its arc each frame anyway).
        Vector3 dir = landing - spawn;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (float.IsNaN(angle) || float.IsInfinity(angle)) angle = 0f;

        GameObject shellObj = Instantiate(
            mortarPrefab, spawn, Quaternion.AngleAxis(angle, Vector3.forward));

        var shell = shellObj.GetComponent<MortarProjectile>();
        if (shell != null)
        {
            // Hand the firing controller to the shell so that, on detonation, it
            // can reuse EnemyController.ApplyDamageToTarget for each victim.
            float damage = stats != null ? stats.Damage : 0f;
            shell.Initialize(enemyController, landing, damage, flightTime, shellMaxLifetime);
        }
        else
        {
            Debug.LogWarning($"[Mort] mortarPrefab '{mortarPrefab.name}' " +
                             $"has no MortarProjectile component.");
        }
    }
}
