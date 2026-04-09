using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Weapon : MonoBehaviour
{
    [SerializeField] public WeaponData originalWeaponData;
    [SerializeField] private WeaponData weaponData;
    [SerializeField] private PolygonCollider2D attackCollider;
    [SerializeField] private GameObject visual;

    private List<EnemyStats> hitEnemies = new List<EnemyStats>();
    private PlayerStats playerStats;
    private bool isOnCooldown = false;
    public WeaponData defaultWeapon;
    public float grapplingDamage = 0f;

    // Input buffering
    private bool attackBuffered = false;
    private float bufferTimer = 0f;
    private const float BUFFER_WINDOW = 0.15f;

    //  Dual-wield: tool slot (right-click) 
    private WeaponData toolData;
    private WeaponData originalToolData;
    private bool isToolOnCooldown = false;
    private bool toolAttackBuffered = false;
    private float toolBufferTimer = 0f;
    private bool isToolRightHeld = false;

    // Subsystems — weapon side
    private FlamethrowerSystem flamethrowerSystem;

    // Subsystems — tool side
    private GrapplingHookSystem grapplingSystem;
    private ObstacleDrawerSystem obstacleDrawerSystem;
    private BombLauncherSystem bombLauncherSystem;
    private TrapLauncherSystem trapLauncherSystem;
    private TurretLauncherSystem turretLauncherSystem;
    private DecoyLauncherSystem decoyLauncherSystem;
    private ShieldSystem shieldSystem;

    // Persist flamethrower fuel across weapon swaps
    private float savedFlamethrowerFuel = -1f;
    private float flamethrowerUnequipTime = -1f;  // Time.time when flamethrower was unequipped
    private WeaponData savedFlamethrowerData;      // Cached reference for regen rate lookup

    // Weapon swap cooldown — prevents attack spam by scrolling
    [Header("Swap Cooldown")]
    [SerializeField] private float swapCooldownDuration = 0.25f;
    private float swapCooldownTimer = 0f;
    private bool IsSwapOnCooldown => swapCooldownTimer > 0f;

    //  Public accessors 
    public WeaponData GetWeaponData() => weaponData;
    public WeaponData GetToolData() => toolData;
    public bool HasTool => toolData != null;
    public ShieldSystem GetShieldSystem() => shieldSystem;

    /// <summary>
    /// Returns true if the given WeaponData represents a shield tool
    /// (has armorBonus but isn't another tool type like grappling hook).
    /// Shield tools do NOT grant passive armor — their protection is
    /// directional and only active while the shield is raised.
    /// </summary>
    private static bool IsShieldTool(WeaponData data)
    {
        if (data == null) return false;
        return data.armorBonus > 0f
            && !data.isGrapplingHook && !data.isObstacleDrawer
            && !data.isBombLauncher && !data.isTrap
            && !data.isTurret && !data.isDecoy
            && !data.isFlamethrower && !data.isRanged;
    }

    public float GetFlamethrowerFuelNormalized()
    {
        if (flamethrowerSystem == null) return 1f;
        return flamethrowerSystem.FuelNormalized;
    }

    //  HOT-SWAP: Weapon (left-click slot)
    public void HotSwapWeapon(WeaponData newData)
    {
        if (newData == null) return;

        // If this is actually a tool, route to tool slot instead
        if (newData.IsTool)
        {
            HotSwapTool(newData);
            return;
        }

        // Ensure playerStats is set
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        // Clean up weapon-side subsystems
        if (flamethrowerSystem != null)
        {
            savedFlamethrowerFuel = flamethrowerSystem.CurrentFuel;
            flamethrowerUnequipTime = Time.time;
            savedFlamethrowerData = weaponData; // keep ref for regen rates
            flamethrowerSystem.Cleanup();
            flamethrowerSystem = null;
        }

        // Remove old armor bonus
        if (weaponData != null && weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor -= weaponData.armorBonus;

        weaponData = newData.CreateRuntimeCopy();

        if (weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor += weaponData.armorBonus;

        var sr = visual.GetComponent<SpriteRenderer>();
        if (sr != null && weaponData.sprite != null)
            sr.sprite = weaponData.sprite;

        ResizeCollider();

        if (weaponData.isFlamethrower)
        {
            flamethrowerSystem = new FlamethrowerSystem(this, weaponData);
            if (savedFlamethrowerFuel >= 0f)
            {
                float restoredFuel = CalculateBackgroundFuelRegen(savedFlamethrowerFuel);
                flamethrowerSystem.SetFuel(restoredFuel);
            }
            // Clear saved state now that we've applied it
            savedFlamethrowerFuel = -1f;
            flamethrowerUnequipTime = -1f;
            savedFlamethrowerData = null;
        }

        // Apply swap cooldown (cancel any existing attack cooldown too)
        ApplySwapCooldown();

        // Update cursor to reflect the weapon (left-hand determines cursor)
        UpdateWeaponCursor();
    }

    // Calculate how much fuel would have regenerated while the flamethrower was unequipped.
    private float CalculateBackgroundFuelRegen(float fuelAtUnequip)
    {
        if (flamethrowerUnequipTime < 0f) return fuelAtUnequip;

        // Use the saved flamethrower data for regen values, fall back to current weaponData
        WeaponData d = savedFlamethrowerData ?? weaponData;
        if (d == null) return fuelAtUnequip;

        float elapsed = Time.time - flamethrowerUnequipTime;

        // Respect the regen delay (same as FlamethrowerSystem does when not firing)
        float regenTime = elapsed - d.flameFuelRegenDelay;
        if (regenTime <= 0f) return fuelAtUnequip;

        float regened = fuelAtUnequip + d.flameFuelRegen * regenTime;
        return Mathf.Min(regened, d.flameFuelMax);
    }

    //  HOT-SWAP: Tool (right-click slot)
    public void HotSwapTool(WeaponData newData)
    {
        if (newData == null) return;

        // Ensure playerStats is set
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        // Clean up old tool subsystems
        CleanupToolSubsystems();

        // Remove old tool armor bonus (only for non-shield tools that grant passive armor)
        // Shield tools do NOT grant passive armor — their protection is directional + active only.
        if (toolData != null && toolData.armorBonus > 0 && !IsShieldTool(toolData) && playerStats != null)
            playerStats.currentArmor -= toolData.armorBonus;

        originalToolData = newData;
        toolData = newData.CreateRuntimeCopy();

        // NOTE: Shield armor bonus is NOT applied passively.
        // Protection is only active while the shield is raised (right-click held)
        // and only blocks attacks from the cursor direction.
        // The ShieldSystem handles blocking via TryBlockOrParry().

        // Initialize tool subsystems
        if (toolData.isGrapplingHook) grapplingSystem = new GrapplingHookSystem(this, toolData);
        if (toolData.isObstacleDrawer) obstacleDrawerSystem = new ObstacleDrawerSystem(this, toolData);
        if (toolData.isBombLauncher) bombLauncherSystem = new BombLauncherSystem(this, toolData);
        if (toolData.isTrap) trapLauncherSystem = new TrapLauncherSystem(this, toolData);
        if (toolData.isTurret) turretLauncherSystem = new TurretLauncherSystem(this, toolData);
        if (toolData.isDecoy) decoyLauncherSystem = new DecoyLauncherSystem(this, toolData);
        if (toolData.armorBonus > 0f) shieldSystem = new ShieldSystem(this, toolData);
    }

    private void CleanupToolSubsystems()
    {
        if (grapplingSystem != null) { grapplingSystem.Cleanup(); grapplingSystem = null; }
        if (obstacleDrawerSystem != null) { obstacleDrawerSystem.Cleanup(); obstacleDrawerSystem = null; }
        if (bombLauncherSystem != null) { bombLauncherSystem.Cleanup(); bombLauncherSystem = null; }
        if (trapLauncherSystem != null) { trapLauncherSystem.Cleanup(); trapLauncherSystem = null; }
        if (turretLauncherSystem != null) { turretLauncherSystem.Cleanup(); turretLauncherSystem = null; }
        if (decoyLauncherSystem != null) { decoyLauncherSystem.Cleanup(); decoyLauncherSystem = null; }
        if (shieldSystem != null) { shieldSystem.Cleanup(); shieldSystem = null; }
    }

    //  SWAP COOLDOWN
    private void ApplySwapCooldown()
    {
        swapCooldownTimer = swapCooldownDuration;
        isOnCooldown = true;
        attackBuffered = false;

        // Stop any running weapon cooldown coroutine — the swap cooldown replaces it
        StopAllCoroutines();
    }

    //  INITIALIZATION
    private void Awake()
    {
        playerStats = GetComponentInParent<PlayerStats>();
        CreateRuntimeWeaponData();
        InitializeWeaponData();
        SetupWeapon();
        attackCollider.enabled = false;
    }

    private void CreateRuntimeWeaponData()
    {
        WeaponData sourceData = null;
        if (WeaponSelectionManager.Instance != null)
            sourceData = WeaponSelectionManager.Instance.GetChosenWeapon();
        if (sourceData == null) sourceData = originalWeaponData;
        if (sourceData == null) sourceData = defaultWeapon;

        if (sourceData != null)
        {
            if (sourceData.IsTool)
            {
                // Use default/fallback for weapon slot, put this in tool slot
                WeaponData fallbackWeapon = defaultWeapon ?? originalWeaponData;
                if (fallbackWeapon != null)
                    weaponData = fallbackWeapon.CreateRuntimeCopy();
                originalToolData = sourceData;
                toolData = sourceData.CreateRuntimeCopy();
            }
            else
            {
                weaponData = sourceData.CreateRuntimeCopy();
            }
        }
        else
            Debug.LogError("No weapon data available for runtime copy!");
    }

    private void InitializeWeaponData()
    {
        if (weaponData == null)
            Debug.LogError("Runtime weapon data is null!");
    }

    private void SetupWeapon()
    {
        if (weaponData == null) return;

        // playerStats already set in Awake, but ensure it's there
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor += weaponData.armorBonus;

        var spriteRenderer = visual.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && weaponData.sprite != null)
            spriteRenderer.sprite = weaponData.sprite;

        ResizeCollider();

        // Weapon-side subsystems
        if (weaponData.isFlamethrower)
            flamethrowerSystem = new FlamethrowerSystem(this, weaponData);

        // Tool-side subsystems
        if (toolData != null)
        {
            // Shield tools do NOT grant passive armor — protection is directional + active only.
            if (toolData.armorBonus > 0 && !IsShieldTool(toolData) && playerStats != null)
                playerStats.currentArmor += toolData.armorBonus;

            if (toolData.isGrapplingHook) grapplingSystem = new GrapplingHookSystem(this, toolData);
            if (toolData.isObstacleDrawer) obstacleDrawerSystem = new ObstacleDrawerSystem(this, toolData);
            if (toolData.isBombLauncher) bombLauncherSystem = new BombLauncherSystem(this, toolData);
            if (toolData.isTrap) trapLauncherSystem = new TrapLauncherSystem(this, toolData);
            if (toolData.isTurret) turretLauncherSystem = new TurretLauncherSystem(this, toolData);
            if (toolData.isDecoy) decoyLauncherSystem = new DecoyLauncherSystem(this, toolData);
            if (toolData.armorBonus > 0f) shieldSystem = new ShieldSystem(this, toolData);
        }

        // Set initial cursor
        UpdateWeaponCursor();
    }

    public void ResetToOriginalStats()
    {
        if (originalWeaponData != null)
            weaponData = originalWeaponData.CreateRuntimeCopy();
    }

    //  UPDATE
    private void Update()
    {
        // Tick swap cooldown
        if (swapCooldownTimer > 0f)
        {
            swapCooldownTimer -= Time.deltaTime;
            if (swapCooldownTimer <= 0f)
            {
                swapCooldownTimer = 0f;
                isOnCooldown = false;
            }
        }

        // Weapon-side updates
        UpdateFlamethrowerSystem();

        // Tool-side updates
        UpdateGrapplingSystem();
        UpdateObstacleDrawerSystem();
        UpdateBombLauncherSystem();
        UpdateTrapSystem();
        UpdateTurretSystem();
        UpdateDecoySystem();
        UpdateShieldSystem();

        // Weapon input buffer
        if (attackBuffered)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0f)
            {
                attackBuffered = false;
            }
            else if (!isOnCooldown)
            {
                attackBuffered = false;
                ExecuteWeaponAttack();
            }
        }

        // Tool input buffer
        if (toolAttackBuffered)
        {
            toolBufferTimer -= Time.deltaTime;
            if (toolBufferTimer <= 0f)
            {
                toolAttackBuffered = false;
            }
            else if (!isToolOnCooldown)
            {
                toolAttackBuffered = false;
                ExecuteToolAttack();
            }
        }
    }

    private void UpdateGrapplingSystem()
    {
        if (toolData?.isGrapplingHook != true || grapplingSystem == null) return;
        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;
        grapplingSystem.SetActive(!inPlacementMode);
        if (!inPlacementMode) grapplingSystem.Update();
    }

    private void UpdateObstacleDrawerSystem()
    {
        if (toolData?.isObstacleDrawer != true || obstacleDrawerSystem == null) return;
        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;
        if (!inPlacementMode) obstacleDrawerSystem.Update();
    }

    private void UpdateFlamethrowerSystem()
    {
        if (weaponData?.isFlamethrower != true || flamethrowerSystem == null) return;
        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;
        if (!inPlacementMode) flamethrowerSystem.Update();
    }

    private void UpdateBombLauncherSystem()
    {
        if (toolData?.isBombLauncher != true || bombLauncherSystem == null) return;
        bombLauncherSystem.Update();
    }

    private void UpdateTrapSystem()
    {
        if (toolData?.isTrap != true || trapLauncherSystem == null) return;
        trapLauncherSystem.Update();
    }

    private void UpdateTurretSystem()
    {
        if (toolData?.isTurret != true || turretLauncherSystem == null) return;
        turretLauncherSystem.Update();
    }

    private void UpdateDecoySystem()
    {
        if (toolData?.isDecoy != true || decoyLauncherSystem == null) return;
        decoyLauncherSystem.Update();
    }

    private void UpdateShieldSystem()
    {
        if (shieldSystem == null) return;
        shieldSystem.Update();
    }

    // ── Shield raise/lower (called by PlayerAttack) ──
    public void RaiseShield()
    {
        if (shieldSystem != null)
            shieldSystem.RaiseShield();
    }

    public void LowerShield()
    {
        if (shieldSystem != null)
            shieldSystem.LowerShield();
    }

    private void ResizeCollider()
    {
        if (attackCollider != null && weaponData != null)
            attackCollider.transform.localScale = weaponData.size;
    }

    //  WEAPON ATTACK (Left-click)
    public void PerformAttack()
    {
        if (isOnCooldown)
        {
            if (!attackBuffered)
            {
                attackBuffered = true;
                bufferTimer = BUFFER_WINDOW;
            }
            return;
        }
        ExecuteWeaponAttack();
    }

    private void ExecuteWeaponAttack()
    {
        if (weaponData == null) return;

        if (weaponData.isFlamethrower)
        {
            if (flamethrowerSystem != null && flamethrowerSystem.CanFire())
                flamethrowerSystem.StartFiring();
        }
        else if (weaponData.isBoomerang)
        {
            ShootBoomerang();
            StartCoroutine(WeaponCooldownRoutine());
        }
        else if (weaponData.isRanged)
        {
            ShootProjectile();
            StartCoroutine(WeaponCooldownRoutine());
        }
        else
        {
            // Melee
            StartCoroutine(AttackRoutine());
            StartCoroutine(WeaponCooldownRoutine());
        }
    }

    public void StopAttack()
    {
        if (weaponData?.isFlamethrower == true && flamethrowerSystem != null)
        {
            if (flamethrowerSystem.IsFiring)
                flamethrowerSystem.StopFiring();
        }
    }

    //  TOOL ATTACK (Right-click)
    public void PerformToolAttack()
    {
        if (toolData == null) return;

        if (isToolOnCooldown)
        {
            if (!toolAttackBuffered)
            {
                toolAttackBuffered = true;
                toolBufferTimer = BUFFER_WINDOW;
            }
            return;
        }
        ExecuteToolAttack();
    }

    private void ExecuteToolAttack()
    {
        if (toolData == null) return;

        if (toolData.isObstacleDrawer)
        {
            if (obstacleDrawerSystem != null && !obstacleDrawerSystem.IsDrawing())
                obstacleDrawerSystem.StartDrawing();
        }
        else if (toolData.isGrapplingHook)
        {
            if (grapplingSystem?.CanFire() == true)
            {
                grapplingSystem.FireHook();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isBombLauncher)
        {
            if (bombLauncherSystem != null)
            {
                bombLauncherSystem.PlaceMine();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isTrap)
        {
            if (trapLauncherSystem != null)
            {
                trapLauncherSystem.PlaceTrap();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isTurret)
        {
            if (turretLauncherSystem != null)
            {
                turretLauncherSystem.PlaceTurret();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isDecoy)
        {
            if (decoyLauncherSystem != null)
            {
                decoyLauncherSystem.PlaceDecoy();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.armorBonus > 0f)
        {
            // Shield — raise handled via OnToolButtonPressed/Released in PlayerAttack
            // ExecuteToolAttack is a no-op for shield; blocking is passive while held.
        }
    }

    public void StopToolAttack()
    {
        if (toolData?.isObstacleDrawer == true && obstacleDrawerSystem != null)
        {
            if (obstacleDrawerSystem.IsDrawing())
            {
                obstacleDrawerSystem.StopDrawing();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
    }

    public void OnToolButtonPressed()
    {
        isToolRightHeld = true;
        UpdateToolCursor();
    }

    public void OnToolButtonReleased()
    {
        isToolRightHeld = false;
        UpdateWeaponCursor();
    }

    //  CURSOR MANAGEMENT
    private void UpdateWeaponCursor()
    {
        if (CursorManager.Instance == null || weaponData == null) return;

        if (weaponData.isFlamethrower)
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Flamethrower);
        else if (weaponData.isBoomerang)
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Boomerang);
        else if (weaponData.isRanged)
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Ranged);
        else
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Melee);
    }

    private void UpdateToolCursor()
    {
        if (CursorManager.Instance == null || toolData == null) return;

        if (toolData.isGrapplingHook) CursorManager.Instance.SetCursor(CursorManager.CursorType.Hook);
        else if (toolData.isObstacleDrawer) CursorManager.Instance.SetCursor(CursorManager.CursorType.ObstacleDrawer);
        else if (toolData.isBombLauncher) CursorManager.Instance.SetCursor(CursorManager.CursorType.BombLauncher);
        else if (toolData.isTrap) CursorManager.Instance.SetCursor(CursorManager.CursorType.Trap);
        else if (toolData.isTurret) CursorManager.Instance.SetCursor(CursorManager.CursorType.Turret);
        else if (toolData.isDecoy) CursorManager.Instance.SetCursor(CursorManager.CursorType.Decoy);
        else if (toolData.armorBonus > 0f) CursorManager.Instance.SetCursor(CursorManager.CursorType.Shield);
    }

    //  COOLDOWN ROUTINES
    private IEnumerator WeaponCooldownRoutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(weaponData.attackCooldown);
        isOnCooldown = false;
    }

    private IEnumerator ToolCooldownRoutine()
    {
        isToolOnCooldown = true;
        yield return new WaitForSeconds(toolData.attackCooldown);
        isToolOnCooldown = false;
    }

    //  PROJECTILE & MELEE
    private void ShootProjectile()
    {
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector2 direction = (Camera.main.ScreenToWorldPoint(mousePos) - transform.position).normalized;

        GameObject projectile = Instantiate(weaponData.projectilePrefab, transform.position, Quaternion.identity);
        var weaponProjectile = projectile.GetComponent<WeaponProjectile>();
        weaponProjectile?.Initialize(direction, weaponData.damage, weaponData.projectileSpeed, weaponData.knockBackForce);
    }

    //  BOOMERANG — creates the GO entirely from code, no prefab needed
    private void ShootBoomerang()
    {
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector2 direction = (Camera.main.ScreenToWorldPoint(mousePos) - transform.position).normalized;

        // Build the boomerang GameObject from scratch
        GameObject go = new GameObject("Boomerang");
        go.transform.position = transform.position;

        // Physics — trigger collider + kinematic body so OnTriggerEnter2D fires
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;
        col.isTrigger = true;

        // The boomerang script — handles movement, damage, VFX
        var bp = go.AddComponent<BoomerangProjectile>();

        // Player root for the return trip
        Transform playerRoot = playerStats != null ? playerStats.transform : (transform.parent ?? transform);

        bp.Initialize(
            playerRoot,
            direction,
            weaponData.damage,
            weaponData.projectileSpeed,
            weaponData.boomerangRange,
            weaponData.knockBackForce,
            weaponData.boomerangCurve
        );
    }

    private IEnumerator AttackRoutine()
    {
        hitEnemies.Clear();
        attackCollider.enabled = true;
        yield return new WaitForSeconds(weaponData.attackCooldown);
        attackCollider.enabled = false;
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!attackCollider.enabled || !other.CompareTag("Enemy")) return;

        var enemy = other.GetComponent<EnemyStats>();
        if (enemy == null || hitEnemies.Contains(enemy)) return;

        hitEnemies.Add(enemy);

        if (weaponData.damage > 0)
        {
            var stats = other.GetComponent<CharacterStats>();
            if (stats != null)
            {
                float damage = weaponData.damage;

                // Apply parry damage bonus if enemy is stunned
                var parryEffect = other.GetComponent<ParryStunEffect>();
                if (parryEffect != null)
                    damage *= parryEffect.DamageMultiplier;

                stats.TakeDamage(damage);
            }

            var vampireEffect = playerStats?.GetComponent<EnergyVampireTouchEffect>();
            if (vampireEffect != null)
                vampireEffect.DrainEnergy();

            // ── Combat Feel ──
            CombatFeel.OnHitEnemy(other.gameObject, isMelee: true);
        }

        if (weaponData.knockBack)
            ApplyKnockback(enemy);
    }

    private void ApplyKnockback(EnemyStats enemy)
    {
        Vector2 direction = GetKnockbackDirection(enemy.transform.position);
        var enemyController = enemy.GetComponent<EnemyController>();
        enemyController?.ApplyKnockback(direction, weaponData.knockBackForce);
    }

    private Vector2 GetKnockbackDirection(Vector3 enemyPosition)
    {
        Transform refTransform = playerStats != null ? playerStats.transform : transform;
        Vector2 direction = (enemyPosition - refTransform.position).normalized;
        if (direction.sqrMagnitude < 1e-4f)
        {
            direction = (enemyPosition - transform.position).normalized;
            if (direction.sqrMagnitude < 1e-4f)
                direction = Random.insideUnitCircle.normalized;
        }
        return direction;
    }

    void OnDestroy()
    {
        if (flamethrowerSystem != null) flamethrowerSystem.Cleanup();
        CleanupToolSubsystems();
    }
}
