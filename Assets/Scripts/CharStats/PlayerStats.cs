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

    public override void TakeDamage(float amount)
    {
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