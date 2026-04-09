using UnityEngine;
using System.Collections.Generic;


// Randomly places obstacle prefabs within the playable area.
// Supports solo obstacles AND composed clusters with role-aware placement.
// Prefab slots are assigned by BiomeManager per biome.
// Prefab slot convention:
//   Slot 0 = primary  prefab      (e.g. tree)
//   Slot 1 = secondary/accent     (e.g. small stone)
//   Slot 2 = tertiary/accent      (e.g. stone variant)

public class ObstacleGenerator : MonoBehaviour
{
    [Header("Prefabs (assigned by BiomeManager per biome — don't edit)")]
    public GameObject[] obstaclePrefabs;

    [Header("Distribution")]
    [Tooltip("Total number of placements (each is a solo obstacle or a cluster).")]
    public int obstacleCount = 15;

    [Tooltip("Minimum distance from map center. -1 = auto-detect from outermost tower ring.")]
    public float minDistanceFromCenter = -1f;

    [Tooltip("Minimum distance from the border ring inner edge.")]
    public float minDistanceFromBorder = 3f;

    [Tooltip("Minimum distance between placement anchors.")]
    public float minDistanceBetweenObstacles = 2.5f;

    [Header("Scale")]
    [Tooltip("Base scale for obstacle prefab instances.")]
    public float baseScale = 0.5f;
    [Range(0f, 0.5f)]
    [Tooltip("Random scale variation (0.2 = ±20%).")]
    public float scaleVariation = 0.2f;

    [Header("Clustering")]
    [Tooltip("Enable composed clusters of multiple prefabs grouped together.")]
    public bool enableClusters = true;

    [Tooltip("Chance each placement becomes a cluster instead of a solo obstacle.")]
    [Range(0f, 1f)]
    public float clusterChance = 0.6f;

    [Tooltip("How far cluster members spread from the anchor (world units).")]
    [Range(0.3f, 5f)]
    public float clusterSpread = 1.5f;

    [Tooltip("Scale factor for accent/secondary pieces. 0.5 = accents are 50–100% of hero size.")]
    [Range(0.3f, 1f)]
    public float clusterSecondaryScaleMin = 0.5f;

    [Header("Custom Cluster Blueprints (optional)")]
    public ObstacleClusterBlueprint[] customBlueprints;

    [Header("Y-Sort (must match GrassCartoonOverlay / PlayerMovement)")]
    public float sortPrecision = 10f;
    public int sortOrderBase = 1000;
    public float sortYOffset = -0.5f;

    private GameObject containerGO;

    //  Built-in cluster templates 

    private static readonly BuiltinTemplate[] builtinTemplates = new BuiltinTemplate[]
    {
        new BuiltinTemplate("TreeGrove", new TemplateMember[] {
            new TemplateMember( 0, new Vector2( 0.00f,  0.30f), 1.10f),
            new TemplateMember( 0, new Vector2(-0.45f,  0.10f), 0.85f),
            new TemplateMember( 0, new Vector2( 0.40f,  0.50f), 0.75f),
            new TemplateMember( 1, new Vector2(-0.20f, -0.35f), 0.55f),
            new TemplateMember( 2, new Vector2( 0.25f, -0.25f), 0.45f),
        }),
        new BuiltinTemplate("RockOutcrop", new TemplateMember[] {
            new TemplateMember( 1, new Vector2( 0.00f,  0.00f), 1.15f),
            new TemplateMember( 2, new Vector2(-0.40f, -0.15f), 0.70f),
            new TemplateMember( 1, new Vector2( 0.35f, -0.20f), 0.55f),
            new TemplateMember( 2, new Vector2( 0.10f,  0.35f), 0.50f),
        }),
        new BuiltinTemplate("TreeWithBase", new TemplateMember[] {
            new TemplateMember( 0, new Vector2( 0.00f,  0.15f), 1.00f),
            new TemplateMember( 1, new Vector2(-0.25f, -0.20f), 0.50f),
            new TemplateMember( 2, new Vector2( 0.20f, -0.25f), 0.40f),
        }),
        new BuiltinTemplate("TwinTrees", new TemplateMember[] {
            new TemplateMember( 0, new Vector2(-0.35f,  0.10f), 1.00f),
            new TemplateMember( 0, new Vector2( 0.35f,  0.20f), 0.90f),
            new TemplateMember( 1, new Vector2( 0.00f, -0.20f), 0.50f),
        }),
        new BuiltinTemplate("ScatteredStones", new TemplateMember[] {
            new TemplateMember( 1, new Vector2( 0.00f,  0.05f), 1.00f),
            new TemplateMember( 2, new Vector2(-0.30f, -0.20f), 0.65f),
            new TemplateMember( 1, new Vector2( 0.35f,  0.15f), 0.55f),
            new TemplateMember( 2, new Vector2(-0.10f,  0.35f), 0.45f),
        }),
        new BuiltinTemplate("LoneTreeAccent", new TemplateMember[] {
            new TemplateMember( 0, new Vector2( 0.00f,  0.10f), 1.05f),
            new TemplateMember(-1, new Vector2( 0.30f, -0.30f), 0.45f),
        }),
        new BuiltinTemplate("DenseThicket", new TemplateMember[] {
            new TemplateMember( 0, new Vector2( 0.00f,  0.20f), 1.00f),
            new TemplateMember( 0, new Vector2(-0.30f, -0.05f), 0.70f),
            new TemplateMember(-1, new Vector2( 0.25f,  0.00f), 0.60f),
            new TemplateMember( 1, new Vector2(-0.15f, -0.30f), 0.45f),
            new TemplateMember( 2, new Vector2( 0.10f, -0.35f), 0.40f),
        }),
        new BuiltinTemplate("ClearingEdge", new TemplateMember[] {
            new TemplateMember( 0, new Vector2(-0.50f,  0.25f), 0.95f),
            new TemplateMember( 0, new Vector2( 0.00f,  0.40f), 1.10f),
            new TemplateMember( 0, new Vector2( 0.50f,  0.20f), 0.85f),
            new TemplateMember( 1, new Vector2(-0.25f, -0.15f), 0.50f),
        }),
    };

    //  Generation 

    [ContextMenu("Generate Obstacles")]
    public void GenerateObstacles()
    {
        Cleanup();

        // Auto-pull prefabs from BiomeManager if not set
        bool hasPrefabs = false;
        if (obstaclePrefabs != null)
            foreach (var p in obstaclePrefabs)
                if (p != null) { hasPrefabs = true; break; }
        if (!hasPrefabs)
        {
            BiomeManager bm = FindBiomeManager();
            if (bm != null)
                obstaclePrefabs = bm.GetObstaclePrefabsForBiome(bm.activeBiome);
        }

        // Separate prefabs by role
        List<GameObject> allValid = new List<GameObject>();
        List<GameObject> heroPrefabs = new List<GameObject>();
        List<GameObject> accentPrefabs = new List<GameObject>();

        if (obstaclePrefabs != null)
        {
            for (int i = 0; i < obstaclePrefabs.Length; i++)
            {
                if (obstaclePrefabs[i] == null) continue;
                allValid.Add(obstaclePrefabs[i]);
                if (i == 0) heroPrefabs.Add(obstaclePrefabs[i]);
                else accentPrefabs.Add(obstaclePrefabs[i]);
            }
        }

        if (allValid.Count == 0)
        {
            Debug.LogWarning("[ObstacleGenerator] No obstacle prefabs assigned — skipping.");
            return;
        }
        if (heroPrefabs.Count == 0) heroPrefabs.AddRange(allValid);
        if (accentPrefabs.Count == 0) accentPrefabs.AddRange(allValid);
        if (obstacleCount <= 0) return;

        float innerRadius = ResolveInnerRadius();
        float outerRadius = ResolveOuterRadius();

        Debug.Log($"[ObstacleGenerator] Zone: inner={innerRadius:F1}, outer={outerRadius:F1}, " +
                  $"count={obstacleCount}, prefabs={allValid.Count}");

        if (outerRadius <= innerRadius)
        {
            Debug.LogWarning($"[ObstacleGenerator] Outer radius ({outerRadius:F1}) <= inner radius ({innerRadius:F1}). No room.");
            return;
        }

        containerGO = new GameObject("Obstacles_Container");
        containerGO.transform.SetParent(null);
        containerGO.transform.position = Vector3.zero;

        // Track anchor positions for our own distance checks.
        // Not using Physics2D.OverlapCircle because our own spawned colliders
        List<Vector2> anchors = new List<Vector2>();
        int placedCount = 0, totalSprites = 0, clusters = 0;
        int maxAttempts = Mathf.Min(obstacleCount * 20, 50000); // cap to prevent hangs

        for (int attempt = 0; attempt < maxAttempts && placedCount < obstacleCount; attempt++)
        {
            // Random anchor in annular zone
            float angle = Random.Range(0f, 2f * Mathf.PI);
            float dist = Mathf.Sqrt(Random.Range(innerRadius * innerRadius, outerRadius * outerRadius));
            Vector2 anchor = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);

            // Distance check against our own previously placed anchors only
            bool tooClose = false;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (Vector2.Distance(anchor, anchors[i]) < minDistanceBetweenObstacles)
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            bool makeCluster = enableClusters && Random.value < clusterChance;

            if (makeCluster)
            {
                int spawned = SpawnComposedCluster(anchor, allValid, heroPrefabs, accentPrefabs,
                                                   innerRadius, outerRadius);
                totalSprites += spawned;
                if (spawned > 0) clusters++;
            }
            else
            {
                GameObject prefab = allValid[Random.Range(0, allValid.Count)];
                float s = baseScale * Random.Range(1f - scaleVariation, 1f + scaleVariation);
                SpawnSinglePrefab(prefab, anchor, s);
                totalSprites++;
            }

            anchors.Add(anchor);
            placedCount++;
        }

        Debug.Log($"[ObstacleGenerator] {placedCount} placements ({clusters} clusters, " +
                  $"{placedCount - clusters} solo) = {totalSprites} sprites.");
    }

    //  Composed cluster spawning 

    private int SpawnComposedCluster(Vector2 anchor,
        List<GameObject> allValid, List<GameObject> heroPrefabs, List<GameObject> accentPrefabs,
        float zoneInner, float zoneOuter)
    {
        if (customBlueprints != null && customBlueprints.Length > 0)
        {
            List<ObstacleClusterBlueprint> valid = new List<ObstacleClusterBlueprint>();
            foreach (var bp in customBlueprints)
                if (bp != null && bp.members != null && bp.members.Length > 0)
                    valid.Add(bp);
            if (valid.Count > 0)
                return SpawnFromBlueprint(valid[Random.Range(0, valid.Count)],
                                          anchor, allValid, heroPrefabs, accentPrefabs,
                                          zoneInner, zoneOuter);
        }

        BuiltinTemplate template = builtinTemplates[Random.Range(0, builtinTemplates.Length)];
        return SpawnFromBuiltinTemplate(template, anchor, allValid, heroPrefabs, accentPrefabs,
                                        zoneInner, zoneOuter);
    }

    private int SpawnFromBuiltinTemplate(BuiltinTemplate template, Vector2 anchor,
        List<GameObject> allValid, List<GameObject> heroPrefabs, List<GameObject> accentPrefabs,
        float zoneInner, float zoneOuter)
    {
        float rotAngle = Random.Range(0f, 2f * Mathf.PI);
        float cosR = Mathf.Cos(rotAngle);
        float sinR = Mathf.Sin(rotAngle);
        int spawned = 0;

        foreach (var member in template.members)
        {
            Vector2 offset = member.offset * clusterSpread;
            Vector2 rotated = new Vector2(
                offset.x * cosR - offset.y * sinR,
                offset.x * sinR + offset.y * cosR
            );
            rotated += new Vector2(
                Random.Range(-0.1f, 0.1f) * clusterSpread,
                Random.Range(-0.1f, 0.1f) * clusterSpread
            );

            Vector2 pos = anchor + rotated;
            float posDist = pos.magnitude;
            if (posDist < zoneInner || posDist > zoneOuter) continue;

            GameObject prefab = ResolvePrefab(member.slotIndex, allValid, heroPrefabs, accentPrefabs);
            float scaleMult = member.scaleMult;
            if (member.slotIndex != 0)
                scaleMult *= Random.Range(clusterSecondaryScaleMin, 1f);
            float s = baseScale * scaleMult * Random.Range(1f - scaleVariation * 0.5f, 1f + scaleVariation * 0.5f);

            SpawnSinglePrefab(prefab, pos, s);
            spawned++;
        }
        return spawned;
    }

    private int SpawnFromBlueprint(ObstacleClusterBlueprint bp, Vector2 anchor,
        List<GameObject> allValid, List<GameObject> heroPrefabs, List<GameObject> accentPrefabs,
        float zoneInner, float zoneOuter)
    {
        float rotAngle = bp.randomizeRotation ? Random.Range(0f, 2f * Mathf.PI) : 0f;
        float cosR = Mathf.Cos(rotAngle);
        float sinR = Mathf.Sin(rotAngle);
        int spawned = 0;

        foreach (var member in bp.members)
        {
            Vector2 offset = member.offset * clusterSpread;
            Vector2 rotated = new Vector2(
                offset.x * cosR - offset.y * sinR,
                offset.x * sinR + offset.y * cosR
            );
            rotated += new Vector2(
                Random.Range(-bp.positionJitter, bp.positionJitter),
                Random.Range(-bp.positionJitter, bp.positionJitter)
            );

            Vector2 pos = anchor + rotated;
            float posDist = pos.magnitude;
            if (posDist < zoneInner || posDist > zoneOuter) continue;

            GameObject prefab = ResolvePrefab(member.prefabSlotIndex, allValid, heroPrefabs, accentPrefabs);
            float s = baseScale * member.scaleMult * Random.Range(1f - scaleVariation * 0.5f, 1f + scaleVariation * 0.5f);

            SpawnSinglePrefab(prefab, pos, s);
            spawned++;
        }
        return spawned;
    }

    private GameObject ResolvePrefab(int slotIndex,
        List<GameObject> allValid, List<GameObject> heroPrefabs, List<GameObject> accentPrefabs)
    {
        if (slotIndex == 0 && heroPrefabs.Count > 0)
            return heroPrefabs[Random.Range(0, heroPrefabs.Count)];
        if ((slotIndex == 1 || slotIndex == 2) && accentPrefabs.Count > 0)
        {
            if (obstaclePrefabs != null && slotIndex < obstaclePrefabs.Length && obstaclePrefabs[slotIndex] != null)
                return obstaclePrefabs[slotIndex];
            return accentPrefabs[Random.Range(0, accentPrefabs.Count)];
        }
        return allValid[Random.Range(0, allValid.Count)];
    }

    //  Single prefab spawning 

    private void SpawnSinglePrefab(GameObject prefab, Vector2 pos, float scale)
    {
        GameObject obs = Instantiate(prefab, new Vector3(pos.x, pos.y, 0f), Quaternion.identity);
        obs.transform.SetParent(containerGO.transform, true);
        obs.transform.localScale = new Vector3(scale, scale, 1f);

        if (obs.GetComponent<Collider2D>() == null)
        {
            BoxCollider2D box = obs.AddComponent<BoxCollider2D>();
            SpriteRenderer sr = obs.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null)
            {
                box.size = sr.sprite.bounds.size;
                box.offset = sr.sprite.bounds.center;
            }
        }

        if (obs.GetComponent<YSortEntity>() == null)
        {
            var ysort = obs.AddComponent<YSortEntity>();
            ysort.sortPrecision = sortPrecision;
            ysort.sortOrderBase = sortOrderBase;
            ysort.sortYOffset = sortYOffset;
        }
    }

    //  Radius resolution 

    private float ResolveInnerRadius()
    {
        if (minDistanceFromCenter >= 0f)
            return minDistanceFromCenter;

        TowerDefenseMap map = FindFirstObjectByType<TowerDefenseMap>();
        if (map != null && map.rings != null && map.rings.Count > 0)
        {
            float maxRingRadius = 0f;
            float lastSlotSize = 1f;
            foreach (var ring in map.rings)
            {
                if (ring.enabled && ring.radius > maxRingRadius)
                {
                    maxRingRadius = ring.radius;
                    lastSlotSize = ring.slotSize;
                }
            }
            if (maxRingRadius > 0f)
                return maxRingRadius + lastSlotSize * 0.5f + 1f;
        }
        return 5f;
    }

    private float ResolveOuterRadius()
    {
        BorderRingGenerator border = FindFirstObjectByType<BorderRingGenerator>();
        BiomeManager bm = FindBiomeManager();

        float borderInnerRadius;
        if (border != null)
        {
            float effectiveInner = border.innerRadius;
            if (effectiveInner < 0f)
                effectiveInner = bm != null ? bm.grassCartoonSpawnRadius : 50f;
            borderInnerRadius = effectiveInner;
        }
        else
        {
            borderInnerRadius = bm != null ? bm.grassCartoonSpawnRadius : 50f;
        }
        return borderInnerRadius - minDistanceFromBorder;
    }

    private BiomeManager FindBiomeManager()
    {
        BiomeManager bm = GetComponent<BiomeManager>();
        if (bm == null) bm = FindFirstObjectByType<BiomeManager>();
        return bm;
    }

    //  Cleanup 

    [ContextMenu("Remove Obstacles")]
    public void Cleanup()
    {
        // Destroy our tracked container
        if (containerGO != null)
        {
            if (Application.isPlaying) Destroy(containerGO);
            else DestroyImmediate(containerGO);
            containerGO = null;
        }

        // Find and destroy any leftover containers from previous sessions.

        GameObject leftover = GameObject.Find("Obstacles_Container");
        if (leftover != null)
        {
            if (Application.isPlaying) Destroy(leftover);
            else DestroyImmediate(leftover);
        }
    }

    void OnDestroy() { Cleanup(); }

    //  Gizmos 

    private void OnDrawGizmos()
    {
        float inner = ResolveInnerRadius();
        float outer = ResolveOuterRadius();

        Gizmos.color = Color.red;
        DrawGizmoCircle(Vector3.zero, inner);
        Gizmos.color = Color.yellow;
        DrawGizmoCircle(Vector3.zero, outer);

        if (containerGO != null)
        {
            Gizmos.color = Color.cyan;
            foreach (Transform child in containerGO.transform)
                Gizmos.DrawWireSphere(child.position, 0.3f);
        }
    }

    private void DrawGizmoCircle(Vector3 center, float radius)
    {
        int segments = 64;
        float step = 2f * Mathf.PI / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * step;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    //  Data structures 

    private struct TemplateMember
    {
        public int slotIndex;
        public Vector2 offset;
        public float scaleMult;
        public TemplateMember(int slot, Vector2 off, float scale)
        { slotIndex = slot; offset = off; scaleMult = scale; }
    }

    private struct BuiltinTemplate
    {
        public string name;
        public TemplateMember[] members;
        public BuiltinTemplate(string n, TemplateMember[] m)
        { name = n; members = m; }
    }
}
