using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WaveConfig", menuName = "Game/Wave Config")]
public class WaveConfig : ScriptableObject
{
    public float timeBetweenWaves = 10f; //global time between waves
    public List<WaveData> waves = new List<WaveData>();
}
