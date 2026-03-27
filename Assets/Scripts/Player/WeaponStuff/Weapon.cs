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

    private GrapplingHookSystem grapplingSystem;
    private ObstacleDrawerSystem obstacleDrawerSystem;
    private FlamethrowerSystem flamethrowerSystem;
    private BombLauncherSystem bombLauncherSystem;
    private TrapLauncherSystem trapLauncherSystem;
    private TurretLauncherSystem turretLauncherSystem;

    // Persist flamethrower fuel across weapon swaps 
    private float savedFlamethrowerFuel = -1f;

    public WeaponData GetWeaponData() => weaponData;


    // Returns 0..1 fuel level for the flamethrower. Returns 1 if not a flamethrower.
    // Used by FlamethrowerFuelUI.

    public float GetFlamethrowerFuelNormalized()
    {
        if (flamethrowerSystem == null) return 1f;
        return flamethrowerSystem.FuelNormalized;
    }

    public void HotSwapWeapon(WeaponData newData)
    {
        if (newData == null) return;

        if (obstacleDrawerSystem != null) { obstacleDrawerSystem.Cleanup(); obstacleDrawerSystem = null; }
        if (grapplingSystem != null) { grapplingSystem.Cleanup(); grapplingSystem = null; }
        if (bombLauncherSystem != null) { bombLauncherSystem.Cleanup(); bombLauncherSystem = null; }
        if (trapLauncherSystem != null) { trapLauncherSystem.Cleanup(); trapLauncherSystem = null; }
        if (turretLauncherSystem != null) { turretLauncherSystem.Cleanup(); turretLauncherSystem = null; }

        // Save flamethrower fuel before destroying the system
        if (flamethrowerSystem != null)
        {
            savedFlamethrowerFuel = flamethrowerSystem.CurrentFuel;
            flamethrowerSystem.Cleanup();
            flamethrowerSystem = null;
        }

        if (weaponData != null && weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor -= weaponData.armorBonus;

        weaponData = newData.CreateRuntimeCopy();

        if (weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor += weaponData.armorBonus;

        var sr = visual.GetComponent<SpriteRenderer>();
        if (sr != null && weaponData.sprite != null)
            sr.sprite = weaponData.sprite;

        ResizeCollider();

        if (weaponData.isGrapplingHook) grapplingSystem = new GrapplingHookSystem(this, weaponData);
        if (weaponData.isObstacleDrawer) obstacleDrawerSystem = new ObstacleDrawerSystem(this, weaponData);
        if (weaponData.isFlamethrower)
        {
            flamethrowerSystem = new FlamethrowerSystem(this, weaponData);
            // Restore saved fuel if we had one (not first equip)
            if (savedFlamethrowerFuel >= 0f)
                flamethrowerSystem.SetFuel(savedFlamethrowerFuel);
        }
        if (weaponData.isBombLauncher)
            bombLauncherSystem = new BombLauncherSystem(this, weaponData);
        if (weaponData.isTrap)
            trapLauncherSystem = new TrapLauncherSystem(this, weaponData);
        if (weaponData.isTurret)
            turretLauncherSystem = new TurretLauncherSystem(this, weaponData);

        if (CursorManager.Instance != null)
        {
            if (weaponData.isGrapplingHook) CursorManager.Instance.SetCursor(CursorManager.CursorType.Hook);
            else if (weaponData.isObstacleDrawer) CursorManager.Instance.SetCursor(CursorManager.CursorType.ObstacleDrawer);
            else if (weaponData.isFlamethrower) CursorManager.Instance.SetCursor(CursorManager.CursorType.Flamethrower);
            else if (weaponData.isBombLauncher) CursorManager.Instance.SetCursor(CursorManager.CursorType.BombLauncher);
            else if (weaponData.isTrap) CursorManager.Instance.SetCursor(CursorManager.CursorType.Trap);
            else if (weaponData.isTurret) CursorManager.Instance.SetCursor(CursorManager.CursorType.Turret);
            else if (weaponData.armorBonus > 0f) CursorManager.Instance.SetCursor(CursorManager.CursorType.Shield);
            else if (weaponData.isRanged) CursorManager.Instance.SetCursor(CursorManager.CursorType.Ranged);
            else CursorManager.Instance.SetCursor(CursorManager.CursorType.Melee);
        }
    }

    private void Awake()
    {
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
            weaponData = sourceData.CreateRuntimeCopy();
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

        playerStats = GetComponentInParent<PlayerStats>();

        if (weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor += weaponData.armorBonus;

        var spriteRenderer = visual.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && weaponData.sprite != null)
            spriteRenderer.sprite = weaponData.sprite;

        ResizeCollider();

        if (weaponData.isGrapplingHook)
            grapplingSystem = new GrapplingHookSystem(this, weaponData);
        if (weaponData.isObstacleDrawer)
            obstacleDrawerSystem = new ObstacleDrawerSystem(this, weaponData);
        if (weaponData.isFlamethrower)
            flamethrowerSystem = new FlamethrowerSystem(this, weaponData);
        if (weaponData.isBombLauncher)
            bombLauncherSystem = new BombLauncherSystem(this, weaponData);
        if (weaponData.isTrap)
            trapLauncherSystem = new TrapLauncherSystem(this, weaponData);
        if (weaponData.isTurret)
            turretLauncherSystem = new TurretLauncherSystem(this, weaponData);
    }

    public void ResetToOriginalStats()
    {
        if (originalWeaponData != null)
            weaponData = originalWeaponData.CreateRuntimeCopy();
    }

    private void Update()
    {
        UpdateGrapplingSystem();
        UpdateObstacleDrawerSystem();
        UpdateFlamethrowerSystem();
        UpdateBombLauncherSystem();
        UpdateTrapSystem();
        UpdateTurretSystem();

        // Input buffer — only fire once, then clear
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
                ExecuteAttack();
            }
        }
    }

    private void UpdateGrapplingSystem()
    {
        if (weaponData?.isGrapplingHook != true || grapplingSystem == null) return;
        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;
        grapplingSystem.SetActive(!inPlacementMode);
        if (!inPlacementMode) grapplingSystem.Update();
    }

    private void UpdateObstacleDrawerSystem()
    {
        if (weaponData?.isObstacleDrawer != true || obstacleDrawerSystem == null) return;
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
        if (weaponData?.isBombLauncher != true || bombLauncherSystem == null) return;
        bombLauncherSystem.Update();
    }

    private void UpdateTrapSystem()
    {
        if (weaponData?.isTrap != true || trapLauncherSystem == null) return;
        trapLauncherSystem.Update();
    }

    private void UpdateTurretSystem()
    {
        if (weaponData?.isTurret != true || turretLauncherSystem == null) return;
        turretLauncherSystem.Update();
    }

    private void ResizeCollider()
    {
        if (attackCollider != null)
            attackCollider.transform.localScale = weaponData.size;
    }

    public void PerformAttack()
    {
        if (isOnCooldown)
        {
            // Buffer only if not already buffered 
            if (!attackBuffered)
            {
                attackBuffered = true;
                bufferTimer = BUFFER_WINDOW;
            }
            return;
        }
        ExecuteAttack();
    }

    private void ExecuteAttack()
    {
        if (weaponData.isObstacleDrawer)
        {
            if (obstacleDrawerSystem != null && !obstacleDrawerSystem.IsDrawing())
                obstacleDrawerSystem.StartDrawing();
        }
        else if (weaponData.isGrapplingHook)
        {
            if (grapplingSystem?.CanFire() == true)
            {
                grapplingSystem.FireHook();
                StartCoroutine(CooldownRoutine());
            }
        }
        else if (weaponData.isFlamethrower)
        {
            if (flamethrowerSystem != null && flamethrowerSystem.CanFire())
            {
                flamethrowerSystem.StartFiring();
            }
        }
        else if (weaponData.isBombLauncher)
        {
            if (bombLauncherSystem != null)
            {
                bombLauncherSystem.PlaceMine();
                StartCoroutine(CooldownRoutine());
            }
        }
        else if (weaponData.isTrap)
        {
            if (trapLauncherSystem != null)
            {
                trapLauncherSystem.PlaceTrap();
                StartCoroutine(CooldownRoutine());
            }
        }
        else if (weaponData.isTurret)
        {
            if (turretLauncherSystem != null)
            {
                turretLauncherSystem.PlaceTurret();
                StartCoroutine(CooldownRoutine());
            }
        }
        else if (weaponData.isRanged)
        {
            ShootProjectile();
            StartCoroutine(CooldownRoutine());
        }
        else
        {
            StartCoroutine(AttackRoutine());
            StartCoroutine(CooldownRoutine());
        }
    }

    public void StopAttack()
    {
        if (weaponData?.isObstacleDrawer == true && obstacleDrawerSystem != null)
        {
            if (obstacleDrawerSystem.IsDrawing())
            {
                obstacleDrawerSystem.StopDrawing();
                StartCoroutine(CooldownRoutine());
            }
        }

        if (weaponData?.isFlamethrower == true && flamethrowerSystem != null)
        {
            if (flamethrowerSystem.IsFiring)
            {
                flamethrowerSystem.StopFiring();
            }
        }
    }

    private IEnumerator CooldownRoutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(weaponData.attackCooldown);
        isOnCooldown = false;
    }

    private void ShootProjectile()
    {
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector2 direction = (Camera.main.ScreenToWorldPoint(mousePos) - transform.position).normalized;

        GameObject projectile = Instantiate(weaponData.projectilePrefab, transform.position, Quaternion.identity);
        var weaponProjectile = projectile.GetComponent<WeaponProjectile>();
        weaponProjectile?.Initialize(direction, weaponData.damage, weaponData.projectileSpeed, weaponData.knockBackForce);
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
                stats.TakeDamage(weaponData.damage);

            var vampireEffect = playerStats?.GetComponent<EnergyVampireTouchEffect>();
            if (vampireEffect != null)
                vampireEffect.DrainEnergy();

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
        Vector2 direction = (enemyPosition - playerStats.transform.position).normalized;
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
        if (obstacleDrawerSystem != null) obstacleDrawerSystem.Cleanup();
        if (flamethrowerSystem != null) flamethrowerSystem.Cleanup();
        if (bombLauncherSystem != null) bombLauncherSystem.Cleanup();
        if (trapLauncherSystem != null) trapLauncherSystem.Cleanup();
        if (turretLauncherSystem != null) turretLauncherSystem.Cleanup();
    }
}
