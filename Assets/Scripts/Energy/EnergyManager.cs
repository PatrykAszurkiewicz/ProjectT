using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class EnergyManager : MonoBehaviour
{
    #region Singleton Management
    private static bool isApplicationQuitting = false;

    public System.Action<GameObject> OnEnemyKilledEvent;


    private static EnergyManager instance;
    private bool isGameOver = false;

    public bool IsGameOver() => isGameOver;
    private HashSet<IEnergyConsumer> destroyedConsumers = new HashSet<IEnergyConsumer>();

    public static EnergyManager Instance
    {
        get
        {
            if (isApplicationQuitting) return null;

            if (instance == null)
                instance = FindFirstObjectByType<EnergyManager>();

            // Deliberately does NOT lazily spawn a new EnergyManager. The manager
            // always lives in the gameplay scene. Auto-creating one here meant a
            // consumer touching EnergyManager.Instance from its OnDestroy DURING a
            // scene reload would spawn a doomed "EnergyManager" GameObject into the
            // closing scene ("Some objects were not cleaned up… EnergyManager"),
            // which then fought the real instance on the next scene and
            // black-screened the run. Returning null here is safe: every call site
            // already null-checks via EnergyManager.Instance?.
            return instance;
        }
    }
    #endregion

    private float accumulatedPlayerEnergyCost = 0f;


    [Header("Resource Generation")]
    public float globalResourceMultiplier = 1.0f;
    public float bonusResourceDropChance = 0.0f;
    public float bonusResourceMultiplier = 2.0f;

    #region Configuration
    [Header("Global Energy Settings")]
    public float globalEnergyDecayRate = 1f;
    public float supplyRange = 2.2f;
    public float supplyRate = 10f;
    //public float maxSupplyDistance = 0.9f;
    public float maxSupplyDistance = 2.2f;
    [Header("Repair System Settings")]
    public int repairEnergyPerClick = 10;
    public int repairCostPerClick = 10;
    public float repairCooldown = 0.5f;
    public bool onlyAllowRepairInPlacementMode = true;

    [Header("Continuous Supply Settings")]
    public float continuousSupplyRate = 10f; // Energy per second when holding button
    public float continuousSupplyCost = 10f; // Player energy cost per second
    public float minSupplyInterval = 0.02f; // Minimum time between supply ticks (2fps)

    [Header("Tower Energy Settings")]
    public float towerMaxEnergy = 100f;
    public float towerEnergyDecayRate = 0.7f;
    public float towerCriticalEnergyThreshold = 0.2f;
    public float towerDeadEnergyThreshold = 0.0f;

    [Header("Central Core Energy Settings")]
    public float coreMaxEnergy = 100f;
    public float coreEnergyDecayRate = 0.7f;
    public float coreCriticalEnergyThreshold = 0.3f;
    public float coreDeadEnergyThreshold = 0.0f;

    [Header("Player Currency Settings")]
    public int playerStartingEnergy = 300;
    public int towerBuildCost = 100;
    public float towerSellRefundPercentage = 0.1f;
    public bool enableCurrencyEarnedFromEnemyKills = false;
    public int energyPerEnemyKill = 0;

    [Header("Player Currency UI")]
    public TMPro.TextMeshProUGUI playerEnergyText;
    public string energyTextFormat = "Energy: {0}";

    [Header("Enemy Damage Settings")]
    public float defaultEnemyDamage = 10f;
    public float enemyDamageToTowers = 10f;
    public float enemyDamageToCore = 15f;
    public bool enableEnemyDamageEffects = true;

    [Header("Visual Colors")]
    public Color normalColor = Color.lightSteelBlue;
    public Color lowEnergyColor = Color.yellow;
    public Color criticalEnergyColor = Color.red;
    public Color depletedEnergyColor = Color.gray;
    public Color damageFlashColor = Color.red;

    [Header("Supply Beam - Enhanced")]
    public Color supplyBeamColor = new Color(0.2f, 0.7f, 1f, 0.35f);
    public Color repairBeamColor = new Color(0.3f, 0.9f, 1f, 0.35f);
    public Color beamGlowColor = new Color(0.6f, 0.8f, 1f, 0.35f);
    public float supplyBeamWidth = 0.3f;
    public LayerMask supplyTargetMask = -1;

    [Header("Beam Effects")]
    public float beamPulseSpeed = 4f;
    public float beamPulseIntensity = 0.6f;
    public float beamFlowSpeed = 8f;
    public bool enableBeamGlow = true;
    public AnimationCurve beamPulseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Beam Y-Sort (must match GrassCartoonOverlay / PlayerMovement)")]
    [Tooltip("Must match GrassCartoonOverlay.sortPrecision")]
    public float beamSortPrecision = 10f;
    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase")]
    public int beamSortOrderBase = 1000;
    [Tooltip("Y offset added to the beam's sort anchor. Negative = sort from lower (feet).")]
    public float beamSortYOffset = -0.3f;
    [Tooltip("Bonus added to beam sortingOrder so it draws ON TOP of foreground sprites and grass at the same Y. " +
             "Set to 0 to draw behind, positive to draw in front.")]
    public int beamSortBias = 5;
    #endregion

    #region Core Components
    [System.NonSerialized]

    private List<IEnergyConsumer> energyConsumers = new List<IEnergyConsumer>();
    private GameObject player;
    private Camera mainCamera;

    // Supply system
    private SupplyBeamController supplyBeam;
    private IEnergyConsumer currentSupplyTarget;
    private bool isSupplying;
    private float lastRepairTime = 0f;

    // Continuous supply system
    public bool isContinuouslySupplying = false;
    private IEnergyConsumer continuousSupplyTarget = null;
    private float lastContinuousSupplyTime = 0f;

    private int currentPlayerEnergy;

    // Events
    public System.Action<float> OnGlobalEnergyChanged;
    public System.Action OnGameOver;
    public System.Action<IEnergyConsumer, float> OnEnergyConsumerDamaged;
    public System.Action<IEnergyConsumer> OnEnergyConsumerDestroyed;

    public System.Action<int> OnPlayerEnergyChanged;
    public System.Action<int> OnPlayerEnergySpent;
    public System.Action<int> OnPlayerEnergyGained;
    public System.Action OnInsufficientPlayerEnergy;
    #endregion

    #region Unity Lifecycle
    void Awake() => InitializeSingleton();
    void Start() => InitializeManager();
    void Update() => UpdateManager();
    void OnDestroy() => CleanupManager();
    void OnApplicationQuit() => HandleApplicationQuit();
    void OnDisable() => CleanupEnergyManager();
    #endregion

    #region Initialization
    void InitializeSingleton()
    {
        if (instance == null)
        {
            instance = this;
            // Initialize supply beam with error handling
            try
            {
                supplyBeam = new SupplyBeamController(this);
                //Debug.Log("[EnergyManager] Supply beam initialized successfully in singleton setup");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EnergyManager] Failed to initialize supply beam in singleton: {e.Message}");
                supplyBeam = null;
            }
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    void InitializeManager()
    {
        FindReferences();
        InitializePlayerEnergy();
        InitializeAnimationCurve();
        StartEnergyDecay();
    }

    void InitializeAnimationCurve()
    {
        if (beamPulseCurve == null || beamPulseCurve.keys.Length == 0)
        {
            beamPulseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    void FindReferences()
    {
        player = GameObject.FindGameObjectWithTag("Player");
        mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();

        if (playerEnergyText == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                var energyObject = canvas.transform.Find("Energy");
                if (energyObject != null)
                {
                    playerEnergyText = energyObject.GetComponent<TextMeshProUGUI>();
                }
            }
        }
    }

    void InitializePlayerEnergy()
    {
        currentPlayerEnergy = playerStartingEnergy;
        UpdatePlayerEnergyUI();
    }

    void StartEnergyDecay() => StartCoroutine(EnergyDecayCoroutine());
    #endregion

    #region Update Logic
    void UpdateManager()
    {
        HandleSupplyInput();

        // Legacy single-supplier energy transfer. The per-player supply path
        // (PlayerTowerPlacer) does NOT use this — it drives SupplyTickForPlayer with
        // its own accumulator and its own beam. This block only runs for the legacy
        // singleton supply, reachable when onlyAllowRepairInPlacementMode == false
        // (free-supply mode). With the default (true) plus per-player supply,
        // isSupplying stays false here, so this is a no-op and there is no double
        // supply. (Previously this transfer was a side-effect inside the beam's
        // Update; it now lives here so the beam can be a pure, reusable visual.)
        if (isSupplying && currentSupplyTarget != null)
        {
            bool inPlacement = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
            if (inPlacement) SupplyEnergyToTarget(currentSupplyTarget, 0f);
            else SupplyEnergyToTarget(currentSupplyTarget, supplyRate * Time.deltaTime);
        }

        // Co-op: the singleton beam must originate from whoever is ACTUALLY supplying,
        // not the globally-tagged player. Resolve to the alive player nearest the
        // target. Single player resolves back to the tagged player (unchanged).
        GameObject supplier = isSupplying ? ResolveSupplyingPlayer(currentSupplyTarget) : player;
        supplyBeam?.Update(isSupplying, currentSupplyTarget, supplier);
    }

    // The player that should own the supply beam for `target`: the nearest alive
    // player to the target. Mirrors the PlayerRegistry.NearestAlive convention used
    // by TowerSlot. Falls back to the tagged player in single player / empty registry.
    private GameObject ResolveSupplyingPlayer(IEnergyConsumer target)
    {
        if (target != null && PlayerRegistry.Count > 0 && PlayerRegistry.Instance != null)
        {
            var near = PlayerRegistry.Instance.NearestAlive(target.GetPosition(), Mathf.Infinity, true);
            if (near != null) return near.gameObject;
        }
        return player;
    }
    void HandleSupplyInput()
    {
        if (player == null) return;

        // Check if we're in placement mode
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        // Placement-mode supply is owned ENTIRELY by the per-player path
        // (PlayerTowerPlacer): each player supplies via their own Build action, aim
        // and beam. The legacy singleton must never engage in placement mode, so it
        // can't double-supply or draw a second beam. This holds regardless of
        // onlyAllowRepairInPlacementMode.
        if (inPlacementMode)
        {
            StopSupplying();
            return;
        }

        if (onlyAllowRepairInPlacementMode)
        {
            // NOT in placement mode AND onlyAllowRepairInPlacementMode is true -> NO supply should happen
            StopSupplying();
            return;
        }

        // Check for input
        bool hasInput = (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
                       (Keyboard.current != null && Keyboard.current.spaceKey.isPressed);

        if (hasInput)
        {
            Vector3 inputPosition = GetInputPosition();
            IEnergyConsumer target = GetSupplyTarget(inputPosition);

            if (target != null && IsPlayerInRange(target))
            {
                StartSupplying(target);
            }
            else
            {
                StopSupplying();
            }
        }
        else
        {
            StopSupplying();
        }
    }
    Vector3 GetInputPosition()
    {
        // Check if we're in placement mode - use sprite cursor position
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        if (inPlacementMode)
        {
            // Use sprite cursor position when in placement mode
            CursorPointer cursorPointer = FindFirstObjectByType<CursorPointer>();
            if (cursorPointer != null)
            {
                return cursorPointer.transform.position;
            }
        }

        // Check mouse input (fallback or when not in placement mode)
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mousePos.z = 0;
            return mousePos;
        }

        // Check keyboard input
        if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
        {
            var closest = GetClosestEnergyConsumer(player.transform.position);
            return closest?.GetPosition() ?? Vector3.zero;
        }

        return Vector3.zero;
    }
    #endregion

    #region Player Currency/Energy Management
    public int GetPlayerEnergy() => currentPlayerEnergy;
    public bool CanPlayerAfford(int amount) => currentPlayerEnergy >= amount;
    public bool CanAffordTower() => CanPlayerAfford(towerBuildCost);
    public int GetTowerBuildCost() => towerBuildCost;
    public int GetTowerSellValue() => Mathf.RoundToInt(towerBuildCost * towerSellRefundPercentage);

    public bool TrySpendPlayerEnergy(int amount)
    {
        if (currentPlayerEnergy >= amount)
        {
            currentPlayerEnergy -= amount;
            OnPlayerEnergySpent?.Invoke(amount);
            OnPlayerEnergyChanged?.Invoke(currentPlayerEnergy);
            UpdatePlayerEnergyUI();
            return true;
        }
        else
        {
            OnInsufficientPlayerEnergy?.Invoke();
            return false;
        }
    }

    public bool TryBuyTower()
    {
        return TrySpendPlayerEnergy(towerBuildCost);
    }

    public void GivePlayerEnergy(int amount)
    {
        if (amount <= 0) return;

        // Augments 334 (Last Stand) & 348 (Energy Attunement) — global multiplier
        // on all energy the player receives. Defaults to 1.0 (no change).
        amount = Mathf.RoundToInt(amount * PlayerEconomyModifiers.EnergyGainMultiplier);
        if (amount <= 0) return;

        currentPlayerEnergy += amount;
        OnPlayerEnergyGained?.Invoke(amount);
        OnPlayerEnergyChanged?.Invoke(currentPlayerEnergy);
        UpdatePlayerEnergyUI();
    }

    public void SetPlayerEnergy(int amount)
    {
        currentPlayerEnergy = Mathf.Max(0, amount);
        OnPlayerEnergyChanged?.Invoke(currentPlayerEnergy);
        UpdatePlayerEnergyUI();
    }

    public void OnEnemyKilled(GameObject enemy)
    {
        if (enableCurrencyEarnedFromEnemyKills)
        {
            int reward = Mathf.RoundToInt(energyPerEnemyKill * globalResourceMultiplier);
            GivePlayerEnergy(reward);
        }

        // Fire event for other systems (like Energy Scavenging augment)
        OnEnemyKilledEvent?.Invoke(enemy);
    }

    void UpdatePlayerEnergyUI()
    {
        if (playerEnergyText != null)
        {
            playerEnergyText.text = string.Format(energyTextFormat, currentPlayerEnergy);
        }
    }

    public void SetPlayerEnergyText(TextMeshProUGUI textComponent)
    {
        playerEnergyText = textComponent;
        UpdatePlayerEnergyUI();
    }
    #endregion

    #region Repair System
    public int CalculateRepairCost(IEnergyConsumer target)
    {
        if (target == null || target.GetEnergyPercentage() >= 1f) return 0;
        return repairCostPerClick;
    }

    public int CalculateFullRepairCost(IEnergyConsumer target)
    {
        if (target == null) return 0;

        float energyNeeded = target.GetMaxEnergy() - target.GetEnergy();
        int repairClicks = Mathf.CeilToInt(energyNeeded / repairEnergyPerClick);
        return repairClicks * repairCostPerClick;
    }

    public bool CanAffordRepair(IEnergyConsumer target)
    {
        int cost = CalculateRepairCost(target);
        return CanPlayerAfford(cost);
    }

    public bool CanAffordFullRepair(IEnergyConsumer target)
    {
        int cost = CalculateFullRepairCost(target);
        return CanPlayerAfford(cost);
    }

    public bool TryRepairTarget(IEnergyConsumer target)
    {
        if (target == null) return false;

        // Check cooldown
        if (Time.time - lastRepairTime < repairCooldown)
        {
            return false;
        }
        // Check if target needs energy
        if (target.GetEnergyPercentage() >= 1f)
        {
            return false;
        }
        // Check if player can afford the repair
        if (!CanAffordRepair(target))
        {
            OnInsufficientPlayerEnergy?.Invoke();
            return false;
        }
        // Calculate actual energy to give (don't exceed max)
        float energyToGive = Mathf.Min(repairEnergyPerClick, target.GetMaxEnergy() - target.GetEnergy());

        if (energyToGive <= 0) return false;
        // Spend player energy
        if (TrySpendPlayerEnergy(repairCostPerClick))
        {
            // Supply energy to target
            target.SupplyEnergy(energyToGive);
            lastRepairTime = Time.time;
            return true;
        }

        return false;
    }
    #endregion

    #region Continuous Supply System
    public void StartContinuousSupply(IEnergyConsumer target)
    {
        if (target != continuousSupplyTarget)
        {
            lastContinuousSupplyTime = Time.time - minSupplyInterval; // Allow immediate first supply
            continuousSupplyTarget = target;
        }
    }

    private void ProcessContinuousSupply(IEnergyConsumer target)
    {
        if (target == null) return;
        // Block supply to destroyed Central Core
        if (target is CentralCore core && core.IsDestroyed())
        {
            return;
        }
        // Check if enough time has passed since last supply
        if (Time.time - lastContinuousSupplyTime < minSupplyInterval)
        {
            return;
        }
        // Check if target needs energy
        if (target.GetEnergyPercentage() >= 1f && !(target is CentralCore))
        {
            return;
        }
        // Calculate energy amounts for this tick
        float deltaTime = Time.time - lastContinuousSupplyTime;

        // Cap deltaTime to prevent huge jumps
        deltaTime = Mathf.Min(deltaTime, minSupplyInterval * 2f); // Max 0.2 seconds

        float energyToGive = continuousSupplyRate * deltaTime;
        float playerEnergyCostThisFrame = continuousSupplyCost * deltaTime;

        // Accumulate the fractional cost
        accumulatedPlayerEnergyCost += playerEnergyCostThisFrame;

        // Only spend whole units of energy
        int energyToSpend = Mathf.FloorToInt(accumulatedPlayerEnergyCost);

        // Check if player can afford the cost
        if (energyToSpend > 0 && currentPlayerEnergy < energyToSpend)
        {
            OnInsufficientPlayerEnergy?.Invoke();
            return;
        }

        // Limit energy to not exceed target's max
        energyToGive = Mathf.Min(energyToGive, target.GetMaxEnergy() - target.GetEnergy());

        if (energyToGive <= 0) return;

        // Spend accumulated player energy if we have enough
        if (energyToSpend > 0)
        {
            if (TrySpendPlayerEnergy(energyToSpend))
            {
                // Subtract the spent amount from accumulated cost
                accumulatedPlayerEnergyCost -= energyToSpend;

                // Supply energy to target
                target.SupplyEnergy(energyToGive);
                lastContinuousSupplyTime = Time.time;

                // Update continuous supply state
                isContinuouslySupplying = true;
                continuousSupplyTarget = target;
            }
        }
        else
        {
            // Smooth energy transfer while accumulating cost
            target.SupplyEnergy(energyToGive);
            lastContinuousSupplyTime = Time.time;

            // Update continuous supply state
            isContinuouslySupplying = true;
            continuousSupplyTarget = target;
        }
    }
    #endregion

    #region Enemy Damage System
    public bool DamageEnergyConsumer(IEnergyConsumer consumer, float damage, GameObject damageSource = null)
    {
        if (consumer == null || damage <= 0) return false;

        if (destroyedConsumers.Contains(consumer)) return false;

        if (consumer is IDamageable damageable)
        {
            return damageable.TakeDamage(damage, damageSource);
        }

        consumer.ConsumeEnergy(damage);

        if (enableEnemyDamageEffects)
        {
            StartCoroutine(DamageFlashEffect(consumer));
        }

        OnEnergyConsumerDamaged?.Invoke(consumer, damage);

        if (consumer.IsEnergyDepleted() && !destroyedConsumers.Contains(consumer))
        {
            HandleEnergyConsumerDestroyed(consumer);
        }

        return true;
    }

    public bool DamageTower(Tower tower, GameObject damageSource = null)
    {
        return DamageEnergyConsumer(tower, enemyDamageToTowers, damageSource);
    }

    public bool DamageCore(CentralCore core, GameObject damageSource = null)
    {
        return DamageEnergyConsumer(core, enemyDamageToCore, damageSource);
    }

    public bool DamageNearestConsumer(Vector3 position, float damage, float maxRange = 2f, GameObject damageSource = null)
    {
        IEnergyConsumer nearest = GetNearestEnergyConsumer(position, maxRange);
        if (nearest != null)
        {
            return DamageEnergyConsumer(nearest, damage, damageSource);
        }
        return false;
    }

    public IEnergyConsumer GetNearestEnergyConsumer(Vector3 position, float maxRange = float.MaxValue)
    {
        IEnergyConsumer nearest = null;
        float nearestDistance = maxRange;

        foreach (var consumer in energyConsumers)
        {
            if (consumer == null) continue;

            float distance = Vector3.Distance(position, consumer.GetPosition());
            if (distance < nearestDistance)
            {
                nearest = consumer;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    public List<IEnergyConsumer> GetEnergyConsumersInRange(Vector3 position, float range)
    {
        List<IEnergyConsumer> consumersInRange = new List<IEnergyConsumer>();

        foreach (var consumer in energyConsumers)
        {
            if (consumer == null) continue;

            float distance = Vector3.Distance(position, consumer.GetPosition());
            if (distance <= range)
            {
                consumersInRange.Add(consumer);
            }
        }

        return consumersInRange;
    }

    private void HandleEnergyConsumerDestroyed(IEnergyConsumer consumer)
    {
        if (destroyedConsumers.Contains(consumer)) return;

        destroyedConsumers.Add(consumer);
        OnEnergyConsumerDestroyed?.Invoke(consumer);

        if (consumer is CentralCore)
        {
            TriggerGameOver();
        }
    }

    private string GetConsumerName(IEnergyConsumer consumer)
    {
        if (consumer is Tower tower)
            return $"Tower ({tower.towerName})";
        else if (consumer is CentralCore)
            return "Central Core";
        else
            return "Unknown Consumer";
    }



    private IEnumerator DamageFlashEffect(IEnergyConsumer consumer)
    {
        SpriteRenderer spriteRenderer = null;

        if (consumer is MonoBehaviour mb)
        {
            spriteRenderer = mb.GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer != null)
        {
            Color originalColor = spriteRenderer.color;

            spriteRenderer.color = damageFlashColor;
            yield return new WaitForSeconds(0.1f);

            spriteRenderer.color = originalColor;
        }
    }
    #endregion

    #region Supply System
    private void EnsureSupplyBeamInitialized()
    {
        if (supplyBeam == null)
        {
            //Debug.LogWarning("[EnergyManager] Supply beam was null, reinitializing...");
            try
            {
                supplyBeam = new SupplyBeamController(this);
                //Debug.Log("[EnergyManager] Supply beam reinitialized successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EnergyManager] Failed to initialize supply beam: {e.Message}");
            }
        }
    }

    void StartSupplying(IEnergyConsumer target)
    {
        //Debug.Log($"[EnergyManager] StartSupplying called for {GetConsumerName(target)}");

        // Ensure supplyBeam is initialized before use
        EnsureSupplyBeamInitialized();

        //Debug.Log($"[EnergyManager] supplyBeam null check: {(supplyBeam == null ? "NULL" : "OK")}");

        currentSupplyTarget = target;
        isSupplying = true;

        if (supplyBeam != null)
        {
            supplyBeam.SetEnabled(true);
            //Debug.Log("[EnergyManager] Supply beam enabled successfully");
        }
        else
        {
            Debug.LogError("[EnergyManager] Supply beam is STILL NULL after reinitialization! Visual effects will not work!");
        }
    }

    public void ForceStopSupply()
    {
        if (isSupplying)
        {
            //Debug.Log("[EnergyManager] ForceStopSupply called - stopping supply operations");
            StopSupplying();
        }
    }

    public void StopSupplying()
    {
        if (isSupplying)
        {
            //Debug.Log("[ENERGY] Manually stopping supply operations");
            currentSupplyTarget = null;
            isSupplying = false;
            supplyBeam?.SetEnabled(false);

            // Reset continuous supply state
            isContinuouslySupplying = false;
            continuousSupplyTarget = null;
            accumulatedPlayerEnergyCost = 0f;
        }
    }

    IEnergyConsumer GetSupplyTarget(Vector3 position)
    {
        IEnergyConsumer closest = null;
        float closestDistance = maxSupplyDistance;

        foreach (var consumer in energyConsumers)
        {
            if (consumer == null) continue;

            // Skip destroyed Central Core only
            if (consumer is CentralCore core && core.IsDestroyed())
            {
                continue;
            }

            float distance = Vector3.Distance(position, consumer.GetPosition());
            if (distance < closestDistance)
            {
                closest = consumer;
                closestDistance = distance;
            }
        }

        return closest;
    }

    IEnergyConsumer GetSupplyTargetOLD(Vector3 position)
    {
        IEnergyConsumer closest = null;
        float closestDistance = maxSupplyDistance;

        foreach (var consumer in energyConsumers)
        {
            if (consumer == null) continue;

            float distance = Vector3.Distance(position, consumer.GetPosition());
            if (distance < closestDistance)
            {
                closest = consumer;
                closestDistance = distance;
            }
        }

        return closest;
    }

    IEnergyConsumer GetClosestEnergyConsumer(Vector3 position)
    {
        IEnergyConsumer closest = null;
        float closestDistance = float.MaxValue;

        foreach (var consumer in energyConsumers)
        {
            if (consumer == null) continue;

            float distance = Vector3.Distance(position, consumer.GetPosition());
            if (distance < closestDistance)
            {
                closest = consumer;
                closestDistance = distance;
            }
        }

        return closest;
    }

    bool IsPlayerInRange(IEnergyConsumer target)
    {
        if (player == null || target == null) return false;
        float distance = Vector3.Distance(player.transform.position, target.GetPosition());
        return distance <= supplyRange;
    }

    public void SupplyEnergyToTarget(IEnergyConsumer target, float amount)
    {
        if (target == null) return;
        // Block supply to destroyed Central Core
        if (target is CentralCore core && core.IsDestroyed())
        {
            return;
        }
        // Check if we're in placement mode (repair mode)
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        if (inPlacementMode)
        {
            EnsureSupplyBeamInitialized();
            if (!isSupplying || currentSupplyTarget != target)
            {
                currentSupplyTarget = target;
                isSupplying = true;

                if (supplyBeam != null)
                {
                    supplyBeam.SetEnabled(true);
                    //Debug.Log($"[ENERGY] Started supplying energy to {GetConsumerName(target)} with visual effects");
                }
                else
                {
                    Debug.LogError($"[ENERGY] Cannot start visual effects - supply beam is null for {GetConsumerName(target)}");
                }
            }

            // Use continuous supply for smooth energy transfer
            ProcessContinuousSupply(target);
        }
        else
        {
            if (onlyAllowRepairInPlacementMode)
            {
                return;
            }

            // Original free energy behavior for backwards compatibility
            target.SupplyEnergy(amount);
        }
    }

    // ----- Per-player supply API (co-op) ------------------------------------------
    // These let each player run its OWN supply session concurrently without the
    // single-supplier state (currentSupplyTarget / accumulatedPlayerEnergyCost) that
    // the legacy mouse path relies on. The wallet stays shared.

    /// <summary>Nearest suppliable consumer to an aim point (within maxSupplyDistance),
    /// skipping a destroyed core. Public wrapper over the internal targeting.</summary>
    public IEnergyConsumer FindSupplyTarget(Vector3 aimPosition) => GetSupplyTarget(aimPosition);

    /// <summary>Is <paramref name="target"/> within supplyRange of an arbitrary world
    /// position (a specific player), rather than the single tagged player?</summary>
    public bool IsWithinSupplyRange(Vector3 fromPosition, IEnergyConsumer target)
    {
        if (target == null) return false;
        return Vector3.Distance(fromPosition, target.GetPosition()) <= supplyRange;
    }

    /// <summary>
    /// One frame of supply from a single player to a single target, using a
    /// PER-PLAYER cost accumulator (passed by ref) so two players supplying at once
    /// don't share state. Spends from the shared wallet; matches the rate/cost of the
    /// legacy ProcessContinuousSupply (continuousSupplyRate / continuousSupplyCost).
    /// </summary>
    public void SupplyTickForPlayer(IEnergyConsumer target, ref float accumulatedCost)
    {
        if (target == null) return;
        if (target is CentralCore core && core.IsDestroyed()) return;

        // Full towers don't accept more (the core always does).
        if (target.GetEnergyPercentage() >= 1f && !(target is CentralCore)) return;

        float dt = Mathf.Min(Time.deltaTime, minSupplyInterval * 2f);
        if (dt <= 0f) return;

        float energyToGive = continuousSupplyRate * dt;
        accumulatedCost += continuousSupplyCost * dt;
        int toSpend = Mathf.FloorToInt(accumulatedCost);

        // Don't overshoot the target's max (and don't charge for energy that wouldn't land).
        energyToGive = Mathf.Min(energyToGive, target.GetMaxEnergy() - target.GetEnergy());
        if (energyToGive <= 0f) return;

        if (toSpend > 0)
        {
            if (currentPlayerEnergy < toSpend) { OnInsufficientPlayerEnergy?.Invoke(); return; }
            if (TrySpendPlayerEnergy(toSpend))
            {
                accumulatedCost -= toSpend;
                target.SupplyEnergy(energyToGive);
            }
        }
        else
        {
            // Sub-integer cost still accruing — transfer the smooth amount for free this frame.
            target.SupplyEnergy(energyToGive);
        }
    }

    /// <summary>Create a per-player supply beam owned by the caller (visual only).</summary>
    public SupplyBeamController CreateSupplyBeam()
    {
        EnsureSupplyBeamInitialized();
        return new SupplyBeamController(this);
    }

    // "Is ANY player currently supplying?" — drives the shared repair sound now that
    // supply is per-player. Suppliers register/unregister themselves here.
    private readonly HashSet<object> _activeSuppliers = new HashSet<object>();
    public bool AnyoneSupplying => _activeSuppliers.Count > 0;
    public void SetSupplierActive(object supplier, bool active)
    {
        if (supplier == null) return;
        if (active) _activeSuppliers.Add(supplier);
        else _activeSuppliers.Remove(supplier);
    }

    #endregion

    #region Energy Decay System
    IEnumerator EnergyDecayCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);
            ProcessEnergyDecay();
        }
    }

    void ProcessEnergyDecay()
    {
        // Debug every 60 frames
        if (Time.frameCount % 60 == 0)
        {
            //Debug.Log($"[ENERGY] Core decay rate: {coreEnergyDecayRate:F2}");
        }
        for (int i = energyConsumers.Count - 1; i >= 0; i--)
        {
            if (energyConsumers[i] == null)
            {
                energyConsumers.RemoveAt(i);
                continue;
            }

            var consumer = energyConsumers[i];
            float decayAmount = GetDecayRate(consumer) * 0.1f;
            consumer.ConsumeEnergy(decayAmount);

            CheckGameOverCondition(consumer);
        }
    }

    float GetDecayRate(IEnergyConsumer consumer)
    {
        float baseRate = consumer is CentralCore ? coreEnergyDecayRate : towerEnergyDecayRate;
        float finalRate = baseRate * globalEnergyDecayRate;
        if (consumer is Tower tower)
        {
            // Check for Tower Commander boost (energy decay reduction)
            var commanderBoost = tower.GetComponent<TowerCommanderBoost>();
            if (commanderBoost != null)
            {
                finalRate *= commanderBoost.GetEnergyDecayMultiplier();
                //Debug.Log($"[ENERGY_MANAGER] Tower {tower.towerName} energy decay: {baseRate * globalEnergyDecayRate:F3} -> {finalRate:F3} (multiplier: {commanderBoost.GetEnergyDecayMultiplier():F3})");
            }

            // Check for Generator Proximity boost
            var generatorBoost = tower.GetComponent<GeneratorProximityBoost>();
            if (generatorBoost != null)
            {
                float beforeProximity = finalRate;
                finalRate *= generatorBoost.GetEnergyEfficiencyMultiplier();
                //Debug.Log($"[ENERGY_MANAGER] Tower {tower.towerName} decay reduced by generator proximity: {beforeProximity:F3} -> {finalRate:F3}");
            }
            // Check for Tower Tether NEAR-zone decay boost  ← NEW
            var tetherDecayBoost = tower.GetComponent<TowerTetherDecayBoost>();
            if (tetherDecayBoost != null)
                finalRate *= tetherDecayBoost.GetDecayMultiplier();

        }

        return finalRate;
    }

    void CheckGameOverCondition(IEnergyConsumer consumer)
    {
        if (!isGameOver && consumer is CentralCore && consumer.GetEnergyPercentage() <= coreDeadEnergyThreshold)
        {
            TriggerGameOver();
        }
    }
    #endregion

    #region Consumer Management
    public List<IEnergyConsumer> GetAllEnergyConsumers()
    {
        return new List<IEnergyConsumer>(energyConsumers);
    }

    public void RegisterEnergyConsumer(IEnergyConsumer consumer)
    {
        if (consumer == null)
        {
            Debug.LogError("Tried to register null consumer!");
            return;
        }

        if (energyConsumers.Contains(consumer))
        {
            return;
        }

        energyConsumers.Add(consumer);
        InitializeConsumerEnergy(consumer);
    }

    public void UnregisterEnergyConsumer(IEnergyConsumer consumer)
    {
        energyConsumers.Remove(consumer);
        destroyedConsumers.Remove(consumer);
    }

    void InitializeConsumerEnergy(IEnergyConsumer consumer)
    {
        if (consumer is CentralCore)
        {
            consumer.SetMaxEnergy(coreMaxEnergy);
            consumer.SetEnergy(coreMaxEnergy);
        }
        else if (consumer is Tower)
        {
            consumer.SetMaxEnergy(towerMaxEnergy);
            consumer.SetEnergy(towerMaxEnergy);
        }
    }
    #endregion

    #region Visual System
    public Color GetEnergyColor(IEnergyConsumer consumer)
    {
        if (consumer.IsEnergyDepleted())
            return depletedEnergyColor;

        if (consumer.IsEnergyLow())
        {
            float criticalThreshold = GetCriticalThreshold(consumer);
            return Color.Lerp(criticalEnergyColor, lowEnergyColor, consumer.GetEnergyPercentage() / criticalThreshold);
        }

        return normalColor;
    }

    public void UpdateConsumerVisuals(IEnergyConsumer consumer, SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer != null)
            spriteRenderer.color = GetEnergyColor(consumer);
    }
    #endregion

    #region Threshold Getters
    public float GetTowerCriticalThreshold() => towerCriticalEnergyThreshold;
    public float GetTowerDeadThreshold() => towerDeadEnergyThreshold;
    public float GetCoreCriticalThreshold() => coreCriticalEnergyThreshold;
    public float GetCoreDeadThreshold() => coreDeadEnergyThreshold;

    public float GetCriticalThreshold(IEnergyConsumer consumer)
    {
        return consumer is CentralCore ? coreCriticalEnergyThreshold : towerCriticalEnergyThreshold;
    }

    public float GetDeadThreshold(IEnergyConsumer consumer)
    {
        return consumer is CentralCore ? coreDeadEnergyThreshold : towerDeadEnergyThreshold;
    }

    public float GetEnemyDamageToTowers() => enemyDamageToTowers;
    public float GetEnemyDamageToCore() => enemyDamageToCore;
    public float GetDefaultEnemyDamage() => defaultEnemyDamage;
    #endregion

    #region Game Management
    public void TriggerGameOver()
    {
        if (isGameOver) return;

        isGameOver = true;
        OnGameOver?.Invoke();
        StopAllCoroutines();
    }
    #endregion

    public bool IsConsumerDestroyed(IEnergyConsumer consumer)
    {
        return destroyedConsumers.Contains(consumer);
    }

    #region Cleanup
    void CleanupEnergyManager()
    {
        StopAllCoroutines();
        energyConsumers?.Clear();
        destroyedConsumers?.Clear();

        // Properly cleanup and null the supply beam
        if (supplyBeam != null)
        {
            supplyBeam.Cleanup();
            supplyBeam = null;
            //Debug.Log("[EnergyManager] Supply beam cleaned up and nulled");
        }

        if (instance == this)
            instance = null;
    }

    public bool IsSupplyBeamReady()
    {
        return supplyBeam != null;
    }

    public void ForceReinitializeSupplyBeam()
    {
        if (supplyBeam != null)
        {
            supplyBeam.Cleanup();
        }
        supplyBeam = null;
        EnsureSupplyBeamInitialized();
    }

    void CleanupManager()
    {
        if (instance == this)
            instance = null;

        StopAllCoroutines();
        energyConsumers?.Clear();
    }

    void HandleApplicationQuit()
    {
        isApplicationQuitting = true;
        instance = null;
    }
    #endregion

    #region Editor Support
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void InitializeOnLoad()
    {
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        switch (state)
        {
            case UnityEditor.PlayModeStateChange.ExitingPlayMode:
                isApplicationQuitting = true;
                instance = null;
                break;
            case UnityEditor.PlayModeStateChange.EnteredEditMode:
                isApplicationQuitting = false;
                instance = null;
                break;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (player == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(player.transform.position, supplyRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(player.transform.position, maxSupplyDistance);

        Gizmos.color = Color.cyan;
        foreach (var consumer in energyConsumers)
        {
            if (consumer != null && IsPlayerInRange(consumer))
                Gizmos.DrawLine(player.transform.position, consumer.GetPosition());
        }

        UnityEditor.Handles.Label(player.transform.position + Vector3.up * 2f,
            $"Player Energy: {currentPlayerEnergy}");
    }

    void OnValidate()
    {
        playerStartingEnergy = Mathf.Max(0, playerStartingEnergy);
        towerBuildCost = Mathf.Max(1, towerBuildCost);
        towerSellRefundPercentage = Mathf.Clamp01(towerSellRefundPercentage);
        energyPerEnemyKill = Mathf.Max(0, energyPerEnemyKill);
        repairEnergyPerClick = Mathf.Max(1, repairEnergyPerClick);
        repairCostPerClick = Mathf.Max(1, repairCostPerClick);
        repairCooldown = Mathf.Max(0.1f, repairCooldown);
        continuousSupplyRate = Mathf.Max(0.1f, continuousSupplyRate);
        continuousSupplyCost = Mathf.Max(0.1f, continuousSupplyCost);
        minSupplyInterval = Mathf.Max(0.01f, minSupplyInterval);
        beamPulseSpeed = Mathf.Max(0.1f, beamPulseSpeed);
        beamPulseIntensity = Mathf.Clamp01(beamPulseIntensity);
        beamFlowSpeed = Mathf.Max(0.1f, beamFlowSpeed);
        supplyBeamWidth = Mathf.Max(0.01f, supplyBeamWidth);
    }
#endif
    #endregion
}

#region Enhanced Supply Beam Controller
public class SupplyBeamController
{
    private readonly EnergyManager energyManager;

    // Two-layer ribbon: a soft core line + a wide, faint additive halo behind it.
    private LineRenderer beam;
    private LineRenderer glow;
    private GameObject container;

    // --- Subtlety tuning (kept private so the look can't be cranked back into the
    //     old strobing beam from the inspector) -------------------------------------
    private const int Segments = 20;     // points along the ribbon (smooth curve)
    private const float WobbleAmplitude = 0.06f;  // gentle lateral sway, world units
    private const float WobbleWaves = 2.2f;   // sine waves along the length
    private const float Sag = 0.10f;  // small downward droop at mid-span
    private const float BreatheAmount = 0.10f;  // +/-10% width breathing
    private const float FlowSoftness = 0.20f;  // half-width of the travelling pulse band
    private const float CoreWidthScale = 0.6f;   // core thinner than the configured width
    private const float GlowWidthScale = 2.2f;   // halo wider than the core
    private const float StartYOffset = 0.10f;  // lift the beam slightly off the player
    private const float EndYOffset = 0.20f;  // meet the structure a touch above centre
    private const float MaxCoreAlpha = 0.55f;  // never let the core go fully opaque
    private const float BaselineAlpha = 0.45f;  // dim level between flowing pulses

    private float flowT;   // 0..1 travelling-pulse position (player -> target)
    private float pulseT;  // width-breathing phase

    private bool isInitialized;
    private bool isDestroyed;

    private readonly Vector3[] points = new Vector3[Segments];

    public SupplyBeamController(EnergyManager manager)
    {
        energyManager = manager;
        try
        {
            Setup();
            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SupplyBeamController] Failed to initialize: {e.Message}");
            isInitialized = false;
        }
    }

    public bool IsValid()
    {
        if (isDestroyed || !isInitialized) return false;
        try { return container != null && beam != null && energyManager != null; }
        catch (System.Exception) { return false; }
    }

    void Setup()
    {
        if (energyManager == null) throw new System.Exception("EnergyManager is null");

        container = new GameObject("SupplyBeamContainer");
        container.transform.SetParent(energyManager.transform);

        float w = Mathf.Max(0.01f, energyManager.supplyBeamWidth);

        beam = MakeLine("SupplyBeam", w * CoreWidthScale, 105, additive: false);

        if (energyManager.enableBeamGlow)
            glow = MakeLine("BeamGlow", w * GlowWidthScale, 100, additive: true);
    }

    LineRenderer MakeLine(string childName, float width, int order, bool additive)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(container.transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.material = additive ? CreateGlowMaterial() : CreateBeamMaterial();
        lr.positionCount = Segments;
        lr.useWorldSpace = true;
        lr.numCapVertices = 6;
        lr.numCornerVertices = 4;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.sortingOrder = order;
        lr.receiveShadows = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.enabled = false;
        return lr;
    }

    Material CreateBeamMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        return mat;
    }

    Material CreateGlowMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        // Additive so the halo reads as light, not paint.
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        return mat;
    }

    public void Update(bool isSupplying, IEnergyConsumer target, GameObject player)
    {
        if (!IsValid()) return;

        try
        {
            if (!isSupplying || target == null || player == null)
            {
                SetEnabled(false);
                return;
            }

            if (!beam.enabled) SetEnabled(true);

            // Pure visual now — energy transfer is driven by the caller
            // (UpdateManager for the legacy singleton, PlayerTowerPlacer for each
            // per-player beam). This lets one controller instance be owned per player
            // with no shared-state conflicts.
            bool repair = TowerPlacementManager.Instance != null &&
                          TowerPlacementManager.Instance.IsInPlacementMode();

            // Anchor points. `player` is the supplier this beam belongs to, so the
            // beam always starts at the RIGHT player (co-op safe).
            Vector3 a = player.transform.position + Vector3.up * StartYOffset;
            Vector3 b = target.GetPosition() + Vector3.up * EndYOffset;

            // Advance animation phases (scaled down for a calm, subtle feel).
            flowT = (flowT + Time.deltaTime * Mathf.Max(0.1f, energyManager.beamFlowSpeed) * 0.12f) % 1f;
            pulseT += Time.deltaTime * Mathf.Max(0.1f, energyManager.beamPulseSpeed);
            float breathe = 0.5f * (Mathf.Sin(pulseT) + 1f); // 0..1

            BuildCurve(a, b);
            beam.SetPositions(points);
            if (glow != null) glow.SetPositions(points);

            // Subtle width breathing around the configured width.
            float baseW = Mathf.Max(0.01f, energyManager.supplyBeamWidth);
            float wMul = 1f + (breathe - 0.5f) * 2f * BreatheAmount;
            beam.startWidth = beam.endWidth = baseW * CoreWidthScale * wMul;
            if (glow != null) glow.startWidth = glow.endWidth = baseW * GlowWidthScale * wMul;

            ApplyColors(repair, breathe);
            ApplySort(a, b);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SupplyBeamController] Update error: {e.Message}");
        }
    }

    // Fills `points` with a softly-curved ribbon: a low-amplitude travelling sine
    // sway plus a small mid-span droop, both tapered to zero at the endpoints so the
    // ends stay pinned exactly to the player and the structure.
    void BuildCurve(Vector3 a, Vector3 b)
    {
        Vector3 dir = b - a;
        float len = dir.magnitude;

        if (len < 1e-4f)
        {
            for (int i = 0; i < Segments; i++) points[i] = a;
            return;
        }

        Vector3 fwd = dir / len;
        Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);
        float t = flowT * Mathf.PI * 2f;

        for (int i = 0; i < Segments; i++)
        {
            float u = (Segments == 1) ? 0f : (float)i / (Segments - 1);
            float taper = Mathf.Sin(u * Mathf.PI);                 // 0 at ends, 1 mid
            float wob = Mathf.Sin(u * Mathf.PI * WobbleWaves * 2f - t) * WobbleAmplitude * taper;

            Vector3 p = Vector3.Lerp(a, b, u) + perp * wob;
            p.y -= Sag * taper;                                    // gentle droop
            p.z = 0f;
            points[i] = p;
        }
    }

    // Soft, low-alpha gradient with a single bright band that travels from the player
    // end to the structure end — reads as energy gently flowing IN (healing), not a
    // strobing laser. The glow stays a steady faint halo.
    void ApplyColors(bool repair, float breathe)
    {
        Color baseColor = repair ? energyManager.repairBeamColor : energyManager.supplyBeamColor;
        float a = baseColor.a;

        float dim = BaselineAlpha * a;
        float peak = Mathf.Min(MaxCoreAlpha, a * (0.85f + 0.15f * breathe));

        var colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(baseColor, 0f),
            new GradientColorKey(Color.Lerp(baseColor, Color.white, 0.35f), 0.5f),
            new GradientColorKey(baseColor, 1f),
        };

        // Travelling alpha pulse centred on flowT, tapering to the dim baseline.
        var ak = new System.Collections.Generic.List<GradientAlphaKey>(6)
        {
            new GradientAlphaKey(dim, 0f),
        };
        float c = Mathf.Clamp01(flowT);
        float l = c - FlowSoftness;
        float r = c + FlowSoftness;
        if (l > 0f && l < 1f) ak.Add(new GradientAlphaKey(dim, l));
        ak.Add(new GradientAlphaKey(peak, c));
        if (r > 0f && r < 1f) ak.Add(new GradientAlphaKey(dim, r));
        ak.Add(new GradientAlphaKey(dim, 1f));
        ak.Sort((x, y) => x.time.CompareTo(y.time));
        while (ak.Count > 8) ak.RemoveAt(ak.Count - 1);

        var g = new Gradient();
        g.SetKeys(colorKeys, ak.ToArray());
        beam.colorGradient = g;

        if (glow != null)
        {
            Color glowColor = repair
                ? Color.Lerp(energyManager.repairBeamColor, energyManager.beamGlowColor, 0.5f)
                : energyManager.beamGlowColor;
            float ga = glowColor.a * (0.35f + 0.15f * breathe);

            var gg = new Gradient();
            gg.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(glowColor, 0f),
                    new GradientColorKey(glowColor, 1f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(ga, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            glow.colorGradient = gg;
        }
    }

    void ApplySort(Vector3 a, Vector3 b)
    {
        // Same y-sort convention as PlayerMovement / GrassCartoonOverlay. Anchor to the
        // foreground (lower-Y) endpoint, then nudge in front by beamSortBias.
        float startSortY = a.y + energyManager.beamSortYOffset;
        float endSortY = b.y + energyManager.beamSortYOffset;
        int startOrder = energyManager.beamSortOrderBase + Mathf.RoundToInt(-startSortY * energyManager.beamSortPrecision);
        int endOrder = energyManager.beamSortOrderBase + Mathf.RoundToInt(-endSortY * energyManager.beamSortPrecision);
        int foreground = Mathf.Max(startOrder, endOrder);
        beam.sortingOrder = foreground + energyManager.beamSortBias;
        if (glow != null) glow.sortingOrder = foreground + energyManager.beamSortBias - 1;
    }

    public void SetEnabled(bool enabled)
    {
        if (!IsValid()) return;
        try
        {
            if (beam != null) beam.enabled = enabled;
            if (glow != null) glow.enabled = enabled;
            if (!enabled)
            {
                flowT = 0f;
                pulseT = 0f;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SupplyBeamController] SetEnabled error: {e.Message}");
        }
    }

    public void Cleanup()
    {
        try
        {
            isDestroyed = true;
            if (container != null)
            {
                Object.DestroyImmediate(container);
                container = null;
            }
            beam = null;
            glow = null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SupplyBeamController] Cleanup error: {e.Message}");
        }
    }
}
#endregion

#region Supporting Components
public interface IEnergyConsumer
{
    void ConsumeEnergy(float amount);
    void SupplyEnergy(float amount);
    void SetEnergy(float amount);
    void SetMaxEnergy(float amount);
    float GetEnergy();
    float GetMaxEnergy();
    float GetEnergyPercentage();
    bool IsEnergyDepleted();
    bool IsEnergyLow();
    Vector3 GetPosition();
}

public class EnergyUI : MonoBehaviour
{
    [Header("UI References")]
    public UnityEngine.UI.Slider energySlider;
    public UnityEngine.UI.Text energyText;
    public UnityEngine.UI.Text statusText;

    private IEnergyConsumer trackedConsumer;

    public void SetTrackedConsumer(IEnergyConsumer consumer) => trackedConsumer = consumer;

    void Update()
    {
        if (trackedConsumer == null) return;

        UpdateEnergySlider();
        UpdateEnergyText();
        UpdateStatusText();
    }

    void UpdateEnergySlider()
    {
        if (energySlider != null)
            energySlider.value = trackedConsumer.GetEnergyPercentage();
    }

    void UpdateEnergyText()
    {
        if (energyText != null)
            energyText.text = $"{trackedConsumer.GetEnergy():F1}/{trackedConsumer.GetMaxEnergy():F1}";
    }

    void UpdateStatusText()
    {
        if (statusText == null) return;

        if (trackedConsumer.IsEnergyDepleted())
        {
            statusText.text = "DEPLETED";
            statusText.color = Color.red;
        }
        else if (trackedConsumer.IsEnergyLow())
        {
            statusText.text = "LOW ENERGY";
            statusText.color = Color.yellow;
        }
        else
        {
            statusText.text = "OPERATIONAL";
            statusText.color = Color.green;
        }
    }
}
#endregion
