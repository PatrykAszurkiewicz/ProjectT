using UnityEngine;

public class CoreRepairSystems : MonoBehaviour
{
    public float regenerationRate = 0.5f;
    public float activationDelay = 30f;

    [Header("Visual Settings")]
    public Color regenAuraColor = new Color(0.2f, 1f, 0.3f, 0.5f); // Bright green
    public float auraPulseSpeed = 2f;
    public float auraScaleMultiplier = 0.45f;

    private CentralCore core;
    private float timeSinceLastDamage = 0f;
    private bool isRegenerating = false;
    private float originalDecayRate = 0.7f;
    private bool isInitialized = false;

    // Visual components
    private GameObject auraObject;
    private SpriteRenderer auraRenderer;

    void Awake()
    {
        core = GetComponent<CentralCore>();
        if (core == null)
        {
            Debug.LogError("[CORE_REPAIR] NO CORE FOUND!");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        if (EnergyManager.Instance == null)
        {
            Debug.LogError("[CORE_REPAIR] EnergyManager not found!");
            return;
        }

        originalDecayRate = EnergyManager.Instance.coreEnergyDecayRate;
        core.OnDamageTaken += OnCoreDamaged;
        isInitialized = true;

        CreateRegenAura();

        //Debug.Log($"[CORE_REPAIR] Initialized: {regenerationRate} HP/sec after {activationDelay}s delay");
    }

    void CreateRegenAura()
    {
        auraObject = new GameObject("RegenAura");
        auraObject.transform.SetParent(transform);
        auraObject.transform.localPosition = Vector3.zero;

        auraRenderer = auraObject.AddComponent<SpriteRenderer>();
        auraRenderer.sprite = CreateAuraSprite();
        auraRenderer.color = regenAuraColor;
        auraRenderer.sortingOrder = core.GetComponent<SpriteRenderer>().sortingOrder + 1;

        float coreSize = core.coreSize;
        auraObject.transform.localScale = Vector3.one * coreSize * auraScaleMultiplier;

        // Start hidden
        auraObject.SetActive(false);
    }

    Sprite CreateAuraSprite()
    {
        int size = 128;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float outerRadius = size * 0.45f;
        float innerRadius = size * 0.30f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 pos = new Vector2(x, y);
                float distance = Vector2.Distance(pos, center);

                if (distance <= outerRadius && distance >= innerRadius)
                {
                    // Smooth gradient ring
                    float alpha = 1f - Mathf.Abs(distance - (innerRadius + outerRadius) * 0.5f) / ((outerRadius - innerRadius) * 0.5f);
                    alpha = Mathf.Clamp01(alpha);
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    colors[y * size + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    void Update()
    {
        if (!isInitialized || core == null || core.IsDestroyed()) return;

        timeSinceLastDamage += Time.deltaTime;

        // Hysteresis thresholds to prevent flickering
        if (!isRegenerating && timeSinceLastDamage >= activationDelay && core.GetEnergyPercentage() < 0.99f)
        {
            isRegenerating = true;
            EnergyManager.Instance.coreEnergyDecayRate = -regenerationRate;

            // Show aura
            if (auraObject != null)
                auraObject.SetActive(true);

            //Debug.Log($"[CORE_REPAIR] Regeneration started at {core.GetEnergy():F1} HP");
        }
        else if (isRegenerating && core.GetEnergyPercentage() >= 1.0f)
        {
            isRegenerating = false;
            EnergyManager.Instance.coreEnergyDecayRate = originalDecayRate;

            // Hide aura
            if (auraObject != null)
                auraObject.SetActive(false);

            //Debug.Log("[CORE_REPAIR] Regeneration stopped (full health)");
        }

        // Update aura pulse effect
        if (isRegenerating && auraRenderer != null)
        {
            UpdateAuraPulse();
        }
    }

    void UpdateAuraPulse()
    {
        float time = Time.time * auraPulseSpeed;
        float pulse = Mathf.Sin(time) * 0.5f + 0.5f; // 0 to 1

        // Pulsate color alpha
        Color currentColor = regenAuraColor;
        currentColor.a = regenAuraColor.a * (0.3f + pulse * 0.7f);
        auraRenderer.color = currentColor;

        // Pulsate size slightly
        float scale = core.coreSize * auraScaleMultiplier * (1f + pulse * 0.15f);
        auraObject.transform.localScale = Vector3.one * scale;

        // Rotate slowly
        auraObject.transform.Rotate(0, 0, 20f * Time.deltaTime);
    }

    void OnCoreDamaged(float damage, GameObject source)
    {
        timeSinceLastDamage = 0f;

        if (isRegenerating)
        {
            isRegenerating = false;
            EnergyManager.Instance.coreEnergyDecayRate = originalDecayRate;

            // Hide aura when interrupted
            if (auraObject != null)
                auraObject.SetActive(false);

            //Debug.Log($"[CORE_REPAIR] Regeneration interrupted by {damage:F1} damage");
        }
    }

    void OnDestroy()
    {
        if (core != null)
            core.OnDamageTaken -= OnCoreDamaged;

        if (isRegenerating && EnergyManager.Instance != null)
            EnergyManager.Instance.coreEnergyDecayRate = originalDecayRate;

        if (auraObject != null)
            Destroy(auraObject);
    }
}