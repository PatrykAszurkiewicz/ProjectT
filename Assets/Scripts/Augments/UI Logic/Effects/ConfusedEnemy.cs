using UnityEngine;
using System.Collections;

public class ConfusedEnemy : MonoBehaviour
{
    private float duration;
    private float timer;

    private EnemyController enemyController;
    private Rigidbody2D rb;
    private EnemyStats enemyStats;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private Coroutine confusionEffectCoroutine;

    public void Initialize(float duration)
    {
        this.duration = duration;
        this.timer = 0f;

        enemyController = GetComponent<EnemyController>();
        rb = GetComponent<Rigidbody2D>();
        enemyStats = GetComponent<EnemyStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
            confusionEffectCoroutine = StartCoroutine(ConfusionVisualEffect());
        }

        // Disable normal controller to override behavior
        if (enemyController != null)
        {
            enemyController.enabled = false;
        }
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

        // Re-enable normal controller
        if (enemyController != null)
        {
            enemyController.enabled = true;
        }

        //Debug.Log($"[CONFUSION] {gameObject.name} returned to normal behavior");

        Destroy(this);
    }

    private void OnDestroy()
    {
        if (confusionEffectCoroutine != null)
            StopCoroutine(confusionEffectCoroutine);

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
