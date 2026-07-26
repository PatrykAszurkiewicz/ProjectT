using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Player damage screen vignette via a Screen Space Canvas Overlay, PLUS a screen
// shake on damage. Self-bootstrapping manager — you attach it to nothing.
// It listens to every player's CharacterStats.OnHealthChanged (the one choke-point all
// damage funnels through) and, on a real HP drop:
//   flashes a red edge vignette (Overlay canvas, so it can't be off-screen or lose a
//     sorting fight), and
//   triggers CameraShake so taking damage shakes the screen again.
[DisallowMultipleComponent]
public class PlayerDamageVignette : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Red tint of the whole effect.")]
    public Color vignetteColor = new Color(0.95f, 0.0f, 0.0f, 1f);

    [Tooltip("Overall peak opacity at a full-strength flash.")]
    [Range(0f, 1f)] public float edgeIntensity = 0.85f;

    [Header("Soft reddish vignette (base glow)")]
    [Tooltip("Strength of the smooth red glow at the edges — the effect that was there before.")]
    [Range(0f, 1f)] public float vignetteBaseStrength = 0.55f;

    [Tooltip("How far the glow reaches inward. 0 = from centre, 0.4 = only the outer part.")]
    [Range(0f, 0.95f)] public float vignetteReach = 0.40f;

    [Tooltip("Softness of the glow's inner falloff.")]
    [Range(0.05f, 1f)] public float vignetteSmoothness = 0.5f;

    [Tooltip("Frame shape. 2 = oval, 3-4 = rounded rectangle, 6+ = hugs the very edges.")]
    [Range(2f, 8f)] public float vignetteRoundness = 2.5f;

    [Header("Subtle edge cracks (thin threads at the border)")]
    [Tooltip("Opacity of the crack threads on top of the glow. Keep low for subtle.")]
    [Range(0f, 1f)] public float crackStrength = 0.45f;

    [Tooltip("Cracks only appear beyond this distance from centre — keep HIGH so they hug the edges.")]
    [Range(0.3f, 0.98f)] public float crackEdgeStart = 0.72f;

    [Tooltip("Line thickness of the cracks. Small = fine threads.")]
    [Range(0.003f, 0.05f)] public float crackThickness = 0.01f;

    [Tooltip("Number of shard cells (Voronoi seeds) feeding the web.")]
    [Range(4, 80)] public int crackCount = 24;

    [Tooltip("Number of short radial threads at the edges.")]
    [Range(0, 30)] public int radialCrackCount = 10;

    [Tooltip("Random seed for the crack layout.")]
    public int crackSeed = 12345;

    [Header("Flash")]
    [Tooltip("Opacity scale for the SMALLEST hit.")]
    [Range(0f, 1f)] public float minHitWeight = 0.45f;

    [Tooltip("A single hit worth this fraction of max HP produces a full-strength flash.")]
    [Range(0.05f, 1f)] public float fullHitDamageFraction = 0.25f;

    [Tooltip("Fade-out speed. ~1.4 fades a full flash in ~0.7s.")]
    public float fadeSpeed = 1.4f;

    [Tooltip("For the 'Low HP only' mode: flashes only when health is below this.")]
    [Range(0.05f, 0.9f)] public float lowHealthThreshold = 0.2f;

    [Header("Repeat Damping (photosensitive-safe sustained damage)")]
    [Tooltip("If true, the FIRST hit in a burst flashes at full strength, but ongoing " +
             "repeated damage no longer flashes per-tick. Instead it fades into a low, " +
             "STEADY glow that undulates slowly and gently (see the sustain settings " +
             "below), so sustained sources (poison DoT, the Eye's constant attack, " +
             "Boss1's laser, the Scarecrow aura) never strobe the screen — important for " +
             "photosensitive players. Turn OFF to restore the old flash-every-tick " +
             "behaviour exactly.")]
    public bool dampRepeatFlashes = true;

    [Tooltip("A hit counts as a 'repeat' (feeds the steady glow instead of a fresh flash) " +
             "if it lands within this many seconds of the previous hit. Sustained sources " +
             "tick far faster than this, so after the first strong flash they settle into " +
             "the steady glow. Once no damage lands for this long, the glow releases and " +
             "the next hit flashes fully again.")]
    [Range(0.05f, 2f)] public float repeatWindow = 0.6f;

    [Tooltip("Opacity ceiling of the steady sustained glow, as a fraction of the hit's " +
             "normal flash weight. 0.22 keeps the ongoing glow faint. Lower = subtler.")]
    [Range(0.05f, 1f)] public float repeatIntensityScale = 0.22f;

    [Tooltip("Escape hatch so a genuinely bigger hit still punches through as a fresh " +
             "flash. A repeat whose raw weight is at least this many times the previous " +
             "hit's weight flashes at full strength (e.g. a heavy melee landing while " +
             "poison ticks). Same-magnitude repeats never satisfy this, so steady sources " +
             "stay in the glow. Set very high to disable punch-through.")]
    [Range(1f, 5f)] public float repeatEscalationRatio = 1.5f;

    [Header("Sustained Glow (slow, gentle — anti-seizure)")]
    [Tooltip("How fast (opacity per second) the steady glow fades in when sustained " +
             "damage starts and fades out when it stops. Kept low so onset/offset is a " +
             "smooth ramp, never an abrupt flash.")]
    [Range(0.2f, 5f)] public float sustainRampSpeed = 1.2f;

    [Tooltip("Frequency of the steady glow's gentle undulation, in Hz (cycles/second). " +
             "Kept WELL below the ~3 Hz photosensitive-seizure threshold. This is fixed " +
             "regardless of how fast the damage actually ticks, so a rapid DoT can never " +
             "drive a fast on-screen flicker. Hard-clamped under 3 Hz at runtime.")]
    [Range(0.2f, 2.5f)] public float sustainPulseHz = 0.9f;

    [Tooltip("How deep the gentle undulation dips below the steady glow level. 0 = a " +
             "perfectly steady glow (safest); 0.25 = dips to 75% and back. Small values " +
             "keep the luminance swing low, which also reduces seizure risk.")]
    [Range(0f, 0.8f)] public float sustainPulseDepth = 0.25f;

    [Header("Intensity Balance")]
    [Tooltip("Opacity multiplier for the FIRST / one-off flash (the punch on getting hit). " +
             "Below 1 makes that initial red hit more subtle without affecting the " +
             "sustained glow. 0.8 = ~20% gentler than before.")]
    [Range(0.1f, 1f)] public float firstFlashIntensity = 0.8f;

    [Header("Biome-Aware Visibility (day vs night)")]
    [Tooltip("If true, the whole effect is boosted in BRIGHT (day) biomes, where faint " +
             "red would otherwise wash out against the light background, and left at its " +
             "base level in DARK (night) biomes / placement mode, where it already reads " +
             "clearly. Reads BiomeManager.activeBiome / night mode at runtime; if no " +
             "BiomeManager exists it assumes a bright scene. Turn OFF for one fixed level " +
             "everywhere.")]
    public bool biomeAwareGlow = true;

    [Tooltip("Multiplier applied to the sustained glow in bright/day biomes so the subtle " +
             "repeat pulses stay visible against a light background. 1 = no boost. The " +
             "glow is inherently faint, so it needs a bigger lift than the flash. " +
             "Dark/night biomes always use 1 (unchanged).")]
    [Range(1f, 6f)] public float daySustainBoost = 3.5f;

    [Tooltip("Multiplier applied to the FIRST / one-off flash in bright/day biomes, on top " +
             "of firstFlashIntensity, so getting hit still reads against a light " +
             "background. Kept modest since the flash is already strong. Dark/night biomes " +
             "always use 1, so the flash stays at the gentle level there.")]
    [Range(1f, 3f)] public float dayFlashBoost = 1.35f;

    [Header("Screen Shake on Damage")]
    [Tooltip("Also shake the screen when a player takes damage (routes through your CameraShake system).")]
    public bool shakeOnDamage = true;

    [Tooltip("Shake intensity at a full-strength hit. Small hits scale down toward minShakeScale.")]
    public float shakeIntensity = 0.12f;

    [Tooltip("Shake duration in seconds.")]
    public float shakeDuration = 0.2f;

    [Tooltip("A tiny hit still shakes at least this fraction of shakeIntensity.")]
    [Range(0f, 1f)] public float minShakeScale = 0.5f;

    //  Options: tri-state mode (persisted) 
    public enum VignetteMode { On = 0, LowHealthOnly = 1, Off = 2 }
    private const string ModeKey = "opt.damageVignetteMode";
    private static int _mode = -1;

    public static VignetteMode Mode
    {
        get { if (_mode < 0) _mode = Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, 2); return (VignetteMode)_mode; }
    }
    public static void SetMode(VignetteMode m) { _mode = (int)m; PlayerPrefs.SetInt(ModeKey, _mode); PlayerPrefs.Save(); }
    public static void CycleMode() { SetMode((VignetteMode)(((int)Mode + 1) % 3)); }

    public static bool Enabled => Mode != VignetteMode.Off;
    public static void SetEnabled(bool on) => SetMode(on ? VignetteMode.On : VignetteMode.Off);

    //  Debug 
    public static bool ProofOfLifeOnStart = true;
    public static bool VerboseDebug = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _mode = -1; _instance = null; }

    private static PlayerDamageVignette _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("~DamageVignetteManager");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<PlayerDamageVignette>();
    }

    public static void TestFlash()
    {
        if (_instance == null) Bootstrap();
        if (_instance != null) _instance.FlashAll(1f);
        Debug.Log("[DamageVignette] TEST flash requested.");
    }

    [ContextMenu("Trigger Test Flash")]
    public void InspectorTestFlash()
    {
        FlashAll(1f);
        if (VerboseDebug) Debug.Log("[DamageVignette] Context menu flash triggered (all players).");
    }

    /// <summary>Flash every player's half (used by the proof-of-life + test hooks).</summary>
    private void FlashAll(float amount)
    {
        EnsureTexture();
        RescanPlayers();
        foreach (var kv in _views) kv.Value.flash = Mathf.Max(kv.Value.flash, amount);
    }

    // One shared crack/glow texture; one canvas+overlay per player.
    private Texture2D _vignetteTex;
    private bool _proofDone;
    private float _rescanT;

    // Biome-aware glow: cache the (per-scene) BiomeManager and a smoothed day/night
    // glow factor. The vignette manager is DontDestroyOnLoad while BiomeManager is
    // per-scene, so the cache is re-resolved whenever it goes null. _glowScaleCurrent
    // is eased toward the target so a biome switch doesn't pop the glow level.
    private BiomeManager _biome;
    private float _biomeSearchT;
    private bool _sceneDark;
    private float _glowScaleCurrent = 1f;
    private float _flashScaleCurrent = 1f;

    // Per-player view: its own overlay, its own flash level, and the rect it occupies.
    private class View
    {
        public PlayerRef player;
        public Canvas canvas;
        public RawImage overlay;
        public RectTransform rt;
        public float flash;
        public Rect lastRect = new Rect(-1, -1, -1, -1);

        // Repeat-damping state (per player). NegativeInfinity so the very first
        // hit is always "fresh". Only updated from real damage flashes, so the
        // FlashAll test/proof-of-life path (which sets 'flash' directly) is
        // never treated as a repeat and never damps the next real hit.
        public float lastFlashTime = float.NegativeInfinity;
        public float lastRawWeight = 0f;

        // Sustained-glow envelope for ongoing repeated damage. 'sustain' is the
        // smoothed level actually shown; 'sustainTarget' is the ceiling the most
        // recent repeats want; 'lastRepeatTime' gates whether the glow is held or
        // released. This is a SMOOTH steady glow, never a per-tick blink.
        public float sustain = 0f;
        public float sustainTarget = 0f;
        public float lastRepeatTime = float.NegativeInfinity;
    }
    private readonly Dictionary<PlayerRef, View> _views = new Dictionary<PlayerRef, View>();
    private readonly List<View> _viewBuffer = new List<View>();   // reused snapshot, no per-frame alloc

    private class Tracker { public float lastHp, lastMax; public Action<float, float> handler; public PlayerRef owner; }
    private readonly Dictionary<CharacterStats, Tracker> _tracked = new Dictionary<CharacterStats, Tracker>();

    private void Awake()
    {
        if (GetComponent<Camera>() != null) { Destroy(this); return; }
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
        EnsureTexture();
    }

    private void OnEnable()
    {
        PlayerRegistry.OnPlayerJoined += OnJoined;
        PlayerRegistry.OnPlayerLeft += OnLeft;
        RescanPlayers();
    }

    private void OnDisable()
    {
        PlayerRegistry.OnPlayerJoined -= OnJoined;
        PlayerRegistry.OnPlayerLeft -= OnLeft;
        UntrackAll();
    }

    private void OnJoined(PlayerRef p) => Track(p);
    private void OnLeft(PlayerRef p) { if (p != null) { Untrack(p.Stats); DestroyView(p); } }

    private void RescanPlayers()
    {
        var all = PlayerRegistry.Instance.All;
        for (int i = 0; i < all.Count; i++) Track(all[i]);
    }

    private void Track(PlayerRef p)
    {
        if (p == null || p.Stats == null) return;
        var stats = p.Stats;
        if (_tracked.ContainsKey(stats)) return;

        var t = new Tracker { lastHp = stats.currentHealth, lastMax = stats.maxHealth, owner = p };
        t.handler = (c, m) => OnHealth(stats, c, m);
        stats.OnHealthChanged += t.handler;
        _tracked[stats] = t;

        EnsureView(p);
        if (VerboseDebug) Debug.Log($"[DamageVignette] tracking '{p.name}' (index {p.PlayerIndex}).");
    }

    private void Untrack(CharacterStats stats)
    {
        if (stats == null) return;
        if (_tracked.TryGetValue(stats, out var t)) { stats.OnHealthChanged -= t.handler; _tracked.Remove(stats); }
    }

    private void UntrackAll()
    {
        foreach (var kv in _tracked) if (kv.Key != null) kv.Key.OnHealthChanged -= kv.Value.handler;
        _tracked.Clear();
    }

    private void OnHealth(CharacterStats stats, float current, float max)
    {
        if (!_tracked.TryGetValue(stats, out var t)) return;

        // Capacity change (augment / buff): rebaseline, never react.
        if (!Mathf.Approximately(max, t.lastMax)) { t.lastMax = max; t.lastHp = current; return; }

        float drop = t.lastHp - current;
        t.lastHp = current;
        if (drop <= 0.01f) return;

        float frac = Mathf.Clamp01(drop / Mathf.Max(1f, max * fullHitDamageFraction));
        PlayerRef who = t.owner;

        // Screen shake — only the damaged player's camera. ShakeFor falls back to
        // shaking every camera if that player's camera can't be resolved (single player),
        // and is scaled by the Options camera-shake slider.
        if (shakeOnDamage)
        {
            float shakeI = shakeIntensity * Mathf.Lerp(minShakeScale, 1f, frac);
            CameraShake.ShakeFor(who, shakeI, shakeDuration);
        }

        // Red flash — only that player's half of the screen.
        Flash(who, frac, current, max);
    }

    private void Flash(PlayerRef who, float frac, float currentHp, float maxHp)
    {
        var mode = Mode;
        if (mode == VignetteMode.Off) return;
        if (mode == VignetteMode.LowHealthOnly && maxHp > 0f && (currentHp / maxHp) >= lowHealthThreshold) return;
        if (who == null) return;

        var view = EnsureView(who);
        if (view == null) return;

        // Raw weight of this hit, exactly as before: small hit -> minHitWeight,
        // full hit -> 1.
        float w = Mathf.Lerp(minHitWeight, 1f, frac);

        if (!dampRepeatFlashes)
        {
            // Legacy path: flash at full weight every hit.
            view.flash = Mathf.Min(1f, Mathf.Max(view.flash, w));
            return;
        }

        // The first hit of a burst (or a hit distinctly bigger than the last, via
        // repeatEscalationRatio) flashes normally. Ongoing same-magnitude repeats
        // do NOT flash per-tick — they feed the smooth sustained glow instead, so
        // a fast DoT/laser can never strobe the screen.
        float now = Time.unscaledTime;
        float sinceLast = now - view.lastFlashTime;
        bool fresh = sinceLast >= repeatWindow
                     || w >= view.lastRawWeight * repeatEscalationRatio;

        view.lastFlashTime = now;
        view.lastRawWeight = w;

        if (fresh)
        {
            // Distinct hit -> normal transient flash (unchanged feel).
            view.flash = Mathf.Min(1f, Mathf.Max(view.flash, w));
        }
        else
        {
            // Sustained repeat -> raise the steady-glow ceiling. The Update loop
            // ramps 'sustain' toward this smoothly and undulates it slowly; nothing
            // here writes 'flash', so there is no per-tick blink.
            view.sustainTarget = Mathf.Min(1f, Mathf.Max(view.sustainTarget, w * repeatIntensityScale));
            view.lastRepeatTime = now;
        }
    }

    private void Update()
    {
        if (_views.Count == 0 && PlayerRegistry.Count > 0)
        {
            _rescanT += Time.unscaledDeltaTime;
            if (_rescanT >= 0.25f) { _rescanT = 0f; RescanPlayers(); }
        }

#if UNITY_EDITOR
        // Live-regenerate the shared texture when a shape field changes while playing.
        if (_vignetteTex != null)
        {
            int h = ShapeHash();
            if (h != _shapeHash)
            {
                _shapeHash = h;
                Destroy(_vignetteTex);
                _vignetteTex = CreateVignetteTexture(512, 288);
                foreach (var kv in _views) if (kv.Value.overlay != null) kv.Value.overlay.texture = _vignetteTex;
            }
        }
#endif

        if (ProofOfLifeOnStart && !_proofDone && Mode != VignetteMode.Off && PlayerRegistry.Count > 0)
        {
            _proofDone = true;
            FlashAll(1f);
        }

        float dtUnscaled = Time.unscaledDeltaTime;
        float dt = fadeSpeed * dtUnscaled;              // transient-flash fade
        float sustainStep = sustainRampSpeed * dtUnscaled; // steady-glow ramp
        bool off = Mode == VignetteMode.Off;
        float now = Time.unscaledTime;

        // Slow, gentle undulation shared by all views. Frequency is hard-clamped
        // below the ~3 Hz photosensitive threshold and the depth is small, so the
        // steady glow can never present as a fast or high-contrast flicker no
        // matter how quickly the underlying damage ticks.
        float safeHz = Mathf.Clamp(sustainPulseHz, 0.05f, 2.9f);
        float depth = Mathf.Clamp01(sustainPulseDepth);
        // cosine in [0..1], starts at trough so onset ramps up gently.
        float wave = 0.5f * (1f - Mathf.Cos(now * safeHz * 2f * Mathf.PI));
        float undulation = 1f - depth * wave;           // in [1-depth .. 1]

        // Day/night visibility: brighten the effect in bright biomes where it would
        // otherwise wash out, keep it at base (1) in dark biomes / placement mode.
        // Eased so a biome switch doesn't pop. Resolved once per frame, not per view.
        RefreshBiomeState(dtUnscaled);
        bool brightBiome = biomeAwareGlow && !_sceneDark;
        float glowScaleTarget = brightBiome ? Mathf.Max(1f, daySustainBoost) : 1f;
        float flashScaleTarget = brightBiome ? Mathf.Max(1f, dayFlashBoost) : 1f;
        _glowScaleCurrent = Mathf.MoveTowards(_glowScaleCurrent, glowScaleTarget, 2f * dtUnscaled);
        _flashScaleCurrent = Mathf.MoveTowards(_flashScaleCurrent, flashScaleTarget, 2f * dtUnscaled);

        // Snapshot: EnsureView/DestroyView can mutate _views, so never iterate it directly.
        _viewBuffer.Clear();
        foreach (var kv in _views) _viewBuffer.Add(kv.Value);

        for (int i = 0; i < _viewBuffer.Count; i++)
        {
            var view = _viewBuffer[i];
            if (view.overlay == null) continue;

            if (off)
            {
                view.flash = 0f;
                view.sustain = 0f;
                view.sustainTarget = 0f;
            }
            else
            {
                // Transient flash fades out exactly as before.
                if (view.flash > 0f) view.flash = Mathf.MoveTowards(view.flash, 0f, dt);

                // Steady glow: held while sustained damage is recent, released
                // smoothly once it stops. Both the hold->0 and the ramp use
                // MoveTowards so onset and offset are gradual, never a pop.
                bool sustainActive = (now - view.lastRepeatTime) <= repeatWindow;
                if (!sustainActive)
                    view.sustainTarget = Mathf.MoveTowards(view.sustainTarget, 0f, sustainStep);
                float sustainGoal = sustainActive ? view.sustainTarget : 0f;
                view.sustain = Mathf.MoveTowards(view.sustain, sustainGoal, sustainStep);
            }

            // Keep this overlay pinned to its player's viewport rect (handles the
            // single -> split transition, and P1 left / P2 right).
            SyncRect(view);

            // Displayed level is the stronger of the transient flash and the
            // gently-undulating steady glow. Taking the max means a fresh flash
            // still reads over an ongoing glow, and the two hand off smoothly.
            //   - firstFlashIntensity keeps the initial hit gentle (esp. at night).
            //   - _flashScaleCurrent / _glowScaleCurrent lift each in bright biomes
            //     only, so day scenes stay visible while night is unchanged.
            float flashPart = view.flash * firstFlashIntensity * _flashScaleCurrent;
            float sustainPart = view.sustain * undulation * _glowScaleCurrent;
            float a = Mathf.Clamp01(Mathf.Max(flashPart, sustainPart)) * edgeIntensity;

            view.overlay.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, a);
            if (view.overlay.enabled != (a > 0.001f)) view.overlay.enabled = a > 0.001f;
        }
    }

    // Resolve the current scene's BiomeManager (this manager persists across scene
    // loads via DontDestroyOnLoad, so the cache is re-acquired whenever it goes null)
    // and decide whether the scene is "dark" (night-like) for glow-visibility purposes.
    private void RefreshBiomeState(float dtUnscaled)
    {
        if (!biomeAwareGlow) { _sceneDark = false; return; }

        // (Re)acquire the per-scene BiomeManager if we don't have a live one.
        // Throttled so we don't call the scene search every frame while it's absent.
        if (_biome == null)
        {
            _biomeSearchT -= dtUnscaled;
            if (_biomeSearchT <= 0f)
            {
                _biomeSearchT = 0.5f;
                _biome = FindAnyObjectByType<BiomeManager>();
            }
        }

        // No biome in this scene (e.g. menus) -> treat as a bright scene (boost applies).
        _sceneDark = _biome != null && IsBiomeDark(_biome);
    }

    // A biome counts as "dark" when night mode is overlaid on any biome, or when the
    // active biome is one of the inherently dark ones. Bright biomes wash out a faint
    // red glow, so those get the day boost; dark biomes already show it clearly.
    private static bool IsBiomeDark(BiomeManager b)
    {
        if (b == null) return false;
        if (b.enableNightMode) return true;
        switch (b.activeBiome)
        {
            case BiomeType.Night:
            case BiomeType.Corruption:
            case BiomeType.PitchBlack:
                return true;
            default:
                return false;
        }
    }

    // Map the player's Camera.rect (viewport 0..1) onto the full-screen canvas via anchors,
    // so the flash covers exactly that player's half. No camera -> full screen.
    private void SyncRect(View view)
    {
        Rect r = (view.player != null && view.player.Camera != null)
            ? view.player.Camera.rect
            : new Rect(0f, 0f, 1f, 1f);

        if (r == view.lastRect) return;
        view.lastRect = r;

        view.rt.anchorMin = new Vector2(r.xMin, r.yMin);
        view.rt.anchorMax = new Vector2(r.xMax, r.yMax);
        view.rt.offsetMin = Vector2.zero;
        view.rt.offsetMax = Vector2.zero;
    }

    private void EnsureTexture()
    {
        if (_vignetteTex != null) return;
        _vignetteTex = CreateVignetteTexture(512, 288);
#if UNITY_EDITOR
        _shapeHash = ShapeHash();
#endif
    }

    // One ScreenSpaceOverlay canvas per player, its image anchored to that player's
    // viewport rect. Overlay canvases ignore camera stacking/URP entirely, which is why
    // this renders reliably; the anchoring is what confines it to the right half.
    private View EnsureView(PlayerRef p)
    {
        if (p == null) return null;
        if (_views.TryGetValue(p, out var existing) && existing.canvas != null) return existing;

        EnsureTexture();

        var go = new GameObject($"DamageVignetteCanvas_P{p.PlayerIndex}");
        go.transform.SetParent(transform, false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var imgGO = new GameObject("Overlay");
        imgGO.transform.SetParent(go.transform, false);
        var overlay = imgGO.AddComponent<RawImage>();
        overlay.raycastTarget = false;
        overlay.texture = _vignetteTex;
        overlay.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f);
        overlay.enabled = false;

        var view = new View
        {
            player = p,
            canvas = canvas,
            overlay = overlay,
            rt = overlay.rectTransform,
        };
        SyncRect(view);
        _views[p] = view;

        if (VerboseDebug) Debug.Log($"[DamageVignette] overlay created for P{p.PlayerIndex} (rect {view.lastRect}).");
        return view;
    }

    private void DestroyView(PlayerRef p)
    {
        if (p == null || !_views.TryGetValue(p, out var v)) return;
        if (v.canvas != null) Destroy(v.canvas.gameObject);
        _views.Remove(p);
    }

#if UNITY_EDITOR
    private int _shapeHash;
    private int ShapeHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + vignetteBaseStrength.GetHashCode();
            h = h * 31 + vignetteReach.GetHashCode();
            h = h * 31 + vignetteSmoothness.GetHashCode();
            h = h * 31 + vignetteRoundness.GetHashCode();
            h = h * 31 + crackStrength.GetHashCode();
            h = h * 31 + crackEdgeStart.GetHashCode();
            h = h * 31 + crackThickness.GetHashCode();
            h = h * 31 + crackCount;
            h = h * 31 + radialCrackCount;
            h = h * 31 + crackSeed;
            return h;
        }
    }
#endif

    // Real GLSL-style smoothstep (Unity's Mathf.SmoothStep is a smoothed lerp, not this).
    private static float SStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    // Soft reddish edge vignette (the base glow) + THIN crack threads confined to the very
    // edges. White with per-pixel alpha; the RawImage colour supplies the red + flash opacity.
    private Texture2D CreateVignetteTexture(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[width * height];

        var rng = new System.Random(crackSeed);
        float aspect = (float)width / height;

        int n = Mathf.Max(2, crackCount);
        var sx = new float[n];
        var sy = new float[n];
        for (int i = 0; i < n; i++) { sx[i] = (float)rng.NextDouble(); sy[i] = (float)rng.NextDouble(); }

        int k = Mathf.Max(0, radialCrackCount);
        var ang = new float[k];
        var wob = new float[k];
        for (int i = 0; i < k; i++) { ang[i] = (float)(rng.NextDouble() * Mathf.PI * 2.0); wob[i] = (float)(rng.NextDouble() * 0.5 - 0.25); }

        float p = Mathf.Max(2f, vignetteRoundness);
        float invW = 1f / width, invH = 1f / height;

        float vigStart = vignetteReach;
        float vigEnd = Mathf.Min(1.4f, vignetteReach + vignetteSmoothness);
        float crackEnd = Mathf.Min(1.6f, crackEdgeStart + 0.22f);

        for (int y = 0; y < height; y++)
        {
            float v = y * invH;
            for (int x = 0; x < width; x++)
            {
                float u = x * invW;

                float nx = Mathf.Abs(u - 0.5f) * 2f;
                float ny = Mathf.Abs(v - 0.5f) * 2f;
                float dShape = Mathf.Pow(Mathf.Pow(nx, p) + Mathf.Pow(ny, p), 1f / p);

                // Base: the soft red glow that fades in toward the edges.
                float vig = SStep(vigStart, vigEnd, dShape) * vignetteBaseStrength;

                // Cracks live only in the outer edge band; skip the work elsewhere.
                float band = SStep(crackEdgeStart, crackEnd, dShape);
                float cracks = 0f;
                if (band > 0.001f && crackStrength > 0f)
                {
                    // Voronoi web thread.
                    float d1 = 9e9f, d2 = 9e9f;
                    for (int i = 0; i < n; i++)
                    {
                        float ax = (u - sx[i]) * aspect;
                        float ay = (v - sy[i]);
                        float dd = ax * ax + ay * ay;
                        if (dd < d1) { d2 = d1; d1 = dd; }
                        else if (dd < d2) { d2 = dd; }
                    }
                    float border = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
                    float web = 1f - SStep(0f, crackThickness, border);

                    // Short radial threads reaching in from the edge.
                    float radial = 0f;
                    if (k > 0)
                    {
                        float cx = (u - 0.5f) * aspect, cy = (v - 0.5f);
                        float r = Mathf.Sqrt(cx * cx + cy * cy);
                        if (r > 1e-4f)
                        {
                            float a = Mathf.Atan2(cy, cx);
                            for (int i = 0; i < k; i++)
                            {
                                float a0 = ang[i] + wob[i] * Mathf.Sin(r * 9f);
                                float dA = Mathf.Abs(Mathf.Repeat(a - a0 + Mathf.PI, Mathf.PI * 2f) - Mathf.PI);
                                float halfW = crackThickness / Mathf.Max(0.06f, r);
                                float line = 1f - SStep(0f, halfW, dA);
                                if (line > radial) radial = line;
                            }
                        }
                    }

                    cracks = Mathf.Max(web, radial) * band * crackStrength;
                }

                float alpha = Mathf.Clamp01(vig + cracks);  // threads sit on top of the glow
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private void OnDestroy()
    {
        UntrackAll();
        foreach (var kv in _views) if (kv.Value.canvas != null) Destroy(kv.Value.canvas.gameObject);
        _views.Clear();
        if (_vignetteTex != null) Destroy(_vignetteTex);
        if (_instance == this) _instance = null;
    }
}


