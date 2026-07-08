using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
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

    public enum AugmentNavMode
    {
        CursorHover,       // the panel under the cursor enlarges
        DirectionalSwitch  // dpad / arrow keys move which panel is enlarged
    }

    [Header("Navigation")]
    [Tooltip("CursorHover: the augment panel under the cursor enlarges. Drive the cursor " +
             "with the mouse or the gamepad right stick (GamepadMenuCursor); the real " +
             "Select/Reroll buttons handle clicks. DirectionalSwitch: use the dpad or " +
             "arrow keys (A/D, Left/Right) to move which panel is enlarged, confirm with " +
             "South/A or Enter, reroll with West/X or R.")]
    public AugmentNavMode navMode = AugmentNavMode.CursorHover;

    [Tooltip("Scale applied to the highlighted/hovered panel. 1 = no enlargement.")]
    public float highlightScale = 1.12f;

    [Tooltip("Optional: panel roots to enlarge, one per slot in the same order as the " +
             "augment slots. Leave EMPTY to enlarge the Select buttons — which in this " +
             "prefab ARE the Augment1/2/3 panels, so the fallback already grows the panel.")]
    [SerializeField] private RectTransform[] panelRoots;

    [Tooltip("DirectionalSwitch only: the RIGHT TRIGGER confirms the highlighted augment " +
             "(matches the cursor-mode click). On by default.")]
    public bool directionalConfirmRightTrigger = true;

    [Tooltip("DirectionalSwitch only: also let the South face button (A / Cross) confirm. " +
             "OFF by default so A doesn't 'close' the menu unexpectedly — turn it on if you " +
             "want the A-to-confirm console convention.")]
    public bool directionalConfirmSouthButton = false;

    [Header("Co-op (Phase 6)")]
    [Tooltip("Which player this menu belongs to: 0 = Player 1, 1 = Player 2. " +
             "Leave at -1 for a single-player / shared menu (mouse-driven, applies " +
             "to player 0). Players spawn at runtime, so this is an INDEX — you " +
             "cannot drag a runtime PlayerRef into the inspector.")]
    public int boundPlayerIndex = -1;

    // Resolve the bound player's PlayerRef at runtime (null if unbound / not spawned).
    private PlayerRef BoundRef()
    {
        if (boundPlayerIndex < 0 || PlayerRegistry.Count == 0) return null;
        return PlayerRegistry.Instance.Get(boundPlayerIndex);
    }

    // Phase 6: gamepad/keyboard navigation for this menu's bound player, so two
    // viewport menus can be driven independently without a MultiplayerEventSystem.
    private PlayerInput _boundInput;
    private int _navSlot;

    // DirectionalSwitch right-trigger confirm: press-edge detection plus a carry-over
    // guard so a trigger still held as the menu opens (the player was firing to clear
    // the wave) can't auto-confirm the first augment.
    private bool _dirTriggerWasDown;
    private bool _dirSwallowTrigger;
    private bool _navJustActivated;

    // In co-op the GameOrchestrator owns Time.timeScale / cursor / suppression
    // (otherwise two menus saving & restoring it fight each other). In single
    // player the menu manages it itself, exactly as before.
    private bool CoopManaged => PlayerRegistry.Count > 1;

    // The PlayerStats that should receive this menu's picks.
    private PlayerStats Chooser()
    {
        var bound = BoundRef();
        if (bound != null && bound.Stats != null) return bound.Stats;
        if (PlayerRegistry.Count > 0)
        {
            var p0 = PlayerRegistry.Instance.Get(0);
            if (p0 != null && p0.Stats != null) return p0.Stats;
        }
        return FindAnyObjectByType<PlayerStats>();
    }

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


    //[SerializeField] private ChosenAugmentsUI chosenAugmentsUI; // menu pauzy itp

    // State variables
    private bool isHidden = false;
    // Saved host state so this menu nests correctly (e.g. opened from the
    // pause menu) instead of forcing timeScale/cursor/input back to defaults.
    private float prevTimeScale = 1f;
    private bool prevCursorVisible;
    private bool prevInputSuppressed;
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

        EnsureInitialized();

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
        //chosenAugmentsUI = FindAnyObjectByType<ChosenAugmentsUI>(FindObjectsInactive.Include);

        if (debugMode) Debug.Log("AugmentsMenu: Start called");

        augmentsMenu.SetActive(false);

        // Wait for AugmentRegistry to be ready
        StartCoroutine(WaitForRegistryAndGenerate());
    }

    // Full one-time init. Awake runs this, but an INACTIVE menu (co-op P2's
    // reward menu starts hidden) never gets Awake — yet the orchestrator/button
    // can still call ActivateAugments on it. So we also run this on activation.
    private bool _initialized;
    private void EnsureInitialized()
    {
        if (_initialized) return;
        if (forcedAugmentIDsList == null) forcedAugmentIDsList = new List<int>();
        InitializeArrays();
        LoadSprites();
        SetupUI();
        _initialized = true;
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

        // Defensive: a co-op duplicate (or a menu enabled before its Awake ran
        // InitializeArrays) can reach here with the per-slot arrays unsized. Build
        // them now so we never index a null array.
        if (currentAugmentIDs == null || currentAugmentIDs.Length != augmentImages.Length ||
            currentSelectedRarities == null || currentSelectedRarities.Length != augmentImages.Length ||
            rerollsLeft == null || rerollsLeft.Length != augmentImages.Length)
        {
            InitializeArrays();
        }
        if (currentAugmentIDs == null) return; // InitializeArrays bailed (no images)

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
            4,   // Obstacle drawer
            65,  // Unlocked grappling hook
            66,  // Unlocked ranged weapon
            81,  // Unlocked shield
            93,  // Unlocked flamethrower
            94,  // Unlocked portable obstacles
            231, // Unlocked heavy melee weapon
            232, // Unlocked dagger melee weapon
            323, // Unlocked stealth cloak
            // One-time special effects
            30,  // God Mode (permanent penalty)
            37,  // Quick revive (once per wave)
            163, // Time Rewind (once per game)
            328, // Cooldown reduction (flat -20%, doesn't stack)
            // Parry upgrades (select once each)
            330, // Longer Parry Stun
            331, // Powerful Parry
            332, // Longer Parry Window
            333, // Heal on Parry
            // One-time tower augment
            344, // Phoenix Protocol (revive recurs every stage)
            334, // Last Stand when player health is below 30%
            339, // Reduces player Max Health by 50%. In return, all towers cost 40% less energy/resources to build and repair
            345, // Generator tower deals additional damage around itself equal to the energy generated
        };

        if (AugmentRegistry.Instance != null)
        {
            var appliedAugments = AugmentRegistry.Instance.GetAppliedAugments();
            // Only exclude non-repeatable augments
            excluded.AddRange(appliedAugments.Where(id => nonRepeatableAugments.Contains(id)));
        }

        //  BLUEPRINT/STARTER EXCLUSIONS 
        // 1) Melee (ID 2) — starter weapon, never offer an "Unlock Melee" card.
        excluded.Add(2);

        // 2) Any weapon/tool unlock-augment whose slot is already unlocked in
        //    WeaponUnlockRegistry (i.e. already in the hotbar). Stops the popup
        //    offering "Unlock Flamethrower" when the player just picked up the
        //    flamethrower blueprint and has it equipped.
        if (WeaponUnlockRegistry.Instance != null)
        {
            // augmentID → slot mapping (mirrors WeaponUnlockRegistry.AugmentToSlot)
            (int augId, int slot)[] unlockMap =
            {
                (2, 0), (66, 1), (65, 2), (81, 3), (4, 4),
                (93, 5), (314, 6), (315, 7), (317, 8), (316, 9), (318, 10),
                (323, 13),
            };
            foreach (var (augId, slot) in unlockMap)
            {
                // Phase 5/6: check THIS menu's player's pool, not a global one.
                int chooserIdx = boundPlayerIndex >= 0 ? boundPlayerIndex : 0;
                if (WeaponUnlockRegistry.Instance.IsUnlocked(slot, chooserIdx))
                    excluded.Add(augId);
            }
        }

        return excluded.Distinct().ToList();
    }

    private void UpdateSlotDisplay(int slotIndex, AugmentData augment, string rarity)
    {
        if (slotIndex < 0 || slotIndex >= augmentImages.Length) return;

        // Update image
        if (augmentImages[slotIndex] != null)
        {
            Sprite sprite = allSprites != null
                ? System.Array.Find(allSprites, s => s != null && s.name == augment.ID.ToString())
                : null;
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
        // Mode routing (Phase 6): the single-player menu (boundPlayerIndex -1) and
        // the per-player menus (0/1) must never both open.
        bool coopMode = PlayerRegistry.Count > 1;
        if (coopMode && boundPlayerIndex < 0)
        {
            // This is the single menu, but we're in co-op (e.g. a debug button wired
            // to it). Hand off to the per-player menus and don't open this one.
            foreach (var m in FindObjectsByType<AugmentsMenu>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (m != null && m != this && m.boundPlayerIndex >= 0)
                    m.ActivateAugments();
            return;
        }
        if (!coopMode && boundPlayerIndex >= 0) return;            // per-player menu in single player
        if (boundPlayerIndex >= 0 && BoundRef() == null) return;   // bound player not spawned

        EnsureInitialized();   // an inactive (co-op) menu never ran Awake

        if (!isMenuActive)
        {
            augmentsMenu.SetActive(true);

            // Reward-screen appear SFX. Gate on boundPlayerIndex <= 0 so co-op's
            // two per-player menus (P0 and P1) don't both chime — only the first
            // (or the single-player menu, index -1) plays it.
            if (boundPlayerIndex <= 0
                && AudioManager.instance != null && FMODEvents.instance != null
                && !FMODEvents.instance.augmentScreen.IsNull)
            {
                AudioManager.instance.PlayOneShot(FMODEvents.instance.augmentScreen, Vector3.zero);
            }

            if (!CoopManaged)
            {
                prevCursorVisible = Cursor.visible;
                prevTimeScale = Time.timeScale;
                prevInputSuppressed = PlayerAttack.InputSuppressed;
                Cursor.visible = true;
                Time.timeScale = 0f;
                PlayerAttack.InputSuppressed = true;
            }
            isMenuActive = true;

            // Force regeneration of augments each time menu opens
            GenerateInitialAugments();

            // Highlight init depends on the mode. DirectionalSwitch pre-selects the
            // first panel so there's something to move from. CursorHover starts with
            // NOTHING enlarged — this is the fix for the left panel being stuck at
            // 1.12x: it only grows once the cursor is actually over it.
            if (navMode == AugmentNavMode.DirectionalSwitch)
            {
                SetupNavHighlight();
                _navJustActivated = true;
                // The right trigger now CONFIRMS the highlighted panel, so stop
                // GamepadMenuCursor from also emulating a mouse click with it.
                GamepadMenuCursor.ClicksSuppressed = true;
            }
            else
            {
                _navSlot = -1;
                ClearNavHighlight();
                GamepadMenuCursor.ClicksSuppressed = false; // cursor click drives selection
            }

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

        // Route this pick to the chooser (Phase 5). Single menu still in Phase 5;
        // Phase 6 binds one menu per player.
        PlayerStats chooser = Chooser();

        // Pass ID, rarity and chooser to the registry
        bool success = ApplyAugmentNew(chosenId, selectedRarity, chooser);

        if (success)
        {
            if (debugMode) Debug.Log($"Successfully applied {selectedRarity} augment {chosenId}, closing menu");

            // znajdź handler augmentów i uruchom efekt (na wybierającego gracza)
            var handler = FindAnyObjectByType<AugmentEffectHandler>();
            if (handler != null)
                handler.ApplyAugmentEffect(chosenId, chooser);

            CloseMenu();
        }
        else
        {
            Debug.LogError($"Failed to apply {selectedRarity} augment {chosenId}");
        }
        //FindAnyObjectByType<StatsUI>().RefreshUI();
        FindAnyObjectByType<StatsPanelUI>()?.RefreshAll();

        //chosenAugmentsUI?.RefreshUI();


    }

    private bool ApplyAugmentNew(int chosenId, string selectedRarity, PlayerStats chooser)
    {
        if (AugmentRegistry.Instance != null)
        {
            bool success = AugmentRegistry.Instance.ApplyAugment(chosenId, selectedRarity, chooser);
            if (success)
            {
                int chooserIdx = 0;
                var pref = chooser != null ? chooser.GetComponent<PlayerRef>() : null;
                if (pref != null) chooserIdx = pref.PlayerIndex;
                RunPersistence.Instance?.RecordAugment(chosenId, selectedRarity, chooserIdx);
            }
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

    // ── Phase 6: per-player pad/keyboard navigation ──────────────────────
    // Only menus explicitly bound to a player navigate this way; an unbound
    // (single-player) menu keeps its mouse-only behaviour untouched.
    private void Update()
    {
        if (!isMenuActive) return;

        if (navMode == AugmentNavMode.CursorHover) UpdateCursorHover();
        else UpdateDirectional();
    }

    // CursorHover: enlarge whichever panel the on-screen cursor is over. The cursor
    // is moved by the mouse or by GamepadMenuCursor (right stick); actual selection
    // happens through the real Select/Reroll buttons, so here we only do the visual
    // highlight. Works the same in single player and co-op.
    private void UpdateCursorHover()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        int hovered = SlotUnderScreenPoint(mouse.position.ReadValue());
        if (hovered != _navSlot)
        {
            _navSlot = hovered;          // -1 when the cursor is over no panel
            ApplyNavHighlight();
        }
    }

    // DirectionalSwitch: dpad / arrow keys move the enlarged panel; South/A or Enter
    // confirms, West/X or R rerolls. Uses the bound player's devices in co-op, or the
    // current gamepad/keyboard in single player.
    private void UpdateDirectional()
    {
        Gamepad pad;
        Keyboard kb;
        if (boundPlayerIndex >= 0)
        {
            if (_boundInput == null)
            {
                var bound = BoundRef();
                if (bound != null)
                    _boundInput = bound.GetComponent<PlayerInput>()
                                  ?? bound.GetComponentInChildren<PlayerInput>();
            }
            pad = BoundPad();
            kb = BoundKeyboard();
        }
        else
        {
            pad = Gamepad.current;
            kb = Keyboard.current;
        }

        // Right-trigger confirm: edge-detected, with a carry-over guard for a trigger
        // still held from clearing the wave at the moment the menu opened.
        float trig = pad != null ? pad.rightTrigger.ReadValue() : 0f;
        bool trigDown = trig > 0.5f;
        if (_navJustActivated)
        {
            _navJustActivated = false;
            _dirSwallowTrigger = trigDown;   // already held on open -> ignore until released
            _dirTriggerWasDown = trigDown;
        }
        if (_dirSwallowTrigger && !trigDown) _dirSwallowTrigger = false;
        bool trigPressed = trigDown && !_dirTriggerWasDown && !_dirSwallowTrigger;
        _dirTriggerWasDown = trigDown;

        int dir = ReadHorizontalStep(pad, kb);

        // Nothing highlighted yet (e.g. opened in a mode with no pre-select): the
        // first directional press lands on the first panel rather than stepping past.
        if (_navSlot < 0)
        {
            if (dir != 0) SetupNavHighlight();
            return;
        }

        if (dir != 0) MoveNav(dir);

        bool confirm = (directionalConfirmRightTrigger && trigPressed)
                    || (directionalConfirmSouthButton && pad != null && pad.buttonSouth.wasPressedThisFrame)
                    || (kb != null && kb.enterKey.wasPressedThisFrame);
        if (confirm) { MenuClickSFX.Play(); ChooseAugment(_navSlot); }

        bool reroll = (pad != null && pad.buttonWest.wasPressedThisFrame)
                   || (kb != null && kb.rKey.wasPressedThisFrame);
        if (reroll) { MenuClickSFX.Play(); Reroll(_navSlot); }
    }

    private int ReadHorizontalStep(Gamepad pad, Keyboard kb)
    {
        if (pad != null)
        {
            if (pad.dpad.right.wasPressedThisFrame) return +1;
            if (pad.dpad.left.wasPressedThisFrame) return -1;
        }
        if (kb != null)
        {
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) return +1;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) return -1;
        }
        return 0;
    }

    private Gamepad BoundPad()
    {
        if (_boundInput == null) return null;
        foreach (var d in _boundInput.devices) if (d is Gamepad g) return g;
        return null;
    }

    private Keyboard BoundKeyboard()
    {
        if (_boundInput == null) return null;
        foreach (var d in _boundInput.devices) if (d is Keyboard k) return k;
        return null;
    }

    private void SetupNavHighlight()
    {
        _navSlot = FirstValidSlot();
        ApplyNavHighlight();
    }

    private int FirstValidSlot()
    {
        if (currentAugmentIDs == null) return 0;
        for (int i = 0; i < currentAugmentIDs.Length; i++)
            if (currentAugmentIDs[i] != -1) return i;
        return 0;
    }

    private void MoveNav(int dir)
    {
        if (currentAugmentIDs == null || currentAugmentIDs.Length == 0) return;
        int n = currentAugmentIDs.Length;
        for (int step = 0; step < n; step++)
        {
            _navSlot = (_navSlot + dir + n) % n;
            if (currentAugmentIDs[_navSlot] != -1) break;
        }
        ApplyNavHighlight();
    }

    private void ApplyNavHighlight()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var target = HighlightTarget(i);
            if (target == null) continue;
            target.localScale = (i == _navSlot) ? Vector3.one * highlightScale : Vector3.one;
        }
    }

    private void ClearNavHighlight()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var target = HighlightTarget(i);
            if (target != null) target.localScale = Vector3.one;
        }
    }

    // One slot per Select button (one per panel).
    private int SlotCount => selectButtons != null ? selectButtons.Length : 0;

    // What actually gets scaled for slot i: an explicit panelRoots entry if the
    // inspector provides one, else the Select button's transform. In this prefab the
    // Select buttons ARE the Augment1/2/3 panels, so the fallback grows the panel.
    private Transform HighlightTarget(int i)
    {
        if (panelRoots != null && i < panelRoots.Length && panelRoots[i] != null)
            return panelRoots[i];
        if (selectButtons != null && i < selectButtons.Length && selectButtons[i] != null)
            return selectButtons[i].transform;
        return null;
    }

    // Which slot's panel the screen-space cursor is over, or -1 for none.
    private int SlotUnderScreenPoint(Vector2 screenPos)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var rt = HighlightTarget(i) as RectTransform;
            if (rt == null || !rt.gameObject.activeInHierarchy) continue;
            Camera cam = HighlightCamera(rt);
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam))
                return i;
        }
        return -1;
    }

    private Camera HighlightCamera(Component c)
    {
        var canvas = c.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    private void CloseMenu()
    {
        augmentsMenu.SetActive(false);
        // Single player restores the host state here. In co-op the orchestrator
        // owns it (it waits for BOTH menus before un-pausing).
        if (!CoopManaged)
        {
            Cursor.visible = prevCursorVisible;
            Time.timeScale = prevTimeScale;
            PlayerAttack.InputSuppressed = prevInputSuppressed;
        }
        isMenuActive = false;
        ClearNavHighlight();
        GamepadMenuCursor.ClicksSuppressed = false;

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

        // BLUEPRINT GATE — weapon/tool unlock augments are only eligible if the
        // player has discovered the corresponding blueprint (boss drop pickup).
        // Filters IDs 2, 4, 65, 66, 81, 93, 314, 315, 316, 317, 318 against
        // WeaponBlueprintRegistry. Non-unlock augments pass through unchanged.
        availableAugments = AugmentBlueprintGate.FilterByBlueprints(availableAugments);

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
            Debug.Log("Rarity system integration is working correctly");
        }
        else
        {
            Debug.LogError("Rarity system integration has issues");
        }
    }
}

