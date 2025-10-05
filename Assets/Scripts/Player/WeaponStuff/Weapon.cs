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

    // Grappling Hook System
    private GrapplingHookSystem grapplingSystem;

    // Obstacle Drawer System
    private ObstacleDrawerSystem obstacleDrawerSystem;

    public WeaponData GetWeaponData() => weaponData;

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

        if (sourceData == null)
            sourceData = originalWeaponData;

        if (sourceData == null)
            sourceData = defaultWeapon;

        if (sourceData != null)
        {
            // Runtime copy
            weaponData = sourceData.CreateRuntimeCopy();
            Debug.Log($"Created runtime copy of weapon: {weaponData.weaponName}");
        }
        else
        {
            Debug.LogError("No weapon data available for runtime copy!");
        }
    }

    private void InitializeWeaponData()
    {
        if (weaponData == null)
        {
            Debug.LogError("Runtime weapon data is null!");
        }
    }

    private void SetupWeapon()
    {
        if (weaponData == null) return;

        playerStats = GetComponentInParent<PlayerStats>();

        // Apply armor bonus
        if (weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor += weaponData.armorBonus;

        // Set visual sprite
        var spriteRenderer = visual.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && weaponData.sprite != null)
            spriteRenderer.sprite = weaponData.sprite;

        ResizeCollider();

        // Initialize grappling hook system
        if (weaponData.isGrapplingHook)
            grapplingSystem = new GrapplingHookSystem(this, weaponData);

        // Initialize obstacle drawer system
        if (weaponData.isObstacleDrawer)
            obstacleDrawerSystem = new ObstacleDrawerSystem(this, weaponData);
    }

    public void ResetToOriginalStats()
    {
        if (originalWeaponData != null)
        {
            // Stwórz nową kopię z oryginalnych danych
            weaponData = originalWeaponData.CreateRuntimeCopy();
            Debug.Log($"Reset weapon stats to original values: {weaponData.weaponName}");
        }
    }

    private void Update()
    {
        UpdateGrapplingSystem();
        UpdateObstacleDrawerSystem();
    }

    private void UpdateGrapplingSystem()
    {
        if (weaponData?.isGrapplingHook != true || grapplingSystem == null) return;

        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;

        grapplingSystem.SetActive(!inPlacementMode);

        if (!inPlacementMode)
            grapplingSystem.Update();
    }

    private void UpdateObstacleDrawerSystem()
    {
        if (weaponData?.isObstacleDrawer != true || obstacleDrawerSystem == null) return;

        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;

        if (!inPlacementMode)
            obstacleDrawerSystem.Update();
    }

    private void ResizeCollider()
    {
        if (attackCollider != null)
            attackCollider.transform.localScale = weaponData.size;
    }

    public void PerformAttack()
    {
        if (isOnCooldown) return;

        if (weaponData.isObstacleDrawer)
        {
            // Starting to draw
            if (obstacleDrawerSystem != null && !obstacleDrawerSystem.IsDrawing())
            {
                obstacleDrawerSystem.StartDrawing();
            }
        }
        else if (weaponData.isGrapplingHook)
        {
            // Grappling hooks only use the grappling system
            if (grapplingSystem?.CanFire() == true)
            {
                grapplingSystem.FireHook();
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

        // Deal damage
        if (weaponData.damage > 0)
        {
            enemy.TakeDamage(weaponData.damage);

            // Energy vampire effect (3 lines)
            var vampireEffect = playerStats?.GetComponent<EnergyVampireTouchEffect>();
            if (vampireEffect != null)
                vampireEffect.DrainEnergy();
        }

        // Apply knockback
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
        // Clean up obstacle drawer system
        if (obstacleDrawerSystem != null)
        {
            obstacleDrawerSystem.Cleanup();
        }
    }
}