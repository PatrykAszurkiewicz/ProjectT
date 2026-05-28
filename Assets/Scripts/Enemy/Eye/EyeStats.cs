using UnityEngine;

// Eye-specific EnemyStats.
//
// The VFX/health-bar behavior previously here moved into EnemyStats — set
// the base class's `deathVfxDuration` field on the Eye prefab to enable it.
// What remains here is genuinely Eye-specific:
//   1. FIXED energy drop — exactly one drop worth `fixedEnergyDrop` on death,
//      instead of the usual stage-driven probabilistic roll.
//   2. SKIP DEATH ANIMATION — the Eye disintegrates straight away rather
//      than playing its idle-collapse animation, because the animation and
//      the VFX otherwise fight each other visually.

public class EyeStats : EnemyStats
{
    [Header("Eye Drop")]
    [Tooltip("Exact energy value dropped on death — a single drop worth this much.")]
    [SerializeField] private int fixedEnergyDrop = 10;

    [Tooltip("If true, the Eye skips its death animation (idle frame collapse) " +
             "and disintegrates straight away. Looks cleaner because the death " +
             "animation and the VFX would otherwise overlap and fight each other. " +
             "Disable only if you've authored a proper death animation that you " +
             "want to play before the dust.")]
    [SerializeField] private bool skipDeathAnimation = true;

    public override void Die()
    {
        // VFX + health-bar removal now lives in the base class. Calling this
        // first ensures the bar is gone before any frames of the disintegration
        // play. No-op if the Eye prefab has deathVfxDuration left at 0.
        TryFireDeathVfx();

        // Reuse the standard physics-stop / death-animation pipeline.
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        transform.rotation = Quaternion.identity;

        // If a death animation is configured AND we're not skipping it,
        // play it then perform death. Otherwise die immediately so the
        // disintegration is the only thing on screen.
        var animController = GetComponent<EnemyAnimationController>();
        if (!skipDeathAnimation
            && animController != null
            && enemyData != null
            && enemyData.death.frameCount > 0)
        {
            animController.PlayDeathAnimation();
            StartCoroutine(DelayedEyeDeath());
            return;
        }

        // No animation — die immediately. The disintegration VFX is already
        // running on its own root and will outlive this GameObject.
        PerformEyeDeath();
    }

    private System.Collections.IEnumerator DelayedEyeDeath()
    {
        // Disable components so the eye can't move/attack while dying.
        var controller = GetComponent<EnemyController>();
        if (controller != null) controller.enabled = false;

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        var collider = GetComponent<Collider2D>();
        if (collider != null) collider.enabled = false;
        if (rb != null) rb.simulated = false;

        var eye = GetComponent<Eye>();
        if (eye != null) eye.enabled = false;

        float animDuration = enemyData.deathAnimationDuration;
        yield return new WaitForSeconds(animDuration);

        PerformEyeDeath();
    }

    private void PerformEyeDeath()
    {
        // Fixed drop — this is the Eye's real reason for overriding death.
        // Bypasses EnergyDropManager's probabilistic table in favour of a
        // single guaranteed drop worth fixedEnergyDrop.
        if (canDropEnergy)
        {
            EnergyDrop.CreateEnergyDrop(transform.position, fixedEnergyDrop);
        }

        // Health bar may have already been pulled by TryFireDeathVfx() — this
        // is defensive in case deathVfxDuration was left at 0 on the prefab.
        var bar = GetHealthBar();
        if (bar != null) Destroy(bar.gameObject);

        // Notify wave spawner so the wave can progress.
        WaveSpawner waveSpawner = FindAnyObjectByType<WaveSpawner>();
        if (waveSpawner != null) waveSpawner.OnEnemyDeath();

        // Notify EnergyManager — used for kill tracking / achievements.
        if (EnergyManager.Instance != null)
            EnergyManager.Instance.OnEnemyKilled(gameObject);

        // Destroy the GameObject (base CharacterStats.Die behaviour).
        Destroy(gameObject);
    }
}
