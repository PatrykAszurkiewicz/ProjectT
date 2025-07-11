using System.Collections;
using UnityEngine;

public class CentralCore : MonoBehaviour, IEnergyConsumer
{
    [Header("Core Configuration")]
    public float maxEnergy = 100f;
    public float currentEnergy = 100f;
    public float coreSize = 2f;

    [Header("Animation Settings")]
    public bool enableAnimation = true;
    public float animationSpeed = 0.1f;
    public int animationFrameCount = 16;
    public int spriteStartIndex = 0;

    [Header("Visual Settings")]
    public Color normalColor = Color.white;
    public float lowEnergyThreshold = 0.3f;

    [Header("Energy Settings")]
    public bool requiresEnergyToFunction = true;

    [Header("Energy Bar Settings")]
    public bool showEnergyBar = true;
    public float energyBarHeight = 0.15f;
    public float energyBarWidth = 1.5f;
    public float energyBarOffset = 0.45f;
    public bool showEnergyText = true;

    // Private variables
    private SpriteRenderer spriteRenderer;
    private Sprite[] coreSprites;
    private Coroutine animationCoroutine;
    private float currentAnimationSpeed = -1f;
    private Vector3 originalScale;
    private Color originalColor;
    private EnergyBar energyBar;

    // Energy state tracking
    private bool isEnergyDepleted = false;
    private bool isEnergyLow = false;

    // Energy events
    public System.Action<float> OnEnergyChanged;
    public System.Action OnEnergyDepleted;
    public System.Action OnEnergyRestored;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        spriteRenderer.sortingOrder = 0;

        originalScale = Vector3.one * coreSize;
        transform.localScale = originalScale;
        originalColor = normalColor;

        LoadCoreSprites();

        if (enableAnimation && coreSprites != null && coreSprites.Length > 0)
        {
            StartCoreAnimation();
        }
    }

    void Start()
    {
        // Register with energy manager
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.RegisterEnergyConsumer(this);
        }

        // Store original color
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        // Setup energy bar
        SetupEnergyBar();

        // Initialize visual state
        UpdateVisualState();
    }

    void Update()
    {
        UpdateEnergyState();

        if (CanOperate())
        {
            // Core-specific operations when energy is sufficient
            ProcessCoreOperations();
        }
    }

    void LoadCoreSprites()
    {
        coreSprites = Resources.LoadAll<Sprite>("Sprites/central_core_spritesheet2");
        if (coreSprites == null || coreSprites.Length == 0)
        {
            Debug.LogError("CentralCore: No sprites found!");
            return;
        }
        spriteRenderer.sprite = coreSprites[spriteStartIndex];
    }

    void StartCoreAnimation()
    {
        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);

        animationCoroutine = StartCoroutine(
            Utilities.AnimateSpritePingPong(
                spriteRenderer,
                coreSprites,
                enableAnimation,
                animationFrameCount,
                spriteStartIndex,
                animationSpeed
            )
        );
    }

    void SetupEnergyBar()
    {
        if (!showEnergyBar) return;

        // Add EnergyBar component
        energyBar = gameObject.AddComponent<EnergyBar>();
        energyBar.showEnergyBar = showEnergyBar;
        energyBar.energyBarHeight = energyBarHeight;
        energyBar.energyBarWidth = energyBarWidth;
        energyBar.energyBarOffset = energyBarOffset;
        energyBar.showEnergyText = showEnergyText;

        // Set colors based on EnergyManager settings
        energyBar.SetColors(
            EnergyManager.Instance.normalColor,
            EnergyManager.Instance.lowEnergyColor,
            EnergyManager.Instance.criticalEnergyColor,
            EnergyManager.Instance.depletedEnergyColor
        );

        energyBar.Initialize(this, spriteRenderer);
    }

    void UpdateEnergyState()
    {
        bool wasEnergyDepleted = isEnergyDepleted;
        bool wasEnergyLow = isEnergyLow;

        isEnergyDepleted = IsEnergyDepleted();
        isEnergyLow = IsEnergyLow();

        if (isEnergyDepleted != wasEnergyDepleted || isEnergyLow != wasEnergyLow)
        {
            UpdateEnergyDependentSystems();
        }
    }

    void UpdateEnergyDependentSystems()
    {
        // Update animation speed based on energy level
        float energyPercentage = GetEnergyPercentage();
        float newSpeed = isEnergyLow ? 0.2f : 0.1f;
        // Slower animation when energy is very low
        if (isEnergyDepleted)
        {
            newSpeed = 0.4f;
        }

        if (Mathf.Abs(newSpeed - currentAnimationSpeed) > 0.01f)
        {
            animationSpeed = newSpeed;
            currentAnimationSpeed = newSpeed;
            StartCoreAnimation();
        }
    }

    void UpdateVisualState()
    {
        if (spriteRenderer == null || EnergyManager.Instance == null) return;
        // Use EnergyManager's common visual update method
        EnergyManager.Instance.UpdateConsumerVisuals(this, spriteRenderer);
        // Core-specific visual effects
        float energyPercentage = GetEnergyPercentage();
        // Scale effect based on energy (optional)
        if (isEnergyDepleted)
        {
            float scaleMultiplier = Mathf.Lerp(0.8f, 1f, energyPercentage / EnergyManager.Instance.GetCoreDeadThreshold());
            transform.localScale = originalScale * scaleMultiplier;
        }
        else
        {
            transform.localScale = originalScale;
        }
    }

    void ProcessCoreOperations()
    {
        // TODO Add core-specific operations that require energy
    }

    bool CanOperate()
    {
        return !requiresEnergyToFunction || !isEnergyDepleted;
    }

    // IEnergyConsumer implementation
    public void ConsumeEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisualState();

            if (currentEnergy <= 0f && previousEnergy > 0f)
            {
                OnEnergyDepleted?.Invoke();
            }
        }
    }

    public void SupplyEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisualState();

            if (previousEnergy <= 0f && currentEnergy > 0f)
            {
                OnEnergyRestored?.Invoke();
            }
        }
    }

    public void SetEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Clamp(amount, 0f, maxEnergy);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisualState();
        }
    }

    public void SetMaxEnergy(float amount)
    {
        maxEnergy = amount;
        currentEnergy = Mathf.Min(currentEnergy, maxEnergy);
        UpdateVisualState();
    }

    public float GetEnergy()
    {
        return currentEnergy;
    }

    public float GetMaxEnergy()
    {
        return maxEnergy;
    }

    public float GetEnergyPercentage()
    {
        return maxEnergy > 0 ? currentEnergy / maxEnergy : 0f;
    }

    public bool IsEnergyDepleted()
    {
        if (EnergyManager.Instance == null) return false; // ADD THIS LINE
        return GetEnergyPercentage() <= EnergyManager.Instance.GetCoreDeadThreshold();
    }

    public bool IsEnergyLow()
    {
        if (EnergyManager.Instance == null) return false; // ADD THIS LINE
        return GetEnergyPercentage() <= EnergyManager.Instance.GetCoreCriticalThreshold();
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }

    // Public methods for external access
    public void RestoreEnergy(float amount)
    {
        SupplyEnergy(amount);
    }

    public bool HasEnergy()
    {
        return currentEnergy > 0f;
    }

    public void SetAnimationSettings(bool enable, float speed, int frameCount, int startIndex)
    {
        enableAnimation = enable;
        animationSpeed = speed;
        animationFrameCount = frameCount;
        spriteStartIndex = startIndex;

        if (enableAnimation && coreSprites != null && coreSprites.Length > 0)
        {
            StartCoreAnimation();
        }
        else if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
    }

    void OnDestroy()
    {
        // Unregister from energy manager
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.UnregisterEnergyConsumer(this);
        }

        // Stop animation
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }

        // Cleanup energy bar
        if (energyBar != null)
        {
            Destroy(energyBar);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Draw energy visualization
        Vector3 energyBarPos = transform.position + Vector3.up * 2.5f;
        float energyBarWidth = 2f;
        float energyBarHeight = 0.2f;

        // Background bar
        UnityEditor.Handles.color = Color.black;
        UnityEditor.Handles.DrawSolidRectangleWithOutline(
            new Vector3[] {
                energyBarPos + new Vector3(-energyBarWidth/2, -energyBarHeight/2),
                energyBarPos + new Vector3(energyBarWidth/2, -energyBarHeight/2),
                energyBarPos + new Vector3(energyBarWidth/2, energyBarHeight/2),
                energyBarPos + new Vector3(-energyBarWidth/2, energyBarHeight/2)
            },
            Color.black, Color.white
        );

        // Energy bar
        float energyPercentage = GetEnergyPercentage();
        Color energyColor = EnergyManager.Instance != null ? EnergyManager.Instance.GetEnergyColor(this) : Color.blue;

        UnityEditor.Handles.color = energyColor;
        float energyWidth = energyBarWidth * energyPercentage;
        Vector3 energyPos = energyBarPos + Vector3.left * (energyBarWidth * (1f - energyPercentage) * 0.5f);

        UnityEditor.Handles.DrawSolidRectangleWithOutline(
            new Vector3[] {
                energyPos + new Vector3(-energyWidth/2, -energyBarHeight/2 * 0.8f),
                energyPos + new Vector3(energyWidth/2, -energyBarHeight/2 * 0.8f),
                energyPos + new Vector3(energyWidth/2, energyBarHeight/2 * 0.8f),
                energyPos + new Vector3(-energyWidth/2, energyBarHeight/2 * 0.8f)
            },
            energyColor, energyColor
        );

        // Energy text
        UnityEditor.Handles.Label(energyBarPos + Vector3.up * 0.5f, $"Core Energy: {currentEnergy:F1}/{maxEnergy:F1}");

        // Core status
        string status = "OPERATIONAL";
        if (IsEnergyDepleted())
            status = "CRITICAL - DEPLETED";
        else if (IsEnergyLow())
            status = "WARNING - LOW ENERGY";

        UnityEditor.Handles.Label(transform.position + Vector3.down * 2f, $"Status: {status}");
    }
#endif
}