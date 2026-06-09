using UnityEngine;

//  CooldownModifier
//  Single source of truth for the global cooldown multiplier. Augment 328
//  ("Shorten cooldown") sets it
public static class CooldownModifier
{
    // 0..1 factor every cooldown duration is multiplied by. Clamped so it can
    // never go negative or above 1 (we only ever SHORTEN cooldowns here).
    public static float Multiplier { get; private set; } = 1f;

    /// Multiply a cooldown duration by the current modifier.
    /// Use this everywhere a cooldown is armed.
    public static float Apply(float seconds) => seconds * Multiplier;

    /// Set the reduction to a flat percentage (idempotent — re-applying the
    /// same augment will NOT double-reduce). reductionPercent = 20 → x0.8.
    public static void SetReductionPercent(float reductionPercent)
    {
        float factor = 1f - (reductionPercent / 100f);
        Multiplier = Mathf.Clamp01(factor);
    }

    /// Alternative API: stack a reduction multiplicatively (picking 20% twice
    /// would give 0.8 * 0.8 = 0.64). Not used by augment 328 by default — call
    /// this instead of SetReductionPercent if you want the augment to stack.
    public static void StackReductionPercent(float reductionPercent)
    {
        float factor = 1f - (reductionPercent / 100f);
        Multiplier = Mathf.Clamp01(Multiplier * factor);
    }

    /// Reset to "no reduction". Call this when a new run starts (wherever you
    /// clear applied augments), since a static survives scene reloads in a
    /// built player.
    public static void Reset() => Multiplier = 1f;
}
