using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Weapon : MonoBehaviour
{
    [SerializeField] private WeaponData weaponData;
    [SerializeField] private PolygonCollider2D attackCollider;
    [SerializeField] private GameObject visual;

    private List<EnemyStats> hitEnemies = new List<EnemyStats>();
    private PlayerStats playerStats;

    private bool isOnCooldown = false;

    // Public method to access weapon data for animation system
    public WeaponData GetWeaponData()
    {
        return weaponData;
    }

    private void Awake()
    {
        // get choosen weapon
        if (weaponData == null && WeaponSelectionManager.Instance != null)
        {
            weaponData = WeaponSelectionManager.Instance.GetChosenWeapon();
        }

        if (weaponData != null)
        {
            playerStats = GetComponentInParent<PlayerStats>();

            if (weaponData.armorBonus > 0 && playerStats != null)
                playerStats.currentArmor += weaponData.armorBonus;

            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null && weaponData.sprite != null)
                sr.sprite = weaponData.sprite;

            ResizeCollider();
        }

        attackCollider.enabled = false;
    }
    private void ResizeCollider()
    {
        if (attackCollider == null) return;

        // Skalowanie razem z bronią
        attackCollider.transform.localScale = weaponData.size;
    }
    public void PerformAttack()
    {
        if (isOnCooldown) return;

        if (weaponData.isRanged)
            ShootProjectile();
        else
            StartCoroutine(AttackRoutine());

        StartCoroutine(CooldownRoutine());
    }

    private IEnumerator CooldownRoutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(weaponData.attackCooldown);
        isOnCooldown = false;
    }

    private void ShootProjectile()
    {
        Vector2 direction = (Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - transform.position).normalized;

        GameObject proj = Instantiate(weaponData.projectilePrefab, transform.position, Quaternion.identity);
        WeaponProjectile p = proj.GetComponent<WeaponProjectile>();

        if (p != null)
        {
            p.Initialize(direction, weaponData.damage, weaponData.projectileSpeed, weaponData.knockBackForce);
        }
    }

    private IEnumerator AttackRoutine()
    {
        hitEnemies.Clear();
        attackCollider.enabled = true;

        yield return new WaitForSeconds(weaponData.attackCooldown);

        attackCollider.enabled = false;

        Debug.Log($"Atak! Trafiono {hitEnemies.Count} przeciwników.");
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!attackCollider.enabled) return;

        if (other.CompareTag("Enemy"))
        {
            EnemyStats enemy = other.GetComponent<EnemyStats>();
            if (enemy != null && !hitEnemies.Contains(enemy))
            {
                hitEnemies.Add(enemy);

                // Apply Damage
                if (weaponData.damage > 0)
                {
                    enemy.TakeDamage(weaponData.damage);
                }

                //  Knockback
                if (weaponData.knockBack)
                {
                    Vector2 dir = (enemy.transform.position - playerStats.transform.position).normalized;

                    if (dir.sqrMagnitude < 1e-4f)
                    {
                        dir = (enemy.transform.position - transform.position).normalized;
                        if (dir.sqrMagnitude < 1e-4f)
                            dir = Random.insideUnitCircle.normalized;
                    }
                    EnemyController ec = enemy.GetComponent<EnemyController>();
                    if (ec != null)
                    {
                        ec.ApplyKnockback(dir, weaponData.knockBackForce);
                    }

                    EnemyController enemyController = enemy.GetComponent<EnemyController>();
                    if (enemyController != null)
                    {
                        enemyController.ApplyKnockback(dir, weaponData.knockBackForce);
                    }
                }
            }
        }
    }
}