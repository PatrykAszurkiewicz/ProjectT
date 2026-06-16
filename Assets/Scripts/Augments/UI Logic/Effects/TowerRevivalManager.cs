using UnityEngine;
using System.Collections.Generic;

//  344 — "Phoenix Protocol"
//  At the start of a new stage, revive up to RevivesPerStage towers that died
//  during the previous stage.

public class TowerRevivalManager : MonoBehaviour
{
    public static TowerRevivalManager Instance { get; private set; }
    public static bool Enabled = false;

    public int RevivesPerStage = 1;

    private int lastStageIndex = -1;

    public static void Configure(int revivesPerStage)
    {
        if (Instance == null)
        {
            var go = new GameObject("TowerRevivalManager");
            Instance = go.AddComponent<TowerRevivalManager>();
            DontDestroyOnLoad(go); // survive any between-stage scene work
        }
        Enabled = true;
        Instance.RevivesPerStage = Mathf.Max(1, revivesPerStage);
        Debug.Log($"[AUGMENT] Phoenix Protocol armed — {Instance.RevivesPerStage} revive(s)/stage.");
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
        lastStageIndex = GameOrchestrator.Instance != null
            ? GameOrchestrator.Instance.CurrentStageIndex : 0;
    }

    private void Update()
    {
        if (!Enabled || GameOrchestrator.Instance == null) return;
        int current = GameOrchestrator.Instance.CurrentStageIndex;
        if (current != lastStageIndex)
        {
            lastStageIndex = current;
            OnStageStarted();
        }
    }

    // Revive up to RevivesPerStage currently-dead towers by refilling their energy.
    public void OnStageStarted()
    {
        if (!Enabled) return;

        // Collect dead-but-revivable towers (energy-depleted / disabled by damage).
        var towers = Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);
        var dead = new List<Tower>();
        for (int i = 0; i < towers.Length; i++)
        {
            var t = towers[i];
            if (t == null) continue;
            if (t.IsDestroyed())               // depleted OR disabled OR hard-destroyed
                dead.Add(t);
        }

        if (dead.Count == 0)
        {
            //Debug.Log("[AUGMENT] Phoenix Protocol — new stage, but no dead towers to revive.");
            return;
        }

        int revived = 0;
        for (int guard = 0; guard < 64 && revived < RevivesPerStage && dead.Count > 0; guard++)
        {
            int idx = Random.Range(0, dead.Count);
            var t = dead[idx];
            dead.RemoveAt(idx);
            if (t == null) continue;

            // Refill to full; SupplyEnergy re-enables disabled towers automatically.
            t.SupplyEnergy(t.GetMaxEnergy());

            // Verify it actually came back (hard-destroyed towers can't be refilled).
            if (t.IsOperational())
            {
                revived++;
                //Debug.Log($"[AUGMENT] Phoenix Protocol revived tower '{t.name}' for the new stage.");
            }
        }

        if (revived == 0)
            Debug.Log("[AUGMENT] Phoenix Protocol — dead towers found but none could be refilled.");
    }
}

