using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnergyManager : MonoBehaviour
{
    #region Singleton Management
    private static bool isApplicationQuitting = false;
    private static EnergyManager instance;

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

    [Header("Tower Energy Settings")]
    public float towerMaxEnergy = 100f;
    public float towerEnergyDecayRate = 0.7f;
    public float towerCriticalEnergyThreshold = 0.2f;
    public float towerDeadEnergyThreshold = 0.05f;

    [Header("Central Core Energy Settings")]
    public float coreMaxEnergy = 100f;
    public float coreEnergyDecayRate = 0.7f;
    public float coreCriticalEnergyThreshold = 0.3f;
    public float coreDeadEnergyThreshold = 0.1f;

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

    // Events
    public System.Action<float> OnGlobalEnergyChanged;
    public System.Action OnGameOver;
    public System.Action<IEnergyConsumer, float> OnEnergyConsumerDamaged;
    public System.Action<IEnergyConsumer> OnEnergyConsumerDestroyed;
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
        StartEnergyDecay();
    }

    void FindReferences()
    {
        player = GameObject.FindGameObjectWithTag("Player");
        mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();
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

        bool supplyInput = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);

        if (supplyInput)
        {
            Vector3 inputPosition = GetInputPosition();
            IEnergyConsumer target = GetSupplyTarget(inputPosition);

            if (target != null && IsPlayerInRange(target))
                StartSupplying(target);
            else
                StopSupplying();
        }
        else
        {
            StopSupplying();
        }
    }

    Vector3 GetInputPosition()
    {
        if (Input.GetMouseButton(0))
        {
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0;
            return mousePos;
        }

        if (Input.GetKey(KeyCode.Space))
        {
            var closest = GetClosestEnergyConsumer(player.transform.position);
            return closest?.GetPosition() ?? Vector3.zero;
        }

        return Vector3.zero;
    }
    #endregion

    #region Enemy Damage System
    /// <summary>
    /// Damage any energy consumer by a specified amount
    /// </summary>
    /// <param name="consumer">The energy consumer to damage</param>
    /// <param name="damage">Amount of energy to remove</param>
    /// <param name="damageSource">Optional source of damage for logging</param>
    /// <returns>True if damage was applied, false if target was invalid</returns>
    public bool DamageEnergyConsumer(IEnergyConsumer consumer, float damage, GameObject damageSource = null)
    {
        if (consumer == null || damage <= 0) return false;

        // Apply damage
        consumer.ConsumeEnergy(damage);

        // Trigger visual/audio effects if enabled
        if (enableEnemyDamageEffects)
        {
            StartCoroutine(DamageFlashEffect(consumer));
        }

        // Log damage for debugging
        string sourceName = damageSource != null ? damageSource.name : "Unknown";
        Debug.Log($"Enemy {sourceName} dealt {damage} damage to {GetConsumerName(consumer)}");

        // Fire damage event
        OnEnergyConsumerDamaged?.Invoke(consumer, damage);

        // Check if this damage caused destruction
        if (consumer.IsEnergyDepleted())
        {
            HandleEnergyConsumerDestroyed(consumer);
        }

        return true;
    }

    /// <summary>
    /// Damage a tower by default tower damage amount
    /// </summary>
    public bool DamageTower(Tower tower, GameObject damageSource = null)
    {
        return DamageEnergyConsumer(tower, enemyDamageToTowers, damageSource);
    }

    /// <summary>
    /// Damage the central core by default core damage amount
    /// </summary>
    public bool DamageCore(CentralCore core, GameObject damageSource = null)
    {
        return DamageEnergyConsumer(core, enemyDamageToCore, damageSource);
    }

    /// <summary>
    /// Find and damage the nearest energy consumer to a position
    /// </summary>
    public bool DamageNearestConsumer(Vector3 position, float damage, float maxRange = 2f, GameObject damageSource = null)
    {
        IEnergyConsumer nearest = GetNearestEnergyConsumer(position, maxRange);
        if (nearest != null)
        {
            return DamageEnergyConsumer(nearest, damage, damageSource);
        }
        return false;
    }

    /// <summary>
    /// Find the nearest energy consumer within range
    /// </summary>
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

    /// <summary>
    /// Get all energy consumers within a certain range
    /// </summary>
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

    /// <summary>
    /// Handle when an energy consumer is destroyed by damage
    /// </summary>
    private void HandleEnergyConsumerDestroyed(IEnergyConsumer consumer)
    {
        string consumerName = GetConsumerName(consumer);
        Debug.Log($"{consumerName} was destroyed by enemy damage!");

        // Fire destruction event
        OnEnergyConsumerDestroyed?.Invoke(consumer);

        // Special handling for core destruction
        if (consumer is CentralCore)
        {
            Debug.Log("Central Core destroyed by enemy attack!");
            TriggerGameOver();
        }
    }

    /// <summary>
    /// Get a display name for an energy consumer
    /// </summary>
    private string GetConsumerName(IEnergyConsumer consumer)
    {
        if (consumer is Tower tower)
            return $"Tower ({tower.towerName})";
        else if (consumer is CentralCore)
            return "Central Core";
        else
            return "Unknown Consumer";
    }

    /// <summary>
    /// Visual effect for when a consumer takes damage
    /// </summary>
    private IEnumerator DamageFlashEffect(IEnergyConsumer consumer)
    {
        // Try to get the SpriteRenderer to flash
        SpriteRenderer spriteRenderer = null;

        if (consumer is MonoBehaviour mb)
        {
            spriteRenderer = mb.GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer != null)
        {
            Color originalColor = spriteRenderer.color;

            // Flash red briefly
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

    public void SupplyEnergyToTarget(IEnergyConsumer target, float amount) => target.SupplyEnergy(amount);
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
        if (consumer is CentralCore && consumer.GetEnergyPercentage() <= coreDeadEnergyThreshold)
            TriggerGameOver();
    }
    #endregion

    #region Consumer Management
    public void RegisterEnergyConsumer(IEnergyConsumer consumer)
    {
        if (energyConsumers.Contains(consumer)) return;

        energyConsumers.Add(consumer);
        InitializeConsumerEnergy(consumer);
    }

    public void UnregisterEnergyConsumer(IEnergyConsumer consumer) => energyConsumers.Remove(consumer);

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

    // Getters for enemy damage values
    public float GetEnemyDamageToTowers() => enemyDamageToTowers;
    public float GetEnemyDamageToCore() => enemyDamageToCore;
    public float GetDefaultEnemyDamage() => defaultEnemyDamage;
    #endregion

    #region Game Management
    public void TriggerGameOver()
    {
        OnGameOver?.Invoke();
        Debug.Log("Game Over - Central Core energy depleted!");
    }
    #endregion

    #region Cleanup
    void CleanupEnergyManager()
    {
        StopAllCoroutines();
        energyConsumers?.Clear();
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

        // Supply range
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(player.transform.position, supplyRange);

        // Max supply distance
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(player.transform.position, maxSupplyDistance);

        // Supply connections
        Gizmos.color = Color.cyan;
        foreach (var consumer in energyConsumers)
        {
            if (consumer != null && IsPlayerInRange(consumer))
                Gizmos.DrawLine(player.transform.position, consumer.GetPosition());
        }
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
        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(energyManager.supplyBeamColor, 0f),
                new GradientColorKey(energyManager.supplyBeamColor, 1f)
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
        float energyToSupply = energyManager.supplyRate * Time.deltaTime;
        energyManager.SupplyEnergyToTarget(target, energyToSupply);
    }

    void UpdateVisualEffects()
    {
        float pulse = Mathf.Sin(Time.time * 10f) * 0.3f + 0.7f;
        Color beamColor = energyManager.supplyBeamColor;
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
// Interface for energy consumers
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

// Energy UI Component
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