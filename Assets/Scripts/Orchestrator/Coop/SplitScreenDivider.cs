using System.Collections.Generic;
using UnityEngine;


// Cosmetic centre seam for the vertical split. Drop this on a Canvas object and
// assign a thin vertical Image (the divider line) to <see cref="dividerVisual"/>.
// It shows the divider only while more than one player is registered, so the single-player view stays clean.
// Phase 0: harmless. With one player the divider is hidden.
public class SplitScreenDivider : MonoBehaviour
{
    [Tooltip("The visual (e.g. a thin vertical UI Image) shown down the middle of the screen. Hidden in single player.")]
    [SerializeField] private GameObject dividerVisual;

    // All live dividers, so a global override (e.g. the boss-intro cinematic going
    // full-screen over the split) can hide/show every seam at once.
    private static readonly List<SplitScreenDivider> _instances = new List<SplitScreenDivider>();

    // When true, every divider is hidden regardless of player count. Set by the
    // boss-intro cinematic while it renders full-screen, cleared when it finishes.
    private static bool _hiddenOverride;

    // Reset static state between Play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instances.Clear();
        _hiddenOverride = false;
    }

    /// Force-hide (or release) every split-screen divider at once. Used by
    /// <see cref="BossZoomController"/> while it takes over the full screen, so the
    /// centre seam doesn't sit on top of the full-screen boss shot.
    public static void SetHiddenOverride(bool hidden)
    {
        _hiddenOverride = hidden;
        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i] != null) _instances[i].Refresh();
    }

    private void Start()
    {
        Refresh();
    }

    private void OnEnable()
    {
        if (!_instances.Contains(this)) _instances.Add(this);
        PlayerRegistry.OnPlayerJoined += OnRosterChanged;
        PlayerRegistry.OnPlayerLeft += OnRosterChanged;
        Refresh();
    }

    private void OnDisable()
    {
        _instances.Remove(this);
        PlayerRegistry.OnPlayerJoined -= OnRosterChanged;
        PlayerRegistry.OnPlayerLeft -= OnRosterChanged;
    }

    private void OnRosterChanged(PlayerRef _) => Refresh();

    private void Refresh()
    {
        if (dividerVisual == null) return;
        bool show = PlayerRegistry.Count > 1 && !_hiddenOverride;
        if (dividerVisual.activeSelf != show)
            dividerVisual.SetActive(show);
    }
}

