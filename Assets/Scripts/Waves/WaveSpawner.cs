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

    /// <summary>
    /// Called by EnemyStats.PerformDeath() for ALL enemies (wave + gremlins + anything).
    /// This ONLY manages the spawner's internal count and music.
    /// It does NOT notify the orchestrator — WaveEnemy.OnDestroy() handles that.
    /// </summary>
    public void OnEnemyDeath()
    {
        enemiesAlive--;

        if (enemiesAlive <= 0)
        {
            if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
                AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Calm);
        }
    }

    Vector2 GetRandomPositionInArea(SpawnDirection direction)
    {
        Collider2D area = spawnAreas.Find(c => c.name.Equals(direction.ToString(), StringComparison.OrdinalIgnoreCase));
        if (area == null)
        {
            Debug.LogWarning($"Brak obszaru spawnu dla kierunku: {direction}");
            return Vector2.zero;
        }

        Bounds bounds = area.bounds;

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

            if (!Physics2D.OverlapCircle(candidate, obstacleClearanceRadius, obstacleAvoidanceLayers))
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

        if (!Physics2D.OverlapCircle(best, obstacleClearanceRadius, obstacleAvoidanceLayers))
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
            if (!Physics2D.OverlapCircle(p, obstacleClearanceRadius, obstacleAvoidanceLayers))
                return p;
        }

        Debug.LogWarning($"WaveSpawner: could not find an obstacle-free spawn point in '{direction}' " +
                         $"after {attempts} random + {perimeterSamples} perimeter attempts. " +
                         $"Spawning at outer corner — enemy may still clip a wall.");
        return best;
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

        Vector2 spawnPosition = GetRandomPositionInArea(direction);
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
