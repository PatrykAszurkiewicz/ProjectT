using UnityEngine;

public class EscalationEffect : MonoBehaviour
{
    [Header("Escalation Settings (Set from CSV)")]
    [System.NonSerialized]
    public float damageIncreasePerWave = 0.02f; // Set from CSV

    [Header("Debug Info")]
    [SerializeField] private int wavesCleared = 0;
    [SerializeField] private float currentDamageMultiplier = 1.0f;

    private WaveSpawner waveSpawner;
    private int lastWaveIndex = -1;

    void Awake()
    {
        waveSpawner = GetComponent<WaveSpawner>();
        if (waveSpawner == null)
        {
            Debug.LogError("[ESCALATION] WaveSpawner component not found!");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        //Debug.Log($"[ESCALATION] Initialized with {damageIncreasePerWave * 100f:F1}% damage increase per wave");
    }

    void Update()
    {
        int currentWaveIndex = waveSpawner.GetCurrentWaveIndex();

        if (currentWaveIndex > lastWaveIndex && lastWaveIndex >= 0)
        {
            OnWaveCompleted();
        }

        lastWaveIndex = currentWaveIndex;
    }

    private void OnWaveCompleted()
    {
        wavesCleared++;
        currentDamageMultiplier = 1.0f + (damageIncreasePerWave * wavesCleared);

        ApplyDamageBoost();

        //Debug.Log($"[ESCALATION] Wave {wavesCleared} completed! Total damage: {currentDamageMultiplier:F3}x");
    }

    private void ApplyDamageBoost()
    {
        ApplyPlayerDamageBoost();
        ApplyTowerDamageBoost();
    }

    private void ApplyPlayerDamageBoost()
    {
        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats == null) return;

        Weapon weapon = playerStats.GetComponentInChildren<Weapon>();
        if (weapon == null) return;

        weapon.ResetToOriginalStats();
        WeaponData weaponData = weapon.GetWeaponData();
        if (weaponData == null) return;

        float originalDamage = weaponData.damage;
        weaponData.damage = originalDamage * currentDamageMultiplier;
    }

    private void ApplyTowerDamageBoost()
    {
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);

        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsDestroyed()) continue;

            var boostComp = tower.GetComponent<EscalationDamageBoost>();
            if (boostComp == null)
            {
                boostComp = tower.gameObject.AddComponent<EscalationDamageBoost>();
            }

            boostComp.damageMultiplier = currentDamageMultiplier;
        }
    }
}

public class EscalationDamageBoost : MonoBehaviour
{
    public float damageMultiplier = 1.0f;

    public float GetDamageMultiplier()
    {
        return damageMultiplier;
    }
}
