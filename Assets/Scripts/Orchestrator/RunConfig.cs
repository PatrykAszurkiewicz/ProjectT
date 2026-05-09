using System.Collections.Generic;
using UnityEngine;

// TOP-LEVEL RUN BLUEPRINT e.g. "Play 4 stages, each with 8 waves + a boss, then a final boss."
/// RunConfig wraps the WaveConfigs. Think of it like:
///   RunConfig = "the whole adventure"  (4 stages)
///     └── each stage uses ONE WaveConfig = "what enemies appear"  (8 waves)
///         └── each stage randomly picks a Biome = "what it looks like"
/// 1. Right-click in Project → Create → Game → Run Config
/// 2. Drag your existing WaveConfig.asset into the "Wave Config Pool" list
/// 3. (Optional) Create more WaveConfig assets for variety

[CreateAssetMenu(fileName = "RunConfig", menuName = "Game/Run Config")]
public class RunConfig : ScriptableObject
{
    [Header(" RUN STRUCTURE ")]
    [Tooltip("How many biome stages before the final boss. Each stage = a full tower defense cycle.")]
    public int stageCount = 2;

    [Tooltip("How many enemy waves per stage (BEFORE the stage boss).")]
    public int wavesPerStage = 1;

    [Tooltip("Spawn a final boss after all stages are complete?")]
    public bool hasFinalBoss = true;

    [Header(" BIOME POOL ")]
    [Tooltip("Which biomes can appear in a run. The orchestrator randomly picks from this list.")]
    public List<BiomeType> availableBiomes = new List<BiomeType>
    {
        BiomeType.Grass,
        BiomeType.Snow,
        BiomeType.Desert,
        BiomeType.Wasteland
    };

    [Tooltip("Can the same biome appear twice in one run?")]
    public bool allowRepeatBiomes = false;

    [Header(" MAP LAYOUTS ")]
    [Tooltip("Pool of map layouts to randomly pick from each stage.\n" +
             "Leave empty to always use TowerDefenseMap's default rings (original behaviour).")]
    public MapLayoutLibrary mapLayoutLibrary;

    [Header(" WAVE CONFIGS ")]
    [Tooltip("Your existing WaveConfig assets go here. The orchestrator picks waves from these.\n" +
             "If you only have ONE WaveConfig, that's fine — just drag it in.")]
    public List<WaveConfig> waveConfigPool = new List<WaveConfig>();

    [Header(" DIFFICULTY SCALING ")]
    [Tooltip("Enemy count multiplier per stage. 1.2 = 20% more enemies each stage.\n" +
             "Stage 1: ×1.0, Stage 2: ×1.2, Stage 3: ×1.44, Stage 4: ×1.73")]
    public float enemyCountScalePerStage = 1.2f;

    [Tooltip("Spawn delay multiplier per stage. 0.85 = enemies spawn 15% faster each stage.\n" +
             "Lower = harder (tighter waves).")]
    public float spawnDelayScalePerStage = 0.85f;

    [Tooltip("Time between waves (seconds). Overrides WaveConfig.timeBetweenWaves during a run.")]
    public float timeBetweenWaves = 3f;

    [Tooltip("Delay between stages (seconds). Player sees biome transition.")]
    public float timeBetweenStages = 3f;

    [Header(" WEATHER / MODIFIER CHANCES ")]
    [Tooltip("Chance each stage gets night mode overlay (0-1).")]
    [Range(0f, 1f)] public float nightModeChance = 0.15f;

    [Tooltip("Chance each stage gets fog overlay (0-1).")]
    [Range(0f, 1f)] public float fogChance = 0.25f;

    [Tooltip("Chance each stage gets rain (0-1). Auto-disabled for Desert.")]
    [Range(0f, 1f)] public float rainChance = 0.2f;

    [Tooltip("Chance each stage gets snow particles (0-1). Auto-disabled for non-Snow.")]
    [Range(0f, 1f)] public float snowChance = 0.1f;

    [Tooltip("Chance each stage gets drifting medieval hot-air balloons with lantern lights (0-1).\n" +
             "Only visible when night mode is also active on that stage — balloons without darkness to cut through have no visible light effect.")]
    [Range(0f, 1f)] public float nightBalloonChance = 0.5f;

    [Header(" BOSS PREFABS (optional) ")]
    [Tooltip("Boss enemy prefab spawned at the end of each stage. Leave empty to skip stage bosses.")]
    public GameObject stageBossPrefab;

    [Tooltip("Final boss prefab spawned after ALL stages. Leave empty to skip.")]
    public GameObject finalBossPrefab;

    [Header(" ENERGY DROP SCALING ")]
    [Tooltip("Base energy value of drops from a regular enemy kill (stage 1).\n" +
             "Scales up by enemyDropScalePerStage each stage.")]
    [Min(1)]
    public int baseEnemyDropValue = 10;

    [Tooltip("Percentage increase in enemy drop value per stage.\n" +
             "0.20 = +20% per stage.  Stage 1: ×1.0, Stage 2: ×1.2, Stage 3: ×1.44 …")]
    [Range(0f, 2f)]
    public float enemyDropScalePerStage = 0.20f;

    [Tooltip("Base energy value of the drop burst when a stage boss dies (stage 1).")]
    [Min(1)]
    public int baseBossDropValue = 80;

    [Tooltip("Percentage increase in boss drop value per stage.\n" +
             "0.25 = +25% per stage.")]
    [Range(0f, 2f)]
    public float bossDropScalePerStage = 0.25f;

    [Tooltip("How many energy drops to spawn from a boss death burst.\n" +
             "Each drop carries baseBossDropValue / bossDropCount energy (scaled).")]
    [Min(1)]
    public int bossDropCount = 5;

    [Header(" POST-STAGE REWARD SCALING ")]
    [Tooltip("Percentage increase applied to BOTH Heal and Augment energy bonuses per stage.\n" +
             "0.15 = +15% per stage.  Stage 1: ×1.0, Stage 2: ×1.15, Stage 3: ×1.32 …")]
    [Range(0f, 2f)]
    public float postStageRewardScalePerStage = 0.15f;

    [Header(" AUGMENT SELECTION ")]
    [Tooltip("Show augment selection popup every N waves (within each stage).\n" +
             "1 = after every wave, 2 = every other wave, 0 = only after stage boss.\n" +
             "The stage boss augment popup is separate and always happens (if enabled in orchestrator).")]
    [Min(0)]
    public int augmentEveryNWaves = 1;
}
