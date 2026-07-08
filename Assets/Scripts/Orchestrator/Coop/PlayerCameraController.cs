using UnityEngine;

// Per-player follow camera for split-screen co-op. One of these lives on each
// player's camera (a duplicate of your GameScene Main Camera, so render settings already match). It:
//    follows its owning player,
//    occupies half the screen via camera.rect (set by CoopManager),
//    registers itself as that player's PlayerRef.Camera (so aim/cursor read it
//     instead of Camera.main — no MainCamera tag needed),
//    disables its own Unity AudioListener (the single listener lives on the core).
// Single player: CoopManager gives it the full-screen rect, so it behaves like
// your old follow camera.
[RequireComponent(typeof(Camera))]
public class PlayerCameraController : MonoBehaviour, ICoopCamera
{
    public PlayerRef Owner => owner;

    [Header("Follow")]
    public Transform followTarget;
    [Tooltip("Camera offset from the player. Z must stay negative for a 2D ortho camera (e.g. -10).")]
    public Vector3 followOffset = new Vector3(0f, 0f, -10f);
    [Tooltip("Higher = snappier follow. ~8-12 feels close to a tight follow cam.")]
    public float followLerp = 10f;
    [Tooltip("Jump straight to the player on the first configured frame instead of easing in from wherever the camera spawned.")]
    public bool snapOnConfigure = true;

    [Header("Lens (optional)")]
    [Tooltip("If > 0, force this orthographic size. Leave 0 to keep whatever the duplicated camera already has.")]
    public float orthographicSize = 0f;

    [Header("Ownership")]
    public PlayerRef owner;
    [Tooltip("Only ONE listener should exist. In split-screen the listener lives on the core, so keep this false on player cameras.")]
    public bool ownsAudioListener = false;

    private Camera _cam;
    private bool _snapped;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (orthographicSize > 0f && _cam.orthographic)
            _cam.orthographicSize = orthographicSize;
        if (owner != null) owner.Camera = _cam;

        // Co-op: each player camera needs its OWN shake so its half can shake
        // independently. Auto-add if the prefab doesn't already carry one, so this
        // works without hand-editing the PlayerCamera prefab.
        if (GetComponent<CameraShake>() == null)
            gameObject.AddComponent<CameraShake>();
    }

    /// <summary>Bind to a player and apply the split rect. Called by CoopManager.</summary>
    public void Configure(PlayerRef player, int index, int totalPlayers)
    {
        owner = player;
        if (_cam == null) _cam = GetComponent<Camera>();

        if (player != null)
        {
            followTarget = player.transform;
            player.Camera = _cam;
        }

        ApplyViewportRect(index, totalPlayers);

        if (snapOnConfigure) _snapped = false; // re-snap when (re)configured
    }

    public void ApplyViewportRect(int index, int totalPlayers)
    {
        if (_cam == null) _cam = GetComponent<Camera>();

        if (totalPlayers <= 1)
        {
            _cam.rect = new Rect(0f, 0f, 1f, 1f);   // full screen
            return;
        }

        // Vertical split: P1 left half, P2 right half.
        _cam.rect = index == 0
            ? new Rect(0f, 0f, 0.5f, 1f)
            : new Rect(0.5f, 0f, 0.5f, 1f);
    }

    private void OnEnable()
    {
        if (!ownsAudioListener)
        {
            var listener = GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (followTarget == null) return;

        Vector3 desired = followTarget.position + followOffset;

        if (snapOnConfigure && !_snapped)
        {
            transform.position = desired;   // no fly-in on first frame
            _snapped = true;
            return;
        }

        float t = 1f - Mathf.Exp(-followLerp * Time.deltaTime); // frame-rate independent
        transform.position = Vector3.Lerp(transform.position, desired, t);
    }
}

