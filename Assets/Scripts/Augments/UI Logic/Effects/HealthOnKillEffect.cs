using UnityEngine;
using System.Collections;

public class HealthOnKillEffect : MonoBehaviour
{
    [Header("Heal Settings")]
    public float healthPerKill = 5f;

    [Header("Visual Feedback")]
    public Color healFlashColor = new Color(0f, 1f, 0f, 0.8f); // Green heal flash
    public float healFlashDuration = 0.2f;

    private PlayerStats playerStats;
    private SpriteRenderer spriteRenderer;
    private int totalKills = 0;
    private float totalHealthRestored = 0f;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (playerStats == null)
        {
            Debug.LogError("[HEALTH_ON_KILL] PlayerStats component not found!");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        // Subscribe to enemy kill events
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent += OnEnemyKilled;
            //Debug.Log($"[HEALTH_ON_KILL] Effect initialized - {healthPerKill} HP per kill");
        }
        else
        {
            Debug.LogError("[HEALTH_ON_KILL] EnergyManager not found!");
            StartCoroutine(RetryEnergyManagerSubscription());
        }
    }

    private IEnumerator RetryEnergyManagerSubscription()
    {
        int retryCount = 0;
        while (EnergyManager.Instance == null && retryCount < 10)
        {
            yield return new WaitForSeconds(0.5f);
            retryCount++;
        }

        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent += OnEnemyKilled;
            //Debug.Log("[HEALTH_ON_KILL] Successfully subscribed to enemy kill events after retry");
        }
        else
        {
            Debug.LogError("[HEALTH_ON_KILL] Failed to find EnergyManager after retries!");
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent -= OnEnemyKilled;
        }

        // Log statistics
        if (totalKills > 0)
        {
            //Debug.Log($"[HEALTH_ON_KILL] Final stats - Kills: {totalKills}, Total HP restored: {totalHealthRestored:F0}");
        }
    }

    private void OnEnemyKilled(GameObject enemy)
    {
        if (playerStats == null || playerStats.IsDead())
            return;

        // Check if player needs healing
        if (playerStats.currentHealth >= playerStats.maxHealth)
        {
            // Player at full health - no healing needed
            return;
        }

        // Calculate actual healing (don't exceed max health)
        float healthBefore = playerStats.currentHealth;
        float actualHeal = Mathf.Min(healthPerKill, playerStats.maxHealth - playerStats.currentHealth);

        // Heal the player
        playerStats.Heal(actualHeal);

        // Update statistics
        totalKills++;
        totalHealthRestored += actualHeal;

        // Visual and audio feedback
        TriggerHealFeedback();

        // Log every 5 kills
        if (totalKills % 5 == 0)
        {
            //Debug.Log($"[HEALTH_ON_KILL] Kill #{totalKills} - Healed {actualHeal:F1} HP ({healthBefore:F0} → {playerStats.currentHealth:F0})");
        }
    }

    private void TriggerHealFeedback()
    {
        // Visual flash effect
        StartCoroutine(HealFlashEffect());

        // Play heal sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            // TODO Add heal audio effect
            //AudioManager.instance.PlayOneShot(FMODEvents.instance.weaponPickup, transform.position);
        }
    }

    private IEnumerator HealFlashEffect()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;

        // Quick green flash
        spriteRenderer.color = healFlashColor;
        yield return new WaitForSeconds(healFlashDuration);
        spriteRenderer.color = originalColor;
    }

    // Public getters for UI/stats display
    public int GetTotalKills() => totalKills;
    public float GetTotalHealthRestored() => totalHealthRestored;
    public float GetHealthPerKill() => healthPerKill;
}
