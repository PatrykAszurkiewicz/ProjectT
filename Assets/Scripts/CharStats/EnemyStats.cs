using UnityEngine;

public class EnemyStats : CharacterStats
{
    public GameObject healthBarPrefab;
    private EnemyHealthBar healthBar;

    public EnemyData enemyData;

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
    public override void TakeDamage(float amount)
    {
        base.TakeDamage(amount);

        if (healthBar != null)
            healthBar.UpdateHealth(currentHealth);
    }

    public override void Die()
    {
        if (healthBar != null)
            Destroy(healthBar.gameObject);

        WaveSpawner waveSpawner = FindObjectOfType<WaveSpawner>();
        if (waveSpawner != null)
            waveSpawner.OnEnemyDeath();

        base.Die();
    }
    public float Damage => enemyData?.damage ?? 0f;
    public float MoveSpeed => enemyData?.moveSpeed ?? 1f;

}
