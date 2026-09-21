using System.Collections.Generic;
using UnityEngine;

// Destructible-hand support for Boss3's ranged tree-hands.
//
//   Boss3HandHurtbox  — the physical target strung along a live branch. Owns nothing
//                       about the rules; it just routes incoming damage into
//                       Boss3TreeHand.TakeHandDamage.
//   Boss3HandShatter  — the "crushes into pieces" moment: bark shards, embers and a
//                       flash, built procedurally like the rest of Boss3's VFX.
//
// WHY A SEPARATE HITBOX AT ALL: the boss body sits 6–9 units away behind its branch,
// so without this the branch is scenery. Giving the branch its own collider is what
// turns "dodge the telegraph" into "or shoot the thing telegraphing at you".

/// A chain of round hit-spots that follows a live Boss3TreeHand branch.
///
/// Deliberately NOT an EnemyStats / CharacterStats. Registering a temporary branch as a
/// real character would have leaked it into every system that enumerates those —
/// EnemyStatModifierManager registration, wave counters, kill/energy hooks, and Boss3's
/// own IsPlayer() test (which treats "a CharacterStats that isn't an EnemyStats" as a
/// player, so the boss would have started spearing its own hands). It is a plain
/// MonoBehaviour with an explicit damage entry point instead.
[DisallowMultipleComponent]
public class Boss3HandHurtbox : MonoBehaviour
{
    // Every live hurtbox, so area/melee damage can find them in one call without a
    // physics query. Cleared between Play sessions for domain-reload-disabled setups,
    // the same guard Boss3Sprites and PlayerAttack use.
    private static readonly List<Boss3HandHurtbox> _all = new List<Boss3HandHurtbox>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _all.Clear();

    private Boss3TreeHand _hand;
    private CircleCollider2D[] _cols;
    private bool _allowTowerShots;
    private bool _active = true;

    // How many hit-spots are actually IN USE this frame. The rest of the pool is
    // parked (disabled) — see SyncTo, which sizes the chain to the branch's current
    // length so the coverage is continuous instead of a dotted line.
    private int _used;

    // Minimum pool size, regardless of what the inspector asks for.
    //
    // The serialized value (6 on the shipped prefab) was being used as the literal
    // number of spots, which cannot cover a branch that grows to 6–9 units: six
    // circles of radius 0.38 span 4.6u of hitbox across an 8u branch, so ~45% of the
    // visible branch had no collider on it at all and shots aimed straight down it
    // sailed through into the boss's body. The pool is now generous and the USED
    // count is derived from the branch length every frame; spare colliders sit
    // disabled and cost nothing.
    private const int MIN_POOL = 20;

    /// The branch this hurtbox belongs to. Null once it has been destroyed.
    public Boss3TreeHand Hand => _hand;

    /// True when this hurtbox is live and its branch can still be broken.
    public bool Damageable => _active && _hand != null && _hand.CanBeDamaged;

#if UNITY_EDITOR
    // Scene-view only. Draws the actual hit-spots so "is the hitbox where the branch
    // is?" is a look rather than a guess.
    private void OnDrawGizmos()
    {
        if (_cols == null) return;
        Gizmos.color = Damageable ? new Color(0.2f, 1f, 0.4f, 0.85f)
                                  : new Color(1f, 0.35f, 0.2f, 0.4f);
        for (int i = 0; i < _cols.Length; i++)
        {
            var c = _cols[i];
            if (c == null || !c.enabled) continue;
            Gizmos.DrawWireSphere(transform.TransformPoint(c.offset), c.radius);
        }
    }
#endif

    /// Build the hurtbox as a child of the branch it belongs to.
    /// <param name="layer">Physics layer to sit on — pass the boss's own layer so the
    /// project's existing collision matrix (which already lets player shots hit that
    /// layer) applies without any new matrix rows.</param>
    public static Boss3HandHurtbox Create(Boss3TreeHand hand, int layer, int spotCount,
                                          float radius, bool allowTowerShots)
    {
        if (hand == null) return null;

        var go = new GameObject("HandHurtbox");
        go.transform.SetParent(hand.transform, false);
        go.layer = Mathf.Clamp(layer, 0, 31);

        // A kinematic body of our own for two reasons: moving a *static* collider every
        // frame forces Unity to rebuild the static broadphase (expensive, and this moves
        // constantly), and useFullKinematicContacts is what keeps trigger callbacks
        // firing against kinematic projectiles — the same reason Boss3 sets it on its
        // own body.
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeAll;

        var hb = go.AddComponent<Boss3HandHurtbox>();
        hb._hand = hand;
        hb._allowTowerShots = allowTowerShots;
        // spotCount is now a FLOOR, not the exact count — SyncTo picks how many of
        // these to switch on based on how long the branch currently is.
        hb._cols = new CircleCollider2D[Mathf.Clamp(Mathf.Max(spotCount, MIN_POOL), 1, 40)];
        for (int i = 0; i < hb._cols.Length; i++)
        {
            var c = go.AddComponent<CircleCollider2D>();
            // Triggers only: the branch must never physically block the player, an
            // enemy, or a projectile — it only *intercepts* player shots.
            c.isTrigger = true;
            c.radius = radius;
            c.enabled = false;              // switched on by the first SyncTo
            hb._cols[i] = c;
        }
        return hb;
    }

    private void OnEnable()
    {
        if (!_all.Contains(this)) _all.Add(this);
    }

    private void OnDisable() => _all.Remove(this);
    private void OnDestroy() => _all.Remove(this);

    /// Turn the whole hitbox on/off (used when the branch stops being destroyable).
    public void SetActive(bool on)
    {
        if (_active == on) return;
        _active = on;
        if (_cols == null) return;
        for (int i = 0; i < _cols.Length; i++)
        {
            if (_cols[i] == null) continue;
            // Only the spots currently in use come back on — the parked tail of the
            // pool stays off until SyncTo decides it is needed.
            _cols[i].enabled = on && i < _used;
        }
    }

    /// Lay the hit-spots along the branch polyline. `startFraction` leaves the stub
    /// nearest the boss uncovered so shots aimed at the BODY aren't eaten by the base
    /// of the branch growing out of it.
    ///
    /// The number of spots used is derived from the branch's CURRENT length so that
    /// consecutive circles always overlap. A fixed count could not do this: the branch
    /// grows from a stub to 6–9 units during the crawl, so a count tuned for one length
    /// leaves holes at the other. Holes are what made breaking a hand feel random —
    /// shots aimed down the branch passed between the spots and hit the boss instead,
    /// which then tripped the body-damage interrupt rather than breaking the branch.
    public void SyncTo(Vector3[] pts, float startFraction)
    {
        if (!_active || _cols == null || pts == null || pts.Length < 2) return;

        float from = Mathf.Clamp01(startFraction);
        float radius = _cols[0] != null ? _cols[0].radius : 0.38f;

        // Length of the part of the branch we actually cover.
        float covered = PolylineLength(pts) * (1f - from);

        // Step just under a diameter, so neighbouring circles overlap slightly and the
        // chain reads as one continuous capsule with no gaps to shoot through.
        float step = Mathf.Max(0.05f, radius * 1.5f);
        int want = Mathf.Clamp(Mathf.CeilToInt(covered / step) + 1, 2, _cols.Length);

        for (int i = 0; i < want; i++)
        {
            var c = _cols[i];
            if (c == null) continue;

            float f = want > 1 ? Mathf.Lerp(from, 1f, i / (float)(want - 1)) : Mathf.Lerp(from, 1f, 0.5f);
            Vector3 world = SampleBranch(pts, f);

            // Local space, so the hurtbox is correct no matter where the branch's root
            // GameObject happens to sit.
            c.offset = transform.InverseTransformPoint(world);
            c.enabled = true;
        }

        // Park the rest. A short branch uses a handful of spots; leaving the unused
        // ones enabled would stack them all on the tip and make it a fat blob.
        for (int i = want; i < _cols.Length; i++)
            if (_cols[i] != null) _cols[i].enabled = false;

        _used = want;
    }

    private static float PolylineLength(Vector3[] pts)
    {
        float len = 0f;
        for (int i = 1; i < pts.Length; i++) len += (pts[i] - pts[i - 1]).magnitude;
        return len;
    }

    // Point at fraction f along the polyline (piecewise linear over the vertices — the
    // vertices are evenly spaced along the branch by construction).
    private static Vector3 SampleBranch(Vector3[] pts, float f)
    {
        float x = Mathf.Clamp01(f) * (pts.Length - 1);
        int i0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, pts.Length - 1);
        int i1 = Mathf.Min(i0 + 1, pts.Length - 1);
        return Vector3.Lerp(pts[i0], pts[i1], x - i0);
    }

    // ── damage intake ────────────────────────────────────────────────────────
    //
    // Mirrors Boss3.OnTriggerEnter2D exactly, minus the CombatStats reporting: damage
    // spent on a branch is not damage dealt to the boss, and quietly folding it into the
    // player's boss-damage figures would have made those numbers mean something new.
    //
    // ONE SHOT = ONE HIT ON ONE BRANCH. Two things used to break that, and both made
    // the number of shots needed to snap a branch feel random:
    //
    //   1. The hit-spots along a branch deliberately OVERLAP (that is what closed the
    //      gaps), so a projectile entering at a joint raises OnTriggerEnter2D on two
    //      colliders in the same physics step — two hits from one shot.
    //   2. Destroy() is deferred to the end of the frame, so between the trigger firing
    //      and the projectile actually going away it can also enter a DIFFERENT branch's
    //      hurtbox. A volley's branches sit ~1.6u apart and cross near the boss, so one
    //      shot could chip two of them and fully break neither.
    //
    // The claim set below is static and stamped with the frame number: the first
    // hurtbox to see a projectile owns it, everything else ignores it for the rest of
    // that frame, and the set self-clears when the frame advances.
    private static readonly HashSet<int> _claimed = new HashSet<int>();
    private static int _claimFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetClaims() { _claimed.Clear(); _claimFrame = -1; }

    private static bool Claim(GameObject projectile)
    {
        if (projectile == null) return false;

        if (_claimFrame != Time.frameCount)
        {
            _claimFrame = Time.frameCount;
            _claimed.Clear();
        }
        return _claimed.Add(projectile.GetInstanceID());
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_active || _hand == null || !_hand.CanBeDamaged || other == null) return;

        var wp = other.GetComponent<WeaponProjectile>();
        if (wp != null)
        {
            if (!Claim(other.gameObject)) return;
            if (_hand.TakeHandDamage(wp.GetDamage(), other.transform.position))
                Destroy(other.gameObject);
            return;
        }

        // Tower fire is OFF by default. Letting turret shots break hands would silently
        // re-route DPS that currently reaches the boss body, changing tower balance in
        // a fight nobody asked to have rebalanced — so it is opt-in from Boss3.
        if (!_allowTowerShots) return;

        var proj = other.GetComponent<Projectile>();
        if (proj != null)
        {
            if (!Claim(other.gameObject)) return;
            if (_hand.TakeHandDamage(proj.damage, other.transform.position))
                Destroy(other.gameObject);
        }
    }

    // ── integration entry point for melee / AoE ──────────────────────────────

    /// Damage every destroyable Boss3 hand overlapping a circle, and return how many
    /// were hit. This is the hook for damage sources that resolve their own targets
    /// (melee swings, explosions, flamethrower ticks) rather than colliding with
    /// something. Each branch is damaged at most once per call.
    ///
    /// Returns 0 — and costs a single early-out — when no Boss3 hand is out, so it is
    /// safe to call unconditionally from a hot path.
    public static int DamageHandsInRadius(Vector2 centre, float radius, float damage)
    {
        if (_all.Count == 0 || damage <= 0f) return 0;

        int hits = 0;
        // Reverse walk: TakeHandDamage can Shatter, which destroys the branch and
        // removes its hurtbox from this list mid-iteration.
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            var hb = _all[i];
            if (hb == null || !hb._active) continue;
            if (hb._hand == null || !hb._hand.CanBeDamaged) continue;
            if (!hb.OverlapsCircle(centre, radius, out Vector3 contact)) continue;
            if (hb._hand.TakeHandDamage(damage, contact)) hits++;
        }
        return hits;
    }

    /// True if any Boss3 hand that can currently be broken overlaps the circle.
    /// Useful for cursor highlighting / aim assist without dealing damage.
    public static bool AnyHandInRadius(Vector2 centre, float radius)
    {
        for (int i = 0; i < _all.Count; i++)
        {
            var hb = _all[i];
            if (hb == null || !hb._active) continue;
            if (hb._hand == null || !hb._hand.CanBeDamaged) continue;
            if (hb.OverlapsCircle(centre, radius, out _)) return true;
        }
        return false;
    }

    private bool OverlapsCircle(Vector2 centre, float radius, out Vector3 contact)
    {
        contact = centre;
        if (_cols == null) return false;

        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < _cols.Length; i++)
        {
            var c = _cols[i];
            if (c == null || !c.enabled) continue;
            Vector2 p = c.ClosestPoint(centre);
            float d = (p - centre).sqrMagnitude;
            if (d < best) { best = d; contact = p; }
            if (d <= radius * radius) found = true;
        }
        return found;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SHATTER — the branch bursting into pieces.
//
// Bark shards are short, unlit LineRenderers (so they read as broken *branch*, not as
// generic square debris) thrown outward from the branch axis with spin and gravity,
// plus a handful of embers and a single bright flash at the break. Fully procedural,
// self-destructing, and uses the same shared "Sprites/Default" material every other
// Boss3 line uses via Boss3FXUtil.
public class Boss3HandShatter : MonoBehaviour
{
    private class Shard
    {
        public Transform t;
        public LineRenderer lr;
        public Vector3 vel;
        public float spin;
        public float life;
        public float age;
        public Color colA, colB;
    }

    private readonly List<Shard> _shards = new List<Shard>();
    private SpriteRenderer _flash;
    private SpriteRenderer _ring;
    private readonly List<SpriteRenderer> _embers = new List<SpriteRenderer>();
    private readonly List<Vector3> _emberVel = new List<Vector3>();
    private readonly List<SpriteRenderer> _dust = new List<SpriteRenderer>();
    private readonly List<Vector3> _dustVel = new List<Vector3>();
    private readonly List<float> _dustSpin = new List<float>();
    private float _age;
    private float _life = 1.15f;

    private const float GRAVITY = -7.5f;

    /// Blow a whole branch apart. `pts` is the branch polyline as it was last drawn.
    public static Boss3HandShatter Spawn(Vector3[] pts, Color bark, Color heat,
                                         string layer, int order, int shardCount,
                                         float branchWidth)
    {
        return Build(pts, bark, heat, layer, order, shardCount, branchWidth,
                     emberCount: Mathf.Clamp(shardCount / 2, 2, 8), withFlash: true,
                     life: 1.5f);
    }

    /// A small puff of chips when the branch is HIT but not yet broken.
    ///
    /// Deliberately much cheaper than a full Spawn — two shards, no embers, no flash.
    /// This fires on every landed shot, so a fast weapon would otherwise be allocating
    /// a dozen GameObjects several times a second.
    public static Boss3HandShatter Chip(Vector3 at, Color bark, Color heat,
                                        string layer, int order)
    {
        var pts = new[] { at + Vector3.left * 0.1f, at + Vector3.right * 0.1f };
        return Build(pts, bark, heat, layer, order, 2, 0.11f,
                     emberCount: 0, withFlash: false, life: 0.42f);
    }

    private static Boss3HandShatter Build(Vector3[] pts, Color bark, Color heat,
                                          string layer, int order, int shardCount,
                                          float branchWidth, int emberCount,
                                          bool withFlash, float life)
    {
        if (pts == null || pts.Length < 2) return null;

        var go = new GameObject("Boss3_HandShatter");
        // Sit the object at the branch mid-point; shards are positioned in world space
        // on spawn and then moved by their own transforms.
        go.transform.position = pts[pts.Length / 2];

        var fx = go.AddComponent<Boss3HandShatter>();
        fx._life = life;
        fx.Assemble(pts, bark, heat, layer, order, shardCount, branchWidth,
                    emberCount, withFlash, life);
        return fx;
    }

    private void Assemble(Vector3[] pts, Color bark, Color heat, string layer, int order,
                          int shardCount, float branchWidth, int emberCount,
                          bool withFlash, float life)
    {
        float width = Mathf.Max(0.05f, branchWidth);

        for (int i = 0; i < shardCount; i++)
        {
            // Spawn each shard on the branch, biased toward the outer half so the
            // burst reads as the *reaching* part of the branch coming apart.
            float f = Mathf.Clamp01(Random.Range(0.15f, 1f));
            float x = f * (pts.Length - 1);
            int i0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, pts.Length - 1);
            int i1 = Mathf.Min(i0 + 1, pts.Length - 1);
            Vector3 pos = Vector3.Lerp(pts[i0], pts[i1], x - i0);

            Vector3 axis = pts[i1] - pts[i0];
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
            axis.Normalize();
            Vector3 perp = new Vector3(-axis.y, axis.x, 0f);

            var sgo = new GameObject("Shard");
            sgo.transform.SetParent(transform, true);
            sgo.transform.position = pos;
            sgo.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg);

            float len = Random.Range(0.14f, 0.42f);
            var lr = Boss3FXUtil.MakeLine(sgo.transform, "S", 2, width * Random.Range(0.45f, 1.0f),
                                          bark, order, layer);
            // Local space so the shard's own transform carries it — a world-space line
            // would need rewriting every frame just to follow its parent.
            lr.useWorldSpace = false;
            lr.SetPosition(0, new Vector3(-len * 0.5f, 0f, 0f));
            lr.SetPosition(1, new Vector3(len * 0.5f, 0f, 0f));

            // Thrown mostly sideways off the branch, with a little outward push along it.
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 v = perp * (side * Random.Range(1.4f, 4.2f))
                      + axis * Random.Range(-0.8f, 2.6f)
                      + Vector3.up * Random.Range(0.4f, 2.2f);

            // The outermost fragments keep the strike's heat; the inner ones are bare bark.
            Color hot = Color.Lerp(bark, heat, Mathf.Clamp01((f - 0.4f) / 0.6f) * 0.85f);

            _shards.Add(new Shard
            {
                t = sgo.transform,
                lr = lr,
                vel = v,
                spin = Random.Range(-540f, 540f),
                // Never outlive the effect itself, or a shard would still be at full
                // alpha when the whole object is destroyed and would visibly pop out.
                life = Mathf.Min(life, Random.Range(0.55f, 1.05f)),
                colA = bark,
                colB = hot
            });
        }

        // Embers riding the break.
        for (int i = 0; i < emberCount; i++)
        {
            Vector3 pos = pts[Random.Range(pts.Length / 3, pts.Length)];
            var sr = Boss3FXUtil.MakeSprite(transform, "Ember", Boss3Sprites.SoftDot,
                                            heat, order + 1, layer);
            sr.transform.position = pos;
            float s = Random.Range(0.16f, 0.34f);
            sr.transform.localScale = new Vector3(s, s, 1f);
            _embers.Add(sr);
            _emberVel.Add(new Vector3(Random.Range(-1.8f, 1.8f), Random.Range(0.6f, 3.0f), 0f));
        }

        // One bright pop at the break so the destruction registers instantly.
        if (withFlash)
        {
            _flash = Boss3FXUtil.MakeSprite(transform, "BreakFlash", Boss3Sprites.Spark,
                                            new Color(1f, 0.92f, 0.75f, 1f), order + 2, layer);
            _flash.transform.position = pts[pts.Length / 2];
            _flash.transform.localScale = Vector3.one * 0.5f;

            // Expanding shock ring along the branch's midpoint — reads as the whole
            // limb letting go at once rather than a local puff.
            _ring = Boss3FXUtil.MakeSprite(transform, "BreakRing", Boss3Sprites.SoftDot,
                                           heat, order, layer);
            _ring.transform.position = pts[pts.Length / 2];
            _ring.transform.localScale = Vector3.one * 0.4f;

            // Dust: soft, slow, drifting motes along the whole broken length. These are
            // what sell "crushed to pieces" rather than "snapped in half" — the shards
            // give it structure, the dust gives it volume.
            int dustCount = Mathf.Clamp(shardCount, 6, 18);
            for (int i = 0; i < dustCount; i++)
            {
                float f = i / (float)Mathf.Max(1, dustCount - 1);
                float x = Mathf.Lerp(0.1f, 1f, f) * (pts.Length - 1);
                int di = Mathf.Clamp(Mathf.RoundToInt(x), 0, pts.Length - 1);

                Color dc = Color.Lerp(bark, new Color(0.42f, 0.36f, 0.40f, 1f), 0.7f);
                dc.a = Random.Range(0.28f, 0.55f);
                var d = Boss3FXUtil.MakeSprite(transform, "Dust", Boss3Sprites.SoftDot,
                                               dc, order - 1, layer);
                d.transform.position = pts[di] + new Vector3(Random.Range(-0.12f, 0.12f),
                                                             Random.Range(-0.12f, 0.12f), 0f);
                float s = Random.Range(0.35f, 0.85f);
                d.transform.localScale = new Vector3(s, s, 1f);
                _dust.Add(d);
                _dustVel.Add(new Vector3(Random.Range(-0.9f, 0.9f), Random.Range(0.15f, 0.8f), 0f));
                _dustSpin.Add(Random.Range(0.6f, 1.5f));   // per-second growth rate
            }
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        _age += dt;

        for (int i = 0; i < _shards.Count; i++)
        {
            var s = _shards[i];
            if (s.t == null) continue;

            s.age += dt;
            float k = Mathf.Clamp01(s.age / s.life);

            s.vel += Vector3.up * (GRAVITY * dt);
            s.vel *= 1f - 1.6f * dt;                       // air drag, so they settle
            s.t.position += s.vel * dt;
            s.t.Rotate(0f, 0f, s.spin * dt);

            if (s.lr != null)
            {
                float a = 1f - k * k;                      // hold, then fade off quickly
                Color ca = s.colA; ca.a = a;
                Color cb = s.colB; cb.a = a;
                s.lr.startColor = ca;
                s.lr.endColor = cb;
            }
        }

        for (int i = 0; i < _embers.Count; i++)
        {
            var e = _embers[i];
            if (e == null) continue;
            Vector3 v = _emberVel[i];
            v += Vector3.up * (GRAVITY * 0.25f * dt);
            _emberVel[i] = v;
            e.transform.position += v * dt;
            var c = e.color;
            c.a = Mathf.Max(0f, c.a - dt * 1.6f);
            e.color = c;
        }

        for (int i = 0; i < _dust.Count; i++)
        {
            var d = _dust[i];
            if (d == null) continue;
            d.transform.position += _dustVel[i] * dt;
            _dustVel[i] *= 1f - 1.1f * dt;                 // dust stalls quickly
            float g = 1f + _dustSpin[i] * dt;              // and keeps billowing outward
            d.transform.localScale *= g;
            var c = d.color;
            c.a = Mathf.Max(0f, c.a - dt * 0.75f);
            d.color = c;
        }

        if (_ring != null)
        {
            float k = Mathf.Clamp01(_age / 0.42f);
            float s = Mathf.Lerp(0.4f, 3.6f, 1f - (1f - k) * (1f - k));
            _ring.transform.localScale = new Vector3(s, s * 0.7f, 1f);
            var c = _ring.color; c.a = (1f - k) * 0.55f; _ring.color = c;
        }

        if (_flash != null)
        {
            float k = Mathf.Clamp01(_age / 0.22f);
            float s = Mathf.Lerp(0.5f, 2.1f, 1f - (1f - k) * (1f - k));
            _flash.transform.localScale = new Vector3(s, s, 1f);
            var c = _flash.color; c.a = 1f - k; _flash.color = c;
        }

        if (_age >= _life) Destroy(gameObject);
    }
}



