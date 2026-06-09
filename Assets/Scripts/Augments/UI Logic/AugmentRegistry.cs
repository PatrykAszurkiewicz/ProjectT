using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System;

#if UNITY_EDITOR
[System.Serializable]
#endif

//[System.Serializable]
public class AugmentData
{
    public int ID;
    public int Priority;
    public string Name;
    public string Category;
    public string Rarity;
    public string AffectedStats;
    public string Description;
    public string SpritePath;
    public string WhoImplements; // Dev
    public string TargetTypes;   // Augment targets

    // Runtime
    public Sprite Icon { get; set; }
    public List<StatModification> ParsedModifications { get; set; }

    public bool AffectsPlayer => TargetTypes.Split(',', ';').Any(t => t.Trim().Equals("Player", StringComparison.OrdinalIgnoreCase));
    public bool AffectsEnemy => TargetTypes.Split(',', ';').Any(t => t.Trim().Equals("Enemy", StringComparison.OrdinalIgnoreCase));
    public bool AffectsTower => TargetTypes.Split(',', ';').Any(t => t.Trim().Equals("Tower", StringComparison.OrdinalIgnoreCase));
    public bool AffectsWeapon => TargetTypes.Split(',', ';').Any(t => t.Trim().Equals("Weapon", StringComparison.OrdinalIgnoreCase));
    public bool IsGlobal => TargetTypes.Split(',', ';').Any(t => t.Trim().Equals("Global", StringComparison.OrdinalIgnoreCase));
}

// ===== RARITY CONFIGURATION SYSTEM =====
[System.Serializable]
public class RarityConfiguration
{
    [Header("Rarity Settings")]
    public string rarityName = "Common";

    [Header("Probability")]
    [Tooltip("Weight for random selection (higher = more likely)")]
    public float probabilityWeight = 50f;

    [Header("Bonus Range (Percentage)")]
    [Tooltip("Minimum bonus percentage (e.g., 0 for 0%)")]
    public float minBonusPercent = 0f;

    [Tooltip("Maximum bonus percentage (e.g., 10 for 10%)")]
    public float maxBonusPercent = 10f;

    [Header("Visual")]
    public Color rarityColor = Color.green;

    public float GetRandomMultiplier()
    {
        float bonusPercent = UnityEngine.Random.Range(minBonusPercent, maxBonusPercent);
        return 1f + (bonusPercent / 100f);
    }
}

// ===== STAT MODIFICATION SYSTEM =====
[System.Serializable]
public class StatModification
{
    public string StatName;
    public ModificationType OperationType;
    public float Value;
    public string TargetType; // "Player", "Tower", "Enemy", "Global"

    public enum ModificationType
    {
        Add,        // statName + value
        Multiply,   // statName * value
        Set,        // statName = value
        Percentage  // statName * (1 + value/100)
    }
}

// ===== STAT MODIFICATION PARSER =====
public static class StatParser
{
    private static readonly Dictionary<string, StatModification.ModificationType> OperatorMap =
        new Dictionary<string, StatModification.ModificationType>
        {
            { "*", StatModification.ModificationType.Multiply },
            { "+", StatModification.ModificationType.Add },
            { "-", StatModification.ModificationType.Add }, // Minus gets converted to negative add
            { "=", StatModification.ModificationType.Set },
            { "%", StatModification.ModificationType.Percentage }
        };

    // Aliases fields to avoid refactoring Tower scripts and Energy Consumer
    private static readonly Dictionary<string, string> StatAliases = new Dictionary<string, string>
    {
        // Tower stats 
        { "tower_damage", "damage"},
        {"tower_range", "range"},
        {"tower_fire_rate", "fireRate"},
        {"tower_rotation_speed", "rotationSpeed"},
        {"tower_armor", "armorReduction"},
        {"tower_max_energy", "maxEnergy"},
        {"tower_energy", "currentEnergy"},

        //{"generator_wear_rate", "generatorSelfConsumption"},
        { "resource_generation", "globalResourceMultiplier"},

        { "tower_health", "maxEnergy"},
        {"tower_max_health", "maxEnergy"},
        {"tower_current_health", "currentEnergy"},
        {"tower_hp", "maxEnergy"},
        {"tower_attack_damage", "damage"},
        {"tower_attack_speed", "fireRate"},
        {"tower_fire_speed", "fireRate"},
        {"tower_attack_rate", "fireRate"},
        {"tower_shoot_rate", "fireRate"},
        {"tower_attack_range", "range"},
        {"tower_sight_range", "range"},
        {"tower_detection_range", "range"},
        {"tower_turn_speed", "rotationSpeed"},
        {"tower_rotation", "rotationSpeed"},
        {"tower_defense", "armorReduction"},
        {"tower_resistance", "armorReduction"},
        {"tower_armor_reduction", "armorReduction"},
        {"tower_cost", "cost"},
        {"tower_build_cost", "cost"},

        {"tower_freeze_chance", "freezeChance"},
        {"tower_health_regen", "healthRegenRate"},
        {"tower_energy_cost", "energyCostMultiplier"},
        {"tower_energy_consumption", "energyCostMultiplier"},
        
        // Generator tower stats
        {"tower_generation_rate", "energyGenerationRate"},
        {"tower_generation_range", "generationRange"},
        {"tower_generation_interval", "generationInterval"},
        {"generator_rate", "energyGenerationRate"},
        {"generator_range", "generationRange"},
        {"generator_interval", "generationInterval"},
        {"generator_speed", "energyGenerationRate"},
        {"generator_efficiency", "energyGenerationRate"},
        {"generator_output", "energyGenerationRate"},
        {"generation_rate", "energyGenerationRate"},
        {"generation_range", "generationRange"},
        {"generation_interval", "generationInterval"},
        {"energy_generation_rate", "energyGenerationRate"},
        {"energy_generation_range", "generationRange"},
        {"energy_generation_interval", "generationInterval"},
        
        // Alternative naming conventions
        {"damage", "damage"},
        {"range", "range"},
        {"fireRate", "fireRate"},
        {"rotationSpeed", "rotationSpeed"},
        {"armorReduction", "armorReduction"},
        {"maxEnergy", "maxEnergy"},
        {"currentEnergy", "currentEnergy"},
        {"energyGenerationRate", "energyGenerationRate"},
        {"generationRange", "generationRange"},
        {"generationInterval", "generationInterval"},

        { "player_armor", "currentArmor" }, // augment 35 (Damage resistance)
    };

    public static List<StatModification> ParseAffectedStats(string affectedStats, string targetTypes)
    {
        var modifications = new List<StatModification>();

        if (string.IsNullOrEmpty(affectedStats)) return modifications;

        // Parse target types into array
        string[] targets = targetTypes.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .ToArray();

        // Check if targets array is empty and provide a default
        if (targets.Length == 0)
        {
            Debug.LogWarning($"StatParser: No valid target types found in '{targetTypes}'. Using 'Player' as default.");
            targets = new string[] { "Player" };
        }

        // Split stat expressions
        string[] statExpressions = affectedStats.Split(new char[] { ',', ';' },
            System.StringSplitOptions.RemoveEmptyEntries);

        // Defensive check: positional stat->target mapping is fragile. Warn loudly
        // when counts disagree (and it isn't the safe single-target broadcast case)
        // so a reordered/added stat doesn't silently bind to the wrong target.
        if (statExpressions.Length != targets.Length && targets.Length != 1)
        {
            Debug.LogWarning($"StatParser: stat/target count mismatch \u2014 {statExpressions.Length} stat(s) " +
                             $"'{affectedStats}' vs {targets.Length} target(s) '{targetTypes}'. " +
                             $"Extra stats fall back to '{targets[0]}'; extra targets are ignored.");
        }

        // Map each stat expression to corresponding target type
        for (int i = 0; i < statExpressions.Length; i++)
        {
            string targetType = i < targets.Length ? targets[i] : targets[0]; // Now safe to access targets[0]
            var modification = ParseSingleStatExpression(statExpressions[i].Trim(), targetType);
            if (modification != null)
            {
                modifications.Add(modification);
            }
        }

        return modifications;
    }

    private static StatModification ParseSingleStatExpression(string expression, string targetType)
    {
        var regex = new Regex(@"^(\w+)([*+=\-%])([0-9]*\.?[0-9]+)$");
        var match = regex.Match(expression);

        if (!match.Success)
        {
            //TODO uncomment
            //Debug.LogWarning($"StatParser: Could not parse stat expression: '{expression}'");
            return null;
        }

        string statName = match.Groups[1].Value;

        if (StatAliases.TryGetValue(statName, out string actualStatName))
        {
            //Debug.Log($"StatParser: Mapping '{statName}' to '{actualStatName}'");
            statName = actualStatName;
        }

        string operatorStr = match.Groups[2].Value;
        if (!float.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            //TODO uncomment
            //Debug.LogWarning($"StatParser: Could not parse numeric value in expression: '{expression}'");
            return null;
        }

        if (!OperatorMap.TryGetValue(operatorStr, out var operationType))
        {
            //TODO uncomment
            //Debug.LogWarning($"StatParser: Unknown operator '{operatorStr}' in expression: '{expression}'");
            return null;
        }

        // Convert minus operator to negative addition
        if (operatorStr == "-")
        {
            value = -value;
        }

        return new StatModification
        {
            StatName = statName,
            OperationType = operationType,
            Value = value,
            TargetType = targetType
        };
    }
}





// ===== STAT APPLICATOR SYSTEM =====
public static class StatApplicator
{

    private static bool IsSimpleEnemyStat(string statName)
    {
        //Debug.Log($"[STAT_APPLICATOR] IsSimpleEnemyStat checking: '{statName}'");
        switch (statName)
        {
            case "MoveSpeed":
            case "Damage":
            case "MaxHealth":
                //Debug.Log($"[STAT_APPLICATOR] '{statName}' IS a simple enemy stat");
                return true;
            default:
                //Debug.Log($"[STAT_APPLICATOR] '{statName}' is NOT a simple enemy stat");
                return false;
        }
    }

    public static bool ApplyModification(StatModification modification, AugmentTarget target)
    {
        //Debug.Log($"[STAT_APPLICATOR] ApplyModification called: StatName={modification.StatName}, TargetType={modification.TargetType}, Value={modification.Value}, OpType={modification.OperationType}");


        // Handle Lightning on Dodge (augment ID 79)
        if (modification.StatName == "lightning_dodge_damage" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }
            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                var lightning = playerObj.GetComponent<LightningOnDodgeEffect>();
                if (lightning == null)
                {
                    lightning = playerObj.AddComponent<LightningOnDodgeEffect>();
                }
                lightning.damagePercent = modification.Value;
                //Debug.Log($"[LIGHTNING_DODGE] Set damage percent: {modification.Value * 100}%");
                return true;
            }
            Debug.LogError("[LIGHTNING_DODGE] Could not find PlayerStats");
            return false;
        }

        // Handle Fire on Dodge (augment ID 80)
        if ((modification.StatName == "fire_dodge_damage" ||
             modification.StatName == "fire_dodge_dot" ||
             modification.StatName == "fire_dodge_duration") &&
            modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                var fire = playerObj.GetComponent<FireOnDodgeEffect>();
                if (fire == null)
                {
                    fire = playerObj.AddComponent<FireOnDodgeEffect>();
                }

                switch (modification.StatName)
                {
                    case "fire_dodge_damage":
                        fire.damagePercent = modification.Value;
                        //Debug.Log($"[FIRE_DODGE] Set initial damage: {modification.Value * 100}%");
                        break;
                    case "fire_dodge_dot":
                        fire.dotPercent = modification.Value;
                        //Debug.Log($"[FIRE_DODGE] Set damage over time: {modification.Value * 100}% per second");
                        break;
                    case "fire_dodge_duration":
                        fire.dotDuration = modification.Value;
                        //Debug.Log($"[FIRE_DODGE] Set damage over time duration: {modification.Value}s");
                        break;
                }
                return true;
            }
            Debug.LogError("[FIRE_DODGE] Could not find PlayerStats");
            return false;
        }


        // Handle Resource Deposits (augment ID 57)
        if (modification.StatName == "resource_node_count" && modification.TargetType == "Global")
        {
            // Ensure ResourceDepositSpawner exists
            var spawner = UnityEngine.Object.FindFirstObjectByType<ResourceDepositSpawner>();
            if (spawner == null)
            {
                var spawnerObj = new GameObject("ResourceDepositSpawner");
                spawner = spawnerObj.AddComponent<ResourceDepositSpawner>();
            }

            int nodeCount = Mathf.RoundToInt(modification.Value);
            spawner.SpawnResourceDeposits(nodeCount);

            //Debug.Log($"[RESOURCE_DEPOSITS] Spawned {nodeCount} resource deposits on map");
            return true;
        }

        // Handle Grappling Hook Damage (augment ID 77)
        if (modification.StatName == "grappling_hook_damage" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                // Get or create Grappling Hook Damage effect
                var grapplingDamage = playerObj.GetComponent<GrapplingHookDamageEffect>();
                if (grapplingDamage == null)
                {
                    grapplingDamage = playerObj.AddComponent<GrapplingHookDamageEffect>();
                }

                // Add damage (supports stacking)
                grapplingDamage.damage += modification.Value;

                //Debug.Log($"[GRAPPLING_DAMAGE] Set damage: {grapplingDamage.damage}");

                return true;
            }

            Debug.LogError("[GRAPPLING_DAMAGE] Could not find PlayerStats!");
            return false;
        }

        // Handle Pheromone Control (augment ID 76)
        if (modification.StatName == "player_enemy_confusion_aura" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;
                // Get or create Pheromone Control effect
                var pheromoneControl = playerObj.GetComponent<PheromoneControlEffect>();
                if (pheromoneControl == null)
                {
                    pheromoneControl = playerObj.AddComponent<PheromoneControlEffect>();
                }
                pheromoneControl.confusionChance = modification.Value;
                //Debug.Log($"[PHEROMONE_CONTROL] Set confusion chance: {modification.Value * 100f}%");
                return true;
            }
            Debug.LogError("[PHEROMONE_CONTROL] Could not find PlayerStats!");
            return false;
        }



        // Handle Berserker Mode (augment ID 68)
        if (modification.StatName == "berserker_damage_per_missing_health" ||
            modification.StatName == "berserker_defense_penalty")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                // Get or create Berserker Mode effect
                var berserkerMode = playerObj.GetComponent<BerserkerModeEffect>();
                if (berserkerMode == null)
                {
                    berserkerMode = playerObj.AddComponent<BerserkerModeEffect>();
                }

                // Set the appropriate parameter
                switch (modification.StatName)
                {
                    case "berserker_damage_per_missing_health":
                        berserkerMode.damagePerMissingHealthPercent = modification.Value;
                        //Debug.Log($"[BERSERKER_MODE] Set damage per missing health: {modification.Value * 100}% per 1%");
                        break;
                    case "berserker_defense_penalty":
                        berserkerMode.defensePenalty = modification.Value;
                        //Debug.Log($"[BERSERKER_MODE] Set defense penalty: {modification.Value * 100}%");
                        break;
                }

                return true;
            }

            Debug.LogError("[BERSERKER_MODE] Could not find PlayerStats!");
            return false;
        }

        // Handle Adrenaline Rush (augment ID 67)
        if (modification.StatName == "adrenaline_health_threshold" ||
            modification.StatName == "adrenaline_duration" ||
            modification.StatName == "adrenaline_cooldown" ||
            modification.StatName == "adrenaline_attack_speed" ||
            modification.StatName == "adrenaline_move_speed")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                // Get or create Adrenaline Rush effect
                var adrenalineRush = playerObj.GetComponent<AdrenalineRushEffect>();
                if (adrenalineRush == null)
                {
                    adrenalineRush = playerObj.AddComponent<AdrenalineRushEffect>();
                }

                // Set the appropriate parameter
                switch (modification.StatName)
                {
                    case "adrenaline_health_threshold":
                        adrenalineRush.healthThreshold = modification.Value;
                        //Debug.Log($"[ADRENALINE_RUSH] Set health threshold: {modification.Value * 100}%");
                        break;
                    case "adrenaline_duration":
                        adrenalineRush.effectDuration = modification.Value;
                        //Debug.Log($"[ADRENALINE_RUSH] Set duration: {modification.Value}s");
                        break;
                    case "adrenaline_cooldown":
                        adrenalineRush.cooldownDuration = modification.Value;
                        //Debug.Log($"[ADRENALINE_RUSH] Set cooldown: {modification.Value}s");
                        break;
                    case "adrenaline_attack_speed":
                        adrenalineRush.attackSpeedMultiplier = modification.Value;
                        //Debug.Log($"[ADRENALINE_RUSH] Set attack speed multiplier: {modification.Value}x");
                        break;
                    case "adrenaline_move_speed":
                        adrenalineRush.movementSpeedMultiplier = modification.Value;
                        //Debug.Log($"[ADRENALINE_RUSH] Set movement speed bonus: +{modification.Value * 100}%");
                        break;
                }

                return true;
            }

            Debug.LogError("[ADRENALINE_RUSH] Could not find PlayerStats!");
            return false;
        }


        // Handle enemy spawn count multiplier (augment ID 56)
        if (modification.StatName == "enemy_spawn_count")
        {
            var waveSpawner = UnityEngine.Object.FindFirstObjectByType<WaveSpawner>();
            if (waveSpawner == null)
            {
                Debug.LogError("[ENEMY_SPAWN_COUNT] WaveSpawner not found");
                return false;
            }

            float currentMultiplier = waveSpawner.enemySpawnCountMultiplier;
            float newMultiplier = CalculateNewValue(currentMultiplier, modification);

            // Clamp to reasonable values (min 10% spawns, max 500%)
            newMultiplier = Mathf.Clamp(newMultiplier, 0.1f, 5f);

            waveSpawner.enemySpawnCountMultiplier = newMultiplier;

            //Debug.Log($"[ENEMY_SPAWN_COUNT] Enemy spawn multiplier: {currentMultiplier:F2}x -> {newMultiplier:F2}x ({(newMultiplier - 1f) * 100f:+0;-0}% enemies)");
            return true;
        }

        // Handle wave spawn delay (augment ID 55)
        if (modification.StatName == "wave_spawn_delay" && modification.TargetType == "Global")
        {
            var waveSpawner = UnityEngine.Object.FindFirstObjectByType<WaveSpawner>();
            if (waveSpawner == null)
            {
                Debug.LogError("[WAVE_DELAY] WaveSpawner not found");
                return false;
            }
            float currentDelay = waveSpawner.waveSpawnDelayModifier;
            float newDelay = CalculateNewValue(currentDelay, modification);
            waveSpawner.waveSpawnDelayModifier = newDelay;
            //Debug.Log($"[WAVE_DELAY] Wave spawn delay modifier: {currentDelay:F1}s -> {newDelay:F1}s (Total delay: {waveSpawner.waveConfig.timeBetweenWaves + newDelay:F1}s)");
            return true;
        }

        // Handle Health on Kill (augment ID 34)
        if (modification.StatName == "health_per_kill" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }
            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;
                // Get or create Health on Kill effect
                var existing = playerObj.GetComponent<HealthOnKillEffect>();
                if (existing != null)
                {
                    // Stack the effect
                    existing.healthPerKill += modification.Value;
                    //Debug.Log($"[HEALTH_ON_KILL] Stacked! Now restores {existing.healthPerKill} HP per kill");
                    return true;
                }
                var healthOnKill = playerObj.AddComponent<HealthOnKillEffect>();
                healthOnKill.healthPerKill = modification.Value;
                //Debug.Log($"[HEALTH_ON_KILL] Added with {modification.Value} HP per kill from CSV");
                return true;
            }

            Debug.LogError("[HEALTH_ON_KILL] Could not find PlayerStats");
            return false;
        }


        // Handle Friendly Fire (augment ID 29)
        if ((modification.StatName == "enemy_infighting_chance" || modification.StatName == "enemy_infighting_duration")
            && modification.TargetType == "Global")
        {
            //Debug.Log($"[FRIENDLY_FIRE_DEBUG] Processing stat: {modification.StatName} = {modification.Value}");

            GameObject managerObj = GameObject.Find("FriendlyFireManager");
            if (managerObj == null)
            {
                managerObj = new GameObject("FriendlyFireManager");
                //Debug.Log("[FRIENDLY_FIRE_DEBUG] Created FriendlyFireManager GameObject");
            }

            var existing = managerObj.GetComponent<FriendlyFireEffect>();
            if (existing == null)
            {
                existing = managerObj.AddComponent<FriendlyFireEffect>();
                //Debug.Log("[FRIENDLY_FIRE_DEBUG] Added FriendlyFireEffect component");
            }

            // Apply the modification with sensible caps
            if (modification.StatName == "enemy_infighting_chance")
            {
                existing.infightingChance = Mathf.Clamp(modification.Value, 0.1f, 1.0f); // 10% to 100%
                //Debug.Log($"[FRIENDLY_FIRE]  Chance set to: {existing.infightingChance * 100f:F1}% (capped)");
            }
            else if (modification.StatName == "enemy_infighting_duration")
            {
                existing.infightingDuration = Mathf.Clamp(modification.Value, 5f, 20f); // 5s to 20s max
                //Debug.Log($"[FRIENDLY_FIRE] ✓ Duration set to: {existing.infightingDuration}s (capped)");
            }

            return true;
        }

        // Handle Momentum - Health boost per kill (augment ID 28)
        if (modification.StatName == "momentum_health" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats == null)
            {
                Debug.LogError("[MOMENTUM] PlayerStats not found!");
                return false;
            }

            var playerObj = playerStats.gameObject;

            // Get or create momentum effect
            var momentum = playerObj.GetComponent<MomentumEffect>();
            if (momentum == null)
            {
                momentum = playerObj.AddComponent<MomentumEffect>();
            }

            // CSV value is the multiplier per kill (1.03), convert to percentage increase (0.03)
            float healthIncreasePerKill = modification.Value - 1.0f;
            momentum.healthMultiplierPerKill = healthIncreasePerKill;

            //Debug.Log($"[MOMENTUM] Set health multiplier: {healthIncreasePerKill * 100f:F1}% per kill (CSV: {modification.Value})");
            return true;
        }

        // Handle Momentum - Damage boost per kill (augment ID 28)
        if (modification.StatName == "momentum_damage" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats == null)
            {
                Debug.LogError("[MOMENTUM] PlayerStats not found!");
                return false;
            }

            var playerObj = playerStats.gameObject;

            // Get or create momentum effect
            var momentum = playerObj.GetComponent<MomentumEffect>();
            if (momentum == null)
            {
                momentum = playerObj.AddComponent<MomentumEffect>();
            }

            // CSV value is the multiplier per kill (1.03), convert to percentage increase (0.03)
            float damageIncreasePerKill = modification.Value - 1.0f;
            momentum.damageMultiplierPerKill = damageIncreasePerKill;

            Debug.Log($"[MOMENTUM] Set damage multiplier: {damageIncreasePerKill * 100f:F1}% per kill (CSV: {modification.Value})");
            return true;
        }


        // Handle Escalation (augment ID 27)
        if (modification.StatName == "escalation_multiplier" && modification.TargetType == "Player")
        {
            WaveSpawner spawner = UnityEngine.Object.FindFirstObjectByType<WaveSpawner>();
            if (spawner == null)
            {
                Debug.LogError("[ESCALATION] WaveSpawner not found!");
                return false;
            }

            var spawnerObj = spawner.gameObject;

            // Check if escalation already exists
            var existing = spawnerObj.GetComponent<EscalationEffect>();
            if (existing != null)
            {
                Debug.LogWarning("[ESCALATION] Escalation already active - cannot stack augment");
                return false;
            }

            float damageIncreasePerWave = modification.Value - 1.0f;

            var escalation = spawnerObj.AddComponent<EscalationEffect>();
            escalation.damageIncreasePerWave = damageIncreasePerWave;

            //Debug.Log($"[ESCALATION] Added with {damageIncreasePerWave * 100f:F1}% damage increase per wave (CSV value: {modification.Value})");
            return true;
        }


        // Handle Ice Armor freeze effect (augment ID 13)
        if (modification.StatName == "ice_armor_duration" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                // Check if ice armor already exists (stacking)
                var existing = playerObj.GetComponent<IceArmorEffect>();
                if (existing != null)
                {
                    // Stack duration additively (3 + 3 = 6 seconds)
                    existing.freezeDuration += modification.Value;

                    //Debug.Log($"[ICE_ARMOR] Stacked! Freeze duration now: {existing.freezeDuration}s");
                    return true;
                }

                var iceArmor = playerObj.AddComponent<IceArmorEffect>();
                iceArmor.freezeDuration = modification.Value;
                //Debug.Log($"[ICE_ARMOR] Added with {iceArmor.freezeDuration}s freeze duration");
                return true;
            }

            Debug.LogError("[ICE_ARMOR] Could not find PlayerStats!");
            return false;
        }


        // Handle Damage Reflection percentage (augment ID 12)
        if (modification.StatName == "reflection_percentage" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                // Check if reflection already exists (stacking)
                var existing = playerObj.GetComponent<DamageReflectionEffect>();
                if (existing != null)
                {
                    // Stack reflection percentage additively (0.30 + 0.30 = 0.60 = 60%)
                    existing.reflectionPercentage += modification.Value;

                    //Debug.Log($"[DAMAGE_REFLECTION] Stacked! Reflection now: {existing.reflectionPercentage * 100:F1}%");
                    return true;
                }

                var reflection = playerObj.AddComponent<DamageReflectionEffect>();
                reflection.reflectionPercentage = modification.Value;
                //Debug.Log($"[DAMAGE_REFLECTION] Added with {reflection.reflectionPercentage * 100:F1}% reflection");
                return true;
            }

            Debug.LogError("[DAMAGE_REFLECTION] Could not find PlayerStats!");
            return false;
        }


        // Handle Lifesteal percentage (augment ID 8)
        if (modification.StatName == "lifesteal_percentage" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = playerStats.gameObject;

                // Check if lifesteal already exists (stacking)
                var existing = playerObj.GetComponent<LifestealEffect>();
                if (existing != null)
                {
                    // Stack lifesteal percentage additively
                    float oldPercentage = existing.lifestealPercentage;

                    switch (modification.OperationType)
                    {
                        case StatModification.ModificationType.Add:
                            existing.lifestealPercentage += modification.Value;
                            break;
                        case StatModification.ModificationType.Set:
                            existing.lifestealPercentage += modification.Value; // Add to existing for stacking
                            break;
                        case StatModification.ModificationType.Multiply:
                            existing.lifestealPercentage *= modification.Value;
                            break;
                    }

                    //Debug.Log($"[LIFESTEAL] Stacked! {oldPercentage * 100:F1}% -> {existing.lifestealPercentage * 100:F1}%");
                    return true;
                }

                var lifesteal = playerObj.AddComponent<LifestealEffect>();
                lifesteal.lifestealPercentage = modification.Value;
                //Debug.Log($"[LIFESTEAL] Added with {modification.Value * 100:F1}% lifesteal from CSV");
                return true;
            }

            Debug.LogError("[LIFESTEAL] Could not find PlayerStats!");
            return false;
        }

        // Handle weapon stats (attackCooldown, damage) when target is Player (augment ID 7)
        if ((modification.StatName == "attackCooldown" || modification.StatName == "damage") &&
            modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var weapon = playerStats.GetComponentInChildren<Weapon>();
                if (weapon == null)
                {
                    weapon = UnityEngine.Object.FindFirstObjectByType<Weapon>();
                }

                if (weapon != null)
                {
                    var weaponData = weapon.GetWeaponData();
                    if (weaponData != null)
                    {
                        // Apply modification to weapon data
                        return ApplyToTarget(weaponData, modification);
                    }
                }
                Debug.LogError($"[WEAPON_STAT] Could not find Weapon or WeaponData for stat: {modification.StatName}");
                return false;
            }
        }

        // Handle Increased Shield Defenses (augment ID 78) — % reduction to damage taken.
        if (modification.StatName == "player_damage_reduction" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();

            if (playerStats != null)
            {
                var playerObj = ((MonoBehaviour)playerStats).gameObject;
                var existing = playerObj.GetComponent<PlayerDamageReductionEffect>();
                if (existing == null)
                    existing = playerObj.AddComponent<PlayerDamageReductionEffect>();
                existing.damageReductionPercent += modification.Value; // additive stacking
                return true;
            }
            Debug.LogError("[DAMAGE_REDUCTION] Could not find PlayerStats");
            return false;
        }

        // Handle Berserker's Fury (augment ID 6)
        if (modification.StatName == "player_damage_stack_kill" && modification.TargetType == "Player") // CHANGED: "Weapon" → "Player"
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = ((MonoBehaviour)playerStats).gameObject;

                // Extract percentage increase from multiplier
                float percentageIncrease = modification.Value - 1.0f;

                var existing = playerObj.GetComponent<BerserkersFuryEffect>();
                if (existing != null)
                {
                    // Stack the effect
                    existing.damageIncreasePerKill += percentageIncrease;
                    //Debug.Log($"[BERSERKER] Stacked! Damage increase per kill: {existing.damageIncreasePerKill * 100f:F1}%");
                    return true;
                }

                var berserkerEffect = playerObj.AddComponent<BerserkersFuryEffect>();
                berserkerEffect.damageIncreasePerKill = percentageIncrease;
                //Debug.Log($"[BERSERKER] Added with {percentageIncrease * 100f:F1}% damage increase per kill");
                return true;
            }

            Debug.LogError("[BERSERKER] Could not find PlayerStats!");
            return false;
        }

        // Handle Core Energy Siphon (augment ID 75)
        if (modification.StatName == "core_energy_from_attacks")
        {
            var map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
            if (map == null)
            {
                Debug.LogError("[CORE_SIPHON] TowerDefenseMap not found");
                return false;
            }

            var core = map.GetCentralCore();
            if (core == null)
            {
                Debug.LogError("[CORE_SIPHON] Central Core not found");
                return false;
            }

            var coreObj = ((MonoBehaviour)core).gameObject;
            var existing = coreObj.GetComponent<CoreEnergySiphonEffect>();

            if (existing != null)
            {
                // Stack siphon percentage additively
                existing.siphonPercentage += modification.Value;
                //Debug.Log($"[CORE_SIPHON] Stacked! Siphon rate: {existing.siphonPercentage * 100}%");
                return true;
            }

            var siphonEffect = coreObj.AddComponent<CoreEnergySiphonEffect>();
            siphonEffect.siphonPercentage = modification.Value;
            //Debug.Log($"[CORE_SIPHON] Added with {modification.Value * 100}% siphon rate");
            return true;
        }


        if (modification.TargetType == "Enemy" && IsSimpleEnemyStat(modification.StatName))
        {
            //Debug.Log($"[STAT_APPLICATOR] Detected simple enemy stat, routing to ApplyGlobalEnemyModifier");
            return ApplyGlobalEnemyModifier(modification);
        }

        // Handle Core Repair Systems (augment ID 74)
        if (modification.StatName == "core_regen_rate" && modification.TargetType == "Global")
        {
            var map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
            if (map == null)
            {
                Debug.LogError("[CORE_REPAIR] TowerDefenseMap not found");
                return false;
            }

            var core = map.GetCentralCore();
            if (core == null)
            {
                Debug.LogError("[CORE_REPAIR] Central Core not found");
                return false;
            }

            var coreObj = ((MonoBehaviour)core).gameObject;
            var existing = coreObj.GetComponent<CoreRepairSystems>();

            if (existing != null)
            {
                // Stack regeneration rate
                existing.regenerationRate += modification.Value;
                //Debug.Log($"[CORE_REPAIR] Stacked! Regen rate: {existing.regenerationRate} HP/sec");
                return true;
            }

            var repairSystem = coreObj.AddComponent<CoreRepairSystems>();
            repairSystem.regenerationRate = modification.Value;
            //Debug.Log($"[CORE_REPAIR] Added: {modification.Value} HP/sec after 30s without damage");
            return true;
        }


        // Handle Core Shield Matrix (augment ID 73)
        if (modification.StatName == "core_shield" && modification.TargetType == "Global")
        {
            var map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
            if (map == null)
            {
                Debug.LogError("[CORE_SHIELD] TowerDefenseMap not found");
                return false;
            }

            var core = map.GetCentralCore();
            if (core == null)
            {
                Debug.LogError("[CORE_SHIELD] Central Core not found");
                return false;
            }

            var coreObj = ((MonoBehaviour)core).gameObject;
            var existing = coreObj.GetComponent<CoreShieldMatrix>();

            if (existing != null)
            {
                // Stack shield strength
                existing.maxShieldStrength += modification.Value;
                existing.currentShieldStrength += modification.Value;
                //Debug.Log($"[CORE_SHIELD] Stacked Shield: {existing.currentShieldStrength}/{existing.maxShieldStrength}");
                return true;
            }

            var shieldMatrix = coreObj.AddComponent<CoreShieldMatrix>();
            shieldMatrix.maxShieldStrength = modification.Value;
            shieldMatrix.currentShieldStrength = modification.Value;
            //Debug.Log($"[CORE_SHIELD] Added shield with {modification.Value} HP");
            return true;
        }

        // Handle Energy Scavenging (augment ID 72)
        if (modification.StatName == "energy_scavenging_amount")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = ((MonoBehaviour)playerStats).gameObject;

                var existing = playerObj.GetComponent<EnergyScavengingEffect>();
                if (existing != null)
                {
                    existing.energyAmount += (int)modification.Value;
                    //Debug.Log($"[ENERGY_SCAVENGING] Stacked! Amount now: {existing.energyAmount}");
                    return true;
                }

                var scavengingEffect = playerObj.AddComponent<EnergyScavengingEffect>();
                scavengingEffect.energyAmount = (int)modification.Value;
                //Debug.Log($"[ENERGY_SCAVENGING] Added with {modification.Value} energy per enemy near generator");
                return true;
            }

            Debug.LogError("[ENERGY_SCAVENGING] Could not find PlayerStats!");
            return false;
        }


        // Handle Energy Vampire Touch (augment ID 71)
        if (modification.StatName == "player_energy_drain")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = ((MonoBehaviour)playerStats).gameObject;

                var existing = playerObj.GetComponent<EnergyVampireTouchEffect>();
                if (existing != null)
                {
                    // Stack the drain amount
                    existing.drainAmount += (int)modification.Value;
                    //Debug.Log($"[ENERGY_VAMPIRE] Stacked! Drain amount now: {existing.drainAmount}");
                    return true;
                }

                var vampireEffect = playerObj.AddComponent<EnergyVampireTouchEffect>();
                vampireEffect.drainAmount = (int)modification.Value;
                //Debug.Log($"[ENERGY_VAMPIRE] Added with {modification.Value} energy drain per melee hit");
                return true;
            }

            Debug.LogError("[ENERGY_VAMPIRE] Could not find PlayerStats!");
            return false;
        }


        // Handle player collection radius (augment ID 64: Auto-collect resources)
        if (modification.StatName == "collectionRadius" && modification.TargetType == "Player")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var collector = playerStats.GetComponent<PlayerEnergyCollector>();
                if (collector != null)
                {
                    float currentValue = collector.collectionRadius;
                    float newValue = CalculateNewValue(currentValue, modification);

                    // Use the new method that updates both the field AND the collider
                    collector.UpdateCollectionRadius(newValue);

                    //Debug.Log($"[AUTO_COLLECT] Collection radius: {currentValue:F2} -> {newValue:F2}");
                    return true;
                }
            }
            Debug.LogError("[AUTO_COLLECT] Could not find PlayerEnergyCollector");
            return false;
        }

        // Handle player energy boost (augment ID 62: Starting resource boost)
        if (modification.StatName == "current_energy" && modification.TargetType == "Global")
        {
            if (EnergyManager.Instance == null)
            {
                Debug.LogError("[AUGMENT] EnergyManager not found for player energy boost");
                return false;
            }

            int currentEnergy = EnergyManager.Instance.GetPlayerEnergy();
            int energyToAdd = 0;

            switch (modification.OperationType)
            {
                case StatModification.ModificationType.Multiply:
                    // For multiply: give (multiplier - 1) * current as bonus e.g., 1.50 means +50% of current
                    energyToAdd = Mathf.RoundToInt(currentEnergy * (modification.Value - 1f));
                    break;
                case StatModification.ModificationType.Add:
                    energyToAdd = Mathf.RoundToInt(modification.Value);
                    break;
                case StatModification.ModificationType.Set:
                    EnergyManager.Instance.SetPlayerEnergy(Mathf.RoundToInt(modification.Value));
                    //Debug.Log($"[AUGMENT] Set player energy to {modification.Value}");
                    return true;
            }

            if (energyToAdd > 0)
            {
                EnergyManager.Instance.GivePlayerEnergy(energyToAdd);
                //Debug.Log($"[AUGMENT] Starting resource boost: gave player {energyToAdd} energy (50% of {currentEnergy})");
                return true;
            }

            return false;
        }


        // Implementation of Augment ID 61
        // Handle generator proximity efficiency boost
        if (modification.StatName == "tower_near_generator_efficiency")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = ((MonoBehaviour)playerStats).gameObject;

                var existing = playerObj.GetComponent<GeneratorProximityEffect>();
                if (existing != null)
                {
                    // Stack multiplicatively (0.8 * 0.8 = 0.64 = 36% total reduction)
                    float oldMultiplier = existing.energyEfficiencyMultiplier;
                    existing.energyEfficiencyMultiplier *= modification.Value;
                    // Cap at 1.0 to prevent increased consumption
                    existing.energyEfficiencyMultiplier = Mathf.Min(existing.energyEfficiencyMultiplier, 1.0f);
                    Debug.Log($"[GENERATOR_PROXIMITY] Stacked! {oldMultiplier:F3}x * {modification.Value:F2} = {existing.energyEfficiencyMultiplier:F3}x (capped at 1.0)");
                    return true;
                }

                var generatorProximity = playerObj.AddComponent<GeneratorProximityEffect>();
                generatorProximity.energyEfficiencyMultiplier = Mathf.Min(modification.Value, 1.0f);
                Debug.Log($"[GENERATOR_PROXIMITY] Added with {generatorProximity.energyEfficiencyMultiplier:F2}x energy efficiency");
                return true;
            }

            Debug.LogError("[GENERATOR_PROXIMITY] Could not find PlayerStats!");
            return false;
        }

        // Handle player-tower coordination (mutual armor boost)
        if ((modification.StatName == "currentArmor" || modification.StatName == "armorReduction") &&
            (modification.TargetType == "Player" || modification.TargetType == "Tower") &&
            modification.OperationType == StatModification.ModificationType.Add)
        {
            //Debug.Log($"[COORDINATION] Detected coordination stat: {modification.StatName} for {modification.TargetType}, value: {modification.Value}");

            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = ((MonoBehaviour)playerStats).gameObject;

                // Check if coordination effect already exists
                var existing = playerObj.GetComponent<PlayerTowerCoordinationEffect>();
                if (existing != null)
                {
                    // STACK THE EFFECT based on which stat this is
                    if (modification.StatName == "currentArmor")
                    {
                        existing.playerArmorBonus += modification.Value;
                        //Debug.Log($"[COORDINATION] Stacked PLAYER armor! New total: +{existing.playerArmorBonus:F1} flat");
                    }
                    else if (modification.StatName == "armorReduction")
                    {
                        existing.towerArmorBonus += modification.Value;
                        //Debug.Log($"[COORDINATION] Stacked TOWER armor! New total: +{existing.towerArmorBonus:F3} (will be clamped to 1.0 max)");
                    }
                    return true;
                }

                // First time - add the coordination effect
                var coordination = playerObj.AddComponent<PlayerTowerCoordinationEffect>();

                // Set the appropriate bonus based on which stat this is
                if (modification.StatName == "currentArmor")
                {
                    coordination.playerArmorBonus = modification.Value;
                    //($"[COORDINATION] Added effect with PLAYER armor bonus: {modification.Value:F1}");
                }
                else if (modification.StatName == "armorReduction")
                {
                    coordination.towerArmorBonus = modification.Value;
                    //Debug.Log($"[COORDINATION] Added effect with TOWER armor bonus: {modification.Value:F3}");
                }

                return true;
            }

            Debug.LogError("[COORDINATION] Could not find PlayerStats!");
            return false;
        }

        // Handle tower adjacency synergy
        if (modification.StatName == "adjacent_tower_damage")
        {
            var map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
            if (map == null)
            {
                Debug.LogError("TowerDefenseMap not found for tower synergy");
                return false;
            }

            var synergy = map.GetComponent<TowerSynergyManager>();
            if (synergy == null)
            {
                synergy = map.gameObject.AddComponent<TowerSynergyManager>();
                synergy.damageMultiplier = modification.Value;
            }
            else
            {
                synergy.damageMultiplier *= modification.Value;
            }

            synergy.enabled = true;

            //Debug.Log($"Tower synergy: {synergy.damageMultiplier}x per adjacent tower");
            return true;
        }


        // Handle additional tower rings
        if (modification.StatName == "additional_tower_rings")
        {
            var map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
            if (map == null) return false;

            int ringsToAdd = Mathf.RoundToInt(modification.Value);
            int currentRingCount = map.rings.Count;
            int availableSlots = map.maxTotalRings - currentRingCount;

            if (availableSlots <= 0)
            {
                Debug.LogWarning($"Cannot add more rings: already at maximum ({map.maxTotalRings} rings)");
                return false;
            }

            if (ringsToAdd > availableSlots)
            {
                //Debug.LogWarning($"Can only add {availableSlots} more ring(s), capping from {ringsToAdd}");
                ringsToAdd = availableSlots;
            }

            for (int i = 0; i < ringsToAdd; i++)
            {
                // Get outermost ring parameters
                float maxRadius = 2.3f;
                int slotCount = 8;
                float slotSize = 1.9f;

                foreach (var ring in map.rings)
                {
                    if (ring.enabled && ring.radius > maxRadius)
                    {
                        maxRadius = ring.radius;
                        slotCount = ring.slotCount;
                        slotSize = ring.slotSize;
                    }
                }

                map.AddRing(maxRadius + 1.8f, slotCount, slotSize);
            }

            map.GenerateMap();
            //Debug.Log($"Added {ringsToAdd} tower placement ring(s) ({map.rings.Count}/{map.maxTotalRings} total)");
            return true;
        }

        // Handle additional tower SLOTS (works on ALL layout types)
        if (modification.StatName == "additional_tower_slots")
        {
            var map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
            if (map == null) return false;

            int slotsToAdd = Mathf.RoundToInt(modification.Value);
            int added = map.AddBonusSlots(slotsToAdd);

            if (added == 0)
            {
                Debug.LogWarning("[Augment] additional_tower_slots: no bonus slots available " +
                                 "in the current layout. Add positions to MapLayoutDefinition.bonusSlotPositions.");
                return false;
            }

            Debug.Log($"[Augment] additional_tower_slots: revealed {added} bonus slot(s).");
            return true;
        }

        // Handle Additional Tower Slot Per Waves (augment ID 320)
        // Reveals 1 bonus slot every Nth wave. Multiple stat lines configure
        // the same component (interval, count-per-trigger, optional cap).
        if (modification.StatName == "slots_per_wave_interval" ||
            modification.StatName == "slots_per_wave_count" ||
            modification.StatName == "slots_per_wave_max")
        {
            // Host the effect on the WaveSpawner so it lives/dies with the spawner
            // (matches the EscalationEffect pattern).
            WaveSpawner spawner = UnityEngine.Object.FindFirstObjectByType<WaveSpawner>();
            if (spawner == null)
            {
                Debug.LogError("[ADDITIONAL_SLOTS_PER_WAVE] WaveSpawner not found in scene!");
                return false;
            }

            var spawnerObj = spawner.gameObject;

            // Get-or-create so multiple stat rows from one CSV line configure
            // ONE component (same way AdrenalineRushEffect is wired).
            var effect = spawnerObj.GetComponent<AdditionalSlotsPerWaveEffect>();
            if (effect == null)
            {
                effect = spawnerObj.AddComponent<AdditionalSlotsPerWaveEffect>();
            }

            switch (modification.StatName)
            {
                case "slots_per_wave_interval":
                    effect.waveInterval = Mathf.Max(1, Mathf.RoundToInt(modification.Value));
                    break;
                case "slots_per_wave_count":
                    effect.slotsPerTrigger = Mathf.Max(1, Mathf.RoundToInt(modification.Value));
                    break;
                case "slots_per_wave_max":
                    effect.maxSlotsToReveal = Mathf.Max(0, Mathf.RoundToInt(modification.Value));
                    break;
            }

            return true;
        }

        // Handle global resource multiplier
        if (modification.StatName == "globalResourceMultiplier")
        {
            return ApplyGlobalResourceMultiplier(modification);
        }

        // Handle global energy decay rate
        if (modification.StatName == "globalEnergyDecayRate")
        {
            if (EnergyManager.Instance == null) return false;

            float currentValue = EnergyManager.Instance.globalEnergyDecayRate;
            float newValue = CalculateNewValue(currentValue, modification);
            newValue = Mathf.Max(0.1f, newValue); // Minimum 10% decay rate
            EnergyManager.Instance.globalEnergyDecayRate = newValue;

            //Debug.Log($"Applied global energy decay rate: {currentValue} -> {newValue}");
            return true;
        }

        // Handle global repair cost effect (both discrete and continuous)
        if (modification.StatName == "tower_repair_cost")
        {
            if (EnergyManager.Instance == null) return false;

            // Update discrete repair cost (repairCostPerClick)
            float currentDiscreteCost = EnergyManager.Instance.repairCostPerClick;
            float newDiscreteCost = CalculateNewValue(currentDiscreteCost, modification);
            EnergyManager.Instance.repairCostPerClick = Mathf.Max(1, Mathf.RoundToInt(newDiscreteCost));

            // Update continuous supply cost (continuousSupplyCost)
            float currentContinuousCost = EnergyManager.Instance.continuousSupplyCost;
            float newContinuousCost = CalculateNewValue(currentContinuousCost, modification);
            EnergyManager.Instance.continuousSupplyCost = Mathf.Max(0.1f, newContinuousCost);

            //Debug.Log($"Applied global repair cost modification - Discrete: {currentDiscreteCost} -> {EnergyManager.Instance.repairCostPerClick}, Continuous: {currentContinuousCost} -> {EnergyManager.Instance.continuousSupplyCost}");
            return true;
        }


        // Handle global tower build cost effect
        if (modification.StatName == "towerBuildCost")
        {
            if (EnergyManager.Instance == null) return false;
            float currentCost = EnergyManager.Instance.towerBuildCost;
            float newCost = CalculateNewValue(currentCost, modification);
            EnergyManager.Instance.towerBuildCost = Mathf.Max(1, Mathf.RoundToInt(newCost));
            //Debug.Log($"Applied global tower build cost modification: {currentCost} -> {EnergyManager.Instance.towerBuildCost}");
            return true;
        }

        // Handle tower energy decay proximity effects
        if (modification.StatName == "tower_energy_decay")
        {
            PlayerStats playerStats = target.Player;
            if (playerStats == null)
            {
                playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            }

            if (playerStats != null)
            {
                var playerObj = ((MonoBehaviour)playerStats).gameObject;

                var existing = playerObj.GetComponent<TowerCommanderEffect>();
                if (existing != null)
                {
                    // STACK: Multiplicative (0.7 * 0.7 = 0.49 = 51% decay reduction total)
                    float oldMultiplier = existing.energyDecayMultiplier;
                    existing.energyDecayMultiplier *= modification.Value;
                    //Debug.Log($"[TOWER_COMMANDER] Stacked! {oldMultiplier:F3}x * {modification.Value:F2} = {existing.energyDecayMultiplier:F3}x energy decay");
                    //Debug.Log($"[TOWER_COMMANDER] Component enabled? {existing.enabled}, isActiveAndEnabled? {existing.isActiveAndEnabled}");

                    //Debug.Log($"[TOWER_COMMANDER] Stacked! {oldMultiplier:F3}x * {modification.Value:F2} = {existing.energyDecayMultiplier:F3}x energy decay");
                    return true;
                }

                var towerCommander = playerObj.AddComponent<TowerCommanderEffect>();
                towerCommander.energyDecayMultiplier = modification.Value;
                towerCommander.ForceUpdate();
                //Debug.Log($"[TOWER_COMMANDER] Added with {modification.Value:F2}x energy decay from CSV");
                return true;
            }

            //Debug.LogError("[TOWER_COMMANDER] Could not find PlayerStats!");
            return false;
        }

        // Handle proximity effects (Symbiosis)
        if (modification.StatName == "proximity_tower_damage")
        {
            if (target.Player != null)
            {
                var playerObj = ((MonoBehaviour)target.Player).gameObject;

                var existing = playerObj.GetComponent<SymbiosisEffect>();
                if (existing != null)
                {
                    // Multiplicative stacking: multiply by the CSV value each time
                    float oldBoost = existing.damageBoost;
                    existing.damageBoost *= modification.Value;

                    //Debug.Log($"[SYMBIOSIS] Stacked! {oldBoost:F3}x * {modification.Value:F2} = {existing.damageBoost:F3}x total");
                    return true;
                }

                var symbiosis = playerObj.AddComponent<SymbiosisEffect>();
                symbiosis.damageBoost = modification.Value;
                //Debug.Log($"[SYMBIOSIS] Added with {modification.Value:F2}x damage boost from CSV");
                return true;
            }
            else
            {
                Debug.LogError("[SYMBIOSIS] Player target is NULL!");
                return false;
            }
        }


        // Handle global resource multiplier
        if (modification.StatName == "globalResourceMultiplier")
        {
            return ApplyGlobalResourceMultiplier(modification);
        }


        // Handle global repair cost effect
        if (modification.StatName == "tower_repair_cost")
        {
            if (EnergyManager.Instance == null) return false;

            float currentCost = EnergyManager.Instance.repairCostPerClick;
            float newCost = CalculateNewValue(currentCost, modification);
            EnergyManager.Instance.repairCostPerClick = Mathf.Max(1, Mathf.RoundToInt(newCost));

            //Debug.Log($"Applied global repair cost modification: {currentCost} -> {EnergyManager.Instance.repairCostPerClick}");
            return true;
        }

        object targetObject = GetTargetObject(modification.TargetType, target);
        if (targetObject == null)
        {
            //Debug.LogWarning($"StatApplicator: No target object found for type: {modification.TargetType}");
            return false;
        }

        return ApplyToTarget(targetObject, modification);
    }

    private static bool ApplyGlobalEnemyModifier(StatModification modification)
    {
        //Debug.Log($"[ENEMY_MODIFIER] ApplyGlobalEnemyModifier called for stat: {modification.StatName}");

        // Ensure the manager exists
        if (EnemyStatModifierManager.Instance == null)
        {
            //Debug.LogWarning("[ENEMY_MODIFIER] Manager instance is null, creating...");
            var managerGO = new GameObject("EnemyStatModifierManager");
            managerGO.AddComponent<EnemyStatModifierManager>();
            //Debug.Log("[ENEMY_MODIFIER] Created EnemyStatModifierManager");
        }

        switch (modification.StatName)
        {
            case "MoveSpeed":
                //Debug.Log($"[ENEMY_MODIFIER] Applying MoveSpeed multiplier: {modification.Value}");
                EnemyStatModifierManager.Instance.ApplyMoveSpeedMultiplier(modification.Value);
                return true;

            case "Damage":
                //Debug.Log($"[ENEMY_MODIFIER] Applying Damage multiplier: {modification.Value}");
                EnemyStatModifierManager.Instance.ApplyDamageMultiplier(modification.Value);
                return true;

            case "MaxHealth":
                //Debug.Log($"[ENEMY_MODIFIER] Applying Health multiplier: {modification.Value}");
                EnemyStatModifierManager.Instance.ApplyHealthMultiplier(modification.Value);
                return true;

            default:
                Debug.LogWarning($"[ENEMY_MODIFIER] Unknown enemy stat: {modification.StatName}");
                return false;
        }
    }

    private static bool ApplyGlobalRepairCostEffect(StatModification modification)
    {
        if (EnergyManager.Instance == null) return false;

        float currentCost = EnergyManager.Instance.repairCostPerClick;
        float newCost = CalculateNewValue(currentCost, modification);
        EnergyManager.Instance.repairCostPerClick = Mathf.Max(1, Mathf.RoundToInt(newCost));

        //Debug.Log($"Applied global repair cost modification: {currentCost} -> {EnergyManager.Instance.repairCostPerClick}");
        return true;
    }

    private static bool ApplyGlobalResourceMultiplier(StatModification modification)
    {
        if (EnergyManager.Instance == null) return false;

        float currentValue = EnergyManager.Instance.globalResourceMultiplier;
        float newValue = CalculateNewValue(currentValue, modification);
        EnergyManager.Instance.globalResourceMultiplier = newValue;

        //Debug.Log($"Applied global resource multiplier: {currentValue} -> {newValue}");
        return true;
    }


    private static bool ApplyGlobalEffect(StatModification modification, AugmentTarget target)
    {
        if (EnergyManager.Instance == null) return false;

        switch (modification.StatName)
        {
            case "GLOBAL_tower_repair_cost":
            case "GLOBAL_repair_cost":
                float currentCost = EnergyManager.Instance.repairCostPerClick;
                float newCost = CalculateNewValue(currentCost, modification);
                EnergyManager.Instance.repairCostPerClick = Mathf.Max(1, Mathf.RoundToInt(newCost));
                //Debug.Log($"Applied global repair cost modification: {currentCost} -> {EnergyManager.Instance.repairCostPerClick}");
                return true;
        }

        return false;
    }

    private static object GetTargetObject(string targetType, AugmentTarget target)
    {
        switch (targetType.Trim().ToLower())
        {
            case "player":
                return target.Player;
            case "weapon":
                return target.Weapon;
            case "enemy":
                return target.Enemy;
            case "tower":
                return target.Tower;
            case "global":
                return target.GlobalContext;
            default:
                Debug.LogWarning($"StatApplicator: Unknown target type: '{targetType}'. Available: Player, Weapon, Enemy, Tower, Global");
                return null;
        }
    }

    private static bool ApplyToTarget(object targetObject, StatModification modification)
    {
        if (targetObject == null)
        {
            Debug.LogWarning($"StatApplicator: Target object is null for stat: {modification.StatName}");
            return false;
        }

        Type targetType = targetObject.GetType();

        // Try direct field access first
        FieldInfo field = targetType.GetField(modification.StatName,
            BindingFlags.Public | BindingFlags.Instance);

        if (field != null && IsNumericType(field.FieldType))
        {
            return ApplyToField(field, targetObject, modification);
        }

        // Try property access
        PropertyInfo property = targetType.GetProperty(modification.StatName,
            BindingFlags.Public | BindingFlags.Instance);

        if (property != null && property.CanWrite && IsNumericType(property.PropertyType))
        {
            return ApplyToProperty(property, targetObject, modification);
        }

        // Log if nothing was found
        Debug.LogWarning($"StatApplicator: No field or property '{modification.StatName}' found on target type {targetType.Name}");
        return false;
    }

    private static bool ApplyToField(FieldInfo field, object target, StatModification modification)
    {
        try
        {
            float currentValue = Convert.ToSingle(field.GetValue(target));
            float newValue = CalculateNewValue(currentValue, modification);

            // Clamp armor reduction to valid range (0-1)
            // TODO create universal method for all fields that should be between 0 and 1 or modify setter methods
            if (field.Name == "armorReduction")
            {
                newValue = Mathf.Clamp01(newValue);
                Debug.Log($"Clamped armorReduction to valid range: {newValue}");
            }

            // -----------------------------------------------------------------
            // Capacity fields (maxHealth / maxStamina / maxMana) MUST NOT be
            // written via raw reflection: doing so leaves currentHealth /
            // currentStamina / currentMana out of sync with the new cap and,
            // critically, never fires OnHealthChanged — so the HealthBarUI
            // keeps showing the old ratio and then "snaps" to a much smaller
            // fill the next time any damage or regen tick fires the event
            // (about healthRegenDelay seconds later). That's the "depleting
            // healthbar" bug for augment 32 — and the structurally identical
            // case for augment 38 (maxStamina) and any future maxMana augment.
            //
            // Route these through their animated setters instead. The setters:
            //   1. update the cap
            //   2. clamp / hold current value appropriately
            //   3. fire the change event so the UI redraws
            //   4. tween current value up to the new cap for a visible,
            //      satisfying fill animation on increases
            // -----------------------------------------------------------------
            if (field.Name == "maxHealth" && target is CharacterStats charStats)
            {
                charStats.SetMaxHealthAnimated(newValue);
                Debug.Log($"Applied {modification.OperationType} {modification.Value} to maxHealth (animated): {currentValue} -> {charStats.maxHealth}");
                return true;
            }

            if (target is PlayerStats playerStats)
            {
                if (field.Name == "maxStamina")
                {
                    playerStats.SetMaxStaminaAnimated(newValue);
                    Debug.Log($"Applied {modification.OperationType} {modification.Value} to maxStamina (animated): {currentValue} -> {playerStats.maxStamina}");
                    return true;
                }
                if (field.Name == "maxMana")
                {
                    playerStats.SetMaxManaAnimated(newValue);
                    Debug.Log($"Applied {modification.OperationType} {modification.Value} to maxMana (animated): {currentValue} -> {playerStats.maxMana}");
                    return true;
                }
            }

            field.SetValue(target, Convert.ChangeType(newValue, field.FieldType));
            Debug.Log($"Applied {modification.OperationType} {modification.Value} to {field.Name}: {currentValue} -> {newValue}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to apply modification to field {field.Name}: {e.Message}");
            return false;
        }
    }

    private static bool ApplyToProperty(PropertyInfo property, object target, StatModification modification)
    {
        try
        {
            float currentValue = Convert.ToSingle(property.GetValue(target));
            float newValue = CalculateNewValue(currentValue, modification);

            property.SetValue(target, Convert.ChangeType(newValue, property.PropertyType));
            Debug.Log($"Applied {modification.OperationType} {modification.Value} to {property.Name}: {currentValue} -> {newValue}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to apply modification to property {property.Name}: {e.Message}");
            return false;
        }
    }

    private static float CalculateNewValue(float currentValue, StatModification modification)
    {
        switch (modification.OperationType)
        {
            case StatModification.ModificationType.Add:
                return currentValue + modification.Value;
            case StatModification.ModificationType.Multiply:
                return currentValue * modification.Value;
            case StatModification.ModificationType.Set:
                return modification.Value;
            case StatModification.ModificationType.Percentage:
                return currentValue * (1f + modification.Value / 100f);
            default:
                return currentValue;
        }
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(float) || type == typeof(int) || type == typeof(double) ||
               type == typeof(decimal) || type == typeof(long) || type == typeof(short);
    }

    // Apply a modification straight to a concrete object (e.g. a WeaponData runtime
    // copy), bypassing the Player/Weapon target-routing special cases. Used to
    // re-apply persistent weapon-stat augments after a weapon hot-swap.
    public static bool ApplyDirectToObject(object targetObject, StatModification modification)
    {
        return ApplyToTarget(targetObject, modification);
    }
}

public class SymbiosisEffect : PlayerProximityEffect
{
    [System.NonSerialized]
    public float damageBoost = 1.0f;

    protected override void UpdateTowerBoost(Tower tower, bool shouldBoost)
    {
        var boostComp = tower.GetComponent<SymbiosisBoost>();

        if (shouldBoost && boostComp == null)
        {
            boostComp = tower.gameObject.AddComponent<SymbiosisBoost>();
            boostComp.damageMultiplier = damageBoost;

            var renderer = tower.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.color = Color.Lerp(Color.white, Color.green, 0.4f);
            }
        }
        else if (!shouldBoost && boostComp != null)
        {
            var renderer = tower.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.color = Color.white;
            Destroy(boostComp);
        }
        else if (shouldBoost && boostComp != null)
        {
            // Update boost value if it changed
            if (Mathf.Abs(boostComp.damageMultiplier - damageBoost) > 0.01f)
            {
                boostComp.damageMultiplier = damageBoost;
            }
        }
    }
}

public class SymbiosisBoost : MonoBehaviour
{
    public float damageMultiplier = 1.0f;

    public float GetDamageMultiplier()
    {
        return damageMultiplier;
    }
}

public class AutoGeneratedAugmentEffect : IAugmentEffect
{
    private AugmentData augmentData;

    public int AugmentID => augmentData.ID;
    public string EffectName => augmentData.Name;

    public AutoGeneratedAugmentEffect(AugmentData data)
    {
        augmentData = data;

        // Skip parsing for NULL stats augments
        if (string.IsNullOrEmpty(augmentData.AffectedStats) || augmentData.AffectedStats == "NULL")
        {
            augmentData.ParsedModifications = new List<StatModification>(); // Empty list
            return;
        }

        // Parse the affected stats when creating the effect
        augmentData.ParsedModifications = StatParser.ParseAffectedStats(
            augmentData.AffectedStats,
            augmentData.TargetTypes);
    }

    public void Apply(AugmentTarget target)
    {
        // Handle NULL stats augments
        if (augmentData.ParsedModifications == null || augmentData.ParsedModifications.Count == 0)
        {
            if (string.IsNullOrEmpty(augmentData.AffectedStats) || augmentData.AffectedStats == "NULL")
            {
                Debug.LogWarning($"Augment '{augmentData.Name}' has NULL stats - no automatic implementation available");
                return;
            }
            else
            {
                Debug.LogWarning($"No parsed modifications found for augment: {augmentData.Name}");
                return;
            }
        }

        int successfulModifications = 0;

        foreach (var modification in augmentData.ParsedModifications)
        {
            bool success = StatApplicator.ApplyModification(modification, target);
            if (success) successfulModifications++;
        }

        Debug.Log($"Applied {successfulModifications}/{augmentData.ParsedModifications.Count} modifications for augment: {augmentData.Name}");
    }

    public bool CanApplyTo(AugmentTarget target)
    {
        // Special case: Global enemy stat modifiers don't need an actual enemy
        if (augmentData.AffectsEnemy && IsGlobalEnemyStatAugment())
        {
            return true; // Can apply even with no enemies spawned yet
        }

        if (augmentData.AffectsPlayer && target.Player != null) return true;
        if (augmentData.AffectsEnemy && target.Enemy != null) return true;
        if (augmentData.AffectsTower && target.Tower != null) return true;
        if (augmentData.IsGlobal && target.GlobalContext != null) return true;

        return target.Player != null || target.Tower != null ||
               target.Enemy != null || target.GlobalContext != null;
    }

    // TODO move those Enemy debuffs to some common list
    private bool IsGlobalEnemyStatAugment()
    {
        if (!augmentData.AffectsEnemy) return false;

        string stats = augmentData.AffectedStats?.ToLower() ?? "";
        return stats.Contains("movespeed") ||
               stats.Contains("damage") ||
               stats.Contains("maxhealth");
    }

    public string GetDescription() => augmentData.Description;
}

// ===== RARITY-AWARE AUGMENT EFFECT =====
public class RarityAwareAugmentEffect : IAugmentEffect
{
    private AugmentData augmentData;
    private string selectedRarity;
    private float rarityMultiplier;
    private List<StatModification> scaledModifications;

    public int AugmentID => augmentData.ID;
    public string EffectName => augmentData.Name;

    public RarityAwareAugmentEffect(AugmentData data, string rarity, RarityConfiguration rarityConfig)
    {
        augmentData = data;
        selectedRarity = rarity ?? "Common";

        // Get random multiplier from configuration
        rarityMultiplier = rarityConfig != null ? rarityConfig.GetRandomMultiplier() : 1f;

        // Skip parsing for NULL stats augments
        if (string.IsNullOrEmpty(augmentData.AffectedStats) || augmentData.AffectedStats == "NULL")
        {
            scaledModifications = new List<StatModification>();
            return;
        }

        // Parse base modifications and apply rarity scaling
        var baseModifications = StatParser.ParseAffectedStats(
            augmentData.AffectedStats,
            augmentData.TargetTypes);

        scaledModifications = ApplyRarityScaling(baseModifications);
    }

    private List<StatModification> ApplyRarityScaling(List<StatModification> baseModifications)
    {
        var scaled = new List<StatModification>();

        foreach (var baseMod in baseModifications)
        {
            // Parry upgrade augments (330-333) are driven entirely by AugmentEffectHandler,
            // which reads their raw CSV values. They must never be rarity-scaled, and
            // PlayerStats has no matching fields, so skip them before they reach the
            // StatApplicator (avoids the misleading scaling log and the no-op warnings).
            if (!string.IsNullOrEmpty(baseMod.StatName) &&
                baseMod.StatName.StartsWith("parry_", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scaledMod = new StatModification
            {
                StatName = baseMod.StatName,
                OperationType = baseMod.OperationType,
                TargetType = baseMod.TargetType,
                Value = CalculateScaledValue(baseMod.Value, baseMod.OperationType, baseMod.StatName)
            };

            scaled.Add(scaledMod);

            Debug.Log($"Rarity scaling: {selectedRarity} - {baseMod.StatName}: Base {baseMod.Value} -> Scaled {scaledMod.Value:F2} (rarity: x{rarityMultiplier:F3})");
        }

        return scaled;
    }

    // Helper method for percentage-based augments
    private bool IsPerEventMultiplierStat(string statName)
    {
        string lower = statName.ToLower();
        return lower == "momentum_health" ||
               lower == "momentum_damage" ||
               lower == "player_damage_stack_kill" ||  // Berserker's Fury
               lower == "escalation_multiplier";       // Escalation
    }

    private bool IsAbsoluteValueStat(string statName)
    {
        string lower = statName.ToLower();
        return lower.Contains("duration") ||
               lower.Contains("chance") ||
               lower.Contains("infighting");
    }

    private bool IsAdrenalineRushStat(string statName)
    {
        string lower = statName.ToLower();
        return lower == "adrenaline_health_threshold" ||
               lower == "adrenaline_duration" ||
               lower == "adrenaline_cooldown" ||
               lower == "adrenaline_attack_speed" ||
               lower == "adrenaline_move_speed";
    }
    private float CalculateScaledValue(float baseValue, StatModification.ModificationType operationType, string statName)
    {
        // Parry upgrade augments (330–333) must NEVER be rarity-scaled — their
        // values are read raw from the CSV by AugmentEffectHandler. This guard is
        // defense-in-depth in case a "parry_*" stat ever flows through here.
        if (!string.IsNullOrEmpty(statName) &&
            statName.StartsWith("parry_", System.StringComparison.OrdinalIgnoreCase))
        {
            return baseValue;
        }

        if (statName == "berserker_damage_per_missing_health")
        {
            // Higher value = more damage bonus (better)
            return baseValue * rarityMultiplier;
            // Common (1.0x): 0.02 (2% per 1% missing = 200% at 0 HP)
            // Rare (1.24x): 0.025 (2.5% per 1% missing = 250% at 0 HP)
            // Epic (1.45x): 0.029 (2.9% per 1% missing = 290% at 0 HP)
            // Legendary (1.85x): 0.037 (3.7% per 1% missing = 370% at 0 HP)
        }
        if (statName == "berserker_defense_penalty")
        {
            // Lower penalty = better (less defense lost)
            return baseValue / rarityMultiplier;
            // Common (1.0x): 0.25 (lose 25% defense)
            // Rare (1.24x): 0.20 (lose 20% defense)
            // Epic (1.45x): 0.17 (lose 17% defense)
            // Legendary (1.85x): 0.14 (lose 14% defense)
        }

        // Handle adrenaline rush special stats - these should not be scaled by rarity
        if (IsAdrenalineRushStat(statName))
        {
            return baseValue; // Return as-is, no rarity scaling for these
        }

        // Handle absolute values that should scale proportionally (durations, chances, etc.)
        if (IsAbsoluteValueStat(statName) && operationType == StatModification.ModificationType.Set)
        {
            // Scale proportionally: treat the value as a bonus to scale
            return baseValue * rarityMultiplier;
        }


        // Handle per-event multiplier stats (momentum, berserker, escalation)
        // These use format 1.XX where 0.XX is the percentage bonus per event
        // Example: 1.03 means "+3% per kill", not "103% bonus"
        if (IsPerEventMultiplierStat(statName))
        {
            // Extract percentage part (1.03 → 0.03)
            float percentageBonus = baseValue - 1.0f;

            // Scale only the percentage (0.03 × 1.223 = 0.0367)
            float scaledPercentage = percentageBonus * rarityMultiplier;

            // Rebuild multiplier (1 + 0.0367 = 1.0367)
            float scaledValue = 1.0f + scaledPercentage;

            //Debug.Log($"[PER-EVENT SCALING] {statName}: {baseValue:F4} ({percentageBonus * 100:F2}% per event) → {scaledValue:F4} ({scaledPercentage * 100:F2}% per event)");

            return scaledValue;
        }

        // Check if this is an enemy debuff stat (special handling)
        bool isEnemyDebuff = IsEnemyDebuffStat(statName);

        // Check if this is an energy decay stat (special handling)
        bool isEnergyDecayStat = statName.ToLower().Contains("decay");

        // Handle energy decay multipliers (values < 1.0 should get smaller with higher rarity)
        if (operationType == StatModification.ModificationType.Multiply && isEnergyDecayStat && baseValue < 1.0f)
        {
            // For decay reduction: smaller values = better effect
            // Use power scaling to make the effect stronger at higher rarities
            return Mathf.Pow(baseValue, rarityMultiplier);
        }

        // Check if this is a cost-related stat
        bool isCostStat = IsCostRelatedStat(statName);

        // Handle multiplicative cost penalties (values > 1.0 for cost stats)
        if (operationType == StatModification.ModificationType.Multiply && isCostStat && baseValue > 1.0f)
        {
            // Use power-based reduction: penalty gets smaller at higher rarities
            return Mathf.Pow(baseValue, 1.0f / rarityMultiplier);
        }

        // Handle multiplicative cost discounts (values < 1.0 for cost stats) - bonuses
        if (operationType == StatModification.ModificationType.Multiply && isCostStat && baseValue < 1.0f)
        {
            // For costs, values < 1.0 are discounts - make them stronger at higher rarities
            float discountAmount = 1.0f - baseValue; // 0.25 for 0.75
            float enhancedDiscount = discountAmount * rarityMultiplier; // More discount at higher rarity
            float scaledValue = 1.0f - enhancedDiscount;
            return Mathf.Max(scaledValue, 0.01f); // Never go below 1% of original cost
        }

        // Handle enemy debuff multipliers (values < 1.0)
        if (operationType == StatModification.ModificationType.Multiply && baseValue < 1.0f)
        {
            if (isEnemyDebuff || IsBeneficialReductionStat(statName))
            {
                // Enemy debuffs AND beneficial player reductions (e.g. attackCooldown
                // *0.714 = faster attacks) get STRONGER at higher rarity, not weaker.
                // Example: 0.85 at Common -> 0.764 at Legendary.
                return Mathf.Pow(baseValue, rarityMultiplier);
            }
            else
            {
                // For player penalties: REDUCE the penalty at higher rarities
                float penaltyAmount = 1.0f - baseValue;
                float reducedPenalty = penaltyAmount / rarityMultiplier;
                float scaledValue = 1.0f - reducedPenalty;
                return Mathf.Min(scaledValue, 1.0f);
            }
        }

        // Handle additive cost penalties (positive values for cost stats)
        if (operationType == StatModification.ModificationType.Add && isCostStat && baseValue > 0f)
        {
            // Reduce penalty magnitude at higher rarities
            return baseValue / rarityMultiplier;
        }

        // Handle additive penalties (negative values) 
        if (operationType == StatModification.ModificationType.Add && baseValue < 0f)
        {
            // Reduce penalty magnitude at higher rarities
            float penaltyAmount = Mathf.Abs(baseValue);
            float reducedPenalty = penaltyAmount / rarityMultiplier;
            return -reducedPenalty;
        }

        // Handle percentage penalties (negative percentages) 
        if (operationType == StatModification.ModificationType.Percentage && baseValue < 0f)
        {
            // Reduce penalty magnitude at higher rarities
            float penaltyAmount = Mathf.Abs(baseValue);
            float reducedPenalty = penaltyAmount / rarityMultiplier;
            return -reducedPenalty;
        }

        // Multiplicative bonuses (value > 1.0): scale ONLY the bonus portion so the
        // rarity multiplier doesn't compound the implicit 1.0 base.
        // e.g. damage*1.15 at 1.10x -> 1 + 0.15*1.10 = 1.165 (not 1.265).
        if (operationType == StatModification.ModificationType.Multiply && baseValue > 1.0f)
        {
            float bonus = baseValue - 1.0f;
            return 1.0f + bonus * rarityMultiplier;
        }

        // Additive / Set bonuses: scale the added/flat amount directly.
        return baseValue * rarityMultiplier;
    }

    // Helper method to identify enemy debuff stats and stats that should not be scaled upwards
    private bool IsEnemyDebuffStat(string statName)
    {
        string lower = statName.ToLower();
        return lower == "movespeed" ||
               lower == "damage" ||
               lower == "maxhealth" ||
               lower == "enemy_spawn_count";
    }

    // Stats where a multiplier < 1.0 is a BENEFIT to the player (a reduction that
    // helps them, e.g. attackCooldown). Higher rarity should make the reduction
    // stronger, not weaker.
    private bool IsBeneficialReductionStat(string statName)
    {
        string lower = statName.ToLower();
        return lower == "attackcooldown";
    }

    private bool IsCostRelatedStat(string statName)
    {
        string lowerStatName = statName.ToLower();
        return lowerStatName.Contains("cost") ||
               lowerStatName.Contains("price") ||
               lowerStatName.Contains("expense") ||
               lowerStatName.Contains("decay") ||
               lowerStatName.Contains("consumption") ||
               lowerStatName.Equals("towerbuildcost") ||
               lowerStatName.Equals("repaircostperclick") ||
               lowerStatName.Equals("energycostmultiplier");
    }

    public void Apply(AugmentTarget target)
    {
        if (scaledModifications == null || scaledModifications.Count == 0)
        {
            if (string.IsNullOrEmpty(augmentData.AffectedStats) || augmentData.AffectedStats == "NULL")
            {
                Debug.Log($"Augment '{augmentData.Name}' has NULL stats, no stat modifications needed, handled by AugmentEffectHandler (unlocks, special abilities, etc.)  ");
                return;
            }
            else if (augmentData.AffectedStats.IndexOf("parry_", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Parry upgrades carry their tunables in AffectedStats for AugmentEffectHandler
                // to read, but apply no stat modifications here. Empty list is expected.
                Debug.Log($"Augment '{augmentData.Name}' is a parry upgrade - values applied by AugmentEffectHandler, no stat modifications needed.");
                return;
            }
            else
            {
                Debug.LogWarning($"No scaled modifications found for augment: {augmentData.Name}");
                return;
            }
        }

        int successfulModifications = 0;

        foreach (var modification in scaledModifications)
        {
            bool success = StatApplicator.ApplyModification(modification, target);
            if (success) successfulModifications++;
        }

        Debug.Log($"Applied {successfulModifications}/{scaledModifications.Count} modifications for {selectedRarity} augment: {augmentData.Name}");

        // Persist weapon-stat modifications (damage / attackCooldown on the player's
        // weapon) so they survive weapon hot-swaps, which rebuild a clean runtime copy.
        if (AugmentRegistry.Instance != null)
            AugmentRegistry.Instance.RegisterPersistentWeaponMods(scaledModifications);
    }

    // TODO: move this method to the common methods for both classes
    public bool CanApplyTo(AugmentTarget target)
    {
        // Special case: Global enemy stat modifiers don't need an actual enemy
        if (augmentData.AffectsEnemy && IsGlobalEnemyStatAugment())
        {
            return true; // Can apply even with no enemies spawned yet
        }

        if (augmentData.AffectsPlayer && target.Player != null) return true;
        if (augmentData.AffectsEnemy && target.Enemy != null) return true;
        if (augmentData.AffectsTower && target.Tower != null) return true;
        if (augmentData.IsGlobal && target.GlobalContext != null) return true;

        return target.Player != null || target.Tower != null ||
               target.Enemy != null || target.GlobalContext != null;
    }

    // Add this helper method to RarityAwareAugmentEffect class
    private bool IsGlobalEnemyStatAugment()
    {
        if (!augmentData.AffectsEnemy) return false;

        string stats = augmentData.AffectedStats?.ToLower() ?? "";
        return stats.Contains("movespeed") ||
               stats.Contains("damage") ||
               stats.Contains("maxhealth");
    }

    public string GetDescription()
    {
        string baseDesc = augmentData.Description;
        return $"{baseDesc} ({selectedRarity} - {(rarityMultiplier - 1f) * 100f:F1}% bonus)";
    }
}

public class AugmentRegistry : MonoBehaviour
{
    private static AugmentRegistry _instance;
    public static AugmentRegistry Instance => _instance;

    [Header("Configuration")]
    public string csvResourcePath = "Data/augments";
    public string spriteBasePath = "Sprites/Augments/";

    [Header("Consolidated Rarity Configuration")]
    private RarityConfiguration[] rarityConfigurations;

    [Header("Debug")]
    public bool debugMode = false;

    // Core data 
    [System.NonSerialized]
    private Dictionary<int, IAugmentEffect> registeredEffects = new Dictionary<int, IAugmentEffect>();
    [System.NonSerialized]
    private Dictionary<int, AugmentData> augmentDatabase = new Dictionary<int, AugmentData>();
    private List<int> appliedAugments = new List<int>();

    // Events
    public System.Action<AugmentData> OnAugmentApplied;
    public System.Action OnDatabaseLoaded;

    [ContextMenu("Find Broken Priority 0 Augments")]
    void FindBrokenAugments()
    {
        var allAugments = AugmentRegistry.Instance.GetAllAugments()
            .Where(a => a.Priority == 0).ToList();
        var brokenIDs = new List<int>();
        foreach (var augment in allAugments)
        {
            bool willWork = false;
            // Check if it has valid affected stats
            if (!string.IsNullOrEmpty(augment.AffectedStats) &&
                augment.AffectedStats != "NULL")
            {
                // Check if stats parsed successfully
                if (augment.ParsedModifications != null &&
                    augment.ParsedModifications.Count > 0)
                {
                    willWork = true;
                }
            }

            if (!willWork)
            {
                brokenIDs.Add(augment.ID);
                Debug.LogWarning($"❌ ID {augment.ID}: '{augment.Name}' - Stats: '{augment.AffectedStats}'");
            }
            else
            {
                Debug.Log($"✅ ID {augment.ID}: '{augment.Name}' - WORKS");
            }
        }

        Debug.Log($"\nBROKEN AUGMENT IDs: {string.Join(", ", brokenIDs)}");
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeRegistry();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeRegistry()
    {
        LoadAugmentDatabase();
        AutoGenerateAllEffects();
        LoadAugmentSprites();

        rarityConfigurations = new RarityConfiguration[]
        {
        new RarityConfiguration
        {
            rarityName = "Common",
            probabilityWeight = 50f,
            minBonusPercent = 0f,
            maxBonusPercent = 10f,
            rarityColor = Color.green
        },
        new RarityConfiguration
        {
            rarityName = "Rare",
            probabilityWeight = 30f,
            minBonusPercent = 20f,
            maxBonusPercent = 30f,
            rarityColor = Color.blue
        },
        new RarityConfiguration
        {
            rarityName = "Epic",
            probabilityWeight = 15f,
            minBonusPercent = 40f,
            maxBonusPercent = 50f,
            rarityColor = new Color(0.8f, 0f, 1f)
        },
        new RarityConfiguration
        {
            rarityName = "Legendary",
            probabilityWeight = 5f,
            minBonusPercent = 70f,
            maxBonusPercent = 100f,
            rarityColor = new Color(1f, 0.6f, 0f)
        }
        };


        if (debugMode)
        {
            Debug.Log($"AugmentRegistry: Auto-generated {registeredEffects.Count} effects from {augmentDatabase.Count} CSV entries");
            Debug.Log($"AugmentRegistry: Configured with {rarityConfigurations.Length} rarity tiers");
        }

        OnDatabaseLoaded?.Invoke();
    }

    // ===== CONSOLIDATED RARITY METHODS =====
    public Dictionary<string, float> GetRarityWeights()
    {
        var weights = new Dictionary<string, float>();
        foreach (var config in rarityConfigurations)
        {
            weights[config.rarityName] = config.probabilityWeight;
        }
        return weights;
    }

    public Dictionary<string, Color> GetRarityColors()
    {
        var colors = new Dictionary<string, Color>();
        foreach (var config in rarityConfigurations)
        {
            colors[config.rarityName] = config.rarityColor;
        }
        return colors;
    }

    public RarityConfiguration GetRarityConfiguration(string rarityName)
    {
        foreach (var config in rarityConfigurations)
        {
            if (config.rarityName.Equals(rarityName, StringComparison.OrdinalIgnoreCase))
            {
                return config;
            }
        }

        // Return default (first) if not found
        if (rarityConfigurations.Length > 0)
        {
            Debug.LogWarning($"Rarity '{rarityName}' not found, using default: {rarityConfigurations[0].rarityName}");
            return rarityConfigurations[0];
        }

        // Fallback if no configurations exist
        Debug.LogError("No rarity configurations found! Creating default Common rarity.");
        return new RarityConfiguration { rarityName = "Common", minBonusPercent = 0f, maxBonusPercent = 10f };
    }

    public Color GetRarityColor(string rarityName)
    {
        var config = GetRarityConfiguration(rarityName);
        return config.rarityColor;
    }

    [ContextMenu("Test Rarity Scaling")]
    public void TestRarityScaling()
    {
        Debug.Log("=== Testing Rarity Scaling ===");

        foreach (var config in rarityConfigurations)
        {
            Debug.Log($"Rarity: {config.rarityName}");
            Debug.Log($"  Weight: {config.probabilityWeight}");
            Debug.Log($"  Range: {config.minBonusPercent}% - {config.maxBonusPercent}%");

            // Test 5 random rolls
            for (int i = 0; i < 5; i++)
            {
                float multiplier = config.GetRandomMultiplier();
                float bonusPercent = (multiplier - 1f) * 100f;
                Debug.Log($"  Roll {i + 1}: {bonusPercent:F1}% bonus (multiplier: {multiplier:F3})");
            }
        }
    }

    // ===== CSV LOADING =====
    private void LoadAugmentDatabase()
    {
        Debug.Log($"AugmentRegistry: Attempting to load CSV from: {csvResourcePath}");

        TextAsset csvFile = Resources.Load<TextAsset>(csvResourcePath);
        if (csvFile == null)
        {
            Debug.LogError($"AugmentRegistry: CSV file not found at Resources/{csvResourcePath}");
            Debug.LogError($"Make sure the file is at: Assets/Resources/{csvResourcePath}.csv");
            return;
        }

        Debug.Log($"AugmentRegistry: CSV file loaded, size: {csvFile.text.Length} characters");

        string[] lines = csvFile.text.Split('\n');
        Debug.Log($"AugmentRegistry: CSV has {lines.Length} lines");

        int loadedCount = 0;
        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] values = ParseCSVLine(line);
            if (values.Length >= 10)
            {
                try
                {
                    var augment = new AugmentData
                    {
                        ID = ParseInt(values[0]),
                        Priority = ParseInt(values[1]),
                        Name = values[2].Trim('"'),
                        Category = values[3].Trim('"'),
                        Rarity = values[4].Trim('"'),
                        AffectedStats = values[5].Trim('"'),
                        Description = values[6].Trim('"'),
                        SpritePath = values[7].Trim('"'),
                        WhoImplements = values[8].Trim('"'),
                        TargetTypes = string.IsNullOrWhiteSpace(values[9].Trim('"')) ? "Player" : values[9].Trim('"') // Default to Player if empty
                    };

                    // Validate essential fields
                    if (augment.ID <= 0)
                    {
                        Debug.LogWarning($"AugmentRegistry: Invalid ID in line {i}: {line}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(augment.Name))
                    {
                        Debug.LogWarning($"AugmentRegistry: Empty name in line {i}: {line}");
                        continue;
                    }

                    if (!augmentDatabase.ContainsKey(augment.ID))
                    {
                        augmentDatabase.Add(augment.ID, augment);
                        loadedCount++;

                        if (debugMode)
                        {
                            //Debug.Log($"Loaded augment: ID={augment.ID}, Name={augment.Name}, Stats={augment.AffectedStats}, Targets={augment.TargetTypes}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"AugmentRegistry: Duplicate ID {augment.ID} found in line {i}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"AugmentRegistry: Error parsing line {i}: {e.Message}. Line content: {line}");
                }
            }
            else
            {
                Debug.LogWarning($"AugmentRegistry: Line {i} has insufficient columns ({values.Length}): {line}");
            }
        }

        //Debug.Log($"AugmentRegistry: Successfully loaded {loadedCount} augments from CSV");
    }

    private string[] ParseCSVLine(string line)
    {
        List<string> result = new List<string>();
        bool inQuotes = false;
        string currentField = "";

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                // Check for escaped quotes ("")
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField += '"'; // Add single quote for escaped double quote
                    i++; // Skip the next quote
                }
                else
                {
                    inQuotes = !inQuotes; // Toggle quote state
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField);
                currentField = "";
            }
            else
            {
                currentField += c;
            }
        }

        // Add the last field
        result.Add(currentField);

        return result.ToArray();
    }

    private int ParseInt(string value)
    {
        return int.TryParse(value.Trim('"'), out int result) ? result : 0;
    }

    // ===== AUTO-GENERATION OF ALL EFFECTS =====
    private void AutoGenerateAllEffects()
    {
        foreach (var kvp in augmentDatabase)
        {
            var augmentData = kvp.Value;

            // Create auto-generated effect for this augment
            var autoEffect = new AutoGeneratedAugmentEffect(augmentData);
            registeredEffects[augmentData.ID] = autoEffect;

            if (debugMode)
            {
                //Debug.Log($"Auto-generated effect for: {augmentData.Name} (ID: {augmentData.ID}) - Stats: {augmentData.AffectedStats}");
            }
        }
    }

    // ===== SPRITE LOADING =====
    private void LoadAugmentSprites()
    {
        var allSprites = Resources.LoadAll<Sprite>(spriteBasePath);
        var spriteDict = allSprites.ToDictionary(s => s.name, s => s);

        foreach (var kvp in augmentDatabase)
        {
            var augment = kvp.Value;
            if (!string.IsNullOrEmpty(augment.SpritePath))
            {
                string spriteName = System.IO.Path.GetFileNameWithoutExtension(augment.SpritePath);
                if (spriteDict.TryGetValue(spriteName, out Sprite sprite))
                {
                    augment.Icon = sprite;
                }
                else if (spriteDict.TryGetValue(augment.ID.ToString(), out sprite))
                {
                    augment.Icon = sprite;
                }
            }
        }
    }

    // ===== PUBLIC API =====
    public bool ApplyAugment(int augmentID)
    {
        return ApplyAugment(augmentID, "Common"); // Default to Common rarity
    }

    // Rarity-aware method
    public bool ApplyAugment(int augmentID, string selectedRarity)
    {
        if (!augmentDatabase.TryGetValue(augmentID, out AugmentData augmentData))
        {
            Debug.LogError($"AugmentRegistry: Augment {augmentID} not found in database");
            return false;
        }

        // Get rarity configuration
        var rarityConfig = GetRarityConfiguration(selectedRarity);

        // Create rarity-specific effect
        var rarityEffect = new RarityAwareAugmentEffect(augmentData, selectedRarity, rarityConfig);

        // Special handling check if this is a multi-target synergy augment (affects both Player and Tower - like coordination)
        if (augmentData.AffectsPlayer && augmentData.AffectsTower)
        {
            //Debug.Log($"[AUGMENT] Detected multi-target synergy augment: {augmentData.Name}");

            // Create a target with BOTH player and tower
            AugmentTarget target = CreateTargetForAugment(augmentData);

            if (target == null || !rarityEffect.CanApplyTo(target))
            {
                Debug.LogError($"AugmentRegistry: Cannot apply multi-target augment {augmentID} to current target");
                return false;
            }

            rarityEffect.Apply(target);
            appliedAugments.Add(augmentID);

            if (debugMode)
            {
                Debug.Log($"Applied {selectedRarity} multi-target augment: {augmentData.Name} (ID: {augmentID})");
            }

            OnAugmentApplied?.Invoke(augmentData);
            return true;
        }

        // Special handling for tower-only augments
        if (augmentData.AffectsTower && !augmentData.AffectsPlayer)
        {
            return ApplyTowerAugment(augmentID, augmentData, rarityEffect);
        }

        // Logic for non-tower augments
        AugmentTarget target2 = CreateTargetForAugment(augmentData);
        if (target2 == null || !rarityEffect.CanApplyTo(target2))
        {
            Debug.LogError($"AugmentRegistry: Cannot apply augment {augmentID} to current target");
            return false;
        }

        rarityEffect.Apply(target2);
        appliedAugments.Add(augmentID);

        if (debugMode)
        {
            Debug.Log($"Applied {selectedRarity} augment: {augmentData.Name} (ID: {augmentID})");
        }

        OnAugmentApplied?.Invoke(augmentData);
        return true;
    }

    private bool ApplyTowerAugment(int augmentID, AugmentData augmentData, IAugmentEffect effect)
    {
        var allTowers = UnityEngine.Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);

        if (allTowers.Length == 0)
        {
            Debug.LogWarning($"AugmentRegistry: No towers found to apply augment {augmentID} ({augmentData.Name}) - will apply to future towers");
        }
        else
        {
            int successCount = 0;

            foreach (var tower in allTowers)
            {
                var target = new AugmentTarget(null as PlayerStats) { Tower = tower };

                if (effect.CanApplyTo(target))
                {
                    try
                    {
                        effect.Apply(target);
                        successCount++;

                        if (debugMode)
                        {
                            Debug.Log($"Applied tower augment '{augmentData.Name}' to existing tower: {tower.towerName} at {tower.transform.position}");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Failed to apply tower augment '{augmentData.Name}' to tower {tower.towerName}: {e.Message}");
                    }
                }
            }

            //Debug.Log($"Applied tower augment '{augmentData.Name}' to {successCount}/{allTowers.Length} existing towers");
        }

        appliedAugments.Add(augmentID);
        OnAugmentApplied?.Invoke(augmentData);

        //Debug.Log($"Tower augment '{augmentData.Name}' will now apply to all future towers");
        return true;
    }

    [ContextMenu("Test All Tower Augments")]
    public void TestAllTowerAugments()
    {
        var towerAugments = GetAugmentsByCategory("Tower")
            .Where(a => a.Priority == 0)
            .ToList();

        //Debug.Log($"=== Testing {towerAugments.Count} Tower Augments ===");

        foreach (var augment in towerAugments)
        {
            TestSingleTowerAugment(augment);
        }
    }

    public void TestSingleTowerAugment(AugmentData augment)
    {
        //Debug.Log($"\n--- Testing Tower Augment: {augment.Name} (ID: {augment.ID}) ---");
        //Debug.Log($"Affected Stats: {augment.AffectedStats}");
        //Debug.Log($"Target Types: {augment.TargetTypes}");

        // Handle NULL stats augments
        if (string.IsNullOrEmpty(augment.AffectedStats) || augment.AffectedStats == "NULL")
        {
            Debug.LogWarning($"⚠️ Augment '{augment.Name}' has NULL stats - requires custom implementation");
            return;
        }

        if (augment.ParsedModifications == null || augment.ParsedModifications.Count == 0)
        {
            Debug.LogError($"❌ No parsed modifications for augment: {augment.Name}");
            return;
        }

        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        if (towers.Length == 0)
        {
            Debug.LogWarning($"⚠️ No towers in scene to test augment: {augment.Name}");
            return;
        }

        bool allModificationsValid = true;
        foreach (var mod in augment.ParsedModifications)
        {
            //Debug.Log($"  Testing modification: {mod.StatName} {mod.OperationType} {mod.Value} (Target: {mod.TargetType})");

            // Handle global repair cost effect
            if (mod.StatName == "tower_repair_cost")
            {
                //Debug.Log($"    ✅ Global repair cost effect (affects EnergyManager)");
                continue;
            }

            // Check if stat exists on tower
            var tower = towers[0];
            var field = tower.GetType().GetField(mod.StatName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var property = tower.GetType().GetProperty(mod.StatName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (field == null && property == null)
            {
                Debug.LogError($"    ❌ Stat '{mod.StatName}' not found on Tower class");
                allModificationsValid = false;
            }
            else
            {
                Debug.Log($"    ✅ Stat '{mod.StatName}' found on Tower class");
            }
        }

        if (allModificationsValid)
        {
            Debug.Log($"✅ Tower augment '{augment.Name}' appears to be correctly implemented");
        }
        else
        {
            Debug.LogError($"❌ Tower augment '{augment.Name}' has implementation issues");
        }
    }

    private bool IsGlobalEffect(string statName)
    {
        // List of stat names that are global effects, not Tower fields
        return statName == "GLOBAL_tower_repair_cost" ||
               statName == "tower_repair_cost" ||
               statName.StartsWith("GLOBAL_");
    }

    private AugmentTarget CreateTargetForAugment(AugmentData augment)
    {
        //Debug.Log($"[CreateTargetForAugment] Augment={augment.Name}, TargetTypes={augment.TargetTypes}");

        var target = new AugmentTarget(null as PlayerStats);

        if (augment.AffectsPlayer)
            target.Player = FindFirstObjectByType<PlayerStats>();

        if (augment.AffectsWeapon)
            target.Weapon = FindFirstObjectByType<Weapon>()?.GetWeaponData();

        // Don't require actual enemy for global enemy stat modifiers
        if (augment.AffectsEnemy)
        {
            // Only try to find enemy if this is NOT a global stat modifier
            if (!IsGlobalEnemyStatAugment(augment))
            {
                target.Enemy = FindFirstObjectByType<EnemyStats>();
            }
            // For global stat modifiers, leave Enemy null - we use the manager instead
        }

        if (augment.AffectsTower)
        {
            target.Tower = FindFirstObjectByType<Tower>();
            var allTowers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
            //Debug.Log($"[CreateTargetForAugment] Found {allTowers.Length} towers in scene for augment: {augment.Name}");
        }

        if (augment.IsGlobal || augment.AffectedStats.Contains("repair_cost") || augment.AffectedStats.Contains("build_cost"))
            target.GlobalContext = EnergyManager.Instance;

        return target;
    }

    private bool IsGlobalEnemyStatAugment(AugmentData augment)
    {
        if (!augment.AffectsEnemy) return false;

        // Check if stats are simple global modifiers
        string stats = augment.AffectedStats?.ToLower() ?? "";
        return stats.Contains("movespeed") ||
               stats.Contains("damage") ||
               stats.Contains("maxhealth");
    }

    // Method to get effects for newly created towers 
    public IAugmentEffect GetEffect(int augmentId)
    {
        return registeredEffects.TryGetValue(augmentId, out IAugmentEffect effect) ? effect : null;
    }

    // Method to apply augments to a single new tower
    public void ApplyTowerAugmentsToSingleTower(Tower tower)
    {
        if (tower == null) return;

        var appliedAugmentsList = GetAppliedAugments();

        foreach (int augmentId in appliedAugmentsList)
        {
            var augmentData = GetAugmentData(augmentId);
            if (augmentData != null && augmentData.AffectsTower)
            {
                var effect = GetEffect(augmentId);
                if (effect != null)
                {
                    var target = new AugmentTarget(null as PlayerStats) { Tower = tower };
                    if (effect.CanApplyTo(target))
                    {
                        try
                        {
                            effect.Apply(target);
                            //Debug.Log($"Applied existing augment '{augmentData.Name}' to new tower: {tower.towerName}");
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"Failed to apply augment '{augmentData.Name}' to new tower: {e.Message}");
                        }
                    }
                }
            }
        }
    }

    // Getters 
    public AugmentData GetAugmentData(int id) => augmentDatabase.TryGetValue(id, out var data) ? data : null;
    public List<AugmentData> GetAllAugments() => augmentDatabase.Values.ToList();
    public List<AugmentData> GetAugmentsByCategory(string category) =>
        augmentDatabase.Values.Where(a => a.Category.Equals(category, System.StringComparison.OrdinalIgnoreCase)).ToList();
    public List<AugmentData> GetAugmentsByRarity(string rarity) =>
        augmentDatabase.Values.Where(a => a.Rarity.Equals(rarity, System.StringComparison.OrdinalIgnoreCase)).ToList();
    public List<int> GetAppliedAugments() => new List<int>(appliedAugments);
    public bool IsAugmentApplied(int id) => appliedAugments.Contains(id);
    public bool HasImplementation(int id) => registeredEffects.ContainsKey(id);
    public RarityConfiguration[] GetRarityConfigurations() => rarityConfigurations;

    // ===== PERSISTENT WEAPON-STAT AUGMENTS =====
    // Weapon hot-swaps rebuild a clean WeaponData runtime copy, which would otherwise
    // discard damage/attackCooldown buffs from augments (1, 7, 31, ...). We remember
    // the scaled modifications here and re-apply them to each new runtime copy.
    [System.NonSerialized]
    private readonly List<StatModification> persistentWeaponStatMods = new List<StatModification>();

    private static bool IsPersistentWeaponStat(StatModification m)
    {
        if (m == null) return false;
        bool rightStat = m.StatName == "damage" || m.StatName == "attackCooldown";
        bool rightTarget = m.TargetType == "Player" || m.TargetType == "Weapon";
        return rightStat && rightTarget;
    }

    // Called when an augment is applied; stores any weapon-stat mods for re-application.
    public void RegisterPersistentWeaponMods(IEnumerable<StatModification> mods)
    {
        if (mods == null) return;
        foreach (var m in mods)
            if (IsPersistentWeaponStat(m))
                persistentWeaponStatMods.Add(m);
    }

    // Called by Weapon after creating a fresh runtime copy (hot-swap or initial equip).
    public void ReapplyWeaponStatAugments(WeaponData runtimeCopy)
    {
        if (runtimeCopy == null || persistentWeaponStatMods.Count == 0) return;
        foreach (var m in persistentWeaponStatMods)
            StatApplicator.ApplyDirectToObject(runtimeCopy, m);
    }
}

// ===== INTERFACE DEFINITIONS =====
public interface IAugmentEffect
{
    int AugmentID { get; }
    string EffectName { get; }
    void Apply(AugmentTarget target);
    bool CanApplyTo(AugmentTarget target);
    string GetDescription();
}

public class AugmentTarget
{
    public PlayerStats Player { get; set; }
    public EnemyStats Enemy { get; set; }
    public Tower Tower { get; set; }
    public EnergyManager GlobalContext { get; set; }
    public WeaponData Weapon { get; set; }

    public AugmentTarget(PlayerStats player) => Player = player;
    public AugmentTarget(EnemyStats enemy) => Enemy = enemy;
    public AugmentTarget(Tower tower) => Tower = tower;
    public AugmentTarget(EnergyManager global) => GlobalContext = global;
    public AugmentTarget(WeaponData weapon) => Weapon = weapon;

    public static AugmentTarget ForPlayer()
        => new AugmentTarget(UnityEngine.Object.FindFirstObjectByType<PlayerStats>());

    public static AugmentTarget ForGlobal()
        => new AugmentTarget(EnergyManager.Instance);

    public static AugmentTarget ForWeapon()
    {
        //Debug.Log("[ForWeapon] Start szukania komponentu Weapon w scenie...");

        var weapon = UnityEngine.Object.FindFirstObjectByType<Weapon>();
        if (weapon == null)
        {
            Debug.LogError("[ForWeapon] Nie znaleziono żadnego komponentu Weapon w scenie!");
            return null;
        }
        //Debug.Log("[ForWeapon] Weapon znaleziony na obiekcie: " + weapon.gameObject.name);

        if (weapon.GetWeaponData() == null)
        {
            Debug.LogError("[ForWeapon] Weapon znaleziony, ale pole weaponData jest PUSTE!");
            return null;
        }
        //Debug.Log("[ForWeapon] WeaponData poprawnie przypisane.");

        return new AugmentTarget(weapon.GetWeaponData());
    }
}

