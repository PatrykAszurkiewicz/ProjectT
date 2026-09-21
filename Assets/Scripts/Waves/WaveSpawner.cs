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

    [Header("Spawn position diagnostics")]
    [Tooltip("Log every wave spawn: which area was used, that area's world bounds, the " +
             "position picked, and where the enemy actually is ~1s later. Use this to find " +
             "out why enemies appear near the centre (disabled/inactive area collider, " +
             "area referencing a prefab asset instead of the scene object, obstacle nudge, " +
             "or something moving the enemy after it spawned).")]
    public bool debugLogSpawnPositions = false;

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

    [Tooltip("Dump the full arc report to the console automatically — once a couple of seconds " +
             "after the scene starts, and again every time an arc is triggered. No right-clicking " +
             "required; just copy the console block.")]
    public bool verboseArcLogging = true;

    [Tooltip("DEBUG PROBE. Draws the arc as a fat, opaque, untextured magenta band at the very " +
             "top of the sorting order, ignoring every style setting. If you turn this on and " +
             "still do not see a thick magenta band, then whatever arc you ARE seeing on screen " +
             "is being drawn by something else, not by this script.")]
    public bool debugLoudArc = false;

    [Tooltip("Look & feel of the wave arc (colour, span, pulse, sorting).")]
    public WaveIndicatorStyle waveIndicatorStyle = new WaveIndicatorStyle();

    [Header("Enemy spawn portals")]
    [Tooltip("Master toggle for the procedural rift that opens where each wave enemy " +
             "appears. Purely cosmetic — see EnemySpawnPortal.cs. Turning this off is a " +
             "complete no-op for gameplay.")]
    public bool showEnemySpawnPortals = true;

    [Tooltip("Size, timing, colour and budget of the spawn portal. Sizing is derived from " +
             "the enemy itself, so a Slime gets a small rift and a Brute a large one; the " +
             "numbers here are multipliers and limits on top of that.")]
    public EnemyPortalStyle enemyPortalStyle = new EnemyPortalStyle();

    [Tooltip("Prefabs that must never get a portal, on top of the built-in exclusions " +
             "(stage bosses, Scarecrow, Vortex). For one-off exceptions a designer spots " +
             "in play — no code change needed.")]
    public List<GameObject> portalExcludedPrefabs = new List<GameObject>();

    [Tooltip("Any prefab whose name CONTAINS one of these fragments gets no portal " +
             "(case-insensitive). Use for whole families at once, e.g. 'Ghost'.")]
    public List<string> portalExcludedNameFragments = new List<string>();

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

    // ROOT-CAUSE FIX for "enemies spawned before the biome / game screen loaded".
    //
    // This used to be:
    //     GameOrchestrator.Instance != null &&
    //     GameOrchestrator.Instance.CurrentState != GameOrchestrator.RunState.Idle
    //
    // CurrentState is legitimately Idle from scene load until the run loop starts. On the
    // Continue path that gap is DeferredResume's wait for players + their Weapon children
    // (bounded at 15s); on a fresh run it is any frame where StartRun is deferred. During
    // that gap the test above said "no orchestrator" and Update() below ran the STANDALONE
    // path: it ticked its own countdown and spawned waves straight out of the inspector
    // WaveConfig — behind the black boot cover, before the biome existed. Restored towers
    // then killed some of them off-screen, which is why enemies were sometimes already dead
    // by the time the arena appeared.
    //
    // Ownership is now asked of the orchestrator itself (claimed in its Awake), so the gap
    // is closed from frame zero. Scenes with NO orchestrator, and scenes with a deliberately
    // dormant one (autoStartRun off, no resume intent), still get the original standalone
    // behaviour.
    public bool IsOrchestratorMode => GameOrchestrator.WavesAreOrchestrated;

    // One-shot diagnostic: how long we have sat in the orchestrator's pre-run window.
    private float _orchestratorBootWait;
    private bool _loggedBootSuppression;

    void Start()
    {
        BootProfiler.Mark("[PERF] WaveSpawner.Start");
        // Build stamp. If this line is missing from the console, the script running in your
        // project is NOT this file — check for a duplicate WaveSpawner.cs or an old prefab.
        Debug.Log($"[WaveSpawner] wave-arc build v4 (solid rails, no texture dependency) on '{name}'.");
        if (verboseArcLogging) StartCoroutine(VerboseBootReport());

        // Build the portal textures now rather than on the first spawn. They are
        // generated procedurally (no art assets, nothing under Resources/), which costs
        // roughly 10 ms once — fine here, a visible hitch mid-wave. Deliberately ABOVE
        // the waveConfig null-check below, so orchestrator-driven scenes warm too.
        if (showEnemySpawnPortals) EnemyPortalSprites.Prewarm();
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

        // Validate BEFORE the waveConfig check: orchestrator-driven scenes often have no
        // WaveConfig here, and they need these spawn-area warnings just as much.
        ValidateSpawnAreas();

        if (waveConfig == null)
        {
            if (!IsOrchestratorMode)
                Debug.LogError("No assigned WaveConfig for the WaveSpawner!");
            return;
        }

        countdown = GetModifiedWaveDelay();

        // PreloadEnemyResources() has been REMOVED. It walked all 19 enemy prefabs
        // calling LoadFolderCached on each — duplicating what the orchestrator's warm
        // already did, and, after the EnemyData migration, duplicating what Unity does
        // during scene load. Profiling put the whole thing at 19 ms, so it was never
        // buying anything either.
        //
        // resourcesPreloaded is NOT vestigial and must NOT be deleted with it: Update()
        // gates STANDALONE (non-orchestrator) wave spawning on this flag, so leaving it
        // false would stop standalone mode ever spawning a wave — silently, with the
        // countdown simply never ticking.
        resourcesPreloaded = true;
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

            if (!TryGetAreaBounds(area, out Bounds ab)) continue;   // already warned
            if (debugLogSpawnPositions)
                Debug.Log($"[WaveSpawner] Spawn area '{area.name}' ({area.GetType().Name}) resolved to " +
                          $"center={(Vector2)ab.center} size={(Vector2)ab.size} " +
                          $"(dist from core {((Vector2)ab.center).magnitude:F1}).", area);

            Vector2 c2 = ab.center;
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




    void Update()
    {
        // NOTE: the wave arcs are driven from LateUpdate(), NOT here — they must be
        // re-aimed after the camera has moved for the frame, and they must keep
        // pulsing through every early-return below (orchestrator mode, enemies alive,
        // waves exhausted). All of those returns are about ADVANCING waves only.

        // ORCHESTRATOR MODE: don't auto-advance
        if (IsOrchestratorMode)
        {
            // Diagnostic only — no spawning happens here. If this line ever appears in the
            // console it means the OLD code would have dealt a standalone wave during the
            // orchestrator's boot/resume window (i.e. behind the loading screen). Harmless
            // to leave in; it fires at most once per scene load.
            if (!_loggedBootSuppression
                && GameOrchestrator.Instance != null
                && GameOrchestrator.Instance.CurrentState == GameOrchestrator.RunState.Idle
                && waveConfig != null && resourcesPreloaded
                && currentWaveIndex < waveConfig.waves.Count)
            {
                _orchestratorBootWait += Time.deltaTime;
                if (_orchestratorBootWait >= GetModifiedWaveDelay())
                {
                    _loggedBootSuppression = true;
                    Debug.Log("[WaveSpawner] Suppressed a STANDALONE wave while the orchestrator was still " +
                              "booting/resuming (screen still on the loading cover). Before the fix this is " +
                              "the wave that spawned enemies before the biome had loaded.");
                }
            }
            return;
        }

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
            SpawnEnemy(plan.prefabs[i], plan.directions[i], "STANDALONE SpawnWave");

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
        // Clamped. This counter is incremented once per SpawnEnemy/SpawnEnemyPublic
        // call and decremented once per death, but the death paths are spread across
        // several classes; clamping means a hypothetical double-notify can only ever
        // cost a stalled wave for one kill instead of driving the count negative and
        // permanently blocking every future wave in standalone mode.
        //
        // BOSSES: GameOrchestrator.SpawnBoss routes through SpawnEnemyPublic, so a
        // boss DOES increment this. The boss death routines call
        // EnemyStats.FireCommonDeathHooks, which calls this, so the pair balances.
        // Orchestrator mode never reads enemiesAlive (Update() returns early), so this
        // only matters if a boss prefab is placed directly in a WaveConfig and run
        // standalone — which used to soft-lock the spawner permanently.
        enemiesAlive = Mathf.Max(0, enemiesAlive - 1);
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

        if (!TryGetAreaBounds(area, out Bounds bounds))
        {
            // Unusable area (no size could be resolved). Old code silently read an empty
            // Bounds here, which is centred on (0,0) — i.e. the enemy spawned on the core.
            return CardinalUnit(direction) * Mathf.Max(1f, waveIndicatorStyle.fallbackRadius);
        }

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
            if (area != null && TryGetAreaBounds(area, out Bounds bounds))
            {
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }

    // DIAGNOSTIC (safe to leave in): warn about any enemy that appears BEFORE the player
    // can see or play the arena. Silent during normal play — it only fires when the screen
    // is still covered by the loading/transition cover, or when no run has started yet.
    //
    // 'source' says WHICH path spawned it, which is what actually identifies the bug:
    //   "STANDALONE SpawnWave"   → the spawner's own countdown dealt a wave (the bug this
    //                              fix targets — should now be impossible in an orchestrated
    //                              scene).
    //   "orchestrator"           → the run loop spawned it early (a gap in the reveal gate).
    //   "unindicated (non-wave)" → something else in the scene spawns enemies directly
    //                              (gremlins / guardians / ambient), outside wave control.
    [Tooltip("Log a warning whenever an enemy spawns while the loading/transition cover is " +
             "still up or before a run has started. Costs nothing during normal play.")]
    public bool warnOnEarlySpawns = true;

    private void TraceEarlySpawn(GameObject prefab, string source)
    {
        if (!warnOnEarlySpawns) return;

        var orch = GameOrchestrator.Instance;
        bool covered = orch != null && orch.ScreenIsCovered;
        bool noRunYet = orch != null && orch.CurrentState == GameOrchestrator.RunState.Idle;
        if (!covered && !noRunYet) return;   // normal, visible, in-run spawn — say nothing

        Debug.LogWarning($"[WaveSpawner] EARLY SPAWN — '{(prefab != null ? prefab.name : "null")}' " +
                         $"spawned via {source} at t={Time.realtimeSinceStartup:F2}s while " +
                         $"screenCovered={covered}, runState={(orch != null ? orch.CurrentState.ToString() : "<no orchestrator>")}. " +
                         "The player cannot see this enemy yet.");
    }

    void SpawnEnemy(GameObject prefab, SpawnDirection direction, string source = "?", bool allowPortal = true)
    {
        if (prefab == null)
        {
            Debug.LogWarning("SpawnEnemy: prefab == null");
            return;
        }

        TraceEarlySpawn(prefab, source);

        float clearance = GetPrefabClearanceRadius(prefab);
        Vector2 spawnPosition = GetRandomPositionInArea(direction, clearance);

        // Fallback
        if (obstacleAvoidanceLayers.value != 0
            && Physics2D.OverlapCircle(spawnPosition, clearance, obstacleAvoidanceLayers))
        {
            spawnPosition = NudgeOutOfObstacle(spawnPosition, clearance);
        }

        GameObject enemyObj = Instantiate(prefab, spawnPosition, Quaternion.identity);

        if (debugLogSpawnPositions) StartCoroutine(TraceSpawnPosition(enemyObj, prefab.name, direction, spawnPosition));

        // ★ Mark as wave enemy — WaveEnemy.OnDestroy() will notify orchestrator
        enemyObj.AddComponent<WaveEnemy>();

        EnemyStats stats = enemyObj.GetComponent<EnemyStats>();
        if (stats != null)
        {
            stats.ConfigureEnergyDrop(0.5f, 10);
        }

        enemiesAlive++;

        // Cosmetic arrival rift. Deliberately LAST, after the enemy is fully wired up and
        // every counter has moved: the portal is a separate root object that only READS
        // the enemy, so it cannot affect anything above it, and TryPlaySpawnPortal
        // swallows its own exceptions so a VFX fault can never stop a wave spawning.
        TryPlaySpawnPortal(prefab, enemyObj, spawnPosition, allowPortal);

        // GROUND TRUTH: an arc appears if and only if an enemy really spawned from this
        // side, and each spawn REFRESHES that arc's hold. This is the only indicator path
        // that survives orchestrator mode, where Update()/SpawnWave() never run and the
        // orchestrator alone decides directions. Config lists and caller-supplied
        // direction lists are both untrustworthy; an actual Instantiate is not.
        if (indicateOnSpawn) ShowWaveIndicator(direction);
    }

    private IEnumerator TraceSpawnPosition(GameObject enemy, string prefabName, SpawnDirection direction, Vector2 picked)
    {
        Collider2D area = spawnAreas?.Find(c => c != null && c.name.Equals(direction.ToString(), StringComparison.OrdinalIgnoreCase));
        string areaInfo = area != null && TryGetAreaBounds(area, out Bounds b)
            ? $"area '{area.name}' center={(Vector2)b.center} size={(Vector2)b.size}"
            : "NO matching area (fallback used)";

        yield return new WaitForSeconds(1f);
        if (enemy == null) yield break;

        Vector2 now = enemy.transform.position;
        float moved = Vector2.Distance(now, picked);
        Debug.Log($"[WaveSpawner] SPAWN {prefabName} dir={direction} | {areaInfo} | picked={picked} " +
                  $"(dist {picked.magnitude:F1}) | after 1s={now} (dist {now.magnitude:F1}, moved {moved:F1})" +
                  (moved > 5f ? "  <-- moved a lot in 1s: something repositioned it after spawn" : ""), enemy);
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

    //  SPAWN PORTALS
    //
    //  The rift itself lives in EnemySpawnPortal.cs and touches nothing. This is only
    //  the "does this spawn deserve one" policy, kept here because it is a spawner
    //  decision, not a VFX one.

    private void TryPlaySpawnPortal(GameObject prefab, GameObject enemyObj, Vector3 spawnPosition, bool allowPortal)
    {
        if (!allowPortal || !showEnemySpawnPortals) return;

        // A fault in a purely decorative effect must never cost the player a wave.
        try
        {
            if (!EnemyDeservesPortal(prefab, enemyObj)) return;
            EnemySpawnPortal.Play(enemyObj, prefab, spawnPosition, enemyPortalStyle);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[WaveSpawner] Spawn portal failed for " +
                             $"'{(prefab != null ? prefab.name : "null")}': {e.Message}. " +
                             "The enemy spawned normally; only the effect was skipped.");
        }
    }

    /// Who does NOT get a portal:
    ///   • STAGE BOSSES. They arrive through this same SpawnEnemy path (see
    ///     GameOrchestrator.SpawnBoss) but have their own entrance — BossZoomController
    ///     pans and zooms onto them. A rift would fight that cinematic.
    ///   • SCARECROW. It teleports in and out on its own visible/hidden cycle; a portal
    ///     on every reappearance would be noise.
    ///   • VORTEX. It is itself a rift that births enemies. A portal around a portal.
    private bool EnemyDeservesPortal(GameObject prefab, GameObject instance)
    {
        GameObject probe = instance != null ? instance : prefab;
        if (probe == null) return false;

        // Covers Boss1, Boss2 and every future BossN — they all derive from
        // BaseBossStats, which is the same test EnemyController uses for isBoss.
        if (probe.GetComponentInChildren<BaseBossStats>(true) != null) return false;

        // Instances are named "<Prefab>(Clone)", so the PREFAB name is the reliable one.
        string prefabName = prefab != null ? prefab.name : probe.name;

        // Belt-and-braces for any boss that does not (yet) derive from BaseBossStats.
        if (IsBossName(prefabName)) return false;

        if (probe.GetComponentInChildren<Scarecrow>(true) != null) return false;
        if (probe.GetComponentInChildren<VortexSpawner>(true) != null) return false;

        if (portalExcludedPrefabs != null && prefab != null)
        {
            for (int i = 0; i < portalExcludedPrefabs.Count; i++)
                if (portalExcludedPrefabs[i] == prefab) return false;
        }

        if (portalExcludedNameFragments != null)
        {
            for (int i = 0; i < portalExcludedNameFragments.Count; i++)
            {
                string frag = portalExcludedNameFragments[i];
                if (string.IsNullOrEmpty(frag)) continue;
                if (prefabName.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
        }

        return true;
    }

    /// "Boss", "Boss1", "Boss12" → true.  "BossMinion", "Bossling" → false.
    /// Deliberately narrow: this is a safety net for the BaseBossStats check above, and
    /// a loose prefix match would silently strip portals from ordinary adds.
    private static bool IsBossName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return false;
        if (!prefabName.StartsWith("Boss", StringComparison.OrdinalIgnoreCase)) return false;
        if (prefabName.Length == 4) return true;
        return char.IsDigit(prefabName[4]);
    }

    //  PUBLIC API FOR ORCHESTRATOR

    public void SpawnEnemyPublic(GameObject prefab, SpawnDirection direction)
    {
        SpawnEnemy(prefab, direction, "orchestrator");
    }

    /// Spawn without telegraphing a direction. Use for anything that is NOT a wave enemy
    /// (gremlins, chest guardians, ambient spawns) so it never lights a wave arc.
    public void SpawnEnemyUnindicated(GameObject prefab, SpawnDirection direction)
    {
        bool prev = indicateOnSpawn;
        indicateOnSpawn = false;
        try { SpawnEnemy(prefab, direction, "unindicated (non-wave)"); }
        finally { indicateOnSpawn = prev; }
    }

    // NOTE: external callers (GameOrchestrator) generally hand us WaveData.spawnDirections —
    // the AUTHORED list — but then spawn enemies from only a subset of it. Honouring that list
    // lights arcs on sides no enemy ever uses. Ignored unless trustCallerDirections is set;
    // indicateOnSpawn covers this correctly instead.
    /// Light ONE arrival arc ahead of any spawn. Used by GameOrchestrator to telegraph the
    /// first enemy's side while the between-wave countdown is still running.
    ///
    /// Deliberately NOT gated on trustCallerDirections, unlike ShowWaveIndicatorsPublic
    /// below: that gate exists because callers pass the whole AUTHORED direction list and
    /// enemies only use a subset. This takes a single direction that a real spawn is
    /// guaranteed to follow, so it cannot lie.
    public void ShowWaveIndicatorPublic(SpawnDirection direction)
    {
        ShowWaveIndicator(direction);
    }

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

        // Guard against a style block that deserialized empty (see EnsureStyleUsable).
        EnsureStyleUsable();

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

        if (verboseArcLogging) LogWaveArcDiagnostics();

        if (debugLogIndicators)
        {
            var st = waveIndicatorStyle;
            Debug.Log($"[WaveSpawner] arc -> {direction} | cameras={_playerCams.Count} live={activeArcs.Count} | " +
                      $"bandThickness={st.bandThickness:F3} tintA={st.masterTint.a:F2} fillA={st.fillAlpha:F2} | " +
                      $"layer={ArcLayerFor(_playerCams.Count > 0 ? _playerCams[0].playerIndex : 0)} " +
                      $"sorting={st.sortingLayerName}:{st.sortingOrder} | shader={ArcShader()?.name}");
        }
    }

    // Automatic report, so nothing has to be clicked. Waits a moment for player cameras and
    // the biome to exist, otherwise the camera section of the report is empty and useless.
    private IEnumerator VerboseBootReport()
    {
        yield return new WaitForSeconds(2f);
        LogArcSuspects();
        LogWaveArcDiagnostics();
    }

    // Manual trigger for debugging: right-click the WaveSpawner component in play mode.
    [ContextMenu("Test wave arc (play mode)")]
    private void TestWaveArc()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[WaveSpawner] Enter play mode first — arcs need live player cameras.");
            return;
        }
        showWaveIndicators = true;
        debugLogIndicators = true;
        ShowWaveIndicator(SpawnDirection.Top);
    }

    // One-shot report of everything that decides whether the arc is visible. Right-click the
    // WaveSpawner component in the inspector (play mode preferred) and copy the console block.
    // The PIXEL figures are the ones that matter: world units mean nothing without the zoom.
    [ContextMenu("Log wave arc diagnostics")]
    public void LogWaveArcDiagnostics()
    {
        EnsureStyleUsable();
        EnsureArcTextures();

        var st = waveIndicatorStyle;
        var sb = new System.Text.StringBuilder(1024);

        sb.AppendLine("===== WAVE ARC DIAGNOSTICS =====");
        sb.AppendLine($"unity={Application.unityVersion}  playing={Application.isPlaying}  screen={Screen.width}x{Screen.height}");
        sb.AppendLine($"showWaveIndicators={showWaveIndicators}  indicateOnSpawn={indicateOnSpawn}  " +
                      $"trustCallerDirections={trustCallerDirections}  orchestratorMode={IsOrchestratorMode}");
        sb.AppendLine($"activeArcs={activeArcs.Count}  currentWaveIndex={currentWaveIndex}");

        var sh = ArcShader();
        sb.AppendLine($"shader='{(sh != null ? sh.name : "NULL")}'  supported={(sh != null && sh.isSupported)}  " +
                      $"WaveArcInstalled={(sh != null && sh.name == "Game/WaveArc")}");
        sb.AppendLine($"bandTex={(_bandTex != null ? _bandTex.width + "x" + _bandTex.height : "NULL")}  " +
                      $"softTex={(_softTex != null ? "ok" : "NULL")}");

        sb.AppendLine($"style: bandThickness={st.bandThickness:F4} minBandPixels={st.minBandPixels:F0} rimThickness={st.rimThickness:F4} " +
                      $"span={st.spanDegrees:F0}deg screenFill={st.screenFill:F2} segments={st.segments}");
        sb.AppendLine($"style: tintA={st.masterTint.a:F2} fillA={st.fillAlpha:F2} glowA={st.glowAlpha:F2} " +
                      $"rimA={st.rimAlpha:F2} borderA={st.borderAlpha:F2}");
        sb.AppendLine($"style: showGlow={st.showGlow} showRails={st.showBorderRails} showRim={st.showRim} " +
                      $"useTexturedBand={st.useTexturedBand}");
        sb.AppendLine($"style: border=#{ColorUtility.ToHtmlStringRGB(st.borderColor)} " +
                      $"fill=#{ColorUtility.ToHtmlStringRGB(st.fillColor)} " +
                      $"sorting={st.sortingLayerName}:{st.sortingOrder}  hold={st.holdDuration:F1}s");

        CollectPlayerCameras();
        sb.AppendLine($"-- cameras ({_playerCams.Count}) --");
        for (int i = 0; i < _playerCams.Count; i++)
        {
            var pc = _playerCams[i];
            if (pc.cam == null) { sb.AppendLine($"  P{i}: NULL CAMERA"); continue; }

            float halfH = pc.cam.orthographicSize;
            float unit = CameraUnit(pc.cam);
            float ppu = pc.cam.pixelHeight / Mathf.Max(0.0001f, halfH * 2f);
            int layer = ArcLayerFor(pc.playerIndex);
            bool sees = (pc.cam.cullingMask & (1 << layer)) != 0;

            if (!pc.cam.orthographic)
                sb.AppendLine($"  !! P{pc.playerIndex} CAMERA IS PERSPECTIVE — orthographicSize is " +
                              "meaningless and every arc size below is wrong.");
            sb.AppendLine($"  P{pc.playerIndex} '{pc.cam.name}' ortho={halfH:F2} aspect={pc.cam.aspect:F2} " +
                          $"viewport={pc.cam.pixelWidth}x{pc.cam.pixelHeight} pixelsPerUnit={ppu:F1}");
            sb.AppendLine($"     band={BandWidthFor(pc.cam):F3}u = {BandWidthFor(pc.cam) * ppu:F1}px   " +
                          $"rim={unit * st.rimThickness:F3}u = {unit * st.rimThickness * ppu:F1}px   " +
                          $"glow={unit * st.bandThickness * st.glowWidthScale * ppu:F1}px");
            sb.AppendLine($"     arcLayer={layer} ('{LayerMask.LayerToName(layer)}') cameraSeesIt={sees} " +
                          $"cullingMask=0x{pc.cam.cullingMask:X8}");
        }

        sb.AppendLine($"-- spawn areas ({(spawnAreas != null ? spawnAreas.Count : 0)}) --");
        foreach (SpawnDirection d in System.Enum.GetValues(typeof(SpawnDirection)))
        {
            Vector2 c;
            sb.AppendLine(TryGetSpawnAreaCenter(d, out c)
                ? $"  {d}: collider found at {c}"
                : $"  {d}: NO COLLIDER named '{d}' — falls back to a cardinal direction");
        }

        sb.AppendLine($"-- live arcs ({activeArcs.Count}) --");
        for (int i = 0; i < activeArcs.Count; i++)
        {
            var a = activeArcs[i];
            string bodyInfo = a.body != null
                ? $"fill: width={a.body.widthMultiplier:F3} pts={a.body.positionCount} " +
                  $"visible={a.body.isVisible} colour={(a.bodyMat != null ? a.bodyMat.color.ToString("F2") : "n/a")} " +
                  $"tex={(a.bodyMat != null && a.bodyMat.mainTexture != null ? "yes" : "none")} " +
                  $"p0={a.body.GetPosition(0)}"
                : "FILL NULL";
            string railInfo = a.railOut != null
                ? $"rail: width={a.railOut.widthMultiplier:F3} visible={a.railOut.isVisible} " +
                  $"colour={(a.railOutMat != null ? a.railOutMat.color.ToString("F2") : "n/a")}"
                : "rail: NONE";
            sb.AppendLine($"  [{i}] dir={a.dir} P{a.playerIndex} life={a.life:F2} env={a.env:F2} " +
                          $"matA={(a.bodyMat != null ? a.bodyMat.color.a.ToString("F2") : "n/a")}");
            sb.AppendLine($"       {bodyInfo}");
            sb.AppendLine($"       {railInfo}");
        }
        sb.AppendLine("===== END =====");

        Debug.Log(sb.ToString());
    }

    // Answers the only question that matters when the arc on screen refuses to change no
    // matter what you tune: WHAT is actually drawing it? Lists every WaveSpawner in the
    // scene, every LineRenderer, and every high-sorting SpriteRenderer, so a second arc
    // system (or a second WaveSpawner) cannot hide.
    [ContextMenu("Log arc suspects (what is drawing on screen)")]
    public void LogArcSuspects()
    {
        var sb = new System.Text.StringBuilder(2048);
        sb.AppendLine("===== ARC SUSPECTS =====");

        var spawners = FindObjectsByType<WaveSpawner>(FindObjectsSortMode.None);
        sb.AppendLine($"-- WaveSpawner instances: {spawners.Length} --");
        foreach (var w in spawners)
            sb.AppendLine($"  '{HierarchyPath(w.transform)}' isThisOne={(w == this)} enabled={w.enabled} " +
                          $"active={w.gameObject.activeInHierarchy} showWaveIndicators={w.showWaveIndicators} " +
                          $"indicateOnSpawn={w.indicateOnSpawn} liveArcs={w.activeArcs.Count}");

        var lrs = FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
        sb.AppendLine($"-- LineRenderers in scene: {lrs.Length} --");
        int shown = 0;
        foreach (var lr in lrs)
        {
            if (shown++ >= 40) { sb.AppendLine("  ...truncated"); break; }
            var mat = lr.sharedMaterial;
            sb.AppendLine($"  '{HierarchyPath(lr.transform)}' sorting={lr.sortingLayerName}:{lr.sortingOrder} " +
                          $"width={lr.widthMultiplier:F3} pts={lr.positionCount} visible={lr.isVisible} " +
                          $"layer='{LayerMask.LayerToName(lr.gameObject.layer)}' " +
                          $"shader='{(mat != null && mat.shader != null ? mat.shader.name : "NONE")}' " +
                          $"color={(mat != null ? mat.color.ToString("F2") : "n/a")}");
        }

        sb.AppendLine("-- SpriteRenderers with sortingOrder >= 500 (overlay suspects) --");
        shown = 0;
        foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr.sortingOrder < 500) continue;
            if (shown++ >= 30) { sb.AppendLine("  ...truncated"); break; }
            sb.AppendLine($"  '{HierarchyPath(sr.transform)}' sorting={sr.sortingLayerName}:{sr.sortingOrder} " +
                          $"sprite='{(sr.sprite != null ? sr.sprite.name : "none")}' color={sr.color.ToString("F2")}");
        }

        sb.AppendLine("-- objects whose NAME looks like an indicator --");
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("arc") || n.Contains("indicator") || n.Contains("telegraph") ||
                n.Contains("warning") || n.Contains("direction"))
                sb.AppendLine($"  '{HierarchyPath(t)}' components=[{ComponentNames(t)}]");
        }

        sb.AppendLine("===== END =====");
        Debug.Log(sb.ToString());
    }

    private static string HierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    private static string ComponentNames(Transform t)
    {
        var parts = new List<string>();
        foreach (var c in t.GetComponents<Component>())
            if (c != null) parts.Add(c.GetType().Name);
        return string.Join(", ", parts);
    }

    // WHY THIS EXISTS. The style block is a [System.Serializable] class stored inside the
    // scene/prefab. When its fields are renamed or added, the OLD serialized data has no
    // entry for the new names — and depending on the Unity version and how the component
    // was authored, those fields can come back as default(T): 0 width, 0 alpha, black
    // colours. Every layer then renders perfectly correctly and is completely invisible,
    // which looks exactly like a sorting bug and is not one.
    //
    // A wholly-unauthored-looking block is replaced outright (running the field
    // initialisers again); a partially broken one has just the fatal fields repaired.
    private bool _styleChecked;

    private void EnsureStyleUsable()
    {
        if (_styleChecked) return;
        _styleChecked = true;

        if (waveIndicatorStyle == null || waveIndicatorStyle.LooksUnauthored)
        {
            waveIndicatorStyle = new WaveIndicatorStyle();
            Debug.LogWarning("[WaveSpawner] waveIndicatorStyle deserialized empty (zero width / zero " +
                             "alpha / black colours) and was reset to defaults. This happens when the " +
                             "component was authored against an older version of the style block. " +
                             "Save the scene or prefab to make the reset stick.");
            return;
        }

        if (waveIndicatorStyle.RepairDegenerate())
            Debug.LogWarning("[WaveSpawner] waveIndicatorStyle had values that render nothing " +
                             "(zero band width or zero alpha); those fields were repaired.");
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
            if (a.cam == null || a.body == null)
            {
                DestroyArc(a);
                activeArcs.RemoveAt(i);
                continue;
            }

            // DEBUG PROBE: bypass the entire style system.
            if (debugLoudArc)
            {
                a.env = 1f;
                a.life = Mathf.Max(a.life, 2f);
                float loud = BandWidthFor(a.cam) * 2.5f;
                a.body.widthMultiplier = loud;
                a.body.sortingOrder = 32760;
                if (a.bodyMat != null)
                {
                    a.bodyMat.mainTexture = null;      // null sampler reads as solid white
                    a.bodyMat.color = Color.magenta;
                }
                AimArc(a, loud);
                continue;
            }

            a.phase += dt * Mathf.Max(0.0001f, st.pulseSpeed);
            a.scroll += dt * st.scrollSpeed;
            a.life -= dt;
            a.env = Mathf.MoveTowards(a.env, a.life > 0f ? 1f : 0f, fadeRate * dt);

            float pulse01 = 0.5f + 0.5f * Mathf.Sin(a.phase * Mathf.PI * 2f);

            // st.masterTint.a is a CEILING: the pulse breathes below it, so the arc can
            // never flash brighter than authored.
            float breathe = 1f - st.pulseDepth + st.pulseDepth * pulse01;
            float alpha = st.masterTint.a * breathe * a.env;

            // Thickness is a FRACTION of what this camera can see, not a world-unit
            // constant. A fixed 0.3 units is a fat band on a 6-unit camera and a 4-pixel
            // scratch on a 20-unit one, which is exactly how the arc vanished on a
            // zoomed-out map. Scaling by the camera keeps it identical at any zoom.
            float unit = CameraUnit(a.cam);
            float width = BandWidthFor(a.cam) * (1f + st.widthPulse * pulse01);

            AimArc(a, width);   // follow the camera + re-point at the spawn side

            // BODY. Tint stays (near) white on purpose — the colours live in the texture,
            // so the border/fill hues are authored in one place instead of two.
            a.body.widthMultiplier = width;
            if (a.bodyMat != null)
            {
                // Untextured, the fill must carry fillColor itself. Leaving the tint white
                // here is precisely how the band ends up rendering as a pale white ribbon
                // when the texture does not bind.
                a.bodyMat.color = st.useTexturedBand
                    ? new Color(st.masterTint.r, st.masterTint.g, st.masterTint.b, alpha)
                    : InteriorColor(st, alpha);
                // Scrolling UVs are what makes the band read as flowing energy rather than
                // a static stripe. Negative so the streaks travel toward the wave's side.
                a.bodyMat.mainTextureOffset = new Vector2(-a.scroll, 0f);
            }

            // GLOW. Wide, soft, deep-violet bloom that seats the band into the field.
            if (a.glow != null)
            {
                a.glow.widthMultiplier = width * Mathf.Max(1f, st.glowWidthScale);
                if (a.glowMat != null)
                {
                    // Bloom from the BRIGHT hue, not the dark fill. A deep violet at 20%
                    // alpha over green grass composites to a muddy grey lozenge; the same
                    // bloom pulled halfway to the border magenta reads as light.
                    Color bloom = Color.Lerp(st.fillColor, st.borderColor, 0.5f);
                    a.glowMat.color = new Color(bloom.r, bloom.g, bloom.b,
                                                st.glowAlpha * breathe * a.env);
                }
            }

            // RAILS. The solid bright border on both edges of the band.
            if (a.railIn != null || a.railOut != null)
            {
                float railW = Mathf.Max(0.0001f, width * st.borderThickness);
                var railCol = new Color(st.borderColor.r, st.borderColor.g, st.borderColor.b,
                                        st.borderAlpha * breathe * a.env);
                if (a.railIn != null)
                {
                    a.railIn.widthMultiplier = railW;
                    if (a.railInMat != null) a.railInMat.color = railCol;
                }
                if (a.railOut != null)
                {
                    a.railOut.widthMultiplier = railW;
                    if (a.railOutMat != null) a.railOutMat.color = railCol;
                }
            }

            // RIM. Thin bright hairline riding just outside the band's outer edge.
            if (a.rim != null)
            {
                a.rim.widthMultiplier = Mathf.Max(unit * st.rimThickness, width * 0.14f);
                if (a.rimMat != null)
                    a.rimMat.color = new Color(st.borderColor.r, st.borderColor.g, st.borderColor.b,
                                               st.rimAlpha * breathe * a.env);
            }

            if (a.life <= 0f && a.env <= 0.0001f)
            {
                DestroyArc(a);
                activeArcs.RemoveAt(i);
            }
        }

        if (activeArcs.Count == 0) RestoreCameraMasks();
    }

    // Interior colour of the band. Pulled slightly toward the border hue: a flat dark fill
    // between two bright rails reads as an empty gap rather than as part of one object.
    private static Color InteriorColor(WaveIndicatorStyle st, float alpha)
    {
        Color c = Color.Lerp(st.fillColor, st.borderColor, 0.22f);
        return new Color(c.r * st.masterTint.r, c.g * st.masterTint.g, c.b * st.masterTint.b,
                         alpha * st.fillAlpha);
    }

    // The camera's smallest visible half-extent, in world units. Everything the arc sizes
    // itself by (radius AND thickness) is expressed as a fraction of this, so the arc looks
    // the same on a tight camera and a zoomed-out one, and in a split-screen half.
    private static float CameraUnit(Camera cam)
    {
        if (cam == null) return 1f;
        float halfH = cam.orthographicSize;
        return Mathf.Max(0.01f, Mathf.Min(halfH * cam.aspect, halfH));
    }

    // The band's thickness in world units for a given camera, WITH A PIXEL FLOOR.
    //
    // A thickness expressed purely as a fraction of the camera still collapses in a narrow
    // or split-screen viewport: 5.5% of the smaller half-extent is ~30px on a 1080p full
    // screen but ~5px in a 200px-wide split, which is a scratch. minBandPixels guarantees
    // the arc is readable no matter how small the viewport gets.
    private float BandWidthFor(Camera cam)
    {
        var st = waveIndicatorStyle;
        float w = CameraUnit(cam) * st.bandThickness;

        if (cam != null && cam.orthographic && cam.pixelHeight > 0)
        {
            float worldPerPixel = (cam.orthographicSize * 2f) / cam.pixelHeight;
            w = Mathf.Max(w, st.minBandPixels * worldPerPixel);
        }
        return Mathf.Max(0.001f, w);
    }

    // Rebuilds every layer of the arc around the CAMERA's current position, on the bearing
    // from that camera toward the spawn area. Cheap and it means the arc both follows the
    // player and re-aims as they move relative to the spawn side.
    private void AimArc(ActiveArc a, float bodyWidth)
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

        // The band has real thickness now, and the glow is thicker still. Pull the
        // centreline in by half the WIDEST layer so nothing clips off the screen edge.
        float widest = bodyWidth * (st.showGlow ? Mathf.Max(1f, st.glowWidthScale) : 1f);
        radius -= widest * 0.5f;
        radius = Mathf.Max(0.25f, radius);

        float half = st.spanDegrees * 0.5f * Mathf.Deg2Rad;

        LayoutLine(a.glow, camPos, a.bearing, radius, half);
        LayoutLine(a.body, camPos, a.bearing, radius, half);
        // Rails ride ON the band's two edges, inset by half their own width so they sit
        // flush with it rather than hanging off it.
        float railW = bodyWidth * st.borderThickness;
        float railOffset = Mathf.Max(0f, bodyWidth - railW) * 0.5f;
        LayoutLine(a.railIn, camPos, a.bearing, radius - railOffset, half);
        LayoutLine(a.railOut, camPos, a.bearing, radius + railOffset, half);

        float rimRadius = radius + bodyWidth * 0.5f + Mathf.Min(halfW, halfH) * st.rimGap;
        LayoutLine(a.rim, camPos, a.bearing, rimRadius, half * st.rimSpanScale);
    }

    // Lays one layer's points out along a circular arc centred on the camera.
    private static void LayoutLine(LineRenderer lr, Vector2 camPos, float bearing, float radius, float halfSpan)
    {
        if (lr == null) return;

        int segs = lr.positionCount - 1;
        if (segs < 1) return;

        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            float ang = bearing - halfSpan + t * (2f * halfSpan);
            lr.SetPosition(i, new Vector3(
                camPos.x + Mathf.Cos(ang) * radius,
                camPos.y + Mathf.Sin(ang) * radius,
                0f));
        }
    }

    private ActiveArc BuildArc(SpawnDirection direction, Vector2 target, Camera cam, int playerIndex)
    {
        var st = waveIndicatorStyle;
        EnsureArcTextures();

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
        // LineRenderers write world positions themselves, so parenting does not move them.
        a.root.transform.SetParent(cam.transform, false);
        a.root.layer = ArcLayerFor(playerIndex);

        // THREE LAYERS, not one. A LineRenderer carries exactly one material, and the
        // look needs three different ones (soft bloom / textured band / crisp hairline),
        // so each gets its own child object. They share geometry via AimArc.
        if (st.showGlow && !debugLoudArc)
        {
            a.glow = NewLayer(a.root, "Glow", st.sortingOrder - 1, out a.glowMat);
        }

        a.body = NewLayer(a.root, "Fill", st.sortingOrder, out a.bodyMat);
        if (st.useTexturedBand && !debugLoudArc)
        {
            a.bodyMat.mainTexture = _bandTex;
            a.bodyMat.mainTextureScale = new Vector2(Mathf.Max(1f, st.textureTiling), 1f);
        }

        // BORDER RAILS: two solid bright lines sitting on the band's edges.
        //
        // The border used to live inside the generated band texture. That made the entire
        // border — the thing that gives the arc its shape — depend on a texture binding
        // correctly through a shader whose _MainTex is declared [PerRendererData], i.e.
        // designed to be fed by a SpriteRenderer's property block rather than by a
        // LineRenderer's material. When that binding does not take, the band samples white
        // and the whole arc washes out to a pale ribbon. Solid vertex-coloured lines have
        // no such failure mode: if anything on this object renders, these render, in the
        // colour you set.
        if (st.showBorderRails && !debugLoudArc)
        {
            a.railIn = NewLayer(a.root, "RailInner", st.sortingOrder + 1, out a.railInMat);
            a.railOut = NewLayer(a.root, "RailOuter", st.sortingOrder + 1, out a.railOutMat);
        }

        if (st.showRim && !debugLoudArc)
            a.rim = NewLayer(a.root, "Rim", st.sortingOrder + 2, out a.rimMat);

        // Size every layer before the first render, or the arc pops at width 1 for a frame.
        float unit = CameraUnit(cam);
        float w = BandWidthFor(cam);
        a.body.widthMultiplier = w;
        if (a.glow != null) a.glow.widthMultiplier = w * Mathf.Max(1f, st.glowWidthScale);
        if (a.rim != null) a.rim.widthMultiplier = Mathf.Max(unit * st.rimThickness, w * 0.14f);
        float rail0 = Mathf.Max(0.0001f, w * st.borderThickness);
        if (a.railIn != null) a.railIn.widthMultiplier = rail0;
        if (a.railOut != null) a.railOut.widthMultiplier = rail0;

        AimArc(a, w);   // place it before its first render
        return a;
    }

    // Shared LineRenderer setup for one layer. Returns the unique material we animate.
    private LineRenderer NewLayer(GameObject parent, string name, int sortingOrder, out Material mat)
    {
        var st = waveIndicatorStyle;

        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.layer = parent.layer;   // per-player isolation is inherited from the root

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        // TransformZ keeps the ribbon flat in world XY — correct for a top-down 2D field.
        // View alignment would billboard toward one camera and skew in the other half.
        lr.alignment = LineAlignment.TransformZ;
        // Stretch maps U along the whole arc and V across its width. The band texture is
        // authored to that layout: V carries border/fill, U carries the streaks.
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 6;
        lr.numCornerVertices = 6;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingLayerName = st.sortingLayerName;
        lr.sortingOrder = sortingOrder;
        lr.positionCount = Mathf.Clamp(st.segments, 16, 128) + 1;

        // Taper the ribbon to nothing at the tips and fade alpha there too, so the arc
        // dissolves softly into the field instead of ending in hard stubs.
        lr.widthCurve = ArcTaperCurve();
        lr.colorGradient = ArcTipFadeGradient();

        mat = new Material(ArcShader());
        // Start fully transparent: LateUpdate owns the alpha, so a layer created after
        // LateUpdate has already run for the frame cannot pop at full opacity.
        mat.color = new Color(1f, 1f, 1f, 0f);
        lr.material = mat;
        return lr;
    }

    // SHADER. Sprites/Default passes UVs straight through — it ignores mainTextureScale
    // and mainTextureOffset entirely, so the band's tiling and scroll are dead on it. The
    // optional WaveArc shader applies TRANSFORM_TEX and keeps vertex colours, which is all
    // the band needs. Without it everything still renders, just without the flow.
    private static Shader _arcShader;

    private static Shader ArcShader()
    {
        if (_arcShader != null) return _arcShader;

        _arcShader = Shader.Find("Game/WaveArc");
        if (_arcShader == null) _arcShader = Resources.Load<Shader>("WaveArc");
        if (_arcShader == null) _arcShader = Shader.Find("Sprites/Default");
        return _arcShader;
    }

    //  ARC TEXTURES
    //
    //  Generated in code rather than imported, so the look is driven entirely by the
    //  inspector style block and there is no art asset to keep in sync. Both textures are
    //  built once per style and SHARED by every arc; only the tint/UV offset is per-arc.

    private Texture2D _bandTex;   // border + fill + streaks, authored across V and along U
    private Texture2D _softTex;   // plain soft falloff across V, tinted by the material
    private int _texKey;

    private void EnsureArcTextures()
    {
        int key = waveIndicatorStyle.TextureKey();
        if (_bandTex != null && _softTex != null && key == _texKey) return;

        // Live arcs still reference the old textures — only swap when nothing is on screen,
        // otherwise a mid-run inspector tweak would blank out the arcs already showing.
        if (_bandTex != null && activeArcs.Count > 0) return;

        if (_bandTex != null) Destroy(_bandTex);
        if (_softTex != null) Destroy(_softTex);

        _bandTex = BuildBandTexture(waveIndicatorStyle);
        _softTex = BuildSoftTexture();
        _texKey = key;
    }

    // V (across the ribbon) carries the structure: bright BORDER at both edges, translucent
    // FILL through the middle, feathered to nothing at the very edge so the band never ends
    // in an aliased hard pixel. U (along the arc) carries chevroned energy streaks that the
    // material scrolls each frame.
    private static Texture2D BuildBandTexture(WaveIndicatorStyle st)
    {
        const int W = 256;   // along the arc — must tile seamlessly in U
        const int H = 64;    // across the ribbon

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            name = "WaveArcBand",
            filterMode = FilterMode.Bilinear,
            wrapModeU = TextureWrapMode.Repeat,   // streaks scroll and must wrap
            wrapModeV = TextureWrapMode.Clamp,    // border must NOT bleed to the far edge
            anisoLevel = 0,
        };

        int streaks = Mathf.Max(1, st.streakCount);
        float sharp = Mathf.Max(1f, st.streakSharpness);
        var px = new Color[W * H];

        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            float d = Mathf.Abs(v * 2f - 1f);   // 0 = centreline, 1 = outer edge

            // Border band. borderThickness is measured inward from the edge as a fraction
            // of the half-width, so 0.3 = the outer 30% of each side is border.
            float inner = 1f - Mathf.Clamp01(st.borderThickness);
            float soft = Mathf.Max(0.001f, st.borderSoftness);
            float border = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner - soft, inner + soft, d));

            // Outermost sliver feathers out — this is what kills the "hard plastic strip" look.
            float edgeFade = 1f - Mathf.SmoothStep(0.90f, 1f, d);

            // Hot core INSIDE the rail: the rail's own centre burns toward white, which is
            // what makes an edge read as lit rather than merely coloured. Without it the
            // border is just a slightly brighter stripe and the whole band looks washed out.
            float hot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner + soft, 0.94f, d)) * edgeFade;
            Color hotColor = Color.Lerp(st.borderColor, Color.white, 0.55f);

            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;

                // Skewing U by the distance from the centreline bends each streak into a
                // chevron pointing along the arc — reads as motion even before it scrolls.
                float uc = u + st.chevron * d;

                // Sharpened cosine -> thin bright bars separated by dark gaps.
                float s = 0.5f + 0.5f * Mathf.Cos(uc * Mathf.PI * 2f * streaks);
                s = Mathf.Pow(s, sharp) * st.streakStrength;

                float grain = 1f - st.grain * Hash01(x, y);

                // Colour: fill -> border across V, pushed toward the border hue by streaks.
                Color c = Color.Lerp(st.fillColor, st.borderColor, border);
                c = Color.Lerp(c, st.borderColor, s * (1f - border));
                c = Color.Lerp(c, hotColor, hot * 0.75f);

                float alpha = Mathf.Lerp(st.fillAlpha, 1f, border);
                alpha = (alpha + s * 0.40f * (1f - border)) * edgeFade * grain;

                // No darkening pass. Multiplying rgb down and then blending at partial alpha
                // is what turns a saturated violet into pale grey-blue on screen.
                float lift = 1f + s * 0.35f;
                px[y * W + x] = new Color(c.r * lift, c.g * lift, c.b * lift, Mathf.Clamp01(alpha));
            }
        }

        tex.SetPixels(px);
        tex.Apply(false, false);
        return tex;
    }

    // Plain white falloff across the ribbon; the material tint supplies the colour. Used by
    // both the wide bloom and the thin rim hairline.
    private static Texture2D BuildSoftTexture()
    {
        const int W = 4, H = 64;

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            name = "WaveArcSoft",
            filterMode = FilterMode.Bilinear,
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp,
            anisoLevel = 0,
        };

        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float d = Mathf.Abs((y + 0.5f) / H * 2f - 1f);
            float a = Mathf.Exp(-4.5f * d * d) * (1f - Mathf.SmoothStep(0.80f, 1f, d));
            for (int x = 0; x < W; x++) px[y * W + x] = new Color(1f, 1f, 1f, a);
        }

        tex.SetPixels(px);
        tex.Apply(false, false);
        return tex;
    }

    // Cheap deterministic hash for the film-grain pass. Keeps the band from looking like
    // a flat vector shape without needing a noise asset.
    private static float Hash01(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFFu) / (float)0xFFFFFFu;
        }
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
        if (a.bodyMat != null) Destroy(a.bodyMat);
        if (a.glowMat != null) Destroy(a.glowMat);
        if (a.railInMat != null) Destroy(a.railInMat);
        if (a.railOutMat != null) Destroy(a.railOutMat);
        if (a.rimMat != null) Destroy(a.rimMat);
        if (a.root != null) Destroy(a.root);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < activeArcs.Count; i++) DestroyArc(activeArcs[i]);
        activeArcs.Clear();
        RestoreCameraMasks();

        // Generated textures are ours to clean up — Unity will not collect them.
        if (_bandTex != null) Destroy(_bandTex);
        if (_softTex != null) Destroy(_softTex);
    }

    private bool TryGetSpawnAreaCenter(SpawnDirection direction, out Vector2 center)
    {
        center = Vector2.zero;
        if (spawnAreas == null) return false;

        Collider2D area = spawnAreas.Find(c => c != null &&
            c.name.Equals(direction.ToString(), StringComparison.OrdinalIgnoreCase));
        if (area == null) return false;
        if (!TryGetAreaBounds(area, out Bounds b)) return false;

        center = b.center;
        return true;
    }

    // WHY THIS EXISTS: Collider2D.bounds is only meaningful while the collider is ENABLED on
    // an ACTIVE GameObject that lives in a loaded scene. Otherwise Unity returns an empty
    // Bounds at (0,0) — so every "random point inside the area" is the map centre, no matter
    // where the object has been dragged in the editor. A spawn-area collider is often
    // disabled on purpose (so it does not physically block enemies); that silently turned
    // every spawn into a core spawn. We now rebuild the area from the collider's own shape
    // and transform in that case, and warn once so the setup can be fixed.
    private readonly HashSet<Collider2D> _warnedAreas = new HashSet<Collider2D>();

    private bool TryGetAreaBounds(Collider2D area, out Bounds bounds)
    {
        bounds = default;
        if (area == null) return false;

        if (!area.gameObject.scene.IsValid())
        {
            WarnAreaOnce(area, "references a PREFAB ASSET, not the object in the scene. Moving the " +
                               "scene object has no effect. Drag the scene object into Spawn Areas.");
            // A prefab asset still has a transform, so fall through to manual shape bounds.
        }

        bool live = area.enabled && area.gameObject.activeInHierarchy && area.gameObject.scene.IsValid();
        if (live)
        {
            bounds = area.bounds;
            if (bounds.size.x > 0.0001f || bounds.size.y > 0.0001f) return true;
        }

        // Manual reconstruction from the shape (ignores rotation, which spawn boxes don't use).
        Transform t = area.transform;
        Vector3 ls = t.lossyScale;
        Vector2 absScale = new Vector2(Mathf.Abs(ls.x), Mathf.Abs(ls.y));

        switch (area)
        {
            case BoxCollider2D box:
                bounds = new Bounds(t.TransformPoint(box.offset), Vector2.Scale(box.size, absScale));
                break;
            case CircleCollider2D circle:
                float r = circle.radius * Mathf.Max(absScale.x, absScale.y);
                bounds = new Bounds(t.TransformPoint(circle.offset), new Vector3(r * 2f, r * 2f, 0f));
                break;
            case CapsuleCollider2D capsule:
                bounds = new Bounds(t.TransformPoint(capsule.offset), Vector2.Scale(capsule.size, absScale));
                break;
            default:
                WarnAreaOnce(area, $"is a {area.GetType().Name} that is disabled/inactive and cannot be " +
                                   "measured. Use a BoxCollider2D, or keep it enabled with Is Trigger ON.");
                return false;
        }

        if (!live)
            WarnAreaOnce(area, $"collider is {(area.enabled ? "on an INACTIVE GameObject" : "DISABLED")}, so " +
                               "Unity reports its bounds at (0,0) and enemies spawned at the map centre. Using " +
                               $"its shape instead (center={(Vector2)bounds.center}). Better: enable it and tick Is Trigger.");

        return bounds.size.x > 0.0001f || bounds.size.y > 0.0001f;
    }

    private void WarnAreaOnce(Collider2D area, string msg)
    {
        if (!_warnedAreas.Add(area)) return;
        Debug.LogWarning($"[WaveSpawner] Spawn area '{area.name}' {msg}", area);
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
            new Keyframe(0.22f, 1f),
            new Keyframe(0.78f, 1f),
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

        // Solid-colour layers sharing one arc of geometry: soft bloom, interior fill, the
        // two bright border rails, and the outer hairline.
        public LineRenderer glow, body, railIn, railOut, rim;
        public Material glowMat, bodyMat, railInMat, railOutMat, rimMat;

        public float phase;       // pulse phase, cycles
        public float scroll;      // texture scroll accumulator, UV units
        public float env;         // 0..1 fade envelope
        public float life;        // seconds of hold remaining
    }

    [System.Serializable]
    public class WaveIndicatorStyle
    {
        [Header("Shape")]
        [Tooltip("Angular width of the arc in degrees, measured at the player's camera. " +
                 "~46 reads as a bracket over that side; larger values wrap further around.")]
        public float spanDegrees = 50f;

        [Tooltip("Extra pull inward, in world units, after the arc has been fitted to the " +
                 "screen. The band's own thickness is already accounted for on top of this.")]
        public float radiusInset = 0.30f;

        [Tooltip("Fraction of the camera's smallest visible half-extent the arc may occupy. " +
                 "0.92 pushes the arc out near the screen edge; 1.0 lets it touch the edge.")]
        [Range(0.3f, 1f)] public float screenFill = 0.70f;

        [Tooltip("Fallback distance from the core used only when no matching spawn-area " +
                 "collider is found for a direction.")]
        public float fallbackRadius = 12f;

        [Tooltip("Points along the arc. Higher = smoother curve on a thick band. 64 is plenty.")]
        [Range(16, 128)] public int segments = 64;

        [Header("Colour")]
        [Tooltip("Bright edge / streak colour — #D60AFF. Alpha here is IGNORED (the band " +
                 "texture drives its own opacity); use masterTint.a for overall strength.")]
        public Color borderColor = new Color(0.839f, 0.039f, 1.000f, 1f);   // #D60AFF

        [Tooltip("Interior fill colour — #5E00A6. Also tints the outer bloom. Alpha IGNORED; " +
                 "see fillAlpha.")]
        public Color fillColor = new Color(0.369f, 0.000f, 0.651f, 1f);     // #5E00A6

        [Tooltip("How opaque the interior of the band is compared to its border. Low values " +
                 "let the battlefield read through the middle.")]
        [Range(0f, 1f)] public float fillAlpha = 0.72f;

        [Tooltip("Master tint and OVERALL ALPHA CEILING. Leave RGB white to show the " +
                 "border/fill colours as authored; drop the alpha to make the whole arc subtler.")]
        public Color masterTint = new Color(1f, 1f, 1f, 1.00f);

        [Header("Band & border")]
        [Tooltip("Thickness of the band as a FRACTION of the camera's smallest visible " +
                 "half-extent, measured at its middle (it tapers to nothing at the tips). " +
                 "0.055 is roughly 3% of screen height — a clearly readable band at any zoom. " +
                 "This is deliberately NOT world units: a fixed world width becomes invisible " +
                 "as soon as the camera zooms out.")]
        [Range(0.005f, 0.30f)] public float bandThickness = 0.050f;

        [Tooltip("MINIMUM band thickness in SCREEN PIXELS, whatever the camera or viewport " +
                 "size. This is the setting that actually guarantees you can see the arc — " +
                 "bandThickness only decides how much THICKER than this it gets on a big view. " +
                 "Raise this if the arc is too thin; 20-30 is a solid readable band.")]
        [Range(2f, 80f)] public float minBandPixels = 12f;

        [Tooltip("Draw the two solid bright border rails on the band's edges. This is what\n" +
                 "gives the arc its shape — leave it on.")]
        public bool showBorderRails = true;

        [Tooltip("Rail thickness as a fraction of the band's thickness. 0.28 = each rail is " +
                 "just over a quarter of the band, leaving a clear fill down the middle.")]
        [Range(0.05f, 0.9f)] public float borderThickness = 0.34f;

        [Tooltip("Opacity of the border rails.")]
        [Range(0f, 1f)] public float borderAlpha = 1.00f;

        [Tooltip("Softness of the border-to-fill transition. 0 is a hard graphic edge; " +
                 "0.1 is a clean but slightly glowing rail.")]
        [Range(0f, 0.5f)] public float borderSoftness = 0.10f;

        [Header("Texture (optional — off by default)")]
        [Tooltip("Overlay the generated streak texture on the fill. OFF by default: it depends " +
                 "on a texture binding through Sprites/Default, whose _MainTex is [PerRendererData] " +
                 "and is not reliably fed from a LineRenderer material. When it fails the fill " +
                 "samples white and the arc washes out. Install WaveArc.shader before enabling this.")]
        public bool useTexturedBand = false;

        [Tooltip("Number of energy streaks per texture tile.")]
        [Range(1, 24)] public int streakCount = 7;

        [Tooltip("How thin and bright each streak is. Higher = thinner, sharper bars.")]
        [Range(1f, 16f)] public float streakSharpness = 6f;

        [Tooltip("How strongly the streaks show. 0 disables them for a plain border+fill band.")]
        [Range(0f, 1f)] public float streakStrength = 0.50f;

        [Tooltip("Bends each streak into a chevron pointing along the arc. 0 = straight bars.")]
        [Range(0f, 0.5f)] public float chevron = 0.06f;

        [Tooltip("Film grain over the band. Small amounts stop it looking like flat vector art.")]
        [Range(0f, 0.5f)] public float grain = 0.06f;

        [Tooltip("How many times the streak pattern repeats along the arc.")]
        [Range(1f, 8f)] public float textureTiling = 2f;

        [Tooltip("Texture scroll speed in UV units per second — the flowing-energy motion. " +
                 "Negative-signed internally so streaks travel toward the threatened side.")]
        public float scrollSpeed = 0.12f;

        [Header("Glow & rim")]
        [Tooltip("Wide soft bloom behind the band, tinted with fillColor.")]
        public bool showGlow = true;

        [Tooltip("Bloom width as a multiple of bandThickness.")]
        [Range(1f, 6f)] public float glowWidthScale = 1.8f;

        [Tooltip("Peak bloom opacity. Keep low — this is atmosphere, not the shape.")]
        [Range(0f, 1f)] public float glowAlpha = 0.22f;

        [Tooltip("Crisp hairline riding just outside the band, tinted with borderColor. " +
                 "This is what makes the arc read as 'drawn' rather than sprayed. Off by " +
                 "default: with the two border rails already drawn, a third line is clutter.")]
        public bool showRim = false;

        [Tooltip("Hairline thickness, also a fraction of the camera's half-extent.")]
        [Range(0.002f, 0.06f)] public float rimThickness = 0.012f;

        [Tooltip("Hairline opacity at the peak of the pulse.")]
        [Range(0f, 1f)] public float rimAlpha = 1.00f;

        [Tooltip("Gap between the band's outer edge and the hairline, as a fraction of the " +
                 "camera's half-extent.")]
        [Range(0f, 0.05f)] public float rimGap = 0.006f;

        [Tooltip("Hairline span as a fraction of the band's span. Slightly under 1 makes the " +
                 "rim finish just inside the band's tips.")]
        [Range(0.5f, 1f)] public float rimSpanScale = 0.99f;

        [Header("Pulse")]
        [Tooltip("Breaths per second. ~0.55 is a calm pulse.")]
        public float pulseSpeed = 0.55f;

        [Tooltip("How deep the alpha breathes. 0.35 = alpha swings between 65% and 100% " +
                 "of the ceiling.")]
        [Range(0f, 1f)] public float pulseDepth = 0.25f;

        [Tooltip("Extra width added at the peak of each breath, as a fraction of bandThickness.")]
        [Range(0f, 1f)] public float widthPulse = 0.10f;

        [Header("Lifetime")]
        [Tooltip("Seconds the arc holds after the LAST enemy spawned from that side (every spawn " +
                 "refreshes it), before fading out.")]
        public float holdDuration = 4.0f;

        [Tooltip("Ease in / ease out time in seconds.")]
        public float fadeDuration = 0.5f;

        [Header("Sorting")]
        [Tooltip("Must sit ABOVE every ground/biome overlay: the night-darkness overlay uses 6000 " +
                 "and fog ~5000, so 7000 keeps the arc visible over all of them. The bloom sits " +
                 "one below this and the rim one above.")]
        public int sortingOrder = 7000;

        [Tooltip("Sorting layer name. 'Default' matches the map, grass and biome overlays.")]
        public string sortingLayerName = "Default";

        // True when the block looks like it was never authored at all — the signature of a
        // serialized instance that lost its new fields. Checked before anything is drawn.
        public bool LooksUnauthored =>
            bandThickness <= 0.0001f &&
            masterTint.a <= 0.0001f &&
            borderColor.maxColorComponent <= 0.001f &&
            fillColor.maxColorComponent <= 0.001f;

        /// <summary>Repair only the fields that make the arc render nothing. Returns true if
        /// anything was changed.</summary>
        public bool RepairDegenerate()
        {
            bool fixedAny = false;

            if (bandThickness <= 0.0001f) { bandThickness = 0.055f; fixedAny = true; }
            if (masterTint.a <= 0.0001f) { masterTint = new Color(1f, 1f, 1f, 1f); fixedAny = true; }
            if (masterTint.maxColorComponent <= 0.001f) { masterTint = new Color(1f, 1f, 1f, masterTint.a); fixedAny = true; }
            if (borderColor.maxColorComponent <= 0.001f) { borderColor = new Color(0.839f, 0.039f, 1f, 1f); fixedAny = true; }
            if (fillColor.maxColorComponent <= 0.001f) { fillColor = new Color(0.369f, 0f, 0.651f, 1f); fixedAny = true; }
            if (fillAlpha <= 0.0001f) { fillAlpha = 0.72f; fixedAny = true; }
            if (borderAlpha <= 0.0001f) { borderAlpha = 1f; fixedAny = true; }
            if (minBandPixels < 2f) { minBandPixels = 12f; fixedAny = true; }
            if (spanDegrees <= 1f) { spanDegrees = 46f; fixedAny = true; }
            if (screenFill <= 0.05f) { screenFill = 0.92f; fixedAny = true; }
            if (holdDuration <= 0.01f) { holdDuration = 3f; fixedAny = true; }
            if (sortingOrder == 0) { sortingOrder = 7000; fixedAny = true; }
            if (string.IsNullOrEmpty(sortingLayerName)) { sortingLayerName = "Default"; fixedAny = true; }

            // Non-fatal, silently normalised: these only affect the look, not visibility.
            if (segments < 16) segments = 64;
            if (borderThickness <= 0.0001f) borderThickness = 0.28f;
            if (borderSoftness <= 0.0001f) borderSoftness = 0.10f;
            if (fadeDuration <= 0.0001f) fadeDuration = 0.5f;
            if (glowWidthScale < 1f) glowWidthScale = 2.6f;
            if (showGlow && glowAlpha <= 0.0001f) glowAlpha = 0.38f;
            if (showRim && rimAlpha <= 0.0001f) rimAlpha = 1f;
            if (rimThickness <= 0.0001f) rimThickness = 0.012f;
            if (rimSpanScale <= 0.01f) rimSpanScale = 0.99f;
            if (streakCount < 1) streakCount = 7;
            if (streakSharpness < 1f) streakSharpness = 6f;
            if (textureTiling < 1f) textureTiling = 2f;

            return fixedAny;
        }

        // Which fields the generated band texture actually depends on. Changing any of them
        // invalidates the cached texture; everything else is a per-frame tint or transform
        // and costs nothing to change live.
        public int TextureKey()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + borderColor.GetHashCode();
                h = h * 31 + fillColor.GetHashCode();
                h = h * 31 + fillAlpha.GetHashCode();
                h = h * 31 + borderThickness.GetHashCode();
                h = h * 31 + borderSoftness.GetHashCode();
                h = h * 31 + streakCount;
                h = h * 31 + streakSharpness.GetHashCode();
                h = h * 31 + streakStrength.GetHashCode();
                h = h * 31 + chevron.GetHashCode();
                h = h * 31 + grain.GetHashCode();
                return h;
            }
        }
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



