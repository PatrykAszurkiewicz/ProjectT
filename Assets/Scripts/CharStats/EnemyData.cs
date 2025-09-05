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
    public float mass = 50f; // Default mass in kg
}
