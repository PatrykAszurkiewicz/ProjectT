using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using System.Collections;

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
    public bool debugMode = true;

    // State variables
    private bool isHidden = false;
    private bool isMenuActive = false;
    private int[] rerollsLeft;
    private int[] currentAugmentIDs;
    private string[] currentSelectedRarities;

    // Data sources
    private static Sprite[] allSprites;

    public struct AugmentWithRarity
    {
        public AugmentData augment;
        public string rarity;
    }

    // Rarity system
    private Dictionary<string, float> rarityWeights = new Dictionary<string, float>
    {
        {"Common", 50f},
        {"Rare", 30f},
        {"Epic", 15f},
        {"Legendary", 5f}
    };

    private Dictionary<string, Color> rarityColors = new Dictionary<string, Color>
    {
        {"Common", Color.green},
        {"Rare", Color.blue},
        {"Epic", new Color(0.8f, 0f, 1f)},
        {"Legendary", new Color(1f, 0.6f, 0f)}
    };

    void Awake()
    {
        if (debugMode) Debug.Log("AugmentsMenu: Awake started");

        InitializeArrays();
        LoadSprites();
        SetupUI();

        if (debugMode) Debug.Log("AugmentsMenu: Awake completed");
    }

    void Start()
    {
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

        int slotCount = augmentImages.Length;
        rerollsLeft = new int[slotCount];
        currentAugmentIDs = new int[slotCount];
        currentSelectedRarities = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            rerollsLeft[i] = maxRerolls;
            currentAugmentIDs[i] = -1; // Initialize with invalid ID
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
                    rerollButtons[i].onClick.RemoveAllListeners(); // Clear existing listeners
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
                    selectButtons[i].onClick.RemoveAllListeners(); // Clear existing listeners
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

        if (debugMode) Debug.Log($"AugmentsMenu: AugmentRegistry ready with {AugmentRegistry.Instance.GetAllAugments().Count} augments");

        GenerateInitialAugments();
    }

    private void GenerateInitialAugments()
    {
        if (debugMode) Debug.Log("AugmentsMenu: Generating initial augments...");

        if (augmentImages == null)
        {
            Debug.LogError("AugmentsMenu: augmentImages is null!");
            return;
        }

        for (int i = 0; i < augmentImages.Length; i++)
        {
            var result = GenerateRandomAugmentWithRarity(GetExcludedIDs());
            if (result.augment != null)
            {
                currentAugmentIDs[i] = result.augment.ID;
                currentSelectedRarities[i] = result.rarity;

                UpdateSlotDisplay(i, result.augment, result.rarity);

                if (debugMode) Debug.Log($"Generated augment for slot {i}: {result.augment.Name} (ID: {result.augment.ID}, Rarity: {result.rarity})");
            }
            else
            {
                Debug.LogWarning($"AugmentsMenu: Could not generate augment for slot {i}");
            }

            UpdateRerollDisplay(i);
        }

        if (debugMode) Debug.Log("AugmentsMenu: Initial augments generation completed");
    }

    private AugmentData GenerateRandomAugment(List<int> excludeIDs)
    {
        if (AugmentRegistry.Instance == null)
        {
            Debug.LogError("AugmentsMenu: AugmentRegistry.Instance is null!");
            return null;
        }

        var allAugments = AugmentRegistry.Instance.GetAllAugments();
        if (allAugments == null || allAugments.Count == 0)
        {
            Debug.LogError("AugmentsMenu: No augments available from AugmentRegistry!");
            return null;
        }

        var availableAugments = allAugments
            .Where(a => a.Priority == 0 && !excludeIDs.Contains(a.ID) && a.Icon != null)
            .ToList();

        if (availableAugments.Count == 0)
        {
            Debug.LogWarning("AugmentsMenu: No available augments after exclusions!");
            return null;
        }

        // Simple random selection - you can add rarity weighting here later
        int randomIndex = UnityEngine.Random.Range(0, availableAugments.Count);
        return availableAugments[randomIndex];
    }

    private List<int> GetExcludedIDs()
    {
        var excluded = new List<int>();

        // Exclude currently displayed augments
        if (currentAugmentIDs != null)
        {
            excluded.AddRange(currentAugmentIDs.Where(id => id != -1));
        }

        // Exclude already applied augments
        if (AugmentRegistry.Instance != null)
        {
            excluded.AddRange(AugmentRegistry.Instance.GetAppliedAugments());
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
        if (debugMode) Debug.Log("AugmentsMenu: ActivateAugments called");

        if (!isMenuActive)
        {
            augmentsMenu.SetActive(true);
            Cursor.visible = true;
            Time.timeScale = 0f;
            isMenuActive = true;

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

        bool success = ApplyAugmentNew(chosenId);

        if (success)
        {
            if (debugMode) Debug.Log($"Successfully applied augment {chosenId}, closing menu");
            CloseMenu();
        }
        else
        {
            Debug.LogError($"Failed to apply augment {chosenId}");
        }
    }

    private bool ApplyAugmentNew(int chosenId)
    {
        if (AugmentRegistry.Instance != null)
        {
            bool success = AugmentRegistry.Instance.ApplyAugment(chosenId);
            if (success && debugMode)
            {
                Debug.Log($"Applied augment ID: {chosenId}");
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

        // Weighted random selection
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
        float currentWeight = 0f;

        for (int i = 0; i < cleanRarities.Count; i++)
        {
            currentWeight += weights[i];
            if (randomValue <= currentWeight)
                return cleanRarities[i];
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

        // Create weighted list of augment-rarity combinations
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

    [ContextMenu("Log Current State")]
    public void LogCurrentState()
    {
        Debug.Log($"AugmentsMenu State:");
        Debug.Log($"- AugmentRegistry.Instance: {(AugmentRegistry.Instance != null ? "Available" : "NULL")}");
        Debug.Log($"- Available augments: {AugmentRegistry.Instance?.GetAllAugments().Count ?? 0}");
        Debug.Log($"- Max rerolls: {maxRerolls}");
        Debug.Log($"- Slots initialized: {currentAugmentIDs?.Length ?? 0}");

        if (currentAugmentIDs != null)
        {
            for (int i = 0; i < currentAugmentIDs.Length; i++)
            {
                Debug.Log($"  Slot {i}: ID={currentAugmentIDs[i]}, Rerolls={rerollsLeft[i]}");
            }
        }
    }
}