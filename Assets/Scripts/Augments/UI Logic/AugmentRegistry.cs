using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System; // DODANE - to by³o g³ówn¹ przyczyn¹ b³êdu

// ===== ENHANCED DATA STRUCTURES =====
[System.Serializable]
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
    public string WhoImplements;

    // Runtime data
    public Sprite Icon { get; set; }
    public List<StatModification> ParsedModifications { get; set; }

    public bool AffectsPlayer => WhoImplements.Contains("Player");
    public bool AffectsEnemy => WhoImplements.Contains("Enemy");
    public bool AffectsTower => WhoImplements.Contains("Tower");
    public bool IsGlobal => WhoImplements.Contains("Global");
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
            { "=", StatModification.ModificationType.Set },
            { "%", StatModification.ModificationType.Percentage }
        };

    public static List<StatModification> ParseAffectedStats(string affectedStats, string whoImplements)
    {
        var modifications = new List<StatModification>();

        if (string.IsNullOrEmpty(affectedStats)) return modifications;

        // Split multiple modifications by comma or semicolon
        string[] statExpressions = affectedStats.Split(new char[] { ',', ';' },
            System.StringSplitOptions.RemoveEmptyEntries);

        foreach (string expression in statExpressions)
        {
            var modification = ParseSingleStatExpression(expression.Trim(), whoImplements);
            if (modification != null)
            {
                modifications.Add(modification);
            }
        }

        return modifications;
    }

    private static StatModification ParseSingleStatExpression(string expression, string whoImplements)
    {
        // Regex to match patterns like: statName*1.5, health+10, damage*1.25, speed%30
        var regex = new Regex(@"^(\w+)([*+=\%])([0-9]*\.?[0-9]+)$");
        var match = regex.Match(expression);

        if (!match.Success)
        {
            Debug.LogWarning($"StatParser: Could not parse stat expression: '{expression}'");
            return null;
        }

        string statName = match.Groups[1].Value;
        string operatorStr = match.Groups[2].Value;
        float value = float.Parse(match.Groups[3].Value);

        if (!OperatorMap.TryGetValue(operatorStr, out var operationType))
        {
            Debug.LogWarning($"StatParser: Unknown operator '{operatorStr}' in expression: '{expression}'");
            return null;
        }

        return new StatModification
        {
            StatName = statName,
            OperationType = operationType,
            Value = value,
            TargetType = whoImplements
        };
    }
}

// ===== STAT APPLICATOR SYSTEM =====
public static class StatApplicator
{
    public static bool ApplyModification(StatModification modification, AugmentTarget target)
    {
        object targetObject = GetTargetObject(modification.TargetType, target);
        if (targetObject == null) return false;

        return ApplyToTarget(targetObject, modification);
    }

    private static object GetTargetObject(string targetType, AugmentTarget target)
    {
        switch (targetType.ToLower())
        {
            case "player":
                return target.Player;
            case "enemy":
                return target.Enemy;
            case "tower":
                return target.Tower;
            case "global":
                return target.GlobalContext;
            default:
                // Try to infer from available targets
                if (target.Player != null) return target.Player;
                if (target.Tower != null) return target.Tower;
                if (target.Enemy != null) return target.Enemy;
                if (target.GlobalContext != null) return target.GlobalContext;
                return null;
        }
    }

    private static bool ApplyToTarget(object targetObject, StatModification modification)
    {
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

        // Try special cases for complex modifications
        return ApplySpecialCase(targetObject, modification);
    }

    private static bool ApplyToField(FieldInfo field, object target, StatModification modification)
    {
        float currentValue = Convert.ToSingle(field.GetValue(target));
        float newValue = CalculateNewValue(currentValue, modification);

        try
        {
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
        float currentValue = Convert.ToSingle(property.GetValue(target));
        float newValue = CalculateNewValue(currentValue, modification);

        try
        {
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

    private static bool ApplySpecialCase(object target, StatModification modification)
    {
        // Handle special cases that need custom logic
        switch (modification.StatName.ToLower())
        {
            case "energygeneration":
            case "energygenerationrate":
                return ApplyEnergyGenerationModification(target, modification);

            case "towerdamage":
                return ApplyAllTowerModification(modification, "damage");

            case "towerrange":
                return ApplyAllTowerModification(modification, "range");

            case "playercurrency":
            case "playerenergy":
                return ApplyPlayerEnergyModification(modification);

            default:
                Debug.LogWarning($"StatApplicator: Unknown stat '{modification.StatName}' for target type {target.GetType().Name}");
                return false;
        }
    }

    private static bool ApplyEnergyGenerationModification(object target, StatModification modification)
    {
        if (target is Tower tower && tower.IsGenerator())
        {
            float currentRate = tower.GetGenerationRate();
            float newRate = CalculateNewValue(currentRate, modification);
            tower.SetGenerationRate(newRate);
            return true;
        }

        // Apply to all generators
        var generators = UnityEngine.Object.FindObjectsByType<Tower>(FindObjectsSortMode.None).Where(t => t.IsGenerator());
        foreach (var generator in generators)
        {
            float currentRate = generator.GetGenerationRate();
            float newRate = CalculateNewValue(currentRate, modification);
            generator.SetGenerationRate(newRate);
        }
        return true;
    }

    private static bool ApplyAllTowerModification(StatModification modification, string statName)
    {
        var towers = UnityEngine.Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);
        bool anyApplied = false;

        foreach (var tower in towers)
        {
            switch (statName.ToLower())
            {
                case "damage":
                    float currentDamage = tower.GetDamage();
                    float newDamage = CalculateNewValue(currentDamage, modification);
                    tower.SetDamage(newDamage);
                    anyApplied = true;
                    break;

                case "range":
                    float currentRange = tower.GetRange();
                    float newRange = CalculateNewValue(currentRange, modification);
                    tower.SetRange(newRange);
                    anyApplied = true;
                    break;
            }
        }

        return anyApplied;
    }

    private static bool ApplyPlayerEnergyModification(StatModification modification)
    {
        if (EnergyManager.Instance == null) return false;

        int currentEnergy = EnergyManager.Instance.GetPlayerEnergy();
        int energyChange = (int)CalculateNewValue(currentEnergy, modification) - currentEnergy;

        if (energyChange > 0)
        {
            EnergyManager.Instance.GivePlayerEnergy(energyChange);
        }
        else if (energyChange < 0)
        {
            EnergyManager.Instance.TrySpendPlayerEnergy(-energyChange);
        }

        return true;
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

// ===== GENERIC AUTO-GENERATED AUGMENT EFFECT =====
public class AutoGeneratedAugmentEffect : IAugmentEffect
{
    private AugmentData augmentData;

    public int AugmentID => augmentData.ID;
    public string EffectName => augmentData.Name;

    public AutoGeneratedAugmentEffect(AugmentData data)
    {
        augmentData = data;

        // Parse the affected stats when creating the effect
        augmentData.ParsedModifications = StatParser.ParseAffectedStats(
            augmentData.AffectedStats,
            augmentData.WhoImplements);
    }

    public void Apply(AugmentTarget target)
    {
        if (augmentData.ParsedModifications == null || augmentData.ParsedModifications.Count == 0)
        {
            Debug.LogWarning($"No parsed modifications found for augment: {augmentData.Name}");
            return;
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

// ===== UPDATED AUGMENT REGISTRY =====
public class AugmentRegistry : MonoBehaviour
{
    private static AugmentRegistry _instance;
    public static AugmentRegistry Instance => _instance;

    [Header("Configuration")]
    public string csvResourcePath = "Data/Tower Defense Augments - Augments";
    public string spriteBasePath = "Sprites/Augments/";

    [Header("Debug")]
    public bool debugMode = true;

    // Core data
    private Dictionary<int, IAugmentEffect> registeredEffects = new Dictionary<int, IAugmentEffect>();
    private Dictionary<int, AugmentData> augmentDatabase = new Dictionary<int, AugmentData>();
    private List<int> appliedAugments = new List<int>();

    // Events
    public System.Action<AugmentData> OnAugmentApplied;
    public System.Action OnDatabaseLoaded;

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

        if (debugMode)
        {
            Debug.Log($"AugmentRegistry: Auto-generated {registeredEffects.Count} effects from {augmentDatabase.Count} CSV entries");
        }

        OnDatabaseLoaded?.Invoke();
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
            if (values.Length >= 9)
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
                    WhoImplements = values[8].Trim('"')
                };

                if (!augmentDatabase.ContainsKey(augment.ID))
                {
                    augmentDatabase.Add(augment.ID, augment);
                    loadedCount++;

                    if (debugMode)
                    {
                        Debug.Log($"Loaded augment: ID={augment.ID}, Name={augment.Name}, Stats={augment.AffectedStats}");
                    }
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
                inQuotes = !inQuotes;
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
        if (!augmentDatabase.TryGetValue(augmentID, out AugmentData augmentData))
        {
            Debug.LogError($"AugmentRegistry: Augment {augmentID} not found in database");
            return false;
        }

        if (!registeredEffects.TryGetValue(augmentID, out IAugmentEffect effect))
        {
            Debug.LogError($"AugmentRegistry: No auto-generated effect found for augment {augmentID}");
            return false;
        }

        // Determine target based on augment data
        AugmentTarget target = CreateTargetForAugment(augmentData);
        if (target == null || !effect.CanApplyTo(target))
        {
            Debug.LogError($"AugmentRegistry: Cannot apply augment {augmentID} to current target");
            return false;
        }

        // Apply the effect
        effect.Apply(target);
        appliedAugments.Add(augmentID);

        if (debugMode)
        {
            Debug.Log($"Applied auto-generated augment: {augmentData.Name} (ID: {augmentID})");
        }

        OnAugmentApplied?.Invoke(augmentData);
        return true;
    }

    private AugmentTarget CreateTargetForAugment(AugmentData augment)
    {
        if (augment.AffectsPlayer)
            return AugmentTarget.ForPlayer();
        if (augment.IsGlobal)
            return AugmentTarget.ForGlobal();

        // For enemy/tower effects, we might need different logic
        // depending on when they're applied
        return AugmentTarget.ForGlobal(); // Fallback to global context
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

    public AugmentTarget(PlayerStats player) => Player = player;
    public AugmentTarget(EnemyStats enemy) => Enemy = enemy;
    public AugmentTarget(Tower tower) => Tower = tower;
    public AugmentTarget(EnergyManager global) => GlobalContext = global;

    public static AugmentTarget ForPlayer() => new AugmentTarget(UnityEngine.Object.FindFirstObjectByType<PlayerStats>());
    public static AugmentTarget ForGlobal() => new AugmentTarget(EnergyManager.Instance);
}