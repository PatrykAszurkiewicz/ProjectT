using UnityEngine;

public abstract class AugmentEffect : ScriptableObject
{
    [Header("Augment Info")]
    public int ID;
    public string augmentName; //maybe needed idk
    //public Sprite icon; // same maybe

    // Apply stuff
    public abstract void ApplyPlayer(PlayerStats playerStats);
    public abstract void ApplyEnemy(EnemyStats enemyStats);
    //public abstract void Apply();
}