using UnityEngine;

[CreateAssetMenu(fileName = "WeaponData", menuName = "Weapons/WeaponData")]
public class WeaponData : ScriptableObject
{
    [Header("Name & Visual")]
    public string weaponName;
    public Sprite sprite;
    [Header("Main stats")]
    public float damage;
    public float attackCooldown;
    public float armorBonus;
    [Header("Knockback")]
    public bool knockBack;
    public float knockBackForce;
    [Header("Ranged")]
    public bool isRanged;
    public GameObject projectilePrefab;
    public float projectileSpeed;
}
