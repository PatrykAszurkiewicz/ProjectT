using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Boss3's third attack — DARK ORBS.
//   Boss3OrbCharge — the well that gathers between the boss's hands during the
//                    wind-up, so the emission is telegraphed and readable.
//   Boss3DarkOrb   — one slow, fluid chaser. It is thrown outward, then curves in
//                    on a player and pops for damage on contact (or when its fuse
//                    burns out).
public class Boss3DarkOrb : MonoBehaviour
{
    [System.Serializable]
    public class Settings
    {
        [Header("Motion")]
        [Tooltip("Cruise speed once it is hunting, world units/sec. MUST stay below the " +
                 "player's moveSpeed (5 on the stock prefab) or the orb becomes unavoidable.")]
        public float chaseSpeed = 2.8f;
        [Tooltip("Speed it is thrown out of the boss with, before homing takes over.")]
        public float launchSpeed = 7f;
        [Tooltip("How fast the launch burst bleeds off while it is still 'dumb'.")]
        public float launchDrag = 10f;
        [Tooltip("Seconds after birth before it starts steering at all.")]
        public float homingDelay = 0.3f;
        [Tooltip("Seconds for steering authority to ramp from none to full — this is what " +
                 "makes the turn-in read as a drift rather than a snap.")]
        public float homingRamp = 0.7f;
        [Tooltip("Steering stiffness. Higher = it corners tighter and is harder to juke.")]
        public float turnResponse = 2.4f;
        [Tooltip("Amplitude of the serpentine weave laid over the chase, degrees.")]
        public float weaveDegrees = 24f;
        [Tooltip("Weave cycles per second.")]
        public float weaveFrequency = 0.55f;
        [Tooltip("How far off to one side an orb aims while it is still far away, world " +
                 "units. This is what makes several orbs arc in from different angles " +
                 "instead of stacking into one blob.")]
        public float flankSpread = 2.6f;
        [Tooltip("Distance over which that sideways aim decays back onto the target.")]
        public float flankFalloff = 8f;

        [Header("Fuse")]
        [Tooltip("Seconds before it burns out and pops wherever it happens to be.")]
        public float lifetime = 13f;
        [Tooltip("Seconds of visible 'about to blow' flicker before that.")]
        public float fuseWarning = 1.6f;
        [Tooltip("How close a player has to be for the orb to detonate on them.")]
        public float contactRadius = 0.75f;

        [Header("Look")]
        public float radius = 0.42f;
        [Tooltip("Nodes in the comet tail. Each chases the one ahead of it.")]
        public int trailNodes = 14;
        [Tooltip("How tightly the tail follows. Lower = a longer, looser smear.")]
        public float trailFollow = 14f;
        public Color voidColor = new Color(0.02f, 0.015f, 0.04f, 1f);
        public Color rimColor = new Color(0.45f, 0.10f, 0.75f, 1f);
        public Color coreColor = new Color(0.85f, 0.35f, 1f, 1f);
        [Tooltip("Above the Y-sort band and the grass, just under the tree-hands.")]
        public int sortingOrder = 2120;

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    private const float BIRTH_DURATION = 0.22f;
    private const int EMBER_COUNT = 3;

    // ── config / wiring ──
    private Settings _s;
    private UnityEngine.Object _owner;          // the boss; orbs die with it
    private System.Func<bool> _abort;           // "the owner is dying — stop"
    private System.Action<Vector2> _onPop;      // damage routing, owned by Boss3

    // ── motion state ──
    private PlayerRef _targetRef;
    private Vector3 _lastKnown;                 // where the target was last seen
    private Vector2 _vel;
    private float _age;
    private float _phase;
    private float _weaveSign;
    private float _flankSign;
    private float _homingDelay;
    private bool _popping;
    private bool _dissipating;

    // ── visual state ──
    private SpriteRenderer _void, _rim, _core;
    private SpriteRenderer[] _embers;
    private float[] _emberPhase;
    private LineRenderer _trail;
    private Vector3[] _trailPts;

    // Every live orb, so the boss can clear its own on death and nothing is left
    // hunting a player after the fight is over.
    private static readonly List<Boss3DarkOrb> _live = new List<Boss3DarkOrb>();

    // "Enter Play Mode without domain reload" keeps statics alive between sessions;
    // stale entries here would be destroyed objects. Same guard Boss3Sprites uses.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _live.Clear();

    /// <summary>
    /// Throw one orb. <paramref name="index"/>/<paramref name="total"/> only shape the
    /// personality of this orb within its volley (which way it weaves, which side it
    /// flanks from, how late it starts hunting) so a volley never flies as one lump.
    /// </summary>
    public static Boss3DarkOrb Spawn(Vector3 origin, Vector2 launchDir, int index, int total,
                                     Settings settings, string sortingLayer,
                                     UnityEngine.Object owner,
                                     System.Func<bool> abort,
                                     System.Action<Vector2> onPop)
    {
        var go = new GameObject("Boss3_DarkOrb");
        go.transform.position = new Vector3(origin.x, origin.y, 0f);
        var orb = go.AddComponent<Boss3DarkOrb>();
        orb.Init(launchDir, index, total, settings, sortingLayer, owner, abort, onPop);
        return orb;
    }

    /// <summary>
    /// Fade out every orb thrown by <paramref name="owner"/>, without dealing damage.
    /// Called when the boss dies so nothing keeps hunting after the fight — and
    /// filtered by owner so one boss dying can never clear another one's orbs.
    /// </summary>
    public static void DissipateAllFrom(UnityEngine.Object owner)
    {
        // Backwards: Dissipate() removes the orb from _live as it goes.
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var o = _live[i];
            if (o == null) { _live.RemoveAt(i); continue; }
            if (owner != null && o._owner != owner) continue;
            o.Dissipate();
        }
    }

    private void Init(Vector2 launchDir, int index, int total, Settings settings, string layer,
                      UnityEngine.Object owner, System.Func<bool> abort, System.Action<Vector2> onPop)
    {
        _s = settings != null ? settings : new Settings();
        _owner = owner;
        _abort = abort;
        _onPop = onPop;

        if (launchDir.sqrMagnitude < 0.0001f) launchDir = Vector2.right;
        _vel = launchDir.normalized * Mathf.Max(0f, _s.launchSpeed);

        _phase = Random.Range(0f, 10f);
        _weaveSign = (index % 2 == 0) ? 1f : -1f;
        _flankSign = (index % 2 == 0) ? 1f : -1f;
        // Stagger the turn-in so a volley arrives strung out rather than all at once.
        _homingDelay = Mathf.Max(0f, _s.homingDelay) + index * 0.12f;

        _targetRef = PickTarget();
        _lastKnown = _targetRef != null
            ? _targetRef.transform.position
            : transform.position + (Vector3)(launchDir.normalized * 6f);

        BuildVisual(layer);
        _live.Add(this);
    }

    private void OnDestroy() => _live.Remove(this);

    // ── target selection ─────────────────────────────────────────────────────

    // A player is worth hunting only while alive and uncloaked. Cloak therefore
    // makes orbs lose you, which matches how the rest of Boss3 treats it, and a
    // downed co-op player reads as dead here so orbs don't hound them on the floor.
    private static bool Huntable(PlayerRef p) =>
        p != null && p.Stats != null && !p.Stats.IsDead() && !p.IsCloaked;

    /// Uniformly random living player. Reservoir sampling so co-op picks fairly
    /// without allocating a candidate list every time. Single player always returns
    /// the one registered player, so behaviour there is fully deterministic.
    private static PlayerRef PickTarget()
    {
        var reg = PlayerRegistry.Instance;
        if (reg == null) return null;
        var all = reg.All;
        if (all == null) return null;

        PlayerRef pick = null;
        int seen = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (!Huntable(p)) continue;
            seen++;
            if (Random.Range(0, seen) == 0) pick = p;
        }
        return pick;
    }

    private static PlayerRef PlayerWithin(Vector2 pos, float radius)
    {
        var reg = PlayerRegistry.Instance;
        if (reg == null) return null;
        var all = reg.All;
        if (all == null) return null;

        float r2 = radius * radius;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (!Huntable(p)) continue;
            if (((Vector2)p.transform.position - pos).sqrMagnitude <= r2) return p;
        }
        return null;
    }

    private void RefreshTarget()
    {
        if (Huntable(_targetRef)) { _lastKnown = _targetRef.transform.position; return; }

        // Target died, cloaked or left: re-roll. If nobody qualifies we keep coasting
        // toward the last place we saw them and burn out there.
        _targetRef = PickTarget();
        if (_targetRef != null) _lastKnown = _targetRef.transform.position;
    }

    // ── motion ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_popping || _dissipating) return;

        // The boss is gone or dying — fade out rather than keep hunting.
        if (_owner == null || (_abort != null && _abort())) { Dissipate(); return; }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;              // paused (timeScale 0) — freeze, like everything else
        _age += dt;

        RefreshTarget();

        Vector2 pos = transform.position;
        Vector2 to = (Vector2)_lastKnown - pos;
        float dist = to.magnitude;
        Vector2 dir = dist > 0.0001f ? to / dist
                    : (_vel.sqrMagnitude > 0.0001f ? _vel.normalized : Vector2.right);

        // Steering authority ramps in from nothing, so the birth reads as a throw and
        // the turn-in as a drift. This is most of why the motion feels fluid.
        float homing = Mathf.Clamp01((_age - _homingDelay) / Mathf.Max(0.05f, _s.homingRamp));

        if (homing <= 0f)
        {
            _vel = Vector2.MoveTowards(_vel, Vector2.zero, Mathf.Max(0f, _s.launchDrag) * dt);
        }
        else
        {
            // Aim off to one side while far out, decaying onto the target as it closes:
            // an arcing approach instead of a straight line, and different per orb.
            Vector2 perp = new Vector2(-dir.y, dir.x) * _flankSign;
            float flank = _s.flankSpread * Mathf.Clamp01(dist / Mathf.Max(0.5f, _s.flankFalloff));
            Vector2 want = ((Vector2)_lastKnown + perp * flank) - pos;
            Vector2 wantDir = want.sqrMagnitude > 0.0001f ? want.normalized : dir;

            // Serpentine weave on top.
            float weave = Mathf.Sin((_age + _phase) * _s.weaveFrequency * Mathf.PI * 2f)
                          * _s.weaveDegrees * Mathf.Deg2Rad * _weaveSign;
            wantDir = Rotate(wantDir, weave);

            // Frame-rate independent approach to the desired velocity.
            float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, _s.turnResponse) * homing * dt);
            _vel = Vector2.Lerp(_vel, wantDir * _s.chaseSpeed, k);
        }

        // Hard ceiling. Nothing above may raise the speed past the launch burst, and
        // the chase itself can never exceed chaseSpeed.
        float max = Mathf.Max(_s.chaseSpeed, _s.launchSpeed);
        if (_vel.magnitude > max) _vel = _vel.normalized * max;

        pos += _vel * dt;
        transform.position = new Vector3(pos.x, pos.y, 0f);

        UpdateVisual(dt);

        // Detonate on ANY player in reach, not only the one being chased — a teammate
        // who walks into someone else's orb still eats it.
        if (_age > BIRTH_DURATION && PlayerWithin(pos, Mathf.Max(0.05f, _s.contactRadius)) != null)
        {
            Pop();
            return;
        }

        if (_age >= Mathf.Max(0.5f, _s.lifetime)) Pop();
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // ── detonation / teardown ────────────────────────────────────────────────

    /// Burst and deal damage through the owner's callback.
    public void Pop()
    {
        if (_popping || _dissipating) return;
        _popping = true;
        _live.Remove(this);

        // Null the callback before invoking so a re-entrant path can never double-hit.
        var cb = _onPop;
        _onPop = null;
        if (cb != null) cb(transform.position);

        StartCoroutine(PopRoutine());
    }

    /// Fade out harmlessly (boss died, fight over). Never deals damage.
    public void Dissipate()
    {
        if (_popping || _dissipating) return;
        _dissipating = true;
        _onPop = null;
        _live.Remove(this);
        StartCoroutine(FadeRoutine(0.3f));
    }

    private IEnumerator PopRoutine()
    {
        const float DUR = 0.3f;
        float t = 0f;
        float baseS = _s.radius * 2f;

        while (t < DUR)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / DUR);
            float ease = 1f - (1f - k) * (1f - k);

            // Rim blows outward as the shockwave; the void core collapses into it.
            SetScale(_rim, baseS * Mathf.Lerp(2.1f, 7.5f, ease));
            SetAlpha(_rim, (1f - k) * 0.85f);

            SetScale(_void, baseS * Mathf.Lerp(1f, 0.1f, ease));
            SetAlpha(_void, 1f - k);

            SetScale(_core, baseS * Mathf.Lerp(0.5f, 3.2f, ease));
            SetAlpha(_core, (1f - k) * (1f - k));

            for (int i = 0; i < _embers.Length; i++)
            {
                if (_embers[i] == null) continue;
                _embers[i].transform.localPosition *= 1f + 4f * Time.deltaTime;
                SetAlpha(_embers[i], 1f - k);
            }

            SetLineAlpha(_trail, (1f - k) * 0.5f);
            yield return null;
        }
        Destroy(gameObject);
    }

    private IEnumerator FadeRoutine(float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            SetAlpha(_void, 1f - k);
            SetAlpha(_rim, (1f - k) * 0.7f);
            SetAlpha(_core, 1f - k);
            for (int i = 0; i < _embers.Length; i++) SetAlpha(_embers[i], (1f - k) * 0.6f);
            SetLineAlpha(_trail, (1f - k) * 0.4f);
            yield return null;
        }
        Destroy(gameObject);
    }

    // ── visuals ──────────────────────────────────────────────────────────────

    private void BuildVisual(string layer)
    {
        int order = _s.sortingOrder;

        // Comet tail. World-space positions, so it smears behind the orb instead of
        // rigidly following its transform.
        int n = Mathf.Max(2, _s.trailNodes);
        _trailPts = new Vector3[n];
        for (int i = 0; i < n; i++) _trailPts[i] = transform.position;

        Color tail = _s.rimColor; tail.a = 0.5f;
        _trail = Boss3FXUtil.MakeLine(transform, "Trail", n, _s.radius, tail, order - 1, layer);
        _trail.startWidth = _s.radius * 1.05f;
        _trail.endWidth = 0.02f;
        Color tailEnd = _s.rimColor; tailEnd.a = 0f;
        _trail.startColor = tail;
        _trail.endColor = tailEnd;

        // Halo BEHIND the dark body, so the body reads as a hole punched in the light.
        _rim = Boss3FXUtil.MakeSprite(transform, "Rim", Boss3Sprites.SoftDot, _s.rimColor, order, layer);
        _void = Boss3FXUtil.MakeSprite(transform, "Void", Boss3Sprites.SoftDot, _s.voidColor, order + 1, layer);
        _core = Boss3FXUtil.MakeSprite(transform, "Core", Boss3Sprites.Spark, _s.coreColor, order + 2, layer);

        _embers = new SpriteRenderer[EMBER_COUNT];
        _emberPhase = new float[EMBER_COUNT];
        for (int i = 0; i < EMBER_COUNT; i++)
        {
            Color c = Color.Lerp(_s.rimColor, _s.coreColor, 0.5f); c.a = 0.7f;
            _embers[i] = Boss3FXUtil.MakeSprite(transform, "Ember" + i, Boss3Sprites.Spark, c, order + 1, layer);
            _emberPhase[i] = Random.Range(0f, Mathf.PI * 2f);
        }

        UpdateVisual(0f);
    }

    private void UpdateVisual(float dt)
    {
        Vector3 pos = transform.position;

        // Tail: every node chases the one ahead of it. Frame-rate independent, and it
        // bunches up when the orb slows — which is exactly how a smoke tail behaves.
        if (_trail != null && _trailPts != null)
        {
            _trailPts[0] = pos;
            if (dt > 0f)
            {
                float f = 1f - Mathf.Exp(-Mathf.Max(0.01f, _s.trailFollow) * dt);
                for (int i = 1; i < _trailPts.Length; i++)
                    _trailPts[i] = Vector3.Lerp(_trailPts[i], _trailPts[i - 1], f);
            }
            _trail.SetPositions(_trailPts);
        }

        float birth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_age / BIRTH_DURATION));
        float t = Time.time + _phase;

        // Fuse: a fast flicker over the last fuseWarning seconds so the burst is read
        // as coming rather than arbitrary.
        float fuseStart = Mathf.Max(0.5f, _s.lifetime) - Mathf.Max(0f, _s.fuseWarning);
        float fuse = _s.fuseWarning > 0.01f
            ? Mathf.Clamp01((_age - fuseStart) / _s.fuseWarning) : 0f;
        float flicker = fuse > 0f ? (0.65f + 0.35f * Mathf.Sin(t * Mathf.Lerp(8f, 34f, fuse))) : 1f;

        float pulse = 1f + 0.1f * Mathf.Sin(t * 5.5f);
        float baseS = _s.radius * 2f * pulse * birth * (1f + 0.25f * fuse);

        // Stretch along travel: a blob that leans into its own motion.
        float speed = _vel.magnitude;
        float stretch = Mathf.Clamp01(speed / Mathf.Max(0.1f, _s.chaseSpeed)) * 0.26f;
        float angle = speed > 0.01f ? Mathf.Atan2(_vel.y, _vel.x) * Mathf.Rad2Deg : 0f;

        if (_void != null)
        {
            _void.transform.localScale = new Vector3(baseS * (1f + stretch), baseS * (1f - stretch * 0.55f), 1f);
            _void.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            SetAlpha(_void, birth);
        }
        if (_rim != null)
        {
            float rs = baseS * (2.1f + 0.12f * Mathf.Sin(t * 3.1f));
            _rim.transform.localScale = new Vector3(rs, rs, 1f);
            SetAlpha(_rim, birth * (0.5f + 0.35f * fuse) * flicker);
        }
        if (_core != null)
        {
            float cs = baseS * (0.42f + 0.06f * Mathf.Sin(t * 7.3f)) * (1f + 0.5f * fuse);
            _core.transform.localScale = new Vector3(cs, cs, 1f);
            SetAlpha(_core, birth * (0.75f + 0.25f * fuse) * flicker);
        }

        for (int i = 0; i < _embers.Length; i++)
        {
            var e = _embers[i];
            if (e == null) continue;
            float spin = (i % 2 == 0 ? 1f : -1f) * (1.6f + 0.5f * i);
            float a = t * spin + _emberPhase[i];
            float r = _s.radius * (1.15f + 0.18f * Mathf.Sin(t * 2.3f + i)) * birth;
            e.transform.localPosition = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.75f, 0f);
            float es = _s.radius * 0.5f * birth;
            e.transform.localScale = new Vector3(es, es, 1f);
            SetAlpha(e, birth * 0.7f * flicker);
        }
    }

    private static void SetScale(SpriteRenderer sr, float s)
    {
        if (sr != null) sr.transform.localScale = new Vector3(s, s, 1f);
    }

    private static void SetAlpha(SpriteRenderer sr, float a)
    {
        if (sr == null) return;
        var c = sr.color; c.a = Mathf.Clamp01(a); sr.color = c;
    }

    private static void SetLineAlpha(LineRenderer lr, float a)
    {
        if (lr == null) return;
        Color s = lr.startColor; s.a = Mathf.Clamp01(a);
        Color e = lr.endColor; e.a = 0f;
        lr.startColor = s; lr.endColor = e;
    }
}


// ORB CHARGE — the well that gathers between the boss's hands before the orbs are
// thrown. Purely a telegraph: it deals nothing, blocks nothing, and follows a live
// position provider so it stays glued to the boss's chest while he floats.
public class Boss3OrbCharge : MonoBehaviour
{
    private const int MOTES = 9;

    private System.Func<Vector3> _follow;
    private Boss3DarkOrb.Settings _s;
    private float _dur, _age;
    private bool _ending;

    private SpriteRenderer _well, _rim;
    private SpriteRenderer[] _motes;
    private float[] _motePhase;

    public static Boss3OrbCharge Spawn(System.Func<Vector3> follow, float duration,
                                       Boss3DarkOrb.Settings settings, string layer)
    {
        var go = new GameObject("Boss3_OrbCharge");
        go.transform.position = follow != null ? follow() : Vector3.zero;
        var c = go.AddComponent<Boss3OrbCharge>();
        c.Init(follow, duration, settings, layer);
        return c;
    }

    private void Init(System.Func<Vector3> follow, float duration,
                      Boss3DarkOrb.Settings settings, string layer)
    {
        _follow = follow;
        _s = settings != null ? settings : new Boss3DarkOrb.Settings();
        _dur = Mathf.Max(0.1f, duration);

        int order = _s.sortingOrder + 3;   // in front of the orbs themselves
        _rim = Boss3FXUtil.MakeSprite(transform, "Rim", Boss3Sprites.SoftDot, _s.rimColor, order, layer);
        _well = Boss3FXUtil.MakeSprite(transform, "Well", Boss3Sprites.SoftDot, _s.voidColor, order + 1, layer);

        _motes = new SpriteRenderer[MOTES];
        _motePhase = new float[MOTES];
        for (int i = 0; i < MOTES; i++)
        {
            Color c = Color.Lerp(_s.rimColor, _s.coreColor, Random.value); c.a = 0.8f;
            _motes[i] = Boss3FXUtil.MakeSprite(transform, "Mote" + i, Boss3Sprites.Spark, c, order + 2, layer);
            _motePhase[i] = Random.Range(0f, 1f);
        }
    }

    private void Update()
    {
        if (_ending) return;
        if (_follow != null) transform.position = _follow();

        _age += Time.deltaTime;
        float k = Mathf.Clamp01(_age / _dur);
        float t = Time.time;

        // The well swells as it fills.
        float s = _s.radius * 2f * Mathf.Lerp(0.2f, 1.5f, k);
        if (_well != null)
        {
            _well.transform.localScale = new Vector3(s, s, 1f);
            SetAlpha(_well, k);
        }
        if (_rim != null)
        {
            float rs = s * (2.2f + 0.2f * Mathf.Sin(t * 9f));
            _rim.transform.localScale = new Vector3(rs, rs, 1f);
            SetAlpha(_rim, 0.25f + 0.45f * k);
        }

        // Motes spiral inward on a loop — matter being pulled in to make the orbs.
        for (int i = 0; i < _motes.Length; i++)
        {
            var m = _motes[i];
            if (m == null) continue;
            float f = Mathf.Repeat(t * 0.9f + _motePhase[i], 1f);     // 1 = far, 0 = swallowed
            float r = _s.radius * Mathf.Lerp(0.2f, 3.4f, f);
            float a = (_motePhase[i] * Mathf.PI * 2f) + f * 6.5f;
            m.transform.localPosition = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.8f, 0f);
            float ms = _s.radius * 0.45f * (1f - f * 0.5f);
            m.transform.localScale = new Vector3(ms, ms, 1f);
            SetAlpha(m, k * (1f - f) * 0.9f);
        }
    }

    /// The orbs are away: flash out and die.
    public void Burst()
    {
        if (_ending) return;
        _ending = true;
        StartCoroutine(EndRoutine(0.22f, true));
    }

    /// Called off instead (the boss died mid wind-up): fade quietly.
    public void Cancel()
    {
        if (_ending) return;
        _ending = true;
        StartCoroutine(EndRoutine(0.15f, false));
    }

    private IEnumerator EndRoutine(float dur, bool burst)
    {
        float t = 0f;
        float baseS = _well != null ? _well.transform.localScale.x : _s.radius;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            if (_well != null)
            {
                float s = burst ? Mathf.Lerp(baseS, baseS * 0.1f, k) : Mathf.Lerp(baseS, 0f, k);
                _well.transform.localScale = new Vector3(s, s, 1f);
                SetAlpha(_well, 1f - k);
            }
            if (_rim != null)
            {
                float s = burst ? Mathf.Lerp(baseS * 2.2f, baseS * 6f, k) : Mathf.Lerp(baseS * 2.2f, 0f, k);
                _rim.transform.localScale = new Vector3(s, s, 1f);
                SetAlpha(_rim, (1f - k) * 0.8f);
            }
            for (int i = 0; i < _motes.Length; i++) SetAlpha(_motes[i], (1f - k) * 0.5f);
            yield return null;
        }
        Destroy(gameObject);
    }

    private static void SetAlpha(SpriteRenderer sr, float a)
    {
        if (sr == null) return;
        var c = sr.color; c.a = Mathf.Clamp01(a); sr.color = c;
    }
}




