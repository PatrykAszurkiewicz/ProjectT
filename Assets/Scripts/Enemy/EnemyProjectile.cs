using UnityEngine;

// A homing projectile fired by enemies (e.g. the Pitcher) at the player towers or the core.
public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("How close (world units) the projectile must get to its target to " +
             "count as a hit and deal damage.")]
    [SerializeField] private float hitRadius = 0.4f;

    [Tooltip("If true, the sprite keeps rotating to face its direction of travel " +
             "as it homes. Turn off for projectiles that shouldn't rotate.")]
    [SerializeField] private bool faceTravelDirection = true;

    [Tooltip("Optional: force a high sorting order so the projectile renders " +
             "above the grass Y-sort range. 0 = leave the prefab's value alone.")]
    [SerializeField] private int forcedSortingOrder = 2000;

    [Header("Projectile Parry (Augment 325)")]
    [Tooltip("How close (world units) the shot must get to the player before it " +
             "becomes parry-able and shows the '!' prompt. This is the reaction " +
             "window — bigger = easier to parry.")]
    [SerializeField] private float parryReactRadius = 2.0f;

    [Tooltip("Damage multiplier applied to the shot's own damage when it is " +
             "parried back into the enemy that fired it. 1 = same as it would " +
             "have hit you for; >1 rewards the parry.")]
    [SerializeField] private float parryReflectMultiplier = 2f;

    [Tooltip("Speed multiplier applied to the return trip after a successful " +
             "parry, so the bounced shot snaps back at the attacker.")]
    [SerializeField] private float parryReturnSpeedMultiplier = 1.5f;

    private EnemyController firer;     // may become null if the firer dies
    private Transform target;          // may become null if the target dies
    private float damage;
    private float speed;
    private float maxLifetime;
    private float age;
    private Vector3 lastKnownTargetPos;
    private bool initialized;

    // Homing vs. straight-line flight.
    //   true  → re-aims at the target every frame (original behaviour; used by
    //           anything that wants a tracking shot, and ALWAYS used for the
    //           parried return trip so the bounce reliably snaps into the firer).
    //   false → commits to the launch direction so the player can side-step it.
    //           The Pitcher launches with this off (see PitcherController).
    private bool homing = true;
    // Fixed launch direction captured at Initialize, used only while flying
    // straight (non-homing, not-yet-parried).
    private Vector3 flightDirection = Vector3.right;

    // Parry state
    private bool parried;                 // true once bounced back at the firer
    private ProjectileParryIndicator parryPrompt;

    public void Initialize(EnemyController firer, Transform target, float damage,
                           float speed, float maxLifetime = 5f, bool homing = true)
    {
        this.firer = firer;
        this.target = target;
        this.damage = damage;
        this.speed = speed;
        this.maxLifetime = Mathf.Max(0.1f, maxLifetime);
        this.homing = homing;
        if (target != null)
        {
            lastKnownTargetPos = target.position;

            // Capture the launch direction once, for the straight-line case. The
            // shot is aimed where the target is at release and then commits to
            // that heading, so a moving player can step out of its path. Homing
            // shots ignore this and recompute their heading every frame instead.
            Vector3 toTarget = target.position - transform.position;
            if (toTarget.sqrMagnitude > 0.000001f)
                flightDirection = toTarget.normalized;
            else
                flightDirection = transform.right; // fired right on top of target
        }
        initialized = true;

        if (forcedSortingOrder != 0)
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = forcedSortingOrder;
        }
    }

    private void Update()
    {
        if (!initialized) return;

        age += Time.deltaTime;
        if (age >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Shield interaction while inbound (not already bounced) 
        // Blocking (damage reduction) works whenever a shield is equipped; the
        // bounce-back is what the parry augment unlocks (handled inside).
        if (!parried)
            TryProjectileParry();

        // Refresh aim toward the target while it's alive; otherwise keep flying
        // toward where it last was and expire harmlessly if nothing is there.
        bool targetAlive = target != null && target.gameObject != null
                            && target.gameObject.activeInHierarchy;

        // STRAIGHT-LINE (dodge-able) flight.
        // Only while this shot is non-homing AND hasn't been parried. A parried
        // shot always falls through to the homing branch below so it reliably
        // tracks back into the firer — the parry behaviour is unchanged.
        if (!homing && !parried)
        {
            transform.position += flightDirection * speed * Time.deltaTime;

            if (faceTravelDirection)
                FaceDirection(flightDirection);

            // Connect only if the shot actually reaches the (living) target. If
            // the player side-steps the path, nothing connects here and the shot
            // sails on to expire by lifetime — that's the dodge.
            if (targetAlive)
            {
                float dToTarget = Vector2.Distance(transform.position, target.position);
                if (dToTarget <= hitRadius)
                {
                    ApplyDamage();
                    Destroy(gameObject);
                }
            }
            return;
        }

        // HOMING flight (original behaviour) — also the parried return trip.
        if (targetAlive) lastKnownTargetPos = target.position;

        Vector3 toTarget = lastKnownTargetPos - transform.position;
        float dist = toTarget.magnitude;

        // Hit: only deal damage if the target is still alive.
        if (dist <= hitRadius)
        {
            if (targetAlive) ApplyDamage();
            Destroy(gameObject);
            return;
        }

        Vector3 dir = toTarget / Mathf.Max(dist, 0.0001f);
        transform.position += dir * speed * Time.deltaTime;

        if (faceTravelDirection)
            FaceDirection(dir);
    }

    private void FaceDirection(Vector3 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (!float.IsNaN(angle) && !float.IsInfinity(angle))
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    // Shield interaction path 
    // While the shot is inside the player's reaction radius, ask the shield each
    // frame whether it intercepts.
    private void TryProjectileParry()
    {
        if (!ProjectileParry.TryResolve(transform.position, out var shield, out var playerT, out int parryingIndex)
            || playerT == null)
        {
            HideParryPrompt();
            return;
        }

        float distToPlayer = Vector2.Distance(transform.position, playerT.position);
        if (distToPlayer > parryReactRadius)
        {
            HideParryPrompt();
            return;
        }

        // Per-player unlock: only THIS (nearest) player's projectile-parry augment counts.
        bool parryUnlocked = ProjectileParry.UnlockedFor(parryingIndex);

        // The "!" prompt only advertises a parry, so only show it once the
        // bounce-back augment is unlocked. Blocking needs no prompt (just hold).
        if (parryUnlocked) ShowParryPrompt();
        else HideParryPrompt();

        var result = shield.TryInterceptProjectile(transform.position);

        bool canBounce = result == ShieldSystem.ProjectileInterception.Parried
                         && parryUnlocked;

        if (canBounce)
        {
            shield.PlayProjectileParryFeedback(transform.position);
            // Stun + debuff the firer exactly like a melee parry does (ShieldSystem
            // .ApplyParry), using the PARRYING player's upgrades — so Powerful Parry
            // (331) and Longer Parry Stun (330) apply per-player to projectile
            // parries too. The firer can be null (it died); ApplyOrRefresh +
            // BecomeParried both handle that, so guard here.
            if (firer != null)
                ParryStunEffect.ApplyOrRefresh(firer.gameObject, parryingIndex);
            BecomeParried();
        }
        else if (result != ShieldSystem.ProjectileInterception.None)
        {
            // Blocked, or a parry attempt without the augment → reduced damage.
            // Block feedback only (no gold parry phantom).
            shield.PlayProjectileBlockFeedback(transform.position);
            ApplyBlockedDamage(playerT, shield);
            HideParryPrompt();
            Destroy(gameObject);
        }
    }

    // A blocked shot still deals reduced damage to the player (shield damage
    // reduction), rather than being fully negated.
    private void ApplyBlockedDamage(Transform playerT, ShieldSystem shield)
    {
        var cs = playerT.GetComponent<CharacterStats>();
        if (cs != null)
            cs.TakeDamage(damage * shield.BlockDamageMultiplier);
    }

    // Flip the shot around to home back into the enemy that fired it.
    private void BecomeParried()
    {
        if (parried) return;
        HideParryPrompt();

        // No firer to bounce back to (it died) — the parry just neutralizes it.
        if (firer == null)
        {
            Destroy(gameObject);
            return;
        }

        parried = true;
        target = firer.transform;
        lastKnownTargetPos = target.position;
        speed *= parryReturnSpeedMultiplier;
    }

    private void ShowParryPrompt()
    {
        if (parryPrompt == null)
            parryPrompt = ProjectileParryIndicator.Attach(transform, yOffset: 0.5f, size: 0.4f);
    }

    private void HideParryPrompt()
    {
        if (parryPrompt != null)
        {
            Destroy(parryPrompt.gameObject);
            parryPrompt = null;
        }
    }

    private void ApplyDamage()
    {
        // Parried shot → damage the enemy it bounced back into.
        if (parried)
        {
            DamageFirer();
            return;
        }

        if (target == null) return;

        // Preferred path: reuse the firing controller's damage routing so the
        // projectile behaves exactly like that enemy's melee hit would — except
        // the shield interaction was already resolved in flight, so we tell the
        // controller not to re-check it (viaProjectile: true).
        if (firer != null)
        {
            firer.ApplyDamageToTarget(target, viaProjectile: true);
            return;
        }

        // Fallback (firer already destroyed): apply damage directly. Mirrors the
        // two damage sinks EnemyController.ApplyDamageToTarget handles —
        // CharacterStats (player) and IEnergyConsumer (towers / core).
        var charStats = target.GetComponent<CharacterStats>();
        if (charStats != null)
        {
            charStats.TakeDamage(damage);
            return;
        }

        var consumer = target.GetComponent<IEnergyConsumer>();
        if (consumer != null && EnergyManager.Instance != null)
        {
            EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
        }
    }

    // Deal the reflected shot's damage to the enemy that originally fired it.
    private void DamageFirer()
    {
        if (firer == null) return;

        var cs = firer.GetComponent<CharacterStats>();
        if (cs == null) return;

        float dmg = damage * parryReflectMultiplier;

        // Respect an active parry-stun damage bonus on the enemy, same as a melee
        // hit would (Weapon.OnTriggerStay2D does the equivalent).
        var stun = firer.GetComponent<ParryStunEffect>();
        if (stun != null) dmg *= stun.DamageMultiplier;

        cs.TakeDamage(dmg);
        CombatJuice.OnPlayerHitEnemy(firer.gameObject, isMelee: false);
    }

    private void OnDestroy()
    {
        HideParryPrompt();
    }
}

