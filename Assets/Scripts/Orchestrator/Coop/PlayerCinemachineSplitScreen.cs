using UnityEngine;
using Unity.Cinemachine;


// Cinemachine-based per-player split-screen camera. Put this on a RENDER camera
// (a duplicate of your GameScene Main Camera that keeps its CinemachineBrain).
// One render camera is spawned per player by CoopManager.
//   • Each render camera's CinemachineBrain is put on its own channel
//     (P1 = Channel01, P2 = Channel02).
//   • Each player's follow vcam (the CinemachineCamera inside your Player
//     prefab) is set to output to the matching channel.
//   • A Brain only renders vcams on its channel, so P1's camera shows P1's
//     vcam and P2's shows P2's — your follow tuning is untouched.
//   • The camera's rect is set to full-screen (1 player) or left/right half.
// The single audio listener lives on the core, so this camera's AudioListener
// (if any) is disabled here.

[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(CinemachineBrain))]
public class PlayerCinemachineSplitScreen : MonoBehaviour, ICoopCamera
{
    [Tooltip("This player's follow vcam (the CinemachineCamera in the Player prefab). " +
             "Auto-found in the owning player's hierarchy if left null.")]
    [SerializeField] private CinemachineVirtualCameraBase vcam;

    public PlayerRef owner;
    public PlayerRef Owner => owner;

    private Camera _cam;
    private CinemachineBrain _brain;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        _brain = GetComponent<CinemachineBrain>();

        // Exactly one audio listener should exist (on the core). Disable ours.
        var al = GetComponent<AudioListener>();
        if (al != null) al.enabled = false;

        if (owner != null) owner.Camera = _cam;

        // Co-op: each player camera needs its own shake so its half shakes
        // independently. CameraShake runs at execution order 1000, i.e. AFTER the
        // CinemachineBrain's LateUpdate, so its offset lands on top of the Brain.
        if (GetComponent<CameraShake>() == null)
            gameObject.AddComponent<CameraShake>();
    }

    public void Configure(PlayerRef player, int index, int totalPlayers)
    {
        owner = player;
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_brain == null) _brain = GetComponent<CinemachineBrain>();

        if (player != null)
        {
            player.Camera = _cam;
            if (vcam == null)
                vcam = player.GetComponentInChildren<CinemachineVirtualCameraBase>(true);
        }

        // Put this brain + the player's vcam on the same private channel.
        OutputChannels channel = ChannelFor(index);
        if (_brain != null) _brain.ChannelMask = channel;
        if (vcam != null) vcam.OutputChannel = channel;

        // Viewport: full screen for 1 player, vertical split for 2.
        _cam.rect = (totalPlayers <= 1)
            ? new Rect(0f, 0f, 1f, 1f)
            : (index == 0 ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0.5f, 0f, 0.5f, 1f));
    }

    private static OutputChannels ChannelFor(int index) => index switch
    {
        0 => OutputChannels.Channel01,
        1 => OutputChannels.Channel02,
        _ => OutputChannels.Channel01,
    };
}

