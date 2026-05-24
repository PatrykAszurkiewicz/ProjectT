using UnityEngine;


// Scarecrow-specific EnemyStats. Hooks the death moment so we can:
//   Hide the health bar BEFORE the visual death effect plays (otherwise it
//     hangs in space during the VFX).
//   Spawn a subtle EnemyDeathVFX so the scarecrow doesn't just pop out
//     of existence on kill.

public class ScarecrowStats : EnemyStats
{
    [Header("Scarecrow Death VFX")]
    [Tooltip("Duration passed to EnemyDeathVFX.Trigger(). Values below 1.0 use " +
             "the small/subtle 'classic chunks' path; values 1.0+ trigger the " +
             "full boss-style sprite-shatter. 0.6s is a good 'minor enemy' feel.")]
    [SerializeField] private float deathVfxDuration = 0.6f;

    [Tooltip("If true, the scarecrow's health bar is destroyed BEFORE the death " +
             "VFX plays so the bar doesn't float above the disintegration. " +
             "Almost always desired.")]
    [SerializeField] private bool destroyHealthBarBeforeVfx = true;

    public override void Die()
    {
        // Hide the bar first so it doesn't hover above the death effect.
        // The default EnemyStats.PerformDeath() also destroys the bar, but
        // by then a few frames of the VFX have already played with the bar
        // still visible. Pulling it now is cleaner.
        if (destroyHealthBarBeforeVfx)
        {
            var bar = GetHealthBar();
            if (bar != null) Destroy(bar.gameObject);
        }

        // Fire the subtle death VFX
        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: deathVfxDuration,
            onComplete: null
        );

        TriggerNonVisualDeathSideEffects();
    }

    // Mirrors the non-visual parts of EnemyStats.PerformDeath()
    private void TriggerNonVisualDeathSideEffects()
    {
        if (canDropEnergy)
        {
            EnergyDropManager.TrySpawnEnemyDrop(
                transform.position,
                GameOrchestrator.Instance?.CurrentStageIndex ?? 0);
        }

        WaveSpawner waveSpawner = FindAnyObjectByType<WaveSpawner>();
        if (waveSpawner != null) waveSpawner.OnEnemyDeath();

        if (EnergyManager.Instance != null)
            EnergyManager.Instance.OnEnemyKilled(gameObject);
    }
}
