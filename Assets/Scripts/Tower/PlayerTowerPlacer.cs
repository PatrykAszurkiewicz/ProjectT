using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Per-player tower placement (Phase 4). Lives on the Player prefab root.
///
/// Each player enters placement mode with their own "Placement" action (RB /
/// Space), aims at a nearby slot with their reticle (PlayerAim.WorldPoint), and
/// builds with "Build" (Left Trigger / left-click). With multiple tower types a
/// per-player selection wheel opens, navigated by the reticle and confirmed with
/// Build. The wallet stays shared (EnergyManager).
///
/// The shared TowerPlacementManager remains the slot/economy service; this class
/// owns input, targeting, highlight, and the wheel for THIS player.
///
/// Single player: one placer, full-screen camera, mouse aim + Space/click — same
/// feel as before.
/// </summary>
public class PlayerTowerPlacer : MonoBehaviour
{
    [Tooltip("How close this player must be to a slot to build in it.")]
    public float buildRange = 1.2f;

    [Header("Revive (Phase 7b)")]
    [Tooltip("How close THIS player must be to a downed teammate to revive them.")]
    public float reviveRange = 2.0f;
    [Tooltip("How near the reticle must be to the downed teammate to target the revive (\"approximately at\").")]
    public float reviveAimRadius = 1.2f;
    [Tooltip("Seconds of continuously holding Build to complete a revive.")]
    public float reviveHoldSeconds = 2.5f;

    private PlayerRef _playerRef;
    private PlayerAim _aim;
    private PlayerAttack _attack;
    private PlayerInput _input;

    private InputAction _placementAction;
    private InputAction _buildAction;

    private bool _placementMode;
    private TowerSlot _highlightedSlot;
    private TowerSelectionWheel _wheel;

    // Phase 7b revive state.
    private PlayerDownedState _reviveTarget;
    private float _reviveProgress;
    private ReviveProgressBar _reviveBar;

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
        }
        if (_placementAction == null || _buildAction == null)
            Debug.LogWarning("[PlayerTowerPlacer] 'Placement'/'Build' actions not found on this player's PlayerInput. " +
                             "Add them to PlayerInputActions (Phase 4 asset).");
    }

    private void Start()
    {
        // Each player gets its own wheel, driven by this player's aim + Build.
        var wheelGO = new GameObject($"TowerSelectionWheel_P{(_playerRef != null ? _playerRef.PlayerIndex : 0)}");
        _wheel = wheelGO.AddComponent<TowerSelectionWheel>();
        _wheel.Configure(_aim, _buildAction, _playerRef);
        wheelGO.SetActive(false);
    }

    private void Update()
    {
        if (Time.timeScale == 0f) return;
        if (_placementAction == null || _buildAction == null) return;

        // Placement toggle (and wheel-cancel).
        if (_placementAction.WasPressedThisFrame())
        {
            if (_wheel != null && _wheel.IsOpen) _wheel.CloseWheel();
            else TogglePlacementMode();
        }

        if (!_placementMode) return;

        // Aim point comes from THIS player's reticle / mouse.
        Vector3 aimPoint = _aim != null ? _aim.WorldPoint : transform.position;

        // While the wheel is open it consumes Build for selection — don't also
        // try to open/build here.
        if (_wheel != null && _wheel.IsOpen) return;

        // Phase 7b (co-op): revive takes priority over building while the reticle
        // is on a downed teammate within reach. Returns true when it's handling a
        // revive this frame, so we skip the slot highlight + build below.
        if (TryHandleRevive(aimPoint)) return;

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

    // Phase 7b: hold-Build-to-revive a downed teammate the reticle is pointing at.
    // Returns true while it's actively handling the revive (so the caller skips
    // the normal highlight/build path this frame). No-op in single player.
    private bool TryHandleRevive(Vector3 aimPoint)
    {
        var target = FindReviveTarget(aimPoint);
        if (target == null)
        {
            CancelRevive();
            return false;
        }

        // Reticle is on a downed teammate — don't also show a build target.
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

    // Nearest downed teammate that is (a) within reviveRange of me and (b) under
    // the reticle (within reviveAimRadius). Null if none / single player.
    private PlayerDownedState FindReviveTarget(Vector3 aimPoint)
    {
        if (PlayerRegistry.Count <= 1) return null;

        PlayerDownedState best = null;
        float bestAimSqr = reviveAimRadius * reviveAimRadius;
        float rangeSqr = reviveRange * reviveRange;

        var all = PlayerRegistry.Instance.All;
        for (int i = 0; i < all.Count; i++)
        {
            var pr = all[i];
            if (pr == null || pr == _playerRef || pr.Stats == null) continue;

            var downed = pr.Stats.GetComponent<PlayerDownedState>();
            if (downed == null || !downed.IsDowned) continue;

            Vector3 tpos = pr.transform.position;
            if ((tpos - transform.position).sqrMagnitude > rangeSqr) continue;

            float aimSqr = ((Vector2)(tpos - aimPoint)).sqrMagnitude;
            if (aimSqr <= bestAimSqr) { bestAimSqr = aimSqr; best = downed; }
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

        TowerSlot best = null;
        float bestDist = float.MaxValue;

        foreach (var s in Hub.GetAllSlots())
        {
            if (s == null || !s.IsAvailable) continue;
            if (Vector2.Distance(transform.position, s.transform.position) > buildRange) continue;

            float d = Vector2.Distance(aimPoint, s.transform.position);
            if (d < bestDist) { bestDist = d; best = s; }
        }

        if (best != _highlightedSlot)
        {
            if (_highlightedSlot != null) _highlightedSlot.SetHighlight(false);
            _highlightedSlot = best;
            if (_highlightedSlot != null) _highlightedSlot.SetHighlight(true);
        }
    }

    private void TogglePlacementMode()
    {
        _placementMode = !_placementMode;

        // Suppress only THIS player's attack while building.
        if (_attack != null) _attack.SetSuppressed(_placementMode);

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
            CancelRevive();
        }
    }

    private void OnDisable()
    {
        CancelRevive();
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
        if (_reviveBar != null) Destroy(_reviveBar.gameObject);
    }
}
