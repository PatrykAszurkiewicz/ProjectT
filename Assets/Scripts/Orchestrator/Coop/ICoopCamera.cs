/// A per-player camera that CoopManager can bind to a player and lay out into a
/// split-screen rect. Implemented by both PlayerCameraController (simple lerp
/// follow, no Cinemachine) and PlayerCinemachineSplitScreen (Cinemachine
/// channels). Keeping this interface lets CoopManager stay package-agnostic —
/// it never references Cinemachine directly.
public interface ICoopCamera
{
    PlayerRef Owner { get; }

    /// <summary>Bind to a player and apply its split-screen rect (and channel, for Cinemachine).</summary>
    void Configure(PlayerRef owner, int index, int totalPlayers);
}
