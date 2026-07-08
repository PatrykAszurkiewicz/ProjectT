using UnityEngine;

// Persistent (DontDestroyOnLoad) holder for the co-op session decision —
// how many players this run is for. It is created/owned by the main menu and
// survives the menu→gameplay scene load, so <see cref="CoopManager"/> can read
// the chosen player count once the gameplay scene starts.
// Kept deliberately separate from the run blueprint (RunConfig): the run
// blueprint (stages/waves/biomes) is shared by both players; co-op is a
// per-session concern.

public class SessionConfig : MonoBehaviour
{
    public static SessionConfig Instance { get; private set; }

    [Tooltip("1 = single player (default, identical to today). 2 = local co-op.")]
    [Range(1, 2)]
    public int TargetPlayerCount = 1;

    public bool CoopEnabled => TargetPlayerCount > 1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// Convenience for menu buttons: set the player count for the next run.
    public void SetPlayerCount(int count)
    {
        TargetPlayerCount = Mathf.Clamp(count, 1, 2);
    }
}


