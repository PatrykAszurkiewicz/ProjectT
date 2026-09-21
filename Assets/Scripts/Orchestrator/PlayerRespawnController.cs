using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// How player death is handled for the whole game.
public enum PlayerLifeMode
{
    /// Death is temporary — the player comes back after a countdown that
    /// grows a little longer every time (default).
    Respawnable = 0,

    /// Death is permanent — the run ends exactly like losing the central core.
    NonRespawnable = 1,
}

// Single source of truth for the Respawnable / Non-Respawnable option.

public static class PlayerLifeSettings
{
    public const string PrefsKey = "PlayerLifeMode";

    /// Raised whenever the mode changes (menu toggle, debug cheat, load).
    public static event Action<PlayerLifeMode> OnModeChanged;

    // null = not read from PlayerPrefs yet. Cached so the death path never hits
    // PlayerPrefs on a gameplay frame.
    private static PlayerLifeMode? _cached;

    /// The active mode. Defaults to <see cref="PlayerLifeMode.Respawnable"/>.
    public static PlayerLifeMode Mode
    {
        get
        {
            if (_cached == null)
            {
                int raw = PlayerPrefs.GetInt(PrefsKey, (int)PlayerLifeMode.Respawnable);
                _cached = raw == (int)PlayerLifeMode.NonRespawnable
                    ? PlayerLifeMode.NonRespawnable
                    : PlayerLifeMode.Respawnable;
            }
            return _cached.Value;
        }
        set
        {
            if (_cached != null && _cached.Value == value) return;
            _cached = value;
            PlayerPrefs.SetInt(PrefsKey, (int)value);
            PlayerPrefs.Save();
            OnModeChanged?.Invoke(value);
        }
    }

    /// Convenience: true while players respawn instead of dying for good.
    public static bool RespawnEnabled => Mode == PlayerLifeMode.Respawnable;

    public static void SetRespawnable(bool respawnable)
        => Mode = respawnable ? PlayerLifeMode.Respawnable : PlayerLifeMode.NonRespawnable;

    public static void Toggle() => SetRespawnable(!RespawnEnabled);

    /// Menu-friendly label for the current mode.
    public static string DisplayName(PlayerLifeMode mode)
        => mode == PlayerLifeMode.NonRespawnable ? "Non-Respawnable Player" : "Respawnable Player";

    public static string CurrentDisplayName => DisplayName(Mode);

    /// Short explanation, handy for a tooltip / sub-label under the toggle.
    public static string Description(PlayerLifeMode mode)
        => mode == PlayerLifeMode.NonRespawnable
            ? "Death is permanent. Losing your last player ends the run, just like losing the core."
            : "Death is temporary. You return after a short delay that grows with every respawn.";

    // Statics survive "Enter Play Mode without domain reload", and a stale
    // subscriber list would keep destroyed menu objects alive. Same pattern the
    // rest of the project uses (PlayerRegistry, PlayerAttack).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _cached = null;      // re-read from PlayerPrefs on first access
        OnModeChanged = null;
    }
}

// RESPAWNABLE MODE — per-player respawn with an escalating back-off.
// Instead of destroying the player object (which would take its camera, its
// PlayerInput seat, its weapon and every augment component with it), a
// respawning player is put into the EXISTING co-op downed state:
// PlayerDownedState already freezes control, drops the colliders so enemies
// walk over the body, pins HP at 0 so PlayerRegistry stops targeting them, and
// plays the prone animation. CoopManager already knows to keep a downed
// player's split-screen camera alive. We just start a clock, and when it runs
// out we reuse PlayerDownedState.Revive() — the same code path a teammate
// revive uses — after teleporting the body to the respawn point.
// BACK-OFF
//   delay = baseRespawnSeconds + backoffIncrementSeconds * (respawns so far)
//   defaults: 8s, then 10s, then 12s, 14s … capped at maxRespawnSeconds.
// The counter is per player and lives on the player object, so it resets
// naturally when a new run loads a fresh scene.
// The component is auto-added on first death (like PlayerDownedState), so no
// prefab work is required. Add it to Player.prefab explicitly if you want to
// tune the values in the Inspector.
[DisallowMultipleComponent]
public class PlayerRespawnController : MonoBehaviour
{
    public enum RespawnPlace
    {
        /// Come back exactly where you fell.
        WhereTheyFell,
        /// Come back at the position this player occupied when the scene loaded.
        InitialSpawnPoint,
        /// Come back next to the central core (safest — it always exists and is inside the map).
        NearCentralCore,
    }

    [Header("Timing (back-off)")]
    [Tooltip("Delay of the FIRST respawn, in seconds.")]
    [Min(0f)] public float baseRespawnSeconds = 8f;

    [Tooltip("Added to the delay for every respawn this player has already used. " +
             "2 = 8s, 10s, 12s, 14s …")]
    [Min(0f)] public float backoffIncrementSeconds = 2f;

    [Tooltip("Upper bound on the delay so a long run doesn't end in minute-long waits. " +
             "0 or less = no cap.")]
    public float maxRespawnSeconds = 60f;

    [Header("On respawn")]
    [Tooltip("Fraction of max health restored when respawning.")]
    [Range(0.05f, 1f)] public float respawnHealthPercent = 1f;

    [Tooltip("Also refill stamina, mana and dashes.")]
    public bool refillPoolsOnRespawn = true;

    [Tooltip("Seconds of damage immunity granted on respawn, so you don't die again " +
             "the instant you land in a crowd. Uses the same TemporaryReviveImmunity " +
             "component the co-op revive uses.")]
    [Min(0f)] public float respawnInvulnerabilitySeconds = 3f;

    [Header("Where")]
    public RespawnPlace place = RespawnPlace.NearCentralCore;

    [Tooltip("Optional explicit respawn transform. Wins over everything else when set.")]
    public Transform respawnPointOverride;

    [Tooltip("Co-op: respawn next to a teammate who is still alive, rather than at the " +
             "place chosen above. Falls back to that place when nobody is up.")]
    public bool preferLivingTeammate = true;

    [Tooltip("Random offset around the chosen point, so two players respawning at once " +
             "don't land inside each other.")]
    [Min(0f)] public float respawnScatterRadius = 1.25f;

    [Header("Co-op")]
    [Tooltip("ON: a downed co-op player auto-respawns if no teammate reaches them in time " +
             "(teammate revive still works and cancels the timer).\n" +
             "OFF: respawning applies to single player only — in co-op a downed player " +
             "waits for a teammate, and a team wipe is game over even in Respawnable mode.")]
    public bool autoRespawnInCoop = true;

    [Header("UI")]
    [Tooltip("Float a countdown bar above the body. Reuses ReviveProgressBar so it matches " +
             "the co-op revive bar; sits slightly higher so both can show at once.")]
    public bool showCountdownBar = true;
    public Color countdownColor = new Color(1f, 0.78f, 0.25f, 1f);
    public Vector3 countdownBarOffset = new Vector3(0f, 1.45f, 0f);

    /// True while this player is waiting out a respawn countdown.
    public bool IsRespawnPending { get; private set; }

    /// Seconds left on the current countdown (0 when none).
    public float SecondsRemaining => IsRespawnPending ? Mathf.Max(0f, _remaining) : 0f;

    /// Length of the current countdown, for a 0..1 progress readout.
    public float SecondsTotal => _total;

    /// How many times this player has already respawned this run.
    public int RespawnCount { get; private set; }

    /// Ticks every frame of the countdown: (stats, secondsRemaining, secondsTotal).
    public event Action<PlayerStats, float, float> OnCountdown;

    /// Raised on this player after a completed respawn.
    public event Action<PlayerStats> OnRespawned;

    /// Global hooks for HUD / audio. Resolve which player via PlayerRef.
    public static event Action<PlayerStats, float> AnyRespawnScheduled;   // (stats, delay)
    public static event Action<PlayerStats> AnyRespawned;

    // Every live controller, so the downed-state team-wipe check can ask
    // "is anybody coming back?" without a scene search on the death frame.
    private static readonly List<PlayerRespawnController> _live = new List<PlayerRespawnController>();

    private PlayerStats _stats;
    private PlayerDownedState _downed;
    private Rigidbody2D _rb;
    private Vector3 _initialSpawn;
    private Coroutine _routine;
    private float _remaining;
    private float _total;
    private ReviveProgressBar _bar;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _live.Clear();
        AnyRespawnScheduled = null;
        AnyRespawned = null;
    }

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _rb = GetComponent<Rigidbody2D>();
        _initialSpawn = transform.position;
    }

    private void OnEnable()
    {
        if (!_live.Contains(this)) _live.Add(this);
    }

    private void OnDisable()
    {
        _live.Remove(this);
        CancelPending();
    }

    private void OnDestroy()
    {
        _live.Remove(this);
        DestroyBar();
    }

    //  ENTRY POINT (called from PlayerStats.Die)
    /// Try to turn this death into a respawn. Returns TRUE if the death was
    /// absorbed — the caller must then NOT run its own death handling.
    /// Returns false when the mode is Non-Respawnable, the run is already over,
    /// or co-op auto-respawn is switched off, so the existing death / downed
    /// paths run exactly as they did before this feature existed.
    public static bool TryBeginRespawn(PlayerStats stats)
    {
        if (stats == null) return false;
        if (!PlayerLifeSettings.RespawnEnabled) return false;
        if (RunHasEnded()) return false;

        var ctrl = stats.GetComponent<PlayerRespawnController>();
        if (ctrl == null) ctrl = stats.gameObject.AddComponent<PlayerRespawnController>();
        return ctrl.BeginRespawn();
    }

    /// True if ANY player is waiting on a respawn. PlayerDownedState uses this so
    /// "everyone is down" isn't mistaken for a team wipe when the team is coming back.

    public static bool AnyRespawnPending()
    {
        for (int i = 0; i < _live.Count; i++)
            if (_live[i] != null && _live[i].IsRespawnPending) return true;
        return false;
    }

    /// Clear every pending respawn (used when the mode is switched mid-countdown).
    public static void CancelAllPending()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
            if (_live[i] != null) _live[i].CancelPending();
    }

    //  COUNTDOWN

    /// Put this player down and schedule their return. True if scheduled.
    public bool BeginRespawn()
    {
        if (IsRespawnPending) return true;                     // idempotent
        if (PlayerRegistry.Count > 1 && !autoRespawnInCoop) return false;

        if (_downed == null) _downed = GetComponent<PlayerDownedState>();
        if (_downed == null) _downed = gameObject.AddComponent<PlayerDownedState>();

        _total = NextDelay();
        _remaining = _total;

        // Mark PENDING before EnterDowned: EnterDowned runs the team-wipe check,
        // and that check asks AnyRespawnPending() to decide whether "everyone is
        // at 0 HP" means the run is lost or just that everyone is waiting.
        IsRespawnPending = true;

        if (!_downed.EnterDowned())
        {
            IsRespawnPending = false;
            return false;
        }

        AnyRespawnScheduled?.Invoke(_stats, _total);
        _routine = StartCoroutine(CountdownThenRespawn());
        return true;
    }

    private float NextDelay()
    {
        float d = baseRespawnSeconds + backoffIncrementSeconds * RespawnCount;
        if (maxRespawnSeconds > 0f) d = Mathf.Min(d, maxRespawnSeconds);
        return Mathf.Max(0f, d);
    }

    private IEnumerator CountdownThenRespawn()
    {
        while (_remaining > 0f)
        {
            // NOTE: these branches call ClearPending(), not CancelPending() —
            // we are INSIDE the coroutine, so stopping it is the `yield break`.

            // A teammate got there first — their revive already cleared the downed
            // state. Cancel silently; this does NOT count against the back-off.
            if (_downed == null || !_downed.IsDowned) { ClearPending(); yield break; }

            // Run ended (core died, victory, or the last player permanently died).
            if (RunHasEnded()) { ClearPending(); yield break; }

            // The option was switched to Non-Respawnable mid-countdown. Honour it
            // immediately: stop coming back, and re-evaluate the wipe now that
            // nobody is pending.
            if (!PlayerLifeSettings.RespawnEnabled)
            {
                ClearPending();
                PlayerDownedState.EvaluateTeamWipe();
                yield break;
            }

            // Scaled time on purpose: the countdown pauses with the game (augment
            // menus and the pause menu both run at timeScale 0).
            _remaining -= Time.deltaTime;
            UpdateBar();
            OnCountdown?.Invoke(_stats, Mathf.Max(0f, _remaining), _total);
            yield return null;
        }

        _routine = null;
        DoRespawn();
    }

    /// Stop a pending respawn without reviving. Safe to call any time.
    public void CancelPending()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        ClearPending();
    }

    // State half of a cancel, without touching the coroutine handle.
    private void ClearPending()
    {
        _routine = null;
        IsRespawnPending = false;
        _remaining = 0f;
        HideBar();
    }

    //  THE RESPAWN ITSELF

    private void DoRespawn()
    {
        IsRespawnPending = false;
        HideBar();

        if (_downed == null || !_downed.IsDowned) return;   // revived out from under us

        RespawnCount++;

        // Move FIRST, while the colliders are still disabled by the downed state —
        // no physics push-out, no chance of landing inside a tower or an enemy.
        Vector3 target = ResolveRespawnPosition();
        transform.position = target;
        if (_rb != null) _rb.position = target;

        // Anything that was steering the body when we died must let go.
        var movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.IsBeingGrappled = false;

        // Reuse the co-op revive verbatim: heals to a percentage, clears the prone
        // visual, re-enables colliders / input / aim / tower placer, grants
        // TemporaryReviveImmunity and raises OnRevived. One code path for
        // "a player is back on their feet" means one place for future bugs.
        float savedImmunity = _downed.reviveInvulnerabilitySeconds;
        _downed.reviveInvulnerabilitySeconds =
            Mathf.Max(savedImmunity, respawnInvulnerabilitySeconds);
        _downed.Revive(Mathf.Clamp(respawnHealthPercent, 0.05f, 1f));
        _downed.reviveInvulnerabilitySeconds = savedImmunity;

        if (refillPoolsOnRespawn) RefillPools();

        OnRespawned?.Invoke(_stats);
        AnyRespawned?.Invoke(_stats);
    }

    private void RefillPools()
    {
        if (_stats == null) return;
        _stats.currentStamina = _stats.maxStamina;
        _stats.currentMana = _stats.maxMana;
        _stats.dashesLeft = _stats.maxDashes;
    }

    private Vector3 ResolveRespawnPosition()
    {
        Vector3 basePos = transform.position;   // WhereTheyFell / last resort

        if (respawnPointOverride != null)
        {
            basePos = respawnPointOverride.position;
        }
        else if (preferLivingTeammate && TryGetLivingTeammatePosition(out Vector3 mate))
        {
            basePos = mate;
        }
        else
        {
            switch (place)
            {
                case RespawnPlace.InitialSpawnPoint:
                    basePos = _initialSpawn;
                    break;

                case RespawnPlace.NearCentralCore:
                    var core = FindFirstObjectByType<CentralCore>();
                    basePos = core != null ? core.transform.position : _initialSpawn;
                    break;

                case RespawnPlace.WhereTheyFell:
                default:
                    break;
            }
        }

        if (respawnScatterRadius > 0f)
        {
            Vector2 o = UnityEngine.Random.insideUnitCircle * respawnScatterRadius;
            basePos += new Vector3(o.x, o.y, 0f);
        }

        basePos.z = transform.position.z;   // keep our own sorting depth

        // Last step, after the scatter: none of the sources above knows where the
        // layout's walls are. WhereTheyFell can drop you back into the obstacle you
        // died against, the scatter can push an otherwise-fine point into one, and
        // _initialSpawn is the prefab's authored position, which predates the layout
        // entirely. Returns basePos unchanged whenever it is already clear.
        return PlayerSpawnSafety.Resolve(basePos, gameObject);
    }

    private bool TryGetLivingTeammatePosition(out Vector3 pos)
    {
        pos = default;
        var reg = PlayerRegistry.Instance;
        if (reg == null) return false;

        var all = reg.All;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null || p.gameObject == gameObject) continue;
            if (p.Stats == null || p.Stats.IsDead()) continue;
            pos = p.transform.position;
            return true;
        }
        return false;
    }

    private static bool RunHasEnded()
    {
        var orch = GameOrchestrator.Instance;
        if (orch == null) return false;
        return orch.CurrentState == GameOrchestrator.RunState.GameOver
            || orch.CurrentState == GameOrchestrator.RunState.Victory;
    }

    //  COUNTDOWN BAR (procedural — reuses the co-op revive bar)

    private void UpdateBar()
    {
        if (!showCountdownBar) return;

        if (_bar == null)
        {
            var go = new GameObject($"~RespawnBar_{name}");
            _bar = go.AddComponent<ReviveProgressBar>();
            _bar.fillColor = countdownColor;          // read by its lazy Build()
            _bar.worldOffset = countdownBarOffset;    // sits above the revive bar
        }

        float progress = _total > 0f ? 1f - Mathf.Clamp01(_remaining / _total) : 1f;
        _bar.Show(transform.position, progress);
    }

    private void HideBar()
    {
        if (_bar != null) _bar.Hide();
    }

    private void DestroyBar()
    {
        if (_bar != null) { Destroy(_bar.gameObject); _bar = null; }
    }

    [ContextMenu("DEBUG / Respawn now")]
    private void DebugRespawnNow()
    {
        if (IsRespawnPending) _remaining = 0f;
    }
}


