
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

        //{ "player_armor", "currentArmor" },
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
            Debug.LogWarning($"StatParser: Could not parse stat expression: '{expression}'");
            return null;
        }

        string statName = match.Groups[1].Value;

        if (StatAliases.TryGetValue(statName, out string actualStatName))
        {
            Debug.Log($"StatParser: Mapping '{statName}' to '{actualStatName}'");
            statName = actualStatName;
        }

        string operatorStr = match.Groups[2].Value;
        if (!float.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            Debug.LogWarning($"StatParser: Could not parse numeric value in expression: '{expression}'");
            return null;
        }

        if (!OperatorMap.TryGetValue(operatorStr, out var operationType))
        {
            Debug.LogWarning($"StatParser: Unknown operator '{operatorStr}' in expression: '{expression}'");
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
    public static bool ApplyModification(StatModification modification, AugmentTarget target)
    {

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
        // Check if we have a valid target for this augment's requirements
        if (augmentData.AffectsPlayer && target.Player != null) return true;
        if (augmentData.AffectsEnemy && target.Enemy != null) return true;
        if (augmentData.AffectsTower && target.Tower != null) return true;
        if (augmentData.IsGlobal && target.GlobalContext != null) return true;

        // If no specific target type, check if any target is available
        return target.Player != null || target.Tower != null ||
               target.Enemy != null || target.GlobalContext != null;
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
    private float CalculateScaledValue(float baseValue, StatModification.ModificationType operationType, string statName)
    {
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


        /*
            // Handle multiplicative penalties (values < 1.0)
            if (operationType == StatModification.ModificationType.Multiply && baseValue < 1.0f)
            {
                // Reduce penalty at higher rarities capped at 1.0
                float penaltyAmount = 1.0f - baseValue;
                float reducedPenalty = penaltyAmount / rarityMultiplier; // Smaller penalty at higher rarity
                float scaledValue = 1.0f - reducedPenalty;
                return Mathf.Min(scaledValue, 1.0f);
            }
        */


        // Handle multiplicative cost discounts (values < 1.0 for cost stats) - bonuses
        if (operationType == StatModification.ModificationType.Multiply && isCostStat && baseValue < 1.0f)
        {
            // For costs, values < 1.0 are discounts - make them stronger at higher rarities
            float discountAmount = 1.0f - baseValue; // 0.25 for 0.75
            float enhancedDiscount = discountAmount * rarityMultiplier; // More discount at higher rarity
            float scaledValue = 1.0f - enhancedDiscount;
            return Mathf.Max(scaledValue, 0.01f); // Never go below 1% of original cost
        }

        // Handle multiplicative penalties (values < 1.0)
        if (operationType == StatModification.ModificationType.Multiply && baseValue < 1.0f)
        {
            // Reduce penalty at higher rarities capped at 1.0
            float penaltyAmount = 1.0f - baseValue;
            float reducedPenalty = penaltyAmount / rarityMultiplier;
            float scaledValue = 1.0f - reducedPenalty;
            return Mathf.Min(scaledValue, 1.0f);
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

        // For all positive values (bonuses), apply normal scaling
        return baseValue * rarityMultiplier;
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
                Debug.LogWarning($"Augment '{augmentData.Name}' has NULL stats - no automatic implementation available");
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
    }

    public bool CanApplyTo(AugmentTarget target)
    {
        if (augmentData.AffectsPlayer && target.Player != null) return true;
        if (augmentData.AffectsEnemy && target.Enemy != null) return true;
        if (augmentData.AffectsTower && target.Tower != null) return true;
        if (augmentData.IsGlobal && target.GlobalContext != null) return true;

        return target.Player != null || target.Tower != null ||
               target.Enemy != null || target.GlobalContext != null;
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
    public bool debugMode = true;

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
                            Debug.Log($"Loaded augment: ID={augment.ID}, Name={augment.Name}, Stats={augment.AffectedStats}, Targets={augment.TargetTypes}");
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

        Debug.Log($"AugmentRegistry: Successfully loaded {loadedCount} augments from CSV");
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
                Debug.Log($"Auto-generated effect for: {augmentData.Name} (ID: {augmentData.ID}) - Stats: {augmentData.AffectedStats}");
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
            Debug.Log($"[AUGMENT] Detected multi-target synergy augment: {augmentData.Name}");

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

            Debug.Log($"Applied tower augment '{augmentData.Name}' to {successCount}/{allTowers.Length} existing towers");
        }

        appliedAugments.Add(augmentID);
        OnAugmentApplied?.Invoke(augmentData);

        Debug.Log($"Tower augment '{augmentData.Name}' will now apply to all future towers");
        return true;
    }

    [ContextMenu("Test All Tower Augments")]
    public void TestAllTowerAugments()
    {
        var towerAugments = GetAugmentsByCategory("Tower")
            .Where(a => a.Priority == 0)
            .ToList();

        Debug.Log($"=== Testing {towerAugments.Count} Tower Augments ===");

        foreach (var augment in towerAugments)
        {
            TestSingleTowerAugment(augment);
        }
    }

    public void TestSingleTowerAugment(AugmentData augment)
    {
        Debug.Log($"\n--- Testing Tower Augment: {augment.Name} (ID: {augment.ID}) ---");
        Debug.Log($"Affected Stats: {augment.AffectedStats}");
        Debug.Log($"Target Types: {augment.TargetTypes}");

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
            Debug.Log($"  Testing modification: {mod.StatName} {mod.OperationType} {mod.Value} (Target: {mod.TargetType})");

            // Handle global repair cost effect
            if (mod.StatName == "tower_repair_cost")
            {
                Debug.Log($"    ✅ Global repair cost effect (affects EnergyManager)");
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
        Debug.Log($"[CreateTargetForAugment] Augment={augment.Name}, TargetTypes={augment.TargetTypes}");

        var target = new AugmentTarget(null as PlayerStats);

        if (augment.AffectsPlayer)
            target.Player = FindFirstObjectByType<PlayerStats>();

        if (augment.AffectsWeapon)
            target.Weapon = FindFirstObjectByType<Weapon>()?.GetWeaponData();

        if (augment.AffectsEnemy)
            target.Enemy = FindFirstObjectByType<EnemyStats>();

        if (augment.AffectsTower)
        {
            target.Tower = FindFirstObjectByType<Tower>();
            var allTowers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
            Debug.Log($"[CreateTargetForAugment] Found {allTowers.Length} towers in scene for augment: {augment.Name}");
        }

        // For global effects (like repair costs) - provide global context
        if (augment.IsGlobal || augment.AffectedStats.Contains("repair_cost") || augment.AffectedStats.Contains("build_cost"))
            target.GlobalContext = EnergyManager.Instance;

        return target;
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
                            Debug.Log($"Applied existing augment '{augmentData.Name}' to new tower: {tower.towerName}");
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
        Debug.Log("[ForWeapon] Start szukania komponentu Weapon w scenie...");

        var weapon = UnityEngine.Object.FindFirstObjectByType<Weapon>();
        if (weapon == null)
        {
            Debug.LogError("[ForWeapon] Nie znaleziono żadnego komponentu Weapon w scenie!");
            return null;
        }
        Debug.Log("[ForWeapon] Weapon znaleziony na obiekcie: " + weapon.gameObject.name);

        if (weapon.GetWeaponData() == null)
        {
            Debug.LogError("[ForWeapon] Weapon znaleziony, ale pole weaponData jest PUSTE!");
            return null;
        }
        Debug.Log("[ForWeapon] WeaponData poprawnie przypisane.");

        return new AugmentTarget(weapon.GetWeaponData());
    }
}

public class TowerCommanderEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float energyDecayMultiplier = 1.0f; // This will be set from CSV (e.g., 0.8 for 20% reduction)
    private const float RANGE = 2.5f;

    void Start()
    {
        Debug.Log($"[TOWER_COMMANDER] Start() called! energyDecayMultiplier={energyDecayMultiplier}");
        Debug.Log($"[TOWER_COMMANDER] Component on object: {gameObject.name}, enabled: {enabled}");

        //Debug.Log($"[TOWER_COMMANDER] TowerCommanderEffect started! Decay multiplier: {energyDecayMultiplier}, Range: {RANGE}");
        // Validate the energy decay multiplier value
        if (energyDecayMultiplier <= 0f)
        {
            //Debug.LogWarning($"[TOWER_COMMANDER] Invalid energyDecayMultiplier value: {energyDecayMultiplier}, setting to 1.0 (no reduction)");
            energyDecayMultiplier = 1.0f;
        }

        // Do an immediate update and then start the repeating updates
        UpdateTowers();
        InvokeRepeating(nameof(UpdateTowers), 0.1f, 0.3f);
        //Debug.Log($"[TOWER_COMMANDER] InvokeRepeating set up successfully");

    }
    void UpdateTowers()
    {
        //Debug.Log($"[TOWER_COMMANDER] === UpdateTowers called! ===");

        try
        {
            Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
            Vector3 playerPos = transform.position;

            //Debug.Log($"[TOWER_COMMANDER] Found {towers.Length} towers, player at {playerPos}");

            foreach (Tower tower in towers)
            {
                if (tower == null || tower.IsDestroyed())
                {
                    //Debug.Log($"[TOWER_COMMANDER] Skipping null/destroyed tower");
                    continue;
                }

                float distance = Vector3.Distance(playerPos, tower.transform.position);
                bool shouldBoost = distance <= RANGE;

                //Debug.Log($"[TOWER_COMMANDER] Tower '{tower.towerName}' at distance {distance:F2}, shouldBoost={shouldBoost}, RANGE={RANGE}");

                var boostComp = tower.GetComponent<TowerCommanderBoost>();

                if (shouldBoost && boostComp == null)
                {
                    //Debug.Log($"[TOWER_COMMANDER] ✓ ADDING boost to {tower.towerName}");
                    boostComp = tower.gameObject.AddComponent<TowerCommanderBoost>();
                    boostComp.energyDecayMultiplier = energyDecayMultiplier;

                    var renderer = tower.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        renderer.color = Color.Lerp(Color.white, Color.cyan, 0.3f);
                    }
                }
                else if (!shouldBoost && boostComp != null)
                {
                    //Debug.Log($"[TOWER_COMMANDER] ✗ REMOVING boost from {tower.towerName}");
                    var renderer = tower.GetComponent<SpriteRenderer>();
                    if (renderer != null) renderer.color = Color.white;

                    Destroy(boostComp);
                }
                else if (shouldBoost && boostComp != null)
                {
                    //Debug.Log($"[TOWER_COMMANDER] Tower {tower.towerName} already has boost");
                }
                else
                {
                    //Debug.Log($"[TOWER_COMMANDER] Tower {tower.towerName} out of range, no boost to remove");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TOWER_COMMANDER] UpdateTowers exception: {e.Message}\n{e.StackTrace}");
        }
    }

    // Public method to force an immediate update when new towers are built
    public void ForceUpdate()
    {
        UpdateTowers();
    }
    void OnDisable()
    {
        CancelInvoke(nameof(UpdateTowers));
    }
}

public class TowerCommanderBoost : MonoBehaviour
{
    public float energyDecayMultiplier = 1.0f;

    public float GetEnergyDecayMultiplier()
    {
        return energyDecayMultiplier;
    }
}


// Base class for player proximity effects on towers
public abstract class PlayerProximityEffect : MonoBehaviour
{
    protected const float RANGE = 2.5f;
    protected float updateInterval = 0.3f;

    protected virtual float GetRange() => RANGE;

    void Start()
    {
        OnEffectStart();
        InvokeRepeating(nameof(UpdateTowers), 0f, updateInterval);
    }

    protected virtual void OnEffectStart() { }

    void UpdateTowers()
    {
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        Vector3 playerPos = transform.position;

        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsDestroyed()) continue;

            float distance = Vector3.Distance(playerPos, tower.transform.position);
            bool shouldBoost = distance <= GetRange();

            UpdateTowerBoost(tower, shouldBoost);
        }

        // TODO - potentially update player based on nearby tower count
        OnProximityUpdate(towers, playerPos);
    }

    protected abstract void UpdateTowerBoost(Tower tower, bool shouldBoost);
    protected virtual void OnProximityUpdate(Tower[] allTowers, Vector3 playerPos) { }
}

public class PlayerTowerCoordinationEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float playerArmorBonus = 0f; // Flat armor for player (from CSV)
    [System.NonSerialized]
    public float towerArmorBonus = 0f; // Percentage armor for towers (from CSV)

    private const float RANGE = 3.5f;

    private PlayerStats playerStats;
    private float basePlayerArmor;

    void Start()
    {
        playerStats = GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            basePlayerArmor = playerStats.currentArmor;
            Debug.Log($"[COORDINATION] Effect started. Base player armor: {basePlayerArmor}, Bonus: +{playerArmorBonus:F1} flat, Tower bonus: +{towerArmorBonus:F3}");
        }

        InvokeRepeating(nameof(UpdateCoordination), 0f, 0.3f);
    }

    void UpdateCoordination()
    {
        if (playerStats == null) return;

        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        Vector3 playerPos = transform.position;
        int nearbyCount = 0;

        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsDestroyed()) continue;

            float distance = Vector3.Distance(playerPos, tower.transform.position);
            bool shouldBoost = distance <= RANGE;

            var boostComp = tower.GetComponent<CoordinationArmorBoost>();

            if (shouldBoost)
            {
                nearbyCount++;

                if (boostComp == null)
                {
                    float towerArmorBefore = tower.armorReduction;

                    boostComp = tower.gameObject.AddComponent<CoordinationArmorBoost>();
                    boostComp.armorBonus = towerArmorBonus; // Use the CSV value
                    boostComp.ApplyBoost(tower);

                    float towerArmorAfter = tower.armorReduction;
                    Debug.Log($"[COORDINATION] Tower '{tower.towerName}': {towerArmorBefore:F3} -> {towerArmorAfter:F3} (clamped to max 1.0)");

                    var renderer = tower.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        renderer.color = Color.Lerp(Color.white, new Color(0.7f, 0.8f, 1f), 0.35f);
                    }
                }
                else
                {
                    // Update existing boost if the bonus changed
                    boostComp.armorBonus = towerArmorBonus;
                    boostComp.ApplyBoost(tower);
                }
            }
            else if (!shouldBoost && boostComp != null)
            {
                boostComp.RemoveBoost(tower);

                var renderer = tower.GetComponent<SpriteRenderer>();
                if (renderer != null) renderer.color = Color.white;

                Destroy(boostComp);
            }
        }

        // Apply player armor bonus
        float newPlayerArmor = nearbyCount > 0 ? basePlayerArmor + playerArmorBonus : basePlayerArmor;

        if (Mathf.Abs(playerStats.currentArmor - newPlayerArmor) > 0.01f)
        {
            playerStats.currentArmor = newPlayerArmor;
            Debug.Log($"[COORDINATION] Player armor: {newPlayerArmor:F1} ({nearbyCount} towers, bonus: +{playerArmorBonus:F1} flat)");
        }
    }

    void OnDestroy()
    {
        if (playerStats != null)
        {
            playerStats.currentArmor = basePlayerArmor;
        }
    }
}

public class CoordinationArmorBoost : MonoBehaviour
{
    public float armorBonus = 0f; // From CSV, will be added to tower's armor
    private float originalArmor = -1f;

    public void ApplyBoost(Tower tower)
    {
        if (tower == null) return;
        if (originalArmor < 0f) originalArmor = tower.armorReduction;

        // Add bonus and CLAMP to prevent going above 1.0 (which would cause extra damage)
        float newArmor = originalArmor + armorBonus;
        tower.armorReduction = Mathf.Clamp(newArmor, 0f, 1.0f);

        if (newArmor > 1.0f)
        {
            Debug.LogWarning($"[COORDINATION] Tower armor capped at 100% (was trying to set {newArmor:F3})");
        }
    }

    public void RemoveBoost(Tower tower)
    {
        if (tower == null || originalArmor < 0f) return;
        tower.armorReduction = originalArmor;
    }
}