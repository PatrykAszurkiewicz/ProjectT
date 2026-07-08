using UnityEngine;
#if USE_URP_2D_LIGHTS
using UnityEngine.Rendering.Universal;
#endif

// Light for projectiles, aiming reticles and explosions. Designed so things stay readable in dark biomes without needing any art or scene setup.
//   Additive soft-disc halo sprite that "adds" brightness over the
//     darkened biome, so the projectile reads as a glowing tracer regardless of
//     render pipeline (works on Built-in AND URP). Procedurally generated, cached.
//   URP 2D point Light2D that actually illuminates the
//     surroundings. This is gated behind the USE_URP_2D_LIGHTS scripting define
//     so the file compiles everywhere. If you're on the URP 2D renderer, add
//     USE_URP_2D_LIGHTS under Project Settings ▸ Player ▸ Scripting Define
//     Symbols and every glow below also casts a soft real light — no other change needed.
// USAGE
//   var glow = ProjectileGlow.Attach(transform, color, worldRadius);   // tracer
//   glow.SetColor(newColor);                                           // e.g. boomerang return trip
//   ProjectileGlow.Flash(worldPos, color, worldRadius);                // explosion blink
[DisallowMultipleComponent]
public class ProjectileGlow : MonoBehaviour
{
    // Sits just under the 2498–2500 projectile-art band so the halo reads as a
    // glow *behind* the sprite, still well above the grass Y-sort range (400–1600).
    public const int DefaultGlowSortingOrder = 2497;
    private const string DefaultSortingLayer = "Default";

    private SpriteRenderer _halo;
    private Color _color = Color.white;
    private float _worldRadius = 0.5f;
    private float _baseAlpha = 0.5f;
    private bool _pulse = true;
    private float _pulseSpeed = 8f;
    private float _pulseAmount = 0.15f;
    private float _seed;

    // NightOverlay integration 
    // The additive halo alone is drawn UNDER the full-screen NightOverlay quad
    // (overlay shader is Queue "Overlay+100" = 4100; sprites/additive live at
    // 3000), so at night the overlay paints over it and crushes it to ~12% with
    // darkness 0.92 — i.e. nearly invisible, and fully invisible in PitchBlack /
    // behind the player in Corruption's directional mode. The halo contributes
    // nothing to the shader's totalLight, so the darkness never opens up for it.
    //
    // The fix the codebase already provides: register a NightLight so this effect
    // adds to _ExtraLightData and the overlay carves out illumination around it
    // (works in directional/Corruption mode too — extra lights are summed
    // independent of the directional branch). Night-only: skipped when no
    // NightOverlay is active, so day biomes add no component and no per-frame cost.
    private NightLight _nightLight;
    private bool _registerNightLight = true;
    private bool _countedNightLight;             // did this instance take a budget slot?
    private float _nightLightRadius = -1f;       // <0 → auto from halo radius
    private float _nightLightIntensity = 0.55f;

    // Performance switches 
    // Master on/off for the whole effect (halos + lights). Set false to A/B
    // against the original look.
    public static bool EnableGlow = true;

    // Toggles ONLY the real NightOverlay lights (the per-pixel full-screen
    // shader cost). Halos stay. Use this to confirm the night-light loop is the
    // bottleneck — if FPS recovers with this false, it was the light count.
    public static bool EnableNightLights = true;

    // The NightOverlay shader loops over every registered light for every screen
    // pixel every frame, so the cost scales with the live light count. Projectiles
    // are numerous and transient, so we cap how many may hold a real light at once;
    // the rest show the (free) additive halo only. Raise for richer night lighting,
    // lower if night frame-rate suffers. Persistent lights (balloons, gunge) and
    // the aim reticle are NOT counted against this budget.
    public static int MaxConcurrentProjectileNightLights = 6;
    private static int _activeProjectileNightLights = 0;

#if USE_URP_2D_LIGHTS
    private Light2D _light;
    private float _lightBaseIntensity = 1f;
#endif

    // Attach a persistent tracer glow as a child of `parent`.
    //   registerNightLight  — also punch a hole in the NightOverlay so the tracer
    //                          is visible in dark biomes (auto no-op in daylight).
    //   nightLightRadius     — world radius of that hole (<0 → auto = halo * 2.4).
    //   nightLightIntensity  — 0..~1; how strongly it reveals the darkness.
    public static ProjectileGlow Attach(Transform parent, Color color, float worldRadius,
                                        float alpha = 0.5f, bool pulse = true,
                                        float pulseSpeed = 8f, float pulseAmount = 0.15f,
                                        int sortingOrder = DefaultGlowSortingOrder,
                                        string sortingLayer = DefaultSortingLayer,
                                        bool registerNightLight = true,
                                        float nightLightRadius = -1f,
                                        float nightLightIntensity = 0.55f)
    {
        if (!EnableGlow) return null;

        var go = new GameObject("Glow");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        var glow = go.AddComponent<ProjectileGlow>();
        glow._color = color;
        glow._worldRadius = Mathf.Max(0.05f, worldRadius);
        glow._baseAlpha = Mathf.Clamp01(alpha);
        glow._pulse = pulse;
        glow._pulseSpeed = pulseSpeed;
        glow._pulseAmount = pulseAmount;
        glow._seed = Random.value * 10f;
        glow._registerNightLight = registerNightLight;
        glow._nightLightRadius = nightLightRadius;
        glow._nightLightIntensity = nightLightIntensity;
        glow.Build(sortingOrder, sortingLayer);
        return glow;
    }

    private void Build(int sortingOrder, string sortingLayer)
    {
        _halo = gameObject.AddComponent<SpriteRenderer>();
        _halo.sprite = SoftDisc();
        _halo.sharedMaterial = SharedAdditiveMaterial;
        if (!string.IsNullOrEmpty(sortingLayer)) _halo.sortingLayerName = sortingLayer;
        _halo.sortingOrder = sortingOrder;
        transform.localScale = Vector3.one * (_worldRadius * 2f);
        ApplyColor(_baseAlpha);

        TryAddNightLight();

#if USE_URP_2D_LIGHTS
        _light = gameObject.AddComponent<Light2D>();
        _light.lightType = Light2D.LightType.Point;
        _light.color = _color;
        _light.pointLightInnerRadius = _worldRadius * 0.4f;
        _light.pointLightOuterRadius = _worldRadius * 3.2f;
        _lightBaseIntensity = 0.8f;
        _light.intensity = _lightBaseIntensity;
        // Light2D ignores the SpriteRenderer scale, so set radii directly above.
#endif
    }

    // Registers a NightLight so the tracer reveals itself through the NightOverlay.
    // Only while a NightOverlay is active — in daylight this adds nothing. The
    // NightLight sits on this same (glow) child and follows the projectile, since
    // NightLight pushes transform.position into its handle every frame.
    private void TryAddNightLight()
    {
        if (!_registerNightLight) return;
        if (!EnableNightLights) return;              // perf switch / A-B test
        if (NightOverlay.Instance == null) return;   // day biome → no cost

        // Budget: don't let transient projectiles flood the full-screen light
        // loop. Past the cap, the projectile keeps its (free) additive halo only.
        if (_activeProjectileNightLights >= MaxConcurrentProjectileNightLights) return;

        float r = _nightLightRadius > 0f ? _nightLightRadius : _worldRadius * 2.4f;

        _nightLight = gameObject.AddComponent<NightLight>();
        _nightLight.radius = r;
        _nightLight.intensity = _nightLightIntensity;
        _nightLight.lightColor = _color;
        _nightLight.warmTintStrength = 0.25f;
        _nightLight.flickerSpeed = 0f;
        _nightLight.flickerAmount = 0f;
        _nightLight.fadeInDuration = 0f;

        _activeProjectileNightLights++;
        _countedNightLight = true;
    }

    private void OnDestroy()
    {
        if (_countedNightLight)
        {
            _activeProjectileNightLights--;
            if (_activeProjectileNightLights < 0) _activeProjectileNightLights = 0;
        }
    }

    public void SetColor(Color color)
    {
        _color = color;
        if (_halo != null) ApplyColor(_halo.color.a);
        if (_nightLight != null) _nightLight.lightColor = color;
#if USE_URP_2D_LIGHTS
        if (_light != null) _light.color = color;
#endif
    }

    public void SetRadius(float worldRadius)
    {
        _worldRadius = Mathf.Max(0.05f, worldRadius);
        transform.localScale = Vector3.one * (_worldRadius * 2f);
#if USE_URP_2D_LIGHTS
        if (_light != null)
        {
            _light.pointLightInnerRadius = _worldRadius * 0.4f;
            _light.pointLightOuterRadius = _worldRadius * 3.2f;
        }
#endif
    }

    public void SetBaseAlpha(float alpha)
    {
        _baseAlpha = Mathf.Clamp01(alpha);
    }

    private void ApplyColor(float alpha)
    {
        Color c = _color; c.a = alpha;
        _halo.color = c;
    }

    private void LateUpdate()
    {
        if (!_pulse) return;

        // Unscaled so tracers keep shimmering even if the game is paused/slowed.
        float pulse = 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime + _seed) * _pulseSpeed);
        float a = Mathf.Clamp01(_baseAlpha * (1f - _pulseAmount + _pulseAmount * 2f * pulse));
        if (_halo != null) ApplyColor(a);
#if USE_URP_2D_LIGHTS
        if (_light != null)
            _light.intensity = _lightBaseIntensity * (1f - _pulseAmount + _pulseAmount * 2f * pulse);
#endif
    }

    // One-shot explosion blink
    // A sharp additive light pop at a world position that flares bright then
    // decays in a fraction of a second, and cleans itself up.
    public static void Flash(Vector3 worldPos, Color color, float worldRadius,
                             float duration = 0.22f, float peakAlpha = 0.95f,
                             int sortingOrder = 5700, string sortingLayer = DefaultSortingLayer)
    {
        var go = new GameObject("GlowFlash");
        go.transform.position = worldPos;
        var flash = go.AddComponent<GlowFlash>();
        flash.Play(color, Mathf.Max(0.1f, worldRadius), duration,
                   Mathf.Clamp01(peakAlpha), sortingOrder, sortingLayer);
    }

    //  Shared additive material (pipeline-agnostic) 
    private static Material _additiveMat;
    public static Material SharedAdditiveMaterial
    {
        get
        {
            if (_additiveMat != null) return _additiveMat;
            // Prefer a genuine additive shader so the glow brightens dark ground.
            Shader s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s == null) s = Shader.Find("Particles/Additive");
            if (s == null) s = Shader.Find("Mobile/Particles/Additive");
            if (s == null) s = Shader.Find("Sprites/Default"); // alpha-blend fallback
            _additiveMat = new Material(s) { name = "ProjectileGlowAdditive" };
            return _additiveMat;
        }
    }

    //  Cached soft radial disc sprite (white, alpha falloff to the edge) 
    private static Sprite _softDisc;
    public static Sprite SoftDisc()
    {
        if (_softDisc != null) return _softDisc;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f;
        float maxD = c;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / maxD; // 0..1
                // Smooth core-bright → soft edge falloff.
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);  // smoothstep
                a = a * a;                  // bias brightness toward the centre
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        _softDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        _softDisc.name = "ProjectileGlowSoftDisc";
        return _softDisc;
    }
}

// Tiny self-destructing component that drives the explosion blink.
[DisallowMultipleComponent]
public class GlowFlash : MonoBehaviour
{
    private SpriteRenderer _sr;
    private float _radius;
    private float _duration;
    private float _peakAlpha;
    private float _age;
    private Color _color;

    // Real light blink through the NightOverlay (night-only). Driven by the same
    // decay curve as the additive flash so the darkness flares and settles.
    private NightLight _nightLight;
    private float _nightLightPeak = 1.2f;

#if USE_URP_2D_LIGHTS
    private UnityEngine.Rendering.Universal.Light2D _light;
    private float _lightPeak = 2.0f;
#endif

    public void Play(Color color, float radius, float duration, float peakAlpha,
                     int sortingOrder, string sortingLayer)
    {
        _color = color;
        _radius = radius;
        _duration = Mathf.Max(0.05f, duration);
        _peakAlpha = peakAlpha;

        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = ProjectileGlow.SoftDisc();
        _sr.sharedMaterial = ProjectileGlow.SharedAdditiveMaterial;
        if (!string.IsNullOrEmpty(sortingLayer)) _sr.sortingLayerName = sortingLayer;
        _sr.sortingOrder = sortingOrder;
        transform.localScale = Vector3.one * (_radius * 1.4f);
        _sr.color = new Color(color.r, color.g, color.b, peakAlpha);

        // Night-only: punch a bright, brief hole in the NightOverlay so the
        // detonation actually lights up the surrounding darkness.
        if (ProjectileGlow.EnableNightLights && NightOverlay.Instance != null)
        {
            _nightLight = gameObject.AddComponent<NightLight>();
            _nightLight.radius = _radius * 1.8f;
            _nightLight.intensity = 0f;            // driven by the curve in Update
            _nightLight.lightColor = color;
            _nightLight.warmTintStrength = 0.3f;
            _nightLight.fadeInDuration = 0f;
        }

#if USE_URP_2D_LIGHTS
        _light = gameObject.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
        _light.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point;
        _light.color = color;
        _light.pointLightInnerRadius = _radius * 0.5f;
        _light.pointLightOuterRadius = _radius * 3.5f;
        _light.intensity = _lightPeak;
#endif
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float p = Mathf.Clamp01(_age / _duration);

        // Sharp rise, quick decay → reads as a punchy blink.
        float curve = p < 0.15f
            ? Mathf.Lerp(0.6f, 1f, p / 0.15f)
            : 1f - ((p - 0.15f) / 0.85f);
        curve = Mathf.Clamp01(curve);

        if (_sr != null)
        {
            transform.localScale = Vector3.one * (_radius * Mathf.Lerp(1.2f, 2.0f, p));
            Color c = _color; c.a = _peakAlpha * curve; _sr.color = c;
        }
        if (_nightLight != null) _nightLight.intensity = _nightLightPeak * curve;
#if USE_URP_2D_LIGHTS
        if (_light != null) _light.intensity = _lightPeak * curve;
#endif

        if (p >= 1f) Destroy(gameObject);
    }
}
