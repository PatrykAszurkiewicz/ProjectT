using UnityEngine;
using System.Collections.Generic;


// Randomly places obstacle prefabs within the playable area.
// Supports solo obstacles AND composed clusters with role-aware placement.
// Prefab slots are assigned by BiomeManager per biome.
// Prefab slot convention:
//   Slot 0 = primary  prefab      (e.g. tree)
//   Slot 1 = secondary/accent     (e.g. small stone)
//   Slot 2 = tertiary/accent      (e.g. stone variant)
//
// SPAWN ZONE
// Biome decorations are placed in an annulus around the map centre:
//   inner = whatever the active MAP LAYOUT occupies (slots, obstacles,
//           connection lines) + clearance   → see ResolveInnerRadius()
//   outer = border ring inner edge - minDistanceFromBorder
// On top of the annulus, individual keep-out discs are built around every
// layout obstacle / tower slot so nothing lands on the designed terrain even
// if the annulus is loosened.

public class ObstacleGenerator : MonoBehaviour
{


    [Tooltip("Extra padding added to the auto-detected inner radius. " +
             "Increase this when layouts have obstacles that extend outward " +
             "from the tower rings (walls, perimeter buildings).")]
    public float extraInnerPadding = 2f;

    [Header("Prefabs (assigned by BiomeManager per biome — don't edit)")]
    public GameObject[] obstaclePrefabs;

    [Header("Solo-Only Prefabs (assigned by BiomeManager — never clustered)")]
    [Tooltip("Prefabs that are always placed individually, never grouped into clusters (e.g. fireplaces).")]
    public GameObject[] soloPrefabs;

    [Tooltip("Number of solo-only obstacles to place (each spawned individually, never clustered).")]
    public int soloCount = 30;

    [Header("Distribution")]
    [Tooltip("Total number of placements (each is a solo obstacle or a cluster).")]
    public int obstacleCount = 70;


    [Tooltip("Minimum distance from map center. -1 = auto-detect from the active map layout " +
             "(or the outermost tower ring when no layout is active).\n" +
             "NOTE: this is a FLOOR, not a cap — when 'Respect Active Layout' is on, the " +
             "inner radius is still pushed out past the layout footprint.")]
    public float minDistanceFromCenter = -1f;

    [Tooltip("Minimum distance from the border ring inner edge.")]
    public float minDistanceFromBorder = 3f;

    [Tooltip("Minimum distance between placement anchors.")]
    public float minDistanceBetweenObstacles = 2.5f;

    [Tooltip("Obstacles will not spawn within this radius of the player at the moment of generation. " +
             "Prevents the player from being trapped inside a freshly spawned obstacle or cluster member. " +
             "Set to 0 to disable.")]
    public float minDistanceFromPlayer = 2.5f;

    [Tooltip("Tag used to find the player GameObject for the spawn exclusion check.")]
    public string playerTag = "Player";

    //  Layout clearance 

    [Header("Map Layout Clearance")]
    [Tooltip("Keep biome decorations away from the active MapLayoutDefinition " +
             "(its obstacles, tower slots and connection lines).\n" +
             "Turn off to restore the old ring-only behaviour.")]
    public bool respectActiveLayout = true;

    [Tooltip("SIMPLE MODE (recommended): push every biome decoration outside a single\n" +
             "radius that encloses the whole layout footprint.\n" +
             "inner radius = (furthest layout element from the core) + Layout Clearance.\n" +
             "Uncheck to allow decorations between layout pieces — they'll still be\n" +
             "kept off individual obstacles by the per-obstacle keep-outs below.")]
    public bool useCircularKeepout = true;

    [Tooltip("Gap between the outermost layout element and the first biome decoration " +
             "(world units). Used when 'Use Circular Keepout' is on.")]
    public float layoutClearance = 3f;

    [Tooltip("Also build a keep-out disc around EVERY individual layout obstacle. " +
             "Cheap safety net — catches augment arches and anything spawned after " +
             "the annulus was computed.")]
    public bool avoidLayoutObstaclesIndividually = true;

    [Tooltip("Padding around each individual layout obstacle (world units).")]
    public float obstacleClearance = 2f;

    [Tooltip("Padding around each tower slot, so decorations never cover a build spot.")]
    public float slotClearance = 1.5f;

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

    // Cached at the start of GenerateObstacles() so every spawn check uses the
    // same player position even if the player moves during generation.
    // null = no player found / exclusion disabled.
    private Vector2? cachedPlayerPos;

    // Keep-out discs built from the active layout (obstacles + tower slots).
    // Radii already include their clearance padding.
    private readonly List<KeepoutDisc> layoutKeepouts = new List<KeepoutDisc>();

    // True once GenerateObstacles() has produced a container. Used by
    // NotifyLayoutChanged() so a layout swap can rebuild the decorations.
    public bool HasGenerated => containerGO != null;

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

        // Cache the player position once. Every solo / cluster member spawn
        // will avoid this point by `minDistanceFromPlayer`.
        cachedPlayerPos = null;
        if (minDistanceFromPlayer > 0f && !string.IsNullOrEmpty(playerTag))
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag(playerTag);
            if (playerGO != null)
                cachedPlayerPos = (Vector2)playerGO.transform.position;
        }

        // Build the layout keep-out discs BEFORE any placement so every
        // candidate position can be tested against the designed terrain.
        BuildLayoutKeepouts();

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

        // Check if we have solo prefabs
        bool hasSolo = false;
        if (soloPrefabs != null)
            foreach (var p in soloPrefabs)
                if (p != null) { hasSolo = true; break; }

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

        if (allValid.Count == 0 && !hasSolo)
        {
            Debug.LogWarning("[ObstacleGenerator] No obstacle prefabs assigned — skipping.");
            return;
        }
        if (heroPrefabs.Count == 0) heroPrefabs.AddRange(allValid);
        if (accentPrefabs.Count == 0) accentPrefabs.AddRange(allValid);

        float innerRadius = ResolveInnerRadius();
        float outerRadius = ResolveOuterRadius();

        //Debug.Log($"[ObstacleGenerator] Zone: inner={innerRadius:F1}, outer={outerRadius:F1}, " +
        //          $"count={obstacleCount}, prefabs={allValid.Count}, solo={hasSolo}");

        if (outerRadius <= innerRadius)
        {
            Debug.LogWarning($"[ObstacleGenerator] Outer radius ({outerRadius:F1}) <= inner radius ({innerRadius:F1}). No room. " +
                             "Lower 'Layout Clearance' / 'Extra Inner Padding', or grow the border ring.");
            return;
        }

        containerGO = new GameObject("Obstacles_Container");
        containerGO.transform.SetParent(null);
        containerGO.transform.position = Vector3.zero;

        // Track anchor positions for our own distance checks.
        // Not using Physics2D.OverlapCircle because our own spawned colliders
        List<Vector2> anchors = new List<Vector2>();
        int placedCount = 0, totalSprites = 0, clusters = 0;

        // Main cluster/solo loop (only if we have regular prefabs)
        if (allValid.Count > 0 && obstacleCount > 0)
        {
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

                // Layout keep-out — never anchor a placement on designed terrain.
                // Widen by clusterSpread when this attempt could become a cluster
                // so members radiating outward stay clear too.
                float layoutMargin = enableClusters ? clusterSpread : 0f;
                if (IsBlockedByLayout(anchor, layoutMargin)) continue;

                // Player exclusion — the anchor is the cluster centre, so we
                // also widen the check by clusterSpread when this attempt
                // would become a cluster (members radiate outward from anchor).
                if (cachedPlayerPos.HasValue)
                {
                    float playerExcl = minDistanceFromPlayer;
                    if (enableClusters) playerExcl += clusterSpread;
                    if (Vector2.Distance(anchor, cachedPlayerPos.Value) < playerExcl)
                        continue;
                }

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

            if (placedCount < obstacleCount)
            {
                Debug.LogWarning($"[ObstacleGenerator] Only placed {placedCount}/{obstacleCount} " +
                                 $"decorations — the free ring (inner={innerRadius:F1}, outer={outerRadius:F1}) " +
                                 "is too tight. Reduce clearance, or lower Min Distance Between Obstacles.");
            }
        }

        //Debug.Log($"[ObstacleGenerator] {placedCount} placements ({clusters} clusters, " +
        //          $"{placedCount - clusters} solo) = {totalSprites} sprites.");

        // Solo-only prefabs — always spawned individually, never in clusters
        int soloSpawned = SpawnSoloOnlyPrefabs(anchors, innerRadius, outerRadius);
        if (soloSpawned > 0)
            Debug.Log($"[ObstacleGenerator] + {soloSpawned} solo-only obstacles.");
    }

    // Called by TowerDefenseMap after a layout is (re)built. Rebuilds the
    // decorations against the NEW layout footprint — but only if we already
    // generated once, so the first-frame ordering (biome vs. layout) doesn't
    // spawn a double set.
    public void NotifyLayoutChanged()
    {
        if (!HasGenerated) return;
        GenerateObstacles();
    }

    //  Solo-only prefab spawning (never clustered) 

    private int SpawnSoloOnlyPrefabs(List<Vector2> anchors, float zoneInner, float zoneOuter)
    {
        // Collect valid solo prefabs
        List<GameObject> validSolo = new List<GameObject>();
        if (soloPrefabs != null)
            foreach (var p in soloPrefabs)
                if (p != null) validSolo.Add(p);

        if (validSolo.Count == 0) return 0;

        // Guard against soloCount being 0 (e.g. Unity serialized old default).
        // If solo prefabs are assigned, spawn at least 10.
        int count = soloCount > 0 ? soloCount : 10;

        int spawned = 0;
        int maxAttempts = count * 20;

        for (int i = 0; i < count; i++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < maxAttempts && !placed; attempt++)
            {
                float angle = Random.Range(0f, 2f * Mathf.PI);
                float dist = Mathf.Sqrt(Random.Range(zoneInner * zoneInner, zoneOuter * zoneOuter));
                Vector2 pos = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);

                // Distance check against all existing anchors (clusters + solos)
                bool tooClose = false;
                for (int a = 0; a < anchors.Count; a++)
                {
                    if (Vector2.Distance(pos, anchors[a]) < minDistanceBetweenObstacles)
                    { tooClose = true; break; }
                }
                if (tooClose) continue;

                // Layout keep-out
                if (IsBlockedByLayout(pos, 0f)) continue;

                // Player exclusion
                if (cachedPlayerPos.HasValue &&
                    Vector2.Distance(pos, cachedPlayerPos.Value) < minDistanceFromPlayer)
                    continue;

                GameObject prefab = validSolo[Random.Range(0, validSolo.Count)];
                float s = baseScale * Random.Range(1f - scaleVariation, 1f + scaleVariation);
                SpawnSinglePrefab(prefab, pos, s);

                anchors.Add(pos);
                spawned++;
                placed = true;
            }
        }
        return spawned;
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

            // Per-member layout keep-out (the anchor was checked with a margin,
            // but members can still drift onto a wall on non-circular setups).
            if (IsBlockedByLayout(pos, 0f)) continue;

            // Per-member player exclusion (anchor was already checked, but
            // members radiate outward and could still land on the player).
            if (cachedPlayerPos.HasValue &&
                Vector2.Distance(pos, cachedPlayerPos.Value) < minDistanceFromPlayer)
                continue;

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

            // Per-member layout keep-out.
            if (IsBlockedByLayout(pos, 0f)) continue;

            // Per-member player exclusion.
            if (cachedPlayerPos.HasValue &&
                Vector2.Distance(pos, cachedPlayerPos.Value) < minDistanceFromPlayer)
                continue;

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

    //  Layout keep-out 

    // Rebuilds `layoutKeepouts` from the map's ACTIVE layout (which is already
    // the layoutSpreadScale-scaled clone, so no extra scaling needed here).
    private void BuildLayoutKeepouts()
    {
        layoutKeepouts.Clear();
        if (!respectActiveLayout || !avoidLayoutObstaclesIndividually) return;

        TowerDefenseMap map = FindMap();
        if (map == null) return;

        MapLayoutDefinition layout = map.activeLayout;
        if (layout != null && layout.obstacles != null)
        {
            foreach (var o in layout.obstacles)
                AddObstacleKeepout(o);
        }

        // Live tower slots (covers custom slots, ring slots, bonus slots added
        // by augments — anything actually spawned in the scene right now).
        var slots = map.GetAllSlots();
        if (slots != null)
        {
            foreach (var s in slots)
            {
                if (s == null) continue;
                AddDisc(s.transform.position, slotClearance + 0.5f);
            }
        }

        // Authored slot positions too, in case the map hasn't been built yet
        // when the biome applies (first frame ordering).
        if (layout != null)
        {
            if (layout.customSlotPositions != null)
                foreach (var p in layout.customSlotPositions)
                    AddDisc(p, layout.customSlotSize * 0.5f + slotClearance);
            if (layout.bonusSlotPositions != null)
                foreach (var p in layout.bonusSlotPositions)
                    AddDisc(p, layout.bonusSlotSize * 0.5f + slotClearance);
        }
    }

    private void AddObstacleKeepout(MapLayoutDefinition.LayoutObstacle o)
    {
        switch (o.shape)
        {
            case MapLayoutDefinition.ObstacleShape.Circle:
                AddDisc(o.position, o.size.x * 0.5f + obstacleClearance);
                break;

            // Ellipse / Crescent / Rectangle are all oblong: cover the long axis
            // with a chain of discs instead of one huge bounding circle, so a
            // long wall doesn't blank out a whole quadrant of the map.
            default:
                AddOblongKeepout(o.position, o.size, o.rotationDegrees, obstacleClearance);
                break;
        }
    }

    private void AddOblongKeepout(Vector2 center, Vector2 size, float rotationDeg, float pad)
    {
        float halfLong = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f;
        float halfShort = Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f;

        // Roughly square → one disc is enough.
        float span = halfLong - halfShort;
        if (span < 0.01f)
        {
            AddDisc(center, halfLong + pad);
            return;
        }

        // Direction of the long axis in world space.
        float axisDeg = rotationDeg + (Mathf.Abs(size.x) >= Mathf.Abs(size.y) ? 0f : 90f);
        float rad = axisDeg * Mathf.Deg2Rad;
        Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        float discRadius = halfShort + pad;
        int steps = Mathf.Max(1, Mathf.CeilToInt(span / Mathf.Max(0.25f, halfShort)));
        for (int i = -steps; i <= steps; i++)
        {
            float t = (float)i / steps;
            AddDisc(center + axis * (span * t), discRadius);
        }
    }

    private void AddDisc(Vector2 center, float radius)
    {
        if (radius <= 0f) return;
        layoutKeepouts.Add(new KeepoutDisc { center = center, radius = radius });
    }

    // True when `p` (grown by `margin`) overlaps any layout keep-out disc.
    private bool IsBlockedByLayout(Vector2 p, float margin)
    {
        if (layoutKeepouts.Count == 0) return false;
        for (int i = 0; i < layoutKeepouts.Count; i++)
        {
            float r = layoutKeepouts[i].radius + margin;
            if ((p - layoutKeepouts[i].center).sqrMagnitude < r * r)
                return true;
        }
        return false;
    }

    //  Radius resolution 

    // Inner edge of the decoration annulus.
    //   1. start from the manual override, or the auto ring/layout estimate
    //   2. when respectActiveLayout + useCircularKeepout are on, push it out
    //      past the whole layout footprint + layoutClearance
    private float ResolveInnerRadius()
    {
        float inner = (minDistanceFromCenter >= 0f)
            ? minDistanceFromCenter
            : AutoInnerRadius();

        if (respectActiveLayout && useCircularKeepout)
        {
            float footprint = LayoutFootprintRadius();
            if (footprint > 0f)
                inner = Mathf.Max(inner, footprint + layoutClearance);
        }

        return inner;
    }

    // Original ring-based estimate (kept for backwards compatibility), but now
    // it prefers the ACTIVE layout's rings over the scene's default rings.
    private float AutoInnerRadius()
    {
        TowerDefenseMap map = FindMap();
        if (map == null) return 5f;

        List<TowerDefenseMap.RingConfiguration> sourceRings = ResolveRingSource(map);
        if (sourceRings != null)
        {
            float maxRingRadius = 0f;
            float lastSlotSize = 1f;
            foreach (var ring in sourceRings)
            {
                if (ring != null && ring.enabled && ring.radius > maxRingRadius)
                {
                    maxRingRadius = ring.radius;
                    lastSlotSize = ring.slotSize;
                }
            }
            if (maxRingRadius > 0f)
                return maxRingRadius + lastSlotSize * 0.5f + 1f + extraInnerPadding;
        }
        return 5f;
    }

    // Which ring list actually drives the slots right now (mirrors
    // TowerDefenseMap.CreateTowerSlots): the layout's rings when it defines
    // them, otherwise the scene rings. Custom layouts use no rings at all.
    private List<TowerDefenseMap.RingConfiguration> ResolveRingSource(TowerDefenseMap map)
    {
        var layout = map.activeLayout;
        if (layout != null && layout.layoutType == MapLayoutDefinition.LayoutType.Custom)
            return null;
        if (layout != null && layout.rings != null && layout.rings.Count > 0)
            return layout.rings;
        return map.rings;
    }

    // Distance from the core to the furthest point of the designed map:
    // rings, custom slots, bonus slots, obstacles and connection lines.
    // Returns -1 when there's nothing to measure.
    private float LayoutFootprintRadius()
    {
        TowerDefenseMap map = FindMap();
        if (map == null) return -1f;

        float max = -1f;

        // Rings (concentric layouts / no layout).
        var sourceRings = ResolveRingSource(map);
        if (sourceRings != null)
        {
            foreach (var ring in sourceRings)
            {
                if (ring == null || !ring.enabled) continue;
                max = Mathf.Max(max, ring.radius + ring.slotSize * 0.5f);
            }
        }

        MapLayoutDefinition layout = map.activeLayout;
        if (layout != null)
        {
            if (layout.customSlotPositions != null)
                foreach (var p in layout.customSlotPositions)
                    max = Mathf.Max(max, p.magnitude + layout.customSlotSize * 0.5f);

            if (layout.bonusSlotPositions != null)
                foreach (var p in layout.bonusSlotPositions)
                    max = Mathf.Max(max, p.magnitude + layout.bonusSlotSize * 0.5f);

            if (layout.obstacles != null)
                foreach (var o in layout.obstacles)
                    max = Mathf.Max(max, o.position.magnitude + ObstacleBoundingRadius(o));

            if (layout.connectionLines != null)
                foreach (var line in layout.connectionLines)
                {
                    if (line == null || line.points == null) continue;
                    foreach (var p in line.points)
                        max = Mathf.Max(max, p.magnitude + line.width);
                }
        }

        // Live slots — catches augment-added rings / revealed bonus slots.
        var slots = map.GetAllSlots();
        if (slots != null)
        {
            foreach (var s in slots)
            {
                if (s == null) continue;
                max = Mathf.Max(max, ((Vector2)s.transform.position).magnitude + 1f);
            }
        }

        return max;
    }

    // Conservative bounding circle of a layout obstacle (half-diagonal for
    // rectangles, half-major-axis for the round shapes).
    private float ObstacleBoundingRadius(MapLayoutDefinition.LayoutObstacle o)
    {
        switch (o.shape)
        {
            case MapLayoutDefinition.ObstacleShape.Circle:
                return Mathf.Abs(o.size.x) * 0.5f;
            case MapLayoutDefinition.ObstacleShape.Ellipse:
            case MapLayoutDefinition.ObstacleShape.Crescent:
                return Mathf.Max(Mathf.Abs(o.size.x), Mathf.Abs(o.size.y)) * 0.5f;
            case MapLayoutDefinition.ObstacleShape.Rectangle:
            default:
                return new Vector2(o.size.x, o.size.y).magnitude * 0.5f; // half diagonal
        }
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

    private TowerDefenseMap FindMap()
    {
        return FindFirstObjectByType<TowerDefenseMap>();
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

        // Layout keep-out discs (magenta). Only populated after a generate;
        // rebuild them on the fly in the editor so the gizmo is always useful.
        if (respectActiveLayout && avoidLayoutObstaclesIndividually && !Application.isPlaying)
            BuildLayoutKeepouts();

        Gizmos.color = new Color(1f, 0.3f, 1f, 0.5f);
        for (int i = 0; i < layoutKeepouts.Count; i++)
            DrawGizmoCircle(layoutKeepouts[i].center, layoutKeepouts[i].radius);

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

    private struct KeepoutDisc
    {
        public Vector2 center;
        public float radius;
    }

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
