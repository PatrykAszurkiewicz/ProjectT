using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GremlinSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    public float spawnInterval = 10f;
    public int maxGremlinsOnMap = 1;
    public float spawnDistance = 8f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Radius around the candidate spawn point that must be clear of obstacles.")]
    public float spawnClearanceRadius = 0.5f;
    [Tooltip("How many random positions to try before giving up on a spawn attempt.")]
    public int maxSpawnPlacementAttempts = 12;
    [Tooltip("Layers checked when validating that the spawn point is free.\n" +
             "By default this is 'everything' — the spawner ignores triggers and the\n" +
             "Player/Enemy layers internally, so leaving this as Everything is fine.")]
    public LayerMask obstacleBlockingMask = ~0;

    [Header("Path Indicator")]
    public bool showPathToGremlin = true;
    public float pathFootprintSpacing = 1.5f;
    public float pathMaxDistance = 20f;

    private List<GameObject> activeGremlins = new List<GameObject>();
    private Transform playerTransform;
    private GameObject gremlinPrefab;
    private GremlinPathIndicator pathIndicator;

    void Start()
    {
        FindPlayer();
        CreateGremlinPrefab();

        // Create path indicator if enabled
        if (showPathToGremlin)
        {
            SetupPathIndicator();
        }

        InvokeRepeating(nameof(TrySpawn), 2f, spawnInterval);
    }

    void SetupPathIndicator()
    {
        GameObject indicatorObj = new GameObject("GremlinPathIndicator");
        indicatorObj.transform.SetParent(transform);
        pathIndicator = indicatorObj.AddComponent<GremlinPathIndicator>();
        pathIndicator.footprintSpacing = pathFootprintSpacing;
        pathIndicator.maxPathDistance = pathMaxDistance;
        pathIndicator.footprintScale = 0.4f; // Slightly larger
        pathIndicator.footprintAlpha = 0.85f; // More visible
        pathIndicator.updateInterval = 2.0f; // Update less frequently
        pathIndicator.alternateFootOrientation = true;
        pathIndicator.fadeInFootprints = false; // No fade-in to prevent blinking
        pathIndicator.fadeOutOldFootprints = true; // Keep slow fade-out
        pathIndicator.enableDebugLogs = false; // Disable logs by default
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
            if (playerObject != null) playerTransform = playerObject.transform;
        }
    }

    void CreateGremlinPrefab()
    {
        gremlinPrefab = new GameObject("Gremlin");

        var rb = gremlinPrefab.AddComponent<Rigidbody2D>();
        var sprite = gremlinPrefab.AddComponent<SpriteRenderer>();
        var collider = gremlinPrefab.AddComponent<CircleCollider2D>();
        gremlinPrefab.AddComponent<GremlinController>();

        rb.gravityScale = 0f;
        rb.linearDamping = 5f;
        rb.freezeRotation = true;
        collider.radius = 0.3f;

        gremlinPrefab.layer = 0;
        gremlinPrefab.tag = "Enemy";
        sprite.sprite = CreateTestSprite();
        sprite.color = Color.red;
        sprite.sortingOrder = 100;

        gremlinPrefab.SetActive(false);
    }

    Sprite CreateTestSprite()
    {
        var texture = new Texture2D(64, 64);
        var colors = new Color[64 * 64];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.red;
        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f, 100f);
    }

    void TrySpawn()
    {
        activeGremlins.RemoveAll(g => g == null);
        if (activeGremlins.Count >= maxGremlinsOnMap) return;
        if (playerTransform == null) return;

        if (TryFindClearSpawnPosition(out Vector3 spawnPos))
        {
            SpawnGremlinAt(spawnPos);
        }
        else
        {
            // Couldn't find a clear spot this tick. Stay silent — next interval will try again.
        }
    }

    // Picks a random direction at `spawnDistance` from the player and verifies the spot
    // is clear of layout obstacles, biome obstacles, towers, etc. If blocked, tries
    // a slightly shorter/longer radius along the same ray before picking a fresh angle.
    bool TryFindClearSpawnPosition(out Vector3 result)
    {
        result = default;
        Vector2 playerPos = playerTransform.position;

        // Layers we should NEVER treat as blocking. The Player and Enemy layers may
        // not be defined in every project — NameToLayer returns -1 in that case,
        // which we just ignore via the bitmask helper below.
        int ignoreMask = LayerToBit(LayerMask.NameToLayer("Player")) |
                         LayerToBit(LayerMask.NameToLayer("Enemy"));
        int testMask = obstacleBlockingMask.value & ~ignoreMask;

        // Small set of distance fallbacks per attempt — keeps the gremlin near the
        // intended ring even if the first radius lands inside a wall.
        float[] radiusOffsets = { 0f, 1.2f, -1.2f, 2.4f };

        for (int attempt = 0; attempt < maxSpawnPlacementAttempts; attempt++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;

            foreach (float offset in radiusOffsets)
            {
                float radius = Mathf.Max(0.5f, spawnDistance + offset);
                Vector2 candidate = playerPos + dir * radius;

                if (IsPositionClear(candidate, testMask))
                {
                    result = new Vector3(candidate.x, candidate.y, 0f);
                    return true;
                }
            }
        }
        return false;
    }

    bool IsPositionClear(Vector2 pos, int testMask)
    {
        // OverlapCircleAll so we can inspect each hit and skip triggers (energy drops,
        // pickup zones, etc.) which shouldn't count as obstacles.
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, spawnClearanceRadius, testMask);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (c == null) continue;
            if (c.isTrigger) continue;
            // Skip anything tagged Player or Enemy in case those tags exist
            // on the default layer (so layer-based filtering doesn't catch them).
            if (c.CompareTag("Player") || c.CompareTag("Enemy")) continue;
            return false;
        }
        return true;
    }

    static int LayerToBit(int layer)
    {
        return (layer < 0 || layer > 31) ? 0 : (1 << layer);
    }

    void SpawnGremlinAt(Vector3 position)
    {
        if (gremlinPrefab == null) return;

        GameObject newGremlin = Instantiate(gremlinPrefab, position, Quaternion.identity);
        newGremlin.SetActive(true);

        // Play gremlin appearance sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.gremlinAppearance, position);
        }

        activeGremlins.Add(newGremlin);
        StartCoroutine(MonitorGremlin(newGremlin));
    }

    IEnumerator MonitorGremlin(GameObject gremlin)
    {
        while (gremlin != null) yield return new WaitForSeconds(1f);
        activeGremlins.Remove(gremlin);
    }

    [ContextMenu("Spawn Gremlin")]
    void SpawnNow() => TrySpawn();

    [ContextMenu("Toggle Path Indicator")]
    void TogglePathIndicator()
    {
        if (pathIndicator != null)
        {
            pathIndicator.gameObject.SetActive(!pathIndicator.gameObject.activeSelf);
        }
        else if (showPathToGremlin)
        {
            SetupPathIndicator();
        }
    }
}
