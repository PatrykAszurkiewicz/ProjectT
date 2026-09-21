using UnityEngine;
using System.Collections.Generic;

public class TrapMine : MonoBehaviour
{
    private float trapDuration = 8f;
    private float bossTrapDuration = 3f;
    private float proximityRadius = 1.2f;
    private float armDelay = 0.4f;

    private bool isArmed, isDisintegrating, hasTriggered;
    private float armTimer;

    private bool isCapturing;
    private float captureTimer, captureTrailTimer;
    private const float CAPTURE_DURATION = 0.25f;
    private GameObject captureTarget;
    private Vector3 captureStartPos;

    private SpriteRenderer baseRenderer, jawLRenderer, jawRRenderer;
    private SpriteRenderer springLRenderer, springRRenderer; // leaf springs, the iconic side arms
    private SpriteRenderer panRenderer;                      // center trigger pan
    private SpriteRenderer chainRenderer;                    // anchor chain trailing off
    private SpriteRenderer shadowRenderer;                   // grounded contact shadow
    private SpriteRenderer hingeLRenderer, hingeRRenderer;   // hinge bolts joining springs to jaws
    private SpriteRenderer nightGlowSR;                      // cool moonlight sheen (night only)
    private NightLight nightLight;                           // actually lights the ground at night
    private float nightGlowBaseA;                            // baseline alpha of the night sheen
    private float spawnScale;
    private float tremorPhase;    // faint tension shiver while armed
    private float glintTimer;     // occasional steel glint while armed

    private TrapHoldEffect activeHoldEffect;
    private const int SORT = 350;

    // Y-sorting (same formula as GrassCartoonOverlay / PlayerMovement)
    // sortPrecision=10 means 1 world-unit of Y = 10 sort-order units.
    // TRAP_SORT_BIAS = -4  →  the trap's highest sub-layer (jaws, +3) lands at baseOrder − 1
    private const float SORT_PRECISION = 10f;
    private const int SORT_ORDER_BASE = 1000;
    private const float SORT_Y_OFFSET = -0.15f;
    private const int TRAP_SORT_BIAS = -4;

    public void Initialize(float trapDuration, float bossTrapDuration, float proximityRadius, float armDelay)
    { this.trapDuration = trapDuration; this.bossTrapDuration = bossTrapDuration; this.proximityRadius = proximityRadius; this.armDelay = armDelay; armTimer = armDelay; }

    void Start() { BuildVisual(); spawnScale = 0f; }

    void Update()
    {
        if (isDisintegrating) return;
        if (isCapturing) { TickCapture(); return; }
        if (hasTriggered) { if (activeHoldEffect == null || activeHoldEffect.IsComplete) FadeOut(); return; }

        // Pop-in
        if (spawnScale < 1f)
        {
            spawnScale = Mathf.Min(spawnScale + Time.deltaTime / 0.2f, 1f);
            float e = 1f + 2.7f * Mathf.Pow(spawnScale - 1f, 3f) + 1.7f * Mathf.Pow(spawnScale - 1f, 2f);
            transform.localScale = Vector3.one * e;
        }
        if (!isArmed) { armTimer -= Time.deltaTime; if (armTimer <= 0f) isArmed = true; }

        // A set steel trap holds dead still. When armed we add only a faint tension
        // tremor and the occasional glint of light catching the steel — no glow, no runes.
        float tremor = 0f;
        if (isArmed)
        {
            tremorPhase += Time.deltaTime * 30f;
            tremor = Mathf.Sin(tremorPhase) * 0.004f + Mathf.Sin(tremorPhase * 2.3f) * 0.002f;

            glintTimer -= Time.deltaTime;
            if (glintTimer <= 0f)
            {
                glintTimer = Random.Range(1.6f, 3.4f);
                SpawnGlint();
            }
        }

        // Jaws: fully spread when armed (open maw), a touch tighter during the arm delay.
        // The tremor rides on top so the sprung steel looks tense, not lifeless.
        float openAngle = (isArmed ? 26f : 20f) + tremor * 60f;
        SetJaws(openAngle);

        // Night sheen — a slow cool shimmer, a little brighter once armed. Null (skipped) by day.
        if (nightGlowSR)
        {
            float shimmer = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.6f);
            float armedMul = isArmed ? 1.15f : 0.85f;
            Color c = nightGlowSR.color;
            c.a = nightGlowBaseA * shimmer * armedMul;
            nightGlowSR.color = c;
        }

        if (isArmed) CheckProximity();
    }

    // 'a' is the open amount in the same 0..~30 range the capture ramp uses.
    // The top jaw lifts up and the bottom jaw drops down to open the maw; at a<=0 they
    // meet at the centre and their teeth interlock (the bite). A slight tilt on the way
    // shut makes the sprung steel read as snapping rather than sliding.
    void SetJaws(float a)
    {
        float k = Mathf.Clamp01(a / 26f);           // 0 = shut, 1 = fully open
        float spread = Mathf.Lerp(0.008f, 0.10f, k); // vertical gap of each jaw from centre
        float tilt = (1f - k) * 4f;                  // small inward tilt as it closes

        if (jawLRenderer)
        {
            jawLRenderer.transform.localPosition = new Vector3(0f, 0.06f + spread, 0f);
            jawLRenderer.transform.localRotation = Quaternion.Euler(0, 0, tilt);
        }
        if (jawRRenderer)
        {
            jawRRenderer.transform.localPosition = new Vector3(0f, -0.06f - spread, 0f);
            jawRRenderer.transform.localRotation = Quaternion.Euler(0, 0, -tilt);
        }
    }


    // Y-sort every trap renderer each frame.

    void LateUpdate()
    {
        float sortY = transform.position.y + SORT_Y_OFFSET;
        int baseOrder = SORT_ORDER_BASE + Mathf.RoundToInt(-sortY * SORT_PRECISION) + TRAP_SORT_BIAS;

        if (shadowRenderer) shadowRenderer.sortingOrder = baseOrder - 3;
        if (chainRenderer) chainRenderer.sortingOrder = baseOrder - 2;
        if (springLRenderer) springLRenderer.sortingOrder = baseOrder;
        if (springRRenderer) springRRenderer.sortingOrder = baseOrder;
        if (baseRenderer) baseRenderer.sortingOrder = baseOrder + 1;
        if (panRenderer) panRenderer.sortingOrder = baseOrder + 2;
        if (jawLRenderer) jawLRenderer.sortingOrder = baseOrder + 3;
        if (jawRRenderer) jawRRenderer.sortingOrder = baseOrder + 3;
        if (hingeLRenderer) hingeLRenderer.sortingOrder = baseOrder + 4;
        if (hingeRRenderer) hingeRRenderer.sortingOrder = baseOrder + 4;
        if (nightGlowSR) nightGlowSR.sortingOrder = baseOrder + 5;
    }

    void TickCapture()
    {
        captureTimer += Time.deltaTime;
        float t = Mathf.Clamp01(captureTimer / CAPTURE_DURATION);
        float e = t * t * (3f - 2f * t);

        if (captureTarget)
        {
            var tp = new Vector3(transform.position.x, transform.position.y, captureTarget.transform.position.z);
            captureTarget.transform.position = Vector3.Lerp(captureStartPos, tp, e);
            var rb = captureTarget.GetComponent<Rigidbody2D>(); if (rb) rb.linearVelocity = Vector2.zero;
            captureTarget.transform.rotation = Quaternion.identity;
            captureTrailTimer += Time.deltaTime;
            if (captureTrailTimer > 0.03f) { captureTrailTimer = 0f; SpawnTrail(captureTarget.transform.position); }
        }

        float ja = t < 0.7f ? Mathf.Lerp(35f, 30f, t / 0.7f) : Mathf.Lerp(30f, -3f, Mathf.Pow((t - 0.7f) / 0.3f, 2f));
        SetJaws(Mathf.Max(ja, -3f));

        if (t >= 1f) { isCapturing = false; SetJaws(0f); Finalize(captureTarget); }
    }

    void SpawnTrail(Vector3 pos)
    {
        var go = new GameObject("GT"); go.transform.position = pos + (Vector3)(Random.insideUnitCircle * 0.15f);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = TrapSpriteCache.SparkDot;
        sr.sortingOrder = SORT + 10; sr.color = new Color(0.80f, 0.82f, 0.85f, 0.75f);
        go.transform.localScale = Vector3.one * Random.Range(0.06f, 0.12f);
        go.AddComponent<StruggleSparkFX>().Initialize(
            (Vector2)(transform.position - pos).normalized * 1.5f + Vector2.up * Random.Range(0.5f, 1.5f),
            Random.Range(0.15f, 0.3f));
    }

    public void Disintegrate()
    {
        if (isDisintegrating) return;
        isDisintegrating = true; isArmed = false;
        if (nightLight) nightLight.enabled = false; // light out with the trap

        // If currently holding an enemy, force-release them
        if (activeHoldEffect != null && !activeHoldEffect.IsComplete)
            activeHoldEffect.ForceRelease();
        activeHoldEffect = null;

        // If mid-capture animation, cancel it
        if (isCapturing)
        {
            isCapturing = false;
            captureTarget = null;
        }

        gameObject.AddComponent<DisintegrateTrap>().Initialize(AllRenderers());
    }

    SpriteRenderer[] AllRenderers()
    {
        var list = new System.Collections.Generic.List<SpriteRenderer>(11)
        { baseRenderer, jawLRenderer, jawRRenderer, springLRenderer, springRRenderer, panRenderer, chainRenderer, shadowRenderer, hingeLRenderer, hingeRRenderer };
        if (nightGlowSR) list.Add(nightGlowSR); // only exists at night
        return list.ToArray();
    }

    void CheckProximity()
    {
        Vector2 pos = transform.position;
        foreach (var go in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            if (!go || !go.activeInHierarchy) continue;
            if (go.GetComponent<TrapHoldEffect>() != null) continue;
            if (Vector2.Distance(pos, (Vector2)go.transform.position) <= proximityRadius) { BeginCapture(go); return; }
        }
    }

    void BeginCapture(GameObject enemy)
    {
        if (hasTriggered || isCapturing) return;
        hasTriggered = true; isCapturing = true; captureTimer = 0f; captureTrailTimer = 0f;
        captureTarget = enemy; captureStartPos = enemy.transform.position;
        var rb = enemy.GetComponent<Rigidbody2D>(); if (rb) rb.linearVelocity = Vector2.zero;
        var ec = enemy.GetComponent<EnemyController>(); if (ec) ec.ApplyFreeze(CAPTURE_DURATION + 0.1f);
        if (CameraShake.Instance) CameraShake.Instance.Shake(0.06f, 0.08f);
        // Capture SFX
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.trapCapture.IsNull)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.trapCapture, transform.position);
    }

    void Finalize(GameObject enemy)
    {
        if (!enemy) { FadeOut(); return; }
        var fx = new GameObject("SnapFX"); fx.transform.position = transform.position;
        fx.AddComponent<TrapSnapVFX>().Initialize(proximityRadius);

        // Bosses get shorter hold duration
        bool isBoss = enemy.GetComponent<Boss1>() != null || enemy.GetComponent<BaseBossStats>() != null;
        float holdDuration = isBoss ? bossTrapDuration : trapDuration;

        activeHoldEffect = enemy.AddComponent<TrapHoldEffect>();
        activeHoldEffect.Initialize(holdDuration, transform.position);
        if (CameraShake.Instance) CameraShake.Instance.Shake(0.10f, 0.10f);
    }

    void FadeOut()
    {
        if (isDisintegrating) return; isDisintegrating = true;
        if (nightLight) nightLight.enabled = false; // light out with the trap
        gameObject.AddComponent<DisintegrateTrap>().Initialize(AllRenderers());
    }

    void BuildVisual()
    {
        // Grounded contact shadow — squashed dark ellipse, offset down so the trap reads as
        // planted. This is what separates it from BRIGHT ground (snow, desert), no glow needed.
        var shadowGo = new GameObject("Shadow"); shadowGo.transform.SetParent(transform, false);
        shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
        shadowRenderer.sprite = TrapSpriteCache.SoftCircle;
        shadowRenderer.sortingOrder = SORT - 3;
        shadowRenderer.color = new Color(0f, 0f, 0f, 0.34f);
        shadowGo.transform.localPosition = new Vector3(0f, -0.14f, 0f);
        shadowGo.transform.localScale = new Vector3(1.5f, 0.82f, 1f);

        // Anchor chain trailing off to the lower-left, tucked behind the plate.
        chainRenderer = MakeSR("Chain", new Vector3(-0.22f, -0.20f, 0), TrapSpriteCache.ChainTail,
            SORT - 2, 0.55f, new Color(0.62f, 0.64f, 0.67f, 1f));
        chainRenderer.transform.localRotation = Quaternion.Euler(0, 0, 28f);

        // Two leaf springs — the iconic side arms. They sit under the plate edge and stick out.
        springLRenderer = MakeSR("SpringL", new Vector3(-0.34f, 0f, 0), TrapSpriteCache.GetSpring(true),
            SORT, 0.62f, Color.white);
        springRRenderer = MakeSR("SpringR", new Vector3(0.34f, 0f, 0), TrapSpriteCache.GetSpring(false),
            SORT, 0.62f, Color.white);

        // Round stamped-steel spring plate.
        baseRenderer = MakeSR("Base", Vector3.zero, TrapSpriteCache.TrapBase, SORT + 1, 0.66f, Color.white);

        // Center trigger pan.
        panRenderer = MakeSR("Pan", Vector3.zero, TrapSpriteCache.Pan, SORT + 2, 0.42f, Color.white);

        // Jaws — top and bottom toothed steel arcs forming the maw. SetJaws drives them open/shut.
        jawLRenderer = MakeSR("JawTop", new Vector3(0, 0.06f, 0), TrapSpriteCache.GetJaw(true), SORT + 3, 0.66f, Color.white);
        jawRRenderer = MakeSR("JawBottom", new Vector3(0, -0.06f, 0), TrapSpriteCache.GetJaw(false), SORT + 3, 0.66f, Color.white);

        // Hinge bolts on the left/right where the jaws pivot against the plate — the posts that
        // visually tie the springs, plate and jaws into one mechanism.
        hingeLRenderer = MakeSR("HingeL", new Vector3(-0.25f, 0f, 0), TrapSpriteCache.Bolt, SORT + 4, 0.17f, Color.white);
        hingeRRenderer = MakeSR("HingeR", new Vector3(0.25f, 0f, 0), TrapSpriteCache.Bolt, SORT + 4, 0.17f, Color.white);

        SetJaws(20f);

        SetupNightVisibility();
    }

    // Night mode darkens the whole scene, so a bare steel trap would vanish. We (1) register a
    // small, dim NightLight so the trap's patch of ground is actually lit through the darkness,
    // and (2) lay a subtle COOL sheen sprite over the steel — moonlight catching metal, not a
    // magic glow. Both no-op in daytime: NightLight does nothing without a NightOverlay, and
    // NightGlow.AddGlow returns null, so the look is unchanged by day.
    void SetupNightVisibility()
    {
        nightLight = gameObject.AddComponent<NightLight>();
        nightLight.radius = 1.5f;              // small pool — reveals the trap, not the battlefield
        nightLight.intensity = 0.5f;
        nightLight.lightColor = new Color(0.66f, 0.78f, 1f); // cold moonlight
        nightLight.warmTintStrength = 0.12f;   // mostly neutral, faint cool tint
        nightLight.flickerSpeed = 0f;          // steady; the shimmer comes from the sheen sprite
        nightLight.fadeInDuration = 0.35f;     // eases in with the pop-in

        var glowGo = NightGlow.AddGlow(
            gameObject,
            new Color(0.72f, 0.82f, 1f),       // cool steel-blue
            radius: 0.62f,
            intensity: 0.34f,
            sortingOrder: SORT + 5,
            localOffset: Vector3.zero);
        if (glowGo != null)
        {
            nightGlowSR = glowGo.GetComponent<SpriteRenderer>();
            nightGlowBaseA = nightGlowSR != null ? nightGlowSR.color.a : 0f;
        }
    }

    // Occasional bright specular glint sliding across a jaw — light catching cold steel.
    void SpawnGlint()
    {
        if (!jawLRenderer && !jawRRenderer) return;
        var host = (Random.value < 0.5f && jawLRenderer) ? jawLRenderer : jawRRenderer;
        if (!host) host = jawLRenderer ? jawLRenderer : jawRRenderer;
        if (!host) return;

        var go = new GameObject("Glint");
        go.transform.position = host.transform.position + (Vector3)(Random.insideUnitCircle * 0.14f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = TrapSpriteCache.SparkDot;
        sr.sortingOrder = host.sortingOrder + 1;
        sr.color = new Color(1f, 1f, 1f, 0.9f);
        go.transform.localScale = Vector3.one * Random.Range(0.05f, 0.09f);
        go.AddComponent<SteelGlintFX>();
    }

    SpriteRenderer MakeSR(string n, Vector3 lp, Sprite s, int order, float sc, Color c)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false);
        go.transform.localPosition = lp; go.transform.localScale = Vector3.one * sc;
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = s; sr.sortingOrder = order; sr.color = c; return sr;
    }

    void OnDrawGizmosSelected() { Gizmos.color = Color.green; Gizmos.DrawWireSphere(transform.position, proximityRadius); }
}


// Trap Sprite Cache

public static class TrapSpriteCache
{
    static Sprite _base, _jawL, _jawR, _glow, _rune, _soft, _spark, _chain, _root, _pulse, _snap;
    static Sprite _pressurePlate;
    static Sprite _vineWrap, _magicSeal, _constrictBand, _energyMote;

    public static Sprite TrapBase { get { if (!_base) _base = GenTrapBase(); return _base; } }
    public static Sprite Glow { get { if (!_glow) _glow = GenGlow(); return _glow; } }
    public static Sprite RuneRing { get { if (!_rune) _rune = GenRune(); return _rune; } }
    public static Sprite SoftCircle { get { if (!_soft) _soft = GenSoft(); return _soft; } }
    public static Sprite SparkDot { get { if (!_spark) _spark = GenSpark(); return _spark; } }
    public static Sprite ChainRing { get { if (!_chain) _chain = GenChain(); return _chain; } }
    public static Sprite RootTendril { get { if (!_root) _root = GenRoot(); return _root; } }
    public static Sprite PulseRing { get { if (!_pulse) _pulse = GenPulse(); return _pulse; } }
    public static Sprite SnapRing { get { if (!_snap) _snap = GenSnapRing(); return _snap; } }
    public static Sprite PressurePlate { get { if (!_pressurePlate) _pressurePlate = GenPressurePlate(); return _pressurePlate; } }
    public static Sprite VineWrap { get { if (!_vineWrap) _vineWrap = GenVineWrap(); return _vineWrap; } }
    public static Sprite MagicSeal { get { if (!_magicSeal) _magicSeal = GenMagicSeal(); return _magicSeal; } }
    public static Sprite ConstrictBand { get { if (!_constrictBand) _constrictBand = GenConstrictBand(); return _constrictBand; } }
    public static Sprite EnergyMote { get { if (!_energyMote) _energyMote = GenEnergyMote(); return _energyMote; } }

    static Sprite _pan, _springL, _springR, _chainTail, _bolt;
    public static Sprite Pan { get { if (!_pan) _pan = GenPan(); return _pan; } }
    public static Sprite ChainTail { get { if (!_chainTail) _chainTail = GenChainTail(); return _chainTail; } }
    public static Sprite Bolt { get { if (!_bolt) _bolt = GenBolt(); return _bolt; } }
    public static Sprite GetSpring(bool left)
    {
        if (left) { if (!_springL) _springL = GenSpring(true); return _springL; }
        else { if (!_springR) _springR = GenSpring(false); return _springR; }
    }

    public static Sprite GetJaw(bool left)
    {
        if (left) { if (!_jawL) _jawL = GenJaw(true); return _jawL; }
        else { if (!_jawR) _jawR = GenJaw(false); return _jawR; }
    }

    //  Helpers 
    static Texture2D MkTex(int w, int h) =>
        new Texture2D(w, h, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

    // Simple 1D Perlin-ish noise for texture variation
    static float Hash(float n) { return Mathf.Abs(Mathf.Sin(n * 127.1f + 311.7f) * 43758.5453f) % 1f; }
    static float Noise2D(float x, float y)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        float a = Hash(ix + iy * 57f), b = Hash(ix + 1 + iy * 57f);
        float c = Hash(ix + (iy + 1) * 57f), d = Hash(ix + 1 + (iy + 1) * 57f);
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    // Smooth anti-alias helper: returns 0..1 for edge at threshold with width
    static float AA(float val, float threshold, float width = 1.5f)
    {
        return Mathf.Clamp01((threshold - val) / width + 0.5f);
    }

    //  TRAP BASE — round stamped-steel spring plate: cold blue-grey steel, brushed grain,
    //  rust bleeding in from the rim, rivets, a recessed dark hub for the pan, and a bright
    //  specular edge so the silhouette reads on dark biomes (grass, marsh) without any glow.
    static Sprite GenTrapBase()
    {
        const int S = 128;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 ct = new Vector2(S * 0.5f, S * 0.49f);
        float outerR = S * 0.44f;

        // Cold steel palette (slightly blue) + rust
        Color steelDark = new Color(0.14f, 0.16f, 0.19f, 1f);
        Color steelMid = new Color(0.30f, 0.33f, 0.37f, 1f);
        Color steelLight = new Color(0.56f, 0.60f, 0.65f, 1f);
        Color steelSpec = new Color(0.86f, 0.90f, 0.96f, 1f); // hot specular edge
        Color rimDark = new Color(0.07f, 0.08f, 0.10f, 1f);
        Color rustDark = new Color(0.34f, 0.17f, 0.08f, 1f);
        Color rustLight = new Color(0.52f, 0.28f, 0.13f, 1f);
        Color rivetC = new Color(0.10f, 0.11f, 0.13f, 1f);

        float rivetDist = outerR * 0.80f;
        float rivetR = 3.4f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Vector2.Distance(p, ct);
                Color c = Color.clear;

                if (d <= outerR + 1.5f)
                {
                    float edgeAA = AA(d, outerR);
                    float nx = (x - ct.x) / outerR;
                    float ny = (y - ct.y) / outerR;
                    // Top-left light source
                    float lightDot = Mathf.Clamp01(0.5f + nx * 0.28f + ny * 0.36f);

                    // Brushed-steel body: fine circular grain
                    float ang = Mathf.Atan2(y - ct.y, x - ct.x);
                    float brushed = Noise2D(Mathf.Cos(ang) * 6f + d * 0.05f, Mathf.Sin(ang) * 6f) * 0.10f - 0.05f;
                    c = Color.Lerp(steelMid, steelLight, lightDot);
                    c.r += brushed; c.g += brushed; c.b += brushed;

                    // Rust patches bleeding in from the rim (noise-masked, stronger near edge)
                    float rustNoise = Noise2D(x * 0.09f + 13f, y * 0.09f - 7f);
                    float rustEdge = Mathf.Clamp01((d / outerR - 0.5f) * 2f);
                    float rust = Mathf.Clamp01((rustNoise - 0.55f) * 2.4f) * rustEdge;
                    if (rust > 0f)
                        c = Color.Lerp(c, Color.Lerp(rustDark, rustLight, rustNoise), rust * 0.8f);

                    // Recessed dark hub where the pan seats
                    float hubR = outerR * 0.40f;
                    if (d < hubR)
                        c = Color.Lerp(c, steelDark, Mathf.Clamp01(1f - d / hubR) * 0.55f);
                    // Thin engraved ring around the hub
                    if (Mathf.Abs(d - hubR) < 1.3f)
                        c = Color.Lerp(c, rimDark, (1f - Mathf.Abs(d - hubR) / 1.3f) * 0.7f);

                    // Beveled outer rim: dark lip + bright specular catch on the top-left
                    float rimWidth = 6f;
                    if (d > outerR - rimWidth)
                    {
                        float rimT = (d - (outerR - rimWidth)) / rimWidth;
                        c = Color.Lerp(c, rimDark, rimT * 0.85f);
                        // Specular sliver just inside the lip, brightest where lit
                        float specBand = Mathf.Abs(d - (outerR - rimWidth * 0.55f));
                        if (specBand < 1.6f)
                            c = Color.Lerp(c, steelSpec, (1f - specBand / 1.6f) * (0.25f + lightDot * 0.6f));
                    }

                    // Rivets around the plate
                    for (int i = 0; i < 8; i++)
                    {
                        float ra = i * 45f * Mathf.Deg2Rad;
                        Vector2 rp = ct + new Vector2(Mathf.Cos(ra), Mathf.Sin(ra)) * rivetDist;
                        float rd = Vector2.Distance(p, rp);
                        if (rd < rivetR + 1f)
                        {
                            float rivetAA = AA(rd, rivetR);
                            float rlx = (x - rp.x) / rivetR, rly = (y - rp.y) / rivetR;
                            float rl = Mathf.Clamp01(0.3f + rlx * 0.3f + rly * 0.4f);
                            Color rc = Color.Lerp(rivetC, steelLight, rl * 0.6f);
                            c = Color.Lerp(c, rc, rivetAA * 0.9f);
                        }
                    }

                    c.a = edgeAA;
                }
                px[y * S + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  PRESSURE PLATE
    static Sprite GenPressurePlate()
    {
        const int S = 96;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 ct = new Vector2(S * 0.5f, S * 0.5f);
        float plateR = S * 0.32f;

        Color plateDark = new Color(0.26f, 0.26f, 0.24f, 1f);
        Color plateMid = new Color(0.38f, 0.38f, 0.35f, 1f);
        Color plateHi = new Color(0.50f, 0.50f, 0.46f, 1f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Vector2.Distance(p, ct);
                Color c = Color.clear;

                // Octagonal shape using max of axis-aligned and diagonal distances
                float dx = Mathf.Abs(x - ct.x), dy = Mathf.Abs(y - ct.y);
                float octDist = Mathf.Max(dx, Mathf.Max(dy, (dx + dy) * 0.707f));
                float octR = plateR * 0.85f;

                if (octDist < octR + 1.5f)
                {
                    float aa = AA(octDist, octR);
                    float nx = (x - ct.x) / octR, ny = (y - ct.y) / octR;
                    float light = Mathf.Clamp01(0.45f + nx * 0.2f + ny * 0.3f);
                    c = Color.Lerp(plateDark, plateHi, light);

                    // Cross-hatch grip pattern
                    float gx = Mathf.Abs(((x + y) % 8) - 4f) / 4f;
                    float gy = Mathf.Abs(((x - y + S) % 8) - 4f) / 4f;
                    float grip = Mathf.Min(gx, gy);
                    if (grip < 0.3f && octDist < octR - 4f)
                        c = Color.Lerp(c, plateDark, (0.3f - grip) * 0.5f);

                    // Inset shadow around edge
                    if (octDist > octR - 3f)
                        c = Color.Lerp(c, plateDark, (octDist - (octR - 3f)) / 3f * 0.6f);

                    c.a = aa;
                }
                px[y * S + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  JAW — a curved steel arc with triangular fangs on the inner edge. topJaw=true bulges
    //  up (∩) with fangs pointing down; false is the vertical mirror (∪, fangs up). Together
    //  they form the maw around the pan. Pivot is the sprite centre; SetJaws spreads them.
    static Sprite GenJaw(bool topJaw)
    {
        const int W = 128, H = 84;
        var t = MkTex(W, H); var px = new Color[W * H];

        Color steelDark = new Color(0.12f, 0.14f, 0.16f, 1f);
        Color steelMid = new Color(0.34f, 0.37f, 0.41f, 1f);
        Color steelLight = new Color(0.60f, 0.64f, 0.70f, 1f);
        Color steelSpec = new Color(0.90f, 0.94f, 1.00f, 1f);
        Color toothTip = new Color(0.80f, 0.84f, 0.90f, 1f);

        Vector2 C = new Vector2(W * 0.5f, H * 0.86f - W * 0.60f); // arc centre below the texture
        float Rc = W * 0.60f;        // centreline radius of the jaw band
        float bandHalf = 7.5f;       // half thickness of the steel band
        float toothLen = 17f;        // fang length
        float innerR = Rc - bandHalf;
        int numTeeth = 10;
        float toothW = W / (float)numTeeth;

        for (int oy = 0; oy < H; oy++)
            for (int ox = 0; ox < W; ox++)
            {
                // Geometry sampled in "top jaw" space; shading uses screen y so light stays top.
                int gy = topJaw ? oy : (H - 1 - oy);
                Vector2 p = new Vector2(ox, gy);
                float dd = Vector2.Distance(p, C);
                Color c = Color.clear;

                float nlx = (ox - W * 0.5f) / (W * 0.5f);
                float nly = (oy - H * 0.5f) / (H * 0.5f);
                float lightDot = Mathf.Clamp01(0.5f - nlx * 0.12f + nly * 0.42f);

                // Steel band (the jaw spine)
                float bandDist = Mathf.Abs(dd - Rc);
                if (bandDist < bandHalf + 1f)
                {
                    float aa = AA(bandDist, bandHalf);
                    c = Color.Lerp(steelMid, steelLight, lightDot);
                    // Outer-edge specular catch
                    if (dd > Rc + bandHalf * 0.35f)
                        c = Color.Lerp(c, steelSpec, Mathf.Clamp01((dd - Rc) / bandHalf) * (0.3f + lightDot * 0.5f));
                    // Inner-edge shadow
                    if (dd < Rc - bandHalf * 0.35f)
                        c = Color.Lerp(c, steelDark, Mathf.Clamp01((Rc - dd) / bandHalf) * 0.5f);
                    c.a = aa;
                }

                // Fangs on the inner edge, pointing toward the trap centre
                if (dd < innerR + 1f && dd > innerR - toothLen)
                {
                    float depth = (innerR - dd) / toothLen;          // 0 at gum, 1 at tip
                    depth = Mathf.Clamp01(depth);
                    float tpos = ox / toothW;
                    float local = tpos - Mathf.Floor(tpos);          // 0..1 across a tooth
                    float halfW = Mathf.Lerp(0.46f, 0.015f, depth);  // taper to a point
                    float td = Mathf.Abs(local - 0.5f);
                    if (td < halfW + 0.02f)
                    {
                        float taa = Mathf.Clamp01((halfW - td) / 0.03f + 0.5f);
                        Color tc = Color.Lerp(steelMid, toothTip, depth * 0.9f);
                        // Bright central ridge down each fang
                        if (td < 0.06f) tc = Color.Lerp(tc, steelSpec, (1f - td / 0.06f) * 0.5f);
                        if (taa > c.a) { tc.a = taa; c = tc; }
                    }
                }

                px[oy * W + ox] = c;
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
    }

    //  PAN — round steel trigger plate that seats in the base hub.
    static Sprite GenPan()
    {
        const int S = 72;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 ct = Vector2.one * S * 0.5f;
        float r = S * 0.42f;

        Color panDark = new Color(0.16f, 0.18f, 0.20f, 1f);
        Color panMid = new Color(0.30f, 0.33f, 0.36f, 1f);
        Color panHi = new Color(0.52f, 0.56f, 0.61f, 1f);
        Color slot = new Color(0.06f, 0.07f, 0.08f, 1f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), ct);
                Color c = Color.clear;
                if (d <= r + 1.5f)
                {
                    float aa = AA(d, r);
                    float nx = (x - ct.x) / r, ny = (y - ct.y) / r;
                    // Gentle dome: brighter toward top-left, darker rim
                    float light = Mathf.Clamp01(0.5f + nx * 0.25f + ny * 0.35f - (d / r) * 0.25f);
                    c = Color.Lerp(panDark, panHi, light);
                    // Rim lip
                    if (d > r - 3f) c = Color.Lerp(c, panDark, (d - (r - 3f)) / 3f * 0.8f);
                    // Trigger slot across the middle
                    if (Mathf.Abs(y - ct.y) < 2.2f && Mathf.Abs(x - ct.x) < r * 0.62f)
                        c = Color.Lerp(c, slot, 0.8f);
                    c = Color.Lerp(c, panMid, 0f);
                    c.a = aa;
                }
                px[y * S + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  LEAF SPRING — the iconic side arm. Points outward (eye at the far end), tapers toward
    //  the plate. left=true points left; right is mirrored.
    static Sprite GenSpring(bool left)
    {
        const int W = 96, H = 52;
        var t = MkTex(W, H); var px = new Color[W * H];

        Color steelDark = new Color(0.13f, 0.15f, 0.17f, 1f);
        Color steelMid = new Color(0.31f, 0.34f, 0.38f, 1f);
        Color steelLight = new Color(0.58f, 0.62f, 0.68f, 1f);
        Color steelSpec = new Color(0.88f, 0.92f, 0.98f, 1f);
        Color rust = new Color(0.44f, 0.23f, 0.11f, 1f);

        float cy = H * 0.5f;
        // Eye (the loop) sits at the outer end
        float eyeX = left ? W * 0.16f : W * 0.84f;
        float eyeOuterR = 12f, eyeInnerR = 5.5f;

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = Color.clear;
                // Position along the arm: u=0 at plate (inner) end, 1 at eye (outer) end
                float u = left ? (1f - x / (float)W) : (x / (float)W);
                float yd = Mathf.Abs(y - cy);

                // Tapered flat bar: thick near plate, thinner toward the eye
                float halfH = Mathf.Lerp(H * 0.34f, H * 0.16f, u);
                if (yd < halfH + 1f && u < 0.9f)
                {
                    float aa = AA(yd, halfH);
                    float light = Mathf.Clamp01(0.5f + (cy - y) / halfH * 0.4f); // top-lit
                    c = Color.Lerp(steelMid, steelLight, light);
                    // Top specular edge
                    if (y < cy - halfH * 0.5f)
                        c = Color.Lerp(c, steelSpec, 0.4f);
                    // Bottom shadow edge
                    if (y > cy + halfH * 0.5f)
                        c = Color.Lerp(c, steelDark, 0.5f);
                    // A little rust streaking mid-arm
                    float rn = Noise2D(x * 0.12f, y * 0.12f + (left ? 4f : 9f));
                    if (rn > 0.66f) c = Color.Lerp(c, rust, (rn - 0.66f) * 1.6f);
                    c.a = aa;
                }

                // The eye/loop at the outer end
                float ed = Vector2.Distance(new Vector2(x, y), new Vector2(eyeX, cy));
                if (ed < eyeOuterR + 1f)
                {
                    if (ed > eyeInnerR)
                    {
                        float ringAA = AA(ed, eyeOuterR) * (1f - AA(ed, eyeInnerR));
                        float light = Mathf.Clamp01(0.5f + (cy - y) / eyeOuterR * 0.5f);
                        Color ec = Color.Lerp(steelMid, steelLight, light);
                        if (ringAA > c.a) { ec.a = Mathf.Clamp01(ringAA); c = ec; }
                    }
                }
                px[y * W + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, W, H), Vector2.one * 0.5f, W);
    }

    //  CHAIN TAIL — a few links running out to an anchor stake ring.
    static Sprite GenChainTail()
    {
        const int W = 96, H = 64;
        var t = MkTex(W, H); var px = new Color[W * H];

        Color linkMid = new Color(0.34f, 0.36f, 0.40f, 1f);
        Color linkLight = new Color(0.62f, 0.66f, 0.72f, 1f);
        Color linkDark = new Color(0.14f, 0.15f, 0.17f, 1f);

        // A gentle diagonal run of oval links from upper-right to lower-left.
        int links = 4;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = Color.clear;
                float best = 999f; bool inside = false;
                for (int i = 0; i < links; i++)
                {
                    float u = i / (float)(links - 1);
                    Vector2 lc = new Vector2(Mathf.Lerp(W * 0.82f, W * 0.20f, u), Mathf.Lerp(H * 0.74f, H * 0.30f, u));
                    // Oval link: ring shape
                    float ex = (x - lc.x) / 9f, ey = (y - lc.y) / 6.5f;
                    float ring = Mathf.Abs(Mathf.Sqrt(ex * ex + ey * ey) - 1f);
                    if (ring < 0.32f) { inside = true; best = Mathf.Min(best, ring); }
                }
                if (inside)
                {
                    float shade = 1f - best / 0.32f;
                    float light = Mathf.Clamp01(0.5f + (H * 0.5f - y) / H * 0.6f);
                    c = Color.Lerp(linkMid, linkLight, light * shade);
                    c = Color.Lerp(linkDark, c, 0.5f + shade * 0.5f);
                    c.a = Mathf.Clamp01(shade * 2.2f);
                }
                // Anchor stake ring at the lower-left end
                Vector2 stake = new Vector2(W * 0.18f, H * 0.30f);
                float sd = Vector2.Distance(new Vector2(x, y), stake);
                if (sd > 6.5f && sd < 11f)
                {
                    float rAA = AA(sd, 11f) * (1f - AA(sd, 6.5f));
                    float light = Mathf.Clamp01(0.5f + (stake.y - y) / 11f * 0.5f);
                    Color sc = Color.Lerp(linkMid, linkLight, light);
                    if (rAA > c.a) { sc.a = Mathf.Clamp01(rAA); c = sc; }
                }
                px[y * W + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, W, H), Vector2.one * 0.5f, W);
    }

    //  BOLT — small domed steel hinge bolt with a hex head and a top-left highlight.
    static Sprite GenBolt()
    {
        const int S = 40;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 ct = Vector2.one * S * 0.5f;
        float r = S * 0.42f;

        Color dark = new Color(0.10f, 0.11f, 0.13f, 1f);
        Color mid = new Color(0.34f, 0.37f, 0.41f, 1f);
        Color hi = new Color(0.72f, 0.76f, 0.82f, 1f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Color c = Color.clear;
                // Hexagonal head via max of three axis projections
                float dx = x - ct.x, dy = y - ct.y;
                float hex = Mathf.Max(Mathf.Abs(dy),
                            Mathf.Max(Mathf.Abs(dx * 0.866f + dy * 0.5f),
                                      Mathf.Abs(dx * 0.866f - dy * 0.5f)));
                if (hex < r + 1f)
                {
                    float aa = AA(hex, r);
                    // Domed shading: bright toward top-left, dark rim
                    float nx = dx / r, ny = dy / r;
                    float light = Mathf.Clamp01(0.5f + nx * 0.35f + ny * 0.45f - (hex / r) * 0.3f);
                    c = Color.Lerp(dark, hi, light);
                    if (hex > r - 2f) c = Color.Lerp(c, dark, (hex - (r - 2f)) / 2f * 0.8f);
                    // Small central pivot dimple
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < r * 0.3f) c = Color.Lerp(c, mid, (1f - d / (r * 0.3f)) * 0.5f);
                    c.a = aa;
                }
                px[y * S + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  GLOW
    static Sprite GenGlow()
    {
        const int S = 64;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        float r = S * 0.45f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float n = Mathf.Clamp01(d / r);

                // Multi-layer falloff: bright core, medium middle, soft outer
                float core = Mathf.Max(0f, 1f - n * 3f);          // very bright, tiny center
                float mid = Mathf.Pow(Mathf.Max(0, 1f - n), 2f); // medium glow
                float outer = Mathf.Max(0, 1f - n * n);           // soft wide halo

                float a = core * 0.6f + mid * 0.3f + outer * 0.15f;
                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  RUNE RING
    static Sprite GenRune()
    {
        const int S = 96;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        float outerR = S * 0.47f, innerR = S * 0.36f;
        float midR = (outerR + innerR) * 0.5f;
        float bandW = (outerR - innerR) * 0.5f;

        // Thin border rings
        float outerBorderR = outerR + 1.5f;
        float innerBorderR = innerR - 1.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;

                // Outer thin border ring
                float outerBorderD = Mathf.Abs(d - outerBorderR);
                if (outerBorderD < 1f) a = Mathf.Max(a, (1f - outerBorderD) * 0.5f);

                // Inner thin border ring
                float innerBorderD = Mathf.Abs(d - innerBorderR);
                if (innerBorderD < 1f) a = Mathf.Max(a, (1f - innerBorderD) * 0.5f);

                // Main glyph band
                if (d >= innerR && d <= outerR)
                {
                    float ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                    if (ang < 0) ang += 360f;

                    // 16 segments, alternating filled/empty with varying patterns
                    int segIdx = (int)(ang / 22.5f);
                    float segLocal = (ang % 22.5f) / 22.5f; // 0..1 within segment

                    if (segIdx % 2 == 0)
                    {
                        // Filled glyph segment with tapered edges
                        float taper = Mathf.Min(segLocal * 6f, (1f - segLocal) * 6f);
                        taper = Mathf.Clamp01(taper);

                        float radialFade = 1f - Mathf.Abs(d - midR) / bandW;
                        a = Mathf.Max(a, radialFade * taper);

                        // Inner detail: dot at segment center
                        float dotAng = (segIdx + 0.5f) * 22.5f * Mathf.Deg2Rad;
                        Vector2 dotPos = c + new Vector2(Mathf.Cos(dotAng), Mathf.Sin(dotAng)) * midR;
                        float dotD = Vector2.Distance(new Vector2(x, y), dotPos);
                        if (dotD < 2.5f)
                            a = Mathf.Max(a, (1f - dotD / 2.5f) * 0.8f);
                    }
                    else
                    {
                        // Cross-bar accent in empty segments (every other empty seg)
                        if (segIdx % 4 == 1)
                        {
                            float crossD = Mathf.Abs(d - midR);
                            if (crossD < 1.2f && segLocal > 0.35f && segLocal < 0.65f)
                                a = Mathf.Max(a, (1f - crossD / 1.2f) * 0.6f);
                        }
                    }
                }

                // Small accent dots at cardinal+diagonal points on outer edge
                for (int i = 0; i < 8; i++)
                {
                    float da = i * 45f * Mathf.Deg2Rad;
                    Vector2 dp = c + new Vector2(Mathf.Cos(da), Mathf.Sin(da)) * (outerR + 3.5f);
                    float dd = Vector2.Distance(new Vector2(x, y), dp);
                    if (dd < 2f) a = Mathf.Max(a, (1f - dd / 2f) * 0.7f);
                }

                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  SOFT CIRCLE
    static Sprite GenSoft()
    {
        const int S = 96;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        float r = S * 0.45f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float n = Mathf.Clamp01(d / r);
                // Smoother cubic falloff
                float a = 1f - n * n * n;
                a *= a; // square for extra softness at edges
                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  SPARK DOT
    static Sprite GenSpark()
    {
        const int S = 16;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float n = d / (S * 0.4f);
                // Hot core + soft halo
                float core = Mathf.Max(0, 1f - n * 2f);
                float halo = Mathf.Max(0, 1f - n);
                float a = core * 0.7f + halo * halo * 0.4f;
                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  CHAIN RING
    static Sprite GenChain()
    {
        const int S = 128;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        float outerR = S * 0.46f, innerR = S * 0.34f;
        float midR = (outerR + innerR) * 0.5f;
        float bandW = (outerR - innerR) * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;

                if (d >= innerR - 2f && d <= outerR + 2f)
                {
                    float ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                    if (ang < 0) ang += 360f;

                    // 12 chain links, each with gap
                    float linkAng = 30f;
                    float localAng = (ang % linkAng) / linkAng; // 0..1 in link

                    // Gap between links
                    if (localAng > 0.08f && localAng < 0.92f)
                    {
                        float radialDist = Mathf.Abs(d - midR);
                        float radialFade = 1f - radialDist / bandW;
                        radialFade = Mathf.Clamp01(radialFade);

                        // Taper at link ends
                        float endTaper = Mathf.Min((localAng - 0.08f) * 8f, (0.92f - localAng) * 8f);
                        endTaper = Mathf.Clamp01(endTaper);

                        // 3D shading: brighter on top half of the ring cross-section
                        float crossSec = (d - innerR) / (outerR - innerR); // 0..1
                        float shade = 0.6f + 0.4f * Mathf.Sin(crossSec * Mathf.PI);

                        // Sinusoidal variation along link for "twisted" look
                        float twist = 0.8f + 0.2f * Mathf.Sin(localAng * Mathf.PI * 2f);

                        a = radialFade * endTaper * shade * twist;
                    }
                }
                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  ROOT TENDRIL
    static Sprite GenRoot()
    {
        const int S = 96;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float ang = Mathf.Atan2(y - c.y, x - c.x);
                float n = d / (S * 0.5f); // normalized 0..1
                float td = 0;

                // 6 main tendrils
                for (int i = 0; i < 6; i++)
                {
                    float baseAng = i * Mathf.PI / 3f;

                    // Main tendril: wider at center, narrow at edge, with organic wobble
                    float wobble = Mathf.Sin(n * 12f + i * 1.7f) * 2.5f;
                    float angDiff = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, baseAng * Mathf.Rad2Deg + wobble));
                    float w = Mathf.Lerp(10f, 2f, n * n); // taper from center

                    if (angDiff < w && n < 0.92f)
                    {
                        float intensity = (1f - angDiff / w) * (1f - n);
                        // Organic edge: soft falloff
                        intensity *= Mathf.Clamp01((0.92f - n) * 5f);
                        td = Mathf.Max(td, intensity);
                    }

                    // Sub-branches: 2 per main tendril, diverge at ~40%
                    for (int b = 0; b < 2; b++)
                    {
                        float branchStart = 0.3f + b * 0.2f;
                        if (n > branchStart && n < 0.85f)
                        {
                            float branchAng = baseAng + (b == 0 ? 0.4f : -0.35f);
                            float bAngDiff = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, branchAng * Mathf.Rad2Deg));
                            float bw = Mathf.Lerp(5f, 1f, (n - branchStart) / (0.85f - branchStart));

                            if (bAngDiff < bw)
                            {
                                float bIntensity = (1f - bAngDiff / bw) * (1f - n) * 0.7f;
                                bIntensity *= Mathf.Clamp01((n - branchStart) * 8f);
                                td = Mathf.Max(td, bIntensity);
                            }
                        }
                    }
                }

                // Central hub
                if (n < 0.18f)
                    td = Mathf.Max(td, (1f - n / 0.18f) * 0.9f);

                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(td * 0.85f));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  PULSE RING
    static Sprite GenPulse()
    {
        const int S = 96;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        float r = S * 0.45f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float n = d / r;
                float a = 0f;

                // Soft inner fill
                float fill = Mathf.Clamp01(1f - n);
                a += fill * fill * 0.5f;

                // Primary ring at 70%
                float ring1 = Mathf.Abs(n - 0.70f);
                if (ring1 < 0.08f)
                    a += (1f - ring1 / 0.08f) * 0.5f;

                // Secondary thinner ring at 50%
                float ring2 = Mathf.Abs(n - 0.50f);
                if (ring2 < 0.04f)
                    a += (1f - ring2 / 0.04f) * 0.3f;

                // Radial energy lines (8 lines emanating outward)
                float ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;
                float lineAng = ang % 45f;
                float lineDist = Mathf.Min(lineAng, 45f - lineAng);
                if (lineDist < 1.5f && n > 0.3f && n < 0.9f)
                    a += (1f - lineDist / 1.5f) * 0.2f * (1f - Mathf.Abs(n - 0.6f) / 0.3f);

                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  SNAP RING
    static Sprite GenSnapRing()
    {
        const int S = 128;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        float outerR = S * 0.48f, innerR = S * 0.38f;
        float midR = (outerR + innerR) * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;

                // Main ring band
                if (d >= innerR && d <= outerR)
                {
                    float hw = (outerR - innerR) * 0.5f;
                    float bandFade = 1f - Mathf.Abs(d - midR) / hw;
                    a = Mathf.Max(a, bandFade);
                }

                // Outer shockwave echo ring (thin)
                float echoR = outerR + 5f;
                float echoD = Mathf.Abs(d - echoR);
                if (echoD < 2f)
                    a = Mathf.Max(a, (1f - echoD / 2f) * 0.4f);

                // Inner bright ring
                float innerBright = Mathf.Abs(d - innerR);
                if (innerBright < 1.5f)
                    a = Mathf.Max(a, (1f - innerBright / 1.5f) * 0.6f);

                // Energy tendril spikes (12 spikes radiating out from ring)
                float ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;
                for (int i = 0; i < 12; i++)
                {
                    float spikeAng = i * 30f;
                    float angDiff = Mathf.Abs(Mathf.DeltaAngle(ang, spikeAng));
                    float spikeWidth = 2.5f;
                    if (angDiff < spikeWidth && d > outerR && d < outerR + 10f)
                    {
                        float spikeFade = (1f - angDiff / spikeWidth) * (1f - (d - outerR) / 10f);
                        a = Mathf.Max(a, spikeFade * 0.6f);
                    }
                }

                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  VINE WRAP
    static Sprite GenVineWrap()
    {
        const int S = 128;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c);
            float ang = Mathf.Atan2(y - c.y, x - c.x);
            float n = d / (S * 0.5f); float a = 0f;
            for (int i = 0; i < 8; i++)
            {
                float bA = i * Mathf.PI * 2f / 8f, sA = bA + n * 1.8f;
                float aD = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, sA * Mathf.Rad2Deg));
                float vW = Mathf.Lerp(1.5f, 5f, n);
                if (aD < vW && n > 0.15f && n < 0.95f)
                    a = Mathf.Max(a, (1f - aD / vW) * Mathf.Clamp01((0.95f - n) * 4f) * Mathf.Clamp01((n - 0.15f) * 6f) * (aD < vW * 0.4f ? 1f : 0.7f));
                for (int th = 0; th < 3; th++)
                {
                    float tN = 0.35f + th * 0.2f; if (Mathf.Abs(n - tN) < 0.06f)
                    {
                        float lS = bA + tN * 1.8f + (th % 2 == 0 ? 0.15f : -0.15f);
                        float tD = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, lS * Mathf.Rad2Deg));
                        if (tD < 3f) a = Mathf.Max(a, (1f - tD / 3f) * 0.6f);
                    }
                }
            }
            if (n < 0.25f) a = Mathf.Max(a, (1f - n / 0.25f) * 0.15f);
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  MAGIC SEAL
    static Sprite GenMagicSeal()
    {
        const int S = 96;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        float oR = S * 0.46f, iR = oR * 0.65f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c);
            float ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg; if (ang < 0) ang += 360f;
            float a = 0f;
            if (Mathf.Abs(d - oR) < 2f) a = Mathf.Max(a, (1f - Mathf.Abs(d - oR) / 2f) * 0.9f);
            if (Mathf.Abs(d - iR) < 1.5f) a = Mathf.Max(a, (1f - Mathf.Abs(d - iR) / 1.5f) * 0.7f);
            for (int star = 0; star < 2; star++) for (int i = 0; i < 3; i++)
            {
                float lA = (i * 120f + star * 30f) * Mathf.Deg2Rad, nA = ((i + 1) * 120f + star * 30f) * Mathf.Deg2Rad;
                Vector2 p1 = c + new Vector2(Mathf.Cos(lA), Mathf.Sin(lA)) * iR, p2 = c + new Vector2(Mathf.Cos(nA), Mathf.Sin(nA)) * iR;
                Vector2 edge = p2 - p1; float len = edge.magnitude; if (len > 0.001f)
                {
                    Vector2 dir = edge / len; float proj = Mathf.Clamp(Vector2.Dot(new Vector2(x, y) - p1, dir), 0, len);
                    float lD = Vector2.Distance(new Vector2(x, y), p1 + dir * proj);
                    if (lD < 1.5f) a = Mathf.Max(a, (1f - lD / 1.5f) * 0.6f);
                }
            }
            for (int i = 0; i < 6; i++)
            {
                float dA = (i * 60f + 15f) * Mathf.Deg2Rad;
                float dotD = Vector2.Distance(new Vector2(x, y), c + new Vector2(Mathf.Cos(dA), Mathf.Sin(dA)) * (oR * 0.82f));
                if (dotD < 3f) a = Mathf.Max(a, (1f - dotD / 3f) * 0.8f);
            }
            if (d < 3.5f) a = Mathf.Max(a, (1f - d / 3.5f) * 0.9f);
            float mR = (oR + iR) * 0.5f, gD = Mathf.Abs(d - mR);
            if (gD < 3f && d > iR && d < oR)
            {
                int seg = (int)(ang / 30f); if (seg % 2 == 0)
                { float sL = (ang % 30f) / 30f; a = Mathf.Max(a, (1f - gD / 3f) * Mathf.Clamp01(Mathf.Min(sL * 5f, (1f - sL) * 5f)) * 0.5f); }
            }
            if (d > oR) a *= Mathf.Clamp01(1f - (d - oR) / 3f);
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }

    //  CONSTRICT BAND
    static Sprite GenConstrictBand()
    {
        const int W = 64, H = 16;
        var t = MkTex(W, H); var px = new Color[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            float xn = (x - W * 0.5f) / (W * 0.5f), yn = (y - H * 0.5f) / (H * 0.5f);
            float taper = Mathf.Clamp01((1f - Mathf.Abs(xn)) * 3f), bD = Mathf.Abs(yn);
            float a = bD < 0.8f ? (1f - bD / 0.8f) * taper * (bD < 0.2f ? 1f : 0.6f) : 0f;
            px[y * W + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, W, H), Vector2.one * 0.5f, W);
    }

    //  ENERGY MOTE
    static Sprite GenEnergyMote()
    {
        const int S = 12;
        var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * 0.5f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float dx = Mathf.Abs(x - c.x), dy = Mathf.Abs(y - c.y);
            float a = Mathf.Max(0, 1f - (dx + dy) / (S * 0.45f));
            a += Mathf.Max(0, 1f - Vector2.Distance(new Vector2(x, y), c) / (S * 0.2f)) * 0.5f;
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
    }
}


// TRAP HOLD EFFECT
// Grey tint: forced every frame on the enemy's main SpriteRenderer.
// This wins over EnemyController.ApplyFreeze()'s cyan 

public class TrapHoldEffect : MonoBehaviour
{
    private float duration, elapsed;
    private Vector2 holdPosition;
    private bool isComplete;
    private Rigidbody2D rb;

    // The enemy's main SpriteRenderer — we tint this directly
    private SpriteRenderer enemySR;

    // Below-enemy VFX
    private SpriteRenderer chainR, rootR, pulseR;
    private GameObject vfxRoot;
    private float chainTimer;

    // Above-enemy VFX
    private GameObject aboveVfxRoot;
    private SpriteRenderer vineWrapR, magicSealR;
    private SpriteRenderer[] constrictBands;
    private const int ABOVE_SORT = 9000;
    private float sealRotation, vinePhase, moteTimer;
    private float enemySpriteHeight = 1f;

    public bool IsComplete => isComplete;

    public void ForceRelease()
    {
        if (isComplete) return;
        Release();
    }

    public void Initialize(float dur, Vector2 pos) { duration = dur; holdPosition = pos; }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        enemySR = GetComponent<SpriteRenderer>();
        transform.rotation = Quaternion.identity;
        if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; }
        holdPosition = transform.position;

        if (enemySR != null && enemySR.sprite != null)
            enemySpriteHeight = enemySR.bounds.size.y;

        BuildVFX();
        BuildAboveEnemyVFX();
    }

    // Cached for LateUpdate tinting
    private float currentStrength = 1f;

    void Update()
    {
        if (isComplete) return;
        elapsed += Time.deltaTime;
        float t = elapsed / duration;
        if (elapsed >= duration) { Release(); return; }

        if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; }
        transform.position = new Vector3(holdPosition.x, holdPosition.y, transform.position.z);
        transform.rotation = Quaternion.identity;

        currentStrength = 1f - t; // 1 at capture → 0 at release

        // ── Below-enemy VFX ──
        if (chainR)
        {
            chainTimer += Time.deltaTime; float p = .5f + .5f * Mathf.Sin(chainTimer * 4);
            chainR.color = new Color(.3f, .9f, .5f, currentStrength * Mathf.Lerp(.4f, .8f, p));
            chainR.transform.localScale = Vector3.one * (Mathf.Lerp(.6f, .8f, p) * (1 - t * .3f));
            chainR.transform.Rotate(0, 0, 60 * Time.deltaTime);
        }
        if (rootR)
        {
            rootR.color = new Color(.2f, .6f, .3f, currentStrength * (.3f + .2f * Mathf.Sin(elapsed * 3)));
            rootR.transform.Rotate(0, 0, -20 * Time.deltaTime);
        }
        if (pulseR)
        {
            float f = Mathf.Lerp(1.5f, 4, t); float pv = Mathf.Max(0, Mathf.Sin(elapsed * f * Mathf.PI)); pv *= pv;
            pulseR.color = new Color(.4f, 1, .5f, pv * (.15f + t * .25f));
            pulseR.transform.localScale = Vector3.one * (.5f + pv * .4f);
        }
        if (t > .6f && Random.value < (t - .6f) * .3f) SpawnSpark();

        // ── Above-enemy VFX ──
        UpdateAboveEnemyVFX(t, currentStrength);
    }

    void UpdateAboveEnemyVFX(float t, float strength)
    {
        if (vineWrapR)
        {
            vinePhase += Time.deltaTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(vinePhase * 3f);
            float constrict = Mathf.Lerp(1.1f, 0.85f, Mathf.Clamp01(elapsed / (duration * 0.5f)));
            vineWrapR.transform.localScale = Vector3.one * enemySpriteHeight * constrict * (1f + Mathf.Sin(vinePhase * 2f) * 0.04f);
            vineWrapR.color = new Color(Mathf.Lerp(0.15f, 0.25f, pulse), Mathf.Lerp(0.7f, 0.9f, pulse),
                Mathf.Lerp(0.3f, 0.5f, pulse), strength * Mathf.Lerp(0.4f, 0.65f, pulse));
            vineWrapR.transform.Rotate(0, 0, -12f * Time.deltaTime);
        }
        if (magicSealR)
        {
            sealRotation += Time.deltaTime * 35f;
            magicSealR.transform.localRotation = Quaternion.Euler(0, 0, sealRotation);
            float sp = 0.5f + 0.5f * Mathf.Sin(elapsed * 2.5f);
            magicSealR.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 0.6f, sp) * Mathf.Min(enemySpriteHeight * 0.8f, 1.2f);
            magicSealR.color = new Color(Mathf.Lerp(0.4f, 0.6f, sp), Mathf.Lerp(0.9f, 1f, sp),
                Mathf.Lerp(0.5f, 0.7f, sp), strength * Mathf.Lerp(0.5f, 0.75f, sp));
        }
        if (constrictBands != null)
            for (int i = 0; i < constrictBands.Length; i++)
            {
                if (!constrictBands[i]) continue;
                float bp = 0.5f + 0.5f * Mathf.Sin(elapsed * 3f + i * 1.3f);
                constrictBands[i].transform.localScale = new Vector3(Mathf.Lerp(0.8f, 1.2f, bp) * enemySpriteHeight, 0.3f + bp * 0.1f, 1f);
                constrictBands[i].color = new Color(0.2f, Mathf.Lerp(0.7f, 0.95f, bp) * Mathf.Lerp(0.6f, 1f, t),
                    Mathf.Lerp(0.3f, 0.5f, bp), strength * Mathf.Lerp(0.25f, 0.5f, bp));
            }

        moteTimer += Time.deltaTime;
        if (moteTimer >= Mathf.Lerp(0.12f, 0.04f, t) && strength > 0.1f)
        { moteTimer = 0f; SpawnEnergyMote(); }
    }


    // LateUpdate runs AFTER all Update() calls
    // 1. Lock position/rotation 
    // 2. Force the grey tint on the enemy sprite

    void LateUpdate()
    {
        if (isComplete) return;
        transform.rotation = Quaternion.identity;
        transform.position = new Vector3(holdPosition.x, holdPosition.y, transform.position.z);

        // Grey tint — applied in LateUpdate so it always overwrites any
        // color changes from EnemyController, IceArmor, Projectile freeze, etc.
        if (enemySR != null)
        {
            float tintAmt = 0.55f * currentStrength;
            enemySR.color = Color.Lerp(Color.white, new Color(0.45f, 0.50f, 0.45f, 1f), tintAmt);
        }
    }

    void Release()
    {
        isComplete = true;
        // Restore to white — the universal default. EnemyController.UnfreezeEnemy()
        // will also set originalColor, but we do it here too for safety.
        if (enemySR != null) enemySR.color = Color.white;
        var b = new GameObject("TRB"); b.transform.position = transform.position; b.AddComponent<TrapReleaseBurstVFX>();
        if (vfxRoot) Destroy(vfxRoot);
        if (aboveVfxRoot) Destroy(aboveVfxRoot);
        Destroy(this);
    }

    void OnDestroy()
    {
        // Safety: restore white if destroyed unexpectedly
        if (enemySR != null && !isComplete) enemySR.color = Color.white;
        if (vfxRoot) Destroy(vfxRoot);
        if (aboveVfxRoot) Destroy(aboveVfxRoot);
    }

    void BuildVFX()
    {
        vfxRoot = new GameObject("TrapHoldVFX"); vfxRoot.transform.SetParent(transform, false);
        chainR = MakeVFX("Chain", Vector3.zero, TrapSpriteCache.ChainRing, 5050, .7f, new Color(.3f, .9f, .5f, .6f));
        rootR = MakeVFX("Root", new Vector3(0, -.15f, 0), TrapSpriteCache.RootTendril, 5049, .5f, new Color(.2f, .6f, .3f, .4f));
        pulseR = MakeVFX("Pulse", new Vector3(0, -.05f, 0), TrapSpriteCache.PulseRing, 5048, .5f, new Color(.4f, 1, .5f, 0));
    }

    SpriteRenderer MakeVFX(string n, Vector3 lp, Sprite s, int order, float sc, Color c)
    {
        var go = new GameObject(n); go.transform.SetParent(vfxRoot.transform, false);
        go.transform.localPosition = lp; go.transform.localScale = Vector3.one * sc;
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = s; sr.sortingOrder = order; sr.color = c; return sr;
    }

    void BuildAboveEnemyVFX()
    {
        aboveVfxRoot = new GameObject("TrapAboveVFX");
        aboveVfxRoot.transform.SetParent(transform, false);

        var vineGo = new GameObject("VineWrap");
        vineGo.transform.SetParent(aboveVfxRoot.transform, false);
        vineGo.transform.localPosition = new Vector3(0, enemySpriteHeight * 0.1f, 0);
        vineGo.transform.localScale = Vector3.one * enemySpriteHeight * 1.1f;
        vineWrapR = vineGo.AddComponent<SpriteRenderer>();
        vineWrapR.sprite = TrapSpriteCache.VineWrap;
        vineWrapR.sortingOrder = ABOVE_SORT;
        vineWrapR.color = new Color(0.2f, 0.8f, 0.4f, 0.5f);

        var sealGo = new GameObject("MagicSeal");
        sealGo.transform.SetParent(aboveVfxRoot.transform, false);
        sealGo.transform.localPosition = new Vector3(0, enemySpriteHeight * 0.55f, 0);
        sealGo.transform.localScale = Vector3.one * Mathf.Min(enemySpriteHeight * 0.8f, 1.2f) * 0.55f;
        magicSealR = sealGo.AddComponent<SpriteRenderer>();
        magicSealR.sprite = TrapSpriteCache.MagicSeal;
        magicSealR.sortingOrder = ABOVE_SORT + 2;
        magicSealR.color = new Color(0.5f, 1f, 0.6f, 0.6f);

        constrictBands = new SpriteRenderer[3];
        float[] bH = { 0.15f, 0.35f, 0.55f };
        for (int i = 0; i < 3; i++)
        {
            var bg = new GameObject($"Band_{i}");
            bg.transform.SetParent(aboveVfxRoot.transform, false);
            bg.transform.localPosition = new Vector3(0, enemySpriteHeight * bH[i], 0);
            bg.transform.localScale = new Vector3(enemySpriteHeight, 0.3f, 1f);
            var bsr = bg.AddComponent<SpriteRenderer>();
            bsr.sprite = TrapSpriteCache.ConstrictBand;
            bsr.sortingOrder = ABOVE_SORT + 1;
            bsr.color = new Color(0.2f, 0.8f, 0.4f, 0.35f);
            constrictBands[i] = bsr;
        }
    }

    void SpawnEnergyMote()
    {
        var go = new GameObject("EM");
        go.transform.position = transform.position + new Vector3(
            Random.Range(-enemySpriteHeight * 0.3f, enemySpriteHeight * 0.3f),
            Random.Range(-0.1f, enemySpriteHeight * 0.5f), 0);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = TrapSpriteCache.EnergyMote;
        sr.sortingOrder = ABOVE_SORT + 3;
        sr.color = new Color(Random.Range(0.3f, 0.5f), Random.Range(0.8f, 1f), Random.Range(0.4f, 0.7f), 0.9f);
        go.transform.localScale = Vector3.one * Random.Range(0.06f, 0.14f);
        go.AddComponent<EnergyMoteFX>().Initialize(
            new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(1.5f, 3f)),
            Random.Range(0.4f, 0.8f));
    }

    void SpawnSpark()
    {
        var go = new GameObject("SS"); go.transform.position = transform.position + (Vector3)(Random.insideUnitCircle * .3f);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = TrapSpriteCache.SparkDot; sr.sortingOrder = 5060;
        sr.color = new Color(Random.Range(.3f, .6f), Random.Range(.8f, 1f), Random.Range(.4f, .7f), 1);
        go.transform.localScale = Vector3.one * Random.Range(.04f, .10f);
        go.AddComponent<StruggleSparkFX>().Initialize(Random.insideUnitCircle.normalized * Random.Range(1f, 3f) + Vector2.up * Random.Range(1, 2.5f), Random.Range(.2f, .4f));
    }
}


// Brief in-place steel glint — a quick bright flash that fades, no movement.
public class SteelGlintFX : MonoBehaviour
{
    float life = 0.28f, maxLife = 0.28f; SpriteRenderer sr; float baseScale;
    void Start() { sr = GetComponent<SpriteRenderer>(); baseScale = transform.localScale.x; }
    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }
        float t = 1f - life / maxLife;              // 0..1
        float a = Mathf.Sin(t * Mathf.PI);          // fade in then out
        if (sr) { Color c = sr.color; c.a = a * 0.9f; sr.color = c; }
        transform.localScale = Vector3.one * baseScale * (0.7f + a * 0.6f);
    }
}

// Spark FX
public class StruggleSparkFX : MonoBehaviour
{
    Vector2 v; float l, ml; SpriteRenderer sr;
    public void Initialize(Vector2 vel, float life) { v = vel; l = ml = life; sr = GetComponent<SpriteRenderer>(); }
    void Update()
    {
        l -= Time.deltaTime; if (l <= 0) { Destroy(gameObject); return; }
        v += Vector2.down * 2 * Time.deltaTime; v *= .97f; transform.position += (Vector3)(v * Time.deltaTime);
        if (sr) { Color c = sr.color; c.a = l / ml; sr.color = c; }
    }
}

/// Rising energy mote — drifts upward with sine wobble, shrinks and fades.
public class EnergyMoteFX : MonoBehaviour
{
    private Vector2 velocity;
    private float life, maxLife;
    private SpriteRenderer sr;
    private float wobbleOffset, initialScale;

    public void Initialize(Vector2 vel, float lifetime)
    {
        velocity = vel; life = maxLife = lifetime;
        sr = GetComponent<SpriteRenderer>();
        wobbleOffset = Random.Range(0f, Mathf.PI * 2f);
        initialScale = transform.localScale.x;
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }
        float t = 1f - life / maxLife;
        float wobble = Mathf.Sin((Time.time + wobbleOffset) * 5f) * 0.5f;
        transform.position += (Vector3)((velocity + new Vector2(wobble, 0f)) * Time.deltaTime);
        if (sr)
        {
            Color c = sr.color; c.a = (1f - t * t) * 0.9f; sr.color = c;
            transform.localScale = Vector3.one * Mathf.Lerp(initialScale, initialScale * 0.15f, t);
        }
    }
}

public class TrapReleaseBurstVFX : MonoBehaviour
{
    float timer; const float D = .5f; readonly List<P> ps = new List<P>();
    void Start()
    {
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("RP"); go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = TrapSpriteCache.SparkDot; sr.sortingOrder = 5100;
            sr.color = new Color(Random.Range(.55f, .75f), Random.Range(.55f, .72f), Random.Range(.55f, .70f), 1);
            float a = Random.Range(0f, 360f) * Mathf.Deg2Rad, sp = Random.Range(2f, 5f), sz = Random.Range(.06f, .14f);
            go.transform.localScale = Vector3.one * sz;
            ps.Add(new P
            {
                g = go,
                s = sr,
                v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp + Vector2.up * Random.Range(.5f, 2f),
                l = Random.Range(.25f, .45f),
                ml = Random.Range(.25f, .45f),
                sz = sz
            });
        }
    }
    void Update()
    {
        timer += Time.deltaTime; if (timer >= D) { foreach (var p in ps) if (p.g) Destroy(p.g); Destroy(gameObject); return; }
        for (int i = ps.Count - 1; i >= 0; i--)
        {
            var p = ps[i]; if (!p.g) { ps.RemoveAt(i); continue; }
            p.l -= Time.deltaTime;
            if (p.l <= 0) { Destroy(p.g); ps.RemoveAt(i); continue; }
            p.v += Vector2.down * 3 * Time.deltaTime;
            p.g.transform.position += (Vector3)(p.v * Time.deltaTime); float t = 1 - p.l / p.ml;
            p.g.transform.localScale = Vector3.one * Mathf.Lerp(p.sz, .01f, t); Color c = p.s.color; c.a = 1 - t; p.s.color = c;
        }
    }
    class P { public GameObject g; public SpriteRenderer s; public Vector2 v; public float l, ml, sz; }
}

public class TrapSnapVFX : MonoBehaviour
{
    float timer; const float D = .4f; SpriteRenderer ringR; readonly List<S> ss = new List<S>();
    public void Initialize(float r)
    {
        var ro = new GameObject("SR"); ro.transform.SetParent(transform, false);
        ringR = ro.AddComponent<SpriteRenderer>(); ringR.sprite = TrapSpriteCache.SnapRing; ringR.sortingOrder = 5200;
        ringR.color = new Color(0.85f, 0.88f, 0.92f, .85f); ro.transform.localScale = Vector3.one * .1f;
        for (int i = 0; i < 10; i++)
        {
            var go = new GameObject("SS"); go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = TrapSpriteCache.SparkDot; sr.sortingOrder = 5210;
            sr.color = new Color(Random.Range(.75f, .95f), Random.Range(.78f, .95f), Random.Range(.80f, 1f), 1);
            float a = Random.Range(0f, 360f) * Mathf.Deg2Rad, sp = Random.Range(4f, 8f), sz = Random.Range(.08f, .15f);
            go.transform.localScale = Vector3.one * sz;
            ss.Add(new S { g = go, s = sr, v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, l = Random.Range(.15f, .35f), sz = sz });
        }
    }
    void Update()
    {
        timer += Time.deltaTime; float t = timer / D;
        if (t >= 1) { foreach (var s in ss) if (s.g) Destroy(s.g); Destroy(gameObject); return; }
        if (ringR) { ringR.transform.localScale = Vector3.one * Mathf.Lerp(.1f, 1.5f, Mathf.Sqrt(t)); Color c = ringR.color; c.a = .9f * (1 - t * t); ringR.color = c; }
        for (int i = ss.Count - 1; i >= 0; i--)
        {
            var s = ss[i]; if (!s.g) { ss.RemoveAt(i); continue; }
            s.l -= Time.deltaTime;
            if (s.l <= 0) { Destroy(s.g); ss.RemoveAt(i); continue; }
            s.g.transform.position += (Vector3)(s.v * Time.deltaTime); s.v *= .92f;
            s.g.transform.localScale = Vector3.one * Mathf.Max(s.sz * (s.l / .35f), .01f); Color c = s.s.color; c.a = s.l / .35f; s.s.color = c;
        }
    }
    class S { public GameObject g; public SpriteRenderer s; public Vector2 v; public float l, sz; }
}

public class DisintegrateTrap : MonoBehaviour
{
    SpriteRenderer[] rs; float timer; const float D = .5f;
    public void Initialize(SpriteRenderer[] r) { rs = r; }
    void Update()
    {
        timer += Time.deltaTime; float t = timer / D;
        if (t >= 1) { Destroy(gameObject); return; }
        transform.localScale = Vector3.one * Mathf.Max((1 - t) * (1 + .1f * Mathf.Sin(t * 30)), 0);
        float a = 1 - t * t; if (rs != null) foreach (var r in rs) if (r) { Color c = r.color; c.a = a; r.color = c; }
    }
}

// Empty marker component to identify grey overlay objects so we never re-process them.
public class TrapOverlayMarker : MonoBehaviour { }



