using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

// Shared look-and-feel + procedural UI factory for the in-game menus
// (ControlRebindScreen, OptionsMenu). Keeps the panel/button sprites, purple
// theme, fonts and widget construction in ONE place so the menus match.
public static class MenuTheme
{
    public const string PanelSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/MenuPanel 1";
    public const string ButtonSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/Button 1";

    public static readonly Color Magenta = new Color(0.78f, 0.30f, 0.92f, 1f);
    public static readonly Color Violet = new Color(0.49f, 0.18f, 0.78f, 1f);
    public static readonly Color GradTop = new Color(0.11f, 0.05f, 0.18f, 0.98f);
    public static readonly Color GradBottom = new Color(0.03f, 0.01f, 0.05f, 0.98f);
    public static readonly Color PanelSolid = new Color(0.10f, 0.07f, 0.15f, 0.98f);
    public static readonly Color BtnSolid = new Color(0.17f, 0.12f, 0.24f, 1f);
    public static readonly Color BtnActive = new Color(0.55f, 0.24f, 0.78f, 1f);
    public static readonly Color TextCol = new Color(0.95f, 0.92f, 0.99f, 1f);
    public static readonly Color ValueCol = new Color(1.00f, 0.86f, 1.00f, 1f);

    private static bool _spritesLoaded;
    private static Sprite _panel, _button;
    public static Sprite PanelSprite { get { EnsureSprites(); return _panel; } }
    public static Sprite ButtonSprite { get { EnsureSprites(); return _button; } }

    private static void EnsureSprites()
    {
        if (_spritesLoaded) return;
        _panel = Resources.Load<Sprite>(PanelSpritePath);
        _button = Resources.Load<Sprite>(ButtonSpritePath);
        _spritesLoaded = true;
    }

    private static bool _fontResolved;
    private static TMP_FontAsset _cachedFont;

    // Priority: explicit TMP asset → explicit .ttf (converted) → Resources TMP →
    // Resources .ttf (converted) → default. Cached so it resolves once.
    public static TMP_FontAsset ResolveFont(TMP_FontAsset tmpSlot, Font ttfSlot)
    {
        // Reuse a previously resolved font (so assigning it on either menu skins both).
        if (_fontResolved && _cachedFont != null) return _cachedFont;

        if (tmpSlot != null) { _cachedFont = tmpSlot; _fontResolved = true; return _cachedFont; }

        if (ttfSlot != null) _cachedFont = TMP_FontAsset.CreateFontAsset(ttfSlot);

        if (_cachedFont == null)
        {
            string[] tmpPaths = { "Fonts/Cinzel-ExtraBold SDF", "Fonts/Cinzel/Cinzel-ExtraBold SDF", "Cinzel-ExtraBold SDF" };
            foreach (var p in tmpPaths) { var f = Resources.Load<TMP_FontAsset>(p); if (f != null) { _cachedFont = f; break; } }
        }
        if (_cachedFont == null)
        {
            string[] ttfPaths = { "Fonts/Cinzel-ExtraBold", "Fonts/Cinzel/static/Cinzel-ExtraBold", "Cinzel-ExtraBold" };
            foreach (var p in ttfPaths) { var f = Resources.Load<Font>(p); if (f != null) { _cachedFont = TMP_FontAsset.CreateFontAsset(f); break; } }
        }

        // Mark resolved only once we actually have a font, so a menu opened later
        // with the font assigned can still populate the cache.
        if (_cachedFont != null) _fontResolved = true;
        return _cachedFont;
    }

    //  generated gradient sprites 
    public static Sprite VerticalGradient(Color top, Color bottom)
    {
        const int h = 128;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(h - 1)));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
    }

    public static Sprite HorizontalFade()
    {
        const int w = 128;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            float a = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(t - 0.5f) * 2f);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f), 100f);
    }

    //  widget factory 
    public static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    public static TextMeshProUGUI NewText(string text, Transform parent, float size,
                                          TextAlignmentOptions align, TMP_FontAsset font = null)
    {
        var go = NewUI("Text", parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = TextCol;
        tmp.alignment = align; tmp.richText = true;
        if (font != null) tmp.font = font;
        go.AddComponent<LayoutElement>();
        return tmp;
    }

    public static Button NewButton(string text, Transform parent, float fontSize, TMP_FontAsset font = null)
    {
        var go = NewUI("Button", parent);
        var img = go.AddComponent<Image>();
        ApplySprite(img, ButtonSprite, BtnSolid);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var c = btn.colors;
        c.highlightedColor = new Color(1.15f, 1.05f, 1.2f, 1f);
        c.pressedColor = new Color(0.8f, 0.6f, 0.95f, 1f);
        c.fadeDuration = 0.08f;
        btn.colors = c;
        go.AddComponent<LayoutElement>();

        var label = NewText(text, go.transform, fontSize, TextAlignmentOptions.Center, font);
        label.color = ValueCol;
        label.fontStyle = FontStyles.Bold;
        Stretch(label.rectTransform);
        return btn;
    }

    // A horizontal value slider (0..1) built via Unity's DefaultControls, then
    // recoloured to the purple theme.
    public static Slider NewSlider(Transform parent, float value, UnityAction<float> onChanged)
    {
        var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.transform.SetParent(parent, false);
        var slider = go.GetComponent<Slider>();
        slider.minValue = 0f; slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(value);
        if (onChanged != null) slider.onValueChanged.AddListener(onChanged);

        var bg = go.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = new Color(0f, 0f, 0f, 0.5f);
            // Thicker track (default is the middle 50% of the slider height).
            var brt = bg.rectTransform;
            brt.anchorMin = new Vector2(0f, 0.18f); brt.anchorMax = new Vector2(1f, 0.82f);
            brt.offsetMin = new Vector2(brt.offsetMin.x, 0f); brt.offsetMax = new Vector2(brt.offsetMax.x, 0f);
        }
        var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
        if (fill != null) fill.color = Magenta;
        var handle = go.transform.Find("Handle Slide Area/Handle")?.GetComponent<Image>();
        if (handle != null)
        {
            handle.color = new Color(0.96f, 0.90f, 1f, 1f);
            // Wider handle = bigger grab target.
            handle.rectTransform.sizeDelta = new Vector2(34f, handle.rectTransform.sizeDelta.y);
        }

        go.AddComponent<LayoutElement>();
        return slider;
    }

    public static void ApplySprite(Image img, Sprite sprite, Color fallback)
    {
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = Color.white;
        }
        else img.color = fallback;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }
}
