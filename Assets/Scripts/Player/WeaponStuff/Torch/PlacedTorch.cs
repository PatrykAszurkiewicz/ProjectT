using UnityEngine;
using System.Collections.Generic;

// A torch dropped on the map by the Torch tool
public class PlacedTorch : MonoBehaviour
{
    // Global registry — keeps the "max N on the map" invariant across tool swaps.
    private static readonly List<PlacedTorch> Active = new List<PlacedTorch>();


    private float lightRadius;
    private float lightIntensity;
    private float flickerSpeed;
    private float flickerAmount;   // fraction of radius/intensity the flicker swings
    private Color lightColor;
    private int maxActive = 3;

    //  Visuals 
    private SpriteRenderer bodyRenderer;     // the torch.png itself
    private SpriteRenderer haloRenderer;     // soft ground halo for readability
    private SpriteRenderer flameGlowRenderer; // additive warm glow at the flame head
    private float flameHeadLocalY;           // local-space Y of the flame head
    private float targetWorldHeight = 1.3f;  // how tall the placed torch is, in world units

    //  Night light handle 
    private NightOverlay.NightLightHandle lightHandle;

    //  Animation state 
    private float spawnScale = 0f;
    private bool vanishing = false;
    private float vanishTimer = 0f;
    private const float VANISH_DURATION = 0.6f;
    private float flickerSeed;

    private const float SORT_PRECISION = 10f;
    private const int SORT_ORDER_BASE = 1000;

    // Spawn timer for occasional embers
    private float emberTimer;

    // SPAWN
    public static PlacedTorch Spawn(Vector3 position, Sprite bodySprite,
                                    float lightRadius, float lightIntensity, Color lightColor,
                                    float flickerSpeed, float flickerAmount, int maxActive)
    {
        GameObject go = new GameObject("PlacedTorch");
        go.transform.position = position;
        go.layer = LayerMask.NameToLayer("Default");

        PlacedTorch torch = go.AddComponent<PlacedTorch>();
        torch.Initialize(bodySprite, lightRadius, lightIntensity, lightColor,
                         flickerSpeed, flickerAmount, maxActive);
        return torch;
    }

    private void Initialize(Sprite bodySprite, float lightRadius, float lightIntensity, Color lightColor,
                            float flickerSpeed, float flickerAmount, int maxActive)
    {
        this.lightRadius = lightRadius > 0f ? lightRadius : 5f;
        this.lightIntensity = lightIntensity > 0f ? lightIntensity : 1f;
        this.lightColor = lightColor;
        this.flickerSpeed = flickerSpeed > 0f ? flickerSpeed : 6f;
        this.flickerAmount = Mathf.Clamp01(flickerAmount);
        this.maxActive = Mathf.Max(1, maxActive);
        this.flickerSeed = Random.value * 100f;

        BuildVisual(bodySprite);
        TryRegisterLight();

        // Register in the global list and enforce the cap.
        Active.Add(this);
        EnforceCap();
    }

    private void EnforceCap()
    {
        // Remove any dead entries first (defensive).
        Active.RemoveAll(t => t == null);

        // Count only torches that aren't already on their way out.
        int liveCount = 0;
        foreach (var t in Active)
            if (t != null && !t.vanishing) liveCount++;

        // Vanish oldest live torches until we're back at the cap.
        if (liveCount > maxActive)
        {
            int toRemove = liveCount - maxActive;
            for (int i = 0; i < Active.Count && toRemove > 0; i++)
            {
                if (Active[i] != null && !Active[i].vanishing && Active[i] != this)
                {
                    Active[i].BeginVanish();
                    toRemove--;
                }
            }
        }
    }

    // UPDATE
    private void Update()
    {
        // Pop-in scale (overshoot ease-out-back), unless we're vanishing.
        if (!vanishing && spawnScale < 1f)
        {
            spawnScale = Mathf.Min(spawnScale + Time.deltaTime / 0.22f, 1f);
            float p = spawnScale - 1f;
            float ease = 1f + 2.70158f * p * p * p + 1.70158f * p * p;
            transform.localScale = Vector3.one * ease;
        }

        // Light may not have existed at spawn (e.g. placed during the day). Keep
        // trying so the torch lights up the moment night falls.
        if (lightHandle == null) TryRegisterLight();

        //  Flame flicker 
        // Layered noise: a fast Perlin wobble + a slower sine breath. Looks like
        // a living flame rather than a uniform pulse.
        float t = Time.time;
        float noise = Mathf.PerlinNoise(flickerSeed, t * flickerSpeed) * 2f - 1f;       // -1..1
        float breath = Mathf.Sin(t * flickerSpeed * 0.45f + flickerSeed);                // -1..1
        float flicker = (noise * 0.7f + breath * 0.3f) * flickerAmount;                  // small swing

        // Vanish fade overrides everything.
        float fade = 1f;
        if (vanishing)
        {
            vanishTimer += Time.deltaTime;
            float v = vanishTimer / VANISH_DURATION;
            if (v >= 1f) { Destroy(gameObject); return; }

            // Shrink + sink slightly + fade.
            fade = 1f - v * v;
            float shrink = (1f - v) * (1f + 0.06f * Mathf.Sin(v * 24f));
            transform.localScale = Vector3.one * Mathf.Max(shrink, 0f);
        }

        // Drive the flame glow renderer.
        if (flameGlowRenderer != null)
        {
            float glowScale = (1.0f + flicker * 2.2f);
            flameGlowRenderer.transform.localScale = Vector3.one * (targetWorldHeight * 0.85f) * glowScale;

            Color gc = new Color(1f, 0.72f, 0.32f, (0.55f + flicker * 1.4f) * fade);
            flameGlowRenderer.color = gc;
        }

        // Subtle warm brightness flicker on the torch body itself.
        if (bodyRenderer != null)
        {
            float b = 1f + flicker * 0.6f;
            bodyRenderer.color = new Color(b, b * 0.97f, b * 0.92f, fade);
        }

        if (haloRenderer != null)
        {
            Color hc = haloRenderer.color;
            hc.a = (0.18f + flicker * 0.6f) * fade;
            haloRenderer.color = hc;
        }

        // Drive the night light: flicker radius + intensity together.
        if (lightHandle != null)
        {
            Vector3 headWorld = transform.TransformPoint(new Vector3(0f, flameHeadLocalY, 0f));
            lightHandle.position = headWorld;
            lightHandle.radius = lightRadius * (1f + flicker) * (vanishing ? Mathf.Max(0f, 1f - vanishTimer / VANISH_DURATION) : 1f);
            lightHandle.intensity = lightIntensity * (1f + flicker) * fade;
            lightHandle.color = lightColor;
        }

        // Occasional rising embers for flavour.
        if (!vanishing)
        {
            emberTimer -= Time.deltaTime;
            if (emberTimer <= 0f)
            {
                emberTimer = Random.Range(0.12f, 0.28f);
                SpawnEmber();
            }
        }

        // Y-sort so the torch occludes correctly against grass/units.
        float sortY = transform.position.y - targetWorldHeight * 0.5f;
        int order = SORT_ORDER_BASE + Mathf.RoundToInt(-sortY * SORT_PRECISION);
        if (haloRenderer != null) haloRenderer.sortingOrder = order - 2;
        if (bodyRenderer != null) bodyRenderer.sortingOrder = order;
        if (flameGlowRenderer != null) flameGlowRenderer.sortingOrder = order + 2;
    }

    public void BeginVanish()
    {
        if (vanishing) return;
        vanishing = true;
        vanishTimer = 0f;
    }

    // Smoothly remove every torch currently on the map.
    public static void VanishAll()
    {
        foreach (var t in Active)
            if (t != null) t.BeginVanish();
    }

    private void TryRegisterLight()
    {
        if (lightHandle != null) return;
        Vector3 headWorld = transform.TransformPoint(new Vector3(0f, flameHeadLocalY, 0f));
        lightHandle = NightOverlay.RegisterLight(
            position: headWorld,
            radius: lightRadius,
            intensity: lightIntensity,
            color: lightColor,
            warmTintStrength: 0.5f);
    }

    private void OnDestroy()
    {
        if (lightHandle != null)
        {
            NightOverlay.UnregisterLight(lightHandle);
            lightHandle = null;
        }
        Active.Remove(this);
    }

    // VISUALS
    private void BuildVisual(Sprite bodySprite)
    {
        if (bodySprite == null)
            bodySprite = Resources.Load<Sprite>("Sprites/torch");

        // Ground halo — keeps the torch readable on any background even in daylight.
        GameObject haloObj = new GameObject("TorchHalo");
        haloObj.transform.SetParent(transform, false);
        haloObj.transform.localPosition = new Vector3(0f, -targetWorldHeight * 0.45f, 0f);
        haloRenderer = haloObj.AddComponent<SpriteRenderer>();
        haloRenderer.sprite = GenerateSoftCircleSprite();
        haloRenderer.color = new Color(1f, 0.7f, 0.3f, 0.18f);
        haloObj.transform.localScale = Vector3.one * (targetWorldHeight * 0.9f);

        // Torch body.
        GameObject bodyObj = new GameObject("TorchBody");
        bodyObj.transform.SetParent(transform, false);
        bodyRenderer = bodyObj.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = bodySprite;

        float bodyScale = 1f;
        if (bodySprite != null && bodySprite.bounds.size.y > 0.0001f)
            bodyScale = targetWorldHeight / bodySprite.bounds.size.y;
        bodyObj.transform.localScale = Vector3.one * bodyScale;

        // Flame head ≈ top 35% of the sprite. With a centred pivot the top edge
        // is at +halfHeight; the flame's visual centre sits a bit below that.
        flameHeadLocalY = targetWorldHeight * 0.34f;

        // Warm additive glow sitting on the flame.
        GameObject glowObj = new GameObject("TorchFlameGlow");
        glowObj.transform.SetParent(transform, false);
        glowObj.transform.localPosition = new Vector3(0f, flameHeadLocalY, 0f);
        flameGlowRenderer = glowObj.AddComponent<SpriteRenderer>();
        flameGlowRenderer.sprite = GenerateSoftCircleSprite();
        flameGlowRenderer.color = new Color(1f, 0.72f, 0.32f, 0.55f);
        // Additive-ish: use the default sprite material but a bright warm colour;
        // if you have an additive sprite material, assign it here for a stronger
        // bloom (e.g. flameGlowRenderer.sharedMaterial = yourAdditiveMat;).
        glowObj.transform.localScale = Vector3.one * (targetWorldHeight * 0.85f);
    }

    private void SpawnEmber()
    {
        GameObject ember = new GameObject("TorchEmber");
        Vector3 headWorld = transform.TransformPoint(
            new Vector3(Random.Range(-0.08f, 0.08f), flameHeadLocalY, 0f));
        ember.transform.position = headWorld;

        SpriteRenderer sr = ember.AddComponent<SpriteRenderer>();
        sr.sprite = GenerateSoftCircleSprite();
        sr.color = new Color(1f, Random.Range(0.55f, 0.8f), 0.2f, 0.9f);
        float startY = transform.position.y - targetWorldHeight * 0.5f;
        sr.sortingOrder = SORT_ORDER_BASE + Mathf.RoundToInt(-startY * SORT_PRECISION) + 3;

        ember.AddComponent<TorchEmber>().Initialize(
            Random.Range(0.06f, 0.12f),
            Random.Range(0.5f, 1.1f),
            Random.Range(0.4f, 0.9f));
    }

    //  Procedural soft circle (shared, cached) 
    private static Sprite _softCircle;
    private static Sprite GenerateSoftCircleSprite()
    {
        if (_softCircle != null) return _softCircle;
        const int SIZE = 64;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[SIZE * SIZE];
        Vector2 c = Vector2.one * (SIZE * 0.5f);
        float maxR = SIZE * 0.5f;
        for (int y = 0; y < SIZE; y++)
            for (int x = 0; x < SIZE; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / maxR;
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // soft falloff
                px[y * SIZE + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _softCircle = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), Vector2.one * 0.5f, SIZE);
        return _softCircle;
    }
}

// A single rising, fading ember spat out by a placed torch.
public class TorchEmber : MonoBehaviour
{
    private float size;
    private float riseSpeed;
    private float lifetime;
    private float maxLifetime;
    private float drift;
    private SpriteRenderer sr;

    public void Initialize(float size, float riseSpeed, float lifetime)
    {
        this.size = size;
        this.riseSpeed = riseSpeed;
        this.lifetime = lifetime;
        this.maxLifetime = lifetime;
        this.drift = Random.Range(-0.3f, 0.3f);
        sr = GetComponent<SpriteRenderer>();
        transform.localScale = Vector3.one * size;
    }

    private void Update()
    {
        lifetime -= Time.deltaTime;
        if (lifetime <= 0f) { Destroy(gameObject); return; }

        transform.position += new Vector3(drift * Time.deltaTime, riseSpeed * Time.deltaTime, 0f);

        float t = 1f - lifetime / maxLifetime;
        transform.localScale = Vector3.one * Mathf.Lerp(size, size * 0.2f, t);
        if (sr != null)
        {
            Color c = sr.color;
            c.a = 1f - t;
            sr.color = c;
        }
    }
}
