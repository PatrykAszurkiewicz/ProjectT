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
             "Reduces flicker when the player walks near the edge.")]
    public float breakRangeMultiplier = 1.15f;

    [Tooltip("Maximum number of simultaneous tethers (safety cap).")]
    public int maxSimultaneousTethers = 8;

    [Header("Zone Thresholds (fraction of maxTetherRange)")]
    [Range(0.05f, 0.95f)] public float nearZoneEnd = 0.30f;
    [Range(0.05f, 0.95f)] public float midZoneEnd = 0.60f;

    [Header("Buff Strengths (scale with number of tethered towers)")]
    [Tooltip("Each connected tower adds this fraction to the FAR-zone range buff. " +
             "E.g. 0.05 with 4 tethers = +20% range. Effective multiplier = 1 + (this × tetherCount).")]
    public float farRangeBonusPerTether = 0.05f;

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

    [Header("Visual - Chain")]
    public Color farColor = new Color(1.0f, 0.85f, 0.3f, 0.85f);  // gold     (range)
    public Color midColor = new Color(1.0f, 0.55f, 0.2f, 0.85f);  // orange   (damage)
    public Color nearColor = new Color(0.3f, 0.9f, 1.0f, 0.85f);  // cyan     (defense)
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
        retargetTimer -= Time.deltaTime;
        if (retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            RescanTowers();
        }

        UpdateAllTethers();
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
        scratchInRange.Clear();
        Tower[] all = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t == null || t.IsDestroyed()) continue;
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
            TetherZone newZone = ComputeZone(dist);

            if (newZone == TetherZone.None)
            {
                // Out of range — drop it. Rescan would also catch this, but this keeps it instant.
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

    private TetherZone ComputeZone(float distance)
    {
        if (distance > maxTetherRange) return TetherZone.None;
        float frac = distance / maxTetherRange;
        if (frac <= nearZoneEnd) return TetherZone.Near;
        if (frac <= midZoneEnd) return TetherZone.Mid;
        return TetherZone.Far;
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

        // Tether count just increased — re-apply existing tethers with the new count-scaled buff.
        RecomputeAllBuffs();
    }

    private void DetachOne(Tower tower)
    {
        if (!activeTethers.TryGetValue(tower, out var at)) return;
        ReleaseTether(at);
        activeTethers.Remove(tower);

        // Tether count just decreased — remaining tethers need weaker buffs.
        RecomputeAllBuffs();
    }

    private void DetachAll()
    {
        foreach (var kvp in activeTethers)
            ReleaseTether(kvp.Value);
        activeTethers.Clear();

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

        int tetherCount = activeTethers.Count;

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
        int count = activeTethers.Count;

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

        // Choose colors: while bulk-supplying, override with electric-blue supply color.
        Color baseTint, glowTint;
        float widthMul;

        if (isBulkSupplying)
        {
            // Strong pulse during supply — energetic feel.
            float supplyPulse = 0.7f + Mathf.Sin(Time.time * 8f + phase) * 0.3f;
            baseTint = supplyColor;
            baseTint.a = supplyColor.a * supplyPulse;
            glowTint = supplyColor;
            glowTint.a = supplyColor.a * 0.5f * supplyPulse;
            widthMul = supplyWidthMultiplier;
        }
        else
        {
            Color zoneColor = at.zone switch
            {
                TetherZone.Far => farColor,
                TetherZone.Mid => midColor,
                TetherZone.Near => nearColor,
                _ => Color.white,
            };
            float pulse = 0.85f + Mathf.Sin(Time.time * 4f + phase) * 0.15f;
            baseTint = zoneColor; baseTint.a = zoneColor.a * pulse;
            glowTint = zoneColor; glowTint.a = zoneColor.a * 0.45f * pulse;
            widthMul = 1f;
        }

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
public class TowerTetherBoost : MonoBehaviour
{
    private Tower tower;

    // True base stats, captured lazily (before any contribution of that kind is live).
    private float baseDamage;
    private bool hasBaseDamage;
    private float baseProjectileRange;
    private bool hasBaseRange;

    // Per-contributor multipliers. A given tether contributes EITHER damage OR range,
    // depending on its current zone — never both at once.
    private readonly Dictionary<object, float> damageContribs = new Dictionary<object, float>();
    private readonly Dictionary<object, float> rangeContribs = new Dictionary<object, float>();

    // Cached reflection handle for the private ProjectileRange setter (see TrySetProjectileRange).
    private static System.Reflection.MethodInfo s_projectileRangeSetter;
    private static bool s_projectileRangeReflectionInit;

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
            float p = 1f;
            foreach (var kv in damageContribs) p *= kv.Value;
            if (alive) tower.SetDamage(baseDamage * Mathf.Max(0f, p));
        }
        else if (hasBaseDamage)
        {
            if (alive) tower.SetDamage(baseDamage);
            hasBaseDamage = false;
        }

        // RANGE = trueBase × product(contribs). ProjectileRange only — never the physical
        // trigger collider (see note below), so world raycasts/lasers are unaffected.
        if (rangeContribs.Count > 0)
        {
            if (!hasBaseRange) { baseProjectileRange = tower.ProjectileRange; hasBaseRange = true; }
            float p = 1f;
            foreach (var kv in rangeContribs) p *= kv.Value;
            if (alive) TrySetProjectileRange(tower, baseProjectileRange * Mathf.Max(0.01f, p));
        }
        else if (hasBaseRange)
        {
            if (alive) TrySetProjectileRange(tower, baseProjectileRange);
            hasBaseRange = false;
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
            if (alive) TrySetProjectileRange(tower, baseProjectileRange);
            hasBaseRange = false;
        }
        damageContribs.Clear();
        rangeContribs.Clear();
    }

    void OnDestroy() => RestoreBase();

    // NOTE on range: we intentionally scale ONLY ProjectileRange, never the tower's physical
    // trigger collider. Inflating the collider would let Physics2D queries (e.g. boss lasers,
    // because queriesHitTriggers defaults to true) hit the enlarged trigger zones of distant
    // tethered towers, causing them to take damage they shouldn't. Tower.IsValidTarget reads
    // ProjectileRange, so scaling it alone is a real but world-safe FAR-zone buff.

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

