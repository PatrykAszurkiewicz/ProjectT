using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ResourceDepositSpawner : MonoBehaviour
{
    public static ResourceDepositSpawner Instance { get; private set; }

    [Header("Spawn Settings")]
    public int energyDropsPerDeposit = 4;
    public int energyValuePerDrop = 15;
    public float depositRadius = 0.8f;
    public float minDistanceFromCore = 6f;
    public float minDistanceBetweenDeposits = 8f;
    public float mapRange = 15f;

    [Header("Visual Settings")]
    public Color depositMarkerColor = new Color(1f, 0.84f, 0f, 0.8f); // Gold color
    public float depositMarkerSize = 1.2f;
    public bool showDepositGlow = true;

    private List<GameObject> activeDeposits = new List<GameObject>();
    private Transform coreTransform;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        FindCore();
    }

    void FindCore()
    {
        var core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
        {
            coreTransform = core.transform;
        }
    }

    public void SpawnResourceDeposits(int count)
    {
        if (coreTransform == null)
        {
            FindCore();
            if (coreTransform == null)
            {
                Debug.LogError("[ResourceDeposit] Cannot find Core - cannot spawn deposits");
                return;
            }
        }

        int spawned = 0;
        int safetyCounter = 0;
        int maxAttempts = count * 30;

        while (spawned < count && safetyCounter < maxAttempts)
        {
            safetyCounter++;

            Vector2 randomPos = new Vector2(
                Random.Range(-mapRange, mapRange),
                Random.Range(-mapRange, mapRange)
            );

            // Check distance from core
            if (Vector2.Distance(randomPos, coreTransform.position) < minDistanceFromCore)
            {
                continue;
            }

            // Check distance from other deposits
            bool tooClose = false;
            foreach (var deposit in activeDeposits)
            {
                if (deposit != null && Vector2.Distance(randomPos, deposit.transform.position) < minDistanceBetweenDeposits)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose) continue;

            // Check for obstacles
            Collider2D hit = Physics2D.OverlapCircle(randomPos, depositRadius * 2f);
            if (hit != null && (hit.CompareTag("Obstacle") || hit.CompareTag("Tower")))
            {
                continue;
            }

            // Valid position found - create deposit
            CreateResourceDeposit(randomPos);
            spawned++;
        }

        //Debug.Log($"[ResourceDeposit] Spawned {spawned}/{count} resource deposits");
    }

    void CreateResourceDeposit(Vector3 position)
    {
        GameObject depositObj = new GameObject("ResourceDeposit");
        depositObj.transform.position = position;
        depositObj.transform.SetParent(transform);

        // Add visual marker
        var renderer = depositObj.AddComponent<SpriteRenderer>();
        renderer.sprite = CreateDepositMarkerSprite();
        renderer.color = depositMarkerColor;
        renderer.sortingOrder = 10;

        // Scale marker
        depositObj.transform.localScale = Vector3.one * depositMarkerSize;

        // Add glow effect if enabled
        if (showDepositGlow)
        {
            AddGlowEffect(depositObj);
        }

        // Spawn energy drops in a cluster around the deposit
        SpawnEnergyCluster(position);

        activeDeposits.Add(depositObj);

        // Auto-destroy marker after energy is collected
        StartCoroutine(MonitorAndDestroyMarker(depositObj, position));
    }

    void SpawnEnergyCluster(Vector3 centerPosition)
    {
        int stageIndex = GameOrchestrator.Instance?.CurrentStageIndex ?? 0;
        int scaledValue = StageEnergyScaling.EnemyDropValue(
            GameOrchestrator.Instance?.runConfig, stageIndex);

        for (int i = 0; i < energyDropsPerDeposit; i++)
        {
            float angle = (360f / energyDropsPerDeposit) * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * depositRadius,
                Mathf.Sin(angle) * depositRadius,
                0f
            );
            EnergyDropManager.TrySpawnEnergyDrop(centerPosition + offset, 1f, scaledValue);
        }
    }

    void AddGlowEffect(GameObject depositObj)
    {
        GameObject glowObj = new GameObject("DepositGlow");
        glowObj.transform.SetParent(depositObj.transform);
        glowObj.transform.localPosition = Vector3.zero;

        var glowRenderer = glowObj.AddComponent<SpriteRenderer>();
        glowRenderer.sprite = CreateDepositMarkerSprite();
        glowRenderer.color = new Color(depositMarkerColor.r, depositMarkerColor.g, depositMarkerColor.b, 0.3f);
        glowRenderer.sortingOrder = 9; // Behind main marker

        // Make glow larger
        glowObj.transform.localScale = Vector3.one * 2f;

        // Add pulsing animation
        var pulseEffect = glowObj.AddComponent<DepositGlowPulse>();
        pulseEffect.pulseSpeed = 2f;
        pulseEffect.minAlpha = 0.1f;
        pulseEffect.maxAlpha = 0.4f;
    }

    IEnumerator MonitorAndDestroyMarker(GameObject marker, Vector3 position)
    {
        // Wait a bit for energy drops to spawn
        yield return new WaitForSeconds(0.5f);

        // Check if energy drops still exist near this position
        while (marker != null)
        {
            bool hasEnergyDrops = CheckForEnergyDropsNearPosition(position);

            if (!hasEnergyDrops)
            {
                // All energy collected - fade out and destroy marker
                yield return StartCoroutine(FadeOutMarker(marker));
                activeDeposits.Remove(marker);
                Destroy(marker);
                yield break;
            }

            yield return new WaitForSeconds(1f);
        }
    }

    bool CheckForEnergyDropsNearPosition(Vector3 position)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(position, depositRadius * 2f);
        foreach (var col in colliders)
        {
            if (col.GetComponent<EnergyDrop>() != null)
            {
                return true;
            }
        }
        return false;
    }

    IEnumerator FadeOutMarker(GameObject marker)
    {
        if (marker == null) yield break;

        var renderers = marker.GetComponentsInChildren<SpriteRenderer>();
        float fadeTime = 1f;
        float elapsed = 0f;

        Color[] startColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                startColors[i] = renderers[i].color;
            }
        }

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeTime);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    Color color = startColors[i];
                    color.a = alpha * startColors[i].a;
                    renderers[i].color = color;
                }
            }

            yield return null;
        }
    }

    Sprite CreateDepositMarkerSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float outerRadius = size * 0.45f;
        float innerRadius = size * 0.25f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);

                if (distance <= innerRadius)
                {
                    // Solid center
                    colors[y * size + x] = Color.white;
                }
                else if (distance <= outerRadius)
                {
                    // Gradient ring
                    float t = (distance - innerRadius) / (outerRadius - innerRadius);
                    float alpha = 1f - t;
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

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}

public class DepositGlowPulse : MonoBehaviour
{
    public float pulseSpeed = 2f;
    public float minAlpha = 0.1f;
    public float maxAlpha = 0.4f;

    private SpriteRenderer spriteRenderer;
    private Color baseColor;
    private float pulseTimer = 0f;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
        }
    }

    void Update()
    {
        if (spriteRenderer == null) return;

        pulseTimer += Time.deltaTime * pulseSpeed;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, (Mathf.Sin(pulseTimer) + 1f) * 0.5f);

        Color color = baseColor;
        color.a = alpha;
        spriteRenderer.color = color;
    }
}
