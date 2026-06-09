using System.Collections.Generic;
using UnityEngine;

// LORE CODEX 
// Stores lore the player has ever discovered. Mirrors WeaponBlueprintRegistry:
// a self-contained, DontDestroyOnLoad singleton backed by PlayerPrefs, so the
// collection survives across runs and sessions.

public class LoreCodex : MonoBehaviour
{
    private static LoreCodex _instance;
    public static LoreCodex Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<LoreCodex>();
                if (_instance == null)
                {
                    var go = new GameObject("LoreCodex");
                    _instance = go.AddComponent<LoreCodex>();
                }
            }
            return _instance;
        }
    }

    private const string PrefsKey = "LoreCodex_v1";

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [Tooltip("Wipe the discovered-lore collection on Play (testing only).")]
    [SerializeField] private bool resetOnPlay = false;

    private readonly HashSet<int> _discovered = new HashSet<int>();

    public System.Action<int> OnFragmentDiscovered;  // (fragment id)
    public System.Action OnCodexChanged;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        // DontDestroyOnLoad needs a root object.
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        if (resetOnPlay)
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            if (debugLog) Debug.Log("[LoreCodex] Reset on play.");
        }

        LoadFromPrefs();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // PUBLIC API 

    public bool IsDiscovered(int id) => _discovered.Contains(id);

    public int DiscoveredCount => _discovered.Count;

    public IReadOnlyCollection<int> DiscoveredIds => _discovered;

    /// Mark a fragment discovered. Returns true if it was newly added.
    public bool Discover(int id)
    {
        if (id < 0) return false;
        if (!_discovered.Add(id)) return false;

        SaveToPrefs();
        if (debugLog) Debug.Log($"[LoreCodex] Fragment discovered: id {id}  ({_discovered.Count}/{LoreContent.TotalCount}).");

        OnFragmentDiscovered?.Invoke(id);
        OnCodexChanged?.Invoke();
        return true;
    }

    /// Every existing fragment id the player has NOT discovered yet.
    public List<int> GetUndiscoveredIds()
    {
        var result = new List<int>();
        foreach (var f in LoreContent.All())
            if (!_discovered.Contains(f.id)) result.Add(f.id);
        return result;
    }

    public bool HasUndiscovered => GetUndiscoveredIds().Count > 0;

    /// Pick a random undiscovered fragment id, or -1 if everything is already known.
    public int PickRandomUndiscoveredId()
    {
        var pool = GetUndiscoveredIds();
        if (pool.Count == 0) return -1;
        return pool[Random.Range(0, pool.Count)];
    }

    /// Wipe the whole collection (debug / "reset progress" button).
    public void ClearAll()
    {
        _discovered.Clear();
        SaveToPrefs();
        OnCodexChanged?.Invoke();
    }

    // SNAPSHOT / RESTORE  (checkpoint + save integration) 

    /// Plain copy of the discovered set — stored in the wave snapshot and the save.
    public List<int> GetDiscoveredSnapshot() => new List<int>(_discovered);

    /// Replace the discovered set wholesale (used by rewind + resume). Persists to
    /// prefs so the codex and the run stay in agreement after the rollback.
    public void RestoreDiscoveredExact(IEnumerable<int> ids)
    {
        _discovered.Clear();
        if (ids != null)
            foreach (int id in ids) if (id >= 0) _discovered.Add(id);

        SaveToPrefs();
        if (debugLog) Debug.Log($"[LoreCodex] Restored discovered set ({_discovered.Count} fragments).");
        OnCodexChanged?.Invoke();
    }

    //  PERSISTENCE 

    private void LoadFromPrefs()
    {
        _discovered.Clear();
        if (!PlayerPrefs.HasKey(PrefsKey)) return;

        string raw = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(raw)) return;

        foreach (string part in raw.Split(','))
            if (int.TryParse(part, out int id)) _discovered.Add(id);

        if (debugLog && _discovered.Count > 0)
            Debug.Log($"[LoreCodex] Loaded {_discovered.Count} fragments from prefs.");
    }

    private void SaveToPrefs()
    {
        PlayerPrefs.SetString(PrefsKey, string.Join(",", _discovered));
        PlayerPrefs.Save();
    }
}
