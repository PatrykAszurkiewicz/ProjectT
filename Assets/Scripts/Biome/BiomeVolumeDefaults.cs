using UnityEngine;


// Per-biome default Global Volume / illumination configuration.
// Original Global_Volume_Profile.asset baseline 
//   Bloom:    threshold=0.6, intensity=9, scatter=0, tint=(0.69, 0.97, 1.0)
//   Vignette: color=(0.60, 0.36, 0.75), intensity=0.34, smoothness=0.2, rounded=true

[System.Serializable]
public struct BiomeVolumeDefaults
{
    public bool volumeEnabled;
    public float volumeWeight;

    //  Bloom 
    public bool bloomEnabled;
    public float bloomIntensity;
    public float bloomThreshold;
    public float bloomScatter;
    public Color bloomTint;

    //  Vignette 
    public bool vignetteEnabled;
    public float vignetteIntensity;
    public float vignetteSmoothness;
    public bool vignetteRounded;
    public Color vignetteColor;

    //  Color Adjustments 
    public bool colorAdjustmentsEnabled;
    public float postExposure;
    public float contrast;
    public float saturation;
    public Color colorFilter;

    //  Global Light 2D 
    public bool globalLightOverride;
    public float globalLightIntensity;
    public Color globalLightColor;


    public static BiomeVolumeDefaults ForBiome(BiomeType biome)
    {
        switch (biome)
        {
            //  Grass
            case BiomeType.Grass:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = true,
                    bloomIntensity = 1.5f,
                    bloomThreshold = 0.9f,
                    bloomScatter = 0.4f,
                    bloomTint = new Color(0.80f, 0.95f, 1.0f, 1f),

                    vignetteEnabled = true,
                    vignetteIntensity = 0.25f,
                    vignetteSmoothness = 0.3f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.50f, 0.35f, 0.60f, 1f),

                    colorAdjustmentsEnabled = false,
                    postExposure = 0f,
                    contrast = 0f,
                    saturation = 0f,
                    colorFilter = Color.white,

                    globalLightOverride = false,
                    globalLightIntensity = 1f,
                    globalLightColor = Color.white,
                };

            //  Snow
            case BiomeType.Snow:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = true,
                    bloomIntensity = 0.6f,
                    bloomThreshold = 1.1f,
                    bloomScatter = 0.3f,
                    bloomTint = new Color(0.85f, 0.92f, 1.0f, 1f),

                    vignetteEnabled = true,
                    vignetteIntensity = 0.4f,
                    vignetteSmoothness = 0.35f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.45f, 0.50f, 0.65f, 1f),

                    colorAdjustmentsEnabled = true,
                    postExposure = 0f,
                    contrast = -3f,
                    saturation = -10f,
                    colorFilter = new Color(0.96f, 0.97f, 1.0f, 1f),

                    globalLightOverride = true,
                    globalLightIntensity = 1.05f,
                    globalLightColor = new Color(0.96f, 0.97f, 1.0f, 1f),
                };

            //  Desert
            case BiomeType.Desert:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = true,
                    bloomIntensity = 1.2f,
                    bloomThreshold = 0.95f,
                    bloomScatter = 0.35f,
                    bloomTint = new Color(1.0f, 0.93f, 0.80f, 1f),

                    vignetteEnabled = true,
                    vignetteIntensity = 0.32f,
                    vignetteSmoothness = 0.3f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.60f, 0.45f, 0.25f, 1f),

                    colorAdjustmentsEnabled = true,
                    postExposure = 0.2f,
                    contrast = 7f,
                    saturation = 3f,
                    colorFilter = new Color(1.0f, 0.97f, 0.92f, 1f),

                    globalLightOverride = true,
                    globalLightIntensity = 1.1f,
                    globalLightColor = new Color(1.0f, 0.97f, 0.93f, 1f),
                };

            // Wasteland
            case BiomeType.Wasteland:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = true,
                    bloomIntensity = 1.0f,
                    bloomThreshold = 0.85f,
                    bloomScatter = 0.3f,
                    bloomTint = new Color(0.75f, 0.55f, 0.85f, 1f),

                    vignetteEnabled = true,
                    vignetteIntensity = 0.32f,
                    vignetteSmoothness = 0.3f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.30f, 0.12f, 0.35f, 1f),

                    colorAdjustmentsEnabled = true,
                    postExposure = 0.53f,
                    contrast = 8f,
                    saturation = -8f,
                    colorFilter = new Color(0.94f, 0.88f, 0.94f, 1f),

                    globalLightOverride = true,
                    globalLightIntensity = 1.54f,
                    globalLightColor = new Color(0.90f, 0.85f, 0.92f, 1f),
                };

            // Stones 
            case BiomeType.Stones:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = true,
                    bloomIntensity = 0.8f,
                    bloomThreshold = 1.0f,
                    bloomScatter = 0.3f,
                    bloomTint = new Color(0.80f, 0.88f, 0.78f, 1f),

                    vignetteEnabled = true,
                    vignetteIntensity = 0.22f,
                    vignetteSmoothness = 0.3f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.28f, 0.35f, 0.25f, 1f),

                    colorAdjustmentsEnabled = true,
                    postExposure = -0.26f,
                    contrast = 62f,
                    saturation = 43f,
                    colorFilter = new Color(0.96f, 0.97f, 0.94f, 1f),

                    globalLightOverride = true,
                    globalLightIntensity = 0.92f,
                    globalLightColor = new Color(0.94f, 0.96f, 0.92f, 1f),
                };

            // GrassCartoon
            case BiomeType.GrassCartoon:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = true,
                    bloomIntensity = 1.2f,
                    bloomThreshold = 0.9f,
                    bloomScatter = 0.35f,
                    bloomTint = new Color(0.80f, 0.95f, 1.0f, 1f),

                    vignetteEnabled = true,
                    vignetteIntensity = 0.22f,
                    vignetteSmoothness = 0.3f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.45f, 0.30f, 0.55f, 1f),

                    colorAdjustmentsEnabled = true,
                    //postExposure = 0f,
                    postExposure = 0.93f,
                    //contrast = 3f,
                    contrast = 7f,
                    //saturation = 8f,
                    saturation = 19f,
                    colorFilter = Color.white,

                    globalLightOverride = false,
                    globalLightIntensity = 1f,
                    globalLightColor = Color.white,
                };

            //  Marsh
            case BiomeType.Marsh:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = true,
                    bloomIntensity = 0.8f,
                    bloomThreshold = 0.95f,
                    bloomScatter = 0.3f,
                    bloomTint = new Color(0.70f, 0.88f, 0.78f, 1f),

                    vignetteEnabled = true,
                    vignetteIntensity = 0.28f,
                    vignetteSmoothness = 0.3f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.18f, 0.30f, 0.20f, 1f),

                    colorAdjustmentsEnabled = true,
                    postExposure = -0.08f,
                    contrast = 2f,
                    saturation = -4f,
                    colorFilter = new Color(0.94f, 0.97f, 0.95f, 1f),

                    globalLightOverride = true,
                    globalLightIntensity = 0.88f,
                    globalLightColor = new Color(0.92f, 0.96f, 0.93f, 1f),
                };

            //  Night
            case BiomeType.Night:
                return new BiomeVolumeDefaults
                {
                    volumeEnabled = true,
                    volumeWeight = 1f,

                    bloomEnabled = false,
                    bloomIntensity = 2.0f,
                    bloomThreshold = 0.7f,
                    bloomScatter = 0.5f,
                    bloomTint = new Color(0.55f, 0.65f, 1.0f, 1f),

                    vignetteEnabled = false,
                    vignetteIntensity = 0.4f,
                    vignetteSmoothness = 0.35f,
                    vignetteRounded = true,
                    vignetteColor = new Color(0.03f, 0.03f, 0.10f, 1f),

                    colorAdjustmentsEnabled = true,
                    postExposure = 1.75f,
                    contrast = 65f,
                    saturation = -15f,
                    colorFilter = new Color(0.80f, 0.83f, 1.0f, 1f),

                    globalLightOverride = false,
                    globalLightIntensity = 0.4f,
                    globalLightColor = new Color(0.65f, 0.68f, 0.88f, 1f),
                };

            default:
                return ForBiome(BiomeType.Grass);
        }
    }
}
