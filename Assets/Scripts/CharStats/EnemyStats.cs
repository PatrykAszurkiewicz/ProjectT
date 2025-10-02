using UnityEngine;

public class EnemyStats : CharacterStats
{
    [Header("Health Bar")]
    public GameObject healthBarPrefab;
    private EnemyHealthBar healthBar;

    [Header("Enemy Data")]
    public EnemyData enemyData;

    [Header("Energy Drop Settings")]
    [Range(0f, 1f)] public float energyDropChance = -1f; // -1 = use global setting
    public int energyDropValue = -1; // -1 = use global setting
    public bool canDropEnergy = true;

    private void Awake()
    {
        if (enemyData != null)
        {
            maxHealth = enemyData.maxHealth;
            currentHealth = maxHealth;

            maxArmor = enemyData.maxArmor;
            currentArmor = maxArmor;
        }
    }

    private void Start()
    {
        if (healthBarPrefab != null)
        {
            GameObject bar = Instantiate(healthBarPrefab);
            healthBar = bar.GetComponent<EnemyHealthBar>();

            if (healthBar != null)
            {
                healthBar.Initialize(transform, maxHealth);
            }
        }
    }

    public override void TakeDamage(float amount)
    {
        base.TakeDamage(amount);

        if (healthBar != null)
            healthBar.UpdateHealth(currentHealth);
    }

    public override void Die()
    {
        // Check if this enemy has a GremlinController and use its death logic
        var gremlinController = GetComponent<GremlinController>();
        if (gremlinController != null)
        {
            // Clean up health bar first
            if (healthBar != null)
                Destroy(healthBar.gameObject);

            // Let GremlinController handle the death 
            gremlinController.Die();
            return;
        }

        // Original logic for regular enemies
        if (canDropEnergy)
        {
            EnergyDropManager.TrySpawnEnergyDrop(transform.position, energyDropChance, energyDropValue);
        }

        // Clean up health bar
        if (healthBar != null)
            Destroy(healthBar.gameObject);

        // Notify wave spawner
        WaveSpawner waveSpawner = FindAnyObjectByType<WaveSpawner>();
        if (waveSpawner != null)
            waveSpawner.OnEnemyDeath();

        // Notify energy manager
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilled(gameObject);
        }

        base.Die();
    }

    public float Damage => enemyData?.damage ?? 0f;
    public float MoveSpeed => enemyData?.moveSpeed ?? 1f;
    public float Mass => enemyData?.mass ?? 50f;

    // Configure drops at runtime
    public void ConfigureEnergyDrop(float dropChance, int dropValue)
    {
        energyDropChance = Mathf.Clamp01(dropChance);
        energyDropValue = Mathf.Max(1, dropValue);
    }

    public void DisableEnergyDrops()
    {
        canDropEnergy = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!canDropEnergy) return;

        // Show drop settings
        float chance = energyDropChance >= 0 ? energyDropChance : (EnergyDropManager.Instance?.globalDropChance ?? 0.5f);
        int value = energyDropValue > 0 ? energyDropValue : (EnergyDropManager.Instance?.defaultEnergyValue ?? 10);

        UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, $"Drop: {(chance * 100f):F0}%");
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.3f, $"Energy: {value}");
    }
#endif
}