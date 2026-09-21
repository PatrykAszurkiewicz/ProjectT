using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class BerserkEnemy : MonoBehaviour
{
    private float duration;
    private float timer;

    private EnemyStats enemyStats;
    private Rigidbody2D rb;

    // Exactly the behaviour components WE disabled — see the external-override
    // helpers on EnemyStats.
    private List<Behaviour> suspendedControllers;

    [Header("Attack Settings")]
    private float attackRange = 1.8f;
    private float attackCooldown = 1f;
    private float attackTimer = 0f;

    private Transform currentTarget;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private Coroutine glowCoroutine;

    /// True if berserk can meaningfully be applied to `enemy`. Callers should check
    /// this BEFORE adding the component; Initialize() self-destructs if they don't.
    public static bool CanAffect(GameObject enemy) => EnemyStats.CanBeExternallyControlled(enemy);

    public void Initialize(float duration)
    {
        // Refuse enemies we cannot actually steer (bosses, non-dynamic bodies,
        // already-dead enemies). Bail BEFORE touching the sprite colour so we leave
        // no trace at all. See EnemyStats.CanBeExternallyControlled for the rationale.
        if (!EnemyStats.CanBeExternallyControlled(gameObject))
        {
            Destroy(this);
            return;
        }

        this.duration = duration;
        this.timer = 0f;

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

        // Silence EVERY self-driving controller, not just EnemyController — see
        // ConfusedEnemy.Initialize for why the single-field version was insufficient.
        suspendedControllers = EnemyStats.SuspendBehaviourControllers(gameObject);
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
        // The berserker can die mid-effect. Stop steering a corpse and tear down.
        if (enemyStats == null || enemyStats.IsDead())
        {
            EndBerserk();
            return;
        }

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
        else
        {
            // NO TARGET -> STOP. Without this the body keeps whatever velocity
            // MoveTowardTarget last wrote, forever.
            //
            // This is not cosmetic. Several enemies (the Insect forces
            // RigidbodyType2D.Kinematic in its controller) run on a Kinematic body:
            // no drag, no collision response, so a leftover velocity carries them
            // straight off the map, through obstacles, still alive. They then keep
            // CountLivingEnemiesInScene() above zero and the wave never completes —
            // it looks exactly like "I killed everything and it froze".
            //
            // ConfusedEnemy has always zeroed velocity in this case; BerserkEnemy
            // never did. Targets can vanish at any moment (the berserker kills the
            // last other enemy), and excluding bosses from UpdateTarget makes the
            // no-target state more common, so this branch is required.
            if (rb != null) rb.linearVelocity = Vector2.zero;
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
            // Bosses are excluded. A berserked minion used to beeline for the boss and
            // chip its armour pool for free, which was never the intent of the augment
            // (it is about turning the horde on itself) and trivialised armour phases.
            .Where(e => e != null && !e.IsDead() && e.gameObject != gameObject
                        && !(e is BaseBossStats))
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

        // Re-enable exactly the controllers we suspended.
        EnemyStats.RestoreBehaviourControllers(gameObject, suspendedControllers);

        //Debug.Log($"[BERSERK] {gameObject.name} returned to normal behavior");

        Destroy(this);
    }

    private void OnDestroy()
    {
        if (glowCoroutine != null)
            StopCoroutine(glowCoroutine);

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        // Idempotent — see the matching comment in ConfusedEnemy.OnDestroy.
        EnemyStats.RestoreBehaviourControllers(gameObject, suspendedControllers);

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
}

