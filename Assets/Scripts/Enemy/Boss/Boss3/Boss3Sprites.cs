using UnityEngine;

// Runtime world-space sprite factory for the Boss3 visuals + tree-hand VFX, so the
// whole boss needs ZERO imported art (matching how BerserkVisual / UIProceduralSprites
// generate everything at runtime). Sprites are created once, cached, and shared by
// every Boss3 instance. Created with HideAndDontSave so they survive scene loads and
// are never accidentally serialized into a scene.
public static class Boss3Sprites
{
    private static Sprite _softDot;
    private static Sprite _spark;

    // Domain-reload-disabled safety: play-mode exit destroys the cached textures, but
    // the static fields would still point at dead objects on the next Play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _softDot = null;
        _spark = null;
    }

    // Soft radial blob — glow heads, eyes, embers, impact flashes, aura.
    public static Sprite SoftDot
    {
        get
        {
            if (_softDot == null) _softDot = BuildRadial(48, 2.0f);
            return _softDot;
        }
    }

    // Tighter, brighter core — used for sharp sparks / thorn tips.
    public static Sprite Spark
    {
        get
        {
            if (_spark == null) _spark = BuildRadial(32, 3.2f);
            return _spark;
        }
    }

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
        return MakeSprite(tex, size);
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

    private static Sprite MakeSprite(Texture2D tex, int size)
    {
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        s.name = "Boss3Procedural_" + size;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}



