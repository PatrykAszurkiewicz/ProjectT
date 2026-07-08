using UnityEngine;
using System.Collections.Generic;


// Spawns procedurally medieval balloons that drift over the map
// Each balloon carries a NightLight that illuminates the ground below


public class NightBalloonController : MonoBehaviour
{
    [Header("Spawning")]
    [Tooltip("How many balloons can exist simultaneously in the sky.")]
    public int maxBalloons = 3;

    [Tooltip("Average seconds between spawn attempts. With some randomness (±50%).")]
    public float spawnInterval = 8f;

    [Tooltip("Spawn balloons outside this radius from the map center.")]
    public float spawnRadius = 35f;

    [Tooltip("Balloons' flight target will be offset from center by at least this much, " +
             "so they pass NEAR the core without flying directly over it.")]
    public float minCoreDistance = 3f;

    [Tooltip("Balloons' flight target will be offset from center by at most this much.")]
    public float maxCoreDistance = 12f;

    [Header("Flight")]
    [Tooltip("Drift speed in world units per second.")]
    public float flightSpeed = 4f;

    [Tooltip("Amplitude of the gentle vertical bobbing motion.")]
    public float bobAmplitude = 0.25f;

    [Tooltip("Frequency of the bobbing (Hz).")]
    public float bobFrequency = 0.4f;

    [Header("Appearance")]
    [Tooltip("Base visual scale. 1 = roughly player-sized.")]
    public float balloonScale = 1.0f;

    [Tooltip("Random scale variation around baseScale (0.15 = ±15%).")]
    [Range(0f, 0.5f)]
    public float scaleVariation = 0.15f;

    [Tooltip("Sorting order for balloon sprites. Should be above the NightOverlay (6000) " +
             "so balloons remain visible through the darkness — they're sky objects.")]
    public int sortingOrder = 7000;

    [Header("Light")]
    [Tooltip("Radius of the light cone on the ground.")]
    public float lightRadius = 4f;

    [Tooltip("Brightness of each balloon's lantern (0-1, higher = brighter).")]
    [Range(0f, 2f)]
    public float lightIntensity = 1.0f;

    [Tooltip("Warm lantern tint.")]
    public Color lightColor = new Color(1.0f, 0.92f, 0.75f, 1f);

    [Tooltip("How strongly the lantern color tints the night darkness around it.")]
    [Range(0f, 1f)]
    public float warmTintStrength = 0.35f;

    [Tooltip("Flicker speed of the lantern (Perlin-noise units, 0 = steady).")]
    public float flickerSpeed = 1.2f;

    [Tooltip("How strongly the light radius wavers due to flicker.")]
    [Range(0f, 0.5f)]
    public float flickerAmount = 0.08f;

    [Header("Light Sweep (Searchlight Beam)")]
    [Tooltip("When enabled, balloons cast a directional light beam towards the ground that slowly " +
             "sweeps around — looks like a searchlight. When disabled, balloons emit only a soft " +
             "radial lantern glow.")]
    public bool enableLightSweep = true;

    [Tooltip("Length of the beam from the balloon down to the ground (world units). " +
             "Also determines how far from the balloon the sweep spot lands.")]
    public float sweepBeamLength = 3.5f;

    [Tooltip("How fast the beam sweeps (rotation speed in degrees per second). " +
             "Positive = clockwise, negative = counter-clockwise. Low values look like a searchlight.")]
    public float sweepSpeed = 20f;

    [Tooltip("Total arc in degrees that the beam sweeps back and forth. " +
             "360 = full rotation, 90 = quarter circle pendulum.")]
    [Range(30f, 360f)]
    public float sweepArc = 100f;

    [Tooltip("Width of the cone at the ground (world units).")]
    public float sweepBeamWidth = 1.8f;

    [Tooltip("Opacity of the visible beam cone sprite.")]
    [Range(0f, 1f)]
    public float sweepBeamOpacity = 0.75f;

    [Tooltip("Opacity of the bright spot on the ground where the beam hits.")]
    [Range(0f, 1f)]
    public float sweepGroundSpotOpacity = 0.85f;


    private readonly List<BalloonInstance> balloons = new List<BalloonInstance>();
    private GameObject containerGO;
    private float nextSpawnTime;

    // Cached procedural sprites (shared across all balloons)
    private static Sprite _envelopeSprite;
    private static Sprite _basketSprite;
    private static Sprite _shadowSprite;
    private static Sprite _beamSprite;
    private static Sprite _groundSpotSprite;

    // Shared additive material for beam + ground spot
    private static Material _additiveMat;


    public void GenerateBalloons()
    {
        CleanupBalloons();

        containerGO = new GameObject("NightBalloons_Container");
        containerGO.transform.SetParent(transform, false);

        EnsureSprites();

        // Stagger the first spawn so the sky doesn't pop in full at once
        nextSpawnTime = Time.time + Random.Range(0.5f, 2f);

        //Debug.Log($"[NightBalloons] Controller activated — will spawn up to {maxBalloons} balloons " +
        //          $"starting in ~{nextSpawnTime - Time.time:F1}s. Night active: {NightOverlay.Instance != null}");
    }

    public void CleanupBalloons()
    {
        foreach (var b in balloons)
        {
            if (b == null) continue;
            if (b.light != null) Destroy(b.light.gameObject); // destroy the light host object
            if (b.groundSpotGO != null) Destroy(b.groundSpotGO);
            if (b.root != null) Destroy(b.root);
        }
        balloons.Clear();

        if (containerGO != null)
        {
            if (Application.isPlaying) Destroy(containerGO);
            else DestroyImmediate(containerGO);
            containerGO = null;
        }
    }

    void OnDestroy()
    {
        CleanupBalloons();
    }


    void Update()
    {
        if (containerGO == null) return;

        // Spawn new balloons up to the max
        if (balloons.Count < maxBalloons && Time.time >= nextSpawnTime)
        {
            SpawnBalloon();
            nextSpawnTime = Time.time + spawnInterval * Random.Range(0.5f, 1.5f);
        }

        // Update existing balloons; collect dead ones
        for (int i = balloons.Count - 1; i >= 0; i--)
        {
            var b = balloons[i];
            if (!UpdateBalloon(b))
            {
                // Balloon finished its traversal — despawn all its parts
                if (b.light != null) Destroy(b.light.gameObject);
                if (b.groundSpotGO != null) Destroy(b.groundSpotGO);
                if (b.root != null) Destroy(b.root);
                balloons.RemoveAt(i);
            }
        }
    }

    //  Spawning
    private void SpawnBalloon()
    {
        // Pick a random entry angle, come in from the edge.
        float entryAngle = Random.Range(0f, Mathf.PI * 2f);
        Vector2 entryPos = new Vector2(Mathf.Cos(entryAngle), Mathf.Sin(entryAngle)) * spawnRadius;

        // Pick a target that passes NEAR the core but not through it.
        float targetAngle = Random.Range(0f, Mathf.PI * 2f);
        float targetDist = Random.Range(minCoreDistance, maxCoreDistance);
        Vector2 targetNear = new Vector2(Mathf.Cos(targetAngle), Mathf.Sin(targetAngle)) * targetDist;

        // Exit point on opposite side (roughly) — so the balloon flies past the core
        Vector2 exitDir = (targetNear - entryPos).normalized;
        Vector2 exitPos = targetNear + exitDir * spawnRadius;

        // Build the balloon
        GameObject root = new GameObject("NightBalloon");
        root.transform.SetParent(containerGO.transform, false);
        root.transform.position = entryPos;

        float scale = balloonScale * Random.Range(1f - scaleVariation, 1f + scaleVariation);
        root.transform.localScale = Vector3.one * scale;

        BuildBalloonVisuals(root);

        // Attach NightLight — the core of the illumination. We put it on a separate
        // child GameObject so that when sweep is enabled we can move the light to the
        // sweep target on the ground without moving the balloon itself.
        GameObject lightHost = new GameObject("BalloonLight");
        lightHost.transform.SetParent(root.transform, false);
        lightHost.transform.localPosition = Vector3.zero;

        NightLight light = lightHost.AddComponent<NightLight>();
        light.radius = lightRadius;
        light.intensity = lightIntensity;
        light.lightColor = lightColor;
        light.warmTintStrength = warmTintStrength;
        light.flickerSpeed = flickerSpeed;
        light.flickerAmount = flickerAmount;
        light.fadeInDuration = 1.2f; // smooth fade-in as it appears

        var instance = new BalloonInstance
        {
            root = root,
            light = light,
            startPos = entryPos,
            endPos = exitPos,
            bobPhase = Random.Range(0f, Mathf.PI * 2f),
            swayPhase = Random.Range(0f, Mathf.PI * 2f),
            spawnTime = Time.time,
            fadeStart = Time.time
        };

        // Build sweep visuals (beam + ground spot) if enabled
        if (enableLightSweep)
        {
            BuildSweepVisuals(instance);
        }

        balloons.Add(instance);

        //Debug.Log($"[NightBalloons] Spawned at {entryPos} → {exitPos}, " +
        //          $"rootScale={scale:F2}, lightRadius={lightRadius}, lightIntensity={lightIntensity}, " +
        //          $"sweep={(enableLightSweep ? "ON" : "OFF")}, " +
        //          $"NightOverlay.Instance={(NightOverlay.Instance != null ? "present" : "NULL — no lighting!")}, " +
        //          $"total active: {balloons.Count}");
    }

    // Build the beam cone + ground spot sprites for one balloon.
    // The beam is parented to the balloon so it moves with it; the ground spot is
    // world-space so it can sit independently at the sweep target point.
    private void BuildSweepVisuals(BalloonInstance b)
    {
        b.hasSweep = true;

        // Randomize the sweep starting orientation per balloon so multiple balloons
        // don't sweep in sync.
        b.sweepPhase = Random.Range(-sweepArc * 0.5f, sweepArc * 0.5f) * Mathf.Deg2Rad;
        b.sweepDirection = Random.value < 0.5f ? 1f : -1f;
        // Base axis points DOWN from the balloon (-Y), with a bit of random rotation
        // so balloons don't all sweep around the same axis.
        b.sweepBaseAngle = -Mathf.PI * 0.5f + Random.Range(-0.3f, 0.3f);

        // BEAM CONE — attached to balloon basket, pivots at its top (at basket level)
        b.beamGO = new GameObject("SweepBeam");
        b.beamGO.transform.SetParent(b.root.transform, false);
        b.beamGO.transform.localPosition = new Vector3(0f, -0.25f, 0f); // at basket level

        // Beam sprite is 0.96 × 1.6 world units at scale 1. We want the length to be
        // sweepBeamLength and the width at the base to be sweepBeamWidth. The sprite's
        // source height is 1.6u and width is 0.96u. Compute scaling:
        float beamSpriteLengthWorld = 1.6f;
        float beamSpriteWidthWorld = 0.96f;
        // localScale is applied AFTER the root's scale, so we undo the root scale
        // for the beam — the beam length should stay in world units regardless of
        // the balloon's own random scale.
        float rootScale = b.root.transform.localScale.x;
        float lengthScale = (sweepBeamLength / beamSpriteLengthWorld) / rootScale;
        float widthScale = (sweepBeamWidth / beamSpriteWidthWorld) / rootScale;
        b.beamGO.transform.localScale = new Vector3(widthScale, lengthScale, 1f);

        b.beamSR = b.beamGO.AddComponent<SpriteRenderer>();
        b.beamSR.sprite = _beamSprite;
        b.beamSR.sharedMaterial = _additiveMat;
        // Beam color: bias heavily toward white so the shaft looks like hot light,
        // not like colored mist. Lerp 70% toward white from the lantern color.
        Color beamColor = Color.Lerp(lightColor, Color.white, 0.7f);
        b.beamSR.color = new Color(beamColor.r, beamColor.g, beamColor.b, sweepBeamOpacity);
        b.beamSR.sortingLayerName = "Default";
        // Beam renders above the night overlay so it's always visible through darkness
        b.beamSR.sortingOrder = sortingOrder + 2;

        // GROUND SPOT — NOT parented; lives in world space at the sweep target.
        // Sized 2.2× the beam width so the soft radial spot extends well beyond the
        // beam's edge, blending the beam tip into a diffuse pool of light rather than
        // looking like a circle stuck onto the end of a line.
        b.groundSpotGO = new GameObject("SweepGroundSpot");
        b.groundSpotGO.transform.SetParent(containerGO.transform, false);
        b.groundSpotGO.transform.localScale = Vector3.one * (sweepBeamWidth * 2.2f);

        b.groundSpotSR = b.groundSpotGO.AddComponent<SpriteRenderer>();
        b.groundSpotSR.sprite = _groundSpotSprite;
        b.groundSpotSR.sharedMaterial = _additiveMat;
        // Ground spot keeps more of the lantern warmth — hot light pools warmly on surfaces
        Color spotColor = Color.Lerp(lightColor, Color.white, 0.3f);
        b.groundSpotSR.color = new Color(spotColor.r, spotColor.g, spotColor.b, sweepGroundSpotOpacity);
        b.groundSpotSR.sortingLayerName = "Default";
        b.groundSpotSR.sortingOrder = sortingOrder + 1;
    }

    // Returns false when the balloon should be despawned (finished its flight).
    private bool UpdateBalloon(BalloonInstance b)
    {
        if (b.root == null) return false;

        float elapsed = Time.time - b.spawnTime;
        float totalDist = Vector2.Distance(b.startPos, b.endPos);
        float travelTime = totalDist / Mathf.Max(0.01f, flightSpeed);
        float t = elapsed / travelTime;

        if (t >= 1f) return false; // done

        // Base position — lerp from entry to exit
        Vector2 basePos = Vector2.Lerp(b.startPos, b.endPos, t);

        // Gentle vertical bobbing + slight lateral sway, so the balloon feels alive
        float bob = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f + b.bobPhase) * bobAmplitude;
        float sway = Mathf.Sin(Time.time * bobFrequency * 0.7f + b.swayPhase) * bobAmplitude * 0.4f;

        Vector3 balloonPos = new Vector3(basePos.x + sway, basePos.y + bob, 0f);
        b.root.transform.position = balloonPos;

        // Fade the balloon sprite(s) in/out near endpoints so it doesn't pop
        float alpha = 1f;
        const float fadeWindow = 0.12f;
        if (t < fadeWindow) alpha = t / fadeWindow;
        else if (t > 1f - fadeWindow) alpha = (1f - t) / fadeWindow;
        alpha = Mathf.Clamp01(alpha);

        foreach (var sr in b.root.GetComponentsInChildren<SpriteRenderer>())
        {
            // Skip the beam sprite — it gets its own alpha handling below so we don't
            // clobber the additive opacity with the balloon body's fade alpha.
            if (sr == b.beamSR) continue;

            Color c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, alpha * (sr.name == "Shadow" ? 0.35f : 1f));
        }

        //  Sweep update 
        if (b.hasSweep && b.beamGO != null)
        {
            // Advance sweep angle. When we hit the arc edges, reverse direction.
            float halfArc = sweepArc * 0.5f * Mathf.Deg2Rad;
            b.sweepPhase += b.sweepDirection * sweepSpeed * Mathf.Deg2Rad * Time.deltaTime;
            if (b.sweepPhase > halfArc) { b.sweepPhase = halfArc; b.sweepDirection = -1f; }
            if (b.sweepPhase < -halfArc) { b.sweepPhase = -halfArc; b.sweepDirection = 1f; }

            // Current beam direction in world space
            float worldAngle = b.sweepBaseAngle + b.sweepPhase;
            Vector2 beamDir = new Vector2(Mathf.Cos(worldAngle), Mathf.Sin(worldAngle));

            // Rotate the beam sprite so its length aligns with beamDir.
            // The sprite has pivot at top-center and points DOWN by default (local -Y),
            // i.e. a default world angle of -90°. We rotate by (worldAngleDeg + 90°).
            float worldAngleDeg = worldAngle * Mathf.Rad2Deg;
            b.beamGO.transform.rotation = Quaternion.Euler(0f, 0f, worldAngleDeg + 90f);

            // Ground spot position — where the beam ends.
            Vector2 spotPos = (Vector2)balloonPos + beamDir * sweepBeamLength;
            if (b.groundSpotGO != null)
            {
                b.groundSpotGO.transform.position = new Vector3(spotPos.x, spotPos.y, 0f);
                // Fade with the balloon
                Color gc = b.groundSpotSR.color;
                b.groundSpotSR.color = new Color(gc.r, gc.g, gc.b, alpha * sweepGroundSpotOpacity);
            }

            // Fade the beam sprite too
            if (b.beamSR != null)
            {
                Color bc = b.beamSR.color;
                b.beamSR.color = new Color(bc.r, bc.g, bc.b, alpha * sweepBeamOpacity);
            }

            // Move the actual NightLight to the sweep target — this is what makes
            // the ground REVEAL through the darkness, not just visually glow.
            if (b.light != null)
            {
                b.light.transform.position = new Vector3(spotPos.x, spotPos.y, 0f);
                b.light.intensity = lightIntensity * alpha;
            }
        }
        else
        {
            // No sweep: light stays at the balloon position, which is the default
            // NightLight behaviour (transform.position is pushed each frame inside
            // the NightLight component).
            if (b.light != null)
            {
                b.light.intensity = lightIntensity * alpha;
            }
        }

        return true;
    }

    //  Procedural balloon visuals

    private void BuildBalloonVisuals(GameObject root)
    {
        // Soft ground shadow — a dim oval below the balloon, drawn at low Z sort
        GameObject shadow = new GameObject("Shadow");
        shadow.transform.SetParent(root.transform, false);
        shadow.transform.localPosition = new Vector3(0f, -0.6f, 0f);
        shadow.transform.localScale = new Vector3(1.1f, 0.35f, 1f);
        SpriteRenderer shSr = shadow.AddComponent<SpriteRenderer>();
        shSr.sprite = _shadowSprite;
        shSr.color = new Color(0f, 0f, 0f, 0.35f);
        shSr.sortingLayerName = "Default";
        shSr.sortingOrder = sortingOrder - 2;

        // Basket — the little wooden box
        GameObject basket = new GameObject("Basket");
        basket.transform.SetParent(root.transform, false);
        basket.transform.localPosition = new Vector3(0f, -0.38f, 0f);
        basket.transform.localScale = new Vector3(0.35f, 0.25f, 1f);
        SpriteRenderer bSr = basket.AddComponent<SpriteRenderer>();
        bSr.sprite = _basketSprite;
        bSr.color = new Color(0.45f, 0.28f, 0.12f, 1f);
        bSr.sortingLayerName = "Default";
        bSr.sortingOrder = sortingOrder;

        // Envelope — the round balloon body
        GameObject envelope = new GameObject("Envelope");
        envelope.transform.SetParent(root.transform, false);
        envelope.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        envelope.transform.localScale = new Vector3(0.8f, 0.95f, 1f);
        SpriteRenderer eSr = envelope.AddComponent<SpriteRenderer>();
        eSr.sprite = _envelopeSprite;
        // Random medieval-feeling balloon color — warm cloth dye tones
        eSr.color = PickMedievalColor();
        eSr.sortingLayerName = "Default";
        eSr.sortingOrder = sortingOrder + 1;
    }

    private Color PickMedievalColor()
    {
        // Hand-picked palette — deep reds, olive, teal, mustard, russet
        Color[] palette = new Color[]
        {
            new Color(0.62f, 0.18f, 0.18f, 1f), // deep red
            new Color(0.50f, 0.38f, 0.14f, 1f), // mustard
            new Color(0.18f, 0.35f, 0.40f, 1f), // dark teal
            new Color(0.42f, 0.22f, 0.12f, 1f), // russet
            new Color(0.30f, 0.35f, 0.20f, 1f), // olive
            new Color(0.55f, 0.30f, 0.45f, 1f), // muted plum
        };
        return palette[Random.Range(0, palette.Length)];
    }

    //  Shared procedural sprites

    private static void EnsureSprites()
    {
        if (_envelopeSprite == null) _envelopeSprite = BuildEnvelopeSprite();
        if (_basketSprite == null) _basketSprite = BuildBasketSprite();
        if (_shadowSprite == null) _shadowSprite = BuildShadowSprite();
        if (_beamSprite == null) _beamSprite = BuildBeamSprite();
        if (_groundSpotSprite == null) _groundSpotSprite = BuildGroundSpotSprite();
        if (_additiveMat == null) _additiveMat = BuildAdditiveMaterial();
    }

    // A simple material for beam/ground-spot sprites, plain alpha-blend (Sprites/Default) with high opacity values 
    private static Material BuildAdditiveMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        return new Material(shader);
    }

    // Cone-shaped beam: narrow at the top (balloon), wide at the bottom (ground).
    private static Sprite BuildBeamSprite()
    {
        int w = 96, h = 160;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[w * h];
        float cx = w * 0.5f;

        for (int y = 0; y < h; y++)
        {
            float ty = y / (float)(h - 1); // 0 bottom, 1 top

            // Cone profile: at top (y=h-1) width is 8% of w, at bottom (y=0) width is 100% of w
            float halfWidthFrac = Mathf.Lerp(0.5f, 0.05f, ty);
            float halfWidth = halfWidthFrac * w;

            // Alpha along the length:
            //  - Near the top (source, ty≈1): full brightness — the beam is strongest at the lamp.
            //  - Near the bottom (ground, ty≈0): fades to near zero — the beam dissipates into
            //    the air before reaching the ground pool. This removes the hard cutoff edge
            //    at the bottom that was making the beam look like a sliced laser.
            //  - Also fade near the very top (ty>0.92) so the beam merges with the basket.
            float lengthAlpha;
            if (ty < 0.18f)
            {
                // Soft fade-out at the ground end — this is the key change.
                lengthAlpha = Mathf.Lerp(0f, 0.7f, ty / 0.18f);
            }
            else if (ty > 0.92f)
            {
                // Gentle fade-in at the source end so the beam doesn't have a hard top line either.
                lengthAlpha = Mathf.Lerp(1f, 0.75f, (ty - 0.92f) / 0.08f);
            }
            else
            {
                // Middle body: ramp smoothly from dim (bottom) to bright (top).
                lengthAlpha = Mathf.Lerp(0.7f, 1f, (ty - 0.18f) / 0.74f);
            }

            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x - cx);
                if (dx > halfWidth) { px[y * w + x] = Color.clear; continue; }

                // Lateral falloff — bright in center, soft edges.
                // Use a gentler exponent (1.2 instead of 2) so the beam disperses laterally
                // rather than looking like a focused laser.
                float lateral = 1f - (dx / halfWidth);
                lateral = Mathf.Pow(lateral, 1.2f);
                float a = lateral * lengthAlpha;
                // Write pure white; runtime SpriteRenderer color tints
                px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        // Pivot at top center (0.5, 1.0) so we rotate around balloon's basket
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1.0f), 100f);
    }

    // A bright soft radial spot — what the beam "hits" on the ground.
    // Similar to _shadowSprite but much softer/brighter falloff profile.
    private static Sprite BuildGroundSpotSprite()
    {
        int s = 128;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[s * s];
        float c = s * 0.5f;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                // Bright hot center, smooth falloff — like the end of a spotlight
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);
                // Hot-spot boost in the inner 30%
                if (d < 0.3f) a = Mathf.Min(1f, a + (0.3f - d) * 0.6f);
                px[y * s + x] = (a > 0.003f) ? new Color(1f, 1f, 1f, a) : Color.clear;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), Vector2.one * 0.5f, 64f);
    }

    // A teardrop/onion-shaped envelope with subtle vertical seam shading.
    private static Sprite BuildEnvelopeSprite()
    {
        int w = 96, h = 128;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] px = new Color[w * h];
        float cx = w * 0.5f;

        for (int y = 0; y < h; y++)
        {
            // Width of the envelope at this Y — widest in the upper middle, tapering to a neck at bottom
            float ty = y / (float)(h - 1); // 0 bottom → 1 top
            // shape: round top, gentle taper to neck at ~0.08
            float halfWidth;
            if (ty > 0.1f)
            {
                // Ellipse-like profile
                float e = (ty - 0.1f) / 0.9f; // 0 at neck → 1 at top
                halfWidth = Mathf.Sin(e * Mathf.PI) * (w * 0.48f);
                // bias fuller at top
                halfWidth *= Mathf.Lerp(0.75f, 1f, Mathf.SmoothStep(0f, 1f, e));
            }
            else
            {
                // Neck pinch at very bottom
                float n = ty / 0.1f;
                halfWidth = Mathf.Lerp(w * 0.08f, w * 0.18f, n);
            }

            for (int x = 0; x < w; x++)
            {
                float dx = x - cx;
                float adx = Mathf.Abs(dx);

                if (adx > halfWidth)
                {
                    px[y * w + x] = Color.clear;
                    continue;
                }

                // Soft edge anti-alias
                float edgeT = 1f - Mathf.Clamp01((halfWidth - adx) / 2.5f);
                float alpha = 1f - edgeT;

                // Vertical seams — 4 subtle dark lines for fabric gores
                float seamShade = 1f;
                float seamFrac = (dx / halfWidth + 1f) * 0.5f; // 0..1 across
                for (int s = 1; s <= 3; s++)
                {
                    float seamPos = s / 4f;
                    float d = Mathf.Abs(seamFrac - seamPos);
                    if (d < 0.02f)
                    {
                        seamShade = Mathf.Min(seamShade, Mathf.Lerp(0.7f, 1f, d / 0.02f));
                    }
                }

                // Shading — brighter on upper-left (light from top-left), darker on right
                float shade = 1f;
                shade *= Mathf.Lerp(0.78f, 1.08f, 1f - (adx / halfWidth));   // center highlight
                shade *= Mathf.Lerp(0.85f, 1.05f, 1f - Mathf.Abs(ty - 0.65f)); // vertical shading
                shade *= seamShade;
                // Slight right-side shadow
                if (dx > 0) shade *= Mathf.Lerp(1f, 0.88f, dx / halfWidth);

                // We write WHITE-ish with shading baked into RGB. The runtime SpriteRenderer color
                // multiplies this by the medieval palette color.
                float v = Mathf.Clamp01(shade);
                px[y * w + x] = new Color(v, v, v, alpha);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.1f), 100f);
    }

    // A small trapezoid basket with horizontal weave stripes.
    private static Sprite BuildBasketSprite()
    {
        int w = 48, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float ty = y / (float)(h - 1); // 0 bottom → 1 top
            // trapezoid — slightly wider at top
            float halfWidth = Mathf.Lerp(w * 0.38f, w * 0.45f, ty);
            for (int x = 0; x < w; x++)
            {
                float dx = x - w * 0.5f;
                float adx = Mathf.Abs(dx);

                if (adx > halfWidth) { px[y * w + x] = Color.clear; continue; }

                float alpha = 1f;
                // weave stripes: darker every 5 pixels
                float shade = (y % 5 < 1) ? 0.72f : 1f;
                // top rim brighter
                if (ty > 0.85f) shade *= 1.15f;
                // side shadow
                if (dx > 0) shade *= Mathf.Lerp(1f, 0.82f, dx / halfWidth);

                shade = Mathf.Clamp01(shade);
                px[y * w + x] = new Color(shade, shade, shade, alpha);

                // Add a couple of vertical rope lines hanging down from the top to the envelope
                if (y > h - 4)
                {
                    if (Mathf.Abs(dx) < 1f && ty > 0.95f)
                        px[y * w + x] = new Color(0.7f, 0.55f, 0.35f, 1f);
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    // Soft elliptical shadow with radial falloff.
    private static Sprite BuildShadowSprite()
    {
        int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[s * s];
        float c = s * 0.5f;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                px[y * s + x] = (a > 0.003f) ? new Color(1f, 1f, 1f, a) : Color.clear;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), Vector2.one * 0.5f, 32f);
    }


    private class BalloonInstance
    {
        public GameObject root;
        public NightLight light;
        public Vector2 startPos;
        public Vector2 endPos;
        public float bobPhase;
        public float swayPhase;
        public float spawnTime;
        public float fadeStart;

        // Light sweep state
        public bool hasSweep;
        public GameObject beamGO;              // cone beam sprite child
        public SpriteRenderer beamSR;
        public GameObject groundSpotGO;        // bright spot child (world-space, not parented)
        public SpriteRenderer groundSpotSR;
        public float sweepPhase;               // radians — current sweep angle
        public float sweepDirection;           // +1 or -1 (pendulum direction)
        public float sweepBaseAngle;           // center axis of this balloon's sweep (radians)
    }
}
