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
    private SpriteRenderer glowRenderer, runeRenderer, haloRenderer, shadowRenderer;
    private float blinkTimer;
    private readonly Color glowOn = new Color(0.15f, 0.85f, 0.45f, 0.9f);
    private readonly Color glowOff = new Color(0.15f, 0.85f, 0.45f, 0.15f);
    private float spawnScale;

    private TrapHoldEffect activeHoldEffect;
    private const int SORT = 350;

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

        // Glow
        if (glowRenderer)
        {
            float iv = isArmed ? 1.4f : 4.2f;
            blinkTimer += Time.deltaTime;
            float p = 0.5f + 0.5f * Mathf.Sin(blinkTimer / iv * Mathf.PI * 2f);
            glowRenderer.color = Color.Lerp(glowOff, glowOn, p);
            glowRenderer.transform.localScale = Vector3.one * (0.22f + p * 0.1f);
        }

        // Halo pulse — bright, pulsating glow
        if (haloRenderer)
        {
            float hp = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.5f);
            float ha = isArmed ? Mathf.Lerp(0.25f, 0.55f, hp) : 0.15f;
            haloRenderer.color = new Color(1f, 0.98f, 0.92f, ha);
            // Scale breathes with the pulse
            float haloScale = isArmed ? Mathf.Lerp(1.3f, 1.55f, hp) : 1.3f;
            haloRenderer.transform.localScale = Vector3.one * haloScale;
        }

        if (runeRenderer) runeRenderer.transform.Rotate(0, 0, -25f * Time.deltaTime);

        // Jaw breathe
        float b = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f);
        SetJaws(Mathf.Lerp(15f, 25f, b));

        if (isArmed) CheckProximity();
    }

    void SetJaws(float a)
    {
        if (jawLRenderer) jawLRenderer.transform.localRotation = Quaternion.Euler(0, 0, a);
        if (jawRRenderer) jawRRenderer.transform.localRotation = Quaternion.Euler(0, 0, -a);
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
        sr.sortingOrder = SORT + 10; sr.color = new Color(0.3f, 1f, 0.5f, 0.8f);
        go.transform.localScale = Vector3.one * Random.Range(0.06f, 0.12f);
        go.AddComponent<StruggleSparkFX>().Initialize(
            (Vector2)(transform.position - pos).normalized * 1.5f + Vector2.up * Random.Range(0.5f, 1.5f),
            Random.Range(0.15f, 0.3f));
    }

    public void Disintegrate()
    {
        if (isDisintegrating) return;
        isDisintegrating = true; isArmed = false;

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

    SpriteRenderer[] AllRenderers() => new[] { baseRenderer, jawLRenderer, jawRRenderer, glowRenderer, runeRenderer, haloRenderer, shadowRenderer };

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
        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.enemyAttack, transform.position);
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
        if (glowRenderer) glowRenderer.color = new Color(1f, 0.3f, 0.1f, 0.8f);
    }

    void FadeOut()
    {
        if (isDisintegrating) return; isDisintegrating = true;
        gameObject.AddComponent<DisintegrateTrap>().Initialize(AllRenderers());
    }

    void BuildVisual()
    {
        // Soft halo behind everything — warm-white ground glow, visible on any biome
        var haloGo = new GameObject("Halo"); haloGo.transform.SetParent(transform, false);
        haloRenderer = haloGo.AddComponent<SpriteRenderer>();
        haloRenderer.sprite = TrapSpriteCache.SoftCircle;
        haloRenderer.sortingOrder = SORT - 2;
        haloRenderer.color = new Color(1f, 0.98f, 0.92f, 0.35f);
        haloGo.transform.localScale = Vector3.one * 1.4f;

        // Dark ground shadow ring beneath the halo (provides contrast on snow/light backgrounds)
        var shadowGo = new GameObject("Shadow"); shadowGo.transform.SetParent(transform, false);
        shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
        shadowRenderer.sprite = TrapSpriteCache.SoftCircle;
        shadowRenderer.sortingOrder = SORT - 3;
        shadowRenderer.color = new Color(0f, 0f, 0f, 0.15f);
        shadowGo.transform.localScale = Vector3.one * 1.65f;

        // Rune
        runeRenderer = MakeSR("Rune", new Vector3(0, -0.02f, 0), TrapSpriteCache.RuneRing,
            SORT, 0.8f, new Color(0.15f, 0.55f, 0.35f, 0.4f));

        // Base — slightly larger for better visibility
        baseRenderer = MakeSR("Base", Vector3.zero, TrapSpriteCache.TrapBase, SORT + 1, 0.65f, Color.white);

        // Jaws — slightly larger
        jawLRenderer = MakeSR("JawL", new Vector3(-0.09f, 0.07f, 0), TrapSpriteCache.GetJaw(true), SORT + 2, 0.6f, Color.white);
        jawLRenderer.transform.localRotation = Quaternion.Euler(0, 0, 20f);
        jawRRenderer = MakeSR("JawR", new Vector3(0.09f, 0.07f, 0), TrapSpriteCache.GetJaw(false), SORT + 2, 0.6f, Color.white);
        jawRRenderer.transform.localRotation = Quaternion.Euler(0, 0, -20f);

        // Glow
        glowRenderer = MakeSR("Glow", new Vector3(0, 0.02f, 0), TrapSpriteCache.Glow, SORT + 3, 0.25f, glowOff);
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

    public static Sprite TrapBase { get { if (!_base) _base = GenTrapBase(); return _base; } }
    public static Sprite Glow { get { if (!_glow) _glow = GenGlow(); return _glow; } }
    public static Sprite RuneRing { get { if (!_rune) _rune = GenRune(); return _rune; } }
    public static Sprite SoftCircle { get { if (!_soft) _soft = GenSoft(); return _soft; } }
    public static Sprite SparkDot { get { if (!_spark) _spark = GenSpark(); return _spark; } }
    public static Sprite ChainRing { get { if (!_chain) _chain = GenChain(); return _chain; } }
    public static Sprite RootTendril { get { if (!_root) _root = GenRoot(); return _root; } }
    public static Sprite PulseRing { get { if (!_pulse) _pulse = GenPulse(); return _pulse; } }
    public static Sprite SnapRing { get { if (!_snap) _snap = GenSnapRing(); return _snap; } }

    public static Sprite GetJaw(bool left)
    {
        if (left) { if (!_jawL) _jawL = GenJaw(true); return _jawL; }
        else { if (!_jawR) _jawR = GenJaw(false); return _jawR; }
    }

    static Sprite GenTrapBase()
    {
        const int S = 48; var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 ct = new Vector2(S * .5f, S * .48f); float oR = S * .42f;
        Color pl = new Color(.30f, .30f, .28f, 1), plL = new Color(.45f, .45f, .40f, 1), rm = new Color(.20f, .20f, .18f, 1);
        float[] na = { 0, 45, 90, 135, 180, 225, 270, 315 }; float nD = oR * .85f, nR = 2.5f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            Vector2 p = new Vector2(x, y); float d = Vector2.Distance(p, ct); Color c = Color.clear;
            if (d <= oR)
            {
                float nx = (x - ct.x) / oR, ny = (y - ct.y) / oR;
                c = Color.Lerp(pl, plL, Mathf.Clamp01(.5f + nx * .3f + ny * .3f));
                if (d > oR - 3) c = Color.Lerp(c, rm, (d - oR + 3) / 3f);
                if (d > oR - 1) c.a = 1f - (d - oR + 1);
                foreach (float a in na)
                {
                    float r = a * Mathf.Deg2Rad; var np = ct + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * nD;
                    float nd = Vector2.Distance(p, np); if (nd < nR) c = Color.Lerp(c, rm, (1 - nd / nR) * .7f);
                }
            }
            px[y * S + x] = c;
        }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenJaw(bool left)
    {
        const int W = 32, H = 24; var t = MkTex(W, H); var px = new Color[W * H];
        Color m = new Color(.35f, .35f, .30f), md = new Color(.22f, .22f, .18f), th = new Color(.50f, .50f, .45f);
        float tw = W / 8f;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            Color c = Color.clear; float yn = y / (float)H, xc = W * .5f, hw = Mathf.Lerp(W * .45f, W * .3f, yn), xd = Mathf.Abs(x - xc);
            if (xd <= hw && y < H - 2)
            {
                c = Color.Lerp(m, md, yn * .6f); c.a = 1;
                if (y >= H - 8) { int ti = (int)(x / tw); if (ti % 2 == (left ? 0 : 1)) c = Color.Lerp(c, th, .7f * ((y - H + 8) / 6f)); }
                float ed = hw - xd; if (ed < 1.5f) c.a = Mathf.Clamp01(ed / 1.5f);
            }
            px[y * W + x] = c;
        }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, W, H), left ? new Vector2(1, 0) : new Vector2(0, 0), W);
    }

    static Sprite GenGlow()
    {
        const int S = 24; var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * .5f; float r = S * .45f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Vector2.Distance(new Vector2(x, y), c); float a = 1 - Mathf.Clamp01(d / r); a *= a; px[y * S + x] = new Color(1, 1, 1, a); }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenRune()
    {
        const int S = 32; var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * .5f; float oR = S * .46f, iR = S * .38f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c); float a = 0;
            if (d >= iR && d <= oR)
            {
                float ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg; if (ang < 0) ang += 360;
                if (((int)(ang / 22.5f)) % 2 == 0) { float mid = (iR + oR) * .5f, hw = (oR - iR) * .5f; a = 1 - Mathf.Abs(d - mid) / hw; }
            }
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenSoft()
    {
        const int S = 48; var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * .5f; float r = S * .45f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c);
            float n = Mathf.Clamp01(d / r); float a = 1 - n * n; px[y * S + x] = new Color(1, 1, 1, a);
        }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenSpark()
    {
        const int S = 8; var t = MkTex(S, S); var px = new Color[S * S]; Vector2 c = Vector2.one * S * .5f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Vector2.Distance(new Vector2(x, y), c); px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(1 - d / (S * .4f))); }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenChain()
    {
        const int S = 64; var t = MkTex(S, S); var px = new Color[S * S];
        Vector2 c = Vector2.one * S * .5f; float oR = S * .46f, iR = S * .34f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c); float a = 0;
            if (d >= iR && d <= oR)
            {
                float ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg; if (ang < 0) ang += 360;
                float lp = (ang % 30) / 30f; if (lp < .6f)
                {
                    float mid = (iR + oR) * .5f, hw = (oR - iR) * .5f;
                    a = (1 - Mathf.Abs(d - mid) / hw) * (.8f + .2f * Mathf.Sin(lp * Mathf.PI));
                }
            }
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenRoot()
    {
        const int S = 48; var t = MkTex(S, S); var px = new Color[S * S]; Vector2 c = Vector2.one * S * .5f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c);
            float ang = Mathf.Atan2(y - c.y, x - c.x); float n = d / (S * .5f); float td = 0;
            for (int i = 0; i < 6; i++)
            {
                float ba = i * Mathf.PI / 3f; float ad = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, ba * Mathf.Rad2Deg));
                float w = Mathf.Lerp(8, 3, n); if (ad < w && n < .9f) td = Mathf.Max(td, (1 - ad / w) * (1 - n));
            }
            if (n < .2f) td = Mathf.Max(td, 1 - n / .2f);
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(td * .8f));
        }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenPulse()
    {
        const int S = 48; var t = MkTex(S, S); var px = new Color[S * S]; Vector2 c = Vector2.one * S * .5f; float r = S * .45f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c);
            float n = d / r; float a = Mathf.Clamp01(1 - n); a = a * a * .7f; float rd = Mathf.Abs(n - .7f);
            if (rd < .1f) a += (1 - rd / .1f) * .3f; px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Sprite GenSnapRing()
    {
        const int S = 64; var t = MkTex(S, S); var px = new Color[S * S]; Vector2 c = Vector2.one * S * .5f;
        float oR = S * .48f, iR = S * .38f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c); float a = 0;
            if (d >= iR && d <= oR) { float mid = (iR + oR) * .5f, hw = (oR - iR) * .5f; a = 1 - Mathf.Abs(d - mid) / hw; }
            px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }

    static Texture2D MkTex(int w, int h) => new Texture2D(w, h, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
}


// TRAP HOLD EFFECT — grey overlay on ALL enemy SpriteRenderers

public class TrapHoldEffect : MonoBehaviour
{
    private float duration, elapsed;
    private Vector2 holdPosition;
    private bool isComplete;
    private Rigidbody2D rb;

    // Grey overlay — one per SpriteRenderer found on the enemy
    private struct OverlayEntry { public SpriteRenderer source; public SpriteRenderer overlay; public GameObject go; }
    private readonly List<OverlayEntry> overlays = new List<OverlayEntry>();

    // VFX
    private SpriteRenderer chainR, rootR, pulseR;
    private GameObject vfxRoot;
    private float chainTimer;

    public bool IsComplete => isComplete;

    /// Called by TrapMine.Disintegrate() when this trap is being replaced while holding an enemy.
    public void ForceRelease()
    {
        if (isComplete) return;
        Release();
    }

    public void Initialize(float dur, Vector2 pos) { duration = dur; holdPosition = pos; }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        transform.rotation = Quaternion.identity;
        if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; }
        holdPosition = transform.position;
        BuildOverlays();
        BuildVFX();
    }

    void BuildOverlays()
    {
        // Find ALL SpriteRenderers on the enemy (root + children)
        var renderers = GetComponentsInChildren<SpriteRenderer>(false);
        foreach (var sr in renderers)
        {
            if (sr == null || sr.sprite == null) continue;
            // Skip overlay renderers we already created
            if (sr.GetComponent<TrapOverlayMarker>() != null) continue;

            var go = new GameObject("GreyOL");
            go.AddComponent<TrapOverlayMarker>(); // Mark so we don't re-process
            // Parent to the SAME transform the source SR is on, so position/scale/rotation match
            go.transform.SetParent(sr.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var olSr = go.AddComponent<SpriteRenderer>();
            olSr.sprite = sr.sprite;
            olSr.flipX = sr.flipX;
            olSr.flipY = sr.flipY;
            olSr.sortingLayerName = sr.sortingLayerName;
            olSr.sortingLayerID = sr.sortingLayerID;
            olSr.sortingOrder = sr.sortingOrder + 1;
            olSr.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);

            overlays.Add(new OverlayEntry { source = sr, overlay = olSr, go = go });
        }
    }

    void Update()
    {
        if (isComplete) return;
        elapsed += Time.deltaTime;
        float t = elapsed / duration;
        if (elapsed >= duration) { Release(); return; }

        if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; }
        transform.position = new Vector3(holdPosition.x, holdPosition.y, transform.position.z);
        transform.rotation = Quaternion.identity;

        // Sync overlays with source sprites (animation frames, flip changes)
        float strength = 1f - t;
        float alpha = 0.7f * strength;
        for (int i = overlays.Count - 1; i >= 0; i--)
        {
            var e = overlays[i];
            if (!e.source || !e.overlay) { if (e.go) Destroy(e.go); overlays.RemoveAt(i); continue; }
            e.overlay.sprite = e.source.sprite;
            e.overlay.flipX = e.source.flipX;
            e.overlay.sortingOrder = e.source.sortingOrder + 1;
            e.overlay.color = new Color(0.2f, 0.2f, 0.2f, alpha);
        }

        // VFX
        if (chainR)
        {
            chainTimer += Time.deltaTime; float p = .5f + .5f * Mathf.Sin(chainTimer * 4);
            chainR.color = new Color(.3f, .9f, .5f, (1 - t) * Mathf.Lerp(.4f, .8f, p));
            chainR.transform.localScale = Vector3.one * (Mathf.Lerp(.6f, .8f, p) * (1 - t * .3f));
            chainR.transform.Rotate(0, 0, 60 * Time.deltaTime);
        }
        if (rootR)
        {
            rootR.color = new Color(.2f, .6f, .3f, (1 - t) * (.3f + .2f * Mathf.Sin(elapsed * 3)));
            rootR.transform.Rotate(0, 0, -20 * Time.deltaTime);
        }
        if (pulseR)
        {
            float f = Mathf.Lerp(1.5f, 4, t); float pv = Mathf.Max(0, Mathf.Sin(elapsed * f * Mathf.PI)); pv *= pv;
            pulseR.color = new Color(.4f, 1, .5f, pv * (.15f + t * .25f));
            pulseR.transform.localScale = Vector3.one * (.5f + pv * .4f);
        }
        if (t > .6f && Random.value < (t - .6f) * .3f) SpawnSpark();
    }

    void LateUpdate()
    {
        if (isComplete) return;
        transform.rotation = Quaternion.identity;
        transform.position = new Vector3(holdPosition.x, holdPosition.y, transform.position.z);
    }

    void Release()
    {
        isComplete = true;
        DestroyOverlays();
        var b = new GameObject("TRB"); b.transform.position = transform.position; b.AddComponent<TrapReleaseBurstVFX>();
        if (vfxRoot) Destroy(vfxRoot);
        Destroy(this);
    }

    void DestroyOverlays()
    {
        foreach (var e in overlays) if (e.go) Destroy(e.go);
        overlays.Clear();
    }

    void OnDestroy() { DestroyOverlays(); if (vfxRoot) Destroy(vfxRoot); }

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

    void SpawnSpark()
    {
        var go = new GameObject("SS"); go.transform.position = transform.position + (Vector3)(Random.insideUnitCircle * .3f);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = TrapSpriteCache.SparkDot; sr.sortingOrder = 5060;
        sr.color = new Color(Random.Range(.3f, .6f), Random.Range(.8f, 1f), Random.Range(.4f, .7f), 1);
        go.transform.localScale = Vector3.one * Random.Range(.04f, .10f);
        go.AddComponent<StruggleSparkFX>().Initialize(Random.insideUnitCircle.normalized * Random.Range(1f, 3f) + Vector2.up * Random.Range(1, 2.5f), Random.Range(.2f, .4f));
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

public class TrapReleaseBurstVFX : MonoBehaviour
{
    float timer; const float D = .5f; readonly List<P> ps = new List<P>();
    void Start()
    {
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("RP"); go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = TrapSpriteCache.SparkDot; sr.sortingOrder = 5100;
            sr.color = new Color(Random.Range(.2f, .5f), Random.Range(.7f, 1f), Random.Range(.3f, .6f), 1);
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
        ringR.color = new Color(.3f, 1, .5f, .9f); ro.transform.localScale = Vector3.one * .1f;
        for (int i = 0; i < 10; i++)
        {
            var go = new GameObject("SS"); go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = TrapSpriteCache.SparkDot; sr.sortingOrder = 5210;
            sr.color = new Color(Random.Range(.5f, .8f), Random.Range(.9f, 1f), Random.Range(.4f, .7f), 1);
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

/// Empty marker component to identify grey overlay objects so we never re-process them.
public class TrapOverlayMarker : MonoBehaviour { }
