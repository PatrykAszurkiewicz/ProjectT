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
            {
                instance = FindFirstObjectByType<EnergyManager>();
                if (instance == null)
                {
                    var go = new GameObject("EnergyManager");
                    instance = go.AddComponent<EnergyManager>();
                }
            }
            return instance;
        }
    }
    #endregion

    private float accumulatedPlayerEnergyCost = 0f;


    #region Configuration
    [Header("Global Energy Settings")]
    public float globalEnergyDecayRate = 1f;
    public float supplyRange = 2f;
    public float supplyRate = 10f;
    public float maxSupplyDistance = 0.5f;

    [Header("Repair System Settings")]
    public int repairEnergyPerClick = 10;
    public int repairCostPerClick = 10;
    public float repairCooldown = 0.5f;
    public bool onlyAllowRepairInPlacementMode = true;

    [Header("Continuous Supply Settings")]
    public float continuousSupplyRate = 3f; // Energy per second when holding button
    public float continuousSupplyCost = 3f; // Player energy cost per second
    public float minSupplyInterval = 0.1f; // Minimum time between supply ticks (20fps)

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
    public float towerSellRefundPercentage = 0.5f;
    public bool enableCurrencyEarnedFromEnemyKills = true;
    public int energyPerEnemyKill = 25;

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
    public Color supplyBeamColor = new Color(0.2f, 0.7f, 1f, 0.35f);   // 0.9f = 230/255
    public Color repairBeamColor = new Color(0.3f, 0.9f, 1f, 0.35f);  // 0.94f = 240/255
    public Color beamGlowColor = new Color(0.6f, 0.8f, 1f, 0.35f);     // 0.5f = 128/255
    public float supplyBeamWidth = 0.3f;
    public LayerMask supplyTargetMask = -1;

    [Header("Beam Effects")]
    public float beamPulseSpeed = 4f;
    public float beamPulseIntensity = 0.6f;
    public float beamFlowSpeed = 8f;
    public bool enableBeamGlow = true;
    public AnimationCurve beamPulseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    #endregion

    #region Core Components
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
            supplyBeam = new SupplyBeamController(this);
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
        supplyBeam?.Update(isSupplying, currentSupplyTarget, player);
    }

    void HandleSupplyInput()
    {
        if (player == null) return;

        // Check if we're in placement mode
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        // Check for input
        bool hasInput = (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
                       (Keyboard.current != null && Keyboard.current.spaceKey.isPressed);

        // Only allow supply/repair in placement mode
        if (onlyAllowRepairInPlacementMode && !inPlacementMode)
        {
            StopSupplying();
            return;
        }

        // Process input
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
        // Check mouse input first
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
            GivePlayerEnergy(energyPerEnemyKill);
        }
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
    void StartSupplying(IEnergyConsumer target)
    {
        currentSupplyTarget = target;
        isSupplying = true;
        supplyBeam?.SetEnabled(true);
    }

    private void StopSupplying()
    {
        currentSupplyTarget = null;
        isSupplying = false;
        supplyBeam?.SetEnabled(false);

        // Reset continuous supply state
        isContinuouslySupplying = false;
        continuousSupplyTarget = null;

        // Reset accumulated cost when stopping supply
        accumulatedPlayerEnergyCost = 0f;
    }

    IEnergyConsumer GetSupplyTarget(Vector3 position)
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

        // Check if we're in placement mode (repair mode)
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        if (inPlacementMode)
        {
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
        return baseRate * globalEnergyDecayRate;
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
        supplyBeam?.Cleanup();

        if (instance == this)
            instance = null;
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
    private LineRenderer supplyBeam;
    private LineRenderer glowBeam; // Additional glow effect
    private GameObject supplyBeamContainer;

    // Enhanced visual feedback fields
    private float beamIntensity = 1f;
    private float glowIntensity = 0.5f;
    private bool isContinuousMode = false;
    private float flowAnimationTime = 0f;
    private float pulseAnimationTime = 0f;

    public SupplyBeamController(EnergyManager manager)
    {
        energyManager = manager;
        SetupSupplyBeam();
    }

    void SetupSupplyBeam()
    {
        supplyBeamContainer = new GameObject("SupplyBeamContainer");
        supplyBeamContainer.transform.SetParent(energyManager.transform);

        // Main beam
        supplyBeam = supplyBeamContainer.AddComponent<LineRenderer>();
        ConfigureMainBeam();

        // Glow effect beam (if enabled)
        if (energyManager.enableBeamGlow)
        {
            SetupGlowBeam();
        }
    }

    void ConfigureMainBeam()
    {
        supplyBeam.material = CreateBeamMaterial();
        supplyBeam.startWidth = energyManager.supplyBeamWidth;
        supplyBeam.endWidth = energyManager.supplyBeamWidth * 0.6f; // Slight taper
        supplyBeam.positionCount = 2;
        supplyBeam.useWorldSpace = true;
        supplyBeam.sortingOrder = 105;
        supplyBeam.enabled = false;

        // Enhanced line properties for smoother appearance
        supplyBeam.numCapVertices = 10;
        supplyBeam.numCornerVertices = 10;
        supplyBeam.useWorldSpace = true;

        SetupEnhancedGradient();
    }

    void SetupGlowBeam()
    {
        GameObject glowObject = new GameObject("BeamGlow");
        glowObject.transform.SetParent(supplyBeamContainer.transform);

        glowBeam = glowObject.AddComponent<LineRenderer>();
        glowBeam.material = CreateGlowMaterial();
        glowBeam.startWidth = energyManager.supplyBeamWidth * 3f;
        glowBeam.endWidth = energyManager.supplyBeamWidth * 2.5f;
        glowBeam.positionCount = 2;
        glowBeam.useWorldSpace = true;
        glowBeam.sortingOrder = 100; // Behind main beam
        glowBeam.enabled = false;
        glowBeam.numCapVertices = 15;
        glowBeam.numCornerVertices = 15;

        SetupGlowGradient();
    }

    Material CreateBeamMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        // Enable blending for transparency
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
        // Additive blending for glow effect
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;

        return mat;
    }

    void SetupEnhancedGradient()
    {
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        Color baseColor = inPlacementMode ? energyManager.repairBeamColor : energyManager.supplyBeamColor;
        var gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[4];
        colorKeys[0] = new GradientColorKey(baseColor, 0f);
        colorKeys[1] = new GradientColorKey(Color.Lerp(baseColor, Color.white, 0.3f), 0.3f);
        colorKeys[2] = new GradientColorKey(Color.Lerp(baseColor, Color.white, 0.5f), 0.7f);
        colorKeys[3] = new GradientColorKey(baseColor, 1f);

        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[4];
        alphaKeys[0] = new GradientAlphaKey(baseColor.a * 0.8f, 0f);
        alphaKeys[1] = new GradientAlphaKey(baseColor.a, 0.2f);
        alphaKeys[2] = new GradientAlphaKey(baseColor.a, 0.8f);
        alphaKeys[3] = new GradientAlphaKey(baseColor.a * 0.6f, 1f);

        gradient.SetKeys(colorKeys, alphaKeys);
        supplyBeam.colorGradient = gradient;
    }

    void SetupGlowGradient()
    {
        if (glowBeam == null) return;

        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        Color glowColor = inPlacementMode ?
            Color.Lerp(energyManager.repairBeamColor, energyManager.beamGlowColor, 0.5f) :
            energyManager.beamGlowColor;

        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(glowColor, 0f),
                new GradientColorKey(glowColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(glowColor.a * glowIntensity, 0.5f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        glowBeam.colorGradient = gradient;
    }

    public void Update(bool isSupplying, IEnergyConsumer target, GameObject player)
    {
        if (!isSupplying || target == null || player == null)
        {
            SetEnabled(false);
            isContinuousMode = false;
            return;
        }

        UpdateBeamPositions(player, target);
        SupplyEnergy(target);

        // Update animation timers
        flowAnimationTime += Time.deltaTime * energyManager.beamFlowSpeed;
        pulseAnimationTime += Time.deltaTime * energyManager.beamPulseSpeed;

        // Detect continuous mode
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        isContinuousMode = inPlacementMode && energyManager.isContinuouslySupplying;

        UpdateEnhancedVisualEffects();
    }

    void UpdateBeamPositions(GameObject player, IEnergyConsumer target)
    {
        Vector3 startPos = player.transform.position;
        Vector3 endPos = target.GetPosition();

        supplyBeam.SetPosition(0, startPos);
        supplyBeam.SetPosition(1, endPos);

        if (glowBeam != null)
        {
            glowBeam.SetPosition(0, startPos);
            glowBeam.SetPosition(1, endPos);
        }
    }

    void SupplyEnergy(IEnergyConsumer target)
    {
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        if (inPlacementMode)
        {
            energyManager.SupplyEnergyToTarget(target, 0);
        }
        else
        {
            float energyToSupply = energyManager.supplyRate * Time.deltaTime;
            energyManager.SupplyEnergyToTarget(target, energyToSupply);
        }
    }

    void UpdateEnhancedVisualEffects()
    {
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        Color baseColor = inPlacementMode ? energyManager.repairBeamColor : energyManager.supplyBeamColor;

        float rawPulse = Mathf.Sin(pulseAnimationTime);
        float pulseValue = (rawPulse + 1f) * 0.5f; // 0 to 1

        if (isContinuousMode)
        {

            beamIntensity = Mathf.Lerp(0.1f, 2.5f, pulseValue);
            glowIntensity = Mathf.Lerp(0.1f, 2.0f, pulseValue);

            float widthMultiplier = Mathf.Lerp(0.5f, 3.0f, pulseValue);
            supplyBeam.startWidth = energyManager.supplyBeamWidth * widthMultiplier;
            supplyBeam.endWidth = energyManager.supplyBeamWidth * widthMultiplier * 0.8f;
        }
        else
        {
            beamIntensity = Mathf.Lerp(0.05f, 3.0f, pulseValue);
            glowIntensity = Mathf.Lerp(0.05f, 2.5f, pulseValue);

            float widthMultiplier = Mathf.Lerp(0.3f, 4.0f, pulseValue);
            supplyBeam.startWidth = energyManager.supplyBeamWidth * widthMultiplier;
            supplyBeam.endWidth = energyManager.supplyBeamWidth * widthMultiplier * 0.7f;
        }

        UpdateDramaticFlowGradient(baseColor);

        // Update glow effect
        if (glowBeam != null)
        {
            UpdateDramaticGlowEffect();
        }
    }

    void UpdateDramaticGlowEffect()
    {
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        Color glowColor = inPlacementMode ?
            Color.Lerp(energyManager.repairBeamColor, energyManager.beamGlowColor, 0.5f) :
            energyManager.beamGlowColor;

        var gradient = new Gradient();
        float currentGlowAlpha = glowColor.a * glowIntensity;

        gradient.SetKeys(
            new GradientColorKey[] {
            new GradientColorKey(glowColor, 0f),
            new GradientColorKey(glowColor, 1f)
            },
            new GradientAlphaKey[] {
            new GradientAlphaKey(0f, 0f),
            new GradientAlphaKey(currentGlowAlpha * 0.2f, 0.2f),
            new GradientAlphaKey(currentGlowAlpha * 2.0f, 0.5f),
            new GradientAlphaKey(currentGlowAlpha * 0.2f, 0.8f),
            new GradientAlphaKey(0f, 1f)
            }
        );
        glowBeam.colorGradient = gradient;

        float rawPulse = Mathf.Sin(pulseAnimationTime * 1.5f);
        float glowPulse = (rawPulse + 1f) * 0.5f; // 0 to 1
        float glowWidthMultiplier = Mathf.Lerp(0.2f, 5.0f, glowPulse);

        glowBeam.startWidth = energyManager.supplyBeamWidth * 3f * glowWidthMultiplier;
        glowBeam.endWidth = energyManager.supplyBeamWidth * 2.5f * glowWidthMultiplier;
    }

    void UpdateDramaticFlowGradient(Color baseColor)
    {
        var gradient = new Gradient();
        float baseAlpha = baseColor.a * beamIntensity;

        gradient.SetKeys(
            new GradientColorKey[] {
            new GradientColorKey(baseColor, 0f),
            new GradientColorKey(Color.Lerp(baseColor, Color.white, 0.8f), 0.5f),
            new GradientColorKey(baseColor, 1f)
            },
            new GradientAlphaKey[] {
            new GradientAlphaKey(baseAlpha * 0.1f, 0f),
            new GradientAlphaKey(baseAlpha * 1.5f, 0.5f),
            new GradientAlphaKey(baseAlpha * 0.1f, 1f)
            }
        );

        supplyBeam.colorGradient = gradient;
    }

    void UpdateFlowGradient(Color baseColor)
    {
        var gradient = new Gradient();

        // Create flowing energy effect by shifting the bright spots
        float flowOffset = (flowAnimationTime % 1f);

        List<GradientColorKey> colorKeys = new List<GradientColorKey>();
        List<GradientAlphaKey> alphaKeys = new List<GradientAlphaKey>();

        // Base color at start and end
        colorKeys.Add(new GradientColorKey(baseColor, 0f));
        colorKeys.Add(new GradientColorKey(baseColor, 1f));

        // Flowing bright spots
        float brightSpot1 = Mathf.Repeat(flowOffset, 1f);
        float brightSpot2 = Mathf.Repeat(flowOffset + 0.5f, 1f);

        Color brightColor = Color.Lerp(baseColor, Color.white, 0.6f);

        if (brightSpot1 > 0.1f && brightSpot1 < 0.9f)
        {
            colorKeys.Add(new GradientColorKey(brightColor, brightSpot1));
        }
        if (brightSpot2 > 0.1f && brightSpot2 < 0.9f)
        {
            colorKeys.Add(new GradientColorKey(brightColor, brightSpot2));
        }

        // Sort color keys by time
        colorKeys.Sort((a, b) => a.time.CompareTo(b.time));

        // Alpha keys for smooth transparency
        float baseAlpha = baseColor.a * beamIntensity;
        alphaKeys.Add(new GradientAlphaKey(baseAlpha * 0.7f, 0f));
        alphaKeys.Add(new GradientAlphaKey(baseAlpha, 0.2f));
        alphaKeys.Add(new GradientAlphaKey(baseAlpha, 0.8f));
        alphaKeys.Add(new GradientAlphaKey(baseAlpha * 0.7f, 1f));

        gradient.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());
        supplyBeam.colorGradient = gradient;
    }

    void UpdateGlowEffect()
    {
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        Color glowColor = inPlacementMode ?
            Color.Lerp(energyManager.repairBeamColor, energyManager.beamGlowColor, 0.5f) :
            energyManager.beamGlowColor;

        var gradient = new Gradient();
        float currentGlowAlpha = glowColor.a * glowIntensity;

        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(glowColor, 0f),
                new GradientColorKey(glowColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(currentGlowAlpha * 0.8f, 0.3f),
                new GradientAlphaKey(currentGlowAlpha, 0.5f),
                new GradientAlphaKey(currentGlowAlpha * 0.8f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        glowBeam.colorGradient = gradient;

        // Slight width pulsing for glow
        float glowWidthMultiplier = 1f + (Mathf.Sin(pulseAnimationTime * 1.5f) * 0.2f);
        glowBeam.startWidth = energyManager.supplyBeamWidth * 3f * glowWidthMultiplier;
        glowBeam.endWidth = energyManager.supplyBeamWidth * 2.5f * glowWidthMultiplier;
    }

    public void SetEnabled(bool enabled)
    {
        supplyBeam.enabled = enabled;
        if (glowBeam != null)
        {
            glowBeam.enabled = enabled;
        }

        if (!enabled)
        {
            isContinuousMode = false;
            beamIntensity = 1f;
            glowIntensity = 0.5f;
            flowAnimationTime = 0f;
            pulseAnimationTime = 0f;
        }
    }

    public void Cleanup()
    {
        if (supplyBeamContainer != null)
        {
            Object.DestroyImmediate(supplyBeamContainer);
            supplyBeamContainer = null;
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