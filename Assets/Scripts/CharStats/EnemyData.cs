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
    public string spriteFolderPath; // "Sprites/EnemySprites/Slime"
    public float animationSpeed = 0.1f;
    public AnimationFrameRange idle = new AnimationFrameRange(0, 14);
    public AnimationFrameRange attack = new AnimationFrameRange(14, 19);
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