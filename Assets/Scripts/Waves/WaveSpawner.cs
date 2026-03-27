using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

public class WaveSpawner : MonoBehaviour
{
    [Header("Spawn areas (Top/Bottom/Left/Right)")]
    public List<Collider2D> spawnAreas;

    [Header("Config")]
    public WaveConfig waveConfig;

    [Header("Modifiers")]
    public float waveSpawnDelayModifier = 0f;
    public float enemySpawnCountMultiplier = 1f;

    private int currentWaveIndex = 0;
    private int enemiesAlive = 0;
    private float countdown;

    // Preloaded resources 
    private bool resourcesPreloaded = false;

    void Start()
    {
        if (waveConfig == null)
        {
            Debug.LogError("No assigned WaveConfig for the WaveSpawner!");
            return;
        }

        countdown = GetModifiedWaveDelay();

        // Preload all enemy prefab resources on Start so Instantiate doesn't trigger
        StartCoroutine(PreloadEnemyResources());
    }


    /// Pre-warm all enemy prefabs by instantiating them once (off-screen, disabled),
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
                {
                    uniquePrefabs.Add(group.enemyPrefab);
                }
            }
        }

        foreach (var prefab in uniquePrefabs)
        {
            // Instantiate disabled, far off-screen
            GameObject warmup = Instantiate(prefab, new Vector3(-9999f, -9999f, 0f), Quaternion.identity);
            warmup.SetActive(true); // Briefly activate to trigger Awake/Start resource loading

            // Wait a frame so each prefab's loading is spread across frames
            yield return null;

            // Destroy the warmup instance
            Destroy(warmup);

            // Wait another frame for cleanup
            yield return null;
        }

        resourcesPreloaded = true;
    }

    void Update()
    {
        if (waveConfig == null) return;
        if (!resourcesPreloaded) return; // Don't start waves until resources are ready
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
                {
                    enemyPrefabsToSpawn.Add(group.enemyPrefab);
                }
            }
        }

        Shuffle(enemyPrefabsToSpawn);

        if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
        {
            AudioManager.instance.EnsureMusicReady();
            AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Intense);
            if (AudioManager.instance.enableDebugLogs)
            {
                Debug.Log($"Wave {currentWaveIndex}: Music switched to Intense.");
            }
        }

        SpawnDirection chosenDir = SpawnDirection.Top;
        if (wave.oneDirectionForAllEnemies && wave.spawnDirections != null && wave.spawnDirections.Count > 0)
        {
            chosenDir = wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];
        }

        foreach (var prefab in enemyPrefabsToSpawn)
        {
            SpawnDirection dir;

            if (wave.spawnDirections == null || wave.spawnDirections.Count == 0)
            {
                dir = chosenDir;
            }
            else
            {
                dir = wave.oneDirectionForAllEnemies
                    ? chosenDir
                    : wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];
            }

            SpawnEnemy(prefab, dir);

            float delay = UnityEngine.Random.Range(wave.minSpawnDelay, wave.maxSpawnDelay);
            yield return new WaitForSeconds(delay);
        }

        currentWaveIndex++;
    }

    public void OnEnemyDeath()
    {
        enemiesAlive--;
        if (enemiesAlive <= 0)
        {
            if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
            {
                AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Calm);
            }
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
        float x = UnityEngine.Random.Range(bounds.min.x, bounds.max.x);
        float y = UnityEngine.Random.Range(bounds.min.y, bounds.max.y);
        return new Vector2(x, y);
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

        EnemyStats stats = enemyObj.GetComponent<EnemyStats>();
        if (stats != null)
        {
            stats.ConfigureEnergyDrop(0.5f, 10);
        }

        enemiesAlive++;
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
