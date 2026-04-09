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

    [Header("Obstacle Drawer Settings")]
    public bool isObstacleDrawer = false;
    [ConditionalField("isObstacleDrawer")] public float drawDuration = 0.3f;
    [ConditionalField("isObstacleDrawer")] public float obstacleWidth = 0.3f;
    [ConditionalField("isObstacleDrawer")] public int maxObstacles = 3;
    [ConditionalField("isObstacleDrawer")] public float minDrawDistance = 0.1f;
    [ConditionalField("isObstacleDrawer")] public Color drawLineColor = new Color(0.8f, 0.8f, 0.8f);
    [ConditionalField("isObstacleDrawer")] public Color solidifiedColor = new Color(0.2f, 0.2f, 0.2f);
    [ConditionalField("isObstacleDrawer")] public float obstacleHealth = 50f;

    [Header("Bomb Launcher Settings")]
    public bool isBombLauncher = false;
    [ConditionalField("isBombLauncher")] public int bombMaxMines = 5;
    [ConditionalField("isBombLauncher")] public float bombProximityRadius = 1.5f;
    [ConditionalField("isBombLauncher")] public float bombExplosionRadius = 3f;
    [ConditionalField("isBombLauncher")] public bool bombFriendlyFire = false;
    [ConditionalField("isBombLauncher")] public float bombArmDelay = 0.5f;

    [Header("Trap Settings")]
    public bool isTrap = false;
    [ConditionalField("isTrap")] public int trapMaxCount = 3;
    [ConditionalField("isTrap")] public float trapHoldDuration = 8f;
    [ConditionalField("isTrap")] public float trapBossHoldDuration = 3f;
    [ConditionalField("isTrap")] public float trapProximityRadius = 1.2f;
    [ConditionalField("isTrap")] public float trapArmDelay = 0.4f;

    [Header("Turret Settings")]
    public bool isTurret = false;
    [ConditionalField("isTurret")] public float turretRange = 5f;
    [ConditionalField("isTurret")] public float turretFireRate = 3f;
    [ConditionalField("isTurret")] public float turretProjectileSpeed = 12f;
    [ConditionalField("isTurret")] public float turretArmDelay = 0.4f;
    [ConditionalField("isTurret")] public float turretRotationSpeed = 300f;

    [Header("Flamethrower Settings")]
    public bool isFlamethrower = false;
    [ConditionalField("isFlamethrower")] public float flameRange = 4.5f;
    [ConditionalField("isFlamethrower")] public float flameConeAngle = 45f;
    [ConditionalField("isFlamethrower")] public float flameDamageInterval = 0.15f;
    [ConditionalField("isFlamethrower")] public float flameSpeed = 6f;
    [ConditionalField("isFlamethrower")] public float flameFuelMax = 100f;
    [ConditionalField("isFlamethrower")] public float flameFuelDrain = 25f;
    [ConditionalField("isFlamethrower")] public float flameFuelRegen = 15f;
    [ConditionalField("isFlamethrower")] public float flameFuelRegenDelay = 0.8f;
    [ConditionalField("isFlamethrower")] public float flameParticlesPerSecond = 50f;
    [ConditionalField("isFlamethrower")] public float flameParticleLifetimeMin = 0.25f;
    [ConditionalField("isFlamethrower")] public float flameParticleLifetimeMax = 0.55f;

    [Header("Decoy Settings")]
    public bool isDecoy = false;
    [ConditionalField("isDecoy")] public float decoyAttractRadius = 5f;
    [ConditionalField("isDecoy")] public float decoyDuration = 7f;
    [ConditionalField("isDecoy")] public float decoyBossDuration = 3f;
    [ConditionalField("isDecoy")] public float decoyArmDelay = 0.3f;
    [ConditionalField("isDecoy")] public Vector2 decoyBossVFXOffset = new Vector2(-1f, 2f);

    [Header("Boomerang Settings")]
    public bool isBoomerang = false;
    [ConditionalField("isBoomerang")] public float boomerangRange = 6f;
    [ConditionalField("isBoomerang")] public float boomerangCurve = 2.5f;


    // Returns true if this weapon data represents a tool (right-hand / right-click slot).
    // Tools: GrapplingHook, ObstacleDrawer, Shield (armorBonus > 0), BombLauncher, Trap, Turret, Decoy.
    // Weapons (left-hand / left-click): Melee, Ranged, Flamethrower, Boomerang.

    public bool IsTool
    {
        get
        {
            return isGrapplingHook || isObstacleDrawer || isBombLauncher
                || isTrap || isTurret || isDecoy || armorBonus > 0f;
        }
    }

    public WeaponData CreateRuntimeCopy()
    {
        WeaponData copy = ScriptableObject.CreateInstance<WeaponData>();

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

        copy.isObstacleDrawer = this.isObstacleDrawer;
        copy.drawDuration = this.drawDuration;
        copy.obstacleWidth = this.obstacleWidth;
        copy.maxObstacles = this.maxObstacles;
        copy.minDrawDistance = this.minDrawDistance;
        copy.drawLineColor = this.drawLineColor;
        copy.solidifiedColor = this.solidifiedColor;
        copy.obstacleHealth = this.obstacleHealth;

        copy.isBombLauncher = this.isBombLauncher;
        copy.bombMaxMines = this.bombMaxMines;
        copy.bombProximityRadius = this.bombProximityRadius;
        copy.bombExplosionRadius = this.bombExplosionRadius;
        copy.bombFriendlyFire = this.bombFriendlyFire;
        copy.bombArmDelay = this.bombArmDelay;

        copy.isTrap = this.isTrap;
        copy.trapMaxCount = this.trapMaxCount;
        copy.trapHoldDuration = this.trapHoldDuration;
        copy.trapBossHoldDuration = this.trapBossHoldDuration;
        copy.trapProximityRadius = this.trapProximityRadius;
        copy.trapArmDelay = this.trapArmDelay;

        copy.isTurret = this.isTurret;
        copy.turretRange = this.turretRange;
        copy.turretFireRate = this.turretFireRate;
        copy.turretProjectileSpeed = this.turretProjectileSpeed;
        copy.turretArmDelay = this.turretArmDelay;
        copy.turretRotationSpeed = this.turretRotationSpeed;

        copy.isFlamethrower = this.isFlamethrower;
        copy.flameRange = this.flameRange;
        copy.flameConeAngle = this.flameConeAngle;
        copy.flameDamageInterval = this.flameDamageInterval;
        copy.flameSpeed = this.flameSpeed;
        copy.flameFuelMax = this.flameFuelMax;
        copy.flameFuelDrain = this.flameFuelDrain;
        copy.flameFuelRegen = this.flameFuelRegen;
        copy.flameFuelRegenDelay = this.flameFuelRegenDelay;
        copy.flameParticlesPerSecond = this.flameParticlesPerSecond;
        copy.flameParticleLifetimeMin = this.flameParticleLifetimeMin;
        copy.flameParticleLifetimeMax = this.flameParticleLifetimeMax;

        copy.isDecoy = this.isDecoy;
        copy.decoyAttractRadius = this.decoyAttractRadius;
        copy.decoyDuration = this.decoyDuration;
        copy.decoyBossDuration = this.decoyBossDuration;
        copy.decoyArmDelay = this.decoyArmDelay;
        copy.decoyBossVFXOffset = this.decoyBossVFXOffset;

        copy.isBoomerang = this.isBoomerang;
        copy.boomerangRange = this.boomerangRange;
        copy.boomerangCurve = this.boomerangCurve;

        return copy;
    }

    void OnValidate()
    {
        if (isGrapplingHook)
        {
            isRanged = false; projectilePrefab = null;
            isObstacleDrawer = false; isFlamethrower = false;
            isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBoomerang = false;
        }
        if (isObstacleDrawer)
        {
            isRanged = false; isGrapplingHook = false; projectilePrefab = null;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBoomerang = false;
        }
        if (isFlamethrower)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false;
        }
        if (isBombLauncher)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isTrap = false; isTurret = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false;
        }
        if (isTrap)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTurret = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false;
        }
        if (isTurret)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false;
        }
        if (isDecoy)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; projectilePrefab = null;
            isBoomerang = false;
        }
        if (isBoomerang)
        {
            isRanged = true; // Boomerang uses ranged attack path but with custom projectile
            isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
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
