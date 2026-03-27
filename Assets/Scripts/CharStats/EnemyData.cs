using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "Enemies/EnemyData")]
public class EnemyData : ScriptableObject
{
    public string enemyName;
    public float maxHealth;
    public float maxArmor;
    public float moveSpeed;
    public float damage;

    [Header("Grappling Physics")]
    [Tooltip("Mass of the enemy in kilograms. Affects grappling hook behavior.")]
    public float mass = 50f;

    [Header("Animation")]
    public string spriteFolderPath; // e.g. for Slime it is "Sprites/EnemySprites/Slime"
    public float animationSpeed = 0.1f;
    //TODO Verify whether it collides with enemy vs boss animation frames
    public AnimationFrameRange laserAttack = new AnimationFrameRange(57, 11);
    public AnimationFrameRange idle = new AnimationFrameRange(0, 14);
    public AnimationFrameRange attack = new AnimationFrameRange(14, 19);
    public AnimationFrameRange death = new AnimationFrameRange(33, 12);
    public float deathAnimationDuration = 1.2f; // How long before destroying the enemy
}

[System.Serializable]
public struct AnimationFrameRange
{
    public int startFrame;
    public int frameCount;

    public AnimationFrameRange(int start, int count)
    {
        startFrame = start;
        frameCount = count;
    }
}
