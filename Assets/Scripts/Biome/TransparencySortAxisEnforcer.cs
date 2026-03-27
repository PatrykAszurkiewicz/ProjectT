using UnityEngine;

/// <summary>
/// Attach to ANY GameObject in the scene (e.g. the BiomeManager or Camera).
/// On Awake, this forces the Transparency Sort Axis to Y=1 via script,
/// which guarantees the setting is correct even if Project Settings get reset.
///
/// Also provides runtime diagnostics (enable debugLogging in Inspector)
/// to verify sorting is working.
///
/// REMOVE THIS SCRIPT once sorting is confirmed working — the Project Settings
/// approach is cleaner for production. This is a belt-and-suspenders safety net.
/// </summary>
public class TransparencySortAxisEnforcer : MonoBehaviour
{
    [Header("Sort Axis (should be 0, 1, 0 for Y-sorting)")]
    public Vector3 transparencySortAxis = new Vector3(0f, 1f, 0f);

    [Header("Debug")]
    [Tooltip("Log sort diagnostic info every N seconds (0 = off)")]
    public float debugLogInterval = 0f;
    private float debugTimer = 0f;

    void Awake()
    {
        // Force the Transparency Sort Axis via script — overrides Project Settings
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = transparencySortAxis;
            Debug.Log($"[TransparencySortAxisEnforcer] Set camera '{cam.name}' " +
                      $"transparencySortMode=CustomAxis, axis={transparencySortAxis}");
        }
        else
        {
            // Also set globally via Graphics settings as fallback
            Debug.LogWarning("[TransparencySortAxisEnforcer] No main camera found at Awake. " +
                             "Will retry in Start.");
        }
    }

    void Start()
    {
        // Retry in case camera wasn't ready at Awake
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = transparencySortAxis;
        }
    }

    void Update()
    {
        if (debugLogInterval <= 0f) return;

        debugTimer += Time.deltaTime;
        if (debugTimer < debugLogInterval) return;
        debugTimer = 0f;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Debug.Log($"[SortDiag] Camera sortMode={cam.transparencySortMode}, " +
                      $"sortAxis={cam.transparencySortAxis}");
        }

        // Find the player and log its sort state
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Debug.Log($"[SortDiag] Player: pos.y={player.transform.position.y:F2}, " +
                          $"sortingLayer='{sr.sortingLayerName}', sortingOrder={sr.sortingOrder}, " +
                          $"sortPoint={sr.spriteSortPoint}");
            }
        }
    }
}
