using UnityEngine;

public class EnemyStats : CharacterStats
{
    public EnemyData enemyData;
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
    public float Damage => enemyData?.damage ?? 0f;
    public float MoveSpeed => enemyData?.moveSpeed ?? 1f;

}
