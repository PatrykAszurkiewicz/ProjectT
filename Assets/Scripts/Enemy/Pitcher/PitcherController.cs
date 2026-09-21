using System.Collections;
using UnityEngine;

// Pitcher ranged enemy. It reuses EnemyController for everything (target
// acquisition, movement, obstacle avoidance, stuck handling, the attack
// cycle and the attack animation timing) and only swaps out what happens at
// the moment the attack lands: instead of an instant melee hit, it throws a
// projectile at the current target.

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

    [Tooltip("Colour of the short fading trail left behind the thrown dart.")]
    [SerializeField] private Color tracerColor = new Color(0.72f, 0.38f, 1f);

    [Tooltip("Trail width at the dart, in WORLD units (independent of prefab scale).")]
    [SerializeField] private float tracerWidth = 0.14f;

    [Tooltip("How long (seconds) a point of the trail lingers.")]
    [SerializeField] private float tracerTime = 0.14f;

    [Header("Release Timing")]
    [Tooltip("Extra wait AFTER EnemyData.hitFrame before the dart actually leaves, " +
             "measured in ATTACK-ANIMATION FRAMES (fractions allowed, e.g. 1.5).\n\n" +
             "The throw animation shows the crystal pushing out of the body first; " +
             "releasing exactly on hitFrame made the dart appear before that. " +
             "Being frame-based, the delay stays in step with the art if you later " +
             "retime attack.speedOverride.\n\n" +
             "0 = release exactly on hitFrame (old behaviour). Keep " +
             "hitFrame + this below attack.frameCount so the dart still leaves " +
             "during the throw animation.")]
    [SerializeField, Min(0f)] private float releaseDelayFrames = 2f;

    private EnemyController enemyController;
    private EnemyStats stats;
    private EnemyAnimationController animController;
    private EnemyFieryEyes fieryEyes;   // optional; flares when the dart leaves

    private Coroutine pendingRelease;
    private Transform pendingTarget;

    private void Awake()
    {
        // Assign in Awake so the override is in place before the first attack
        // cycle can run — same ordering rationale as InsectController.
        enemyController = GetComponent<EnemyController>();
        stats = GetComponent<EnemyStats>();
        animController = GetComponent<EnemyAnimationController>();
        fieryEyes = GetComponent<EnemyFieryEyes>();

        if (enemyController != null)
            enemyController.AttackHandlerOverride = ThrowProjectile;
    }

    private void OnDisable()
    {
        // Disabling a MonoBehaviour stops its coroutines; forget the pending throw
        // so a stale handle can't be "flushed" later.
        pendingRelease = null;
        pendingTarget = null;
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
    // locked: player, a tower, or the core. The actual release is deferred by
    // releaseDelayFrames so the dart leaves once the art shows it pushing out.
    private void ThrowProjectile(Transform target)
    {
        if (target == null) return;

        float delay = GetReleaseDelaySeconds();
        if (delay <= 0f)
        {
            Release(target);
            return;
        }

        // Only reachable if the delay outlasts a whole attack cycle + cooldown.
        // Let the earlier dart go now rather than silently swallowing it.
        if (pendingRelease != null)
        {
            StopCoroutine(pendingRelease);
            pendingRelease = null;
            Transform earlier = pendingTarget;
            pendingTarget = null;
            if (IsLiveTarget(earlier) && CanStillThrow()) Release(earlier);
        }

        pendingTarget = target;
        pendingRelease = StartCoroutine(ReleaseAfterDelay(target, delay));
    }

    private float GetReleaseDelaySeconds()
    {
        if (releaseDelayFrames <= 0f) return 0f;
        float secondsPerFrame = (stats != null && stats.enemyData != null)
            ? stats.enemyData.AttackAnimSpeed
            : 0.1f;
        return releaseDelayFrames * Mathf.Max(0f, secondsPerFrame);
    }

    private IEnumerator ReleaseAfterDelay(Transform target, float delay)
    {
        // Scaled time, same clock the attack animation samples (Time.time), so the
        // release stays locked to the sprite and pauses with the game.
        yield return new WaitForSeconds(delay);

        pendingRelease = null;
        pendingTarget = null;

        // The Pitcher can die, get parry-stunned or be taken over by a
        // confusion/berserk effect inside the delay. A corpse must not throw.
        if (!CanStillThrow()) yield break;

        // The original target may have died during the wind-up; fall back to
        // whatever the controller is locked on now instead of throwing at nothing.
        if (!IsLiveTarget(target))
        {
            target = enemyController != null ? enemyController.CurrentTarget : null;
            if (!IsLiveTarget(target)) yield break;
        }

        Release(target);
    }

    private bool CanStillThrow()
    {
        if (enemyController == null || !enemyController.enabled) return false; // DelayedDeath / CC suspension disable it
        if (stats != null && stats.IsDead()) return false;

        if (animController != null && (animController.IsDying || animController.IsAnimationFrozen))
            return false;

        var stun = GetComponent<ParryStunEffect>();
        if (stun != null && stun.IsStunActive) return false;

        return true;
    }

    private static bool IsLiveTarget(Transform t)
        => t != null && t.gameObject.activeInHierarchy;

    // The actual throw (the body of the old ThrowProjectile, unchanged apart from
    // the eye flare). Aims at the target's position at RELEASE time, not at the
    // hit frame, so the delay doesn't make the shot lag behind a moving player.
    private void Release(Transform target)
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

        // Throw SFX.
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.pitcherAttack.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.pitcherAttack, spawn);
        }

        if (fieryEyes != null)
            fieryEyes.Flare();

        GameObject projObj = Instantiate(
            projectilePrefab, spawn, Quaternion.AngleAxis(angle, Vector3.forward));

        // The AngleAxis above already puts the dart's tip on its heading (the shot is
        // non-homing, so that stays true for the whole flight). Just add the tracer.
        ProjectileDart.Attach(projObj.transform, tracerColor, tracerWidth, tracerTime);

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



