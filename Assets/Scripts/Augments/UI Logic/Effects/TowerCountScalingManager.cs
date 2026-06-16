using UnityEngine;
using System.Collections.Generic;

//  340 — "Phalanx"
//  For every active tower on the map, ALL towers gain fire rate & range.
//  Fire rate rides the global multiplier (read in Tower.CanFire); range is
//  re-applied per tower because changing range also resizes the detection
//  collider.
//  Per-tower amounts are pushed in from the CSV by AugmentEffectHandler:
//    aug_firerate_per_tower : +fire rate per active tower (default 0.03 = +3%)
//    aug_range_per_tower    : +range    per active tower (default 0.02 = +2%)
public class TowerCountScalingManager : MonoBehaviour
{
    public float FireRatePerTower = 0.03f;
    public float RangePerTower = 0.02f;

    private const float RecalcInterval = 0.5f; // seconds between recounts
    private float timer;

    // Remember each tower's UN-buffed range so the bonus is always recomputed
    // from the base, never compounded frame over frame.
    private readonly Dictionary<Tower, float> baseRanges = new Dictionary<Tower, float>();

    public static TowerCountScalingManager Instance { get; private set; }

    // Creates the manager if needed and applies the CSV-tuned per-tower values.
    public static void Configure(float fireRatePerTower, float rangePerTower)
    {
        if (Instance == null)
        {
            var go = new GameObject("TowerCountScalingManager");
            Instance = go.AddComponent<TowerCountScalingManager>();
        }
        Instance.FireRatePerTower = fireRatePerTower;
        Instance.RangePerTower = rangePerTower;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < RecalcInterval) return;
        timer = 0f;
        Recalculate();
    }

    private void Recalculate()
    {
        var towers = Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);

        int count = 0;
        for (int i = 0; i < towers.Length; i++)
            if (towers[i] != null && !towers[i].IsDestroyed()) count++;

        TowerCombatModifiers.PerCountFireRateMultiplier = 1f + FireRatePerTower * count;

        float rangeFactor = 1f + RangePerTower * count;
        for (int i = 0; i < towers.Length; i++)
        {
            var t = towers[i];
            if (t == null || t.IsDestroyed()) continue;
            if (t.isEnergyGenerator) continue; // generators don't use combat range

            if (!baseRanges.TryGetValue(t, out float baseRange))
            {
                baseRange = t.GetRange();
                baseRanges[t] = baseRange;
            }
            t.SetRange(baseRange * rangeFactor);
        }

        // Drop entries for towers that no longer exist.
        var dead = new List<Tower>();
        foreach (var kv in baseRanges)
            if (kv.Key == null) dead.Add(kv.Key);
        foreach (var d in dead) baseRanges.Remove(d);
    }
}
