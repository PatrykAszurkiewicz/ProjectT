using UnityEngine;


// Cosmetic centre seam for the vertical split. Drop this on a Canvas object and
// assign a thin vertical Image (the divider line) to <see cref="dividerVisual"/>.
// It shows the divider only while more than one player is registered, so the single-player view stays clean.
// Phase 0: harmless. With one player the divider is hidden.
public class SplitScreenDivider : MonoBehaviour
{
    [Tooltip("The visual (e.g. a thin vertical UI Image) shown down the middle of the screen. Hidden in single player.")]
    [SerializeField] private GameObject dividerVisual;

    private void Start()
    {
        Refresh();
    }

    private void OnEnable()
    {
        PlayerRegistry.OnPlayerJoined += OnRosterChanged;
        PlayerRegistry.OnPlayerLeft += OnRosterChanged;
        Refresh();
    }

    private void OnDisable()
    {
        PlayerRegistry.OnPlayerJoined -= OnRosterChanged;
        PlayerRegistry.OnPlayerLeft -= OnRosterChanged;
    }

    private void OnRosterChanged(PlayerRef _) => Refresh();

    private void Refresh()
    {
        if (dividerVisual == null) return;
        bool show = PlayerRegistry.Count > 1;
        if (dividerVisual.activeSelf != show)
            dividerVisual.SetActive(show);
    }
}
