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

    [Header("Back to Main Menu button — soft glow / animation")]
    [Tooltip("How far the organic halo spreads beyond the button (x,y in px). It keeps the " +
             "button from sitting hard against the Win/Lose backgrounds.")]
    public Vector2 menuGlowPadding = new Vector2(190f, 150f);
    [Tooltip("Halo colour on the WIN screen. Bright background → a bold dark shadow reads best.")]
    public Color menuGlowColorWin = new Color(0f, 0f, 0f, 0.78f);
    [Tooltip("Halo colour on the LOSE screen. Dark background → a bright soft glow reads best.")]
    public Color menuGlowColorLose = new Color(0.88f, 0.89f, 0.97f, 0.72f);
    [Tooltip("Seconds for the button to smoothly fade in when the screen appears.")]
    public float menuFadeInDuration = 0.55f;
    [Tooltip("Seconds per breathing pulse of the glow. Set to 0 to fade in only, no pulse.")]
    public float menuPulsePeriod = 2.4f;

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

        // Soft halo behind the button (larger than it; centred; not raycastable).
        var glowGO = new GameObject("Glow", typeof(RectTransform));
        glowGO.transform.SetParent(container.transform, false);
        var grt = glowGO.GetComponent<RectTransform>();
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
        grt.pivot = new Vector2(0.5f, 0.5f);
        grt.sizeDelta = menuButtonSize + menuGlowPadding * 2f;
        var glow = glowGO.AddComponent<Image>();
        glow.sprite = SoftShadowSprite();
        glow.type = Image.Type.Simple;
        glow.raycastTarget = false;
        glow.color = isWin ? menuGlowColorWin : menuGlowColorLose;

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

        // Drive fade-in + breathing pulse on unscaled time (the run is frozen here).
        var fx = container.AddComponent<WinLoseButtonFX>();
        fx.Init(group, glow, grt, menuFadeInDuration, menuPulsePeriod);

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

    // Lazily builds (once, shared by both screens) a soft ORGANIC glow sprite for the
    // button's halo — a diffuse radial falloff whose edge is broken up with Perlin noise
    // so it reads as a wispy, smoky glow instead of a hard geometric shape. White, so it
    // tints per screen (dark smoke on Win, soft grey on Lose) via the Image colour.
    private static Sprite _softShadowSprite;
    private static Sprite SoftShadowSprite()
    {
        if (_softShadowSprite != null) return _softShadowSprite;

        const int w = 360, h = 236;   // extra res for the fine filament / wisp detail

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f) / w;      // 0..1
                float ny = (y + 0.5f) / h;      // 0..1
                float dx = (nx - 0.5f) * 2f;    // -1..1
                float dy = (ny - 0.5f) * 2f;    // -1..1
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float fine = Mathf.PerlinNoise(nx * 15.0f + 210f, ny * 15.0f + 33f);

                // Two-scale domain warp → the whole shape flows organically (not an ellipse).
                float w1x = Mathf.PerlinNoise(nx * 1.7f + 150f, ny * 1.7f + 60f) * 2f - 1f;
                float w1y = Mathf.PerlinNoise(nx * 1.7f + 230f, ny * 1.7f + 95f) * 2f - 1f;
                float w2x = Mathf.PerlinNoise(nx * 4.3f + 300f, ny * 4.3f + 12f) * 2f - 1f;
                float w2y = Mathf.PerlinNoise(nx * 4.3f + 360f, ny * 4.3f + 77f) * 2f - 1f;
                float wdx = dx + 0.26f * w1x + 0.09f * w2x;
                float wdy = dy + 0.26f * w1y + 0.09f * w2y;
                float d = Mathf.Sqrt(wdx * wdx + wdy * wdy) + (fine - 0.5f) * 0.10f;

                // Billowy TURBULENCE (abs-folded noise) → soft smoke/mist structure that
                // looks natural, not like a painted stain.
                float turb = Mathf.Abs(2f * Mathf.PerlinNoise(nx * 2.6f + 3f, ny * 2.6f + 7f) - 1f) * 0.55f
                           + Mathf.Abs(2f * Mathf.PerlinNoise(nx * 5.3f + 41f, ny * 5.3f + 22f) - 1f) * 0.30f
                           + Mathf.Abs(2f * Mathf.PerlinNoise(nx * 10.4f + 90f, ny * 10.4f + 61f) - 1f) * 0.15f;

                // Soft GAUSSIAN envelope → seamless blend: no hard cutoff, a long smooth tail
                // that fades imperceptibly into the background.
                float env = Mathf.Exp(-2.7f * d * d);
                // Concave socket: a textured ring at the button crease keeps the inset look.
                float rim = Mathf.Exp(-((d - 0.44f) * (d - 0.44f)) / (2f * 0.30f * 0.30f));

                float baseA = env * (0.42f + 0.72f * turb);
                float a = Mathf.Max(baseA, rim * (0.5f + 0.5f * turb) * 0.95f);

                // Soft web wisps (broad, gentle — not sharp veins), woven into the mid ring.
                float wx = nx * 6.5f + 1.6f * Mathf.PerlinNoise(nx * 3.0f + 130f, ny * 3.0f + 17f);
                float wy = ny * 6.5f + 1.6f * Mathf.PerlinNoise(nx * 3.0f + 205f, ny * 3.0f + 88f);
                float web = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx, wy) - 1f)), 3f);
                float webBand = Mathf.Clamp01(baseA * (1f - baseA) * 4f);
                a += web * webBand * 0.22f;

                // Organic edge DISSOLVE: erode only the low-alpha outer region into wisps so
                // the shadow dissipates into the background instead of ending on a contour.
                float erode = S01(0.12f, 0.72f, fine * 0.55f + turb * 0.45f);
                float edgeAmt = S01(0.03f, 0.42f, a);
                a *= Mathf.Lerp(erode, 1f, edgeAmt);

                // Final clean fade to fully transparent at the texture edge.
                a *= 1f - S01(0.82f, 0.98f, dist);

                a = Mathf.Clamp01(a);

                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        _softShadowSprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                                          new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _softShadowSprite;
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

// Fades the Win/Lose "Back to Main Menu" button in and gives its glow a gentle breathing
// pulse. Everything runs on UNSCALED time because the Win/Lose screen freezes the game
// (Time.timeScale = 0) — scaled-time animation would sit dead-frozen. Added at runtime by
// CombatStats.EnsureMenuButton (so this class never needs to match a filename).
public class WinLoseButtonFX : MonoBehaviour
{
    private CanvasGroup _group;      // whole button+glow → faded in once
    private Graphic _glow;           // shadow → alpha pulsed
    private RectTransform _glowRt;   // shadow → scale "breathed" (button stays static)
    private float _fadeIn;
    private float _pulsePeriod;

    private float _startTime;
    private float _glowBaseAlpha;
    private bool _ready;
    private bool _started;           // captures start time on the first REAL frame

    public void Init(CanvasGroup group, Graphic glow, RectTransform glowRt,
                     float fadeInDuration, float pulsePeriod)
    {
        _group = group;
        _glow = glow;
        _glowRt = glowRt;
        _fadeIn = Mathf.Max(0f, fadeInDuration);
        _pulsePeriod = pulsePeriod;
        _glowBaseAlpha = glow != null ? glow.color.a : 1f;
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
        // glow texture causes a one-frame hitch; if we timed from Init() that hitch could eat
        // part of the fade and make it snap in. Timing from here guarantees a real fade.
        if (!_started)
        {
            _startTime = Time.unscaledTime;
            _started = true;
        }

        float t = Time.unscaledTime - _startTime;   // unscaled: animates while timeScale = 0

        // One-time smooth fade-in of the whole button.
        if (_group != null)
        {
            float f = _fadeIn > 0f ? Mathf.Clamp01(t / _fadeIn) : 1f;
            _group.alpha = f * f * (3f - 2f * f);    // smoothstep ease
        }

        if (_pulsePeriod > 0.01f)
        {
            float s = 0.5f + 0.5f * Mathf.Sin((t / _pulsePeriod) * Mathf.PI * 2f);   // 0..1

            // Pulse the SHADOW only — its opacity and its (irregular) size breathe. The
            // button, text and container are left untouched, so the button stays static.
            if (_glow != null)
            {
                var c = _glow.color;
                c.a = _glowBaseAlpha * Mathf.Lerp(0.65f, 1f, s);   // breathe, but stay bold
                _glow.color = c;
            }
            if (_glowRt != null)
            {
                float sc = Mathf.Lerp(0.94f, 1.09f, s);            // shadow grows/shrinks
                _glowRt.localScale = new Vector3(sc, sc, 1f);
            }
        }
    }
}
