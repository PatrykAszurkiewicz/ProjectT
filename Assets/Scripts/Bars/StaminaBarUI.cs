using UnityEngine;


// Drives  player's stamina bar.
// Co-op: set <see cref="playerIndex"/> (0 = P1, 1 = P2). Duplicate the bar
// group for the second player and set its index to 1. Binding is lazy.
// Single player: the P2 bar (index 1) is auto-HIDDEN. The P1 bar (index 0)
// behaves exactly as before.
public class StaminaBarUI : MonoBehaviour
{
    public ResourceBarUI staminaBarUI;

    [Tooltip("Which player this bar tracks. 0 = first player, 1 = second player.")]
    [SerializeField] private int playerIndex = 0;

    [Tooltip("Optional. The visual root to show/hide when this player slot is absent " +
             "(used to hide P2's bar in single player). If empty, the assigned bar's " +
             "GameObject is used. For drop-in co-op safety, point this at the bar VISUAL " +
             "(a child), not the GameObject holding THIS script.")]
    [SerializeField] private GameObject hideRootWhenAbsent;

    private PlayerStats pstats;

    private void Update()
    {
        ApplyVisibility();

        if (!SlotExists()) return;

        if (pstats == null) pstats = ResolveStats();
        if (pstats != null && staminaBarUI != null)
            staminaBarUI.SetValue(pstats.currentStamina, pstats.maxStamina);
    }

    /// Co-op registry populated -> index must be within the player count.
    /// Registry empty (single player) -> only slot 0 is real.
    private bool SlotExists()
    {
        if (PlayerRegistry.Count > 0) return playerIndex < PlayerRegistry.Count;
        return playerIndex == 0;
    }

    private void ApplyVisibility()
    {
        var root = hideRootWhenAbsent != null
            ? hideRootWhenAbsent
            : (staminaBarUI != null ? staminaBarUI.gameObject : null);
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
        return FindAnyObjectByType<PlayerStats>();
    }
}
