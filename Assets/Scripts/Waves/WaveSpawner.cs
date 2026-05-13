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

        countdown = GetModifiedWaveDelay();
        StartCoroutine(PreloadEnemyResources());
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
        // ORCHESTRATOR MODE: don't auto-advance
        if (IsOrchestratorMode) return;

        // STANDALONE MODE: original behavior
        if (waveConfig == null) return;
        if (!resourcesPreloaded) return;
        if (enemiesAlive > 0) return;
        if (currentWaveIndex >= waveConfig.waves.Count) return;

        countdown -= Time.deltaTime;

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

        ShowWaveIndicators(wave.spawnDirections);

        List<GameObject> enemyPrefabsToSpawn = new List<GameObject>();
        if (wave.enemies != null)
        {
            foreach (var group in wave.enemies)
            {
                if (group == null) continue;
                if (group.enemyPrefab == null) continue;
                if (group.count <= 0) continue;

                int modifiedCount = Mathf.Max(1, Mathf.RoundToInt(group.count * enemySpawnCountMultiplier));

                for (int i = 0; i < modifiedCount; i++)
                    enemyPrefabsToSpawn.Add(group.enemyPrefab);
            }
        }

        Shuffle(enemyPrefabsToSpawn);

        if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
        {
            AudioManager.instance.EnsureMusicReady();
            AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Intense);
            if (AudioManager.instance.enableDebugLogs)
                Debug.Log($"Wave {currentWaveIndex}: Music switched to Intense.");
        }

        SpawnDirection chosenDir = SpawnDirection.Top;
        if (wave.oneDirectionForAllEnemies && wave.spawnDirections != null && wave.spawnDirections.Count > 0)
            chosenDir = wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];

        foreach (var prefab in enemyPrefabsToSpawn)
        {
            SpawnDirection dir;

            if (wave.spawnDirections == null || wave.spawnDirections.Count == 0)
                dir = chosenDir;
            else
                dir = wave.oneDirectionForAllEnemies
                    ? chosenDir
                    : wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];

            SpawnEnemy(prefab, dir);

            float delay = UnityEngine.Random.Range(wave.minSpawnDelay, wave.maxSpawnDelay);
            yield return new WaitForSeconds(delay);
        }

        currentWaveIndex++;
    }

    /// Called by EnemyStats.PerformDeath() for ALL enemies (wave + gremlins + anything).
    /// This ONLY manages the spawner's internal count and music.
    /// It does NOT notify the orchestrator — WaveEnemy.OnDestroy() handles that.
    public void OnEnemyDeath()
    {
        enemiesAlive--;

        if (enemiesAlive <= 0)
        {
            if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
                AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Calm);
        }
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

    public void ShowWaveIndicatorsPublic(List<SpawnDirection> dirs)
    {
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

    void ShowWaveIndicator(SpawnDirection direction)
    {
        // Implementation for showing wave indicators
    }

    public void TestWave(int waveIndex)
    {
        if (waveIndex < 0 || waveIndex >= waveConfig.waves.Count)
        {
            Debug.LogWarning("Invalid wave index: " + waveIndex);
            return;
        }

        StopAllCoroutines();
        StartCoroutine(SpawnWave(waveIndex));
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
