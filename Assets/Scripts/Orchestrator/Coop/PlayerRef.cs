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
    /// Co-op v1 bridges to the existing global cloak (PlayerCloakEffect); it
    /// becomes truly per-player in a later phase. Phase 1 targeting passes
    /// includeCloaked:true at every call site, so this has no behavioural effect
    /// yet — it just exists for the helpers to consult later.
    /// </summary>
    public bool IsCloaked => PlayerCloakEffect.IsActive;

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
        PlayerRegistry.Unregister(this);
    }
}
