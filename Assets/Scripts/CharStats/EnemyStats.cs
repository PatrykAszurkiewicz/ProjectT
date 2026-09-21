using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyStats : CharacterStats
{
    [Header("Health Bar")]
    public GameObject healthBarPrefab;
    private EnemyHealthBar healthBar;

    [Header("Enemy Data")]
    public EnemyData enemyData;

    [Header("Energy Drop Settings")]
    [Range(0f, 1f)] public float energyDropChance = -1f;
    public int energyDropValue = -1;
    public bool canDropEnergy = true;

    [Header("Visual Feedback")]
    public float damageFlashDuration = 0.1f;
    public Color damageFlashColor = new Color(2f, 2f, 2f, 1f); // Additive bright white
    private SpriteRenderer spriteRenderer;
    private Coroutine damageFlashCoroutine;

    // The colour the sprite was showing immediately BEFORE the current flash
    // sequence began - i.e. what the flash must restore to when it ends.
    //
    // BUGFIX: DamageFlashCoroutine used to read spriteRenderer.color at the top of
    // EVERY run and treat that as the colour to restore. StartDamageFlash stops any
    // in-flight coroutine and immediately starts a new one, so when the stop landed
    // during the lit half of the blink, the new run captured damageFlashColor
    // (additive bright white) as "original" and restored to THAT from then on. Any
    // enemy hit twice inside ~0.15s - anything under sustained tower fire - latched
    // permanently white.
    //
    // The fix captures this ONLY when no flash is already running, so it is still
    // "whatever the sprite looked like before we touched it" and never the flash
    // colour. That distinction matters: several systems tint an enemy for a while
    // (EnemyController.ApplyFreeze -> cyan, ConfusedEnemy -> magenta, BerserkEnemy
    // -> red, GremlinController -> green/red state tints) and a hit taken during one
    // of those must return to the TINT, not to the spawn colour. A fixed
    // spawn-time snapshot would have quietly stripped those tints on every hit.
    private Color preFlashColor = Color.white;

    [Header("Death VFX (optional)")]
    [Tooltip("If > 0, EnemyDeathVFX.Trigger() is fired on death. " +
             "Values below 1.0 use the lighter 'classic chunks' disintegration; " +
             "values 1.0+ trigger the full boss-style sprite-shatter. " +
             "Leave at 0 to disable (default — preserves legacy behavior).")]
    [SerializeField] protected float baseDeathVfxDuration = 0f;

    [Tooltip("If true (and baseDeathVfxDuration > 0), the health bar is destroyed " +
             "BEFORE the death VFX plays so it doesn't float above the " +
             "disintegration. Ignored when baseDeathVfxDuration is 0.")]
    [SerializeField] protected bool baseDestroyHealthBarBeforeVfx = true;

    // Guard so the VFX is only triggered once even if Die() somehow fires twice.
    private bool deathVfxFired = false;

    /// <summary>
    /// Public runtime setter for the death VFX. Controllers that build their
    /// enemy from code (like BufferController) call this in Awake so they
    /// don't depend on prefab inspector values being set correctly. Pass
    /// duration &gt; 0 to enable, 0 to disable.
    /// </summary>
    public void ConfigureDeathVfx(float duration, bool destroyHealthBarBeforeVfx = true)
    {
        baseDeathVfxDuration = duration;
        baseDestroyHealthBarBeforeVfx = destroyHealthBarBeforeVfx;
    }

    [Header("Death VFX Style")]
    [Tooltip("Which parts of the death effect play.\n\n" +
             "Auto (default): Base Death Vfx Duration decides both, exactly as " +
             "before — under 1.0 gives the cheap generic puff, 1.0 or over gives " +
             "sprite disintegration AND the big dust blast.\n\n" +
             "Disintegrate Only: the enemy breaks into pieces of its own sprite " +
             "with NO dust blast. Use this for smaller enemies, where a " +
             "boss-sized blast is out of proportion. Still needs a duration of " +
             "1.0 or more to have time to play.\n\n" +
             "Disintegrate With Blast: both, regardless of duration.\n\n" +
             "Classic Only: the generic puff, regardless of duration.")]
    [SerializeField] protected DeathVfxStyle deathVfxStyle = DeathVfxStyle.Auto;

    /// Companion-component setter, matching ConfigureDeathVfx above.
    public void ConfigureDeathVfxStyle(DeathVfxStyle style)
    {
        deathVfxStyle = style;
    }

    protected SpriteRenderer SpriteRenderer => spriteRenderer;
    protected EnemyHealthBar HealthBar => healthBar;

    /// Public accessor for the runtime-instantiated health bar. Used by support
    /// enemies (e.g. Scarecrow) that need to hide the bar during invisible
    /// phases. May be null before Start() runs or if no healthBarPrefab is set.
    public EnemyHealthBar GetHealthBar() => healthBar;

    // =========================================================================
    //  SHARED DEATH BOOK-KEEPING
    // -------------------------------------------------------------------------
    //  Static so the hand-rolled death paths can reuse it: Boss1/Boss2/Boss3
    //  (ExecuteBossDeath), EyeStats (PerformEyeDeath) and BomberController
    //  (PerformExplosionDeath) all bypass PerformDeath() because they need custom
    //  death behaviour. Each re-implemented this tail by hand and each
    //  independently forgot the SAME two calls — so augment 335 (Energy Tithe)
    //  silently paid nothing for those kills and TowerKillAttribution leaked an
    //  entry per corpse.
    //
    //  NOT INCLUDED: energy / blueprint drops. Every death path deliberately owns
    //  its own drop behaviour (the Eye's fixed drop, the bosses' reward ring, the
    //  Bomber's own roll, the standard stage roll). Putting drops here would
    //  double them up.
    //
    //  CALL ORDER is load-bearing and must not be reshuffled:
    //     1. WaveSpawner.OnEnemyDeath()     - internal live counter
    //     2. TowerKillRewards.OnEnemyKilled - augment 335, READS the attribution
    //     3. EnergyManager.OnEnemyKilled    - kill tracking + OnEnemyKilledEvent
    //                                         (lifesteal / health-on-kill augments)
    //     4. TowerKillAttribution.Forget    - MUST be last: clears what (2) reads
    // =========================================================================

    /// Fire the shared death book-keeping for `enemy`. Safe to call from any death
    /// path; every step is individually null-guarded.
    ///
    /// `notifyWaveSpawner` exists only for callers that already decremented the
    /// spawner themselves — pass false there so the counter isn't double-decremented.
    public static void FireCommonDeathHooks(GameObject enemy, bool notifyWaveSpawner = true)
    {
        if (enemy == null) return;

        if (notifyWaveSpawner)
        {
            WaveSpawner waveSpawner = FindAnyObjectByType<WaveSpawner>();
            if (waveSpawner != null)
                waveSpawner.OnEnemyDeath();
        }

        // Augment 335 - Energy Tithe: flat energy when a tower made the kill.
        // Reads TowerKillAttribution, so it MUST run before Forget() below.
        TowerKillRewards.OnEnemyKilled(enemy);

        if (EnergyManager.Instance != null)
            EnergyManager.Instance.OnEnemyKilled(enemy);

        // Clear tower-kill attribution so the lookup table doesn't grow.
        TowerKillAttribution.Forget(enemy);
    }

    // =========================================================================
    //  EXTERNAL BEHAVIOUR OVERRIDE (Confusion / Berserk augments)
    // -------------------------------------------------------------------------
    //  ConfusedEnemy and BerserkEnemy used to assume every enemy is driven by an
    //  EnemyController and did exactly one thing: enemyController.enabled = false.
    //  That is wrong for a growing number of enemies:
    //
    //    * BufferController, ParfumerController and BomberController have NO
    //      EnemyController — they replace it and drive Rigidbody2D.linearVelocity
    //      from their own FixedUpdate. FixedUpdate runs after the effect's Update,
    //      so the enemy's own velocity won every physics step: the augment did
    //      nothing but tint the sprite while the Buffer kept dropping fog, the
    //      Parfumer kept poisoning and the Bomber kept charging the core.
    //
    //    * Bosses run their attacks from their own coroutines that no component
    //      toggle can stop, and Boss3 additionally hard-writes transform.position
    //      every frame. Bosses are refused outright by CanBeExternallyControlled.
    //
    //  ADDING A NEW SELF-DRIVING ENEMY: append its controller type to
    //  ExternallySuspendableControllers below. An enemy whose controller is not
    //  listed behaves exactly as it does today (no worse), so this list being
    //  non-exhaustive is safe, just incomplete.
    // =========================================================================

    private static readonly System.Type[] ExternallySuspendableControllers =
    {
        typeof(EnemyController),
        typeof(BufferController),
        typeof(ParfumerController),
        typeof(BomberController),
    };

    // ── Crowd-control immunity ────────────────────────────────────────────────
    //
    // Enemies that augments 29 (Friendly Fire / berserk) and 76 (Pheromones /
    // confusion) must never affect. An enemy belongs here when ANY of these is true:
    //
    //   (a) It has no conventional melee attack, so "turn it on its allies" is
    //       meaningless — the effect can only ever remove it from the fight.
    //   (b) It runs its own attack loop independent of EnemyController, so
    //       suspending the controller does not actually stop it.
    //   (c) It is stationary or anchored by design, so steering it is either a no-op
    //       or breaks the state machine that owns its position.
    //
    // Presence of ANY listed component on the enemy grants immunity. Listing the
    // controller rather than the stats type means Insect and EliteInsect are both
    // covered by one entry, and no prefab editing is required.
    //
    // To exempt one more enemy: add its controller type here. To exempt a single
    // prefab without touching code, tick `immuneToCrowdControl` on its EnemyStats.
    private static readonly System.Type[] CrowdControlImmuneComponents =
    {
        // Burrowing ambusher. Kinematic, anchored, and its dive/tunnel/leap state
        // machine bails whenever EnemyController is disabled — which froze the leap
        // mid-arc and launched it across the map on resume. (a) and (c).
        typeof(InsectController),

        // Suicide bomber. Its "attack" is detonating on the Core; there is nothing to
        // redirect at another enemy. (a).
        typeof(BomberController),

        // Poison-aura support with no melee attack. (a).
        typeof(ParfumerController),

        // Fog-aura support with no melee attack — same shape as the Parfumer. (a).
        typeof(BufferController),

        // Stationary buff totem. Disables its own EnemyController in Awake, owns an
        // appear/disappear state machine, and its aura is a child object that keeps
        // running regardless. (a), (b) and (c).
        typeof(Scarecrow),
    };

    [Tooltip("Exempt this enemy from the crowd-control augments (29 Friendly Fire, " +
             "76 Pheromones). Per-prefab override; enemy types exempted in code are " +
             "immune regardless of this flag.")]
    public bool immuneToCrowdControl = false;

    /// True if an external effect (confusion / berserk) can meaningfully drive this
    /// enemy. Refuses bosses, dead enemies and the crowd-control-immune types above.
    public static bool CanBeExternallyControlled(GameObject enemy)
    {
        if (enemy == null) return false;

        var stats = enemy.GetComponent<EnemyStats>();
        if (stats == null) return false;
        if (stats is BaseBossStats) return false;
        if (stats.IsDead()) return false;

        // Per-prefab opt-out.
        if (stats.immuneToCrowdControl) return false;

        // Hazard spawner rather than a combatant: it has no melee attack and its
        // spawn loop is its own coroutine, so CC cannot meaningfully change it.
        if (stats is VortexStats) return false;

        for (int i = 0; i < CrowdControlImmuneComponents.Length; i++)
            if (enemy.GetComponent(CrowdControlImmuneComponents[i]) != null) return false;

        // Body type. KINEMATIC is fine — Rigidbody2D.linearVelocity moves a Kinematic
        // body, and several existing enemies are steered that way.
        //
        // STATIC is refused. Unity cannot set velocity on a static body at all (it
        // logs "Cannot use 'linearVelocity' on a static body"), so an external effect
        // has no way to move it. Before the suspension fix that only meant the effect
        // was a harmless no-op; now that we genuinely disable the enemy's own
        // controller, accepting a static body would leave it COMPLETELY FROZEN for the
        // whole effect instead — strictly worse than doing nothing. An enemy with no
        // Rigidbody2D at all is still accepted, preserving the old "effective stun".
        var rb = enemy.GetComponent<Rigidbody2D>();
        if (rb != null && rb.bodyType == RigidbodyType2D.Static) return false;

        return true;
    }

    /// Disable every known self-driving controller on `enemy`, returning the list of
    /// components actually turned off. Pass that same list to
    /// RestoreBehaviourControllers().
    public static List<Behaviour> SuspendBehaviourControllers(GameObject enemy)
    {
        var suspended = new List<Behaviour>();
        if (enemy == null) return suspended;

        for (int i = 0; i < ExternallySuspendableControllers.Length; i++)
        {
            var comp = enemy.GetComponent(ExternallySuspendableControllers[i]) as Behaviour;
            // Only touch components that are currently ON, so Restore can never switch
            // something back on that was deliberately off.
            if (comp != null && comp.enabled)
            {
                comp.enabled = false;
                suspended.Add(comp);
            }
        }

        return suspended;
    }

    /// Re-enable exactly the components SuspendBehaviourControllers() turned off.
    ///
    /// `skipIfDead` guards the case where the enemy died while the effect was
    /// running: a death routine disables the controller on purpose, and blindly
    /// re-enabling it would let a corpse resume moving or fire a queued shot.
    public static void RestoreBehaviourControllers(
        GameObject enemy, List<Behaviour> suspended, bool skipIfDead = true)
    {
        if (suspended == null) return;

        if (skipIfDead && enemy != null)
        {
            var stats = enemy.GetComponent<EnemyStats>();
            if (stats != null && stats.IsDead())
            {
                suspended.Clear();
                return;
            }
        }

        for (int i = 0; i < suspended.Count; i++)
        {
            if (suspended[i] != null)
                suspended[i].enabled = true;
        }

        suspended.Clear();
    }

    // Death fail-safe
    // Most enemies reach CharacterStats.Die() -> Destroy(gameObject) and cannot get
    // stuck. A few do NOT: Boss1/Boss2/Boss3 and ScarecrowStats hand teardown off to
    // EnemyDeathVFX.Trigger() and never call Destroy themselves. If that VFX fails to
    // run to completion (missing component, unreadable sprite, an exception inside the
    // effect) the object stays alive in the scene forever — which stalls
    // GameOrchestrator.WaitForBossDead() for a boss, and keeps a corpse counting as a
    // living enemy in CountLivingEnemiesInScene for anything else.
    //
    // This schedules a guaranteed Destroy well AFTER the VFX should have finished. On
    // the happy path the object is already gone and Unity drops the pending destroy,
    // so the normal death is completely unaffected.
    [Tooltip("Extra seconds to wait past the death VFX duration before force-destroying " +
             "this GameObject. Only used by death paths that delegate teardown to " +
             "EnemyDeathVFX, and only ever fires if that VFX failed to clean up. The " +
             "margin is deliberately generous so it can never cut a healthy VFX short.")]
    public float deathFailsafeExtraSeconds = 5f;

    /// Call at the END of a death routine that does NOT call base.Die(), passing the
    /// same duration handed to EnemyDeathVFX.Trigger().
    protected void ScheduleDeathFailsafe(float vfxDuration)
    {
        float delay = Mathf.Max(0f, vfxDuration) + Mathf.Max(0.5f, deathFailsafeExtraSeconds);
        Destroy(gameObject, delay);
    }

    /// Re-push this enemy's CAPACITY to its world-space health bar.
    ///
    /// The bar's maximum is baked in once at Initialize() time, so anything that
    /// changes maxHealth after Start() (the retroactive rescale in
    /// EnemyStatModifierManager.ApplyHealthChangeToExistingEnemies, driven by an
    /// enemy-MaxHealth augment picked mid-fight) left the bar reading against a
    /// stale denominator. Callers that mutate maxHealth at runtime should call this
    /// afterwards. No-op when there is no bar.
    public virtual void RefreshHealthBarCapacity()
    {
        if (healthBar != null)
            healthBar.SetMaxHealth(maxHealth, currentHealth);
    }

    protected void CallStartDamageFlash()
    {
        StartDamageFlash();
    }

    protected virtual void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            preFlashColor = spriteRenderer.color;

            // REMOVED: originalMaterial / flashMaterial.
            // Both were written here and destroyed in OnDestroy, and read nowhere
            // else - StartDamageFlash tints spriteRenderer.color, not the material.
            // So this was two wasted Material allocations per enemy spawn, and worse:
            // reading `spriteRenderer.material` (the getter, as opposed to
            // sharedMaterial) forces Unity to INSTANTIATE a per-renderer material
            // copy, taking every enemy out of the batch it would otherwise share.
            // Nothing outside this class could see either field (both private), so
            // removing them changes no behaviour. Any other system that wants a
            // per-instance material still gets one lazily from the .material getter.
        }

        if (enemyData != null)
        {
            enemyData = ScriptableObjectUtility.Clone(enemyData);

            maxHealth = enemyData.maxHealth;

            if (EnemyStatModifierManager.Instance != null)
            {
                // Augment health multiplier — applies to every enemy, bosses
                // included (unchanged behaviour).
                maxHealth *= EnemyStatModifierManager.Instance.GetHealthMultiplier();

                // Per-stage health scaling, composed multiplicatively on top.
                // Regular enemies always; bosses only when the run opts in, because
                // a boss's armour pool and special-attack damage do NOT scale, so
                // inflating only its HP would unbalance the fight by default.
                if (!(this is BaseBossStats) || EnemyStatModifierManager.Instance.StageScalingAffectsBosses)
                    maxHealth *= EnemyStatModifierManager.Instance.GetStageHealthMultiplier();

            }

            // Difficulty (Normal/Nightmare) HP — regular enemies here; bosses take
            // theirs (HP + armour) in BaseBossStats.Awake so it isn't applied twice.
            //
            // DELIBERATELY OUTSIDE the `Instance != null` block above. DifficultyHealth-
            // Multiplier is a static read that needs no live manager, but it used to sit
            // inside that block: if an enemy's Awake beat the manager's Awake (component
            // order is not guaranteed, and SetStageScaling can create the manager lazily)
            // the enemy spawned with NO Nightmare scaling at all. On Normal this is a
            // no-op (x1), so nothing changes for existing Normal runs.
            if (!(this is BaseBossStats))
                maxHealth *= EnemyStatModifierManager.DifficultyHealthMultiplier;

            currentHealth = maxHealth;

            // Regular enemies take their armor from EnemyData. Bosses manage
            // their own armor pool (BaseBossStats.bossArmor); seeding
            // currentArmor on them would stack a SECOND mitigation layer that
            // kicks in after their armor is destroyed, making them nearly
            // unkillable. So skip bosses here.
            if (!(this is BaseBossStats))
                currentArmor = enemyData.maxArmor;
        }

        EnemyStatModifierManager.Instance?.RegisterEnemy(this);
    }

    // PROTECTED VIRTUAL (was private). Boss1/Boss2/Boss3 each declare their own
    // OnDestroy; Unity dispatches only the MOST-DERIVED declaration, so while this
    // was private the bosses silently hid it and never unregistered themselves from
    // EnemyStatModifierManager (leaking a HashSet entry per boss) nor released their
    // flash material. Those three now `override` this and call base.OnDestroy().
    // Any other EnemyStats subclass that declares its own private OnDestroy() still
    // compiles (with a CS0108 hide warning) and behaves exactly as it did before.
    protected virtual void OnDestroy()
    {
        EnemyStatModifierManager.Instance?.UnregisterEnemy(this);

        // (The flash material this used to destroy is gone - see Awake.)
    }

#if UNITY_EDITOR
    // Editor-only: keep the prefab's inspector showing the REAL values that
    // EnemyData will impose at runtime, instead of stale hand-typed numbers.
    // Never runs in a build, and never runs during Play so it can't reset a
    // live enemy's currentHealth mid-fight.
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (enemyData == null) return;

        maxHealth = enemyData.maxHealth;
        currentHealth = enemyData.maxHealth;
        if (!(this is BaseBossStats))
            currentArmor = enemyData.maxArmor;
    }
#endif

    //private void Start()
    protected virtual void Start()

    {
        if (healthBarPrefab != null)
        {
            GameObject bar = Instantiate(healthBarPrefab);
            healthBar = bar.GetComponent<EnemyHealthBar>();

            if (healthBar != null)
            {
                healthBar.Initialize(transform, maxHealth);
            }
        }
    }

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField, ReadOnly] private float currentMoveSpeedDebug;
#endif

#if UNITY_EDITOR
    private void Update()
    {
        currentMoveSpeedDebug = MoveSpeed;
    }
#endif

    public override void TakeDamage(float amount)
    {
        //Debug.Log($"[ENEMY_STATS] TakeDamage amount={amount}");

        base.TakeDamage(amount);

        StartDamageFlash();

        if (healthBar != null)
            healthBar.UpdateHealth(currentHealth);
    }

    // The base Heal() updates currentHealth and raises OnHealthChanged, but the
    // enemy health bar is driven by explicit UpdateHealth calls (see TakeDamage
    // above), not by that event - so healing an enemy used to move its real HP
    // while the bar stayed frozen at the damaged value until the next hit.
    // Mirroring TakeDamage here keeps the bar honest for every enemy heal
    // (WolfController's lifesteal, and any future healer/regen enemy).
    // Purely additive: nothing previously depended on the bar being stale.
    public override void Heal(float amount)
    {
        base.Heal(amount);

        if (healthBar != null)
            healthBar.UpdateHealth(currentHealth);
    }

    private void StartDamageFlash()
    {
        if (spriteRenderer == null) return;

        if (damageFlashCoroutine != null)
        {
            // Already mid-flash. preFlashColor is ALREADY the pre-flash colour, so
            // leave it alone - re-capturing here is exactly the bug. Put the sprite
            // back before relaunching so the stop doesn't strand it lit.
            StopCoroutine(damageFlashCoroutine);
            spriteRenderer.color = preFlashColor;
        }
        else
        {
            // Not flashing, so whatever is on screen right now IS the resting look -
            // including any freeze / confusion / berserk tint currently applied.
            preFlashColor = spriteRenderer.color;
        }

        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        // Restores to preFlashColor, captured by StartDamageFlash before the FIRST
        // blink of this sequence - never re-read here, which is what used to let the
        // flash colour become the restore target. See the field comment.

        // Double blink 
        for (int i = 0; i < 2; i++)
        {
            spriteRenderer.color = damageFlashColor;
            yield return new WaitForSeconds(damageFlashDuration);
            if (spriteRenderer == null) yield break;
            spriteRenderer.color = preFlashColor;
            yield return new WaitForSeconds(0.05f);
            if (spriteRenderer == null) yield break;
        }

        damageFlashCoroutine = null;
    }


    public override void Die()
    {
        //Debug.Log($"[ENEMY_STATS] Die() called on {gameObject.name}");

        // Opt-in disintegration VFX. Fires once, before anything else, so the
        // health bar is gone before the first frame of the effect plays.
        // Default (baseDeathVfxDuration == 0) means this whole block is skipped
        // and the original death behavior is bit-for-bit preserved.
        TryFireDeathVfx();

        // Stop all movement and reset rotation before death animation.
        // BUGFIX (pre-existing, surfaced by the console): Unity REFUSES velocity
        // writes on a Static body and logs "Cannot use 'linearVelocity' on a static
        // body" every single time. Several enemy prefabs use Static bodies (see
        // CanBeExternallyControlled, which refuses them for the same reason), so every
        // one of their deaths spat two errors. A Static body has no velocity to clear
        // in the first place, so skipping it is a pure no-op besides the silence.
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null && rb.bodyType != RigidbodyType2D.Static)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        transform.rotation = Quaternion.identity;
        // Check if this enemy has death animation
        var animController = GetComponent<EnemyAnimationController>();
        if (animController != null && enemyData != null && enemyData.death.frameCount > 0)
        {
            // Play death animation and delay destruction
            animController.PlayDeathAnimation();
            StartCoroutine(DelayedDeath());
            return;
        }

        // No animation - die immediately (old behavior)
        PerformDeath();
    }

    // Fires the opt-in disintegration VFX exactly once per enemy lifetime.
    // No-op when baseDeathVfxDuration <= 0, which is the default 
    protected void TryFireDeathVfx()
    {
        if (baseDeathVfxDuration <= 0f) return;
        if (deathVfxFired) return;
        deathVfxFired = true;

        if (baseDestroyHealthBarBeforeVfx && healthBar != null)
        {
            Destroy(healthBar.gameObject);
            healthBar = null;
        }

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: baseDeathVfxDuration,
            onComplete: null,
            style: deathVfxStyle
        );
    }

    private IEnumerator DelayedDeath()
    {
        // Disable components so enemy can't move/attack while dying
        var controller = GetComponent<EnemyController>();
        if (controller != null) controller.enabled = false;

        // Same static-body guard as Die(). This is the SECOND site: Die() hands off
        // to DelayedDeath for any enemy with a death animation, so guarding only
        // Die() silenced the no-animation deaths and left this one still spamming
        // "Cannot use 'linearVelocity' on a static body" for every animated death.
        // (rb.simulated below is fine on a Static body; only the velocity setters throw.)
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null && rb.bodyType != RigidbodyType2D.Static) rb.linearVelocity = Vector2.zero;

        var collider = GetComponent<Collider2D>();
        if (collider != null) collider.enabled = false;

        if (rb != null) rb.simulated = false;

        // Wait for animation to complete
        float animDuration = enemyData.deathAnimationDuration;
        yield return new WaitForSeconds(animDuration);

        // Destroy the enemy
        PerformDeath();
    }
    private void PerformDeath()
    {
        var gremlinController = GetComponent<GremlinController>();
        if (gremlinController != null)
        {
            if (healthBar != null)
                Destroy(healthBar.gameObject);

            gremlinController.Die();
            return;
        }

        if (canDropEnergy)
        {
            // Augments 341 / 342 honour the per-enemy override (e.g. the Wolf's
            // guaranteed drop) and add tower-kill bonuses where applicable.
            int stageIdx = GameOrchestrator.Instance?.CurrentStageIndex ?? 0;
            EnemyDropAugments.SpawnEnemyDrop(
                transform.position, stageIdx, gameObject, energyDropChance, energyDropValue);

            // If this is a boss, also spawn the boss burst on top
            if (GetComponent<BaseBossStats>() != null)
                EnergyDropManager.SpawnBossDrop(transform.position, stageIdx);
        }

        if (healthBar != null)
            Destroy(healthBar.gameObject);

        // Shared death book-keeping (wave counter -> augment 335 tithe ->
        // EnergyManager kill event -> attribution cleanup), in exactly the order it
        // ran here before. Hoisted to the static helper above so the hand-rolled death
        // paths (bosses, EyeStats, BomberController) can fire the identical set —
        // they each used to miss the tithe and the attribution cleanup.
        EnemyStats.FireCommonDeathHooks(gameObject);

        base.Die();
    }



    /// The full outgoing-damage multiplier for THIS enemy: augment multiplier x
    /// per-stage scaling x difficulty, with the same boss gate `Damage` has always
    /// used. Extracted from `Damage` so attacks whose damage does NOT come from
    /// EnemyData can scale identically — see ScaleDamage below.
    ///
    /// Difficulty is applied even when EnemyStatModifierManager.Instance is null
    /// (it is a static read). Previously the whole calculation was skipped in that
    /// case, silently dropping Nightmare scaling; on Normal this is x1 either way,
    /// so no existing Normal run changes.
    public float DamageMultiplier
    {
        get
        {
            float multiplier = 1f;

            var mgr = EnemyStatModifierManager.Instance;
            if (mgr != null)
            {
                multiplier *= mgr.GetDamageMultiplier();

                // Per-stage damage scaling, composed multiplicatively with the
                // augment multiplier. Regular enemies always; bosses only when opted
                // in. (Bosses generally attack via their own damage fields rather
                // than this property, so this mainly keeps any boss that DOES read
                // .Damage consistent with the health-scaling gate above.)
                if (!(this is BaseBossStats) || mgr.StageScalingAffectsBosses)
                    multiplier *= mgr.GetStageDamageMultiplier();
            }

            // Difficulty (Normal/Nightmare) damage — applies to EVERY enemy, boss
            // melee included. Boss special attacks scale separately via
            // BaseBossStats.BossStageDamageMultiplier.
            multiplier *= EnemyStatModifierManager.DifficultyDamageMultiplier;

            return multiplier;
        }
    }

    /// Scales a hard-coded / serialized damage number the same way `Damage` scales
    /// EnemyData.damage. Use this for any attack whose damage does NOT live in
    /// EnemyData — DoT auras, lasers with their own DPS field, etc. — so those
    /// attacks honour the enemy-damage augment and per-stage scaling instead of
    /// only the difficulty multiplier (or nothing at all).
    public float ScaleDamage(float rawDamage) => rawDamage * DamageMultiplier;

    public float Damage
    {
        get
        {
            // NOTE: baseDamage is the per-enemy CLONED enemyData.damage, so any
            // per-enemy growth/buff (e.g. the Berserk's eat growth that multiplies
            // its own clone) is already baked into baseDamage and compounds here.
            float baseDamage = enemyData?.damage ?? 0f;
            return baseDamage * DamageMultiplier;
        }
    }

    public float MoveSpeed
    {
        get
        {
            float baseSpeed = enemyData?.moveSpeed ?? 1f;
            if (EnemyStatModifierManager.Instance != null)
            {
                float multiplier = EnemyStatModifierManager.Instance.GetMoveSpeedMultiplier();
                float finalSpeed = baseSpeed * multiplier;
                return finalSpeed;
            }
            return baseSpeed;
        }
    }

    public float Mass => enemyData?.mass ?? 50f;

    public void ConfigureEnergyDrop(float dropChance, int dropValue)
    {
        energyDropChance = Mathf.Clamp01(dropChance);
        energyDropValue = Mathf.Max(1, dropValue);
    }

    public void DisableEnergyDrops()
    {
        canDropEnergy = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!canDropEnergy) return;

        float chance = energyDropChance >= 0 ? energyDropChance : (EnergyDropManager.Instance?.globalDropChance ?? 0.5f);
        int value = energyDropValue > 0 ? energyDropValue : (EnergyDropManager.Instance?.defaultEnergyValue ?? 10);

        UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, $"Drop: {(chance * 100f):F0}%");
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.3f, $"Energy: {value}");
    }
#endif
}

#if UNITY_EDITOR
// Shows maxHealth / currentHealth / currentArmor as READ-ONLY on enemies,
// because EnemyData is the source of truth and OnValidate keeps them synced.
// They stay visible (useful for watching HP tick down in Play mode) but can't
// be hand-edited into a value the runtime will silently overwrite.
//
// This targets EnemyStats and its subclasses ONLY (the `true` flag), so the
// Player — which uses CharacterStats directly and DOES author these in the
// inspector — is completely unaffected.
[UnityEditor.CustomEditor(typeof(EnemyStats), true)]
public class EnemyStatsEditor : UnityEditor.Editor
{
    private static readonly string[] DataDriven =
        { "maxHealth", "currentHealth", "currentArmor" };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Everything except the three data-driven fields, drawn normally.
        DrawPropertiesExcluding(serializedObject, DataDriven);

        // The three data-driven fields, drawn disabled (read-only).
        using (new UnityEditor.EditorGUI.DisabledScope(true))
        {
            foreach (var prop in DataDriven)
            {
                var sp = serializedObject.FindProperty(prop);
                if (sp != null) UnityEditor.EditorGUILayout.PropertyField(sp);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif





