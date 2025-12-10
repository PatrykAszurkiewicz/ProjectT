using UnityEngine;
using System.Collections;

public class ImmunityPhasesEffect : MonoBehaviour
{
    private const float DEFAULT_IMMUNITY_DURATION = 2f;
    private const float DEFAULT_IMMUNITY_COOLDOWN = 15f;

    [Header("Immunity Settings")]
    [System.NonSerialized]
    public float immunityDuration = 0f;  // Set from constant in Awake
    [System.NonSerialized]
    public float immunityCooldown = 0f;  // Set from constant in Awake

    [Header("Visual Feedback")]
    public Color immunityColor = new Color(0.5f, 0.8f, 1f, 0.8f);
    public float flashSpeed = 8f;

    private PlayerStats playerStats;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;

    private bool isImmune = false;
    private bool isOnCooldown = false;
    private float cooldownTimer = 0f;

    private Coroutine immunityCoroutine;
    private Coroutine visualEffectCoroutine;

    void Awake()
    {
        // Set default values from constants
        immunityDuration = DEFAULT_IMMUNITY_DURATION;
        immunityCooldown = DEFAULT_IMMUNITY_COOLDOWN;

        playerStats = GetComponent<PlayerStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (playerStats == null)
        {
            Debug.LogError("[IMMUNITY_PHASES] PlayerStats not found!");
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
        //Debug.Log($"[IMMUNITY_PHASES] ✓ Component active: {immunityDuration}s immunity, {immunityCooldown}s cooldown");
    }

    void OnDestroy()
    {
        // Restore original color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }

    void Update()
    {
        // Update cooldown timer
        if (isOnCooldown)
        {
            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer <= 0f)
            {
                isOnCooldown = false;
                //Debug.Log("[IMMUNITY_PHASES] Cooldown finished - ready!");
            }
        }
    }

    // Called by PlayerStats.TakeDamage BEFORE applying damage
    public bool ShouldBlockDamage()
    {
        // If already immune, block damage
        if (isImmune)
        {
            //Debug.Log("[IMMUNITY_PHASES] Already immune - blocking damage");
            return true;
        }

        // If not immune and not on cooldown, activate immunity and allow THIS hit
        if (!isOnCooldown)
        {
            //Debug.Log("[IMMUNITY_PHASES] First hit received - activating immunity AFTER this hit");
            // We let this hit through, but activate immunity for future hits
            ActivateImmunityAfterHit();
            return false; // Allow this first hit
        }

        // On cooldown - allow damage
        //Debug.Log("[IMMUNITY_PHASES] On cooldown - allowing damage");
        return false;
    }

    private void ActivateImmunityAfterHit()
    {
        if (immunityCoroutine != null)
        {
            StopCoroutine(immunityCoroutine);
        }

        immunityCoroutine = StartCoroutine(ImmunityCoroutine());
    }

    private IEnumerator ImmunityCoroutine()
    {
        // Small delay to ensure damage from first hit is processed
        yield return new WaitForSeconds(0.05f);

        isImmune = true;
        //Debug.Log($"[IMMUNITY_PHASES] ✓✓✓ IMMUNITY ACTIVE for {immunityDuration}s! ✓✓✓");

        // Play sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.dashSound, transform.position);
        }

        // Start visual effect
        if (visualEffectCoroutine != null)
        {
            StopCoroutine(visualEffectCoroutine);
        }
        visualEffectCoroutine = StartCoroutine(ImmunityVisualEffect());

        // Wait for immunity duration
        yield return new WaitForSeconds(immunityDuration);

        // End immunity
        isImmune = false;
        isOnCooldown = true;
        cooldownTimer = immunityCooldown;

        // Stop visual effect
        if (visualEffectCoroutine != null)
        {
            StopCoroutine(visualEffectCoroutine);
            visualEffectCoroutine = null;
        }

        // Restore original color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        //Debug.Log($"[IMMUNITY_PHASES] Immunity ended. Cooldown: {immunityCooldown}s");
        immunityCoroutine = null;
    }

    private IEnumerator ImmunityVisualEffect()
    {
        if (spriteRenderer == null) yield break;

        float elapsed = 0f;
        while (isImmune)
        {
            elapsed += Time.deltaTime * flashSpeed;
            float alpha = Mathf.Lerp(0.3f, 1f, (Mathf.Sin(elapsed) + 1f) / 2f);
            Color flashColor = Color.Lerp(originalColor, immunityColor, alpha);
            spriteRenderer.color = flashColor;
            yield return null;
        }
    }

    // Public getters for UI
    public bool IsImmune() => isImmune;
    public bool IsOnCooldown() => isOnCooldown;
    public float GetCooldownRemaining() => cooldownTimer;
    public float GetImmunityCooldown() => immunityCooldown;
}
