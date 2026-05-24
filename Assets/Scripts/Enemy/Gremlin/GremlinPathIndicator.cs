using UnityEngine;
using System.Collections.Generic;

public class GremlinPathIndicator : MonoBehaviour
{
    [Header("Path Settings")]
    [Tooltip("Distance between each footprint")]
    public float footprintSpacing = 1.5f;

    [Tooltip("Maximum distance to show path")]
    public float maxPathDistance = 100f;

    [Tooltip("Scale of footprint sprites")]
    public float footprintScale = 0.3f;

    [Tooltip("Opacity of footprints (0-1)")]
    [Range(0f, 1f)]
    public float footprintAlpha = 0.65f;

    [Tooltip("How often to update the path (seconds)")]
    public float updateInterval = 0.3f;

    [Tooltip("Only update path if gremlin moved this far")]
    public float minimumUpdateDistance = 1.5f; // Only update if gremlin moved significantly

    [Tooltip("Alternate foot orientation for realism")]
    public bool alternateFootOrientation = true;

    [Header("Animation")]
    public bool fadeInFootprints = false;
    public float fadeInDuration = 0.3f;

    public bool fadeOutOldFootprints = true;
    public float footprintLifetime = 0.7f;
    public float fadeOutDuration = 0.3f;

    [Header("Debug")]
    public bool enableDebugLogs = false;
    public bool showDebugGizmos = false;

    private Transform playerTransform;
    private List<GameObject> footprintObjects = new List<GameObject>();
    private Sprite footprintSprite;
    private float updateTimer;
    private bool isLeftFoot = true;
    private Vector3 lastGremlinPosition = Vector3.zero;
    private bool hasGremlinPosition = false;

    void Start()
    {
        if (enableDebugLogs) Debug.Log("[PathIndicator] Starting initialization");
        FindPlayer();
        LoadFootprintSprite();
        updateTimer = updateInterval;

        if (enableDebugLogs)
        {
            Debug.Log($"[PathIndicator] Initialized - Player: {(playerTransform != null ? "Found" : "NOT FOUND")}, Sprite: {(footprintSprite != null ? "Loaded" : "NOT LOADED")}");
        }
    }

    void FindPlayer()
    {
        var playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null)
        {
            playerTransform = playerMovement.transform;
        }
        else
        {
            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                playerTransform = playerObject.transform;
            }
        }
    }

    void LoadFootprintSprite()
    {
        footprintSprite = Resources.Load<Sprite>("Sprites/Feet");

        if (footprintSprite == null)
        {
            if (enableDebugLogs)
                Debug.LogWarning("[PathIndicator] Footprint sprite not found at Resources/Sprites/Feet. Trying Feet.png path...");

            // Try alternative path
            Texture2D texture = Resources.Load<Texture2D>("Sprites/Feet");
            if (texture != null)
            {
                footprintSprite = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    Vector2.one * 0.5f, 100f);
                if (enableDebugLogs)
                    Debug.Log("[PathIndicator] Successfully created sprite from Feet texture");
            }
            else
            {
                if (enableDebugLogs)
                    Debug.LogWarning("[PathIndicator] Creating fallback footprint sprite");
                footprintSprite = CreateFallbackFootprint();
            }
        }
        else
        {
            if (enableDebugLogs)
                Debug.Log("[PathIndicator] Footprint sprite loaded successfully from Resources/Sprites/Feet");
        }
    }

    Sprite CreateFallbackFootprint()
    {
        int size = 32;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];

        // Create a simple foot shape
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                // Simple oval shape for foot
                float dx = (x - size * 0.5f) / (size * 0.3f);
                float dy = (y - size * 0.3f) / (size * 0.5f);
                float dist = dx * dx + dy * dy;

                if (dist < 1f)
                {
                    colors[y * size + x] = new Color(1f, 1f, 1f, 0.8f);
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
        updateTimer -= Time.deltaTime;

        if (updateTimer <= 0f)
        {
            updateTimer = updateInterval;
            UpdatePath();
        }

        // Continuously check if we should clear the path
        // (in case gremlin died or disappeared)
        if (footprintObjects.Count > 0)
        {
            GameObject gremlin = FindClosestGremlin();
            if (gremlin == null)
            {
                // No gremlin exists, clear path immediately
                ClearPath();
                hasGremlinPosition = false;
            }
        }
    }

    void UpdatePath()
    {
        if (playerTransform == null)
        {
            if (enableDebugLogs) Debug.Log("[PathIndicator] No player transform found");
            FindPlayer();
            return;
        }

        if (footprintSprite == null)
        {
            if (enableDebugLogs) Debug.LogWarning("[PathIndicator] No footprint sprite loaded");
            return;
        }

        // Find closest gremlin
        GameObject closestGremlin = FindClosestGremlin();

        if (closestGremlin == null)
        {
            if (enableDebugLogs && footprintObjects.Count > 0)
                Debug.Log("[PathIndicator] No gremlin found, clearing path");
            ClearPath();
            hasGremlinPosition = false;
            return;
        }

        // Calculate path
        Vector3 playerPos = playerTransform.position;
        Vector3 gremlinPos = closestGremlin.transform.position;
        float distance = Vector3.Distance(playerPos, gremlinPos);

        // Check if gremlin has moved significantly since last update
        if (hasGremlinPosition)
        {
            float movementDistance = Vector3.Distance(lastGremlinPosition, gremlinPos);
            if (movementDistance < minimumUpdateDistance && footprintObjects.Count > 0)
            {
                // Gremlin hasn't moved much, don't recreate path
                if (enableDebugLogs)
                    Debug.Log($"[PathIndicator] Gremlin moved only {movementDistance:F2}, skipping update");
                return;
            }
        }

        //if (enableDebugLogs)
        //Debug.Log($"[PathIndicator] Updating path - Gremlin at distance {distance:F2}, max: {maxPathDistance}");

        // Don't show path if gremlin is too far
        if (distance > maxPathDistance)
        {
            if (enableDebugLogs) Debug.Log("[PathIndicator] Gremlin too far, clearing path");
            ClearPath();
            hasGremlinPosition = false;
            return;
        }

        // Store current position for next update
        lastGremlinPosition = gremlinPos;
        hasGremlinPosition = true;
        ClearPath();
        // Calculate number of footprints based on distance
        int footprintCount = Mathf.FloorToInt(distance / footprintSpacing);

        if (enableDebugLogs)
            Debug.Log($"[PathIndicator] Creating {footprintCount} footprints (spacing: {footprintSpacing})");

        if (footprintCount <= 0) return;

        // Create footprints along the path
        Vector3 direction = (gremlinPos - playerPos).normalized;

        for (int i = 1; i <= footprintCount; i++)
        {
            float t = (float)i / footprintCount;
            Vector3 position = playerPos + direction * (distance * t);

            CreateFootprint(position, direction, i);
        }

        if (enableDebugLogs)
            Debug.Log($"[PathIndicator] Created {footprintObjects.Count} footprint objects");
    }

    void CreateFootprint(Vector3 position, Vector3 direction, int index)
    {
        GameObject footprint = new GameObject($"Footprint_{index}");
        footprint.transform.position = position;
        footprint.transform.SetParent(transform);

        SpriteRenderer renderer = footprint.AddComponent<SpriteRenderer>();
        renderer.sprite = footprintSprite;
        renderer.sortingLayerName = "Default";

        // Match the grass's Y-sort formula (see GrassCartoonOverlay):
        //   sortOrder = sortOrderBase + round(-y * sortPrecision)
        // Grass uses base=1000, precision=10. The gremlin's YSortEntity uses the
        // same base/precision with sortYOffset=-0.2 (which adds ~+2 to its order).
        // We subtract 5 here so a footprint at the gremlin's feet sorts BELOW
        // the gremlin sprite, while still landing in the grass band range
        // (~400–1600) so it covers the grass at its own Y position.
        const int kSortBase = 1000;
        const float kSortPrecision = 10f;
        const int kBelowEntityBias = 5;
        renderer.sortingOrder = kSortBase + Mathf.RoundToInt(-position.y * kSortPrecision) - kBelowEntityBias;

        // Set initial alpha
        Color color = Color.white;
        color.a = fadeInFootprints ? 0f : footprintAlpha;
        renderer.color = color;

        // Calculate rotation to follow path direction
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // Alternate foot orientation for realism
        if (alternateFootOrientation)
        {
            float offset = isLeftFoot ? -15f : 15f;
            angle += offset;
            isLeftFoot = !isLeftFoot;
        }

        footprint.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        footprint.transform.localScale = Vector3.one * footprintScale;

        // Add to tracking list 
        footprintObjects.Add(footprint);

        if (enableDebugLogs && index == 1)
            Debug.Log($"[PathIndicator] Created footprint at {position}, scale: {footprintScale}, alpha: {color.a}");
        if (fadeInFootprints)
        {
            StartCoroutine(FadeInFootprint(renderer));
        }
        if (fadeOutOldFootprints)
        {
            StartCoroutine(FadeOutFootprint(footprint, renderer));
        }
        else
        {
            Destroy(footprint, footprintLifetime);
        }
    }

    System.Collections.IEnumerator FadeInFootprint(SpriteRenderer renderer)
    {
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            if (renderer == null) yield break;

            elapsed += Time.deltaTime;
            float t = elapsed / fadeInDuration;
            float smoothT = t * t * (3f - 2f * t);
            float alpha = Mathf.Lerp(0f, footprintAlpha, smoothT);
            Color color = renderer.color;
            color.a = alpha;
            renderer.color = color;
            yield return null;
        }

        if (renderer != null)
        {
            Color color = renderer.color;
            color.a = footprintAlpha;
            renderer.color = color;
        }
    }

    System.Collections.IEnumerator FadeOutFootprint(GameObject footprint, SpriteRenderer renderer)
    {
        yield return new WaitForSeconds(footprintLifetime);

        if (footprint == null || renderer == null)
        {
            // Clean up tracking list
            if (footprint != null)
                footprintObjects.Remove(footprint);
            yield break;
        }

        float elapsed = 0f;
        Color color = renderer.color;
        float startAlpha = color.a;

        while (elapsed < fadeOutDuration)
        {
            if (footprint == null || renderer == null)
            {
                // Clean up tracking list
                if (footprint != null)
                    footprintObjects.Remove(footprint);
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = elapsed / fadeOutDuration;
            float smoothT = t * t * (3f - 2f * t);
            float alpha = Mathf.Lerp(startAlpha, 0f, smoothT);
            color = renderer.color;
            color.a = alpha;
            renderer.color = color;
            yield return null;
        }

        // Clean up
        if (footprint != null)
        {
            footprintObjects.Remove(footprint);
            Destroy(footprint);
        }
    }

    GameObject FindClosestGremlin()
    {
        if (playerTransform == null)
        {
            if (enableDebugLogs) Debug.Log("[PathIndicator] No player transform in FindClosestGremlin");
            return null;
        }

        GameObject[] gremlins = GameObject.FindGameObjectsWithTag("Enemy");

        if (enableDebugLogs)
            Debug.Log($"[PathIndicator] Found {gremlins.Length} objects with 'Enemy' tag");

        GameObject closest = null;
        float closestDistance = float.MaxValue;
        int gremlinCount = 0;

        foreach (GameObject gremlin in gremlins)
        {
            if (gremlin == null) continue;
            GremlinController controller = gremlin.GetComponent<GremlinController>();
            if (controller == null)
            {
                if (enableDebugLogs)
                    Debug.Log($"[PathIndicator] Object '{gremlin.name}' has Enemy tag but no GremlinController");
                continue;
            }

            gremlinCount++;
            float distance = Vector3.Distance(playerTransform.position, gremlin.transform.position);

            if (enableDebugLogs)
                Debug.Log($"[PathIndicator] Gremlin '{gremlin.name}' at distance {distance:F2}");

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = gremlin;
            }
        }

        if (enableDebugLogs)
            Debug.Log($"[PathIndicator] Found {gremlinCount} actual gremlins, closest: {(closest != null ? closest.name : "none")}");

        return closest;
    }

    void ClearPath()
    {
        foreach (GameObject footprint in footprintObjects)
        {
            if (footprint != null)
            {
                Destroy(footprint);
            }
        }
        footprintObjects.Clear();
    }

    void OnDestroy()
    {
        ClearPath();
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;

        if (playerTransform == null) return;

        GameObject gremlin = FindClosestGremlin();
        if (gremlin != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(playerTransform.position, gremlin.transform.position);

            // Draw footprint positions
            Vector3 direction = (gremlin.transform.position - playerTransform.position).normalized;
            float distance = Vector3.Distance(playerTransform.position, gremlin.transform.position);
            int footprintCount = Mathf.FloorToInt(distance / footprintSpacing);

            Gizmos.color = Color.cyan;
            for (int i = 1; i <= footprintCount; i++)
            {
                float t = (float)i / footprintCount;
                Vector3 position = playerTransform.position + direction * (distance * t);
                Gizmos.DrawWireSphere(position, 0.2f);
            }
        }
    }
}

