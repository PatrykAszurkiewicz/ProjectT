using UnityEngine;

// LORE PAPER ART 
// Shared, CACHED procedural art for the lore UI
public static class LorePaperArt
{
    private static Sprite _paper;   // Unity '== null' catches destroyed-on-stop, so this re-bakes safely
    private static Sprite _panel;
    private static Sprite _solid;
    private static Sprite _btn, _btnSel;

    public static void Warm()
    {
        MakePaperSprite();
        MakePanelSprite();
        MakeSolidSprite();
        MakeButtonSprite(false);
        MakeButtonSprite(true);
    }


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

    // aged-paper sheet 
    public static Sprite MakePaperSprite()
    {
        if (_paper != null) return _paper;

        const int w = 320, h = 220, seed = 1337;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];

        Color paper = new Color(0.94f, 0.91f, 0.83f);
        Color warm = new Color(0.88f, 0.81f, 0.66f);
        Color stainC = new Color(0.74f, 0.62f, 0.44f);
        Color scorch = new Color(0.34f, 0.19f, 0.09f);
        Color charK = new Color(0.07f, 0.05f, 0.03f);
        Color clear = new Color(0, 0, 0, 0);

        const float baseInset = 7f, tearAmp = 9f, crinkleAmp = 3f, biteDepth = 22f, charBand = 10f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));

                // Organic torn outline: big undulation + crinkle + occasional deep bite.
                float n1 = Fbm(x * 0.022f, y * 0.022f, seed);
                float n2 = Fbm(x * 0.085f, y * 0.085f, seed + 91);
                float boundary = baseInset + (n1 - 0.5f) * 2f * tearAmp + (n2 - 0.5f) * 2f * crinkleAmp;
                float bite = Fbm(x * 0.016f + 40f, y * 0.016f + 40f, seed + 7);
                if (bite > 0.70f) boundary += (bite - 0.70f) * biteDepth;

                float ragged = edge - boundary;
                if (ragged < 0f) { px[idx] = clear; continue; }

                // Body: warmer toward the edges, with fibre grain + faint laid-lines.
                float ageT = Mathf.Clamp01(1f - edge / 95f);
                Color body = Color.Lerp(paper, warm, ageT * 0.7f);
                float fibre = (Fbm(x * 0.5f, y * 0.5f, seed + 5) - 0.5f) * 0.05f;
                float laid = Mathf.Sin(y * 0.8f) * 0.010f + Mathf.Sin(x * 0.33f) * 0.006f;
                body.r = Mathf.Clamp01(body.r + fibre + laid);
                body.g = Mathf.Clamp01(body.g + fibre + laid);
                body.b = Mathf.Clamp01(body.b + fibre + laid);

                // Faint, broad age stain (subtle so it doesn't blotch).
                float stain = Fbm(x * 0.018f + 13f, y * 0.018f - 7f, seed + 31);
                if (stain > 0.68f) body = Color.Lerp(body, stainC, (stain - 0.68f) * 0.8f);

                Color col = body;
                float a = 1f;

                // Scorched rim, width flickering like flame licks.
                float rimW = charBand * (0.6f + 0.9f * n2);
                if (ragged < rimW)
                {
                    float t = ragged / rimW;                         // 0 at tear .. 1 inner
                    Color burn = Color.Lerp(charK, scorch, Mathf.Clamp01(t * 1.7f));
                    col = Color.Lerp(burn, body, Mathf.SmoothStep(0f, 1f, t));
                    a = Mathf.Lerp(0.40f, 1f, t);                    // frail at the very edge
                }

                px[idx] = new Color(col.r, col.g, col.b, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        _paper = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return _paper;
    }

    // dark textured panel (archive background) 
    public static Sprite MakePanelSprite()
    {
        if (_panel != null) return _panel;

        const int w = 192, h = 128, seed = 5150;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];

        Color baseCol = new Color(0.135f, 0.10f, 0.075f); // dark leather/wood
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = x / (float)w - 0.5f, ny = y / (float)h - 0.5f;
                float vign = 1f - (nx * nx + ny * ny) * 0.9f;        // slightly lighter centre
                float grain = (Fbm(x * 0.6f, y * 0.6f, seed) - 0.5f) * 0.05f;
                float streak = Mathf.Sin(y * 0.25f) * 0.012f;        // faint horizontal grain
                float s = Mathf.Clamp01(vign * 0.6f + 0.55f + grain + streak);
                px[y * w + x] = new Color(baseCol.r * s, baseCol.g * s, baseCol.b * s, 1f);
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        // 9-slice border so it tiles cleanly on big panels without smearing the grain.
        _panel = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                               100f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
        return _panel;
    }

    // ── shared helpers ──
    // List-item button: vertical gradient, a thin bright bevel, and a scorched
    // (darkened, slightly frail) outer edge. 9-sliced so it scales to any row size.
    public static Sprite MakeButtonSprite(bool selected)
    {
        if (selected) { if (_btnSel != null) return _btnSel; }
        else { if (_btn != null) return _btn; }

        const int w = 128, h = 72;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];

        Color top, bot, edge, rim;
        if (selected)
        {
            top = new Color(0.80f, 0.30f, 0.70f);   // bright magenta
            bot = new Color(0.34f, 0.08f, 0.34f);
            edge = new Color(0.09f, 0.02f, 0.10f);   // charred
            rim = new Color(1.00f, 0.78f, 1.00f);
        }
        else
        {
            top = new Color(0.34f, 0.30f, 0.36f);   // dark purple-grey
            bot = new Color(0.13f, 0.11f, 0.14f);
            edge = new Color(0.05f, 0.04f, 0.05f);
            rim = new Color(0.72f, 0.70f, 0.80f);
        }

        const float band = 15f;     // charred edge thickness (px)
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);                 // 0 bottom .. 1 top
            float vs = v * v * (3f - 2f * v);
            float sheen = Mathf.Clamp01((v - 0.55f) / 0.45f);  // gentle top sheen
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                Color baseC = Color.Lerp(bot, top, vs);
                baseC = Color.Lerp(baseC, Color.Lerp(baseC, rim, 0.18f), sheen * 0.5f);

                int d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                float t = Mathf.Clamp01(d / band);        // 0 at border .. 1 inside
                float ts = Mathf.SmoothStep(0f, 1f, t);
                Color col = Color.Lerp(edge, baseC, ts);

                if (d >= band && d < band + 2f)           // thin bright bevel just inside the char
                    col = Color.Lerp(col, rim, 0.22f);

                float a = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(t * 1.8f)); // frail at the very edge
                px[idx] = new Color(col.r, col.g, col.b, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        var border = new Vector4(band + 7, band + 7, band + 7, band + 7);
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, border);
        if (selected) _btnSel = s; else _btn = s;
        return s;
    }

    public static Sprite MakeSolidSprite()
    {
        if (_solid != null) return _solid;
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        _solid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
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

