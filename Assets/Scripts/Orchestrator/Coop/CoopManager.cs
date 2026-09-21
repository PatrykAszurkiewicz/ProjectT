using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.SceneManagement;
using System.Collections;

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

    // Single-player: the one PlayerInput we bind to EVERY local device so the lone
    // character can be driven by keyboard+mouse OR a gamepad (both live at once). Held
    // so we can keep its binding mask widened and pair hot-plugged pads. Null in co-op.
    private PlayerInput _solo;
    private bool _reapplyingSoloMask;

    // True once we have run the per-scene setup at least once. Distinguishes "our own
    // Start is about to run" from "we are a persisted instance and a NEW scene just
    // loaded under us" — the two cases sceneLoaded cannot tell apart on its own.
    private bool _initialized;

    // Retry handle. A gameplay scene may not have its PlayerInputManager reachable on
    // the exact frame sceneLoaded fires (it can be created or enabled from another
    // object's Start), so we re-check for a few frames before declaring LEGACY mode.
    private Coroutine _retry;
    private const string SoloBindingGroups = "Keyboard&Mouse;Gamepad";

    private void Awake()
    {
        // DIAG (temporary): the duplicate branch used to be completely silent, so a
        // second scene load where the fresh CoopManager kills itself looked exactly
        // like "CoopManager never existed". The surviving instance's SCENE NAME is the
        // tell: 'DontDestroyOnLoad' means an earlier CoopManager was carried across the
        // load (most likely as a child of an object that persists itself, e.g. the one
        // SessionConfig detaches to the root) and its Start() will never run again.
        if (Instance != null && Instance != this)
        {
            if (Instance.gameObject.scene.name == "DontDestroyOnLoad")
                Debug.LogWarning($"[CoopManager] '{Instance.name}' is living in DontDestroyOnLoad, so " +
                                 $"this scene's copy is redundant and is being destroyed. The persisted " +
                                 "instance now re-seats players on every scene load, so this is survivable " +
                                 "— but it is NOT the intended layout. Something on that GameObject " +
                                 "(SessionConfig, most likely) is calling DontDestroyOnLoad and dragging " +
                                 "CoopManager along. Give that component its own GameObject.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        TargetPlayerCount = ResolveTargetPlayerCount();

        // This object can end up in DontDestroyOnLoad without asking for it — anything
        // sharing its GameObject that calls DontDestroyOnLoad takes us along (that is
        // exactly how 'CoopSystems' persisted: SessionConfig lives on it and detaches
        // itself to the root). A persisted CoopManager's Start() runs ONCE, in the first
        // gameplay scene, and never again — so every later load came up with no player,
        // no player camera, and Unity's "No cameras rendering" grey.
        //
        // Re-running the per-scene setup on each load makes us correct either way:
        // scene-local (the intended layout) or accidentally persistent.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Seating intent for the run that is ABOUT to start. Re-read on every scene load so
    // a co-op run followed by a solo run doesn't inherit the old count.
    private int ResolveTargetPlayerCount()
    {
        if (RunResumeIntent.Pending && RunResumeIntent.PlayerCount > 0)
            return Mathf.Clamp(RunResumeIntent.PlayerCount, 1, 2);   // continue/lobby is authoritative
        if (SessionConfig.Instance != null)
            return Mathf.Clamp(SessionConfig.Instance.TargetPlayerCount, 1, 2);
        return 1;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Not yet set up: we are a fresh instance in the scene that just loaded, and our
        // own Start() is a few moments away. Let it do the work — re-entering here would
        // seat the player twice.
        if (!_initialized) return;

        // Additive loads (overlays, UI scenes) don't replace the gameplay scene, so the
        // seating we already have is still valid.
        if (mode != LoadSceneMode.Single) return;

        ReinitializeForNewScene();
    }

    // Give the scene a few frames to produce a PlayerInputManager before concluding
    // there isn't one. Runs ONLY when the immediate attempt failed, so the normal case
    // still seats the player on the same frame as before — a delayed spawn would break
    // anything that resolves the player once in its own Start (GremlinSpawner does).
    private IEnumerator RetryInitialize()
    {
        for (int i = 0; i < 5; i++)
        {
            yield return null;
            if (TryInitializeForScene()) { _retry = null; yield break; }
        }
        _retry = null;
        ReportNoManager();
    }

    // A menu scene legitimately has no PlayerInputManager and never will. Only a
    // GAMEPLAY scene missing one is an error worth shouting about.
    private void ReportNoManager()
    {
        bool gameplayScene = FindFirstObjectByType<GameOrchestrator>() != null;

        if (gameplayScene)
        {
            Debug.LogError($"[CoopManager] No PlayerInputManager in gameplay scene " +
                           $"'{SceneManager.GetActiveScene().name}' → LEGACY mode: NO player will be " +
                           "spawned, and therefore no player camera ('No cameras rendering'). Note " +
                           "FindFirstObjectByType skips INACTIVE objects.");
        }
        else if (debugLog)
        {
            Debug.Log($"[CoopManager] Standing by in '{SceneManager.GetActiveScene().name}' — no " +
                      "PlayerInputManager here, which is normal for a menu scene. Seating resumes " +
                      "when a gameplay scene loads.");
        }
    }

    // Drop everything that belonged to the OLD scene, then set up again from scratch.
    // Every reference we hold across the load is dead: the PlayerInputManager is scene-
    // local and was destroyed with its scene, the player cameras went with it, and the
    // join/leave subscriptions pointed at that destroyed manager.
    private void ReinitializeForNewScene()
    {
        ClearSceneState();

        TargetPlayerCount = ResolveTargetPlayerCount();

        _initialized = false;
        if (_retry != null) { StopCoroutine(_retry); _retry = null; }
        if (!TryInitializeForScene()) _retry = StartCoroutine(RetryInitialize());
    }

    private void ClearSceneState()
    {
        if (playerInputManager != null)
        {
            playerInputManager.onPlayerJoined -= HandlePlayerJoined;
            playerInputManager.onPlayerLeft -= HandlePlayerLeft;
        }
        playerInputManager = null;

        _cameras.Clear();
        _spawnedCameras.Clear();

        if (_solo != null) _solo.onControlsChanged -= OnSoloControlsChanged;
        InputSystem.onDeviceChange -= OnSoloDeviceChange;
        _solo = null;
    }

    private void Start()
    {
        if (!TryInitializeForScene()) _retry = StartCoroutine(RetryInitialize());
    }

    /// <summary>
    /// Wire up to this scene's PlayerInputManager and seat the players.
    /// Returns false (quietly) when there is no PlayerInputManager to bind to — the
    /// caller decides whether that is a menu scene (fine) or a broken gameplay scene.
    /// </summary>
    private bool TryInitializeForScene()
    {
        _initialized = true;

        if (playerInputManager == null)
            playerInputManager = FindFirstObjectByType<PlayerInputManager>();

        if (playerInputManager == null)
        {
            ManagedMode = false;
            return false;
        }

        ManagedMode = true;

        // DIAG (temporary): proves Start actually ran, and on which objects.
        Debug.Log($"[CoopManager] Start on '{name}' (scene '{gameObject.scene.name}') — " +
                  $"PlayerInputManager='{playerInputManager.name}' (scene " +
                  $"'{playerInputManager.gameObject.scene.name}'), TargetPlayerCount={TargetPlayerCount}, " +
                  $"existing PlayerInput.all={PlayerInput.all.Count}, " +
                  $"playerPrefab={(playerPrefab != null ? playerPrefab.name : "NULL")}");

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

        return true;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_retry != null) { StopCoroutine(_retry); _retry = null; }

        if (playerInputManager != null)
        {
            playerInputManager.onPlayerJoined -= HandlePlayerJoined;
            playerInputManager.onPlayerLeft -= HandlePlayerLeft;
        }

        // Single-player binding teardown (no-ops if we never bound a solo player).
        InputSystem.onDeviceChange -= OnSoloDeviceChange;
        if (_solo != null) _solo.onControlsChanged -= OnSoloControlsChanged;

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

        // The prefab seats every player at one authored position. If the map has
        // ALREADY been built by the time we get here — a second player joining
        // mid-stage, or a scene where the layout goes up before seating — that
        // position can be inside a layout obstacle, and a body that starts fully
        // inside a static collider wedges instead of being pushed out.
        //
        // The reverse ordering (player first, map second) is the common one and is
        // handled on the map's side, at the end of TowerDefenseMap.GenerateMap.
        // Both are no-ops unless the player is genuinely inside solid geometry.
        // pref.gameObject rather than pi.gameObject: PlayerRef requires PlayerStats,
        // so it is always on the character root — the object that carries the body
        // collider we measure against. PlayerInput is normally on the same object,
        // but the lookup above already allows for it being on a child.
        PlayerSpawnSafety.EvacuateIfStuck(pref != null ? pref.gameObject : pi.gameObject);

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

        // Revive re-enables the player's PlayerInput, which re-fires onPlayerJoined.
        // Because HandlePlayerLeft kept the downed player's camera alive (see there),
        // this player already has one — REUSE it instead of spawning a second render
        // camera. (For prefab-carried cameras the GetComponentInChildren above already
        // found it; this covers the separately-spawned render-camera / Cinemachine case,
        // where the camera is not a child of the player.)
        if (cam == null && pref != null) cam = FindCameraForOwner(pref);

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

        // Co-op is fully seated for the whole run. When a player is DOWNED, the death
        // path disables its PlayerInput component to cut control — and disabling a
        // PlayerInput raises PlayerInput.OnDisable, which fires this onPlayerLeft even
        // though the player is still very much in the run, alive on-screen, and awaiting
        // a teammate revive. Tearing its camera down here is exactly what made the
        // survivor's view snap to full-screen.
        //
        // A downed player stays REGISTERED in PlayerRegistry by design (it's pinned at
        // 0 HP so AllDead() can count it), so "still registered" is a reliable signal
        // that this is a transient input toggle, not a genuine departure. In that case
        // keep the camera and leave the split layout untouched.
        if (IsStillSeated(pi))
        {
            if (debugLog)
                Debug.Log($"[CoopManager] Ignoring transient LEFT for idx={pi.playerIndex} " +
                          "(player still seated — downed/awaiting revive). Split layout preserved.");
            return;
        }

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

    // True if this PlayerInput's player object is still part of the run — i.e. its
    // PlayerRef is still registered in PlayerRegistry. A DOWNED player is kept
    // registered on purpose (pinned at 0 HP so AllDead() counts it) and its GameObject
    // stays active; only its control is cut. So a "left" event for a still-registered
    // player is a transient PlayerInput disable (down), not a genuine departure, and
    // its split-screen camera must be preserved.
    private static bool IsStillSeated(PlayerInput pi)
    {
        if (pi == null) return false;

        // includeInactive:true so this still resolves even if the death path went as
        // far as deactivating the object rather than just disabling PlayerInput.
        var pref = pi.GetComponentInChildren<PlayerRef>(true);
        if (pref == null) return false;

        var reg = PlayerRegistry.Instance;
        if (reg == null) return false;

        var all = reg.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] == pref) return true;
        return false;
    }

    // Find an already-registered camera bound to this player, so a revive-driven
    // rejoin reuses it instead of spawning a duplicate.
    private ICoopCamera FindCameraForOwner(PlayerRef owner)
    {
        if (owner == null) return null;
        for (int i = 0; i < _cameras.Count; i++)
            if (_cameras[i] != null && _cameras[i].Owner == owner) return _cameras[i];
        return null;
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

        // Single player: ONE PlayerInput driven by keyboard+mouse AND any gamepad,
        // both active at once. NOTE: we do NOT rely on PlayerInput's auto-switch — a
        // PlayerInputManager is present, so the join gate (DisableJoining once seated)
        // turns OFF the unpaired-device listening that auto-switch needs, and the pad
        // would stay dead. Instead we pair every local device to this one player and
        // widen its binding mask to span both groups (see BindSoloToAllDevices).
        if (TargetPlayerCount <= 1)
        {
            if (playerInputManager.playerCount < 1)
            {
                PlayerInput solo;
                if (Keyboard.current != null && Mouse.current != null)
                    solo = playerInputManager.JoinPlayer(-1, -1, "Keyboard&Mouse", Keyboard.current, Mouse.current);
                else
                    solo = playerInputManager.JoinPlayer();   // pad-only machine

                // DIAG (temporary): JoinPlayer returns NULL when the manager refuses the
                // join (join gate closed, maxPlayerCount already reached). Nothing fires
                // onPlayerJoined in that case, so the old code just fell through quietly.
                if (solo == null)
                    Debug.LogError("[CoopManager] JoinPlayer returned NULL — the PlayerInputManager " +
                                   "refused the join. Check its Max Player Count and whether joining " +
                                   "was left disabled. No player, no camera.");
                else
                    BindSoloToAllDevices(solo);
            }
            else
            {
                // DIAG (temporary): a leftover PlayerInput from a previous run would
                // block the join for the new one.
                Debug.LogWarning($"[CoopManager] Skipping solo auto-join: playerInputManager.playerCount " +
                                 $"is already {playerInputManager.playerCount}. If no player is visible, " +
                                 "that seat is a stale leftover from the previous run.");
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

    //  Single-player: bind ONE player to keyboard+mouse AND every gamepad 

    // Pair every local device to the single player and widen its binding mask so the
    // Keyboard&Mouse AND Gamepad binding groups are BOTH active. This replaces the old
    // auto-switch approach, which never fired in managed mode (the manager disables the
    // unpaired-device listening auto-switch depends on once the player is seated). Runs
    // only for a 1-player run, so co-op seating is unaffected.
    private void BindSoloToAllDevices(PlayerInput solo)
    {
        if (solo == null) return;
        _solo = solo;

        // We manage devices explicitly; don't let PlayerInput auto-switch schemes (that
        // would re-narrow the mask back to a single group and kill one of the inputs).
        solo.neverAutoSwitchControlSchemes = true;

        PairAllLocalDevicesToSolo();
        ApplySoloMask();

        // Re-assert the widened mask if something later narrows it (e.g. an input
        // component re-enable when pausing), and pair pads plugged in after start.
        solo.onControlsChanged += OnSoloControlsChanged;
        InputSystem.onDeviceChange += OnSoloDeviceChange;

        if (debugLog)
        {
            string devs = "";
            foreach (var d in solo.devices) devs += d.displayName + " ";
            Debug.Log($"[CoopManager] Single player bound to all local devices: [{devs.Trim()}] — keyboard+mouse and gamepad both active.");
        }
    }

    private void OnSoloControlsChanged(PlayerInput pi)
    {
        if (_reapplyingSoloMask) return;   // guard: our own re-apply must not loop
        ApplySoloMask();
    }

    private void OnSoloDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (_solo == null) return;
        if ((change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            && (device is Gamepad || device is Keyboard || device is Mouse))
        {
            PairAllLocalDevicesToSolo();
            ApplySoloMask();
        }
    }

    private void PairAllLocalDevicesToSolo()
    {
        if (_solo == null) return;
        var user = _solo.user;
        if (!user.valid) return;

        PairIfNeeded(user, Keyboard.current);
        PairIfNeeded(user, Mouse.current);
        foreach (var pad in Gamepad.all) PairIfNeeded(user, pad);
    }

    private static void PairIfNeeded(InputUser user, InputDevice device)
    {
        if (device == null) return;
        foreach (var d in user.pairedDevices)
            if (d == device) return;   // already paired — don't unpair/re-pair (avoids a device-lost flicker)
        InputUser.PerformPairingWithDevice(device, user);
    }

    // Widen the binding mask to BOTH groups so keyboard+mouse and gamepad bindings are
    // simultaneously live. Only PAIRED devices can fire, so XR/Joystick/Touch bindings
    // stay dormant even though they aren't listed here. Re-entrancy-guarded so
    // re-asserting the mask can't loop with onControlsChanged.
    private void ApplySoloMask()
    {
        if (_solo == null || _solo.actions == null) return;
        var cur = _solo.actions.bindingMask;
        if (cur.HasValue && cur.Value.groups == SoloBindingGroups) return;   // already correct

        _reapplyingSoloMask = true;
        _solo.actions.bindingMask = new InputBinding { groups = SoloBindingGroups };
        _reapplyingSoloMask = false;
    }

    // NOTE: There is intentionally no runtime device-change join here. Co-op always
    // starts fully seated — the start lobby (CoopStartLobby) and resume gate
    // (ContinueRunMenu) both wait for the required controllers BEFORE GameScene loads.
    // A controller lost mid-run is handled by ControllerDisconnectGuard (pause + re-pair),
    // not by spawning a new player. This removes the old mid-run surprise-split entirely.

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



