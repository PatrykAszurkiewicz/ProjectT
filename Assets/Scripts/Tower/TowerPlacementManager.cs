using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Collections;
using FMOD.Studio;

public class TowerPlacementManager : MonoBehaviour
{
    public static TowerPlacementManager Instance;

    [Header("Tower Prefabs - Assign Individual Towers")]
    public GameObject towerPrefab1;
    public GameObject towerPrefab2;
    public GameObject towerPrefab3;
    public GameObject towerPrefab4;
    public GameObject towerPrefab5;
    public GameObject towerPrefab6;

    [System.NonSerialized]
    private List<GameObject> towerPrefabs = new List<GameObject>();

    public int selectedTowerIndex = 0;

    [Header("Player Proximity Settings")]
    public float buildRange = 1.2f;
    public Transform playerTransform;
    public bool requirePlayerProximity = true;

    [Header("Tower Creation Animation")]
    public string towerCreationSpritePath = "Sprites/tower_creation_decay_spritesheet4";
    public float creationAnimationSpeed = 0.1f;
    public bool playCreationAnimation = true;

    [Header("Energy Repair Settings")]
    public int energyRepairAmount = 10;
    public float energyRepairCooldown = 0.2f;

    [Header("Continuous Supply Settings")]
    public bool useContinuousSupply = true; // Toggle between old and new behavior

    [Header("UI References")]
    public GameObject towerSelectionUI;

    private TowerDefenseMap mapGenerator;
    private bool isPlacementMode = false;

    [System.NonSerialized]
    private List<TowerSlot> allSlots = new List<TowerSlot>();
    [System.NonSerialized]
    private TowerSlot currentHighlightedSlot = null;
    [System.NonSerialized]
    private IEnergyConsumer currentHighlightedConsumer = null;
    [System.NonSerialized]
    private TowerSelectionWheel selectionWheel;

    private bool clickProcessed = false;
    private float lastRepairTime = -Mathf.Infinity;

    // Continuous supply system
    private bool isCurrentlySupplying = false;
    private IEnergyConsumer currentSupplyTarget = null;

    // REPAIR SOUND SYSTEM
    private EventInstance repairSound;
    private bool isRepairSoundInitialized = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        // Initialize the Non-Serialized fields
        allSlots = new List<TowerSlot>();
        towerPrefabs = new List<GameObject>();
    }

    void Start()
    {
        mapGenerator = FindFirstObjectByType<TowerDefenseMap>();
        if (playerTransform == null)
        {
            PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
            if (player != null)
            {
                playerTransform = player.transform;
            }
            else
            {
                requirePlayerProximity = false;
            }
        }

        // Build tower list from individual prefab fields
        BuildTowerList();

        // Create selection wheel
        CreateSelectionWheel();

        // Initialize repair sound
        InitializeRepairSound();
    }

    private void InitializeRepairSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            try
            {
                repairSound = AudioManager.instance.CreateInstance(FMODEvents.instance.towerRepair);
                isRepairSoundInitialized = true;
                //Debug.Log("[TowerPlacement] Repair sound initialized successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TowerPlacement] Failed to initialize repair sound: {e.Message}");
                isRepairSoundInitialized = false;
            }
        }
        else
        {
            Debug.LogWarning("[TowerPlacement] AudioManager or FMODEvents not available for repair sound initialization");
            // Try again later
            StartCoroutine(RetryRepairSoundInitialization());
        }
    }

    private IEnumerator RetryRepairSoundInitialization()
    {
        float retryInterval = 1f;
        int maxRetries = 5;
        int retryCount = 0;

        while (!isRepairSoundInitialized && retryCount < maxRetries)
        {
            yield return new WaitForSeconds(retryInterval);

            if (AudioManager.instance != null && FMODEvents.instance != null)
            {
                try
                {
                    repairSound = AudioManager.instance.CreateInstance(FMODEvents.instance.towerRepair);
                    isRepairSoundInitialized = true;
                    //Debug.Log("[TowerPlacement] Repair sound initialized successfully on retry");
                    break;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[TowerPlacement] Repair sound initialization retry {retryCount + 1} failed: {e.Message}");
                }
            }

            retryCount++;
        }

        if (!isRepairSoundInitialized)
        {
            Debug.LogError("[TowerPlacement] Failed to initialize repair sound after multiple retries");
        }
    }

    private void UpdateRepairSound()
    {
        if (!isRepairSoundInitialized) return;

        // Check if target is still valid (not a destroyed core)
        bool shouldPlaySound = isCurrentlySupplying && currentSupplyTarget != null;

        if (shouldPlaySound && currentSupplyTarget is CentralCore core)
        {
            if (core.IsDestroyed())
            {
                shouldPlaySound = false;
            }
        }

        if (shouldPlaySound)
        {
            PLAYBACK_STATE playbackState;
            repairSound.getPlaybackState(out playbackState);

            if (playbackState.Equals(PLAYBACK_STATE.STOPPED))
            {
                repairSound.start();
            }
        }
        else
        {
            repairSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        }
    }

    void BuildTowerList()
    {
        towerPrefabs = new List<GameObject>();

        if (towerPrefab1 != null) towerPrefabs.Add(towerPrefab1);
        if (towerPrefab2 != null) towerPrefabs.Add(towerPrefab2);
        if (towerPrefab3 != null) towerPrefabs.Add(towerPrefab3);
        if (towerPrefab4 != null) towerPrefabs.Add(towerPrefab4);
        if (towerPrefab5 != null) towerPrefabs.Add(towerPrefab5);
        if (towerPrefab6 != null) towerPrefabs.Add(towerPrefab6);
    }

    void CreateSelectionWheel()
    {
        // Per-player wheels are created by each PlayerTowerPlacer (Phase 4).
    }

    // Public method to expose placement mode state for EnergyManager
    public bool IsInPlacementMode()
    {
        return isPlacementMode;
    }

    void Update()
    {
        // Per-player placement (toggle / aim / build) is owned by PlayerTowerPlacer.
        // The hub keeps the shared services: economy, slot registry, supply, repair.
        HandleMouseClicks();   // consumer energy-supply for the mouse player only
        UpdateRepairSound();

        if (isPlacementMode && requirePlayerProximity && Time.frameCount % 5 == 0)
        {
            UpdateEnergyConsumerHighlights();
        }
    }

    void UpdateEnergyConsumerHighlights()
    {
        // Clear previous highlight
        if (currentHighlightedConsumer != null)
        {
            SetConsumerHighlight(currentHighlightedConsumer, false);
            currentHighlightedConsumer = null;
        }

        if (!requirePlayerProximity || playerTransform == null || EnergyManager.Instance == null)
            return;

        // Get sprite cursor position
        CursorPointer cursorPointer = FindFirstObjectByType<CursorPointer>();
        if (cursorPointer == null) return;

        Vector3 spritePosition = cursorPointer.transform.position;

        var allConsumers = EnergyManager.Instance.GetAllEnergyConsumers();
        //Debug.Log($"[TowerPlacement] Found {allConsumers.Count} energy consumers. Cursor at: {spritePosition}");

        foreach (var consumer in allConsumers)
        {
            if (consumer != null)
            {
                string consumerType = consumer.GetType().Name;
                Vector3 pos = consumer.GetPosition();
                float distance = Vector3.Distance(spritePosition, pos);
                //Debug.Log($"[TowerPlacement] {consumerType} at {pos}, distance: {distance:F2} (maxSupplyDistance: {EnergyManager.Instance.maxSupplyDistance})");
            }
        }

        IEnergyConsumer target = GetSupplyTarget(spritePosition);

        if (target != null)
        {
            //Debug.Log($"[TowerPlacement] Target found: {target.GetType().Name}");
            bool inPlayerRange = IsPlayerInRange(target);
            //Debug.Log($"[TowerPlacement] Player in range: {inPlayerRange} (supplyRange: {EnergyManager.Instance.supplyRange})");

            if (inPlayerRange)
            {
                SetConsumerHighlight(target, true);
                currentHighlightedConsumer = target;
                //Debug.Log($"[TowerPlacement] Highlighting target: {target.GetType().Name}");
            }
        }
        else
        {
            //Debug.Log("[TowerPlacement] No target found");
        }
    }

    IEnergyConsumer GetSupplyTarget(Vector3 position)
    {
        if (EnergyManager.Instance == null) return null;

        var allConsumers = EnergyManager.Instance.GetAllEnergyConsumers();
        IEnergyConsumer closest = null;
        float closestDistance = EnergyManager.Instance.maxSupplyDistance;

        foreach (var consumer in allConsumers)
        {
            if (consumer == null) continue;

            float distance = Vector3.Distance(position, consumer.GetPosition());
            //Debug.Log($"[GetSupplyTarget] Checking {consumer.GetType().Name}: distance {distance:F2} vs maxSupplyDistance {closestDistance:F2}");

            if (distance < closestDistance)
            {
                closest = consumer;
                closestDistance = distance;
                //Debug.Log($"[GetSupplyTarget] New closest: {consumer.GetType().Name} at {distance:F2}");
            }
        }

        return closest;
    }

    void SetConsumerHighlight(IEnergyConsumer consumer, bool highlight)
    {
        if (consumer is CentralCore core)
        {
            core.SetHighlight(highlight);
        }
        else if (consumer is MonoBehaviour mb)
        {
            var spriteRenderer = mb.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                if (highlight)
                {
                    Color currentColor = spriteRenderer.color;
                    spriteRenderer.color = Color.Lerp(currentColor, Color.cyan, 0.3f);
                }
                else
                {
                    EnergyManager.Instance?.UpdateConsumerVisuals(consumer, spriteRenderer);
                }
            }
        }
    }

    void UpdateSlotHighlights()
    {
        if (currentHighlightedSlot != null)
        {
            currentHighlightedSlot.SetHighlight(false);
            currentHighlightedSlot = null;
        }

        if (!requirePlayerProximity || playerTransform == null) return;

        Vector2 cursorDirection = GetCursorDirectionFromPlayer();
        TowerSlot closestSlotInDirection = null;
        float closestDistance = float.MaxValue;

        foreach (TowerSlot slot in allSlots)
        {
            if (slot == null || !slot.IsAvailable) continue;

            Vector2 directionToSlot = (slot.transform.position - playerTransform.position).normalized;
            float distance = Vector2.Distance(playerTransform.position, slot.transform.position);

            if (distance > buildRange) continue;

            float dotProduct = Vector2.Dot(cursorDirection, directionToSlot);
            if (dotProduct > 0.1f && distance < closestDistance)
            {
                closestDistance = distance;
                closestSlotInDirection = slot;
            }
        }

        if (closestSlotInDirection != null)
        {
            closestSlotInDirection.SetHighlight(true);
            currentHighlightedSlot = closestSlotInDirection;
        }
    }

    Vector2 GetCursorDirectionFromPlayer()
    {
        if (playerTransform == null) return Vector2.right;

        // Find the CursorPointer component to get the sprite cursor direction
        CursorPointer cursorPointer = FindFirstObjectByType<CursorPointer>();
        if (cursorPointer != null)
        {
            Vector3 direction = (cursorPointer.transform.position - playerTransform.position).normalized;
            return new Vector2(direction.x, direction.y);
        }

        // Fallback to right direction if no cursor pointer found
        return Vector2.right;
    }

    bool IsPlayerInRange(IEnergyConsumer consumer)
    {
        if (consumer == null || EnergyManager.Instance == null) return false;
        return IsPlayerInRange(consumer.GetPosition());
    }

    bool IsPlayerInRange(Vector3 targetPosition)
    {
        if (!requirePlayerProximity || playerTransform == null || EnergyManager.Instance == null) return true;
        float distance = Vector2.Distance(playerTransform.position, targetPosition);
        bool inRange = distance <= EnergyManager.Instance.supplyRange;
        //Debug.Log($"[IsPlayerInRange] Player distance: {distance:F2} vs supplyRange: {EnergyManager.Instance.supplyRange}, inRange: {inRange}");
        return inRange;
    }

    void HandleInput()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TogglePlacementMode();
        }

        // Tower selection with number keys
        if (towerPrefabs != null)
        {
            for (int i = 1; i <= towerPrefabs.Count && i <= 9; i++)
            {
                if (Keyboard.current[(Key)(Key.Digit1 + i - 1)].wasPressedThisFrame)
                {
                    selectedTowerIndex = i - 1;
                }
            }
        }

        // TODO add tower disassembly mechanics 
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            RemoveTowerAtMousePosition();
        }
    }

    void HandleMouseClicks()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame && !clickProcessed)
        {
            clickProcessed = true;

            if (isPlacementMode)
            {
                if (currentHighlightedConsumer != null)
                {
                    //Debug.Log($"[TowerPlacement] Mouse clicked on highlighted consumer: {GetConsumerDisplayName(currentHighlightedConsumer)}");

                    if (useContinuousSupply)
                    {
                        StartContinuousSupplyToConsumer(currentHighlightedConsumer);
                    }
                    else
                    {
                        if (Time.time - lastRepairTime >= energyRepairCooldown)
                        {
                            TryRepairEnergyConsumer(currentHighlightedConsumer);
                        }
                    }
                }
                else
                {
                    CursorPointer cursorPointer = FindFirstObjectByType<CursorPointer>();
                    if (cursorPointer != null)
                    {
                        Vector3 spritePosition = cursorPointer.transform.position;
                        IEnergyConsumer target = GetSupplyTarget(spritePosition);

                        if (target != null && IsPlayerInRange(target))
                        {
                            if (useContinuousSupply)
                            {
                                StartContinuousSupplyToConsumer(target);
                            }
                            else
                            {
                                if (Time.time - lastRepairTime >= energyRepairCooldown)
                                {
                                    TryRepairEnergyConsumer(target);
                                }
                            }
                        }
                        // else: slot placement is handled per-player by PlayerTowerPlacer.
                    }
                    // else: no cursor -> nothing to supply; placement is per-player.
                }
            }
            StartCoroutine(ResetClickProcessing());
        }

        // Handle mouse button release
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            if (isCurrentlySupplying)
            {
                //Debug.Log("[TowerPlacement] Mouse released, stopping continuous supply");
            }
            StopContinuousSupply();
        }

        // Continue supplying while button is held
        if (Mouse.current.leftButton.isPressed && isCurrentlySupplying && currentSupplyTarget != null)
        {
            ContinuouslySupplyEnergy();
        }
    }

    private void StartContinuousSupplyToConsumer(IEnergyConsumer consumer)
    {
        if (consumer == null || EnergyManager.Instance == null) return;
        // Block supply to destroyed Central Core
        if (consumer is CentralCore core && core.IsDestroyed())
        {
            return;
        }

        //Debug.Log($"[TowerPlacement] Starting continuous supply to {GetConsumerDisplayName(consumer)}");

        isCurrentlySupplying = true;
        currentSupplyTarget = consumer;

        EnergyManager.Instance.StartContinuousSupply(consumer);
        EnergyManager.Instance.SupplyEnergyToTarget(consumer, 0);

        // Immediate first supply
        ContinuouslySupplyEnergy();

        // Sound will be started by UpdateRepairSound() in the next frame
    }

    private void ContinuouslySupplyEnergy()
    {
        if (currentSupplyTarget == null || EnergyManager.Instance == null) return;

        // Check if target still needs energy - allow Central Core to always receive energy
        if (currentSupplyTarget.GetEnergyPercentage() >= 0.999f && !(currentSupplyTarget is CentralCore))
        {
            //Debug.Log($"[TowerPlacement] Target {GetConsumerDisplayName(currentSupplyTarget)} is full, stopping supply");
            StopContinuousSupply();
            return;
        }

        EnergyManager.Instance.SupplyEnergyToTarget(currentSupplyTarget, 0);
        //Debug.Log($"[TowerPlacement] Continuously supplying energy to {GetConsumerDisplayName(currentSupplyTarget)}");
    }

    private void StopContinuousSupply()
    {
        if (isCurrentlySupplying)
        {
            //Debug.Log($"[TowerPlacement] Stopping continuous supply to {GetConsumerDisplayName(currentSupplyTarget)}");
        }

        isCurrentlySupplying = false;
        currentSupplyTarget = null;

        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.StopSupplying();
        }
    }

    // Keep the original TryRepairEnergyConsumer for discrete repairs when useContinuousSupply is false
    void TryRepairEnergyConsumer(IEnergyConsumer consumer)
    {
        if (consumer == null || EnergyManager.Instance == null) return;

        if (!EnergyManager.Instance.CanPlayerAfford(energyRepairAmount)) return;

        float energyPercent = consumer.GetEnergyPercentage();

        if (energyPercent >= 0.999f && !(consumer is CentralCore)) return;

        if (EnergyManager.Instance.TrySpendPlayerEnergy(energyRepairAmount))
        {
            consumer.SupplyEnergy(energyRepairAmount);
            lastRepairTime = Time.time;

            // Play a one-shot repair sound for discrete repairs
            if (AudioManager.instance != null && FMODEvents.instance != null)
            {
                AudioManager.instance.PlayOneShot(FMODEvents.instance.towerRepair, consumer.GetPosition());
            }
        }
    }

    string GetConsumerDisplayName(IEnergyConsumer consumer)
    {
        if (consumer == null) return "null";
        if (consumer is Tower tower)
            return $"Tower ({tower.towerName})";
        else if (consumer is CentralCore)
            return "Central Core";
        else
            return $"Unknown Consumer ({consumer.GetType().Name})";
    }

    System.Collections.IEnumerator ResetClickProcessing()
    {
        yield return null;
        clickProcessed = false;
    }

    void HandleSlotClick()
    {
        if (currentHighlightedSlot != null && currentHighlightedSlot.IsAvailable)
        {
            OnSlotClicked(currentHighlightedSlot);
        }
        else
        {
            Vector2 cursorDirection = GetCursorDirectionFromPlayer();
            TowerSlot nearestSlot = FindNearestSlotInDirection(cursorDirection);

            //  Only use the nearby slot if cursor is close to it
            if (nearestSlot != null)
            {
                // Get cursor position
                CursorPointer cursorPointer = FindFirstObjectByType<CursorPointer>();
                if (cursorPointer != null)
                {
                    float distanceToCursor = Vector2.Distance(cursorPointer.transform.position, nearestSlot.transform.position);

                    // Only auto-select slots that are very close to cursor (within 0.8 units)
                    if (distanceToCursor <= 0.8f)
                    {
                        OnSlotClicked(nearestSlot);
                    }
                }
            }
        }
    }

    TowerSlot FindNearestSlotInDirection(Vector2 direction)
    {
        if (playerTransform == null) return null;

        TowerSlot nearestSlot = null;
        float nearestDistance = float.MaxValue;

        foreach (TowerSlot slot in allSlots)
        {
            if (slot == null || !slot.IsAvailable) continue;

            Vector2 directionToSlot = (slot.transform.position - playerTransform.position).normalized;
            float distance = Vector2.Distance(playerTransform.position, slot.transform.position);

            if (distance > buildRange) continue;

            float dotProduct = Vector2.Dot(direction, directionToSlot);
            if (dotProduct > -0.5f && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSlot = slot;
            }
        }

        return nearestSlot;
    }

    // ── Phase 4: per-player placement API (shared service) ───────────────
    private readonly HashSet<PlayerTowerPlacer> _activePlacers = new HashSet<PlayerTowerPlacer>();

    /// A PlayerTowerPlacer reports entering/leaving placement mode. isPlacementMode
    /// (used to gate supply + consumer highlight) is true while ANY player builds.
    public void NotifyPlacementMode(PlayerTowerPlacer placer, bool active)
    {
        if (placer == null) return;
        if (active) _activePlacers.Add(placer); else _activePlacers.Remove(placer);
        isPlacementMode = _activePlacers.Count > 0;
    }

    /// Configured tower prefabs as an array (for a player's selection wheel).
    public GameObject[] GetTowerPrefabsArray()
    {
        if (towerPrefabs == null || towerPrefabs.Count == 0) BuildTowerList();
        return towerPrefabs != null ? towerPrefabs.ToArray() : new GameObject[0];
    }

    /// True if `byPlayer` may build in `slot` (available + within build range).
    public bool CanBuildAt(TowerSlot slot, Transform byPlayer)
    {
        if (slot == null || !slot.IsAvailable) return false;
        if (requirePlayerProximity && byPlayer != null &&
            Vector2.Distance(byPlayer.position, slot.transform.position) > buildRange) return false;
        return true;
    }

    /// Build `towerIndex` into `slot` on behalf of `byPlayer` (proximity + energy
    /// checked; spends from the shared wallet). Returns true on success.
    public bool BuildAt(TowerSlot slot, int towerIndex, Transform byPlayer)
    {
        if (!CanBuildAt(slot, byPlayer)) return false;
        if (towerPrefabs == null || towerIndex < 0 || towerIndex >= towerPrefabs.Count) return false;
        PlaceTowerDirectly(slot, towerIndex);
        return true;
    }

    public void RegisterSlot(TowerSlot slot)
    {
        if (allSlots == null) allSlots = new List<TowerSlot>();
        if (slot != null && !allSlots.Contains(slot))
        {
            allSlots.Add(slot);
        }
    }

    public void UnregisterSlot(TowerSlot slot)
    {
        if (allSlots != null)
        {
            allSlots.Remove(slot);
        }
    }

    // ── Restore support (used by RunPersistence to rebuild saved towers) ──

    /// Find a registered slot by its (ring, slot) identity. Null if none match.
    public TowerSlot FindSlot(int ringIndex, int slotIndex)
    {
        if (allSlots == null) return null;
        foreach (var s in allSlots)
            if (s != null && s.ringIndex == ringIndex && s.slotIndex == slotIndex)
                return s;
        return null;
    }

    /// Find the configured tower prefab whose Tower.towerType matches `type`.
    /// Returns null if no prefab of that type is wired up.
    public GameObject FindPrefabForType(Tower.TowerType type)
    {
        if (towerPrefabs == null || towerPrefabs.Count == 0) BuildTowerList();
        if (towerPrefabs != null)
        {
            foreach (var prefab in towerPrefabs)
            {
                if (prefab == null) continue;
                var t = prefab.GetComponent<Tower>();
                if (t != null && t.towerType == type) return prefab;
            }
        }
        return null;
    }

    /// Rebuild a tower into a slot for a RESTORE — bypasses energy cost and the
    /// build animation, then applies tower augments. Returns the new Tower or null.
    public Tower RestoreTowerInto(TowerSlot slot, Tower.TowerType type, int upgradeLevel, float currentEnergy)
    {
        if (slot == null) return null;
        var prefab = FindPrefabForType(type);
        if (prefab == null)
        {
            Debug.LogWarning($"[TowerPlacement] No prefab for tower type {type}; cannot restore.");
            return null;
        }
        return slot.PlaceTowerForRestore(prefab, upgradeLevel, currentEnergy);
    }

    public void TogglePlacementMode()
    {
        isPlacementMode = !isPlacementMode;
        PlayerAttack.InputSuppressed = isPlacementMode;
        if (CursorManager.Instance != null)
        {
            if (isPlacementMode)
            {
                CursorManager.Instance.SetCursor(CursorManager.CursorType.Repair);
            }
            else
            {
                CursorManager.Instance.ReturnToPreviousCursor();
            }
        }

        if (towerSelectionUI != null)
        {
            towerSelectionUI.SetActive(isPlacementMode);
        }

        // Clear highlights when exiting placement mode
        if (!isPlacementMode)
        {
            // Stop continuous supply and visual effects when exiting placement mode
            StopContinuousSupply();

            // Also stop the EnergyManager's supply system
            if (EnergyManager.Instance != null)
            {
                EnergyManager.Instance.StopSupplying();
            }

            if (currentHighlightedSlot != null)
            {
                currentHighlightedSlot.SetHighlight(false);
                currentHighlightedSlot = null;
            }
            if (currentHighlightedConsumer != null)
            {
                SetConsumerHighlight(currentHighlightedConsumer, false);
                currentHighlightedConsumer = null;
            }
            if (allSlots != null)
            {
                foreach (TowerSlot slot in allSlots)
                {
                    slot?.SetHighlight(false);
                }
            }

            // Hide wheel if visible
            if (selectionWheel != null)
            {
                selectionWheel.CloseWheel();
            }

            //Debug.Log("[PLACEMENT] Exited placement mode, stopped all supply operations");
        }
        else
        {
            //Debug.Log("[PLACEMENT] Entered placement mode");
        }
    }

    public void OnSlotClicked(TowerSlot slot)
    {
        if (!isPlacementMode || !slot.IsAvailable) return;
        if (towerPrefabs == null || towerPrefabs.Count == 0) return;

        if (requirePlayerProximity && !IsPlayerInRange(slot.transform.position)) return;

        // Per-player wheel lives on PlayerTowerPlacer; this legacy path places
        // the currently-selected tower directly.
        if (towerPrefabs.Count > 0)
        {
            PlaceTowerDirectly(slot, selectedTowerIndex);
        }
    }

    // Called by the wheel when a tower is selected
    public void PlaceTowerFromWheel(int towerIndex, GameObject towerPrefab, TowerSlot slot)
    {
        PlaceTowerDirectly(slot, towerIndex);
    }

    // Helper method to place tower directly
    private void PlaceTowerDirectly(TowerSlot slot, int towerIndex)
    {
        if (towerPrefabs == null || towerIndex < 0 || towerIndex >= towerPrefabs.Count) return;

        bool success = slot.PlaceTower(towerPrefabs[towerIndex]);

        if (success)
        {
            if (currentHighlightedSlot == slot)
            {
                currentHighlightedSlot.SetHighlight(false);
                currentHighlightedSlot = null;
            }

            if (playCreationAnimation && slot.currentTower != null)
            {
                StartCoroutine(PlayTowerCreationAnimation(slot.currentTower));
            }
        }
    }

    IEnumerator PlayTowerCreationAnimation(GameObject tower)
    {
        Sprite[] creationSprites = Resources.LoadAll<Sprite>(towerCreationSpritePath);
        if (creationSprites == null || creationSprites.Length == 0) yield break;

        SpriteRenderer towerRenderer = tower.GetComponent<SpriteRenderer>();
        if (towerRenderer == null) yield break;

        Sprite originalSprite = towerRenderer.sprite;

        for (int i = creationSprites.Length - 1; i >= 0; i--)
        {
            if (towerRenderer != null && creationSprites[i] != null)
            {
                towerRenderer.sprite = creationSprites[i];
                yield return new WaitForSeconds(creationAnimationSpeed);
            }
        }

        if (towerRenderer != null && originalSprite != null)
        {
            towerRenderer.sprite = originalSprite;
        }
    }

    void RemoveTowerAtMousePosition()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouseWorldPos.z = 0f;

        if (allSlots != null)
        {
            foreach (TowerSlot slot in allSlots)
            {
                if (slot != null && slot.IsClickedAt(mouseWorldPos) && slot.IsOccupied)
                {
                    if (requirePlayerProximity && !IsPlayerInRange(slot.transform.position)) continue;

                    slot.RemoveTower();
                    break;
                }
            }
        }
    }

    public void SelectTower(int index)
    {
        if (towerPrefabs != null && index >= 0 && index < towerPrefabs.Count)
        {
            selectedTowerIndex = index;
        }
    }

    public void AddNewRing(float radius, int slotCount)
    {
        if (mapGenerator != null)
        {
            mapGenerator.AddRing(radius, slotCount);
            mapGenerator.GenerateMap();
        }
    }

    public List<TowerSlot> GetAllSlots() => allSlots != null ? new List<TowerSlot>(allSlots) : new List<TowerSlot>();

    public List<TowerSlot> GetAvailableSlots()
    {
        List<TowerSlot> availableSlots = new List<TowerSlot>();
        if (allSlots != null)
        {
            foreach (TowerSlot slot in allSlots)
            {
                if (slot != null && slot.IsAvailable)
                {
                    availableSlots.Add(slot);
                }
            }
        }
        return availableSlots;
    }

    // Utility method to get tower count
    public int GetTowerTypeCount()
    {
        return towerPrefabs != null ? towerPrefabs.Count : 0;
    }

    // Debug method to rebuild tower list manually if needed
    [ContextMenu("Rebuild Tower List")]
    public void RebuildTowerList()
    {
        BuildTowerList();
        //Debug.Log($"Tower list rebuilt with {GetTowerTypeCount()} towers");
    }

    void OnDestroy()
    {
        if (isRepairSoundInitialized)
        {
            repairSound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            repairSound.release();
        }
    }

    void OnDrawGizmos()
    {
        if (requirePlayerProximity && playerTransform != null)
        {
            Gizmos.color = isPlacementMode ? Color.green : Color.gray;
            Gizmos.DrawWireSphere(playerTransform.position, buildRange);
        }
    }
}

