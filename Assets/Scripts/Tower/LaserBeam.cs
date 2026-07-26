using System.Collections.Generic;
using UnityEngine;

// Laser VFX
//   LaserVfxAssets     — the procedural textures/sprites the beams draw with,
//                        generated once per session and shared by everything below
//                        (and by Tower.InitializeLaser).
//   LaserBeam          — a self-contained laser-beam VFX component. NOTE: currently
//                        unreferenced; the live laser lives in Tower.cs's Laser
//                        Tower System region. Kept as-is in case you want it.
//   LaserChargeVisual  — the Laser Tower's idle 'charged capacitor' glow.

[DisallowMultipleComponent]
public class LaserBeam : MonoBehaviour
{
    //  Color
    [Header("Color & Intensity")]
    [Tooltip("Beam hue. The core burns toward white; the aura keeps this color.")]
    [ColorUsage(true, true)] public Color beamColor = new Color(1f, 0.18f, 0.12f, 1f);

    [Tooltip("Master brightness. Push past 1 if you have bloom for a hot look.")]
    [Range(0.25f, 4f)] public float intensity = 1.6f;

    //  Width
    [Header("Width (world units)")]
    public float coreWidth = 0.06f;
    public float glowWidth = 0.20f;
    public float auraWidth = 0.5f;
    [Tooltip("Live width breathing (0 = steady, 0.3 = ±30%).")]
    [Range(0f, 0.6f)] public float widthJitter = 0.16f;

    //  Motion
    [Header("Beam Motion")]
    [Range(1, 32)] public int segments = 16;
    [Tooltip("Sideways plasma waver amplitude (world units).")]
    public float waverAmplitude = 0.03f;
    public float waverSpeed = 10f;
    [Tooltip("Speed the energy pattern scrolls along the core.")]
    public float energyScrollSpeed = 7f;

    //  Timing
    [Header("Timing")]
    public float chargeTime = 0.30f;
    [Tooltip("Duration of the over-bright flash when the beam ignites.")]
    public float ignitionFlash = 0.10f;
    public float powerDownTime = 0.13f;
    [Tooltip("If Fire() isn't called for this long, auto power-down (safety net).")]
    public float holdTimeout = 0.08f;

    //  FX toggles
    [Header("Effects")]
    public bool chargeParticles = true;
    public bool impactSparks = true;
    public bool shockRings = true;

    //  Sorting
    [Header("Sorting (visibility fix)")]
    public string sortingLayer = "VFX";
    [Tooltip("Must beat the grass (~1600 on Default). 32000 is safe.")]
    public int sortingOrder = 32000;

    //  State
    private enum State { Idle, Charging, Firing, PoweringDown }
    private State state = State.Idle;

    private LineRenderer auraLR, glowLR, coreLR;
    private SpriteRenderer muzzle, impact;
    private ParticleSystem sparks, inflow;

    private Material addLineMat, addSpriteMat;
    private Texture2D beamTex, dotTex, ringTex;
    private Sprite dotSprite, ringSprite;

    private Vector3 originWS, endpointWS;
    private float stateTimer, envelope, lastFireTime, noiseSeed;

    private readonly List<Ring> rings = new List<Ring>();

    public bool IsFiring => state == State.Firing;

    //  Public API
    /// Call every frame while lasing. start = muzzle, end = hit point.
    /// First call after idle triggers the charge-up automatically
    public void Fire(Vector3 startWS, Vector3 endWS)
    {
        originWS = startWS;
        endpointWS = endWS;
        lastFireTime = Time.time;
        if (state == State.Idle || state == State.PoweringDown)
        {
            state = State.Charging;
            stateTimer = 0f;
            if (chargeParticles && inflow != null) { inflow.Clear(); inflow.Play(); }
        }
    }

    // Call when the target is lost / tower stops firing
    public void StopFiring()
    {
        if (state == State.Charging || state == State.Firing)
        {
            state = State.PoweringDown;
            stateTimer = 0f;
        }
    }

    // Kill everything instantly (e.g. tower destroyed)
    public void StopImmediate() { state = State.Idle; envelope = 0f; ApplyEnvelope(0f); }

    //  Lifecycle
    void Awake()
    {
        noiseSeed = Random.value * 1000f;
        BuildAssets();
        BuildRenderers();
        ApplyEnvelope(0f);
    }

    void OnDestroy()
    {
        // Only the two materials are ours. The textures and sprites belong to
        // LaserVfxAssets and are shared with every other laser — destroying them here
        // would blank out every beam on the map.
        LaserVfxAssets.SafeDestroy(addLineMat);
        LaserVfxAssets.SafeDestroy(addSpriteMat);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        stateTimer += dt;

        // Watchdog: if the tower stops calling Fire(), power down gracefully.
        if ((state == State.Firing || state == State.Charging) &&
            Time.time - lastFireTime > holdTimeout)
            StopFiring();

        switch (state)
        {
            case State.Idle:
                envelope = Mathf.MoveTowards(envelope, 0f, dt / Mathf.Max(0.01f, powerDownTime));
                break;

            case State.Charging:
                envelope = Mathf.MoveTowards(envelope, 0.3f, dt / Mathf.Max(0.01f, chargeTime));
                if (stateTimer >= chargeTime) Ignite();
                break;

            case State.Firing:
                envelope = Mathf.MoveTowards(envelope, 1f, dt * 12f);
                break;

            case State.PoweringDown:
                envelope = Mathf.MoveTowards(envelope, 0f, dt / Mathf.Max(0.01f, powerDownTime));
                if (chargeParticles && inflow != null && inflow.isPlaying) inflow.Stop();
                if (envelope <= 0.001f) state = State.Idle;
                break;
        }

        RenderBeam(dt);
        UpdateRings(dt);
    }

    private void Ignite()
    {
        state = State.Firing;
        stateTimer = 0f;
        envelope = 1.15f;   // brief over-bright snap-on
        if (chargeParticles && inflow != null) inflow.Stop();
        EmitSparks(14);
        if (shockRings)
        {
            SpawnRing(originWS, 0.15f, 1.0f, 0.28f);   // muzzle burst
            SpawnRing(endpointWS, 0.1f, 1.3f, 0.30f);  // impact burst
        }
    }

    //  Rendering
    private void RenderBeam(float dt)
    {
        ApplyEnvelope(envelope);

        bool visible = envelope > 0.002f;
        auraLR.enabled = glowLR.enabled = coreLR.enabled = visible;
        muzzle.enabled = visible;
        impact.enabled = visible && state != State.Charging;

        if (chargeParticles && inflow != null)
            inflow.transform.position = originWS;

        if (!visible) return;

        float t = Time.time;
        float e = Mathf.Clamp01(envelope);
        float over = Mathf.Max(0f, envelope - 1f);

        // Endpoint burns/jitters a touch for a "cutting" feel.
        Vector3 endJit = endpointWS + (Vector3)(Random.insideUnitCircle * 0.015f * e);

        Vector3 dir = endJit - originWS;
        float len = dir.magnitude;
        if (len < 1e-4f) { dir = Vector3.up; len = 1e-4f; }
        Vector3 fwd = dir / len;
        Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);

        // During charge the beam only reaches part-way (grows toward the target).
        float reach = state == State.Charging
            ? Mathf.SmoothStep(0f, 1f, stateTimer / Mathf.Max(0.01f, chargeTime)) * 0.6f
            : 1f;

        int pts = Mathf.Max(2, segments + 1);
        BuildPolyline(auraLR, pts, len, reach, fwd, perp, t, e, 0.6f);
        BuildPolyline(glowLR, pts, len, reach, fwd, perp, t, e, 1f);
        BuildPolyline(coreLR, pts, len, reach, fwd, perp, t, e, 1f);

        // Width breathing (+ ignition thickening).
        float jitter = 1f + (Mathf.PerlinNoise(t * 15f, noiseSeed) - 0.5f) * 2f * widthJitter;
        float snap = 0.55f + 0.45f * e;
        coreLR.widthMultiplier = coreWidth * jitter * snap * (1f + over * 1.6f);
        glowLR.widthMultiplier = glowWidth * (0.85f + 0.15f * jitter) * snap * (1f + over);
        auraLR.widthMultiplier = auraWidth * (0.9f + 0.1f * jitter) * (0.5f + 0.5f * e) * (1f + over * 0.5f);

        // Energy scroll along the core.
        var off = coreLR.material.mainTextureOffset;
        off.x -= energyScrollSpeed * dt * 0.15f;
        coreLR.material.mainTextureOffset = off;
        coreLR.material.mainTextureScale = new Vector2(Mathf.Max(1f, len * 0.8f), 1f);

        // Muzzle flare.
        muzzle.transform.position = originWS;
        float mPulse = 0.9f + 0.22f * Mathf.Sin(t * 34f);
        muzzle.transform.localScale = Vector3.one * Mathf.Lerp(0.18f, 0.6f, e) * mPulse * (1f + over);
        muzzle.transform.rotation = Quaternion.Euler(0, 0, t * 90f);

        // Impact hot-spot at the (reached) endpoint.
        Vector3 hit = originWS + fwd * (len * reach);
        impact.transform.position = hit;
        float iPulse = 0.85f + 0.3f * Mathf.PerlinNoise(t * 22f, 5.1f);
        impact.transform.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.55f, e) * iPulse * (1f + over);
        impact.transform.rotation = Quaternion.Euler(0, 0, -t * 120f);

        if (sparks != null) sparks.transform.position = hit;
        // Occasional sparks while cutting.
        if (impactSparks && state == State.Firing && Random.value < 0.15f) EmitSparks(1);
    }

    private void BuildPolyline(LineRenderer lr, int pts, float len, float reach,
                               Vector3 fwd, Vector3 perp, float t, float e, float waverScale)
    {
        lr.positionCount = pts;
        for (int i = 0; i < pts; i++)
        {
            float f = i / (float)(pts - 1);
            Vector3 p = originWS + fwd * (len * f * reach);
            if (waverAmplitude > 0f && i != 0 && i != pts - 1)
            {
                float shape = Mathf.Sin(f * Mathf.PI);
                float n = Mathf.PerlinNoise(noiseSeed + f * 3.5f, t * waverSpeed) - 0.5f;
                p += perp * (n * 2f * waverAmplitude * waverScale * shape * e);
            }
            lr.SetPosition(i, p);
        }
    }

    private void ApplyEnvelope(float env)
    {
        float e = Mathf.Clamp01(env);
        float over = Mathf.Max(0f, env - 1f);

        Color core = Color.Lerp(beamColor, Color.white, 0.7f + 0.3f * e) * intensity * (0.4f + 0.6f * e) * (1f + over);
        core.a = e;
        Color glow = beamColor * intensity * (0.35f + 0.65f * e); glow.a = e * 0.85f;
        Color aura = beamColor * intensity * (0.25f + 0.4f * e); aura.a = e * 0.4f;

        if (coreLR) SetLine(coreLR, core, 0.06f);
        if (glowLR) SetLine(glowLR, glow, 0.08f);
        if (auraLR) SetLine(auraLR, aura, 0.15f);

        if (muzzle) muzzle.color = Fade(Color.Lerp(beamColor, Color.white, 0.6f) * intensity, Mathf.Clamp01(e * 1.2f));
        if (impact) impact.color = Fade(Color.Lerp(beamColor, Color.white, 0.4f) * intensity, Mathf.Clamp01(e));
    }

    private static Color Fade(Color c, float a) { c.a = a; return c; }

    private static void SetLine(LineRenderer lr, Color c, float edge)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(c.a, edge),
                new GradientAlphaKey(c.a, 1f - edge),
                new GradientAlphaKey(c.a * 0.5f, 1f)
            });
        lr.colorGradient = g;
    }

    //  Rings
    private struct Ring { public SpriteRenderer sr; public float age, life, maxScale; public Color col; public bool active; }

    private void SpawnRing(Vector3 pos, float startScale, float maxScale, float life)
    {
        int idx = -1;
        for (int i = 0; i < rings.Count; i++) if (!rings[i].active) { idx = i; break; }
        if (idx == -1)
        {
            var go = new GameObject("LaserRing");
            go.transform.SetParent(transform, false);
            var newSr = go.AddComponent<SpriteRenderer>();
            newSr.sprite = ringSprite; newSr.material = addSpriteMat;
            newSr.sortingLayerName = ResolveSortingLayer(sortingLayer);
            newSr.sortingOrder = sortingOrder + 2;
            rings.Add(new Ring { sr = newSr });
            idx = rings.Count - 1;
        }
        var r = rings[idx];
        r.sr.transform.position = pos;
        r.sr.transform.localScale = Vector3.one * startScale;
        r.age = 0f; r.life = life; r.maxScale = maxScale;
        r.col = Color.Lerp(beamColor, Color.white, 0.5f) * intensity;
        r.active = true; r.sr.enabled = true;
        rings[idx] = r;
    }

    private void UpdateRings(float dt)
    {
        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            if (!r.active) continue;
            r.age += dt;
            float k = r.age / r.life;
            if (k >= 1f) { r.active = false; r.sr.enabled = false; rings[i] = r; continue; }
            float s = Mathf.Lerp(0.1f, r.maxScale, Mathf.SmoothStep(0f, 1f, k));
            r.sr.transform.localScale = Vector3.one * s;
            var c = r.col; c.a = 1f - k; r.sr.color = c;
            rings[i] = r;
        }
    }

    //  Sparks
    private void EmitSparks(int n) { if (impactSparks && sparks != null) sparks.Emit(n); }

    //  Build
    private void BuildAssets()
    {
        // Same pixels this class used to generate per instance — now built once and
        // shared (see LaserVfxAssets at the bottom of this file).
        beamTex = LaserVfxAssets.BeamTexture;
        dotTex = LaserVfxAssets.DotTexture;
        ringTex = LaserVfxAssets.RingTexture;
        dotSprite = LaserVfxAssets.DotSprite;
        ringSprite = LaserVfxAssets.RingSprite;

        Shader s = LaserVfxAssets.AdditiveShader;
        addLineMat = new Material(s) { mainTexture = beamTex }; LaserVfxAssets.Tint(addLineMat, Color.white);
        addSpriteMat = new Material(s) { mainTexture = dotTex }; LaserVfxAssets.Tint(addSpriteMat, Color.white);
    }

    private void BuildRenderers()
    {
        string layer = ResolveSortingLayer(sortingLayer);
        auraLR = MakeLine("LaserAura", new Material(addLineMat), layer, sortingOrder);
        glowLR = MakeLine("LaserGlow", new Material(addLineMat), layer, sortingOrder + 1);
        coreLR = MakeLine("LaserCore", new Material(addLineMat), layer, sortingOrder + 2);

        muzzle = MakeSprite("LaserMuzzle", dotSprite, layer, sortingOrder + 3);
        impact = MakeSprite("LaserImpact", dotSprite, layer, sortingOrder + 3);

        if (impactSparks) sparks = MakeSparks(layer, sortingOrder + 4);
        if (chargeParticles) inflow = MakeInflow(layer, sortingOrder + 1);
    }

    private LineRenderer MakeLine(string name, Material mat, string layer, int order)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.material = mat;
        lr.numCapVertices = 8; lr.numCornerVertices = 4;
        lr.textureMode = LineTextureMode.Tile;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingLayerName = layer; lr.sortingOrder = order;
        lr.positionCount = 2; lr.enabled = false;
        return lr;
    }

    private SpriteRenderer MakeSprite(string name, Sprite spr, string layer, int order)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = spr; sr.material = addSpriteMat;
        sr.sortingLayerName = layer; sr.sortingOrder = order; sr.enabled = false;
        return sr;
    }

    private ParticleSystem MakeSparks(string layer, int order)
    {
        var go = new GameObject("LaserSparks"); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.gravityModifier = 1.3f;
        main.startColor = Color.Lerp(beamColor, Color.white, 0.5f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.02f;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeGradient(Color.white, beamColor);
        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.material = addSpriteMat; psr.sortingLayerName = layer; psr.sortingOrder = order;
        ps.Stop();
        return ps;
    }

    private ParticleSystem MakeInflow(string layer, int order)
    {
        // Energy motes converging INTO the muzzle during charge-up.
        var go = new GameObject("LaserInflow"); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.startLifetime = 0.4f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = Color.Lerp(beamColor, Color.white, 0.4f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 60;
        var em = ps.emission; em.enabled = true; em.rateOverTime = 90f;
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.7f; sh.radiusThickness = 0f;   // spawn on the ring edge
        var vel = ps.velocityOverLifetime; vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.radial = new ParticleSystem.MinMaxCurve(-2.2f);   // pull toward center
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeGradient(beamColor, Color.white);
        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.material = addSpriteMat; psr.sortingLayerName = layer; psr.sortingOrder = order;
        ps.Stop();
        return ps;
    }

    //  Helpers
    private static Gradient FadeGradient(Color from, Color to)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    private static string ResolveSortingLayer(string preferred)
    {
        if (!string.IsNullOrEmpty(preferred))
            foreach (var l in SortingLayer.layers) if (l.name == preferred) return preferred;
        return "Default";
    }

    // FindAdditiveShader / TrySetTint / MakeDotTexture / MakeRingTexture /
    // MakeBeamTexture / SafeDestroy all moved to LaserVfxAssets below, which builds
    // them once per session instead of once per instance.
}


// ============================================================================
/// The procedural textures/sprites the laser VFX draws with, generated ONCE per
/// session and shared by every laser tower and by LaserChargeVisual.
///
/// These used to be built per tower inside Tower.InitializeLaser(): three
/// Texture2Ds (~37k pixels of per-pixel sqrt/sin work), two Sprite.Creates and
/// three Apply() GPU uploads, every single time a laser tower was dropped — and
/// they were never released, so each placement also leaked them. Both problems go
/// away by building them once and handing out the same references.
///
/// Nothing here is a project asset: it's all generated at runtime, so there is
/// still nothing to import. HideAndDontSave keeps Resources.UnloadUnusedAssets
/// (which runs on every single-mode scene load) from collecting them.
public static class LaserVfxAssets
{
    private static Texture2D beamTex, dotTex, ringTex, auraTex;
    private static Sprite dotSprite, ringSprite, auraSprite;
    private static Material spriteMat;
    private static Shader additiveShader;

    // Domain reload may be disabled in Play mode settings: statics survive, the
    // runtime objects they point at do not. The null checks in each getter already
    // cover that (Unity's == null is true for destroyed objects); this just makes
    // the intent explicit.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAssets()
    {
        beamTex = null; dotTex = null; ringTex = null; auraTex = null;
        dotSprite = null; ringSprite = null; auraSprite = null;
        spriteMat = null; additiveShader = null;
    }

    /// Tiling texture for the beam line renderers: hot centre, soft edges, a ridged
    /// energy pattern along U that the core scrolls.
    public static Texture2D BeamTexture
    {
        get
        {
            if (beamTex == null) beamTex = Keep(MakeBeam(128, 32));
            return beamTex;
        }
    }

    /// Radial falloff with a white-hot centre. Muzzle flare, impact spot, particles.
    public static Texture2D DotTexture
    {
        get
        {
            if (dotTex == null) dotTex = Keep(MakeDot(128));
            return dotTex;
        }
    }

    /// Crisp hollow ring: a narrow band with a hard-ish falloff. Reads as a drawn
    /// circle — right for the beam's ignition shock pulse, wrong for a soft glow.
    /// Use AuraTexture for anything meant to look like an aura.
    public static Texture2D RingTexture
    {
        get
        {
            if (ringTex == null) ringTex = Keep(MakeRing(128));
            return ringTex;
        }
    }

    public static Sprite DotSprite
    {
        get
        {
            if (dotSprite == null)
                dotSprite = Keep(Sprite.Create(DotTexture, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 256f));
            return dotSprite;
        }
    }

    public static Sprite RingSprite
    {
        get
        {
            if (ringSprite == null)
                ringSprite = Keep(Sprite.Create(RingTexture, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 256f));
            return ringSprite;
        }
    }

    /// Soft aura shell: a WIDE gaussian band that fades to nothing both at the
    /// centre and well before the texture edge, so it never shows a drawn outline at
    /// any scale. This is the one to use for pulsating glows.
    public static Texture2D AuraTexture
    {
        get
        {
            if (auraTex == null) auraTex = Keep(MakeAura(128));
            return auraTex;
        }
    }

    public static Sprite AuraSprite
    {
        get
        {
            if (auraSprite == null)
                auraSprite = Keep(Sprite.Create(AuraTexture, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 256f));
            return auraSprite;
        }
    }

    /// Additive shader, resolved once. Shader.Find is not free and was being called
    /// on every placement.
    public static Shader AdditiveShader
    {
        get
        {
            if (additiveShader == null)
            {
                string[] names =
                {
                    "Legacy Shaders/Particles/Additive", "Particles/Additive",
                    "Mobile/Particles/Additive", "Sprites/Default"
                };
                foreach (var n in names)
                {
                    var s = Shader.Find(n);
                    if (s != null) { additiveShader = s; break; }
                }
                if (additiveShader == null) additiveShader = Shader.Find("Sprites/Default");
            }
            return additiveShader;
        }
    }

    /// Shared additive material for glow sprites/particles. Safe to share: tinting
    /// is done through SpriteRenderer.color (vertex colours), never through the
    /// material, so no renderer needs its own copy. Assign it to
    /// Renderer.sharedMaterial — assigning to .material would silently clone it.
    public static Material SharedSpriteMaterial
    {
        get
        {
            if (spriteMat == null)
            {
                spriteMat = Keep(new Material(AdditiveShader) { mainTexture = DotTexture });
                Tint(spriteMat, Color.white);
            }
            return spriteMat;
        }
    }

    /// The legacy particle shaders default _TintColor to grey; force white so the
    /// vertex colour is what actually drives the look.
    public static void Tint(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    /// Destroy a runtime-created object safely from either edit or play mode.
    public static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }


    private static T Keep<T>(T o) where T : Object
    {
        // DontUnloadUnusedAsset (part of HideAndDontSave) — otherwise a scene load
        // can collect these out from under the cache.
        o.hideFlags = HideFlags.HideAndDontSave;
        return o;
    }

    //  generators (moved verbatim out of Tower.cs) 

    private static Texture2D MakeDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float c = (size - 1) * 0.5f; var px = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
            float hot = Mathf.Clamp01(1f - d * 2.4f);
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a + hot * 0.6f));
        }
        tex.SetPixels32(px); tex.Apply(); return tex;
    }

    private static Texture2D MakeRing(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float c = (size - 1) * 0.5f; var px = new Color32[size * size];
        float inner = 0.62f, outer = 0.9f, mid = (inner + outer) * 0.5f, half = (outer - inner) * 0.5f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - mid) / Mathf.Max(0.001f, half)); a *= a;
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels32(px); tex.Apply(); return tex;
    }

    /// Soft aura shell. Where MakeRing uses a narrow triangular band squared (crisp
    /// edges by design), this is a broad gaussian: peak at half radius, sigma 0.2, so
    /// the band bleeds smoothly across most of the disc. The centre falls to ~4% on
    /// its own, and an outer feather takes it to exactly 0 before the texture edge —
    /// without that, the gaussian would still be ~19% at the border and you'd see a
    /// cut circle where the sprite ends.
    private static Texture2D MakeAura(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float c = (size - 1) * 0.5f; var px = new Color32[size * size];
        const float mid = 0.5f, sigma = 0.2f;
        float twoSigmaSq = 2f * sigma * sigma;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float band = Mathf.Exp(-((d - mid) * (d - mid)) / twoSigmaSq);
            float feather = 1f - Mathf.SmoothStep(0.7f, 1f, d);   // -> 0 by the edge
            px[y * size + x] = new Color(1f, 1f, 1f, band * feather * 0.8f);
        }
        tex.SetPixels32(px); tex.Apply(); return tex;
    }

    private static Texture2D MakeBeam(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h]; float cy = (h - 1) * 0.5f;
        for (int y = 0; y < h; y++)
        {
            float dv = Mathf.Abs(y - cy) / cy; float edge = Mathf.Clamp01(1f - dv); edge *= edge;
            float hot = Mathf.Clamp01(1f - dv * 2.5f);
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float ridge = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 6f);
                float a = Mathf.Clamp01(edge * (0.7f + 0.3f * ridge) + hot * 0.6f);
                px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels32(px); tex.Apply(); return tex;
    }
}

/// The Laser Tower's "charged capacitor" idle: a visible spin-up at the muzzle,
/// then a held charge that pulses until the beam spends it.
///   place tower  -> Charging   (motes rush in, rings collapse inward, orb swells,
///                               flicker tightens into a whine, dips, then SNAPS)
///                -> Charged    (held orb breathing, a slow aura ring around it, and
///                               a pulse ring rippling outward every idlePingInterval)
///   beam fires   -> Discharging(orb dumps into the beam and goes dark; the beam's
///                               own muzzle flare takes over the muzzle point)
///   beam stops   -> short pause, then Charging -> Charged again
/// SCALE NOTE. Everything here is sized in the tower's LOCAL space, same as the
/// beam's own muzzle flare (which runs 0.18 -> 0.60), and the Laser Tower root is
/// scaled by spriteScale (0.55). So orbScale 0.8 lands at roughly 0.22 world units
/// across — a bit bigger than the muzzle flare, comfortably under the beam's 0.5
/// aura. If you rescale the tower art, these follow automatically.

[DisallowMultipleComponent]
public class LaserChargeVisual : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds to spin up from empty to fully charged.")]
    public float chargeDuration = 0.9f;

    [Tooltip("Pause after the beam stops before the tower starts drawing charge again.")]
    public float rechargeDelay = 0.2f;

    [Tooltip("How fast the held charge is dumped into the beam once the tower fires.")]
    public float dischargeDuration = 0.12f;

    [Header("Size & brightness")]
    [Tooltip("Size of the charged orb, in the tower's local space — the same space as " +
             "the beam's own muzzle flare, which runs 0.18 - 0.60.")]
    public float orbScale = 0.8f;

    [Tooltip("Multiplier on the beam's own intensity. 1 = burns as hot as the beam.")]
    [Range(0f, 2f)] public float brightness = 0.85f;

    [Header("Charging")]
    [Tooltip("Seconds between the rings that collapse inward while drawing charge. 0 = off.")]
    public float collapseRingInterval = 0.26f;

    [Tooltip("Energy motes converging into the muzzle.")]
    public bool convergingMotes = true;

    [Header("Charged idle")]
    [Tooltip("Breathing rate of the held charge, in Hz.")]
    public float idlePulseHz = 1.5f;

    [Tooltip("Seconds between the pulse rings that ripple outward from a held charge. 0 = off.")]
    public float idlePingInterval = 1.15f;

    [Tooltip("Slow rotating ring sitting around the held charge.")]
    public bool idleAuraRing = true;

    private enum State { Empty, Charging, Charged, Discharging }

    private Tower tower;
    private Transform root;
    private SpriteRenderer halo, core, auraRing;
    private ParticleSystem motes;

    private State state = State.Empty;
    private float charge;        // 0..1 stored charge
    private float stateTimer;
    private float flash;         // 0..1 decaying "just topped up" pop
    private float whinePhase;    // integrated, so the rising pitch doesn't phase-jump
    private float ringTimer, pingTimer;
    private float noiseSeed;
    private float lastOrbScale = -1f;

    private string layerName;
    private int ringOrder;

    // Ring pool. One pool drives all three ring effects (collapse / snap / idle
    // ping) — same shape as Tower.SpawnLaserRing, so it should read familiar.
    private struct Pulse
    {
        public SpriteRenderer sr;
        public float age, life, from, to, alpha, mix;
        public bool active;
    }
    private readonly List<Pulse> pulses = new List<Pulse>();

    private const float FlashFade = 0.22f;
    private const float MoteLifetime = 0.38f;

    /// 0..1 — how charged the tower reads right now.
    public float Charge => charge;

    void Awake()
    {
        tower = GetComponent<Tower>();
        if (tower == null || !tower.isLaserTower) { enabled = false; return; }

        noiseSeed = Random.value * 1000f;
        Build();
        Apply();
    }

    void OnDisable()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (root != null) root.gameObject.SetActive(true);
    }

    void Update()
    {
        if (tower == null || root == null) return;

        float dt = Time.deltaTime;

        // "Operational" mirrors the gate Tower.Update() uses before it runs any laser
        // logic, so a depleted / damaged-out / destroyed tower goes dark instead of
        // sitting there fully charged.
        bool operational = tower.IsOperational();
        bool firing = tower.IsLaserFiring;

        switch (state)
        {
            case State.Empty:
                charge = Mathf.MoveTowards(charge, 0f, dt / Mathf.Max(0.01f, dischargeDuration));
                if (!operational || firing) { stateTimer = 0f; break; }
                stateTimer += dt;
                if (stateTimer >= rechargeDelay) EnterCharging();
                break;

            case State.Charging:
                if (!operational || firing) { EnterEmpty(firing); break; }
                charge = Mathf.MoveTowards(charge, 1f, dt / Mathf.Max(0.01f, chargeDuration));

                // Rings collapse inward, faster as the charge builds.
                if (collapseRingInterval > 0f)
                {
                    ringTimer -= dt;
                    if (ringTimer <= 0f)
                    {
                        SpawnPulse(3.4f, 0.7f, collapseRingInterval * 1.6f, 0.3f, 0.25f);
                        ringTimer = Mathf.Lerp(collapseRingInterval, collapseRingInterval * 0.55f, charge);
                    }
                }
                if (charge >= 1f) EnterCharged();
                break;

            case State.Charged:
                if (!operational || firing) { EnterEmpty(firing); break; }
                charge = 1f;

                // The pulse that ripples out around the charge point.
                if (idlePingInterval > 0f)
                {
                    pingTimer -= dt;
                    if (pingTimer <= 0f)
                    {
                        SpawnPulse(0.9f, 3.3f, Mathf.Min(0.95f, idlePingInterval * 0.85f), 0.2f, 0.15f);
                        pingTimer = idlePingInterval;
                    }
                }
                break;

            case State.Discharging:
                charge = Mathf.MoveTowards(charge, 0f, dt / Mathf.Max(0.01f, dischargeDuration));
                if (!firing) EnterEmpty(false);
                break;
        }

        flash = Mathf.MoveTowards(flash, 0f, dt / FlashFade);

        // Pitch climbs with the charge. Integrating the phase (rather than sin(t*f))
        // keeps it from jumping every time the frequency moves.
        whinePhase += dt * Mathf.Lerp(6f, 30f, charge);

        UpdatePulses(dt);
        Apply();
    }

    //  state transitions 

    private void EnterCharging()
    {
        state = State.Charging;
        stateTimer = 0f;
        ringTimer = 0f;      // fire the first collapse ring immediately
    }

    private void EnterCharged()
    {
        state = State.Charged;
        charge = 1f;
        stateTimer = 0f;
        flash = 1f;                                   // the pop as the capacitor tops off
        pingTimer = idlePingInterval * 0.6f;
        SpawnPulse(0.55f, 2.6f, 0.34f, 0.45f, 0.6f);  // snap shell, outward and bright
    }

    private void EnterEmpty(bool firing)
    {
        state = firing ? State.Discharging : State.Empty;
        stateTimer = 0f;
    }

    //  rendering 

    private void Apply()
    {
        // The beam starts from transform.position + up * laserMuzzleOffset in WORLD
        // space, and the tower root is scaled by spriteScale — so track the muzzle in
        // world space and keep the artwork sized in the tower's local space, exactly
        // like the beam's own muzzle flare does.
        root.position = tower.LaserMuzzleWorldPosition;

        if (!Mathf.Approximately(lastOrbScale, orbScale)) ApplyMoteGeometry();

        float t = Time.time;
        float c = Mathf.Clamp01(charge);
        float ease = c * c * (3f - 2f * c);                       // smoothstep
        float breathe = 1f + 0.09f * c * Mathf.Sin(t * Mathf.PI * 2f * idlePulseHz);
        float flick = 1f + (Mathf.PerlinNoise(t * 11f, noiseSeed) - 0.5f) * 0.2f;

        // Rising whine while spinning up; silent once held.
        float whine = state == State.Charging
            ? 1f + 0.22f * c * Mathf.Sin(whinePhase * Mathf.PI * 2f)
            : 1f;

        // Capacitors dip just before they pop. Cheap, and it makes the snap land.
        float dip = (state == State.Charging && c > 0.82f)
            ? Mathf.Lerp(1f, 0.68f, (c - 0.82f) / 0.18f)
            : 1f;

        Color beam = tower.LaserBeamColor;
        float I = Mathf.Max(0.01f, tower.LaserIntensity) * brightness;
        bool lit = c > 0.004f || flash > 0.004f;

        // Soft coloured halo.
        halo.enabled = lit;
        if (lit)
        {
            halo.transform.localScale = Vector3.one * orbScale * (0.45f + 0.55f * ease) * breathe * whine * dip * (1f + 0.55f * flash);
            halo.color = Fade(beam * I * (0.4f + 0.6f * ease), Mathf.Clamp01((0.6f * ease) * dip + 0.5f * flash));
            halo.transform.localRotation = Quaternion.Euler(0f, 0f, t * 22f);
        }

        // White-hot core. Squared response so it only really arrives near full charge

        core.enabled = lit;
        if (lit)
        {
            core.transform.localScale = Vector3.one * orbScale * 0.42f * (0.12f + 0.88f * ease * ease) * flick * whine * dip * (1f + 0.9f * flash);
            core.color = Fade(Color.Lerp(beam, Color.white, 0.75f) * I * (0.5f + 0.5f * ease) * (1f + flash),
                              Mathf.Clamp01((ease * 1.15f) * dip + flash));
            core.transform.localRotation = Quaternion.Euler(0f, 0f, -t * Mathf.Lerp(40f, 130f, c));
        }

        // Slow ring hanging around a held charge.
        bool auraOn = idleAuraRing && ease > 0.2f;
        auraRing.enabled = auraOn;
        if (auraOn)
        {
            float aPulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * idlePulseHz);   // half the orb's rate
            // Breathe mostly in ALPHA and only a little in scale — a glow swelling in
            // place, rather than a circle visibly growing and shrinking.
            auraRing.transform.localScale = Vector3.one * orbScale * (1.5f + 0.1f * aPulse) * (0.7f + 0.3f * ease);
            auraRing.color = Fade(beam * I, (0.07f + 0.13f * aPulse) * ease);
        }

        if (motes != null)
        {
            bool want = convergingMotes && (state == State.Charging || state == State.Charged) && c > 0.02f;
            var em = motes.emission;
            em.rateOverTime = state == State.Charging ? 85f : 14f;   // trickle while held
            if (want && !motes.isPlaying) motes.Play();
            else if (!want && motes.isPlaying) motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private static Color Fade(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

    //  ring pool 

    /// Scales are multiples of orbScale. from > to reads as a collapse (accelerating
    /// inward, alpha peaking mid-flight); from < to reads as an outward pulse.
    private void SpawnPulse(float from, float to, float life, float alpha, float mix)
    {
        int idx = -1;
        for (int i = 0; i < pulses.Count; i++) if (!pulses[i].active) { idx = i; break; }
        if (idx == -1)
        {
            var go = new GameObject("Pulse");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LaserVfxAssets.AuraSprite;   // soft shell, never a drawn circle
            sr.sharedMaterial = LaserVfxAssets.SharedSpriteMaterial;
            sr.sortingLayerName = layerName;
            sr.sortingOrder = ringOrder;
            pulses.Add(new Pulse { sr = sr });
            idx = pulses.Count - 1;
        }

        var p = pulses[idx];
        p.age = 0f; p.life = Mathf.Max(0.01f, life);
        p.from = from; p.to = to; p.alpha = alpha; p.mix = mix;
        p.active = true;
        p.sr.enabled = true;
        pulses[idx] = p;
    }

    private void UpdatePulses(float dt)
    {
        Color beam = tower.LaserBeamColor;
        float I = Mathf.Max(0.01f, tower.LaserIntensity) * brightness;

        for (int i = 0; i < pulses.Count; i++)
        {
            var p = pulses[i];
            if (!p.active) continue;

            p.age += dt;
            float k = p.age / p.life;
            if (k >= 1f) { p.active = false; if (p.sr) p.sr.enabled = false; pulses[i] = p; continue; }

            if (p.sr)
            {
                bool collapsing = p.from > p.to;
                float e = collapsing ? k * k : Mathf.SmoothStep(0f, 1f, k);
                float a = collapsing ? Mathf.Sin(k * Mathf.PI) : 1f - k;
                p.sr.transform.localScale = Vector3.one * orbScale * Mathf.Lerp(p.from, p.to, e);
                p.sr.color = Fade(Color.Lerp(beam, Color.white, p.mix) * I, p.alpha * a);
            }
            pulses[i] = p;
        }
    }

    //  build 

    private void Build()
    {
        var go = new GameObject("LaserChargeOrb");
        go.transform.SetParent(transform, false);
        root = go.transform;

        layerName = tower.LaserSortingLayerName;
        int order = tower.LaserSortingOrder;
        ringOrder = order + 1;

        // Additive, so draw order between these barely matters — they're kept just
        // under the beam's muzzle flare so the beam always reads on top.
        auraRing = MakeSprite("Aura", LaserVfxAssets.AuraSprite, layerName, order + 1);
        halo = MakeSprite("Halo", LaserVfxAssets.DotSprite, layerName, order + 2);
        core = MakeSprite("Core", LaserVfxAssets.DotSprite, layerName, order + 2);

        if (convergingMotes) motes = MakeMotes(layerName, order + 1);
        ApplyMoteGeometry();
    }

    private SpriteRenderer MakeSprite(string name, Sprite sprite, string layer, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        // sharedMaterial, not material: assigning .material clones the material per
        // renderer. Tinting goes through sr.color (vertex colours), so one shared
        // additive material is correct and batches.
        sr.sharedMaterial = LaserVfxAssets.SharedSpriteMaterial;
        sr.sortingLayerName = layer;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    /// Motes spawn on a ring just outside the orb and fall inward, arriving about as
    /// they die. Re-derived whenever orbScale is touched, so the inspector stays live.
    private void ApplyMoteGeometry()
    {
        lastOrbScale = orbScale;
        if (motes == null) return;

        float radius = Mathf.Max(0.05f, orbScale * 1.9f);
        var sh = motes.shape;
        sh.radius = radius;

        var vel = motes.velocityOverLifetime;
        vel.radial = new ParticleSystem.MinMaxCurve(-radius / MoteLifetime);
    }

    private ParticleSystem MakeMotes(string layer, int order)
    {
        var go = new GameObject("Motes");
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();

        var m = ps.main;
        m.loop = true; m.playOnAwake = false;
        m.startLifetime = MoteLifetime;
        m.startSpeed = 0f;
        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        m.startColor = Color.Lerp(tower.LaserBeamColor, Color.white, 0.35f);
        m.simulationSpace = ParticleSystemSimulationSpace.Local;
        m.maxParticles = 64;

        var em = ps.emission; em.enabled = true; em.rateOverTime = 85f;

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radiusThickness = 0f;          // spawn on the ring edge, not filled

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(tower.LaserBeamColor, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = LaserVfxAssets.SharedSpriteMaterial;
        psr.sortingLayerName = layer;
        psr.sortingOrder = order;

        ps.Stop();
        return ps;
    }
}

