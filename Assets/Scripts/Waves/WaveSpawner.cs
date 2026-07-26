using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

// WAVE SPAWNER
// MODE 1 (STANDALONE): Works exactly like before — auto-advances waves.
// MODE 2 (ORCHESTRATOR-DRIVEN): Orchestrator calls SpawnEnemyPublic().

public class WaveSpawner : MonoBehaviour
{
    [Header("Spawn areas (Top/Bottom/Left/Right)")]
    public List<Collider2D> spawnAreas;

    [Header("Config")]
    public WaveConfig waveConfig;

    [Header("Modifiers")]
    public float waveSpawnDelayModifier = 0f;
    public float enemySpawnCountMultiplier = 1f;

    [Header("Obstacle Avoidance (must match TowerDefenseMap.obstacleLayerName)")]
    [Tooltip("Physics layers considered solid when picking a spawn position.\n" +
             "Set to the same 'Obstacle' layer used by TowerDefenseMap layout obstacles.\n" +
             "Leave at 'Nothing' to disable the check (legacy behaviour — enemies may " +
             "spawn inside walls).")]
    public LayerMask obstacleAvoidanceLayers;

    [Tooltip("Radius around the candidate spawn point that must be free of obstacles. " +
             "Should be ~enemy collider radius (a small bit larger is safer).")]
    public float obstacleClearanceRadius = 0.6f;

    [Tooltip("How many random points to try inside the spawn area before falling back " +
             "to a nudge-toward-area-edge strategy.")]
    public int obstacleAvoidanceMaxAttempts = 12;

    [Header("Wave direction indicators")]
    [Tooltip("Master toggle for the subtle pulsing arc that telegraphs which side a wave spawns from.")]
    public bool showWaveIndicators = true;

    [Tooltip("STANDALONE mode only: show the arc this many seconds BEFORE the wave actually spawns, " +
             "as an early warning. Orchestrator-driven runs show it when the orchestrator triggers the wave.")]
    public float indicatorLeadTime = 3f;

    [Tooltip("Show an arc when an enemy ACTUALLY spawns from a direction, and refresh it on " +
             "every further spawn from that side. This is the accurate source of truth and the " +
             "only path that works in orchestrator mode. Leave ON.")]
    public bool indicateOnSpawn = true;

    [Tooltip("Honour direction lists passed to ShowWaveIndicatorsPublic() by an external caller " +
             "(e.g. GameOrchestrator). OFF by default: callers typically pass every AUTHORED " +
             "direction from WaveData, while enemies only use a subset — which is what makes the " +
             "arcs appear to lie. With indicateOnSpawn ON you do not need this.")]
    public bool trustCallerDirections = false;

    [Tooltip("Log every arc trigger with its direction. Use to confirm arcs match real spawns.")]
    public bool debugLogIndicators = false;

    [Tooltip("Look & feel of the wave arc (colour, span, pulse, sorting).")]
    public WaveIndicatorStyle waveIndicatorStyle = new WaveIndicatorStyle();

    // Guards the standalone early-warning so it fires once per wave.
    private int indicatorsShownForWave = -1;

    // Live arcs — one per (direction x player camera). Ticked from LateUpdate().
    private readonly List<ActiveArc> activeArcs = new List<ActiveArc>();

    // Resolved player cameras, rebuilt whenever a wave is telegraphed.
    private readonly List<(Camera cam, int playerIndex)> _playerCams = new List<(Camera, int)>();

    // Original cullingMask per camera, captured before we carve out the arc layers.
    private Dictionary<Camera, int> _maskedCams;

    private int currentWaveIndex = 0;
    private int enemiesAlive = 0;
    private float countdown;

    private bool resourcesPreloaded = false;

    public bool IsOrchestratorMode =>
        GameOrchestrator.Instance != null &&
        GameOrchestrator.Instance.CurrentState != GameOrchestrator.RunState.Idle;

    void Start()
    {
        // If the obstacle avoidance mask wasn't set in the inspector, derive it from TowerDefenseMap.obstacleLayerName at runtime. 
        if (obstacleAvoidanceLayers.value == 0)
        {
            string layerName = "Obstacle";
            var mapInstance = FindFirstObjectByType<TowerDefenseMap>();
            if (mapInstance != null && !string.IsNullOrEmpty(mapInstance.obstacleLayerName))
                layerName = mapInstance.obstacleLayerName;

            int layerIndex = LayerMask.NameToLayer(layerName);
            if (layerIndex >= 0)
            {
                obstacleAvoidanceLayers = (LayerMask)(1 << layerIndex);
                Debug.Log($"[WaveSpawner] Auto-configured obstacleAvoidanceLayers to '{layerName}' (bit {layerIndex}).");
            }
            else
            {
                Debug.LogWarning($"[WaveSpawner] Layer '{layerName}' not found. Spawn-obstacle avoidance is disabled — enemies may spawn inside walls.");
            }
        }

        if (waveConfig == null)
        {
            if (!IsOrchestratorMode)
                Debug.LogError("No assigned WaveConfig for the WaveSpawner!");
            return;
        }

        ValidateSpawnAreas();

        countdown = GetModifiedWaveDelay();
        StartCoroutine(PreloadEnemyResources());
    }

    // Spawn areas are matched to a SpawnDirection by NAME, never by list order — so the
    // order of Elements 0..3 in the Inspector is irrelevant. What DOES matter is that a
    // collider called "Top" actually sits above the core, etc. If a name and its position
    // disagree, arcs and enemies still agree with each other (both use this lookup), but
    // both will point somewhere the designer did not expect. Warn loudly.
    private void ValidateSpawnAreas()
    {
        if (spawnAreas == null) return;

        foreach (SpawnDirection d in System.Enum.GetValues(typeof(SpawnDirection)))
        {
            Collider2D area = spawnAreas.Find(c => c != null &&
                c.name.Equals(d.ToString(), StringComparison.OrdinalIgnoreCase));

            if (area == null)
            {
                Debug.LogWarning($"[WaveSpawner] No spawn area named '{d}'. Enemies for that " +
                                 $"direction will fall back to another area, and its arc will " +
                                 $"point at that fallback.");
                continue;
            }

            Vector2 c2 = area.bounds.center;
            Vector2 expect = CardinalUnit(d);
            // Dot < 0 means the area sits on the OPPOSITE side of the core from its name.
            if (Vector2.Dot(c2.normalized, expect) < 0f)
            {
                Debug.LogWarning($"[WaveSpawner] Spawn area '{area.name}' is positioned at {c2}, " +
                                 $"which is on the opposite side of the core from '{d}'. " +
                                 $"Its wave arc will point at {c2} — rename or move the collider.");
            }
        }
    }

    private IEnumerator PreloadEnemyResources()
    {
        if (waveConfig == null || waveConfig.waves == null)
        {
            resourcesPreloaded = true;
            yield break;
        }

        HashSet<GameObject> uniquePrefabs = new HashSet<GameObject>();

        foreach (var wave in waveConfig.waves)
        {
            if (wave.enemies == null) continue;
            foreach (var group in wave.enemies)
            {
                if (group != null && group.enemyPrefab != null)
                    uniquePrefabs.Add(group.enemyPrefab);
            }
        }

        foreach (var prefab in uniquePrefabs)
        {
            GameObject warmup = Instantiate(prefab, new Vector3(-9999f, -9999f, 0f), Quaternion.identity);
            warmup.SetActive(true);
            yield return null;
            Destroy(warmup);
            yield return null;
        }

        resourcesPreloaded = true;
    }

    void Update()
    {
        // NOTE: the wave arcs are driven from LateUpdate(), NOT here — they must be
        // re-aimed after the camera has moved for the frame, and they must keep
        // pulsing through every early-return below (orchestrator mode, enemies alive,
        // waves exhausted). All of those returns are about ADVANCING waves only.

        // ORCHESTRATOR MODE: don't auto-advance
        if (IsOrchestratorMode) return;

        // STANDALONE MODE: original behavior
        if (waveConfig == null) return;
        if (!resourcesPreloaded) return;
        if (enemiesAlive > 0) return;
        if (currentWaveIndex >= waveConfig.waves.Count) return;

        countdown -= Time.deltaTime;

        // Early-warning arc: telegraph the coming wave a few seconds before it spawns.
        // Fires once per wave; SpawnWave() refreshes the same arcs when enemies actually
        // start appearing, so they hold through the spawn then fade out on their own.
        //
        // Reads the CACHED plan, so the sides promised here are exactly the sides the
        // enemies will use — EnsurePlan rolls the directions once and SpawnWave reuses it.
        if (showWaveIndicators
            && indicatorsShownForWave != currentWaveIndex
            && countdown <= indicatorLeadTime)
        {
            indicatorsShownForWave = currentWaveIndex;
            ShowWaveIndicators(EnsurePlan(currentWaveIndex).usedDirections);
        }

        if (countdown <= 0f)
        {
            StartCoroutine(SpawnWave(currentWaveIndex));
            countdown = GetModifiedWaveDelay();
        }
    }

    private float GetModifiedWaveDelay()
    {
        return waveConfig.timeBetweenWaves + waveSpawnDelayModifier;
    }

    IEnumerator SpawnWave(int index)
    {
        if (index < 0 || index >= waveConfig.waves.Count)
        {
            Debug.LogWarning("SpawnWave: invalid index " + index);
            yield break;
        }

        WaveData wave = waveConfig.waves[index];

        if (wave.extraDelayBeforeStart > 0)
            yield return new WaitForSeconds(wave.extraDelayBeforeStart);

        // Decide WHO spawns and FROM WHERE up front, then indicate only the directions
        // this plan actually uses. Previously the indicators were fed wave.spawnDirections
        // (every AUTHORED direction) while the spawn loop then picked a subset — so with
        // oneDirectionForAllEnemies the arcs promised 3 sides and enemies came from 1.
        WavePlan plan = EnsurePlan(index);

        ShowWaveIndicators(plan.usedDirections);

        // NOTE: music is NOT driven from here any more. MusicDirector is the single
        // owner of the MusicSection parameter and reacts to GameOrchestrator's
        // RunState. This block used to fight the orchestrator's own Intense/Calm
        // calls over the same parameter.

        for (int i = 0; i < plan.prefabs.Count; i++)
        {
            SpawnEnemy(plan.prefabs[i], plan.directions[i]);

            float delay = UnityEngine.Random.Range(wave.minSpawnDelay, wave.maxSpawnDelay);
            yield return new WaitForSeconds(delay);
        }

        ClearPlan();
        currentWaveIndex++;
    }

    //  WAVE PLANNING
    //
    //  The plan is the single source of truth for a wave: the exact prefab list, and the
    //  exact direction each one spawns from. The indicators read plan.usedDirections, the
    //  spawn loop reads plan.prefabs/plan.directions — so an arc can never point at a side
    //  no enemy uses.
    //
    //  It is built ONCE per wave (on the early-warning, or on first use) and cached, so the
    //  arc shown seconds before the wave matches the enemies that then arrive. Rebuilding it
    //  at spawn time would re-roll the random directions and reintroduce the mismatch.

    private WavePlan _plan;
    private int _planIndex = -1;

    private WavePlan EnsurePlan(int index)
    {
        if (_plan != null && _planIndex == index) return _plan;
        _plan = BuildWavePlan(index);
        _planIndex = index;
        return _plan;
    }

    private void ClearPlan()
    {
        _plan = null;
        _planIndex = -1;
    }

    private WavePlan BuildWavePlan(int index)
    {
        var plan = new WavePlan();
        WaveData wave = waveConfig.waves[index];

        // Expand groups into a flat prefab list, honouring the count multiplier.
        if (wave.enemies != null)
        {
            foreach (var group in wave.enemies)
            {
                if (group == null) continue;
                if (group.enemyPrefab == null) continue;
                if (group.count <= 0) continue;

                int modifiedCount = Mathf.Max(1, Mathf.RoundToInt(group.count * enemySpawnCountMultiplier));
                for (int i = 0; i < modifiedCount; i++)
                    plan.prefabs.Add(group.enemyPrefab);
            }
        }

        Shuffle(plan.prefabs);

        bool hasDirs = wave.spawnDirections != null && wave.spawnDirections.Count > 0;

        SpawnDirection chosenDir = SpawnDirection.Top;
        if (hasDirs && wave.oneDirectionForAllEnemies)
            chosenDir = wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];

        // Assign a direction per enemy — the same rolls the spawn loop used to make inline.
        for (int i = 0; i < plan.prefabs.Count; i++)
        {
            SpawnDirection dir;
            if (!hasDirs)
                dir = chosenDir;
            else
                dir = wave.oneDirectionForAllEnemies
                    ? chosenDir
                    : wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];

            plan.directions.Add(dir);

            // Distinct set — only these get an arc. A direction that no enemy rolled
            // (easy with few enemies and several authored directions) shows nothing.
            if (!plan.usedDirections.Contains(dir)) plan.usedDirections.Add(dir);
        }

        return plan;
    }

    private class WavePlan
    {
        public readonly List<GameObject> prefabs = new List<GameObject>();
        public readonly List<SpawnDirection> directions = new List<SpawnDirection>();
        public readonly List<SpawnDirection> usedDirections = new List<SpawnDirection>();
    }

    /// Called by EnemyStats.PerformDeath() for ALL enemies (wave + gremlins + anything).
    /// This ONLY manages the spawner's internal count.
    /// It does NOT notify the orchestrator — WaveEnemy.OnDestroy() handles that.
    ///
    /// Music was removed from here deliberately. This counter hits 0 constantly during
    /// an orchestrated wave — the orchestrator spawns through SpawnEnemyPublic with up
    /// to ~1.5s between spawns, so killing enemy #1 before enemy #2 appears dropped the
    /// count to 0 and yanked the music to Calm mid-fight. It also fired on gremlin and
    /// boss deaths. MusicDirector reads the orchestrator's RunState instead.
    public void OnEnemyDeath()
    {
        enemiesAlive--;
    }

    Vector2 GetRandomPositionInArea(SpawnDirection direction, float clearanceRadius = -1f)
    {
        Collider2D area = spawnAreas.Find(c => c != null && c.name.Equals(direction.ToString(), StringComparison.OrdinalIgnoreCase));
        if (area == null)
        {
            // Fall back to ANY configured spawn area so the enemy at least
            // appears on the map perimeter rather than at its centre.
            Collider2D fallback = spawnAreas.Find(c => c != null);
            if (fallback != null)
            {
                Debug.LogWarning($"[WaveSpawner] No spawn area found for direction '{direction}'. " +
                                 $"Falling back to '{fallback.name}'. Configure all four directions " +
                                 $"in the WaveSpawner Inspector to silence this warning.");
                area = fallback;
            }
            else
            {
                Debug.LogError($"[WaveSpawner] No spawn areas configured at all. Cannot spawn for '{direction}'. " +
                               $"Returning a perimeter point as best-effort.");
                // Last resort: a fixed-radius point on the map perimeter in
                // some direction. Better than (0, 0) which is the core.
                return new Vector2(0f, 12f);
            }
        }

        Bounds bounds = area.bounds;

        // Clearance radius
        float clearance = (clearanceRadius > 0f) ? clearanceRadius : obstacleClearanceRadius;

        // If the avoidance layer mask is empty, skip the check entirely (preserve
        // original behaviour and avoid Physics2D calls).
        bool avoidanceEnabled = obstacleAvoidanceLayers.value != 0;

        // Try N random points inside the spawn rectangle.
        int attempts = avoidanceEnabled ? Mathf.Max(1, obstacleAvoidanceMaxAttempts) : 1;
        for (int i = 0; i < attempts; i++)
        {
            float x = UnityEngine.Random.Range(bounds.min.x, bounds.max.x);
            float y = UnityEngine.Random.Range(bounds.min.y, bounds.max.y);
            Vector2 candidate = new Vector2(x, y);

            if (!avoidanceEnabled) return candidate;

            if (!Physics2D.OverlapCircle(candidate, clearance, obstacleAvoidanceLayers))
                return candidate;
        }

        // Fallback
        Vector2[] corners = new Vector2[]
        {
            new Vector2(bounds.min.x, bounds.min.y),
            new Vector2(bounds.max.x, bounds.min.y),
            new Vector2(bounds.min.x, bounds.max.y),
            new Vector2(bounds.max.x, bounds.max.y),
        };

        Vector2 best = corners[0];
        float bestDistSq = best.sqrMagnitude;
        for (int i = 1; i < corners.Length; i++)
        {
            float d = corners[i].sqrMagnitude;
            if (d > bestDistSq) { best = corners[i]; bestDistSq = d; }
        }

        if (!Physics2D.OverlapCircle(best, clearance, obstacleAvoidanceLayers))
            return best;

        // Walk along the rectangle edge that's FARTHEST from the map centre
        Vector2 edgeStart, edgeEnd;
        float absCx = Mathf.Abs(bounds.center.x);
        float absCy = Mathf.Abs(bounds.center.y);
        if (absCy >= absCx)
        {
            // Top or Bottom area: outer edge is horizontal at the far y.
            float outerY = bounds.center.y >= 0f ? bounds.max.y : bounds.min.y;
            edgeStart = new Vector2(bounds.min.x, outerY);
            edgeEnd = new Vector2(bounds.max.x, outerY);
        }
        else
        {
            // Left or Right area: outer edge is vertical at the far x.
            float outerX = bounds.center.x >= 0f ? bounds.max.x : bounds.min.x;
            edgeStart = new Vector2(outerX, bounds.min.y);
            edgeEnd = new Vector2(outerX, bounds.max.y);
        }

        const int perimeterSamples = 32;
        for (int i = 0; i < perimeterSamples; i++)
        {
            float t = i / (float)(perimeterSamples - 1);
            Vector2 p = Vector2.Lerp(edgeStart, edgeEnd, t);
            if (!Physics2D.OverlapCircle(p, clearance, obstacleAvoidanceLayers))
                return p;
        }

        Debug.LogWarning($"WaveSpawner: could not find an obstacle-free spawn point in '{direction}' " +
                         $"after {attempts} random + {perimeterSamples} perimeter attempts. " +
                         $"Spawning at outer corner — enemy may still clip a wall.");
        return best;
    }

    // Estimate the clearance radius needed for a given prefab by inspecting
    // its non-trigger Collider2D. Matches the OverlapCircle test we'll do
    // against obstacles, so a prefab whose body would clip a wall is rejected.
    private float GetPrefabClearanceRadius(GameObject prefab)
    {
        if (prefab == null) return obstacleClearanceRadius;

        Vector3 scale = prefab.transform.localScale;
        float scaleFactor = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        if (scaleFactor < 0.0001f) scaleFactor = 1f;

        // Special case: Boss1 sets its CircleCollider2D radius at runtime in
        // ConfigureBossCollider() based on its serialized bossColliderRadius field. 
        var boss1 = prefab.GetComponent<Boss1>();
        if (boss1 != null)
        {
            // Use reflection — bossColliderRadius is private. Accessing it
            // this way avoids touching Boss1's public surface.
            var field = typeof(Boss1).GetField("bossColliderRadius",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                float bossRadius = (float)field.GetValue(boss1);
                if (bossRadius > 0f)
                    return bossRadius * scaleFactor * 1.1f;
            }
        }

        // Look for the largest non-trigger collider on the prefab root.
        // For most enemies this is a CircleCollider2D; bosses and odd shapes
        // get the bounds-based fallback.
        Collider2D[] colliders = prefab.GetComponents<Collider2D>();
        float maxRadius = 0f;
        foreach (var c in colliders)
        {
            if (c == null || c.isTrigger) continue;
            float r;
            if (c is CircleCollider2D circle)
                r = circle.radius * scaleFactor;
            else
                r = Mathf.Max(c.bounds.extents.x, c.bounds.extents.y);
            if (r > maxRadius) maxRadius = r;
        }

        if (maxRadius <= 0f) return obstacleClearanceRadius;

        // Slight bump above body radius so the spawn point isn't just barely
        // clear — leaves some breathing room as enemies start moving.
        return maxRadius * 1.1f;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;

        if (spawnAreas == null) return;

        foreach (var area in spawnAreas)
        {
            if (area != null)
            {
                var bounds = area.bounds;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }

    void SpawnEnemy(GameObject prefab, SpawnDirection direction)
    {
        if (prefab == null)
        {
            Debug.LogWarning("SpawnEnemy: prefab == null");
            return;
        }

        float clearance = GetPrefabClearanceRadius(prefab);
        Vector2 spawnPosition = GetRandomPositionInArea(direction, clearance);

        // Fallback
        if (obstacleAvoidanceLayers.value != 0
            && Physics2D.OverlapCircle(spawnPosition, clearance, obstacleAvoidanceLayers))
        {
            spawnPosition = NudgeOutOfObstacle(spawnPosition, clearance);
        }

        GameObject enemyObj = Instantiate(prefab, spawnPosition, Quaternion.identity);

        // ★ Mark as wave enemy — WaveEnemy.OnDestroy() will notify orchestrator
        enemyObj.AddComponent<WaveEnemy>();

        EnemyStats stats = enemyObj.GetComponent<EnemyStats>();
        if (stats != null)
        {
            stats.ConfigureEnergyDrop(0.5f, 10);
        }

        enemiesAlive++;

        // GROUND TRUTH: an arc appears if and only if an enemy really spawned from this
        // side, and each spawn REFRESHES that arc's hold. This is the only indicator path
        // that survives orchestrator mode, where Update()/SpawnWave() never run and the
        // orchestrator alone decides directions. Config lists and caller-supplied
        // direction lists are both untrustworthy; an actual Instantiate is not.
        if (indicateOnSpawn) ShowWaveIndicator(direction);
    }

    // Iteratively pushes the spawn point outward (away from world origin)
    // by the clearance radius until it no longer overlaps any obstacle.
    // Bounded by a step count so it can't loop forever.
    private Vector2 NudgeOutOfObstacle(Vector2 start, float clearance)
    {
        // Direction outward from map centre (origin). If the start is exactly
        // at the origin (unlikely) pick an arbitrary direction.
        Vector2 outward = start.sqrMagnitude > 0.0001f ? start.normalized : Vector2.up;

        const int MAX_STEPS = 20;
        Vector2 current = start;
        for (int i = 0; i < MAX_STEPS; i++)
        {
            current += outward * clearance;
            if (!Physics2D.OverlapCircle(current, clearance, obstacleAvoidanceLayers))
                return current;
        }

        // Couldn't escape — log and return the last attempted position. The
        // post-spawn stuck-detection in EnemyController will eventually try
        // to recover, but this is a layout configuration issue worth fixing.
        Debug.LogWarning($"[WaveSpawner] Could not nudge spawn point out of obstacle after {MAX_STEPS} steps " +
                         $"from {start}. The spawn area may be entirely inside a layout obstacle.");
        return current;
    }

    //  PUBLIC API FOR ORCHESTRATOR

    public void SpawnEnemyPublic(GameObject prefab, SpawnDirection direction)
    {
        SpawnEnemy(prefab, direction);
    }

    /// Spawn without telegraphing a direction. Use for anything that is NOT a wave enemy
    /// (gremlins, chest guardians, ambient spawns) so it never lights a wave arc.
    public void SpawnEnemyUnindicated(GameObject prefab, SpawnDirection direction)
    {
        bool prev = indicateOnSpawn;
        indicateOnSpawn = false;
        try { SpawnEnemy(prefab, direction); }
        finally { indicateOnSpawn = prev; }
    }

    // NOTE: external callers (GameOrchestrator) generally hand us WaveData.spawnDirections —
    // the AUTHORED list — but then spawn enemies from only a subset of it. Honouring that list
    // lights arcs on sides no enemy ever uses. Ignored unless trustCallerDirections is set;
    // indicateOnSpawn covers this correctly instead.
    public void ShowWaveIndicatorsPublic(List<SpawnDirection> dirs)
    {
        if (!trustCallerDirections)
        {
            if (debugLogIndicators)
                Debug.Log("[WaveSpawner] ShowWaveIndicatorsPublic ignored (trustCallerDirections=false); " +
                          "arcs are driven by real spawns instead.");
            return;
        }
        ShowWaveIndicators(dirs);
    }


    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int k = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[k]) = (list[k], list[i]);
        }
    }

    public int GetCurrentWaveIndex()
    {
        return currentWaveIndex;
    }

    void ShowWaveIndicators(List<SpawnDirection> dirs)
    {
        if (dirs == null) return;
        foreach (var d in dirs) ShowWaveIndicator(d);
    }

    public void TestWave(int waveIndex)
    {
        if (waveIndex < 0 || waveIndex >= waveConfig.waves.Count)
        {
            Debug.LogWarning("Invalid wave index: " + waveIndex);
            return;
        }

        StopAllCoroutines();
        ClearPlan();   // a forced wave re-rolls its own directions
        StartCoroutine(SpawnWave(waveIndex));
    }

    //  WAVE DIRECTION ARCS
    //
    //  A subtle, pulsing arc telegraphing which side a wave spawns from. The arc is
    //  ATTACHED TO EACH PLAYER'S CAMERA: it is centred on that camera and re-aimed every
    //  frame at the bearing from the camera to the spawn area, so it follows the player
    //  around the map and always points at the incoming wave.
    //
    //  ONE ARC PER (DIRECTION x PLAYER CAMERA). A single world-space arc cannot follow two
    //  cameras at once in split-screen co-op, so each player gets their own.
    //
    //  PER-CAMERA ISOLATION. PlayerCamera.prefab ships with cullingMask = Everything, so
    //  without isolation player 1's arc would also render into player 2's view whenever it
    //  fell inside their frustum. Each arc therefore lives on a reserved layer keyed to its
    //  player (P0 -> 31, P1 -> 30 — the SAME convention PlacementModeScreenEffect uses for
    //  its private Volume), and every player camera masks OUT the other players' arc layers.
    //  Original culling masks are captured once and restored in OnDestroy.
    //
    //  SORTING: high order on the Default sorting layer. The night-darkness overlay
    //  (BiomeManager) is the highest thing in the world at 6000, fog/searchlight ~5000;
    //  7000 clears them.

    void ShowWaveIndicator(SpawnDirection direction)
    {
        if (!showWaveIndicators) return;
        if (debugLogIndicators) Debug.Log($"[WaveSpawner] arc -> {direction}");

        // Where the wave comes FROM, in world space. Arcs aim at this point.
        Vector2 target;
        if (!TryGetSpawnAreaCenter(direction, out target))
            target = CardinalUnit(direction) * Mathf.Max(1f, waveIndicatorStyle.fallbackRadius);

        // One arc per player camera.
        CollectPlayerCameras();
        for (int i = 0; i < _playerCams.Count; i++)
        {
            var pc = _playerCams[i];
            if (pc.cam == null) continue;

            // Already live for this direction on this camera? Extend its hold, never stack.
            bool found = false;
            for (int j = 0; j < activeArcs.Count; j++)
            {
                if (activeArcs[j].dir == direction && activeArcs[j].cam == pc.cam)
                {
                    activeArcs[j].life = Mathf.Max(activeArcs[j].life, waveIndicatorStyle.holdDuration);
                    found = true;
                    break;
                }
            }
            if (found) continue;

            activeArcs.Add(BuildArc(direction, target, pc.cam, pc.playerIndex));
        }

        ApplyArcLayerMasks();
    }

    /// <summary>Ease every live arc out (e.g. when a run ends).</summary>
    public void HideWaveIndicators()
    {
        for (int i = 0; i < activeArcs.Count; i++) activeArcs[i].life = 0f;
    }

    // Arcs are re-aimed from live camera positions, so this MUST run after the camera has
    // moved for the frame — hence LateUpdate, not Update. Driving it from Update would
    // leave the arc one frame behind the player, which reads as jitter while running.
    //
    // Unscaled time, matching PlayerDamageVignette / PlacementModeScreenEffect, so arcs
    // keep breathing while the game is time-frozen in placement mode.
    private void LateUpdate()
    {
        if (activeArcs.Count == 0) return;

        float dt = Time.unscaledDeltaTime;
        var st = waveIndicatorStyle;
        float fadeRate = st.fadeDuration > 0.0001f ? 1f / st.fadeDuration : 1000f;

        for (int i = activeArcs.Count - 1; i >= 0; i--)
        {
            var a = activeArcs[i];

            // The camera can be destroyed under us (player leaves / scene swap).
            if (a.cam == null || a.arc == null)
            {
                DestroyArc(a);
                activeArcs.RemoveAt(i);
                continue;
            }

            a.phase += dt * Mathf.Max(0.0001f, st.pulseSpeed);
            a.life -= dt;
            a.env = Mathf.MoveTowards(a.env, a.life > 0f ? 1f : 0f, fadeRate * dt);

            float pulse01 = 0.5f + 0.5f * Mathf.Sin(a.phase * Mathf.PI * 2f);

            // st.color.a is a CEILING: the pulse breathes below it, so the arc can never
            // flash brighter than authored. That is what keeps this subtle.
            float alpha = st.color.a * (1f - st.pulseDepth + st.pulseDepth * pulse01) * a.env;
            float width = st.lineWidth * (1f + st.widthPulse * pulse01);

            AimArc(a);   // follow the camera + re-point at the spawn side

            a.arc.widthMultiplier = width;
            if (a.arcMat != null)
                a.arcMat.color = new Color(st.color.r, st.color.g, st.color.b, alpha);

            if (a.life <= 0f && a.env <= 0.0001f)
            {
                DestroyArc(a);
                activeArcs.RemoveAt(i);
            }
        }

        if (activeArcs.Count == 0) RestoreCameraMasks();
    }

    // Rebuilds the arc's points around the CAMERA's current position, on the bearing from
    // that camera toward the spawn area. Cheap (48 points) and it means the arc both
    // follows the player and re-aims as they move relative to the spawn side.
    private void AimArc(ActiveArc a)
    {
        var st = waveIndicatorStyle;

        Vector2 camPos = a.cam.transform.position;
        Vector2 toTarget = a.target - camPos;

        // Player standing (almost) on the spawn area — keep the last good bearing rather
        // than letting Atan2 snap wildly through a near-zero vector.
        if (toTarget.sqrMagnitude > 1e-4f)
            a.bearing = Mathf.Atan2(toTarget.y, toTarget.x);

        // Fit to THIS camera. Every arc point lies exactly `radius` from the camera centre,
        // so staying inside the camera's inscribed circle (min of half-width, half-height)
        // guarantees the whole arc is on screen at any bearing and any span.
        float halfH = a.cam.orthographicSize;
        float halfW = halfH * a.cam.aspect;
        float radius = Mathf.Min(halfW, halfH) * Mathf.Clamp01(st.screenFill) - st.radiusInset;
        radius = Mathf.Max(0.25f, radius);

        float half = st.spanDegrees * 0.5f * Mathf.Deg2Rad;
        int segs = a.arc.positionCount - 1;
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            float ang = a.bearing - half + t * (2f * half);
            a.arc.SetPosition(i, new Vector3(
                camPos.x + Mathf.Cos(ang) * radius,
                camPos.y + Mathf.Sin(ang) * radius,
                0f));
        }
    }

    private ActiveArc BuildArc(SpawnDirection direction, Vector2 target, Camera cam, int playerIndex)
    {
        var st = waveIndicatorStyle;

        var a = new ActiveArc
        {
            dir = direction,
            cam = cam,
            target = target,
            playerIndex = playerIndex,
            env = 0f,
            life = st.holdDuration,
        };

        a.root = new GameObject($"WaveIndicator_{direction}_P{playerIndex}");
        // Parent to the camera purely for hierarchy tidiness + automatic teardown; the
        // LineRenderer writes world positions itself, so parenting does not move it.
        a.root.transform.SetParent(cam.transform, false);
        a.root.layer = ArcLayerFor(playerIndex);

        a.arc = a.root.AddComponent<LineRenderer>();
        a.arcMat = ConfigureLine(a.arc, st.lineWidth);
        a.arc.positionCount = 49;   // 48 segments

        // Taper the ribbon to nothing at the tips and fade alpha there too, so the arc
        // dissolves softly into the field instead of ending in hard stubs.
        a.arc.widthCurve = ArcTaperCurve();
        a.arc.colorGradient = ArcTipFadeGradient();

        AimArc(a);   // place it before its first render
        return a;
    }

    // Shared LineRenderer setup. Returns the unique material instance we animate.
    private Material ConfigureLine(LineRenderer lr, float width)
    {
        var st = waveIndicatorStyle;

        lr.useWorldSpace = true;
        // TransformZ keeps the ribbon flat in world XY — correct for a top-down 2D field.
        // View alignment would billboard toward one camera and skew in the other half.
        lr.alignment = LineAlignment.TransformZ;
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingLayerName = st.sortingLayerName;
        lr.sortingOrder = st.sortingOrder;

        // Sprites/Default respects 2D sorting, vertex colours and transparency, and is
        // already used (known-good under URP) by GrassCartoonOverlay.
        var mat = new Material(Shader.Find("Sprites/Default"));
        lr.material = mat;
        return mat;
    }

    //  Per-camera isolation

    // Reserved layer for a player's arcs. Mirrors PlacementModeScreenEffect's volume
    // layer convention exactly (P0 -> 31, P1 -> 30) so the two features agree on which
    // high layers are private per-player scratch space.
    private static int ArcLayerFor(int playerIndex) => Mathf.Clamp(31 - playerIndex, 8, 31);

    // Each player camera renders ONLY its own arc layer, never another player's. We touch
    // just the reserved arc bits and leave the rest of the mask alone, so a camera that
    // was set to Everything still sees everything else.
    private void ApplyArcLayerMasks()
    {
        if (_maskedCams == null) _maskedCams = new Dictionary<Camera, int>();

        int allArcBits = 0;
        for (int i = 0; i < _playerCams.Count; i++)
            allArcBits |= 1 << ArcLayerFor(_playerCams[i].playerIndex);

        for (int i = 0; i < _playerCams.Count; i++)
        {
            var pc = _playerCams[i];
            if (pc.cam == null) continue;

            if (!_maskedCams.ContainsKey(pc.cam))
                _maskedCams[pc.cam] = pc.cam.cullingMask;   // capture original once

            int mine = 1 << ArcLayerFor(pc.playerIndex);
            pc.cam.cullingMask = (_maskedCams[pc.cam] & ~allArcBits) | mine;
        }
    }

    private void RestoreCameraMasks()
    {
        if (_maskedCams == null) return;
        foreach (var kv in _maskedCams)
            if (kv.Key != null) kv.Key.cullingMask = kv.Value;
        _maskedCams.Clear();
    }

    // Resolve every player's camera. Prefers PlayerRegistry (same source PlayerDamageVignette
    // uses); falls back to PlayerRef scan, then Camera.main for a plain single-player scene.
    private void CollectPlayerCameras()
    {
        _playerCams.Clear();

        var reg = PlayerRegistry.Instance;
        if (reg != null && reg.All != null && reg.All.Count > 0)
        {
            var all = reg.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Camera != null)
                    _playerCams.Add((all[i].Camera, all[i].PlayerIndex));
        }

        if (_playerCams.Count == 0)
        {
            foreach (var pr in FindObjectsByType<PlayerRef>(FindObjectsSortMode.None))
                if (pr != null && pr.Camera != null)
                    _playerCams.Add((pr.Camera, pr.PlayerIndex));
        }

        if (_playerCams.Count == 0 && Camera.main != null)
            _playerCams.Add((Camera.main, 0));
    }

    private static void DestroyArc(ActiveArc a)
    {
        if (a.arcMat != null) Destroy(a.arcMat);
        if (a.root != null) Destroy(a.root);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < activeArcs.Count; i++) DestroyArc(activeArcs[i]);
        activeArcs.Clear();
        RestoreCameraMasks();
    }

    private bool TryGetSpawnAreaCenter(SpawnDirection direction, out Vector2 center)
    {
        center = Vector2.zero;
        if (spawnAreas == null) return false;

        Collider2D area = spawnAreas.Find(c => c != null &&
            c.name.Equals(direction.ToString(), StringComparison.OrdinalIgnoreCase));
        if (area == null) return false;

        center = area.bounds.center;
        return true;
    }

    private static Vector2 CardinalUnit(SpawnDirection direction)
    {
        switch (direction)
        {
            case SpawnDirection.Top: return Vector2.up;
            case SpawnDirection.Bottom: return Vector2.down;
            case SpawnDirection.Left: return Vector2.left;
            case SpawnDirection.Right: return Vector2.right;
            default: return Vector2.up;
        }
    }

    private static AnimationCurve ArcTaperCurve()
    {
        var c = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.14f, 1f),
            new Keyframe(0.86f, 1f),
            new Keyframe(1f, 0f));
        for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0.5f);
        return c;
    }

    private static Gradient ArcTipFadeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.16f),
                new GradientAlphaKey(1f, 0.84f),
                new GradientAlphaKey(0f, 1f),
            });
        return g;
    }

    //  Nested types — kept here so the whole effect lives in one file.

    // Plain data, no MonoBehaviour: WaveSpawner already has the LateUpdate() and OnDestroy()
    // this needs, so a per-arc component would only duplicate plumbing.
    private class ActiveArc
    {
        public SpawnDirection dir;
        public Camera cam;        // the camera this arc is attached to
        public Vector2 target;    // world point the arc aims at (spawn area centre)
        public int playerIndex;
        public float bearing;     // last good aim angle, radians
        public GameObject root;
        public LineRenderer arc;
        public Material arcMat;
        public float phase;       // pulse phase, cycles
        public float env;         // 0..1 fade envelope
        public float life;        // seconds of hold remaining
    }

    [System.Serializable]
    public class WaveIndicatorStyle
    {
        [Header("Shape")]
        [Tooltip("Angular width of the arc in degrees, measured at the player's camera. " +
                 "~40 is a restrained hint of a bracket; larger values wrap further around.")]
        public float spanDegrees = 40f;

        [Tooltip("Extra pull inward, in world units, after the arc has been fitted to the screen.")]
        public float radiusInset = 0.25f;

        [Tooltip("Fraction of the camera's smallest visible half-extent the arc may occupy. " +
                 "0.92 pushes the arc out near the screen edge; 1.0 lets it touch the edge.")]
        [Range(0.3f, 1f)] public float screenFill = 0.92f;

        [Tooltip("Fallback distance from the core used only when no matching spawn-area " +
                 "collider is found for a direction.")]
        public float fallbackRadius = 12f;

        [Header("Colour & weight")]
        [Tooltip("Arc colour — light purple (#E673FE), a whitened take on the crystal violet. " +
                 "The ALPHA here is the CEILING: the pulse breathes below it, so keep it modest " +
                 "(~0.2) for a subtle look.")]
        public Color color = new Color(0.90f, 0.45f, 1.00f, 0.20f);

        [Tooltip("Line thickness in world units at the arc's middle (tapers to nothing at the tips). " +
                 "Below ~0.03 the ribbon goes sub-2px in a split-screen half and shimmers.")]
        public float lineWidth = 0.04f;

        [Header("Pulse")]
        [Tooltip("Breaths per second. ~0.6 is a calm, subtle pulse.")]
        public float pulseSpeed = 0.6f;

        [Tooltip("How deep the alpha breathes. 0.6 = alpha swings between 40% and 100% of the ceiling.")]
        [Range(0f, 1f)] public float pulseDepth = 0.60f;

        [Tooltip("Extra width added at the peak of each breath, as a fraction of lineWidth.")]
        [Range(0f, 1f)] public float widthPulse = 0.06f;

        [Header("Lifetime")]
        [Tooltip("Seconds the arc holds after the LAST enemy spawned from that side (every spawn " +
                 "refreshes it), before fading out.")]
        public float holdDuration = 3.0f;

        [Tooltip("Ease in / ease out time in seconds.")]
        public float fadeDuration = 0.5f;

        [Header("Sorting")]
        [Tooltip("Must sit ABOVE every ground/biome overlay: the night-darkness overlay uses 6000 " +
                 "and fog ~5000, so 7000 keeps the arc visible over all of them.")]
        public int sortingOrder = 7000;

        [Tooltip("Sorting layer name. 'Default' matches the map, grass and biome overlays.")]
        public string sortingLayerName = "Default";
    }

#if UNITY_EDITOR
    [Header("Debug Info")]
    [SerializeField] private float totalWaveDelay;
    [SerializeField] private float effectiveSpawnMultiplier;

    private void OnValidate()
    {
        if (waveConfig != null)
        {
            totalWaveDelay = waveConfig.timeBetweenWaves + waveSpawnDelayModifier;
        }
        effectiveSpawnMultiplier = enemySpawnCountMultiplier;
    }
#endif
}
