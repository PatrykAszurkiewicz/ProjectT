using UnityEngine;

// Runtime sprite factory so the boss-bar VFX need ZERO imported art.
// Everything is generated once, cached, and shared by every effect.
//    SoftDot  — radial falloff blob. Glow heads, trails, embers, sparks.
//    SoftBand — vertical falloff strip. Stretched along an edge it reads as
//                a soft "bloom" hugging the frame.
// Textures are created with HideAndDontSave so they survive scene loads and
// never end up serialized into a scene by accident.
public static class UIProceduralSprites
{
    private static Sprite _softDot;
    private static Sprite _softBand;

    // Domain-reload-disabled safety: the cached sprites are destroyed when play
    // mode exits, but the static fields would still point at the dead objects.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _softDot = null;
        _softBand = null;
    }

    public static Sprite SoftDot
    {
        get
        {
            if (_softDot == null) _softDot = BuildRadial(64, 2.0f);
            return _softDot;
        }
    }

    public static Sprite SoftBand
    {
        get
        {
            if (_softBand == null) _softBand = BuildBand(8, 64, 1.7f);
            return _softBand;
        }
    }

    // White disc whose alpha falls off from the centre. `falloff` > 1 tightens
    // the core (more "spark"), < 1 spreads it (more "haze").
    private static Sprite BuildRadial(int size, float falloff)
    {
        var tex = NewTexture(size, size);
        var px = new Color32[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), falloff);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);
        return MakeSprite(tex, size, size);
    }

    // White strip, opaque along the middle row, fading to nothing at top and
    // bottom. Stretched horizontally it becomes an edge bloom; rotate 90° for
    // the vertical edges.
    private static Sprite BuildBand(int width, int height, float falloff)
    {
        var tex = NewTexture(width, height);
        var px = new Color32[width * height];
        float half = height * 0.5f;

        for (int y = 0; y < height; y++)
        {
            float dy = Mathf.Abs(y + 0.5f - half) / half;
            float a = Mathf.Pow(Mathf.Clamp01(1f - dy), falloff);
            var c = new Color32(255, 255, 255, (byte)(a * 255f));
            for (int x = 0; x < width; x++) px[y * width + x] = c;
        }

        tex.SetPixels32(px);
        tex.Apply(false, false);
        return MakeSprite(tex, width, height);
    }

    private static Texture2D NewTexture(int w, int h)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static Sprite MakeSprite(Texture2D tex, int w, int h)
    {
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                              100f, 0, SpriteMeshType.FullRect);
        s.name = "Procedural_" + w + "x" + h;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}
