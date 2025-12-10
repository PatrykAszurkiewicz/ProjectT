using UnityEngine;
using System.Collections;

public class MomentumEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float healthMultiplierPerKill = 0f;

    [System.NonSerialized]
    public float damageMultiplierPerKill = 0f;

    [Header("Current State")]
    [SerializeField] private int consecutiveKills = 0;

    private float baseDamage = 0f;
    private float baseMaxHealth = 0f;
    private Weapon weapon;
    private WeaponData weaponData;
    private PlayerStats playerStats;
    private bool isInitialized = false;
    private float lastKnownHealth = -1f;
    private Coroutine healthMonitorCoroutine;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();

        if (playerStats == null)
        {
            Debug.LogError("[MOMENTUM] PlayerStats component not found!");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        // Validate CSV values were set
        if (Mathf.Approximately(healthMultiplierPerKill, 0f) || Mathf.Approximately(damageMultiplierPerKill, 0f))
        {
            Debug.LogError("[MOMENTUM] Multipliers are 0! CSV values were NOT applied by StatApplicator!");
            enabled = false;
            return;
        }

        // Capture base health
        baseMaxHealth = playerStats.maxHealth;

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
            Debug.LogError("[MOMENTUM] Could not find Weapon component!");
            enabled = false;
            yield break;
        }

        yield return null;

        weaponData = weapon.GetWeaponData();

        if (weaponData == null)
        {
            Debug.LogError("[MOMENTUM] Weapon data is null!");
            enabled = false;
            yield break;
        }

        baseDamage = weaponData.damage;
        isInitialized = true;

        //Debug.Log($"[MOMENTUM] Initialized - Base damage: {baseDamage:F1}, Base health: {baseMaxHealth:F1}, Health: {healthMultiplierPerKill * 100f:F1}%/kill, Damage: {damageMultiplierPerKill * 100f:F1}%/kill");
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
            Debug.LogError("[MOMENTUM] Failed to find EnergyManager after retries!");
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

        // Restore original stats
        if (isInitialized)
        {
            if (weaponData != null)
            {
                weaponData.damage = baseDamage;
            }
            if (playerStats != null)
            {
                playerStats.maxHealth = baseMaxHealth;
                playerStats.currentHealth = Mathf.Min(playerStats.currentHealth, baseMaxHealth);
            }
        }
    }

    private void OnEnemyKilled(GameObject enemy)
    {
        if (!isInitialized) return;

        consecutiveKills++;
        UpdateStats();

        float healthMult = GetHealthMultiplier();
        float damageMult = GetDamageMultiplier();

        //Debug.Log($"[MOMENTUM] Kill #{consecutiveKills} | Health: {healthMult:F3}x (+{(healthMult - 1) * 100:F1}%) | Damage: {damageMult:F3}x (+{(damageMult - 1) * 100:F1}%)");
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
        if (consecutiveKills == 0) return;

        //Debug.Log($"[MOMENTUM] Player damaged! Resetting {consecutiveKills} kills");

        consecutiveKills = 0;
        UpdateStats();
    }

    private void UpdateStats()
    {
        if (!isInitialized) return;

        // Update damage
        if (weaponData != null)
        {
            float damageMultiplier = GetDamageMultiplier();
            weaponData.damage = baseDamage * damageMultiplier;
        }

        // Update health
        if (playerStats != null)
        {
            float healthMultiplier = GetHealthMultiplier();
            float newMaxHealth = baseMaxHealth * healthMultiplier;

            // Calculate health difference to add to current health
            float healthDifference = newMaxHealth - playerStats.maxHealth;

            playerStats.maxHealth = newMaxHealth;
            playerStats.currentHealth += healthDifference;
            playerStats.currentHealth = Mathf.Clamp(playerStats.currentHealth, 0f, playerStats.maxHealth);
        }
    }

    // Public getters for UI
    public int GetConsecutiveKills() => consecutiveKills;

    public float GetHealthMultiplier()
    {
        // Formula: 1 + (percentPerKill * killCount)
        // Example: 0.03 per kill * 10 kills = 0.30 = 130% (1.30x)
        return 1f + (healthMultiplierPerKill * consecutiveKills);
    }

    public float GetDamageMultiplier()
    {
        // Formula: 1 + (percentPerKill * killCount)
        return 1f + (damageMultiplierPerKill * consecutiveKills);
    }
}

