using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

/// LIVE invariant watchdog. Drop this on a GameObject in your real gameplay scene and just
/// PLAY. It re-checks a set of co-op invariants a few times per second and logs the MOMENT
/// one breaks — and again when it recovers. It's edge-triggered: you get one line when a thing
/// breaks and one when it heals, not a per-frame flood. Every line is tagged [LIVE].
/// This is the "assert conditions while I play" tool: as you move, place towers, tether, pick
/// augments, take damage, go down/revive, etc., anything that violates an invariant surfaces
/// immediately with the offending value.
/// It is READ-ONLY and defensive: every check is wrapped, and a system that isn't present yet
/// (early frames) is simply skipped — never a false alarm, never a crash.
/// Hotkeys (Play mode): F11 = dump full current status,  F12 = toggle monitoring on/off.

public class LiveInvariantMonitor : MonoBehaviour
{
    private const string TAG = "[LIVE] ";

    [Tooltip("Seconds between sweeps. 0 = every frame. 0.25 (4x/sec) is plenty responsive and cheap.")]
    public float checkInterval = 0.25f;

    [Tooltip("Highest player index to range-check for per-player modifiers (covers up to N players).")]
    public int maxPlayersToCheck = 4;

    [Tooltip("Master switch. Toggle live with F12.")]
    public bool monitoring = true;

    // Per-invariant last-known OK state (absent = treated as OK so the first break logs once).
    private readonly Dictionary<string, bool> _state = new Dictionary<string, bool>();
    private float _accum;

    private void Awake()
    {
        Debug.LogWarning(TAG + $"LiveInvariantMonitor ALIVE on scene '{gameObject.scene.name}'. " +
                         "Watching co-op invariants as you play. F11 = status, F12 = toggle.");
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
        if (checkInterval > 0f && _accum < checkInterval) return;
        _accum = 0f;

        Sweep();
    }

    private void ToggleMonitoring()
    {
        monitoring = !monitoring;
        Debug.LogWarning(TAG + (monitoring ? "monitoring RESUMED." : "monitoring PAUSED (F12 to resume)."));
    }

    private void Sweep()
    {
        //  Audio: the split-screen breakers 
        //CheckCount("fmod_listener_is_single", ActiveCount<StudioListener>(), 1,
        //    "FMOD needs exactly one active Studio Listener; 0 = silent/mispositioned 3D, >1 = needs multi-listener config.");
        //CheckCount("unity_listener_is_single", ActiveCount<AudioListener>(), 1,
        //    "Unity needs exactly one active AudioListener (split-screen often ends up with 0 or 2).");
        Check("audiomanager_present", AudioManager.instance != null,
            "AudioManager.instance went null (destroyed as a duplicate, or scene unloaded it).");

        //  Shared economy 
        TryCheck("wallet_nonnegative", () =>
        {
            if (EnergyManager.Instance == null) return (true, null); // not up yet -> skip
            int e = EnergyManager.Instance.GetPlayerEnergy();
            return (e >= 0, e >= 0 ? null : $"shared wallet went negative: {e}");
        });

        //  Registry integrity 
        TryCheck("registry_no_duplicate_index", () =>
        {
            var reg = PlayerRegistry.Instance;
            if (reg == null || reg.All == null) return (true, null);
            var seen = new HashSet<int>();
            foreach (var p in reg.All)
            {
                if (p == null) continue;
                if (!seen.Add(p.PlayerIndex))
                    return (false, $"two registered players share PlayerIndex {p.PlayerIndex}.");
            }
            return (true, null);
        });
        TryCheck("registry_stats_present", () =>
        {
            var reg = PlayerRegistry.Instance;
            if (reg == null || reg.All == null) return (true, null);
            foreach (var p in reg.All)
                if (p != null && p.Stats == null)
                    return (false, $"registered PlayerRef index {p.PlayerIndex} has null Stats.");
            return (true, null);
        });

        // --- Per-player cooldown bounds (catches a runaway / leaked modifier) ---
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

        // --- Tower stats sanity (catches tether boost corruption LIVE) ---
        TryCheck("tower_stats_finite_nonneg", () =>
        {
            var towers = Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);
            foreach (var t in towers)
            {
                if (t == null) continue;
                float d, r;
                try { d = t.GetDamage(); r = t.GetRange(); } catch { continue; }
                if (float.IsNaN(d) || float.IsInfinity(d) || d < -0.0001f)
                    return (false, $"tower '{SafeName(t)}' damage invalid: {d} (tether snapshot corruption?)");
                if (float.IsNaN(r) || float.IsInfinity(r) || r < -0.0001f)
                    return (false, $"tower '{SafeName(t)}' range invalid: {r}");
            }
            return (true, null);
        });

        // --- Tether decay boost bounds (each shared aggregator stays a valid [0,1] multiplier) ---
        TryCheck("tether_decay_in_0_1", () =>
        {
            var boosts = Object.FindObjectsByType<TowerTetherDecayBoost>(FindObjectsSortMode.None);
            foreach (var b in boosts)
            {
                if (b == null) continue;
                float m = b.GetDecayMultiplier();
                if (float.IsNaN(m) || m < -0.0001f || m > 1.0001f)
                    return (false, $"TowerTetherDecayBoost multiplier out of [0,1]: {m}");
            }
            return (true, null);
        });
    }

    //  ===== status dump =====

    [ContextMenu("Dump live invariant status (F11)")]
    private void DumpStatus()
    {
        Debug.Log(TAG + "===== LIVE INVARIANT STATUS =====");
        Sweep(); // refresh
        if (_state.Count == 0) { Debug.Log(TAG + "(no invariants evaluated yet)"); return; }
        int broken = 0;
        foreach (var kv in _state)
        {
            Debug.Log(TAG + $"  {(kv.Value ? "OK  " : "FAIL")}  {kv.Key}");
            if (!kv.Value) broken++;
        }
        if (broken == 0) Debug.Log(TAG + "<color=lime>all invariants currently holding</color>");
        else Debug.LogError(TAG + $"{broken} invariant(s) currently BROKEN — see FAIL line(s) above.");
        Debug.Log(TAG + "=================================");
    }

    //   evaluation core (edge-triggered logging) 

    private void CheckCount(string key, int actual, int expected, string detailIfBroken)
        => Record(key, actual == expected, actual == expected ? null : $"{detailIfBroken} (found {actual})");

    private void Check(string key, bool ok, string detailIfBroken)
        => Record(key, ok, ok ? null : detailIfBroken);

    private void TryCheck(string key, System.Func<(bool ok, string detail)> probe)
    {
        bool ok; string detail;
        try { (ok, detail) = probe(); }
        catch { return; } // system not ready / transient — don't toggle state, don't alarm
        Record(key, ok, detail);
    }

    private void Record(string key, bool ok, string detail)
    {
        bool had = _state.TryGetValue(key, out bool prev);
        bool prevOk = !had || prev; // unseen == OK baseline
        _state[key] = ok;

        if (prevOk && !ok)
            Debug.LogError(TAG + $"BROKEN: {key}" + (string.IsNullOrEmpty(detail) ? "" : $" — {detail}"));
        else if (!prevOk && ok)
            Debug.LogWarning(TAG + $"recovered: {key}");
    }

    private static int ActiveCount<T>() where T : Behaviour
    {
        var all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int n = 0;
        foreach (var b in all)
            if (b != null && b.isActiveAndEnabled) n++;
        return n;
    }

    private static string SafeName(Tower t)
    {
        try { return t.towerName; } catch { return t.name; }
    }
}
