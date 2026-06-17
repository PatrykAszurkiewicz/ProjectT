using UnityEngine;


// Drives one player's health bar.
// Co-op: set <see cref="playerIndex"/> (0 = P1, 1 = P2). Duplicate the bar group for the
// second player and set its index to 1. Binding is lazy.
// Single player: the P2 bar (index 1) is auto-HIDDEN. P1 (index 0) behaves exactly as before.
// HIDING is done with a CanvasGroup (alpha) + disabling child Renderers (particles), NOT
// SetActive — so this script keeps running and the bar re-appears correctly if a second player
// joins, and the WHOLE group hides (frame, shadow, blur, particles), not just the fill image.

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private ResourceBarUI healthBarUI;

    [Tooltip("Which player this bar tracks. 0 = first player, 1 = second player.")]
    [SerializeField] private int playerIndex = 0;

    [Tooltip("Optional. The visual root to show/hide when this player slot is absent. If empty, " +
             "THIS GameObject is used (the whole bar group). Hiding uses a CanvasGroup + child " +
             "Renderer toggle, so it is safe to point at the script's own object.")]
    [SerializeField] private GameObject hideRootWhenAbsent;

    private PlayerStats stats;
    private bool bound;

    private CanvasGroup _cg;
    private Renderer[] _renderers;
    private bool? _lastVisible;

    private void Start()
    {
        ApplyVisibility();
        TryBind();
    }

    private void Update()
    {
        ApplyVisibility();
        if (!bound) TryBind();
    }

    private void TryBind()
    {
        var ps = ResolveStats();
        if (ps == null || healthBarUI == null) return;

        stats = ps;
        healthBarUI.SetValue(stats.currentHealth, stats.maxHealth);
        stats.OnHealthChanged += healthBarUI.SetValue;
        bound = true;
    }

    /// Co-op registry populated -> authoritative (index must be within the player count).
    /// Registry empty (single player) -> only slot 0 is real.
    private bool SlotExists()
    {
        if (PlayerRegistry.Count > 0) return playerIndex < PlayerRegistry.Count;
        return playerIndex == 0;
    }

    private void ApplyVisibility() => SetGroupVisible(SlotExists());

    private void SetGroupVisible(bool visible)
    {
        if (_lastVisible == visible) return; // no per-frame churn
        _lastVisible = visible;

        var root = hideRootWhenAbsent != null ? hideRootWhenAbsent : gameObject;

        if (_cg == null || _cg.gameObject != root)
        {
            _cg = root.GetComponent<CanvasGroup>();
            if (_cg == null) _cg = root.AddComponent<CanvasGroup>();
        }
        _cg.alpha = visible ? 1f : 0f;          // hides all UI graphics in the group
        _cg.interactable = visible;
        _cg.blocksRaycasts = visible;

        if (_renderers == null) _renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers) if (r != null) r.enabled = visible; // hides particles etc.
    }

    private PlayerStats ResolveStats()
    {
        if (!SlotExists()) return null;

        if (PlayerRegistry.Count > 0)
        {
            var pref = PlayerRegistry.Instance.Get(playerIndex);
            return pref != null ? pref.Stats : null;
        }
        return FindAnyObjectByType<PlayerStats>();
    }

    private void OnDestroy()
    {
        if (stats != null && healthBarUI != null)
            stats.OnHealthChanged -= healthBarUI.SetValue;
    }
}
