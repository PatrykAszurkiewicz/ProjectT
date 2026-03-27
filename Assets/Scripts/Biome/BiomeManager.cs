using UnityEngine;


public class BiomeManager : MonoBehaviour
{

    [Tooltip("Scales the background ground texture. Larger = each tile covers more area (ground looks zoomed out). Smaller = tiles are smaller (ground looks zoomed in). Does NOT affect camera or game objects.")]
    public float backgroundScale = 2.2f;

    [Tooltip("How far the ground tiles extend from the center (in world units). Increase if you see tile edges when zoomed out.")]
    public float groundCoverageRadius = 80f;

    [Header("Active Biome")]
    public BiomeType activeBiome = BiomeType.Grass;

    [Header("Fog Effect")]
    [Tooltip("Enable/disable fog overlay on top of any biome")]
    public bool enableFog = false;

    [Tooltip("When true, switching biomes auto-applies that biome's default fog on/off, colors, and smoke colors. " +
             "You can still override manually in the editor afterwards.")]
    public bool applyBiomeFogDefaults = true;

    [Tooltip("When true, switching biomes auto-applies that biome's default background scale. " +
             "You can still override manually in the editor afterwards.")]
    public bool applyBiomeBackgroundDefaults = true;

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

    // Marsh settings — water puddles under grass
    [Header("Marsh — Puddle Distribution")]
    public int marshPuddleCount = 900;
    public float marshSpawnRadius = 60f;
    public float marshCoreExclusion = 1.5f;
    public int marshClusterCount = 140;
    public float marshClusterSpread = 4.0f;
    [Range(0f, 1f)] public float marshFreeScatter = 0.12f;

    [Header("Marsh — Puddle Size")]
    public float marshPuddleMinRadius = 0.25f;
    public float marshPuddleMaxRadius = 1.6f;
    public int marshLargePuddleCount = 40;
    public float marshLargePuddleMinRadius = 2.0f;
    public float marshLargePuddleMaxRadius = 5.0f;

    [Header("Marsh — Puddle Shape")]
    [Range(8, 32)] public int marshPuddleSegments = 24;
    [Range(0f, 0.6f)] public float marshShapeDistortion = 0.35f;
    [Range(1f, 3f)] public float marshMaxElongation = 2.0f;
    [Range(0f, 0.4f)] public float marshConcavityChance = 0.25f;
    [Range(0f, 0.5f)] public float marshConcavityDepth = 0.35f;
    [Range(0.05f, 0.5f)] public float marshShoreBandWidth = 0.25f;

    [Header("Marsh — Water Colors")]
    public Color marshWaterShallow = new Color(0.10f, 0.22f, 0.20f, 0.82f);
    public Color marshWaterDeep = new Color(0.04f, 0.10f, 0.14f, 0.92f);
    public Color marshWaterEdge = new Color(0.08f, 0.18f, 0.14f, 0.50f);
    public Color marshReflectionColor = new Color(0.45f, 0.58f, 0.68f, 0.40f);
    public Color marshSpecularHighlight = new Color(0.85f, 0.92f, 1.00f, 0.55f);

    [Header("Marsh — Mud/Shore")]
    public Color marshMudDark = new Color(0.08f, 0.06f, 0.03f, 0.80f);
    public Color marshMudLight = new Color(0.16f, 0.13f, 0.07f, 0.60f);
    public Color marshWetGround = new Color(0.04f, 0.12f, 0.06f, 0.55f);

    [Header("Marsh — Water Animation")]
    public float marshEdgeWobbleStrength = 0.025f;
    public float marshEdgeWobbleSpeed = 1.5f;
    [Range(0f, 0.15f)] public float marshColorShimmerStrength = 0.06f;
    public float marshColorShimmerSpeed = 0.8f;
    [Range(0f, 0.15f)] public float marshBreatheStrength = 0.06f;
    public float marshBreatheSpeed = 0.5f;

    [Header("Marsh — Ripples")]
    public int marshRipplesPerPuddle = 3;
    public float marshRippleSpeed = 1.2f;
    public Color marshRippleColor = new Color(0.50f, 0.65f, 0.72f, 0.45f);

    [Header("Marsh — Caustics")]
    public int marshCausticCount = 2000;
    public Color marshCausticColor = new Color(0.60f, 0.78f, 0.65f, 0.30f);
    public float marshCausticDriftSpeed = 0.25f;

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
    private bool lastUseGPUGrass = true;
    private bool lastEnableFog = false;
    private float lastFogDensity = -1f;
    private Color lastFogColor;
    private Color lastFogSmokeColor;
    private Color lastFogSmokeDarkCore;
    private float lastBackgroundScale = -1f;
    private float lastGroundCoverage = -1f;
    private bool initialized = false;

    // Map background paths per biome 
    private string GetBackgroundPath(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass: return "Backgrounds/Background8";
            case BiomeType.Snow: return "Backgrounds/Background9";
            case BiomeType.Desert: return "Backgrounds/Background10";
            case BiomeType.Wasteland: return "Backgrounds/Background11";
            case BiomeType.Stones: return "Backgrounds/Background12";
            //case BiomeType.GrassCartoon: return "Backgrounds/BackgroundGrassDark";
            case BiomeType.GrassCartoon: return "Backgrounds/Background13";
            case BiomeType.Marsh: return "Backgrounds/Background8"; // Same grass background
            default: return "Backgrounds/Background8";
        }
    }

    void Awake()
    {
        lastUseGPUGrass = useGPUGrass;
        lastBackgroundScale = backgroundScale;
        lastGroundCoverage = groundCoverageRadius;
        PatchMapBackground();
    }

    void Start()
    {
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
        Debug.LogWarning($"[BiomeManager] Fog toggled to: {(enableFog ? "ON" : "OFF")}");
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
            Debug.Log($"[BiomeManager] Applied fog defaults for {activeBiome}: fog={enableFog}, density={fogDensity:F2}, " +
                      $"fogColor=({fogColor.r:F2},{fogColor.g:F2},{fogColor.b:F2}), " +
                      $"smokeColor=({fogSmokeColor.r:F2},{fogSmokeColor.g:F2},{fogSmokeColor.b:F2})");
        }

        // 0b. Apply biome-specific background defaults (if enabled)
        if (applyBiomeBackgroundDefaults)
        {
            BiomeBackgroundDefaults bgDefaults = BiomeBackgroundDefaults.ForBiome(activeBiome);
            backgroundScale = bgDefaults.backgroundScale;
            lastBackgroundScale = backgroundScale;
            Debug.Log($"[BiomeManager] Applied background defaults for {activeBiome}: scale={backgroundScale:F2}");
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
        RemoveOverlay<FogOverlay>();

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
        }

        // 5. Apply fog (or remove it if defaults turned it off)
        ApplyFog();

        // 6. Sync all trackers so Update() only reacts to *subsequent* manual changes
        lastAppliedBiome = activeBiome;
        lastUseGPUGrass = useGPUGrass;
        lastEnableFog = enableFog;
        lastFogDensity = fogDensity;
        lastFogColor = fogColor;
        lastFogSmokeColor = fogSmokeColor;
        lastFogSmokeDarkCore = fogSmokeDarkCore;
        initialized = true;
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

        Debug.LogWarning($"[BiomeManager] Fog enabled — density {fogDensity:F2}, smoke color ({fogSmokeColor.r:F2},{fogSmokeColor.g:F2},{fogSmokeColor.b:F2}), " +
                         $"{fogBankCount} banks, {fogSmokeColumnCount} smoke columns");
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
        g.spawnRadius = grassSpawnRadius;
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

        g.sortingOrder = -1;
        g.GenerateGrass();

        Debug.LogWarning($"[BiomeManager] GPU grass active — {grassBladeCount:N0} blades, {grassClumpCount:N0} clumps");
    }


    //  GRASS CPU 


    void SetupGrassOverlayCPU()
    {
        GrassOverlay g = gameObject.AddComponent<GrassOverlay>();

        // Use reduced counts for CPU mode
        g.bladeCount = cpuGrassBladeCount;
        g.spawnRadius = grassSpawnRadius;
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

        g.sortingOrder = -1;
        g.GenerateGrass();

        Debug.LogWarning($"[BiomeManager] CPU grass active — {cpuGrassBladeCount:N0} blades, {cpuGrassClumpCount:N0} clumps (low-end preview)");
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

        d.sortingOrder = -1;
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

        w.sortingOrder = -1;
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

        st.sortingOrder = -1;
        st.GenerateStones();

        Debug.LogWarning($"[BiomeManager] Stones biome active — {stonesGroundCount:N0} ground elements, {stonesDustMoteCount:N0} dust motes");
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

        //Debug.LogWarning($"[BiomeManager] GrassCartoon biome active — {grassCartoonInstanceCount:N0} instances from {validCount} prefab(s), radius={grassCartoonSpawnRadius}");
    }

    // Marsh overlay — water puddles UNDER grass

    void SetupMarshOverlay()
    {
        // 1. First spawn water puddles (rendered above background, below grass)
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
        mw.largePuddleCount = marshLargePuddleCount;
        mw.largePuddleMinRadius = marshLargePuddleMinRadius;
        mw.largePuddleMaxRadius = marshLargePuddleMaxRadius;

        // Puddle shape
        mw.puddleSegments = marshPuddleSegments;
        mw.shapeDistortion = marshShapeDistortion;
        mw.maxElongation = marshMaxElongation;
        mw.concavityChance = marshConcavityChance;
        mw.concavityDepth = marshConcavityDepth;
        mw.shoreBandWidth = marshShoreBandWidth;

        // Water colours
        mw.waterShallow = marshWaterShallow;
        mw.waterDeep = marshWaterDeep;
        mw.waterEdge = marshWaterEdge;
        mw.reflectionColor = marshReflectionColor;
        mw.specularHighlight = marshSpecularHighlight;

        // Mud / shore
        mw.mudDark = marshMudDark;
        mw.mudLight = marshMudLight;
        mw.wetGround = marshWetGround;

        // Animation
        mw.edgeWobbleStrength = marshEdgeWobbleStrength;
        mw.edgeWobbleSpeed = marshEdgeWobbleSpeed;
        mw.colorShimmerStrength = marshColorShimmerStrength;
        mw.colorShimmerSpeed = marshColorShimmerSpeed;
        mw.breatheStrength = marshBreatheStrength;
        mw.breatheSpeed = marshBreatheSpeed;

        // Ripples
        mw.ripplesPerPuddle = marshRipplesPerPuddle;
        mw.rippleSpeed = marshRippleSpeed;
        mw.rippleColor = marshRippleColor;

        // Caustics
        mw.causticCount = marshCausticCount;
        mw.causticColor = marshCausticColor;
        mw.causticDriftSpeed = marshCausticDriftSpeed;

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

        // 2. Then spawn grass ON TOP with darker/wetter tint
        if (useGPUGrass)
            SetupMarshGrassGPU();
        else
            SetupMarshGrassCPU();

        Debug.LogWarning($"[BiomeManager] Marsh biome active — {marshPuddleCount} puddles + {marshLargePuddleCount} ponds, " +
                         $"grass mode={(useGPUGrass ? "GPU" : "CPU")}");
    }

    void SetupMarshGrassGPU()
    {
        GrassOverlayGPU g = gameObject.AddComponent<GrassOverlayGPU>();

        // Same distribution as Grass biome
        g.bladeCount = grassBladeCount;
        g.spawnRadius = grassSpawnRadius;
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
        g.spawnRadius = grassSpawnRadius;
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

        g.sortingOrder = -1;
        g.GenerateGrass();
    }
}

