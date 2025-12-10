using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class StatsUI : MonoBehaviour
{
    [Header("References")]
    public PlayerStats playerStats;
    public GameObject statTextPrefab;
    public Transform container;

    [Header("Display Settings")]
    [SerializeField] private bool showPlayerStats = true;
    [SerializeField] private bool showWeaponStats = true;
    [SerializeField] private bool showTowerStats = true;
    [SerializeField] private bool showEnemyStats = true;
    [SerializeField] private bool showCoreStats = true;
    [SerializeField] private bool showGlobalStats = true;

    [Header("Colors")]
    [SerializeField] private Color headerColor = new Color(1f, 1f, 0.95f);
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color positiveColor = new Color(0.95f, 1f, 0.95f);
    [SerializeField] private Color negativeColor = Color.red;
    [SerializeField] private Color warningColor = new Color(1f, 0.65f, 0f);
    [SerializeField] private Color multiplierColor = new Color(0.7f, 1f, 0.7f); // Light green for multipliers

    [Header("Auto-Refresh")]
    [SerializeField] private bool autoRefresh = true;
    [SerializeField] private float refreshInterval = 0.5f;

    [Header("Multiplier Display")]
    [SerializeField] private bool showMultipliers = true;
    [SerializeField] private float multiplierThreshold = 0.001f; // Show almost always, even 1.00x

    [HideInInspector]
    public List<TextMeshProUGUI> textObjects = new List<TextMeshProUGUI>();

    private float refreshTimer = 0f;
    private int currentIndex = 0;

    // Base values captured at game start 
    private Dictionary<string, float> baseValues = new Dictionary<string, float>();
    private bool baseValuesCaptured = false;

    private void Start()
    {
        if (playerStats == null)
        {
            playerStats = FindAnyObjectByType<PlayerStats>();
        }

        // Capture base values at game start
        CaptureBaseValues();

        RefreshUI();
    }

    private void CaptureBaseValues()
    {
        if (baseValuesCaptured) return;

        // Capture player base stats
        if (playerStats != null)
        {
            baseValues["player_maxHealth"] = playerStats.maxHealth;
            baseValues["player_currentArmor"] = playerStats.currentArmor;
            baseValues["player_moveSpeed"] = playerStats.moveSpeed;
            baseValues["player_sprintMultiplier"] = playerStats.sprintMultiplier;
            baseValues["player_maxStamina"] = playerStats.maxStamina;
            baseValues["player_staminaRegenRate"] = playerStats.staminaRegenRate;
            baseValues["player_healthRegenRate"] = playerStats.healthRegenRate;
            baseValues["player_dashSpeed"] = playerStats.dashSpeed;
        }

        baseValuesCaptured = true;
    }

    private void Update()
    {
        // Add null check for destroyed player
        if (playerStats == null)
        {
            // Player is destroyed, stop updating UI
            return;
        }

        if (autoRefresh)
        {
            refreshTimer += Time.deltaTime;
            if (refreshTimer >= refreshInterval)
            {
                RefreshUI();
                refreshTimer = 0f;
            }
        }
    }

    private TextMeshProUGUI GetOrCreateTextAt(int index)
    {
        while (textObjects.Count <= index)
        {
            GameObject newTextObj = Instantiate(statTextPrefab, container);
            TextMeshProUGUI tmp = newTextObj.GetComponent<TextMeshProUGUI>();

            if (tmp == null)
            {
                tmp = newTextObj.GetComponentInChildren<TextMeshProUGUI>();
            }

            if (tmp != null)
            {
                textObjects.Add(tmp);
            }
            else
            {
                Debug.LogError("StatTextPrefab doesn't have TextMeshProUGUI component");
                Destroy(newTextObj);
                return null;
            }
        }

        if (index >= 0 && index < textObjects.Count)
        {
            return textObjects[index];
        }

        Debug.LogError($"Index {index} out of range! TextObjects count: {textObjects.Count}");
        return null;
    }

    private void AddLine(string text, Color? color = null)
    {
        var textObj = GetOrCreateTextAt(currentIndex);
        if (textObj != null)
        {
            textObj.text = text;
            textObj.color = color ?? normalColor;
            textObj.gameObject.SetActive(true);
        }
        currentIndex++;
    }

    private void AddHeader(string text)
    {
        Color white = new Color(1f, 1f, 1f);
        AddLine($"<b><size=22>{text}</size></b>", white);
    }

    private void AddSpacer()
    {
        AddLine("", normalColor);
    }

    public void RefreshUI()
    {
        // Safety check - if player is destroyed, skip refresh
        if (showPlayerStats && playerStats == null)
        {
            // Try to find player again (might have respawned)
            playerStats = FindAnyObjectByType<PlayerStats>();

            // If still null, clear UI and return
            if (playerStats == null)
            {
                ClearUI();
                return;
            }
        }

        currentIndex = 0;

        if (showPlayerStats && playerStats != null)
        {
            DisplayPlayerStats();
        }

        if (showWeaponStats && playerStats != null) // Added null check
        {
            DisplayWeaponStats();
        }

        if (showTowerStats)
        {
            DisplayTowerStats();
        }

        if (showEnemyStats)
        {
            DisplayEnemyStats();
        }

        if (showCoreStats)
        {
            DisplayCoreStats();
        }

        if (showGlobalStats)
        {
            DisplayGlobalStats();
        }

        // Hide unused text objects
        for (int i = currentIndex; i < textObjects.Count; i++)
        {
            if (textObjects[i] != null)
            {
                textObjects[i].gameObject.SetActive(false);
            }
        }
    }


    private void DisplayPlayerStats()
    {
        AddHeader("PLAYER");

        // Health
        float healthMultiplier = CalculateAugmentMultiplierForStat("maxHealth", "Player");
        AddLineWithMultiplier($"Health: {playerStats.maxHealth:F0}", healthMultiplier);

        // Armor
        float armorMultiplier = CalculateAugmentMultiplierForStat("currentArmor", "Player");
        AddLineWithMultiplier($"Armor: {playerStats.currentArmor:F1}", armorMultiplier,
            GetStatColor(playerStats.currentArmor, 0, 10));

        // Move Speed
        float speedMultiplier = CalculateAugmentMultiplierForStat("moveSpeed", "Player");
        AddLineWithMultiplier($"Move Speed: {playerStats.moveSpeed:F1}", speedMultiplier,
            GetStatColor(playerStats.moveSpeed, 3, 8));

        // Sprint Multiplier
        float sprintMult = CalculateAugmentMultiplierForStat("sprintMultiplier", "Player");
        AddLineWithMultiplier($"Sprint Mult: {playerStats.sprintMultiplier:F2}x", sprintMult);

        // Stamina
        float staminaMultiplier = CalculateAugmentMultiplierForStat("maxStamina", "Player");
        AddLineWithMultiplier($"Stamina: {playerStats.maxStamina:F1}", staminaMultiplier);

        // Stamina Regen
        float staminaRegenMult = CalculateAugmentMultiplierForStat("staminaRegenRate", "Player");
        AddLineWithMultiplier($"Stamina Regen: {playerStats.staminaRegenRate:F1}/s", staminaRegenMult);

        // Health Regen
        float healthRegenMult = CalculateAugmentMultiplierForStat("healthRegenRate", "Player");
        AddLineWithMultiplier($"Health Regen: {playerStats.healthRegenRate:F1}/s", healthRegenMult,
            GetStatColor(playerStats.healthRegenRate, 0, 5));

        // Dash Speed
        float dashSpeedMult = CalculateAugmentMultiplierForStat("dashSpeed", "Player");
        AddLineWithMultiplier($"Dash Speed: {playerStats.dashSpeed:F1}", dashSpeedMult);

        AddSpacer();
    }

    private void DisplayWeaponStats()
    {
        AddHeader("WEAPON");

        // Safety check - player might be destroyed
        if (playerStats == null)
        {
            AddLine("Player not found", negativeColor);
            AddSpacer();
            return;
        }

        var weapon = playerStats.GetComponentInChildren<Weapon>();
        if (weapon == null)
        {
            AddLine("No weapon equipped", warningColor);
            AddSpacer();
            return;
        }

        var weaponData = weapon.GetWeaponData();
        if (weaponData == null)
        {
            AddLine("Weapon data not found", negativeColor);
            AddSpacer();
            return;
        }

        AddLine($"{weaponData.weaponName}", headerColor);

        if (weaponData.damage > 0)
            AddLine($"Damage: {weaponData.damage:F1}", positiveColor);

        AddLine($"Cooldown: {weaponData.attackCooldown:F2}s");

        if (weaponData.armorBonus > 0)
            AddLine($"Armor Bonus: +{weaponData.armorBonus:F1}", positiveColor);

        if (weaponData.knockBack)
            AddLine($"Knockback: {weaponData.knockBackForce:F1}");

        if (weaponData.isRanged)
        {
            AddLine($"Type: Ranged", new Color(0.5f, 0.8f, 1f));
            AddLine($"Projectile Speed: {weaponData.projectileSpeed:F1}");
        }
        else if (weaponData.isGrapplingHook)
        {
            AddLine($"Hook Range: {weaponData.hookRange:F1}");
            AddLine($"Hook Speed: {weaponData.hookSpeed:F1}");
            AddLine($"Pull Force: {weaponData.pullForce:F1}");
        }
        else if (weaponData.isObstacleDrawer)
        {
            AddLine($"Max Obstacles: {weaponData.maxObstacles}");
        }
        else
        {
            AddLine($"Type: Melee", Color.white);
        }

        AddSpacer();
    }

    // Add this new helper method
    private void ClearUI()
    {
        // Hide all text objects when player is destroyed
        for (int i = 0; i < textObjects.Count; i++)
        {
            if (textObjects[i] != null)
            {
                textObjects[i].gameObject.SetActive(false);
            }
        }
    }

    private void DisplayTowerStats()
    {
        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);

        AddHeader("TOWERS");

        if (towers.Length == 0)
        {
            AddLine("No towers built", warningColor);
            AddSpacer();
            return;
        }

        var towerGroups = towers.GroupBy(t => t.towerType);

        foreach (var group in towerGroups)
        {
            var tower = group.First();
            string typeName = tower.towerType.ToString();
            int count = group.Count();

            AddLine($"[{typeName}] x{count}", headerColor);

            if (tower.IsGenerator())
            {
                float genRateMult = CalculateAugmentMultiplierForStat("energyGenerationRate", "Tower");
                AddLineWithMultiplier($"  Gen Rate: {tower.GetGenerationRate():F1}/s", genRateMult, positiveColor);
                AddLine($"  Gen Range: {tower.generationRange:F1}");
                AddLine($"  Self Cost: {tower.generatorSelfConsumption * 100:F0}%");
            }
            else
            {
                float damageMult = CalculateAugmentMultiplierForStat("damage", "Tower");
                AddLineWithMultiplier($"  Damage: {tower.GetDamage():F1}", damageMult);

                float rangeMult = CalculateAugmentMultiplierForStat("range", "Tower");
                AddLineWithMultiplier($"  Range: {tower.GetRange():F1}", rangeMult);

                float fireRateMult = CalculateAugmentMultiplierForStat("fireRate", "Tower");
                AddLineWithMultiplier($"  Fire Rate: {tower.GetFireRate():F2}/s", fireRateMult);

                if (tower.freezeChance > 0)
                    AddLine($"  Freeze: {tower.freezeChance * 100:F0}%", new Color(0.5f, 0.8f, 1f));

                if (tower.energyCostMultiplier != 1f)
                {
                    Color costColor = tower.energyCostMultiplier < 1f ? positiveColor : negativeColor;
                    AddLine($"  Energy Cost: {tower.energyCostMultiplier:F2}x", costColor);
                }
            }

            float maxEnergyMult = CalculateAugmentMultiplierForStat("maxEnergy", "Tower");
            AddLineWithMultiplier($"  Max Energy: {tower.GetMaxEnergy():F0}", maxEnergyMult);

            if (tower.GetArmor() > 0)
            {
                float armorMult = CalculateAugmentMultiplierForStat("armorReduction", "Tower");
                AddLineWithMultiplier($"  Armor: {tower.GetArmor() * 100:F0}%", armorMult, positiveColor);
            }

            if (tower.healthRegenRate > 0)
                AddLine($"  Regen: {tower.healthRegenRate:F1}/s", positiveColor);
        }

        AddSpacer();
    }

    private void DisplayEnemyStats()
    {
        AddHeader("ENEMIES");

        if (EnemyStatModifierManager.Instance == null)
        {
            AddLine("Manager not found", negativeColor);
            AddSpacer();
            return;
        }

        float speedMult = EnemyStatModifierManager.Instance.GetMoveSpeedMultiplier();
        float damageMult = EnemyStatModifierManager.Instance.GetDamageMultiplier();
        float healthMult = EnemyStatModifierManager.Instance.GetHealthMultiplier();

        // Show multipliers with colors
        AddLine($"Speed: {speedMult:F2}x", GetMultiplierColor(speedMult, true));
        AddLine($"Damage: {damageMult:F2}x", GetMultiplierColor(damageMult, true));
        AddLine($"Health: {healthMult:F2}x", GetMultiplierColor(healthMult, true));

        var exampleEnemy = FindFirstObjectByType<EnemyStats>();
        if (exampleEnemy == null)
        {
            AddLine("No enemies spawned", warningColor);
        }

        AddSpacer();
    }

    private void DisplayCoreStats()
    {
        var core = FindFirstObjectByType<CentralCore>();

        AddHeader("CENTRAL CORE");

        if (core == null)
        {
            AddLine("Core not found!", negativeColor);
            AddSpacer();
            return;
        }

        float energyPercent = core.GetEnergyPercentage();
        Color energyColor = energyPercent < 0.3f ? negativeColor :
                           energyPercent < 0.5f ? warningColor : positiveColor;

        AddLine($"Energy: {core.GetEnergy():F0} / {core.GetMaxEnergy():F0}", energyColor);
        AddLine($"Status: {GetCoreStatus(core)}", GetCoreStatusColor(core));

        if (core.GetArmor() > 0)
            AddLine($"Armor: {core.GetArmor() * 100:F0}%", positiveColor);

        var shield = core.GetComponent<CoreShieldMatrix>();
        if (shield != null)
        {
            AddLine($"Shield: {shield.currentShieldStrength:F0} / {shield.maxShieldStrength:F0}",
                new Color(0.5f, 0.8f, 1f));
        }

        var repair = core.GetComponent<CoreRepairSystems>();
        if (repair != null && repair.regenerationRate > 0)
        {
            AddLine($"Regen: {repair.regenerationRate:F1}/s", positiveColor);
        }

        var siphon = core.GetComponent<CoreEnergySiphonEffect>();
        if (siphon != null && siphon.siphonPercentage > 0)
        {
            AddLine($"Siphon: {siphon.siphonPercentage * 100:F0}%", positiveColor);
        }

        AddSpacer();
    }

    private void DisplayGlobalStats()
    {
        AddHeader("GLOBAL");
        if (EnergyManager.Instance != null)
        {
            AddLine($"Player Energy: {EnergyManager.Instance.GetPlayerEnergy()}");

            float resourceMult = EnergyManager.Instance.globalResourceMultiplier;
            float resourceAugmentMult = CalculateAugmentMultiplierForStat("globalResourceMultiplier", "Global");
            AddLineWithMultiplier($"Resource Mult: {resourceMult:F2}x", resourceAugmentMult,
                GetMultiplierColor(resourceMult, false));

            if (EnergyManager.Instance.bonusResourceDropChance > 0)
            {
                AddLine($"Bonus Drop: {EnergyManager.Instance.bonusResourceDropChance * 100:F0}%",
                    positiveColor);
            }

            AddLine($"Tower Cost: {EnergyManager.Instance.GetTowerBuildCost()}");
            AddLine($"Tower Refund: {EnergyManager.Instance.GetTowerSellValue()}");

            float decayRate = EnergyManager.Instance.globalEnergyDecayRate;
            if (decayRate != 1f)
            {
                Color decayColor = decayRate < 1f ? positiveColor : negativeColor;
                float decayMult = CalculateAugmentMultiplierForStat("globalEnergyDecayRate", "Global");
                AddLineWithMultiplier($"Decay Rate: {decayRate:F2}x", decayMult, decayColor);
            }
        }

        AddSpacer();
    }

    #region Multiplier Calculation
    private float CalculateAugmentMultiplierForStat(string statName, string targetType)
    {
        if (AugmentRegistry.Instance == null) return 1f;

        var appliedAugments = AugmentRegistry.Instance.GetAppliedAugments();
        float totalMultiplier = 1f;

        foreach (int augmentId in appliedAugments)
        {
            var augmentData = AugmentRegistry.Instance.GetAugmentData(augmentId);
            if (augmentData?.ParsedModifications == null) continue;

            foreach (var mod in augmentData.ParsedModifications)
            {
                // Check if this modification affects the stat we're looking for
                if (!IsStatMatch(mod.StatName, statName)) continue;
                if (!mod.TargetType.Equals(targetType, System.StringComparison.OrdinalIgnoreCase)) continue;

                // Accumulate modifications
                switch (mod.OperationType)
                {
                    case StatModification.ModificationType.Multiply:
                        totalMultiplier *= mod.Value;
                        break;
                    case StatModification.ModificationType.Percentage:
                        totalMultiplier *= (1f + mod.Value / 100f);
                        break;
                    case StatModification.ModificationType.Add:
                        // For additive modifications, we need to know the base value
                        break;
                }
            }
        }

        return totalMultiplier;
    }

    private bool IsStatMatch(string modStatName, string targetStatName)
    {
        if (modStatName.Equals(targetStatName, System.StringComparison.OrdinalIgnoreCase))
            return true;

        // Handle common aliases
        if (targetStatName == "maxHealth" && modStatName.Equals("health", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (targetStatName == "moveSpeed" && modStatName.Equals("speed", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private float CalculateAugmentMultiplier(string statName, string targetType)
    {
        return CalculateAugmentMultiplierForStat(statName, targetType);
    }

    private void AddLineWithMultiplier(string baseText, float multiplier, Color? baseColor = null)
    {
        if (!showMultipliers || Mathf.Abs(multiplier - 1f) < multiplierThreshold)
        {
            // Multiplier not significant or disabled, show normal line
            AddLine(baseText, baseColor);
            return;
        }

        // Format multiplier text
        string multiplierText = FormatMultiplier(multiplier);
        string fullText = $"{baseText} <color=#{ColorUtility.ToHtmlStringRGB(multiplierColor)}>{multiplierText}</color>";

        AddLine(fullText, baseColor);
    }

    private string FormatMultiplier(float multiplier)
    {
        if (multiplier > 1f)
        {
            return $"({multiplier:F2}x)";
        }
        else if (multiplier < 1f)
        {
            // Show as percentage reduction for values < 1
            float reduction = (1f - multiplier) * 100f;
            return $"(-{reduction:F0}%)";
        }
        else
        {
            return "(1.00x)";
        }
    }
    #endregion

    #region Helper Methods
    private Color GetStatColor(float value, float low, float high)
    {
        if (value < low) return negativeColor;
        if (value > high) return positiveColor;
        return normalColor;
    }

    private Color GetMultiplierColor(float multiplier, bool lowerIsBetter)
    {
        if (lowerIsBetter)
        {
            if (multiplier < 0.9f) return positiveColor;
            if (multiplier > 1.1f) return negativeColor;
        }
        else
        {
            if (multiplier > 1.1f) return positiveColor;
            if (multiplier < 0.9f) return negativeColor;
        }
        return normalColor;
    }

    private string GetCoreStatus(CentralCore core)
    {
        if (core.IsEnergyDepleted()) return "DEPLETED";
        if (core.IsEnergyLow()) return "CRITICAL";
        return "OPERATIONAL";
    }

    private Color GetCoreStatusColor(CentralCore core)
    {
        if (core.IsEnergyDepleted()) return negativeColor;
        if (core.IsEnergyLow()) return warningColor;
        return positiveColor;
    }
    #endregion

    #region Public Methods
    public void TogglePlayerStats() => showPlayerStats = !showPlayerStats;
    public void ToggleWeaponStats() => showWeaponStats = !showWeaponStats;
    public void ToggleTowerStats() => showTowerStats = !showTowerStats;
    public void ToggleEnemyStats() => showEnemyStats = !showEnemyStats;
    public void ToggleCoreStats() => showCoreStats = !showCoreStats;
    public void ToggleGlobalStats() => showGlobalStats = !showGlobalStats;
    public void ToggleMultiplierDisplay() => showMultipliers = !showMultipliers;

    [ContextMenu("Force Refresh")]
    public void ForceRefresh()
    {
        RefreshUI();
    }
    #endregion
}