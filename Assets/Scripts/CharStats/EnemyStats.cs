using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using System.Collections;

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
    private Material originalMaterial;
    private Material flashMaterial;

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

    protected SpriteRenderer SpriteRenderer => spriteRenderer;
    protected EnemyHealthBar HealthBar => healthBar;

    /// Public accessor for the runtime-instantiated health bar. Used by support
    /// enemies (e.g. Scarecrow) that need to hide the bar during invisible
    /// phases. May be null before Start() runs or if no healthBarPrefab is set.
    public EnemyHealthBar GetHealthBar() => healthBar;

    protected void CallStartDamageFlash()
    {
        StartDamageFlash();
    }

    protected virtual void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            // Store original material
            originalMaterial = spriteRenderer.material;

            // Create flash material with additive blending
            flashMaterial = new Material(originalMaterial);
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

    private void OnDestroy()
    {
        EnemyStatModifierManager.Instance?.UnregisterEnemy(this);

        // Clean up materials
        if (flashMaterial != null)
        {
            Destroy(flashMaterial);
        }
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

    private void StartDamageFlash()
    {
        if (spriteRenderer == null) return;

        if (damageFlashCoroutine != null)
            StopCoroutine(damageFlashCoroutine);
        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;

        // Double blink 
        for (int i = 0; i < 2; i++)
        {
            spriteRenderer.color = damageFlashColor;
            yield return new WaitForSeconds(damageFlashDuration);
            spriteRenderer.color = originalColor;
            yield return new WaitForSeconds(0.05f);
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

        // Stop all movement and reset rotation before death animation
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
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
            onComplete: null
        );
    }

    private IEnumerator DelayedDeath()
    {
        // Disable components so enemy can't move/attack while dying
        var controller = GetComponent<EnemyController>();
        if (controller != null) controller.enabled = false;

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

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
            // Per-enemy override: if the prefab specifies a positive
            // energyDropValue AND a non-negative drop chance, use those
            // directly. Otherwise fall back to the stage-driven default
            // table on EnergyDropManager. This keeps existing enemies
            // unchanged (defaults are -1 / -1) while letting specific
            // enemies like the Wolf guarantee a fixed drop (10 energy,
            // one unit, 100% chance).
            if (energyDropValue > 0 && energyDropChance >= 0f)
            {
                EnergyDropManager.TrySpawnEnergyDrop(transform.position, energyDropChance, energyDropValue);
            }
            else
            {
                EnergyDropManager.TrySpawnEnemyDrop(transform.position, GameOrchestrator.Instance?.CurrentStageIndex ?? 0);
            }
            // If this is a boss, also spawn the boss burst on top
            if (GetComponent<BaseBossStats>() != null)
                EnergyDropManager.SpawnBossDrop(transform.position, GameOrchestrator.Instance?.CurrentStageIndex ?? 0);
        }

        if (healthBar != null)
            Destroy(healthBar.gameObject);

        WaveSpawner waveSpawner = FindAnyObjectByType<WaveSpawner>();
        if (waveSpawner != null)
            waveSpawner.OnEnemyDeath();

        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilled(gameObject);
        }

        base.Die();
    }

    public float Damage
    {
        get
        {
            float baseDamage = enemyData?.damage ?? 0f;
            if (EnemyStatModifierManager.Instance != null)
            {
                float multiplier = EnemyStatModifierManager.Instance.GetDamageMultiplier();

                // Per-stage damage scaling, composed multiplicatively with the
                // augment multiplier. Regular enemies always; bosses only when opted
                // in. (Bosses generally attack via their own damage fields rather
                // than this property, so this mainly keeps any boss that DOES read
                // .Damage consistent with the health-scaling gate above.)
                if (!(this is BaseBossStats) || EnemyStatModifierManager.Instance.StageScalingAffectsBosses)
                    multiplier *= EnemyStatModifierManager.Instance.GetStageDamageMultiplier();

                // NOTE: baseDamage is the per-enemy CLONED enemyData.damage, so any
                // per-enemy growth/buff (e.g. the Berserk's eat growth that multiplies
                // its own clone) is already baked into baseDamage and compounds here.
                return baseDamage * multiplier;
            }
            return baseDamage;
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

