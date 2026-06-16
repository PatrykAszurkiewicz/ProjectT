using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Central co-op coordinator for the gameplay scene (Phase 2).
//  - LEGACY mode (no PlayerInputManager found) does nothing 
//  - MANAGED mode (a PlayerInputManager is present): pushes the Player prefab to
//    the manager, reacts to joins, assigns each player's PlayerIndex, spawns a
//    per-player camera, and lays out the vertical split.
// Cameras are resolved through the ICoopCamera interface, so this class never
// references Cinemachine. The actual camera component (PlayerCinemachineSplitScreen
// for Cinemachine, or PlayerCameraController for a simple follow) does the work.
public class CoopManager : MonoBehaviour
{
    public static CoopManager Instance { get; private set; }

    [Header("Editor testing")]
    [Tooltip("Editor-only: force co-op (2 players) when you press Play directly without a menu. No effect in builds.")]
    [SerializeField] private bool forceCoopInEditor = false;

    [Tooltip("Editor/standalone convenience: auto-join local devices at Start (KB+M as P1, a gamepad as P2). Turn OFF if a lobby pre-binds devices.")]
    [SerializeField] private bool autoJoinDevicesOnStart = true;

    [Header("Spawning (managed mode)")]
    [Tooltip("Log spawn/camera diagnostics to the Console.")]
    [SerializeField] private bool debugLog = true;

    [Tooltip("The Player prefab to spawn. Assign your Player.prefab here — CoopManager pushes it onto the PlayerInputManager at runtime.")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("Optional. The PlayerInputManager. Leave null to auto-find one in the scene.")]
    [SerializeField] private PlayerInputManager playerInputManager;

    [Tooltip("Per-player render camera prefab (a duplicate of your Main Camera carrying PlayerCinemachineSplitScreen, or a Camera + PlayerCameraController). Spawned once per player.")]
    [SerializeField] private GameObject playerCameraPrefab;

    public int TargetPlayerCount { get; private set; } = 1;
    public bool CoopEnabled => TargetPlayerCount > 1;
    public bool ManagedMode { get; private set; }

    private readonly List<ICoopCamera> _cameras = new List<ICoopCamera>();
    private readonly HashSet<ICoopCamera> _spawnedCameras = new HashSet<ICoopCamera>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        int target = 1;
        if (SessionConfig.Instance != null)
            target = Mathf.Clamp(SessionConfig.Instance.TargetPlayerCount, 1, 2);

#if UNITY_EDITOR
        if (forceCoopInEditor) target = 2;
#endif
        TargetPlayerCount = target;
    }

    private void Start()
    {
        if (playerInputManager == null)
            playerInputManager = FindFirstObjectByType<PlayerInputManager>();

        if (playerInputManager == null)
        {
            ManagedMode = false;
            return;
        }

        ManagedMode = true;

        if (playerInputManager.playerPrefab == null && playerPrefab != null)
            playerInputManager.playerPrefab = playerPrefab;

        playerInputManager.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

        playerInputManager.onPlayerJoined += HandlePlayerJoined;
        playerInputManager.onPlayerLeft += HandlePlayerLeft;

        foreach (var pi in PlayerInput.all)
            HandlePlayerJoined(pi);

        UpdateJoinGate();

        if (autoJoinDevicesOnStart)
            AutoJoinLocalDevices();

        // Hot-plug: a pad connected after Start takes over the keyboard player.
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        if (playerInputManager != null)
        {
            playerInputManager.onPlayerJoined -= HandlePlayerJoined;
            playerInputManager.onPlayerLeft -= HandlePlayerLeft;
        }
        if (Instance == this) Instance = null;
    }

    //  Join / leave 

    private void HandlePlayerJoined(PlayerInput pi)
    {
        if (pi == null) return;

        var pref = pi.GetComponent<PlayerRef>();
        if (pref == null) pref = pi.GetComponentInChildren<PlayerRef>();
        if (pref != null)
        {
            pref.PlayerIndex = pi.playerIndex;
            PlayerRegistry.ResortByIndex();
        }

        if (debugLog)
        {
            string devs = "";
            foreach (var d in pi.devices) devs += d.displayName + " ";
            Debug.Log($"[CoopManager] JOINED idx={pi.playerIndex} name='{pi.gameObject.name}' " +
                      $"pos={pi.transform.position} scheme='{pi.currentControlScheme}' devices=[{devs.Trim()}] " +
                      $"PlayerRef={(pref != null ? "yes" : "NULL")} actionsEnabled={pi.inputIsActive}");
        }

        // A camera carried by the player prefab? (simple-follow setups). Else
        // spawn the per-player render camera prefab (Cinemachine setups).
        ICoopCamera cam = pi.GetComponentInChildren<ICoopCamera>(true);
        if (cam == null && playerCameraPrefab != null)
        {
            var camGO = Instantiate(playerCameraPrefab);
            cam = camGO.GetComponent<ICoopCamera>();
            if (cam == null)
            {
                Debug.LogError("[CoopManager] playerCameraPrefab has no ICoopCamera component (PlayerCinemachineSplitScreen or PlayerCameraController).");
                Destroy(camGO);
            }
            else
            {
                _spawnedCameras.Add(cam);
            }
        }

        if (cam != null)
        {
            // Establish owner now; RecomputeCameraLayout re-applies with the
            // correct index/total once everyone is known.
            cam.Configure(pref, pref != null ? pref.PlayerIndex : pi.playerIndex, 1);
            if (!_cameras.Contains(cam)) _cameras.Add(cam);

            if (debugLog)
            {
                var camMb = cam as MonoBehaviour;
                // GetComponent by string needs no Cinemachine compile reference.
                bool hasBrain = camMb != null && camMb.GetComponent("CinemachineBrain") != null;
                Debug.Log($"[CoopManager] camera bound for idx={pi.playerIndex}: " +
                          $"component={cam.GetType().Name} " +
                          $"followTarget={(pref != null ? pref.name : "NULL — camera will NOT follow")} " +
                          $"hasCinemachineBrain={hasBrain}" +
                          (hasBrain ? "  <-- REMOVE the CinemachineBrain from the PlayerCamera prefab; it overrides the follow!" : ""));
            }
        }
        else if (debugLog)
        {
            Debug.LogWarning("[CoopManager] No ICoopCamera for this player (no camera on prefab and playerCameraPrefab missing/invalid).");
        }

        RecomputeCameraLayout();
        UpdateJoinGate();
    }

    private void HandlePlayerLeft(PlayerInput pi)
    {
        if (pi == null) return;

        ICoopCamera cam = pi.GetComponentInChildren<ICoopCamera>(true);
        if (cam != null) RemoveCamera(cam);

        // Also drop any spawned camera whose owner is this player.
        ICoopCamera orphan = null;
        foreach (var c in _cameras)
            if (c != null && c.Owner != null && pi.GetComponentInChildren<PlayerRef>() == c.Owner) { orphan = c; break; }
        if (orphan != null) RemoveCamera(orphan);

        RecomputeCameraLayout();
        UpdateJoinGate();
    }

    private void RemoveCamera(ICoopCamera cam)
    {
        _cameras.Remove(cam);
        if (_spawnedCameras.Remove(cam) && cam is MonoBehaviour mb && mb != null)
            Destroy(mb.gameObject);
    }

    private void UpdateJoinGate()
    {
        if (playerInputManager == null) return;
        if (playerInputManager.playerCount >= TargetPlayerCount)
            playerInputManager.DisableJoining();
        else
            playerInputManager.EnableJoining();
    }

    private void RecomputeCameraLayout()
    {
        // Drop destroyed entries (interface refs don't auto-null on destroy).
        _cameras.RemoveAll(c => c == null || (c as MonoBehaviour) == null);

        _cameras.Sort((a, b) =>
        {
            int ia = a.Owner != null ? a.Owner.PlayerIndex : 0;
            int ib = b.Owner != null ? b.Owner.PlayerIndex : 0;
            return ia.CompareTo(ib);
        });

        int total = _cameras.Count;
        for (int i = 0; i < total; i++)
            _cameras[i].Configure(_cameras[i].Owner, i, total);
    }

    //  Auto-join (editor / no-lobby convenience) 

    private void AutoJoinLocalDevices()
    {
        if (playerInputManager == null) return;
        if (playerInputManager.playerPrefab == null)
        {
            Debug.LogWarning("[CoopManager] No Player prefab assigned — cannot auto-join. Assign 'playerPrefab' on CoopManager.");
            return;
        }

        // Single player: ONE PlayerInput that uses keyboard+mouse AND a gamepad,
        // auto-switching to whichever the player last used (classic 1P feel).
        // Joining on Keyboard&Mouse makes the keyboard work immediately, and
        // leaving auto-switch enabled lets a gamepad button take over on use.
        if (TargetPlayerCount <= 1)
        {
            if (playerInputManager.playerCount < 1)
            {
                PlayerInput solo;
                if (Keyboard.current != null && Mouse.current != null)
                    solo = playerInputManager.JoinPlayer(-1, -1, "Keyboard&Mouse", Keyboard.current, Mouse.current);
                else
                    solo = playerInputManager.JoinPlayer();   // pad-only machine

                if (solo != null) solo.neverAutoSwitchControlSchemes = false;
            }
            return;
        }

        // Gamepads-first: give each player slot its own pad if available, and
        // use Keyboard&Mouse only as a fallback for one remaining slot. So:
        //   2 pads        -> P1 = pad1, P2 = pad2
        //   1 pad         -> P1 = pad1, P2 = Keyboard&Mouse
        //   0 pads        -> P1 = Keyboard&Mouse
        int padCursor = 0;
        bool kbmUsed = false;

        while (playerInputManager.playerCount < TargetPlayerCount)
        {
            Gamepad pad = NextUnpairedGamepad(ref padCursor);
            if (pad != null)
            {
                playerInputManager.JoinPlayer(-1, -1, "Gamepad", pad);
            }
            else if (!kbmUsed && Keyboard.current != null && Mouse.current != null && !DeviceInUse(Keyboard.current))
            {
                kbmUsed = true;
                playerInputManager.JoinPlayer(-1, -1, "Keyboard&Mouse", Keyboard.current, Mouse.current);
            }
            else
            {
                Debug.LogWarning($"[CoopManager] Only {playerInputManager.playerCount}/{TargetPlayerCount} players could auto-join " +
                                 "(not enough devices). Connect another gamepad and it will take over the keyboard player.");
                break;
            }
        }

        MakeNonFirstPlayersFlexible();
    }

    // Co-op: let every player AFTER the first be driven by keyboard+mouse OR any
    // UNPAIRED gamepad, auto-switching between them as the user touches a device.
    // The Input System only auto-switches to devices that aren't paired to
    // someone else, so a flexible P2 can never steal P1's controller. P1 stays
    // locked to its own device (auto-switch off) so it's always protected.
    private void MakeNonFirstPlayersFlexible()
    {
        foreach (var pi in PlayerInput.all)
            if (pi != null && pi.playerIndex >= 1)
                pi.neverAutoSwitchControlSchemes = false;
    }

    // ---- Hot-plug: a pad connected after Start takes over the KB+M player ---

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!ManagedMode) return;
        // Single player auto-switches between keyboard and gamepad on its own —
        // don't re-pair or spawn anyone when a pad is plugged in.
        if (TargetPlayerCount <= 1) return;
        if (change != InputDeviceChange.Added && change != InputDeviceChange.Reconnected) return;
        if (!(device is Gamepad pad)) return;
        if (DeviceInUse(pad)) return;

        // If a player slot is still empty (e.g. started with too few devices),
        // fill it with the new pad. Otherwise do nothing: the flexible players
        // auto-switch to an unpaired pad the moment it's used, so plugging a pad
        // in and pressing it hands it to P2 without us re-pairing anything.
        if (playerInputManager != null && playerInputManager.playerCount < TargetPlayerCount)
        {
            EnableJoiningTemporarilyAndJoin(pad);
            if (debugLog)
                Debug.Log($"[CoopManager] Joined a new player on '{pad.displayName}'.");
        }
    }

    private void EnableJoiningTemporarilyAndJoin(Gamepad pad)
    {
        bool wasDisabled = !playerInputManager.joiningEnabled;
        if (wasDisabled) playerInputManager.EnableJoining();
        playerInputManager.JoinPlayer(-1, -1, "Gamepad", pad);
        MakeNonFirstPlayersFlexible();
        UpdateJoinGate(); // re-applies the disable if we're now at target
    }

    private static Gamepad NextUnpairedGamepad(ref int cursor)
    {
        var all = Gamepad.all;
        while (cursor < all.Count)
        {
            var pad = all[cursor++];
            if (!DeviceInUse(pad)) return pad;
        }
        return null;
    }

    private static bool DeviceInUse(InputDevice device)
    {
        foreach (var pi in PlayerInput.all)
            foreach (var d in pi.devices)
                if (d == device) return true;
        return false;
    }
}

