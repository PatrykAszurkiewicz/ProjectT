using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    public List<Transform> spawnPoints; // Top/Bottom/Left/Right jako nazwy
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
            yield return new WaitForSeconds(wave.spawnDelay);
        }

        currentWaveIndex++;
    }

    void SpawnEnemy(WaveData wave)
    {
        Transform spawnPoint = GetSpawnPointByDirection(wave.spawnDirection);
        GameObject enemyPrefab = Resources.Load<GameObject>("Enemies/" + wave.enemyPrefab);
        Instantiate(enemyPrefab, spawnPoint.position, Quaternion.identity);
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

    Transform GetSpawnPointByDirection(string direction)
    {
        return spawnPoints.Find(t => t.name.Equals(direction, StringComparison.OrdinalIgnoreCase));
    }

    void ShowWaveIndicator(string direction)
    {
        // np. wyświetlenie strzałki lub efektu
    }
}
