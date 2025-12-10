using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class BerserkEnemy : MonoBehaviour
{
    private float duration;
    private float timer;

    private EnemyController enemyController;
    private EnemyStats enemyStats;
    private Rigidbody2D rb;

    [Header("Attack Settings")]
    private float attackRange = 1.8f;
    private float attackCooldown = 1f;
    private float attackTimer = 0f;

    private Transform currentTarget;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private Coroutine glowCoroutine;

    public void Initialize(float duration)
    {
        this.duration = duration;
        this.timer = 0f;

        enemyController = GetComponent<EnemyController>();
        enemyStats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        //Debug.Log($"[BERSERK] {gameObject.name} initialized - Duration: {duration}s");

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
            glowCoroutine = StartCoroutine(GlowEffect());
            //Debug.Log($"[BERSERK] {gameObject.name} started RED GLOW effect");
        }

        if (enemyController != null)
        {
            enemyController.enabled = false;
            //Debug.Log($"[BERSERK] {gameObject.name} disabled normal controller");
        }
    }

    private IEnumerator GlowEffect()
    {
        float glowSpeed = 2f; // How fast the glow pulses
        Color glowColor = new Color(2f, 0.3f, 0.3f, 1f); // Bright red with additive effect

        while (true)
        {
            // Pulse between original and glow color
            float t = (Mathf.Sin(Time.time * glowSpeed) + 1f) / 2f; // 0 to 1 sine wave
            spriteRenderer.color = Color.Lerp(originalColor, glowColor, t);
            yield return null;
        }
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= duration)
        {
            EndBerserk();
            return;
        }

        UpdateTarget();

        if (currentTarget != null)
        {
            float distance = Vector2.Distance(transform.position, currentTarget.position);

            if (distance <= attackRange)
            {
                if (rb != null)
                    rb.linearVelocity = Vector2.zero;

                attackTimer -= Time.deltaTime;

                if (attackTimer <= 0f)
                {
                    AttackTarget(currentTarget);
                    attackTimer = attackCooldown;
                }
            }
            else
            {
                MoveTowardTarget();
            }
        }
    }

    private void MoveTowardTarget()
    {
        if (currentTarget == null || rb == null || enemyStats == null) return;

        Vector2 direction = (currentTarget.position - transform.position).normalized;
        float moveSpeed = enemyStats.MoveSpeed;
        rb.linearVelocity = direction * moveSpeed;
    }

    private void UpdateTarget()
    {
        var otherEnemies = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None)
            .Where(e => e != null && !e.IsDead() && e.gameObject != gameObject)
            .ToList();

        if (otherEnemies.Count == 0)
        {
            currentTarget = null;
            return;
        }

        float closestDist = Mathf.Infinity;
        EnemyStats closestEnemy = null;

        foreach (var enemy in otherEnemies)
        {
            float dist = Vector2.Distance(transform.position, enemy.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestEnemy = enemy;
            }
        }

        currentTarget = closestEnemy?.transform;
    }

    private void AttackTarget(Transform target)
    {
        var stats = target.GetComponent<EnemyStats>();
        if (stats != null)
        {
            float damage = enemyStats?.Damage ?? 10f;
            damage *= 5f;

            stats.TakeDamage(damage);

            //Debug.Log($"[BERSERK] ★★★ {gameObject.name} HIT {target.name} for {damage} damage (HP: {stats.currentHealth:F0}/{stats.maxHealth})");
        }
    }

    private void EndBerserk()
    {
        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        // Stop glow effect
        if (glowCoroutine != null)
            StopCoroutine(glowCoroutine);

        // Restore original color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        if (enemyController != null)
        {
            enemyController.enabled = true;
        }

        //Debug.Log($"[BERSERK] {gameObject.name} returned to normal behavior");

        Destroy(this);
    }

    private void OnDestroy()
    {
        if (glowCoroutine != null)
            StopCoroutine(glowCoroutine);

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        if (enemyController != null && !enemyController.enabled)
        {
            enemyController.enabled = true;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
}