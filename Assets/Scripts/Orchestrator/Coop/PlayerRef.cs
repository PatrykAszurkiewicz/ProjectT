using UnityEngine;

// Per-player identity + cached sibling references. One of these lives on every
// player object. It self-registers in <see cref="PlayerRegistry"/> while
// enabled, so all the co-op targeting/hazard helpers can resolve "the players"
// without any global single-lookups.
// Phase 0/1 notes:
//  - In single player there is exactly one of these (index 0), so every
//    registry helper returns the one and only player — behaviour is identical
//    to the old FindGameObjectWithTag("Player") path.
//  - <see cref="PlayerStats.Awake"/> auto-adds a PlayerRef if one isn't present,
//    so the existing single-player object self-registers with no prefab work.
//    When the player prefab is built in Phase 2 you can add PlayerRef explicitly
//    and set <see cref="PlayerIndex"/>; the auto-add guard avoids duplicates.
//  - <see cref="Camera"/> is assigned by PlayerCameraController in Phase 2;
//    it stays null in single player and consumers fall back to Camera.main.

[RequireComponent(typeof(PlayerStats))]
public class PlayerRef : MonoBehaviour
{
    [Tooltip("0 = player one, 1 = player two. Set by CoopManager when spawned; defaults to 0 for the single-player setup.")]
    public int PlayerIndex = 0;

    /// <summary>This player's stats component (cached in Awake).</summary>
    public PlayerStats Stats { get; private set; }

    [Tooltip("This player's camera. Assigned by PlayerCameraController in co-op (Phase 2). Null in single player → consumers fall back to Camera.main.")]
    public Camera Camera;

    /// <summary>
    /// True while this player is hidden from enemy targeting.
    ///
    /// CO-OP CORRECTNESS NOTE. This used to be a bare
    ///     <c>=> PlayerCloakEffect.IsActive</c>
    /// which is a GLOBAL flag: in co-op, P1 activating a cloak hid P2 from every enemy
    /// too. It is now a per-player value with the global flag only as a fallback, so a
    /// cloak system that reports per player gets correct behaviour and a legacy global
    /// one behaves exactly as before (identical in single player either way).
    ///
    /// The per-player value is pushed in via <see cref="SetCloaked"/> rather than
    /// discovered with GetComponent, deliberately: nothing in this project reveals
    /// whether PlayerCloakEffect is a MonoBehaviour, so a GetComponent&lt;T&gt; here would
    /// be a guess that might not even compile.
    ///
    /// TO FINISH THE PER-PLAYER CLOAK: call <c>playerRef.SetCloaked(true/false)</c> from
    /// wherever PlayerCloakEffect starts and stops. Until you do, this returns the global
    /// flag and behaviour is unchanged from today.
    /// </summary>
    public bool IsCloaked => _cloakOverride ?? PlayerCloakEffect.IsActive;

    // null = "no per-player answer, use the global flag".
    private bool? _cloakOverride;

    /// <summary>
    /// Report THIS player's cloak state. Call with true when this specific player
    /// becomes invisible and false when they become visible again.
    /// </summary>
    public void SetCloaked(bool cloaked) => _cloakOverride = cloaked;

    /// <summary>Stop reporting a per-player cloak state and defer to the global flag.</summary>
    public void ClearCloakOverride() => _cloakOverride = null;

    /// <summary>
    /// Which camera renders THIS player's view. Single shared implementation of a rule
    /// that several systems need (placement greyscale, downed greyscale, tower action
    /// menu, cursor), and that MUST return this player's own split-screen camera —
    /// returning another player's is exactly what puts UI on the wrong half.
    ///
    ///   1) <see cref="Camera"/> — set by PlayerCameraController.Configure. The normal answer.
    ///   2) Co-op fallback: the render camera whose ICoopCamera.Owner is this player.
    ///      Matched by owner rather than grabbing Camera.main, so it can never hijack the
    ///      other player's half. Cached back onto Camera so every consumer agrees.
    ///   3) Single player ONLY: the tagged main camera (or first active), which owns the
    ///      whole screen. Deliberately NOT done in co-op — better to return null and let
    ///      the caller skip than to render on the wrong half.
    /// </summary>
    public Camera ResolveCamera()
    {
        if (Camera != null) return Camera;

        var cams = UnityEngine.Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
        {
            var c = cams[i];
            if (c == null) continue;
            var coop = c.GetComponent<ICoopCamera>();
            if (coop != null && coop.Owner == this)
            {
                Camera = c;
                return c;
            }
        }

        if (PlayerRegistry.Count <= 1)
        {
            if (UnityEngine.Camera.main != null) return UnityEngine.Camera.main;
            for (int i = 0; i < cams.Length; i++)
                if (cams[i] != null && cams[i].isActiveAndEnabled) return cams[i];
        }

        return null;
    }

    /// <summary>Null-safe <see cref="ResolveCamera"/> for callers that may not have a PlayerRef.</summary>
    public static Camera ResolveCameraFor(PlayerRef player)
    {
        if (player != null) return player.ResolveCamera();
        if (PlayerRegistry.Count <= 1 && UnityEngine.Camera.main != null) return UnityEngine.Camera.main;
        return null;
    }

    private void Awake()
    {
        Stats = GetComponent<PlayerStats>();
    }

    private void OnEnable()
    {
        PlayerRegistry.Register(this);
    }

    private void OnDisable()
    {
        // Leaving play (death teardown, despawn, scene unload) must not leave this
        // player permanently marked cloaked for a future re-enable.
        _cloakOverride = null;
        PlayerRegistry.Unregister(this);
    }
}


