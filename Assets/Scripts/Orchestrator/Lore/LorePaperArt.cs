using UnityEngine;

// LORE PAPER ART
// Shared, CACHED procedural art for the lore UI.
//
// Everything is authored in a 1x "design space" (320x220 sheet, 192x128 panel,
// 128x72 button) and baked at Scale x that resolution, so the scroll is no longer a
// 2.8x bilinear blow-up of a small texture.
//
// The sheet is built from:
//   - four INDEPENDENT edge tear profiles (each side rips differently; corners fall
//     out of where two profiles meet rather than from one shared radial field)
//   - flame bites chewed out of the border, plus a few burn-through pinholes
//   - a scorch rim whose depth licks in and out, with faint ember warmth
//   - foxing specks, warped water-stain rings, two soft fold creases, fibre grain
// All of it is deterministic from PaperSeed, so the sheet looks the same every run.
public static class LorePaperArt
{
    private static Sprite _paper;   // Unity '== null' catches destroyed-on-stop, so this re-bakes safely
    private static Sprite _panel;
    private static Sprite _solid;
    private static Sprite _btn, _btnSel;

    // QUALITY
    // 1 = the old sizes. 3 = 960x660 paper, a hair over the 900x620 the scroll draws it
    // at on a 1080p canvas, so it lands ~1:1. Push to 4 if you ship at 1440p+.
    private static int _scale = 3;
    public static int Scale
    {
        get => _scale;
        set
        {
            int v = Mathf.Clamp(value, 1, 6);
            if (v == _scale) return;
            _scale = v;
            Invalidate();
        }
    }

    // Change this and you get a completely different sheet with the same character.
    public static int PaperSeed = 1337;

    // Design-space dimensions. The tuning constants below are calibrated against these;
    // turn Scale, not these.
    private const int PaperW = 320, PaperH = 220;
    private const int PanelW = 192, PanelH = 128;
    private const int BtnW = 128, BtnH = 72;

    // Tear tuning, all in design pixels.
    private const float BaseInset = 7f;     // how far in the paper starts, on average
    private const float TearAmp = 8f;       // slow undulation of the rip
    private const float CrinkleAmp = 3f;    // medium chatter
    private const float FringeAmp = 1.1f;   // per-pixel deckle hairs
    private const float Crinkle2D = 2.2f;   // 2D wobble so corners aren't just two lines crossing
    private const float BiteDepth = 26f;    // occasional deep gouge along an edge
    private const float CharBand = 10f;     // nominal scorch width

    /// Bake everything up front. Call from a loading screen — the sheet is ~630k pixels
    /// at Scale 3 and you don't want it happening on the frame a chest opens.
    public static void Warm()
    {
        MakePaperSprite();
        MakePanelSprite();
        MakeSolidSprite();
        MakeButtonSprite(false);
        MakeButtonSprite(true);
    }

    /// Drop the cache and free the textures (called automatically when Scale changes).
    public static void Invalidate()
    {
        Kill(ref _paper); Kill(ref _panel); Kill(ref _solid);
        Kill(ref _btn); Kill(ref _btnSel);
    }

    private static void Kill(ref Sprite s)
    {
        if (s == null) { s = null; return; }
        var tex = s.texture;
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(s);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(s);
            if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
        }
        s = null;
    }

    // NOISE

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + seed * 362437;
            n = (n ^ (n >> 13)) * 1274126177;
            n = n ^ (n >> 16);
            return (n & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float v00 = Hash(xi, yi, seed), v10 = Hash(xi + 1, yi, seed);
        float v01 = Hash(xi, yi + 1, seed), v11 = Hash(xi + 1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(v00, v10, u), Mathf.Lerp(v01, v11, u), v);
    }

    private static float Fbm(float x, float y, int seed)
    {
        float amp = 0.5f, freq = 1f, sum = 0f, norm = 0f;
        for (int o = 0; o < 3; o++)
        {
            sum += amp * ValueNoise(x * freq, y * freq, seed + o * 17);
            norm += amp; amp *= 0.5f; freq *= 2f;
        }
        return sum / norm;
    }

    // The big fields are low frequency, so they're evaluated once per DESIGN pixel and
    // bilinearly sampled. At Scale 3 that's a ninth of the cost of per-bake-pixel Fbm
    // and visually identical.
    private static float[] BuildField(int cw, int ch, float freq, float ox, float oy, int seed)
    {
        var f = new float[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                f[y * cw + x] = Fbm(x * freq + ox, y * freq + oy, seed);
        return f;
    }

    private static float SampleField(float[] f, int cw, int ch, float dx, float dy)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(dx), 0, cw - 2);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(dy), 0, ch - 2);
        float tx = Mathf.Clamp01(dx - x0), ty = Mathf.Clamp01(dy - y0);
        int r0 = y0 * cw + x0, r1 = r0 + cw;
        return Mathf.Lerp(Mathf.Lerp(f[r0], f[r0 + 1], tx),
                          Mathf.Lerp(f[r1], f[r1 + 1], tx), ty);
    }

    // One side's rip, as inset-from-that-edge vs. position along it. Each edge gets its
    // own seed, which is what makes the four sides read as separately torn.
    private static float[] EdgeProfile(int n, int seed)
    {
        var a = new float[n + 2];
        for (int i = 0; i < a.Length; i++)
        {
            float t = i;
            float v = (Fbm(t * 0.030f, 11.3f, seed) - 0.5f) * 2f * TearAmp
                    + (Fbm(t * 0.110f, 29.7f, seed + 13) - 0.5f) * 2f * CrinkleAmp
                    + (Fbm(t * 0.420f, 53.1f, seed + 29) - 0.5f) * 2f * FringeAmp;

            // Occasional deep gouge where the page tore badly.
            float bite = Fbm(t * 0.017f, 71.9f, seed + 7);
            if (bite > 0.64f) v += (bite - 0.64f) * BiteDepth;

            a[i] = BaseInset + v;
        }
        return a;
    }

    private static float SampleLine(float[] a, float t)
    {
        int i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, a.Length - 2);
        return Mathf.Lerp(a[i0], a[i0 + 1], Mathf.Clamp01(t - i0));
    }

    // rimMul: how wide the scorch band is around this cut. Border bites keep the full
    // band; small burn-throughs use a fraction of it, otherwise a 2px hole gets a 10px
    // black halo and reads as a polka dot instead of a hole.
    private struct Bite { public float x, y, r, rimMul; public int seed; }
    private struct Ring { public float x, y, r; public int seed; }

    // AGED-PAPER SHEET
    public static Sprite MakePaperSprite()
    {
        if (_paper != null) return _paper;

        int sc = _scale;
        int w = PaperW * sc, h = PaperH * sc;
        int seed = PaperSeed;

        var tex = NewTex(w, h);
        var px = new Color32[w * h];

        Color paper = new Color(0.94f, 0.91f, 0.83f);
        Color warm = new Color(0.88f, 0.81f, 0.66f);
        Color stainC = new Color(0.74f, 0.62f, 0.44f);
        Color foxC = new Color(0.56f, 0.38f, 0.22f);   // rust-brown age specks
        Color scorch = new Color(0.34f, 0.19f, 0.09f);
        Color ember = new Color(0.55f, 0.26f, 0.08f);  // faint warmth just inside the char
        Color charK = new Color(0.07f, 0.05f, 0.03f);
        // Transparent pixels keep the char colour, so filtering along the tear blends
        // toward soot instead of pulling a black halo out of nowhere.
        Color clear = new Color(charK.r, charK.g, charK.b, 0f);

        const float aaPixels = 1.0f;   // coverage feather, in BAKE pixels

        // Four independent rips.
        var edgeL = EdgeProfile(PaperH, seed + 101);
        var edgeR = EdgeProfile(PaperH, seed + 202);
        var edgeB = EdgeProfile(PaperW, seed + 303);
        var edgeT = EdgeProfile(PaperW, seed + 404);

        // Coarse 2D fields.
        int cw = PaperW + 2, ch = PaperH + 2;
        var fCrinkle = BuildField(cw, ch, 0.085f, 0f, 0f, seed + 91);     // corner wobble
        var fStain = BuildField(cw, ch, 0.018f, 13f, -7f, seed + 31);     // broad discolouration
        var fLick = BuildField(cw, ch, 0.040f, -21f, 33f, seed + 57);     // scorch depth
        var fWarp = BuildField(cw, ch, 0.055f, 60f, 60f, seed + 77);      // water-ring distortion

        // Fold creases: one near-vertical, one near-horizontal, each wobbling.
        var creaseV = new float[PaperH + 2];
        for (int i = 0; i < creaseV.Length; i++)
            creaseV[i] = PaperW * 0.385f + (Fbm(i * 0.020f, 5.5f, seed + 61) - 0.5f) * 14f;
        var creaseH = new float[PaperW + 2];
        for (int i = 0; i < creaseH.Length; i++)
            creaseH[i] = PaperH * 0.615f + (Fbm(i * 0.018f, 9.5f, seed + 62) - 0.5f) * 11f;

        // Flame bites chewed out of the border, plus small burn-through pinholes just
        // inside it. Both stay in the margin so they never eat into the text block.
        var bites = new Bite[9];
        for (int i = 0; i < bites.Length; i++)
        {
            bool pinhole = i >= 6;
            float ra = Hash(i, 1, seed + 811), rb = Hash(i, 2, seed + 812), rc = Hash(i, 3, seed + 813);
            float inset = pinhole ? Mathf.Lerp(11f, 21f, rc) : Mathf.Lerp(-4f, 7f, rc);
            float radius = pinhole ? Mathf.Lerp(1.4f, 3.4f, rb) : Mathf.Lerp(7f, 19f, rb);
            float rimMul = pinhole ? 0.26f : 1f;
            int bs = seed + 900 + i;

            switch (i & 3)
            {
                case 0: bites[i] = new Bite { x = inset, y = ra * PaperH, r = radius, rimMul = rimMul, seed = bs }; break;
                case 1: bites[i] = new Bite { x = PaperW - inset, y = ra * PaperH, r = radius, rimMul = rimMul, seed = bs }; break;
                case 2: bites[i] = new Bite { x = ra * PaperW, y = inset, r = radius, rimMul = rimMul, seed = bs }; break;
                default: bites[i] = new Bite { x = ra * PaperW, y = PaperH - inset, r = radius, rimMul = rimMul, seed = bs }; break;
            }
        }

        // Water-stain rings — faint fill with a darker tide line at the rim.
        var rings = new Ring[3];
        for (int i = 0; i < rings.Length; i++)
        {
            rings[i] = new Ring
            {
                x = Mathf.Lerp(PaperW * 0.12f, PaperW * 0.88f, Hash(i, 7, seed + 700)),
                y = Mathf.Lerp(PaperH * 0.12f, PaperH * 0.88f, Hash(i, 8, seed + 701)),
                r = Mathf.Lerp(22f, 48f, Hash(i, 9, seed + 702)),
                seed = seed + 710 + i
            };
        }

        float invSc = 1f / sc;

        for (int y = 0; y < h; y++)
        {
            float dy = y * invSc;
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                float dx = x * invSc;

                float crinkle = SampleField(fCrinkle, cw, ch, dx, dy);

                // Distance inside the sheet: nearest of the four independent rips.
                float ragged = Mathf.Min(
                    Mathf.Min(dx - SampleLine(edgeL, dy), (PaperW - 1 - dx) - SampleLine(edgeR, dy)),
                    Mathf.Min(dy - SampleLine(edgeB, dx), (PaperH - 1 - dy) - SampleLine(edgeT, dx)));

                // 2D wobble so corners aren't just two straight profiles intersecting.
                ragged -= (crinkle - 0.5f) * 2f * Crinkle2D;

                // Silhouette detail evaluated at BAKE resolution. The 1D edge profiles
                // are sampled per design pixel, which is smooth once blown up 3x; these
                // two octaves are what actually make the outline read as crisp torn
                // fibre. Coherent lobes, deliberately not a fine speckle — a very high
                // frequency here throws isolated dots and looks like static.
                ragged += (Fbm(dx * 0.55f, dy * 0.55f, seed + 301) - 0.5f) * 2f * 2.0f;
                ragged += (ValueNoise(dx * 1.45f, dy * 1.45f, seed + 302) - 0.5f) * 2f * 0.55f;

                // Bites and pinholes carve inward.
                float rimMul = 1f;
                for (int b = 0; b < bites.Length; b++)
                {
                    float bdx = dx - bites[b].x, bdy = dy - bites[b].y;
                    float d2 = bdx * bdx + bdy * bdy;
                    float rMax = bites[b].r * 1.8f;
                    if (d2 >= rMax * rMax) continue;

                    float d = Mathf.Sqrt(d2);
                    if (d < 0.0001f) d = 0.0001f;
                    // Direction-dependent radius -> ragged hole, no trig needed. Two
                    // octaves, or the "hole" comes out as a smooth circle.
                    float ux = bdx / d, uy = bdy / d;
                    float wob = 0.50f + 0.60f * ValueNoise(ux * 3.1f + 5f, uy * 3.1f + 5f, bites[b].seed)
                                      + 0.34f * ValueNoise(ux * 7.9f + 11f, uy * 7.9f + 11f, bites[b].seed + 5);
                    float cand = d - bites[b].r * wob;
                    if (cand < ragged) { ragged = cand; rimMul = bites[b].rimMul; }
                }

                // Antialiased coverage rather than a hard cutoff — this is what kills the
                // stair-stepping along the burn line.
                float cover = Mathf.Clamp01((ragged * sc + aaPixels) / (2f * aaPixels));
                if (cover <= 0f) { px[idx] = clear; continue; }

                // BODY
                float ageT = Mathf.Clamp01(1f - ragged / 85f);      // warmer toward every torn edge
                Color body = Color.Lerp(paper, warm, ageT * 0.72f);

                // Fibre: a stretched octave (thread direction), a design-space octave
                // (the original grain), and bake-resolution tooth.
                float fibre = (ValueNoise(dx * 0.85f, dy * 0.20f, seed + 4) - 0.5f) * 0.030f
                            + (ValueNoise(dx * 0.5f, dy * 0.5f, seed + 5) - 0.5f) * 0.035f
                            + (Hash(x, y, seed + 23) - 0.5f) * 0.013f;
                float laid = Mathf.Sin(dy * 0.8f) * 0.009f + Mathf.Sin(dx * 0.33f) * 0.005f;
                float shade = 1f;

                // Fold creases: a soft valley with a catchlight on one side.
                float creaseX = SampleLine(creaseV, dy);
                float cv = Mathf.Abs(dx - creaseX);
                if (cv < 8f)
                {
                    float t = 1f - cv / 8f;
                    shade -= t * t * 0.055f;
                    if (dx > creaseX) shade += t * 0.022f;
                }
                float chz = Mathf.Abs(dy - SampleLine(creaseH, dx));
                if (chz < 7f)
                {
                    float t = 1f - chz / 7f;
                    shade -= t * t * 0.042f;
                }

                body.r = Mathf.Clamp01(body.r * shade + fibre + laid);
                body.g = Mathf.Clamp01(body.g * shade + fibre + laid);
                body.b = Mathf.Clamp01(body.b * shade + fibre + laid);

                // Broad age stain.
                float stain = SampleField(fStain, cw, ch, dx, dy);
                if (stain > 0.66f) body = Color.Lerp(body, stainC, (stain - 0.66f) * 0.85f);

                // Water rings: warped radius, faint fill, darker tide line at the rim.
                float warp = SampleField(fWarp, cw, ch, dx, dy);
                for (int r = 0; r < rings.Length; r++)
                {
                    float rdx = dx - rings[r].x, rdy = dy - rings[r].y;
                    float rd2 = rdx * rdx + rdy * rdy;
                    float lim = rings[r].r * 1.35f;
                    if (rd2 >= lim * lim) continue;

                    float rn = Mathf.Sqrt(rd2) / rings[r].r * (0.90f + 0.20f * warp);
                    if (rn >= 1.22f) continue;

                    float fill = Mathf.SmoothStep(1.22f, 0.15f, rn) * 0.065f;
                    float e = (rn - 0.90f) / 0.11f;
                    float tide = Mathf.Exp(-e * e) * 0.19f;      // concentrated tide line
                    body = Color.Lerp(body, stainC, Mathf.Clamp01(fill + tide));
                }

                // Foxing: rust specks on a coarse cell grid, sized so they never clip at
                // cell borders (radius <= 1.8, centre kept in [2.2, 4.8] of a 7px cell).
                int fxi = Mathf.FloorToInt(dx * (1f / 7f)), fyi = Mathf.FloorToInt(dy * (1f / 7f));
                float fh = Hash(fxi, fyi, seed + 201);
                if (fh > 0.795f)
                {
                    float ox = 2.2f + Hash(fxi, fyi, seed + 202) * 2.6f;
                    float oy = 2.2f + Hash(fxi, fyi, seed + 203) * 2.6f;
                    float sxp = dx - fxi * 7f - ox, syp = dy - fyi * 7f - oy;
                    float rr = 0.55f + (fh - 0.795f) * 6.0f;      // up to ~1.8
                    float dd = Mathf.Sqrt(sxp * sxp + syp * syp);
                    if (dd < rr)
                    {
                        float a2 = 1f - dd / rr;
                        body = Color.Lerp(body, foxC, a2 * a2 * 0.34f);
                    }
                }

                Color col = body;
                float alpha = 1f;

                // SCORCH RIM — depth licks in and out like flame damage.
                float lick = SampleField(fLick, cw, ch, dx, dy);
                float rimW = CharBand * (0.45f + 1.30f * lick) * rimMul
                             * (0.78f + 0.44f * ValueNoise(dx * 0.9f, dy * 0.9f, seed + 303));
                float rd = Mathf.Max(0f, ragged);
                if (rd < rimW)
                {
                    float t = rd / rimW;                                  // 0 at tear .. 1 inner
                    Color burn = Color.Lerp(charK, scorch, Mathf.Clamp01(t * 1.7f));
                    // A little ember warmth where the burn ran deepest.
                    if (lick > 0.62f) burn = Color.Lerp(burn, ember, (lick - 0.62f) * 1.6f * (1f - t));
                    col = Color.Lerp(burn, body, Mathf.SmoothStep(0f, 1f, t));
                    alpha = Mathf.Clamp01(0.30f + rd * 0.55f);            // thin frail lip, then solid
                }

                px[idx] = new Color(col.r, col.g, col.b, alpha * cover);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);
        // pixelsPerUnit scales with the texture so SetNativeSize and any 9-slice maths
        // still yield the same on-screen size the 1x version did.
        _paper = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * sc);
        return _paper;
    }

    private static Texture2D NewTex(int w, int h)
    {
        // No mipmaps on purpose: these draw at or near 1:1 in a Canvas, and mip blending
        // across the transparent torn edge would soften exactly what we're sharpening.
        return new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };
    }

    // DARK TEXTURED PANEL (archive background)
    public static Sprite MakePanelSprite()
    {
        if (_panel != null) return _panel;

        int sc = _scale;
        int w = PanelW * sc, h = PanelH * sc;
        const int seed = 5150;

        var tex = NewTex(w, h);
        var px = new Color32[w * h];

        float invSc = 1f / sc;
        Color baseCol = new Color(0.135f, 0.10f, 0.075f); // dark leather/wood

        for (int y = 0; y < h; y++)
        {
            float dy = y * invSc;
            for (int x = 0; x < w; x++)
            {
                float dx = x * invSc;
                float nx = x / (float)w - 0.5f, ny = y / (float)h - 0.5f;
                float vign = 1f - (nx * nx + ny * ny) * 0.9f;        // slightly lighter centre
                float grain = (Fbm(dx * 0.6f, dy * 0.6f, seed) - 0.5f) * 0.05f
                            + (Hash(x, y, seed + 3) - 0.5f) * 0.018f; // fine tooth at bake res
                float streak = Mathf.Sin(dy * 0.25f) * 0.012f;       // faint horizontal grain
                float s = Mathf.Clamp01(vign * 0.6f + 0.55f + grain + streak);
                px[y * w + x] = new Color(baseCol.r * s, baseCol.g * s, baseCol.b * s, 1f);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);
        // 9-slice border. Border and PPU both scale with the texture, so the visible
        // corner size is unchanged from the 1x version.
        float b = 24f * sc;
        _panel = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                               100f * sc, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        return _panel;
    }

    // LIST-ITEM BUTTON
    // Vertical gradient, a thin bright bevel, and a scorched outer edge. 9-sliced.
    public static Sprite MakeButtonSprite(bool selected)
    {
        if (selected) { if (_btnSel != null) return _btnSel; }
        else { if (_btn != null) return _btn; }

        int sc = _scale;
        int w = BtnW * sc, h = BtnH * sc;

        var tex = NewTex(w, h);
        var px = new Color32[w * h];

        Color top, bot, edge, rim;
        if (selected)
        {
            top = new Color(0.80f, 0.30f, 0.70f);   // bright magenta
            bot = new Color(0.34f, 0.08f, 0.34f);
            edge = new Color(0.09f, 0.02f, 0.10f);  // charred
            rim = new Color(1.00f, 0.78f, 1.00f);
        }
        else
        {
            top = new Color(0.34f, 0.30f, 0.36f);   // dark purple-grey
            bot = new Color(0.13f, 0.11f, 0.14f);
            edge = new Color(0.05f, 0.04f, 0.05f);
            rim = new Color(0.72f, 0.70f, 0.80f);
        }

        const float bandDesign = 15f;               // charred edge thickness (design px)
        float band = bandDesign * sc;
        float bevel = 2f * sc;

        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);                      // 0 bottom .. 1 top
            float vs = v * v * (3f - 2f * v);
            float sheen = Mathf.Clamp01((v - 0.55f) / 0.45f);  // gentle top sheen
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                Color baseC = Color.Lerp(bot, top, vs);
                baseC = Color.Lerp(baseC, Color.Lerp(baseC, rim, 0.18f), sheen * 0.5f);

                float d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                float t = Mathf.Clamp01(d / band);             // 0 at border .. 1 inside
                float ts = Mathf.SmoothStep(0f, 1f, t);
                Color col = Color.Lerp(edge, baseC, ts);

                if (d >= band && d < band + bevel)             // thin bright bevel inside the char
                {
                    float bt = 1f - Mathf.Abs((d - band) / bevel * 2f - 1f);
                    col = Color.Lerp(col, rim, 0.22f * bt);
                }

                float a = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(t * 1.8f)); // frail at the very edge
                px[idx] = new Color(col.r, col.g, col.b, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);
        float bp = (bandDesign + 7f) * sc;
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * sc, 0,
                              SpriteMeshType.FullRect, new Vector4(bp, bp, bp, bp));
        if (selected) _btnSel = s; else _btn = s;
        return s;
    }

    // SOLID (dividers, backdrop)
    public static Sprite MakeSolidSprite()
    {
        if (_solid != null) return _solid;
        var tex = NewTex(8, 8);
        var px = new Color32[64];
        for (int i = 0; i < 64; i++) px[i] = Color.white;
        tex.SetPixels32(px); tex.Apply(false, false);
        _solid = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f);
        return _solid;
    }

    public static Font GetUIFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }
}




