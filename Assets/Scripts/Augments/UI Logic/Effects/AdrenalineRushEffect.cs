using UnityEngine;
using System.Collections;

public class AdrenalineRushEffect : MonoBehaviour
{
    [Header("CSV-Driven Parameters")]
    [System.NonSerialized] public float healthThreshold = 0.3f;
    [System.NonSerialized] public float effectDuration = 15f;
    [System.NonSerialized] public float cooldownDuration = 30f;
    [System.NonSerialized] public float attackSpeedMultiplier = 0.5f;
    [System.NonSerialized] public float movementSpeedMultiplier = 0.6f;

    [Header("Visual Feedback")]
    [SerializeField] private Color rushColor = new Color(1f, 0.5f, 0f, 0.8f);
    [SerializeField] private float glowIntensity = 0.6f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private PlayerStats playerStats;
    private Weapon weapon;
    private SpriteRenderer spriteRenderer;

    private bool isEffectActive = false;
    private bool isOnCooldown = false;
    private float originalAttackCooldown;
    private Color originalColor;

    // The exact WeaponData we boosted, and the exact cooldown value we wrote into
    // it. Both are needed because a weapon hot-swap (augments 2 / 66 / 93 / 318 /
    // 327) replaces the runtime WeaponData object mid-effect: RemoveBoosts used to
    // write weapon A's original cooldown onto weapon B, permanently corrupting B.
    // We now only restore the object we actually boosted, and only if it still holds
    // the value we wrote.
    private WeaponData boostedWeaponData;
    private float writtenAttackCooldown = float.NaN;

    // Movement is restored MULTIPLICATIVELY rather than by re-assigning a saved
    // absolute. Saving an absolute meant any move-speed augment picked during the
    // 15s rush was silently reverted when the rush ended.
    private float appliedMoveSpeedFactor = 1f;

    private Coroutine effectCoroutine;
    private Coroutine cooldownCoroutine;

    // Debug tracking
    private float lastHealthCheck = -1f;
    private float debugCheckInterval = 2f;
    private float debugTimer = 0f;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (playerStats == null)
        {
            Debug.LogError("[ADRENALINE_RUSH] PlayerStats not found!");
            enabled = false;
            return;
        }

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }

    void Start()
    {
        // Validate CSV values were applied
        if (Mathf.Approximately(healthThreshold, 0f))
        {
            Debug.LogWarning("[ADRENALINE_RUSH] healthThreshold is 0! Using default 0.3");
            healthThreshold = 0.3f;
        }
        if (Mathf.Approximately(effectDuration, 0f))
        {
            Debug.LogWarning("[ADRENALINE_RUSH] effectDuration is 0! Using default 15s");
            effectDuration = 15f;
        }

        // Find weapon
        weapon = GetComponentInChildren<Weapon>();
        if (weapon == null)
        {
            weapon = FindFirstObjectByType<Weapon>();
        }

        if (weapon == null)
        {
            Debug.LogError("[ADRENALINE_RUSH] Weapon not found!");
            enabled = false;
            return;
        }

        // Store original values
        var weaponData = weapon.GetWeaponData();
        if (weaponData != null)
        {
            originalAttackCooldown = weaponData.attackCooldown;
        }

        //Debug.Log($"[ADRENALINE_RUSH] Effect initialized from CSV:");
        //Debug.Log($"  Threshold: {healthThreshold * 100}% health");
        //Debug.Log($"  Duration: {effectDuration}s");
        //Debug.Log($"  Cooldown: {cooldownDuration}s");
        //Debug.Log($"  Attack Speed: {attackSpeedMultiplier}x cooldown (= {1f / attackSpeedMultiplier}x faster attacks)");
        //Debug.Log($"  Move Speed: +{movementSpeedMultiplier * 100}% bonus");
    }

    void Update()
    {
        // Debug logging every few seconds
        if (showDebugLogs)
        {
            debugTimer += Time.deltaTime;
            if (debugTimer >= debugCheckInterval)
            {
                debugTimer = 0f;
                float currentHealthPercent = playerStats.currentHealth / playerStats.maxHealth;
                Debug.Log($"[ADRENALINE_RUSH] Status Check - Health: {currentHealthPercent * 100:F1}%, Active: {isEffectActive}, Cooldown: {isOnCooldown}, Can Trigger: {ShouldTriggerEffect()}");
            }
        }

        // Keep the boost pointed at the weapon that is actually equipped.
        RetargetBoostIfWeaponSwapped();

        // Check if we should trigger the effect
        if (!isEffectActive && !isOnCooldown && ShouldTriggerEffect())
        {
            TriggerAdrenalineRush();
        }
    }

    private bool ShouldTriggerEffect()
    {
        if (playerStats == null || playerStats.IsDead())
            return false;

        float healthPercentage = playerStats.currentHealth / playerStats.maxHealth;
        bool shouldTrigger = healthPercentage <= healthThreshold;

        // Log when health crosses threshold
        if (showDebugLogs && Mathf.Abs(healthPercentage - lastHealthCheck) > 0.05f)
        {
            lastHealthCheck = healthPercentage;
            if (shouldTrigger && !isEffectActive && !isOnCooldown)
            {
                Debug.Log($"[ADRENALINE_RUSH] ⚡ Threshold reached! Health at {healthPercentage * 100:F1}% (threshold: {healthThreshold * 100}%)");
            }
        }

        return shouldTrigger;
    }

    private void TriggerAdrenalineRush()
    {
        if (effectCoroutine != null)
            StopCoroutine(effectCoroutine);

        effectCoroutine = StartCoroutine(AdrenalineRushCoroutine());
    }

    private IEnumerator AdrenalineRushCoroutine()
    {
        isEffectActive = true;

        // Apply boosts
        ApplyBoosts();

        // Visual feedback
        UpdateVisualEffect(true);

        // Play sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            //TODO Add audio
            //AudioManager.instance.PlayOneShot(FMODEvents.instance.dashSound, transform.position);
        }

        float healthPercentage = playerStats.currentHealth / playerStats.maxHealth;
        //Debug.Log($"[ADRENALINE_RUSH] ACTIVATED Health: {healthPercentage * 100:F1}%");
        //Debug.Log($"  Attack cooldown: {originalAttackCooldown}s → {originalAttackCooldown * attackSpeedMultiplier}s");
        //Debug.Log($"  Move speed: {originalMoveSpeed} → {originalMoveSpeed * (1f + movementSpeedMultiplier)}");

        // Wait for effect duration
        yield return new WaitForSeconds(effectDuration);

        // Remove boosts
        RemoveBoosts();

        // Restore visuals
        UpdateVisualEffect(false);

        isEffectActive = false;

        //Debug.Log($"[ADRENALINE_RUSH] Effect ended after {effectDuration}s, starting {cooldownDuration}s cooldown");

        // Start cooldown
        if (cooldownCoroutine != null)
            StopCoroutine(cooldownCoroutine);
        cooldownCoroutine = StartCoroutine(CooldownCoroutine());

        effectCoroutine = null;
    }

    private IEnumerator CooldownCoroutine()
    {
        isOnCooldown = true;
        //Debug.Log($"[ADRENALINE_RUSH] Cooldown started ({cooldownDuration}s)");

        yield return new WaitForSeconds(cooldownDuration);

        isOnCooldown = false;

        float currentHealthPercent = playerStats.currentHealth / playerStats.maxHealth;
        bool willRetrigger = currentHealthPercent <= healthThreshold;

        //Debug.Log($"[ADRENALINE_RUSH] Cooldown finished! Health: {currentHealthPercent * 100:F1}%, Will re-trigger: {willRetrigger}");

        cooldownCoroutine = null;
    }

    private void ApplyBoosts()
    {
        // Boost attack speed (reduce cooldown) on the weapon that is live RIGHT NOW.
        ApplyAttackBoostTo(weapon != null ? weapon.GetWeaponData() : null);

        // Boost movement speed (multiplicative, so it composes with other sources).
        appliedMoveSpeedFactor = 1f + movementSpeedMultiplier;
        float beforeMoveSpeed = playerStats.moveSpeed;
        playerStats.moveSpeed *= appliedMoveSpeedFactor;

        if (showDebugLogs)
        {
            Debug.Log($"[ADRENALINE_RUSH] Applied move boost: {beforeMoveSpeed} → {playerStats.moveSpeed}");
        }
    }

    private void ApplyAttackBoostTo(WeaponData weaponData)
    {
        boostedWeaponData = weaponData;
        writtenAttackCooldown = float.NaN;
        if (weaponData == null) return;

        originalAttackCooldown = weaponData.attackCooldown;
        weaponData.attackCooldown *= attackSpeedMultiplier;
        writtenAttackCooldown = weaponData.attackCooldown;

        if (showDebugLogs)
        {
            Debug.Log($"[ADRENALINE_RUSH] Applied attack boost: {originalAttackCooldown}s → {weaponData.attackCooldown}s cooldown");
        }
    }

    /// Undo the cooldown boost on the weapon we actually boosted — never on whatever
    /// happens to be equipped now — and only if that field still holds our value.
    private void RemoveAttackBoost()
    {
        if (boostedWeaponData != null
            && (float.IsNaN(writtenAttackCooldown)
                || Mathf.Approximately(boostedWeaponData.attackCooldown, writtenAttackCooldown)))
        {
            boostedWeaponData.attackCooldown = originalAttackCooldown;

            if (showDebugLogs)
            {
                Debug.Log($"[ADRENALINE_RUSH] Restored attack cooldown: {boostedWeaponData.attackCooldown}s");
            }
        }

        boostedWeaponData = null;
        writtenAttackCooldown = float.NaN;
    }

    /// Called each frame while the rush is active. If the player swapped weapons
    /// mid-rush, hand the boost over to the new WeaponData so the rush keeps working
    /// AND the old weapon gets its own value back.
    private void RetargetBoostIfWeaponSwapped()
    {
        if (!isEffectActive || weapon == null) return;

        WeaponData live = weapon.GetWeaponData();
        if (live == null || ReferenceEquals(live, boostedWeaponData)) return;

        RemoveAttackBoost();
        ApplyAttackBoostTo(live);
    }

    private void RemoveBoosts()
    {
        // Restore attack speed on the weapon we boosted.
        RemoveAttackBoost();

        // Restore movement speed by dividing OUR factor back out, so a move-speed
        // augment picked during the rush survives instead of being reverted.
        if (appliedMoveSpeedFactor > 0.0001f)
            playerStats.moveSpeed /= appliedMoveSpeedFactor;
        appliedMoveSpeedFactor = 1f;

        if (showDebugLogs)
        {
            Debug.Log($"[ADRENALINE_RUSH] Restored move speed: {playerStats.moveSpeed}");
        }
    }

    private void UpdateVisualEffect(bool active)
    {
        if (spriteRenderer == null) return;

        if (active)
        {
            Color glowColor = Color.Lerp(originalColor, rushColor, glowIntensity);
            spriteRenderer.color = glowColor;
        }
        else
        {
            spriteRenderer.color = originalColor;
        }
    }

    void OnDestroy()
    {
        if (isEffectActive)
        {
            RemoveBoosts();
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        if (effectCoroutine != null)
            StopCoroutine(effectCoroutine);

        if (cooldownCoroutine != null)
            StopCoroutine(cooldownCoroutine);
    }

    void OnDisable()
    {
        if (isEffectActive && weapon != null)
        {
            RemoveBoosts();
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }

    // Public getters for UI/debugging
    public bool IsEffectActive() => isEffectActive;
    public bool IsOnCooldown() => isOnCooldown;
    public float GetHealthThreshold() => healthThreshold;
    public float GetEffectDuration() => effectDuration;
    public float GetCooldownDuration() => cooldownDuration;
}

