using UnityEngine;
using System.Collections;

public class BerserkersFuryEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float damageIncreasePerKill = 0f;

    [Header("Visual Feedback")]
    public Color stackEffectColor = new Color(1f, 0.3f, 0.3f, 0.6f);
    public float maxStackGlowIntensity = 0.8f;

    private int killCount = 0;
    private float baseDamage = 0f;
    private Weapon weapon;
    private WeaponData weaponData;
    private PlayerStats playerStats;
    private bool isInitialized = false;
    private float lastKnownHealth = -1f;
    private Coroutine healthMonitorCoroutine;

    // Visual effect
    private SpriteRenderer playerRenderer;
    private Color originalColor;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        playerRenderer = GetComponent<SpriteRenderer>();

        if (playerStats == null)
        {
            Debug.LogError("[BERSERKER] PlayerStats component not found!");
            enabled = false;
            return;
        }

        if (playerRenderer != null)
        {
            originalColor = playerRenderer.color;
        }
    }

    void Start()
    {

        if (Mathf.Approximately(damageIncreasePerKill, 0f))
        {
            Debug.LogError("[BERSERKER] damageIncreasePerKill is 0! CSV value was NOT applied by StatApplicator!");
            enabled = false;
            return;
        }

        //Debug.Log($"[BERSERKER] Starting with CSV value: {damageIncreasePerKill * 100f:F1}% per kill (multiplicative stacking)");

        StartCoroutine(InitializeWeaponReference());

        // Subscribe to enemy kill events
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent += OnEnemyKilled;
        }
        else
        {
            StartCoroutine(RetryEnergyManagerSubscription());
        }

        // Start monitoring player health
        healthMonitorCoroutine = StartCoroutine(MonitorPlayerHealth());
    }

    private IEnumerator InitializeWeaponReference()
    {
        yield return null;

        // Find weapon
        weapon = GetComponentInChildren<Weapon>();

        if (weapon == null)
        {
            var playerAttack = GetComponent<PlayerAttack>();
            if (playerAttack != null)
            {
                weapon = playerAttack.GetComponentInChildren<Weapon>();
            }
        }

        if (weapon == null)
        {
            weapon = Object.FindFirstObjectByType<Weapon>();

        }

        if (weapon == null)
        {
            Debug.LogError("[BERSERKER] Could not find Weapon component!");
            enabled = false;
            yield break;
        }

        yield return null;

        weaponData = weapon.GetWeaponData();

        if (weaponData == null)
        {
            Debug.LogError("[BERSERKER] Weapon data is null!");
            enabled = false;
            yield break;
        }

        baseDamage = weaponData.damage;
        isInitialized = true;

        //Debug.Log($"[BERSERKER] Initialized - Base damage: {baseDamage:F1}, Weapon: {weaponData.weaponName}, CSV rate: {damageIncreasePerKill * 100f:F1}%");
    }

    private IEnumerator RetryEnergyManagerSubscription()
    {
        int retryCount = 0;
        while (EnergyManager.Instance == null && retryCount < 10)
        {
            yield return new WaitForSeconds(1f);
            retryCount++;
        }

        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent += OnEnemyKilled;
        }
        else
        {
            Debug.LogError("[BERSERKER] Failed to find EnergyManager after retries!");
        }
    }

    void OnDestroy()
    {
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent -= OnEnemyKilled;
        }

        if (healthMonitorCoroutine != null)
        {
            StopCoroutine(healthMonitorCoroutine);
        }

        // Restore original weapon damage
        if (isInitialized && weaponData != null)
        {
            weaponData.damage = baseDamage;
        }

        if (playerRenderer != null)
        {
            playerRenderer.color = originalColor;
        }
    }

    private void OnEnemyKilled(GameObject enemy)
    {
        if (!isInitialized) return;

        killCount++;
        UpdateWeaponDamage();
        UpdateVisualEffect();

        // Play stack sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            //TODO Add audio effect
            //AudioManager.instance.PlayOneShot(FMODEvents.instance.weaponPickup, transform.position);
        }

        float multiplier = GetDamageMultiplier();
        float percentIncrease = (multiplier - 1f) * 100f;

        //Debug.Log($"[BERSERKER] Kill #{killCount} | {multiplier:F4}x | +{percentIncrease:F1}% | Damage: {baseDamage:F1} → {weaponData.damage:F1}");
    }

    private IEnumerator MonitorPlayerHealth()
    {
        yield return new WaitUntil(() => playerStats != null);

        lastKnownHealth = playerStats.currentHealth;

        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            if (playerStats.currentHealth < lastKnownHealth - 0.1f)
            {
                OnPlayerDamaged();
            }

            lastKnownHealth = playerStats.currentHealth;
        }
    }

    private void OnPlayerDamaged()
    {
        if (killCount == 0) return;

        float oldMultiplier = GetDamageMultiplier();
        //Debug.Log($"[BERSERKER] Player damaged! Resetting {killCount} stacks ({oldMultiplier:F3}x damage lost)");

        // Play reset sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            //TODO Add audio effect
            //AudioManager.instance.PlayOneShot(FMODEvents.instance.playerDeath, transform.position);
        }

        killCount = 0;
        UpdateWeaponDamage();
        UpdateVisualEffect();
    }

    private void UpdateWeaponDamage()
    {
        if (!isInitialized || weaponData == null) return;

        float multiplier = GetDamageMultiplier();
        float newDamage = baseDamage * multiplier;

        weaponData.damage = newDamage;

        // Log milestone kills
        if (killCount > 0 && (killCount % 10 == 0 || killCount == 5))
        {
            float percentIncrease = (multiplier - 1f) * 100f;
            //Debug.Log($"[BERSERKER] Milestone: {killCount} kills! {baseDamage:F1} → {newDamage:F1} (+{percentIncrease:F1}%)");
        }
    }

    private void UpdateVisualEffect()
    {
        if (playerRenderer == null) return;

        if (killCount == 0)
        {
            playerRenderer.color = originalColor;
        }
        else
        {
            float stackIntensity = Mathf.Clamp01(killCount / 50f) * maxStackGlowIntensity;
            Color glowColor = Color.Lerp(originalColor, stackEffectColor, stackIntensity);
            playerRenderer.color = glowColor;
        }
    }

    private float GetDamageMultiplier()
    {
        // Formula: (1 + percentPerKill)^killCount
        return Mathf.Pow(1f + damageIncreasePerKill, killCount);
    }

    // Public getters for UI
    public int GetKillCount() => killCount;
    public float GetCurrentMultiplier() => GetDamageMultiplier();
    public float GetBaseDamage() => baseDamage;
    public float GetCurrentDamage() => weaponData?.damage ?? 0f;
}
