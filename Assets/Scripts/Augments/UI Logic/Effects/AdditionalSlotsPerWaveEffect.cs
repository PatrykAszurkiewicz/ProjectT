using UnityEngine;

// Reveals one bonus tower slot every Xth wavee
// Hooks into GameOrchestrator.OnWaveCleared if available 
// WaveSpawner.GetCurrentWaveIndex() in standalone mode.
// The component is AddComponent'd by AugmentRegistry on the WaveSpawner
// GameObject (matches the AdrenalineRushEffect / EscalationEffect pattern).
public class AdditionalSlotsPerWaveEffect : MonoBehaviour
{
    [Header("CSV-Driven Parameters")]
    [System.NonSerialized] public int waveInterval = 1;       // reveal a slot every N waves

    [System.NonSerialized] public int slotsPerTrigger = 1;    // how many slots to reveal each time
    [System.NonSerialized] public int maxSlotsToReveal = 0;   // 0 = unlimited (capped by layout pool)

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private WaveSpawner waveSpawner;
    private TowerDefenseMap map;

    // For orchestrator-mode subscription
    private bool subscribedToOrchestrator = false;

    // For standalone polling fallback
    private int lastObservedWave = -1;
    private const float POLL_INTERVAL = 0.5f;

    // Counters
    private int wavesSinceLastReveal = 0;
    private int slotsRevealedTotal = 0;

    void Start()
    {
        // Validate parameters
        if (waveInterval <= 0)
        {
            Debug.LogWarning("[ADDITIONAL_SLOTS_PER_WAVE] waveInterval was 0 or negative; defaulting to 3");
            waveInterval = 3;
        }
        if (slotsPerTrigger <= 0) slotsPerTrigger = 1;

        waveSpawner = GetComponent<WaveSpawner>();
        if (waveSpawner == null)
        {
            waveSpawner = FindFirstObjectByType<WaveSpawner>();
        }

        map = FindFirstObjectByType<TowerDefenseMap>();
        if (map == null)
        {
            Debug.LogError("[ADDITIONAL_SLOTS_PER_WAVE] TowerDefenseMap not found in scene!");
            enabled = false;
            return;
        }

        // Prefer the orchestrator's OnWaveCleared event if available.
        // This is the correct signal in campaign / multi-stage mode.
        if (GameOrchestrator.Instance != null)
        {
            GameOrchestrator.Instance.OnWaveCleared += HandleOrchestratorWaveCleared;
            subscribedToOrchestrator = true;

            if (showDebugLogs)
            {
                Debug.Log($"<color=yellow>[ADDITIONAL_SLOTS_PER_WAVE] Subscribed to GameOrchestrator.OnWaveCleared</color>\n" +
                          $"  interval={waveInterval}, slotsPerTrigger={slotsPerTrigger}, " +
                          $"maxSlots={maxSlotsToReveal} (0 = unlimited)");
            }
        }
        else if (waveSpawner != null)
        {
            // Standalone fallback: poll WaveSpawner.GetCurrentWaveIndex()
            lastObservedWave = waveSpawner.GetCurrentWaveIndex();
            InvokeRepeating(nameof(PollWaveSpawner), POLL_INTERVAL, POLL_INTERVAL);

            if (showDebugLogs)
            {
                Debug.Log($"<color=yellow>[ADDITIONAL_SLOTS_PER_WAVE] No orchestrator - polling WaveSpawner</color>\n" +
                          $"  interval={waveInterval}, slotsPerTrigger={slotsPerTrigger}, " +
                          $"maxSlots={maxSlotsToReveal} (0 = unlimited). " +
                          $"Starting wave: {lastObservedWave}");
            }
        }
        else
        {
            Debug.LogError("[ADDITIONAL_SLOTS_PER_WAVE] No GameOrchestrator AND no WaveSpawner - cannot track waves!");
            enabled = false;
        }
    }

    // Orchestrator-mode handler. Called once per wave cleared with the wave index (0-based).
    private void HandleOrchestratorWaveCleared(int waveIndex)
    {
        if (showDebugLogs)
        {
            Debug.Log($"<color=green>[ADDITIONAL_SLOTS_PER_WAVE] OnWaveCleared event: wave {waveIndex} cleared</color>");
        }

        wavesSinceLastReveal++;

        if (wavesSinceLastReveal >= waveInterval)
        {
            wavesSinceLastReveal -= waveInterval;
            TriggerReveal(waveIndex + 1); // human-friendly wave number
        }
    }

    // Standalone-mode polling. Watches WaveSpawner.currentWaveIndex.
    private void PollWaveSpawner()
    {
        if (waveSpawner == null) return;

        int current = waveSpawner.GetCurrentWaveIndex();
        if (current == lastObservedWave) return;

        int advanced = current - lastObservedWave;
        lastObservedWave = current;

        if (advanced <= 0) return; // wave reset / restart

        if (showDebugLogs)
        {
            Debug.Log($"<color=green>[ADDITIONAL_SLOTS_PER_WAVE] WaveSpawner advanced by {advanced} (now at {current})</color>");
        }

        wavesSinceLastReveal += advanced;

        while (wavesSinceLastReveal >= waveInterval)
        {
            wavesSinceLastReveal -= waveInterval;
            if (!TriggerReveal(current)) return;
        }
    }

    // Returns false if the effect should stop (cap hit or pool exhausted).
    private bool TriggerReveal(int waveNum)
    {
        if (maxSlotsToReveal > 0 && slotsRevealedTotal >= maxSlotsToReveal)
        {
            if (showDebugLogs)
            {
                Debug.Log($"[ADDITIONAL_SLOTS_PER_WAVE] Reached max slots cap ({maxSlotsToReveal}). Disabling effect.");
            }
            StopAll();
            return false;
        }

        int requested = slotsPerTrigger;
        if (maxSlotsToReveal > 0)
        {
            int remainingCap = maxSlotsToReveal - slotsRevealedTotal;
            requested = Mathf.Min(requested, remainingCap);
        }

        int added = map.AddBonusSlots(requested);
        slotsRevealedTotal += added;

        if (added > 0)
        {
            //Debug.Log($"<color=magenta>[ADDITIONAL_SLOTS_PER_WAVE] ★ Wave {waveNum}: revealed {added} bonus slot(s). " +
            //          $"Total: {slotsRevealedTotal}{(maxSlotsToReveal > 0 ? $"/{maxSlotsToReveal}" : "")} ★</color>");
            return true;
        }
        else
        {
            //Debug.LogWarning($"[ADDITIONAL_SLOTS_PER_WAVE] Wave {waveNum}: no bonus slots available " +
            //                 "in current layout (pool exhausted). Add positions to " +
            //                 "MapLayoutDefinition.bonusSlotPositions.");
            StopAll();
            return false;
        }
    }

    private void StopAll()
    {
        CancelInvoke(nameof(PollWaveSpawner));
        if (subscribedToOrchestrator && GameOrchestrator.Instance != null)
        {
            GameOrchestrator.Instance.OnWaveCleared -= HandleOrchestratorWaveCleared;
            subscribedToOrchestrator = false;
        }
    }

    void OnDestroy()
    {
        StopAll();
    }

    void OnDisable()
    {
        StopAll();
    }

    // Public getters for UI / debugging
    public int GetSlotsRevealed() => slotsRevealedTotal;
    public int GetWavesUntilNextReveal() => Mathf.Max(0, waveInterval - wavesSinceLastReveal);
}
