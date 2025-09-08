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
    public List<SpawnDirection> spawnDirections = new List<SpawnDirection>();
    public bool oneDirectionForAllEnemies = false;
    public float minSpawnDelay = 0.5f;
    public float maxSpawnDelay = 1.5f;
    public List<EnemyGroup> enemies = new List<EnemyGroup>();
}
