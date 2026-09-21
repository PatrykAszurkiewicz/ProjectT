using UnityEngine;

// A soft green glow that breathes behind the Parfumer's body sprite, giving the
// enemy a subtle toxic aura.

[DisallowMultipleComponent]
public class ParfumerAura : MonoBehaviour
{
    private SpriteRenderer auraRenderer;
    private SpriteRenderer bodyRenderer;   // tracked so we stay just behind the body's sort order

    private Color baseColor = new Color(0.45f, 0.95f, 0.35f, 1f);
    private float diameter = 2.8f;         // world units
    private float yOffset = -0.15f;        // world units
    private float pulseSpeed = 2.2f;
    private float minAlpha = 0.10f;
    private float maxAlpha = 0.26f;
    private float scalePulse = 0.12f;      // +/- fraction of diameter over the pulse

    private float phase;

    // Built once and reused by every Parfumer — the tint is applied per-instance
    // via SpriteRenderer.color, so a single white glow sprite serves them all.
    private static Sprite _softGlow;

    /// Configure the aura. Called by ParfumerController right after AddComponent.
    public void Configure(SpriteRenderer body, Color color, float diameter, float yOffset,
                          float pulseSpeed, float minAlpha, float maxAlpha, float scalePulse)
    {
        this.bodyRenderer = body;
        this.baseColor = color;
        this.diameter = Mathf.Max(0.01f, diameter);
        this.yOffset = yOffset;
        this.pulseSpeed = pulseSpeed;
        this.minAlpha = Mathf.Clamp01(minAlpha);
        this.maxAlpha = Mathf.Clamp01(maxAlpha);
        this.scalePulse = Mathf.Max(0f, scalePulse);

        EnsureRenderer();

        // Randomise the starting phase so a pack of Parfumers doesn't pulse in lockstep.
        phase = Random.Range(0f, Mathf.PI * 2f);
        ApplyTransform(0.5f);
        ApplyColor(0.5f);
    }

    private void Awake()
    {
        EnsureRenderer();
    }

    private void EnsureRenderer()
    {
        if (auraRenderer == null)
            auraRenderer = GetComponent<SpriteRenderer>();
        if (auraRenderer == null)
            auraRenderer = gameObject.AddComponent<SpriteRenderer>();

        if (auraRenderer.sprite == null)
            auraRenderer.sprite = GetSoftGlowSprite();
    }

    private void Update()
    {
        phase += Time.deltaTime * pulseSpeed;
        float t = Mathf.Sin(phase) * 0.5f + 0.5f; // 0..1

        ApplyTransform(t);
        ApplyColor(t);

        // Keep the glow one step behind the body's current sorting, even as
        // Y-sorting changes the body's order while the enemy moves.
        if (bodyRenderer != null && auraRenderer != null)
        {
            auraRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
            auraRenderer.sortingOrder = bodyRenderer.sortingOrder - 1;
        }
    }

    // Converts the desired world size/offset into local values, dividing out the
    // parent's scale so the prefab's 0.25 root scale doesn't shrink the glow.
    private void ApplyTransform(float t)
    {
        float parentScale = 1f;
        if (transform.parent != null)
        {
            parentScale = transform.parent.lossyScale.x;
            if (Mathf.Abs(parentScale) < 1e-4f) parentScale = 1f;
        }

        float worldD = diameter * (1f + Mathf.Lerp(-scalePulse, scalePulse, t));
        float localD = worldD / parentScale;
        transform.localScale = new Vector3(localD, localD, 1f);

        transform.localPosition = new Vector3(0f, yOffset / parentScale, 0f);
    }

    private void ApplyColor(float t)
    {
        if (auraRenderer == null) return;
        Color c = baseColor;
        c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
        auraRenderer.color = c;
    }

    // A 1x1 world-unit soft radial disc (opaque core feathering to a transparent
    // rim) — the same idea as the death-VFX ember sprite. pixelsPerUnit == SIZE
    // makes the sprite exactly one world unit across at localScale 1, so localScale
    // maps straight onto diameter.
    private static Sprite GetSoftGlowSprite()
    {
        if (_softGlow != null) return _softGlow;

        const int SIZE = 128;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var px = new Color[SIZE * SIZE];
        Vector2 ctr = new Vector2((SIZE - 1) * 0.5f, (SIZE - 1) * 0.5f);
        float maxR = SIZE * 0.5f;

        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), ctr) / maxR; // 0 centre -> 1 edge
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smoothstep for a soft, glow-like falloff
                px[y * SIZE + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        _softGlow = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE),
                                  new Vector2(0.5f, 0.5f), SIZE);
        _softGlow.name = "ParfumerAuraGlow";
        return _softGlow;
    }
}


