using UnityEngine;

/// <summary>
/// Per-biome default fog configuration. Used by BiomeManager to auto-configure
/// fog when switching biomes. The editor toggle still overrides these at runtime.
/// </summary>
[System.Serializable]
public struct BiomeFogDefaults
{
    public bool fogEnabled;
    public float fogDensity;
    public Color fogColor;
    public Color fogColorDeep;
    public Color smokeColor;
    public Color smokeDarkCore;


    /// Returns fog defaults for each biome.

    public static BiomeFogDefaults ForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass:
                return new BiomeFogDefaults
                {
                    fogEnabled = true,
                    fogDensity = 0.80f,
                    fogColor = new Color(0.78f, 0.82f, 0.85f, 1.0f),
                    fogColorDeep = new Color(0.58f, 0.63f, 0.70f, 1.0f),
                    smokeColor = new Color(0.75f, 0.80f, 0.85f, 1.0f),
                    smokeDarkCore = new Color(0.50f, 0.55f, 0.62f, 1.0f),
                };

            case BiomeType.Snow:
                return new BiomeFogDefaults
                {
                    fogEnabled = false,
                    fogDensity = 0.30f,
                    fogColor = new Color(0.88f, 0.90f, 0.95f, 1.0f),
                    fogColorDeep = new Color(0.72f, 0.76f, 0.85f, 1.0f),
                    smokeColor = new Color(0.85f, 0.88f, 0.94f, 1.0f),
                    smokeDarkCore = new Color(0.60f, 0.65f, 0.75f, 1.0f),
                };

            case BiomeType.Desert:
                return new BiomeFogDefaults
                {
                    fogEnabled = false,
                    fogDensity = 0.20f,
                    fogColor = new Color(0.90f, 0.82f, 0.65f, 1.0f),
                    fogColorDeep = new Color(0.75f, 0.65f, 0.45f, 1.0f),
                    smokeColor = new Color(0.88f, 0.80f, 0.62f, 1.0f),
                    smokeDarkCore = new Color(0.65f, 0.55f, 0.38f, 1.0f),
                };

            case BiomeType.Wasteland:
                return new BiomeFogDefaults
                {
                    fogEnabled = true,
                    fogDensity = 1.10f,
                    // Grey smoke
                    // fogColor = new Color(0.45f, 0.42f, 0.38f, 1.0f),
                    // fogColorDeep = new Color(0.30f, 0.28f, 0.25f, 1.0f),
                    //  smokeColor = new Color(0.35f, 0.32f, 0.28f, 1.0f),
                    //  smokeDarkCore = new Color(0.18f, 0.16f, 0.14f, 1.0f),

                    // More saturated purple smog
                    fogColor = new Color(0.55f, 0.32f, 0.60f, 1.0f),
                    fogColorDeep = new Color(0.38f, 0.18f, 0.45f, 1.0f),
                    smokeColor = new Color(0.48f, 0.22f, 0.55f, 1.0f),
                    smokeDarkCore = new Color(0.28f, 0.10f, 0.35f, 1.0f),

                };

            case BiomeType.Stones:
                return new BiomeFogDefaults
                {
                    fogEnabled = true,
                    fogDensity = 0.70f,
                    fogColor = new Color(0.55f, 0.62f, 0.52f, 1.0f),
                    fogColorDeep = new Color(0.38f, 0.46f, 0.36f, 1.0f),
                    smokeColor = new Color(0.42f, 0.58f, 0.38f, 1.0f),
                    smokeDarkCore = new Color(0.22f, 0.38f, 0.20f, 1.0f),
                };

            // GrassCartoon uses the same fog settings as Grass
            case BiomeType.GrassCartoon:
                return new BiomeFogDefaults
                {
                    fogEnabled = true,
                    fogDensity = 0.80f,
                    fogColor = new Color(0.78f, 0.82f, 0.85f, 1.0f),
                    fogColorDeep = new Color(0.58f, 0.63f, 0.70f, 1.0f),
                    smokeColor = new Color(0.75f, 0.80f, 0.85f, 1.0f),
                    smokeDarkCore = new Color(0.50f, 0.55f, 0.62f, 1.0f),
                };


            case BiomeType.Night:
                return new BiomeFogDefaults
                {
                    fogEnabled = false,
                    fogDensity = 0f,
                    fogColor = new Color(0.05f, 0.05f, 0.12f, 1f),
                    smokeColor = new Color(0.03f, 0.03f, 0.08f, 1f),
                    smokeDarkCore = new Color(0.01f, 0.01f, 0.04f, 1f)
                };

            case BiomeType.Marsh:
                return new BiomeFogDefaults
                {
                    fogEnabled = true,
                    fogDensity = 0.80f,
                    fogColor = new Color(0.62f, 0.72f, 0.65f, 1.0f),
                    fogColorDeep = new Color(0.40f, 0.52f, 0.45f, 1.0f),
                    smokeColor = new Color(0.55f, 0.65f, 0.58f, 1.0f),
                    smokeDarkCore = new Color(0.30f, 0.42f, 0.35f, 1.0f),
                };

            default:
                return ForBiome(BiomeType.Grass);
        }
    }
}
