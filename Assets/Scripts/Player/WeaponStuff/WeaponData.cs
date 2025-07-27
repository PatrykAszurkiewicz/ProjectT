using UnityEngine;

[CreateAssetMenu(fileName = "WeaponData", menuName = "Weapons/WeaponData")]
public class WeaponData : ScriptableObject
{
    public string weaponName;
    public float damage;
    public float attackDuration;
    public float armorBonus;
    public bool knockBack;
    public float knockBackForce;
    public Sprite sprite;

    public GameObject projectilePrefab;
    public float projectileSpeed;
    public bool isRanged;
}
