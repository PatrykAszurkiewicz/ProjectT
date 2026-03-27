using UnityEngine;


// Static helper to fire all combat-feel effects at once.

public static class CombatJuice
{
    // Tuning — adjust these to taste
    private const float MELEE_HITSTOP = 0.065f;
    private const float MELEE_SHAKE = 0.08f;
    private const float MELEE_SHAKE_DURATION = 0.1f;

    private const float RANGED_HITSTOP = 0.03f;
    private const float RANGED_SHAKE = 0.03f;
    private const float RANGED_SHAKE_DURATION = 0.06f;


    // Call whenever the player deals damage to an enemy.

    public static void OnPlayerHitEnemy(GameObject enemy, bool isMelee)
    {
        if (enemy == null) return;

        // 1. Hit flash on the enemy sprite
        var flash = enemy.GetComponent<HitFlash>();
        if (flash != null)
            flash.Flash();

        // 2. Hitstop (movement freeze, not timeScale)
        if (HitStop.Instance != null)
        {
            float dur = isMelee ? MELEE_HITSTOP : RANGED_HITSTOP;
            HitStop.Instance.Freeze(dur);
        }

        // 3. Camera shake
        if (CameraShake.Instance != null)
        {
            float intensity = isMelee ? MELEE_SHAKE : RANGED_SHAKE;
            float duration = isMelee ? MELEE_SHAKE_DURATION : RANGED_SHAKE_DURATION;
            CameraShake.Instance.Shake(intensity, duration);
        }
    }


    // Lighter version for enemy-on-player hits

    public static void OnEnemyHitPlayer()
    {
        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(0.06f, 0.08f);
    }


    // Heavy version for boss attacks, crits, etc.

    public static void OnHeavyHit(GameObject enemy)
    {
        if (enemy == null) return;

        var flash = enemy.GetComponent<HitFlash>();
        if (flash != null)
            flash.Flash();

        if (HitStop.Instance != null)
            HitStop.Instance.Freeze(0.12f);

        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(0.15f, 0.2f);
    }
}
