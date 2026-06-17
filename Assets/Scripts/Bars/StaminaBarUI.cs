using UnityEngine;


// Drives one player's stamina bar.
// HIDING uses a CanvasGroup (alpha) + child Renderer toggle, NOT SetActive — so the WHOLE
// group hides (not just the fill), the script keeps running (re-appears when P2 joins), and
// no GameObject toggling destabilizes child particle systems.

public class StaminaBarUI : MonoBehaviour
{
    public ResourceBarUI staminaBarUI;

    [Tooltip("Which player this bar tracks. 0 = first player, 1 = second player.")]
    [SerializeField] private int playerIndex = 0;

    [Tooltip("Optional. The visual root to show/hide when this player slot is absent. If empty, " +
             "THIS GameObject is used (the whole bar group).")]
    [SerializeField] private GameObject hideRootWhenAbsent;

    private PlayerStats pstats;

    private CanvasGroup _cg;
    private Renderer[] _renderers;
    private bool? _lastVisible;

    private void Update()
    {
        ApplyVisibility();

        if (!SlotExists()) return;

        if (pstats == null) pstats = ResolveStats();
        if (pstats != null && staminaBarUI != null)
            staminaBarUI.SetValue(pstats.currentStamina, pstats.maxStamina);
    }

    private bool SlotExists()
    {
        if (PlayerRegistry.Count > 0) return playerIndex < PlayerRegistry.Count;
        return playerIndex == 0;
    }

    private void ApplyVisibility() => SetGroupVisible(SlotExists());

    private void SetGroupVisible(bool visible)
    {
        if (_lastVisible == visible) return;
        _lastVisible = visible;

        var root = hideRootWhenAbsent != null ? hideRootWhenAbsent : gameObject;

        if (_cg == null || _cg.gameObject != root)
        {
            _cg = root.GetComponent<CanvasGroup>();
            if (_cg == null) _cg = root.AddComponent<CanvasGroup>();
        }
        _cg.alpha = visible ? 1f : 0f;
        _cg.interactable = visible;
        _cg.blocksRaycasts = visible;

        if (_renderers == null) _renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers) if (r != null) r.enabled = visible;
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

