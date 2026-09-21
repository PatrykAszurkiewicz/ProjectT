using UnityEngine;


// Procedurally generates the "Bomber" enemy sprite 
public static class BomberSprite
{
    /// Texture is square; this is its side length in pixels.
    public const int SIZE = 256;

    // Palette sampled from the reference shard: deep indigo hide → magenta threads,
    // with pale-lilac spikes.
    private static readonly Color HIDE_BASE = new Color(0.34f, 0.07f, 0.40f); // lit violet hide
    private static readonly Color THREAD = new Color(0.87f, 0.29f, 1.00f); // bright magenta filament
    private static readonly Color THREAD_DIM = new Color(0.50f, 0.12f, 0.66f); // thread glow / undertone
    private static readonly Color SPIKE_LILAC = new Color(0.74f, 0.62f, 0.80f); // pale violet bone

    private static Sprite _cached;
    private static float _cachedPpu = -1f;


    /// Returns the shared procedural Bomber sprite, (re)building it if the
    /// requested pixels-per-unit differs from the cached one.

    public static Sprite Get(float pixelsPerUnit)
    {
        if (_cached != null && Mathf.Approximately(_cachedPpu, pixelsPerUnit))
            return _cached;

        _cached = Build(pixelsPerUnit);
        _cachedPpu = pixelsPerUnit;
        return _cached;
    }


    private static Sprite Build(float ppu)
    {
        var b = new Color[SIZE * SIZE]; // starts fully transparent (Color default = 0,0,0,0)

        // Layout (y is UP, origin bottom-left — Unity texture convention).
        // Content is CENTRED so the sprite rolls cleanly about its pivot.
        Vector2 C = new Vector2(128f, 128f);   // body centre
        float R = 70f;                          // body radius (spikes extend past this)

        // Light directions reused across the piece: key light from upper-left.
        Vector3 L = new Vector3(-0.45f, 0.55f, 0.70f).normalized; // 3D, for spheres
        Vector2 L2D = new Vector2(-0.6f, 0.6f).normalized;        // 2D, for spikes

        // 1) Ring spikes FIRST, so the body covers their inner base (they read as
        //    growing out of the sphere, not stuck on front). Irregular lengths for
        //    an organic, hand-grown feel.
        DrawRingSpikes(b, C, R, L2D);

        // 2) Dark occlusion rim behind the body → crisp readable silhouette.
        Disc(b, C.x, C.y, R + 2.4f, new Color(0.03f, 0.03f, 0.04f, 0.95f), 1.5f);

        // 3) The shaded, mottled, leathery body.
        DrawMonsterBody(b, C, R, L);

        // 4) Interwoven, faintly glowing purple thread lattice across the hide —
        //    the organic, veined 'woven' look.
        DrawThreads(b, C, R);

        // 5) Raised nodules / warts on the visible face.
        DrawNodules(b, C, R, L);

        // 6) A few FRONT-facing spikes emerging toward the viewer → real depth.
        DrawFrontSpikes(b, C, R, L2D);

        // 7) Cool backlight kiss along the top rim to separate from dark biomes.
        RimLight(b, C, R);

        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
        {
            name = "ProcBomberTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(b);
        tex.Apply(false, false); // keep CPU copy readable → death-shatter can read pixels

        var sp = Sprite.Create(
            tex, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f),
            ppu, 0, SpriteMeshType.FullRect);
        sp.name = "ProcBomber"; // stable name → EnemyDeathVFX pixel-cache key is stable
        return sp;
    }


    private static void DrawMonsterBody(Color[] b, Vector2 C, float R, Vector3 L)
    {
        Vector3 V = new Vector3(0f, 0f, 1f);
        Vector3 H = (L + V).normalized;

        // Dark violet hide sampled from the reference — deep indigo in shadow,
        // rich magenta-violet where the light catches it.
        Color baseCol = HIDE_BASE;
        float feather = 1.6f;

        int x0 = Mathf.FloorToInt(C.x - R - feather), x1 = Mathf.CeilToInt(C.x + R + feather);
        int y0 = Mathf.FloorToInt(C.y - R - feather), y1 = Mathf.CeilToInt(C.y + R + feather);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - C.x) / R;
                float dy = (y - C.y) / R;
                float rr = dx * dx + dy * dy;
                if (rr > 1.15f) continue;

                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - rr));
                Vector3 N = new Vector3(dx, dy, nz);

                // Diffuse — darker ambient than a metal ball for a grim look.
                float diff = Mathf.Max(0f, Vector3.Dot(N, L));
                float lum = 0.13f + 0.86f * diff;

                // Organic mottling (multi-octave value noise across the surface).
                float n = Fbm(x * 0.055f, y * 0.055f);
                lum *= 0.84f + 0.30f * n;

                Color col = baseCol * lum;

                // Patchy hue drift (some areas hotter magenta, some deeper indigo)
                // for living, uneven hide.
                float tone = Fbm(x * 0.028f + 41f, y * 0.028f + 17f) - 0.5f;
                col.r += tone * 0.055f;
                col.b += tone * 0.060f;
                col.g += tone * 0.015f;

                // Leathery sheen plus a tighter glossy reflection (wet/organic look).
                float ndh = Mathf.Max(0f, Vector3.Dot(N, H));
                float sheen = Mathf.Pow(ndh, 9f) * 0.16f;   // broad soft sheen
                float gloss = Mathf.Pow(ndh, 42f) * 0.55f;  // crisp specular reflection
                float s = sheen + gloss;
                col.r += s; col.g += s; col.b += s * 0.95f;

                // Grounding: darken the lower belly a touch (ambient occlusion).
                col *= 1f - 0.14f * Mathf.Clamp01(-dy);

                // Dark contact ring at the very silhouette.
                float rim = Mathf.Pow(1f - nz, 3.5f);
                col *= 1f - 0.30f * rim;

                float dist = Mathf.Sqrt(rr) * R;
                float cov = 1f - SS(R - feather, R, dist);
                if (cov <= 0f) continue;

                col.a = cov;
                Blend(b, x, y, col);
            }

        // Baked light reflections on the wet hide: a broad key reflection and a
        // crisp hotspot upper-left, plus a dim magenta bounce lower-right.
        Glow(b, C.x - R * 0.34f, C.y + R * 0.36f, R * 0.36f, new Color(0.80f, 0.72f, 1f, 0.32f), 2.4f);
        Disc(b, C.x - R * 0.32f, C.y + R * 0.40f, R * 0.10f, new Color(1f, 0.95f, 1f, 0.85f), 2.0f);
        Glow(b, C.x + R * 0.36f, C.y - R * 0.42f, R * 0.26f, new Color(0.62f, 0.30f, 0.78f, 0.18f), 2.4f);
    }

    private static void RimLight(Color[] b, Vector2 C, float R)
    {
        // A thin cool arc along the upper-left rim; helps the dark body separate
        // from dark biomes without looking shiny.
        int x0 = Mathf.FloorToInt(C.x - R - 3f), x1 = Mathf.CeilToInt(C.x + R + 3f);
        int y0 = Mathf.FloorToInt(C.y - R - 3f), y1 = Mathf.CeilToInt(C.y + R + 3f);
        Vector2 dir = new Vector2(-0.6f, 0.62f).normalized;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - C.x) / R, dy = (y - C.y) / R;
                float rr = dx * dx + dy * dy;
                if (rr > 1.02f || rr < 0.80f) continue;
                float band = 1f - Mathf.Abs(Mathf.Sqrt(rr) - 0.95f) / 0.06f;
                if (band <= 0f) continue;
                float facing = Mathf.Clamp01(Vector2.Dot(new Vector2(dx, dy).normalized, dir));
                float a = band * Mathf.Pow(facing, 1.5f) * 0.5f;
                if (a <= 0f) continue;
                Blend(b, x, y, new Color(0.78f, 0.42f, 0.98f, a));
            }
    }

    private static void DrawThreads(Color[] b, Vector2 C, float R)
    {
        // Two families of gently S-curved strands — one running top↕bottom, one
        // running left↔right — crossing to read as an interwoven mesh. Control
        // points bulge outward at the middle to suggest the sphere's curvature.
        // Families are drawn INTERLEAVED (v,h,v,h,…); because every strand lays a
        // thin dark edge, whichever is drawn later reads as passing OVER the one
        // beneath, giving the over/under weave.
        Vector2 O(float x, float y) => C + new Vector2(x, y);

        (Vector2, Vector2, Vector2)[] vert =
        {
            (O(-40f, 52f), O(-54f, 0f),  O(-34f, -52f)),
            (O(-14f, 55f), O(-24f, 0f),  O(-6f,  -55f)),
            (O( 12f, 55f), O( 4f,  0f),  O( 18f, -55f)),
            (O( 38f, 52f), O( 52f, 0f),  O( 30f, -52f)),
        };
        (Vector2, Vector2, Vector2)[] horiz =
        {
            (O(-52f, 34f), O( 0f, 48f),  O( 52f, 30f)),
            (O(-55f, 10f), O( 0f, 22f),  O( 55f, 14f)),
            (O(-55f,-14f), O( 0f, -4f),  O( 55f,-18f)),
            (O(-52f,-38f), O( 0f,-30f),  O( 50f,-42f)),
        };

        for (int i = 0; i < 4; i++)
        {
            Thread(b, vert[i].Item1, vert[i].Item2, vert[i].Item3);
            Thread(b, horiz[i].Item1, horiz[i].Item2, horiz[i].Item3);
        }

        // Glowing organic nodes where strands knot together.
        Vector2[] nodes = { O(-14f, 4f), O(12f, 6f), O(-6f, -22f), O(20f, -20f), O(0f, 26f) };
        foreach (var n in nodes) ThreadNode(b, n);
    }

    // One glowing filament: soft magenta halo, a thin dark edge (so crossings read
    // as woven), a bright core, and a hot centre line.
    private static void Thread(Color[] b, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        BezierStroke(b, p0, p1, p2, 6.0f, 4.6f, new Color(THREAD_DIM.r, THREAD_DIM.g, THREAD_DIM.b, 0.16f));
        BezierStroke(b, p0, p1, p2, 3.4f, 1.6f, new Color(0.05f, 0.01f, 0.07f, 0.80f));
        BezierStroke(b, p0, p1, p2, 2.4f, 1.1f, new Color(THREAD.r, THREAD.g, THREAD.b, 0.95f));
        BezierStroke(b, p0, p1, p2, 1.1f, 0.6f, new Color(1f, 0.72f, 1f, 0.9f));
    }

    private static void ThreadNode(Color[] b, Vector2 c)
    {
        Glow(b, c.x, c.y, 9f, new Color(THREAD.r, THREAD.g, THREAD.b, 0.30f), 2.2f);
        Disc(b, c.x, c.y, 3.2f, new Color(THREAD.r, THREAD.g, THREAD.b, 0.95f), 1.0f);
        Disc(b, c.x - 0.6f, c.y + 0.6f, 1.5f, new Color(1f, 0.82f, 1f, 0.95f), 0.8f);
    }

    private static void DrawNodules(Color[] b, Vector2 C, float R, Vector3 L)
    {
        // Raised warts scattered on the visible hemisphere (deterministic).
        Vector2[] p =
        {
            new(-30f,  36f), new( 34f,  22f), new( 6f,  46f),
            new(-44f, -8f),  new( 40f, -18f), new(-16f, -30f),
            new( 18f, -4f),  new(-4f,  10f),
        };
        float[] rr = { 9f, 11f, 7f, 8f, 10f, 7.5f, 6f, 8.5f };
        for (int i = 0; i < p.Length; i++)
            Bump(b, C + p[i], rr[i], L);
    }

    // A small shaded dome sitting on the body surface.
    private static void Bump(Color[] b, Vector2 c, float r, Vector3 L)
    {
        Color skin = new Color(0.38f, 0.12f, 0.44f);
        float feather = 1.2f;

        // Soft socket shadow so it reads as raised.
        Disc(b, c.x, c.y - 1.2f, r + 1.6f, new Color(0.05f, 0.045f, 0.04f, 0.42f), 2.0f);

        int x0 = Mathf.FloorToInt(c.x - r - feather), x1 = Mathf.CeilToInt(c.x + r + feather);
        int y0 = Mathf.FloorToInt(c.y - r - feather), y1 = Mathf.CeilToInt(c.y + r + feather);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - c.x) / r, dy = (y - c.y) / r;
                float d2 = dx * dx + dy * dy;
                if (d2 > 1.10f) continue;
                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - d2));
                Vector3 N = new Vector3(dx, dy, nz);
                float diff = Mathf.Max(0f, Vector3.Dot(N, L));
                float lum = 0.18f + 0.95f * diff;
                Color col = skin * lum;
                float spec = Mathf.Pow(diff, 6f) * 0.20f + Mathf.Pow(diff, 30f) * 0.55f;
                col.r += spec; col.g += spec; col.b += spec * 0.9f;
                float cov = 1f - SS(1f - feather / r, 1f, Mathf.Sqrt(d2));
                if (cov <= 0f) continue;
                col.a = cov;
                Blend(b, x, y, col);
            }
        // Tiny glossy glint on the lit shoulder of the wart.
        Disc(b, c.x - r * 0.30f, c.y + r * 0.34f, r * 0.18f, new Color(1f, 1f, 1f, 0.7f), 1.0f);
    }


    private static void DrawRingSpikes(Color[] b, Vector2 C, float R, Vector2 L2D)
    {
        const int N = 12;
        for (int i = 0; i < N; i++)
        {
            // Even spacing + deterministic angular jitter → organic, not mechanical.
            float jitter = (Hash(i * 7 + 3, 11) - 0.5f) * (Mathf.PI * 2f / N) * 0.55f;
            float ang = (i / (float)N) * Mathf.PI * 2f + jitter;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

            float len = Mathf.Lerp(24f, 42f, Hash(i, 91));   // varied length
            float baseR = Mathf.Lerp(7.5f, 11.5f, Hash(i, 53)); // varied thickness

            Vector2 basePt = C + dir * (R - 6f); // sunk into the body
            Spike(b, basePt, dir, len, baseR, L2D);
        }
    }

    private static void DrawFrontSpikes(Color[] b, Vector2 C, float R, Vector2 L2D)
    {
        // A few spikes angled toward the viewer, rooted on the front face.
        (Vector2 baseOff, Vector2 dir, float len, float br)[] fs =
        {
            (new Vector2(-18f,  22f), new Vector2(-0.5f,  0.86f), 34f, 10.5f),
            (new Vector2( 26f,  10f), new Vector2( 0.86f, 0.5f),  30f, 9.5f),
            (new Vector2( 8f,  -26f), new Vector2( 0.25f,-0.97f), 28f, 9f),
            (new Vector2(-30f, -14f), new Vector2(-0.9f, -0.2f),  26f, 8.5f),
        };
        foreach (var s in fs)
            Spike(b, C + s.baseOff, s.dir.normalized, s.len, s.br, L2D);
    }

    // One tapered, shaded bone spike (dark outline + lit body + highlight ridge).
    private static void Spike(Color[] b, Vector2 basePt, Vector2 dir, float len, float baseR, Vector2 L2D)
    {
        dir = dir.normalized;
        Vector2 tip = basePt + dir * len;

        float lit = Mathf.Clamp01(Vector2.Dot(dir, L2D));
        Color bone = SPIKE_LILAC;
        Color boneLit = bone * (0.52f + 0.62f * lit);
        boneLit.a = 1f;

        // Dark socket where it meets the hide.
        Disc(b, basePt.x, basePt.y, baseR + 1.5f, new Color(0.05f, 0.045f, 0.04f, 0.7f), 1.4f);

        // Outline pass (slightly fatter, dark) → clean silhouette on any biome.
        TaperStroke(b, basePt, tip, baseR + 1.7f, 1.1f, new Color(0.05f, 0.045f, 0.05f, 0.95f));
        // Bone body.
        TaperStroke(b, basePt, tip, baseR, 0.5f, boneLit);

        // Highlight ridge, offset toward the light.
        Vector2 perp = new Vector2(-dir.y, dir.x);
        if (Vector2.Dot(perp, L2D) < 0f) perp = -perp;
        Vector2 hiBase = basePt + perp * (baseR * 0.35f);
        Vector2 hiTip = tip + perp * (0.3f);
        Color hi = Color.Lerp(boneLit, Color.white, 0.55f); hi.a = 0.85f;
        TaperStroke(b, hiBase, hiTip, baseR * 0.38f, 0.4f, hi);

        // A small glint of light catching the spike near its tip when it faces
        // the light — reads as facets twinkling as the ball rolls.
        if (lit > 0.35f)
        {
            Vector2 g = tip - dir * (len * 0.18f);
            Disc(b, g.x, g.y, baseR * 0.24f, new Color(1f, 0.92f, 1f, 0.55f * lit), 0.9f);
        }
    }

    private static void TaperStroke(Color[] b, Vector2 a, Vector2 c, float r0, float r1, Color col)
    {
        const int steps = 48;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 p = Vector2.Lerp(a, c, t);
            Disc(b, p.x, p.y, Mathf.Lerp(r0, r1, t), col, 0.9f);
        }
    }

    private static float Hash(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0x7fffffff) / 2147483647f;
    }

    private static float VNoise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float a = Hash(xi, yi), c1 = Hash(xi + 1, yi), c2 = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
        return Mathf.Lerp(Mathf.Lerp(a, c1, u), Mathf.Lerp(c2, d, u), v);
    }

    private static float Fbm(float x, float y)
    {
        float s = 0f, amp = 0.5f, f = 1f, norm = 0f;
        for (int i = 0; i < 3; i++) { s += amp * VNoise(x * f, y * f); norm += amp; amp *= 0.5f; f *= 2f; }
        return s / norm;
    }


    private static float SS(float e0, float e1, float x)
    {
        if (e1 <= e0) return x < e0 ? 0f : 1f;
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    private static void Blend(Color[] b, int x, int y, Color c)
    {
        if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || c.a <= 0f) return;
        int i = y * SIZE + x;
        Color d = b[i];
        float outA = c.a + d.a * (1f - c.a);
        if (outA <= 1e-5f) { b[i] = default; return; }
        float ia = 1f / outA;
        b[i] = new Color(
            (c.r * c.a + d.r * d.a * (1f - c.a)) * ia,
            (c.g * c.a + d.g * d.a * (1f - c.a)) * ia,
            (c.b * c.a + d.b * d.a * (1f - c.a)) * ia,
            outA);
    }

    private static void Disc(Color[] b, float cx, float cy, float r, Color col, float feather = 1.2f)
    {
        int x0 = Mathf.FloorToInt(cx - r - feather), x1 = Mathf.CeilToInt(cx + r + feather);
        int y0 = Mathf.FloorToInt(cy - r - feather), y1 = Mathf.CeilToInt(cy + r + feather);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float cov = 1f - SS(r - feather, r, d);
                if (cov <= 0f) continue;
                Color c = col; c.a *= cov; Blend(b, x, y, c);
            }
    }

    // Soft radial falloff (transparent at the edge) — used for light glows and
    // the baked surface reflections.
    private static void Glow(Color[] b, float cx, float cy, float r, Color col, float power = 2f)
    {
        int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
        int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d >= r) continue;
                float f = Mathf.Pow(1f - d / r, power);
                Color c = col; c.a *= f; Blend(b, x, y, c);
            }
    }

    private static void BezierStroke(Color[] b, Vector2 p0, Vector2 p1, Vector2 p2, float r0, float r1, Color col)
    {
        const int steps = 64;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float u = 1f - t;
            Vector2 p = u * u * p0 + 2f * u * t * p1 + t * t * p2;
            Disc(b, p.x, p.y, Mathf.Lerp(r0, r1, t), col, 1.0f);
        }
    }
}


