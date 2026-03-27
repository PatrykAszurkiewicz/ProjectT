using UnityEngine;

/// Base class for all boss enemies
public abstract class BaseBossStats : EnemyStats
{
    [Header("Boss Armor System")]
    public float maxArmor = 1000f;
    protected float bossArmor;
    protected bool armorDestroyed = false;

    public bool IsArmorDestroyed => armorDestroyed;
    public float CurrentArmor => bossArmor;

    protected BossHead spawnedHead;

    protected override void Awake()
    {
        base.Awake();
        bossArmor = maxArmor;
    }

    public override void TakeDamage(float amount)
    {
        //Debug.Log($"[BASE_BOSS] TakeDamage amount={amount}");
        if (!armorDestroyed && bossArmor > 0)
        {
            bossArmor -= amount;

            if (bossArmor <= 0)
            {
                float overflow = Mathf.Abs(bossArmor);
                bossArmor = 0;
                OnArmorDestroyed();

                if (overflow > 0)
                {
                    base.TakeDamage(overflow);
                }
            }

            CallStartDamageFlash();
        }
        else
        {
            base.TakeDamage(amount);
        }

        UpdateBossHealthBar();
    }

    // Called when armor is destroyed. Override for custom behavior.
    protected virtual void OnArmorDestroyed()
    {
        armorDestroyed = true;
        bossArmor = 0;
        //Debug.Log($"{enemyData?.enemyName ?? "Boss"} ARMOR DESTROYED!");
    }

    public virtual void DestroyArmor()
    {
        if (armorDestroyed) return;
        OnArmorDestroyed();
        UpdateBossHealthBar();
    }

    protected virtual void UpdateBossHealthBar()
    {
        if (HealthBar != null)
        {
            float totalCurrent = currentHealth + (armorDestroyed ? 0 : bossArmor);
            HealthBar.UpdateHealth(totalCurrent);
        }
    }

    public virtual void OnHeadDestroyed()
    {
        DestroyArmor();
        spawnedHead = null;
        // Validate armor state after head destruction
        Debug.Assert(armorDestroyed, $"{enemyData?.enemyName ?? "Boss"}: armorDestroyed should be true after head destroyed!");
        Debug.Assert(bossArmor <= 0f, $"{enemyData?.enemyName ?? "Boss"}: bossArmor should be 0 after head destroyed, but is {bossArmor}!");


    }
}
