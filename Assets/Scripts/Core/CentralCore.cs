using System.Collections;
using UnityEngine;

public class CentralCore : MonoBehaviour, IEnergyConsumer, IDamageable
{
    [Header("Collision Settings")]
    public SpriteCollisionConfig collisionConfig = new SpriteCollisionConfig()
    {
        enableCollision = true,
        isTrigger = false,
        colliderType = SpriteCollisionConfig.ColliderType.Circle,
        paddingPercent = 0.1f // 10% padding for Core
    };
    private Collider2D spriteCollider;

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
    public float armorReduction = 0f;
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

    // Highlight system for repair functionality
    private bool isHighlighted = false;
    private Color highlightColor = Color.cyan;
    private bool isRegisteredWithEnergyManager = false;

    // Events
    public System.Action<float> OnEnergyChanged;
    public System.Action OnEnergyDepleted;
    public System.Action OnEnergyRestored;
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
        SetupSpriteCollision();
        SetupEnergyBar();
        UpdateVisualState();

        // TODO - remove testing energy resupplying
        //if (currentEnergy >= maxEnergy)
        //{
        //    currentEnergy = maxEnergy * 0.8f;
        //}
        if (transform.position.z != 0f)
        {
            Vector3 fixedPosition = new Vector3(transform.position.x, transform.position.y, 0f);
            transform.position = fixedPosition;
        }
    }
    void SetupSpriteCollision()
    {
        if (spriteRenderer?.sprite != null)
        {
            spriteCollider = SpriteCollisionManager.SetupCollision(gameObject, collisionConfig);
        }
        else
        {
            // Delay setup if sprite is not ready
            SpriteCollisionManager.SetupCollisionDelayed(this, collisionConfig);
        }
    }

    // TODO remove the helper methods
    //public Bounds GetSpriteBounds() => SpriteCollisionManager.GetSpriteBounds(gameObject);
    //public bool IsPointWithinSprite(Vector3 worldPoint) => SpriteCollisionManager.IsPointWithinSprite(gameObject, worldPoint);
    //public float GetCollisionRadius() => SpriteCollisionManager.GetCollisionRadius(gameObject);
    //public void UpdateCollisionSettings() => spriteCollider = SpriteCollisionManager.UpdateCollisionSettings(gameObject, collisionConfig);


    void LoadCoreSprites()
    {
        coreSprites = Resources.LoadAll<Sprite>("Sprites/central_core_spritesheet2");
        if (coreSprites?.Length > 0)
            spriteRenderer.sprite = coreSprites[spriteStartIndex];
    }

    void StartAnimationIfEnabled()
    {
        if (enableAnimation && coreSprites?.Length > 0)
            StartCoreAnimation();
    }

    void RegisterWithEnergyManager()
    {
        if (isRegisteredWithEnergyManager) return;

        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.RegisterEnergyConsumer(this);
            isRegisteredWithEnergyManager = true;
        }
    }

    void SetupEnergyBar()
    {
        if (!energyBarSettings.show) return;

        energyBar = gameObject.AddComponent<EnergyBar>();
        energyBar.showEnergyBar = energyBarSettings.show;
        energyBar.energyBarHeight = energyBarSettings.height;
        energyBar.energyBarWidth = energyBarSettings.width;
        energyBar.energyBarOffset = energyBarSettings.offset;
        energyBar.showEnergyText = energyBarSettings.showText;

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

    #region Highlight System for Repair
    public void SetHighlight(bool highlight)
    {
        if (isHighlighted == highlight) return;

        isHighlighted = highlight;

        if (highlight)
        {
            Color currentColor = spriteRenderer.color;
            spriteRenderer.color = Color.Lerp(currentColor, highlightColor, 0.6f);
        }
        else
        {
            UpdateEnergyVisuals();
        }
    }

    public bool IsHighlighted() => isHighlighted;

    private Color GetCurrentEnergyColor()
    {
        if (EnergyManager.Instance != null)
        {
            return EnergyManager.Instance.GetEnergyColor(this);
        }
        return normalColor;
    }
    #endregion

    #region IDamageable Implementation
    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (immuneToEnemyDamage || isDestroyed) return false;

        float actualDamage = damageAmount * (1f - armorReduction);
        bool wasCritical = IsInCriticalState();

        ConsumeEnergy(actualDamage);

        if (enableDamageEffects)
        {
            StartDamageFlash();
            if (IsInCriticalState())
            {
                StartCriticalShake();
            }
        }

        OnDamageTaken?.Invoke(actualDamage, damageSource);

        if (!wasCritical && IsInCriticalState())
        {
            OnCoreEnteredCriticalState?.Invoke();
        }

        if (IsEnergyDepleted())
        {
            DestroyCore(damageSource);
            return true;
        }

        return false;
    }

    public bool CanTakeDamage() => !immuneToEnemyDamage && !isDestroyed;
    public float GetCurrentHealth() => currentEnergy;
    public float GetMaxHealth() => maxEnergy;
    public float GetHealthPercentage() => GetEnergyPercentage();
    public bool IsDestroyed() => isDestroyed;
    public bool IsInCriticalState() => IsEnergyLow() && !IsEnergyDepleted();

    private void DestroyCore(GameObject damageSource)
    {
        if (isDestroyed) return;

        isDestroyed = true;
        OnCoreDestroyed?.Invoke(damageSource);
        StopAllEffects();

        // Only trigger game over if not already triggered
        if (EnergyManager.Instance != null && !EnergyManager.Instance.IsGameOver())
        {
            EnergyManager.Instance.TriggerGameOver();
        }
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

        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(damageFlashDuration);
        spriteRenderer.color = originalColor;

        damageFlashCoroutine = null;
    }

    private void StartCriticalShake()
    {
        if (shakeCoroutine != null) return;
        shakeCoroutine = StartCoroutine(CriticalShakeCoroutine());
    }

    private IEnumerator CriticalShakeCoroutine()
    {
        while (IsInCriticalState() && !isDestroyed)
        {
            Vector3 shakeOffset = Random.insideUnitCircle * criticalHealthShakeIntensity;
            transform.position = originalPosition + shakeOffset;
            yield return new WaitForSeconds(0.05f);
        }

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

        transform.position = originalPosition;
    }
    #endregion

    #region Update Logic
    void UpdateCoreState()
    {
        if (isDestroyed) return;

        if (!isRegisteredWithEnergyManager)
        {
            RegisterWithEnergyManager();
            if (!isRegisteredWithEnergyManager) return;
        }

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

        bool isCritical = IsInCriticalState();
        if (wasCritical && !isCritical)
        {
            OnCoreExitedCriticalState?.Invoke();
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
        // TODO Add Core-specific operations when energy is sufficient
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
        if (isHighlighted) return;

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
    #region Test Methods
    [ContextMenu("Test Set Energy to 50%")]
    void TestSetEnergyTo50Percent()
    {
        SetEnergy(maxEnergy * 0.5f);
    }

    [ContextMenu("Test Highlight On")]
    void TestHighlightOn()
    {
        SetHighlight(true);
    }

    [ContextMenu("Test Highlight Off")]
    void TestHighlightOff()
    {
        SetHighlight(false);
    }
    #endregion
#endif
}