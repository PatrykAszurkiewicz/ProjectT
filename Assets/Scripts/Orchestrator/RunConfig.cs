using System.Collections.Generic;
using UnityEngine;

// TOP-LEVEL RUN BLUEPRINT e.g. "Play 4 stages, each with 8 waves + a boss, then a final boss."
// RunConfig wraps the WaveConfigs. Think of it like:
//   RunConfig = "the whole adventure"  (4 stages)
//     └── each stage uses ONE WaveConfig = "what enemies appear"  (8 waves)
//         └── each stage randomly picks a Biome = "what it looks like"
// 1. Right-click in Project → Create → Game → Run Config
// 2. Drag your existing WaveConfig.asset into the "Wave Config Pool" list
// 3. (Optional) Create more WaveConfig assets for variety

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

    [Tooltip("GLOBAL wave pacing — how the gap BETWEEN waves works during a run:\n" +
             "• Countdown — wait 'Time Between Waves' seconds (original behaviour).\n" +
             "• Ready Up — wait until every player clicks the on-screen READY button\n" +
             "   (both players in local co-op). 'Time Between Waves' is ignored.\n" +
             "• Immediate — the next wave starts the instant the previous one is cleared.")]
    public WavePacingMode wavePacingMode = WavePacingMode.Countdown;

    [Tooltip("Time between waves (seconds). Used by the Countdown pacing mode. " +
             "Overrides WaveConfig.timeBetweenWaves during a run.")]
    public float timeBetweenWaves = 3f;

    [Tooltip("Delay between stages (seconds). Player sees biome transition.")]
    public float timeBetweenStages = 3f;

    [Header(" ENEMY STAT SCALING ")]
    [Tooltip("Enemy MAX HEALTH multiplier per stage (compounding).\n" +
             "1.15 = +15% health each stage.  Stage 1: ×1.0, Stage 2: ×1.15, Stage 3: ×1.32 …\n" +
             "Set to 1 to disable per-stage health scaling. Stacks multiplicatively with any\n" +
             "enemy-health augment (effective = augment × stage).")]
    [Min(1f)]
    public float enemyHealthScalePerStage = 1.15f;

    [Tooltip("Enemy DAMAGE multiplier per stage (compounding).\n" +
             "1.10 = +10% damage each stage.  Stage 1: ×1.0, Stage 2: ×1.10, Stage 3: ×1.21 …\n" +
             "Set to 1 to disable per-stage damage scaling. Stacks multiplicatively with any\n" +
             "enemy-damage augment (effective = augment × stage).")]
    [Min(1f)]
    public float enemyDamageScalePerStage = 1.10f;

    [Tooltip("Also apply the per-stage HP/damage scaling to STAGE BOSSES and the FINAL BOSS.\n" +
             "OFF by default: bosses are already chosen/tuned per stage via the boss prefab lists,\n" +
             "their armour pool and special-attack damage do NOT run through this multiplier, and\n" +
             "scaling only their health pool can unbalance them. Turn ON to scale boss HP too\n" +
             "(boss special-attack damage still won't scale — it uses dedicated per-boss fields).")]
    public bool scaleBossesWithStage = false;

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

    [Header(" BOSS PREFABS ")]
    [Tooltip("Per-stage boss list. Element 0 = Stage 1's boss, Element 1 = Stage 2's boss, etc.\n" +
             "If this list has a (non-empty) entry for a stage, THAT prefab is used for that stage.\n" +
             "If a stage has no entry (the list is shorter, or the slot is empty) it falls back to\n" +
             "'Stage Boss Prefab' below. Leave the whole list empty to use the fallback for every stage.")]
    public List<GameObject> stageBossPrefabs = new List<GameObject>();

    [Tooltip("Fallback stage boss, used for any stage with no explicit entry in 'Stage Boss Prefabs'.\n" +
             "Leave this empty too to skip the stage boss on stages that have no explicit prefab.")]
    public GameObject stageBossPrefab;

    [Tooltip("Final boss prefab spawned after ALL stages. This is your final-boss choice —\n" +
             "set it to whichever boss you want at the very end. Leave empty (or untick Has Final\n" +
             "Boss) to skip the final boss.")]
    public GameObject finalBossPrefab;

    // Resolve which boss prefab a given stage (0-based) should spawn.
    // Priority: explicit per-stage entry  →  singular fallback  →  null (no boss this stage).
    public GameObject GetStageBoss(int stageIndex)
    {
        if (stageBossPrefabs != null
            && stageIndex >= 0 && stageIndex < stageBossPrefabs.Count
            && stageBossPrefabs[stageIndex] != null)
            return stageBossPrefabs[stageIndex];

        return stageBossPrefab; // may be null → that stage simply has no boss
    }

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

    [Header(" ROGUELIKE RANDOMIZATION ")]
    [Tooltip("ON: shuffle the pooled waves once per run (seeded), so each run deals them in a\n" +
             "different order. OFF: waves play in pool order (Stage 1 = Wave 0, Stage 2 = Wave 1…).\n" +
             "Ignored when Use Procedural Waves is on.")]
    public bool randomizeWaves = false;

    [Tooltip("ON: draw stage bosses randomly (no repeats until the pool is exhausted) from\n" +
             "'Stage Boss Prefabs'. OFF: use the fixed per-stage mapping (Element 0 = Stage 1…).")]
    public bool randomizeBosses = false;

    [Header(" PROCEDURAL WAVES (optional) ")]
    [Tooltip("ON: ignore the authored WaveConfig waves and BUILD each wave by randomly sampling\n" +
             "the Enemy Pool below. This is the most replayable option. OFF: use authored waves.")]
    public bool useProceduralWaves = false;

    [Tooltip("Enemies that procedural waves can draw from. Each entry has a spawn weight and an\n" +
             "earliest stage it may appear (so tougher enemies show up only in later stages).")]
    public List<EnemyPoolEntry> enemyPool = new List<EnemyPoolEntry>();

    [Tooltip("How many enemies a procedural wave contains at Stage 1. Per-stage difficulty\n" +
             "scaling (Enemy Count Scale Per Stage) is applied on top of this automatically.")]
    [Min(1)]
    public int baseEnemiesPerWave = 5;

    [Tooltip("Min/Max delay between enemy spawns in a procedural wave (seconds).")]
    public float proceduralMinSpawnDelay = 0.5f;
    public float proceduralMaxSpawnDelay = 1.5f;

    [Header(" AUGMENT SELECTION ")]
    [Tooltip("Show augment selection popup every N waves (within each stage).\n" +
             "1 = after every wave, 2 = every other wave, 0 = only after stage boss.\n" +
             "The stage boss augment popup is separate and always happens (if enabled in orchestrator).")]
    [Min(0)]
    public int augmentEveryNWaves = 1;
}

// One candidate enemy for procedural wave generation.
[System.Serializable]
public class EnemyPoolEntry
{
    public GameObject enemyPrefab;

    [Tooltip("Relative likelihood of being picked. 2 = twice as common as a weight-1 entry.")]
    [Min(0f)]
    public float weight = 1f;

    [Tooltip("Earliest stage (0-based) this enemy may appear in. 0 = from Stage 1.")]
    [Min(0)]
    public int minStageIndex = 0;
}
