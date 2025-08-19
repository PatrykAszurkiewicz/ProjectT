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

    [Header("Tower Energy Settings")]
    public float towerMaxEnergy = 100f;
    public float towerEnergyDecayRate = 0.7f;
    public float towerCriticalEnergyThreshold = 0.2f;
    //public float towerDeadEnergyThreshold = 0.05f;
    public float towerDeadEnergyThreshold = 0.0f;
    [Header("Central Core Energy Settings")]
    public float coreMaxEnergy = 100f;
    public float coreEnergyDecayRate = 0.7f;
    public float coreCriticalEnergyThreshold = 0.3f;
    //public float coreDeadEnergyThreshold = 0.1f;
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

    [Header("Supply Beam")]
    public Color supplyBeamColor = Color.cyan;
    public Color repairBeamColor = Color.green;
    public float supplyBeamWidth = 0.1f;
    public LayerMask supplyTargetMask = -1;
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
        StartEnergyDecay();
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

    void StopSupplying()
    {
        currentSupplyTarget = null;
        isSupplying = false;
        supplyBeam?.SetEnabled(false);
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
            // Repair mode
            TryRepairTarget(target);
        }
        else
        {
            if (onlyAllowRepairInPlacementMode)
            {
                return;
            }

            // Original free energy behavior for backwards compatibility
            // TODO review if it should be refactored
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
    }
#endif
    #endregion
}

#region Supply Beam Controller
public class SupplyBeamController
{
    private readonly EnergyManager energyManager;
    private LineRenderer supplyBeam;
    private GameObject supplyBeamContainer;

    public SupplyBeamController(EnergyManager manager)
    {
        energyManager = manager;
        SetupSupplyBeam();
    }

    void SetupSupplyBeam()
    {
        supplyBeamContainer = new GameObject("SupplyBeam");
        supplyBeamContainer.transform.SetParent(energyManager.transform);

        supplyBeam = supplyBeamContainer.AddComponent<LineRenderer>();
        ConfigureLineRenderer();
    }

    void ConfigureLineRenderer()
    {
        supplyBeam.material = new Material(Shader.Find("Sprites/Default"));
        supplyBeam.startWidth = energyManager.supplyBeamWidth;
        supplyBeam.endWidth = energyManager.supplyBeamWidth;
        supplyBeam.positionCount = 2;
        supplyBeam.useWorldSpace = true;
        supplyBeam.sortingOrder = 100;
        supplyBeam.enabled = false;

        SetupGradient();
    }

    void SetupGradient()
    {
        // Check if we're in placement mode to choose beam color
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        Color beamColor = inPlacementMode ? energyManager.repairBeamColor : energyManager.supplyBeamColor;

        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(beamColor, 0f),
                new GradientColorKey(beamColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        supplyBeam.colorGradient = gradient;
    }

    public void Update(bool isSupplying, IEnergyConsumer target, GameObject player)
    {
        if (!isSupplying || target == null || player == null)
        {
            SetEnabled(false);
            return;
        }

        UpdateBeamPositions(player, target);
        SupplyEnergy(target);
        UpdateVisualEffects();
    }

    void UpdateBeamPositions(GameObject player, IEnergyConsumer target)
    {
        supplyBeam.SetPosition(0, player.transform.position);
        supplyBeam.SetPosition(1, target.GetPosition());
    }

    void SupplyEnergy(IEnergyConsumer target)
    {
        // Check if we're in placement mode for different supply behavior
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        if (inPlacementMode)
        {
            // Repair mode
            energyManager.SupplyEnergyToTarget(target, 0);
        }
        else
        {
            // TODO consider if we can remove combat mode energy supply?
            float energyToSupply = energyManager.supplyRate * Time.deltaTime;
            energyManager.SupplyEnergyToTarget(target, energyToSupply);
        }
    }

    void UpdateVisualEffects()
    {
        // Update beam color based on mode
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();
        Color baseColor = inPlacementMode ? energyManager.repairBeamColor : energyManager.supplyBeamColor;

        float pulse = Mathf.Sin(Time.time * 10f) * 0.3f + 0.7f;
        Color beamColor = baseColor;
        beamColor.a = pulse;

        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(beamColor, 0f), new GradientColorKey(beamColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        supplyBeam.colorGradient = gradient;
    }

    public void SetEnabled(bool enabled) => supplyBeam.enabled = enabled;

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