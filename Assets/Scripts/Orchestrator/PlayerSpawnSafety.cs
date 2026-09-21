using System.Collections.Generic;
using UnityEngine;

// Keeps players out of the parts of a map layout.

public static class PlayerSpawnSafety
{
    // Extra breathing room beyond the player's own collider radius when CHOOSING a
    // new position. Deliberately small: it only has to stop the body resting flush
    // against a wall, where the solver can still nudge it.
    public static float clearancePadding = 0.15f;

    // Never relocate a player further than this from where they were meant to be.
    // Beyond it we give up and leave the position alone rather than teleport
    // someone across the map.
    public static float maxRelocationDistance = 12f;

    // Radial granularity of the outward search.
    public static float searchStep = 0.35f;

    // Directions tried per ring. 24 => one candidate every 15 degrees.
    public static int samplesPerRing = 24;

    // Used when a player has no usable non-trigger collider to measure.
    public static float fallbackBodyRadius = 0.5f;

    public static bool debugLog = true;

    private static TowerDefenseMap _map;

    // Blocking colliders, plus a cached bounding disc for each so the search can
    // reject far-away obstacles with one squared-distance test instead of a
    // ClosestPoint call. Parallel lists, all three the same length.
    private static readonly List<Collider2D> _blockers = new List<Collider2D>();
    private static readonly List<Vector2> _blockerCenters = new List<Vector2>();
    private static readonly List<float> _blockerRadii = new List<float>();

    private static readonly List<Collider2D> _raw = new List<Collider2D>();
    private static readonly List<Collider2D> _ownColliders = new List<Collider2D>();

    // "Enter Play Mode without domain reload" keeps statics alive between sessions;
    // a cached map from the previous session would be a destroyed object and every
    // query would silently find nothing.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _map = null;
        _blockers.Clear();
        _blockerCenters.Clear();
        _blockerRadii.Clear();
        _raw.Clear();
        _ownColliders.Clear();
    }

    private static TowerDefenseMap Map
    {
        get
        {
            // Unity's overloaded == reports a destroyed object as null, so this also
            // re-finds the map after a scene load.
            if (_map == null) _map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
            return _map;
        }
    }

    //  PUBLIC API
    /// The requested position if it is clear of solid layout geometry, otherwise the
    /// nearest position that is. Z (sorting depth) is preserved.
    /// Returns <paramref name="desired"/> untouched when there is no map, no
    /// obstacles, or nothing in the way — so callers can route every spawn through
    /// this without changing existing behaviour.

    public static Vector3 Resolve(Vector3 desired, float bodyRadius)
    {
        if (!RefreshBlockers()) return desired;

        Vector2 p = desired;
        float clearance = bodyRadius + Mathf.Max(0f, clearancePadding);

        if (!Overlaps(p, clearance)) return desired;

        Vector2 escape = PreferredEscapeDirection(p, clearance);

        // Full clearance first. If a tight layout has nothing that roomy, accept a
        // spot that merely fits the body — still far better than being in a wall.
        Vector3 found;
        bool ok = TrySearch(p, clearance, escape, out found);
        if (!ok && bodyRadius < clearance) ok = TrySearch(p, bodyRadius, escape, out found);

        if (!ok)
        {
            Debug.LogWarning($"[SpawnSafety] {desired} is inside a layout obstacle and no clear spot " +
                             $"was found within {maxRelocationDistance:F0}u. Leaving the position as-is.");
            return desired;
        }

        found.z = desired.z;
        if (debugLog)
            Debug.Log($"[SpawnSafety] Spawn point {desired} was inside a layout obstacle — " +
                      $"moved to {found} ({Vector2.Distance(desired, found):F2}u away).");
        return found;
    }

    /// <summary>Convenience overload that measures the body from the object's own collider.</summary>
    public static Vector3 Resolve(Vector3 desired, GameObject forPlayer)
    {
        return Resolve(desired, BodyRadius(forPlayer));
    }


    /// Move <paramref name="player"/> out of solid layout geometry if — and only if —
    /// it is genuinely inside some. Returns true when the player was moved.

    public static bool EvacuateIfStuck(GameObject player)
    {
        if (player == null) return false;
        if (!RefreshBlockers()) return false;

        float body = BodyRadius(player);
        Vector3 pos = player.transform.position;

        // STRICT test: no padding, and a hair under the true radius. Standing
        // shoulder-to-shoulder with a wall is legal play and must never trigger a
        // teleport — only real penetration does.
        if (!Overlaps(pos, body * 0.98f)) return false;

        Vector2 escape = PreferredEscapeDirection(pos, body);
        float clearance = body + Mathf.Max(0f, clearancePadding);

        Vector3 target;
        bool ok = TrySearch(pos, clearance, escape, out target);
        if (!ok) ok = TrySearch(pos, body, escape, out target);

        if (!ok)
        {
            Debug.LogWarning($"[SpawnSafety] '{player.name}' is inside a layout obstacle at {pos} and " +
                             $"no clear spot was found within {maxRelocationDistance:F0}u.");
            return false;
        }

        target.z = pos.z;
        MoveTo(player, target);

        if (debugLog)
            Debug.Log($"[SpawnSafety] '{player.name}' was inside a layout obstacle at {pos} — " +
                      $"moved to {target} ({Vector2.Distance(pos, target):F2}u away).");
        return true;
    }

    /// Push every registered player out of solid layout geometry. Called right after
    /// the map is (re)built, which is the moment obstacles can appear on top of
    /// someone. Cheap no-op when there are no players yet or the layout has no
    /// blocking obstacles.

    public static int EvacuateAllPlayers()
    {
        var registry = PlayerRegistry.Instance;
        if (registry == null) return 0;

        var all = registry.All;
        if (all == null || all.Count == 0) return 0;
        if (!RefreshBlockers()) return 0;

        int moved = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null) continue;
            if (EvacuateIfStuck(p.gameObject)) moved++;
        }
        return moved;
    }

    /// Radius of the player's physical body, taken from its own non-trigger collider.
    /// Computed from the collider's SHAPE rather than its bounds so it still works
    /// while the collider is disabled — which is exactly the state a downed player is
    /// in when PlayerRespawnController places them.
    public static float BodyRadius(GameObject go)
    {
        if (go == null) return fallbackBodyRadius;

        // Root only. Children carry cursor / aim / trigger colliders that say nothing
        // about how much space the character physically occupies.
        go.GetComponents(_ownColliders);

        float best = 0f;
        for (int i = 0; i < _ownColliders.Count; i++)
        {
            var c = _ownColliders[i];
            if (c == null || c.isTrigger) continue;
            best = Mathf.Max(best, ShapeRadius(c));
        }
        _ownColliders.Clear();

        return best > 0.01f ? best : fallbackBodyRadius;
    }

    //  INTERNALS

    // Rebuilds the blocking-collider cache. False means "nothing can block anyone",
    // which lets every caller bail out immediately.
    private static bool RefreshBlockers()
    {
        _blockers.Clear();
        _blockerCenters.Clear();
        _blockerRadii.Clear();

        var map = Map;
        if (map == null) return false;

        map.CollectBlockingObstacleColliders(_raw);
        if (_raw.Count == 0) return false;

        // Colliders created moments ago (AddComponent straight after setting the
        // transform) are not in the physics world's transform state until the next
        // fixed step. Both bounds and ClosestPoint read that state, so flush it once
        // here rather than trusting the ordering.
        if (Application.isPlaying) Physics2D.SyncTransforms();

        for (int i = 0; i < _raw.Count; i++)
        {
            var c = _raw[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;

            Bounds b = c.bounds;
            _blockers.Add(c);
            _blockerCenters.Add(b.center);
            _blockerRadii.Add(new Vector2(b.extents.x, b.extents.y).magnitude);
        }

        _raw.Clear();
        return _blockers.Count > 0;
    }

    // True when a disc of `radius` at `p` touches any blocking obstacle.
    // ClosestPoint returns the query point itself when it is inside the collider, so
    // full containment reads as distance 0 and is caught correctly.
    private static bool Overlaps(Vector2 p, float radius)
    {
        float r2 = radius * radius;

        for (int i = 0; i < _blockers.Count; i++)
        {
            // Bounding-disc reject: skips the ClosestPoint call for everything that
            // clearly cannot reach us, which is most of the map on every candidate.
            float reach = _blockerRadii[i] + radius;
            if ((_blockerCenters[i] - p).sqrMagnitude > reach * reach) continue;

            var c = _blockers[i];
            if (c == null) continue;

            if ((c.ClosestPoint(p) - p).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    // Rough "which way is out" from the obstacles currently overlapping p, so the
    // search tries the short way out before circling the whole ring.
    private static Vector2 PreferredEscapeDirection(Vector2 p, float radius)
    {
        Vector2 sum = Vector2.zero;
        float r2 = radius * radius;

        for (int i = 0; i < _blockers.Count; i++)
        {
            float reach = _blockerRadii[i] + radius;
            if ((_blockerCenters[i] - p).sqrMagnitude > reach * reach) continue;

            var c = _blockers[i];
            if (c == null) continue;

            Vector2 away = p - c.ClosestPoint(p);
            if (away.sqrMagnitude > r2) continue;   // not actually one of the offenders

            // Deep inside: ClosestPoint hands back p itself, so there is no direction
            // to read from it. Use the collider's centre instead.
            if (away.sqrMagnitude < 0.0001f)
                away = p - _blockerCenters[i];

            if (away.sqrMagnitude > 0.0001f) sum += away.normalized;
        }

        if (sum.sqrMagnitude > 0.0001f) return sum.normalized;

        // Nothing usable (perfectly symmetric containment). Away from the map centre
        // is a reasonable first guess — it heads for open ground rather than the core
        // — and the ring search covers every other direction anyway.
        return p.sqrMagnitude > 0.0001f ? p.normalized : Vector2.right;
    }

    // Expanding-ring search. Within each ring the angles are visited outward from
    // `preferred` (0, +1, -1, +2, -2 ...), so the first hit is the nearest clear spot
    // in roughly the direction the obstacle is pushing.
    private static bool TrySearch(Vector2 from, float requiredClearance, Vector2 preferred, out Vector3 found)
    {
        found = from;

        float step = Mathf.Max(0.15f, searchStep);
        int samples = Mathf.Max(4, samplesPerRing);
        float angleStep = 360f / samples;
        float baseAngle = Mathf.Atan2(preferred.y, preferred.x) * Mathf.Rad2Deg;
        float limit = Mathf.Max(step, maxRelocationDistance);

        for (float dist = step; dist <= limit + 0.001f; dist += step)
        {
            for (int s = 0; s < samples; s++)
            {
                int k = (s + 1) / 2;                       // 0, 1, 1, 2, 2, 3, 3 ...
                float sign = (s % 2 == 0) ? 1f : -1f;      // +, -, +, -, ...
                float a = (baseAngle + sign * k * angleStep) * Mathf.Deg2Rad;

                Vector2 candidate = from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist;

                if (!IsInsideMap(candidate, requiredClearance)) continue;
                if (Overlaps(candidate, requiredClearance)) continue;

                found = candidate;
                return true;
            }
        }
        return false;
    }

    // Keep the relocation on the playable disc. The border collider is a trigger, so
    // nothing physically stops us — this is purely so we don't shove someone off the
    // terrain to escape a wall out on the rim.
    private static bool IsInsideMap(Vector2 p, float radius)
    {
        var map = Map;
        if (map == null) return true;

        float usable = map.mapRadius - radius;
        if (usable <= 0.1f) return true;   // body wider than the map — don't over-constrain

        return p.sqrMagnitude <= usable * usable;
    }

    private static void MoveTo(GameObject go, Vector3 target)
    {
        go.transform.position = target;

        // Keep the physics body in step so the solver doesn't drag the visual back to
        // where the body still thinks it is. Velocity is left alone on purpose: this
        // can fire mid-dash and zeroing it would be a visible hitch.
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = target;

        if (Application.isPlaying) Physics2D.SyncTransforms();
    }

    // Shape-derived radius. Bounds would be simpler but are meaningless on a disabled
    // collider, and a downed player's colliders are disabled.
    private static float ShapeRadius(Collider2D c)
    {
        Vector3 s = c.transform.lossyScale;
        float sx = Mathf.Abs(s.x);
        float sy = Mathf.Abs(s.y);

        var circle = c as CircleCollider2D;
        if (circle != null) return circle.radius * Mathf.Max(sx, sy);

        var capsule = c as CapsuleCollider2D;
        if (capsule != null) return Mathf.Max(capsule.size.x * sx, capsule.size.y * sy) * 0.5f;

        var box = c as BoxCollider2D;
        if (box != null) return Mathf.Max(box.size.x * sx, box.size.y * sy) * 0.5f;

        // Polygon / edge / composite: bounds are the only option, and they need the
        // collider to be live. Returning 0 falls through to fallbackBodyRadius.
        if (c.enabled && c.gameObject.activeInHierarchy)
            return Mathf.Max(c.bounds.extents.x, c.bounds.extents.y);

        return 0f;
    }
}
