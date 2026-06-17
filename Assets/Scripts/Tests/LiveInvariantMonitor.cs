using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

// LIVE invariant watchdog. Drop on a GameObject in your gameplay scene and PLAY. It re-checks a
// set of co-op invariants several times per second and logs the MOMENT one breaks — and again
// when it recovers. It is EDGE-TRIGGERED: silence means everything is holding (that's the good
// case). To make activity visible it also prints a periodic HEARTBEAT with how many checks have
// run. Every line is tagged [LIVE].
// READ-ONLY and defensive: each check is wrapped; a system that isn't present yet is skipped,
// never a false alarm. Hotkeys: F11 = full status dump (with per-invariant check counts),
// F12 = toggle monitoring.
public class LiveInvariantMonitor : MonoBehaviour
{
    private const string TAG = "[LIVE] ";

    [Tooltip("Seconds between sweeps. 0 = every frame. 0.25 (4x/sec) is responsive and cheap.")]
    public float checkInterval = 0.25f;

    [Tooltip("Seconds between heartbeat lines (proof the monitor is alive). 0 = no heartbeat.")]
    public float heartbeatInterval = 5f;

    [Tooltip("Highest player index to range-check for per-player modifiers.")]
    public int maxPlayersToCheck = 4;

    [Tooltip("Log EVERY check result each sweep (very noisy — debugging only).")]
    public bool verbose = false;

    [Tooltip("Master switch. Toggle live with F12.")]
    public bool monitoring = true;

    private readonly Dictionary<string, bool> _state = new Dictionary<string, bool>();
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
    private float _accum, _hbAccum;
    private long _sweeps;

    private void Awake()
    {
        Debug.LogWarning(TAG + $"LiveInvariantMonitor ALIVE on scene '{gameObject.scene.name}'. " +
                         "Edge-triggered: silence = all holding. Heartbeat every " +
                         $"{heartbeatInterval:F0}s. F11 = status, F12 = toggle.");
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.f11Key.wasPressedThisFrame) DumpStatus();
            if (kb.f12Key.wasPressedThisFrame) ToggleMonitoring();
        }
#else
        if (Input.GetKeyDown(KeyCode.F11)) DumpStatus();
        if (Input.GetKeyDown(KeyCode.F12)) ToggleMonitoring();
#endif
        if (!monitoring) return;

        _accum += Time.unscaledDeltaTime;
        if (checkInterval <= 0f || _accum >= checkInterval)
        {
            _accum = 0f;
            Sweep();
        }

        if (heartbeatInterval > 0f)
        {
            _hbAccum += Time.unscaledDeltaTime;
            if (_hbAccum >= heartbeatInterval)
            {
                _hbAccum = 0f;
                Heartbeat();
            }
        }
    }

    private void ToggleMonitoring()
    {
        monitoring = !monitoring;
        Debug.LogWarning(TAG + (monitoring ? "monitoring RESUMED." : "monitoring PAUSED (F12 to resume)."));
    }

    private void Heartbeat()
    {
        int broken = 0;
        foreach (var kv in _state) if (!kv.Value) broken++;
        long totalChecks = 0;
        foreach (var kv in _counts) totalChecks += kv.Value;
        if (broken == 0)
            Debug.Log(TAG + $"heartbeat: sweep #{_sweeps}, {_state.Count} invariants, {totalChecks} checks run, all holding \u2713");
        else
            Debug.LogError(TAG + $"heartbeat: sweep #{_sweeps}, {broken} invariant(s) BROKEN right now (F11 for details).");
    }

    private void Sweep()
    {
        _sweeps++;

        // --- Audio (split-screen breakers) ---
        //CheckCount("fmod_listener_is_single", ActiveCount<StudioListener>(), 1,"FMOD needs exactly one active Studio Listener.");
        //CheckCount("unity_listener_is_single", ActiveCount<AudioListener>(), 1,"Unity needs exactly one active AudioListener.");
        //Check("audiomanager_present", AudioManager.instance != null,"AudioManager.instance went null.");

        // --- Shared economy ---
        TryCheck("wallet_nonnegative", () =>
        {
            if (EnergyManager.Instance == null) return (true, null);
            int e = EnergyManager.Instance.GetPlayerEnergy();
            return (e >= 0, e >= 0 ? null : $"shared wallet negative: {e}");
        });

        // --- Registry integrity ---
        TryCheck("registry_no_duplicate_index", () =>
        {
            var reg = PlayerRegistry.Instance;
            if (reg?.All == null) return (true, null);
            var seen = new HashSet<int>();
            foreach (var p in reg.All)
                if (p != null && !seen.Add(p.PlayerIndex))
                    return (false, $"duplicate PlayerIndex {p.PlayerIndex}.");
            return (true, null);
        });
        TryCheck("registry_stats_present", () =>
        {
            var reg = PlayerRegistry.Instance;
            if (reg?.All == null) return (true, null);
            foreach (var p in reg.All)
                if (p != null && p.Stats == null)
                    return (false, $"PlayerRef index {p.PlayerIndex} has null Stats.");
            return (true, null);
        });

        // --- Per-player resource bounds (catches runaway damage/heal/regen LIVE) ---
        TryCheck("player_health_in_bounds", () =>
        {
            var reg = PlayerRegistry.Instance;
            if (reg?.All == null) return (true, null);
            foreach (var p in reg.All)
            {
                var s = p?.Stats;
                if (s == null || s.maxHealth <= 0f) continue;
                if (float.IsNaN(s.currentHealth) || s.currentHealth < -0.01f || s.currentHealth > s.maxHealth + 0.01f)
                    return (false, $"P{p.PlayerIndex} health {s.currentHealth}/{s.maxHealth} out of range.");
            }
            return (true, null);
        });
        TryCheck("player_stamina_in_bounds", () =>
        {
            var reg = PlayerRegistry.Instance;
            if (reg?.All == null) return (true, null);
            foreach (var p in reg.All)
            {
                var s = p?.Stats;
                if (s == null || s.maxStamina <= 0f) continue;
                if (float.IsNaN(s.currentStamina) || s.currentStamina < -0.01f || s.currentStamina > s.maxStamina + 0.01f)
                    return (false, $"P{p.PlayerIndex} stamina {s.currentStamina}/{s.maxStamina} out of range.");
            }
            return (true, null);
        });
        TryCheck("player_position_finite", () =>
        {
            var reg = PlayerRegistry.Instance;
            if (reg?.All == null) return (true, null);
            foreach (var p in reg.All)
            {
                if (p == null) continue;
                Vector3 pos = p.transform.position;
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsInfinity(pos.x) || float.IsInfinity(pos.y))
                    return (false, $"P{p.PlayerIndex} position non-finite: {pos}.");
            }
            return (true, null);
        });

        // --- Per-player cooldown bounds ---
        TryCheck("cooldown_multipliers_in_0_1", () =>
        {
            for (int i = 0; i < maxPlayersToCheck; i++)
            {
                float m = CooldownModifier.MultiplierFor(i);
                if (float.IsNaN(m) || m < -0.0001f || m > 1.0001f)
                    return (false, $"CooldownModifier P{i} out of [0,1]: {m}");
            }
            return (true, null);
        });

        // --- Tower sanity (tether corruption tripwire) ---
        TryCheck("tower_stats_finite_nonneg", () =>
        {
            foreach (var t in Object.FindObjectsByType<Tower>(FindObjectsSortMode.None))
            {
                if (t == null) continue;
                float d, r;
                try { d = t.GetDamage(); r = t.GetRange(); } catch { continue; }
                if (float.IsNaN(d) || float.IsInfinity(d) || d < -0.0001f)
                    return (false, $"tower '{SafeName(t)}' damage invalid: {d}");
                if (float.IsNaN(r) || float.IsInfinity(r) || r < -0.0001f)
                    return (false, $"tower '{SafeName(t)}' range invalid: {r}");
            }
            return (true, null);
        });
        TryCheck("tower_upgrade_level_nonneg", () =>
        {
            foreach (var t in Object.FindObjectsByType<Tower>(FindObjectsSortMode.None))
            {
                if (t == null) continue;
                int lvl;
                try { lvl = t.upgradeLevel; } catch { continue; }
                if (lvl < 0) return (false, $"tower '{SafeName(t)}' negative upgradeLevel: {lvl}");
            }
            return (true, null);
        });

        // --- Tether decay aggregator bounds ---
        TryCheck("tether_decay_in_0_1", () =>
        {
            foreach (var b in Object.FindObjectsByType<TowerTetherDecayBoost>(FindObjectsSortMode.None))
            {
                if (b == null) continue;
                float m = b.GetDecayMultiplier();
                if (float.IsNaN(m) || m < -0.0001f || m > 1.0001f)
                    return (false, $"TowerTetherDecayBoost out of [0,1]: {m}");
            }
            return (true, null);
        });
    }

    [ContextMenu("Dump live invariant status (F11)")]
    private void DumpStatus()
    {
        Debug.Log(TAG + "===== LIVE INVARIANT STATUS =====");
        Sweep();
        if (_state.Count == 0) { Debug.Log(TAG + "(no invariants evaluated yet)"); return; }
        int broken = 0;
        foreach (var kv in _state)
        {
            int n = _counts.TryGetValue(kv.Key, out int c) ? c : 0;
            Debug.Log(TAG + $"  {(kv.Value ? "OK  " : "FAIL")}  {kv.Key}  (checked {n}x)");
            if (!kv.Value) broken++;
        }
        Debug.Log(TAG + $"sweeps: {_sweeps}");
        if (broken == 0) Debug.Log(TAG + "<color=lime>all invariants currently holding</color>");
        else Debug.LogError(TAG + $"{broken} invariant(s) currently BROKEN — see FAIL line(s).");
        Debug.Log(TAG + "=================================");
    }

    private void CheckCount(string key, int actual, int expected, string detail)
        => Record(key, actual == expected, actual == expected ? null : $"{detail} (found {actual})");

    private void Check(string key, bool ok, string detail)
        => Record(key, ok, ok ? null : detail);

    private void TryCheck(string key, System.Func<(bool ok, string detail)> probe)
    {
        bool ok; string detail;
        try { (ok, detail) = probe(); }
        catch { return; }
        Record(key, ok, detail);
    }

    private void Record(string key, bool ok, string detail)
    {
        _counts[key] = (_counts.TryGetValue(key, out int c) ? c : 0) + 1;

        bool had = _state.TryGetValue(key, out bool prev);
        bool prevOk = !had || prev;
        _state[key] = ok;

        if (verbose)
            Debug.Log(TAG + $"check {key}: {(ok ? "ok" : "FAIL")}");

        if (prevOk && !ok)
            Debug.LogError(TAG + $"BROKEN: {key}" + (string.IsNullOrEmpty(detail) ? "" : $" — {detail}"));
        else if (!prevOk && ok)
            Debug.LogWarning(TAG + $"recovered: {key}");
    }

    private static int ActiveCount<T>() where T : Behaviour
    {
        var all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int n = 0;
        foreach (var b in all) if (b != null && b.isActiveAndEnabled) n++;
        return n;
    }

    private static string SafeName(Tower t)
    {
        try { return t.towerName; } catch { return t.name; }
    }
}