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

    [Header("Battle Hammer Settings")]
    // Slow, heavy melee weapon. The slow swing comes from a high attackCooldown.
    // On impact it performs a ground slam: AoE damage in a radius around the
    // player plus a fully-procedural dusty shockwave / crack / debris VFX
    // (no sprites required). Tuned to feel weighty and impactful.
    public bool isHammer = false;
    [ConditionalField("isHammer")] public float hammerSlamRadius = 2.8f;
    // Wind-up before the slam connects — this is what makes the hammer "feel"
    // slow and telegraphed, separate from attackCooldown (gates the next swing).
    [ConditionalField("isHammer")] public float hammerWindup = 0.42f;
    // AoE damage is dealt as a fraction of `damage` so the slam can hit harder
    // (or softer) than a single-target melee tick. 1 = same as `damage`.
    [ConditionalField("isHammer")] public float hammerAoEDamageMultiplier = 1f;
    // Enemies caught in the slam are knocked back from the impact point.
    [ConditionalField("isHammer")] public float hammerSlamKnockback = 12f;

    [Header("Battle Hammer — Feel")]
    // Freeze-frame on impact. Heavier than a normal melee hit for weight.
    [ConditionalField("isHammer")] public float hammerHitStop = 0.13f;
    // Two-stage camera shake: a sharp jolt then a lingering rumble.
    [ConditionalField("isHammer")] public float hammerShakeJolt = 0.42f;
    [ConditionalField("isHammer")] public float hammerShakeRumble = 0.16f;
    [ConditionalField("isHammer")] public float hammerShakeRumbleDuration = 0.55f;

    [Header("Battle Hammer — Visuals")]
    // Dusty warm impact tint and dark earth tone for cracks/debris.
    [ConditionalField("isHammer")] public Color hammerShockwaveColor = new Color(0.93f, 0.74f, 0.45f, 1f);
    [ConditionalField("isHammer")] public Color hammerCrackColor = new Color(0.16f, 0.12f, 0.09f, 1f);
    // Number of debris chunks flung up by the slam.
    [ConditionalField("isHammer")] public int hammerDebrisCount = 14;
    // Number of dust particles in the expanding ring cloud.
    [ConditionalField("isHammer")] public int hammerDustCount = 26;
    // Resources path of the semi-transparent "ghost" hammer sprite that plays
    // the swing animation (instead of rotating the cursor weapon). Place the
    // PNG under Assets/Resources/ and give the path relative to Resources
    // without the extension, e.g. "Sprites/BattleHammerSmall".
    [ConditionalField("isHammer")] public string hammerGhostSpritePath = "Icons/BattleHammerSmall";
    // World-space height of the ghost hammer sprite (it is scaled to this).
    [ConditionalField("isHammer")] public float hammerGhostSize = 2.4f;
    // Opacity of the ghost hammer sprite (0 = invisible, 1 = opaque).
    [ConditionalField("isHammer")][Range(0f, 1f)] public float hammerGhostAlpha = 0.5f;

    [Header("Battle Hammer — Charge Attack")]
    // Hold left-click to charge; release to slam. A full charge multiplies
    // damage, radius and screen-shake.
    [ConditionalField("isHammer")] public bool hammerChargeEnabled = true;
    // Seconds of holding to reach a full charge.
    [ConditionalField("isHammer")] public float hammerChargeTime = 0.9f;
    // Extra damage at full charge, as a fraction (0.3 = +30%). Damage scales
    // linearly from +0% (instant tap) to +this at full charge.
    [ConditionalField("isHammer")] public float hammerChargeBonus = 0.3f;
    // At full charge the slam radius is multiplied by this (1 = no growth).
    [ConditionalField("isHammer")] public float hammerChargeRadiusBonus = 1.25f;

    [Header("Battle Hammer — Directional Reach Swing")]
    // The slam lands this many world units AWAY from the player, in the
    // direction the player is aiming (toward the mouse cursor). Set to 0 to
    // slam centred on the player (legacy stomp behaviour).
    [ConditionalField("isHammer")] public float hammerReachDistance = 2.6f;
    // A charged swing reaches further — reach is multiplied up to this at full
    // charge (1 = reach never changes with charge).
    [ConditionalField("isHammer")] public float hammerChargeReachBonus = 1.3f;

    [Header("Revenant Necronomicon Settings")]
    public bool isBook = false;
    [ConditionalField("isBook")] public float bookAuraRadius = 5f;
    [ConditionalField("isBook")] public float bookAuraDuration = 8f;
    [ConditionalField("isBook")] public float bookArmDelay = 0.3f;
    [ConditionalField("isBook")] public int bookMaxShadows = 6;
    [ConditionalField("isBook")] public float bookShadowLifetime = 12f;
    [ConditionalField("isBook")] public float bookShadowDamage = 12f;
    [ConditionalField("isBook")] public float bookShadowAttackRange = 1.2f;
    [ConditionalField("isBook")] public float bookShadowAttackInterval = 0.8f;
    [ConditionalField("isBook")] public float bookShadowMoveSpeed = 3.5f;
    // Post-aura recharge: the book cannot be recast for this many seconds
    // AFTER its aura ends. Distinct from attackCooldown (the generic tool
    // re-fire delay) so the book always has a real cooldown gap. If left at 0
    // the system falls back to attackCooldown, then a 5s default.
    [ConditionalField("isBook")] public float bookCooldown = 6f;

    [Header("Stealth Cloak Settings")]
    // Right-click tool. Activating it makes the player invisible — enemies and
    // bosses ignore the player — for `cloakDuration` seconds, or until the
    // player damages an enemy/boss, whichever comes first. `cloakCooldown`
    // gates re-activation. The cloak deals no damage and has no projectile.
    public bool isCloak = false;
    [ConditionalField("isCloak")] public float cloakDuration = 30f;
    [ConditionalField("isCloak")] public float cloakCooldown = 10f;
    // Player sprite opacity while cloaked (0 = fully invisible, 1 = opaque).
    [ConditionalField("isCloak")][Range(0f, 1f)] public float cloakPlayerAlpha = 0.28f;


    // Returns true if this weapon data represents a tool (right-hand / right-click slot).
    // Tools: GrapplingHook, ObstacleDrawer, Shield (armorBonus > 0), BombLauncher, Trap, Turret, Decoy, Book, Cloak.
    // Weapons (left-hand / left-click): Melee, Ranged, Flamethrower, Boomerang, Battle Hammer.

    public bool IsTool
    {
        get
        {
            return isGrapplingHook || isObstacleDrawer || isBombLauncher
                || isTrap || isTurret || isDecoy || isBook || isCloak || armorBonus > 0f;
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

        copy.isHammer = this.isHammer;
        copy.hammerSlamRadius = this.hammerSlamRadius;
        copy.hammerWindup = this.hammerWindup;
        copy.hammerAoEDamageMultiplier = this.hammerAoEDamageMultiplier;
        copy.hammerSlamKnockback = this.hammerSlamKnockback;
        copy.hammerHitStop = this.hammerHitStop;
        copy.hammerShakeJolt = this.hammerShakeJolt;
        copy.hammerShakeRumble = this.hammerShakeRumble;
        copy.hammerShakeRumbleDuration = this.hammerShakeRumbleDuration;
        copy.hammerShockwaveColor = this.hammerShockwaveColor;
        copy.hammerCrackColor = this.hammerCrackColor;
        copy.hammerDebrisCount = this.hammerDebrisCount;
        copy.hammerDustCount = this.hammerDustCount;
        copy.hammerGhostSpritePath = this.hammerGhostSpritePath;
        copy.hammerGhostSize = this.hammerGhostSize;
        copy.hammerGhostAlpha = this.hammerGhostAlpha;
        copy.hammerChargeEnabled = this.hammerChargeEnabled;
        copy.hammerChargeTime = this.hammerChargeTime;
        copy.hammerChargeBonus = this.hammerChargeBonus;
        copy.hammerChargeRadiusBonus = this.hammerChargeRadiusBonus;
        copy.hammerReachDistance = this.hammerReachDistance;
        copy.hammerChargeReachBonus = this.hammerChargeReachBonus;

        copy.isBook = this.isBook;
        copy.bookAuraRadius = this.bookAuraRadius;
        copy.bookAuraDuration = this.bookAuraDuration;
        copy.bookArmDelay = this.bookArmDelay;
        copy.bookMaxShadows = this.bookMaxShadows;
        copy.bookShadowLifetime = this.bookShadowLifetime;
        copy.bookShadowDamage = this.bookShadowDamage;
        copy.bookShadowAttackRange = this.bookShadowAttackRange;
        copy.bookShadowAttackInterval = this.bookShadowAttackInterval;
        copy.bookShadowMoveSpeed = this.bookShadowMoveSpeed;
        copy.bookCooldown = this.bookCooldown;

        copy.isCloak = this.isCloak;
        copy.cloakDuration = this.cloakDuration;
        copy.cloakCooldown = this.cloakCooldown;
        copy.cloakPlayerAlpha = this.cloakPlayerAlpha;

        return copy;
    }

    void OnValidate()
    {
        if (isGrapplingHook)
        {
            isRanged = false; projectilePrefab = null;
            isObstacleDrawer = false; isFlamethrower = false;
            isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBoomerang = false; isBook = false; isHammer = false; isCloak = false;
        }
        if (isObstacleDrawer)
        {
            isRanged = false; isGrapplingHook = false; projectilePrefab = null;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBoomerang = false; isBook = false; isHammer = false; isCloak = false;
        }
        if (isFlamethrower)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false; isBook = false; isHammer = false; isCloak = false;
        }
        if (isBombLauncher)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isTrap = false; isTurret = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false; isBook = false; isHammer = false; isCloak = false;
        }
        if (isTrap)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTurret = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false; isBook = false; isHammer = false; isCloak = false;
        }
        if (isTurret)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isDecoy = false; projectilePrefab = null;
            isBoomerang = false; isBook = false; isHammer = false; isCloak = false;
        }
        if (isDecoy)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; projectilePrefab = null;
            isBoomerang = false; isBook = false; isHammer = false; isCloak = false;
        }
        if (isBoomerang)
        {
            isRanged = true; // Boomerang uses ranged attack path but with custom projectile
            isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBook = false; isHammer = false; isCloak = false;
        }
        if (isBook)
        {
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBoomerang = false; projectilePrefab = null; isHammer = false; isCloak = false;
        }
        if (isHammer)
        {
            // Battle Hammer is a melee-class weapon: it uses the melee attack path
            // (no projectile) and clears every other type flag so it stays a pure
            // weapon-slot entry.
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBoomerang = false; isBook = false; projectilePrefab = null; isCloak = false;
        }
        if (isCloak)
        {
            // Stealth Cloak is a pure utility tool: no projectile, no damage,
            // no other type flags. It clears everything else so it stays a
            // clean tool-slot entry.
            isRanged = false; isGrapplingHook = false; isObstacleDrawer = false;
            isFlamethrower = false; isBombLauncher = false; isTrap = false; isTurret = false; isDecoy = false;
            isBoomerang = false; isBook = false; isHammer = false; projectilePrefab = null;
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
