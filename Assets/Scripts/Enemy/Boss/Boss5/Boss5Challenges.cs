using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  BOSS 5 — CHALLENGE MECHANICS
//  One class per State B mechanic

public static class Boss5MapBounds
{
    private static TowerDefenseMap _map;
    private static int _obstacleMask = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _map = null; _obstacleMask = -1; }

    public static TowerDefenseMap Map
    {
        get
        {
            if (_map == null) _map = Object.FindAnyObjectByType<TowerDefenseMap>();
            return _map;
        }
    }

    private static int ObstacleMask
    {
        get
        {
            if (_obstacleMask < 0)
            {
                string layerName = Map != null ? Map.obstacleLayerName : "Obstacle";
                if (string.IsNullOrEmpty(layerName)) layerName = "Obstacle";
                _obstacleMask = LayerMask.GetMask(layerName);
            }
            return _obstacleMask;
        }
    }

    /// True when a circle of `radius` at `pos` clips map geometry the player cannot
    /// stand in. Uses 0.8× the radius so a zone that merely brushes a wall is still
    /// accepted — the player only has to get their body inside, not the whole circle.
    public static bool OverlapsObstacle(Vector3 pos, float radius)
    {
        if (ObstacleMask == 0) return false;   // no Obstacle layer in this project
        return Physics2D.OverlapCircle(pos, radius * 0.8f, ObstacleMask) != null;
    }
}


public abstract class Boss5Challenge
{
    protected readonly Boss5 Boss;
    private bool _endRequested;
    private bool _cleanedUp;
    private Boss5CountdownRing _countdown;

    protected Boss5Challenge(Boss5 boss) { Boss = boss; }

    public abstract Boss5Mechanic Mechanic { get; }

    /// One colour per mechanic, used by the countdown ring and the boss aura so the
    /// player can tell WHICH rule is live at a glance, before reading any detail.
    ///   amber = don't move · red = don't attack · green = get to the zone
    public static Color ColorFor(Boss5Mechanic m)
    {
        switch (m)
        {
            case Boss5Mechanic.MotionPenalty: return new Color(1f, 0.72f, 0.15f);
            case Boss5Mechanic.AttackPenalty: return new Color(1f, 0.28f, 0.22f);
            case Boss5Mechanic.SafeZone: return new Color(0.35f, 1f, 0.55f);
        }
        return Color.white;
    }

    /// True once the challenge has been asked to wind up early — normally because
    /// the player failed it and Boss5.endStateBOnFailure is on.
    protected bool EndRequested => _endRequested;

    public void RequestEnd() => _endRequested = true;

    public IEnumerator Run()
    {
        try
        {
            yield return Execute();
        }
        finally
        {
            SafeCleanup();
        }
    }

    protected abstract IEnumerator Execute();

    /// Tear down every visual / global this challenge created. Must be idempotent.
    protected virtual void Cleanup() { }

    private void SafeCleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        DestroyCountdown();
        Cleanup();
    }

    /// Called from Boss5's death / destroy path.
    public void AbortAndCleanup()
    {
        _endRequested = true;
        SafeCleanup();
    }

    /// Return true to ABSORB the player's damage (the boss takes none) — used by
    /// the Attack Penalty mechanic. Return false to let it through as normal.
    public virtual bool InterceptPlayerDamage(float amount) => false;

    // ── countdown 

    /// Put a deadline ring around the boss. Every mechanic calls this, because a
    /// timed rule with no visible timer reads as arbitrary punishment.
    protected void StartCountdown(float radius = 2.6f)
    {
        DestroyCountdown();
        if (Boss == null) return;
        _countdown = Boss5CountdownRing.Create(
            Boss.transform, new Vector2(0f, 0f), radius, ColorFor(Mechanic));
        _countdown.SetRemaining(1f);
    }

    protected void UpdateCountdown(float elapsed, float duration)
    {
        if (_countdown == null || duration <= 0f) return;
        _countdown.SetRemaining(1f - Mathf.Clamp01(elapsed / duration));
    }

    private void DestroyCountdown()
    {
        if (_countdown != null) Object.Destroy(_countdown.gameObject);
        _countdown = null;
    }

    // ── shared helpers ────────────────────────────────────────────────────────

    /// Every alive player, as a snapshot list. Radius-based because that is the only
    /// enumeration PlayerRegistry exposes that filters out downed players.
    protected static List<PlayerStats> AlivePlayers()
    {
        var result = new List<PlayerStats>(2);
        var reg = PlayerRegistry.Instance;
        if (reg == null) return result;
        foreach (var ps in reg.AllAliveInRadius(Vector3.zero, 100000f))
            if (ps != null) result.Add(ps);
        return result;
    }

    protected static Vector3 Centroid(List<PlayerStats> players)
    {
        if (players == null || players.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < players.Count; i++) sum += players[i].transform.position;
        return sum / players.Count;
    }
}


//  MECHANIC 1 — MOTION PENALTY 

[System.Serializable]
public class Boss5MotionPenaltySettings
{
    [Tooltip("How long the 'don't move' window lasts at FULL boss health.")]
    [Min(1f)] public float durationAtFullHp = 6f;

    [Tooltip("How long it lasts at MAX enrage. Kept close to the full-health value: " +
             "the enrage pressure comes from the tolerance below, not from the length.")]
    [Min(1f)] public float durationAtMaxEnrage = 5f;

    [Tooltip("Movement below this many world units per second does not count. " +
             "Absorbs physics jitter and enemies bumping into the player.")]
    [Min(0f)] public float movementDeadzonePerSecond = 0.28f;

    [Tooltip("GRACE at the start of the window: no filling at all for this long, so " +
             "movement already underway when the bell tolls doesn't punish you before " +
             "you've had a chance to react.")]
    [Min(0f)] public float graceSeconds = 0.55f;

    [Tooltip("How long you must move CONTINUOUSLY before the circle starts filling. " +
             "This is what makes a single step safe: taking one pace and stopping " +
             "never reaches this, so nothing accumulates. Resets the moment you stop.")]
    [Min(0f)] public float sustainedMovementSeconds = 0.22f;

    [Tooltip("How fast the circle fills toward red once movement is sustained, at " +
             "FULL boss health. 0.42 = about 2.4 seconds of continuous running to fail, " +
             "on top of the sustain delay above.")]
    [Min(0.01f)] public float fillPerSecondAtFullHp = 0.42f;

    [Tooltip("Fill rate at MAX enrage.")]
    [Min(0.01f)] public float fillPerSecondAtMaxEnrage = 1.7f;

    [Tooltip("How fast the circle drains back toward green while you hold still. " +
             "Kept higher than the fill rate so stopping is visibly rewarded.")]
    [Min(0.01f)] public float decayPerSecond = 0.8f;

    [Tooltip("Movement speed treated as 'normal walking'. The circle fills in " +
             "proportion to how fast you are actually going relative to this, so " +
             "SPRINTING across the arena fills far quicker than shuffling — which is " +
             "what stops a long fast run from going unpunished. Set it near your " +
             "player's base moveSpeed.")]
    [Min(0.1f)] public float referenceSpeed = 4f;

    [Tooltip("Cap on the speed multiplier, so a dash cannot fill the whole circle in " +
             "a single frame.")]
    [Range(1f, 5f)] public float maxSpeedMultiplier = 2.5f;

    [Tooltip("At or above this enrage level ANY sustained movement fills the circle " +
             "instantly.\n\n" +
             "DISABLED BY DEFAULT (1.1 is above the 0-1 enrage range). The spec asks " +
             "for 'a single step near death', but in play an instant unavoidable " +
             "explosion reads as a bug rather than a difficulty spike — you cannot " +
             "see it coming and cannot answer it. The enrage fill-rate ramp above " +
             "already makes late-fight movement punishing. Set this to ~0.9 if you " +
             "want the original behaviour back.")]
    [Range(0f, 1.1f)] public float instantFailEnrageThreshold = 1.1f;

    [Tooltip("Radius of the circle drawn under the player, in world units.")]
    [Min(0.2f)] public float circleRadius = 1.15f;

    [Header("Reaction — display only")]
    [Tooltip("How far toward amber the circle is allowed to travel on the SUSTAIN " +
             "timer alone, before the real meter has started accumulating.\n\n" +
             "COLOUR ONLY. It cannot fill the meter, cannot shorten the grace window " +
             "and cannot cause a failure — it just lets the circle warn you slightly " +
             "before damage is owed. 0.35 is the original behaviour. Raise it if you " +
             "want an earlier visual warning WITHOUT making the mechanic harsher; the " +
             "timings above are the only things that change difficulty.")]
    [Range(0f, 0.9f)] public float warnLeadStrength = 0.35f;

    [Header("Gradient")]
    public Color safeColor = new Color(0.15f, 1f, 0.25f, 1f);
    public Color warnColor = new Color(1f, 0.9f, 0.1f, 1f);
    public Color failColor = new Color(1f, 0.12f, 0.1f, 1f);

    /// Deliberately does nothing.
    ///
    /// An earlier version of this method rewrote these timings to "recommended"
    /// values whenever they matched a previous default. That is the wrong trade:
    /// it silently overrode numbers that were already tuned in the inspector, and
    /// the first anyone knew about it was the mechanic playing harder than it used
    /// to. Difficulty lives in the inspector now, and only there. The call site is
    /// kept so the shape does not churn again.
    public void MigrateLegacyDefaults() { }
}

public class Boss5MotionPenaltyChallenge : Boss5Challenge
{
    public override Boss5Mechanic Mechanic => Boss5Mechanic.MotionPenalty;

    private readonly Dictionary<PlayerStats, Boss5GroundCircle> _circles =
        new Dictionary<PlayerStats, Boss5GroundCircle>();
    private readonly Dictionary<PlayerStats, float> _meters = new Dictionary<PlayerStats, float>();
    private readonly Dictionary<PlayerStats, Vector3> _lastPos = new Dictionary<PlayerStats, Vector3>();
    private readonly Dictionary<PlayerStats, float> _moveTime = new Dictionary<PlayerStats, float>();

    public Boss5MotionPenaltyChallenge(Boss5 boss) : base(boss) { }

    protected override IEnumerator Execute()
    {
        // Guard against a null settings block. Unity does not run a serialized
        // class's field initialisers when it deserialises a component that was saved
        // before the field existed, so a prefab authored against an earlier Boss5 can
        // hand us null here — which would throw on the very first line and leave the
        // mechanic silently doing nothing.
        var s = Boss.motionPenalty ?? (Boss.motionPenalty = new Boss5MotionPenaltySettings());
        s.MigrateLegacyDefaults();
        float enrage = Boss.Enrage;

        float duration = Mathf.Lerp(s.durationAtFullHp, s.durationAtMaxEnrage, enrage);
        float fillRate = Mathf.Lerp(s.fillPerSecondAtFullHp, s.fillPerSecondAtMaxEnrage, enrage);
        bool instantFail = enrage >= s.instantFailEnrageThreshold;

        var players0 = AlivePlayers();
        foreach (var p in players0) EnsureCircle(p, s);

        // One line per activation. If the circle ever fails to appear again, this
        // says immediately whether the mechanic ran and how many players it found —
        // which is the difference between "not running" and "running but invisible".
        Debug.Log($"[Boss5] Mechanic 1 (don't move) — {duration:F1}s, " +
                  $"{players0.Count} player(s), radius {s.circleRadius}, " +
                  $"instantFail={instantFail}");

        StartCountdown();

        float elapsed = 0f;
        while (elapsed < duration && !EndRequested)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            UpdateCountdown(elapsed, duration);

            // Re-scan so a player revived mid-window also gets a circle, and so a
            // player who went down loses theirs.
            var players = AlivePlayers();
            PruneCirclesNotIn(players);

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var circle = EnsureCircle(p, s);
                if (circle == null) continue;

                Vector3 pos = p.transform.position;
                if (!_lastPos.TryGetValue(p, out var prev)) prev = pos;

                // MOVEMENT-SOURCE: world-position delta, converted to units/second.
                // Raw, unsmoothed, as it originally was. A smoothing filter was tried
                // here and removed: holding the measured speed up across a stuttered
                // frame also held the SUSTAIN timer alive, which made the mechanic
                // harsher rather than steadier.
                float speed = dt > 0.0001f ? (pos - prev).magnitude / dt : 0f;
                _lastPos[p] = pos;

                bool moving = speed > s.movementDeadzonePerSecond;

                // How long this player has been moving WITHOUT stopping. A single
                // step never accumulates: the timer resets the instant they stand
                // still, so it takes sustained running to start filling anything.
                float moveTime = _moveTime.TryGetValue(p, out var mt) ? mt : 0f;
                moveTime = moving ? moveTime + dt : 0f;
                _moveTime[p] = moveTime;

                float meter = _meters.TryGetValue(p, out var m) ? m : 0f;

                bool graceOver = elapsed > s.graceSeconds;
                float sustain = Mathf.Clamp01(
                    s.sustainedMovementSeconds <= 0f ? 1f : moveTime / s.sustainedMovementSeconds);

                // Fill in proportion to actual speed: a sprint costs far more than a
                // shuffle, so covering real distance always registers.
                float speedMul = Mathf.Clamp(
                    speed / Mathf.Max(0.1f, s.referenceSpeed), 0.5f, s.maxSpeedMultiplier);

                if (moving && graceOver && sustain >= 1f)
                    meter = instantFail ? 1f : meter + fillRate * speedMul * dt;
                else if (!moving)
                    meter -= s.decayPerSecond * dt;
                // Moving but still inside grace or sustain: the meter holds. Neither
                // punished nor rewarded — this is the window to notice and stop.

                meter = Mathf.Clamp01(meter);
                _meters[p] = meter;

                // Show the sustain build-up as a warm-up toward amber BEFORE the meter
                // itself moves, so the circle visibly reacts once you are committed to
                // running. COLOUR ONLY — it never touches the meter, so it cannot make
                // the mechanic punish sooner than the timings above say it should.
                float display = Mathf.Max(meter, graceOver ? sustain * s.warnLeadStrength : 0f);

                circle.SetColor(GradientAt(s, display));
                circle.SetPulse(display);

                if (meter >= 1f)
                {
                    Boss.OnChallengeFailed(this);
                    // Drain everyone so a single Kaboom does not immediately chain
                    // into a second one on the very next frame.
                    ResetAllMeters();
                    break;
                }
            }

            yield return null;
        }
    }

    /// Green -> yellow -> red. Two-leg lerp rather than a straight green->red so the
    /// midpoint reads as amber instead of a muddy olive.
    private static Color GradientAt(Boss5MotionPenaltySettings s, float t)
    {
        return t < 0.5f
            ? Color.Lerp(s.safeColor, s.warnColor, t * 2f)
            : Color.Lerp(s.warnColor, s.failColor, (t - 0.5f) * 2f);
    }

    private Boss5GroundCircle EnsureCircle(PlayerStats p, Boss5MotionPenaltySettings s)
    {
        if (p == null) return null;
        if (_circles.TryGetValue(p, out var existing) && existing != null) return existing;

        var circle = Boss5GroundCircle.Create(p.transform, s.circleRadius, s.safeColor);
        _circles[p] = circle;
        _meters[p] = 0f;
        _moveTime[p] = 0f;
        _lastPos[p] = p.transform.position;
        return circle;
    }

    private void PruneCirclesNotIn(List<PlayerStats> alive)
    {
        if (_circles.Count == 0) return;
        List<PlayerStats> stale = null;
        foreach (var kv in _circles)
        {
            if (kv.Key == null || !alive.Contains(kv.Key))
                (stale ??= new List<PlayerStats>()).Add(kv.Key);
        }
        if (stale == null) return;
        foreach (var k in stale)
        {
            if (_circles.TryGetValue(k, out var c) && c != null) Object.Destroy(c.gameObject);
            _circles.Remove(k);
            _meters.Remove(k);
            _moveTime.Remove(k);
            _lastPos.Remove(k);
        }
    }

    /// Drain every meter AND every sustain timer after a Kaboom, so a player who is
    /// still running does not immediately chain into a second explosion on the very
    /// next frame. They get the full grace-plus-sustain window again.
    private void ResetAllMeters()
    {
        var keys = new List<PlayerStats>(_meters.Keys);
        foreach (var k in keys) { _meters[k] = 0f; _moveTime[k] = 0f; }
    }

    protected override void Cleanup()
    {
        foreach (var kv in _circles)
            if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
        _circles.Clear();
        _meters.Clear();
        _moveTime.Clear();
        _lastPos.Clear();
    }
}


//  MECHANIC 2 — ATTACK PENALTY 

[System.Serializable]
public class Boss5AttackPenaltySettings
{
    [Tooltip("How long the 'don't attack' window lasts at FULL boss health.")]
    [Min(1f)] public float durationAtFullHp = 7f;

    [Tooltip("How long it lasts at MAX enrage.")]
    [Min(1f)] public float durationAtMaxEnrage = 5f;

    [Tooltip("Hits needed to completely fill the icon (= Kaboom) at FULL boss health.")]
    [Min(1)] public int hitsToFailAtFullHp = 6;

    [Tooltip("Hits needed at MAX enrage. 1 = the spec's 'near death, a single hit explodes'.")]
    [Min(1)] public int hitsToFailAtMaxEnrage = 1;

    [Tooltip("FLOOR on the enrage-scaled budget above. The allowance shrinks as the " +
             "boss loses health, and at a budget of 1 the very first swing is the " +
             "Kaboom \u2014 no reflected warning fires, because the warning is skipped on " +
             "the hit that fills the meter. From the player's side that is " +
             "indistinguishable from a meter that never reset.\n\n" +
             "A floor of 2 guarantees at least one escalating warning hit in every " +
             "window, however enraged the boss is. Set to 1 to allow true one-shot " +
             "windows again.")]
    [Min(1)] public int minimumHitsToFail = 2;

    [Tooltip("How many of the early hits reflect damage back at the player as a warning. " +
             "Set 0 to remove the teaching hits entirely.")]
    [Min(0)] public int warningHits = 3;

    [Tooltip("Damage reflected by the FIRST accidental hit.")]
    [Min(0f)] public float reflectBaseDamage = 6f;

    [Tooltip("Multiplier applied per warning hit, so the punishment escalates. " +
             "1.8 with a base of 6 gives 6, 10.8, 19.4 …")]
    [Min(1f)] public float reflectEscalation = 1.8f;

    [Header("Icon")]
    [Tooltip("Where the sign floats relative to the boss pivot, in world units.\n\n" +
             "2.6 sits it just over the Bellkeeper's head rather than adrift in the sky " +
             "above it. Raise it if your boss art is taller than the collider suggests.")]
    public Vector2 iconOffset = new Vector2(0f, 2.6f);

    [Tooltip("Sign size in world units. Everything else — plate, rim, slash, meter bar " +
             "— is derived from this, so it is the single scale knob.\n\n" +
             "NOTE: this is a serialized field, so if the Boss5 component was already " +
             "saved on your prefab it keeps ITS value and this new default will not " +
             "apply on its own — use the component cog ▸ 'Apply Recommended Challenge " +
             "Tuning', or right-click the Mechanic 2 header and Reset.")]
    [Min(0.2f)] public float iconSize = 0.7f;

    [Tooltip("Colour of the backing plate behind the weapon glyph.\n\n" +
             "It CANNOT go dark. The weapon art is black on transparent and a " +
             "SpriteRenderer tint multiplies, so black art can never be lightened — " +
             "the glyph only reads against a light backing. This is a PALE lilac for " +
             "that reason: it reads as purple while staying bright enough to keep the " +
             "weapon silhouette legible. Darken it and the glyph disappears.")]
    public Color plateColor = new Color(0.87f, 0.79f, 0.98f, 0.92f);

    [Tooltip("Colour the hit meter fills with. The meter is the bar UNDER the sign, " +
             "not a wash over the glyph itself. Also drives the rim, the halo and the " +
             "night light, so the whole sign follows this one colour.")]
    public Color fillColor = new Color(0.70f, 0.24f, 0.92f, 0.9f);

    [Tooltip("Colour of the diagonal 'no' stroke across the glyph.\n\n" +
             "Left RED on purpose while everything else went purple: it is the one " +
             "element whose job is to say 'forbidden' rather than to show a quantity, " +
             "and against a lilac plate red is the mark that reads first. Set it to a " +
             "deep violet (e.g. 0.38, 0.08, 0.58) if you would rather the sign be " +
             "entirely monochrome.")]
    public Color slashColor = new Color(0.90f, 0.12f, 0.10f, 0.92f);

    /// COSMETICS ONLY. This upgrades the icon's look for a prefab saved before the
    /// new fields existed \u2014 position, size, plate and slash colour. It deliberately
    /// does NOT touch durations, hit budgets, reflect damage or anything else that
    /// changes how hard the mechanic plays. Balance stays wherever the inspector
    /// has it.
    public void MigrateLegacyDefaults()
    {
        if (Mathf.Abs(iconOffset.y - 3.4f) < 0.0005f && Mathf.Abs(iconOffset.x) < 0.0005f)
            iconOffset = new Vector2(0f, 2.6f);

        if (Mathf.Abs(iconSize - 0.9f) < 0.0005f) iconSize = 0.7f;

        // The old red meter, and the brass plate that briefly replaced white.
        if (Approx(fillColor, new Color(1f, 0.25f, 0.2f, 0.9f)))
            fillColor = new Color(0.70f, 0.24f, 0.92f, 0.9f);

        if (Approx(plateColor, new Color(0.98f, 0.82f, 0.42f, 0.92f)))
            plateColor = new Color(0.87f, 0.79f, 0.98f, 0.92f);

        // A plate left fully transparent means the field was serialized before it
        // existed; rendering nothing would leave the black glyph invisible, so treat
        // it as "never set" rather than as a deliberate choice.
        if (plateColor.a <= 0.01f) plateColor = new Color(0.87f, 0.79f, 0.98f, 0.92f);
        if (slashColor.a <= 0.01f) slashColor = new Color(0.90f, 0.12f, 0.10f, 0.92f);
    }

    private static bool Approx(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f
            && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;
    }
}

public class Boss5AttackPenaltyChallenge : Boss5Challenge
{
    public override Boss5Mechanic Mechanic => Boss5Mechanic.AttackPenalty;

    private Boss5WeaponIconMeter _icon;
    private int _hits;
    private int _hitsToFail = 1;
    private bool _failed;

    public Boss5AttackPenaltyChallenge(Boss5 boss) : base(boss) { }

    protected override IEnumerator Execute()
    {
        // See the note in Mechanic 1: a prefab saved before this field existed can
        // deserialise it as null, so build one rather than throwing.
        var s = Boss.attackPenalty ?? (Boss.attackPenalty = new Boss5AttackPenaltySettings());
        s.MigrateLegacyDefaults();
        float enrage = Boss.Enrage;

        // Explicit per-activation reset. Boss5 builds a NEW challenge object for every
        // State B, so these are already 0/false on arrival and this changes nothing
        // today — it is here so the reset does not silently depend on that allocation.
        // If the challenge is ever pooled or re-Run(), a stale _hits would carry the
        // last window's damage into the next one, which looks exactly like the meter
        // failing to reset.
        _hits = 0;
        _failed = false;

        float duration = Mathf.Lerp(s.durationAtFullHp, s.durationAtMaxEnrage, enrage);

        // THE ALLOWANCE SHRINKS WITH ENRAGE, and that is what makes a later window
        // feel like a continuation of the previous one. At full health the boss
        // tolerates six hits; with ~70% of its pool gone, two; near death, exactly
        // one — so a single swing can be an instant Kaboom even though the counter
        // genuinely started at zero. The cell row on the icon now shows the current
        // budget BEFORE the first hit lands.
        _hitsToFail = Mathf.Max(Mathf.Max(1, s.minimumHitsToFail), Mathf.RoundToInt(
            Mathf.Lerp(s.hitsToFailAtFullHp, s.hitsToFailAtMaxEnrage, enrage)));

        _icon = Boss5WeaponIconMeter.Create(Boss.transform, s.iconOffset, s.iconSize,
                                            s.fillColor, s.plateColor, s.slashColor);
        // One cell per allowed hit, so the player can read how many swings are left
        // instead of guessing from a smooth gradient.
        _icon.SetSegments(_hitsToFail);
        _icon.SetFill(0f);

        Debug.Log($"[Boss5] Mechanic 2 (don't attack) — {duration:F1}s, " +
                  $"{_hitsToFail} hit(s) to fail, enrage {enrage:F2}, hits reset to 0");

        StartCountdown();

        float elapsed = 0f;
        while (elapsed < duration && !EndRequested)
        {
            elapsed += Time.deltaTime;
            UpdateCountdown(elapsed, duration);

            // Re-resolve the equipped weapon every frame so a mid-challenge weapon
            // swap is reflected immediately, as the spec requires.
            var nearest = NearestPlayer();
            if (_icon != null) _icon.SetWeaponSprite(Boss5WeaponIconProvider.Resolve(nearest));

            yield return null;
        }
    }

    /// Boss5 routes every PLAYER hit here while this challenge is running.
    /// Always absorbs (returns true) — the boss takes 0 damage from the player.
    public override bool InterceptPlayerDamage(float amount)
    {
        if (_failed) return true;

        _hits++;

        float fill = Mathf.Clamp01(_hits / (float)_hitsToFail);

        // Per-hit trace. If the Kaboom ever fires on a hit number you did not
        // expect, this line and the activation line above settle it immediately:
        // either the budget was small to begin with (enrage), or one swing is being
        // counted more than once (two colliders, or a damage source arriving through
        // two paths).
        Debug.Log($"[Boss5] Mechanic 2 hit {_hits}/{_hitsToFail} ({amount:F1} dmg absorbed)");

        if (_icon != null)
        {
            _icon.SetFill(fill);
            _icon.Punch();
        }

        var s = Boss.attackPenalty;

        // Escalating teaching damage on the early hits. Skipped entirely once the
        // icon is about to fill, because that hit is punished by the Kaboom instead.
        if (_hits <= s.warningHits && fill < 1f && s.reflectBaseDamage > 0f)
        {
            float reflected = s.reflectBaseDamage
                              * Mathf.Pow(Mathf.Max(1f, s.reflectEscalation), _hits - 1)
                              * Boss.SpecialDamageMultiplier;

            var victim = NearestPlayer();
            if (victim != null)
            {
                victim.TakeDamage(reflected);
                EnemyController.NotifyCharacterDamaged(victim, reflected, Boss.gameObject);
            }
        }

        if (fill >= 1f)
        {
            _failed = true;
            Boss.OnChallengeFailed(this);
        }

        return true;   // damage absorbed either way
    }

    private PlayerStats NearestPlayer()
    {
        var reg = PlayerRegistry.Instance;
        if (reg == null) return null;
        var pref = reg.NearestAlive(Boss.transform.position, includeCloaked: true);
        if (pref == null) return null;
        return pref.GetComponentInParent<PlayerStats>() ?? pref.GetComponentInChildren<PlayerStats>();
    }

    protected override void Cleanup()
    {
        if (_icon != null) Object.Destroy(_icon.gameObject);
        _icon = null;
    }
}


//  MECHANIC 3 — SAFE ZONE 

[System.Serializable]
public class Boss5SafeZoneSettings
{
    [Tooltip("Seconds to reach the zone at FULL boss health.")]
    [Min(0.5f)] public float timeLimitAtFullHp = 9f;

    [Tooltip("Seconds to reach the zone at MAX enrage. The spec's 'perfectly sprint " +
             "and dash to arrive in time' — tune against your player's sprint speed.")]
    [Min(0.5f)] public float timeLimitAtMaxEnrage = 3f;

    [Tooltip("Radius of the safe circle in world units.")]
    [Min(0.5f)] public float zoneRadius = 2.6f;

    [Tooltip("Nearest the zone may be placed from the player.")]
    [Min(1f)] public float minDistanceFromPlayer = 9f;

    [Tooltip("Furthest the zone may be placed from the player.")]
    [Min(1f)] public float maxDistanceFromPlayer = 14f;

    [Tooltip("Extra clearance kept between the edge of the safe circle and the map's " +
             "boundary collider, so the zone is never half-buried in the wall.")]
    [Min(0f)] public float mapEdgeMargin = 1.5f;

    [Tooltip("How far the danger overlay extends outward from the zone. Make this " +
             "comfortably larger than a screen so no un-tinted ground is visible.")]
    [Min(10f)] public float dangerCoverRadius = 60f;

    public Color dangerColor = new Color(0.7f, 0.04f, 0.06f, 1f);

    [Tooltip("Peak opacity the danger flood reaches as the timer runs out.")]
    [Range(0f, 1f)] public float dangerMaxAlpha = 0.5f;

    [Header("Reveal")]
    [Tooltip("Freeze the game (Time.timeScale = 0) while the camera pans to the zone " +
             "and back. Turn OFF if another system of yours owns timeScale during a boss.")]
    public bool useTimeStopReveal = true;

    [Tooltip("Seconds for each leg of the pan (out, then back). Real time — unaffected " +
             "by the freeze.")]
    [Min(0.05f)] public float panDuration = 0.65f;

    [Tooltip("Seconds the camera lingers on the zone before panning back.")]
    [Min(0f)] public float holdOnZoneDuration = 0.9f;
}

public class Boss5SafeZoneChallenge : Boss5Challenge
{
    public override Boss5Mechanic Mechanic => Boss5Mechanic.SafeZone;

    private Boss5SafeZoneVisual _visual;
    private Boss5CameraPan _pan;
    private bool _timeStopped;
    private float _restoreTimeScale = 1f;
    private readonly List<PlayerAim> _suppressedAims = new List<PlayerAim>();

    public Boss5SafeZoneChallenge(Boss5 boss) : base(boss) { }

    protected override IEnumerator Execute()
    {
        // See the note in Mechanic 1 about null settings blocks.
        var s = Boss.safeZone ?? (Boss.safeZone = new Boss5SafeZoneSettings());
        float enrage = Boss.Enrage;
        float timeLimit = Mathf.Lerp(s.timeLimitAtFullHp, s.timeLimitAtMaxEnrage, enrage);

        var players = AlivePlayers();
        if (players.Count == 0) yield break;

        Debug.Log($"[Boss5] Mechanic 3 (safe zone) — {timeLimit:F1}s, " +
                  $"radius {s.zoneRadius}, {players.Count} player(s)");

        Vector3 anchor = Centroid(players);
        Vector3 zonePos = ChooseZonePosition(anchor, s);

        _visual = Boss5SafeZoneVisual.Create(zonePos, s.zoneRadius, s.dangerCoverRadius,
                                             s.dangerColor, s.dangerMaxAlpha);

        // ---- reveal --------------------------------------------------------
        if (s.useTimeStopReveal)
        {
            // Capture rather than assume 1: another system (a menu, a cutscene)
            // may already own timeScale, and stomping it to 1 on release would be
            // a regression outside this boss.
            _restoreTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _timeStopped = true;
        }

        _pan = Boss5CameraPan.Begin();
        if (_pan != null)
        {
            SuppressAim(true);
            yield return _pan.PanTo(zonePos, s.panDuration);
            yield return new WaitForSecondsRealtime(s.holdOnZoneDuration);
            yield return _pan.PanBack(s.panDuration);
            _pan.End();
            _pan = null;
            SuppressAim(false);
        }

        ReleaseTimeStop();

        // Countdown starts AFTER the reveal — showing a timer draining during a
        // frozen cutscene would read as time the player was robbed of.
        StartCountdown();

        // ---- the actual race ------------------------------------------------
        float elapsed = 0f;
        while (elapsed < timeLimit && !EndRequested)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / timeLimit);
            if (_visual != null) _visual.SetProgress(progress);
            UpdateCountdown(elapsed, timeLimit);
            yield return null;
        }

        if (EndRequested) yield break;

        // ---- judgement 
        bool anyoneOutside = false;
        float r2 = s.zoneRadius * s.zoneRadius;
        foreach (var p in AlivePlayers())
        {
            if (p == null) continue;
            Vector3 d = p.transform.position - zonePos;
            d.z = 0f;
            if (d.sqrMagnitude > r2) { anyoneOutside = true; break; }
        }

        if (anyoneOutside) Boss.OnChallengeFailed(this);
        else if (_visual != null) _visual.PlaySuccessFlourish();

        // Let the success/failure read for a beat before the zone disappears.
        yield return new WaitForSeconds(0.5f);
    }

    /// Picks a reachable spot for the safe zone.
    /// The playfield is a DISC: TowerDefenseMap has a public `mapRadius` and puts a
    /// boundary collider on it, so a zone placed outside would be literally
    /// unreachable and the mechanic would be an unavoidable Kaboom. We therefore
    /// reject out-of-bounds candidates, reject anything sitting inside an obstacle,
    /// and only clamp to the boundary as a last resort.
    private Vector3 ChooseZonePosition(Vector3 anchor, Boss5SafeZoneSettings s)
    {
        var map = Boss5MapBounds.Map;
        Vector3 mapCenter = map != null ? map.transform.position : Vector3.zero;
        // Keep the whole circle inside the boundary, not just its centre.
        float limit = map != null
            ? Mathf.Max(0f, map.mapRadius - s.zoneRadius - s.mapEdgeMargin)
            : float.MaxValue;

        float min = Mathf.Min(s.minDistanceFromPlayer, s.maxDistanceFromPlayer);
        float max = Mathf.Max(s.minDistanceFromPlayer, s.maxDistanceFromPlayer);

        // Bias away from the boss so the zone is never "stand on the boss".
        Vector2 awayFromBoss = (Vector2)(anchor - Boss.transform.position);
        float baseAngle = awayFromBoss.sqrMagnitude > 0.01f
            ? Mathf.Atan2(awayFromBoss.y, awayFromBoss.x) * Mathf.Rad2Deg
            : Random.Range(0f, 360f);

        const int ATTEMPTS = 24;
        Vector3 fallback = anchor;

        for (int i = 0; i < ATTEMPTS; i++)
        {
            // Widen the arc and shorten the distance as attempts fail, so a player
            // pinned against the map edge still gets a valid zone.
            float spread = Mathf.Lerp(110f, 180f, i / (float)ATTEMPTS);
            float dist = Mathf.Lerp(Random.Range(min, max), min, i / (float)ATTEMPTS);

            float angle = (baseAngle + Random.Range(-spread, spread)) * Mathf.Deg2Rad;
            Vector3 pos = anchor + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * dist;
            pos.z = 0f;

            Vector3 off = pos - mapCenter;
            off.z = 0f;
            if (off.magnitude > limit)
            {
                // Remember a clamped version in case nothing better turns up.
                fallback = mapCenter + off.normalized * limit;
                fallback.z = 0f;
                continue;
            }

            if (Boss5MapBounds.OverlapsObstacle(pos, s.zoneRadius)) { fallback = pos; continue; }

            return pos;
        }

        return fallback;
    }

    /// Freeze each live player's aim for the duration of the pan.
    ///
    /// PlayerAim resolves its reticle with cam.ScreenToWorldPoint, so while the
    /// camera is parked over the safe zone the player's aim point would snap across
    /// the map — and PlayerAim deliberately runs on RAW input with no Time.deltaTime,
    /// so the timeScale=0 freeze does not stop it (see the comment on its Suppressed
    /// field).
    ///
    /// Uses the PER-INSTANCE SetSuppressed, never SetAllSuppressed: the global gate
    /// belongs to the Win/Lose screen, and clearing it here could un-freeze aim that
    /// something else wanted frozen. We also restore ONLY the components we
    /// suppressed. Downed players are not in PlayerAim.All (PlayerDownedState
    /// disables their PlayerAim, and OnDisable removes it from the list), so a revive
    /// cannot be affected either.
    private void SuppressAim(bool suppress)
    {
        if (suppress)
        {
            _suppressedAims.Clear();
            for (int i = 0; i < PlayerAim.All.Count; i++)
            {
                var aim = PlayerAim.All[i];
                if (aim == null) continue;
                aim.SetSuppressed(true);
                _suppressedAims.Add(aim);
            }
            return;
        }

        for (int i = 0; i < _suppressedAims.Count; i++)
            if (_suppressedAims[i] != null) _suppressedAims[i].SetSuppressed(false);
        _suppressedAims.Clear();
    }

    private void ReleaseTimeStop()
    {
        if (!_timeStopped) return;
        _timeStopped = false;
        Time.timeScale = _restoreTimeScale;
    }

    protected override void Cleanup()
    {
        // Order matters: release the freeze FIRST. If anything below threw, the game
        // would otherwise stay frozen at timeScale 0 with no way back.
        ReleaseTimeStop();
        SuppressAim(false);

        if (_pan != null) { _pan.End(); _pan = null; }
        if (_visual != null) { Object.Destroy(_visual.gameObject); _visual = null; }
    }
}



