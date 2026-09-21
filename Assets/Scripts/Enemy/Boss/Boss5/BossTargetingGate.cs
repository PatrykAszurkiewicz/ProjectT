using System.Collections.Generic;
using UnityEngine;

//  BOSS TARGETING GATE
//  Registry of enemies that towers must currently IGNORE.
//  The Bellkeeper's State B requires that "towers automatically cease attacking
//  the boss". There is no existing project-wide concept of a temporarily
//  untargetable enemy, and adding a field to EnemyStats would touch every enemy in
//  the game. This gate is opt-in from both ends instead: Boss5 registers itself,
//  and the tower targeting code asks one question. Nothing else in the project
//  changes behaviour, because for every other enemy the answer is always false.


public static class BossTargetingGate
{
    private static readonly HashSet<GameObject> _untargetable = new HashSet<GameObject>();

    /// Clear the registry between play sessions. Required because with domain
    /// reload disabled a static collection survives exiting play mode, and a stale
    /// entry would make the next run's enemies permanently untargetable.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _untargetable.Clear();

    /// Mark (or unmark) an enemy as off-limits to towers. Safe to call every frame.
    public static void Set(GameObject enemy, bool untargetable)
    {
        if (enemy == null) return;
        if (untargetable) _untargetable.Add(enemy);
        else _untargetable.Remove(enemy);
    }

    /// True when towers must not target / damage this object right now.
    /// Returns false for everything that never registered, which is every enemy
    /// in the game except a Bellkeeper in State B.
    public static bool IsUntargetable(GameObject enemy)
    {
        if (enemy == null || _untargetable.Count == 0) return false;
        if (_untargetable.Contains(enemy)) return true;

        // Also honour the flag when the tower is holding a CHILD collider of the
        // boss rather than its root (large enemies often have hit-box children).
        var root = enemy.transform.root;
        return root != null && root.gameObject != enemy && _untargetable.Contains(root.gameObject);
    }

    /// Component overload, so the call site can pass whatever it already has.
    public static bool IsUntargetable(Component c) => c != null && IsUntargetable(c.gameObject);

    /// Transform overload.
    public static bool IsUntargetable(Transform t) => t != null && IsUntargetable(t.gameObject);

    /// Drop destroyed objects. Optional — call from a manager if you ever worry
    /// about the set growing; with one boss at a time it never does.
    public static void Sweep()
    {
        if (_untargetable.Count == 0) return;
        _untargetable.RemoveWhere(go => go == null);
    }
}


