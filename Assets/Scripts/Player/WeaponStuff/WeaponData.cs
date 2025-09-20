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

    [Header("Weapon Size Settings")]
    public Vector2 size = Vector2.one;

    [Header("Grappling Hook Settings")]
    public bool isGrapplingHook = false;
    [ConditionalField("isGrapplingHook")] public float hookRange = 12f;
    [ConditionalField("isGrapplingHook")] public float hookSpeed = 20f;
    [ConditionalField("isGrapplingHook")] public float pullForce = 15f;
    [ConditionalField("isGrapplingHook")] public float targetingAngle = 25f;
    [ConditionalField("isGrapplingHook")] public Color hookLineColor = Color.lightSteelBlue;
    [ConditionalField("isGrapplingHook")] public Color targetHighlightColor = Color.yellow;
    [ConditionalField("isGrapplingHook")] public float lineWidth = 0.08f;

    public WeaponData CreateRuntimeCopy()
    {
        WeaponData copy = ScriptableObject.CreateInstance<WeaponData>();

        // copy all variables
        copy.weaponName = this.weaponName;
        copy.sprite = this.sprite;
        copy.damage = this.damage;
        copy.attackCooldown = this.attackCooldown;
        copy.armorBonus = this.armorBonus;
        copy.knockBack = this.knockBack;
        copy.knockBackForce = this.knockBackForce;
        copy.isRanged = this.isRanged;
        copy.projectilePrefab = this.projectilePrefab;
        copy.projectileSpeed = this.projectileSpeed;
        copy.size = this.size;
        copy.isGrapplingHook = this.isGrapplingHook;
        copy.hookRange = this.hookRange;
        copy.hookSpeed = this.hookSpeed;
        copy.pullForce = this.pullForce;
        copy.targetingAngle = this.targetingAngle;
        copy.hookLineColor = this.hookLineColor;
        copy.targetHighlightColor = this.targetHighlightColor;
        copy.lineWidth = this.lineWidth;

        return copy;
    }
    void OnValidate()
    {
        if (isGrapplingHook)
        {
            isRanged = false;
            projectilePrefab = null;
        }
    }
}

public class ConditionalFieldAttribute : PropertyAttribute
{
    public string conditionalSourceField;
    public ConditionalFieldAttribute(string conditionalSourceField)
    {
        this.conditionalSourceField = conditionalSourceField;
    }
}