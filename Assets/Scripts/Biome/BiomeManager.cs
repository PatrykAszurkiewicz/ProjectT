using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


public class BiomeManager : MonoBehaviour
{

    [Tooltip("Scales the background ground texture. Larger = each tile covers more area (ground looks zoomed out). Smaller = tiles are smaller (ground looks zoomed in). Does NOT affect camera or game objects.")]
    public float backgroundScale = 2.2f;

    [Tooltip("How far the ground tiles extend from the center (in world units). Increase if you see tile edges when zoomed out.")]
    public float groundCoverageRadius = 80f;

    [Header("Active Biome")]
    public BiomeType activeBiome = BiomeType.Grass;

    [Header("Universal Night Mode")]
    [Tooltip("Enable night overlay on ANY biome — adds darkness, torch, and player glow on top of the current biome.")]
    public bool enableNightMode = false;

    [Tooltip("When true, switching night mode on/off at runtime will re-apply the biome with/without the night overlay.")]
    private bool lastEnableNightMode = false;

    [Header("Night Balloons")]
    [Tooltip("Spawn procedural medieval hot-air balloons with lantern light sources that drift over the map. " +
             "Only active while Night Mode is on (or the Night biome is active).")]
    public bool enableNightBalloons = false;

    [Tooltip("How many balloons can be in the sky at once.")]
    [Range(1, 10)]
    public int nightBalloonMaxCount = 3;

    [Tooltip("Average seconds between balloon spawn attempts (actual interval is randomized ±50%).")]
    public float nightBalloonSpawnInterval = 8f;

    [Tooltip("Balloons enter the map from this radius outward.")]
    public float nightBalloonSpawnRadius = 35f;

    [Tooltip("Balloons will fly past the central core, offset by between min and max distance from center. " +
             "Setting both low = balloons fly close to the core. Setting both high = balloons stay on the outskirts.")]
    public float nightBalloonMinCoreDistance = 3f;

    public float nightBalloonMaxCoreDistance = 12f;

    [Tooltip("Drift speed (world units per second).")]
    public float nightBalloonFlightSpeed = 4f;

    [Tooltip("Visual scale. Player sprite is ~1 world unit; 1.0 = roughly player-sized.")]
    public float nightBalloonScale = 1.0f;

    [Tooltip("Radius of the lantern light cone reaching the ground below.")]
    public float nightBalloonLightRadius = 4f;

    [Tooltip("Brightness of each balloon's lantern (higher = brighter).")]
    [Range(0f, 2f)]
    public float nightBalloonLightIntensity = 1.0f;

    [Tooltip("Warm lantern color tint.")]
    public Color nightBalloonLightColor = new Color(1.0f, 0.92f, 0.75f, 1f);

    [Tooltip("How strongly the lantern color tints the night darkness around it.")]
    [Range(0f, 1f)]
    public float nightBalloonWarmTintStrength = 0.35f;

    [Tooltip("Flicker speed of the lantern (0 = steady, higher = more rapid flicker).")]
    public float nightBalloonFlickerSpeed = 1.2f;

    [Tooltip("How strongly the light radius wavers due to flicker.")]
    [Range(0f, 0.5f)]
    public float nightBalloonFlickerAmount = 0.08f;

    [Tooltip("When ON, balloons cast a sweeping directional searchlight beam with a cone + ground spot. " +
             "When OFF, balloons emit only a soft radial lantern glow (original behaviour).")]
    public bool nightBalloonEnableSweep = true;

    [Tooltip("Length of the searchlight beam from balloon to ground (world units).")]
    public float nightBalloonSweepBeamLength = 3.5f;

    [Tooltip("Sweep rotation speed in degrees per second. Positive = clockwise, negative = counter-clockwise.")]
    public float nightBalloonSweepSpeed = 20f;

    [Tooltip("Total sweep arc in degrees. 360 = full rotation, 90 = quarter-circle pendulum.")]
    [Range(30f, 360f)]
    public float nightBalloonSweepArc = 100f;

    [Tooltip("Width of the beam cone at its base on the ground (world units).")]
    public float nightBalloonSweepBeamWidth = 1.8f;

    [Tooltip("Opacity of the visible beam cone.")]
    [Range(0f, 1f)]
    public float nightBalloonSweepBeamOpacity = 0.75f;

    [Tooltip("Opacity of the bright ground spot where the beam hits.")]
    [Range(0f, 1f)]
    public float nightBalloonSweepGroundSpotOpacity = 0.85f;

    private bool lastEnableNightBalloons = false;

    [Header("Fog Effect")]
    [Tooltip("Enable/disable fog overlay on top of any biome")]
    public bool enableFog = false;

    [Tooltip("When true, switching biomes auto-applies that biome's default fog on/off, colors, and smoke colors. " +
             "You can still override manually in the editor afterwards.")]
    public bool applyBiomeFogDefaults = true;

    [Tooltip("When true, switching biomes auto-applies that biome's default background scale. " +
             "You can still override manually in the editor afterwards.")]
    public bool applyBiomeBackgroundDefaults = true;

    //Weather — particle effect references (children of Main Camera)
    [Header("Weather Particles")]
    [Tooltip("When true, switching biomes auto-applies that biome's default rain/snow particles.")]
    public bool applyBiomeWeatherDefaults = true;

    [Tooltip("Enable/disable rain particle effect")]
    public bool enableRain = false;

    [Tooltip("Enable/disable snow particle effect")]
    public bool enableSnow = false;

    [Tooltip("Drag the ParticleRain GameObject here (child of Main Camera)")]
    public GameObject particleRain;

    [Tooltip("Drag the ParticleSnow GameObject here (child of Main Camera)")]
    public GameObject particleSnow;

    //Shadow — per-biome shadow overlay
    [Header("Shadow Overlay")]
    [Tooltip("When true, switching biomes auto-applies that biome's shadow prefab.")]
    public bool applyBiomeShadowDefaults = true;

    [Tooltip("Enable/disable the shadow overlay")]
    public bool enableShadow = true;

    [Tooltip("Shadow prefab slots. Slot 0 is the default used by all biomes. " +
             "Add more slots for per-biome shadows in the future.")]
    public GameObject[] shadowPrefabs;

    // Global Volume — per-biome post-processing & illumination
    [Header("Global Volume / Illumination")]
    [Tooltip("When true, switching biomes auto-applies that biome's volume defaults (bloom, vignette, color, light).")]
    public bool applyBiomeVolumeDefaults = true;

    [Tooltip("Enable/disable the Global Volume post-processing")]
    public bool enableVolume = true;

    [Tooltip("Volume weight (0 = no effect, 1 = full effect)")]
    [Range(0f, 1f)]
    public float volumeWeight = 1f;

    [Header("Volume — Bloom")]
    public bool volumeBloomEnabled = true;
    [Range(0f, 20f)] public float volumeBloomIntensity = 1.5f;
    [Range(0f, 2f)] public float volumeBloomThreshold = 0.9f;
    [Range(0f, 1f)] public float volumeBloomScatter = 0.4f;
    public Color volumeBloomTint = new Color(0.80f, 0.95f, 1.0f, 1f);

    [Header("Volume — Vignette")]
    public bool volumeVignetteEnabled = true;
    [Range(0f, 1f)] public float volumeVignetteIntensity = 0.25f;
    [Range(0f, 1f)] public float volumeVignetteSmoothness = 0.3f;
    public bool volumeVignetteRounded = true;
    public Color volumeVignetteColor = new Color(0.50f, 0.35f, 0.60f, 1f);

    [Header("Volume — Color Adjustments")]
    public bool volumeColorAdjustmentsEnabled = false;
    [Range(-3f, 3f)] public float volumePostExposure = 0f;
    [Range(-100f, 100f)] public float volumeContrast = 0f;
    [Range(-100f, 100f)] public float volumeSaturation = 0f;
    public Color volumeColorFilter = Color.white;

    [Header("Volume — Global Light 2D")]
    [Tooltip("Override the scene's Global Light 2D intensity and color")]
    public bool volumeGlobalLightOverride = false;
    [Range(0f, 3f)] public float volumeGlobalLightIntensity = 1f;
    public Color volumeGlobalLightColor = Color.white;

    [Range(0f, 2f)]
    [Tooltip("Master fog density (0 = clear, 1 = moderate, 2 = very dense)")]
    public float fogDensity = 0.25f;

    [Tooltip("Overall fog color tint")]
    public Color fogColor = new Color(0.78f, 0.82f, 0.85f, 1.0f);

    [Tooltip("Smoke column primary color")]
    public Color fogSmokeColor = new Color(0.72f, 0.76f, 0.82f, 1.0f);

    [Tooltip("Smoke column dark inner core color")]
    public Color fogSmokeDarkCore = new Color(0.48f, 0.52f, 0.58f, 1.0f);

    [Header("Rendering Mode")]
    [Tooltip("Toggle ON = GPU instanced (GrassOverlayGPU), OFF = CPU mesh (GrassOverlay). Use to preview what non-GPU players see.")]
    public bool useGPUGrass = true;

    [Tooltip("Blade count override for CPU mode")]
    public int cpuGrassBladeCount = 50000;
    [Tooltip("Clump count override for CPU mode")]
    public int cpuGrassClumpCount = 450;

    [Header("Background Tiling")]
    public string backgroundObjectName = "Background";

    // Grass settings
    [Header("Grass — Distribution")]
    public int grassBladeCount = 4000000;
    //public float grassSpawnRadius = 22f;
    public float grassSpawnRadius = 60f;
    public float grassCoreExclusion = 1.5f;

    [Header("Grass — Blade Shape")]
    public float grassBladeHeight = 0.22f;
    public float grassBladeWidth = 0.02f;
    public float grassCurvature = 0.4f;
    public float grassTipCounterCurve = 0.15f;
    public float grassWidthWobble = 0.12f;

    [Header("Grass — Blade Type Mix")]
    [Range(0f, 0.5f)] public float grassShortBladeRatio = 0.22f;
    [Range(0f, 0.3f)] public float grassTallBladeRatio = 0.12f;
    [Range(0f, 0.15f)] public float grassDeadBladeRatio = 0.06f;

    [Header("Grass — Clumping")]
    //public int grassClumpCount = 44500;
    public int grassClumpCount = 200000;

    //public float grassClumpSpread = 0.1f;
    public float grassClumpSpread = 1f;
    //[Range(0f, 1f)] public float grassFreeScatter = 0.07f;
    [Range(0f, 1f)] public float grassFreeScatter = 0.2f;
    [Range(0f, 1f)] public float grassClumpLeanCoherence = 0.5f;

    [Header("Grass — Colors")]
    public Color grassDarkBase = new Color(0.09f, 0.30f, 0.05f, 1.0f);
    public Color grassMidBlade = new Color(0.15f, 0.46f, 0.09f, 0.93f);
    public Color grassBrightTip = new Color(0.28f, 0.64f, 0.15f, 0.72f);
    public Color grassGroundCover = new Color(0.07f, 0.25f, 0.04f, 1.0f);
    public Color grassDeadBase = new Color(0.28f, 0.24f, 0.08f, 0.95f);
    public Color grassDeadTip = new Color(0.40f, 0.34f, 0.12f, 0.75f);

    [Header("Grass — Wind & Shading")]
    public float grassWindStrength = 0.12f;
    public float grassWindSpeed = 2.0f;
    public float grassGustStrength = 0.15f;
    public float grassGustScale = 0.25f;
    public float grassGustSpeed = 0.7f;
    public float grassShadowDarken = 0.18f;
    public float grassHighlightBrighten = 0.12f;
    public float grassAmbientOcclusion = 0.22f;
    public float grassTipHighlight = 0.12f;
    public float grassLightAngle = 135f;
    public float grassSubsurfaceStrength = 0.15f;
    public Color grassSubsurfaceColor = new Color(0.4f, 0.7f, 0.15f, 1f);
    public float grassWindColorShift = 0.1f;
    public float grassPatchScale = 0.15f;
    public float grassPatchStrength = 0.12f;

    // Snow settings 
    [Header("Snow — Airborne Particles")]
    public int snowParticleCount = 3500;
    public float snowFallSpeed = 0.5f;
    public float snowDrift = 0.6f;
    public float snowSpawnHeight = 30f;
    public float snowSpawnRadius = 60f;

    [Header("Snow — Flake Types")]
    [Range(0f, 0.5f)] public float snowPowderRatio = 0.35f;
    [Range(0f, 0.3f)] public float snowClumpRatio = 0.12f;
    [Range(0f, 0.15f)] public float snowCrystalFlakeRatio = 0.08f;

    [Header("Snow — Ground Accumulation")]
    public int snowGroundCount = 150000;
    public float snowGroundRadius = 60f;
    public float snowGroundCoreExclusion = 1.5f;

    [Header("Snow — Snowdrifts")]
    public int snowDriftCount = 280;
    public float snowDriftMinLength = 0.4f;
    public float snowDriftMaxLength = 1.8f;
    public float snowDriftMinWidth = 0.08f;
    public float snowDriftMaxWidth = 0.3f;
    public float snowDriftWindAlignment = 0.7f;
    public float snowDriftWindAngle = -20f;
    public Color snowDriftColorBright = new Color(0.96f, 0.98f, 1.0f, 0.92f);
    public Color snowDriftColorShadow = new Color(0.78f, 0.83f, 0.92f, 0.80f);

    [Header("Snow — Ice Crystal Patches")]
    public int snowIcePatchCount = 1200;
    public float snowIcePatchMinSize = 0.015f;
    public float snowIcePatchMaxSize = 0.06f;
    public Color snowIceColorBase = new Color(0.85f, 0.92f, 1.0f, 0.70f);
    public Color snowIceColorGlint = new Color(1.0f, 1.0f, 1.0f, 0.95f);

    [Header("Snow — Frost Vegetation")]
    public int snowFrostVegCount = 600;
    public float snowFrostVegMinHeight = 0.06f;
    public float snowFrostVegMaxHeight = 0.18f;
    public float snowFrostVegWidth = 0.008f;
    public Color snowFrostVegBase = new Color(0.25f, 0.22f, 0.18f, 0.85f);
    public Color snowFrostVegTip = new Color(0.75f, 0.82f, 0.92f, 0.70f);
    public Color snowFrostVegIce = new Color(0.88f, 0.93f, 1.0f, 0.60f);

    [Header("Snow — Appearance")]
    public float snowFlakeMinSize = 0.02f;
    public float snowFlakeMaxSize = 0.08f;
    public float snowGroundPatchMinSize = 0.05f;
    public float snowGroundPatchMaxSize = 0.16f;
    public Color snowColorBright = new Color(0.95f, 0.97f, 1.0f, 0.90f);
    public Color snowColorMid = new Color(0.82f, 0.86f, 0.92f, 0.75f);
    public Color snowColorShadow = new Color(0.62f, 0.68f, 0.78f, 0.60f);
    public Color snowGroundTint = new Color(0.88f, 0.91f, 0.96f, 0.50f);

    [Header("Snow — Wind")]
    public float snowWindStrength = 1.8f;
    public float snowWindSpeed = 1.5f;
    public float snowGustStrength = 0.8f;
    public float snowGustSpeed = 0.4f;
    public float snowTurbulence = 1.8f;
    public float snowSwirl = 0.6f;

    // Desert settings 
    [Header("Desert — Ground General")]
    //public int desertGroundCount = 32000;
    public int desertGroundCount = 120000;
    // public float desertGroundRadius = 22f;
    public float desertGroundRadius = 60f;


    public float desertGroundCoreExclusion = 1.5f;
    public float desertWindAngle = 10f;

    [Header("Desert — Sand Ripples")]
    public int desertRippleCount = 400;
    public float desertRippleMinLength = 0.6f;
    public float desertRippleMaxLength = 2.5f;
    public float desertRippleWidth = 0.025f;
    public float desertRippleWindAlignment = 0.85f;
    public Color desertRippleCrest = new Color(0.95f, 0.86f, 0.62f, 0.30f);
    public Color desertRippleTrough = new Color(0.68f, 0.56f, 0.35f, 0.22f);

    [Header("Desert — Cracked Earth")]
    public int desertCrackedEarthCount = 180;
    public float desertCrackedEarthMinSize = 0.12f;
    public float desertCrackedEarthMaxSize = 0.35f;
    public float desertCrackWidth = 0.004f;
    public Color desertCrackColor = new Color(0.30f, 0.24f, 0.16f, 0.40f);
    public Color desertCrackedSurface = new Color(0.78f, 0.68f, 0.48f, 0.25f);

    [Header("Desert — Dried Scrub")]
    public int desertScrubCount = 350;
    public float desertScrubMinHeight = 0.04f;
    public float desertScrubMaxHeight = 0.14f;
    public float desertScrubWidth = 0.006f;
    public Color desertScrubBase = new Color(0.32f, 0.26f, 0.15f, 0.80f);
    public Color desertScrubTip = new Color(0.50f, 0.42f, 0.25f, 0.55f);
    public Color desertScrubDead = new Color(0.42f, 0.35f, 0.22f, 0.65f);

    [Header("Desert — Heat Shimmer Wisps")]
    public int desertShimmerWispCount = 120;
    public float desertShimmerRiseSpeed = 0.3f;
    public Color desertShimmerWispColor = new Color(0.92f, 0.85f, 0.65f, 0.06f);

    [Header("Desert — Dust Devils")]
    public int desertDustDevilCount = 3;
    public int desertDustDevilParticles = 80;
    public float desertDustDevilRadius = 0.4f;
    public float desertDustDevilHeight = 2.5f;
    public float desertDustDevilSpinSpeed = 4.0f;
    public float desertDustDevilDriftSpeed = 0.3f;
    public Color desertDustDevilColor = new Color(0.85f, 0.75f, 0.50f, 0.35f);

    [Header("Desert — Saltation (Low Sand)")]
    public int desertSaltationCount = 5000;
    public float desertSaltationSpawnRadius = 60f;
    public float desertSaltationMinHeight = -60f;
    public float desertSaltationMaxHeight = 60f;
    [Range(0f, 1f)] public float desertSaltationGroundBias = 0.75f;
    public float desertSaltationMinSize = 0.01f;
    public float desertSaltationMaxSize = 0.05f;

    [Header("Desert — Dust Haze (High)")]
    public int desertDustCount = 800;
    public float desertDustSpawnRadius = 60f;
    public float desertDustMinHeight = -50f;
    public float desertDustMaxHeight = 60f;
    public float desertDustMinSize = 0.06f;
    public float desertDustMaxSize = 0.20f;

    [Header("Desert — Colors")]
    public Color desertSandBright = new Color(0.92f, 0.82f, 0.58f, 0.85f);
    public Color desertSandMid = new Color(0.82f, 0.70f, 0.45f, 0.70f);
    public Color desertSandDark = new Color(0.62f, 0.50f, 0.30f, 0.55f);
    public Color desertSandShadow = new Color(0.45f, 0.38f, 0.25f, 0.45f);
    public Color desertDustColor = new Color(0.90f, 0.82f, 0.60f, 0.18f);

    [Header("Desert — Wind")]
    public float desertWindStrength = 3.5f;
    public float desertWindSpeed = 2.0f;
    public float desertGustStrength = 1.5f;
    public float desertGustSpeed = 0.5f;
    public float desertTurbulence = 2.2f;

    [Header("Desert — Saltation Physics")]
    public float desertBounceHeight = 0.4f;
    public float desertBounceSpeed = 3.5f;
    public float desertStreakDrift = 0.3f;

    [Header("Desert — Dust Physics")]
    public float desertDustDrift = 0.15f;
    public float desertDustWindMult = 0.25f;
    public float desertDustSwirl = 0.3f;

    // Wasteland settings 
    [Header("Wasteland — Ground")]
    public int wastelandGroundCount = 180000;
    public float wastelandGroundRadius = 60f;
    public float wastelandCoreExclusion = 1.5f;

    [Header("Wasteland — Toxic Pools")]
    public int wastelandToxicPoolCount = 200;
    public Color wastelandToxicPoolCenter = new Color(0.18f, 0.32f, 0.08f, 0.45f);
    public Color wastelandToxicPoolEdge = new Color(0.28f, 0.38f, 0.10f, 0.15f);

    [Header("Wasteland — Rust Stains")]
    public int wastelandRustStainCount = 500;
    public Color wastelandRustColorDark = new Color(0.35f, 0.14f, 0.06f, 0.50f);
    public Color wastelandRustColorLight = new Color(0.52f, 0.28f, 0.10f, 0.30f);

    [Header("Wasteland — Bone/Debris")]
    public int wastelandBoneCount = 400;
    public Color wastelandBoneColor = new Color(0.62f, 0.56f, 0.45f, 0.70f);
    public Color wastelandBoneTip = new Color(0.48f, 0.42f, 0.35f, 0.45f);

    [Header("Wasteland — Dust Storm")]
    public int wastelandDustStormCount = 4500;
    public float wastelandDustStormSpawnRadius = 60f;

    [Header("Wasteland — Ash Flecks")]
    public int wastelandAshCount = 6000;
    public float wastelandAshSpawnRadius = 60f;

    [Header("Wasteland — Embers")]
    public int wastelandEmberCount = 1500;
    public float wastelandEmberSpawnRadius = 60f;
    public Color wastelandEmberHot = new Color(1.0f, 0.50f, 0.10f, 0.92f);
    public Color wastelandEmberDim = new Color(0.70f, 0.28f, 0.05f, 0.55f);
    public float wastelandEmberFlickerSpeed = 5.0f;

    [Header("Wasteland — Smoke Wisps")]
    public int wastelandSmokeWispCount = 200;
    public float wastelandSmokeRiseSpeed = 0.15f;
    public Color wastelandSmokeColor = new Color(0.12f, 0.10f, 0.08f, 0.18f);

    [Header("Wasteland — High Haze")]
    public int wastelandHazeCount = 350;
    public float wastelandHazeSpawnRadius = 60f;

    [Header("Wasteland — Wind")]
    public float wastelandWindStrength = 1.8f;
    public float wastelandWindAngle = 8f;

    // Stones settings
    [Header("Stones — Ground")]
    public int stonesGroundCount = 80000;
    public float stonesGroundRadius = 60f;
    public float stonesCoreExclusion = 1.5f;

    [Header("Stones — Dust Motes")]
    public int stonesDustMoteCount = 1200;
    public float stonesDustMoteSpawnRadius = 60f;

    [Header("Stones — Wind")]
    public float stonesWindStrength = 0.8f;
    public float stonesWindAngle = 5f;

    // GrassCartoon settings
    [Header("GrassCartoon — Distribution")]
    public int grassCartoonInstanceCount = 50000;
    public float grassCartoonSpawnRadius = 50f;
    public float grassCartoonCoreExclusion = 1.5f;

    [Header("GrassCartoon — Prefab Slots (up to 8)")]
    [Tooltip("Drag grass prefabs here. Empty slots are skipped.")]
    public GameObject grassCartoonPrefab1;
    public GameObject grassCartoonPrefab2;
    public GameObject grassCartoonPrefab3;
    public GameObject grassCartoonPrefab4;
    public GameObject grassCartoonPrefab5;
    public GameObject grassCartoonPrefab6;
    public GameObject grassCartoonPrefab7;
    public GameObject grassCartoonPrefab8;

    [Header("GrassCartoon — Scale")]
    [Tooltip("Base scale for prefab instances. Prefabs are natively 0.5, so 0.5 = original size.")]
    public float grassCartoonBaseScale = 0.2f;
    [Range(0f, 0.5f)]
    [Tooltip("Random size variation (0.15 = ±15%)")]
    public float grassCartoonScaleVariation = 0.25f;

    // Border Ring — per-biome prefab slots (up to 6 each) + overlap toggle

    [Header("Border Ring — Grass Biome")]
    public GameObject borderGrassPrefab1;
    public GameObject borderGrassPrefab2;
    public GameObject borderGrassPrefab3;
    public GameObject borderGrassPrefab4;
    [Tooltip("When ON, border prefabs won't visually overlap (good for rocks). OFF = overlapping (good for trees).")]
    public bool borderGrassPreventOverlap = false;
    [Tooltip("Spacing between border prefabs for this biome (smaller = denser).")]
    public float borderGrassSpacing = 0.8f;

    [Header("Border Ring — Snow Biome")]
    public GameObject borderSnowPrefab1;
    public GameObject borderSnowPrefab2;
    public GameObject borderSnowPrefab3;
    public GameObject borderSnowPrefab4;
    public bool borderSnowPreventOverlap = false;
    public float borderSnowSpacing = 0.8f;

    [Header("Border Ring — Desert Biome")]
    public GameObject borderDesertPrefab1;
    public GameObject borderDesertPrefab2;
    public GameObject borderDesertPrefab3;
    public GameObject borderDesertPrefab4;
    public bool borderDesertPreventOverlap = true;
    public float borderDesertSpacing = 0.4f;

    [Header("Border Ring — Wasteland Biome")]
    public GameObject borderWastelandPrefab1;
    public GameObject borderWastelandPrefab2;
    public GameObject borderWastelandPrefab3;
    public GameObject borderWastelandPrefab4;
    public bool borderWastelandPreventOverlap = true;
    public float borderWastelandSpacing = 0.4f;

    [Header("Border Ring — Stones Biome")]
    public GameObject borderStonesPrefab1;
    public GameObject borderStonesPrefab2;
    public GameObject borderStonesPrefab3;
    public GameObject borderStonesPrefab4;
    public bool borderStonesPreventOverlap = true;
    public float borderStonesSpacing = 0.4f;

    [Header("Border Ring — GrassCartoon Biome")]
    public GameObject borderGrassCartoonPrefab1;
    public GameObject borderGrassCartoonPrefab2;
    public GameObject borderGrassCartoonPrefab3;
    public GameObject borderGrassCartoonPrefab4;
    public bool borderGrassCartoonPreventOverlap = false;
    public float borderGrassCartoonSpacing = 0.8f;

    [Header("Border Ring — Marsh Biome")]
    public GameObject borderMarshPrefab1;
    public GameObject borderMarshPrefab2;
    public GameObject borderMarshPrefab3;
    public GameObject borderMarshPrefab4;
    public bool borderMarshPreventOverlap = false;
    public float borderMarshSpacing = 0.8f;

    [Header("Border Ring — Night Biome")]
    public GameObject borderNightPrefab1;
    public GameObject borderNightPrefab2;
    public GameObject borderNightPrefab3;
    public GameObject borderNightPrefab4;
    public bool borderNightPreventOverlap = false;
    public float borderNightSpacing = 0.8f;

    // Obstacle Generation — per-biome prefab slots (up to 3 cluster + solo)
    // All other obstacle settings (count, scale, clustering, etc.) are on the ObstacleGenerator component directly.

    [Header("Obstacles — Custom Cluster Blueprints (optional)")]
    [Tooltip("Drag ObstacleClusterBlueprint assets here to override built-in compositions. " +
             "Leave empty to use the 8 built-in templates (TreeGrove, RockOutcrop, etc.).")]
    public ObstacleClusterBlueprint[] obstacleCustomBlueprints;

    [Header("Obstacles — Grass Biome (drag up to 3 prefabs)")]
    public GameObject obstacleGrassPrefab1;
    public GameObject obstacleGrassPrefab2;
    public GameObject obstacleGrassPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleGrassSoloPrefab;

    [Header("Obstacles — Snow Biome")]
    public GameObject obstacleSnowPrefab1;
    public GameObject obstacleSnowPrefab2;
    public GameObject obstacleSnowPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleSnowSoloPrefab;

    [Header("Obstacles — Desert Biome")]
    public GameObject obstacleDesertPrefab1;
    public GameObject obstacleDesertPrefab2;
    public GameObject obstacleDesertPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleDesertSoloPrefab;

    [Header("Obstacles — Wasteland Biome")]
    public GameObject obstacleWastelandPrefab1;
    public GameObject obstacleWastelandPrefab2;
    public GameObject obstacleWastelandPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleWastelandSoloPrefab;

    [Header("Obstacles — Stones Biome")]
    public GameObject obstacleStonesPrefab1;
    public GameObject obstacleStonesPrefab2;
    public GameObject obstacleStonesPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleStonesSoloPrefab;

    [Header("Obstacles — GrassCartoon Biome")]
    public GameObject obstacleGrassCartoonPrefab1;
    public GameObject obstacleGrassCartoonPrefab2;
    public GameObject obstacleGrassCartoonPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleGrassCartoonSoloPrefab;

    [Header("Obstacles — Marsh Biome")]
    public GameObject obstacleMarshPrefab1;
    public GameObject obstacleMarshPrefab2;
    public GameObject obstacleMarshPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleMarshSoloPrefab;

    [Header("Obstacles — Night Biome")]
    public GameObject obstacleNightPrefab1;
    public GameObject obstacleNightPrefab2;
    public GameObject obstacleNightPrefab3;
    [Tooltip("Solo-only obstacle (e.g. fireplace). Always spawned individually, never in clusters.")]
    public GameObject obstacleNightSoloPrefab;

    // Marsh settings — water puddles under grass
    [Header("Marsh — Puddle Distribution")]
    public int marshPuddleCount = 900;
    public float marshSpawnRadius = 60f;
    public float marshCoreExclusion = 2.0f;
    public int marshClusterCount = 140;
    public float marshClusterSpread = 4.0f;
    [Range(0f, 1f)] public float marshFreeScatter = 0.12f;

    [Header("Marsh — Puddle Size — Small")]
    public float marshPuddleMinRadius = 0.08f;
    public float marshPuddleMaxRadius = 0.3f;
    [Header("Marsh — Puddle Size — Medium")]
    public int marshMediumPuddleCount = 80;
    public float marshMediumMinRadius = 0.25f;
    public float marshMediumMaxRadius = 0.7f;
    [Header("Marsh — Wetland Chains")]
    public int marshWetlandChainCount = 30;
    public int marshWetlandMinLobes = 3;
    public int marshWetlandMaxLobes = 5;
    public float marshWetlandLobeMinRadius = 0.2f;
    public float marshWetlandLobeMaxRadius = 0.6f;
    public float marshWetlandLobeSpacing = 0.55f;

    [Header("Marsh — Puddle Shape")]
    [Range(8, 32)] public int marshPuddleSegments = 24;
    [Range(0f, 0.6f)] public float marshShapeDistortion = 0.35f;
    [Range(1f, 3f)] public float marshMaxElongation = 2.0f;
    [Range(0f, 0.4f)] public float marshConcavityChance = 0.25f;
    [Range(0f, 0.5f)] public float marshConcavityDepth = 0.35f;
    [Range(0.05f, 0.5f)] public float marshShoreBandWidth = 0.25f;

    [Header("Marsh — Water Colors")]
    public Color marshWaterShallow = new Color(0.10f, 0.22f, 0.20f, 0.82f);
    public Color marshWaterMid = new Color(0.06f, 0.15f, 0.17f, 0.88f);
    public Color marshWaterDeep = new Color(0.04f, 0.10f, 0.14f, 0.92f);
    public Color marshWaterEdge = new Color(0.08f, 0.18f, 0.14f, 0.50f);
    public Color marshReflectionColor = new Color(0.45f, 0.58f, 0.68f, 0.40f);
    public Color marshSpecularHighlight = new Color(0.85f, 0.92f, 1.00f, 0.55f);

    [Header("Marsh — Mud/Shore")]
    public Color marshMudDark = new Color(0.08f, 0.06f, 0.03f, 0.80f);
    public Color marshMudLight = new Color(0.16f, 0.13f, 0.07f, 0.60f);
    public Color marshWetGround = new Color(0.04f, 0.12f, 0.06f, 0.55f);
    public Color marshFoamColor = new Color(0.35f, 0.38f, 0.30f, 0.45f);
    [Range(0.02f, 0.15f)] public float marshFoamWidth = 0.06f;

    [Header("Marsh — Water Animation")]
    public float marshEdgeWobbleStrength = 0.025f;
    public float marshEdgeWobbleSpeed = 1.5f;
    [Range(0f, 0.15f)] public float marshColorShimmerStrength = 0.06f;
    public float marshColorShimmerSpeed = 0.8f;
    [Range(0f, 0.15f)] public float marshBreatheStrength = 0.06f;
    public float marshBreatheSpeed = 0.5f;
    public float marshWaveStrength = 0.012f;
    public float marshWaveSpeed = 0.6f;
    public float marshWaveScale = 3.0f;

    [Header("Marsh — Ripples")]
    public int marshRipplesPerPuddle = 3;
    public float marshRippleSpeed = 1.2f;
    public Color marshRippleColor = new Color(0.50f, 0.65f, 0.72f, 0.45f);

    [Header("Marsh — Insect Dimples")]
    public int marshDimpleSlots = 60;
    public float marshDimpleInterval = 2.5f;

    [Header("Marsh — Caustics")]
    public int marshCausticCount = 2000;
    public Color marshCausticColor = new Color(0.60f, 0.78f, 0.65f, 0.30f);
    public float marshCausticDriftSpeed = 0.25f;

    [Header("Marsh — Sediment")]
    public int marshSedimentCount = 3000;

    [Header("Marsh — Surface Film")]
    public int marshFilmPatchCount = 50;

    [Header("Marsh — Lily Pads")]
    public int marshLilyPadCount = 80;
    public Color marshLilyPadColor = new Color(0.10f, 0.35f, 0.08f, 0.92f);
    public Color marshLilyPadDark = new Color(0.05f, 0.20f, 0.04f, 0.95f);

    [Header("Marsh — Reeds")]
    public int marshReedCount = 300;
    public float marshReedMinHeight = 0.12f;
    public float marshReedMaxHeight = 0.35f;
    public Color marshReedBase = new Color(0.12f, 0.25f, 0.06f, 0.92f);
    public Color marshReedTip = new Color(0.25f, 0.38f, 0.14f, 0.70f);

    [Header("Marsh — Grass Tint (darker/wetter grass)")]
    public Color marshGrassDarkBase = new Color(0.06f, 0.26f, 0.06f, 1.0f);
    public Color marshGrassMidBlade = new Color(0.10f, 0.38f, 0.10f, 0.93f);
    public Color marshGrassBrightTip = new Color(0.20f, 0.54f, 0.16f, 0.72f);
    public Color marshGrassGroundCover = new Color(0.05f, 0.20f, 0.05f, 1.0f);

    // Night overlay settings — used by universal night mode AND the legacy Night biome
    [Header("Night — Darkness Preset")]
    [Tooltip("Quick preset: Dusk (dim evening), Dark (proper night), PitchBlack (torch-only), Custom (manual).")]
    public NightOverlay.NightPreset nightPreset = NightOverlay.NightPreset.Dark;

    [Header("Night — Darkness (used when preset = Custom)")]
    [Range(0f, 1f)]
    [Tooltip("Master darkness. 1 = pitch black. Ignored unless preset is Custom.")]
    public float nightDarkness = 0.92f;

    [Tooltip("Base night color")]
    public Color nightColor = new Color(0.02f, 0.02f, 0.06f, 1f);

    [Range(0f, 0.3f)]
    [Tooltip("Ambient visibility. 0 = truly black outside torch. Ignored unless preset is Custom.")]
    public float nightAmbientLight = 0.04f;

    [Header("Night — Player Glow (used when preset = Custom)")]
    public float nightPlayerGlowRadius = 1.8f;

    [Range(0f, 1f)]
    [Tooltip("Glow strength. 0 = no glow at all (pitch black around player). Ignored unless preset is Custom.")]
    public float nightPlayerGlowStrength = 0.35f;

    [Header("Night — Torch")]
    public bool nightTorchEnabled = true;

    public float nightTorchRange = 8f;

    [Range(5f, 60f)]
    public float nightTorchHalfAngle = 22f;

    [Range(0f, 1f)]
    public float nightTorchEdgeSoftness = 0.35f;

    [Range(0f, 1f)]
    [Tooltip("How much the torch reveals. 0 = torch shows nothing, 1 = full reveal. Ignored unless preset is Custom.")]
    public float nightTorchBrightness = 1.0f;

    public Color nightTorchWarmTint = new Color(1.0f, 0.85f, 0.55f, 0.12f);

    public float nightFlickerSpeed = 3.5f;

    [Range(0f, 0.15f)]
    public float nightFlickerIntensity = 0.06f;

    // Legacy Night biome grass settings (kept for backwards compatibility with Night biome)
    [Header("Night Biome — Grass Distribution (only used when activeBiome = Night)")]
    public int nightGrassInstanceCount = 50000;
    public float nightGrassSpawnRadius = 50f;
    public float nightGrassCoreExclusion = 1.5f;

    [Header("Night Biome — Grass Scale")]
    public float nightGrassBaseScale = 0.2f;
    [Range(0f, 0.5f)]
    public float nightGrassScaleVariation = 0.25f;

    // Fog-specific settings exposed in BiomeManager
    [Header("Fog — Deep Field (visibility reduction)")]
    public int fogDeepCount = 10;
    [Range(0f, 1.5f)] public float fogDeepDensity = 0.6f;
    [Range(0f, 0.6f)] public float fogVisibilityReduction = 0.35f;

    [Header("Fog — Rolling Banks")]
    public int fogBankCount = 16;
    public float fogBankMinSize = 12f;
    public float fogBankMaxSize = 28f;
    public float fogBankDriftSpeed = 0.25f;
    [Range(0f, 1.5f)] public float fogBankDensity = 0.7f;

    [Header("Fog — Smoke Columns")]
    public int fogSmokeColumnCount = 30;
    public float fogSmokeMinHeight = 20f;
    public float fogSmokeMaxHeight = 50f;
    public float fogSmokeMinWidth = 6f;
    public float fogSmokeMaxWidth = 16f;
    public float fogSmokeSpawnRadius = 60f;
    [Range(0f, 1.5f)] public float fogSmokeDensity = 0.75f;
    public float fogSmokeRiseSpeed = 0.2f;
    public float fogSmokeBillowAmount = 1.2f;
    public float fogSmokeDissipation = 0.7f;

    [Header("Fog — Near Wisps")]
    public int fogNearWispCount = 14;
    [Range(0f, 1.5f)] public float fogNearWispDensity = 0.45f;
    public float fogNearWispDriftSpeed = 0.7f;

    [Header("Fog — Moisture")]
    public int fogMoistureCount = 600;

    // Internal references 
    private BiomeType lastAppliedBiome = (BiomeType)(-1);
    private GameObject activeShadowInstance; //Shadow
    private bool lastUseGPUGrass = true;
    private bool lastEnableFog = false;
    private float lastFogDensity = -1f;
    private Color lastFogColor;
    private Color lastFogSmokeColor;
    private Color lastFogSmokeDarkCore;
    private float lastBackgroundScale = -1f;
    private float lastGroundCoverage = -1f;
    private bool lastEnableRain = false;   //Weather
    private bool lastEnableSnow = false;   //Weather
    private bool lastEnableShadow = false; //Shadow
    private bool lastEnableVolume = false; //Volume
    private float lastVolumeWeight = -1f;
    private float lastVolumeBloomIntensity = -1f;
    private float lastVolumeBloomThreshold = -1f;
    private float lastVolumeBloomScatter = -1f;
    private bool lastVolumeBloomEnabled = false;
    private bool lastVolumeVignetteEnabled = false;
    private float lastVolumeVignetteIntensity = -1f;
    private bool lastVolumeColorAdjEnabled = false;
    private float lastVolumePostExposure = -99f;
    private float lastVolumeContrast = -999f;
    private float lastVolumeSaturation = -999f;
    private bool lastVolumeGlobalLightOverride = false;
    private float lastVolumeGlobalLightIntensity = -1f;
    private bool initialized = false;

    // Volume runtime references
    private GameObject activeVolumeInstance;
    private Volume activeVolumeComponent;
    private Light2D activeGlobalLight;

    // Map background paths per biome 
    private string GetBackgroundPath(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass: return "Backgrounds/Background8";
            case BiomeType.Snow: return "Backgrounds/BackgroundSnow";// "Backgrounds/Background9";
            case BiomeType.Desert: return "Backgrounds/BackgroundDesert"; // "Backgrounds/Background10"; 
            case BiomeType.Wasteland: return "Backgrounds/Background11";
            case BiomeType.Stones: return "Backgrounds/Background12";
            //case BiomeType.GrassCartoon: return "Backgrounds/BackgroundGrassDark";
            case BiomeType.GrassCartoon: return "Backgrounds/Background13";
            case BiomeType.Marsh: return "Backgrounds/Background8"; // Same grass background
            case BiomeType.Night: return "Backgrounds/Background13"; // Same as GrassCartoon base
            case BiomeType.Corruption: return "Backgrounds/Background13"; // Same as Night base
            case BiomeType.PitchBlack: return "Backgrounds/Background13"; // Same as Night base
            default: return "Backgrounds/Background8";
        }
    }

    /// Returns the 4-slot prefab array for the given biome's border ring.
    GameObject[] GetBorderPrefabsForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass:
                return new GameObject[] {
                    borderGrassPrefab1, borderGrassPrefab2,
                    borderGrassPrefab3, borderGrassPrefab4
                };
            case BiomeType.Snow:
                return new GameObject[] {
                    borderSnowPrefab1, borderSnowPrefab2,
                    borderSnowPrefab3, borderSnowPrefab4
                };
            case BiomeType.Desert:
                return new GameObject[] {
                    borderDesertPrefab1, borderDesertPrefab2,
                    borderDesertPrefab3, borderDesertPrefab4
                };
            case BiomeType.Wasteland:
                return new GameObject[] {
                    borderWastelandPrefab1, borderWastelandPrefab2,
                    borderWastelandPrefab3, borderWastelandPrefab4
                };
            case BiomeType.Stones:
                return new GameObject[] {
                    borderStonesPrefab1, borderStonesPrefab2,
                    borderStonesPrefab3, borderStonesPrefab4
                };
            case BiomeType.GrassCartoon:
                return new GameObject[] {
                    borderGrassCartoonPrefab1, borderGrassCartoonPrefab2,
                    borderGrassCartoonPrefab3, borderGrassCartoonPrefab4
                };
            case BiomeType.Marsh:
                return new GameObject[] {
                    borderMarshPrefab1, borderMarshPrefab2,
                    borderMarshPrefab3, borderMarshPrefab4
                };
            case BiomeType.Night:
                return new GameObject[] {
                    borderNightPrefab1, borderNightPrefab2,
                    borderNightPrefab3, borderNightPrefab4
                };
            case BiomeType.Corruption:
                // Re-use Night's border prefabs for Corruption (visual continuity).
                return GetBorderPrefabsForBiome(BiomeType.Night);
            case BiomeType.PitchBlack:
                // Re-use Night's border prefabs — barely visible anyway.
                return GetBorderPrefabsForBiome(BiomeType.Night);
            default:
                return GetBorderPrefabsForBiome(BiomeType.Grass);
        }
    }

    // Returns whether the given biome's border should prevent overlap.
    bool GetBorderPreventOverlap(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass: return borderGrassPreventOverlap;
            case BiomeType.Snow: return borderSnowPreventOverlap;
            case BiomeType.Desert: return borderDesertPreventOverlap;
            case BiomeType.Wasteland: return borderWastelandPreventOverlap;
            case BiomeType.Stones: return borderStonesPreventOverlap;
            case BiomeType.GrassCartoon: return borderGrassCartoonPreventOverlap;
            case BiomeType.Marsh: return borderMarshPreventOverlap;
            case BiomeType.Night: return borderNightPreventOverlap;
            case BiomeType.Corruption: return borderNightPreventOverlap;
            case BiomeType.PitchBlack: return borderNightPreventOverlap;
            default: return false;
        }
    }

    // Returns the per-biome spacing for the border ring.
    float GetBorderSpacing(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass: return borderGrassSpacing;
            case BiomeType.Snow: return borderSnowSpacing;
            case BiomeType.Desert: return borderDesertSpacing;
            case BiomeType.Wasteland: return borderWastelandSpacing;
            case BiomeType.Stones: return borderStonesSpacing;
            case BiomeType.GrassCartoon: return borderGrassCartoonSpacing;
            case BiomeType.Marsh: return borderMarshSpacing;
            case BiomeType.Night: return borderNightSpacing;
            case BiomeType.Corruption: return borderNightSpacing;
            case BiomeType.PitchBlack: return borderNightSpacing;
            default: return 0.8f;
        }
    }

    // Returns the 3-slot prefab array for the given biome's obstacle generation.
    public GameObject[] GetObstaclePrefabsForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass:
                return new GameObject[] { obstacleGrassPrefab1, obstacleGrassPrefab2, obstacleGrassPrefab3 };
            case BiomeType.Snow:
                return new GameObject[] { obstacleSnowPrefab1, obstacleSnowPrefab2, obstacleSnowPrefab3 };
            case BiomeType.Desert:
                return new GameObject[] { obstacleDesertPrefab1, obstacleDesertPrefab2, obstacleDesertPrefab3 };
            case BiomeType.Wasteland:
                return new GameObject[] { obstacleWastelandPrefab1, obstacleWastelandPrefab2, obstacleWastelandPrefab3 };
            case BiomeType.Stones:
                return new GameObject[] { obstacleStonesPrefab1, obstacleStonesPrefab2, obstacleStonesPrefab3 };
            case BiomeType.GrassCartoon:
                return new GameObject[] { obstacleGrassCartoonPrefab1, obstacleGrassCartoonPrefab2, obstacleGrassCartoonPrefab3 };
            case BiomeType.Marsh:
                return new GameObject[] { obstacleMarshPrefab1, obstacleMarshPrefab2, obstacleMarshPrefab3 };
            case BiomeType.Night:
                return new GameObject[] { obstacleNightPrefab1, obstacleNightPrefab2, obstacleNightPrefab3 };
            case BiomeType.Corruption:
                return new GameObject[] { obstacleNightPrefab1, obstacleNightPrefab2, obstacleNightPrefab3 };
            case BiomeType.PitchBlack:
                return new GameObject[] { obstacleNightPrefab1, obstacleNightPrefab2, obstacleNightPrefab3 };
            default:
                return GetObstaclePrefabsForBiome(BiomeType.Grass);
        }
    }

    // Returns solo-only prefabs for the given biome (never placed in clusters).
    public GameObject[] GetObstacleSoloPrefabsForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass: return new GameObject[] { obstacleGrassSoloPrefab };
            case BiomeType.Snow: return new GameObject[] { obstacleSnowSoloPrefab };
            case BiomeType.Desert: return new GameObject[] { obstacleDesertSoloPrefab };
            case BiomeType.Wasteland: return new GameObject[] { obstacleWastelandSoloPrefab };
            case BiomeType.Stones: return new GameObject[] { obstacleStonesSoloPrefab };
            case BiomeType.GrassCartoon: return new GameObject[] { obstacleGrassCartoonSoloPrefab };
            case BiomeType.Marsh: return new GameObject[] { obstacleMarshSoloPrefab };
            case BiomeType.Night: return new GameObject[] { obstacleNightSoloPrefab };
            case BiomeType.Corruption: return new GameObject[] { obstacleNightSoloPrefab };
            case BiomeType.PitchBlack: return new GameObject[] { obstacleNightSoloPrefab };
            default: return GetObstacleSoloPrefabsForBiome(BiomeType.Grass);
        }
    }

    void Awake()
    {
        lastUseGPUGrass = useGPUGrass;
        lastBackgroundScale = backgroundScale;
        lastGroundCoverage = groundCoverageRadius;
        lastEnableNightMode = enableNightMode;
        lastEnableVolume = enableVolume;
        lastVolumeWeight = volumeWeight;
        lastVolumeBloomEnabled = volumeBloomEnabled;
        lastVolumeBloomIntensity = volumeBloomIntensity;
        lastVolumeBloomThreshold = volumeBloomThreshold;
        lastVolumeBloomScatter = volumeBloomScatter;
        lastVolumeVignetteEnabled = volumeVignetteEnabled;
        lastVolumeVignetteIntensity = volumeVignetteIntensity;
        lastVolumeColorAdjEnabled = volumeColorAdjustmentsEnabled;
        lastVolumePostExposure = volumePostExposure;
        lastVolumeContrast = volumeContrast;
        lastVolumeSaturation = volumeSaturation;
        lastVolumeGlobalLightOverride = volumeGlobalLightOverride;
        lastVolumeGlobalLightIntensity = volumeGlobalLightIntensity;
        PatchMapBackground();
    }

    [Tooltip("Force the auto biome apply on Start even when a GameOrchestrator is " +
             "present. Normally leave OFF: the orchestrator drives the biome per stage, " +
             "and auto-applying here too causes a redundant, race-prone double biome " +
             "build at launch. Turn ON only for standalone biome testing.")]
    public bool forceApplyBiomeOnStart = false;

    void Start()
    {
        // If an orchestrator exists it owns biome selection and will call SetBiome()
        // itself. Applying here too races that path at launch (and double-builds the
        // whole biome). Stand down unless explicitly forced for standalone testing.
        if (!forceApplyBiomeOnStart && GameOrchestrator.Instance != null)
            return;

        StartCoroutine(ApplyBiomeDeferred());
    }

    void Update()
    {
        // Don't run change detection until first ApplyBiome has completed
        if (!initialized) return;

        // Live biome switch detection — apply defaults when biome changes in inspector
        if (activeBiome != lastAppliedBiome)
        {
            ApplyBiome();
            return;
        }

        // Live night mode toggle detection
        if (enableNightMode != lastEnableNightMode)
        {
            lastEnableNightMode = enableNightMode;
            ApplyNightOverlay();
            // Balloons depend on night being active — refresh them too
            ApplyNightBalloons();
            return;
        }

        // Live night balloons toggle detection
        if (enableNightBalloons != lastEnableNightBalloons)
        {
            lastEnableNightBalloons = enableNightBalloons;
            ApplyNightBalloons();
            return;
        }

        // Live background scale/coverage adjustment
        if (Mathf.Abs(backgroundScale - lastBackgroundScale) > 0.001f ||
            Mathf.Abs(groundCoverageRadius - lastGroundCoverage) > 0.5f)
        {
            lastBackgroundScale = backgroundScale;
            lastGroundCoverage = groundCoverageRadius;
            SetupBackground();
        }

        // Live toggle detection — switch GPU/CPU grass at runtime
        if ((activeBiome == BiomeType.Grass || activeBiome == BiomeType.Marsh) && useGPUGrass != lastUseGPUGrass)
        {
            Debug.LogWarning($"[BiomeManager] Switching grass renderer: {(useGPUGrass ? "GPU" : "CPU")}");
            lastUseGPUGrass = useGPUGrass;
            ApplyBiome();
        }

        // Live toggle detection — fog on/off, density, or color changes (manual tweaks)
        if (enableFog != lastEnableFog ||
            (enableFog && Mathf.Abs(fogDensity - lastFogDensity) > 0.01f) ||
            (enableFog && (fogColor != lastFogColor ||
                           fogSmokeColor != lastFogSmokeColor ||
                           fogSmokeDarkCore != lastFogSmokeDarkCore)))
        {
            lastEnableFog = enableFog;
            lastFogDensity = fogDensity;
            lastFogColor = fogColor;
            lastFogSmokeColor = fogSmokeColor;
            lastFogSmokeDarkCore = fogSmokeDarkCore;
            ApplyFog();
        }

        // Live toggle detection — weather particles (rain/snow) //Weather
        if (enableRain != lastEnableRain || enableSnow != lastEnableSnow)
        {
            lastEnableRain = enableRain;
            lastEnableSnow = enableSnow;
            ApplyWeather();
        }

        // Live toggle detection — shadow overlay //Shadow
        if (enableShadow != lastEnableShadow)
        {
            lastEnableShadow = enableShadow;
            ApplyShadow();
        }

        // Live toggle detection — volume on/off or any parameter change //Volume
        if (enableVolume != lastEnableVolume ||
            (enableVolume && (
                Mathf.Abs(volumeWeight - lastVolumeWeight) > 0.01f ||
                volumeBloomEnabled != lastVolumeBloomEnabled ||
                Mathf.Abs(volumeBloomIntensity - lastVolumeBloomIntensity) > 0.01f ||
                Mathf.Abs(volumeBloomThreshold - lastVolumeBloomThreshold) > 0.01f ||
                Mathf.Abs(volumeBloomScatter - lastVolumeBloomScatter) > 0.01f ||
                volumeVignetteEnabled != lastVolumeVignetteEnabled ||
                Mathf.Abs(volumeVignetteIntensity - lastVolumeVignetteIntensity) > 0.01f ||
                volumeColorAdjustmentsEnabled != lastVolumeColorAdjEnabled ||
                Mathf.Abs(volumePostExposure - lastVolumePostExposure) > 0.01f ||
                Mathf.Abs(volumeContrast - lastVolumeContrast) > 0.1f ||
                Mathf.Abs(volumeSaturation - lastVolumeSaturation) > 0.1f ||
                volumeGlobalLightOverride != lastVolumeGlobalLightOverride ||
                Mathf.Abs(volumeGlobalLightIntensity - lastVolumeGlobalLightIntensity) > 0.01f
            )))
        {
            SyncVolumeTrackers();
            ApplyVolume();
        }
    }

    private System.Collections.IEnumerator ApplyBiomeDeferred()
    {
        yield return null; // wait one frame
        ApplyBiome();
    }

    public void SetBiome(BiomeType biome)
    {
        activeBiome = biome;
        ApplyBiome();
    }

    /// Switch between GPU and CPU grass at runtime.

    public void SetGrassMode(bool gpu)
    {
        useGPUGrass = gpu;
        // Update() will detect the change and reapply
    }

    /// Toggle fog on/off at runtime.
    public void SetFog(bool enabled)
    {
        enableFog = enabled;
        // Update() will detect the change and reapply
    }

    /// Toggle universal night mode on/off at runtime.
    public void SetNightMode(bool enabled)
    {
        enableNightMode = enabled;
        // Update() will detect the change and reapply
    }

    [ContextMenu("Toggle GPU/CPU Grass")]
    public void ToggleGrassMode()
    {
        useGPUGrass = !useGPUGrass;
        Debug.LogWarning($"[BiomeManager] Grass mode toggled to: {(useGPUGrass ? "GPU (GrassOverlayGPU)" : "CPU (GrassOverlay)")}");
        // Update() will detect the change and reapply
    }

    [ContextMenu("Toggle Fog")]
    public void ToggleFog()
    {
        enableFog = !enableFog;
        //Debug.LogWarning($"[BiomeManager] Fog toggled to: {(enableFog ? "ON" : "OFF")}");
    }

    [ContextMenu("Toggle Night Mode")]
    public void ToggleNightMode()
    {
        enableNightMode = !enableNightMode;
        //Debug.LogWarning($"[BiomeManager] Night mode toggled to: {(enableNightMode ? "ON" : "OFF")}");
        // Update() will detect the change and reapply
    }

    /// Toggle Global Volume on/off at runtime.
    public void SetVolume(bool enabled)
    {
        enableVolume = enabled;
        // Update() will detect the change and reapply
    }

    [ContextMenu("Toggle Volume")]
    public void ToggleVolume()
    {
        enableVolume = !enableVolume;
        //Debug.LogWarning($"[BiomeManager] Volume toggled to: {(enableVolume ? "ON" : "OFF")}");
    }

    [ContextMenu("Reapply Current Biome")]
    public void ApplyBiome()
    {
        // 0. Apply biome-specific fog defaults (if enabled)
        if (applyBiomeFogDefaults)
        {
            BiomeFogDefaults defaults = BiomeFogDefaults.ForBiome(activeBiome);
            enableFog = defaults.fogEnabled;
            fogDensity = defaults.fogDensity;
            fogColor = defaults.fogColor;
            fogSmokeColor = defaults.smokeColor;
            fogSmokeDarkCore = defaults.smokeDarkCore;
        }

        // 0b. Apply biome-specific background defaults (if enabled)
        if (applyBiomeBackgroundDefaults)
        {
            BiomeBackgroundDefaults bgDefaults = BiomeBackgroundDefaults.ForBiome(activeBiome);
            backgroundScale = bgDefaults.backgroundScale;
            lastBackgroundScale = backgroundScale;
        }

        // 0c. Apply biome-specific weather defaults (if enabled) //Weather
        if (applyBiomeWeatherDefaults)
        {
            BiomeWeatherDefaults weatherDefaults = BiomeWeatherDefaults.ForBiome(activeBiome);
            enableRain = weatherDefaults.rainEnabled;
            enableSnow = weatherDefaults.snowEnabled;
        }

        // 0d. Apply biome-specific shadow defaults (if enabled) //Shadow
        if (applyBiomeShadowDefaults)
        {
            BiomeShadowDefaults shadowDefaults = BiomeShadowDefaults.ForBiome(activeBiome);
            enableShadow = shadowDefaults.shadowEnabled;
        }

        // 0e. Apply biome-specific volume/illumination defaults (if enabled) //Volume
        if (applyBiomeVolumeDefaults)
        {
            BiomeVolumeDefaults volDefaults = BiomeVolumeDefaults.ForBiome(activeBiome);
            enableVolume = volDefaults.volumeEnabled;
            volumeWeight = volDefaults.volumeWeight;

            volumeBloomEnabled = volDefaults.bloomEnabled;
            volumeBloomIntensity = volDefaults.bloomIntensity;
            volumeBloomThreshold = volDefaults.bloomThreshold;
            volumeBloomScatter = volDefaults.bloomScatter;
            volumeBloomTint = volDefaults.bloomTint;

            volumeVignetteEnabled = volDefaults.vignetteEnabled;
            volumeVignetteIntensity = volDefaults.vignetteIntensity;
            volumeVignetteSmoothness = volDefaults.vignetteSmoothness;
            volumeVignetteRounded = volDefaults.vignetteRounded;
            volumeVignetteColor = volDefaults.vignetteColor;

            volumeColorAdjustmentsEnabled = volDefaults.colorAdjustmentsEnabled;
            volumePostExposure = volDefaults.postExposure;
            volumeContrast = volDefaults.contrast;
            volumeSaturation = volDefaults.saturation;
            volumeColorFilter = volDefaults.colorFilter;

            volumeGlobalLightOverride = volDefaults.globalLightOverride;
            volumeGlobalLightIntensity = volDefaults.globalLightIntensity;
            volumeGlobalLightColor = volDefaults.globalLightColor;
        }

        // 1. Remove previous overlays 
        RemoveOverlay<GrassOverlay>();
        RemoveOverlay<GrassOverlayGPU>();
        RemoveOverlay<SnowOverlay>();
        RemoveOverlay<DesertOverlay>();
        RemoveOverlay<WastelandOverlay>();
        RemoveOverlay<StonesOverlay>();
        RemoveOverlay<GrassCartoonOverlay>();
        RemoveOverlay<MarshWaterOverlay>();
        RemoveOverlay<MarshFootstepRipples>();
        RemoveOverlay<FogOverlay>();
        RemoveOverlay<NightOverlay>();
        RemoveOverlay<NightBalloonController>();

        // 2. Set background image + tile it
        SetupBackground();

        // 3. Patch TowerDefenseMap so its terrain matches
        PatchMapBackground();

        // 4. Spawn the correct overlay
        switch (activeBiome)
        {
            case BiomeType.Grass:
                if (useGPUGrass)
                    SetupGrassOverlayGPU();
                else
                    SetupGrassOverlayCPU();
                break;
            case BiomeType.Snow:
                SetupSnowOverlay();
                break;
            case BiomeType.Desert:
                SetupDesertOverlay();
                break;
            case BiomeType.Wasteland:
                SetupWastelandOverlay();
                break;
            case BiomeType.Stones:
                SetupStonesOverlay();
                break;
            case BiomeType.GrassCartoon:
                SetupGrassCartoonOverlay();
                break;
            case BiomeType.Marsh:
                SetupMarshOverlay();
                break;
            case BiomeType.Night:
                SetupNightBiome();
                break;
            case BiomeType.Corruption:
                // SWAPPED: Corruption now uses the old PitchBlack look.
                SetupPitchBlackBiome();
                break;
            case BiomeType.PitchBlack:
                // SWAPPED: PitchBlack now uses the old Corruption look.
                SetupCorruptionBiome();
                break;
        }

        // 5. Apply fog (or remove it if defaults turned it off)
        ApplyFog();

        // 5b. Apply weather particles (rain/snow) //Weather
        ApplyWeather();

        // 5c. Apply shadow overlay //Shadow
        ApplyShadow();

        // 5d. Apply Global Volume / illumination //Volume
        ApplyVolume();

        // 6. Apply universal night overlay if enabled (works on ANY biome, including Night)
        ApplyNightOverlay();

        // 6b. Apply night balloons (requires night overlay to be visible)
        ApplyNightBalloons();

        // 7. Generate border ring with per-biome prefabs, overlap, and spacing
        BorderRingGenerator border = GetComponent<BorderRingGenerator>();
        if (border != null)
        {
            border.prefabs = GetBorderPrefabsForBiome(activeBiome);
            border.preventOverlap = GetBorderPreventOverlap(activeBiome);
            border.spacing = GetBorderSpacing(activeBiome);
            border.GenerateBorder();
        }

        // 7b. Generate random obstacles within the playable area
        ObstacleGenerator obsGen = GetComponent<ObstacleGenerator>();
        if (obsGen == null)
            obsGen = FindFirstObjectByType<ObstacleGenerator>();
        if (obsGen != null)
        {
            // Only push per-biome prefabs and blueprints — all other settings
            // (count, scale, clustering, etc.) live on ObstacleGenerator's own inspector.
            obsGen.obstaclePrefabs = GetObstaclePrefabsForBiome(activeBiome);
            obsGen.soloPrefabs = GetObstacleSoloPrefabsForBiome(activeBiome);
            obsGen.customBlueprints = obstacleCustomBlueprints;
            obsGen.GenerateObstacles();
        }
        else
        {
            Debug.LogWarning("[BiomeManager] No ObstacleGenerator found in scene — skipping obstacle generation. " +
                             "Add an ObstacleGenerator component to any GameObject.");
        }

        // 8. Sync all trackers so Update() only reacts to *subsequent* manual changes
        lastAppliedBiome = activeBiome;
        lastUseGPUGrass = useGPUGrass;
        lastEnableFog = enableFog;
        lastEnableNightMode = enableNightMode;
        lastEnableNightBalloons = enableNightBalloons;
        lastFogDensity = fogDensity;
        lastFogColor = fogColor;
        lastFogSmokeColor = fogSmokeColor;
        lastFogSmokeDarkCore = fogSmokeDarkCore;
        lastEnableRain = enableRain;     //Weather
        lastEnableSnow = enableSnow;     //Weather
        lastEnableShadow = enableShadow; //Shadow
        SyncVolumeTrackers(); //Volume
        initialized = true;
    }

    // Night overlay management — can be toggled independently of biome (like fog)

    void ApplyNightOverlay()
    {
        // When the Corruption biome is active, SetupCorruptionBiome has already
        // installed a NightOverlay in directional mode. Do not touch it here —
        // otherwise we'd replace it with a non-directional overlay if
        // enableNightMode happens to be true.
        if (activeBiome == BiomeType.Corruption) return;

        // PitchBlack: same protection — SetupPitchBlackBiome owns the overlay.
        if (activeBiome == BiomeType.PitchBlack) return;

        RemoveOverlay<NightOverlay>();

        if (!enableNightMode) return;

        // Don't double-apply if the Night biome already set up its own NightOverlay
        // (Night biome always gets its overlay via SetupNightBiome, but universal night
        //  mode flag takes over — so we remove and re-add with the universal settings)

        NightOverlay night = gameObject.AddComponent<NightOverlay>();

        night.preset = nightPreset;

        night.darkness = nightDarkness;
        night.nightColor = nightColor;
        night.ambientLight = nightAmbientLight;
        night.playerGlowRadius = nightPlayerGlowRadius;
        night.playerGlowStrength = nightPlayerGlowStrength;

        night.torchEnabled = nightTorchEnabled;
        night.torchRange = nightTorchRange;
        night.torchHalfAngle = nightTorchHalfAngle;
        night.torchEdgeSoftness = nightTorchEdgeSoftness;
        night.torchBrightness = nightTorchBrightness;
        night.torchWarmTint = nightTorchWarmTint;
        night.flickerSpeed = nightFlickerSpeed;
        night.flickerIntensity = nightFlickerIntensity;
        night.sortingOrder = 6000;
        night.GenerateNight();
        //Debug.LogWarning($"[BiomeManager] Night mode ON — preset={nightPreset}, " +
        //                 $"torch={(nightTorchEnabled ? "ON" : "OFF")}, " +
        //                 $"biome={activeBiome}");
    }

    // Night balloon management — hot-air lanterns that drift over the map at night.
    // Only meaningful when some form of NightOverlay is active (universal night mode
    // or the dedicated Night biome); otherwise the balloons still spawn but their
    // lanterns have nothing to illuminate through.

    void ApplyNightBalloons()
    {
        RemoveOverlay<NightBalloonController>();

        // Balloons only make sense alongside an active NightOverlay.
        bool nightActive = enableNightMode
                        || activeBiome == BiomeType.Night
                        || activeBiome == BiomeType.Corruption
                        || activeBiome == BiomeType.PitchBlack;

        Debug.Log($"[BiomeManager] ApplyNightBalloons — enabled={enableNightBalloons}, " +
                  $"nightMode={enableNightMode}, biome={activeBiome}, nightActive={nightActive}");

        if (!enableNightBalloons || !nightActive)
        {
            if (enableNightBalloons && !nightActive)
                Debug.Log("[BiomeManager] Balloons requested but night is not active — skipping.");
            return;
        }

        NightBalloonController bc = gameObject.AddComponent<NightBalloonController>();

        bc.maxBalloons = nightBalloonMaxCount;
        bc.spawnInterval = nightBalloonSpawnInterval;
        bc.spawnRadius = nightBalloonSpawnRadius;
        bc.minCoreDistance = nightBalloonMinCoreDistance;
        bc.maxCoreDistance = nightBalloonMaxCoreDistance;

        bc.flightSpeed = nightBalloonFlightSpeed;
        bc.balloonScale = nightBalloonScale;

        bc.lightRadius = nightBalloonLightRadius;
        bc.lightIntensity = nightBalloonLightIntensity;
        bc.lightColor = nightBalloonLightColor;
        bc.warmTintStrength = nightBalloonWarmTintStrength;
        bc.flickerSpeed = nightBalloonFlickerSpeed;
        bc.flickerAmount = nightBalloonFlickerAmount;

        bc.enableLightSweep = nightBalloonEnableSweep;
        bc.sweepBeamLength = nightBalloonSweepBeamLength;
        bc.sweepSpeed = nightBalloonSweepSpeed;
        bc.sweepArc = nightBalloonSweepArc;
        bc.sweepBeamWidth = nightBalloonSweepBeamWidth;
        bc.sweepBeamOpacity = nightBalloonSweepBeamOpacity;
        bc.sweepGroundSpotOpacity = nightBalloonSweepGroundSpotOpacity;

        // Above ground/entities, below the NightOverlay which is at 6000
        bc.sortingOrder = 4000;

        bc.GenerateBalloons();
    }

    // Fog management — can be toggled independently of biome

    void ApplyFog()
    {
        RemoveOverlay<FogOverlay>();

        if (!enableFog) return;

        FogOverlay f = gameObject.AddComponent<FogOverlay>();

        f.fogDensity = fogDensity;
        f.fogColor = fogColor;
        f.fogColorDeep = new Color(fogColor.r * 0.75f, fogColor.g * 0.77f, fogColor.b * 0.82f, 1.0f);
        f.fogRadius = 65f;

        // Visibility reduction via deep fog field
        f.enableVisibilityReduction = true;
        f.visibilityReductionStrength = fogVisibilityReduction;
        f.visibilityColor = new Color(fogColor.r * 0.94f, fogColor.g * 0.95f, fogColor.b * 0.97f, 1.0f);
        f.deepFogCount = fogDeepCount;
        f.deepFogDensity = fogDeepDensity;

        // Rolling fog banks
        f.fogBankCount = fogBankCount;
        f.fogBankMinSize = fogBankMinSize;
        f.fogBankMaxSize = fogBankMaxSize;
        f.fogBankDriftSpeed = fogBankDriftSpeed;
        f.fogBankDensity = fogBankDensity;

        // Smoke columns — now with configurable colors
        f.smokeColumnCount = fogSmokeColumnCount;
        f.smokeMinHeight = fogSmokeMinHeight;
        f.smokeMaxHeight = fogSmokeMaxHeight;
        f.smokeMinWidth = fogSmokeMinWidth;
        f.smokeMaxWidth = fogSmokeMaxWidth;
        f.smokeSpawnRadius = fogSmokeSpawnRadius;
        f.smokeDensity = fogSmokeDensity;
        f.smokeRiseSpeed = fogSmokeRiseSpeed;
        f.smokeBillowAmount = fogSmokeBillowAmount;
        f.smokeDissipation = fogSmokeDissipation;
        f.smokeColor = fogSmokeColor;
        f.smokeDarkCore = fogSmokeDarkCore;

        // Near wisps
        f.nearWispCount = fogNearWispCount;
        f.nearWispDensity = fogNearWispDensity;
        f.nearWispDriftSpeed = fogNearWispDriftSpeed;

        // Moisture
        f.moistureCount = fogMoistureCount;

        // Sorting — must be above grass Y-sort range (sortOrderBase ± spawnRadius*precision ≈ 400–1600)
        f.sortingOrder = 5000;

        f.GenerateFog();

        //Debug.LogWarning($"[BiomeManager] Fog enabled — density {fogDensity:F2}, smoke color ({fogSmokeColor.r:F2},{fogSmokeColor.g:F2},{fogSmokeColor.b:F2}), " +
        //                 $"{fogBankCount} banks, {fogSmokeColumnCount} smoke columns");
    }

    // Weather particle management — activates/deactivates ParticleRain & ParticleSnow //Weather

    void ApplyWeather()
    {
        if (particleRain != null)
            particleRain.SetActive(enableRain);
        else if (enableRain)
            Debug.LogWarning("[BiomeManager] enableRain is ON but particleRain reference is not assigned.");
        if (particleSnow != null)
            particleSnow.SetActive(enableSnow);
        else if (enableSnow)
            Debug.LogWarning("[BiomeManager] enableSnow is ON but particleSnow reference is not assigned.");
    }

    // Shadow overlay management — instantiates/destroys shadow prefab per biome //Shadow
    void ApplyShadow()
    {
        // Destroy previous shadow instance
        if (activeShadowInstance != null)
        {
            if (Application.isPlaying) Destroy(activeShadowInstance);
            else DestroyImmediate(activeShadowInstance);
            activeShadowInstance = null;
        }

        if (!enableShadow) return;

        // Determine which prefab to use
        BiomeShadowDefaults shadowDefaults = BiomeShadowDefaults.ForBiome(activeBiome);
        int prefabIndex = shadowDefaults.shadowPrefabIndex;

        if (shadowPrefabs == null || shadowPrefabs.Length == 0)
        {
            Debug.LogWarning("[BiomeManager] enableShadow is ON but no shadow prefabs assigned.");
            return;
        }

        if (prefabIndex < 0 || prefabIndex >= shadowPrefabs.Length || shadowPrefabs[prefabIndex] == null)
        {
            Debug.LogWarning($"[BiomeManager] Shadow prefab index {prefabIndex} is invalid or null.");
            return;
        }

        activeShadowInstance = Instantiate(shadowPrefabs[prefabIndex]);
        activeShadowInstance.name = "Shadow_BiomeInstance";

        // Place shadow above all biome overlays (grass Y-sort range ~400–1600) but below fog (5000) and night (6000)
        SpriteRenderer sr = activeShadowInstance.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.sortingOrder = 4000;
    }

    // Volume / illumination management — creates a runtime Volume with VolumeProfile //Volume

    void SyncVolumeTrackers()
    {
        lastEnableVolume = enableVolume;
        lastVolumeWeight = volumeWeight;
        lastVolumeBloomEnabled = volumeBloomEnabled;
        lastVolumeBloomIntensity = volumeBloomIntensity;
        lastVolumeBloomThreshold = volumeBloomThreshold;
        lastVolumeBloomScatter = volumeBloomScatter;
        lastVolumeVignetteEnabled = volumeVignetteEnabled;
        lastVolumeVignetteIntensity = volumeVignetteIntensity;
        lastVolumeColorAdjEnabled = volumeColorAdjustmentsEnabled;
        lastVolumePostExposure = volumePostExposure;
        lastVolumeContrast = volumeContrast;
        lastVolumeSaturation = volumeSaturation;
        lastVolumeGlobalLightOverride = volumeGlobalLightOverride;
        lastVolumeGlobalLightIntensity = volumeGlobalLightIntensity;
    }

    void ApplyVolume()
    {
        // Destroy previous volume instance
        if (activeVolumeInstance != null)
        {
            if (Application.isPlaying) Destroy(activeVolumeInstance);
            else DestroyImmediate(activeVolumeInstance);
            activeVolumeInstance = null;
            activeVolumeComponent = null;
        }

        if (!enableVolume) return;

        // Create a new GameObject with a Volume component
        activeVolumeInstance = new GameObject("GlobalVolume_BiomeInstance");
        activeVolumeInstance.transform.SetParent(null);
        activeVolumeInstance.transform.position = Vector3.zero;

        activeVolumeComponent = activeVolumeInstance.AddComponent<Volume>();
        activeVolumeComponent.isGlobal = true;
        activeVolumeComponent.priority = 1; // Override the default prefab volume
        activeVolumeComponent.weight = volumeWeight;

        // Create a runtime VolumeProfile (no asset needed)
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        activeVolumeComponent.profile = profile;

        //  Bloom 
        if (volumeBloomEnabled)
        {
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.overrideState = true;
            bloom.intensity.value = volumeBloomIntensity;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = volumeBloomThreshold;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = volumeBloomScatter;
            bloom.tint.overrideState = true;
            bloom.tint.value = volumeBloomTint;
        }

        //  Vignette 
        if (volumeVignetteEnabled)
        {
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = volumeVignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = volumeVignetteSmoothness;
            vignette.rounded.overrideState = true;
            vignette.rounded.value = volumeVignetteRounded;
            vignette.color.overrideState = true;
            vignette.color.value = volumeVignetteColor;
        }

        //  Color Adjustments 
        if (volumeColorAdjustmentsEnabled)
        {
            ColorAdjustments ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.overrideState = true;
            ca.postExposure.value = volumePostExposure;
            ca.contrast.overrideState = true;
            ca.contrast.value = volumeContrast;
            ca.saturation.overrideState = true;
            ca.saturation.value = volumeSaturation;
            ca.colorFilter.overrideState = true;
            ca.colorFilter.value = volumeColorFilter;
        }

        //  Global Light 2D 
        if (volumeGlobalLightOverride)
        {
            ApplyGlobalLight2D();
        }
        else
        {
            ResetGlobalLight2D();
        }

        //Debug.LogWarning($"[BiomeManager] Volume applied — weight={volumeWeight:F2}, " +
        //                 $"bloom={(volumeBloomEnabled ? $"ON i={volumeBloomIntensity:F2}" : "OFF")}, " +
        //                 $"vignette={(volumeVignetteEnabled ? $"ON i={volumeVignetteIntensity:F2}" : "OFF")}, " +
        //                 $"colorAdj={(volumeColorAdjustmentsEnabled ? $"ON exp={volumePostExposure:F2}" : "OFF")}, " +
        //                 $"light2D={(volumeGlobalLightOverride ? $"ON i={volumeGlobalLightIntensity:F2}" : "OFF")}");
    }

    /// Finds the scene's Global Light 2D and overrides its intensity/color.
    void ApplyGlobalLight2D()
    {
        if (activeGlobalLight == null)
        {
            // Find the existing Global Light 2D in scene
            Light2D[] allLights = FindObjectsByType<Light2D>(FindObjectsSortMode.None);
            foreach (var light in allLights)
            {
                if (light.lightType == Light2D.LightType.Global)
                {
                    activeGlobalLight = light;
                    break;
                }
            }
        }

        if (activeGlobalLight != null)
        {
            activeGlobalLight.intensity = volumeGlobalLightIntensity;
            activeGlobalLight.color = volumeGlobalLightColor;
        }
        else
        {
            Debug.LogWarning("[BiomeManager] volumeGlobalLightOverride is ON but no Global Light 2D found in scene.");
        }
    }

    // Resets the Global Light 2D to default daylight values.
    void ResetGlobalLight2D()
    {
        if (activeGlobalLight != null)
        {
            activeGlobalLight.intensity = 1f;
            activeGlobalLight.color = Color.white;
        }
    }

    // Background 

    void SetupBackground()
    {
        GameObject bg = GameObject.Find(backgroundObjectName);
        if (bg == null)
        {
            Debug.LogError($"[BiomeManager] Could not find '{backgroundObjectName}'.");
            return;
        }

        // Apply background scale to the GameObject's transform
        float s = Mathf.Max(0.01f, backgroundScale);
        bg.transform.localScale = new Vector3(s, s, 1f);

        // Swap the sprite to match the biome
        SpriteRenderer sr = bg.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            string path = GetBackgroundPath(activeBiome);
            Texture2D tex = Resources.Load<Texture2D>(path);
            if (tex != null)
            {
                Sprite spr = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    Vector2.one * 0.5f, 100f);
                sr.sprite = spr;
            }
            else
            {
                Debug.LogWarning($"[BiomeManager] Background texture not found at '{path}'.");
            }
        }

        // Tile it — tiler accounts for the parent's scale automatically
        BackgroundTiler tiler = bg.GetComponent<BackgroundTiler>();
        if (tiler == null)
            tiler = bg.AddComponent<BackgroundTiler>();

        tiler.autoCalculateGrid = true;
        tiler.coverageRadius = groundCoverageRadius;
        tiler.GenerateTiles();
    }

    void PatchMapBackground()
    {
        TowerDefenseMap map = FindFirstObjectByType<TowerDefenseMap>();
        if (map == null) return;

        string biomeBackgroundPath = GetBackgroundPath(activeBiome);
        map.backgroundImagePath = biomeBackgroundPath;

        if (map.backgroundGameObject != null)
        {
            SpriteRenderer sr = map.backgroundGameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Texture2D tex = Resources.Load<Texture2D>(biomeBackgroundPath);
                if (tex != null)
                {
                    sr.sprite = Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height),
                        Vector2.one * 0.5f, 100f);
                }
            }
        }
    }

    // Overlay helpers 

    void RemoveOverlay<T>() where T : MonoBehaviour
    {
        T existing = GetComponent<T>();
        if (existing != null)
        {
            DestroyImmediate(existing);
        }
    }


    //  GRASS GPU 

    void SetupGrassOverlayGPU()
    {
        GrassOverlayGPU g = gameObject.AddComponent<GrassOverlayGPU>();

        g.bladeCount = grassBladeCount;
        // Grass must reach the same radius the background is tiled to (groundCoverageRadius)
        // or it leaves a bare border outside the grass disc. Expand to cover background + margin.
        //g.spawnRadius = Mathf.Max(grassSpawnRadius, groundCoverageRadius * 1.2f);
        g.spawnRadius = groundCoverageRadius * 1.2f;
        g.coreExclusionRadius = grassCoreExclusion;

        g.bladeHeight = grassBladeHeight;
        g.bladeWidth = grassBladeWidth;
        g.bladeCurvature = grassCurvature;
        g.tipCounterCurve = grassTipCounterCurve;
        g.widthWobble = grassWidthWobble;

        g.shortBladeRatio = grassShortBladeRatio;
        g.tallBladeRatio = grassTallBladeRatio;
        g.deadBladeRatio = grassDeadBladeRatio;

        g.clumpCount = grassClumpCount;
        g.clumpSpread = grassClumpSpread;
        g.freeScatterRatio = grassFreeScatter;
        g.clumpLeanCoherence = grassClumpLeanCoherence;

        g.colorDarkBase = grassDarkBase;
        g.colorMidBlade = grassMidBlade;
        g.colorBrightTip = grassBrightTip;
        g.colorGroundCover = grassGroundCover;
        g.colorDeadBase = grassDeadBase;
        g.colorDeadTip = grassDeadTip;

        g.windStrength = grassWindStrength;
        g.windSpeed = grassWindSpeed;
        g.gustStrength = grassGustStrength;
        g.gustScale = grassGustScale;
        g.gustSpeed = grassGustSpeed;

        g.shadowDarken = grassShadowDarken;
        g.highlightBrighten = grassHighlightBrighten;
        g.ambientOcclusion = grassAmbientOcclusion;
        g.tipHighlight = grassTipHighlight;
        g.lightAngle = grassLightAngle;
        g.subsurfaceStrength = grassSubsurfaceStrength;
        g.subsurfaceColor = grassSubsurfaceColor;
        g.windColorShift = grassWindColorShift;
        g.patchScale = grassPatchScale;
        g.patchStrength = grassPatchStrength;

        //g.sortingOrder = -1; // GPU instanced grass renders via DrawMeshInstancedIndirect (on top)
        g.sortingOrder = 5;
        g.GenerateGrass();

        //Debug.LogWarning($"[BiomeManager] GPU grass active — {grassBladeCount:N0} blades, {grassClumpCount:N0} clumps");
    }


    //  GRASS CPU 


    void SetupGrassOverlayCPU()
    {
        GrassOverlay g = gameObject.AddComponent<GrassOverlay>();

        // Use reduced counts for CPU mode
        g.bladeCount = cpuGrassBladeCount;
        // Grass must reach the same radius the background is tiled to (groundCoverageRadius)
        // or it leaves a bare border outside the grass disc. Expand to cover background + margin.
        g.spawnRadius = Mathf.Max(grassSpawnRadius, groundCoverageRadius * 1.2f);
        g.coreExclusionRadius = grassCoreExclusion;

        g.bladeHeight = grassBladeHeight;
        g.bladeWidth = grassBladeWidth;
        g.bladeCurvature = grassCurvature;
        g.tipCounterCurve = grassTipCounterCurve;
        g.widthWobble = grassWidthWobble;

        g.shortBladeRatio = grassShortBladeRatio;
        g.tallBladeRatio = grassTallBladeRatio;
        g.deadBladeRatio = grassDeadBladeRatio;

        // Use reduced clump count for CPU mode
        g.clumpCount = cpuGrassClumpCount;
        g.clumpSpread = grassClumpSpread;
        g.freeScatterRatio = grassFreeScatter;
        g.clumpLeanCoherence = grassClumpLeanCoherence;

        g.colorDarkBase = grassDarkBase;
        g.colorMidBlade = grassMidBlade;
        g.colorBrightTip = grassBrightTip;
        g.colorGroundCover = grassGroundCover;
        g.colorDeadBase = grassDeadBase;
        g.colorDeadTip = grassDeadTip;
        g.colorVariation = 0.07f;

        g.windStrength = grassWindStrength;
        g.windSpeed = grassWindSpeed;
        g.windTurbulence = 2.0f;
        g.gustStrength = grassGustStrength;
        g.gustScale = grassGustScale;
        g.gustSpeed = grassGustSpeed;

        g.shadowDarken = grassShadowDarken;
        g.highlightBrighten = grassHighlightBrighten;
        g.ambientOcclusion = grassAmbientOcclusion;
        g.tipHighlight = grassTipHighlight;
        g.lightAngle = grassLightAngle;
        g.subsurfaceStrength = grassSubsurfaceStrength;
        g.subsurfaceColor = grassSubsurfaceColor;
        g.windColorShift = grassWindColorShift;
        g.patchScale = grassPatchScale;
        g.patchStrength = grassPatchStrength;
        //g.sortingOrder = 0;   // above background (-1)
        g.sortingOrder = 5;
        g.GenerateGrass();
        //Debug.LogWarning($"[BiomeManager] CPU grass active — {cpuGrassBladeCount:N0} blades, {cpuGrassClumpCount:N0} clumps (low-end preview)");
    }

    // Snow overlay

    void SetupSnowOverlay()
    {
        SnowOverlay s = gameObject.AddComponent<SnowOverlay>();

        // Falling particles
        s.particleCount = snowParticleCount;
        s.fallSpeed = snowFallSpeed;
        s.drift = snowDrift;
        s.spawnHeight = snowSpawnHeight;
        s.spawnRadius = snowSpawnRadius;

        // Flake types
        s.powderRatio = snowPowderRatio;
        s.clumpRatio = snowClumpRatio;
        s.crystalFlakeRatio = snowCrystalFlakeRatio;

        // Ground accumulation
        s.groundCount = snowGroundCount;
        s.groundRadius = snowGroundRadius;
        s.groundCoreExclusion = snowGroundCoreExclusion;

        // Snowdrifts
        s.driftCount = snowDriftCount;
        s.driftMinLength = snowDriftMinLength;
        s.driftMaxLength = snowDriftMaxLength;
        s.driftMinWidth = snowDriftMinWidth;
        s.driftMaxWidth = snowDriftMaxWidth;
        s.driftWindAlignment = snowDriftWindAlignment;
        s.driftWindAngle = snowDriftWindAngle;
        s.driftColorBright = snowDriftColorBright;
        s.driftColorShadow = snowDriftColorShadow;

        // Ice crystal patches
        s.icePatchCount = snowIcePatchCount;
        s.icePatchMinSize = snowIcePatchMinSize;
        s.icePatchMaxSize = snowIcePatchMaxSize;
        s.iceColorBase = snowIceColorBase;
        s.iceColorGlint = snowIceColorGlint;

        // Frost vegetation
        s.frostVegCount = snowFrostVegCount;
        s.frostVegMinHeight = snowFrostVegMinHeight;
        s.frostVegMaxHeight = snowFrostVegMaxHeight;
        s.frostVegWidth = snowFrostVegWidth;
        s.frostVegBase = snowFrostVegBase;
        s.frostVegTip = snowFrostVegTip;
        s.frostVegIce = snowFrostVegIce;

        // Appearance
        s.flakeMinSize = snowFlakeMinSize;
        s.flakeMaxSize = snowFlakeMaxSize;
        s.groundPatchMinSize = snowGroundPatchMinSize;
        s.groundPatchMaxSize = snowGroundPatchMaxSize;
        s.colorBright = snowColorBright;
        s.colorMid = snowColorMid;
        s.colorShadow = snowColorShadow;
        s.groundTint = snowGroundTint;

        // Wind
        s.windStrength = snowWindStrength;
        s.windSpeed = snowWindSpeed;
        s.gustStrength = snowGustStrength;
        s.gustSpeed = snowGustSpeed;
        s.turbulence = snowTurbulence;
        s.swirl = snowSwirl;

        s.sortingOrder = -1;

        // Drive full-map coverage from the KNOWN map radius, not the camera.
        // SetupSnowOverlay runs during biome load when gameplay cameras may not be
        // live yet, so SnowOverlay's camera auto-detect would collapse back to 60 and
        // leave the central square. groundCoverageRadius is the same radius we tile the
        // background to, so the snow now fills exactly what the background fills.
        s.mapCoverageRadius = groundCoverageRadius * 1.2f;
        s.GenerateSnow();
    }

    // Desert overlay 

    void SetupDesertOverlay()
    {
        DesertOverlay d = gameObject.AddComponent<DesertOverlay>();

        d.groundElementCount = desertGroundCount;
        d.groundRadius = desertGroundRadius;
        d.groundCoreExclusion = desertGroundCoreExclusion;
        d.windAngle = desertWindAngle;

        // Sand ripples
        d.rippleCount = desertRippleCount;
        d.rippleMinLength = desertRippleMinLength;
        d.rippleMaxLength = desertRippleMaxLength;
        d.rippleWidth = desertRippleWidth;
        d.rippleWindAlignment = desertRippleWindAlignment;
        d.rippleCrest = desertRippleCrest;
        d.rippleTrough = desertRippleTrough;

        // Cracked earth
        d.crackedEarthCount = desertCrackedEarthCount;
        d.crackedEarthMinSize = desertCrackedEarthMinSize;
        d.crackedEarthMaxSize = desertCrackedEarthMaxSize;
        d.crackWidth = desertCrackWidth;
        d.crackColor = desertCrackColor;
        d.crackedSurface = desertCrackedSurface;

        // Dried scrub
        d.scrubCount = desertScrubCount;
        d.scrubMinHeight = desertScrubMinHeight;
        d.scrubMaxHeight = desertScrubMaxHeight;
        d.scrubWidth = desertScrubWidth;
        d.scrubBase = desertScrubBase;
        d.scrubTip = desertScrubTip;
        d.scrubDead = desertScrubDead;

        // Heat shimmer wisps
        d.shimmerWispCount = desertShimmerWispCount;
        d.shimmerRiseSpeed = desertShimmerRiseSpeed;
        d.shimmerWispColor = desertShimmerWispColor;

        // Dust devils
        d.dustDevilCount = desertDustDevilCount;
        d.dustDevilParticles = desertDustDevilParticles;
        d.dustDevilRadius = desertDustDevilRadius;
        d.dustDevilHeight = desertDustDevilHeight;
        d.dustDevilSpinSpeed = desertDustDevilSpinSpeed;
        d.dustDevilDriftSpeed = desertDustDevilDriftSpeed;
        d.dustDevilColor = desertDustDevilColor;

        // Saltation
        d.saltationCount = desertSaltationCount;
        d.saltationSpawnRadius = desertSaltationSpawnRadius;
        d.saltationMinHeight = desertSaltationMinHeight;
        d.saltationMaxHeight = desertSaltationMaxHeight;
        d.saltationGroundBias = desertSaltationGroundBias;
        d.saltationMinSize = desertSaltationMinSize;
        d.saltationMaxSize = desertSaltationMaxSize;

        // Dust haze
        d.dustCount = desertDustCount;
        d.dustSpawnRadius = desertDustSpawnRadius;
        d.dustMinHeight = desertDustMinHeight;
        d.dustMaxHeight = desertDustMaxHeight;
        d.dustMinSize = desertDustMinSize;
        d.dustMaxSize = desertDustMaxSize;

        // Colors
        d.sandBright = desertSandBright;
        d.sandMid = desertSandMid;
        d.sandDark = desertSandDark;
        d.sandShadow = desertSandShadow;
        d.dustColor = desertDustColor;

        // Wind
        d.windStrength = desertWindStrength;
        d.windSpeed = desertWindSpeed;
        d.gustStrength = desertGustStrength;
        d.gustSpeed = desertGustSpeed;
        d.turbulence = desertTurbulence;

        // Saltation physics
        d.bounceHeight = desertBounceHeight;
        d.bounceSpeed = desertBounceSpeed;
        d.streakDrift = desertStreakDrift;

        // Dust physics
        d.dustDrift = desertDustDrift;
        d.dustWindMult = desertDustWindMult;
        d.dustSwirl = desertDustSwirl;

        //d.sortingOrder = 0;   // above background (-1); sub-layers add +1/+2/+3 on top
        d.sortingOrder = 5;
        d.GenerateDesert();
    }

    // Wasteland overlay

    void SetupWastelandOverlay()
    {
        WastelandOverlay w = gameObject.AddComponent<WastelandOverlay>();

        w.groundElementCount = wastelandGroundCount;
        w.groundRadius = wastelandGroundRadius;
        w.groundCoreExclusion = wastelandCoreExclusion;

        // Toxic pools
        w.toxicPoolCount = wastelandToxicPoolCount;
        w.toxicPoolCenter = wastelandToxicPoolCenter;
        w.toxicPoolEdge = wastelandToxicPoolEdge;

        // Rust stains
        w.rustStainCount = wastelandRustStainCount;
        w.rustColorDark = wastelandRustColorDark;
        w.rustColorLight = wastelandRustColorLight;

        // Bone/debris
        w.boneCount = wastelandBoneCount;
        w.boneColor = wastelandBoneColor;
        w.boneTip = wastelandBoneTip;

        // Dust storm
        w.dustStormCount = wastelandDustStormCount;
        w.dustStormSpawnRadius = wastelandDustStormSpawnRadius;

        // Ash flecks
        w.ashCount = wastelandAshCount;
        w.ashSpawnRadius = wastelandAshSpawnRadius;

        // Embers
        w.emberCount = wastelandEmberCount;
        w.emberSpawnRadius = wastelandEmberSpawnRadius;
        w.emberColorHot = wastelandEmberHot;
        w.emberColorDim = wastelandEmberDim;
        w.emberFlickerSpeed = wastelandEmberFlickerSpeed;

        // Smoke wisps
        w.smokeWispCount = wastelandSmokeWispCount;
        w.smokeRiseSpeed = wastelandSmokeRiseSpeed;
        w.smokeColor = wastelandSmokeColor;

        // Haze
        w.hazeCount = wastelandHazeCount;
        w.hazeSpawnRadius = wastelandHazeSpawnRadius;

        // Wind
        w.windStrength = wastelandWindStrength;
        w.windAngle = wastelandWindAngle;

        //w.sortingOrder = 0;   // above background (-1); sub-layers add offsets on top
        w.sortingOrder = 5;
        w.GenerateWasteland();
    }

    // Stones overlay

    void SetupStonesOverlay()
    {
        StonesOverlay st = gameObject.AddComponent<StonesOverlay>();

        st.groundElementCount = stonesGroundCount;
        st.groundRadius = stonesGroundRadius;
        st.groundCoreExclusion = stonesCoreExclusion;
        st.dustMoteCount = stonesDustMoteCount;
        st.dustMoteSpawnRadius = stonesDustMoteSpawnRadius;
        st.windStrength = stonesWindStrength;
        st.windAngle = stonesWindAngle;
        //st.sortingOrder = 0;   // above background (-1); sub-layers add offsets on top
        st.sortingOrder = 5;
        st.GenerateStones();
        //Debug.LogWarning($"[BiomeManager] Stones biome active — {stonesGroundCount:N0} ground elements, {stonesDustMoteCount:N0} dust motes");
    }

    // GrassCartoon overlay

    void SetupGrassCartoonOverlay()
    {
        // Collect all non-null prefab slots into an array
        GameObject[] allSlots = new GameObject[]
        {
            grassCartoonPrefab1, grassCartoonPrefab2,
            grassCartoonPrefab3, grassCartoonPrefab4,
            grassCartoonPrefab5, grassCartoonPrefab6,
            grassCartoonPrefab7, grassCartoonPrefab8
        };

        int validCount = 0;
        foreach (var p in allSlots)
            if (p != null) validCount++;

        if (validCount == 0)
        {
            Debug.LogError("[BiomeManager] GrassCartoon biome selected but NO prefabs assigned! " +
                           "Drag grass prefabs into the BiomeManager inspector " +
                           "under 'GrassCartoon — Prefab Slots'.");
            return;
        }

        GrassCartoonOverlay gc = gameObject.AddComponent<GrassCartoonOverlay>();

        gc.instanceCount = grassCartoonInstanceCount;
        gc.spawnRadius = grassCartoonSpawnRadius;
        gc.coreExclusionRadius = grassCartoonCoreExclusion;

        gc.prefabs = allSlots;

        gc.baseScale = grassCartoonBaseScale;
        gc.scaleVariation = grassCartoonScaleVariation;

        // Y-sort: bake sortingOrder from Y position at spawn time.
        // sortOrderBase keeps all values above the background (sortingOrder -1).
        // Must match PlayerMovement.sortPrecision and sortOrderBase.
        gc.sortPrecision = 10f;
        gc.sortOrderBase = 1000;

        gc.GenerateCartoonGrass();
    }

    // Marsh overlay — water puddles UNDER grass

    void SetupMarshOverlay()
    {
        // Spawn water puddles (rendered above background, below grass)
        MarshWaterOverlay mw = gameObject.AddComponent<MarshWaterOverlay>();
        // Distribution
        mw.puddleCount = marshPuddleCount;
        mw.spawnRadius = marshSpawnRadius;
        mw.coreExclusionRadius = marshCoreExclusion;
        mw.clusterCount = marshClusterCount;
        mw.clusterSpread = marshClusterSpread;
        mw.freeScatterRatio = marshFreeScatter;
        // Puddle size
        mw.puddleMinRadius = marshPuddleMinRadius;
        mw.puddleMaxRadius = marshPuddleMaxRadius;
        mw.mediumPuddleCount = marshMediumPuddleCount;
        mw.mediumMinRadius = marshMediumMinRadius;
        mw.mediumMaxRadius = marshMediumMaxRadius;
        mw.wetlandChainCount = marshWetlandChainCount;
        mw.wetlandMinLobes = marshWetlandMinLobes;
        mw.wetlandMaxLobes = marshWetlandMaxLobes;
        mw.wetlandLobeMinRadius = marshWetlandLobeMinRadius;
        mw.wetlandLobeMaxRadius = marshWetlandLobeMaxRadius;
        mw.wetlandLobeSpacing = marshWetlandLobeSpacing;
        // Puddle shape
        mw.puddleSegments = marshPuddleSegments;
        mw.shapeDistortion = marshShapeDistortion;
        mw.maxElongation = marshMaxElongation;
        mw.concavityChance = marshConcavityChance;
        mw.concavityDepth = marshConcavityDepth;
        mw.shoreBandWidth = marshShoreBandWidth;
        // Water colours (4-ring gradient)
        mw.waterShallow = marshWaterShallow;
        mw.waterMid = marshWaterMid;
        mw.waterDeep = marshWaterDeep;
        mw.waterEdge = marshWaterEdge;
        mw.reflectionColor = marshReflectionColor;
        mw.specularHighlight = marshSpecularHighlight;
        // Mud / shore / foam
        mw.mudDark = marshMudDark;
        mw.mudLight = marshMudLight;
        mw.wetGround = marshWetGround;
        mw.foamColor = marshFoamColor;
        mw.foamWidth = marshFoamWidth;
        // Animation
        mw.edgeWobbleStrength = marshEdgeWobbleStrength;
        mw.edgeWobbleSpeed = marshEdgeWobbleSpeed;
        mw.colorShimmerStrength = marshColorShimmerStrength;
        mw.colorShimmerSpeed = marshColorShimmerSpeed;
        mw.breatheStrength = marshBreatheStrength;
        mw.breatheSpeed = marshBreatheSpeed;
        mw.waveStrength = marshWaveStrength;
        mw.waveSpeed = marshWaveSpeed;
        mw.waveScale = marshWaveScale;
        // Ripples
        mw.ripplesPerPuddle = marshRipplesPerPuddle;
        mw.rippleSpeed = marshRippleSpeed;
        mw.rippleColor = marshRippleColor;
        // Insect dimples
        mw.dimpleSlots = marshDimpleSlots;
        mw.dimpleInterval = marshDimpleInterval;
        // Caustics
        mw.causticCount = marshCausticCount;
        mw.causticColor = marshCausticColor;
        mw.causticDriftSpeed = marshCausticDriftSpeed;
        // Sediment
        mw.sedimentCount = marshSedimentCount;
        // Surface film
        mw.filmPatchCount = marshFilmPatchCount;
        // Lily pads
        mw.lilyPadCount = marshLilyPadCount;
        mw.lilyPadColor = marshLilyPadColor;
        mw.lilyPadDark = marshLilyPadDark;

        // Reeds
        mw.reedCount = marshReedCount;
        mw.reedMinHeight = marshReedMinHeight;
        mw.reedMaxHeight = marshReedMaxHeight;
        mw.reedBase = marshReedBase;
        mw.reedTip = marshReedTip;

        mw.sortingOrder = 1; // Above background (0), below game elements
        mw.GenerateWater();

        // Footstep ripples (player/enemy movement creates ripples on water)
        MarshFootstepRipples mfr = gameObject.AddComponent<MarshFootstepRipples>();
        mfr.sortingOrder = mw.sortingOrder + 12; // above all water sub-layers (reeds = +10)
        mfr.Init(mw);
        // Then spawn grass ON TOP with darker/wetter tint
        if (useGPUGrass)
            SetupMarshGrassGPU();
        else
            SetupMarshGrassCPU();
        //Debug.LogWarning($"[BiomeManager] Marsh biome active — {marshWetlandChainCount} wetland chains + {marshMediumPuddleCount} medium + {marshPuddleCount} small puddles, " +
        //                 $"grass mode={(useGPUGrass ? "GPU" : "CPU")}");
    }

    void SetupMarshGrassGPU()
    {
        GrassOverlayGPU g = gameObject.AddComponent<GrassOverlayGPU>();

        // Same distribution as Grass biome
        g.bladeCount = grassBladeCount;
        // Grass must reach the same radius the background is tiled to (groundCoverageRadius)
        // or it leaves a bare border outside the grass disc. Expand to cover background + margin.
        g.spawnRadius = Mathf.Max(grassSpawnRadius, groundCoverageRadius * 1.2f);
        g.coreExclusionRadius = grassCoreExclusion;

        g.bladeHeight = grassBladeHeight;
        g.bladeWidth = grassBladeWidth;
        g.bladeCurvature = grassCurvature;
        g.tipCounterCurve = grassTipCounterCurve;
        g.widthWobble = grassWidthWobble;

        g.shortBladeRatio = grassShortBladeRatio;
        g.tallBladeRatio = grassTallBladeRatio;
        g.deadBladeRatio = grassDeadBladeRatio;

        g.clumpCount = grassClumpCount;
        g.clumpSpread = grassClumpSpread;
        g.freeScatterRatio = grassFreeScatter;
        g.clumpLeanCoherence = grassClumpLeanCoherence;

        // Marsh-specific darker/wetter grass colors
        g.colorDarkBase = marshGrassDarkBase;
        g.colorMidBlade = marshGrassMidBlade;
        g.colorBrightTip = marshGrassBrightTip;
        g.colorGroundCover = marshGrassGroundCover;
        g.colorDeadBase = grassDeadBase;
        g.colorDeadTip = grassDeadTip;

        g.windStrength = grassWindStrength;
        g.windSpeed = grassWindSpeed;
        g.gustStrength = grassGustStrength;
        g.gustScale = grassGustScale;
        g.gustSpeed = grassGustSpeed;

        g.shadowDarken = grassShadowDarken;
        g.highlightBrighten = grassHighlightBrighten;
        g.ambientOcclusion = grassAmbientOcclusion;
        g.tipHighlight = grassTipHighlight;
        g.lightAngle = grassLightAngle;
        g.subsurfaceStrength = grassSubsurfaceStrength;
        g.subsurfaceColor = grassSubsurfaceColor;
        g.windColorShift = grassWindColorShift;
        g.patchScale = grassPatchScale;
        g.patchStrength = grassPatchStrength;

        g.sortingOrder = -1; // GPU instanced grass renders via DrawMeshInstancedIndirect (on top)
        g.GenerateGrass();
    }

    void SetupMarshGrassCPU()
    {
        GrassOverlay g = gameObject.AddComponent<GrassOverlay>();

        g.bladeCount = cpuGrassBladeCount;
        // Grass must reach the same radius the background is tiled to (groundCoverageRadius)
        // or it leaves a bare border outside the grass disc. Expand to cover background + margin.
        g.spawnRadius = Mathf.Max(grassSpawnRadius, groundCoverageRadius * 1.2f);
        g.coreExclusionRadius = grassCoreExclusion;

        g.bladeHeight = grassBladeHeight;
        g.bladeWidth = grassBladeWidth;
        g.bladeCurvature = grassCurvature;
        g.tipCounterCurve = grassTipCounterCurve;
        g.widthWobble = grassWidthWobble;

        g.shortBladeRatio = grassShortBladeRatio;
        g.tallBladeRatio = grassTallBladeRatio;
        g.deadBladeRatio = grassDeadBladeRatio;

        g.clumpCount = cpuGrassClumpCount;
        g.clumpSpread = grassClumpSpread;
        g.freeScatterRatio = grassFreeScatter;
        g.clumpLeanCoherence = grassClumpLeanCoherence;

        // Marsh-specific darker/wetter grass colors
        g.colorDarkBase = marshGrassDarkBase;
        g.colorMidBlade = marshGrassMidBlade;
        g.colorBrightTip = marshGrassBrightTip;
        g.colorGroundCover = marshGrassGroundCover;
        g.colorDeadBase = grassDeadBase;
        g.colorDeadTip = grassDeadTip;
        g.colorVariation = 0.07f;

        g.windStrength = grassWindStrength;
        g.windSpeed = grassWindSpeed;
        g.windTurbulence = 2.0f;
        g.gustStrength = grassGustStrength;
        g.gustScale = grassGustScale;
        g.gustSpeed = grassGustSpeed;

        g.shadowDarken = grassShadowDarken;
        g.highlightBrighten = grassHighlightBrighten;
        g.ambientOcclusion = grassAmbientOcclusion;
        g.tipHighlight = grassTipHighlight;
        g.lightAngle = grassLightAngle;
        g.subsurfaceStrength = grassSubsurfaceStrength;
        g.subsurfaceColor = grassSubsurfaceColor;
        g.windColorShift = grassWindColorShift;
        g.patchScale = grassPatchScale;
        g.patchStrength = grassPatchStrength;

        g.sortingOrder = 0;   // above background (-1)
        g.GenerateGrass();
    }

    // Night biome (legacy) — GrassCartoon base + darkness + directional torch
    // This is the dedicated Night biome entry. Universal night mode (enableNightMode)
    // adds the darkness overlay on top of ANY biome and is handled separately in ApplyNightOverlay().

    void SetupNightBiome()
    {
        // 1. Spawn the same GrassCartoon prefab grass underneath
        GameObject[] allSlots = new GameObject[]
        {
            grassCartoonPrefab1, grassCartoonPrefab2,
            grassCartoonPrefab3, grassCartoonPrefab4,
            grassCartoonPrefab5, grassCartoonPrefab6,
            grassCartoonPrefab7, grassCartoonPrefab8
        };

        int validCount = 0;
        foreach (var p in allSlots)
            if (p != null) validCount++;

        if (validCount == 0)
        {
            Debug.LogWarning("[BiomeManager] Night biome: no GrassCartoon prefabs assigned — " +
                             "skipping grass layer. Drag prefabs into 'GrassCartoon — Prefab Slots'.");
        }
        else
        {
            GrassCartoonOverlay gc = gameObject.AddComponent<GrassCartoonOverlay>();

            gc.instanceCount = nightGrassInstanceCount;
            gc.spawnRadius = nightGrassSpawnRadius;
            gc.coreExclusionRadius = nightGrassCoreExclusion;

            gc.prefabs = allSlots;

            gc.baseScale = nightGrassBaseScale;
            gc.scaleVariation = nightGrassScaleVariation;

            gc.sortPrecision = 10f;
            gc.sortOrderBase = 1000;

            gc.GenerateCartoonGrass();
        }

        // 2. If universal night mode is NOT enabled, spawn the night overlay here
        //    (if enableNightMode is ON, ApplyNightOverlay() will handle it after this method)
        if (!enableNightMode)
        {
            NightOverlay night = gameObject.AddComponent<NightOverlay>();

            night.preset = nightPreset;
            night.darkness = nightDarkness;
            night.nightColor = nightColor;
            night.ambientLight = nightAmbientLight;
            night.playerGlowRadius = nightPlayerGlowRadius;
            night.playerGlowStrength = nightPlayerGlowStrength;

            night.torchEnabled = nightTorchEnabled;
            night.torchRange = nightTorchRange;
            night.torchHalfAngle = nightTorchHalfAngle;
            night.torchEdgeSoftness = nightTorchEdgeSoftness;
            night.torchBrightness = nightTorchBrightness;
            night.torchWarmTint = nightTorchWarmTint;
            night.flickerSpeed = nightFlickerSpeed;
            night.flickerIntensity = nightFlickerIntensity;

            night.sortingOrder = 6000;
            night.GenerateNight();
        }

        //Debug.LogWarning($"[BiomeManager] Night biome active — preset={nightPreset}, " +
        //                 $"torch={(nightTorchEnabled ? "ON" : "OFF")}, " +
        //                 $"grass instances={nightGrassInstanceCount:N0}");
    }

    // Corruption biome — same GrassCartoon base + NightOverlay as Night,
    // but the NightOverlay runs in directional-darkness mode so the player
    // is shrouded in pitch-black behind them and only sees forward.
    void SetupCorruptionBiome()
    {
        // 1. Spawn the same GrassCartoon prefab grass underneath (same as Night)
        GameObject[] allSlots = new GameObject[]
        {
            grassCartoonPrefab1, grassCartoonPrefab2,
            grassCartoonPrefab3, grassCartoonPrefab4,
            grassCartoonPrefab5, grassCartoonPrefab6,
            grassCartoonPrefab7, grassCartoonPrefab8
        };

        int validCount = 0;
        foreach (var p in allSlots)
            if (p != null) validCount++;

        if (validCount == 0)
        {
            Debug.LogWarning("[BiomeManager] Corruption biome: no GrassCartoon prefabs assigned — " +
                             "skipping grass layer. Drag prefabs into 'GrassCartoon — Prefab Slots'.");
        }
        else
        {
            GrassCartoonOverlay gc = gameObject.AddComponent<GrassCartoonOverlay>();

            gc.instanceCount = nightGrassInstanceCount;
            gc.spawnRadius = nightGrassSpawnRadius;
            gc.coreExclusionRadius = nightGrassCoreExclusion;

            gc.prefabs = allSlots;

            gc.baseScale = nightGrassBaseScale;
            gc.scaleVariation = nightGrassScaleVariation;

            gc.sortPrecision = 10f;
            gc.sortOrderBase = 1000;

            gc.GenerateCartoonGrass();
        }

        // 2Spawn the night overlay in DIRECTIONAL mode.
        //    Unlike SetupNightBiome we always take ownership of the overlay here
        //    (regardless of enableNightMode). ApplyNightOverlay() is guarded
        //    against the Corruption biome so it won't replace this one.
        RemoveOverlay<NightOverlay>();

        NightOverlay night = gameObject.AddComponent<NightOverlay>();

        night.preset = nightPreset;
        night.darkness = nightDarkness;
        night.nightColor = nightColor;
        night.ambientLight = nightAmbientLight;
        night.playerGlowRadius = nightPlayerGlowRadius;
        night.playerGlowStrength = nightPlayerGlowStrength;

        night.torchEnabled = nightTorchEnabled;
        night.torchRange = nightTorchRange;
        night.torchHalfAngle = nightTorchHalfAngle;
        night.torchEdgeSoftness = nightTorchEdgeSoftness;
        night.torchBrightness = nightTorchBrightness;
        night.torchWarmTint = nightTorchWarmTint;
        night.flickerSpeed = nightFlickerSpeed;
        night.flickerIntensity = nightFlickerIntensity;

        // --- Corruption-specific overrides ---
        night.directionalMode = true;
        night.frontConeHalfAngle = 80f;   // wide peripheral arc (~human FOV)
        night.frontConeRange = 5f;        // shorter than torchRange so torch stays brighter
        night.frontConeDimming = 0.25f;   // enemies are vague shapes, not gameplay-clear
        night.feetGlowRadius = 0.7f;      // tiny always-on bubble at player's feet
        night.feetGlowStrength = 0.35f;

        night.sortingOrder = 6000;
        night.GenerateNight();
    }

    // PitchBlack biome — only the torch cone is visible. 
    void SetupPitchBlackBiome()
    {
        // 1. Spawn the same GrassCartoon prefab grass underneath (same as Night)
        GameObject[] allSlots = new GameObject[]
        {
            grassCartoonPrefab1, grassCartoonPrefab2,
            grassCartoonPrefab3, grassCartoonPrefab4,
            grassCartoonPrefab5, grassCartoonPrefab6,
            grassCartoonPrefab7, grassCartoonPrefab8
        };

        int validCount = 0;
        foreach (var p in allSlots)
            if (p != null) validCount++;

        if (validCount == 0)
        {
            Debug.LogWarning("[BiomeManager] PitchBlack biome: no GrassCartoon prefabs assigned — " +
                             "skipping grass layer. Drag prefabs into 'GrassCartoon — Prefab Slots'.");
        }
        else
        {
            GrassCartoonOverlay gc = gameObject.AddComponent<GrassCartoonOverlay>();

            gc.instanceCount = nightGrassInstanceCount;
            gc.spawnRadius = nightGrassSpawnRadius;
            gc.coreExclusionRadius = nightGrassCoreExclusion;

            gc.prefabs = allSlots;

            gc.baseScale = nightGrassBaseScale;
            gc.scaleVariation = nightGrassScaleVariation;

            gc.sortPrecision = 10f;
            gc.sortOrderBase = 1000;

            gc.GenerateCartoonGrass();
        }

        // Spawn the night overlay configured for "only the cone is visible".

        RemoveOverlay<NightOverlay>();

        NightOverlay night = gameObject.AddComponent<NightOverlay>();

        // Start from the Custom preset so ApplyPreset doesn't overwrite the
        // hard-coded values on the first frame. (The other presets would
        // restore playerGlowStrength = 0.08+ etc., breaking the effect.)
        night.preset = NightOverlay.NightPreset.Custom;

        //  Only the cone is visible

        night.darkness = 1.0f;            // maximum darkness alpha outside the cone
        night.ambientLight = 0.0f;        // no minimum visibility — pure black elsewhere
        night.playerGlowRadius = 1.0f;    // ~1 world unit ≈ player sprite radius
        night.playerGlowStrength = 0.45f; // soft, just enough to wrap the sprite
        night.nightColor = new Color(0.0f, 0.0f, 0.0f, 1f);

        // Directional mode OFF — we want a pure cone, not a feet bubble +
        // wide peripheral cone (that's Corruption's job).
        night.directionalMode = false;

        // Torch: wider, longer, brighter than Night 
        night.torchEnabled = nightTorchEnabled;
        night.torchHalfAngle = 44f;       // 2x wider than Night's default 22°
        night.torchRange = 30f;           // long reach — quadratic falloff means visible length ≈ half this
        night.torchEdgeSoftness = 0.35f;  // soft edges so the cone fades smoothly
        night.torchBrightness = 1.0f;     // peak brightness — the cone IS the gameplay
        night.torchWarmTint = nightTorchWarmTint;
        night.flickerSpeed = nightFlickerSpeed;
        night.flickerIntensity = nightFlickerIntensity;

        night.sortingOrder = 6000;
        night.GenerateNight();
    }
}


