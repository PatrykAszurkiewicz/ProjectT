using UnityEngine;

// PARRY UPGRADES  (Augments 330–333)
//   330  Longer Parry Stun    → ExtraStunSeconds        (parry_stun_bonus_seconds)
//   331  Powerful Parry        → PowerfulParry* fields    (parry_damage_bonus, parry_damage_duration)
//   332  Longer Parry Window   → ExtraParryFrames         (parry_window_frames)
//   333  Heal on Parry         → HealOnParry* fields       (parry_heal_percent)
//   - ParryStunEffect : ExtraStunSeconds + the PowerfulParry fields (applies to
//                       BOTH melee and projectile parry, since every parry stun
//                       routes through ParryStunEffect.ApplyOrRefresh).
//   - EnemyController  : ExtraParryFrames (widens the melee parry window).
//   - ShieldSystem     : HealOnParry fields (heals on melee AND projectile parry).
public static class ParryUpgrades
{
    //  Base parry tuning (was hard-coded in ShieldSystem; centralized here) 
    public const float BaseStunNormal = 3f;   // seconds, normal enemies
    public const float BaseStunBoss = 2f;   // seconds, bosses
    public const float BaseDamageBonus = 0.30f; // +30% damage while a fresh parry's debuff is up

    //  330 — Longer Parry Stun 
    // Added on top of the base stun duration for every parried enemy.
    public static float ExtraStunSeconds = 0f;

    //  331 — Powerful Parry 
    // When enabled, the parry damage-debuff uses these values and lasts its own
    // duration, INDEPENDENT of the (shorter) stun freeze.
    public static bool PowerfulParryEnabled = false;
    public static float PowerfulParryDamageBonus = 0.30f; // +30%
    public static float PowerfulParryDuration = 5f;    // seconds

    //  332 — Longer Parry Window 
    // Extra frames added to the END of each enemy's melee parry window.
    public static int ExtraParryFrames = 0;

    //  333 — Heal on Parry 
    // Heals this fraction of max health on every successful parry.
    public static bool HealOnParryEnabled = false;
    public static float HealOnParryPercent = 0.03f; // 3%

    // Resolve the damage bonus + how long it should last for a fresh parry.
    // Centralized so melee and projectile parry behave identically.
    public static void ResolveDamageDebuff(float fallbackBonus, float fallbackDuration,
                                           out float bonus, out float duration)
    {
        if (PowerfulParryEnabled)
        {
            bonus = PowerfulParryDamageBonus;
            duration = PowerfulParryDuration;
        }
        else
        {
            bonus = fallbackBonus;
            duration = fallbackDuration;
        }
    }

    // Optional: call when starting a fresh run if you ever need to clear upgrades.
    public static void ResetAll()
    {
        ExtraStunSeconds = 0f;
        PowerfulParryEnabled = false;
        PowerfulParryDamageBonus = 0.30f;
        PowerfulParryDuration = 5f;
        ExtraParryFrames = 0;
        HealOnParryEnabled = false;
        HealOnParryPercent = 0.03f;
    }
}
