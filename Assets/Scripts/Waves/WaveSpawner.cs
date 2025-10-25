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

    private int currentWaveIndex = 0;
    private int enemiesAlive = 0;
    private float countdown;

    void Start()
    {
        if (waveConfig == null)
        {
            Debug.LogError("❌ Brak przypisanego WaveConfig do WaveSpawner!");
            return;
        }

        countdown = waveConfig.timeBetweenWaves;
    }

    void Update()
    {
        if (waveConfig == null) return;
        if (enemiesAlive > 0) return;
        if (currentWaveIndex >= waveConfig.waves.Count) return;

        countdown -= Time.deltaTime;

        if (countdown <= 0f)
        {
            StartCoroutine(SpawnWave(currentWaveIndex));
            countdown = waveConfig.timeBetweenWaves;
        }
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

                for (int i = 0; i < group.count; i++)
                {
                    enemyPrefabsToSpawn.Add(group.enemyPrefab);
                }
            }
        }

        // Opcjonalne tasowanie kolejności
        Shuffle(enemyPrefabsToSpawn);

        if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
        {
            // Ensure music system is ready
            AudioManager.instance.EnsureMusicReady();
            // Force music to Intense with retry logic
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

        // Spawn każdego prefabu — dla każdego losujemy kierunek (chyba że oneDirectionForAllEnemies == true)
        foreach (var prefab in enemyPrefabsToSpawn)
        {
            SpawnDirection dir;

            if (wave.spawnDirections == null || wave.spawnDirections.Count == 0)
            {
                // brak zdefiniowanych kierunków -> fallback (chosenDir albo Top)
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
        Instantiate(prefab, spawnPosition, Quaternion.identity);
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

    void ShowWaveIndicators(List<SpawnDirection> dirs)
    {
        if (dirs == null) return;
        foreach (var d in dirs) ShowWaveIndicator(d);
    }

    void ShowWaveIndicator(SpawnDirection direction)
    {
        // np. wyświetlenie strzałki/efektu dla konkretnego kierunku
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
}
