using UnityEngine;

// Eye-specific EnemyStats. Differs from vanilla EnemyStats in two ways:
//   1. FIXED energy drop — exactly one drop worth 10 energy on death,
//   2. DISINTEGRATION death VFX — fires EnemyDeathVFX.Trigger() on death,
//      same pipeline the bosses and the Scarecrow use. Health bar is
//      pulled before the VFX so it doesn't float above the disintegration.

public class EyeStats : EnemyStats
{
    [Header("Eye Drop")]
    [Tooltip("Exact energy value dropped on death — a single drop worth this much.")]
    [SerializeField] private int fixedEnergyDrop = 10;

    [Header("Eye Death VFX")]
    [Tooltip("Duration passed to EnemyDeathVFX.Trigger() on death. " +
             "Values < 1.0 use the lighter 'classic chunks' disintegration; " +
             "values ≥ 1.0 trigger the full boss-style sprite shatter. " +
             "0.9 gives the Eye a beefier disintegration than a regular mob " +
             "without going full boss; bump to 1.2 if you want sprite shatter.")]
    [SerializeField] private float deathVfxDuration = 0.9f;

    [Tooltip("If true, the health bar is destroyed BEFORE the death VFX plays " +
             "so it doesn't float above the disintegration. Almost always desired.")]
    [SerializeField] private bool destroyHealthBarBeforeVfx = true;

    [Tooltip("If true, the Eye skips its death animation (idle frame collapse) " +
             "and disintegrates straight away. Looks cleaner because the death " +
             "animation and the VFX would otherwise overlap and fight each other. " +
             "Disable only if you've authored a proper death animation that you " +
             "want to play before the dust.")]
    [SerializeField] private bool skipDeathAnimation = true;

    // Guard so the VFX is only triggered once even if Die() somehow fires twice.
    private bool deathVfxFired = false;

    public override void Die()
    {
        // Fire the disintegration VFX FIRST 
        FireDeathVfx();

        // Reuse the standard death pipeline (stops physics, plays death
        // animation, then calls PerformDeath via DelayedDeath) 

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

    private void FireDeathVfx()
    {
        if (deathVfxFired) return;
        deathVfxFired = true;
        if (destroyHealthBarBeforeVfx)
        {
            var bar = GetHealthBar();
            if (bar != null) Destroy(bar.gameObject);
        }

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: deathVfxDuration,
            onComplete: null
        );
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

        if (canDropEnergy)
        {
            EnergyDrop.CreateEnergyDrop(transform.position, fixedEnergyDrop);
        }

        // Clean up health bar
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
