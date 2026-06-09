using UnityEngine;


// Static helper to fire all combat-feel effects at once.

public static class CombatJuice
{
    // Tuning — adjust these to taste
    private const float MELEE_HITSTOP = 0.065f;
    private const float MELEE_SHAKE = 0.12f; //0.08f;
    private const float MELEE_SHAKE_DURATION = 0.2f;

    private const float RANGED_HITSTOP = 0.03f;
    private const float RANGED_SHAKE = 0.08f;
    private const float RANGED_SHAKE_DURATION = 0.1f;


    // Tuning for boss death freeze
    private const float BOSS_DEATH_HITSTOP = 0.25f;
    private const float BOSS_DEATH_SHAKE = 0.30f;
    private const float BOSS_DEATH_SHAKE_DURATION = 1.00f;



    // Call whenever the player deals damage to an enemy.
    public static void OnPlayerHitEnemy(GameObject enemy, bool isMelee)
    {
        //Debug.Log($"[CombatJuice] OnPlayerHitEnemy called. CameraShake.Instance = {CameraShake.Instance}");

        if (enemy == null) return;

        // 1. Hit flash on the enemy sprite
        var flash = enemy.GetComponent<HitFlash>();
        if (flash != null)
            //flash.Flash();
            flash.Flash(isMelee);

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


    /// Call when a boss dies. Lets you tune the death-freeze independently of normal hits.
    /// Pass a custom hitstopDuration to override the default (e.g. for stronger/weaker pause).
    public static void OnBossKilled(GameObject boss, float hitstopDuration = -1f)
    {
        if (boss == null) return;

        // Flash the boss one last time
        var flash = boss.GetComponent<HitFlash>();
        if (flash != null) flash.Flash(true);

        // Hitstop — tunable per call
        if (HitStop.Instance != null)
        {
            float dur = hitstopDuration < 0f ? BOSS_DEATH_HITSTOP : hitstopDuration;
            //HitStop.Instance.Freeze(dur);
            HitStop.Instance.Freeze(dur, ignoreCooldown: true);
        }

        // Bigger shake for the kill
        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(BOSS_DEATH_SHAKE, BOSS_DEATH_SHAKE_DURATION);
    }

    // Immediately cancels any active camera shake from BOTH shake systems
    // (CameraShake singleton and CombatFeelSystem's CameraShaker).
    // Call this when pausing, opening the augment menu, or starting a stage transition.
    public static void StopAllShake()
    {
        if (CameraShake.Instance != null)
            CameraShake.Instance.StopShake();

        //if (CombatFeelManager.Instance != null)
        //    CombatFeelManager.Instance.StopShake();
    }
}

