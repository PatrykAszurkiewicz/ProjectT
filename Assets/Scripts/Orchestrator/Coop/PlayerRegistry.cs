using System;
using System.Collections.Generic;
using UnityEngine;


// The single source of truth for "who are the players, and where are they".
// Every co-op-aware enemy/hazard resolves players through here instead of a
// global single-lookup like FindGameObjectWithTag("Player").
// Two selection helpers cover the two distinct needs:
//  - <see cref="NearestAlive"/> : chase / aggro — pick ONE player (proximity).
//  - <see cref="AllAliveInRadius"/> : area hazards — affect ALL players in range.
// With a single registered player both helpers reproduce the old behaviour
// exactly, so Phase 1 is a no-op for single player.
// Implemented as a lazily-created plain C# singleton (not a MonoBehaviour) so
// that <see cref="PlayerRef"/> can register from OnEnable without depending on
// any scene object existing first.

public class PlayerRegistry
{
    private static PlayerRegistry _instance;
    public static PlayerRegistry Instance => _instance ??= new PlayerRegistry();

    private readonly List<PlayerRef> _all = new List<PlayerRef>();

    /// <summary>All currently registered players, ordered by PlayerIndex.</summary>
    public IReadOnlyList<PlayerRef> All => _all;

    /// <summary>Number of registered players (1 in single player).</summary>
    public static int Count => Instance._all.Count;

    /// <summary>Raised when a player registers (joins / spawns / re-enables).</summary>
    public static event Action<PlayerRef> OnPlayerJoined;

    /// <summary>Raised when a player unregisters (leaves / despawns / disables).</summary>
    public static event Action<PlayerRef> OnPlayerLeft;

    // Reset static state between Play sessions when "Enter Play Mode without
    // domain reload" is enabled, so stale entries/handlers don't leak.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        OnPlayerJoined = null;
        OnPlayerLeft = null;
    }

    public static void Register(PlayerRef p)
    {
        if (p == null) return;
        var list = Instance._all;
        if (list.Contains(p)) return;
        list.Add(p);
        list.Sort((a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
        OnPlayerJoined?.Invoke(p);
    }

    public static void Unregister(PlayerRef p)
    {
        if (p == null) return;
        if (Instance._all.Remove(p))
            OnPlayerLeft?.Invoke(p);
    }

    /// <summary>
    /// Re-sort the roster by PlayerIndex. Called by CoopManager after it assigns
    /// a spawned player's index (which happens just after the PlayerRef has
    /// already registered with the default index 0).
    /// </summary>
    public static void ResortByIndex()
    {
        Instance._all.Sort((a, b) =>
        {
            int ia = a != null ? a.PlayerIndex : 0;
            int ib = b != null ? b.PlayerIndex : 0;
            return ia.CompareTo(ib);
        });
    }

    /// <summary>Get a player by index (prefers PlayerIndex match, falls back to list position). Null if none.</summary>
    public PlayerRef Get(int index)
    {
        for (int i = 0; i < _all.Count; i++)
            if (_all[i] != null && _all[i].PlayerIndex == index) return _all[i];
        if (index >= 0 && index < _all.Count) return _all[index];
        return null;
    }

    /// <summary>
    /// Category A — chase / aggro. Returns the nearest alive player to
    /// <paramref name="from"/> within <paramref name="maxRange"/> (default
    /// unlimited), or null if none qualify (caller should fall back to the core).
    /// </summary>
    public PlayerStats NearestAlive(Vector2 from,
                                    float maxRange = float.PositiveInfinity,
                                    bool includeCloaked = false)
    {
        PlayerStats best = null;
        float bestSqr = maxRange * maxRange;

        for (int i = 0; i < _all.Count; i++)
        {
            var p = _all[i];
            if (p == null || p.Stats == null || p.Stats.IsDead()) continue;
            if (!includeCloaked && p.IsCloaked) continue;

            float d = ((Vector2)p.transform.position - from).sqrMagnitude;
            if (d <= bestSqr)
            {
                bestSqr = d;
                best = p.Stats;
            }
        }
        return best;
    }

    /// <summary>
    /// Category B — area hazards. Enumerates every alive player whose position is
    /// within <paramref name="radius"/> of <paramref name="center"/>. Lazy; safe
    /// to iterate while applying damage because Unity defers Destroy to end of
    /// frame (the list is not mutated mid-iteration).
    /// </summary>
    public IEnumerable<PlayerStats> AllAliveInRadius(Vector2 center, float radius)
    {
        float r2 = radius * radius;
        for (int i = 0; i < _all.Count; i++)
        {
            var p = _all[i];
            if (p == null || p.Stats == null || p.Stats.IsDead()) continue;
            if (((Vector2)p.transform.position - center).sqrMagnitude <= r2)
                yield return p.Stats;
        }
    }

    /// <summary>
    /// True only if there is at least one registered player and every one of them
    /// is dead. Used by the co-op game-over check in Phase 7. Returns false when
    /// no players are registered yet (not a loss condition).
    /// </summary>
    public bool AllDead()
    {
        if (_all.Count == 0) return false;
        for (int i = 0; i < _all.Count; i++)
        {
            var p = _all[i];
            if (p != null && p.Stats != null && !p.Stats.IsDead())
                return false;
        }
        return true;
    }
}

