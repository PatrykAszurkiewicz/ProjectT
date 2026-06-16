using System.Collections.Generic;
using UnityEngine;

// PARRY UPGRADES  (Augments 330–333)  —  Phase 8: per-player
//   330  Longer Parry Stun    → ExtraStunSeconds        (parry_stun_bonus_seconds)
//   331  Powerful Parry        → PowerfulParry* fields    (parry_damage_bonus, parry_damage_duration)
//   332  Longer Parry Window   → ExtraParryFrames         (parry_window_frames)
//   333  Heal on Parry         → HealOnParry* fields       (parry_heal_percent)
//
//   In co-op each player only gets the parry upgrades THEY picked. Every value is
//   keyed by the PARRYING player's index. The parrying player is known at every
//   parry site: ShieldSystem owns its player (melee), and the projectile path
//   resolves the intercepting player (see ProjectileParry).
//
//   Single player is byte-identical: the back-compat accessors below read/clear
//   player 0, and player 0 is the only player.
//
//   Read sites: ParryStunEffect (ExtraStun + PowerfulParry, both melee & projectile),
//   EnemyController (ExtraParryFrames widens the window), ShieldSystem (HealOnParry).
public static class ParryUpgrades
{
    //  Base parry tuning (truly global — same for everyone) 
    public const float BaseStunNormal = 3f;   // seconds, normal enemies
    public const float BaseStunBoss = 2f;   // seconds, bosses
    public const float BaseDamageBonus = 0.30f; // +30% damage while a fresh parry's debuff is up

    // Per-player upgrade state. Absent index → Default (no upgrades).
    private struct State
    {
        public float ExtraStunSeconds;
        public bool PowerfulParryEnabled;
        public float PowerfulParryDamageBonus;
        public float PowerfulParryDuration;
        public int ExtraParryFrames;
        public bool HealOnParryEnabled;
        public float HealOnParryPercent;

        public static State Default => new State
        {
            ExtraStunSeconds = 0f,
            PowerfulParryEnabled = false,
            PowerfulParryDamageBonus = 0.30f,
            PowerfulParryDuration = 5f,
            ExtraParryFrames = 0,
            HealOnParryEnabled = false,
            HealOnParryPercent = 0.03f,
        };
    }

    private static readonly Dictionary<int, State> _byPlayer = new Dictionary<int, State>();

    private static State Get(int idx) => _byPlayer.TryGetValue(idx, out var s) ? s : State.Default;

    //  Per-player reads 
    public static float ExtraStunSecondsFor(int idx) => Get(idx).ExtraStunSeconds;
    public static bool PowerfulParryEnabledFor(int idx) => Get(idx).PowerfulParryEnabled;
    public static float PowerfulParryDamageBonusFor(int idx) => Get(idx).PowerfulParryDamageBonus;
    public static float PowerfulParryDurationFor(int idx) => Get(idx).PowerfulParryDuration;
    public static int ExtraParryFramesFor(int idx) => Get(idx).ExtraParryFrames;
    public static bool HealOnParryEnabledFor(int idx) => Get(idx).HealOnParryEnabled;
    public static float HealOnParryPercentFor(int idx) => Get(idx).HealOnParryPercent;

    //  Back-compat single-player reads (player 0) 
    // Kept so read sites not yet converted (e.g. the cosmetic ParryIndicator)
    // still compile and behave exactly as single player.
    public static float ExtraStunSeconds => ExtraStunSecondsFor(0);
    public static bool PowerfulParryEnabled => PowerfulParryEnabledFor(0);
    public static float PowerfulParryDamageBonus => PowerfulParryDamageBonusFor(0);
    public static float PowerfulParryDuration => PowerfulParryDurationFor(0);
    public static int ExtraParryFrames => ExtraParryFramesFor(0);
    public static bool HealOnParryEnabled => HealOnParryEnabledFor(0);
    public static float HealOnParryPercent => HealOnParryPercentFor(0);

    //  Per-player setters (called by AugmentEffectHandler with the chooser index) 
    public static void SetLongerParryStun(int idx, float seconds)
    {
        var s = Get(idx); s.ExtraStunSeconds = seconds; _byPlayer[idx] = s;
    }

    public static void SetPowerfulParry(int idx, float bonus, float duration)
    {
        var s = Get(idx);
        s.PowerfulParryEnabled = true;
        s.PowerfulParryDamageBonus = bonus;
        s.PowerfulParryDuration = duration;
        _byPlayer[idx] = s;
    }

    public static void SetLongerParryWindow(int idx, int frames)
    {
        var s = Get(idx); s.ExtraParryFrames = frames; _byPlayer[idx] = s;
    }

    public static void SetHealOnParry(int idx, float percent)
    {
        var s = Get(idx);
        s.HealOnParryEnabled = true;
        s.HealOnParryPercent = percent;
        _byPlayer[idx] = s;
    }

    // Resolve the damage bonus + how long it should last for a fresh parry, for
    // ONE player. Centralized so melee and projectile parry behave identically.
    public static void ResolveDamageDebuff(int idx, float fallbackBonus, float fallbackDuration,
                                           out float bonus, out float duration)
    {
        var s = Get(idx);
        if (s.PowerfulParryEnabled)
        {
            bonus = s.PowerfulParryDamageBonus;
            duration = s.PowerfulParryDuration;
        }
        else
        {
            bonus = fallbackBonus;
            duration = fallbackDuration;
        }
    }

    // Back-compat overload (player 0).
    public static void ResolveDamageDebuff(float fallbackBonus, float fallbackDuration,
                                           out float bonus, out float duration)
        => ResolveDamageDebuff(0, fallbackBonus, fallbackDuration, out bonus, out duration);

    // Clear ALL players' upgrades. Call when starting a fresh run.
    public static void ResetAll() => _byPlayer.Clear();

    // Cosmetic telegraph helper: the widest parry-window extension across all
    // players, so a shared enemy "!" indicator opens as early as the most-upgraded
    // player's window. Mechanics stay per-player (each shield calls IsInParryWindow
    // with its own index) — this only affects the visual hint. Single player: this
    // is just player 0's value, identical to before.
    public static int MaxExtraParryFrames()
    {
        int max = 0;
        foreach (var kv in _byPlayer)
            if (kv.Value.ExtraParryFrames > max) max = kv.Value.ExtraParryFrames;
        return max;
    }
}
