using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  BOSS 5 — VISUALS


public static class Boss5Sprites
{
    private static Sprite _softDisc;
    private static Sprite _radialGlow;
    private static Sprite _edgeDisc;
    private static Sprite _slash;
    private static Sprite _fillQuad;
    private static Sprite _spark;
    private static Sprite _beam;
    private static Sprite _fallbackWeapon;
    private static readonly Dictionary<int, Sprite> _glowRings = new Dictionary<int, Sprite>();
    private static readonly Dictionary<int, Sprite> _dashRings = new Dictionary<int, Sprite>();
    private static readonly Dictionary<int, Sprite> _holeOverlays = new Dictionary<int, Sprite>();

    private static Material _additive;
    private static bool _additiveResolved;

    // Every cached sprite is checked with `!= null` before reuse. Unity destroys
    // runtime-created objects on play-mode exit, and its overloaded null check
    // reports a destroyed object as null — so the cache rebuilds itself on the next
    // session instead of handing out dead references. Same idiom Boss2WarningSprites
    // uses. Explicit clearing is belt-and-braces for domain-reload-off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _softDisc = _radialGlow = _edgeDisc = _slash = _fillQuad = null;
        _spark = _beam = _fallbackWeapon = null;
        _glowRings.Clear();
        _dashRings.Clear();
        _holeOverlays.Clear();
        _additive = null;
        _additiveResolved = false;
    }

    private static Texture2D NewTex(int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
    }

    private static Sprite Make(Texture2D tex)
    {
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                             new Vector2(0.5f, 0.5f), tex.width);
    }

    // ── Fast radial renderer (LUT based) ─────────────────────────────────────
    //
    //  PERFORMANCE NOTE — READ BEFORE CHANGING.
    //  The first version of this evaluated a delegate 3x3 times per pixel: at 512²
    //  that is 2.36 MILLION calls, each with a sqrt and an atan2, for EVERY ring
    //  texture. With ~14 distinct rings in the fight that stalled the main thread
    //  for seconds at a time — long enough that FMOD logged "waited 10.0 seconds
    //  for DSP graph to go idle" and the game visibly froze whenever a new effect
    //  appeared.
    //
    //  The shapes here are separable: a radial profile, optionally modulated by an
    //  angular mask. So we precompute two small 1D lookup tables (supersampled in
    //  1D, which is nearly free) and then fill the texture with one sqrt and a
    //  couple of interpolated array reads per pixel. Same visual result, ~100x
    //  faster. atan2 is only paid on pixels where the angular mask can matter.
    //
    //  Anti-aliasing still holds because the LUTs are sampled with linear
    //  interpolation and every profile feathers over at least ~2 pixels.

    /// Ring textures are scaled down to a couple of world units on screen, so 256
    /// is ample — a 0.008 hairline still lands on ~2 texels — and it is FOUR TIMES
    /// cheaper to bake than 512. This constant is the single biggest lever on spawn
    /// cost; do not raise it without re-measuring.
    private const int RingTexSize = 256;

    private const int RadialLutN = 2048;
    private const int AngularLutN = 2048;

    /// Build a 1D table over [0,1], supersampled so the profile itself is smooth.
    private static float[] BuildLut(int n, System.Func<float, float> f, int ss = 4)
    {
        var lut = new float[n];
        float inv = 1f / (n - 1);
        float step = inv / ss;
        for (int i = 0; i < n; i++)
        {
            float baseT = i * inv;
            float acc = 0f;
            for (int k = 0; k < ss; k++)
                acc += f(baseT + (k - (ss - 1) * 0.5f) * step);
            lut[i] = acc / ss;
        }
        return lut;
    }

    /// Build a 1D table over [0,360) degrees.
    private static float[] BuildAngularLut(int n, System.Func<float, float> f, int ss = 4)
    {
        var lut = new float[n];
        float inv = 360f / n;
        float step = inv / ss;
        for (int i = 0; i < n; i++)
        {
            float baseA = i * inv;
            float acc = 0f;
            for (int k = 0; k < ss; k++)
                acc += f(Mathf.Repeat(baseA + (k - (ss - 1) * 0.5f) * step, 360f));
            lut[i] = acc / ss;
        }
        return lut;
    }

    private static float SampleLut(float[] lut, float t01)
    {
        if (t01 <= 0f) return lut[0];
        if (t01 >= 1f) return lut[lut.Length - 1];
        float f = t01 * (lut.Length - 1);
        int i = (int)f;
        float frac = f - i;
        int j = Mathf.Min(i + 1, lut.Length - 1);
        return lut[i] + (lut[j] - lut[i]) * frac;
    }

    /// alpha(pixel) = radial[d]  +  modulated[d] * angular[angle]
    ///
    /// `modulated` and `angular` may be null for a purely radial shape, in which
    /// case no atan2 is evaluated at all.
    private static Sprite RenderComposite(int size, float[] radial,
                                          float[] modulated, float[] angular)
    {
        var tex = NewTex(size);
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;
        float invMaxR = 1f / c;
        bool hasAngular = modulated != null && angular != null;
        var clear = new Color(1f, 1f, 1f, 0f);

        for (int y = 0; y < size; y++)
        {
            float dy = y - c;
            float dy2 = dy * dy;
            int row = y * size;

            for (int x = 0; x < size; x++)
            {
                float dx = x - c;
                float d = Mathf.Sqrt(dx * dx + dy2) * invMaxR;

                if (d > 1f) { px[row + x] = clear; continue; }

                float a = radial != null ? SampleLut(radial, d) : 0f;

                if (hasAngular)
                {
                    float m = SampleLut(modulated, d);
                    // Skip the expensive atan2 wherever the modulated band is zero,
                    // which is most of the texture for ticks and dashes.
                    if (m > 0.001f)
                    {
                        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        if (ang < 0f) ang += 360f;
                        a += m * SampleLut(angular, ang / 360f);
                    }
                }

                px[row + x] = a <= 0f ? clear
                                      : new Color(1f, 1f, 1f, a < 1f ? a : 1f);
            }
        }

        tex.SetPixels(px);
        return Make(tex);
    }

    /// Cheap deterministic value noise, for surface grain. Deterministic so the
    /// texture is identical every run and cannot shimmer between sessions.
    private static float Grain(float x, float y, float scale)
    {
        return Mathf.PerlinNoise(x * scale + 13.7f, y * scale + 91.3f);
    }

    // ── Prewarm ──────────────────────────────────────────────────────────────

    /// Build every texture this boss needs, ONE PER FRAME.
    ///
    /// Baking them all in a single call is what froze the game for ~2s at spawn: the
    /// individual bakes are only a few ms each, but a dozen back-to-back inside one
    /// Update blows the frame budget spectacularly. Yielding between them spreads the
    /// work over about fifteen frames, which is invisible while the boss walks in.
    ///
    /// Building them LAZILY is not an option either — that just moves the stall to
    /// the first Kaboom, which is the worst possible moment.
    ///
    /// Every Get* below is idempotent and cached, so a second boss costs nothing.
    public static IEnumerator PrewarmRoutine()
    {
        GetSoftDisc(); yield return null;
        GetRadialGlow(); yield return null;
        GetEdgeGradientDisc(); yield return null;
        GetSpark(); GetQuad(); yield return null;
        GetBeam(); GetSlash(0.030f); yield return null;
        GetFallbackWeaponIcon(); yield return null;

        // Ground circle + safe zone. Quantisation in GetGlowRing collapses the
        // near-duplicate requests onto a handful of shared textures.
        GetGlowRing(RimCore, RimGlow); yield return null;
        GetGlowRing(RimCoreFine, RimGlowFine); yield return null;
        GetTickRing(24, 6); yield return null;
        GetTickRing(32, 8); yield return null;
        GetDashedRing(20, 0.010f); yield return null;
        GetAngularSweep(140f); yield return null;

        // Kaboom / resonance / impact / success burst.
        GetGlowRing(0.008f, 0.075f); yield return null;
        GetGlowRing(0.012f, 0.10f); yield return null;
    }

    /// Shared rim geometry, so every ring in the fight is visibly the same family
    /// and the prewarm list stays in step with what the effects actually request.
    public const float RimCore = 0.008f;      // primary hairline half-width
    public const float RimGlow = 0.075f;
    public const float RimCoreFine = 0.004f;  // inner accent hairline
    public const float RimGlowFine = 0.025f;

    // ── Additive material ────────────────────────────────────────────────────

    /// Opt-in additive blending for halos.
    ///
    /// OFF BY DEFAULT, deliberately. Additive blending makes a halo read as light,
    /// but Unity's legacy particle shader multiplies the vertex colour by its own
    /// `_TintColor`, which defaults to 50% grey. A ring authored at alpha 0.95
    /// therefore reaches the screen at roughly a quarter strength and disappears
    /// against a bright biome — which is exactly what made the Mechanic 1 circle
    /// invisible. The alpha-blended path is predictable and the thin-core-plus-bloom
    /// textures already carry the glow.
    public static bool useAdditiveGlow = false;

    /// An additive particle material if the project ships one, else null.
    public static Material AdditiveMaterial
    {
        get
        {
            if (_additiveResolved) return _additive;
            _additiveResolved = true;

            string[] candidates =
            {
                "Legacy Shaders/Particles/Additive",
                "Particles/Additive",
                "Mobile/Particles/Additive",
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var sh = Shader.Find(candidates[i]);
                if (sh == null) continue;
                _additive = new Material(sh);
                // Neutralise the 50% grey tint so the sprite's own colour survives.
                if (_additive.HasProperty("_TintColor"))
                    _additive.SetColor("_TintColor", Color.white);
                return _additive;
            }
            return null;   // alpha blending it is
        }
    }

    /// Switch a renderer to additive IF that has been opted into and is available.
    /// Safe no-op otherwise — the renderer keeps the default sprite material.
    public static void MakeGlowy(SpriteRenderer sr)
    {
        if (sr == null || !useAdditiveGlow) return;
        var mat = AdditiveMaterial;
        if (mat != null) sr.sharedMaterial = mat;
    }

    // ── Night mode ───────────────────────────────────────────────────────────

    /// Attach a NightLight so this effect illuminates the darkness in night mode.
    /// NightLight registers itself only when NightOverlay.Instance exists, so by day
    /// this is a dormant component whose Update returns immediately.
    public static NightLight AddNightLight(GameObject go, Color color, float radius,
                                           float intensity = 0.9f, float warmTint = 0.35f)
    {
        if (go == null) return null;
        var nl = go.AddComponent<NightLight>();
        nl.radius = radius;
        nl.intensity = intensity;
        nl.lightColor = color;
        nl.warmTintStrength = warmTint;
        return nl;
    }

    // ── Discs 

    /// Filled disc with a soft edge. Contrast underlays and flashes.
    public static Sprite GetSoftDisc()
    {
        if (_softDisc != null) return _softDisc;
        const int S = 128;
        var tex = NewTex(S);
        float c = (S - 1) * 0.5f, r = c - 1f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((r - d) / 2f));
            }
        tex.SetPixels(px);
        return _softDisc = Make(tex);
    }

    /// Soft radial falloff — bright centre fading smoothly to nothing. This is the
    /// halo primitive; the squared falloff matches NightGlow's, so a Boss5 glow and
    /// a night glow on the same object read as one light rather than two.
    public static Sprite GetRadialGlow()
    {
        if (_radialGlow != null) return _radialGlow;
        const int S = 128;
        var tex = NewTex(S);
        float c = (S - 1) * 0.5f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f));
            }
        tex.SetPixels(px);
        return _radialGlow = Make(tex);
    }

    /// Fill disc: clear in the middle, gathering softly toward the rim.
    ///
    /// The concentric contour bands that used to be here read as cheap — regular
    /// rings inside a ring is exactly the "drawn in Paint" signature. What replaces
    /// them is a smooth radial ramp plus two layers of grain at different scales:
    /// coarse blotches to break the gradient into something organic, fine speckle to
    /// keep it from banding on 8-bit alpha. No repeating structure at all.
    public static Sprite GetEdgeGradientDisc()
    {
        if (_edgeDisc != null) return _edgeDisc;

        const int S = 256;
        var tex = NewTex(S);
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f, maxR = c - 1f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                if (d > 1f) { px[y * S + x] = Color.clear; continue; }

                float edge = Mathf.Clamp01((1f - d) / 0.06f);      // soft cut at the rim
                float ramp = Mathf.Pow(Mathf.Clamp01(d), 2.6f);    // build toward it

                // Coarse organic blotching, then fine speckle. Centred on 1.0 so the
                // grain modulates the ramp rather than darkening it overall.
                float coarse = 0.80f + Grain(x, y, 0.020f) * 0.40f;
                float fine = 0.94f + Grain(x, y, 0.130f) * 0.12f;

                px[y * S + x] = new Color(1f, 1f, 1f,
                    Mathf.Clamp01(edge * (0.06f + 0.94f * ramp) * coarse * fine));
            }

        tex.SetPixels(px);
        return _edgeDisc = Make(tex);
    }

    // ── Rings ────────────────────────────────────────────────────────────────

    /// A refined ring rim.
    ///
    /// The "thick frame a kid would draw" look came from one fat uniform stroke. A
    /// rim that reads as designed has STRUCTURE, so this builds four elements:
    ///
    ///   · a hairline primary stroke (thin — most of the perceived quality is here)
    ///   · a fainter parallel accent line just inside it, the technical double-rule
    ///     that makes an instrument bezel look machined
    ///   · an ASYMMETRIC bloom, weighted outward, because light spilling outward
    ///     reads as emission while a symmetric blur reads as a blurry marker pen
    ///   · a whisper of inward falloff so the disc and the rim are joined rather
    ///     than the rim floating on top of the fill
    ///
    /// `coreFraction` is the primary line's half-width as a fraction of the radius.
    /// Keep it SMALL — 0.006-0.012 is the readable range; anything above ~0.02
    /// starts to look like a frame again.
    public static Sprite GetGlowRing(float coreFraction, float glowFraction)
    {
        // Quantise the request. Callers ask for a dozen slightly different rings
        // (0.008 vs 0.009 vs 0.010) that are visually indistinguishable once scaled
        // down to a couple of world units — but each unique pair used to mean another
        // full texture bake, and 14 bakes in one frame is what froze the spawn.
        // Snapping to a coarse grid collapses them onto ~3 shared textures.
        coreFraction = Mathf.Round(coreFraction / 0.004f) * 0.004f;
        glowFraction = Mathf.Round(glowFraction / 0.025f) * 0.025f;

        int key = Mathf.RoundToInt(coreFraction * 10000f) * 100000
                + Mathf.RoundToInt(glowFraction * 10000f);
        if (_glowRings.TryGetValue(key, out var cached) && cached != null) return cached;

        float glow = Mathf.Max(0.004f, glowFraction);
        float core = Mathf.Max(0.0015f, coreFraction);
        float ringR = 1f - glow;                    // seat inward, leave room for bloom
        float accentR = ringR - core * 4.5f;        // parallel rule just inside

        var radial = BuildLut(RadialLutN, d =>
        {
            float off = d - ringR;                  // signed: + is outward
            float aoff = Mathf.Abs(off);

            // Primary hairline. Feathered over ~1.5px at 512.
            float line = Mathf.Clamp01((core - aoff) / 0.0035f);

            // Inner parallel accent, deliberately much fainter.
            float accent = Mathf.Clamp01((core * 0.55f - Mathf.Abs(d - accentR)) / 0.0035f) * 0.30f;

            // Asymmetric bloom: 100% outward, 45% inward.
            float bloom;
            if (off >= 0f) bloom = Mathf.Pow(Mathf.Clamp01(1f - off / glow), 3.2f) * 0.40f;
            else bloom = Mathf.Pow(Mathf.Clamp01(1f + off / (glow * 0.75f)), 3.2f) * 0.18f;

            return line + accent + bloom;
        });

        return _glowRings[key] = RenderComposite(RingTexSize, radial, null, null);
    }

    /// A measurement bezel: a hairline circle with short radial ticks pointing
    /// inward. Ticks share one length (major ones are WIDER and brighter rather than
    /// longer), which keeps the shape separable and therefore cheap to rasterise.
    public static Sprite GetTickRing(int minorTicks, int majorEvery)
    {
        int key = 900000 + minorTicks * 100 + majorEvery;
        if (_dashRings.TryGetValue(key, out var cached) && cached != null) return cached;

        minorTicks = Mathf.Clamp(minorTicks, 4, 180);
        majorEvery = Mathf.Max(1, majorEvery);
        float segment = 360f / minorTicks;

        const float RING_R = 0.965f;
        const float TICK_LEN = 0.055f;
        float inner = RING_R - TICK_LEN;

        // Base: the hairline bezel circle itself.
        var radial = BuildLut(RadialLutN, d =>
            Mathf.Clamp01((0.0035f - Mathf.Abs(d - RING_R)) / 0.003f) * 0.5f);

        // Modulated band: where ticks are allowed to live, tapering inward so they
        // fade to a point instead of ending on a blunt edge.
        var modulated = BuildLut(RadialLutN, d =>
        {
            if (d > RING_R || d < inner) return 0f;
            float t = (d - inner) / TICK_LEN;        // 0 inner, 1 at the bezel
            return Mathf.Clamp01((RING_R - d) / 0.004f) * Mathf.Pow(t, 0.6f);
        });

        // Angular mask: tick presence, wider and brighter on the majors.
        var angular = BuildAngularLut(AngularLutN, ang =>
        {
            int index = Mathf.RoundToInt(ang / segment) % minorTicks;
            if (index < 0) index += minorTicks;
            float delta = Mathf.Abs(Mathf.DeltaAngle(ang, index * segment));
            bool major = (index % majorEvery) == 0;
            float halfWidth = major ? 0.75f : 0.38f;          // degrees
            float w = Mathf.Clamp01((halfWidth - delta) / 0.30f);
            return w * (major ? 0.95f : 0.5f);
        });

        return _dashRings[key] = RenderComposite(RingTexSize, radial, modulated, angular);
    }

    /// A ring of separated dashes. Rotating one over a solid ring gives the mark
    /// motion and urgency without any extra draw complexity.
    public static Sprite GetDashedRing(int dashCount, float coreFraction)
    {
        int key = dashCount * 10000 + Mathf.RoundToInt(coreFraction * 1000f);
        if (_dashRings.TryGetValue(key, out var cached) && cached != null) return cached;

        dashCount = Mathf.Max(1, dashCount);
        float segment = 360f / dashCount;
        float dashHalf = segment * 0.28f;              // ~56% duty cycle
        float core = Mathf.Max(0.003f, coreFraction);
        float glow = core * 2.6f;
        float ringR = 1f - glow;

        var modulated = BuildLut(RadialLutN, d =>
        {
            float off = Mathf.Abs(d - ringR);
            if (off > glow) return 0f;
            float line = Mathf.Clamp01((core - off) / 0.0035f);
            float bloom = Mathf.Pow(Mathf.Clamp01(1f - off / glow), 3f) * 0.40f;
            return line + bloom;
        });

        var angular = BuildAngularLut(AngularLutN, ang =>
        {
            float within = Mathf.Abs(Mathf.Repeat(ang, segment) - segment * 0.5f);
            // Feather the dash ends so they don't alias into hard blocks.
            return Mathf.Clamp01((dashHalf - within) / (segment * 0.12f));
        });

        return _dashRings[key] = RenderComposite(RingTexSize, null, modulated, angular);
    }

    /// An angular comet sweep — bright at one bearing, fading around the circle.
    /// Rotated over a ring it reads as a radar scan, which is what sells the mark as
    /// actively measuring the player rather than a static decal.
    public static Sprite GetAngularSweep(float tailDegrees = 150f)
    {
        int key = 800000 + Mathf.RoundToInt(tailDegrees);
        if (_glowRings.TryGetValue(key, out var cached) && cached != null) return cached;

        float tail = Mathf.Clamp(tailDegrees, 20f, 350f);

        // Concentrate the sweep toward the rim so it grazes the ring.
        var modulated = BuildLut(RadialLutN, d =>
            Mathf.Pow(Mathf.Clamp01(d), 3.5f) * Mathf.Clamp01((1f - d) / 0.05f));

        var angular = BuildAngularLut(AngularLutN, ang =>
            ang > tail ? 0f : Mathf.Pow(1f - ang / tail, 1.8f));

        return _glowRings[key] = RenderComposite(RingTexSize, null, modulated, angular);
    }

    /// One annular wedge, centred on 0 degrees (pointing +X), spanning `spanDegrees`.
    ///
    /// N of these rotated by 360/N tile into a SEAMLESS ring — which is how the
    /// countdown gets a continuous arc that depletes, instead of the row of
    /// disconnected dots it had before. The angular edges are feathered by only a
    /// fraction of a degree so neighbours blend without overlapping into a bright seam.
    public static Sprite GetArcSegment(float spanDegrees, float coreFraction)
    {
        int key = 700000 + Mathf.RoundToInt(spanDegrees * 10f) * 100
                + Mathf.RoundToInt(coreFraction * 1000f);
        if (_dashRings.TryGetValue(key, out var cached) && cached != null) return cached;

        float span = Mathf.Clamp(spanDegrees, 1f, 90f);
        float half = span * 0.5f;
        float core = Mathf.Max(0.006f, coreFraction);
        float glow = core * 2.2f;
        float ringR = 1f - glow;

        var modulated = BuildLut(RadialLutN, d =>
        {
            float off = Mathf.Abs(d - ringR);
            if (off > glow) return 0f;
            float band = Mathf.Clamp01((core - off) / 0.004f);
            float bloom = Mathf.Pow(Mathf.Clamp01(1f - off / glow), 2.6f) * 0.45f;
            return band + bloom;
        });

        // SEAM FIX. The feather must extend OUTWARD past the wedge's nominal half
        // width, not inward from it. Feathering inward left a band near every
        // boundary where neither wedge was fully opaque, so the ring rendered as a
        // row of dimmed gaps — the "dotted line" look. Running alpha at 1 all the way
        // to `half` and only then fading means adjacent solid regions butt exactly
        // and each feather overlaps into its neighbour's solid area.
        float feather = Mathf.Min(0.6f, span * 0.18f);
        var angular = BuildAngularLut(AngularLutN, ang =>
            Mathf.Clamp01((half + feather - Mathf.Abs(Mathf.DeltaAngle(ang, 0f))) / feather));

        return _dashRings[key] = RenderComposite(RingTexSize, null, modulated, angular);
    }

    /// Large opaque field with a TRANSPARENT circular hole, feathered at the hole
    /// edge. Scaled to the cover radius and centred on the safe zone, this floods
    /// the map with danger colour while leaving the zone clear — no shader needed.
    public static Sprite GetHoleOverlay(float holeFraction)
    {
        int key = Mathf.RoundToInt(Mathf.Clamp01(holeFraction) * 1000f);
        if (_holeOverlays.TryGetValue(key, out var cached) && cached != null) return cached;

        const int S = 512;
        var tex = NewTex(S);
        float c = (S - 1) * 0.5f, outerR = c;
        float holeR = outerR * Mathf.Clamp01(holeFraction);
        // Feather proportional to the hole, so a small hole doesn't get a fat blurry rim.
        float feather = Mathf.Max(2f, holeR * 0.06f);

        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((d - holeR) / feather));
            }
        tex.SetPixels(px);
        return _holeOverlays[key] = Make(tex);
    }

    // ── Small pieces ─────────────────────────────────────────────────────────

    /// Plain white quad. Meter fills, flashes.
    public static Sprite GetQuad()
    {
        if (_fillQuad != null) return _fillQuad;
        var tex = NewTex(4);
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        return _fillQuad = Make(tex);
    }

    /// A soft elongated blob — debris, embers, impact sparks. Squashed on Y so it
    /// streaks along travel once the emitter rotates it.
    public static Sprite GetSpark()
    {
        if (_spark != null) return _spark;
        const int S = 32;
        var tex = NewTex(S);
        float c = (S - 1) * 0.5f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / (c * 0.42f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f));
            }
        tex.SetPixels(px);
        return _spark = Make(tex);
    }

    /// A light shaft for the safe-zone beacon: a narrow cone that widens as it
    /// rises, brightest low-but-not-at-the-floor, fading out toward the top.
    ///
    /// TWO BUGS THIS FIXES, both of which made the beacon vanish entirely.
    /// The sprite is 4 world units tall at scale 1, so multiplying by zoneRadius*5
    /// produced a 52-unit column — and since the bottom fifth was authored
    /// transparent, that transparent section alone was taller than the camera's
    /// whole view. The beacon was real, just entirely off-screen. The fade-in is now
    /// a shallow 10% of the sprite, and the caller scales it to a few units, not
    /// fifty. See Boss5SafeZoneVisual.Build.
    public static Sprite GetBeam()
    {
        if (_beam != null) return _beam;
        const int W = 96, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        float cx = (W - 1) * 0.5f;

        for (int y = 0; y < H; y++)
        {
            float up = y / (float)(H - 1);                 // 0 at base, 1 at top

            // Clear right at the floor so it never washes over a standing player,
            // but only for a shallow band — 10%, not 22%.
            float footFade = Mathf.Clamp01(up / 0.10f);
            // Brightest in the lower third, thinning steadily toward the top.
            float body = Mathf.Pow(Mathf.Clamp01(1f - (up - 0.18f) / 0.82f), 1.5f);
            float vertical = footFade * body;

            // Cone: narrow at the base, ~2.4x wider at the top, like a shaft of
            // light rather than a rectangular bar.
            float halfWidth = cx * (0.34f + 0.66f * up);

            for (int x = 0; x < W; x++)
            {
                float across = 1f - Mathf.Abs(x - cx) / halfWidth;
                if (across <= 0f) { px[y * W + x] = Color.clear; continue; }
                // Soft-edged shaft, plus a slightly hotter core down the middle.
                float edge = Mathf.Pow(across, 1.9f);
                float core = Mathf.Pow(across, 7f) * 0.45f;
                px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((edge + core) * vertical));
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        // Pivot at the BOTTOM centre so the shaft stands on its ground point.
        return _beam = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), H);
    }

    /// A rounded diagonal bar — the "crossed out" stroke over the weapon icon.
    ///
    /// Drawn on the ANTI-DIAGONAL (top-left to bottom-right). The project's sword
    /// art runs bottom-left to top-right, so a stroke on the main diagonal lay
    /// directly along the blade and read as part of the sword rather than as a
    /// prohibition. Crossing the other way is what makes the "no" register.
    ///
    /// `thicknessFraction` is the bar's half-width as a fraction of the sprite.
    public static Sprite GetSlash(float thicknessFraction = 0.030f)
    {
        int key = 600000 + Mathf.RoundToInt(thicknessFraction * 10000f);
        if (_dashRings.TryGetValue(key, out var cached) && cached != null) return cached;

        const int S = 256;
        const float MAX = S - 1;
        float half = Mathf.Max(1f, S * Mathf.Clamp(thicknessFraction, 0.004f, 0.2f));
        var tex = NewTex(S);
        var px = new Color[S * S];
        float lo = S * 0.16f, hi = S * 0.84f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // Perpendicular distance to the line x + y = S-1.
                float d = Mathf.Abs(x + y - MAX) / Mathf.Sqrt(2f);

                // Position ALONG that line, used to round the two ends off.
                float t = (x - y + MAX) * 0.5f;
                float cap = Mathf.Clamp01((t - lo) / 5f) * Mathf.Clamp01((hi - t) / 5f);

                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((half - d) / 1.6f) * cap);
            }
        tex.SetPixels(px);
        return _dashRings[key] = Make(tex);
    }

    /// Fallback weapon glyph.

    public const string FallbackIconResourcePath = "Sprites/Augments/2";

    public static Sprite GetFallbackWeaponIcon()
    {
        if (_fallbackWeapon != null) return _fallbackWeapon;

        _fallbackWeapon = Resources.Load<Sprite>(FallbackIconResourcePath);
        if (_fallbackWeapon != null) return _fallbackWeapon;

        Debug.LogWarning($"[Boss5] Could not load '{FallbackIconResourcePath}' from " +
                         "Resources; using the built-in glyph instead.");

        const int S = 64;
        var tex = NewTex(S);
        var px = new Color[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, 0f);

        void FillRect(int x0, int y0, int x1, int y1)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(S - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(S - 1, x1); x++)
                    px[y * S + x] = Color.white;
        }

        FillRect(29, 18, 34, 56);   // blade
        FillRect(22, 14, 41, 18);   // crossguard
        FillRect(30, 6, 33, 14);    // grip
        FillRect(27, 2, 36, 6);     // pommel

        tex.SetPixels(px);
        return _fallbackWeapon = Make(tex);
    }
}


// =============================================================================
//  SHARED BUILDER HELPERS
// =============================================================================
public static class Boss5Fx
{
    public const string SortLayer = "Default";

    public static SpriteRenderer Child(Transform parent, string name, Sprite sprite,
                                       int order, bool glowy = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = order;
        if (glowy) Boss5Sprites.MakeGlowy(sr);
        return sr;
    }

    public static void SetAlpha(SpriteRenderer sr, float a)
    {
        if (sr == null) return;
        var c = sr.color;
        c.a = a;
        sr.color = c;
    }

    public static void Tint(SpriteRenderer sr, Color rgb, float a)
    {
        if (sr == null) return;
        sr.color = new Color(rgb.r, rgb.g, rgb.b, a);
    }

    public static void Scale(SpriteRenderer sr, float worldDiameter)
    {
        if (sr != null) sr.transform.localScale = Vector3.one * worldDiameter;
    }
}


// =============================================================================
//  WEAPON ICON PROVIDER
// -----------------------------------------------------------------------------
//  Mechanic 2 needs "the sprite of the weapon this player currently has equipped".
//
//  It works out of the box: the DEFAULT resolver finds the player's `Weapon`
//  component (PlayerAttack holds one in a private field, but the component itself
//  sits on the player or a child, so GetComponentInChildren reaches it without any
//  edit to PlayerAttack), asks it for its WeaponData, and pulls the icon sprite off
//  that asset. That last step is resolved ONCE by reflection and cached.
//
//  TO REPLACE IT with a direct, reflection-free line, set Resolver at startup:
//
//      Boss5WeaponIconProvider.Resolver = player =>
//          player.GetComponentInChildren<Weapon>()?.GetWeaponData()?.icon;
// =============================================================================
public static class Boss5WeaponIconProvider
{
    public static System.Func<PlayerStats, Sprite> Resolver;

    /// When true, the icon tracks the player's EQUIPPED weapon (the original spec:
    /// "updates dynamically if the player switches weapons"). When false — the
    /// default — it always shows the project's own weapon icon from Resources.
    ///
    /// Defaulted OFF because the equipped-weapon lookup was silently winning over
    /// the configured icon: the project sprite was only ever a fallback, so it never
    /// appeared. Flip this to true if you want the dynamic behaviour back.
    public static bool useEquippedWeaponIcon = false;

    private static bool _iconMemberResolved;
    private static System.Reflection.FieldInfo _iconField;
    private static System.Reflection.PropertyInfo _iconProperty;

    private static readonly string[] IconMemberNames =
        { "icon", "weaponIcon", "sprite", "weaponSprite", "image", "uiIcon", "iconSprite" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Resolver = null;
        useEquippedWeaponIcon = false;
        _iconMemberResolved = false;
        _iconField = null;
        _iconProperty = null;
    }

    public static Sprite Resolve(PlayerStats player)
    {
        if (player == null) return Boss5Sprites.GetFallbackWeaponIcon();

        try
        {
            Sprite s = (Resolver != null) ? Resolver(player) : DefaultResolve(player);
            if (s != null) return s;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Boss5] Weapon icon resolver threw: {e.Message}");
            Resolver = null;
            _iconMemberResolved = true;
            _iconField = null;
            _iconProperty = null;
        }

        return Boss5Sprites.GetFallbackWeaponIcon();
    }

    private static Sprite DefaultResolve(PlayerStats player)
    {
        // Configured project icon wins unless dynamic tracking is explicitly on.
        if (!useEquippedWeaponIcon) return Boss5Sprites.GetFallbackWeaponIcon();

        var weapon = player.GetComponentInChildren<Weapon>();
        if (weapon == null) return null;

        WeaponData data = weapon.GetWeaponData();
        if (data == null) return null;

        if (!_iconMemberResolved)
        {
            _iconMemberResolved = true;
            var type = data.GetType();

            foreach (var name in IconMemberNames)
            {
                var f = type.GetField(name);
                if (f != null && f.FieldType == typeof(Sprite)) { _iconField = f; return f.GetValue(data) as Sprite; }

                var p = type.GetProperty(name);
                if (p != null && p.PropertyType == typeof(Sprite) && p.CanRead)
                { _iconProperty = p; return p.GetValue(data) as Sprite; }
            }

            foreach (var f in type.GetFields())
                if (f.FieldType == typeof(Sprite)) { _iconField = f; return f.GetValue(data) as Sprite; }

            return null;
        }

        if (_iconField != null) return _iconField.GetValue(data) as Sprite;
        if (_iconProperty != null) return _iconProperty.GetValue(data) as Sprite;
        return null;
    }
}


//  ORBITING MOTES

public class Boss5Motes : MonoBehaviour
{
    private struct Mote
    {
        public SpriteRenderer Dot;
        public SpriteRenderer Tail;
        public float Phase;        // orbital offset
        public float Speed;        // per-mote speed multiplier
        public float Bob;          // radial drift frequency
        public float Twinkle;      // brightness frequency
        public float Size;
    }

    private Mote[] _motes;
    private float _radius;
    private Color _color = Color.white;
    private float _energy;         // 0..1, drives speed and brightness
    private float _t;

    public static Boss5Motes Create(Transform parent, float radius, Color color,
                                    int count, int sortingOrder)
    {
        var go = new GameObject("Motes");
        go.transform.SetParent(parent, false);
        var m = go.AddComponent<Boss5Motes>();
        m._radius = Mathf.Max(0.1f, radius);
        m._color = color;
        m.Build(Mathf.Clamp(count, 1, 24), sortingOrder);
        return m;
    }

    private void Build(int count, int order)
    {
        _motes = new Mote[count];
        for (int i = 0; i < count; i++)
        {
            // Even base spacing, jittered — perfectly even spacing would just be the
            // concentric-circle problem again in a different form.
            float basePhase = (360f / count) * i + Random.Range(-14f, 14f);

            var tail = Boss5Fx.Child(transform, $"MoteTail{i}", Boss5Sprites.GetSpark(), order);
            var dot = Boss5Fx.Child(transform, $"Mote{i}", Boss5Sprites.GetRadialGlow(), order + 1);

            _motes[i] = new Mote
            {
                Dot = dot,
                Tail = tail,
                Phase = basePhase,
                Speed = Random.Range(0.82f, 1.22f),
                Bob = Random.Range(0.7f, 1.5f),
                Twinkle = Random.Range(1.6f, 3.4f),
                Size = Random.Range(0.7f, 1.3f),
            };
        }
        SetColor(_color);
    }

    public void SetColor(Color c)
    {
        _color = c;
        if (_motes == null) return;
        var hot = Color.Lerp(c, Color.white, 0.55f);   // cores burn brighter than the rim
        for (int i = 0; i < _motes.Length; i++)
        {
            Boss5Fx.Tint(_motes[i].Dot, hot, 0.9f);
            Boss5Fx.Tint(_motes[i].Tail, c, 0.35f);
        }
    }

    /// 0..1 — faster, brighter, more agitated as danger rises.
    public void SetEnergy(float e) => _energy = Mathf.Clamp01(e);

    private void Update()
    {
        if (_motes == null) return;

        _t += Time.deltaTime;
        float orbit = Mathf.Lerp(22f, 150f, _energy);      // degrees per second
        float baseSize = _radius * 0.10f;

        for (int i = 0; i < _motes.Length; i++)
        {
            ref var m = ref _motes[i];

            float ang = m.Phase + _t * orbit * m.Speed;
            float rad = ang * Mathf.Deg2Rad;

            // Radial drift so they weave across the rim instead of tracing it exactly.
            float drift = Mathf.Sin(_t * m.Bob + m.Phase) * _radius * 0.055f;
            float r = _radius + drift;

            var pos = new Vector3(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r, 0f);

            float twinkle = 0.55f + 0.45f * Mathf.Sin(_t * m.Twinkle + m.Phase);
            float size = baseSize * m.Size * (0.75f + 0.45f * twinkle)
                                  * (1f + _energy * 0.35f);

            m.Dot.transform.localPosition = pos;
            m.Dot.transform.localScale = Vector3.one * size;
            Boss5Fx.SetAlpha(m.Dot, (0.45f + 0.55f * twinkle) * (0.55f + 0.45f * _energy));

            // Tail trails BEHIND along the orbit, stretched by current speed.
            float tailAng = ang - 9f - _energy * 12f;
            float tailRad = tailAng * Mathf.Deg2Rad;
            m.Tail.transform.localPosition =
                new Vector3(Mathf.Cos(tailRad) * r, Mathf.Sin(tailRad) * r, 0f);
            m.Tail.transform.localRotation = Quaternion.Euler(0f, 0f, tailAng + 90f);
            m.Tail.transform.localScale =
                new Vector3(size * (2.2f + _energy * 2.6f), size * 0.7f, 1f);
            Boss5Fx.SetAlpha(m.Tail, 0.18f * (0.4f + 0.6f * _energy) * twinkle);
        }
    }
}


// =============================================================================
//  GROUND CIRCLE  (Mechanic 1 — don't move)
// -----------------------------------------------------------------------------
//  Layers, back to front:
//     shadow  — soft dark disc, the biome contrast underlay
//     fill    — edge-gradient disc in the current meter colour
//     halo    — wide soft glow, sells it as light rather than paint
//     ring    — thin crisp stroke with its own bloom
//     dashes  — counter-rotating ticks; spin rate rises with the meter
//  plus a NightLight so the mark lights the ground in night mode.
// =============================================================================
public class Boss5GroundCircle : MonoBehaviour
{
    private const int ShadowOrder = 3005;
    private const int FillOrder = 3006;
    private const int HaloOrder = 3007;
    private const int SweepOrder = 3008;
    private const int TicksOrder = 3009;
    private const int RingOrder = 3010;
    private const int CoreOrder = 3011;
    private const int MoteOrder = 3012;

    private SpriteRenderer _shadow, _fill, _halo, _sweep, _ticks, _ring, _core;
    private Boss5Motes _motes;
    private NightLight _nightLight;
    private Transform _follow;
    private float _radius;
    private float _meter;
    private Color _color = Color.green;
    private float _sweepSpin;
    private float _breath;

    public static Boss5GroundCircle Create(Transform follow, float radius, Color color)
    {
        var go = new GameObject("Boss5_MotionCircle");
        var c = go.AddComponent<Boss5GroundCircle>();
        c._follow = follow;
        c._radius = Mathf.Max(0.2f, radius);
        c._breath = Random.Range(0f, 10f);   // desync co-op players' pulses
        c.Build();
        c.SetColor(color);
        return c;
    }

    private void Build()
    {
        float d = _radius * 2f;

        // Dark underlay: the layer that makes the mark readable on Snow.
        _shadow = Boss5Fx.Child(transform, "Shadow", Boss5Sprites.GetSoftDisc(), ShadowOrder);
        Boss5Fx.Scale(_shadow, d * 1.10f);
        _shadow.color = new Color(0f, 0f, 0f, 0.30f);

        // Grainy fill, no repeating structure.
        _fill = Boss5Fx.Child(transform, "Fill", Boss5Sprites.GetEdgeGradientDisc(), FillOrder);
        Boss5Fx.Scale(_fill, d);

        _halo = Boss5Fx.Child(transform, "Halo", Boss5Sprites.GetRadialGlow(), HaloOrder);
        Boss5Fx.Scale(_halo, d * 1.55f);

        // Slow radar sweep grazing the rim.
        _sweep = Boss5Fx.Child(transform, "Sweep", Boss5Sprites.GetAngularSweep(140f), SweepOrder);
        Boss5Fx.Scale(_sweep, d * 0.99f);

        _ticks = Boss5Fx.Child(transform, "Ticks", Boss5Sprites.GetTickRing(24, 6), TicksOrder);
        Boss5Fx.Scale(_ticks, d);

        // THIN rim. Both strokes are hairlines — the perceived quality of the whole
        // mark lives here, and anything heavier immediately reads as a drawn frame.
        _ring = Boss5Fx.Child(transform, "Ring",
            Boss5Sprites.GetGlowRing(Boss5Sprites.RimCore, Boss5Sprites.RimGlow), RingOrder);
        Boss5Fx.Scale(_ring, d);

        _core = Boss5Fx.Child(transform, "Core",
            Boss5Sprites.GetGlowRing(Boss5Sprites.RimCoreFine, Boss5Sprites.RimGlowFine), CoreOrder);
        Boss5Fx.Scale(_core, d);

        // Orbiting lights instead of a second concentric ring of dashes.
        _motes = Boss5Motes.Create(transform, _radius, _color, 7, MoteOrder);

        _nightLight = Boss5Sprites.AddNightLight(gameObject, _color, _radius * 2.2f, 0.75f, 0.3f);
    }

    public void SetColor(Color c)
    {
        _color = c;
        Boss5Fx.Tint(_fill, c, 0.30f);
        Boss5Fx.Tint(_halo, c, 0.26f);
        Boss5Fx.Tint(_sweep, Color.Lerp(c, Color.white, 0.35f), 0.26f);
        Boss5Fx.Tint(_ticks, Color.Lerp(c, Color.white, 0.20f), 0.55f);
        Boss5Fx.Tint(_ring, c, 0.95f);
        // Push the hairline toward white so it stays legible even when the base
        // colour is a saturated red or green over similar ground.
        Boss5Fx.Tint(_core, Color.Lerp(c, Color.white, 0.65f), 0.9f);
        if (_motes != null) _motes.SetColor(c);
        if (_nightLight != null) _nightLight.lightColor = c;
    }

    /// 0..1 — drives pulse rate, glow, sweep and mote energy, so the danger reads
    /// even for a player who cannot separate the red from the green.
    public void SetPulse(float t) => _meter = Mathf.Clamp01(t);

    private void LateUpdate()
    {
        if (_follow == null) { Destroy(gameObject); return; }

        Vector3 p = _follow.position;
        p.z = 0f;
        transform.position = p;

        float dt = Time.deltaTime;
        float d = _radius * 2f;

        // Two-rate pulse: a slow breath always present, plus an alarm beat that takes
        // over as the meter fills. A single sine reads as mechanical blinking;
        // layering them gives something closer to a heartbeat under stress.
        _breath += dt;
        float slow = Mathf.Sin(_breath * 1.6f) * 0.5f + 0.5f;
        float rate = Mathf.Lerp(2.2f, 13f, _meter);
        float beat = Mathf.Sin(_breath * rate);
        float depth = Mathf.Lerp(0.014f, 0.10f, _meter);

        float ringD = d * (1f + beat * depth + slow * 0.010f);
        Boss5Fx.Scale(_ring, ringD);
        Boss5Fx.Scale(_core, ringD);
        Boss5Fx.Scale(_ticks, ringD * 0.998f);

        // Halo breathes out of phase so the two don't strobe into one flat flash.
        float haloBeat = Mathf.Sin(_breath * rate + Mathf.PI * 0.5f) * 0.5f + 0.5f;
        Boss5Fx.SetAlpha(_halo, Mathf.Lerp(0.18f, 0.50f, _meter) * (0.55f + 0.45f * haloBeat));
        Boss5Fx.Scale(_halo, d * (1.5f + 0.14f * haloBeat * _meter));

        Boss5Fx.SetAlpha(_fill, Mathf.Lerp(0.22f, 0.46f, _meter) * (0.9f + 0.1f * slow));
        Boss5Fx.SetAlpha(_shadow, Mathf.Lerp(0.26f, 0.38f, _meter));
        Boss5Fx.SetAlpha(_ticks, Mathf.Lerp(0.38f, 0.75f, _meter));

        _sweepSpin += Mathf.Lerp(70f, 380f, _meter) * dt;
        _sweep.transform.localRotation = Quaternion.Euler(0f, 0f, _sweepSpin);
        Boss5Fx.SetAlpha(_sweep, Mathf.Lerp(0.16f, 0.38f, _meter));

        _ticks.transform.localRotation = Quaternion.Euler(0f, 0f, _sweepSpin * 0.05f);

        if (_motes != null) _motes.SetEnergy(_meter);

        if (_nightLight != null)
            _nightLight.intensity = Mathf.Lerp(0.55f, 1.2f, _meter) * (0.85f + 0.15f * haloBeat);
    }
}


//  WEAPON ICON METER  (Mechanic 2 — don't attack)
// -----------------------------------------------------------------------------
//  A crossed-out weapon glyph floating over the boss, with the hit counter as a
//  SEPARATE BAR underneath it.
//
//  The meter used to be a coloured wash rising up the plate behind the glyph.
//  That put the two pieces of information in the same pixels: as the fill climbed
//  it ate the silhouette of the very icon that says WHAT is forbidden, and the
//  red-on-red left nothing readable at the moment it mattered most. Splitting
//  them means the sign always reads as a sign, and the count always reads as a
//  count. The bar is also notched once per allowed hit, so "two swings left" is
//  something you can see rather than estimate off a gradient.
//
//  The plate is a PALE lilac, not white and not a saturated purple. It cannot go
//  dark or deep: the weapon art is black on transparent and a SpriteRenderer tint
//  MULTIPLIES, so black can never be lifted — the glyph only exists against a
//  light backing. Pale lilac is as far toward purple as the plate can travel while
//  the silhouette stays legible; the saturated violet lives in the meter, the rim
//  and the halo instead, where nothing has to read through it.
// =============================================================================
public class Boss5WeaponIconMeter : MonoBehaviour
{
    private const int GlowOrder = 4099;
    private const int PlateOrder = 4100;
    private const int RingOrder = 4101;
    private const int IconOrder = 4102;
    private const int SlashOrder = 4103;
    private const int BarShadowOrder = 4104;
    private const int BarTrackOrder = 4105;
    private const int BarFillOrder = 4106;

    /// Above this many hits the cells would be thinner than the gaps between them,
    /// so the row collapses back to one continuous bar.
    private const int MaxDrawnSegments = 12;

    private SpriteRenderer _glow, _plate, _ring, _icon, _slash;
    private SpriteRenderer _barShadow;
    private SpriteRenderer[] _cellTrack;
    private SpriteRenderer[] _cellFill;
    private int _cellCount = 1;

    private Transform _follow;
    private Vector2 _offset;
    private float _size;
    private Color _fillColor;
    private Color _plateColor;
    private Color _slashColor = new Color(0.90f, 0.12f, 0.10f, 0.92f);
    private float _fillAmount;
    private int _segments = 1;
    private Sprite _sourceSprite;
    private float _punch;

    private static readonly Color DefaultPlate = new Color(0.87f, 0.79f, 0.98f, 0.92f);
    private static readonly Color DefaultSlash = new Color(0.90f, 0.12f, 0.10f, 0.92f);

    // Bar geometry, all derived from _size so the whole sign scales as one thing.
    private float BarWidth => _size * 1.50f;
    private float BarHeight => _size * 0.15f;
    private float BarY => -(_size * 1.42f * 0.5f + BarHeight * 0.5f + _size * 0.13f);

    public static Boss5WeaponIconMeter Create(Transform follow, Vector2 offset,
                                              float size, Color fillColor)
        => Create(follow, offset, size, fillColor, DefaultPlate, DefaultSlash);

    public static Boss5WeaponIconMeter Create(Transform follow, Vector2 offset,
                                              float size, Color fillColor, Color plateColor)
        => Create(follow, offset, size, fillColor, plateColor, DefaultSlash);

    public static Boss5WeaponIconMeter Create(Transform follow, Vector2 offset, float size,
                                              Color fillColor, Color plateColor, Color slashColor)
    {
        var go = new GameObject("Boss5_WeaponIconMeter");
        var m = go.AddComponent<Boss5WeaponIconMeter>();
        m._follow = follow;
        m._offset = offset;
        m._size = Mathf.Max(0.2f, size);
        m._fillColor = fillColor;
        // A fully transparent plate means the field was never set on a prefab saved
        // between versions. Rendering nothing would leave the black glyph invisible,
        // so fall back rather than honour it.
        m._plateColor = plateColor.a <= 0.01f ? DefaultPlate : plateColor;
        m._slashColor = slashColor.a <= 0.01f ? DefaultSlash : slashColor;
        m.Build();
        return m;
    }

    private void Build()
    {
        _glow = Boss5Fx.Child(transform, "Glow", Boss5Sprites.GetRadialGlow(), GlowOrder);
        Boss5Fx.Scale(_glow, _size * 2.2f);
        Boss5Fx.Tint(_glow, _fillColor, 0.16f);

        // Soft-edged translucent disc, not a solid chip — see the class note on why
        // it has to stay light, and why it is no longer white.
        _plate = Boss5Fx.Child(transform, "Plate", Boss5Sprites.GetSoftDisc(), PlateOrder);
        Boss5Fx.Scale(_plate, _size * 1.42f);
        _plate.color = _plateColor;

        // Hairline rim, not a frame.
        _ring = Boss5Fx.Child(transform, "Ring",
            Boss5Sprites.GetGlowRing(Boss5Sprites.RimCoreFine, Boss5Sprites.RimGlowFine), RingOrder);
        Boss5Fx.Scale(_ring, _size * 1.42f);
        Boss5Fx.Tint(_ring, _fillColor, 0.75f);

        _icon = Boss5Fx.Child(transform, "Icon", Boss5Sprites.GetFallbackWeaponIcon(), IconOrder);
        _icon.color = Color.white;          // show the art at its authored colour

        // Crossing rule. A touch heavier than the rim so the prohibition reads as
        // the dominant mark, but still a rule rather than a marker stroke. Kept red
        // against the lilac plate: it is the only element that says "forbidden"
        // rather than showing a quantity, so it should not share the meter's hue.
        _slash = Boss5Fx.Child(transform, "Slash", Boss5Sprites.GetSlash(0.030f), SlashOrder);
        _slash.color = _slashColor;
        Boss5Fx.Scale(_slash, _size * 1.12f);

        BuildBar();

        Boss5Sprites.AddNightLight(gameObject, _fillColor, _size * 2.0f, 0.55f, 0.25f);

        SetWeaponSprite(Boss5Sprites.GetFallbackWeaponIcon());
        ApplyFill();
    }

    /// The hit counter: a row of discrete CELLS sitting below the sign, one cell
    /// per allowed hit, never over the glyph.
    ///
    /// Cells rather than a continuous bar with notches, because the allowance is
    /// not constant. It shrinks with the boss's enrage — six hits at full health,
    /// two when the boss is badly hurt, one when it is nearly dead — and a smooth
    /// bar looks identical in all three cases until the first hit lands, by which
    /// point a one-hit window has already exploded. Six boxes and one box are
    /// different at a glance, BEFORE you swing.
    private void BuildBar()
    {
        _barShadow = Boss5Fx.Child(transform, "BarShadow", Boss5Sprites.GetQuad(), BarShadowOrder);
        RebuildBar();
    }

    /// Set how many hits this activation allows. Called by the challenge with its
    /// enrage-scaled budget, so the row always shows the CURRENT rule rather than
    /// whatever the last activation used.
    public void SetSegments(int count)
    {
        count = Mathf.Max(1, count);
        if (count == _segments && _cellTrack != null) return;
        _segments = count;
        RebuildBar();
    }

    private void RebuildBar()
    {
        DestroyCells();

        float w = BarWidth, h = BarHeight, edge = h * 0.30f;

        // Above the cap the cells would be thinner than the gaps between them, so
        // the row collapses back to a single continuous bar.
        _cellCount = (_segments > 1 && _segments <= MaxDrawnSegments) ? _segments : 1;
        float gap = _cellCount > 1 ? h * 0.42f : 0f;
        float cw = (w - gap * (_cellCount - 1)) / _cellCount;

        if (_barShadow != null)
        {
            _barShadow.transform.localScale = new Vector3(w + edge * 2f, h + edge * 2f, 1f);
            _barShadow.transform.localPosition = new Vector3(0f, BarY, 0f);
            _barShadow.color = ShadowColor();
        }

        // Empty cells: a heavily darkened plate colour, so the row belongs to the
        // sign rather than looking like a separate UI element that wandered in.
        var empty = new Color(_plateColor.r * 0.26f, _plateColor.g * 0.24f,
                              _plateColor.b * 0.22f, 0.92f);

        _cellTrack = new SpriteRenderer[_cellCount];
        _cellFill = new SpriteRenderer[_cellCount];

        for (int i = 0; i < _cellCount; i++)
        {
            float x = CellCenterX(i, cw, gap, w);

            var track = Boss5Fx.Child(transform, "BarCell", Boss5Sprites.GetQuad(), BarTrackOrder);
            track.transform.localScale = new Vector3(cw, h, 1f);
            track.transform.localPosition = new Vector3(x, BarY, 0f);
            track.color = empty;
            _cellTrack[i] = track;

            var fill = Boss5Fx.Child(transform, "BarCellFill", Boss5Sprites.GetQuad(), BarFillOrder);
            fill.transform.localScale = new Vector3(cw, h, 1f);
            fill.transform.localPosition = new Vector3(x, BarY, 0f);
            fill.color = _fillColor;
            fill.enabled = false;
            _cellFill[i] = fill;
        }

        ApplyFill();
    }

    private void DestroyCells()
    {
        DestroyAll(_cellTrack); _cellTrack = null;
        DestroyAll(_cellFill); _cellFill = null;
    }

    private static void DestroyAll(SpriteRenderer[] arr)
    {
        if (arr == null) return;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] != null) Destroy(arr[i].gameObject);
    }

    private static float CellCenterX(int i, float cellW, float gap, float totalW)
        => -totalW * 0.5f + cellW * 0.5f + i * (cellW + gap);

    /// Hair-trigger tell. At one or two allowed hits the outline takes the meter
    /// colour instead of plain black, and LateUpdate pulses it — the row is short
    /// enough by then that the cell count alone is easy to miss mid-fight.
    private bool HairTrigger => _segments <= 2;

    private Color ShadowColor()
    {
        if (!HairTrigger) return new Color(0f, 0f, 0f, 0.55f);
        return new Color(_fillColor.r, _fillColor.g, _fillColor.b, 0.8f);
    }

    /// Called every frame by the challenge, so it early-outs when the sprite has not
    /// actually changed.
    public void SetWeaponSprite(Sprite sprite)
    {
        if (sprite == null || sprite == _sourceSprite) return;
        _sourceSprite = sprite;
        if (_icon != null)
        {
            _icon.sprite = sprite;
            // Fit inside the plate with margin so the slash still reads at the edges.
            FitToSize(_icon.transform, sprite, _size * 0.78f);
        }
    }

    public void SetFill(float amount)
    {
        amount = Mathf.Clamp01(amount);
        if (Mathf.Approximately(amount, _fillAmount)) return;
        _fillAmount = amount;
        ApplyFill();
    }

    public void Punch() => _punch = 1f;

    /// Light up cells left to right.
    ///
    /// The fill fraction is hits / hitsToFail and the row has exactly hitsToFail
    /// cells, so each hit lands on a cell boundary and the partial case only arises
    /// in the collapsed single-bar mode. Handling both with the same arithmetic
    /// keeps them from drifting apart.
    ///
    /// The old implementation cut a sub-Sprite out of the disc texture on every hit
    /// and destroyed the previous one; scaled quads do the same job with no
    /// allocation at all, and they can sit somewhere other than on top of the glyph.
    private void ApplyFill()
    {
        if (_cellFill == null || _cellTrack == null) return;

        float w = BarWidth, h = BarHeight;
        float gap = _cellCount > 1 ? h * 0.42f : 0f;
        float cw = (w - gap * (_cellCount - 1)) / _cellCount;

        float exact = _fillAmount * _cellCount;

        for (int i = 0; i < _cellFill.Length; i++)
        {
            var fill = _cellFill[i];
            if (fill == null) continue;

            float f = Mathf.Clamp01(exact - i);
            if (f <= 0.001f) { fill.enabled = false; continue; }

            fill.enabled = true;
            float fw = cw * f;
            float left = CellCenterX(i, cw, gap, w) - cw * 0.5f;
            fill.transform.localScale = new Vector3(fw, h, 1f);
            fill.transform.localPosition = new Vector3(left + fw * 0.5f, BarY, 0f);

            // Deepens toward opaque as the row approaches full, so the last cell is
            // unmistakably the last one.
            Boss5Fx.Tint(fill, _fillColor, Mathf.Lerp(0.78f, 1f, _fillAmount));
        }
    }

    private static void FitToSize(Transform t, Sprite sprite, float size)
    {
        float longest = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        if (longest <= 0.0001f) { t.localScale = Vector3.one; return; }
        t.localScale = Vector3.one * (size / longest);
    }

    private void LateUpdate()
    {
        if (_follow == null) { Destroy(gameObject); return; }

        Vector3 p = _follow.position + (Vector3)_offset;
        p.z = 0f;
        p.y += Mathf.Sin(Time.time * 2.2f) * 0.06f;
        transform.position = p;

        float urgency = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(3f, 13f, _fillAmount));
        Boss5Fx.SetAlpha(_glow, Mathf.Lerp(0.12f, 0.45f, _fillAmount) * (0.55f + 0.45f * urgency));
        Boss5Fx.SetAlpha(_ring, Mathf.Lerp(0.65f, 1f, _fillAmount));

        // The row carries the urgency now, so it is what pulses — the glyph stays
        // steady and legible throughout. At one or two allowed hits the outline
        // pulses too, which is the tell that this window has no room for mistakes.
        if (_cellFill != null)
        {
            float a = Mathf.Lerp(0.78f, 1f, _fillAmount) * (0.80f + 0.20f * urgency);
            for (int i = 0; i < _cellFill.Length; i++)
                if (_cellFill[i] != null && _cellFill[i].enabled)
                    Boss5Fx.SetAlpha(_cellFill[i], a);
        }

        if (_barShadow != null && HairTrigger)
            Boss5Fx.SetAlpha(_barShadow, Mathf.Lerp(0.45f, 0.95f, urgency));

        if (_punch > 0f)
        {
            _punch = Mathf.Max(0f, _punch - Time.deltaTime * 5f);
            transform.localScale = Vector3.one * (1f + Mathf.Sin(_punch * Mathf.PI) * 0.24f);
        }
        else if (transform.localScale != Vector3.one)
        {
            transform.localScale = Vector3.one;
        }
    }
}


// =============================================================================
//  SAFE ZONE VISUAL  (Mechanic 3 — evade)
// -----------------------------------------------------------------------------
//  The danger flood is one big sprite with a transparent hole punched over the
//  zone, alpha-ramping as the timer runs down. The zone itself gets the full ring
//  treatment plus a vertical beacon column, so it can be found from off-screen.
// =============================================================================
public class Boss5SafeZoneVisual : MonoBehaviour
{
    private const int DangerOrder = 3020;
    private const int ShadowOrder = 3021;
    private const int FillOrder = 3022;
    private const int HaloOrder = 3023;
    private const int SweepOrder = 3024;
    private const int TicksOrder = 3025;
    private const int RingOrder = 3026;
    private const int CoreOrder = 3027;
    private const int DashOrder = 3028;
    private const int BeamOrder = 3029;

    private SpriteRenderer _danger, _shadow, _fill, _halo, _sweep, _ticks, _ring, _core, _beam;
    private Boss5Motes _motes;
    private float _sweepSpin;
    private NightLight _nightLight;
    private float _zoneRadius;
    private float _maxAlpha;
    private Color _dangerColor;
    private float _progress;
    private bool _succeeded;
    private float _beamCheck;
    private float _beamAlpha = 0.30f;
    private bool _someoneInside;

    private static readonly Color SafeGreen = new Color(0.35f, 1f, 0.55f, 1f);

    public static Boss5SafeZoneVisual Create(Vector3 pos, float zoneRadius, float coverRadius,
                                             Color dangerColor, float maxAlpha)
    {
        var go = new GameObject("Boss5_SafeZone");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        var v = go.AddComponent<Boss5SafeZoneVisual>();
        v._zoneRadius = Mathf.Max(0.3f, zoneRadius);
        v._dangerColor = dangerColor;
        v._maxAlpha = Mathf.Clamp01(maxAlpha);
        v.Build(Mathf.Max(coverRadius, zoneRadius * 4f));
        return v;
    }

    private void Build(float coverRadius)
    {
        float d = _zoneRadius * 2f;

        _danger = Boss5Fx.Child(transform, "DangerFlood",
            Boss5Sprites.GetHoleOverlay(_zoneRadius / coverRadius), DangerOrder);
        Boss5Fx.Scale(_danger, coverRadius * 2f);
        Boss5Fx.Tint(_danger, _dangerColor, 0f);

        _shadow = Boss5Fx.Child(transform, "Shadow", Boss5Sprites.GetSoftDisc(), ShadowOrder);
        Boss5Fx.Scale(_shadow, d * 1.05f);
        _shadow.color = new Color(0f, 0f, 0f, 0.26f);

        _fill = Boss5Fx.Child(transform, "ZoneFill", Boss5Sprites.GetEdgeGradientDisc(), FillOrder);
        Boss5Fx.Scale(_fill, d);
        Boss5Fx.Tint(_fill, SafeGreen, 0.20f);

        _halo = Boss5Fx.Child(transform, "Halo", Boss5Sprites.GetRadialGlow(), HaloOrder, glowy: true);
        Boss5Fx.Scale(_halo, d * 1.6f);
        Boss5Fx.Tint(_halo, SafeGreen, 0.22f);

        // Instrument ticks + sweep, matching the Mechanic 1 mark so the two read as
        // the same visual language rather than two unrelated effects.
        _sweep = Boss5Fx.Child(transform, "ZoneSweep", Boss5Sprites.GetAngularSweep(160f), SweepOrder);
        Boss5Fx.Scale(_sweep, d * 0.99f);
        Boss5Fx.Tint(_sweep, Color.Lerp(SafeGreen, Color.white, 0.35f), 0.26f);

        _ticks = Boss5Fx.Child(transform, "ZoneTicks", Boss5Sprites.GetTickRing(32, 8), TicksOrder);
        Boss5Fx.Scale(_ticks, d);
        Boss5Fx.Tint(_ticks, Color.Lerp(SafeGreen, Color.white, 0.25f), 0.8f);

        _ring = Boss5Fx.Child(transform, "ZoneRing",
            Boss5Sprites.GetGlowRing(Boss5Sprites.RimCore, Boss5Sprites.RimGlow), RingOrder);
        Boss5Fx.Scale(_ring, d);
        Boss5Fx.Tint(_ring, SafeGreen, 0.95f);

        _core = Boss5Fx.Child(transform, "ZoneCore",
            Boss5Sprites.GetGlowRing(Boss5Sprites.RimCoreFine, Boss5Sprites.RimGlowFine), CoreOrder);
        Boss5Fx.Scale(_core, d);
        Boss5Fx.Tint(_core, Color.Lerp(SafeGreen, Color.white, 0.65f), 0.9f);

        // Orbiting lights rather than a second concentric ring — same language as
        // the Mechanic 1 mark, and it reads as a beacon gathering energy.
        _motes = Boss5Motes.Create(transform, _zoneRadius, SafeGreen, 9, DashOrder);

        // Beacon column — makes the zone findable when it is off the edge of frame.
        _beam = Boss5Fx.Child(transform, "Beacon", Boss5Sprites.GetBeam(), BeamOrder, glowy: true);
        _beam.transform.localPosition = Vector3.zero;
        // Sprite is 1 world unit tall at scale 1 (PPU = texture height), so these
        // multipliers ARE the on-screen size in world units. Roughly 2.4x the zone
        // radius tall reads as a shaft without leaving the camera.
        _beam.transform.localScale = new Vector3(_zoneRadius * 2.0f, _zoneRadius * 2.4f, 1f);
        Boss5Fx.Tint(_beam, SafeGreen, 0.30f);

        _nightLight = Boss5Sprites.AddNightLight(gameObject, SafeGreen, _zoneRadius * 3f, 1.1f, 0.25f);
    }

    /// 0..1 across the challenge timer — the flood darkens as time runs out.
    ///
    /// It starts at a BASE level rather than fully transparent. Ramping from zero
    /// meant the danger was invisible for the first second or two of the window —
    /// precisely when the player is deciding whether to run — so the map read as
    /// safe at the exact moment the decision mattered.
    public void SetProgress(float t)
    {
        _progress = Mathf.Clamp01(t);
        const float BASE = 0.42f;   // fraction of max shown the instant the zone opens
        float a = _maxAlpha * (BASE + (1f - BASE) * _progress);
        Boss5Fx.Tint(_danger, _dangerColor, a);
    }

    public void PlaySuccessFlourish()
    {
        _succeeded = true;
        Boss5Fx.Tint(_fill, SafeGreen, 0.38f);
        Boss5Fx.Tint(_halo, SafeGreen, 0.5f);
        Boss5Fx.Tint(_ring, Color.white, 1f);
        StartCoroutine(SuccessBurst());
    }

    private IEnumerator SuccessBurst()
    {
        // One clean expanding ring, so "you made it" reads instantly.
        var burst = Boss5Fx.Child(transform, "SuccessBurst",
            Boss5Sprites.GetGlowRing(0.014f, 0.12f), RingOrder + 1, glowy: true);
        float life = 0.5f, t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float eased = 1f - (1f - k) * (1f - k);
            Boss5Fx.Scale(burst, _zoneRadius * 2f * (1f + eased * 1.6f));
            Boss5Fx.Tint(burst, SafeGreen, 1f - k);
            yield return null;
        }
        if (burst != null) Destroy(burst.gameObject);
    }

    private void Update()
    {
        if (_ring == null) return;

        float d = _zoneRadius * 2f;

        // Beckoning pulse that accelerates toward the deadline.
        float rate = Mathf.Lerp(2.2f, 11f, _progress);
        float beat = Mathf.Sin(Time.time * rate);
        float ringD = d * (1f + beat * 0.05f);
        Boss5Fx.Scale(_ring, ringD);
        Boss5Fx.Scale(_core, ringD);
        Boss5Fx.Scale(_ticks, ringD * 0.998f);

        _sweepSpin += Mathf.Lerp(70f, 260f, _progress) * Time.deltaTime;
        if (_sweep != null)
            _sweep.transform.localRotation = Quaternion.Euler(0f, 0f, _sweepSpin);

        float haloBeat = beat * 0.5f + 0.5f;
        Boss5Fx.SetAlpha(_halo,
            (_succeeded ? 0.5f : Mathf.Lerp(0.18f, 0.4f, _progress)) * (0.6f + 0.4f * haloBeat));

        if (_motes != null) _motes.SetEnergy(_succeeded ? 1f : Mathf.Lerp(0.25f, 1f, _progress));

        // Beacon: quieter overall, and it steps aside once somebody is standing in
        // the zone. Its job is to be findable from across the map, and the moment
        // you have arrived it has done that job — keeping it bright just obscures
        // your own sprite.
        if (_beam != null)
        {
            _beamCheck -= Time.deltaTime;
            if (_beamCheck <= 0f)
            {
                _beamCheck = 0.15f;                     // no need to test every frame
                _someoneInside = false;
                var reg = PlayerRegistry.Instance;
                if (reg != null)
                {
                    foreach (var ps in reg.AllAliveInRadius(transform.position, _zoneRadius * 1.35f))
                        if (ps != null) { _someoneInside = true; break; }
                }
            }

            float target = _someoneInside ? 0.10f : (0.26f + 0.12f * haloBeat);
            if (_succeeded) target *= 0.5f;
            _beamAlpha = Mathf.MoveTowards(_beamAlpha, target, Time.deltaTime * 0.6f);
            Boss5Fx.SetAlpha(_beam, _beamAlpha);
        }

        if (_nightLight != null)
            _nightLight.intensity = Mathf.Lerp(0.85f, 1.35f, haloBeat);
    }
}


//  COUNTDOWN RING  

public class Boss5CountdownRing : MonoBehaviour
{
    private const int BackOrder = 4090;
    private const int GlowOrder = 4092;
    private const int CoreOrder = 4094;

    // Round beads, not bars. Every previous attempt failed the same way: a dashed
    // ring reads as a dotted line and a wedge ring reads as a heavy frame, because
    // both are made of STROKES. Discrete round points sitting in space have no
    // stroke weight to look clumsy, so they stay clean at any size.
    private const int Beads = 28;

    private SpriteRenderer[] _back, _glow, _core;
    private Transform _follow;
    private Vector2 _offset;
    private float _radius;
    private Color _color = Color.white;
    private float _remaining = 1f;

    public static Boss5CountdownRing Create(Transform follow, Vector2 offset,
                                            float radius, Color color, int segments = 28)
    {
        var go = new GameObject("Boss5_CountdownRing");
        var r = go.AddComponent<Boss5CountdownRing>();
        r._follow = follow;
        r._offset = offset;
        r._radius = Mathf.Max(0.3f, radius);
        r._color = color;
        r.Build();
        return r;
    }

    private void Build()
    {
        _back = new SpriteRenderer[Beads];
        _glow = new SpriteRenderer[Beads];
        _core = new SpriteRenderer[Beads];

        var dot = Boss5Sprites.GetRadialGlow();

        for (int i = 0; i < Beads; i++)
        {
            Vector3 pos = BeadPosition(i);

            // Dark backing so a bead never disappears against a pale biome.
            var b = Boss5Fx.Child(transform, $"BeadBack{i}", dot, BackOrder);
            b.transform.localPosition = pos;
            b.color = new Color(0f, 0f, 0f, 0.35f);
            _back[i] = b;

            // Coloured halo.
            var g = Boss5Fx.Child(transform, $"BeadGlow{i}", dot, GlowOrder);
            g.transform.localPosition = pos;
            _glow[i] = g;

            // Hot near-white centre — this is what makes a bead read as a light
            // rather than a coloured dot.
            var c = Boss5Fx.Child(transform, $"BeadCore{i}", dot, CoreOrder);
            c.transform.localPosition = pos;
            _core[i] = c;
        }

        SetColor(_color);
        SetRemaining(1f);
        Boss5Sprites.AddNightLight(gameObject, _color, _radius * 1.8f, 0.5f, 0.3f);
    }

    /// Beads run clockwise from 12 o'clock, the direction a clock empties.
    private Vector3 BeadPosition(int i)
    {
        float ang = (90f - (360f / Beads) * i) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * _radius;
    }

    public void SetColor(Color c)
    {
        _color = c;
        SetRemaining(_remaining);
    }

    /// <param name="remaining01">1 at the start of the challenge, 0 when time is up.</param>
    public void SetRemaining(float remaining01)
    {
        _remaining = Mathf.Clamp01(remaining01);
        if (_glow == null) return;

        float litExact = _remaining * Beads;
        int lit = Mathf.CeilToInt(litExact);
        float partial = Mathf.Clamp01(litExact - (lit - 1));   // the leading bead

        // Urgency ramps over the last third rather than snapping on at a threshold.
        float urgency = 1f - Mathf.Clamp01(_remaining / 0.34f);
        float flash = 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.Lerp(4f, 14f, urgency));

        float baseSize = _radius * 0.115f;
        var hot = Color.Lerp(_color, Color.white, 0.65f);

        for (int i = 0; i < Beads; i++)
        {
            bool on = i < lit;
            // Fade the leading bead out smoothly instead of popping it off.
            float strength = !on ? 0f : (i == lit - 1 ? partial : 1f);

            // Travelling shimmer so the live beads look energised, not static.
            float wave = 0.85f + 0.15f * Mathf.Sin(Time.time * 3.2f - i * 0.42f);

            // Spent beads stay as dim ghosts, so the full track is always readable
            // and the player can see proportion, not just an absolute amount.
            float glowA = on ? Mathf.Lerp(0.10f, 0.62f, strength) * wave * flash : 0.07f;
            float coreA = on ? Mathf.Lerp(0f, 0.95f, strength) * flash : 0f;
            float backA = on ? 0.35f : 0.16f;

            float size = baseSize * (on ? Mathf.Lerp(0.75f, 1f, strength) : 0.55f);
            // The leading bead is bigger and breathes — the eye tracks a moving
            // point far better than it tracks a shrinking length.
            if (on && i == lit - 1) size *= 1.35f + 0.15f * Mathf.Sin(Time.time * 9f);

            Boss5Fx.Tint(_glow[i], _color, glowA);
            Boss5Fx.Tint(_core[i], hot, coreA);
            Boss5Fx.SetAlpha(_back[i], backA);

            _glow[i].transform.localScale = Vector3.one * (size * 2.6f);
            _core[i].transform.localScale = Vector3.one * (size * 0.85f);
            _back[i].transform.localScale = Vector3.one * (size * 1.7f);
        }
    }

    private void LateUpdate()
    {
        if (_follow == null) { Destroy(gameObject); return; }

        Vector3 p = _follow.position + (Vector3)_offset;
        p.z = 0f;
        transform.position = p;

        // Re-apply every frame so the shimmer, the flash and the leading bead's
        // breathing keep animating between SetRemaining calls.
        SetRemaining(_remaining);
    }
}


// =============================================================================
//  CAMERA PAN  (Mechanic 3 reveal)
// -----------------------------------------------------------------------------
//  Pans every live player camera off to the safe zone and back. Whatever component
//  drives the camera is disabled for the duration and restored afterwards, so a
//  follow script or a Cinemachine brain cannot fight the tween.
//
//  Everything here is REAL-TIME (WaitForSecondsRealtime, unscaledDeltaTime) because
//  the reveal runs while Time.timeScale is 0.
// =============================================================================
public class Boss5CameraPan
{
    private class Entry
    {
        public Transform CamTransform;
        public Vector3 OriginalPos;
        public readonly List<Behaviour> Suspended = new List<Behaviour>();
    }

    private readonly List<Entry> _entries = new List<Entry>();

    public static Boss5CameraPan Begin()
    {
        var pan = new Boss5CameraPan();

        foreach (var cam in ResolveCameras())
        {
            if (cam == null) continue;
            var e = new Entry
            {
                CamTransform = cam.transform,
                OriginalPos = cam.transform.position
            };

            // Suspend anything that writes this camera's transform. ICoopCamera covers
            // PlayerCameraController / PlayerCinemachineSplitScreen; the name check
            // catches a CinemachineBrain without taking a hard dependency on the
            // Cinemachine assembly.
            foreach (var b in cam.GetComponents<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                bool drivesCamera = b is ICoopCamera
                                    || b.GetType().Name.Contains("CinemachineBrain");
                if (!drivesCamera) continue;
                b.enabled = false;
                e.Suspended.Add(b);
            }

            pan._entries.Add(e);
        }

        return pan._entries.Count > 0 ? pan : null;
    }

    private static IEnumerable<Camera> ResolveCameras()
    {
        var reg = PlayerRegistry.Instance;
        var found = new List<Camera>();

        if (reg != null && reg.All != null)
        {
            for (int i = 0; i < reg.All.Count; i++)
            {
                var pref = reg.All[i];
                if (pref != null && pref.Camera != null && !found.Contains(pref.Camera))
                    found.Add(pref.Camera);
            }
        }

        if (found.Count == 0 && Camera.main != null) found.Add(Camera.main);
        return found;
    }

    public IEnumerator PanTo(Vector3 worldPos, float duration)
        => Tween(e => new Vector3(worldPos.x, worldPos.y, e.OriginalPos.z), duration);

    public IEnumerator PanBack(float duration)
        => Tween(e => e.OriginalPos, duration);

    private IEnumerator Tween(System.Func<Entry, Vector3> targetFor, float duration)
    {
        duration = Mathf.Max(0.05f, duration);

        var starts = new Vector3[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
            starts[i] = _entries[i].CamTransform != null
                ? _entries[i].CamTransform.position
                : Vector3.zero;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float eased = k * k * (3f - 2f * k);   // smoothstep: eases both ends

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.CamTransform == null) continue;
                e.CamTransform.position = Vector3.Lerp(starts[i], targetFor(e), eased);
            }
            yield return null;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.CamTransform != null) e.CamTransform.position = targetFor(e);
        }
    }

    /// Restore every suspended driver and snap each camera home. Idempotent.
    public void End()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.CamTransform != null) e.CamTransform.position = e.OriginalPos;
            for (int j = 0; j < e.Suspended.Count; j++)
                if (e.Suspended[j] != null) e.Suspended[j].enabled = true;
            e.Suspended.Clear();
        }
        _entries.Clear();
    }
}


//  KABOOM VFX

public class Boss5KaboomVFX : MonoBehaviour
{
    private const int Order = 3500;

    public static void Play(Vector3 pos, float radius, Color tint)
    {
        var go = new GameObject("Boss5_KaboomVFX");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.AddComponent<Boss5KaboomVFX>().Run(Mathf.Max(1f, radius), tint);
    }

    private void Run(float radius, Color tint)
    {
        StartCoroutine(Flash(radius * 0.45f, tint));
        StartCoroutine(Ring(radius * 0.85f, 0.50f, 0.00f, 0.012f, Color.white));
        StartCoroutine(Ring(radius * 1.00f, 0.62f, 0.07f, 0.010f, new Color(1f, 0.72f, 0.35f)));
        StartCoroutine(Ring(radius * 1.25f, 0.80f, 0.16f, 0.008f, tint));
        StartCoroutine(Sparks(radius, tint));
        StartCoroutine(Dust(radius));

        var nl = Boss5Sprites.AddNightLight(gameObject, new Color(1f, 0.8f, 0.5f), radius * 0.9f, 1.6f, 0.2f);
        if (nl != null) StartCoroutine(FadeLight(nl, 1.6f, 0.55f));

        Destroy(gameObject, 1.6f);
    }

    private IEnumerator FadeLight(NightLight nl, float from, float life)
    {
        float t = 0f;
        while (t < life && nl != null)
        {
            t += Time.deltaTime;
            nl.intensity = Mathf.Lerp(from, 0f, t / life);
            yield return null;
        }
    }

    private IEnumerator Flash(float radius, Color tint)
    {
        var sr = Boss5Fx.Child(transform, "Flash", Boss5Sprites.GetRadialGlow(), Order + 4, glowy: true);
        float life = 0.30f, t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            sr.color = Color.Lerp(Color.white, tint, k) * new Color(1f, 1f, 1f, 1f - k);
            Boss5Fx.Scale(sr, radius * 2f * (0.35f + k * 1.1f));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private IEnumerator Ring(float maxRadius, float life, float delay, float core, Color col)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        var sr = Boss5Fx.Child(transform, "Shock",
            Boss5Sprites.GetGlowRing(core, core * 7f), Order, glowy: true);
        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float eased = 1f - Mathf.Pow(1f - k, 3f);   // fast out, gentle settle
            Boss5Fx.Scale(sr, maxRadius * 2f * eased);
            Boss5Fx.Tint(sr, col, (1f - k) * (1f - k));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private IEnumerator Sparks(float radius, Color tint)
    {
        const int COUNT = 22;
        for (int i = 0; i < COUNT; i++)
            StartCoroutine(Spark((360f / COUNT) * i + Random.Range(-7f, 7f), radius, tint));
        yield break;
    }

    private IEnumerator Spark(float angleDeg, float radius, Color tint)
    {
        var sr = Boss5Fx.Child(transform, "Spark", Boss5Sprites.GetSpark(), Order + 2, glowy: true);
        // Rotate the squashed blob so its long axis points along travel.
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

        float dist = radius * Random.Range(0.45f, 0.95f);
        float life = Random.Range(0.35f, 0.7f);
        float size = Random.Range(0.35f, 0.8f);
        Vector3 dir = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                                  Mathf.Sin(angleDeg * Mathf.Deg2Rad), 0f);
        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float eased = 1f - Mathf.Pow(1f - k, 2.5f);   // decelerate outward
            sr.transform.localPosition = dir * dist * eased;
            sr.transform.localScale = new Vector3(size * (1f - k * 0.55f), size * 0.55f, 1f);
            Boss5Fx.Tint(sr, Color.Lerp(Color.white, tint, k), 1f - k);
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private IEnumerator Dust(float radius)
    {
        const int COUNT = 10;
        for (int i = 0; i < COUNT; i++)
        {
            StartCoroutine(DustPuff(Random.Range(0f, 360f), radius));
            if (i % 3 == 0) yield return new WaitForSeconds(0.03f);
        }
    }

    private IEnumerator DustPuff(float angleDeg, float radius)
    {
        var sr = Boss5Fx.Child(transform, "Dust", Boss5Sprites.GetRadialGlow(), Order + 1);
        Vector3 dir = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                                  Mathf.Sin(angleDeg * Mathf.Deg2Rad), 0f);
        float dist = radius * Random.Range(0.25f, 0.6f);
        float life = Random.Range(0.7f, 1.2f);
        float size = radius * Random.Range(0.18f, 0.34f);
        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            sr.transform.localPosition = dir * dist * (1f - Mathf.Pow(1f - k, 2f));
            Boss5Fx.Scale(sr, size * (0.6f + k * 1.4f));
            Boss5Fx.Tint(sr, new Color(0.55f, 0.5f, 0.45f), 0.4f * (1f - k));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }
}


// =============================================================================
//  BELL RESONANCE — the toll
// -----------------------------------------------------------------------------
//  Replaces the three expanding concentric rings, which read as clip-art: evenly
//  spaced perfect circles are the most obviously "generated" shape there is.
//
//  What a struck bell actually does to its surroundings is shove AIR. So this is
//  built from that idea instead:
//    · a single very faint, very fast pressure ring — one, not three, and gone in
//      a quarter second, so it registers as a shove rather than as decoration
//    · dust lifted off the ground in an irregular ring, drifting outward and up,
//      each puff at its own speed so the edge is ragged
//    · embers/motes flung out and decelerating, arcing back down under gravity
//    · a light pulse that WOBBLES as it decays, the way a struck bell's tone beats
// =============================================================================
public class Boss5BellResonance : MonoBehaviour
{
    private const int Order = 3300;

    public static void Play(Vector3 pos, Color tint, float radius = 5f, int ringCount = 3)
    {
        var go = new GameObject("Boss5_BellResonance");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.AddComponent<Boss5BellResonance>().Run(tint, Mathf.Max(1f, radius));
    }

    private void Run(Color tint, float radius)
    {
        StartCoroutine(PressureRing(tint, radius));

        // Irregular dust ring. Angles are jittered rather than evenly spaced so the
        // silhouette never resolves into a circle.
        int puffs = 14;
        for (int i = 0; i < puffs; i++)
            StartCoroutine(DustPuff((360f / puffs) * i + Random.Range(-13f, 13f), radius));

        int embers = 10;
        for (int i = 0; i < embers; i++)
            StartCoroutine(Ember((360f / embers) * i + Random.Range(-18f, 18f), radius, tint));

        var nl = Boss5Sprites.AddNightLight(gameObject, tint, radius * 0.8f, 0.9f, 0.4f);
        if (nl != null) StartCoroutine(FadeLight(nl));

        Destroy(gameObject, 2.2f);
    }

    private IEnumerator FadeLight(NightLight nl)
    {
        float life = 0.8f, t = 0f;
        while (t < life && nl != null)
        {
            t += Time.deltaTime;
            // Wobble as it fades — a bell's tone beating, not a lamp switching off.
            nl.intensity = 0.9f * (1f - t / life) * (0.7f + 0.3f * Mathf.Sin(t * 26f));
            yield return null;
        }
    }

    /// One faint, fast pressure front. Squashed slightly on Y so it lies on the
    /// ground plane rather than hanging in the air like a bubble.
    private IEnumerator PressureRing(Color tint, float radius)
    {
        var sr = Boss5Fx.Child(transform, "Pressure",
            Boss5Sprites.GetGlowRing(Boss5Sprites.RimCoreFine, 0.10f), Order);
        float life = 0.26f, t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float d = radius * 2f * (1f - Mathf.Pow(1f - k, 2.6f));
            sr.transform.localScale = new Vector3(d, d * 0.82f, 1f);
            Boss5Fx.Tint(sr, tint, 0.30f * (1f - k) * (1f - k));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// Dust lifted off the ground: outward, upward, expanding and thinning.
    private IEnumerator DustPuff(float angleDeg, float radius)
    {
        yield return new WaitForSeconds(Random.Range(0f, 0.09f));

        var sr = Boss5Fx.Child(transform, "Dust", Boss5Sprites.GetRadialGlow(), Order - 1);
        var dir = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                              Mathf.Sin(angleDeg * Mathf.Deg2Rad), 0f);
        float dist = radius * Random.Range(0.35f, 0.85f);
        float life = Random.Range(0.9f, 1.6f);
        float size = radius * Random.Range(0.10f, 0.20f);
        float rise = Random.Range(0.15f, 0.5f);
        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float outward = 1f - Mathf.Pow(1f - k, 2.4f);       // decelerating
            Vector3 p = dir * dist * outward;
            p.y += rise * k;                                    // drifts up as it spreads
            sr.transform.localPosition = p;
            float d = size * (0.55f + k * 2.0f);
            sr.transform.localScale = new Vector3(d, d * 0.8f, 1f);
            // Warm dust, thinning fast at first then lingering.
            Boss5Fx.Tint(sr, new Color(0.60f, 0.56f, 0.50f), 0.34f * Mathf.Pow(1f - k, 1.5f));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// Embers flung out, decelerating, then falling.
    private IEnumerator Ember(float angleDeg, float radius, Color tint)
    {
        var sr = Boss5Fx.Child(transform, "Ember", Boss5Sprites.GetSpark(), Order + 1);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

        var dir = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                              Mathf.Sin(angleDeg * Mathf.Deg2Rad), 0f);
        float dist = radius * Random.Range(0.4f, 0.95f);
        float life = Random.Range(0.5f, 0.95f);
        float size = radius * Random.Range(0.035f, 0.07f);
        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            Vector3 p = dir * dist * (1f - Mathf.Pow(1f - k, 2.6f));
            p.y -= k * k * radius * 0.35f;                      // gravity
            sr.transform.localPosition = p;
            sr.transform.localScale = new Vector3(size * (1.6f - k), size * 0.6f, 1f);
            Boss5Fx.Tint(sr, Color.Lerp(Color.white, tint, k), (1f - k) * 0.9f);
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }
}



