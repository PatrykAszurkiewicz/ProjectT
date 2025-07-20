using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Weapon : MonoBehaviour
{
    [SerializeField] private WeaponData weaponData;
    [SerializeField] private PolygonCollider2D attackCollider;
    [SerializeField] private GameObject visual;

    private List<EnemyStats> hitEnemies = new List<EnemyStats>();
    private PlayerStats playerStats;

    private void Awake()
    {
        playerStats = GetComponentInParent<PlayerStats>();

        // Bonus armor np. dla tarczy
        if (weaponData != null && weaponData.armorBonus > 0 && playerStats != null)
        {
            playerStats.currentArmor += weaponData.armorBonus;
        }

        // Ustaw sprite broni
        SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
        if (sr != null && weaponData.sprite != null)
        {
            sr.sprite = weaponData.sprite;
        }

        // Domyślnie collider off
        attackCollider.enabled = false;
    }

    public void PerformAttack()
    {
        StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        hitEnemies.Clear();
        attackCollider.enabled = true;

        yield return new WaitForSeconds(weaponData.attackDuration);

        attackCollider.enabled = false;

        foreach (EnemyStats enemy in hitEnemies)
        {
            if (enemy != null)
            {
                if (weaponData.damage > 0)
                {
                    enemy.TakeDamage(weaponData.damage);
                }

                if (weaponData.knocksBack)
                {
                    Vector2 dir = (enemy.transform.position - transform.position).normalized;
                    Rigidbody2D rb = enemy.GetComponent<Rigidbody2D>();
                    if (rb != null)
                        rb.AddForce(dir * weaponData.knockBackForce, ForceMode2D.Impulse);
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!attackCollider.enabled) return;

        if (other.CompareTag("Enemy"))
        {
            EnemyStats enemy = other.GetComponent<EnemyStats>();
            if (enemy != null && !hitEnemies.Contains(enemy))
            {
                hitEnemies.Add(enemy);
            }
        }
    }
}
