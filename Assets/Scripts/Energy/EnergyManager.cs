using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnergyManager : MonoBehaviour
{
    private static bool isApplicationQuitting = false;
    [Header("Global Energy Settings")]
    public float globalEnergyDecayRate = 1f; // Energy lost per second globally
    public float supplyRange = 2f; // Range at which player can supply energy
    public float supplyRate = 10f; // Energy supplied per second
    public float maxSupplyDistance = 0.5f; // Maximum distance for supply interaction

    [Header("Tower Energy Settings")]
    public float towerMaxEnergy = 100f;
    public float towerEnergyDecayRate = 0.7f; // Energy lost per second for towers
    public float towerCriticalEnergyThreshold = 0.2f; // TODO 20% - towers become less effective
    public float towerDeadEnergyThreshold = 0.05f; // 5% - towers stop functioning

    [Header("Central Core Energy Settings")]
    public float coreMaxEnergy = 100f;
    public float coreEnergyDecayRate = 0.7f; // Energy lost per second for core
    public float coreCriticalEnergyThreshold = 0.3f; // 30% - visual warnings
    public float coreDeadEnergyThreshold = 0.1f; // TODO add Game Over condition

    [Header("Common Visual Settings")]
    public Color normalColor = Color.lightSteelBlue;
    //public Color normalColor = new Color(0f, 0.5f, 1f, 1f); // Electric Blue

    public Color lowEnergyColor = Color.yellow;
    public Color criticalEnergyColor = Color.red;
    public Color depletedEnergyColor = Color.gray;

    [Header("Supply Visual Settings")]
    public Color supplyBeamColor = Color.cyan;
    public float supplyBeamWidth = 0.1f;
    public LayerMask supplyTargetMask = -1;

    // Private variables
    private static EnergyManager instance;
    private List<IEnergyConsumer> energyConsumers = new List<IEnergyConsumer>();
    private GameObject player;
    private Camera mainCamera;
    private LineRenderer supplyBeam;
    private GameObject supplyBeamContainer;
    private IEnergyConsumer currentSupplyTarget;
    private bool isSupplying = false;

    // Events
    public System.Action<float> OnGlobalEnergyChanged;
    public System.Action OnGameOver;

    public static EnergyManager Instance
    {
        get
        {
            // Don't create new instances if application is quitting
            if (isApplicationQuitting)
            {
                return null;
            }

            if (instance == null)
            {
                instance = Object.FindFirstObjectByType<EnergyManager>();

                if (instance == null)
                {
                    GameObject go = new GameObject("EnergyManager");
                    instance = go.AddComponent<EnergyManager>();
                }
            }
            return instance;
        }
    }

    //void OnApplicationQuit()
    //{
    //    CleanupEnergyManager();
    //}
    void OnDisable()
    {
        CleanupEnergyManager();
    }
    void CleanupEnergyManager()
    {
        // Stop all coroutines
        StopAllCoroutines();
        // Clear energy consumers list
        if (energyConsumers != null)
        {
            energyConsumers.Clear();
        }
        // Cleanup supply beam
        if (supplyBeam != null)
        {
            supplyBeam.enabled = false;
        }

        if (supplyBeamContainer != null)
        {
            DestroyImmediate(supplyBeamContainer);
            supplyBeamContainer = null;
        }
        if (instance == this)
        {
            instance = null;
        }
    }


    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        StopAllCoroutines();

        if (energyConsumers != null)
        {
            energyConsumers.Clear();
        }
    }


    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        SetupSupplyBeam();
    }
    void OnApplicationQuit()
    {
        isApplicationQuitting = true;
        instance = null;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void InitializeOnLoad()
    {
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            isApplicationQuitting = true;
            instance = null;
        }
        else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
        {
            isApplicationQuitting = false;
            instance = null;
        }
    }
#endif

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
        mainCamera = Camera.main;
        if (mainCamera == null)
            mainCamera = Object.FindFirstObjectByType<Camera>();

        StartCoroutine(EnergyDecayCoroutine());
    }

    void Update()
    {
        HandleSupplyInput();
        UpdateSupplyBeam();
    }

    void SetupSupplyBeam()
    {
        supplyBeamContainer = new GameObject("SupplyBeam");
        supplyBeamContainer.transform.SetParent(transform);

        supplyBeam = supplyBeamContainer.AddComponent<LineRenderer>();
        supplyBeam.material = new Material(Shader.Find("Sprites/Default"));
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(supplyBeamColor, 0f), new GradientColorKey(supplyBeamColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        supplyBeam.colorGradient = grad;
        supplyBeam.startWidth = supplyBeamWidth;
        supplyBeam.endWidth = supplyBeamWidth;
        supplyBeam.positionCount = 2;
        supplyBeam.useWorldSpace = true;
        supplyBeam.sortingOrder = 100; // Above everything else
        supplyBeam.enabled = false;
    }

    void HandleSupplyInput()
    {
        if (player == null) return;

        bool supplyInput = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);
        if (supplyInput)
        {
            Vector3 inputPosition = Vector3.zero;

            if (Input.GetMouseButton(0))
            {
                inputPosition = mainCamera.ScreenToWorldPoint(Input.mousePosition);
                inputPosition.z = 0;
            }
            else if (Input.GetKey(KeyCode.Space))
            {
                IEnergyConsumer closest = GetClosestEnergyConsumer(player.transform.position);
                if (closest != null)
                {
                    inputPosition = closest.GetPosition();
                }
            }

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

    void StartSupplying(IEnergyConsumer target)
    {
        currentSupplyTarget = target;
        isSupplying = true;
        supplyBeam.enabled = true;
    }

    void StopSupplying()
    {
        currentSupplyTarget = null;
        isSupplying = false;
        supplyBeam.enabled = false;
    }

    void UpdateSupplyBeam()
    {
        if (!isSupplying || currentSupplyTarget == null || player == null)
        {
            supplyBeam.enabled = false;
            return;
        }

        supplyBeam.SetPosition(0, player.transform.position);
        supplyBeam.SetPosition(1, currentSupplyTarget.GetPosition());

        float energyToSupply = supplyRate * Time.deltaTime;
        currentSupplyTarget.SupplyEnergy(energyToSupply);

        float pulse = Mathf.Sin(Time.time * 10f) * 0.3f + 0.7f;
        Color beamColor = supplyBeamColor;
        beamColor.a = pulse;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(beamColor, 0f), new GradientColorKey(beamColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        supplyBeam.colorGradient = grad;
    }

    IEnumerator EnergyDecayCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            for (int i = energyConsumers.Count - 1; i >= 0; i--)
            {
                if (energyConsumers[i] == null)
                {
                    energyConsumers.RemoveAt(i);
                    continue;
                }

                float decayAmount = GetDecayRate(energyConsumers[i]) * 0.1f;
                energyConsumers[i].ConsumeEnergy(decayAmount);

                // Check for game over condition if it's the central core
                if (energyConsumers[i] is CentralCore && energyConsumers[i].GetEnergyPercentage() <= coreDeadEnergyThreshold)
                {
                    TriggerGameOver();
                }
            }
        }
    }

    float GetDecayRate(IEnergyConsumer consumer)
    {
        if (consumer is CentralCore)
        {
            return coreEnergyDecayRate * globalEnergyDecayRate;
        }
        else if (consumer is Tower)
        {
            return towerEnergyDecayRate * globalEnergyDecayRate;
        }

        return globalEnergyDecayRate;
    }

    // Common energy visual methods
    public Color GetEnergyColor(IEnergyConsumer consumer)
    {
        if (consumer.IsEnergyDepleted())
            return depletedEnergyColor;
        else if (consumer.IsEnergyLow())
        {
            float criticalThreshold = GetCriticalThreshold(consumer);
            return Color.Lerp(criticalEnergyColor, lowEnergyColor, consumer.GetEnergyPercentage() / criticalThreshold);
        }
        else
            return normalColor;
    }

    public void UpdateConsumerVisuals(IEnergyConsumer consumer, SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = GetEnergyColor(consumer);
    }

    // Public methods
    public void RegisterEnergyConsumer(IEnergyConsumer consumer)
    {
        if (!energyConsumers.Contains(consumer))
        {
            energyConsumers.Add(consumer);

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
    }

    public void UnregisterEnergyConsumer(IEnergyConsumer consumer)
    {
        energyConsumers.Remove(consumer);
    }

    public float GetTowerCriticalThreshold()
    {
        return towerCriticalEnergyThreshold;
    }

    public float GetTowerDeadThreshold()
    {
        return towerDeadEnergyThreshold;
    }

    public float GetCoreCriticalThreshold()
    {
        return coreCriticalEnergyThreshold;
    }

    public float GetCoreDeadThreshold()
    {
        return coreDeadEnergyThreshold;
    }

    public float GetCriticalThreshold(IEnergyConsumer consumer)
    {
        if (consumer is CentralCore)
            return coreCriticalEnergyThreshold;
        else if (consumer is Tower)
            return towerCriticalEnergyThreshold;

        return 0.2f; // Default
    }

    public float GetDeadThreshold(IEnergyConsumer consumer)
    {
        if (consumer is CentralCore)
            return coreDeadEnergyThreshold;
        else if (consumer is Tower)
            return towerDeadEnergyThreshold;

        return 0.05f; // Default
    }

    public void TriggerGameOver()
    {
        OnGameOver?.Invoke();
        Debug.Log("Game Over - Central Core energy depleted!");
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
            {
                Gizmos.DrawLine(player.transform.position, consumer.GetPosition());
            }
        }
    }
}

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

// Energy UI Component (optional)
public class EnergyUI : MonoBehaviour
{
    [Header("UI References")]
    public UnityEngine.UI.Slider energySlider;
    public UnityEngine.UI.Text energyText;
    public UnityEngine.UI.Text statusText;

    private IEnergyConsumer trackedConsumer;

    public void SetTrackedConsumer(IEnergyConsumer consumer)
    {
        trackedConsumer = consumer;
    }

    void Update()
    {
        if (trackedConsumer == null) return;

        float energyPercentage = trackedConsumer.GetEnergyPercentage();

        if (energySlider != null)
        {
            energySlider.value = energyPercentage;
        }

        if (energyText != null)
        {
            energyText.text = $"{trackedConsumer.GetEnergy():F1}/{trackedConsumer.GetMaxEnergy():F1}";
        }

        if (statusText != null)
        {
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
}