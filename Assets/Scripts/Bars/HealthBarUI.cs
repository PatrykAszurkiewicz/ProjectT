using UnityEngine;


// Drives player's health bar.
// Co-op: set <see cref="playerIndex"/> to choose which player this bar tracks
// (0 = P1, 1 = P2). Duplicate the bar group for the second player and set its
// index to 1. Binding is lazy — it waits until that player has spawned.
// Single player: the P2 bar (index 1) is auto-HIDDEN, because that player slot
// doesn't exist. The P1 bar (index 0) behaves exactly as before (binds to the
// only PlayerStats in the scene).

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private ResourceBarUI healthBarUI;

    [Tooltip("Which player this bar tracks. 0 = first player, 1 = second player.")]
    [SerializeField] private int playerIndex = 0;

    [Tooltip("Optional. The visual root to show/hide when this player slot is absent " +
             "(used to hide P2's bar in single player). If empty, the assigned bar's " +
             "GameObject is used. NOTE: for drop-in co-op safety, point this at the bar " +
             "VISUAL (a child), not the GameObject that holds THIS script — hiding the " +
             "script's own object would stop it from ever showing the bar again.")]
    [SerializeField] private GameObject hideRootWhenAbsent;

    private PlayerStats stats;
    private bool bound;

    private void Start()
    {
        ApplyVisibility();
        TryBind();
    }

    private void Update()
    {
        // Player may spawn after frame 0 (CoopManager), and players can appear/leave,
        // so keep visibility + binding in sync. Cheap once bound.
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

    /// True if a player exists (or will exist) for this bar's index.
    /// Co-op registry populated -> authoritative (index must be within the player count).
    /// Registry empty (single player, no registry) -> only slot 0 is real.
    private bool SlotExists()
    {
        if (PlayerRegistry.Count > 0) return playerIndex < PlayerRegistry.Count;
        return playerIndex == 0;
    }

    private void ApplyVisibility()
    {
        var root = hideRootWhenAbsent != null
            ? hideRootWhenAbsent
            : (healthBarUI != null ? healthBarUI.gameObject : null);
        if (root == null) return;

        bool visible = SlotExists();
        if (root.activeSelf != visible) root.SetActive(visible);
    }

    private PlayerStats ResolveStats()
    {
        if (!SlotExists()) return null;

        if (PlayerRegistry.Count > 0)
        {
            var pref = PlayerRegistry.Instance.Get(playerIndex);
            return pref != null ? pref.Stats : null;
        }
        // Single player: registry unused — old behaviour (slot 0 only).
        return FindAnyObjectByType<PlayerStats>();
    }

    private void OnDestroy()
    {
        if (stats != null && healthBarUI != null)
            stats.OnHealthChanged -= healthBarUI.SetValue;
    }
}

