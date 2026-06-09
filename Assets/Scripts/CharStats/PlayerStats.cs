using UnityEngine;
using System;
using System.Collections;

public class PlayerStats : CharacterStats
{
    [Header("Mana")]
    public float maxMana = 50f;
    public float currentMana = 50f;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.5f;

    [Header("Dashing")]
    public float dashForce = 5f;
    public int maxDashes = 3;
    public float dashTime = 0.2f;
    public float dashSpeed = 20f;
    public float dashCooldown = 1f;
    public float dashRegenRate = 2f;
    public int dashesLeft = 2;

    [Header("Stamina")]
    public float maxStamina = 5f;
    public float staminaRegenRate = 1f;
    public float staminaDrainRate = 1.5f;
    public float currentStamina = 5f;

    [Header("Stamina Costs — Actions")]
    [Tooltip("Stamina cost per melee swing.")]
    public float meleeAttackStaminaCost = 0.8f;
    [Tooltip("Stamina cost per ranged shot (bow / gun / boomerang).")]
    public float rangedAttackStaminaCost = 0.4f;
    [Tooltip("Stamina drained per second while flamethrower is firing.")]
    public float flamethrowerStaminaDrainPerSec = 1.4f;
    [Tooltip("Stamina cost when starting an obstacle draw.")]
    public float obstacleDrawerStaminaCost = 1.5f;
    [Tooltip("Stamina cost per grappling hook fire (smaller — mobility tool).")]
    public float grapplingHookStaminaCost = 1.2f;
    [Tooltip("Stamina cost per successful shield block or parry.")]
    public float shieldBlockStaminaCost = 1.3f;

    [Header("Health Regen")]
    public float healthRegenRate = 2f;
    public float healthRegenDelay = 3f;
    private float regenTimer = 0f;

    [Header("Visual Feedback")]
    public float damageFlashDuration = 0.1f;
    public Color damageFlashColor = new Color(2f, 2f, 2f, 1f); // Additive bright flash
    private SpriteRenderer spriteRenderer;
    private Coroutine damageFlashCoroutine;
    private Color baseColor = Color.white; // Store base color

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
        {
            Debug.LogWarning($"[PLAYER_BLINK] {gameObject.name}: SpriteRenderer not found!");
        }
        else
        {
            // Store the base color at start
            baseColor = spriteRenderer.color;
        }
    }

    // Called by PlayerMovement after it moves the SpriteRenderer to a child object
    // for Y-sorting. This ensures damage flash and all visual effects target the correct (visible) renderer.
    public void UpdateSpriteRenderer(SpriteRenderer newSR)
    {
        spriteRenderer = newSR;
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
        }
    }

    public override void Die()
    {
        // Check for Quick Revive before dying
        var quickRevive = GetComponent<QuickReviveEffect>();
        if (quickRevive != null && quickRevive.IsAvailable())
        {
            //Debug.Log("[PLAYER] Death intercepted ");
            // Not calling base.Die() - let QuickReviveEffect handle the revival
            return;
        }

        // No Quick Revive available - proceed with normal death
        // Debug.Log("[PLAYER] Player has died permanently");

        // Play death animation if available
        var movement = GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.PlayDeathAnimation();
        }

        base.Die();
    }
    public void SetHealthAndNotify(float newHealth)
    {
        currentHealth = Mathf.Clamp(newHealth, 0f, maxHealth);
        TriggerHealthChangedEvent(); // Call the protected method from base class
    }

    public override void TakeDamage(float amount)
    {
        //Debug.Log($"[PLAYER] ========== TakeDamage({amount}) CALLED ==========");

        // Check for Berserker Mode damage amplification
        var berserkerMode = GetComponent<BerserkerModeEffect>();
        if (berserkerMode != null)
        {
            amount = berserkerMode.ModifyIncomingDamage(amount);
        }

        // Augment 78 (Increased Shield Defenses): flat % reduction to incoming damage.
        // Placed here so it covers every damage source, not just one call site.
        var damageReduction = GetComponent<PlayerDamageReductionEffect>();
        if (damageReduction != null)
        {
            amount = damageReduction.Apply(amount);
        }

        // Check for immunity (augment ID 11)
        var immunityPhases = GetComponent<ImmunityPhasesEffect>();

        //Debug.Log($"[PLAYER] ImmunityPhasesEffect component: {(immunityPhases != null ? "EXISTS" : "NULL")}");

        if (immunityPhases != null)
        {
            //Debug.Log($"[PLAYER] Component found - enabled: {immunityPhases.enabled}, isActiveAndEnabled: {immunityPhases.isActiveAndEnabled}");

            bool shouldBlock = immunityPhases.ShouldBlockDamage();
            //Debug.Log($"[PLAYER] ShouldBlockDamage returned: {shouldBlock}");

            if (shouldBlock)
            {
                //Debug.Log("[PLAYER] ✓✓✓ Damage BLOCKED by Immunity Phases! ✓✓✓");
                return; // No damage taken
            }
        }
        else
        {
            //Debug.Log("[PLAYER] No ImmunityPhasesEffect component found!");
        }


        // Check for temporary revive immunity (augment ID 37)
        var tempImmunity = GetComponent<TemporaryReviveImmunity>();
        if (tempImmunity != null && tempImmunity.ShouldBlockDamage())
        {
            //Debug.Log("[PLAYER] Damage BLOCKED by temporary revive immunity!");
            return; // No damage taken
        }

        //Debug.Log($"[PLAYER] Applying damage: {amount}");
        base.TakeDamage(amount);
        regenTimer = 0f;

        // Trigger damage flash
        StartDamageFlash();
    }
    private void StartDamageFlash()
    {
        if (spriteRenderer == null) return;

        // Restore color before stopping
        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
            spriteRenderer.color = baseColor; // Force restore
        }

        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;
        for (int i = 0; i < 2; i++)
        {
            spriteRenderer.color = damageFlashColor;
            yield return new WaitForSeconds(damageFlashDuration);
            spriteRenderer.color = baseColor; // CHANGED: use baseColor
            yield return new WaitForSeconds(0.05f);
        }

        spriteRenderer.color = baseColor;
        damageFlashCoroutine = null;
    }

    private void Update()
    {
        if (!IsDead() && currentHealth < maxHealth)
        {
            regenTimer += Time.deltaTime;

            if (regenTimer >= healthRegenDelay)
            {
                Heal(healthRegenRate * Time.deltaTime);
            }
        }
    }

    // Cleanup on disable
    private void OnDisable()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = baseColor;
        }

        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
            damageFlashCoroutine = null;
        }
    }

    // Cleanup on destroy
    private void OnDestroy()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = baseColor;
        }
    }

    public void UseMana(float amount)
    {
        currentMana -= amount;
        if (currentMana < 0) currentMana = 0;
    }

    // Returns true if the player has at least <paramref name="amount"/> stamina available
    // (used to gate offensive actions — they shouldn't even fire if exhausted).
    public bool HasStamina(float amount)
    {
        return currentStamina >= amount;
    }

    // Atomically check + consume stamina. Returns true if the cost was paid in full.
    // Use this for one-shot actions (melee swing, ranged shot, obstacle draw start,
    // grappling hook fire) where the action should be blocked if there isn't enough.
    public bool TryConsumeStamina(float amount)
    {
        if (amount <= 0f) return true;
        if (currentStamina < amount) return false;
        currentStamina -= amount;
        currentStamina = Mathf.Max(currentStamina, 0f);
        return true;
    }

    // Drain stamina without gating (drains whatever is available, clamped to 0).

    public bool DrainStamina(float amount)
    {
        if (amount <= 0f) return false;
        currentStamina -= amount;
        if (currentStamina <= 0f)
        {
            currentStamina = 0f;
            return true;
        }
        return false;
    }

    public void RegenerateMana(float amount)
    {
        currentMana += amount;
        if (currentMana > maxMana) currentMana = maxMana;
    }

    // ============================================================
    //  Animated maxStamina change
    // ============================================================
    // Mirror of CharacterStats.SetMaxHealthAnimated for the stamina pool.
    //
    // StaminaBarUI polls currentStamina/maxStamina every Update, and the
    // underlying ResourceBarUI distinguishes ratio-down (damage shadow) from
    // ratio-up (fill). We preserve the ratio first, then animate up — and
    // when the player is at/near full stamina (the natural fill-up would be
    // invisible) we briefly dip currentStamina by `maxStaminaMinPulseFraction`
    // and tween it back up. Reads as a confident pulse, not damage.
    [Header("Capacity Change Animation (Stamina / Mana)")]
    [Tooltip("Duration of the smooth fill-up tween when maxStamina increases.")]
    public float maxStaminaIncreaseAnimDuration = 0.5f;
    [Tooltip("Minimum visible motion (as a fraction of maxStamina) when an augment is picked. Same role as maxHealthMinPulseFraction.")]
    [Range(0f, 0.5f)]
    public float maxStaminaMinPulseFraction = 0.18f;
    [Tooltip("Duration of the smooth fill-up tween when maxMana increases.")]
    public float maxManaIncreaseAnimDuration = 0.5f;
    [Tooltip("Minimum visible motion (as a fraction of maxMana) when an augment is picked.")]
    [Range(0f, 0.5f)]
    public float maxManaMinPulseFraction = 0.18f;

    private Coroutine _maxStaminaTween;
    private Coroutine _maxManaTween;

    public void SetMaxStaminaAnimated(float newMax)
    {
        if (newMax <= 0f || float.IsNaN(newMax) || float.IsInfinity(newMax))
        {
            Debug.LogWarning($"[PlayerStats] SetMaxStaminaAnimated ignored invalid value: {newMax}");
            return;
        }

        float oldMax = maxStamina;

        if (newMax <= oldMax)
        {
            maxStamina = newMax;
            if (currentStamina > maxStamina) currentStamina = maxStamina;
            return;
        }

        // Preserve fill ratio across the cap change.
        float ratio = (oldMax > 0f) ? Mathf.Clamp01(currentStamina / oldMax) : 1f;
        maxStamina = newMax;
        currentStamina = maxStamina * ratio;

        if (_maxStaminaTween != null)
        {
            StopCoroutine(_maxStaminaTween);
            _maxStaminaTween = null;
        }

        if (!isActiveAndEnabled) return;

        // Guarantee visible motion: if the natural fill gap is too small, dip
        // currentStamina to widen it. Pulse reads as a positive flash.
        float naturalGapFraction = (maxStamina - currentStamina) / maxStamina;
        float minGapFraction = Mathf.Clamp01(maxStaminaMinPulseFraction);
        if (naturalGapFraction < minGapFraction)
        {
            currentStamina = maxStamina * (1f - minGapFraction);
        }

        if (currentStamina < maxStamina)
        {
            _maxStaminaTween = StartCoroutine(AnimateStaminaFillUp());
        }
    }

    private IEnumerator AnimateStaminaFillUp()
    {
        float startValue = currentStamina;
        float targetValue = maxStamina;
        float duration = Mathf.Max(0.05f, maxStaminaIncreaseAnimDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Player started consuming stamina mid-tween — let gameplay win,
            // existing regen will refill from wherever we land.
            if (currentStamina < startValue - 0.01f) { _maxStaminaTween = null; yield break; }
            // maxStamina changed again (another augment) — fresh tween took over.
            if (!Mathf.Approximately(maxStamina, targetValue)) { _maxStaminaTween = null; yield break; }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);
            currentStamina = Mathf.Lerp(startValue, targetValue, eased);
            yield return null;
        }

        currentStamina = targetValue;
        _maxStaminaTween = null;
    }

    // Animated maxMana change
    // No augment touches maxMana today, but adding this preemptively means
    // when one is introduced it Just Works without re-fixing the bug.
    public void SetMaxManaAnimated(float newMax)
    {
        if (newMax <= 0f || float.IsNaN(newMax) || float.IsInfinity(newMax))
        {
            Debug.LogWarning($"[PlayerStats] SetMaxManaAnimated ignored invalid value: {newMax}");
            return;
        }

        float oldMax = maxMana;

        if (newMax <= oldMax)
        {
            maxMana = newMax;
            if (currentMana > maxMana) currentMana = maxMana;
            return;
        }

        float ratio = (oldMax > 0f) ? Mathf.Clamp01(currentMana / oldMax) : 1f;
        maxMana = newMax;
        currentMana = maxMana * ratio;

        if (_maxManaTween != null)
        {
            StopCoroutine(_maxManaTween);
            _maxManaTween = null;
        }

        if (!isActiveAndEnabled) return;

        float naturalGapFraction = (maxMana - currentMana) / maxMana;
        float minGapFraction = Mathf.Clamp01(maxManaMinPulseFraction);
        if (naturalGapFraction < minGapFraction)
        {
            currentMana = maxMana * (1f - minGapFraction);
        }

        if (currentMana < maxMana)
        {
            _maxManaTween = StartCoroutine(AnimateManaFillUp());
        }
    }

    private IEnumerator AnimateManaFillUp()
    {
        float startValue = currentMana;
        float targetValue = maxMana;
        float duration = Mathf.Max(0.05f, maxManaIncreaseAnimDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (currentMana < startValue - 0.01f) { _maxManaTween = null; yield break; }
            if (!Mathf.Approximately(maxMana, targetValue)) { _maxManaTween = null; yield break; }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);
            currentMana = Mathf.Lerp(startValue, targetValue, eased);
            yield return null;
        }

        currentMana = targetValue;
        _maxManaTween = null;
    }
}

