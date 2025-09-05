using UnityEngine;
using System.Collections;

public class EnergyDrop : MonoBehaviour
{
    [Header("Drop Configuration")]
    public int energyValue = 10;
    public float lifetime = 30f;
    public float collectionRadius = 0.8f;

    [Header("Arc Motion")]
    public float magnetRange = 2.5f;
    public float arcSpeed = 5f;
    public float arcHeight = 1f;

    [Header("Glow Effect")]
    public bool enableGlowEffect = true;
    public float glowPulseSpeed = 3f;
    public float glowScaleMultiplier = 2.5f;
    public Color glowColor = new Color(0.3f, 0.8f, 1f, 0.5f);

    // Internal state
    private Transform playerTransform;
    private bool isCollected = false;
    private bool isMovingToPlayer = false;
    private Vector3 spawnPosition;
    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float arcProgress = 0f;
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D dropCollider;

    // Glow effect components
    private GameObject glowObject;
    private SpriteRenderer glowRenderer;
    private float glowTimer = 0f;

    void Awake()
    {
        SetupComponents();
        FindPlayer();
        InitializeVisuals();
        SetupGlowEffect();
    }

    void Start()
    {
        spawnPosition = transform.position;
        StartCoroutine(LifetimeTimer());
    }

    void Update()
    {
        if (isCollected) return;

        UpdateArcMotion();
        CheckPlayerCollection();
        UpdateGlowEffect();
    }

    void SetupComponents()
    {
        // Auto-setup sprite renderer
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = 15;

        // Auto-setup collider
        dropCollider = GetComponent<CircleCollider2D>();
        if (dropCollider == null)
        {
            dropCollider = gameObject.AddComponent<CircleCollider2D>();
        }
        dropCollider.isTrigger = true;
        dropCollider.radius = collectionRadius;

        // Set layer
        gameObject.layer = LayerMask.NameToLayer("Default");
    }

    void FindPlayer()
    {
        // Automatically find player using multiple methods
        var playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null)
        {
            playerTransform = playerMovement.transform;
            return;
        }

        // TODO remove fallback: find by tag
        var playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
            return;
        }

        // TODO remove fallback: find PlayerStats
        var playerStats = FindFirstObjectByType<PlayerStats>();
        if (playerStats != null)
        {
            playerTransform = playerStats.transform;
        }
    }

    void InitializeVisuals()
    {
        // Load sprite if not already assigned
        if (spriteRenderer != null && spriteRenderer.sprite == null)
        {
            // TODO remove try to load a default energy sprite from Resources
            Sprite energySprite = Resources.Load<Sprite>("Sprites/energy_orb");
            if (energySprite == null)
            {
                // TODO remove create a simple circle sprite as fallback
                spriteRenderer.sprite = CreateSimpleCircleSprite();
                spriteRenderer.color = Color.cyan;
            }
            else
            {
                spriteRenderer.sprite = energySprite;
            }
        }
    }

    void SetupGlowEffect()
    {
        if (!enableGlowEffect || spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        // Create glow object as child
        glowObject = new GameObject("EnergyGlow");
        glowObject.transform.SetParent(transform);
        glowObject.transform.localPosition = Vector3.zero;
        glowObject.transform.localRotation = Quaternion.identity;

        // Setup glow sprite renderer
        glowRenderer = glowObject.AddComponent<SpriteRenderer>();
        glowRenderer.sprite = spriteRenderer.sprite;
        glowRenderer.color = glowColor;
        glowRenderer.sortingLayerName = spriteRenderer.sortingLayerName;
        glowRenderer.sortingOrder = spriteRenderer.sortingOrder - 1; // Behind main sprite

        // Set initial glow scale
        glowObject.transform.localScale = Vector3.one * glowScaleMultiplier;
    }

    void UpdateGlowEffect()
    {
        if (!enableGlowEffect || glowRenderer == null || isMovingToPlayer)
            return;

        // Update glow timer
        glowTimer += Time.deltaTime * glowPulseSpeed;

        // Calculate pulsating alpha sine wave between 0.2 and 1.0
        float alpha = 0.2f + 0.8f * (0.5f + 0.5f * Mathf.Sin(glowTimer));

        // Apply alpha to glow color
        Color currentGlowColor = glowColor;
        currentGlowColor.a = alpha * glowColor.a;
        glowRenderer.color = currentGlowColor;
        float scaleVariation = 1f + 0.1f * Mathf.Sin(glowTimer * 1.5f);
        glowObject.transform.localScale = Vector3.one * glowScaleMultiplier * scaleVariation;
    }

    Sprite CreateSimpleCircleSprite()
    {
        int size = 32;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = distance <= radius ? 1f - (distance / radius) * 0.3f : 0f;
                colors[y * size + x] = new Color(0.3f, 0.8f, 1f, alpha);
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    void UpdateArcMotion()
    {
        if (playerTransform == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // Start moving to player when in range
        if (!isMovingToPlayer && distanceToPlayer <= magnetRange)
        {
            StartArcToPlayer();
        }

        if (isMovingToPlayer)
        {
            MoveInArcToPlayer();
        }
    }

    void StartArcToPlayer()
    {
        isMovingToPlayer = true;
        startPosition = transform.position;
        targetPosition = playerTransform.position;
        arcProgress = 0f;

        // Disable glow effect during movement to player
        if (enableGlowEffect && glowObject != null)
        {
            glowObject.SetActive(false);
        }
    }

    void MoveInArcToPlayer()
    {
        if (playerTransform == null) return;

        // Update target position to follow moving player
        targetPosition = playerTransform.position;

        // Move along arc
        arcProgress += arcSpeed * Time.deltaTime;

        if (arcProgress >= 1f)
        {
            CollectEnergy();
            return;
        }

        // Calculate position along arc
        Vector3 midPoint = (startPosition + targetPosition) / 2f;

        // Add arc height at the middle of the motion
        float arcHeightMultiplier = Mathf.Sin(arcProgress * Mathf.PI);
        Vector3 arcOffset = Vector3.up * arcHeight * arcHeightMultiplier;

        // Linear interpolation between start and target with arc height
        Vector3 linearPosition = Vector3.Lerp(startPosition, targetPosition, arcProgress);
        transform.position = linearPosition + arcOffset;

        // Add rotation
        transform.Rotate(0, 0, 360f * Time.deltaTime);
    }

    void CheckPlayerCollection()
    {
        if (playerTransform == null) return;

        float distance = Vector3.Distance(transform.position, playerTransform.position);
        if (distance <= collectionRadius)
        {
            CollectEnergy();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isCollected) return;

        // Check multiple ways to identify player
        if (other.CompareTag("Player") ||
            other.GetComponent<PlayerMovement>() != null ||
            other.GetComponent<PlayerStats>() != null)
        {
            CollectEnergy();
        }
    }

    public void CollectEnergy()
    {
        if (isCollected) return;
        isCollected = true;

        // Give energy to player through EnergyManager
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.GivePlayerEnergy(energyValue);
        }

        // Auto-register collection with player collector if it exists
        var playerCollector = playerTransform?.GetComponent<PlayerEnergyCollector>();
        if (playerCollector != null)
        {
            playerCollector.OnEnergyDropCollected(energyValue);
        }

        // Play collection sound
        PlayCollectionSound();

        // Collection effect and destroy
        StartCoroutine(CollectionEffect());
    }

    void PlayCollectionSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.resourceDropCollection, transform.position);
        }
    }

    IEnumerator CollectionEffect()
    {
        if (dropCollider != null) dropCollider.enabled = false;

        // Disable glow effect during collection
        if (glowObject != null)
        {
            glowObject.SetActive(false);
        }

        // Disintegration effect
        float duration = 0.4f;
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;
        Color startColor = spriteRenderer.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Shrink with accelerating curve
            float scaleT = 1f - (t * t);
            transform.localScale = startScale * scaleT;

            // Fade out with smooth curve
            if (spriteRenderer != null)
            {
                Color color = startColor;
                color.a = Mathf.Lerp(1f, 0f, t * t);
                spriteRenderer.color = color;
            }

            // Spin faster as it disintegrates
            transform.Rotate(0, 0, 720f * t * Time.deltaTime);

            yield return null;
        }

        Destroy(gameObject);
    }

    IEnumerator LifetimeTimer()
    {
        yield return new WaitForSeconds(lifetime);
        if (!isCollected)
        {
            StartCoroutine(FadeOutAndDestroy());
        }
    }

    IEnumerator FadeOutAndDestroy()
    {
        float fadeTime = 2f;
        float elapsed = 0f;
        Color startColor = spriteRenderer.color;
        Color glowStartColor = glowRenderer != null ? glowRenderer.color : Color.clear;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float fadeProgress = elapsed / fadeTime;

            // Fade main sprite
            if (spriteRenderer != null)
            {
                Color color = startColor;
                color.a = Mathf.Lerp(1f, 0f, fadeProgress);
                spriteRenderer.color = color;
            }

            // Fade glow sprite
            if (glowRenderer != null)
            {
                Color glowColor = glowStartColor;
                glowColor.a = Mathf.Lerp(glowStartColor.a, 0f, fadeProgress);
                glowRenderer.color = glowColor;
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    // Public interface
    public void SetEnergyValue(int value)
    {
        energyValue = Mathf.Max(1, value);
    }

    public void SetLifetime(float time)
    {
        lifetime = Mathf.Max(1f, time);
    }

    public void SetGlowEnabled(bool enabled)
    {
        enableGlowEffect = enabled;
        if (glowObject != null)
        {
            glowObject.SetActive(enabled && !isMovingToPlayer);
        }
    }

    public static GameObject CreateEnergyDrop(Vector3 position, int energyValue = 10)
    {
        GameObject drop = new GameObject("EnergyDrop");
        drop.transform.position = position;

        var dropComponent = drop.AddComponent<EnergyDrop>();
        dropComponent.energyValue = energyValue;

        return drop;
    }
}