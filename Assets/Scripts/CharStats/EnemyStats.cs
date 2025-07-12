using UnityEngine;

public class EnemyStats : CharacterStats
{
    public GameObject healthBarPrefab; // Przypisz prefab w Inspectorze
    private EnemyHealthBar healthBar;

    public EnemyData enemyData;

    private void Start()
    {
        // Zak³adamy, ¿e Awake ju¿ ustawi³o zdrowie
        if (healthBarPrefab != null)
        {
            GameObject bar = Instantiate(healthBarPrefab);
            healthBar = bar.GetComponent<EnemyHealthBar>();
            healthBar.Initialize(transform, maxHealth);
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

        base.Die();
    }
    public float Damage => enemyData?.damage ?? 0f;
    public float MoveSpeed => enemyData?.moveSpeed ?? 1f;

}
