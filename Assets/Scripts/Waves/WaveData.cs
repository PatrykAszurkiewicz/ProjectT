using System.Collections.Generic;

[System.Serializable]
public class EnemyGroup
{
    public string enemyPrefab;
    public int count;
}

[System.Serializable]
public class WaveData
{
    public int waveNumber;
    public string spawnDirection;
    public float minSpawnDelay;
    public float maxSpawnDelay;
    public List<EnemyGroup> enemies;
}