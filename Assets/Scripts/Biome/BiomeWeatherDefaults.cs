using UnityEngine;

// Per-biome default weather particle configuration. Used by BiomeManager to auto-toggle ParticleRain / ParticleSnow when switching biomes.

[System.Serializable]
public struct BiomeWeatherDefaults
{
    public bool rainEnabled;
    public bool snowEnabled;

    /// Returns weather defaults for each biome.
    public static BiomeWeatherDefaults ForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            case BiomeType.Snow:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = true };

            case BiomeType.Desert:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            case BiomeType.Wasteland:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            case BiomeType.Stones:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            case BiomeType.GrassCartoon:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            case BiomeType.Marsh:
                return new BiomeWeatherDefaults { rainEnabled = true, snowEnabled = false };

            case BiomeType.Night:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            // Corruption: no particle weather — the darkness is the weather.
            case BiomeType.Corruption:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            // PitchBlack: same — no weather particles, nothing would be visible anyway.
            case BiomeType.PitchBlack:
                return new BiomeWeatherDefaults { rainEnabled = false, snowEnabled = false };

            default:
                return ForBiome(BiomeType.Grass);
        }
    }
}

