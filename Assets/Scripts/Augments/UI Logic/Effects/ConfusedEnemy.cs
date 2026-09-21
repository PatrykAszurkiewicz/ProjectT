using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ConfusedEnemy : MonoBehaviour
{
    private float duration;
    private float timer;

    private Rigidbody2D rb;
    private EnemyStats enemyStats;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private Coroutine confusionEffectCoroutine;

    // Exactly the behaviour components WE disabled, so EndConfusion / OnDestroy can
    // re-enable that set and nothing else. Replaces the old single `enemyController`
    // field, which only ever handled EnemyController-driven enemies.
    private List<Behaviour> suspendedControllers;

    /// True if confusion can meaningfully be applied to `enemy`. Callers (e.g.
    /// PheromoneControlEffect) should check this BEFORE adding the component, so a
    /// boss or a self-driving enemy isn't given a misleading tint while carrying on
    /// at full strength. Initialize() also self-destructs if it is ignored.
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

        rb = GetComponent<Rigidbody2D>();
        enemyStats = GetComponent<EnemyStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
            confusionEffectCoroutine = StartCoroutine(ConfusionVisualEffect());
        }

        // Silence EVERY self-driving controller, not just EnemyController. Enemies
        // like the Buffer / Parfumer / Bomber have no EnemyController and drive their
        // own Rigidbody2D from FixedUpdate, which used to overwrite the confused
        // velocity every physics step and make this augment a no-op on them.
        suspendedControllers = EnemyStats.SuspendBehaviourControllers(gameObject);
    }

    private IEnumerator ConfusionVisualEffect()
    {
        // Purple/pink pulsing effect for confusion
        Color confusionColor = new Color(1f, 0f, 1f, 1f); // Magenta
        float pulseSpeed = 3f;

        while (true)
        {
            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f;
            spriteRenderer.color = Color.Lerp(originalColor, confusionColor, t * 0.5f);
            yield return null;
        }
    }

    private void Update()
    {
        // The enemy can die mid-confusion. Stop steering a corpse and let
        // EndConfusion tear the effect down (Restore skips re-enabling controllers
        // on a dead enemy, so a death routine's disables are respected).
        if (enemyStats == null || enemyStats.IsDead())
        {
            EndConfusion();
            return;
        }

        timer += Time.deltaTime;

        if (timer >= duration)
        {
            EndConfusion();
            return;
        }

        // Move towards closest tower/core (ignoring player)
        UpdateMovement();
    }

    private void UpdateMovement()
    {
        if (rb == null || enemyStats == null) return;

        // Find closest tower or core
        GameObject core = GameObject.FindGameObjectWithTag("Core");
        GameObject[] towers = GameObject.FindGameObjectsWithTag("Tower");

        Transform closestTarget = null;
        float closestDist = Mathf.Infinity;

        // Check towers
        foreach (var tower in towers)
        {
            if (tower == null || !tower.activeInHierarchy) continue;

            var towerComponent = tower.GetComponent<Tower>();
            if (towerComponent != null && towerComponent.IsDestroyed()) continue;

            float dist = Vector2.Distance(transform.position, tower.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestTarget = tower.transform;
            }
        }

        // Check core
        if (core != null)
        {
            float coreDist = Vector2.Distance(transform.position, core.transform.position);
            if (coreDist < closestDist)
            {
                closestTarget = core.transform;
            }
        }

        // Move towards target (if any found)
        if (closestTarget != null)
        {
            Vector2 direction = (closestTarget.position - transform.position).normalized;
            rb.linearVelocity = direction * enemyStats.MoveSpeed;
        }
        else
        {
            // Stop if no valid targets (shouldn't happen often)
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void EndConfusion()
    {
        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        // Stop visual effect
        if (confusionEffectCoroutine != null)
            StopCoroutine(confusionEffectCoroutine);

        // Restore original color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        // Re-enable exactly the controllers we suspended.
        EnemyStats.RestoreBehaviourControllers(gameObject, suspendedControllers);

        //Debug.Log($"[CONFUSION] {gameObject.name} returned to normal behavior");

        Destroy(this);
    }

    private void OnDestroy()
    {
        if (confusionEffectCoroutine != null)
            StopCoroutine(confusionEffectCoroutine);

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        // Idempotent: Restore() clears the list, so the EndConfusion path above has
        // already emptied it and this does nothing. It only matters when the
        // component is destroyed without EndConfusion running (scene unload, the
        // enemy GameObject being torn down mid-effect).
        EnemyStats.RestoreBehaviourControllers(gameObject, suspendedControllers);

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
}


