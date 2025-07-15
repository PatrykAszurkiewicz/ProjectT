using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    public List<Collider2D> spawnAreas; // Top/Bottom/Left/Right jako nazwy
    public float timeBetweenWaves = 5f;
    private float countdown;

    private int currentWaveIndex = 0;
    private List<WaveData> waves;
    private int enemiesAlive = 0;

    void Start()
    {
        LoadWaves();
        countdown = timeBetweenWaves;
    }

    void Update()
    {
        if (enemiesAlive > 0) return;

        countdown -= Time.deltaTime;

        if (countdown <= 0f)
        {
            StartCoroutine(SpawnWave());
            countdown = timeBetweenWaves;
        }
    }

    IEnumerator SpawnWave()
    {
        WaveData wave = waves[currentWaveIndex];
        ShowWaveIndicator(wave.spawnDirection); // 🔺 inicjator kierunku

        for (int i = 0; i < wave.enemyCount; i++)
        {
            SpawnEnemy(wave);

            float delay = UnityEngine.Random.Range(wave.minSpawnDelay, wave.maxSpawnDelay);
            yield return new WaitForSeconds(delay);
        }

        currentWaveIndex++;
    }

    void SpawnEnemy(WaveData wave)
    {
        Vector2 spawnPosition = GetRandomPositionInArea(wave.spawnDirection);
        GameObject enemyPrefab = Resources.Load<GameObject>("Enemies/" + wave.enemyPrefab);
        Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
        enemiesAlive++;
    }

    public void OnEnemyDeath()
    {
        enemiesAlive--;
    }

    void LoadWaves()
    {
        string json = Resources.Load<TextAsset>("Data/waves").text;
        waves = JsonUtilityWrapper.FromJsonList<WaveData>(json);
    }

    Collider2D GetSpawnAreaByDirection(string direction)
    {
        return spawnAreas.Find(c => c.name.Equals(direction, StringComparison.OrdinalIgnoreCase));
    }

    Vector2 GetRandomPositionInArea(string direction)
    {
        Collider2D area = spawnAreas.Find(c => c.name.Equals(direction, StringComparison.OrdinalIgnoreCase));

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


    void ShowWaveIndicator(string direction)
    {
        // np. wyświetlenie strzałki lub efektu
    }

}
