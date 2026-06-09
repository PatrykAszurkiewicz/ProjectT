using System.Collections.Generic;
using UnityEngine;

// RUNTIME DATA for a single stage during a run.
[System.Serializable]
public class StageData
{
    public int stageIndex;            // 0, 1, 2, 3...
    public BiomeType biome;           // randomly picked
    public MapLayoutDefinition layout; // null = use TowerDefenseMap default rings
    public bool nightMode;            // randomly rolled
    public bool fogEnabled;           // randomly rolled
    public bool rainEnabled;          // randomly rolled
    public bool snowEnabled;          // randomly rolled
    public bool balloonsEnabled;      // randomly rolled — night lantern balloons
    public float enemyCountMultiplier;    // scales up per stage
    public float spawnDelayMultiplier;    // scales down per stage (faster)
    public float enemyHealthMultiplier = 1f; // scales enemy max HP per stage (compounding)
    public float enemyDamageMultiplier = 1f; // scales enemy damage per stage (compounding)
    public List<WaveData> waves;      // the actual waves to spawn
    public bool hasStageBoss;         // spawn boss after waves?
    public GameObject stageBossPrefab; // resolved at plan time — WHICH boss spawns (null = none)

    public override string ToString()
    {
        return $"Stage {stageIndex + 1}: {biome}" +
               $"{(layout != null ? $" [{layout.layoutName}]" : "")}" +
               $"{(nightMode ? " [NIGHT]" : "")}" +
               $"{(balloonsEnabled ? " [BALLOONS]" : "")}" +
               $"{(fogEnabled ? " [FOG]" : "")}" +
               $"{(rainEnabled ? " [RAIN]" : "")}" +
               $" — {waves?.Count ?? 0} waves" +
               $" — enemies ×{enemyCountMultiplier:F2}" +
               $" — HP ×{enemyHealthMultiplier:F2} dmg ×{enemyDamageMultiplier:F2}" +
               $"{(hasStageBoss ? " + BOSS" : "")}";
    }
}

