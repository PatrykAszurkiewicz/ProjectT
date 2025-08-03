using System.Collections;
using UnityEngine;

public class CentralCore : MonoBehaviour, IEnergyConsumer, IDamageable
{
    #region Configuration
    [Header("Core Configuration")]
    public float maxEnergy = 100f;
    public float currentEnergy = 100f;
    public float coreSize = 2f;

    [Header("Animation")]
    public bool enableAnimation = true;
    public float animationSpeed = 0.1f;
    public int animationFrameCount = 16;
    public int spriteStartIndex = 0;

    [Header("Visual")]
    public Color normalColor = Color.white;
    public float lowEnergyThreshold = 0.3f;

    [Header("Energy")]
    public bool requiresEnergyToFunction = true;

    [Header("Energy Bar")]
    public EnergyBarSettings energyBarSettings = new EnergyBarSettings();

    [Header("Damage Settings")]
    public float armorReduction = 0f; // 0-1 range, reduces incoming damage
    public bool immuneToEnemyDamage = false;
    public float damageFlashDuration = 0.2f;
    public bool enableDamageEffects = true;
    public float criticalHealthShakeIntensity = 0.1f;

    [System.Serializable]
    public class EnergyBarSettings
    {
        public bool show = true;
        public float height = 0.15f;
        public float width = 1.5f;
        public float offset = 0.45f;
        public bool showText = true;
    }
    #endregion

    #region Core Components
    private SpriteRenderer spriteRenderer;
    private Sprite[] coreSprites;
    private Coroutine animationCoroutine;
    private EnergyBar energyBar;

    // State tracking
    private Vector3 originalScale;
    private Vector3 originalPosition;
    private Color originalColor;
    private float currentAnimationSpeed = -1f;
    private bool isEnergyDepleted, isEnergyLow;
    private bool isDestroyed = false;
    private Coroutine damageFlashCoroutine;
    private Coroutine shakeCoroutine;

    // Energy events
    public System.Action<float> OnEnergyChanged;
    public System.Action OnEnergyDepleted;
    public System.Action OnEnergyRestored;

    // Damage events
    public System.Action<float, GameObject> OnDamageTaken;
    public System.Action<GameObject> OnCoreDestroyed;
    public System.Action OnCoreEnteredCriticalState;
    public System.Action OnCoreExitedCriticalState;
    #endregion

    #region Unity Lifecycle
    void Awake() => InitializeComponents();
    void Start() => SetupCore();
    void Update() => UpdateCoreState();
    void OnDestroy() => Cleanup();
    #endregion

    #region Initialization
    void InitializeComponents()
    {
        gameObject.tag = "Core";
        // Ensure SpriteRenderer is properly added
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        spriteRenderer.sortingOrder = 0;
        spriteRenderer.sortingLayerName = "Default";

        originalScale = Vector3.one * coreSize;
        originalPosition = transform.position;
        transform.localScale = originalScale;
        originalColor = normalColor;

        LoadCoreSprites();
        StartAnimationIfEnabled();
    }

    void SetupCore()
    {
        RegisterWithEnergyManager();
        originalColor = spriteRenderer.color;
        SetupEnergyBar();
        UpdateVisualState();
    }

    void LoadCoreSprites()
    {
        coreSprites = Resources.LoadAll<Sprite>("Sprites/central_core_spritesheet2");
        if (coreSprites?.Length > 0)
            spriteRenderer.sprite = coreSprites[spriteStartIndex];
        else
            Debug.LogError("CentralCore: No sprites found!");
    }

    void StartAnimationIfEnabled()
    {
        if (enableAnimation && coreSprites?.Length > 0)
            StartCoreAnimation();
    }

    void RegisterWithEnergyManager() => EnergyManager.Instance?.RegisterEnergyConsumer(this);

    void SetupEnergyBar()
    {
        if (!energyBarSettings.show) return;

        energyBar = gameObject.AddComponent<EnergyBar>();
        energyBar.showEnergyBar = energyBarSettings.show;
        energyBar.energyBarHeight = energyBarSettings.height;
        energyBar.energyBarWidth = energyBarSettings.width;
        energyBar.energyBarOffset = energyBarSettings.offset;
        energyBar.showEnergyText = energyBarSettings.showText;

        // Set colors based on EnergyManager settings
        if (EnergyManager.Instance != null)
        {
            energyBar.SetColors(
                EnergyManager.Instance.normalColor,
                EnergyManager.Instance.lowEnergyColor,
                EnergyManager.Instance.criticalEnergyColor,
                EnergyManager.Instance.depletedEnergyColor
            );
        }

        energyBar.Initialize(this, spriteRenderer);
    }
    #endregion

    #region IDamageable Implementation
    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (immuneToEnemyDamage || isDestroyed) return false;

        // Apply armor reduction
        float actualDamage = damageAmount * (1f - armorReduction);

        // Store previous state for event checking
        bool wasCritical = IsInCriticalState();

        // Remove energy based on damage
        ConsumeEnergy(actualDamage);

        // Trigger damage effects
        if (enableDamageEffects)
        {
            StartDamageFlash();

            // Start shaking if in critical state
            if (IsInCriticalState())
            {
                StartCriticalShake();
            }
        }

        // Log damage
        string sourceName = damageSource != null ? damageSource.name : "Unknown";
        //Debug.Log($"Central Core took {actualDamage:F1} damage from {sourceName}. Energy: {currentEnergy:F1}/{maxEnergy:F1}");

        // Fire damage event
        OnDamageTaken?.Invoke(actualDamage, damageSource);

        // Check for critical state change
        if (!wasCritical && IsInCriticalState())
        {
            OnCoreEnteredCriticalState?.Invoke();
            //Debug.LogWarning("Central Core entered critical state!");
        }

        // Check if core is destroyed
        if (IsEnergyDepleted())
        {
            DestroyCore(damageSource);
            return true;
        }

        return false;
    }

    public bool CanTakeDamage()
    {
        return !immuneToEnemyDamage && !isDestroyed;
    }

    public float GetCurrentHealth()
    {
        return currentEnergy;
    }

    public float GetMaxHealth()
    {
        return maxEnergy;
    }

    public float GetHealthPercentage()
    {
        return GetEnergyPercentage();
    }

    public bool IsDestroyed()
    {
        return isDestroyed;
    }

    public bool IsInCriticalState()
    {
        return IsEnergyLow() && !IsEnergyDepleted();
    }

    private void DestroyCore(GameObject damageSource)
    {
        if (isDestroyed) return;

        isDestroyed = true;
        OnCoreDestroyed?.Invoke(damageSource);

        // Stop all effects
        StopAllEffects();

        // Trigger game over through energy manager
        EnergyManager.Instance?.TriggerGameOver();

        //Debug.LogError($"Central Core has been destroyed by {(damageSource != null ? damageSource.name : "unknown enemy")}!");
    }

    private void StartDamageFlash()
    {
        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
        }
        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;
        Color flashColor = EnergyManager.Instance?.damageFlashColor ?? Color.red;

        // Flash effect
        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(damageFlashDuration);
        spriteRenderer.color = originalColor;

        damageFlashCoroutine = null;
    }

    private void StartCriticalShake()
    {
        if (shakeCoroutine != null) return; // Already shaking

        shakeCoroutine = StartCoroutine(CriticalShakeCoroutine());
    }

    private IEnumerator CriticalShakeCoroutine()
    {
        while (IsInCriticalState() && !isDestroyed)
        {
            // Random shake offset
            Vector3 shakeOffset = Random.insideUnitCircle * criticalHealthShakeIntensity;
            transform.position = originalPosition + shakeOffset;

            yield return new WaitForSeconds(0.05f);
        }

        // Return to original position
        transform.position = originalPosition;
        shakeCoroutine = null;
    }

    private void StopAllEffects()
    {
        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
            damageFlashCoroutine = null;
        }

        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }

        // Return to original position
        transform.position = originalPosition;
    }
    #endregion

    #region Update Logic
    void UpdateCoreState()
    {
        if (isDestroyed) return;

        UpdateEnergyState();

        if (CanOperate())
            ProcessCoreOperations();
    }

    void UpdateEnergyState()
    {
        bool wasEnergyDepleted = isEnergyDepleted;
        bool wasEnergyLow = isEnergyLow;
        bool wasCritical = IsInCriticalState();

        isEnergyDepleted = IsEnergyDepleted();
        isEnergyLow = IsEnergyLow();

        if (isEnergyDepleted != wasEnergyDepleted || isEnergyLow != wasEnergyLow)
            UpdateEnergyDependentSystems();

        // Handle critical state changes
        bool isCritical = IsInCriticalState();
        if (wasCritical && !isCritical)
        {
            OnCoreExitedCriticalState?.Invoke();
            //Debug.Log("Central Core exited critical state.");
        }
    }

    void UpdateEnergyDependentSystems()
    {
        UpdateAnimationSpeed();
        UpdateVisualState();
    }

    void UpdateAnimationSpeed()
    {
        float newSpeed = CalculateAnimationSpeed();

        if (Mathf.Abs(newSpeed - currentAnimationSpeed) > 0.01f)
        {
            animationSpeed = newSpeed;
            currentAnimationSpeed = newSpeed;
            StartCoreAnimation();
        }
    }

    float CalculateAnimationSpeed()
    {
        if (isEnergyDepleted) return 0.4f;
        if (isEnergyLow) return 0.2f;
        return 0.1f;
    }

    void ProcessCoreOperations()
    {
        // Core-specific operations when energy is sufficient
        // TODO: Add core functionality here
    }

    bool CanOperate() => !requiresEnergyToFunction || !isEnergyDepleted;
    #endregion

    #region Animation System
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

    public void SetAnimationSettings(bool enable, float speed, int frameCount, int startIndex)
    {
        enableAnimation = enable;
        animationSpeed = speed;
        animationFrameCount = frameCount;
        spriteStartIndex = startIndex;

        if (enableAnimation && coreSprites?.Length > 0)
            StartCoreAnimation();
        else
            StopAnimation();
    }

    void StopAnimation()
    {
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
    }
    #endregion

    #region Visual Updates
    void UpdateVisualState()
    {
        UpdateEnergyVisuals();
        UpdateScaleEffect();
    }

    void UpdateEnergyVisuals()
    {
        if (spriteRenderer != null && EnergyManager.Instance != null)
            EnergyManager.Instance.UpdateConsumerVisuals(this, spriteRenderer);
    }

    void UpdateScaleEffect()
    {
        if (isEnergyDepleted)
        {
            float energyPercentage = GetEnergyPercentage();
            float deadThreshold = EnergyManager.Instance?.GetCoreDeadThreshold() ?? 0.1f;
            float scaleMultiplier = Mathf.Lerp(0.8f, 1f, energyPercentage / deadThreshold);
            transform.localScale = originalScale * scaleMultiplier;
        }
        else
        {
            transform.localScale = originalScale;
        }
    }
    #endregion

    #region IEnergyConsumer Implementation
    public void ConsumeEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisualState();

            if (currentEnergy <= 0f && previousEnergy > 0f)
                OnEnergyDepleted?.Invoke();
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
                OnEnergyRestored?.Invoke();
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

    public float GetEnergy() => currentEnergy;
    public float GetMaxEnergy() => maxEnergy;
    public float GetEnergyPercentage() => maxEnergy > 0 ? currentEnergy / maxEnergy : 0f;
    public Vector3 GetPosition() => transform.position;

    public bool IsEnergyDepleted() =>
        EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetCoreDeadThreshold();

    public bool IsEnergyLow() =>
        EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetCoreCriticalThreshold();
    #endregion

    #region Public Methods
    public void RestoreEnergy(float amount) => SupplyEnergy(amount);
    public bool HasEnergy() => currentEnergy > 0f;

    public void SetArmor(float newArmor) => armorReduction = Mathf.Clamp01(newArmor);
    public float GetArmor() => armorReduction;
    #endregion

    #region Cleanup
    void Cleanup()
    {
        EnergyManager.Instance?.UnregisterEnergyConsumer(this);
        StopAnimation();
        StopAllEffects();
        if (energyBar != null) Destroy(energyBar);
    }
    #endregion

#if UNITY_EDITOR
    #region Editor Gizmos
    void OnDrawGizmosSelected()
    {
        DrawEnergyVisualization();
        DrawStatusInfo();
    }

    void DrawEnergyVisualization()
    {
        Vector3 energyBarPos = transform.position + Vector3.up * 2.5f;
        float energyBarWidth = 2f;
        float energyBarHeight = 0.2f;

        // Background
        UnityEditor.Handles.color = Color.black;
        DrawRectangle(energyBarPos, energyBarWidth, energyBarHeight, Color.black, Color.white);

        // Energy bar
        float energyPercentage = GetEnergyPercentage();
        Color energyColor = EnergyManager.Instance?.GetEnergyColor(this) ?? Color.blue;
        float energyWidth = energyBarWidth * energyPercentage;
        Vector3 energyPos = energyBarPos + Vector3.left * (energyBarWidth * (1f - energyPercentage) * 0.5f);

        DrawRectangle(energyPos, energyWidth, energyBarHeight * 0.8f, energyColor, energyColor);

        // Energy text
        UnityEditor.Handles.Label(energyBarPos + Vector3.up * 0.5f,
            $"Core Energy: {currentEnergy:F1}/{maxEnergy:F1}");
    }

    void DrawStatusInfo()
    {
        string status = GetStatusText();
        UnityEditor.Handles.Label(transform.position + Vector3.down * 2f, $"Status: {status}");

        // Damage info
        Vector3 damagePos = transform.position + Vector3.down * 2.3f;
        string damageStatus = isDestroyed ? "DESTROYED" : "OPERATIONAL";
        UnityEditor.Handles.Label(damagePos, $"Damage Status: {damageStatus} | Armor: {armorReduction * 100f:F0}%");
    }

    string GetStatusText()
    {
        if (isDestroyed) return "DESTROYED";
        if (IsEnergyDepleted()) return "CRITICAL - DEPLETED";
        if (IsEnergyLow()) return "WARNING - LOW ENERGY";
        return "OPERATIONAL";
    }

    void DrawRectangle(Vector3 center, float width, float height, Color fill, Color outline)
    {
        Vector3[] points = {
            center + new Vector3(-width/2, -height/2),
            center + new Vector3(width/2, -height/2),
            center + new Vector3(width/2, height/2),
            center + new Vector3(-width/2, height/2)
        };

        UnityEditor.Handles.DrawSolidRectangleWithOutline(points, fill, outline);
    }
    #endregion
#endif
}