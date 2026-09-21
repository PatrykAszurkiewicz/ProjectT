using System.Collections.Generic;
using UnityEngine;

// Companion visual for Boss3 — a glitchy, slim, near-black shade with fiery red eyes,
// inspired by the Berserk enemy and Elden Ring's cemetery shade. Everything is
// generated procedurally (no imported art), following the same approach as
// BerserkVisual.
//
// This rewrite makes the boss read as a MENACING, FLOATING, underwater-like wraith
// rather than a static silhouette:
//   • The body silhouette (torso/legs/head/crown) is a baked sprite. The ARMS are NOT
//     baked — they are live, articulated thread-branches that droop and sway at rest
//     and RAISE toward a target during an attack, so the tree-hand strike visibly
//     erupts from the boss's hands.
//   • Floating "hair" tendrils on the crown and a few body-outline wisps undulate with
//     slow sine motion, like black threads suspended in water.
//   • Chromatic RGB "ghost" copies drift with a slow swim on top of the fast glitch.
//   • Drifting shadow motes rise around the body for an underwater particulate feel.
//
// IMPORTANT: the whole-body float/drift is owned by Boss3 (it moves the transform
// around a teleport-updated anchor). This script must therefore NEVER write to the
// root transform's position — doing so previously pinned the boss to the origin and
// cancelled every teleport. Everything here animates children / world-space lines.
//
// Public surface used by Boss3:
//   • SetFacing(sign)                 — which way the silhouette faces
//   • Reveal (0..1)                   — master fade, used to dissolve during teleports
//   • Glitch(intensity)               — kick a one-off glitch burst
//   • HandOrigin(sign)                — LIVE world point a tree-hand extends from
//   • BodyCentre()                    — approx body centre (blink-flash aim)
//   • AimArm(sign, worldTarget)       — raise that arm toward a world point
//   • RelaxArms()                     — let both arms fall back to the rest float
//   • BreakArm(sign, regrowSeconds)   — sever that arm (its tree-hand was destroyed)
//   • IsArmBroken(sign) / ArmGrowth   — is it still growing back?
[DisallowMultipleComponent]
public class Boss3Visual : MonoBehaviour
{
    // ── Silhouette texture layout ──
    private const int TEX_W = 140;
    private const int TEX_H = 240;
    // 2.5x smaller than the original 7.0 world-height.
    private const float SPRITE_WORLD_HEIGHT = 2.8f;
    private const float PIVOT_Y = 0.055f;                          // pivot near the feet
    private static readonly float PPU = TEX_H / SPRITE_WORLD_HEIGHT;
    private static readonly float WORLD_W = TEX_W / PPU;
    private static readonly float WORLD_H = TEX_H / PPU;

    // Palette.
    private static readonly Color CORE = new Color(0.020f, 0.020f, 0.032f, 1f); // near-black body
    private static readonly Color RIM = new Color(0.42f, 0.10f, 0.44f, 1f);    // faint violet edge
    private static readonly Color EYE = new Color(1f, 0.14f, 0.03f, 1f);       // fierce fiery red
    private static readonly Color AURA = new Color(0.55f, 0.06f, 0.10f, 1f);    // dark-red backing halo
    private static readonly Color THREAD = new Color(0.030f, 0.028f, 0.045f, 1f); // floating black thread

    // Anchor fractions (texture space).
    private const float EYE_Y_FRAC = 0.815f;
    private const float EYE_DX_FRAC = 0.052f;
    private const float BODY_MID_FRAC = 0.46f;

    // Shoulder / rest-hand anchors for the articulated arms (texture fracs, +x = right).
    private const float SHOULDER_DX_FRAC = 0.085f;
    private const float SHOULDER_Y_FRAC = 0.66f;
    private const float RESTHAND_DX_FRAC = 0.26f;
    private const float RESTHAND_Y_FRAC = 0.40f;

    [Header("Glitch")]
    [Tooltip("Baseline chromatic RGB-split, as a fraction of sprite height.")]
    [SerializeField] private float chromaticBase = 0.016f;
    [Tooltip("Extra split at the peak of a glitch burst.")]
    [SerializeField] private float chromaticBurst = 0.11f;
    [Tooltip("Average seconds between idle glitch bursts.")]
    [SerializeField] private float glitchInterval = 0.7f;
    [Range(0f, 1f)][SerializeField] private float ghostBaseAlpha = 0.32f;
    [Tooltip("Slow underwater 'swim' of the chromatic ghosts, world units.")]
    [SerializeField] private float ghostSwimAmplitude = 0.06f;
    [SerializeField] private float ghostSwimSpeed = 1.5f;

    [Header("Eyes")]
    [Range(0f, 1f)][SerializeField] private float eyeGlowAlpha = 0.9f;
    [SerializeField] private float eyeGlowSizeFrac = 0.12f;

    [Header("Aura")]
    [Range(0f, 1f)][SerializeField] private float auraAlpha = 0.42f;
    [SerializeField] private float auraSizeFrac = 0.95f;

    [Header("Floating threads (hair + outline wisps)")]
    [Tooltip("How far the tendrils sway, world units.")]
    [SerializeField] private float threadSway = 0.11f;
    [SerializeField] private float threadSwaySpeed = 1.7f;
    [SerializeField] private float threadWidth = 0.05f;

    [Header("Arms")]
    [Tooltip("World length of a raised arm (shoulder → hand).")]
    [SerializeField] private float raisedArmLength = 1.05f;
    [SerializeField] private float armWidth = 0.11f;
    [Tooltip("How fast the arms snap up / settle back (per second).")]
    [SerializeField] private float armRaiseUpSpeed = 6f;
    [SerializeField] private float armRaiseDownSpeed = 2.6f;
    [SerializeField] private float armSwayAmplitude = 0.07f;
    [SerializeField] private float armSwaySpeed = 1.5f;

    [Header("Arm break / regrow")]
    [Tooltip("Seconds an arm takes to grow back after its tree-hand was destroyed. " +
             "Boss3 normally passes its own value; this is the fallback.")]
    [SerializeField] private float armRegrowDuration = 1.6f;
    [Tooltip("How violently a freshly-broken stump twitches while it knits back together.")]
    [SerializeField] private float armStumpTremor = 0.09f;

    [Header("Drifting shadow motes")]
    [SerializeField] private int moteCount = 6;
    [SerializeField] private float moteSizeFrac = 0.14f;
    [Range(0f, 1f)][SerializeField] private float moteAlpha = 0.30f;

    // ── Runtime refs ──
    private SpriteRenderer mainRenderer;
    private Transform glitchRoot;                 // holds ghosts/eyes/aura/threads/arms/motes
    private SpriteRenderer ghostR, ghostC;
    private SpriteRenderer eyeL, eyeR;
    private SpriteRenderer aura;

    private float glitchTimer;
    private float glitchSpike;
    private float facing = 1f;
    private float reveal = 1f;
    private bool built;
    private bool dead;

    // Phase / punish visual state.
    private bool _vulnerable;
    private float _vulnLerp;      // smoothed 0..1 for the exposed (punish) look
    private bool _charging;
    private float _chargeLerp;    // smoothed 0..1 for the "interruptible wind-up" look
    private bool _phase2;
    private float _phase2Lerp;    // smoothed 0..1 for the enraged recolor
    private float _hitFlash;      // decaying additive pip on damage
    private SpriteRenderer _vulnRing, _vulnRing2;

    // Articulated arms. idx 0 = left (sign −1), idx 1 = right (sign +1).
    private readonly LineRenderer[] _arms = new LineRenderer[2];
    private readonly float[] _armRaiseCur = new float[2];
    private readonly float[] _armRaiseTgt = new float[2];
    private readonly Vector3[] _armAimWorld = new Vector3[2];
    private readonly Vector3[] _handTipWorld = new Vector3[2];

    // Arm break / regrow. _armGrow is the fraction of the limb that currently exists:
    // 1 = whole, 0 = severed at the shoulder. It stays at 1 for the entire fight unless
    // a tree-hand is destroyed, so nothing about the original arm behaviour changes.
    private readonly float[] _armGrow = { 1f, 1f };
    private readonly float[] _armRegrowSpan = { 1.6f, 1.6f };
    private readonly float[] _armBreakFlash = new float[2];

    // Floating tendrils (hair + outline wisps).
    private class Tendril
    {
        public LineRenderer lr;
        public Vector2 rootFrac;   // (dxFrac, yFrac) in texture space
        public Vector2 dir;        // base growth direction (frac space)
        public float len;          // world length
        public float width;
        public float phase;
        public float waveAmp;
        public float waveFreq;
        public int orderOffset;
        public Color color;
    }
    private readonly List<Tendril> _tendrils = new List<Tendril>();

    // Drifting shadow motes.
    private SpriteRenderer[] _motes;
    private Vector3[] _moteBaseLocal;
    private float[] _motePhase;
    private float[] _moteSpeed;

    private static Sprite _bodySprite;

    // ── public surface ──────────────────────────────────────────────────────

    public float Reveal { get => reveal; set => reveal = Mathf.Clamp01(value); }

    public void SetFacing(float sign)
    {
        if (sign > 0.01f) facing = 1f;
        else if (sign < -0.01f) facing = -1f;
    }

    public void Glitch(float intensity = 1f)
    {
        glitchSpike = Mathf.Max(glitchSpike, Mathf.Clamp01(intensity));
    }

    /// Raise the arm on `sign`'s side toward a world point (called during an attack).
    public void AimArm(float sign, Vector3 worldTarget)
    {
        int idx = sign >= 0f ? 1 : 0;
        _armAimWorld[idx] = worldTarget;
        _armRaiseTgt[idx] = 1f;
    }

    /// Let both arms fall back to their drooping rest float.
    public void RelaxArms()
    {
        _armRaiseTgt[0] = 0f;
        _armRaiseTgt[1] = 0f;
    }

    /// The tree-hand that grew from this arm was destroyed: sever the limb at the
    /// shoulder. It knits back over `regrowSeconds` (pass &lt;= 0 for the inspector
    /// default) and cannot be raised until it has.
    public void BreakArm(float sign, float regrowSeconds = -1f)
    {
        int idx = sign >= 0f ? 1 : 0;
        _armGrow[idx] = 0f;
        _armRegrowSpan[idx] = regrowSeconds > 0f ? regrowSeconds : Mathf.Max(0.05f, armRegrowDuration);
        _armBreakFlash[idx] = 1f;
        _armRaiseTgt[idx] = 0f;
        _armRaiseCur[idx] = 0f;
    }

    /// True while that arm is still growing back. Boss3 checks this so it never tries
    /// to throw a branch from a stump.
    public bool IsArmBroken(float sign) => _armGrow[sign >= 0f ? 1 : 0] < 0.999f;

    /// 0..1 regrowth progress of that arm (1 = whole).
    public float ArmGrowth(float sign) => _armGrow[sign >= 0f ? 1 : 0];

    /// LIVE world point a tree-hand should extend from (the animated hand tip).
    public Vector3 HandOrigin(float sign)
    {
        int idx = sign >= 0f ? 1 : 0;
        if (built) return _handTipWorld[idx];
        // Pre-build fallback: a static rest hand.
        float s = sign >= 0f ? 1f : -1f;
        return LocalToWorld(RESTHAND_DX_FRAC * s * facing, RESTHAND_Y_FRAC);
    }

    public Vector3 BodyCentre() => LocalToWorld(0f, BODY_MID_FRAC);

    /// Called by Boss3 when it dies: hide the ghosts/arms/threads/eyes so only the body
    /// silhouette remains for the disintegration VFX to shatter (nothing freezes mid-air).
    public void OnDeath()
    {
        dead = true;
        if (glitchRoot != null) glitchRoot.gameObject.SetActive(false);
    }

    /// Toggle the "destabilised / exposed" look for the punish window.
    public void SetVulnerable(bool on) => _vulnerable = on;

    /// Toggle the "charging / interruptible" look during an attack wind-up
    /// (distinct colour from the punish window: cyan-white "burst me now").
    public void SetCharging(bool on) => _charging = on;

    /// Enter the enraged phase recolor (hotter eyes/aura, wilder glitch).
    public void SetPhase2(bool on) => _phase2 = on;

    /// Kick a brief additive hit-flash pip when the boss takes damage.
    public void HitFlash(float strength = 1f) => _hitFlash = Mathf.Max(_hitFlash, Mathf.Clamp01(strength));

    // ── lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        mainRenderer = GetComponent<SpriteRenderer>();
        EnsureBodySprite();
        if (mainRenderer != null)
        {
            mainRenderer.sprite = _bodySprite;
            mainRenderer.color = Color.white;
        }

        // Seed arm aim so the very first frame isn't NaN.
        for (int i = 0; i < 2; i++)
            _armAimWorld[i] = transform.position + Vector3.up;
    }

    private void Start()
    {
        Build();
        glitchTimer = Random.Range(0.15f, glitchInterval);
    }

    private void Build()
    {
        if (built || mainRenderer == null) return;

        var rootGo = new GameObject("Boss3_Glitch");
        glitchRoot = rootGo.transform;
        glitchRoot.SetParent(transform, false);

        ghostR = MakeGhost("GhostR", new Color(1f, 0.16f, 0.16f, ghostBaseAlpha));
        ghostC = MakeGhost("GhostC", new Color(0.25f, 0.72f, 1f, ghostBaseAlpha));

        eyeL = MakeGlow("EyeL", EYE, LocalPos(-EYE_DX_FRAC, EYE_Y_FRAC), eyeGlowSizeFrac);
        eyeR = MakeGlow("EyeR", EYE, LocalPos(EYE_DX_FRAC, EYE_Y_FRAC), eyeGlowSizeFrac);

        aura = MakeGlow("Aura", AURA, LocalPos(0f, BODY_MID_FRAC), auraSizeFrac);
        aura.transform.localScale = new Vector3(auraSizeFrac * WORLD_W, auraSizeFrac * WORLD_H, 1f);

        // Vulnerability rings: two soft rings around the body that flare when the boss is
        // destabilised, making the punish window unmistakable. Hidden (alpha 0) otherwise.
        _vulnRing = MakeGlow("VulnRing", new Color(1f, 0.85f, 0.4f, 0f), LocalPos(0f, BODY_MID_FRAC), auraSizeFrac);
        _vulnRing2 = MakeGlow("VulnRing2", new Color(1f, 0.3f, 0.2f, 0f), LocalPos(0f, BODY_MID_FRAC), auraSizeFrac);

        BuildArms();
        BuildTendrils();
        BuildMotes();

        built = true;
    }

    // ── build helpers ──────────────────────────────────────────────────────

    private SpriteRenderer MakeGhost(string name, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(glitchRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _bodySprite;
        sr.color = col;
        return sr;
    }

    private SpriteRenderer MakeGlow(string name, Color col, Vector3 localPos, float sizeFrac)
    {
        var go = new GameObject(name);
        go.transform.SetParent(glitchRoot, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * (sizeFrac * SPRITE_WORLD_HEIGHT);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss3Sprites.SoftDot;
        sr.color = col;
        return sr;
    }

    private LineRenderer MakeLine(string name, int pts, float width, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(glitchRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 3;
        lr.numCornerVertices = 3;
        lr.positionCount = pts;
        lr.startWidth = width;
        lr.endWidth = width * 0.25f;
        lr.startColor = lr.endColor = col;
        return lr;
    }

    private void BuildArms()
    {
        _arms[0] = MakeLine("ArmL", 6, armWidth, THREAD);
        _arms[1] = MakeLine("ArmR", 6, armWidth, THREAD);
    }

    private void BuildTendrils()
    {
        // Crown hair: several thin tendrils fanning up/out from the head.
        int hair = 6;
        Vector2 crown = new Vector2(0f, 0.90f);
        for (int i = 0; i < hair; i++)
        {
            float t = hair > 1 ? i / (float)(hair - 1) : 0.5f;
            float dx = Mathf.Lerp(-0.22f, 0.22f, t);
            var td = new Tendril
            {
                rootFrac = new Vector2(crown.x + dx * 0.4f, crown.y),
                dir = new Vector2(dx, 0.55f).normalized,
                len = 0.6f + 0.35f * Mathf.Sin(t * Mathf.PI),
                width = threadWidth,
                phase = i * 0.9f,
                waveAmp = threadSway * (0.8f + 0.5f * Mathf.Sin(t * Mathf.PI)),
                waveFreq = threadSwaySpeed * Random.Range(0.85f, 1.2f),
                orderOffset = 1,
                color = THREAD
            };
            td.lr = MakeLine("Hair" + i, 7, td.width, td.color);
            _tendrils.Add(td);
        }

        // Body outline wisps: a couple down each side of the torso, drifting like the
        // silhouette's edge is dissolving into water.
        AddWisp(-0.11f, 0.58f, new Vector2(-0.5f, -0.7f), 0.45f, 0.3f);
        AddWisp(0.11f, 0.58f, new Vector2(0.5f, -0.7f), 0.45f, 1.4f);
        AddWisp(-0.08f, 0.34f, new Vector2(-0.6f, -0.5f), 0.35f, 2.1f);
        AddWisp(0.08f, 0.34f, new Vector2(0.6f, -0.5f), 0.35f, 3.0f);
    }

    private void AddWisp(float dxFrac, float yFrac, Vector2 dir, float len, float phase)
    {
        var td = new Tendril
        {
            rootFrac = new Vector2(dxFrac, yFrac),
            dir = dir.normalized,
            len = len,
            width = threadWidth * 0.85f,
            phase = phase,
            waveAmp = threadSway * 0.9f,
            waveFreq = threadSwaySpeed * 0.8f,
            orderOffset = -1,
            color = THREAD
        };
        td.lr = MakeLine("Wisp", 6, td.width, td.color);
        _tendrils.Add(td);
    }

    private void BuildMotes()
    {
        int n = Mathf.Max(0, moteCount);
        _motes = new SpriteRenderer[n];
        _moteBaseLocal = new Vector3[n];
        _motePhase = new float[n];
        _moteSpeed = new float[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Mote" + i);
            go.transform.SetParent(glitchRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Boss3Sprites.SoftDot;
            sr.color = new Color(THREAD.r, THREAD.g, THREAD.b, moteAlpha);
            float s = moteSizeFrac * SPRITE_WORLD_HEIGHT * Random.Range(0.4f, 1.1f);
            go.transform.localScale = new Vector3(s, s, 1f);
            _motes[i] = sr;
            _moteBaseLocal[i] = LocalPos(Random.Range(-0.28f, 0.28f), Random.Range(0.2f, 0.95f));
            _motePhase[i] = Random.Range(0f, Mathf.PI * 2f);
            _moteSpeed[i] = Random.Range(0.25f, 0.6f);
        }
    }

    // ── per-frame ──────────────────────────────────────────────────────────

    private void Update()
    {
        float dt = Time.deltaTime;
        glitchSpike = Mathf.Max(0f, glitchSpike - dt * 3.0f);

        // Baseline glitch is more frequent/violent when destabilised or enraged.
        float intervalMul = Mathf.Lerp(1f, 0.5f, Mathf.Max(_vulnLerp, _phase2Lerp * 0.7f));
        glitchTimer -= dt;
        if (glitchTimer <= 0f)
        {
            glitchTimer = glitchInterval * intervalMul * Random.Range(0.55f, 1.6f);
            glitchSpike = Mathf.Max(glitchSpike, Random.Range(0.45f, 0.9f) + _vulnLerp * 0.3f);
        }

        // Smooth the state lerps + decay the hit flash.
        _vulnLerp = Mathf.MoveTowards(_vulnLerp, _vulnerable ? 1f : 0f, dt * (_vulnerable ? 6f : 3f));
        _chargeLerp = Mathf.MoveTowards(_chargeLerp, _charging ? 1f : 0f, dt * (_charging ? 8f : 5f));
        _phase2Lerp = Mathf.MoveTowards(_phase2Lerp, _phase2 ? 1f : 0f, dt * 2f);
        _hitFlash = Mathf.Max(0f, _hitFlash - dt * 5f);

        // Smooth the arm raise (up fast, settle slow).
        for (int i = 0; i < 2; i++)
        {
            float speed = _armRaiseTgt[i] > _armRaiseCur[i] ? armRaiseUpSpeed : armRaiseDownSpeed;
            _armRaiseCur[i] = Mathf.MoveTowards(_armRaiseCur[i], _armRaiseTgt[i], speed * dt);

            // Regrow a severed arm, and hold the raise down to whatever exists so a
            // stump can't reach. Both are no-ops while _armGrow is 1.
            if (_armGrow[i] < 1f)
            {
                _armGrow[i] = Mathf.Min(1f, _armGrow[i] + dt / Mathf.Max(0.05f, _armRegrowSpan[i]));
                _armRaiseCur[i] = Mathf.Min(_armRaiseCur[i], _armGrow[i]);
            }
            _armBreakFlash[i] = Mathf.Max(0f, _armBreakFlash[i] - dt * 2.2f);
        }

        // Body fade + hit-flash brighten. NEVER move the root transform here (Boss3 owns position).
        if (mainRenderer != null)
        {
            float lift = _hitFlash * 0.6f;
            var mc = mainRenderer.color;
            mainRenderer.color = new Color(mc.r + lift, mc.g + lift, mc.b + lift, reveal);
        }
    }

    private void LateUpdate()
    {
        if (dead || !built || mainRenderer == null) return;

        int baseOrder = mainRenderer.sortingOrder;
        int layer = mainRenderer.sortingLayerID;
        Sprite frame = mainRenderer.sprite;
        bool flipX = facing < 0f;
        float faceSign = flipX ? -1f : 1f;
        float t = Time.time;

        glitchRoot.localPosition = Vector3.zero;   // stays glued to the (floating) root

        // Chromatic split (fast glitch) + a slow underwater swim.
        float chroma = (chromaticBase + chromaticBurst * glitchSpike) * SPRITE_WORLD_HEIGHT;
        float wobble = 0.65f + 0.35f * Mathf.Sin(t * 41f);
        Vector3 swim = new Vector3(Mathf.Sin(t * ghostSwimSpeed) * ghostSwimAmplitude,
                                   Mathf.Sin(t * ghostSwimSpeed * 0.7f + 1.3f) * ghostSwimAmplitude * 0.6f, 0f);

        Vector3 tear = Vector3.zero;
        if (glitchSpike > 0.25f)
        {
            float j = chromaticBurst * SPRITE_WORLD_HEIGHT * glitchSpike;
            tear = new Vector3(Random.Range(-j, j), Random.Range(-j, j) * 0.5f, 0f);
        }

        float ghostA = (ghostBaseAlpha + 0.32f * glitchSpike) * reveal;
        SyncGhost(ghostR, frame, flipX, baseOrder - 1, layer,
                  new Vector3(-chroma, -chroma * 0.35f, 0f) * wobble + tear + swim, ghostA);
        SyncGhost(ghostC, frame, flipX, baseOrder - 2, layer,
                  new Vector3(chroma, chroma * 0.35f, 0f) * wobble - tear - swim, ghostA);

        // Arms.
        DrawArm(0, t, baseOrder, layer, faceSign);
        DrawArm(1, t, baseOrder, layer, faceSign);

        // Floating tendrils.
        for (int i = 0; i < _tendrils.Count; i++)
            DrawTendril(_tendrils[i], t, baseOrder, layer, faceSign);

        // Motes drift up and recycle.
        DrawMotes(t, baseOrder, layer);

        // Eyes — pulse + flicker + brighten with glitch, and flare when vulnerable /
        // shift white-hot in phase 2. Hit-flash briefly blows them out for punchy feedback.
        float pulse = 0.62f + 0.38f * Mathf.Sin(t * 6.1f);
        float flick = (Random.value < 0.05f) ? 0.5f : 1f;
        float eyeBoost = 1f + _vulnLerp * 0.6f + _hitFlash * 0.8f;
        float eyeA = (eyeGlowAlpha * pulse * flick + glitchSpike * 0.35f) * eyeBoost * reveal;
        Color eyeCol = Color.Lerp(EYE, new Color(1f, 0.55f, 0.15f, 1f), _phase2Lerp);
        eyeCol = Color.Lerp(eyeCol, Color.white, _hitFlash * 0.6f);
        float eyeSize = eyeGlowSizeFrac * (1f + _vulnLerp * 0.25f + _phase2Lerp * 0.15f);
        PlaceEye(eyeL, -EYE_DX_FRAC, EYE_Y_FRAC, flipX, baseOrder + 2, layer, eyeA, eyeCol, eyeSize);
        PlaceEye(eyeR, EYE_DX_FRAC, EYE_Y_FRAC, flipX, baseOrder + 2, layer, eyeA, eyeCol, eyeSize);

        // Aura — hotter + larger in phase 2.
        if (aura != null)
        {
            float ap = 0.75f + 0.25f * Mathf.Sin(t * 2.2f);
            Color baseAura = Color.Lerp(AURA, new Color(0.8f, 0.12f, 0.06f, 1f), _phase2Lerp);
            Color ac = baseAura; ac.a = Mathf.Clamp01(auraAlpha * ap * (1f + _phase2Lerp * 0.4f)) * reveal;
            aura.color = ac;
            aura.sortingOrder = baseOrder - 3;
            aura.sortingLayerID = layer;
            float aw = (1f + 0.05f * Mathf.Sin(t * 3.0f) + glitchSpike * 0.12f) * (1f + _phase2Lerp * 0.18f);
            aura.transform.localScale = new Vector3(auraSizeFrac * WORLD_W * aw,
                                                    auraSizeFrac * WORLD_H * aw, 1f);
        }

        // Exposed rings: shown both while CHARGING an attack (cyan-white "burst me to
        // interrupt") and while DESTABILISED (orange-red "punish"). Punish wins the colour
        // if both are somehow active. The alpha follows whichever state is stronger.
        float expose = Mathf.Max(_vulnLerp, _chargeLerp);
        bool punishDominant = _vulnLerp >= _chargeLerp;
        Color ringColA = punishDominant ? new Color(1f, 0.85f, 0.4f) : new Color(0.55f, 0.85f, 1f);
        Color ringColB = punishDominant ? new Color(1f, 0.3f, 0.2f) : new Color(0.8f, 0.95f, 1f);
        DrawVulnRing(_vulnRing, t, baseOrder, layer, 5.0f, 0.9f, 1.0f, expose, ringColA);
        DrawVulnRing(_vulnRing2, t, baseOrder, layer, 3.3f, 0.7f, 1.35f, expose, ringColB);
    }

    private void DrawVulnRing(SpriteRenderer ring, float t, int baseOrder, int layer,
                              float pulseSpeed, float baseAlpha, float sizeMul,
                              float exposeLerp, Color tint)
    {
        if (ring == null) return;
        float a = exposeLerp * baseAlpha * (0.55f + 0.45f * Mathf.Sin(t * pulseSpeed));
        ring.sortingOrder = baseOrder - 4;
        ring.sortingLayerID = layer;
        ring.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(a) * reveal);
        // Expanding breathing scale so it reads as an unstable, cracking shell.
        float grow = (0.9f + 0.35f * exposeLerp) * sizeMul * (1f + 0.12f * Mathf.Sin(t * pulseSpeed * 1.3f));
        ring.transform.localScale = new Vector3(auraSizeFrac * WORLD_W * grow,
                                                auraSizeFrac * WORLD_H * grow, 1f);
    }

    // ── arm drawing ──────────────────────────────────────────────────────────

    private void DrawArm(int idx, float t, int baseOrder, int layer, float faceSign)
    {
        var lr = _arms[idx];
        if (lr == null) return;

        float sign = idx == 1 ? 1f : -1f;

        Vector3 shoulder = LocalToWorld(SHOULDER_DX_FRAC * sign * faceSign, SHOULDER_Y_FRAC);

        // Rest: hand droops down-outward and sways like it hangs in water.
        float sway = Mathf.Sin(t * armSwaySpeed + idx * 1.7f) * armSwayAmplitude;
        float sway2 = Mathf.Cos(t * armSwaySpeed * 0.7f + idx) * armSwayAmplitude * 0.6f;
        Vector3 restHand = LocalToWorld(RESTHAND_DX_FRAC * sign * faceSign, RESTHAND_Y_FRAC)
                           + new Vector3(sway, sway2, 0f);

        // Raised: reach from shoulder toward the aim point.
        Vector3 aim = _armAimWorld[idx];
        Vector3 toAim = aim - shoulder;
        Vector3 aimDir = toAim.sqrMagnitude > 1e-4f ? toAim.normalized : Vector3.up;
        float tremor = Mathf.Sin(t * 22f + idx) * 0.02f;
        Vector3 raisedHand = shoulder + aimDir * raisedArmLength
                             + new Vector3(0f, tremor, 0f);

        float raise = _armRaiseCur[idx];
        Vector3 hand = Vector3.Lerp(restHand, raisedHand, raise);

        // Severed: the limb only exists out to `grow` of its length, and the raw end
        // twitches while it knits. At grow == 1 this is an exact no-op, so the normal
        // arm is bit-for-bit what it always was.
        float grow = _armGrow[idx];
        if (grow < 1f)
        {
            float twitch = Mathf.Sin(t * 34f + idx * 2.1f) * armStumpTremor * (1f - grow);
            hand = Vector3.Lerp(shoulder, hand, Mathf.Max(0.04f, grow))
                   + new Vector3(twitch, twitch * 0.6f, 0f);
        }

        _handTipWorld[idx] = hand;

        // Elbow control point: bend outward at rest, straighten toward a menacing point
        // when raised. Add a little perpendicular swim.
        Vector3 mid = Vector3.Lerp(shoulder, hand, 0.5f);
        Vector3 armVec = hand - shoulder;
        Vector3 perp = new Vector3(-armVec.y, armVec.x, 0f).normalized;
        float bend = Mathf.Lerp(0.18f, 0.06f, raise) * sign * faceSign;
        float armSwim = Mathf.Sin(t * threadSwaySpeed + idx) * 0.04f;
        Vector3 elbow = mid + perp * (bend + armSwim);

        int n = lr.positionCount;
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            // Quadratic bezier shoulder→elbow→hand for a natural limb curve.
            Vector3 a = Vector3.Lerp(shoulder, elbow, f);
            Vector3 b = Vector3.Lerp(elbow, hand, f);
            Vector3 p = Vector3.Lerp(a, b, f);
            lr.SetPosition(i, p);
        }

        lr.startWidth = armWidth;
        lr.endWidth = armWidth * 0.28f;
        lr.sortingOrder = baseOrder + 1;
        lr.sortingLayerID = layer;

        // Faintly hotter at the hand when raised (about to strike).
        Color root = THREAD; root.a = reveal;
        Color tip = Color.Lerp(THREAD, new Color(0.5f, 0.08f, 0.12f, 1f), raise); tip.a = reveal;

        // A severed arm's raw end glows — bright at the moment of the break, cooling to
        // an ember as it grows back — so "it is coming back" is legible at a glance.
        if (grow < 1f)
        {
            Color raw = Color.Lerp(new Color(1f, 0.45f, 0.15f, 1f), Color.white, _armBreakFlash[idx]);
            tip = Color.Lerp(tip, raw, Mathf.Clamp01(1f - grow) * 0.9f);
            tip.a = reveal;
            lr.endWidth = armWidth * Mathf.Lerp(0.9f, 0.28f, grow);   // blunt, torn end
        }

        lr.startColor = root;
        lr.endColor = tip;
    }

    // ── tendril drawing ────────────────────────────────────────────────────

    private void DrawTendril(Tendril td, float t, int baseOrder, int layer, float faceSign)
    {
        var lr = td.lr;
        if (lr == null) return;

        lr.sortingOrder = baseOrder + td.orderOffset;
        lr.sortingLayerID = layer;

        Vector3 root = LocalToWorld(td.rootFrac.x * faceSign, td.rootFrac.y);
        Vector3 dirW = new Vector3(td.dir.x * faceSign * WORLD_W, td.dir.y * WORLD_H, 0f);
        if (dirW.sqrMagnitude < 1e-5f) dirW = Vector3.up;
        dirW.Normalize();
        Vector3 perp = new Vector3(-dirW.y, dirW.x, 0f);

        int n = lr.positionCount;
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            float wave = Mathf.Sin(t * td.waveFreq + td.phase + f * 3.2f) * td.waveAmp * f;
            Vector3 p = root + dirW * (td.len * f) + perp * wave;
            lr.SetPosition(i, p);
        }

        lr.startWidth = td.width;
        lr.endWidth = 0f;
        Color c = td.color; c.a = reveal;
        Color ce = td.color; ce.a = reveal * 0.05f;
        lr.startColor = c;
        lr.endColor = ce;
    }

    // ── mote drawing ────────────────────────────────────────────────────────

    private void DrawMotes(float t, int baseOrder, int layer)
    {
        if (_motes == null) return;
        for (int i = 0; i < _motes.Length; i++)
        {
            var sr = _motes[i];
            if (sr == null) continue;

            float rise = ((t * _moteSpeed[i] + _motePhase[i]) % 1f);   // 0..1 loop
            float drift = Mathf.Sin((t * _moteSpeed[i] + _motePhase[i]) * 6.28f) * 0.12f;
            Vector3 local = _moteBaseLocal[i] + new Vector3(drift, rise * WORLD_H * 0.5f, 0f);
            sr.transform.localPosition = local;

            // Fade in then out across the rise so they twinkle like suspended dust.
            float fade = Mathf.Sin(rise * Mathf.PI);
            Color c = sr.color; c.a = moteAlpha * fade * reveal; sr.color = c;
            sr.sortingOrder = baseOrder - 3;
            sr.sortingLayerID = layer;
        }
    }

    // ── shared helpers ───────────────────────────────────────────────────────

    private void SyncGhost(SpriteRenderer g, Sprite frame, bool flipX, int order, int layer,
                           Vector3 localOffset, float alpha)
    {
        if (g == null) return;
        g.sprite = frame;
        g.flipX = flipX;
        g.enabled = frame != null && alpha > 0.02f;
        g.sortingOrder = order;
        g.sortingLayerID = layer;
        g.transform.localPosition = localOffset;
        Color c = g.color; c.a = Mathf.Clamp01(alpha); g.color = c;
    }

    private void PlaceEye(SpriteRenderer e, float dxFrac, float yFrac, bool flipX,
                          int order, int layer, float alpha, Color col, float sizeFrac)
    {
        if (e == null) return;
        float sign = flipX ? -1f : 1f;
        e.transform.localPosition = LocalPos(dxFrac * sign, yFrac);
        e.transform.localScale = Vector3.one * (sizeFrac * SPRITE_WORLD_HEIGHT);
        e.sortingOrder = order;
        e.sortingLayerID = layer;
        Color c = col; c.a = Mathf.Clamp01(alpha); e.color = c;
    }

    private Vector3 LocalPos(float dxFrac, float yFrac)
    {
        return new Vector3(dxFrac * WORLD_W, (yFrac - PIVOT_Y) * WORLD_H, 0f);
    }

    private Vector3 LocalToWorld(float dxFrac, float yFrac)
    {
        return transform.TransformPoint(LocalPos(dxFrac, yFrac));
    }

    // ── silhouette generation (no arms — those are the live line-renderers) ──

    private static void EnsureBodySprite()
    {
        if (_bodySprite != null) return;

        var tex = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var seg = BuildSegments();
        var px = new Color32[TEX_W * TEX_H];
        const float RIM_PX = 3.0f;

        for (int y = 0; y < TEX_H; y++)
        {
            for (int x = 0; x < TEX_W; x++)
            {
                float inside = SampleInside(seg, x + 0.5f, y + 0.5f);
                float alpha = Mathf.Clamp01(inside + 0.5f);
                if (alpha <= 0.001f) { px[y * TEX_W + x] = new Color32(0, 0, 0, 0); continue; }

                float rimT = Mathf.Clamp01(inside / RIM_PX);
                Color c = Color.Lerp(RIM, CORE, rimT);
                c.a = alpha;
                px[y * TEX_W + x] = c;
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);

        _bodySprite = Sprite.Create(tex, new Rect(0, 0, TEX_W, TEX_H),
                                    new Vector2(0.5f, PIVOT_Y), PPU, 0, SpriteMeshType.FullRect);
        _bodySprite.name = "Boss3_Silhouette";
        _bodySprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private struct Seg { public float ax, ay, bx, by, ra, rb; }

    // Slim, hunched humanoid: legs, tapering torso, small head, and an insectoid crown
    // of thin filaments. NO arms — the arms are drawn live so they can raise and strike.
    private static Seg[] BuildSegments()
    {
        float cx = TEX_W * 0.5f;
        var list = new List<Seg>();

        void Add(float ax, float ay, float bx, float by, float ra, float rb)
            => list.Add(new Seg { ax = ax, ay = ay, bx = bx, by = by, ra = ra, rb = rb });

        // Legs — long, thin, slightly splayed.
        Add(cx - 8f, 92f, cx - 13f, 14f, 6.5f, 3.5f);
        Add(cx + 8f, 92f, cx + 13f, 14f, 6.5f, 3.5f);
        // Torso — narrow, tapering up to the shoulders.
        Add(cx, 90f, cx, 168f, 11f, 8.5f);
        // Neck + small head.
        Add(cx, 168f, cx, 182f, 4.5f, 4.5f);
        Add(cx, 192f, cx, 192f, 11f, 11f);

        // Crown — radiating thin filaments (the wing/antenna halo of the shade).
        Vector2 crown = new Vector2(cx, 198f);
        int spokes = 13;
        for (int i = 0; i < spokes; i++)
        {
            float t = i / (float)(spokes - 1);
            float ang = Mathf.Lerp(200f, -20f, t) * Mathf.Deg2Rad;
            float len = 30f + 26f * Mathf.Sin(t * Mathf.PI);
            float bend = (t - 0.5f) * 14f;
            Vector2 tip = crown + new Vector2(Mathf.Cos(ang) * len, Mathf.Sin(ang) * len + Mathf.Abs(bend) * 0.2f);
            Vector2 mid = Vector2.Lerp(crown, tip, 0.55f) + new Vector2(bend * 0.15f, 0f);
            Add(crown.x, crown.y, mid.x, mid.y, 1.7f, 1.2f);
            Add(mid.x, mid.y, tip.x, tip.y, 1.2f, 0.4f);
        }

        return list.ToArray();
    }

    private static float SampleInside(Seg[] segs, float px, float py)
    {
        float best = -999f;
        for (int i = 0; i < segs.Length; i++)
        {
            var s = segs[i];
            float vx = s.bx - s.ax, vy = s.by - s.ay;
            float wx = px - s.ax, wy = py - s.ay;
            float len2 = vx * vx + vy * vy;
            float tproj = len2 > 1e-4f ? Mathf.Clamp01((wx * vx + wy * vy) / len2) : 0f;
            float projx = s.ax + vx * tproj, projy = s.ay + vy * tproj;
            float dx = px - projx, dy = py - projy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float r = Mathf.Lerp(s.ra, s.rb, tproj);
            float inside = r - dist;
            if (inside > best) best = inside;
        }
        return best;
    }
}





