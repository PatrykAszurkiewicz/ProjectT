using System.Collections.Generic;
using UnityEngine;

public enum SpawnDirection { Top, Bottom, Left, Right }

[System.Serializable]
public class EnemyGroup
{
    public GameObject enemyPrefab;
    public int count = 1;
}

[System.Serializable]
public class WaveData
{
    public int waveNumber;
    [Tooltip("Optional bonus delay before this wave")]
    public float extraDelayBeforeStart = 0f;
    [Tooltip("Choose from where enemies spawn")]
    public List<SpawnDirection> spawnDirections = new List<SpawnDirection>();
    [Tooltip("Spawn all enemies from 1 random direction")]
    public bool oneDirectionForAllEnemies = false;
    [Tooltip("Minimum spawn delay between enemies")]
    public float minSpawnDelay = 0.5f;
    [Tooltip("Maximum spawn delay between enemies")]
    public float maxSpawnDelay = 1.5f;
    public List<EnemyGroup> enemies = new List<EnemyGroup>();
}
