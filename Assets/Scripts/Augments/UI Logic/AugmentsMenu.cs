using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using System.Collections;

#if UNITY_EDITOR
[System.Serializable]
#endif

public class AugmentsMenu : MonoBehaviour
{
    [Header("UI References")]
    public GameObject augmentsMenu;
    public GameObject hideShow;

    [Header("Augment Slots")]
    [SerializeField] private Image[] augmentImages;
    [SerializeField] private Image[] rarityImages;
    [SerializeField] private TextMeshProUGUI[] nameTexts;
    [SerializeField] private TextMeshProUGUI[] descriptionTexts;
    [SerializeField] private TextMeshProUGUI[] rerollNumberText;
    [SerializeField] private Button[] rerollButtons;
    [SerializeField] private Button[] selectButtons;

    [Header("Settings")]
    public int maxRerolls = 2;

    [Header("Debug")]
    public bool debugMode = false;

    [Header("Debug Options - Forced Augments")]
    [Tooltip("Enter augment IDs to force specific augments in debug mode. Leave empty for random generation.")]
    //[SerializeField] private int[] forcedAugmentIDs = new int[0];
    //[SerializeField] private List<int> forcedAugmentIDsList = new List<int>();
    //private int[] forcedAugmentIDs
    //{
    //    get => forcedAugmentIDsList.ToArray();
    //    set => forcedAugmentIDsList = new List<int>(value ?? new int[0]);
    //}


    [SerializeField] private List<int> forcedAugmentIDsList = new List<int>();

    // Remove the problematic property entirely and use the List directly
    public void SetForcedAugments(int[] augments)
    {
        forcedAugmentIDsList = new List<int>(augments ?? new int[0]);
    }

    public int[] GetForcedAugments()
    {
        return forcedAugmentIDsList.ToArray();
    }


    [SerializeField] private ChosenAugmentsUI chosenAugmentsUI; // menu pauzy itp

    // State variables
    private bool isHidden = false;
    private bool isMenuActive = false;
    [System.NonSerialized]

    private int[] rerollsLeft;
    [System.NonSerialized]

    private int[] currentAugmentIDs;
    [System.NonSerialized]

    private string[] currentSelectedRarities;

    // Data sources
    [System.NonSerialized]

    private static Sprite[] allSprites;

    public struct AugmentWithRarity
    {
        public AugmentData augment;
        public string rarity;
    }

    //  Get data from AugmentRegistry 
    private Dictionary<string, float> rarityWeights => AugmentRegistry.Instance?.GetRarityWeights() ?? new Dictionary<string, float>
    {
        {"Common", 50f}, {"Rare", 30f}, {"Epic", 15f}, {"Legendary", 5f} // Fallback if registry not available
    };

    private Dictionary<string, Color> rarityColors => AugmentRegistry.Instance?.GetRarityColors() ?? new Dictionary<string, Color>
    {
        {"Common", Color.green}, {"Rare", Color.blue}, {"Epic", new Color(0.8f, 0f, 1f)}, {"Legendary", new Color(1f, 0.6f, 0f)} // Fallback if registry not available
    };

    void Awake()
    {
        if (debugMode) Debug.Log("AugmentsMenu: Awake started");

        // Ensure forced augments array is properly initialized
        //if (forcedAugmentIDs == null)
        //    forcedAugmentIDs = new int[0];

        if (forcedAugmentIDsList == null)
            forcedAugmentIDsList = new List<int>();
        InitializeArrays();
        LoadSprites();
        SetupUI();

        if (debugMode) Debug.Log("AugmentsMenu: Awake completed");
    }

    void OnValidate()
    {
        // Clean up null entries in forced augments list
        if (forcedAugmentIDsList != null)
        {
            forcedAugmentIDsList.RemoveAll(id => id < 0);
        }
    }

    void OnEnable()
    {
        // Ensure arrays are initialized even after deserialization
        if (augmentImages != null && (rerollsLeft == null || rerollsLeft.Length != augmentImages.Length))
        {
            InitializeArrays();
        }
    }

    void Start()
    {
        chosenAugmentsUI = FindAnyObjectByType<ChosenAugmentsUI>(FindObjectsInactive.Include);

        if (debugMode) Debug.Log("AugmentsMenu: Start called");

        augmentsMenu.SetActive(false);

        // Wait for AugmentRegistry to be ready
        StartCoroutine(WaitForRegistryAndGenerate());
    }

    private void InitializeArrays()
    {
        if (augmentImages == null || augmentImages.Length == 0)
        {
            Debug.LogError("AugmentsMenu: augmentImages array is null or empty!");
            return;
        }

        for (int i = 0; i < augmentImages.Length; i++)
        {
            if (augmentImages[i] == null)
            {
                Debug.LogError($"AugmentsMenu: augmentImages[{i}] is null!");
            }
        }

        int slotCount = augmentImages.Length;
        rerollsLeft = new int[slotCount];
        currentAugmentIDs = new int[slotCount];
        currentSelectedRarities = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            rerollsLeft[i] = maxRerolls;
            currentAugmentIDs[i] = -1;
            currentSelectedRarities[i] = "Common";
        }

        if (debugMode) Debug.Log($"AugmentsMenu: Initialized {slotCount} slots with {maxRerolls} rerolls each");
    }

    private void LoadSprites()
    {
        if (allSprites == null)
        {
            allSprites = Resources.LoadAll<Sprite>("Sprites/Augments");
            if (debugMode) Debug.Log($"AugmentsMenu: Loaded {allSprites.Length} sprites from Resources/Sprites/Augments");
        }
    }

    private void SetupUI()
    {
        // Setup reroll buttons
        if (rerollButtons != null)
        {
            for (int i = 0; i < rerollButtons.Length; i++)
            {
                if (rerollButtons[i] != null)
                {
                    int slotIndex = i;
                    rerollButtons[i].onClick.RemoveAllListeners();
                    rerollButtons[i].onClick.AddListener(() => Reroll(slotIndex));
                }
            }
        }

        // Setup select buttons
        if (selectButtons != null)
        {
            for (int i = 0; i < selectButtons.Length; i++)
            {
                if (selectButtons[i] != null)
                {
                    int slotIndex = i;
                    selectButtons[i].onClick.RemoveAllListeners();
                    selectButtons[i].onClick.AddListener(() => ChooseAugment(slotIndex));
                }
            }
        }

        if (debugMode) Debug.Log("AugmentsMenu: UI setup completed");
    }

    private IEnumerator WaitForRegistryAndGenerate()
    {
        if (debugMode) Debug.Log("AugmentsMenu: Waiting for AugmentRegistry...");

        // Wait for AugmentRegistry to be available and loaded
        yield return new WaitUntil(() => AugmentRegistry.Instance != null);
        yield return new WaitUntil(() => AugmentRegistry.Instance.GetAllAugments().Count > 0);

        // Small delay to ensure everything is fully initialized
        yield return new WaitForSeconds(0.1f);

        // Validate rarity configuration consistency
        ValidateRarityConfiguration();

        if (debugMode) Debug.Log($"AugmentsMenu: AugmentRegistry ready with {AugmentRegistry.Instance.GetAllAugments().Count} augments");

        GenerateInitialAugments();
    }

    private void ValidateRarityConfiguration()
    {
        if (AugmentRegistry.Instance == null)
        {
            Debug.LogError("AugmentRegistry not found! Rarity system will not work properly.");
            return;
        }

        var weights = rarityWeights;
        var colors = rarityColors;

        if (weights.Count != colors.Count)
        {
            Debug.LogWarning($"Rarity configuration mismatch: {weights.Count} weights vs {colors.Count} colors");
        }

        foreach (var rarity in weights.Keys)
        {
            if (!colors.ContainsKey(rarity))
            {
                Debug.LogError($"Rarity '{rarity}' has weight but no color defined!");
            }
        }

        if (debugMode)
        {
            Debug.Log($"Validated {weights.Count} rarity configurations from AugmentRegistry");
            foreach (var kvp in weights)
            {
                Debug.Log($"  {kvp.Key}: Weight={kvp.Value}, Color={colors[kvp.Key]}");
            }
        }
    }

    private void GenerateInitialAugments()
    {
        // Add unique timestamp to see if this method is actually called each time
        //Debug.Log($"🔄 [GENERATION] Starting at {System.DateTime.Now:HH:mm:ss.fff} - Tick: {System.Environment.TickCount}");

        // Reset random seed with current time to ensure different results
        int newSeed = System.Environment.TickCount + UnityEngine.Random.Range(0, 10000);
        UnityEngine.Random.InitState(newSeed);
        //Debug.Log($"🎲 [SEED] Set random seed to: {newSeed}");

        if (debugMode) Debug.Log("AugmentsMenu: Generating initial augments...");

        if (augmentImages == null)
        {
            Debug.LogError("AugmentsMenu: augmentImages is null!");
            return;
        }

        for (int i = 0; i < augmentImages.Length; i++)
        {
            AugmentWithRarity result;

            // Check for forced augments in debug mode
            if (debugMode && ShouldUseForcedAugment(i))
            {
                int forcedID = GetForcedAugmentID(i);
                var forcedAugment = AugmentRegistry.Instance?.GetAugmentData(forcedID);

                if (forcedAugment != null)
                {
                    //Debug.Log($"🔧 [FORCED] Slot {i}: About to generate rarity for ID {forcedID}");
                    string rarity = GetRandomRarityForAugment(forcedAugment.ID);
                    result = new AugmentWithRarity { augment = forcedAugment, rarity = rarity };
                    //Debug.Log($"🔧 [FORCED] Slot {i}: ID {forcedID} -> {rarity} rarity");
                }
                else
                {
                    Debug.LogWarning($"[DEBUG] Forced augment ID {forcedID} not found, falling back to random.");
                    result = GenerateRandomAugmentWithRarity(GetExcludedIDs());
                }
            }
            else
            {
                // Random generation
                result = GenerateRandomAugmentWithRarity(GetExcludedIDs());
            }

            if (result.augment != null)
            {
                currentAugmentIDs[i] = result.augment.ID;
                currentSelectedRarities[i] = result.rarity;
                //Debug.Log($"🎯 [STORED] Slot {i}: Stored ID {result.augment.ID} with rarity {result.rarity}");
                UpdateSlotDisplay(i, result.augment, result.rarity);
            }

            UpdateRerollDisplay(i);
        }

        if (debugMode) Debug.Log("AugmentsMenu: Initial augments generation completed");
    }
    // Simplified forced augment methods
    private bool ShouldUseForcedAugment(int slotIndex)
    {
        return forcedAugmentIDsList != null &&
               slotIndex < forcedAugmentIDsList.Count &&
               forcedAugmentIDsList[slotIndex] > 0;
    }

    private int GetForcedAugmentID(int slotIndex)
    {
        if (forcedAugmentIDsList == null || slotIndex >= forcedAugmentIDsList.Count)
            return -1;

        return forcedAugmentIDsList[slotIndex];
    }

    private List<int> GetExcludedIDs()
    {
        var excluded = new List<int>();

        // Exclude currently displayed augments
        if (currentAugmentIDs != null)
        {
            excluded.AddRange(currentAugmentIDs.Where(id => id != -1));
        }

        // Define augments that CANNOT be selected multiple times
        HashSet<int> nonRepeatableAugments = new HashSet<int>
        { 
            // Unlock augments
            2,   // Unlocked melee
            3,   // Obstacles generation
            //4,   // More obstacles
            65,  // Unlocked grappling hook
            66,  // Unlocked ranged weapon
            93,  // Unlocked flamethrower
            94,  // Unlocked portable obstacles
            231, // Unlocked heavy melee weapon
            232, // Unlocked dagger melee weapon
            // One-time special effects
            30,  // God Mode (permanent penalty)
            37,  // Quick revive (once per wave)
            163, // Time Rewind (once per game)
        };

        if (AugmentRegistry.Instance != null)
        {
            var appliedAugments = AugmentRegistry.Instance.GetAppliedAugments();
            // Only exclude non-repeatable augments
            excluded.AddRange(appliedAugments.Where(id => nonRepeatableAugments.Contains(id)));
        }

        return excluded.Distinct().ToList();
    }

    private void UpdateSlotDisplay(int slotIndex, AugmentData augment, string rarity)
    {
        if (slotIndex < 0 || slotIndex >= augmentImages.Length) return;

        // Update image
        if (augmentImages[slotIndex] != null)
        {
            Sprite sprite = System.Array.Find(allSprites, s => s.name == augment.ID.ToString());
            if (sprite != null)
            {
                augmentImages[slotIndex].sprite = sprite;
            }
            else if (debugMode)
            {
                Debug.LogWarning($"No sprite found for augment ID: {augment.ID}");
            }
        }

        // Update rarity color
        if (rarityImages != null && slotIndex < rarityImages.Length && rarityImages[slotIndex] != null)
        {
            Color rarityColor = rarityColors.ContainsKey(rarity) ? rarityColors[rarity] : Color.white;
            rarityImages[slotIndex].color = rarityColor;
        }

        // Update name text
        if (nameTexts != null && slotIndex < nameTexts.Length && nameTexts[slotIndex] != null)
        {
            nameTexts[slotIndex].text = augment.Name;
        }

        // Update description text
        if (descriptionTexts != null && slotIndex < descriptionTexts.Length && descriptionTexts[slotIndex] != null)
        {
            descriptionTexts[slotIndex].text = augment.Description;
        }
    }

    private void UpdateRerollDisplay(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= rerollsLeft.Length) return;

        // Update reroll count text
        if (rerollNumberText != null && slotIndex < rerollNumberText.Length && rerollNumberText[slotIndex] != null)
        {
            rerollNumberText[slotIndex].text = rerollsLeft[slotIndex].ToString();
        }

        // Update reroll button interactability
        if (rerollButtons != null && slotIndex < rerollButtons.Length && rerollButtons[slotIndex] != null)
        {
            rerollButtons[slotIndex].interactable = rerollsLeft[slotIndex] > 0;
        }
    }

    // ===== PUBLIC INTERFACE =====
    public void ActivateAugments()
    {
        //Debug.Log($"🎮 [MENU] ActivateAugments called at {System.DateTime.Now:HH:mm:ss.fff}");

        if (!isMenuActive)
        {
            augmentsMenu.SetActive(true);
            Cursor.visible = true;
            Time.timeScale = 0f;
            isMenuActive = true;

            // Force regeneration of augments each time menu opens
            //Debug.Log("🔄 [MENU] Forcing augment regeneration");
            GenerateInitialAugments();

            if (debugMode) Debug.Log("AugmentsMenu: Menu activated");
        }
    }

    public void HideShowButton()
    {
        isHidden = !isHidden;
        hideShow.SetActive(!isHidden);
    }

    public void Reroll(int slotIndex)
    {
        if (debugMode) Debug.Log($"AugmentsMenu: Reroll called for slot {slotIndex}");

        if (slotIndex < 0 || slotIndex >= rerollsLeft.Length)
        {
            Debug.LogError($"Invalid slot index: {slotIndex}");
            return;
        }

        if (rerollsLeft[slotIndex] <= 0)
        {
            if (debugMode) Debug.Log($"No rerolls left for slot {slotIndex}");
            return;
        }

        // Play reroll sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.augmentReroll, transform.position);
        }

        rerollsLeft[slotIndex]--;

        var result = GenerateRandomAugmentWithRarity(GetExcludedIDs());
        if (result.augment != null)
        {
            currentAugmentIDs[slotIndex] = result.augment.ID;
            currentSelectedRarities[slotIndex] = result.rarity;

            UpdateSlotDisplay(slotIndex, result.augment, result.rarity);

            if (debugMode) Debug.Log($"Rerolled slot {slotIndex} to: {result.augment.Name} (ID: {result.augment.ID}, Rarity: {result.rarity})");
        }
        else
        {
            Debug.LogWarning($"Failed to generate new augment for reroll in slot {slotIndex}");
        }

        UpdateRerollDisplay(slotIndex);
    }

    // Passes both augment ID and selected rarity to the Registry
    public void ChooseAugment(int slotIndex)
    {
        if (debugMode) Debug.Log($"AugmentsMenu: ChooseAugment called for slot {slotIndex}");

        if (slotIndex < 0 || slotIndex >= currentAugmentIDs.Length)
        {
            Debug.LogError($"Invalid slot index: {slotIndex}");
            return;
        }

        int chosenId = currentAugmentIDs[slotIndex];
        if (chosenId == -1)
        {
            Debug.LogError($"No valid augment in slot {slotIndex}");
            return;
        }

        // Get the selected rarity for this slot
        string selectedRarity = GetCurrentAugmentRarity(slotIndex);

        // Pass both ID and rarity to the registry
        bool success = ApplyAugmentNew(chosenId, selectedRarity);

        if (success)
        {
            if (debugMode) Debug.Log($"Successfully applied {selectedRarity} augment {chosenId}, closing menu");

            // znajdź handler augmentów i uruchom efekt
            var handler = FindAnyObjectByType<AugmentEffectHandler>();
            if (handler != null)
                handler.ApplyAugmentEffect(chosenId);

            CloseMenu();
        }
        else
        {
            Debug.LogError($"Failed to apply {selectedRarity} augment {chosenId}");
        }
        FindAnyObjectByType<StatsUI>().RefreshUI();

        chosenAugmentsUI.RefreshUI();

    }

    private bool ApplyAugmentNew(int chosenId, string selectedRarity)
    {
        if (AugmentRegistry.Instance != null)
        {
            bool success = AugmentRegistry.Instance.ApplyAugment(chosenId, selectedRarity);
            if (success && debugMode)
            {
                Debug.Log($"Applied {selectedRarity} augment ID: {chosenId}");
            }
            return success;
        }
        else
        {
            Debug.LogError("AugmentRegistry.Instance is null when trying to apply augment");
            return false;
        }
    }

    private void CloseMenu()
    {
        augmentsMenu.SetActive(false);
        Cursor.visible = false;
        Time.timeScale = 1f;
        isMenuActive = false;

        if (debugMode) Debug.Log("AugmentsMenu: Menu closed");
    }

    public void ResetRerolls()
    {
        if (rerollsLeft == null) return;

        for (int i = 0; i < rerollsLeft.Length; i++)
        {
            rerollsLeft[i] = maxRerolls;
            UpdateRerollDisplay(i);
        }

        if (debugMode) Debug.Log($"Reset all rerolls to {maxRerolls}");
    }

    // ===== RARITY SYSTEM =====
    private string GetRandomRarityForAugment(int augmentId)
    {
        var augmentData = AugmentRegistry.Instance?.GetAugmentData(augmentId);
        if (augmentData == null)
            return "Common";

        string rarityString = augmentData.Rarity;
        if (string.IsNullOrEmpty(rarityString))
            return "Common";

        // Parse multiple rarities if separated by commas
        rarityString = rarityString.Trim('"');
        string[] rarities = rarityString.Split(',');
        List<string> cleanRarities = new List<string>();

        foreach (string rarity in rarities)
        {
            string cleanRarity = rarity.Trim();
            if (!string.IsNullOrEmpty(cleanRarity))
                cleanRarities.Add(cleanRarity);
        }

        if (cleanRarities.Count == 0) return "Common";
        if (cleanRarities.Count == 1) return cleanRarities[0];

        // Weighted random selection using consolidated rarity weights
        List<float> weights = new List<float>();
        foreach (string rarity in cleanRarities)
        {
            if (rarityWeights.ContainsKey(rarity))
                weights.Add(rarityWeights[rarity]);
            else
                weights.Add(10f);
        }

        float totalWeight = weights.Sum();
        float randomValue = UnityEngine.Random.Range(0f, totalWeight);

        Debug.Log($"[RARITY DEBUG] ID {augmentId}: Rarities={string.Join(",", cleanRarities)}, Weights={string.Join(",", weights)}, TotalWeight={totalWeight}, RandomValue={randomValue}");

        float currentWeight = 0f;
        for (int i = 0; i < cleanRarities.Count; i++)
        {
            currentWeight += weights[i];
            if (randomValue <= currentWeight)
            {
                Debug.Log($"[RARITY DEBUG] Selected: {cleanRarities[i]} (currentWeight={currentWeight})");
                return cleanRarities[i];
            }
        }

        return cleanRarities[0];
    }
    private AugmentWithRarity GenerateRandomAugmentWithRarity(List<int> excludeIDs)
    {
        if (AugmentRegistry.Instance == null)
        {
            Debug.LogError("AugmentsMenu: AugmentRegistry.Instance is null!");
            return new AugmentWithRarity();
        }

        var allAugments = AugmentRegistry.Instance.GetAllAugments();
        if (allAugments == null || allAugments.Count == 0)
        {
            Debug.LogError("AugmentsMenu: No augments available from AugmentRegistry!");
            return new AugmentWithRarity();
        }

        var availableAugments = allAugments
            .Where(a => a.Priority == 0 && !excludeIDs.Contains(a.ID) && a.Icon != null)
            .ToList();

        if (availableAugments.Count == 0)
        {
            Debug.LogWarning("AugmentsMenu: No available augments after exclusions!");
            return new AugmentWithRarity();
        }

        // Create weighted list of augment-rarity combinations using consolidated rarity weights
        List<(AugmentData augment, string rarity, float weight)> weightedCombinations = new List<(AugmentData, string, float)>();

        foreach (var augment in availableAugments)
        {
            string rarityString = augment.Rarity;
            if (string.IsNullOrEmpty(rarityString))
            {
                // Default to Common if no rarity specified
                if (rarityWeights.ContainsKey("Common"))
                    weightedCombinations.Add((augment, "Common", rarityWeights["Common"]));
                continue;
            }

            // Parse multiple rarities if separated by commas
            rarityString = rarityString.Trim('"');
            string[] rarities = rarityString.Split(',');

            foreach (string rarity in rarities)
            {
                string cleanRarity = rarity.Trim();
                if (!string.IsNullOrEmpty(cleanRarity))
                {
                    float weight = rarityWeights.ContainsKey(cleanRarity) ? rarityWeights[cleanRarity] : 10f;
                    weightedCombinations.Add((augment, cleanRarity, weight));
                }
            }
        }

        if (weightedCombinations.Count == 0)
        {
            Debug.LogWarning("AugmentsMenu: No weighted combinations available!");
            return new AugmentWithRarity();
        }

        // Perform weighted random selection
        float totalWeight = weightedCombinations.Sum(w => w.weight);
        float randomValue = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        foreach (var combination in weightedCombinations)
        {
            currentWeight += combination.weight;
            if (randomValue <= currentWeight)
            {
                return new AugmentWithRarity
                {
                    augment = combination.augment,
                    rarity = combination.rarity
                };
            }
        }

        // Fallback to first combination
        return new AugmentWithRarity
        {
            augment = weightedCombinations[0].augment,
            rarity = weightedCombinations[0].rarity
        };
    }

    // ===== GETTERS =====
    public string GetCurrentAugmentRarity(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < currentSelectedRarities.Length)
            return currentSelectedRarities[slotIndex];
        return "Common";
    }

    public AugmentData GetCurrentAugmentData(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= currentAugmentIDs.Length) return null;

        int augmentId = currentAugmentIDs[slotIndex];
        if (augmentId == -1) return null;

        return AugmentRegistry.Instance?.GetAugmentData(augmentId);
    }

    // ===== DEBUG METHODS =====
    [ContextMenu("Test Generate Augments")]
    public void TestGenerateAugments()
    {
        if (Application.isPlaying)
        {
            GenerateInitialAugments();
        }
    }

    [ContextMenu("Validate Forced Augments")]
    public void ValidateForcedAugments()
    {
        if (forcedAugmentIDsList == null || forcedAugmentIDsList.Count == 0)
        {
            Debug.Log("No forced augments configured.");
            return;
        }

        Debug.Log($"Validating {forcedAugmentIDsList.Count} forced augments:");
        for (int i = 0; i < forcedAugmentIDsList.Count; i++)
        {
            int id = forcedAugmentIDsList[i];
            if (id <= 0)
            {
                Debug.LogWarning($"Slot {i}: Invalid ID {id}");
                continue;
            }

            if (AugmentRegistry.Instance != null)
            {
                var augment = AugmentRegistry.Instance.GetAugmentData(id);
                if (augment != null)
                {
                    Debug.Log($"Slot {i}: Valid - {augment.Name} (ID: {id})");
                }
                else
                {
                    Debug.LogError($"Slot {i}: ID {id} not found in registry!");
                }
            }
            else
            {
                Debug.LogWarning($"AugmentRegistry not available for validation");
            }
        }
    }

    [ContextMenu("Log Current State")]
    public void LogCurrentState()
    {
        Debug.Log($"AugmentsMenu State:");
        Debug.Log($"- AugmentRegistry.Instance: {(AugmentRegistry.Instance != null ? "Available" : "NULL")}");
        Debug.Log($"- Available augments: {AugmentRegistry.Instance?.GetAllAugments().Count ?? 0}");
        Debug.Log($"- Max rerolls: {maxRerolls}");
        Debug.Log($"- Slots initialized: {currentAugmentIDs?.Length ?? 0}");
        Debug.Log($"- Forced augments count: {forcedAugmentIDsList?.Count ?? 0}");

        Debug.Log($"- Rarity weights from registry: {rarityWeights.Count} entries");
        Debug.Log($"- Rarity colors from registry: {rarityColors.Count} entries");

        if (currentAugmentIDs != null)
        {
            for (int i = 0; i < currentAugmentIDs.Length; i++)
            {
                Debug.Log($"  Slot {i}: ID={currentAugmentIDs[i]}, Rarity={currentSelectedRarities[i]}, Rerolls={rerollsLeft[i]}");
            }
        }
    }

    [ContextMenu("Test Rarity System Integration")]
    public void TestRaritySystemIntegration()
    {
        Debug.Log("=== Testing Rarity System Integration ===");

        if (AugmentRegistry.Instance == null)
        {
            Debug.LogError("AugmentRegistry not available!");
            return;
        }

        var registryWeights = AugmentRegistry.Instance.GetRarityWeights();
        var registryColors = AugmentRegistry.Instance.GetRarityColors();
        var menuWeights = rarityWeights;
        var menuColors = rarityColors;

        Debug.Log($"Registry has {registryWeights.Count} weight entries, {registryColors.Count} color entries");
        Debug.Log($"Menu sees {menuWeights.Count} weight entries, {menuColors.Count} color entries");

        bool weightsMatch = true;
        bool colorsMatch = true;

        foreach (var kvp in registryWeights)
        {
            if (!menuWeights.ContainsKey(kvp.Key) || menuWeights[kvp.Key] != kvp.Value)
            {
                Debug.LogError($"Weight mismatch for {kvp.Key}: Registry={kvp.Value}, Menu={menuWeights.GetValueOrDefault(kvp.Key, -1)}");
                weightsMatch = false;
            }
        }

        foreach (var kvp in registryColors)
        {
            if (!menuColors.ContainsKey(kvp.Key) || menuColors[kvp.Key] != kvp.Value)
            {
                Debug.LogError($"Color mismatch for {kvp.Key}: Registry={kvp.Value}, Menu={menuColors.GetValueOrDefault(kvp.Key, Color.black)}");
                colorsMatch = false;
            }
        }

        if (weightsMatch && colorsMatch)
        {
            Debug.Log("✅ Rarity system integration is working correctly!");
        }
        else
        {
            Debug.LogError("❌ Rarity system integration has issues!");
        }
    }
}