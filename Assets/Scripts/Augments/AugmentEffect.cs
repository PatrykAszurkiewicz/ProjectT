using UnityEngine;

public abstract class AugmentEffect : ScriptableObject
{
    public int ID; //take ID from CSV
    public string augmentName;
    public string description;
    public Sprite icon;

    // Apply methods
    public virtual void ApplyToPlayer(PlayerStats player) { }
    public virtual void ApplyToEnemy(EnemyStats enemy) { }
    public abstract void Apply();
    //public virtual void ApplyToGame(GameManager game) { }
}