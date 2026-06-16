using System.Collections.Generic;
using UnityEngine;

//  CooldownModifier  (per-player)
//  Augment 328 ("Shorten cooldown") shortens cooldowns. In co-op each player
//  only gets the reduction THEY picked, so it's keyed by playerIndex now.
//  Single player is byte-identical: every call site that doesn't pass an index
//  resolves to player 0 via the back-compat overloads below, and player 0 is the
//  only player, so nothing changes.
public static class CooldownModifier
{
    // Per-player 0..1 factor every cooldown duration is multiplied by. Absent =
    // 1.0 (no reduction). We only ever SHORTEN, so values stay clamped to [0,1].
    private static readonly Dictionary<int, float> _byPlayer = new Dictionary<int, float>();

    /// This player's current multiplier (1 when they have no reduction).
    public static float MultiplierFor(int playerIndex)
        => _byPlayer.TryGetValue(playerIndex, out float m) ? m : 1f;

    /// Back-compat single-player read (player 0).
    public static float Multiplier => MultiplierFor(0);

    /// Multiply a cooldown duration by THIS player's modifier.
    /// Use this everywhere a cooldown is armed, passing the owning player's index.
    public static float Apply(float seconds, int playerIndex) => seconds * MultiplierFor(playerIndex);

    /// Back-compat: single-player / unspecified caller → player 0.
    public static float Apply(float seconds) => Apply(seconds, 0);

    /// Set one player's reduction to a flat percentage (idempotent — re-applying
    /// the same augment will NOT double-reduce). reductionPercent = 20 → x0.8.
    public static void SetReductionPercent(float reductionPercent, int playerIndex)
    {
        float factor = 1f - (reductionPercent / 100f);
        _byPlayer[playerIndex] = Mathf.Clamp01(factor);
    }

    /// Back-compat (player 0).
    public static void SetReductionPercent(float reductionPercent) => SetReductionPercent(reductionPercent, 0);

    /// Stack a reduction multiplicatively for one player (20% twice → 0.64).
    public static void StackReductionPercent(float reductionPercent, int playerIndex)
    {
        float factor = 1f - (reductionPercent / 100f);
        _byPlayer[playerIndex] = Mathf.Clamp01(MultiplierFor(playerIndex) * factor);
    }

    /// Back-compat (player 0).
    public static void StackReductionPercent(float reductionPercent) => StackReductionPercent(reductionPercent, 0);

    /// Reset ALL players to "no reduction". Call when a new run starts (a static
    /// survives scene reloads in a built player).
    public static void Reset() => _byPlayer.Clear();
}

