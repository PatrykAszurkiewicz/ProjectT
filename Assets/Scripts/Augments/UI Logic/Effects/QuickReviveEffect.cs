using UnityEngine;
using System.Collections;

public class QuickReviveEffect : MonoBehaviour
{
    [Header("Revive Settings")]
    public float reviveHealthPercentage = 0.5f; // 50% health after revive
    public float reviveInvulnerabilityDuration = 2f; // Brief invulnerability after revive
    public float reviveDelay = 0.1f; // Small delay before revival

    [Header("Visual Feedback")]
    public Color reviveFlashColor = new Color(0f, 1f, 0.5f, 1f); // Green flash
    public float reviveFlashDuration = 0.3f;
    public int reviveFlashCount = 3;

    private bool hasBeenUsedThisWave = false;
    private PlayerStats playerStats;
    private SpriteRenderer spriteRenderer;
    private bool isProcessingRevive = false;
    private int lastWaveNumber = -1;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (playerStats == null)
        {
            Debug.LogError("[QUICK_REVIVE] PlayerStats component not found!");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        // Start monitoring wave changes for reset
        StartCoroutine(MonitorWaveChanges());
        //Debug.Log("[QUICK_REVIVE] Quick Revive effect initialized ");
    }

    private IEnumerator MonitorWaveChanges()
    {
        yield return new WaitForSeconds(0.5f); // Initial delay

        WaveSpawner spawner = FindFirstObjectByType<WaveSpawner>();

        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            if (spawner != null)
            {
                int currentWave = spawner.GetCurrentWaveIndex();
                if (currentWave != lastWaveNumber && lastWaveNumber != -1)
                {
                    ResetRevive();
                }
                lastWaveNumber = currentWave;
            }
        }
    }

    void Update()
    {
        // Check if player is dead and revive is available
        // This needs to happen immediately when IsDead() becomes true
        if (playerStats.IsDead() && !hasBeenUsedThisWave && !isProcessingRevive)
        {
            //Debug.Log("[QUICK_REVIVE] Death detected Triggering revive");
            StartCoroutine(PerformRevive());
        }
    }

    private IEnumerator PerformRevive()
    {
        isProcessingRevive = true;
        hasBeenUsedThisWave = true;

        //Debug.Log("[QUICK_REVIVE] Player died Activating Quick Revive...");

        // Play revive sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            //TODO add revive sound
            //AudioManager.instance.PlayOneShot(FMODEvents.instance.weaponPickup, transform.position);
        }

        // Small delay for dramatic effect
        yield return new WaitForSeconds(reviveDelay);

        // Revive player with 50% health
        float reviveHealth = playerStats.maxHealth * reviveHealthPercentage;
        playerStats.SetHealthAndNotify(reviveHealth);

        //Debug.Log($"[QUICK_REVIVE] Player revived with {reviveHealth:F0}/{playerStats.maxHealth:F0} HP ({reviveHealthPercentage * 100}%)");

        // Visual feedback - revive flash
        StartCoroutine(ReviveFlashEffect());

        // Grant brief invulnerability after revive
        GrantTemporaryInvulnerability();

        isProcessingRevive = false;
    }

    private void GrantTemporaryInvulnerability()
    {
        // Check if ImmunityPhasesEffect already exists
        var permanentImmunity = GetComponent<ImmunityPhasesEffect>();

        if (permanentImmunity != null)
        {
            //Debug.Log("[QUICK_REVIVE] Player has permanent immunity augment - skipping temporary immunity");
            return;
        }

        // Add temporary immunity component
        var tempImmunity = gameObject.GetComponent<TemporaryReviveImmunity>();
        if (tempImmunity != null)
        {
            Destroy(tempImmunity);
        }

        tempImmunity = gameObject.AddComponent<TemporaryReviveImmunity>();
        tempImmunity.Initialize(reviveInvulnerabilityDuration);

        //Debug.Log($"[QUICK_REVIVE] Granted {reviveInvulnerabilityDuration}s temporary invulnerability");
    }

    private IEnumerator ReviveFlashEffect()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;

        for (int i = 0; i < reviveFlashCount; i++)
        {
            spriteRenderer.color = reviveFlashColor;
            yield return new WaitForSeconds(reviveFlashDuration / (reviveFlashCount * 2));

            spriteRenderer.color = originalColor;
            yield return new WaitForSeconds(reviveFlashDuration / (reviveFlashCount * 2));
        }

        spriteRenderer.color = originalColor;
    }

    private void ResetRevive()
    {
        if (hasBeenUsedThisWave)
        {
            hasBeenUsedThisWave = false;
            //Debug.Log("[QUICK_REVIVE] 🔄 Revive recharged for new wave!");
        }
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    // Public getters for UI
    public bool IsAvailable() => !hasBeenUsedThisWave && !isProcessingRevive;
    public bool HasBeenUsed() => hasBeenUsedThisWave;
}

// Temporary immunity component specifically
public class TemporaryReviveImmunity : MonoBehaviour
{
    private float endTime;
    private bool isActive = false;

    public void Initialize(float seconds)
    {
        endTime = Time.time + seconds;
        isActive = true;
        //Debug.Log($"[TEMP_IMMUNITY] Activated for {seconds} seconds");
    }

    void Update()
    {
        if (isActive && Time.time >= endTime)
        {
            //Debug.Log("[TEMP_IMMUNITY] Temporary immunity expired");
            Destroy(this);
        }
    }

    public bool ShouldBlockDamage()
    {
        return isActive && Time.time < endTime;
    }
}
