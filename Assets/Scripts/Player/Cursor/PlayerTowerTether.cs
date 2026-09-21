using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;


// Creates an energetic tether between the player and EVERY tower within range.
// Each tether grants a buff to its tower based on that tower's individual distance:
//   FAR  zone (60% - 100% of maxTetherRange)  -> tower RANGE        bonus  (sniping)
//   MID  zone (30% - 60%  of maxTetherRange)  -> tower DAMAGE       bonus  (power)
//   NEAR zone (0%  - 30%  of maxTetherRange)  -> tower DECAY        reduction (defense)

[DisallowMultipleComponent]
public class PlayerTowerTether : MonoBehaviour
{
    [Header("Tether Range")]
    [Tooltip("Maximum distance at which a tether will form. Beyond this the tower is not tethered.")]
    public float maxTetherRange = 8f;

    [Tooltip("Once tethered, a tower stays tethered up to this multiplier of maxTetherRange. " +
             "Reduces flicker when the player walks near the edge.\n\n" +
             "This is the CONNECT/BREAK hysteresis band: a tether forms at maxTetherRange " +
             "and only breaks at maxTetherRange x this.")]
    public float breakRangeMultiplier = 1.15f;

    [Tooltip("Hysteresis on the NEAR/MID/FAR boundaries, as a fraction of maxTetherRange. " +
             "The zone you are currently in is widened by this much, so standing exactly on " +
             "a boundary doesn't strobe the colour and the buff between two zones. " +
             "0.04 with an 8-unit range = you must walk ~0.32 units past a boundary before " +
             "the zone flips. 0 = old behaviour (flips on the exact threshold).")]
    [Range(0f, 0.25f)] public float zoneHysteresis = 0.04f;

    [Tooltip("Maximum number of simultaneous tethers (safety cap).")]
    public int maxSimultaneousTethers = 8;

    [Header("Zone Thresholds (fraction of maxTetherRange)")]
    [Range(0.05f, 0.95f)] public float nearZoneEnd = 0.30f;
    [Range(0.05f, 0.95f)] public float midZoneEnd = 0.60f;

    [Header("Buff Strengths (scale with number of tethered towers)")]
    [Tooltip("Each connected tower adds this fraction to the FAR-zone range buff. " +
             "E.g. 0.12 with 4 tethers = +48% range. Effective multiplier = 1 + (this × tetherCount).\n\n" +
             "This was 0.05, which is +0.5 world units on a typical 10-unit tower — smaller " +
             "than the gap between two dots on the range ring, so the buff was invisible " +
             "even though it was working.")]
    public float farRangeBonusPerTether = 0.12f;

    [Tooltip("Whether the FAR-zone range buff also grows the tower's DETECTION collider.\n\n" +
             "It must, to do anything. Tower only ever considers enemies that entered its " +
             "trigger (OnTriggerEnter2D) or its OverlapCircle sweep, both sized from the " +
             "collider; ProjectileRange is only a filter applied to that set. Raising " +
             "ProjectileRange alone therefore bought nothing past the collider's built-in " +
             "+0.5 margin — the ring would grow but the tower still couldn't see further.\n\n" +
             "TRADE-OFF: a bigger trigger is also a bigger target for Physics2D queries that " +
             "hit triggers (e.g. boss lasers), so a heavily buffed tower can take hits from " +
             "further away. Turn this off to go back to a cosmetic-only range buff.")]
    public bool farBuffGrowsDetectionRange = true;

    [Tooltip("Each connected tower adds this fraction to the MID-zone damage buff. " +
             "E.g. 0.10 with 3 tethers = +30% damage. Effective multiplier = 1 + (this × tetherCount).")]
    public float midDamageBonusPerTether = 0.10f;

    [Tooltip("Each connected tower SUBTRACTS this fraction from the NEAR-zone decay multiplier. " +
             "E.g. 0.20 with 3 tethers = decay × 0.4 (60% slower). Effective multiplier = clamp(1 - (this × tetherCount), 0, 1).")]
    public float nearDecayReductionPerTether = 0.20f;

    [Tooltip("Cap on the total bonus applied to FAR/MID buffs from tether count. " +
             "E.g. 2.0 means the buff multiplier is capped at 3.0× (1 + 2.0). Set high to disable.")]
    public float maxBuffBonus = 2.0f;

    [Tooltip("Core energy decay multiplier applied while AT LEAST ONE tower is in NEAR zone " +
             "(0.7 = 30% slower decay). 1.0 disables. Applied exactly once regardless of how " +
             "many close-range tethers are active, so it doesn't compound.")]
    public float nearCoreDecayMultiplier = 0.7f;

    [Header("Bulk Supply (placement mode)")]
    [Tooltip("If true: while in placement mode and holding LMB, supply all tethered towers in parallel. " +
             "Player energy is consumed for each tower per supply tick. Single-target supply (clicking " +
             "directly on a tower) and tower placement (clicking on a slot) take priority and are not affected.")]
    public bool enableBulkSupply = true;

    [Tooltip("Verbose logging: prints to Console every bulk-supply tick AND whenever a tethered tower's " +
             "energy DROPS unexpectedly (i.e. by something other than this script). Use to diagnose 'towers " +
             "lose energy when player is hit' issues — if the drop is logged but bulk supply ISN'T running " +
             "in that frame, the cause is external to PlayerTowerTether.")]
    public bool debugLogTowerEnergyChanges = false;

    [Tooltip("Energy transfer rate per second per tethered tower. Conservation is strict: " +
             "every unit drained from the player is delivered to a tower (1:1). " +
             "Total player drain per second = this × number-of-towers-needing-energy. " +
             "Example: 10 with 4 damaged towers = the player loses 40/sec, each tower gains 10/sec.")]
    public float bulkSupplyEnergyPerSec = 5f;

    [Header("Update Cadence")]
    [Tooltip("How often (seconds) we re-scan for towers in range. Visual updates are per-frame.")]
    public float retargetInterval = 0.1f;

    [Tooltip("Buff strength scales with how many towers are tethered, so EVERY tethered " +
             "tower's buff changes the moment any ONE tether forms or breaks. Walking " +
             "around therefore made every tower's range twitch constantly.\n\n" +
             "Gaining a tether applies immediately (gaining power should feel instant), " +
             "but LOSING one waits this long before the count drops, so a tether that " +
             "blinks out and back doesn't ripple through every other tower. " +
             "0 = old behaviour.")]
    [Min(0f)] public float tetherCountSettleSeconds = 0.4f;

    [Header("Visual - Chain")]
    // Zone colours must be distinguishable AT A GLANCE, mid-fight, over grass. Gold vs
    // orange (the old far/mid pair) are neighbouring hues at similar brightness, so the
    // FAR buff looked like it never engaged. Violet / red-orange / cyan are three
    // clearly separate hues.
    public Color farColor = new Color(0.75f, 0.50f, 1.00f, 0.90f);  // violet   (range)
    public Color midColor = new Color(1.00f, 0.40f, 0.18f, 0.90f);  // red-orange (damage)
    public Color nearColor = new Color(0.30f, 0.90f, 1.00f, 0.90f); // cyan     (defense)
    [Tooltip("Color used while bulk-supplying energy to towers. Replaces zone color during supply.")]
    public Color supplyColor = new Color(0.3f, 0.6f, 1.0f, 1.0f); // electric blue
    [Tooltip("How fast the energy 'pulse' travels from player to tower along the chain during bulk supply. " +
             "World units per second.")]
    public float supplyFlowSpeed = 6f;
    [Tooltip("Width multiplier applied to the chain during bulk supply (makes it look more energetic).")]
    public float supplyWidthMultiplier = 1.5f;
    public float baseWidth = 0.05f;
    public float glowWidth = 0.12f;
    [Tooltip("Number of points sampled along each tether (more = smoother sway).")]
    [Range(2, 32)] public int chainSegments = 14;
    [Tooltip("Amplitude of the sine wobble that runs along the chain.")]
    public float wobbleAmplitude = 0.08f;
    [Tooltip("How fast the wobble travels along the chain.")]
    public float wobbleSpeed = 6f;
    [Tooltip("Anchor offset on the player (local space).")]
    public Vector2 playerAnchorOffset = new Vector2(0f, 0f);
    [Tooltip("Anchor offset on the tower (local space).")]
    public Vector2 towerAnchorOffset = new Vector2(0f, 0.2f);

    [Tooltip("Opacity units per second for the chain fading in when a tether forms and out " +
             "as it stretches into the break band. Stops tethers from popping into existence " +
             "at full brightness on the retarget tick.")]
    [Min(0.1f)] public float chainFadeSpeed = 4f;

    [Tooltip("How fast the chain colour blends when the zone changes (higher = snappier). " +
             "Without this the gold/orange/cyan swap is a hard cut mid-walk.")]
    [Min(0.1f)] public float zoneColorBlendSpeed = 6f;

    [Header("Y-Sort (must match GrassCartoonOverlay / PlayerMovement)")]
    public float sortPrecision = 10f;
    public int sortOrderBase = 1000;
    [Tooltip("Y offset applied to the tower position when computing the chain's sortingOrder. " +
             "Negative = sort from lower on the tower sprite.")]
    public float towerSortYOffset = -0.3f;
    [Tooltip("Y offset applied to the PLAYER position when computing the player-end sort anchor. " +
             "Should match PlayerMovement.sortYOffset (default -0.3).")]
    public float playerSortYOffset = -0.3f;
    [Tooltip("Subtracted from the chain's sortingOrder so it draws just BEHIND the foreground sprite " +
             "(player or tower, whichever is in front). Should be small (1-5).")]
    public int chainSortBias = 2;

    public enum TetherZone { None, Near, Mid, Far }

    // All state for a single active tether. Owns its own LineRenderers and boost helper.
    private class ActiveTether
    {
        public Tower tower;
        public TowerTetherBoost boost;
        public TetherZone zone = TetherZone.None;
        public GameObject visualRoot;
        public LineRenderer chainBase;
        public LineRenderer chainGlow;
        public bool contributesToCoreDecay;  // true while this tether is in NEAR zone
        public TowerTetherDecayBoost nearDecayBoost; // attached to tower while in NEAR zone
        public float debugLastEnergy;         // for damage-source diagnostic
        public bool debugLastEnergyValid;

        // Visual smoothing state. visualAlpha starts at 0 so a new tether fades in
        // instead of appearing at full strength on the next retarget tick.
        public float visualAlpha;
        public Color displayColor;
        public bool hasDisplayColor;
    }

    // Active tethers keyed by tower so we can dedupe trivially.
    private readonly Dictionary<Tower, ActiveTether> activeTethers = new Dictionary<Tower, ActiveTether>();

    // Reusable scratch buffers to avoid per-frame allocs.
    private readonly List<Tower> scratchInRange = new List<Tower>(16);
    private readonly List<Tower> scratchToRemove = new List<Tower>(8);

    private float retargetTimer;
    private float bulkCostAccumulator;
    private bool isBulkSupplying = false;

    // Per-player supply input (co-op). Bulk supply used to poll the global mouse, so
    // gamepad players couldn't bulk-supply. Now it reads THIS player's Build action
    // (left-click for mouse, left trigger for gamepad) and yields to this player's own
    // single-target supply via the sibling PlayerTowerPlacer.
    private UnityEngine.InputSystem.InputAction _buildAction;
    private PlayerTowerPlacer _placer;
    private bool _supplyInputResolved;

    private void ResolveSupplyInput()
    {
        if (_supplyInputResolved) return;
        _supplyInputResolved = true;

        var pi = GetComponent<UnityEngine.InputSystem.PlayerInput>()
                 ?? GetComponentInParent<UnityEngine.InputSystem.PlayerInput>();
        if (pi != null && pi.actions != null)
            _buildAction = pi.actions.FindAction("Build", false);

        _placer = GetComponent<PlayerTowerPlacer>() ?? GetComponentInParent<PlayerTowerPlacer>();
    }

    // Core decay aggregate state
    private int nearTetherCount = 0;
    private bool coreDecayHooked = false;

    // Settled tether count — see tetherCountSettleSeconds. Rises instantly, falls slowly.
    private int effectiveTetherCount = 0;
    private int pendingCount = -1;
    private float pendingSince;

    /// The count the buff formulas use. Never the raw dictionary size.
    private int EffectiveTetherCount => Mathf.Max(effectiveTetherCount, 0);

    /// Rise-fast / fall-slow debounce. Returns true if the effective count changed.
    private bool UpdateEffectiveCount()
    {
        int actual = activeTethers.Count;

        if (actual == effectiveTetherCount) { pendingCount = -1; return false; }

        // Gaining a tether is applied at once — a buff appearing late feels broken.
        if (actual > effectiveTetherCount || tetherCountSettleSeconds <= 0f)
        {
            effectiveTetherCount = actual;
            pendingCount = -1;
            return true;
        }

        // Losing one has to hold steady for the settle window first.
        if (actual != pendingCount) { pendingCount = actual; pendingSince = Time.time; }

        if (Time.time - pendingSince >= tetherCountSettleSeconds)
        {
            effectiveTetherCount = actual;
            pendingCount = -1;
            return true;
        }
        return false;
    }

    void OnDisable()
    {
        DetachAll();
    }

    void OnDestroy()
    {
        DetachAll();
    }

    void Update()
    {
        // The boost component is shared per tower and added at runtime, so it has no
        // inspector of its own — mirror the setting onto it each frame (a static write).
        TowerTetherBoost.GrowRangeCollider = farBuffGrowsDetectionRange;

        retargetTimer -= Time.deltaTime;
        if (retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            RescanTowers();
        }

        UpdateAllTethers();

        // Fold in any settled change to the tether count (a tether that dropped and
        // stayed dropped). Rises were already applied the moment they happened.
        if (UpdateEffectiveCount()) RecomputeAllBuffs();

        UpdateBulkSupply();
    }

    // Targeting: maintain the set of towers we are tethered to

    private void RescanTowers()
    {
        Vector3 me = transform.position;
        float maxSqr = maxTetherRange * maxTetherRange;
        float breakDist = maxTetherRange * breakRangeMultiplier;
        float breakSqr = breakDist * breakDist;

        // 1. Prune existing tethers whose tower is destroyed or out of break-range.
        scratchToRemove.Clear();
        foreach (var kvp in activeTethers)
        {
            Tower t = kvp.Key;
            if (t == null || t.IsDestroyed())
            {
                scratchToRemove.Add(t);
                continue;
            }
            float sqr = ((Vector2)(t.transform.position - me)).sqrMagnitude;
            if (sqr > breakSqr)
                scratchToRemove.Add(t);
        }
        foreach (var t in scratchToRemove)
            DetachOne(t);

        // 2. Find candidate towers within maxTetherRange.
        // Uses the shared Tower.ActiveTowers registry rather than FindObjectsByType,
        // which allocated a fresh array ten times a second per player.
        scratchInRange.Clear();
        var all = Tower.ActiveTowers;
        if (all == null) return;   // pruning above already ran; nothing left to scan
        foreach (var t in all)
        {
            if (t == null || !t.gameObject.activeInHierarchy || t.IsDestroyed()) continue;
            float sqr = ((Vector2)(t.transform.position - me)).sqrMagnitude;
            if (sqr <= maxSqr)
                scratchInRange.Add(t);
        }

        // Sort nearest-first so the cap picks the most relevant towers.
        scratchInRange.Sort((a, b) =>
        {
            float da = ((Vector2)(a.transform.position - me)).sqrMagnitude;
            float db = ((Vector2)(b.transform.position - me)).sqrMagnitude;
            return da.CompareTo(db);
        });

        // 3. Add new tethers up to the cap.
        int budget = Mathf.Max(0, maxSimultaneousTethers - activeTethers.Count);
        foreach (var t in scratchInRange)
        {
            if (activeTethers.ContainsKey(t)) continue;
            if (budget <= 0) break;
            AttachTo(t);
            budget--;
        }
    }

    // Per-frame buff + visual update for every active tether

    private void UpdateAllTethers()
    {
        if (activeTethers.Count == 0) return;

        Vector3 me = transform.position;

        // We may need to remove tethers mid-iteration if the tower was destroyed since last rescan.
        scratchToRemove.Clear();

        foreach (var kvp in activeTethers)
        {
            Tower t = kvp.Key;
            ActiveTether at = kvp.Value;

            if (t == null || t.IsDestroyed())
            {
                scratchToRemove.Add(t);
                continue;
            }

            float dist = Vector2.Distance(me, t.transform.position);

            // FIX: this used to call ComputeZone(dist), which returns None the instant
            // dist exceeds maxTetherRange. That ran every frame and dropped the tether
            // at exactly 1.0x range, so breakRangeMultiplier's hysteresis band never
            // got a chance to hold the tether: RescanTowers would re-attach it on the
            // next 0.1s tick, the next frame would drop it again, and walking along the
            // edge produced a chain that flickered on a ~0.1s beat. Passing the CURRENT
            // zone lets ComputeZone apply the break band, which is what it was for.
            TetherZone newZone = ComputeZone(dist, at.zone);

            if (newZone == TetherZone.None)
            {
                // Past the break range — drop it. Rescan would also catch this, but this
                // keeps it instant. The chain has already faded out across the band.
                scratchToRemove.Add(t);
                continue;
            }

            if (newZone != at.zone)
                ApplyZone(at, newZone);

            // Diagnostic: detect external tower energy drops while tethered.
            if (debugLogTowerEnergyChanges)
            {
                float now = t.GetEnergy();
                if (at.debugLastEnergyValid)
                {
                    float delta = now - at.debugLastEnergy;
                    // Threshold: 0.5 energy. Decay is ~0.07 per tick at default settings, so
                    // anything bigger likely indicates damage (or a config tweak).
                    if (delta < -0.5f)
                    {
                        /*
                        Debug.LogWarning(
                            $"[Tether-Diag] Tower '{t.towerName}' lost {-delta:F2} energy in {Time.deltaTime * 1000f:F1}ms. " +
                            $"Energy: {at.debugLastEnergy:F1} -> {now:F1}. " +
                            $"isBulkSupplying={isBulkSupplying} (if false, the drop is NOT caused by this script).");
                            */
                    }
                }
                at.debugLastEnergy = now;
                at.debugLastEnergyValid = true;
            }
            else
            {
                at.debugLastEnergyValid = false;
            }

            UpdateChainVisual(at);
        }

        foreach (var t in scratchToRemove)
            DetachOne(t);
    }

    // Bulk supply: while in placement mode and holding LMB, continuously supply every
    // tethered tower in parallel. Single-target supply (clicking on a specific tower) and
    // tower placement (clicking on a slot) take priority — we skip bulk supply whenever
    // EnergyManager.isContinuouslySupplying is true.
    //
    // Implementation: each frame we compute (rate × deltaTime) per tower, supply that
    // (smooth, sub-integer amounts are fine — Tower.SupplyEnergy accepts floats). The
    // player's integer energy gauge is debited whenever the accumulated cost crosses a
    // whole unit. This produces a visibly continuous stream rather than discrete jumps.
    private void UpdateBulkSupply()
    {
        // Reset visual flag each frame; we'll re-set it below if bulk supply is active.
        isBulkSupplying = false;

        if (!enableBulkSupply) return;
        if (activeTethers.Count == 0) return;

        var em = EnergyManager.Instance;
        if (em == null) return;

        // Only active in placement mode.
        var pm = TowerPlacementManager.Instance;
        if (pm == null || !pm.IsInPlacementMode()) return;

        ResolveSupplyInput();

        // Yield to THIS player's single-target supply (aiming directly at a tower/core).
        // Falls back to the legacy global flag if there's no sibling placer.
        if (_placer != null) { if (_placer.IsSingleSupplying) return; }
        else if (em.isContinuouslySupplying) return;

        // THIS player's Build action must be held (left-click for mouse, left trigger
        // for gamepad). Replaces the old global-mouse check so gamepad players — and
        // both players in gamepad+gamepad co-op — can bulk-supply. Falls back to the
        // global mouse only for a legacy object with no Build action.
        bool held = _buildAction != null
            ? _buildAction.IsPressed()
            : (Mouse.current != null && Mouse.current.leftButton.isPressed);
        if (!held) return;

        // We are now actively in bulk-supply state (drives the electric-blue visual).
        isBulkSupplying = true;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // This frame's per-tower transfer amount. Strict 1:1 conservation: this same amount
        // is both drained from the player and delivered to the tower.
        float perTowerThisFrame = bulkSupplyEnergyPerSec * dt;

        foreach (var kvp in activeTethers)
        {
            var at = kvp.Value;
            if (at == null || at.tower == null || at.tower.IsDestroyed()) continue;

            var consumer = at.tower as IEnergyConsumer;
            if (consumer == null) continue;

            float deficit = consumer.GetMaxEnergy() - consumer.GetEnergy();
            if (deficit <= 0f) continue;

            // Cap delivery at the deficit — no overshoot, and we don't drain player energy
            // for energy that wouldn't actually land on the tower.
            float transfer = Mathf.Min(perTowerThisFrame, deficit);

            // Accumulate the fractional cost; debit the player whenever it crosses an integer.
            // (Player energy is int-typed in EnergyManager, but towers accept float supply.)
            bulkCostAccumulator += transfer;
            int wholeCost = (int)bulkCostAccumulator;

            if (wholeCost > 0)
            {
                if (em.TrySpendPlayerEnergy(wholeCost))
                {
                    bulkCostAccumulator -= wholeCost;
                }
                else
                {
                    // Player can't afford. Reset accumulator and stop supplying this frame.
                    bulkCostAccumulator = 0f;
                    break;
                }
            }

            consumer.SupplyEnergy(transfer);
        }
    }

    /// Zone for `distance`, given the zone this tether is ALREADY in.
    ///
    /// Two bands of hysteresis, both driven by `current`:
    ///
    ///   1. CONNECT/BREAK. A tether forms at maxTetherRange but is only dropped past
    ///      maxTetherRange x breakRangeMultiplier. Passing current == None (a tower we
    ///      are not tethered to yet) uses the tighter connect range, so the band can't
    ///      be used to reach further than intended.
    ///
    ///   2. ZONE BOUNDARIES. The band we're currently in is widened by zoneHysteresis
    ///      on the side(s) it can be left from, so hovering on a threshold can't
    ///      strobe the buff and the chain colour frame to frame.
    private TetherZone ComputeZone(float distance, TetherZone current)
    {
        bool alreadyTethered = current != TetherZone.None;
        float dropRange = alreadyTethered
            ? maxTetherRange * Mathf.Max(1f, breakRangeMultiplier)
            : maxTetherRange;

        if (distance > dropRange) return TetherZone.None;

        float frac = distance / Mathf.Max(0.0001f, maxTetherRange);

        float nearEnd = nearZoneEnd;
        float midEnd = midZoneEnd;
        float h = Mathf.Max(0f, zoneHysteresis);

        switch (current)
        {
            case TetherZone.Near: nearEnd += h; break;                 // harder to leave Near outward
            case TetherZone.Mid: nearEnd -= h; midEnd += h; break;     // harder to leave Mid either way
            case TetherZone.Far: midEnd -= h; break;                   // harder to leave Far inward
        }

        // A big hysteresis with tight thresholds could invert the boundaries.
        if (midEnd < nearEnd) midEnd = nearEnd;

        if (frac <= nearEnd) return TetherZone.Near;
        if (frac <= midEnd) return TetherZone.Mid;
        return TetherZone.Far;
    }


    // Diagnostics

    /// Live state for one tower, for the telemetry log. False if not tethered.
    public bool TryGetTetherState(Tower t, out string zone, out float distance, out float rangeMultiplier)
    {
        zone = "None"; distance = -1f; rangeMultiplier = 1f;
        if (t == null || !activeTethers.TryGetValue(t, out var at)) return false;

        zone = at.zone.ToString();
        distance = Vector2.Distance(transform.position, t.transform.position);
        rangeMultiplier = ComputeRangeMultiplier(EffectiveTetherCount);
        return true;
    }

    /// One compact line of this player's tether state for the telemetry block.
    public string TelemetryLine()
    {
        return $"tether: active={activeTethers.Count} effectiveCount={EffectiveTetherCount} " +
               $"maxRange={maxTetherRange:F1} breakAt={maxTetherRange * breakRangeMultiplier:F1} " +
               $"zones[near<={nearZoneEnd:F2} mid<={midZoneEnd:F2}] hyst={zoneHysteresis:F2} " +
               $"FARmult=x{ComputeRangeMultiplier(EffectiveTetherCount):F3} " +
               $"growCollider={farBuffGrowsDetectionRange}";
    }

    /// Dump the live numbers for every tether: distance, zone, the multiplier being
    /// applied, and the tower's resulting reach. Use this rather than judging the buff
    /// by eye — a +5% range change is smaller than the gap between two dots on the
    /// range ring. Wire it to a key:
    ///     if (Input.GetKeyDown(KeyCode.F10)) GetComponent<PlayerTowerTether>().LogTetherState();
    [ContextMenu("Log Tether State")]
    public void LogTetherState()
    {
        if (activeTethers.Count == 0)
        {
            Debug.Log($"[Tether] No active tethers (maxTetherRange={maxTetherRange}).");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"[Tether] tethers={activeTethers.Count} effectiveCount={EffectiveTetherCount} ");
        sb.Append($"growCollider={farBuffGrowsDetectionRange}\n");
        sb.Append($"  multipliers at this count: FAR range x{ComputeRangeMultiplier(EffectiveTetherCount):F3}, ");
        sb.Append($"MID damage x{ComputeDamageMultiplier(EffectiveTetherCount):F3}, ");
        sb.Append($"NEAR decay x{ComputeDecayMultiplier(EffectiveTetherCount):F3}");

        Vector3 me = transform.position;
        foreach (var kvp in activeTethers)
        {
            var t = kvp.Key;
            var at = kvp.Value;
            if (t == null) continue;

            float d = Vector2.Distance(me, t.transform.position);
            sb.Append($"\n  '{t.towerName}' dist={d:F2} ({d / Mathf.Max(0.0001f, maxTetherRange) * 100f:F0}% of range) ");
            sb.Append($"zone={at.zone} ProjectileRange={t.ProjectileRange:F2} ");
            sb.Append($"colliderReach={t.RangeColliderWorldRadius:F2} damage={t.GetDamage():F1}");

            // The tower can only ever shoot what it DETECTS, so this is the number that
            // decides whether the FAR buff did anything at all.
            if (t.RangeColliderWorldRadius < t.ProjectileRange - 0.01f)
                sb.Append("  <-- collider is SMALLER than ProjectileRange: the extra reach is dead");
        }

        Debug.Log(sb.ToString());
    }

    // Attach / detach plumbing

    private void AttachTo(Tower tower)
    {
        if (tower == null || tower.IsDestroyed()) return;

        // CO-OP: the boost component is shared per tower (one instance), and each player
        // registers its own keyed contribution. We must NOT destroy an existing boost here —
        // doing so would wipe the OTHER player's contribution and corrupt the tower's base
        // stat. GetOrCreate returns the existing shared boost if another player already made one.
        var at = new ActiveTether
        {
            tower = tower,
            boost = TowerTetherBoost.GetOrCreate(tower),
            zone = TetherZone.None,
        };
        BuildChainVisualsFor(at);
        activeTethers[tower] = at;

        // Tether count just increased — rises apply immediately, so push it through now
        // and re-apply existing tethers with the new count-scaled buff.
        UpdateEffectiveCount();
        RecomputeAllBuffs();
    }

    private void DetachOne(Tower tower)
    {
        if (!activeTethers.TryGetValue(tower, out var at)) return;
        ReleaseTether(at);
        activeTethers.Remove(tower);

        // Deliberately NOT recomputing here. The count only drops after
        // tetherCountSettleSeconds of staying dropped (see UpdateEffectiveCount), so a
        // tether that blinks out and back can't ripple a buff change through every
        // other tower on the board.
    }

    private void DetachAll()
    {
        foreach (var kvp in activeTethers)
            ReleaseTether(kvp.Value);
        activeTethers.Clear();

        effectiveTetherCount = 0;
        pendingCount = -1;

        // Make sure the core decay multiplier is fully unwound even if counts got out of sync.
        if (coreDecayHooked)
        {
            UnapplyCoreDecayMultiplier();
            nearTetherCount = 0;
        }
    }

    //Restore stat, destroy boost, destroy visuals, update core-decay aggregate.
    private void ReleaseTether(ActiveTether at)
    {
        if (at == null) return;

        // Remove core-decay contribution if this tether was in NEAR zone.
        if (at.contributesToCoreDecay)
        {
            at.contributesToCoreDecay = false;
            DecrementNearCount();
        }

        if (at.boost != null)
        {
            // Remove ONLY this tether's contribution. The shared boost restores the tower's
            // true base and self-destructs once its last contributor (the other player) leaves.
            at.boost.ClearContribution(at);
            at.boost = null;
        }

        if (at.nearDecayBoost != null)
        {
            at.nearDecayBoost.ClearContribution(at);
            at.nearDecayBoost = null;
        }

        if (at.visualRoot != null)
        {
            Destroy(at.visualRoot);
            at.visualRoot = null;
        }
    }

    //Restore the tower stat and apply the buff matching the new zone.
    private void ApplyZone(ActiveTether at, TetherZone newZone)
    {
        // Update core-decay aggregate
        bool wasNear = at.contributesToCoreDecay;
        bool isNear = newZone == TetherZone.Near;
        if (wasNear && !isNear) { at.contributesToCoreDecay = false; DecrementNearCount(); }
        else if (!wasNear && isNear) { at.contributesToCoreDecay = true; IncrementNearCount(); }

        int tetherCount = EffectiveTetherCount;

        // Each tether contributes to AT MOST one boost type at a time (its current zone).
        // We Set the contribution for the new zone and Clear the boost type(s) we no longer use.
        // The aggregator captures the tower's true base once and applies the PRODUCT of all
        // players' contributions, so two players stack correctly and the base restores cleanly.
        switch (newZone)
        {
            case TetherZone.Far:
                if (at.nearDecayBoost != null) { at.nearDecayBoost.ClearContribution(at); at.nearDecayBoost = null; }
                at.boost = TowerTetherBoost.GetOrCreate(at.tower);
                at.boost?.SetRangeContribution(at, ComputeRangeMultiplier(tetherCount)); // also drops this key's damage contribution
                break;

            case TetherZone.Mid:
                if (at.nearDecayBoost != null) { at.nearDecayBoost.ClearContribution(at); at.nearDecayBoost = null; }
                at.boost = TowerTetherBoost.GetOrCreate(at.tower);
                at.boost?.SetDamageContribution(at, ComputeDamageMultiplier(tetherCount)); // also drops this key's range contribution
                break;

            case TetherZone.Near:
                // NEAR uses the decay boost, not the damage/range boost — drop the latter contribution.
                if (at.boost != null) { at.boost.ClearContribution(at); at.boost = null; }
                // Shared decay aggregator (EnergyManager reads GetDecayMultiplier()). Composes with
                // TowerCommanderBoost / GeneratorProximityBoost, and now with the OTHER player's tether.
                at.nearDecayBoost = TowerTetherDecayBoost.GetOrCreate(at.tower);
                at.nearDecayBoost?.SetContribution(at, ComputeDecayMultiplier(tetherCount));
                break;

            case TetherZone.None:
                // No buff zone — withdraw this tether's contributions entirely.
                if (at.boost != null) { at.boost.ClearContribution(at); at.boost = null; }
                if (at.nearDecayBoost != null) { at.nearDecayBoost.ClearContribution(at); at.nearDecayBoost = null; }
                break;
        }

        at.zone = newZone;
    }

    // Buff scaling helpers — buff strength scales with the total number of currently tethered towers.
    // Formulas live in the pure, static TetherMath class (bottom of file) so they can be
    // unit-tested without a scene. These instance methods just feed in this player's
    // serialized tuning values. Behavior is byte-identical to the original inline math.
    private float ComputeRangeMultiplier(int count)
        => TetherMath.RangeMultiplier(count, farRangeBonusPerTether, maxBuffBonus);

    private float ComputeDamageMultiplier(int count)
        => TetherMath.DamageMultiplier(count, midDamageBonusPerTether, maxBuffBonus);

    private float ComputeDecayMultiplier(int count)
        => TetherMath.DecayMultiplier(count, nearDecayReductionPerTether);

    // Re-apply every active tether's current-zone buff using the up-to-date tether count.
    // Called whenever tethers are added or removed so existing tethers reflect the new count.
    private void RecomputeAllBuffs()
    {
        if (activeTethers.Count == 0) return;
        int count = EffectiveTetherCount;

        foreach (var kvp in activeTethers)
        {
            var at = kvp.Value;
            if (at == null || at.tower == null || at.tower.IsDestroyed()) continue;

            switch (at.zone)
            {
                case TetherZone.Far:
                    at.boost = TowerTetherBoost.GetOrCreate(at.tower);
                    at.boost?.SetRangeContribution(at, ComputeRangeMultiplier(count));
                    break;
                case TetherZone.Mid:
                    at.boost = TowerTetherBoost.GetOrCreate(at.tower);
                    at.boost?.SetDamageContribution(at, ComputeDamageMultiplier(count));
                    break;
                case TetherZone.Near:
                    at.nearDecayBoost = TowerTetherDecayBoost.GetOrCreate(at.tower);
                    at.nearDecayBoost?.SetContribution(at, ComputeDecayMultiplier(count));
                    break;
            }
        }
    }

    // Core decay aggregate (apply once regardless of how many NEAR tethers exist)

    private void IncrementNearCount()
    {
        nearTetherCount++;
        if (nearTetherCount == 1)
            ApplyCoreDecayMultiplier();
    }

    private void DecrementNearCount()
    {
        nearTetherCount = Mathf.Max(0, nearTetherCount - 1);
        if (nearTetherCount == 0)
            UnapplyCoreDecayMultiplier();
    }

    private void ApplyCoreDecayMultiplier()
    {
        if (coreDecayHooked) return;
        if (EnergyManager.Instance == null) return;
        if (Mathf.Approximately(nearCoreDecayMultiplier, 1f)) return;
        // Multiplicative composition: if CoreRepairSystems is regenerating (negative rate),
        // sign is preserved and the magnitude is dampened.
        EnergyManager.Instance.coreEnergyDecayRate *= nearCoreDecayMultiplier;
        coreDecayHooked = true;
    }

    private void UnapplyCoreDecayMultiplier()
    {
        if (!coreDecayHooked) return;
        if (EnergyManager.Instance != null && !Mathf.Approximately(nearCoreDecayMultiplier, 0f))
            EnergyManager.Instance.coreEnergyDecayRate /= nearCoreDecayMultiplier;
        coreDecayHooked = false;
    }

    // Chain visuals

    private void BuildChainVisualsFor(ActiveTether at)
    {
        at.visualRoot = new GameObject($"Tether_{(at.tower != null ? at.tower.name : "?")}");
        at.visualRoot.transform.SetParent(transform, false);

        var glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(at.visualRoot.transform, false);
        at.chainGlow = glowGO.AddComponent<LineRenderer>();
        ConfigureLine(at.chainGlow, glowWidth);

        var baseGO = new GameObject("Base");
        baseGO.transform.SetParent(at.visualRoot.transform, false);
        at.chainBase = baseGO.AddComponent<LineRenderer>();
        ConfigureLine(at.chainBase, baseWidth);

        at.visualRoot.SetActive(false);
    }

    private void ConfigureLine(LineRenderer lr, float width)
    {
        lr.useWorldSpace = true;
        lr.positionCount = chainSegments;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 2;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.sortingLayerName = "Default";
        lr.receiveShadows = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void UpdateChainVisual(ActiveTether at)
    {
        if (at.visualRoot == null) return;

        if (at.tower == null || at.zone == TetherZone.None)
        {
            if (at.visualRoot.activeSelf) at.visualRoot.SetActive(false);
            return;
        }

        if (!at.visualRoot.activeSelf) at.visualRoot.SetActive(true);

        Vector3 a = transform.position + (Vector3)playerAnchorOffset;
        Vector3 b = at.tower.transform.position + (Vector3)towerAnchorOffset;

        // Per-tether phase offset so multiple tethers don't pulse / wobble in lockstep.
        float phase = at.tower.GetInstanceID() * 0.37f;

        // ── Edge fade ───────────────────────────────────────────────────────
        // Full strength inside maxTetherRange, ramping to zero across the break band.
        // Together with visualAlpha starting at 0, both ends of a tether's life are a
        // fade rather than a pop: forming one eases in over a few frames, and one
        // about to break dims out as you walk away instead of cutting mid-stride.
        float dist = Vector2.Distance(transform.position, at.tower.transform.position);
        float breakRange = maxTetherRange * Mathf.Max(1f, breakRangeMultiplier);
        float edge = breakRange > maxTetherRange + 0.001f
            ? Mathf.Clamp01(Mathf.InverseLerp(breakRange, maxTetherRange, dist))
            : 1f;
        at.visualAlpha = Mathf.MoveTowards(at.visualAlpha, edge, chainFadeSpeed * Time.deltaTime);

        // ── Colour ──────────────────────────────────────────────────────────
        // Blended toward the zone's colour instead of assigned, so crossing a zone
        // boundary reads as the chain shifting hue over ~0.2s rather than a hard cut.
        Color zoneColor = at.zone switch
        {
            TetherZone.Far => farColor,
            TetherZone.Mid => midColor,
            TetherZone.Near => nearColor,
            _ => Color.white,
        };
        Color targetColor = isBulkSupplying ? supplyColor : zoneColor;

        if (!at.hasDisplayColor) { at.displayColor = targetColor; at.hasDisplayColor = true; }
        // Exponential blend: frame-rate independent, unlike a raw Lerp with a fixed t.
        at.displayColor = Color.Lerp(at.displayColor, targetColor,
                                     1f - Mathf.Exp(-zoneColorBlendSpeed * Time.deltaTime));

        float pulse = isBulkSupplying
            ? 0.7f + Mathf.Sin(Time.time * 8f + phase) * 0.3f    // strong pulse during supply
            : 0.85f + Mathf.Sin(Time.time * 4f + phase) * 0.15f;
        float widthMul = isBulkSupplying ? supplyWidthMultiplier : 1f;
        float glowFactor = isBulkSupplying ? 0.5f : 0.45f;

        Color tint = at.displayColor;
        Color baseTint = tint; baseTint.a = tint.a * pulse * at.visualAlpha;
        Color glowTint = tint; glowTint.a = tint.a * glowFactor * pulse * at.visualAlpha;

        // Set start/end colors.
        at.chainBase.startColor = baseTint;
        at.chainBase.endColor = baseTint;
        at.chainGlow.startColor = glowTint;
        at.chainGlow.endColor = glowTint;

        // Set widths.
        at.chainBase.startWidth = baseWidth * widthMul;
        at.chainBase.endWidth = baseWidth * widthMul;
        at.chainGlow.startWidth = glowWidth * widthMul;
        at.chainGlow.endWidth = glowWidth * widthMul;

        // While supplying, install a traveling-pulse gradient on the base line:
        // bright bands move from the player end (key 0) to the tower end (key 1), creating
        // the visual impression of energy flowing into the tower.
        if (isBulkSupplying)
        {
            ApplyFlowingPulseGradient(at.chainBase, baseTint, phase, fromPlayerEnd: true);
        }
        else if (at.chainBase.colorGradient.alphaKeys.Length > 2)
        {
            // Reset to plain gradient (start->end same color) so we don't leak supply visuals
            // into the next non-supply frame.
            ResetChainGradient(at.chainBase, baseTint);
        }

        // Build chain points.
        Vector3 dir = b - a;
        float len = dir.magnitude;
        if (len < 0.001f)
        {
            for (int i = 0; i < chainSegments; i++)
            {
                at.chainBase.SetPosition(i, a);
                at.chainGlow.SetPosition(i, a);
            }
        }
        else
        {
            Vector3 fwd = dir / len;
            Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);

            float t = Time.time * wobbleSpeed + phase;
            // During supply, tame the perpendicular wobble — the visual interest comes from
            // the longitudinal pulse instead, and a steadier line reads as "carrying current."
            float wobbleScale = isBulkSupplying ? 0.3f : 1f;

            for (int i = 0; i < chainSegments; i++)
            {
                float u = (chainSegments == 1) ? 0f : (float)i / (chainSegments - 1);
                float taper = Mathf.Sin(u * Mathf.PI);  // zero at endpoints
                float wob = Mathf.Sin(u * Mathf.PI * 4f - t) * wobbleAmplitude * taper * wobbleScale;

                Vector3 p = Vector3.Lerp(a, b, u) + perp * wob;
                at.chainBase.SetPosition(i, p);
                at.chainGlow.SetPosition(i, p);
            }
        }

        // Y-sort
        // Sort-order formula matches PlayerMovement / YSortEntity / GrassCartoonOverlay:
        //     order = sortOrderBase + round(-(y + offset) * sortPrecision)
        // Lower world-Y -> larger -y -> HIGHER order -> rendered on TOP (foreground).
        float towerSortY = at.tower.transform.position.y + towerSortYOffset;
        float playerSortY = transform.position.y + playerSortYOffset;
        int towerOrder = sortOrderBase + Mathf.RoundToInt(-towerSortY * sortPrecision);
        int playerOrder = sortOrderBase + Mathf.RoundToInt(-playerSortY * sortPrecision);
        int anchorOrder = Mathf.Min(towerOrder, playerOrder); // background sprite
        int baseOrder = anchorOrder - chainSortBias;       // base layer behind both sprites
        int glowOrder = anchorOrder - chainSortBias - 1;   // glow one step further back
        at.chainBase.sortingOrder = baseOrder;
        at.chainGlow.sortingOrder = glowOrder;
    }

    // Builds a Gradient with bright bands that scroll from one end to the other.
    // Used during bulk supply to suggest energy flowing player -> tower.
    private void ApplyFlowingPulseGradient(LineRenderer lr, Color baseColor, float phase, bool fromPlayerEnd)
    {
        // Scroll position 0..1 representing where the brightest pulse currently sits along the line.
        // Speed is in "fractions of the line per second" so we normalize by an estimated typical length.
        float scroll = (Time.time * supplyFlowSpeed * 0.15f + phase) % 1f;
        if (!fromPlayerEnd) scroll = 1f - scroll;

        var gradient = new Gradient();

        // Color keys: solid color across the whole line.
        var ck = new GradientColorKey[2];
        ck[0] = new GradientColorKey(baseColor, 0f);
        ck[1] = new GradientColorKey(baseColor, 1f);

        // Alpha keys: dim background + a brighter pulse that moves with `scroll`.
        // We place a pulse "peak" at `scroll` and let it fall off either side.
        // We also add a second pulse offset by 0.5 so there's always one in view.
        const float dim = 0.35f;
        const float peakWidth = 0.18f;
        float a = baseColor.a;

        float p1 = scroll;
        float p2 = (scroll + 0.5f) % 1f;

        var ak = new System.Collections.Generic.List<GradientAlphaKey>(8);
        ak.Add(new GradientAlphaKey(dim * a, 0f));
        AddPulseKeys(ak, p1, peakWidth, dim * a, a);
        AddPulseKeys(ak, p2, peakWidth, dim * a, a);
        ak.Add(new GradientAlphaKey(dim * a, 1f));

        // Sort by time so Unity is happy.
        ak.Sort((x, y) => x.time.CompareTo(y.time));
        // Unity allows max 8 alpha keys; trim if needed.
        while (ak.Count > 8) ak.RemoveAt(ak.Count - 1);

        gradient.SetKeys(ck, ak.ToArray());
        lr.colorGradient = gradient;
    }

    private static void AddPulseKeys(System.Collections.Generic.List<GradientAlphaKey> keys,
        float center, float halfWidth, float dimAlpha, float peakAlpha)
    {
        float left = center - halfWidth;
        float right = center + halfWidth;

        if (left >= 0f && left <= 1f) keys.Add(new GradientAlphaKey(dimAlpha, left));
        if (center >= 0f && center <= 1f) keys.Add(new GradientAlphaKey(peakAlpha, center));
        if (right >= 0f && right <= 1f) keys.Add(new GradientAlphaKey(dimAlpha, right));
    }

    private static void ResetChainGradient(LineRenderer lr, Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, 1f) }
        );
        lr.colorGradient = gradient;
    }
}


/// Shared, per-TOWER aggregator (ONE instance per tower, regardless of how many players
/// tether it). Each contributor (one player's tether to this tower) registers a keyed
/// multiplier; the tower stat = trueBase × PRODUCT(all contributions). The true base is
/// captured lazily the first time a contribution of that kind is added, and restored when
/// the last contributor leaves (then the component self-destructs). This is what lets two
/// players' tether buffs STACK correctly on the same tower instead of clobbering each other.
/// Single player (one contributor) is byte-identical to the old snapshot/apply behavior.
// Runs its re-apply pass BEFORE other LateUpdates (TowerRangeIndicator is at the
// default 0), so by the time anything reads ProjectileRange in the same frame, an
// external overwrite has already been corrected.
[DefaultExecutionOrder(-100)]
public class TowerTetherBoost : MonoBehaviour
{
    private Tower tower;

    // True base stats, captured lazily (before any contribution of that kind is live).
    private float baseDamage;
    private bool hasBaseDamage;
    private float baseProjectileRange;
    private bool hasBaseRange;

    // The trigger radius that went with baseProjectileRange. Scaled by the same product
    // so the buff actually extends what the tower can DETECT, not just what it filters.
    private float baseColliderWorldRadius;

    /// Mirrored from PlayerTowerTether.farBuffGrowsDetectionRange.
    public static bool GrowRangeCollider = true;

    /// Log whenever an external system overwrites a stat we are buffing. Leave on while
    /// hunting down whatever is writing Tower.range.
    public static bool LogAdoptions = true;

    // Per-contributor multipliers. A given tether contributes EITHER damage OR range,
    // depending on its current zone — never both at once.
    private readonly Dictionary<object, float> damageContribs = new Dictionary<object, float>();
    private readonly Dictionary<object, float> rangeContribs = new Dictionary<object, float>();

    // Cached reflection handles for Tower's private range plumbing.
    private static System.Reflection.MethodInfo s_projectileRangeSetter;
    private static bool s_projectileRangeReflectionInit;
    private static System.Reflection.MethodInfo s_colliderRadiusSetter;
    private static bool s_colliderReflectionInit;

    // Last values WE wrote. Anything else on the tower is an external writer.
    private float lastAppliedDamage;
    private float lastAppliedRange;
    private bool hasAppliedDamage;
    private bool hasAppliedRange;

    /// EXTERNAL-WRITER DETECTION. This is what made the buff behave erratically.
    ///
    /// Tower.range is a PROPERTY whose setter does `ProjectileRange = _range` — note it
    /// assigns the RAW range, while SetupTower assigns
    /// `max(range * 2, tentacleReach * 3.5, 6)`. So anything that touches tower.range
    /// (an augment, the JSON config reload, SetupTower2 on a rebuild) both wipes our
    /// buff AND can roughly halve ProjectileRange outright.
    ///
    /// Worse, `baseProjectileRange` was captured once and trusted forever. If an
    /// external write landed while a contribution was live, the captured base no longer
    /// matched reality, and when the last contributor left we "restored" a number that
    /// was never the tower's true base — the range ratcheted. That is exactly the
    /// "sometimes the range changes, sometimes it doesn't" symptom. Nothing is cached in
    /// Unity; the stale base lived in this component.
    ///
    /// Now: if the live value drifts from what we last wrote, adopt it as the new base
    /// and re-apply our product on top. Composes with every other writer instead of
    /// fighting them.
    void LateUpdate()
    {
        // Nothing applied = nothing to defend against an external writer.
        if (!hasAppliedDamage && !hasAppliedRange) return;
        if (tower == null) { tower = GetComponent<Tower>(); if (tower == null) return; }
        if (tower.IsDestroyed()) return;

        bool reapply = false;

        if (hasBaseDamage && hasAppliedDamage &&
            Mathf.Abs(tower.GetDamage() - lastAppliedDamage) > 0.0001f)
        {
            // Adopt the observed value AS THE NEW BASE — do not divide our multiplier out.
            // See the note below: external writers write unbuffed numbers.
            if (LogAdoptions)
                Debug.Log($"[Tether] '{tower.towerName}' damage changed externally " +
                          $"{lastAppliedDamage:F2} -> {tower.GetDamage():F2}; adopting as new base.");
            baseDamage = tower.GetDamage();
            reapply = true;
        }

        if (hasBaseRange && hasAppliedRange &&
            Mathf.Abs(tower.ProjectileRange - lastAppliedRange) > 0.0001f)
        {
            // FIX (the downward ratchet): this used to do `base = observed / ourMultiplier`,
            // which assumes the external writer's number already contained our buff. It
            // never does. Tower.range's setter writes a RAW value and SetupTower re-derives
            // one, both unaware that any buff exists.
            //
            // With a x1.2 buff live, an external write of 5.0 gave base = 4.167, we then
            // re-applied 4.167 x 1.2 = 5.0 (so the buff silently did nothing), and when the
            // tether finally released we "restored" 4.167 — permanently shrinking the tower.
            // Repeat per pass and the range walks downward: 10 -> 5 -> 4.167 -> 3.571.
            if (LogAdoptions)
                Debug.Log($"[Tether] '{tower.towerName}' ProjectileRange changed externally " +
                          $"{lastAppliedRange:F3} -> {tower.ProjectileRange:F3}; adopting as new base " +
                          $"(our multiplier x{ProductOf(rangeContribs):F3} will be re-applied on top).");

            baseProjectileRange = tower.ProjectileRange;

            // The same writer resized the collider, so re-capture that too or we would
            // scale a stale radius.
            baseColliderWorldRadius = tower.RangeColliderWorldRadius;
            reapply = true;
        }

        if (reapply) Recompute();
    }

    private static float ProductOf(Dictionary<object, float> d)
    {
        float p = 1f;
        foreach (var kv in d) p *= kv.Value;
        return p;
    }

    // ── Read-only diagnostics ───────────────────────────────────────────────
    // So TowerRangeIndicator (and any future HUD) can say WHY a tower's range moved
    // instead of just reporting that it did.

    /// Combined range multiplier currently applied by all contributors. 1 = none.
    public float RangeMultiplier => rangeContribs.Count > 0 ? ProductOf(rangeContribs) : 1f;

    /// Combined damage multiplier currently applied by all contributors. 1 = none.
    public float DamageMultiplier => damageContribs.Count > 0 ? ProductOf(damageContribs) : 1f;

    /// The tower's unbuffed ProjectileRange, or -1 when we hold no range contribution.
    public float BaseProjectileRange => hasBaseRange ? baseProjectileRange : -1f;

    public int RangeContributorCount => rangeContribs.Count;
    public int DamageContributorCount => damageContribs.Count;

    /// Get the single shared boost for a tower, creating it if absent. NEVER destroys an
    /// existing one — that would wipe another player's contribution.
    public static TowerTetherBoost GetOrCreate(Tower tower)
    {
        if (tower == null || tower.IsDestroyed()) return null;
        var b = tower.GetComponent<TowerTetherBoost>();
        if (b == null) b = tower.gameObject.AddComponent<TowerTetherBoost>();
        b.tower = tower;
        return b;
    }

    /// Register/replace this contributor's DAMAGE multiplier (and drop any RANGE one it had).
    public void SetDamageContribution(object key, float multiplier)
    {
        if (key == null) return;
        if (rangeContribs.Remove(key)) { /* zone switched range->damage */ }
        damageContribs[key] = Mathf.Max(0f, multiplier);
        Recompute();
    }

    /// Register/replace this contributor's RANGE multiplier (and drop any DAMAGE one it had).
    public void SetRangeContribution(object key, float multiplier)
    {
        if (key == null) return;
        if (damageContribs.Remove(key)) { /* zone switched damage->range */ }
        rangeContribs[key] = Mathf.Max(0.01f, multiplier);
        Recompute();
    }

    /// Remove this contributor entirely. The tower's true base is restored once the last
    /// contributor leaves; the (now idle) component stays for cheap reuse. We deliberately do
    /// NOT Destroy() here — a deferred destroy could hand a doomed instance to another player's
    /// GetOrCreate in the same frame. Idle = no contributions = base restored = no effect.
    public void ClearContribution(object key)
    {
        if (key == null) return;
        bool removed = damageContribs.Remove(key);
        removed |= rangeContribs.Remove(key);
        if (removed) Recompute(); // empties restore base; remaining contributors keep their product
    }

    private void Recompute()
    {
        if (tower == null) tower = GetComponent<Tower>();
        if (tower == null) return;
        bool alive = !tower.IsDestroyed();

        // DAMAGE = trueBase × product(contribs)
        if (damageContribs.Count > 0)
        {
            if (!hasBaseDamage) { baseDamage = tower.GetDamage(); hasBaseDamage = true; }
            float p = ProductOf(damageContribs);
            float want = baseDamage * Mathf.Max(0f, p);
            if (alive) { tower.SetDamage(want); lastAppliedDamage = want; hasAppliedDamage = true; }
        }
        else if (hasBaseDamage)
        {
            if (alive) tower.SetDamage(baseDamage);
            hasBaseDamage = false;
            hasAppliedDamage = false;
        }

        // RANGE = trueBase × product(contribs). ProjectileRange only — never the physical
        // trigger collider (see note below), so world raycasts/lasers are unaffected.
        if (rangeContribs.Count > 0)
        {
            if (!hasBaseRange) { baseProjectileRange = tower.ProjectileRange; hasBaseRange = true; }
            if (baseColliderWorldRadius <= 0f) baseColliderWorldRadius = tower.RangeColliderWorldRadius;

            float p = Mathf.Max(0.01f, ProductOf(rangeContribs));
            float want = baseProjectileRange * p;
            if (alive)
            {
                TrySetProjectileRange(tower, want);
                lastAppliedRange = want;
                hasAppliedRange = true;

                // Scale the detection trigger by the same factor, preserving whatever
                // margin the tower type was set up with (standard towers get +0.5,
                // generators/heal get none). Tower.ApplyRangeColliderRadius forces a
                // re-sweep, so enemies already standing in the new band are picked up
                // rather than waiting for a fresh OnTriggerEnter2D that will never come.
                if (GrowRangeCollider && baseColliderWorldRadius > 0f)
                    TrySetColliderWorldRadius(tower, baseColliderWorldRadius * p);
            }
        }
        else if (hasBaseRange)
        {
            if (alive)
            {
                TrySetProjectileRange(tower, baseProjectileRange);
                if (GrowRangeCollider && baseColliderWorldRadius > 0f)
                    TrySetColliderWorldRadius(tower, baseColliderWorldRadius);
            }
            hasBaseRange = false;
            hasAppliedRange = false;
            baseColliderWorldRadius = 0f;
        }
    }

    private void RestoreBase()
    {
        if (tower == null) tower = GetComponent<Tower>();
        bool alive = tower != null && !tower.IsDestroyed();
        if (hasBaseDamage)
        {
            if (alive) tower.SetDamage(baseDamage);
            hasBaseDamage = false;
        }
        if (hasBaseRange)
        {
            if (alive)
            {
                TrySetProjectileRange(tower, baseProjectileRange);
                if (GrowRangeCollider && baseColliderWorldRadius > 0f)
                    TrySetColliderWorldRadius(tower, baseColliderWorldRadius);
            }
            hasBaseRange = false;
        }
        baseColliderWorldRadius = 0f;
        hasAppliedDamage = false;
        hasAppliedRange = false;
        damageContribs.Clear();
        rangeContribs.Clear();
    }

    void OnDestroy() => RestoreBase();

    // NOTE on range: we intentionally scale ONLY ProjectileRange, never the tower's physical
    // trigger collider. Inflating the collider would let Physics2D queries (e.g. boss lasers,
    // because queriesHitTriggers defaults to true) hit the enlarged trigger zones of distant
    // tethered towers, causing them to take damage they shouldn't. Tower.IsValidTarget reads
    // ProjectileRange, so scaling it alone is a real but world-safe FAR-zone buff.

    /// Set the tower's detection trigger from a WORLD radius. Tower's own
    /// SetRangeColliderWorldRadius is private, and it is the ONLY correct way to do this:
    /// CircleCollider2D.radius is local-space, and tower prefabs are authored at scale
    /// 0.25, so writing .radius directly would give a trigger a quarter of the intended
    /// size (the exact bug documented above Tower.SetRangeColliderWorldRadius).
    private static void TrySetColliderWorldRadius(Tower tower, float worldRadius)
    {
        if (!s_colliderReflectionInit)
        {
            s_colliderRadiusSetter = typeof(Tower).GetMethod(
                "SetRangeColliderWorldRadius",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            s_colliderReflectionInit = true;

            if (s_colliderRadiusSetter == null)
                Debug.LogWarning("[Tether] Tower.SetRangeColliderWorldRadius not found — the " +
                                 "FAR range buff will be cosmetic only.");
        }

        if (s_colliderRadiusSetter == null) return;
        try { s_colliderRadiusSetter.Invoke(tower, new object[] { worldRadius }); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Tether] Failed to set the range collider: {e.Message}");
        }
    }

    /// Set Tower.ProjectileRange via reflection (its setter is private).
    private static void TrySetProjectileRange(Tower tower, float value)
    {
        if (!s_projectileRangeReflectionInit)
        {
            var prop = typeof(Tower).GetProperty(
                "ProjectileRange",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            // GetSetMethod(true) returns the setter even when it's private.
            s_projectileRangeSetter = prop?.GetSetMethod(nonPublic: true);
            s_projectileRangeReflectionInit = true;
        }

        if (s_projectileRangeSetter != null)
        {
            try { s_projectileRangeSetter.Invoke(tower, new object[] { value }); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Tether] Failed to set ProjectileRange via reflection: {e.Message}");
            }
        }
    }
}


/// Attached to a tower while the player is in its NEAR-zone tether. Multiplies the tower's
/// per-tick energy decay rate. EnergyManager.GetDecayRate() must be modified to multiply
/// finalRate by this component's value (see one-line patch below). Composes naturally with
/// TowerCommanderBoost and GeneratorProximityBoost — all three multipliers stack.
/// Set decayMultiplier = 0.0 to fully cancel decay (tower energy holds steady from the
/// EnergyManager's perspective; enemy damage still lands normally).
/// Set 0.5 to halve, 1.0 for no effect.
public class TowerTetherDecayBoost : MonoBehaviour
{
    // Per-contributor decay multipliers (each in [0,1]; lower = more decay reduction).
    private readonly Dictionary<object, float> contribs = new Dictionary<object, float>();

    // Effective multiplier = clamp01(product of all contributors). 1 = no effect (neutral).
    private float effective = 1f;

    // Read-only so external code (EnergyManager) keeps compiling against `.decayMultiplier`,
    // while only this class can change it. EnergyManager calls GetDecayMultiplier().
    public float decayMultiplier => effective;
    public float GetDecayMultiplier() => effective;

    /// One shared decay boost per tower; never destroys an existing one.
    public static TowerTetherDecayBoost GetOrCreate(Tower tower)
    {
        if (tower == null || tower.IsDestroyed()) return null;
        var b = tower.GetComponent<TowerTetherDecayBoost>();
        if (b == null) b = tower.gameObject.AddComponent<TowerTetherDecayBoost>();
        return b;
    }

    public void SetContribution(object key, float multiplier)
    {
        if (key == null) return;
        contribs[key] = Mathf.Clamp01(multiplier);
        Recompute();
    }

    public void ClearContribution(object key)
    {
        if (key == null) return;
        if (contribs.Remove(key)) Recompute(); // empty -> product 1 -> neutral (no decay change)
    }

    private void Recompute()
    {
        float p = 1f;
        foreach (var kv in contribs) p *= kv.Value;
        effective = Mathf.Clamp01(p);
    }
}


/// Pure, static tether buff math — no scene, no state — so it can be unit-tested directly.
/// PlayerTowerTether's instance Compute* methods delegate here with their serialized tuning
/// values. Behavior is byte-identical to the original inline formulas.
public static class TetherMath
{
    /// FAR-zone range multiplier: 1 + min(maxBonus, perTether × count).
    public static float RangeMultiplier(int count, float perTether, float maxBonus)
        => 1f + Mathf.Min(maxBonus, perTether * Mathf.Max(0, count));

    /// MID-zone damage multiplier: 1 + min(maxBonus, perTether × count).
    public static float DamageMultiplier(int count, float perTether, float maxBonus)
        => 1f + Mathf.Min(maxBonus, perTether * Mathf.Max(0, count));

    /// NEAR-zone decay multiplier: clamp01(1 − reductionPerTether × count). Lower = slower decay.
    public static float DecayMultiplier(int count, float reductionPerTether)
        => Mathf.Clamp01(1f - reductionPerTether * Mathf.Max(0, count));
}




