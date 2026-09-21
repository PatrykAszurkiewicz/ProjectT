using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GremlinSpawner : MonoBehaviour
{
    // ── Gremlin sprite frames ────────────────────────────────────────────────
    // The Gremlin has NO PREFAB ASSET. CreateGremlinPrefab() builds it in code with
    // new GameObject("Gremlin") + AddComponent<GremlinController>(), so there is
    // nothing on disk for the prefab migration tool to fill — GremlinController's
    // sprite path was a private const and its frame array was never reachable.
    //
    // This spawner IS a real scene/prefab component, so the references live here and
    // are handed down at spawn time. Assign the 25 frames from
    // Assets/.../EnemySprites/Gremlin/ (00–24) on THIS component in the Inspector.
    [Header("Gremlin Sprites")]
    [Tooltip("Gremlin animation frames in order (00–24). Handed to each spawned " +
             "GremlinController. Leave empty to fall back to Resources.")]
    [SerializeField] private Sprite[] gremlinSpriteFrames;

    [Header("Spawn Settings")]
    public float spawnInterval = 10f;
    public int maxGremlinsOnMap = 1;
    public float spawnDistance = 8f;

    [Header("Core Avoidance")]
    [Tooltip("Minimum distance a gremlin must keep from the Central Core (the map centre) " +
             "when it spawns. Stops gremlins popping into existence on top of the core just " +
             "because the player happened to be standing near it.\n" +
             "Automatically clamped so the ring always fits inside the map.")]
    public float minDistanceFromCore = 24f;
    [Tooltip("Outer limit — gremlins won't spawn FURTHER than this from the core, so the " +
             "chase stays finite. Set to 0 for no outer limit.\n" +
             "This is an explicit world-space number on purpose: it is NOT derived from " +
             "TowerDefenseMap.mapRadius.")]
    public float maxDistanceFromCore = 36f;
    [Tooltip("OFF by default, and you almost certainly want it OFF.\n" +
             "TowerDefenseMap.mapRadius is NOT the walkable world — it is the small " +
             "tower-slot / collider radius (~16), while the actual ground reaches " +
             "backgroundCoverageRadius (~80). Its boundary collider is a trigger, so it " +
             "confines nothing. Turning this ON caps spawns at the edge of the layout " +
             "rings, which is exactly the 'gremlin spawns too close' problem.")]
    public bool clampToMapRadius = false;
    [Tooltip("Only used when Clamp To Map Radius is ON: mapRadius minus this margin.")]
    public float mapEdgeMargin = 1.5f;
    [Tooltip("If NO spot at all satisfies the core rule (small map, player parked on the " +
             "core, everything blocked), relax the minimum in steps instead of skipping the " +
             "spawn.\nTurn this OFF to make the rule absolute — the gremlin then just won't " +
             "spawn this tick and will retry on the next interval.")]
    public bool relaxCoreDistanceIfStuck = true;

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
    [Tooltip("The footprint trail is HIDDEN entirely once the gremlin is further than " +
             "this from the player (GremlinPathIndicator line 217). Keep it comfortably " +
             "above Max Distance From Core or a far-spawned gremlin leaves no trail at all.")]
    public float pathMaxDistance = 60f;

    private List<GameObject> activeGremlins = new List<GameObject>();
    private Transform playerTransform;
    private GameObject gremlinPrefab;
    private GremlinPathIndicator pathIndicator;

    // Resolved lazily — the map and its Central Core are rebuilt on stage/layout
    // changes, so these are re-fetched whenever the cached reference dies.
    private CentralCore centralCore;
    private TowerDefenseMap towerMap;
    private bool warnedCoreDistanceClamped;

    // Small set of distance fallbacks per attempt — keeps the gremlin near the
    // intended ring even if the first radius lands inside a wall.
    private static readonly float[] RadiusOffsets = { 0f, 1.2f, -1.2f, 2.4f };

    // Last-resort relaxation of the core rule, applied in order.
    private static readonly float[] CoreDistanceRelaxSteps = { 0.75f, 0.5f, 0.25f, 0f };

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

        rb.gravityScale = 0f;
        rb.linearDamping = 5f;
        rb.freezeRotation = true;
        collider.radius = 0.3f;

        gremlinPrefab.layer = 0;
        gremlinPrefab.tag = "Enemy";
        sprite.sortingOrder = 100;

        // Deactivate BEFORE adding the controller. Awake does not run on an inactive
        // GameObject, so the template never builds its GremlinVisual child — which is
        // what Instantiate was copying into every clone, giving each gremlin a second,
        // static visual that trailed the animated one. See the matching note in
        // GremlinController.SetupComponents().
        gremlinPrefab.SetActive(false);

        // FIX — GremlinController is added LAST, and no test sprite is assigned.
        //
        // AddComponent on an ACTIVE GameObject runs Awake IMMEDIATELY, and
        // GremlinController.SetupVisuals() moves the visible sprite onto a child
        // ("GremlinVisual") and hides THIS body renderer with color.a = 0, leaving it
        // as nothing but the ground-anchored Y-sort source.
        //
        // The controller used to be added on the line above, so everything after it —
        //     sprite.sprite = CreateTestSprite();
        //     sprite.color  = Color.red;
        // — ran AFTERWARDS and un-hid the body renderer, at sortingOrder 100. Every
        // gremlin then carried a second, static, non-animating sprite behind the
        // hopping one. That is the "double animation" ghost.
        var gremlinCtrl = gremlinPrefab.AddComponent<GremlinController>();

        // Hand over the direct references so the controller never touches Resources.
        if (gremlinSpriteFrames != null && gremlinSpriteFrames.Length > 0)
            gremlinCtrl.SetSpriteFrames(gremlinSpriteFrames);
        else
            Debug.LogWarning("[GremlinSpawner] No Gremlin Sprite Frames assigned — the Gremlin " +
                             "will fall back to Resources/Sprites/EnemySprites/Gremlin, which " +
                             "breaks once that art leaves the Resources folder. Assign the 25 " +
                             "frames on this GremlinSpawner component.");
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

    // Picks a spawn point that is clear of layout obstacles / biome props / towers AND
    // far enough from the Central Core, in three passes (best first):
    //
    //   1. The original behaviour — a ring at `spawnDistance` around the player — but
    //      only accepting points that also respect the core-distance rule and stay
    //      inside the map.
    //   2. If the player is standing near the core, that entire ring sits inside the
    //      forbidden zone, so pass 1 can never succeed. Fall back to a ring around the
    //      CORE at `minDistanceFromCore`, fanned out from the player's side of the map
    //      so the chase doesn't start on the opposite edge.
    //   3. Only if both fail, relax the core distance in steps (unless the rule is
    //      configured as absolute) so a spawn still happens eventually rather than the
    //      gremlin silently never appearing.
    bool TryFindClearSpawnPosition(out Vector3 result)
    {
        result = default;
        if (playerTransform == null) return false;

        int testMask = BuildObstacleTestMask();
        Vector2 core = GetCoreCenter();
        bool hasMax = TryGetMaxSpawnRadius(out float maxRadius);
        float minCore = GetEffectiveMinCoreDistance(hasMax, maxRadius);

        // Pass 1 — preferred: keep the familiar "spawnDistance away from the player" feel.
        if (TrySampleAroundPlayer(core, minCore, maxRadius, hasMax, testMask, out result))
            return true;

        // Pass 2 — player is on/near the core, or the player ring is fully blocked.
        if (TrySampleAroundCore(core, minCore, maxRadius, hasMax, testMask, out result))
            return true;

        // Pass 3 — nothing fits the rule at all.
        if (!relaxCoreDistanceIfStuck) return false;

        foreach (float factor in CoreDistanceRelaxSteps)
        {
            if (TrySampleAroundPlayer(core, minCore * factor, maxRadius, hasMax, testMask, out result))
                return true;
        }
        return false;
    }

    // Random directions on a ring around the player, with the same short/long radius
    // fallbacks as before, now filtered by the core-distance and map-edge rules.
    bool TrySampleAroundPlayer(Vector2 core, float minCore, float maxRadius, bool hasMax,
                               int testMask, out Vector3 result)
    {
        result = default;
        Vector2 playerPos = playerTransform.position;

        for (int attempt = 0; attempt < maxSpawnPlacementAttempts; attempt++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;

            foreach (float offset in RadiusOffsets)
            {
                float radius = Mathf.Max(0.5f, spawnDistance + offset);
                Vector2 candidate = playerPos + dir * radius;

                if (!PassesCoreRules(candidate, core, minCore, maxRadius, hasMax)) continue;
                if (!IsPositionClear(candidate, testMask)) continue;

                result = new Vector3(candidate.x, candidate.y, 0f);
                return true;
            }
        }
        return false;
    }

    // Ring around the CORE, starting on the player's side and fanning outward
    // (0°, +25°, -25°, +50°, -50° …), trying a few radii between the minimum allowed
    // distance and the map edge for each angle.
    bool TrySampleAroundCore(Vector2 core, float minCore, float maxRadius, bool hasMax,
                             int testMask, out Vector3 result)
    {
        result = default;
        if (minCore <= 0f) return false;

        Vector2 playerPos = playerTransform.position;
        Vector2 toPlayer = playerPos - core;
        float baseAngle = toPlayer.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg
            : Random.Range(0f, 360f);

        float inner = minCore + 0.5f;
        float outer = hasMax ? Mathf.Max(inner, maxRadius) : inner + spawnDistance;

        // Don't drop the gremlin in the player's lap just because that side of the
        // core happened to be free.
        float minPlayerGap = spawnDistance * 0.5f;

        int fanSteps = Mathf.Max(8, maxSpawnPlacementAttempts) * 2;
        for (int step = 0; step < fanSteps; step++)
        {
            float spread = 25f * ((step + 1) / 2);
            float angle = baseAngle + (step % 2 == 0 ? spread : -spread);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            for (int r = 0; r < 4; r++)
            {
                float radius = Mathf.Lerp(inner, outer, r / 3f);
                Vector2 candidate = core + dir * radius;

                if (Vector2.Distance(candidate, playerPos) < minPlayerGap) continue;
                if (!IsPositionClear(candidate, testMask)) continue;

                result = new Vector3(candidate.x, candidate.y, 0f);
                return true;
            }
        }
        return false;
    }

    bool PassesCoreRules(Vector2 candidate, Vector2 core, float minCore,
                         float maxRadius, bool hasMax)
    {
        float distanceFromCore = Vector2.Distance(candidate, core);
        if (distanceFromCore < minCore) return false;
        if (hasMax && distanceFromCore > maxRadius) return false;
        return true;
    }

    // Layers we should NEVER treat as blocking. The Player and Enemy layers may
    // not be defined in every project — NameToLayer returns -1 in that case,
    // which we just ignore via the bitmask helper below.
    int BuildObstacleTestMask()
    {
        int ignoreMask = LayerToBit(LayerMask.NameToLayer("Player")) |
                         LayerToBit(LayerMask.NameToLayer("Enemy"));
        return obstacleBlockingMask.value & ~ignoreMask;
    }

    // The Central Core lives at the world origin, but TowerDefenseMap destroys and
    // rebuilds it on every stage/layout change — so resolve lazily and re-resolve
    // whenever the cached reference has died, instead of caching once in Start().
    Vector2 GetCoreCenter()
    {
        if (centralCore == null)
        {
            if (towerMap == null) towerMap = FindFirstObjectByType<TowerDefenseMap>();
            if (towerMap != null) centralCore = towerMap.GetCentralCore();
            if (centralCore == null) centralCore = FindFirstObjectByType<CentralCore>();
        }

        // No core in the scene (test scene, or core destroyed on game over): the map is
        // still built around the origin, so that stays the right centre to avoid.
        return centralCore != null ? (Vector2)centralCore.transform.position : Vector2.zero;
    }

    // Outer bound on the spawn ring. Returns false when there is NO outer bound at all,
    // in which case only minDistanceFromCore applies.
    //
    // Deliberately NOT derived from TowerDefenseMap.mapRadius by default: mapRadius is
    // the tower-slot / collider radius (~16) and its boundary collider is a trigger, so
    // it describes the layout, not the world the player can walk in (the ground covers
    // backgroundCoverageRadius, ~80). Clamping to it pinned every gremlin to the edge of
    // the outermost ring.
    bool TryGetMaxSpawnRadius(out float maxRadius)
    {
        maxRadius = 0f;
        bool hasBound = false;

        if (maxDistanceFromCore > 0f)
        {
            maxRadius = maxDistanceFromCore;
            hasBound = true;
        }

        if (clampToMapRadius)
        {
            if (towerMap == null) towerMap = FindFirstObjectByType<TowerDefenseMap>();
            if (towerMap != null && towerMap.mapRadius > 0f)
            {
                float layoutEdge = Mathf.Max(1f, towerMap.mapRadius - mapEdgeMargin);
                maxRadius = hasBound ? Mathf.Min(maxRadius, layoutEdge) : layoutEdge;
                hasBound = true;
            }
        }

        return hasBound;
    }

    // The min ring has to leave room inside the outer bound, otherwise every candidate
    // fails and the spawn falls through to the relax pass every single time.
    float GetEffectiveMinCoreDistance(bool hasMax, float maxRadius)
    {
        float min = Mathf.Max(0f, minDistanceFromCore);
        if (!hasMax) return min;

        float cap = Mathf.Max(0f, maxRadius - spawnClearanceRadius - 0.5f);
        if (min > cap)
        {
            if (!warnedCoreDistanceClamped)
            {
                warnedCoreDistanceClamped = true;
                string bound = clampToMapRadius && towerMap != null
                    ? $"Max Distance From Core {maxDistanceFromCore:F1} / mapRadius {towerMap.mapRadius:F1} - margin {mapEdgeMargin:F1}"
                    : $"Max Distance From Core {maxDistanceFromCore:F1}";
                Debug.LogWarning($"[GremlinSpawner] Min Distance From Core ({minDistanceFromCore:F1}) " +
                                 $"is too close to the outer bound ({bound}) — clamping to {cap:F1}. " +
                                 $"Raise Max Distance From Core, or turn OFF Clamp To Map Radius " +
                                 $"(mapRadius is the layout ring radius, not the walkable world).");
            }
            min = cap;
        }
        return min;
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

        // Play gremlin appearance sound. Uses the newer GremlinAppearance2 event; the
        // old gremlinAppearance is intentionally NOT also played, so only one spawn
        // cue fires. (Swap back, or play both, if you wanted them layered.)
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.gremlinAppearance2.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.gremlinAppearance2, position);
        }

        activeGremlins.Add(newGremlin);
        StartCoroutine(MonitorGremlin(newGremlin));
    }

    IEnumerator MonitorGremlin(GameObject gremlin)
    {
        while (gremlin != null) yield return new WaitForSeconds(1f);
        activeGremlins.Remove(gremlin);
    }

    // Red   = inner limit, gremlins may NOT spawn inside this.
    // Green = outer limit.
    // Yellow (dim) = TowerDefenseMap.mapRadius, drawn for reference so you can see how
    //                small the layout ring extent is compared to the spawn band.
    void OnDrawGizmosSelected()
    {
        Vector3 core = Vector3.zero;
        var map = towerMap != null ? towerMap : FindFirstObjectByType<TowerDefenseMap>();
        var found = centralCore != null ? centralCore : FindFirstObjectByType<CentralCore>();
        if (found != null) core = found.transform.position;

        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.9f);
        Gizmos.DrawWireSphere(core, minDistanceFromCore);

        if (maxDistanceFromCore > 0f)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(core, maxDistanceFromCore);
        }

        if (map != null && map.mapRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(core, map.mapRadius);
        }
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


