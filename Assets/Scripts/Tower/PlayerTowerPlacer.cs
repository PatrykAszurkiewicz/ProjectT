using UnityEngine;
using UnityEngine.InputSystem;


// Per-player tower placement
// Each player enters placement mode with their own "Placement" action (RB /
// Space), aims at a nearby slot with their reticle (PlayerAim.WorldPoint), and
// builds with "Build" (Left Trigger / left-click). With multiple tower types a
// per-player selection wheel opens, navigated by the reticle and confirmed with
// Build. The wallet stays shared (EnergyManager).
// The shared TowerPlacementManager remains the slot/economy service; this class
// owns input, targeting, highlight, and the wheel for THIS player.

public class PlayerTowerPlacer : MonoBehaviour
{
    [Tooltip("How close this player must be to a slot to build in it.")]
    public float buildRange = 1.8f;

    [Header("Revive (Phase 7b)")]
    [Tooltip("How close THIS player must be to a downed teammate to revive them.")]
    public float reviveRange = 2.0f;
    [Tooltip("UNUSED. Revive is now directional (reviveRange + facing via aim Direction), " +
             "not reticle-proximity. Kept only so existing scenes/prefabs don't lose the value; " +
             "safe to ignore. Tune the facing cone with ReviveFacingMinDot in code.")]
    public float reviveAimRadius = 1.2f;
    [Tooltip("Seconds of continuously holding Build to complete a revive.")]
    public float reviveHoldSeconds = 2.5f;

    private PlayerRef _playerRef;
    private PlayerAim _aim;
    private PlayerAttack _attack;
    private PlayerInput _input;

    private InputAction _placementAction;
    private InputAction _buildAction;
    // Tool button (Right Mouse / Left Trigger) — opens the upgrade / disassemble popup.
    private InputAction _toolAction;

    private bool _placementMode;
    private TowerSlot _highlightedSlot;
    private TowerSelectionWheel _wheel;

    // Upgrade / disassemble popup (Phase: tower management). Opened by a TOOL-button
    // press (Right Mouse / Left Trigger) on a tower in placement mode. Per player.
    private TowerActionMenu _actionMenu;

    // Phase 7b revive state.
    private PlayerDownedState _reviveTarget;
    private float _reviveProgress;
    private ReviveProgressBar _reviveBar;

    // Per-player energy supply (co-op). Each placer owns its OWN beam + cost
    // accumulator, so two players can supply different targets simultaneously with no
    // shared-state conflicts. Replaces the old global-mouse single beam.
    private SupplyBeamController _supplyBeam;
    private IEnergyConsumer _supplyTarget;
    private float _supplyAccum;
    private bool _singleSupplying;
    /// True while THIS player is supplying a specific tower/core (used by the
    /// tether to yield its bulk-supply to single-target supply)
    public bool IsSingleSupplying => _singleSupplying;

    private TowerPlacementManager Hub => TowerPlacementManager.Instance;

    private void Awake()
    {
        _playerRef = GetComponent<PlayerRef>() ?? GetComponentInParent<PlayerRef>();
        _aim = GetComponentInParent<PlayerAim>() ?? GetComponentInChildren<PlayerAim>();
        _attack = GetComponentInParent<PlayerAttack>() ?? GetComponentInChildren<PlayerAttack>();
        _input = GetComponent<PlayerInput>() ?? GetComponentInParent<PlayerInput>();

        if (_input != null)
        {
            _placementAction = _input.actions != null ? _input.actions.FindAction("Placement", false) : null;
            _buildAction = _input.actions != null ? _input.actions.FindAction("Build", false) : null;
            // Tower management (upgrade / disassemble popup) is on the TOOL button
            // (Right Mouse / Left Trigger) so it never clashes with Build's hold-to-supply.
            _toolAction = _input.actions != null ? _input.actions.FindAction("AttackTool", false) : null;
        }
        if (_placementAction == null || _buildAction == null)
            Debug.LogWarning("[PlayerTowerPlacer] 'Placement'/'Build' actions not found on this player's PlayerInput. " +
                             "Add them to PlayerInputActions (Phase 4 asset).");
        if (_toolAction == null)
            Debug.LogWarning("[PlayerTowerPlacer] 'AttackTool' action not found — the tower upgrade/disassemble " +
                             "popup won't open. Expected it bound to Right Mouse / Left Trigger.");
    }

    private void Start()
    {
        // Each player gets its own wheel, driven by this player's aim + Build.
        // The wheel is deliberately left UNPARENTED (a scene-root object) and is
        // destroyed explicitly in OnDestroy instead.
        //
        // HISTORY, so nobody "fixes" this back: it originally leaked — one wheel per
        // respawn, never cleaned up — and the obvious fix was to parent it to the player
        // so it died with them. That parenting broke the wheel's SIZE, because the player
        // transform is SCALED and the wheel inherited that scale. It looked fine at first
        // only because the wheel's children were attached with `.parent =`
        // (worldPositionStays: TRUE), which made Unity write localScale = 1/playerScale
        // on each child and accidentally cancel the inheritance out — until the hover
        // handler reset a slice to Vector3.one and it shrank permanently.
        //
        // Keeping the wheel at the scene root means its world scale is 1, which is what
        // all its hard-coded sizes (1.45f backing, 0.42f hub, HoverPop) assume. Explicit
        // cleanup in OnDestroy solves the leak without touching scale at all.
        var wheelGO = new GameObject($"TowerSelectionWheel_P{(_playerRef != null ? _playerRef.PlayerIndex : 0)}");
        _wheel = wheelGO.AddComponent<TowerSelectionWheel>();
        _wheel.Configure(_aim, _buildAction, _playerRef);
        wheelGO.SetActive(false);

        // This player's own upgrade / disassemble popup, driven by this player's aim
        // and Build action. Hidden until a tower is tapped.
        var menuGO = new GameObject($"TowerActionMenu_P{(_playerRef != null ? _playerRef.PlayerIndex : 0)}");
        menuGO.transform.SetParent(transform, false);
        _actionMenu = menuGO.AddComponent<TowerActionMenu>();
        _actionMenu.Configure(_aim, _buildAction, _toolAction, _playerRef);

        // This player's own supply beam (visual only; transfer goes through
        // EnergyManager.SupplyTickForPlayer). Starts disabled.
        if (EnergyManager.Instance != null)
            _supplyBeam = EnergyManager.Instance.CreateSupplyBeam();
    }

    private void Update()
    {
        if (Time.timeScale == 0f) return;
        if (_placementAction == null || _buildAction == null) return;

        // Placement toggle (and wheel / menu cancel).
        if (_placementAction.WasPressedThisFrame())
        {
            if (_actionMenu != null && _actionMenu.IsOpen) _actionMenu.Close();
            else if (_wheel != null && _wheel.IsOpen) _wheel.CloseWheel();
            else TogglePlacementMode();
        }

        if (!_placementMode) return;

        // Aim point comes from THIS player's reticle / mouse.
        Vector3 aimPoint = _aim != null ? _aim.WorldPoint : transform.position;

        // Phase 7b (co-op): reviving a downed teammate takes precedence over every
        // tower interaction (build / supply / upgrade / disassemble) while a downed
        // teammate is within revive reach AND this player is FACING them. Facing is
        // directional for both mouse and gamepad (PlayerAim.Direction), so you orient
        // toward the teammate — no need to park the cursor on the body. Turn to face a
        // tower instead and normal placement resumes. If a wheel/menu is open, close it
        // so it can't keep ownership of Build, then handle the revive and skip the rest.
        // FIX: this called FindReviveTarget() and then TryHandleRevive() called it AGAIN,
        // scanning the registry twice per player per frame. Resolve once and pass it down.
        var reviveTarget = FindReviveTarget();
        if (reviveTarget != null)
        {
            if (_actionMenu != null && _actionMenu.IsOpen) _actionMenu.Close();
            if (_wheel != null && _wheel.IsOpen) _wheel.CloseWheel();
            StopSupplying();
            if (_highlightedSlot != null) { _highlightedSlot.SetHighlight(false); _highlightedSlot = null; }
            HandleRevive(reviveTarget);   // shows the bar + fills on held Build
            return;
        }

        // While the upgrade/disassemble popup is open it owns Build for its own
        // selection — don't build / supply / open anything else underneath it.
        if (_actionMenu != null && _actionMenu.IsOpen) return;

        // While the wheel is open it consumes Build for selection — don't also
        // try to open/build here.
        if (_wheel != null && _wheel.IsOpen) return;

        // The guard above already handled every in-reach-and-facing case, so there is
        // nothing left to check here — just make sure a stale bar/progress is cleared.
        CancelRevive();

        // Tool button (Right Mouse / Left Trigger) on a tower opens the upgrade /
        // disassemble popup. Independent of Build, so hold-to-supply is unaffected.
        if (_toolAction != null && _toolAction.WasPressedThisFrame() && TryOpenTowerMenu(aimPoint))
            return;

        // Per-player energy supply: if the reticle is on a damaged tower/core within
        // THIS player's supply range and Build is held, supply it (mouse OR gamepad).
        // Takes priority over slot building, mirroring the old single-target-supply
        // priority. Returns true while actively supplying (skips slot build below).
        if (TryHandleSupply(aimPoint)) return;

        UpdateHighlight(aimPoint);

        if (_buildAction.WasPressedThisFrame() && _highlightedSlot != null)
        {
            var prefabs = Hub != null ? Hub.GetTowerPrefabsArray() : null;
            if (prefabs == null || prefabs.Length == 0) return;

            if (prefabs.Length > 1 && _wheel != null)
                _wheel.OpenWheel(prefabs, _highlightedSlot, transform);
            else
                Hub.BuildAt(_highlightedSlot, 0, transform);
        }
    }

    // Per-player single-target supply. While a tower/core is targeted within supply
    // range AND Build is held, transfer energy and draw this player's own beam.
    //   Mouse:   precise — the structure under the reticle (cursor).
    //   Gamepad: directional — the structure the stick points toward (distance only
    //            breaks ties), so you just face a nearby tower/core and hold the
    //            trigger instead of parking a finicky reticle on it.
    // Returns true only while actively supplying, so the caller skips slot building.
    private bool TryHandleSupply(Vector3 aimPoint)
    {
        var em = EnergyManager.Instance;
        if (em == null) { StopSupplying(); return false; }

        bool holding = _buildAction != null && _buildAction.IsPressed();
        IEnergyConsumer target = null;

        if (_aim != null && _aim.UsingGamepad)
        {
            // Directional pick. Also compute the best-aligned build slot: if the stick
            // points more at a slot than at a structure, that's a BUILD intent, so we
            // don't supply (building still works the same directional way).
            target = BestSuppliableByAim(em, aimPoint, out float supplyScore);
            FindBuildSlot(aimPoint, out float slotScore);
            if (target != null && slotScore >= supplyScore) target = null;
        }
        else
        {
            // Mouse: precise reticle targeting (unchanged — the behaviour you liked).
            target = em.FindSupplyTarget(aimPoint);
            if (target != null && !em.IsWithinSupplyRange(transform.position, target)) target = null;
        }

        if (target == null || !holding)
        {
            if (_singleSupplying) StopSupplying();
            return false;
        }

        // Supplying this frame — don't also show a build target.
        if (_highlightedSlot != null) { _highlightedSlot.SetHighlight(false); _highlightedSlot = null; }

        if (!_singleSupplying || _supplyTarget != target)
        {
            _supplyTarget = target;
            _supplyAccum = 0f;   // fresh accumulator when (re)starting on a new target
        }
        _singleSupplying = true;
        em.SetSupplierActive(this, true);          // drives the shared repair sound

        em.SupplyTickForPlayer(target, ref _supplyAccum);

        if (_supplyBeam != null) _supplyBeam.Update(true, target, gameObject);
        return true;
    }

    // Best suppliable structure for the current aim, within supplyRange, scored by
    // AimScore (directional on gamepad). Skips a destroyed core and full towers.
    private IEnergyConsumer BestSuppliableByAim(EnergyManager em, Vector3 aimPoint, out float bestScore)
    {
        IEnergyConsumer best = null;
        bestScore = float.MinValue;

        var all = em.GetAllEnergyConsumers();
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (c is CentralCore core && core.IsDestroyed()) continue;
            if (c.GetEnergyPercentage() >= 0.999f) continue;   // already full

            float score = AimScore(c.GetPosition(), aimPoint, em.supplyRange);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    // Shared aim scoring for picking a build slot or supply target.
    //   Gamepad: reward alignment of (player -> candidate) with the stick direction;
    //            candidates outside a forward cone are rejected; distance is only a
    //            minor tiebreaker, so alignment ("which way am I pointing") dominates.
    //            If the player is basically standing on the candidate, direction is
    //            ignored (it's clearly the target).
    //   Mouse:   nearest to the reticle/cursor point (precise), as before.
    // Returns float.MinValue when the candidate is out of range or outside the cone.
    private const float GamepadAimMinDot = 0.2f;      // ~ +/-78 degree forward cone
    private const float OnTargetRadius = 0.6f;      // "standing on it" => ignore direction

    // Revive facing cone (used by FindReviveTarget for BOTH mouse and gamepad). Slightly
    // tighter than the build/supply gamepad cone so you can point at a downed teammate to
    // revive OR turn toward a nearby tower to build/upgrade it. ~ +/-69 degrees.
    private const float ReviveFacingMinDot = 0.35f;
    private float AimScore(Vector3 candidatePos, Vector3 aimPoint, float range)
    {
        Vector2 toT = (Vector2)(candidatePos - transform.position);
        float dist = toT.magnitude;
        if (dist > range) return float.MinValue;

        if (_aim != null && _aim.UsingGamepad)
        {
            if (dist <= OnTargetRadius) return 1000f - dist;         // on top of it
            float align = Vector2.Dot(toT / dist, _aim.Direction);   // -1..1
            if (align < GamepadAimMinDot) return float.MinValue;     // not pointing at it
            return align * 100f - dist;                              // alignment dominates
        }

        // Mouse / precise: closest to the cursor wins (higher score = closer).
        return -Vector2.Distance((Vector2)candidatePos, (Vector2)aimPoint);
    }

    private void StopSupplying()
    {
        if (!_singleSupplying && _supplyTarget == null) return;
        _singleSupplying = false;
        _supplyTarget = null;
        _supplyAccum = 0f;
        if (EnergyManager.Instance != null) EnergyManager.Instance.SetSupplierActive(this, false);
        if (_supplyBeam != null) _supplyBeam.Update(false, null, gameObject); // disables the beam
    }

    // Phase 7b: hold-Build-to-revive the downed teammate this player is facing (see
    // FindReviveTarget for the directional rule). Returns true while it's actively
    // handling the revive (so the caller skips the normal build path). No-op solo.
    private bool HandleRevive(PlayerDownedState target)
    {
        if (target == null)
        {
            CancelRevive();
            return false;
        }

        // Facing a downed teammate — don't also show a build target.
        if (_highlightedSlot != null) { _highlightedSlot.SetHighlight(false); _highlightedSlot = null; }
        _reviveTarget = target;

        // Hold Build continuously to fill; releasing resets.
        bool holding = _buildAction != null && _buildAction.IsPressed();
        if (holding)
        {
            _reviveProgress += Time.deltaTime / Mathf.Max(0.1f, reviveHoldSeconds);
            if (_reviveProgress >= 1f)
            {
                target.Revive();   // uses the downed player's configured revive %
                CancelRevive();
                return true;
            }
        }
        else
        {
            _reviveProgress = 0f;
        }

        EnsureBar();
        _reviveBar.Show(target.transform.position, _reviveProgress);
        return true;
    }

    // Best downed teammate to revive, chosen DIRECTIONALLY for BOTH mouse and gamepad.
    // Qualification: within reviveRange AND facing the teammate — i.e. this player's
    // aim Direction points at them. It uses PlayerAim.Direction, which is the
    // player->cursor direction for mouse and the stick direction for gamepad, so you
    // just orient toward the downed teammate; the cursor/reticle does NOT need to sit
    // on the body. This mirrors how the gamepad build/supply aim works (AimScore).
    // Standing basically on top of the teammate ignores direction (you can't
    // meaningfully "face" something you're on). Best-aligned wins; null if none.
    private PlayerDownedState FindReviveTarget()
    {
        if (PlayerRegistry.Count <= 1) return null;

        Vector2 aimDir = _aim != null ? _aim.Direction : (Vector2)transform.right;

        PlayerDownedState best = null;
        float bestScore = float.MinValue;

        var all = PlayerRegistry.Instance.All;
        for (int i = 0; i < all.Count; i++)
        {
            var pr = all[i];
            if (pr == null || pr == _playerRef || pr.Stats == null) continue;

            var downed = pr.Stats.GetComponent<PlayerDownedState>();
            if (downed == null || !downed.IsDowned) continue;

            Vector2 toT = (Vector2)(pr.transform.position - transform.position);
            float dist = toT.magnitude;
            if (dist > reviveRange) continue;                    // proximity gate

            float score;
            if (dist <= OnTargetRadius)
            {
                score = 1000f - dist;                            // basically on them → always qualifies
            }
            else
            {
                float align = Vector2.Dot(toT / dist, aimDir);   // -1..1: how much I'm facing them
                if (align < ReviveFacingMinDot) continue;        // not facing them → not a revive
                score = align * 100f - dist;                     // alignment dominates, nearer breaks ties
            }

            if (score > bestScore) { bestScore = score; best = downed; }
        }
        return best;
    }

    private void CancelRevive()
    {
        _reviveTarget = null;
        _reviveProgress = 0f;
        if (_reviveBar != null) _reviveBar.Hide();
    }

    private void EnsureBar()
    {
        if (_reviveBar != null) return;
        var go = new GameObject($"ReviveBar_P{(_playerRef != null ? _playerRef.PlayerIndex : 0)}");
        _reviveBar = go.AddComponent<ReviveProgressBar>();
    }

    private void UpdateHighlight(Vector3 aimPoint)
    {
        if (Hub == null) return;

        TowerSlot best = FindBuildSlot(aimPoint);

        if (best != _highlightedSlot)
        {
            if (_highlightedSlot != null) _highlightedSlot.SetHighlight(false);
            _highlightedSlot = best;
            if (_highlightedSlot != null) _highlightedSlot.SetHighlight(true);
        }
    }

    // Best available slot for the current aim, within buildRange. Uses the same
    // AimScore as supply, so on a gamepad you point the stick at the slot you want
    // (distance breaks ties) and on a mouse it's the slot nearest the cursor. Shared
    // by UpdateHighlight (highlight/build) and the supply path (build-vs-supply intent).
    private TowerSlot FindBuildSlot(Vector3 aimPoint) => FindBuildSlot(aimPoint, out _);

    private TowerSlot FindBuildSlot(Vector3 aimPoint, out float bestScore)
    {
        bestScore = float.MinValue;
        if (Hub == null) return null;

        TowerSlot best = null;
        foreach (var s in Hub.GetAllSlots())
        {
            if (s == null || !s.IsAvailable) continue;

            float score = AimScore(s.transform.position, aimPoint, buildRange);
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    //  Tower management → upgrade / disassemble popup 

    // Best OCCUPIED slot (one holding a tower) under the current aim, within build
    // range. Uses the same AimScore as building/supply (mouse: nearest reticle;
    // gamepad: best-aligned), so it picks the tower you're pointing at.
    private TowerSlot FindOccupiedSlotUnderAim(Vector3 aimPoint, out float bestScore)
    {
        bestScore = float.MinValue;
        if (Hub == null) return null;

        TowerSlot best = null;
        foreach (var s in Hub.GetAllSlots())
        {
            if (s == null || !s.IsOccupied || s.currentTower == null) continue;

            float score = AimScore(s.transform.position, aimPoint, buildRange);
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    // Open the upgrade / disassemble popup if a tower is under the current aim within
    // build range. Returns true when it opened (so the caller skips build/supply).
    private bool TryOpenTowerMenu(Vector3 aimPoint)
    {
        if (_actionMenu == null || Hub == null) return false;

        TowerSlot slot = FindOccupiedSlotUnderAim(aimPoint, out _);
        if (slot == null || slot.currentTower == null) return false;

        var tower = slot.currentTower.GetComponent<Tower>();
        if (tower == null) return false;

        // Drop any in-progress supply/highlight so the popup is the sole interaction.
        StopSupplying();
        if (_highlightedSlot != null) { _highlightedSlot.SetHighlight(false); _highlightedSlot = null; }

        _actionMenu.Open(tower, ResolveEffectCamera());
        return true;
    }

    private void TogglePlacementMode()
    {
        _placementMode = !_placementMode;

        // Suppress only THIS player's attack while building.
        if (_attack != null) _attack.SetSuppressed(_placementMode);

        // Greyscale + pulse on THIS player's half of the screen (see below).
        SetPlacementScreenEffect(_placementMode);

        // This player's cursor.
        var cm = CursorManager.For(_playerRef);
        if (cm != null)
        {
            if (_placementMode) cm.SetCursor(CursorManager.CursorType.Repair);
            else cm.ReturnToPreviousCursor();
        }

        // Let the shared hub know someone is in placement mode (gates supply etc).
        if (Hub != null) Hub.NotifyPlacementMode(this, _placementMode);

        if (!_placementMode)
        {
            if (_highlightedSlot != null) { _highlightedSlot.SetHighlight(false); _highlightedSlot = null; }
            if (_wheel != null) _wheel.CloseWheel();
            if (_actionMenu != null) _actionMenu.Close();
            CancelRevive();
            StopSupplying();
        }
    }

    // Greyscale + pulse "building mode" screen effect, on THIS player's camera.
    // Per-player by construction: each player's camera owns its split-screen rect,
    // so the effect only desaturates that player's half. Single player: the one
    // camera owns the full-screen rect → whole view. Camera comes from
    // ResolveEffectCamera(), which in co-op always resolves THIS player's own camera
    // (never another player's). The component is added lazily and re-used; turning it
    // off never adds one needlessly.
    private void SetPlacementScreenEffect(bool on)
    {
        Camera cam = ResolveEffectCamera();
        if (cam == null)
        {
            if (on)
                Debug.LogWarning("[PlayerTowerPlacer] Placement greyscale: no camera found " +
                                 "(PlayerRef.Camera is null, no Camera.main, and Camera.allCameras " +
                                 "is empty). Tag your game camera 'MainCamera' or assign PlayerRef.Camera.");
            return;
        }

        var fx = cam.GetComponent<PlacementModeScreenEffect>();
        if (fx == null)
        {
            if (!on) return;   // nothing to turn off
            fx = cam.gameObject.AddComponent<PlacementModeScreenEffect>();
        }
        fx.SetEngaged(on);
    }

    // Which camera renders THIS player's view. This drives both the placement
    // greyscale and the tower action menu's Screen Space - Camera canvas, so it MUST
    // return this player's own split-screen camera — returning another player's camera
    // is exactly what made the menu appear on the wrong half of the screen.
    //
    //   1) PlayerRef.Camera — assigned by PlayerCameraController /
    //      PlayerCinemachineSplitScreen. The normal, correct answer.
    //   2) Co-op fallback: the render camera whose ICoopCamera.Owner is THIS player.
    //      Used if PlayerRef.Camera hasn't been wired yet. Crucially we match by owner
    //      instead of grabbing Camera.main, so we can never hijack the other player's
    //      half. The result is cached back onto PlayerRef so aim/cursor/effects agree.
    //   3) Single player ONLY: the tagged main camera (or first active), which owns the
    //      whole screen. In co-op we deliberately do NOT do this — better to return null
    //      and let the caller use a full-screen overlay than to render on the wrong half.
    private Camera ResolveEffectCamera()
    {
        if (_playerRef != null && _playerRef.Camera != null) return _playerRef.Camera;

        if (_playerRef != null)
        {
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null) continue;
                var coop = c.GetComponent<ICoopCamera>();
                if (coop != null && coop.Owner == _playerRef)
                {
                    _playerRef.Camera = c;   // cache so every consumer agrees from now on
                    return c;
                }
            }
        }

        if (PlayerRegistry.Count <= 1)
        {
            if (Camera.main != null) return Camera.main;
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
                if (cams[i] != null && cams[i].isActiveAndEnabled) return cams[i];
        }

        return null;   // co-op: couldn't find this player's camera — caller overlays full-screen
    }

    // NOTE: the player's facing / torch / cursor are frozen while the tower wheel is
    // open by PlayerAim's menu capture (TowerSelectionWheel.OpenWheel/CloseWheel).
    // The old approach here re-applied transform rotations in LateUpdate, which had
    // no effect because facing is driven by PlayerAim.Direction, not transform rotation.

    private void OnDisable()
    {
        CancelRevive();
        if (_actionMenu != null) _actionMenu.Close();
        // The wheel lives at the scene root, so it does NOT go away when this component
        // is disabled (e.g. the player is downed). Close it explicitly, otherwise it
        // stays open and keeps this player's aim frozen by its menu capture.
        if (_wheel != null && _wheel.IsOpen) _wheel.CloseWheel();
        StopSupplying();
        SetPlacementScreenEffect(false); // drop greyscale if disabled mid-build (e.g. downed)
        if (_placementMode)
        {
            _placementMode = false;
            if (_attack != null) _attack.SetSuppressed(false);
            if (Hub != null) Hub.NotifyPlacementMode(this, false);
            if (_highlightedSlot != null) { _highlightedSlot.SetHighlight(false); _highlightedSlot = null; }
        }
    }

    private void OnDestroy()
    {
        // The wheel lives at the scene root (see the note in the setup above), so it is
        // NOT destroyed automatically with this player — do it here or it leaks.
        if (_wheel != null) Destroy(_wheel.gameObject);

        if (_reviveBar != null) Destroy(_reviveBar.gameObject);
        if (EnergyManager.Instance != null) EnergyManager.Instance.SetSupplierActive(this, false);
        if (_supplyBeam != null) { _supplyBeam.Cleanup(); _supplyBeam = null; }
    }
}



