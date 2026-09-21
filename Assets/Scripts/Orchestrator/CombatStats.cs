using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

//  CombatStats  (tracking + Win/Lose screen in one component)
// Attached to the WinLoseScreens GameObject (or any always-active
// object). It shows the Win/Lose child on the run outcome and fills it with the run's
// combat stats. Six stats are tracked:
//   Damage dealt by player   (per player; co-op splits P1/P2)
//   Damage taken by player   (per player)
//   DPS                      
//   Damage dealt by towers   (shared)
//   Damage taken by towers   (shared)
//   Enemies killed           (shared)
// All numbers are fed by report calls placed at the real damage SOURCES, so player
// and tower damage are cleanly separated instead of guessed:
//   Player dealt : Weapon (melee) + WeaponProjectile (ranged) + boss projectile
//                    intake in Boss1/Boss2. Attributed to the firing player.
//   Tower dealt  : Tower's four attack paths + the tower Projectile + boss intake.
//   Player taken : each player's CharacterStats.OnDamaged (auto, precise).
//   Tower taken  : Tower.TakeDamage (auto, one choke, post-armor).
//   Kills        : enemies whose HP reaches 0 (detected by a light periodic scan).
// Persistence: CaptureInto/RestoreFrom round-trip everything through RunSaveData
// (RunPersistence calls them at each wave-start autosave and on resume). Totals reset
// only when a brand-new run begins (RunPersistence.BeginRun), so the Win/Lose screen
// still shows the finished run and the next run starts clean.

[DisallowMultipleComponent]
public class CombatStats : MonoBehaviour
{
    public static CombatStats Instance { get; private set; }

    [Header("Screens")]
    [Tooltip("The WinLoseScreens object. Empty = the object this component is on.")]
    public GameObject winLoseRoot;
    [Tooltip("The 'Win' child. Empty = auto-find a child named 'Win'.")]
    public GameObject winScreen;
    [Tooltip("The 'Lose' child. Empty = auto-find a child named 'Lose'.")]
    public GameObject loseScreen;

    [Header("Stats Label")]
    [Tooltip("Cinzel-Black SDF font asset (Assets/Fonts/Cinzel/static/Cinzel-Black SDF.asset).")]
    public TMP_FontAsset statsFont;
    [Tooltip("Optional existing TMP label under Win. Empty = auto-create / reuse a child whose name contains 'stat'.")]
    public TMP_Text winStatsLabel;
    [Tooltip("Optional existing TMP label under Lose. Empty = auto-create / reuse a child whose name contains 'stat'.")]
    public TMP_Text loseStatsLabel;

    [Header("Auto-created label look")]
    [Tooltip("Smaller than a title so all six rows (and both players in co-op) fit.")]
    public float fontSize = 28f;
    public Color textColor = new Color(0.96f, 0.90f, 0.70f, 1f);
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Range(0f, 1f)] public float outlineWidth = 0.2f;
    public Vector2 anchorMin = new Vector2(0.12f, 0.05f);
    public Vector2 anchorMax = new Vector2(0.88f, 0.5f);

    [Header("Label consistency")]
    [Tooltip("Force BOTH the Win and Lose stat labels to the same layout (alignment, " +
             "size, anchors, margins) using the values above, so they always match even " +
             "if the two text objects in the scene were set up differently. Turn OFF to " +
             "keep each assigned label's own hand-tuned formatting.")]
    public bool matchLabelStyle = true;
    [Tooltip("Alignment applied when Match Label Style is on.")]
    public TextAlignmentOptions labelAlignment = TextAlignmentOptions.Center;

    [Header("Win screen overrides")]
    [Tooltip("The Win background is bright, so the Win stats use their own darker colour, " +
             "smaller size and lower position (to clear the 'YOU WON' logo). Lose keeps the " +
             "shared values above. Turn off to make Win use the shared style too.")]
    public bool winOverrides = true;
    [Tooltip("Greyish so it stays readable on the bright Win background.")]
    public Color winTextColor = new Color(0.28f, 0.29f, 0.33f, 1f);
    public float winFontSize = 29f;
    public Vector2 winAnchorMin = new Vector2(0.10f, 0.03f);
    public Vector2 winAnchorMax = new Vector2(0.90f, 0.42f);

    [Header("DPS")]
    [Tooltip("A player counts as 'actively attacking' for this many seconds after each " +
             "hit they land. DPS = their damage dealt ÷ that active time, so standing " +
             "idle between fights doesn't drag the number down.")]
    public float attackActiveWindow = 2f;

    [Header("Back to Main Menu button")]
    [Tooltip("Scene loaded when the Win/Lose 'Back to Main Menu' button is pressed. " +
             "Defaults to the scene MainMenu.BackToMainMenu() loads (\"MenuScene\"). " +
             "Change here if your menu scene is named differently.")]
    public string mainMenuScene = "MenuScene";
    [Tooltip("Text shown on the button.")]
    public string menuButtonLabel = "Back to Menu";

    [Tooltip("Resources path (NO file extension) of the button background sprite. " +
             "Points at the sprite you added under Assets/Resources/.")]
    public string menuButtonSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/Button 1";
    [Tooltip("Button size in pixels. ~2.5:1 keeps the sprite's look (source is 648×257).")]
    public Vector2 menuButtonSize = new Vector2(340f, 134f);
    [Tooltip("Inset in pixels from the RIGHT edge of the screen to the button.")]
    public float menuButtonRightMargin = 40f;
    [Tooltip("Inset in pixels from the BOTTOM edge of the screen to the button.")]
    public float menuButtonBottomMargin = 36f;
    [Tooltip("Max font size for the button label (auto-shrinks to fit).")]
    public float menuButtonFontSize = 28f;
    [Tooltip("Button label colour — light, so it reads on the dark button.")]
    public Color menuButtonTextColor = new Color(0.95f, 0.93f, 0.98f, 1f);

    [Header("Back to Main Menu button — smoky glow + purple/black threads")]
    [Tooltip("How far the smoky halo spreads beyond the button (x,y in px). It keeps the " +
             "button from sitting hard against the Win/Lose backgrounds.")]
    public Vector2 menuGlowPadding = new Vector2(190f, 150f);
    [Tooltip("Extra px (x,y) the woven THREAD ring reaches beyond the smoke halo, so the " +
             "filaments can drift past the smoke's edge as they weave.")]
    public Vector2 menuThreadPadding = new Vector2(58f, 46f);
    [Tooltip("Per-screen master INTENSITY for the WIN screen (bright background). Only the " +
             "ALPHA is used as an overall opacity multiplier — the effect's purple/black " +
             "palette is baked into the sprite, so it blends on any background.")]
    public Color menuGlowColorWin = new Color(1f, 1f, 1f, 0.92f);
    [Tooltip("Per-screen master INTENSITY for the LOSE screen (dark background). Only the " +
             "ALPHA is used as an overall opacity multiplier.")]
    public Color menuGlowColorLose = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Overall size of the smoke+thread aura, as a multiplier (1 = the button's own " +
             "size + padding). Lower = smaller aura; the button itself is never scaled. " +
             "Tune this live in the Inspector — drag it up for a bigger halo, down for tighter.")]
    [Range(0.35f, 1.6f)]
    public float menuAuraSize = 0.77f;
    [Tooltip("Seconds for the button to smoothly fade in when the screen appears.")]
    public float menuFadeInDuration = 0.55f;
    [Tooltip("Seconds per breathing pulse of the smoke halo. Set to 0 to fade in only, no pulse.")]
    public float menuPulsePeriod = 2.4f;
    [Tooltip("Peak sway of the thread layers, in degrees. They oscillate this far each way " +
             "(never a full spin) so the filaments read as slowly weaving, not rotating.")]
    public float menuThreadSwayDegrees = 7f;
    [Tooltip("Seconds per full weave cycle of the threads. The two thread layers use " +
             "slightly different periods so they drift against each other.")]
    public float menuThreadSwayPeriod = 9f;
    [Tooltip("How far (px) the thread layers gently drift while weaving, for parallax life.")]
    public float menuThreadDriftPixels = 9f;

    [Header("Debug")]
    public bool debugLog = false;

    //  Per-player accumulators 
    private class PlayerCombat
    {
        public float dealt, received;
        public float activeSeconds;   // time spent actively attacking (DPS denominator)
        public float lastDealtTime;   // Time.time of the last damage dealt (-1 = never)
    }
    private readonly Dictionary<int, PlayerCombat> _players = new Dictionary<int, PlayerCombat>();

    //  Shared (run-wide) accumulators 
    private float _towerDamageDealt;
    private float _towerDamageTaken;
    private int _enemiesKilled;

    private bool _subscribedOrch;
    private bool _resolvedScreens;

    // True while the Win/Lose screen is holding a gameplay freeze on UIModalStack.
    // Guards against a double push and lets OnDisable release it on a scene reload.
    private bool _frozenForOutcome;

    // Cached "Back to Main Menu" buttons (one per screen, like the stat labels) and
    // the lazily-loaded background sprite.
    private UnityEngine.UI.Button _winMenuButton, _loseMenuButton;
    private Sprite _menuButtonSprite;
    private bool _menuButtonSpriteLoaded;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        if (winLoseRoot == null) winLoseRoot = gameObject;
        ResolveScreens();
        SetActiveSafe(winScreen, false);
        SetActiveSafe(loseScreen, false);
    }

    private void OnEnable()
    {
        PlayerRegistry.OnPlayerJoined += OnPlayerJoined;
        PlayerRegistry.OnPlayerLeft += OnPlayerLeft;
        RescanPlayers();
        TrySubscribeOrchestrator();
    }

    private void OnDisable()
    {
        PlayerRegistry.OnPlayerJoined -= OnPlayerJoined;
        PlayerRegistry.OnPlayerLeft -= OnPlayerLeft;
        UntrackAllPlayers();
        UntrackAllEnemies();
        UnsubscribeOrchestrator();
        UnfreezeForOutcome();
    }

    private void Start() => TrySubscribeOrchestrator();

    private void OnDestroy()
    {
        UntrackAllPlayers();
        UntrackAllEnemies();
        if (Instance == this) Instance = null;
    }

    //  Public reporting API 

    public static void ReportPlayerDamageDealt(int playerIndex, float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        if (Instance == null) return;
        var pc = Instance.GetOrCreate(playerIndex);
        pc.dealt += amount;
        pc.lastDealtTime = Time.time;   // mark this player as actively attacking
    }

    public static void ReportPlayerDamageDealt(PlayerRef owner, float amount)
        => ReportPlayerDamageDealt(owner != null ? owner.PlayerIndex : 0, amount);

    /// <summary>Owner-aware with a position fallback: if owner is null, credit the nearest player.</summary>
    public static void ReportPlayerDamageDealt(PlayerRef owner, float amount, Vector3 hitPos)
    {
        int idx = owner != null ? owner.PlayerIndex : NearestPlayerIndex(hitPos);
        ReportPlayerDamageDealt(idx, amount);
    }

    public static void ReportPlayerDamageDealt(GameObject attacker, float amount)
        => ReportPlayerDamageDealt(ResolvePlayerIndex(attacker), amount);

    public static void ReportTowerDamageDealt(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        if (Instance == null) return;
        Instance._towerDamageDealt += amount;
    }

    public static void ReportTowerDamageTaken(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        if (Instance == null) return;
        Instance._towerDamageTaken += amount;
    }

    public static void ReportDamageReceived(int playerIndex, float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        if (Instance == null) return;
        Instance.GetOrCreate(playerIndex).received += amount;
    }

    //  Queries (read by the Win/Lose display) 

    public int PlayerCount
    {
        get
        {
            int fromRegistry = PlayerRegistry.Count;
            int fromCoop = (CoopManager.Instance != null && CoopManager.Instance.CoopEnabled) ? 2 : 1;
            int fromTracked = 1;
            foreach (var idx in _players.Keys) fromTracked = Mathf.Max(fromTracked, idx + 1);
            return Mathf.Clamp(Mathf.Max(Mathf.Max(fromRegistry, fromCoop), fromTracked), 1, 8);
        }
    }

    public float GetDamageDealt(int i) => _players.TryGetValue(i, out var pc) ? pc.dealt : 0f;
    public float GetDamageReceived(int i) => _players.TryGetValue(i, out var pc) ? pc.received : 0f;

    /// <summary>Real DPS: damage dealt ÷ seconds actually spent attacking.</summary>
    public float GetAverageDps(int i)
    {
        if (!_players.TryGetValue(i, out var pc)) return 0f;
        return pc.activeSeconds > 0.1f ? pc.dealt / pc.activeSeconds : 0f;
    }

    public float TowerDamageDealt => _towerDamageDealt;
    public float TowerDamageTaken => _towerDamageTaken;
    public int EnemiesKilled => _enemiesKilled;

    //  Run lifecycle (called by RunPersistence) 

    public void ResetForNewRun()
    {
        _players.Clear();
        _towerDamageDealt = 0f;
        _towerDamageTaken = 0f;
        _enemiesKilled = 0;
    }

    public void CaptureInto(RunSaveData data)
    {
        if (data == null) return;
        data.towerDamageDealt = _towerDamageDealt;
        data.towerDamageTaken = _towerDamageTaken;
        data.enemiesKilled = _enemiesKilled;
        if (data.players == null) return;
        foreach (var pe in data.players)
        {
            if (pe == null) continue;
            if (_players.TryGetValue(pe.playerIndex, out var pc))
            {
                pe.damageDealt = pc.dealt;
                pe.damageReceived = pc.received;
                pe.activeAttackSeconds = pc.activeSeconds;
            }
        }
    }

    public void RestoreFrom(RunSaveData data)
    {
        if (data == null) return;
        ResetForNewRun();
        _towerDamageDealt = Mathf.Max(0f, data.towerDamageDealt);
        _towerDamageTaken = Mathf.Max(0f, data.towerDamageTaken);
        _enemiesKilled = Mathf.Max(0, data.enemiesKilled);
        if (data.players == null) return;
        foreach (var pe in data.players)
        {
            if (pe == null) continue;
            var pc = GetOrCreate(pe.playerIndex);
            pc.dealt = Mathf.Max(0f, pe.damageDealt);
            pc.received = Mathf.Max(0f, pe.damageReceived);
            pc.activeSeconds = Mathf.Max(0f, pe.activeAttackSeconds);
        }
    }

    //  Internals: attribution 

    private PlayerCombat GetOrCreate(int i)
    {
        if (i < 0) i = 0;
        if (!_players.TryGetValue(i, out var pc)) { pc = new PlayerCombat { lastDealtTime = -999f }; _players[i] = pc; }
        return pc;
    }

    private static int ResolvePlayerIndex(GameObject attacker)
    {
        if (attacker == null) return 0;
        var pr = attacker.GetComponentInParent<PlayerRef>();
        return pr != null ? pr.PlayerIndex : 0;
    }

    private static int NearestPlayerIndex(Vector3 worldPos)
    {
        var reg = PlayerRegistry.Instance;
        if (reg == null) return 0;
        var ps = reg.NearestAlive((Vector2)worldPos, includeCloaked: true);
        if (ps == null) return 0;
        var pr = ps.GetComponent<PlayerRef>();
        return pr != null ? pr.PlayerIndex : 0;
    }

    //  Damage received: per-player OnDamaged 

    private class RecvHook { public int index; public Action<float> handler; }
    private readonly Dictionary<CharacterStats, RecvHook> _recv = new Dictionary<CharacterStats, RecvHook>();

    private void OnPlayerJoined(PlayerRef p) => TrackPlayer(p);
    private void OnPlayerLeft(PlayerRef p) { if (p != null) UntrackPlayer(p.Stats); }

    private void RescanPlayers()
    {
        var all = PlayerRegistry.Instance.All;
        for (int i = 0; i < all.Count; i++) TrackPlayer(all[i]);
    }

    private void TrackPlayer(PlayerRef p)
    {
        if (p == null || p.Stats == null) return;
        var stats = p.Stats;
        if (_recv.ContainsKey(stats)) return;
        int index = p.PlayerIndex;
        GetOrCreate(index);
        var hook = new RecvHook { index = index };
        hook.handler = dmg => ReportDamageReceived(index, dmg);
        stats.OnDamaged += hook.handler;
        _recv[stats] = hook;
    }

    private void UntrackPlayer(CharacterStats stats)
    {
        if (stats == null) return;
        if (_recv.TryGetValue(stats, out var h)) { stats.OnDamaged -= h.handler; _recv.Remove(stats); }
    }

    private void UntrackAllPlayers()
    {
        foreach (var kv in _recv) if (kv.Key != null) kv.Key.OnDamaged -= kv.Value.handler;
        _recv.Clear();
    }

    //  Enemies killed: enemy HP-reaches-zero detection 
    // A light periodic scan subscribes to each enemy's OnHealthChanged and counts a
    // kill the first time its HP hits 0. Damage attribution is done at the sources
    // (above), so this scan is ONLY for the kill count — bosses included.

    private class EnemyHook { public float lastMax; public bool counted; public Action<float, float> handler; }
    private readonly Dictionary<EnemyStats, EnemyHook> _enemies = new Dictionary<EnemyStats, EnemyHook>();
    private float _scanTimer;
    private const float ScanInterval = 0.2f;
    private static readonly List<EnemyStats> s_dead = new List<EnemyStats>();

    private void ScanEnemies()
    {
        s_dead.Clear();
        foreach (var kv in _enemies) if (kv.Key == null) s_dead.Add(kv.Key);
        for (int i = 0; i < s_dead.Count; i++) _enemies.Remove(s_dead[i]);

        var all = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var es = all[i];
            if (es == null || _enemies.ContainsKey(es)) continue;
            var hook = new EnemyHook { lastMax = es.maxHealth, counted = false };
            var captured = es;
            hook.handler = (cur, max) => OnEnemyHealth(captured, cur, max);
            es.OnHealthChanged += hook.handler;
            _enemies[es] = hook;
        }
    }

    private void OnEnemyHealth(EnemyStats es, float current, float max)
    {
        if (es == null || !_enemies.TryGetValue(es, out var h)) return;
        if (!Mathf.Approximately(max, h.lastMax)) { h.lastMax = max; return; }  // capacity change
        if (!h.counted && current <= 0.01f)
        {
            h.counted = true;
            _enemiesKilled++;
        }
    }

    private void UntrackAllEnemies()
    {
        foreach (var kv in _enemies) if (kv.Key != null) kv.Key.OnHealthChanged -= kv.Value.handler;
        _enemies.Clear();
    }

    //  Update: DPS active-time + enemy discovery + late-bind orchestrator 

    private static bool IsCombatState(GameOrchestrator.RunState s)
        => s == GameOrchestrator.RunState.WaveActive
        || s == GameOrchestrator.RunState.StageBoss
        || s == GameOrchestrator.RunState.FinalBoss;

    private void Update()
    {
        if (!_subscribedOrch) TrySubscribeOrchestrator();

        // DPS active-time: accumulate for any player who has attacked within the
        // active window. Uses scaled time so pauses don't inflate it.
        float now = Time.time;
        float dt = Time.deltaTime;
        foreach (var pc in _players.Values)
            if (pc.lastDealtTime > 0f && now - pc.lastDealtTime <= attackActiveWindow)
                pc.activeSeconds += dt;

        // Discover enemies for kill-counting (combat states only).
        var orch = GameOrchestrator.Instance;
        _scanTimer += Time.unscaledDeltaTime;
        if (_scanTimer >= ScanInterval)
        {
            _scanTimer = 0f;
            if (orch != null && IsCombatState(orch.CurrentState)) ScanEnemies();
        }
    }

    //  Orchestrator hookup (late-bind, like RunProgressBar) 

    private void TrySubscribeOrchestrator()
    {
        if (_subscribedOrch) return;
        var orch = GameOrchestrator.Instance;
        if (orch == null) return;
        orch.OnVictory += HandleVictory;
        orch.OnGameOver += HandleGameOver;
        _subscribedOrch = true;
    }

    private void UnsubscribeOrchestrator()
    {
        if (!_subscribedOrch) return;
        var orch = GameOrchestrator.Instance;
        if (orch != null) { orch.OnVictory -= HandleVictory; orch.OnGameOver -= HandleGameOver; }
        _subscribedOrch = false;
    }

    private void HandleVictory() => ShowOutcome(true);
    private void HandleGameOver() => ShowOutcome(false);

    private void ShowOutcome(bool win)
    {
        ResolveScreens();
        SetActiveSafe(winLoseRoot, true);
        SetActiveSafe(win ? loseScreen : winScreen, false);
        SetActiveSafe(win ? winScreen : loseScreen, true);

        var label = win ? EnsureLabel(ref winStatsLabel, winScreen, "WIN")
                        : EnsureLabel(ref loseStatsLabel, loseScreen, "LOSE");
        if (label != null) label.text = BuildStatsText();

        // Build (once) the "Back to Main Menu" button below the stats on this screen.
        if (win) EnsureMenuButton(winScreen, ref _winMenuButton, true);
        else EnsureMenuButton(loseScreen, ref _loseMenuButton, false);

        HideGameplayHud();

        // Freeze the run underneath so the player can't keep moving/acting behind the
        // Win/Lose screen. This is the same modal freeze every menu uses (and the same
        // one the augment menu uses to stop the player), so it stops scaled-time movement
        // and physics, and — because it registers as the frontmost modal — the pause menu
        // can't open beneath the outcome screen and un-freeze the run.
        FreezeForOutcome();

        if (debugLog) Debug.Log($"[CombatStats] Showing {(win ? "WIN" : "LOSE")} screen.");
    }

    // Push a gameplay freeze onto UIModalStack for the terminal Win/Lose screen.
    // We do NOT disable the players' PlayerInput to cut control: CoopManager /
    // ControllerDisconnectGuard read a disabled PlayerInput as a controller
    // *disconnect*, which would fire the disconnect guard. The modal freeze is the
    // project-wide "gameplay is paused" signal and stops movement without that.
    private void FreezeForOutcome()
    {
        if (_frozenForOutcome) return;
        _frozenForOutcome = true;

        // freeze only if a run is actually live (matches OptionsMenu / ContinueRunMenu).
        // The outcome screen fires inside the gameplay scene, so this is true here.
        UIModalStack.Push(this, freeze: UIModalStack.GameplayActive);

        // The timeScale freeze stops movement/physics, but attack and aim run off raw
        // input in Update with no Time.deltaTime, so they ignore it. Suppress both so the
        // player can't fire or rotate the cursor/aim under the Win/Lose screen. Both gates
        // also self-clear on the next scene load, so leaving the screen restores them.
        PlayerAttack.SetAllSuppressed(true);
        PlayerAim.SetAllSuppressed(true);
    }

    // Release the freeze. Called from OnDisable so a scene reload (Restart / Quit from
    // the Win/Lose buttons) can't strand a freeze layer on the stack. ScreenFade also
    // resets Time.timeScale to 1 on load, so gameplay is never left frozen.
    private void UnfreezeForOutcome()
    {
        if (!_frozenForOutcome) return;
        _frozenForOutcome = false;

        if (UIModalStack.Contains(this)) UIModalStack.Pop(this);

        PlayerAttack.SetAllSuppressed(false);
        PlayerAim.SetAllSuppressed(false);
    }

    //  Back to Main Menu button 

    // Creates a themed "Back to Main Menu" button pinned to the bottom-right corner of
    // the given screen (clear of the centred stats). Built once per screen and cached,
    // mirroring the Win/Lose stat-label pattern. A soft glow sits behind it (dark on the
    // bright Win screen, grey on the dark Lose screen) and the whole thing fades in and
    // gently pulses — driven on UNSCALED time so it animates while the run is frozen.
    private void EnsureMenuButton(GameObject screen, ref Button cached, bool isWin)
    {
        if (screen == null) return;
        if (cached != null) { cached.transform.parent.SetAsLastSibling(); return; }

        // A freshly-loaded gameplay scene may not have an EventSystem until a menu opens;
        // the button needs one to be clickable. EnsureEventSystem is idempotent.
        MenuTheme.EnsureEventSystem();

        // Container (bottom-right) — holds the glow + button, and owns the CanvasGroup we
        // fade and the WinLoseButtonFX that drives the animation.
        var container = new GameObject("BackToMainMenuFX", typeof(RectTransform));
        container.transform.SetParent(screen.transform, false);
        var crt = container.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f);   // bottom-right
        crt.pivot = new Vector2(1f, 0f);
        crt.sizeDelta = menuButtonSize;
        crt.anchoredPosition = new Vector2(-menuButtonRightMargin, menuButtonBottomMargin);
        var group = container.AddComponent<CanvasGroup>();
        group.alpha = 0f;   // faded in by the FX

        // Per-screen master intensity (alpha only — the purple/black palette is baked in).
        float master = Mathf.Clamp01((isWin ? menuGlowColorWin : menuGlowColorLose).a);

        // Helper: a centred, non-raycastable Image child sized off the button. menuAuraSize
        // scales the whole aura (threads + smoke) without touching the button itself.
        Image AddLayer(string layerName, Sprite sprite, Vector2 pad, float alpha,
                       out RectTransform outRt)
        {
            var lgo = new GameObject(layerName, typeof(RectTransform));
            lgo.transform.SetParent(container.transform, false);
            outRt = lgo.GetComponent<RectTransform>();
            outRt.anchorMin = outRt.anchorMax = new Vector2(0.5f, 0.5f);
            outRt.pivot = new Vector2(0.5f, 0.5f);
            outRt.sizeDelta = (menuButtonSize + pad * 2f) * Mathf.Max(0.1f, menuAuraSize);
            var im = lgo.AddComponent<Image>();
            im.sprite = sprite;
            im.type = Image.Type.Simple;
            im.raycastTarget = false;
            im.color = new Color(1f, 1f, 1f, alpha);   // white tint → show baked colour
            return im;
        }

        // 1) Soft smoky halo (breathes). Dark screen: purple→black cloud that vanishes on the
        // dark bg. Bright screen: a faint violet glow hugging the threads, open in the middle.
        var smoke = AddLayer("Smoke", SmokeSprite(isWin), menuGlowPadding, master, out var grt);

        // 2+3) Two woven THREAD layers sharing one baked sprite. They sway in opposite
        // directions (see WinLoseButtonFX) so the filaments read as slowly weaving. The extra
        // spread (added here as a constant so it applies regardless of Inspector values) gives
        // the curling tentacles room to reach well past the smoke body.
        Vector2 threadPad = menuGlowPadding + menuThreadPadding + new Vector2(90f, 66f);
        var threadA = AddLayer("ThreadsA", ThreadSprite(isWin), threadPad, master, out var trtA);
        var threadB = AddLayer("ThreadsB", ThreadSprite(isWin), threadPad, master * 0.85f, out var trtB);
        trtB.localScale = new Vector3(-1.06f, 1.06f, 1f);   // mirrored+larger → the two never align

        // Button itself, filling the container.
        var go = new GameObject("BackToMainMenuButton", typeof(RectTransform));
        go.transform.SetParent(container.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        var sprite = LoadMenuButtonSprite();
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = Color.white;
        }
        else
        {
            // Sprite missing (wrong path / not under a Resources folder): a solid dark
            // panel keeps the button usable rather than invisible.
            img.color = new Color(0.20f, 0.23f, 0.30f, 1f);
        }

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(OnBackToMainMenu);

        // Label
        var txtGO = new GameObject("Label", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(28f, 12f); trt.offsetMax = new Vector2(-28f, -12f);

        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text = menuButtonLabel;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 12f;
        tmp.fontSizeMax = menuButtonFontSize;
        tmp.color = menuButtonTextColor;
        tmp.raycastTarget = false;
        var f = ResolveFont();
        if (f != null) tmp.font = f;

        // Drive fade-in + breathing smoke + weaving threads on unscaled time (run frozen).
        var fx = container.AddComponent<WinLoseButtonFX>();
        fx.Init(group, smoke, grt, threadA, trtA, threadB, trtB,
                menuFadeInDuration, menuPulsePeriod,
                menuThreadSwayDegrees, menuThreadSwayPeriod, menuThreadDriftPixels);

        container.transform.SetAsLastSibling();   // draw above the stats label
        cached = btn;

        if (debugLog) Debug.Log($"[CombatStats] Built 'Back to Main Menu' button on '{screen.name}'.");
    }

    private Sprite LoadMenuButtonSprite()
    {
        if (_menuButtonSpriteLoaded) return _menuButtonSprite;
        _menuButtonSpriteLoaded = true;

        if (!string.IsNullOrEmpty(menuButtonSpritePath))
            _menuButtonSprite = Resources.Load<Sprite>(menuButtonSpritePath);

        if (_menuButtonSprite == null && debugLog)
            Debug.LogWarning($"[CombatStats] Button sprite not found at Resources/'{menuButtonSpritePath}' " +
                             "— using a solid fallback. (Path must be under a 'Resources' folder, no extension.)");
        return _menuButtonSprite;
    }

    // GLSL-style edge smoothstep: 0 below e0, 1 above e1, smooth in between. This is what
    // the glow math needs — deliberately NOT Unity's Mathf.SmoothStep (which lerps e0..e1).
    private static float S01(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // Purple palette sampled from the reference art: a deep purple-black core through mid
    // purple up to a vivid violet and a hot pink-violet highlight on the brightest threads.
    // Baking these into the sprites (rather than tinting a white glow) is what lets ONE
    // effect read on both bright and dark backgrounds: the near-black core shows against
    // bright screens, the vivid threads glow against dark ones.
    private static readonly Color PAL_BLACK = new Color(0.045f, 0.000f, 0.065f);
    private static readonly Color PAL_DARK = new Color(0.200f, 0.000f, 0.250f);
    private static readonly Color PAL_MID = new Color(0.360f, 0.011f, 0.430f);
    private static readonly Color PAL_VIVID = new Color(0.790f, 0.100f, 0.940f);
    private static readonly Color PAL_HOT = new Color(0.940f, 0.480f, 1.000f);

    // BRIGHT-screen palette. On a light background the dark/near-black smoke of the dark
    // palette just darkens to a muddy grey, so the Win screen uses saturated violets only
    // (saturated purple stays purple over white; dark grey-purple turns grey) — a light
    // glow colour, a deeper body, and a saturated vein colour instead of near-black.
    private static readonly Color PAL_B_GLOW = new Color(0.660f, 0.240f, 0.860f);
    private static readonly Color PAL_B_DEEP = new Color(0.520f, 0.090f, 0.720f);
    private static readonly Color PAL_B_VEIN = new Color(0.460f, 0.060f, 0.600f);

    // Ridged filament: folds Perlin noise into sharp thin "veins" (1 on the ridge line,
    // falling off fast). Higher power → thinner, crisper threads. This is the core of the
    // woven-thread look.
    private static float Ridged(float x, float y, float power)
    {
        float r = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x, y) - 1f);
        return Mathf.Pow(Mathf.Clamp01(r), power);
    }

    // Shared shape field for BOTH the smoke and the threads, so they always agree. The effect
    // is an ORGANIC, TENTACLED shape: a soft body that throws out a few fat, curling arms in
    // noise-chosen directions. The arms are built from an ANGULAR noise (varies by direction →
    // radial arms) sampled in a strongly domain-WARPED frame (so the arms bend and writhe
    // instead of shooting out straight). A hard corner guard on the true radius guarantees the
    // texture corners always stay transparent, so no matter how the arms fall it can never read
    // as a rectangle. The arm tips fade over a long soft edge so they dissolve into the bg.
    //   turb    – billowy turbulence (structure + internal erosion)
    //   fine    – high-freq noise (internal erosion)
    //   M       – tentacle mask (dark-screen smoke body); already eroded into wisps
    //   threadA – bright fine filaments, confined to the shape
    //   threadB – broad dark veins, confined to the shape
    private static void FxFields(float nx, float ny, bool bright,
                                 out float turb, out float fine, out float M,
                                 out float threadA, out float threadB)
    {
        float dx = (nx - 0.5f) * 2f;   // -1..1
        float dy = (ny - 0.5f) * 2f;   // -1..1
        float rTrue = Mathf.Sqrt(dx * dx + dy * dy);   // undistorted radius (corner guard)

        // Strong two-octave domain warp of the POSITION → arms curl and the whole shape is
        // asymmetric. This is deliberately large; the corner guard below keeps it safe.
        float wx = Mathf.PerlinNoise(nx * 1.7f + 15f, ny * 1.7f + 6f) * 2f - 1f;
        float wy = Mathf.PerlinNoise(nx * 1.7f + 23f, ny * 1.7f + 9f) * 2f - 1f;
        float wx2 = Mathf.PerlinNoise(nx * 3.3f + 60f, ny * 3.3f + 2f) * 2f - 1f;
        float wy2 = Mathf.PerlinNoise(nx * 3.3f + 80f, ny * 3.3f + 7f) * 2f - 1f;
        float WX = dx + 0.30f * wx + 0.12f * wx2;
        float WY = dy + 0.30f * wy + 0.12f * wy2;
        float rw = Mathf.Sqrt(WX * WX + WY * WY);

        // Angular noise in the WARPED frame → a few fat arms that bend. Powered lightly so the
        // arms are fat (not needle spikes) but still sparse (only some directions reach out).
        float thw = Mathf.Atan2(WY, WX);
        float caw = Mathf.Cos(thw);
        float saw = Mathf.Sin(thw);
        float an = Mathf.PerlinNoise(caw * 1.2f + 10f, saw * 1.2f + 20f);
        float an2 = Mathf.PerlinNoise(caw * 2.3f + 50f, saw * 2.3f + 5f);
        float armn = Mathf.Clamp01(0.62f * an + 0.38f * an2);
        float spike = Mathf.Pow(armn, 1.8f);
        float boundary = Mathf.Clamp(0.52f + 0.50f * spike, 0.42f, 1.0f);   // arms reach far

        fine = Mathf.PerlinNoise(nx * 15.0f + 21f, ny * 15.0f + 3f);
        turb = Mathf.Abs(2f * Mathf.PerlinNoise(nx * 2.6f + 3f, ny * 2.6f + 7f) - 1f) * 0.55f
             + Mathf.Abs(2f * Mathf.PerlinNoise(nx * 5.3f + 41f, ny * 5.3f + 22f) - 1f) * 0.30f
             + Mathf.Abs(2f * Mathf.PerlinNoise(nx * 10.4f + 9f, ny * 10.4f + 6f) - 1f) * 0.15f;

        // Hard corner guard on the TRUE radius: forces alpha to 0 by the corners no matter what
        // the warped arms do. Arms can still reach the mid-side edges; corners can't fill.
        float guard = 1f - S01(0.98f, 1.25f, rTrue);

        // The tentacle body: inside the arm boundary, long soft fade for wispy tips, eroded
        // internally into wisps, then corner-guarded.
        M = Mathf.Clamp01(1f - S01(boundary - 0.34f, boundary, rw));
        M *= Mathf.Clamp01(0.35f + 0.85f * turb + 0.25f * (fine - 0.5f));
        M = Mathf.Clamp01(M * guard);

        // Ridged filaments sampled in the warped frame → they flow along the arms.
        float tx = nx * 4.4f + 1.9f * Mathf.PerlinNoise(nx * 2.0f + 130f, ny * 2.0f + 2f);
        float ty = ny * 4.4f + 1.9f * Mathf.PerlinNoise(nx * 2.0f + 20f, ny * 2.0f + 80f);
        float thA = Ridged(tx * 1.3f + WX * 0.8f + 5f, ty * 1.3f + WY * 0.8f + 9f, 3.0f);
        float thB = Ridged(tx * 0.8f + 40f, ty * 0.8f + 43f, 2.3f);

        float Mth = Mathf.Clamp01(1f - S01(boundary - 0.28f, boundary, rw)) * (0.5f + 0.5f * turb);
        Mth *= guard;
        // Bright screen clears the centre so the button is framed; dark screen fills through.
        float hole = S01(0.05f, 0.62f, rw);
        float openMul = bright ? (0.35f + 0.65f * hole) : 1f;

        threadA = Mth * thA * openMul;
        threadB = Mth * thB * (1f - thA) * openMul;
    }

    // Lazily builds (once per screen kind) the SMOKY HALO behind the button.
    //   • Dark screen  (bright=false): the original dark-purple → near-black organic cloud.
    //     It vanishes into a dark background and adds a faint purple haze — looks great.
    //   • Bright screen (bright=true): that dark cloud would grey out on a light background,
    //     so instead we bake only a FAINT violet glow that hugs the threads and leave the
    //     centre open, so the bright background shows through between the filaments.
    private static Sprite _smokeSpriteWin, _smokeSpriteLose;
    private static Sprite SmokeSprite(bool bright)
    {
        if (bright && _smokeSpriteWin != null) return _smokeSpriteWin;
        if (!bright && _smokeSpriteLose != null) return _smokeSpriteLose;

        const int w = 340, h = 224;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f) / w;
                float ny = (y + 0.5f) / h;

                FxFields(nx, ny, bright, out float turb, out _, out float M,
                         out float threadA, out float threadB);

                float a;
                Color c;

                if (bright)
                {
                    // Faint glow that FOLLOWS the (centre-cleared) threads, plus a whisper of
                    // ambient blob. Centre stays open so the bright bg reads through.
                    float glow = Mathf.Clamp01(threadA * 0.95f + threadB * 0.7f);
                    a = Mathf.Clamp01(glow * 0.34f + M * turb * 0.035f);

                    float dens = Mathf.Clamp01(a / 0.34f);
                    float deep = S01(0.20f, 0.90f, dens);
                    c = PAL_B_GLOW * (1f - deep) + PAL_B_DEEP * deep;
                }
                else
                {
                    // The organic blob itself, coloured dark-purple → near-black toward its
                    // densest wisps. M already fades before the edges, so no contour, no oval.
                    a = Mathf.Clamp01(M * (0.34f + 0.66f * turb));

                    float dens = Mathf.Clamp01(a);
                    float core = S01(0.40f, 0.98f, dens);
                    float midm = S01(0.05f, 0.45f, dens) * (1f - core);
                    float darkm = 1f - core - midm;
                    c = PAL_MID * midm + PAL_DARK * darkm + PAL_BLACK * core;
                }

                px[y * w + x] = new Color32(
                    (byte)(Mathf.Clamp01(c.r) * 255f),
                    (byte)(Mathf.Clamp01(c.g) * 255f),
                    (byte)(Mathf.Clamp01(c.b) * 255f),
                    (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                                   new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        if (bright) _smokeSpriteWin = sprite; else _smokeSpriteLose = sprite;
        return sprite;
    }

    // Lazily builds (once per screen kind) the woven THREAD ring: bright violet filaments
    // plus a few darker veins, laid into a ring around the button so they frame it. Instanced
    // twice (mirrored) and swayed in opposite directions by the FX so the copies weave.
    //   • Dark screen: dark-purple body, near-black veins (they read against the dark bg).
    //   • Bright screen: saturated-purple body + veins instead of near-black, so the darker
    //     parts stay purple rather than greying out on a light background.
    private static Sprite _threadSpriteWin, _threadSpriteLose;
    private static Sprite ThreadSprite(bool bright)
    {
        if (bright && _threadSpriteWin != null) return _threadSpriteWin;
        if (!bright && _threadSpriteLose != null) return _threadSpriteLose;

        const int w = 360, h = 236;   // extra res for crisp filaments
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f) / w;
                float ny = (y + 0.5f) / h;

                FxFields(nx, ny, bright, out _, out _, out _,
                         out float threadA, out float threadB);

                // Threads are already confined to the organic blob and centre-cleared on the
                // bright screen, so there's no radial fade to apply here — the blob handles it.
                float a = Mathf.Clamp01(threadA * (bright ? 0.95f : 0.90f) + threadB * 0.6f);
                if (bright) a *= 0.92f;               // a touch lighter so threads sit on the bg
                a = Mathf.Clamp01(a);

                // Colour: bright threads ride vivid→hot violet either way. The connective body
                // and the darker veins swap to saturated purple on the bright screen (vs the
                // dark-purple/near-black used on the dark screen) so nothing greys out.
                float bt = Mathf.Clamp01(threadA * 2.4f);
                Color brightCol = PAL_VIVID * 0.55f + PAL_HOT * 0.45f;
                Color body = bright ? PAL_B_DEEP : PAL_DARK;
                Color vein = bright ? PAL_B_VEIN : PAL_BLACK;
                Color c = body * (1f - bt) + brightCol * bt;
                float dt = Mathf.Clamp01(threadB * 1.6f);
                c = c * (1f - dt) + vein * dt;

                px[y * w + x] = new Color32(
                    (byte)(Mathf.Clamp01(c.r) * 255f),
                    (byte)(Mathf.Clamp01(c.g) * 255f),
                    (byte)(Mathf.Clamp01(c.b) * 255f),
                    (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                                   new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        if (bright) _threadSpriteWin = sprite; else _threadSpriteLose = sprite;
        return sprite;
    }

    // Return to the main-menu scene. The freeze + attack/aim suppression are deliberately
    // left ON through the fade so the player can't act on the fading Win/Lose screen.
    // Cleanup is layered and can't strand control:
    //   • _frozenForOutcome=false stops OnDisable from re-touching the stack at unload.
    //   • ForceClear drops our UIModalStack freeze layer (matches the pre-load convention
    //     in CoopStartLobby / ContinueRunMenu); ScreenFade restores Time.timeScale on load.
    //   • Attack/aim suppression self-clears on the scene load via the PlayerAttack /
    //     PlayerAim AfterSceneLoad resets, so the incoming scene always starts un-suppressed.
    private void OnBackToMainMenu()
    {
        _frozenForOutcome = false;
        RunResumeIntent.Clear();     // leaving to the menu — don't carry a stale resume intent
        UIModalStack.ForceClear();
        ScreenFade.LoadScene(mainMenuScene);
    }

    // Hide in-game HUD that shouldn't sit on top of the Win/Lose screen. Currently
    // the per-player weapon-roll hotbar (one per player in co-op).
    private void HideGameplayHud()
    {
        var rolls = FindObjectsByType<WeaponRollUI>(FindObjectsSortMode.None);
        for (int i = 0; i < rolls.Length; i++)
            if (rolls[i] != null) rolls[i].SetHudVisible(false);
    }

    //  Screen + label resolution 

    private void ResolveScreens()
    {
        if (_resolvedScreens) return;
        var root = winLoseRoot != null ? winLoseRoot.transform : transform;
        if (winScreen == null) winScreen = FindChildByName(root, "Win");
        if (loseScreen == null) loseScreen = FindChildByName(root, "Lose");
        _resolvedScreens = true;
    }

    private static GameObject FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) return c.gameObject;
        }
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != root && string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase))
                return all[i].gameObject;
        return null;
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private string BuildStatsText()
    {
        var sb = new StringBuilder(320);
        int count = PlayerCount;

        if (count <= 1)
        {
            AppendPlayerBlock(sb, 0, null);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                AppendPlayerBlock(sb, i, $"PLAYER {i + 1}");
                sb.Append('\n');
            }
        }

        // Shared (tower + kills).
        sb.Append("Tower Damage Dealt: ").Append(_towerDamageDealt.ToString("N0")).Append('\n');
        sb.Append("Tower Damage Taken: ").Append(_towerDamageTaken.ToString("N0")).Append('\n');
        sb.Append("Enemies Killed: ").Append(_enemiesKilled.ToString("N0"));
        return sb.ToString();
    }

    private void AppendPlayerBlock(StringBuilder sb, int idx, string header)
    {
        if (!string.IsNullOrEmpty(header)) sb.Append(header).Append('\n');
        sb.Append("Damage Dealt: ").Append(GetDamageDealt(idx).ToString("N0")).Append('\n');
        sb.Append("Damage Taken: ").Append(GetDamageReceived(idx).ToString("N0")).Append('\n');
        sb.Append("DPS: ").Append(GetAverageDps(idx).ToString("N1")).Append('\n');
    }

    private TMP_Text EnsureLabel(ref TMP_Text cached, GameObject screen, string tag)
    {
        TMP_Text label = cached;
        string how = "assigned";

        if (label == null)
        {
            // Reuse a descendant whose name hints it's the stats label.
            if (screen != null)
            {
                var existing = screen.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < existing.Length; i++)
                {
                    var n = existing[i].name.ToLowerInvariant();
                    if (n.Contains("stat") || n.Contains("dps") || n.Contains("combat"))
                    {
                        label = existing[i];
                        how = "found";
                        break;
                    }
                }
            }

            // Otherwise build one.
            if (label == null && screen != null)
            {
                if (screen.GetComponentInParent<Canvas>() == null && debugLog)
                    Debug.LogWarning("[CombatStats] Screen is not under a Canvas — the stats label may not render.");

                var go = new GameObject("CombatStatsText", typeof(RectTransform));
                go.transform.SetParent(screen.transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.richText = true;
                tmp.raycastTarget = false;
                label = tmp;
                how = "created";
            }

            cached = label;
        }

        if (label == null) return null;

        // Font: created labels always get the resolved font; assigned/found labels are
        // only re-fonted when an explicit Stats Font is set (so we don't clobber a
        // deliberately-chosen font on your own object).
        if (how == "created" || statsFont != null)
        {
            var f = ResolveFont();
            if (f != null) label.font = f;
        }

        // Optionally force BOTH screens' labels to an identical layout so a Win/Lose
        // mismatch (different rect, alignment, auto-size, parent size…) can't happen.
        // The Win screen can use its own colour/size/position (bright background).
        if (matchLabelStyle || how == "created")
            ApplyMatchedStyle(label, isWin: tag == "WIN");

        if (debugLog) Debug.Log($"[CombatStats] {tag} stats label: {how} '{label.name}'.");
        return label;
    }

    // Applies the component's look to a label. Lose (and Win when winOverrides is off)
    // uses the shared values; Win uses its darker/smaller/lower overrides.
    private void ApplyMatchedStyle(TMP_Text label, bool isWin)
    {
        bool useWin = isWin && winOverrides;

        label.alignment = labelAlignment;
        label.enableAutoSizing = false;   // a stray auto-size range is a common 'packed' cause
        label.fontSize = useWin ? winFontSize : fontSize;
        label.color = useWin ? winTextColor : textColor;
        label.margin = Vector4.zero;
        if (outlineWidth > 0f) { label.outlineWidth = outlineWidth; label.outlineColor = outlineColor; }

        var rt = label.rectTransform;
        rt.anchorMin = useWin ? winAnchorMin : anchorMin;
        rt.anchorMax = useWin ? winAnchorMax : anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = Vector2.zero;
    }

    private void ApplyFont(TMP_Text label)
    {
        if (label == null) return;
        var font = ResolveFont();
        if (font != null) label.font = font;
    }

    private TMP_FontAsset _resolvedFont;
    private bool _fontLookupDone;

    private TMP_FontAsset ResolveFont()
    {
        if (statsFont != null) return statsFont;
        if (_fontLookupDone) return _resolvedFont;
        _fontLookupDone = true;

        var loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        TMP_FontAsset anyCinzel = null;
        for (int i = 0; i < loaded.Length; i++)
        {
            var fa = loaded[i];
            if (fa == null) continue;
            var n = fa.name.ToLowerInvariant();
            if (n.Contains("cinzel"))
            {
                if (n.Contains("black")) { _resolvedFont = fa; break; }
                if (anyCinzel == null) anyCinzel = fa;
            }
        }
        if (_resolvedFont == null) _resolvedFont = anyCinzel;

        if (_resolvedFont == null)
        {
            string[] guesses = { "Cinzel-Black SDF", "Fonts/Cinzel-Black SDF", "Fonts/Cinzel/static/Cinzel-Black SDF" };
            for (int i = 0; i < guesses.Length; i++)
            {
                var fa = Resources.Load<TMP_FontAsset>(guesses[i]);
                if (fa != null) { _resolvedFont = fa; break; }
            }
        }

        if (_resolvedFont == null)
            Debug.LogWarning("[CombatStats] Cinzel-Black SDF not found — assign 'Stats Font' on the CombatStats " +
                             "component to Assets/Fonts/Cinzel/static/Cinzel-Black SDF.asset.");
        return _resolvedFont;
    }

#if UNITY_EDITOR
    [ContextMenu("Preview WIN screen")] private void PreviewWin() => ShowOutcome(true);
    [ContextMenu("Preview LOSE screen")] private void PreviewLose() => ShowOutcome(false);
#endif
}

// Fades the Win/Lose "Back to Main Menu" button in, gives its smoky halo a gentle breathing
// pulse, and slowly WEAVES the two purple/black thread layers against each other. Everything
// runs on UNSCALED time because the Win/Lose screen freezes the game (Time.timeScale = 0) —
// scaled-time animation would sit dead-frozen. Added at runtime by CombatStats.EnsureMenuButton
// (so this class never needs to match a filename).
public class WinLoseButtonFX : MonoBehaviour
{
    private CanvasGroup _group;        // whole button+glow → faded in once

    private Graphic _smoke;            // halo → alpha pulsed
    private RectTransform _smokeRt;    // halo → scale "breathed"

    private Graphic _threadA, _threadB;      // woven filament layers
    private RectTransform _threadRtA, _threadRtB;

    private float _fadeIn, _pulsePeriod;
    private float _swayDeg, _swayPeriod, _driftPx;

    private float _startTime;
    private float _smokeBaseA, _threadBaseA, _threadBaseB;
    private float _threadSignA, _threadSignB;   // preserve each layer's mirror (localScale sign)
    private float _threadMagA, _threadMagB;
    private bool _ready, _started;

    public void Init(CanvasGroup group,
                     Graphic smoke, RectTransform smokeRt,
                     Graphic threadA, RectTransform threadRtA,
                     Graphic threadB, RectTransform threadRtB,
                     float fadeInDuration, float pulsePeriod,
                     float swayDegrees, float swayPeriod, float driftPixels)
    {
        _group = group;
        _smoke = smoke; _smokeRt = smokeRt;
        _threadA = threadA; _threadRtA = threadRtA;
        _threadB = threadB; _threadRtB = threadRtB;

        _fadeIn = Mathf.Max(0f, fadeInDuration);
        _pulsePeriod = pulsePeriod;
        _swayDeg = swayDegrees;
        _swayPeriod = Mathf.Max(0.01f, swayPeriod);
        _driftPx = driftPixels;

        _smokeBaseA = smoke != null ? smoke.color.a : 1f;
        _threadBaseA = threadA != null ? threadA.color.a : 1f;
        _threadBaseB = threadB != null ? threadB.color.a : 1f;

        // Remember each thread layer's mirror + magnitude so sway scaling keeps the flip.
        _threadSignA = _threadRtA != null ? Mathf.Sign(_threadRtA.localScale.x) : 1f;
        _threadSignB = _threadRtB != null ? Mathf.Sign(_threadRtB.localScale.x) : 1f;
        _threadMagA = _threadRtA != null ? Mathf.Abs(_threadRtA.localScale.x) : 1f;
        _threadMagB = _threadRtB != null ? Mathf.Abs(_threadRtB.localScale.y) : 1.06f;

        if (_group != null) _group.alpha = 0f;
        _ready = true;
        _started = false;            // don't start the clock until the first Update
    }

    private void OnEnable()
    {
        // Restart the intro if the object is re-enabled (e.g. the screen is re-shown).
        if (_ready)
        {
            _started = false;
            if (_group != null) _group.alpha = 0f;
        }
    }

    private void Update()
    {
        if (!_ready) return;

        // Start the clock on the first frame we actually render, NOT in Init(). Building the
        // glow textures causes a one-frame hitch; timing from Init() could eat part of the
        // fade and make it snap in. Timing from here guarantees a real fade.
        if (!_started)
        {
            _startTime = Time.unscaledTime;
            _started = true;
        }

        float t = Time.unscaledTime - _startTime;   // unscaled: animates while timeScale = 0
        const float TAU = Mathf.PI * 2f;

        // One-time smooth fade-in of the whole button.
        if (_group != null)
        {
            float f = _fadeIn > 0f ? Mathf.Clamp01(t / _fadeIn) : 1f;
            _group.alpha = f * f * (3f - 2f * f);    // smoothstep ease
        }

        // Organic "breath": two sine waves at different rates so the pulse never feels like a
        // metronome — it swells and eases irregularly, like something alive. 0..1.
        float breath = 0.5f + 0.5f * (0.72f * Mathf.Sin((t / _pulsePeriod) * TAU)
                                    + 0.28f * Mathf.Sin((t / (_pulsePeriod * 1.9f)) * TAU + 1.1f));
        breath = Mathf.Clamp01(breath);

        // Breathing smoke halo — opacity + size gently swell. The button stays static.
        if (_pulsePeriod > 0.01f)
        {
            if (_smoke != null)
            {
                var c = _smoke.color;
                c.a = _smokeBaseA * Mathf.Lerp(0.60f, 1f, breath);
                _smoke.color = c;
            }
            if (_smokeRt != null)
            {
                float sc = Mathf.Lerp(0.94f, 1.10f, breath);
                _smokeRt.localScale = new Vector3(sc, sc, 1f);
            }
        }

        // Weaving threads — the two layers oscillate (never spin) in opposite directions,
        // drift a few px, and shimmer their opacity out of phase, so the filaments look like
        // they slowly braid past each other. They also brighten a touch on the breath's swell
        // (via 'breath'), so the whole aura pulses as one instead of in two separate rhythms.
        float pa = t / _swayPeriod;
        float pb = t / (_swayPeriod * 1.37f) + 0.25f;

        WeaveLayer(_threadA, _threadRtA, _threadBaseA, _threadSignA, _threadMagA, pa, +1f, breath, TAU);
        WeaveLayer(_threadB, _threadRtB, _threadBaseB, _threadSignB, _threadMagB, pb, -1f, breath, TAU);
    }

    // Applies one thread layer's sway (rotation), drift (position) and shimmer (alpha).
    // 'dir' flips the sway/drift direction so the two layers move against each other; 'sign'
    // and 'mag' preserve the layer's baked mirror + scale; 'breath' is the shared pulse.
    private void WeaveLayer(Graphic g, RectTransform rt, float baseAlpha,
                            float sign, float mag, float phase, float dir, float breath, float TAU)
    {
        float wave = Mathf.Sin(phase * TAU);          // -1..1
        if (rt != null)
        {
            // Compound sway: primary swing + a faster, smaller second harmonic → the arms
            // curl and uncurl instead of rocking as a rigid unit. Never a full spin.
            float sway = dir * _swayDeg * (wave + 0.35f * Mathf.Sin(phase * TAU * 1.9f + 0.7f));
            rt.localRotation = Quaternion.Euler(0f, 0f, sway);

            float drift = _driftPx * dir;
            rt.anchoredPosition = new Vector2(drift * wave,
                                              drift * 0.6f * Mathf.Cos(phase * TAU));

            // Anisotropic "breathing" — the layer stretches along one axis while squashing the
            // other, out of phase between the two thread layers, so the tentacles visibly reach
            // out and draw back rather than just scaling uniformly.
            float sc = mag * Mathf.Lerp(0.98f, 1.04f, 0.5f + 0.5f * wave);
            float stretch = 0.07f * Mathf.Sin(phase * TAU * 0.8f + dir);
            rt.localScale = new Vector3(sign * sc * (1f + stretch), sc * (1f - stretch), 1f);
        }
        if (g != null)
        {
            var c = g.color;
            float shimmer = Mathf.Lerp(0.72f, 1f, 0.5f + 0.5f * Mathf.Sin(phase * TAU + dir));
            float pulse = Mathf.Lerp(0.90f, 1.06f, breath);   // gently brighten on the swell
            c.a = baseAlpha * shimmer * pulse;
            g.color = c;
        }
    }
}



