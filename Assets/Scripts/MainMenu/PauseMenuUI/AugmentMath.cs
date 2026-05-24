using System;

/// <summary>
/// Computes the combined augment multiplier for a given stat / target.
///
/// This was previously buried inside StatsUI. It's pulled out into a static
/// helper so the panel UI and the tooltip (StatWithAugmentInfo) both use the
/// exact same logic — no drift between what the column shows and what the
/// hover tooltip explains.
///
/// Only Multiply / Percentage modifications fold into a multiplier. Additive
/// modifications can't be expressed as a multiplier without the base value,
/// so they're ignored here (the detailed panel shows raw values anyway).
/// </summary>
public static class AugmentMath
{
    /// <summary>
    /// Returns the product of every applied augment modification that targets
    /// (statName, targetType). Returns 1.0 when nothing matches.
    /// </summary>
    public static float Multiplier(string statName, string targetType)
    {
        if (AugmentRegistry.Instance == null) return 1f;

        float total = 1f;
        var applied = AugmentRegistry.Instance.GetAppliedAugments();

        foreach (int id in applied)
        {
            var data = AugmentRegistry.Instance.GetAugmentData(id);
            if (data?.ParsedModifications == null) continue;

            foreach (var mod in data.ParsedModifications)
            {
                if (!StatMatches(mod.StatName, statName)) continue;
                if (!string.Equals(mod.TargetType, targetType, StringComparison.OrdinalIgnoreCase)) continue;

                switch (mod.OperationType)
                {
                    case StatModification.ModificationType.Multiply:
                        total *= mod.Value;
                        break;
                    case StatModification.ModificationType.Percentage:
                        total *= (1f + mod.Value / 100f);
                        break;
                    // Add: skipped — needs the base value to become a multiplier.
                }
            }
        }

        return total;
    }

    /// <summary>
    /// Case-insensitive stat-name match with a few friendly aliases
    /// (health/maxHealth, speed/moveSpeed).
    /// </summary>
    public static bool StatMatches(string modStatName, string targetStatName)
    {
        if (string.IsNullOrEmpty(modStatName) || string.IsNullOrEmpty(targetStatName))
            return false;

        if (modStatName.Equals(targetStatName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (targetStatName.Equals("maxHealth", StringComparison.OrdinalIgnoreCase) &&
            modStatName.Equals("health", StringComparison.OrdinalIgnoreCase))
            return true;

        if (targetStatName.Equals("moveSpeed", StringComparison.OrdinalIgnoreCase) &&
            modStatName.Equals("speed", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
