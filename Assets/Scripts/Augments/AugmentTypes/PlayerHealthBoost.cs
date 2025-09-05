using UnityEngine;

[CreateAssetMenu(menuName = "Augments/Player Health Boost")]
public class PlayerHealthBoost : AugmentEffect
{
    public float mult = 1.5f;

    public override void ApplyPlayer(PlayerStats playerStats)
    {
        playerStats.maxHealth *= mult;
        //Debug.Log(playerStats.maxHealth);
    }

    public override void ApplyEnemy(EnemyStats enemyStats) { }

}

