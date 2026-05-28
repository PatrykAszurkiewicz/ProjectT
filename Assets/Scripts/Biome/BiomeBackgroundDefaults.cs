using UnityEngine;

/// <summary>
/// Per-biome default background configuration. Used by BiomeManager to auto-configure
/// background scaling when switching biomes. The inspector field still overrides at runtime.
/// </summary>
[System.Serializable]
public struct BiomeBackgroundDefaults
{
    [Tooltip("Scales the background ground texture. Larger = each tile covers more area.")]
    public float backgroundScale;

    /// Returns background defaults for each biome.
    public static BiomeBackgroundDefaults ForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass:
                return new BiomeBackgroundDefaults { backgroundScale = 2.2f };

            case BiomeType.Snow:
                return new BiomeBackgroundDefaults { backgroundScale = 2.2f };

            case BiomeType.Desert:
                return new BiomeBackgroundDefaults { backgroundScale = 2.2f };

            case BiomeType.Wasteland:
                return new BiomeBackgroundDefaults { backgroundScale = 2.2f };

            case BiomeType.Stones:
                return new BiomeBackgroundDefaults { backgroundScale = 2.2f };

            case BiomeType.GrassCartoon:
                return new BiomeBackgroundDefaults { backgroundScale = 1.0f };

            case BiomeType.Marsh:
                return new BiomeBackgroundDefaults { backgroundScale = 2.2f };

            case BiomeType.Night:
                return new BiomeBackgroundDefaults { backgroundScale = 1.0f };

            // Corruption uses the same GrassCartoon-derived background as Night 
            case BiomeType.Corruption:
                return new BiomeBackgroundDefaults { backgroundScale = 1.0f };

            // PitchBlack: same GrassCartoon-derived base
            case BiomeType.PitchBlack:
                return new BiomeBackgroundDefaults { backgroundScale = 1.0f };

            default:
                return ForBiome(BiomeType.Grass);
        }
    }
}

