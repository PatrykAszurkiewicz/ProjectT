using UnityEngine;


public static class NightGlow
{
    private static Sprite _glowSprite;
    private const int GlowTextureSize = 64;


    // Returns true if universal night mode is currently active in the scene.

    public static bool IsNightActive()
    {
        return Object.FindFirstObjectByType<NightOverlay>() != null;
    }


    // Adds a soft radial glow halo as a child of the target GameObject.

    /// <param name="parent">The effect object to attach the glow to.</param>
    /// <param name="color">Glow tint color (alpha controls base opacity).</param>
    /// <param name="radius">World-space radius of the glow.</param>
    /// <param name="intensity">Opacity multiplier (0–1).</param>
    /// <param name="sortingOrder">Sorting order for the glow sprite.</param>
    /// <param name="localOffset">Optional local position offset.</param>
    public static GameObject AddGlow(
        GameObject parent,
        Color color,
        float radius = 2f,
        float intensity = 0.5f,
        int sortingOrder = -1,
        Vector3? localOffset = null)
    {
        if (!IsNightActive()) return null;
        if (parent == null) return null;

        EnsureGlowSprite();

        GameObject glowObj = new GameObject("NightGlow");
        glowObj.transform.SetParent(parent.transform, false);
        glowObj.transform.localPosition = localOffset ?? Vector3.zero;
        glowObj.transform.localRotation = Quaternion.identity;

        // The glow sprite is 64x64 at 32 PPU = 2 world units across.
        // Scale to match desired radius (diameter = radius * 2).
        float baseWorldSize = (float)GlowTextureSize / 32f; // 2.0
        float scale = (radius * 2f) / baseWorldSize;
        glowObj.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = glowObj.AddComponent<SpriteRenderer>();
        sr.sprite = _glowSprite;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = sortingOrder;
        sr.color = new Color(color.r, color.g, color.b, intensity);

        return glowObj;
    }


    // Standalone glow (not parented) — useful for ground effects like gunge puddles.

    public static GameObject AddGlowAtPosition(
        Vector3 worldPosition,
        Color color,
        float radius = 2f,
        float intensity = 0.5f,
        int sortingOrder = -1)
    {
        if (!IsNightActive()) return null;

        EnsureGlowSprite();

        GameObject glowObj = new GameObject("NightGlow_World");
        glowObj.transform.position = worldPosition;

        float baseWorldSize = (float)GlowTextureSize / 32f;
        float scale = (radius * 2f) / baseWorldSize;
        glowObj.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = glowObj.AddComponent<SpriteRenderer>();
        sr.sprite = _glowSprite;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = sortingOrder;
        sr.color = new Color(color.r, color.g, color.b, intensity);

        return glowObj;
    }

    private static void EnsureGlowSprite()
    {
        if (_glowSprite != null) return;

        int s = GlowTextureSize;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[s * s];
        float center = s * 0.5f;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                // Soft radial falloff — strong in center, smooth fade to edge
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                pixels[y * s + x] = (a > 0.003f)
                    ? new Color(1f, 1f, 1f, a)
                    : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, s, s),
                                     Vector2.one * 0.5f, 32f);
    }
}
