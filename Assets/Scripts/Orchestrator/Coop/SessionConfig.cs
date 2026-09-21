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

        // FIX: DontDestroyOnLoad ONLY works on root GameObjects. Parented under a menu
        // panel it logged
        //     "DontDestroyOnLoad only works for root GameObjects..."
        // and then did NOTHING — so this object died with the menu scene and the
        // gameplay scene came up with SessionConfig.Instance == null. CoopManager then
        // never learned that co-op had been requested and every run started solo, with
        // no error beyond that one easily-missed warning.
        //
        // Detaching first makes persistence work regardless of where the object was
        // authored in the menu hierarchy.
        if (transform.parent != null)
        {
            Debug.Log($"[SessionConfig] Detaching from '{transform.parent.name}' so " +
                      "DontDestroyOnLoad can actually persist this object across the scene load.");
            transform.SetParent(null, true);
        }

        DontDestroyOnLoad(gameObject);
    }

    /// Convenience for menu buttons: set the player count for the next run.
    public void SetPlayerCount(int count)
    {
        TargetPlayerCount = Mathf.Clamp(count, 1, 2);
    }
}


