using UnityEngine;

public class LifestealEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float lifestealPercentage = 0f;

    private PlayerStats playerStats;
    private Weapon weapon;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[LIFESTEAL] PlayerStats not found!");
            enabled = false;
        }
    }

    void Start()
    {
        weapon = GetComponentInChildren<Weapon>();
        if (weapon == null)
        {
            weapon = FindFirstObjectByType<Weapon>();
        }

        if (weapon == null)
        {
            Debug.LogError("[LIFESTEAL] Weapon not found!");
            enabled = false;
            return;
        }

        // Validate that percentage was set from CSV
        if (Mathf.Approximately(lifestealPercentage, 0f))
        {
            Debug.LogWarning("[LIFESTEAL] lifestealPercentage is 0! CSV value may not have been applied.");
        }

        // Subscribe to enemy kill events
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent += OnEnemyKilled;
        }
    }

    void OnDestroy()
    {
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent -= OnEnemyKilled;
        }
    }

    private void OnEnemyKilled(GameObject enemy)
    {
        if (playerStats == null || weapon == null) return;
        if (lifestealPercentage <= 0f) return; // Don't heal if percentage is 0

        var weaponData = weapon.GetWeaponData();
        if (weaponData == null) return;

        // Calculate heal amount based on weapon damage
        float healAmount = weaponData.damage * lifestealPercentage;
        // Heal the player
        if (healAmount > 0 && !playerStats.IsDead())
        {
            float healthBefore = playerStats.currentHealth;
            playerStats.Heal(healAmount);
            //Debug.Log($"[LIFESTEAL] Healed {healAmount:F1} HP ({lifestealPercentage * 100}% of {weaponData.damage:F1} damage). Health: {healthBefore:F1} -> {playerStats.currentHealth:F1}");
        }
    }
    // Public getter for UI
    public float GetLifestealPercentage() => lifestealPercentage;
}
