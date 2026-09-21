using System.Collections.Generic;
using UnityEngine;

// A single ranged tree-hand strike for Boss3
//   TELEGRAPH  — LOCK-ON. The branch only buds out of the hand while a ring converges
//                on the target, so where it is going is clear before it moves.
//   CRAWL      — the branch reaches for that point slowly and steadily, heat building
//                along it. This is the long window the counterplay lives in.
//   STRIKE     — the last sliver snaps onto the target, a flash pops, onImpact fires
//   RETRACT    — recoils back to the hand and fades
// Nothing is decided until the branch ARRIVES: through lock-on, crawl and strike it is
// itself a target, carrying a small HP pool and a hitbox strung along its length. Break
// it and it bursts into pieces, the pending hit is cancelled outright, and Boss3 is
// notified via onBroken so it can drop the volley and go prone.
// See Boss3HandHurtbox / Boss3HandShatter.
// Damage is not applied here — Boss3 passes an onImpact(Vector2) callback so all the
// player/tower/core target-resolution lives in one place (Boss3.ApplyHandDamage),
// matching how Boss2 resolves its meteor damage.
public class Boss3TreeHand : MonoBehaviour
{
    [System.Serializable]
    public class Settings
    {
        public float telegraphDuration = 0.85f;   // LOCK-ON: ring converges, branch buds
        public float crawlDuration = 2.2f;        // the branch's slow reach to the target
        public float strikeDuration = 0.12f;      // final snap onto the target
        public float holdDuration = 0.06f;        // fully-extended dwell
        public float retractDuration = 0.28f;     // recoil back
        public int segments = 11;                 // jaggedness of the main branch
        public float jitter = 0.34f;              // perpendicular wander, world units
        public float telegraphWidth = 0.06f;
        public float strikeWidth = 0.2f;
        public int forkCount = 4;                 // twigs distributed along the branch
        public float impactRadius = 1.1f;         // visual flash size (damage radius is Boss3's)
        public Color barkColor = new Color(0.06f, 0.04f, 0.07f, 1f);      // near-black bark body
        public Color telegraphColor = new Color(0.62f, 0.14f, 0.62f, 1f); // faint violet tip glow
        public Color strikeColor = new Color(1f, 0.28f, 0.10f, 1f);       // fiery red tip
        public int sortingOrder = 2100;           // fixed, above the Y-sort band + grass

        // ── Destructible hand (counterplay) ────────────────────────────────────
        // The branch carries its own small HP pool while it is winding up. Break it
        // and the strike never lands (see Boss3.OnHandDestroyed → interrupt).
        [Header("Destructible")]
        [Tooltip("When off the branch has no hitbox at all and behaves exactly as it " +
                 "did before this feature existed.")]
        public bool destructible = true;
        [Tooltip("Damage the branch itself can absorb before it shatters.")]
        public float handHealth = 55f;
        [Tooltip("Radius of each of the round hit-spots strung along the branch.")]
        public float hitboxRadius = 0.38f;
        [Tooltip("How many hit-spots are strung along the branch. More = a smoother, " +
                 "more forgiving hitbox at a slightly higher physics cost.")]
        public int hitboxCount = 6;
        [Tooltip("Fraction of the branch (measured from the boss's hand) left with NO " +
                 "hitbox, so shots aimed at the boss's body aren't eaten by the stub " +
                 "of the branch growing out of it.")]
        [Range(0f, 0.6f)] public float hitboxStartFraction = 0.22f;
        [Tooltip("Bark shards thrown when the branch is destroyed.")]
        public int breakShardCount = 22;

        [Tooltip("ON (default): a branch breaks after a fixed NUMBER OF HITS, whatever " +
                 "each hit was worth. This is what makes breaking a hand consistent — " +
                 "with a damage pool the count depended on the weapon, the augments and " +
                 "the stage multiplier, so the same branch took 3 shots one run and 6 " +
                 "the next. OFF falls back to the old handHealth damage pool.")]
        public bool breakByHitCount = true;

        [Tooltip("Hits needed to snap one branch when the mode above is ON.")]
        public int hitsToBreak = 3;

        [Tooltip("Seconds a branch ignores further COUNTED hits after taking one. This " +
                 "only matters for continuous sources — a flamethrower or a melee AoE " +
                 "that ticks every frame would otherwise spend all three hits in a " +
                 "fraction of a second. Single shots are never affected: nothing fires " +
                 "faster than this.")]
        public float minHitInterval = 0.08f;

        [Tooltip("Log every hit a branch takes, with the HP left in ITS OWN pool. Turn " +
                 "this on when 'why didn't that branch break?' comes up — the log " +
                 "distinguishes shots that never touched a hitbox (no line at all) from " +
                 "shots that landed but hadn't reached the branch's health yet.")]
        public bool debugLog = false;

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    private enum Phase { Telegraph, Crawl, Strike, Hold, Retract, Done }

    // The branch base follows the boss's LIVE hand (which raises during the attack),
    // so we ask for the origin every frame via a provider rather than caching a point.
    private System.Func<Vector3> _originProvider;
    private Vector3 _tip;             // LOCKED target point
    private Settings _s;
    private System.Action<Vector2> _onImpact;
    private string _sortingLayer;

    private LineRenderer _main;
    private readonly List<LineRenderer> _forks = new List<LineRenderer>();
    private SpriteRenderer _tipGlow;
    private SpriteRenderer _targetRing;   // converging lock-on ring at the strike point
    private SpriteRenderer _flash;

    private Phase _phase = Phase.Telegraph;
    private float _t;
    private bool _impactFired;

    // ── Destructible-hand state ───────────────────────────────────────────────
    // The branch is a target in its own right while it is winding up. Boss3 owns the
    // consequence (interrupt + stagger) via _onBroken; this class owns the HP pool,
    // the hitbox that follows the branch, and the shatter VFX.
    private System.Action<Boss3TreeHand> _onBroken;
    private Boss3HandHurtbox _hurtbox;
    private float _health;
    private float _maxHealth;
    private float _damageFlash;          // decaying white pip when the branch is hit
    private bool _broken;
    private float _nextChipTime;
    private float _nextCountedHitTime;
    private const float CHIP_INTERVAL = 0.07f;

    // Live branch polyline, refreshed every Render() and reused by the hurtbox and by
    // the shatter VFX so the pieces fly from exactly where the branch was drawn.
    private Vector3[] _pts;

    /// True while the branch can still be destroyed: it is reaching, and it has not yet
    /// landed its hit. That is the WHOLE approach — lock-on, the long crawl, and the
    /// final snap — which is the window the counterplay lives in. Once the strike has
    /// connected there is nothing left to prevent, so the hitbox switches off rather
    /// than soaking shots for no gameplay reason.
    public bool CanBeDamaged =>
        !_broken && _s != null && _s.destructible && !_impactFired &&
        (_phase == Phase.Telegraph || _phase == Phase.Crawl || _phase == Phase.Strike);

    /// 0..1 remaining branch integrity (1 = untouched). Used for the visible fraying.
    public float HealthFraction => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 1f;

    /// Which of the boss's two arms this branch grew from (+1 right, −1 left).
    /// Boss3 uses it to break/regrow the matching arm.
    public float ArmSign { get; private set; } = 1f;

    // Per-segment perpendicular offsets, regenerated occasionally for a "glitchy" crawl.
    private float[] _wander;
    private float _wanderTimer;

    // How far along the path the branch sits at the end of each approach phase.
    // The lock-on only buds it out of the hand; the crawl does nearly all the travel;
    // the strike covers the last sliver as a snap onto the target.
    private const float BUD_EXTENT = 0.10f;
    private const float CRAWL_EXTENT = 0.92f;

    // NOTE: every parameter after `onImpact` is optional and defaults to the pre-
    // destructible-hands behaviour, so any other call site keeps compiling and keeps
    // its old behaviour untouched.
    public static Boss3TreeHand Spawn(System.Func<Vector3> originProvider, Vector3 tip,
                                      Settings settings, string sortingLayer,
                                      System.Action<Vector2> onImpact,
                                      System.Action<Boss3TreeHand> onBroken = null,
                                      int hurtboxLayer = -1,
                                      bool towerShotsCanBreak = false,
                                      float armSign = 1f)
    {
        var go = new GameObject("Boss3_TreeHand");
        var hand = go.AddComponent<Boss3TreeHand>();
        hand.Init(originProvider, tip, settings, sortingLayer, onImpact,
                  onBroken, hurtboxLayer, towerShotsCanBreak, armSign);
        return hand;
    }

    private void Init(System.Func<Vector3> originProvider, Vector3 tip,
                      Settings settings, string sortingLayer, System.Action<Vector2> onImpact,
                      System.Action<Boss3TreeHand> onBroken, int hurtboxLayer,
                      bool towerShotsCanBreak, float armSign)
    {
        _originProvider = originProvider;
        _tip = tip;
        _s = settings ?? new Settings();
        _onImpact = onImpact;
        _sortingLayer = sortingLayer;
        _onBroken = onBroken;
        ArmSign = armSign >= 0f ? 1f : -1f;

        // In hit-count mode the "health" pool is simply the number of hits left, so
        // every downstream consumer — the fraying in Render, HealthFraction, the
        // Shatter trigger — keeps working unchanged.
        _maxHealth = _s.breakByHitCount
            ? Mathf.Max(1, _s.hitsToBreak)
            : Mathf.Max(1f, _s.handHealth);
        _health = _maxHealth;

        _wander = new float[Mathf.Max(2, _s.segments) + 1];
        _pts = new Vector3[_wander.Length];
        RegenWander();

        _main = BuildLine("Branch", _s.telegraphWidth);

        // Several small forked twigs distributed along the branch for the tree-like read.
        int forkCount = Mathf.Max(0, _s.forkCount);
        for (int i = 0; i < forkCount; i++)
            _forks.Add(BuildLine("Twig" + i, _s.telegraphWidth * 0.6f));

        _tipGlow = BuildDot("TipGlow", _s.telegraphColor, Boss3Sprites.SoftDot);
        _targetRing = BuildDot("TargetRing", _s.telegraphColor, Boss3Sprites.Spark);
        _flash = BuildDot("Flash", _s.strikeColor, Boss3Sprites.Spark);
        _flash.enabled = false;

        // Physical hitbox so the branch can be shot / hit. Only built when the boss
        // actually wants destructible hands AND it was given a layer to sit on — a
        // -1 layer means "no hitbox", which is exactly the old behaviour.
        if (_s.destructible && hurtboxLayer >= 0)
            _hurtbox = Boss3HandHurtbox.Create(this, hurtboxLayer,
                                               Mathf.Max(1, _s.hitboxCount),
                                               Mathf.Max(0.05f, _s.hitboxRadius),
                                               towerShotsCanBreak);
    }

    // ── taking damage ────────────────────────────────────────────────────────

    /// Apply damage to the BRANCH (not to the boss). Returns true if it landed, so a
    /// caller can decide whether to consume its projectile. Silently no-ops once the
    /// branch has struck or already broken, which is what keeps this from turning into
    /// a permanent bullet sponge parked in front of the boss.
    public bool TakeHandDamage(float amount) => TakeHandDamage(amount, TipPosition());

    /// <inheritdoc cref="TakeHandDamage(float)"/>
    /// <param name="at">Where the hit landed, so the chips fly from the right spot.</param>
    public bool TakeHandDamage(float amount, Vector3 at)
    {
        if (!CanBeDamaged || amount <= 0f) return false;

        if (_s.breakByHitCount)
        {
            // Too soon to count (a continuous source ticking every frame). The shot is
            // still reported as landed so the caller consumes it — a projectile that
            // visibly struck the branch must not sail on into the boss.
            if (Time.time < _nextCountedHitTime) return true;
            _nextCountedHitTime = Time.time + Mathf.Max(0f, _s.minHitInterval);
            _health -= 1f;
        }
        else
        {
            _health -= amount;
        }

        _damageFlash = 1f;

        if (_s.debugLog)
        {
            string pool = _s.breakByHitCount
                ? $"hit {(int)(_maxHealth - _health)}/{(int)_maxHealth}"
                : $"{Mathf.Max(0f, _health):F1}/{_maxHealth:F1} hp left";
            Debug.Log($"[Boss3Hand] arm {(ArmSign >= 0f ? "R" : "L")} took {amount:F1} — " +
                      pool + (_health <= 0f ? "  → BREAKS" : ""));
        }

        // A few bark chips per hit so chipping away at it reads before it gives.
        // Rate-limited: a flamethrower or a fast auto weapon can land many hits a
        // second and each puff allocates GameObjects. The white flash above still
        // fires on EVERY hit, so feedback never actually drops.
        if (Time.time >= _nextChipTime)
        {
            _nextChipTime = Time.time + CHIP_INTERVAL;
            Boss3HandShatter.Chip(at, _s.barkColor, _s.strikeColor, _sortingLayer,
                                  _s.sortingOrder + 2);
        }

        if (_health <= 0f) Shatter();
        return true;
    }

    /// The branch gives: it bursts into bark shards and is gone. The pending strike is
    /// cancelled outright — `_impactFired` is latched true so the damage callback can
    /// never run — and Boss3 is told so it can stagger and drop the rest of the volley.
    public void Shatter()
    {
        if (_broken || _phase == Phase.Done) return;
        _broken = true;
        _impactFired = true;                 // the strike is cancelled, not merely early
        _phase = Phase.Done;

        if (_hurtbox != null) _hurtbox.SetActive(false);

        Boss3HandShatter.Spawn(_pts, _s.barkColor, _s.strikeColor, _sortingLayer,
                               _s.sortingOrder + 2, Mathf.Max(4, _s.breakShardCount),
                               _s.strikeWidth);

        // Tell the boss BEFORE we die, while `this` is still a valid reference.
        var cb = _onBroken;
        _onBroken = null;
        cb?.Invoke(this);

        Destroy(gameObject);
    }

    // Current world position of the branch's growing tip (falls back to the locked
    // target point before the first Render()).
    private Vector3 TipPosition()
    {
        if (_pts == null || _pts.Length == 0) return _tip;
        return _pts[_pts.Length - 1];
    }

    // One shared "Sprites/Default" material for every line this class builds.
    //
    // Was `new Material(Shader.Find("Sprites/Default"))` per LineRenderer, which is
    // a shader-registry string lookup and a fresh Material object EVERY time - and
    // this is a hot path (a branch plus its twigs, per hand, per volley), so it
    // ran constantly rather than
    // once at setup. Nothing here sets per-instance material state; all colouring
    // goes through startColor/endColor, which are vertex colours. So one shared
    // instance covers every line.
    //
    // Assigned via sharedMaterial, never .material - reading the `.material` getter
    // makes Unity instantiate a per-renderer copy, which is the thing being avoided.
    private static Material _lineMat;

    // Play-mode exit destroys the material, but with domain reload disabled the
    // static field would still point at the dead object next Play. Same guard
    // Boss3Sprites uses.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLineMaterial() => _lineMat = null;

    private static Material LineMaterial
    {
        get
        {
            if (_lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                _lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _lineMat;
        }
    }

    private LineRenderer BuildLine(string name, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = LineMaterial;
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.startWidth = lr.endWidth = width;
        lr.sortingOrder = _s.sortingOrder;
        if (!string.IsNullOrEmpty(_sortingLayer)) lr.sortingLayerName = _sortingLayer;
        lr.startColor = lr.endColor = _s.telegraphColor;
        return lr;
    }

    private SpriteRenderer BuildDot(string name, Color col, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = col;
        sr.sortingOrder = _s.sortingOrder + 1;
        if (!string.IsNullOrEmpty(_sortingLayer)) sr.sortingLayerName = _sortingLayer;
        return sr;
    }

    private void RegenWander()
    {
        for (int i = 0; i < _wander.Length; i++)
        {
            // Ends pinned (0), middle wanders — a taut-but-crooked branch.
            float edge = Mathf.Sin((i / (float)(_wander.Length - 1)) * Mathf.PI);
            _wander[i] = Random.Range(-1f, 1f) * _s.jitter * edge;
        }
    }

    private Vector3 Origin()
    {
        return _originProvider != null ? _originProvider() : transform.position;
    }

    // A ring that starts wide and CONVERGES onto the locked strike point over the
    // telegraph, so the exact impact moment is unambiguous — the dodge cue.
    private void UpdateTargetRing(float k)
    {
        if (_targetRing == null) return;
        _targetRing.enabled = true;
        _targetRing.transform.position = _tip;
        float size = Mathf.Lerp(_s.impactRadius * 3.2f, _s.impactRadius * 1.0f, k);
        _targetRing.transform.localScale = new Vector3(size, size, 1f);
        // Brighten and shift violet→red as it locks on.
        Color c = Color.Lerp(_s.telegraphColor, _s.strikeColor, k);
        c.a = Mathf.Lerp(0.15f, 0.8f, k) * (0.7f + 0.3f * Mathf.Sin(Time.time * 20f));
        _targetRing.color = c;
    }

    private void HideTargetRing()
    {
        if (_targetRing != null) _targetRing.enabled = false;
    }

    private void Update()
    {
        if (_phase == Phase.Done) return;

        float dt = Time.deltaTime;
        _t += dt;
        _damageFlash = Mathf.Max(0f, _damageFlash - dt * 5f);

        // Occasionally re-roll the crookedness so the branch "glitches" as it holds.
        _wanderTimer -= dt;
        if (_wanderTimer <= 0f)
        {
            _wanderTimer = Random.Range(0.05f, 0.12f);
            RegenWander();
        }

        switch (_phase)
        {
            // LOCK-ON: the branch only buds out of the hand while the ring converges on
            // the target. The point is to say "this is where it's going" BEFORE it
            // starts travelling, so the crawl that follows is legible from frame one.
            case Phase.Telegraph:
                {
                    float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _s.telegraphDuration));
                    float grow = 1f - (1f - k) * (1f - k);
                    Render(grow * BUD_EXTENT, _s.telegraphWidth, _s.telegraphColor, tipPulse: true);
                    UpdateTargetRing(k * 0.35f);
                    if (k >= 1f) { _phase = Phase.Crawl; _t = 0f; }
                    break;
                }

            // CRAWL: the whole point of the fight's counterplay. The branch reaches for
            // its locked point at a steady, readable pace — SLOW enough that the player
            // can turn, aim at the branch and break it, instead of only having time to
            // sidestep. Nothing is decided until it arrives.
            case Phase.Crawl:
                {
                    float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _s.crawlDuration));

                    // Linear travel with a small stutter, so it creeps like something
                    // growing rather than sliding smoothly like a projectile.
                    float creep = k + Mathf.Sin(k * Mathf.PI * 9f) * 0.012f;
                    float ext = Mathf.Lerp(BUD_EXTENT, CRAWL_EXTENT, Mathf.Clamp01(creep));

                    // Heat builds along the approach: violet at the hand, reddening as
                    // it closes, so "how much time is left" is readable from colour.
                    Color c = Color.Lerp(_s.telegraphColor, _s.strikeColor, k * 0.75f);
                    float w = Mathf.Lerp(_s.telegraphWidth, _s.strikeWidth * 0.8f, k);
                    Render(ext, w, c, tipPulse: false);

                    // The ring keeps closing over the whole approach — it hits its
                    // tightest exactly as the branch arrives.
                    UpdateTargetRing(0.35f + k * 0.65f);

                    if (k >= 1f) { _phase = Phase.Strike; _t = 0f; }
                    break;
                }

            case Phase.Strike:
                {
                    HideTargetRing();
                    float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _s.strikeDuration));
                    float ext = Mathf.Lerp(CRAWL_EXTENT, 1f, k);
                    float w = Mathf.Lerp(_s.strikeWidth * 0.8f, _s.strikeWidth, k);
                    Render(ext, w, Color.Lerp(_s.telegraphColor, _s.strikeColor, 0.75f + k * 0.25f),
                           tipPulse: false);

                    if (!_impactFired && k >= 1f)
                    {
                        _impactFired = true;
                        _onImpact?.Invoke(_tip);
                        PopFlash();
                        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.18f, 0.10f);
                        _phase = Phase.Hold; _t = 0f;
                    }
                    break;
                }
            case Phase.Hold:
                {
                    Render(1f, _s.strikeWidth, _s.strikeColor, tipPulse: false);
                    if (_t >= _s.holdDuration) { _phase = Phase.Retract; _t = 0f; }
                    break;
                }
            case Phase.Retract:
                {
                    float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _s.retractDuration));
                    float ext = 1f - k;                       // recoil back to the hand
                    float w = Mathf.Lerp(_s.strikeWidth, 0f, k);
                    Color c = _s.strikeColor; c.a = 1f - k;
                    Render(ext, w, c, tipPulse: false);
                    if (_tipGlow != null) { var g = _tipGlow.color; g.a = (1f - k) * 0.6f; _tipGlow.color = g; }
                    if (k >= 1f) { _phase = Phase.Done; Destroy(gameObject); }
                    break;
                }
        }

        // Shatter() destroys this object mid-switch; bail before touching anything else.
        if (_broken) return;

        // Keep the hitbox glued to the branch we just drew, and switch it off the
        // instant the branch stops being destroyable (after impact / during retract),
        // so a spent branch never soaks another shot.
        if (_hurtbox != null)
        {
            bool live = CanBeDamaged;
            _hurtbox.SetActive(live);
            if (live) _hurtbox.SyncTo(_pts, _s.hitboxStartFraction);
        }

        // Flash fade (independent of phase once popped).
        if (_flash != null && _flash.enabled)
        {
            var fc = _flash.color;
            fc.a = Mathf.Max(0f, fc.a - dt * 4.5f);
            _flash.color = fc;
            float sc = _flash.transform.localScale.x + dt * _s.impactRadius * 3f;
            _flash.transform.localScale = new Vector3(sc, sc, 1f);
            if (fc.a <= 0.01f) _flash.enabled = false;
        }
    }

    // Draw the branch from the hand toward `extend` fraction of the way to the tip,
    // with the crooked wander applied perpendicular to the branch direction.
    private void Render(float extend, float width, Color color, bool tipPulse)
    {
        Vector3 origin = Origin();
        Vector3 end = Vector3.Lerp(origin, _tip, Mathf.Clamp01(extend));
        Vector3 dir = (end - origin);
        float len = dir.magnitude;
        Vector3 fwd = len > 1e-4f ? dir / len : Vector3.right;
        Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);

        // Bark body is near-black; only the growing TIP glows (violet → fiery red). The
        // `color` passed in drives that tip glow so the strike reads as heat travelling
        // out along the branch, not a flat coloured line.
        Color bark = _s.barkColor; bark.a = color.a;
        Color tipCol = color;

        // Battle damage: the branch visibly thins and pales as its integrity drops, and
        // pips white for a moment on each hit — so the player can read that shooting it
        // is doing something long before it finally gives.
        //
        // Pushed hard on purpose. With a 3-hit break there are only two intermediate
        // states, and they have to be unmistakable at a glance: when a volley throws
        // three branches at once, "which of these have I already hit twice?" is the
        // whole decision, and a subtle tint could not answer it.
        float integrity = HealthFraction;
        if (_s.destructible && integrity < 1f)
        {
            width *= Mathf.Lerp(0.45f, 1f, integrity);
            Color ash = new Color(0.62f, 0.50f, 0.48f, 1f);
            bark = Color.Lerp(bark, ash, (1f - integrity) * 0.9f);
            bark.a = color.a;

            // A damaged branch also stutters — the closer to breaking, the more it
            // flickers, which reads instantly even in peripheral vision.
            float fray = 1f - integrity;
            float stutter = 0.82f + 0.18f * Mathf.Sin(Time.time * (14f + 26f * fray));
            width *= Mathf.Lerp(1f, stutter, fray);
        }
        if (_damageFlash > 0f)
        {
            bark = Color.Lerp(bark, Color.white, _damageFlash * 0.85f);
            tipCol = Color.Lerp(tipCol, Color.white, _damageFlash * 0.6f);
            bark.a = color.a;
            tipCol.a = color.a;
        }

        int n = _wander.Length;
        _main.positionCount = n;
        _main.startWidth = width;
        _main.endWidth = width * 0.3f;       // taper to a thin tip
        _main.startColor = bark;
        _main.endColor = tipCol;             // glowing tip

        if (_pts == null || _pts.Length != n) _pts = new Vector3[n];

        Vector3 tipPos = origin;
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(origin, end, f) + perp * _wander[i];
            _main.SetPosition(i, p);
            _pts[i] = p;                     // cached for the hitbox + the shatter VFX
            tipPos = p;
        }

        // Forked twigs branch off along the whole length (not just the tip), fanning to
        // alternating sides so it reads as a jagged, living branch.
        int fc = _forks.Count;
        for (int fi = 0; fi < fc; fi++)
        {
            var twig = _forks[fi];
            float baseF = fc > 1 ? Mathf.Lerp(0.32f, 0.9f, fi / (float)(fc - 1)) : 0.7f;
            int wi = Mathf.Clamp((int)(baseF * (n - 1)), 0, n - 1);
            Vector3 baseP = Vector3.Lerp(origin, end, baseF) + perp * _wander[wi];
            float side = (fi % 2 == 0) ? 1f : -1f;
            float twigLen = len * Mathf.Lerp(0.22f, 0.1f, baseF) * extend;   // longer low, shorter high
            Vector3 twigDir = (fwd * 0.5f + perp * side).normalized;
            Vector3 twigTip = baseP + twigDir * twigLen;

            // Twigs share the branch's heat: darker at the join, glowing at their tips.
            Color twBark = bark;
            Color twTip = new Color(tipCol.r, tipCol.g, tipCol.b, tipCol.a * 0.9f);

            twig.positionCount = 3;
            twig.startWidth = width * 0.55f;
            twig.endWidth = 0f;
            twig.startColor = twBark;
            twig.endColor = twTip;
            twig.SetPosition(0, baseP);
            twig.SetPosition(1, Vector3.Lerp(baseP, twigTip, 0.5f) + perp * side * (_s.jitter * 0.3f));
            twig.SetPosition(2, twigTip);
        }

        // Tip marker sits at the LOCKED spot during the wind-up so the player can read
        // exactly where the strike will land, then rides the tip during the strike.
        if (_tipGlow != null)
        {
            Vector3 markPos = tipPulse ? _tip : tipPos;
            _tipGlow.transform.position = markPos;
            float pulse = tipPulse ? (0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 8f))) : 1f;
            float size = _s.impactRadius * (tipPulse ? 0.5f : 0.7f) * (0.85f + 0.3f * pulse);
            _tipGlow.transform.localScale = new Vector3(size, size, 1f);
            Color tg = tipPulse ? _s.telegraphColor : _s.strikeColor;
            tg.a = (tipPulse ? 0.5f : 0.85f) * pulse;
            _tipGlow.color = tg;
        }
    }

    private void PopFlash()
    {
        if (_flash == null) return;
        _flash.enabled = true;
        _flash.transform.position = _tip;
        float s = _s.impactRadius;
        _flash.transform.localScale = new Vector3(s, s, 1f);
        _flash.color = new Color(1f, 0.9f, 0.7f, 1f);
    }

    // Boss died mid-swing: fade out cleanly instead of freezing on screen.
    public void Cancel()
    {
        if (_phase == Phase.Done) return;
        _phase = Phase.Retract;
        _t = _s.retractDuration * 0.5f;
        // A cancelled branch is no longer a threat, so it stops being a target too.
        if (_hurtbox != null) _hurtbox.SetActive(false);
    }
}




