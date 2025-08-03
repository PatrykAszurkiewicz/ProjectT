using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

public class WaveSpawner : MonoBehaviour
{
    public List<Collider2D> spawnAreas; // Top/Bottom/Left/Right jako nazwy
    public float timeBetweenWaves = 10f;
    private float countdown;
    //public float initialWaveDelay = 5f;

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

        if (currentWaveIndex >= waves.Count)
        {
            Debug.Log("End of waves");
            return;
        }

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

        List<string> enemyPrefabsToSpawn = new List<string>();

        foreach (var group in wave.enemies)
        {
            for (int i = 0; i < group.count; i++)
            {
                enemyPrefabsToSpawn.Add(group.enemyPrefab);
            }
        }

        // opcjonalnie: tasowanie kolejności
        Shuffle(enemyPrefabsToSpawn);
        AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Intense);

        foreach (var prefabName in enemyPrefabsToSpawn)
        {
            SpawnEnemy(prefabName, wave.spawnDirection);

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
            AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Calm);
        }
    }

    void LoadWaves()
    {
        string json = Resources.Load<TextAsset>("Data/waves").text;
        waves = JsonUtilityWrapper.FromJsonList<WaveData>(json);
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
    void SpawnEnemy(string prefabName, string direction)
    {
        Vector2 spawnPosition = GetRandomPositionInArea(direction);
        GameObject enemyPrefab = Resources.Load<GameObject>("Enemies/" + prefabName);
        Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
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
    void ShowWaveIndicator(string direction)
    {
        // np. wyświetlenie strzałki lub efektu
    }

}
