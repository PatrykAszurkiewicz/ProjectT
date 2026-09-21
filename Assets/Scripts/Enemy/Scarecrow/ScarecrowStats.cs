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

        // Guaranteed teardown. This path never calls base.Die(), so nothing here ever
        // reaches CharacterStats.Die() -> Destroy(gameObject) — the EnemyDeathVFX above
        // is the only thing that removes the object. If that VFX fails, the corpse
        // stays in the scene and keeps counting as a living enemy in
        // GameOrchestrator.CountLivingEnemiesInScene(), which can stall a stage. Fires
        // well after the VFX should have completed, so a healthy death is unaffected.
        ScheduleDeathFailsafe(deathVfxDuration);
    }

    // Mirrors the non-visual parts of EnemyStats.PerformDeath()
    private void TriggerNonVisualDeathSideEffects()
    {
        if (canDropEnergy)
        {
            // Routed through EnemyDropAugments, exactly as EnemyStats.PerformDeath does.
            // This used to call EnergyDropManager.TrySpawnEnemyDrop directly — the
            // pre-augment API — so Lucky Strikes (337), Marksman's Bounty (341) and
            // Plunder (342) all silently skipped the Scarecrow. The Scarecrow is an
            // ordinary drop-bearing enemy, so it should honour them like every other.
            int stageIdx = GameOrchestrator.Instance?.CurrentStageIndex ?? 0;
            EnemyDropAugments.SpawnEnemyDrop(
                transform.position, stageIdx, gameObject, energyDropChance, energyDropValue);
        }

        // Shared death book-keeping: wave counter -> augment 335 tithe -> EnergyManager
        // kill event -> attribution cleanup. This path previously did only the wave
        // counter and the EnergyManager call, so a Scarecrow killed by a tower paid NO
        // tithe and leaked a TowerKillAttribution entry.
        EnemyStats.FireCommonDeathHooks(gameObject);
    }
}


