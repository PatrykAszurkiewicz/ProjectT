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

    public void RegenerateMana(float amount)
    {
        currentMana += amount;
        if (currentMana > maxMana) currentMana = maxMana;
    }
}