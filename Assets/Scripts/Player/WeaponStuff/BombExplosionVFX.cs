using UnityEngine;
using System.Collections.Generic;


public class BombExplosionVFX : MonoBehaviour
{
    private float explosionRadius;
    private float timer;
    private const float DURATION = 0.7f;

    // Sorting range: 2000-2500 (same band as flamethrower particles)
    private const int SORT_BASE = 2200;

    // Shared material — same as FlamethrowerSystem uses
    private static Material _spriteMat;
    private static Material SpriteMat
    {
        get
        {
            if (_spriteMat == null)
                _spriteMat = new Material(Shader.Find("Sprites/Default"));
            return _spriteMat;
        }
    }

    // Core visuals
    private SpriteRenderer flashRenderer;
    private SpriteRenderer ringRenderer;
    private SpriteRenderer scorchRenderer;

    // Particles
    private readonly List<Particle> embers = new List<Particle>();
    private readonly List<Particle> smokePuffs = new List<Particle>();
    private readonly List<Particle> sparks = new List<Particle>();
    private readonly List<Particle> debris = new List<Particle>();

    public void Initialize(float radius)
    {
        explosionRadius = radius;
        BuildCoreVisuals();
        SpawnEmbers();
        SpawnSmokePuffs();
        SpawnSparks();
        SpawnDebris();
    }

    private SpriteRenderer MakeSR(GameObject go, Sprite sprite, int sortOrder, Color color)
    {
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.material = new Material(SpriteMat); // explicit material
        sr.sortingOrder = sortOrder;
        sr.color = color;
        return sr;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = timer / DURATION;

        if (t >= 1f)
        {
            if (scorchRenderer != null)
            {
                scorchRenderer.transform.SetParent(null, true);
                scorchRenderer.gameObject.AddComponent<ScorchFader>()
                    .Initialize(scorchRenderer, 2.5f);
            }
            Destroy(gameObject);
            return;
        }

        UpdateFlash(t);
        UpdateRing(t);
        UpdateParticles(embers);
        UpdateParticles(smokePuffs);
        UpdateParticles(sparks);
        UpdateParticles(debris);
    }

    //  CORE VISUALS 

    private void BuildCoreVisuals()
    {
        // Ground scorch mark
        GameObject scorchObj = new GameObject("Scorch");
        scorchObj.transform.SetParent(transform, false);
        scorchRenderer = MakeSR(scorchObj, GenerateSoftCircleSprite(), SORT_BASE - 200,
            new Color(0.12f, 0.08f, 0.04f, 0.45f));
        scorchObj.transform.localScale = Vector3.one * (explosionRadius * 1.3f);

        // Bright fiery flash
        GameObject flashObj = new GameObject("Flash");
        flashObj.transform.SetParent(transform, false);
        flashRenderer = MakeSR(flashObj, GenerateSoftCircleSprite(), SORT_BASE + 100,
            new Color(1f, 0.92f, 0.55f, 1f));
        flashObj.transform.localScale = Vector3.one * 0.3f;

        // Shockwave ring
        GameObject ringObj = new GameObject("Ring");
        ringObj.transform.SetParent(transform, false);
        ringRenderer = MakeSR(ringObj, GenerateRingSprite(), SORT_BASE + 110,
            new Color(1f, 0.55f, 0.15f, 0.95f));
        ringObj.transform.localScale = Vector3.one * 0.2f;
    }

    private void UpdateFlash(float t)
    {
        if (flashRenderer == null) return;

        float expandT = Mathf.Clamp01(t / 0.12f);
        float scale = Mathf.Lerp(0.3f, explosionRadius * 1.8f, EaseOutQuad(expandT));
        scale *= Mathf.Max(1f - Mathf.Pow(Mathf.Clamp01((t - 0.1f) / 0.5f), 1.5f), 0f);
        flashRenderer.transform.localScale = Vector3.one * Mathf.Max(scale, 0f);

        Color c = Color.Lerp(
            new Color(1f, 0.95f, 0.7f, 1f),
            new Color(1f, 0.35f, 0.05f, 0f),
            Mathf.Clamp01(t * 1.8f));
        flashRenderer.color = c;
    }

    private void UpdateRing(float t)
    {
        if (ringRenderer == null) return;
        float ringScale = Mathf.Lerp(0.2f, explosionRadius * 2.8f, EaseOutCubic(t));
        ringRenderer.transform.localScale = Vector3.one * ringScale;

        Color rc = ringRenderer.color;
        rc.a = 0.95f * (1f - t * t);
        ringRenderer.color = rc;
    }

    //  EMBERS 

    private void SpawnEmbers()
    {
        int count = Random.Range(14, 22);
        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject("Ember");
            go.transform.SetParent(transform, true);
            go.transform.position = transform.position;

            float hue = Random.Range(0f, 0.11f);
            Color col = Color.HSVToRGB(hue, Random.Range(0.75f, 1f), 1f);
            col.a = 1f;
            SpriteRenderer sr = MakeSR(go, GenerateCircleSprite(), SORT_BASE + 120 + i, col);

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float speed = Random.Range(4f, 10f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            float startSz = Random.Range(0.15f, 0.35f);
            go.transform.localScale = Vector3.one * startSz;

            embers.Add(new Particle
            {
                go = go,
                sr = sr,
                velocity = dir * speed + Vector2.up * Random.Range(1.5f, 4f),
                lifetime = Random.Range(0.35f, 0.65f),
                maxLifetime = Random.Range(0.35f, 0.65f),
                startSize = startSz,
                endSize = Random.Range(0.03f, 0.08f),
                gravity = Random.Range(4f, 9f),
                rotationSpeed = Random.Range(-400f, 400f),
                fadeStart = 0.3f
            });
        }
    }

    //  SMOKE PUFFS 

    private void SpawnSmokePuffs()
    {
        int count = Random.Range(6, 10);
        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject("Smoke");
            go.transform.SetParent(transform, true);

            float offAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float offDist = Random.Range(0f, explosionRadius * 0.25f);
            go.transform.position = transform.position +
                new Vector3(Mathf.Cos(offAngle), Mathf.Sin(offAngle), 0f) * offDist;

            float grey = Random.Range(0.25f, 0.45f);
            Color col = new Color(grey, grey, grey * 0.9f, 0.7f);
            SpriteRenderer sr = MakeSR(go, GenerateSoftCircleSprite(), SORT_BASE + 90 + i, col);

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            float startSz = Random.Range(0.5f, 0.9f);
            go.transform.localScale = Vector3.one * startSz;

            smokePuffs.Add(new Particle
            {
                go = go,
                sr = sr,
                velocity = dir * Random.Range(0.4f, 1.5f) + Vector2.up * Random.Range(0.8f, 2f),
                lifetime = Random.Range(0.5f, 0.8f),
                maxLifetime = Random.Range(0.5f, 0.8f),
                startSize = startSz,
                endSize = Random.Range(1.2f, 2.0f),
                gravity = -0.5f, // floats up
                rotationSpeed = Random.Range(-40f, 40f),
                fadeStart = 0.2f
            });
        }
    }

    //  SPARKS — fast bright streaks 

    private void SpawnSparks()
    {
        int count = Random.Range(8, 14);
        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject("Spark");
            go.transform.SetParent(transform, true);
            go.transform.position = transform.position;

            Color col = new Color(1f, Random.Range(0.7f, 0.95f), Random.Range(0.3f, 0.6f), 1f);
            SpriteRenderer sr = MakeSR(go, GenerateSparkSprite(), SORT_BASE + 150 + i, col);

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float speed = Random.Range(7f, 16f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            go.transform.rotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);

            float startSz = Random.Range(0.2f, 0.35f);
            go.transform.localScale = Vector3.one * startSz;

            sparks.Add(new Particle
            {
                go = go,
                sr = sr,
                velocity = dir * speed,
                lifetime = Random.Range(0.12f, 0.3f),
                maxLifetime = Random.Range(0.12f, 0.3f),
                startSize = startSz,
                endSize = 0.03f,
                gravity = Random.Range(3f, 8f),
                rotationSpeed = 0f,
                fadeStart = 0f
            });
        }
    }

    //  DEBRIS 

    private void SpawnDebris()
    {
        int count = Random.Range(4, 8);
        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject("Debris");
            go.transform.SetParent(transform, true);
            go.transform.position = transform.position;

            Color col = new Color(
                Random.Range(0.2f, 0.35f),
                Random.Range(0.15f, 0.25f),
                Random.Range(0.08f, 0.15f),
                0.9f);
            SpriteRenderer sr = MakeSR(go, GenerateCircleSprite(), SORT_BASE + 130 + i, col);

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float speed = Random.Range(3f, 7f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            float startSz = Random.Range(0.12f, 0.25f);
            go.transform.localScale = Vector3.one * startSz;

            debris.Add(new Particle
            {
                go = go,
                sr = sr,
                velocity = dir * speed + Vector2.up * Random.Range(2f, 5f),
                lifetime = Random.Range(0.4f, 0.7f),
                maxLifetime = Random.Range(0.4f, 0.7f),
                startSize = startSz,
                endSize = startSz * 0.7f,
                gravity = Random.Range(8f, 14f),
                rotationSpeed = Random.Range(-500f, 500f),
                fadeStart = 0.5f
            });
        }
    }

    //  PARTICLE UPDATE 

    private void UpdateParticles(List<Particle> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var p = list[i];
            if (p.go == null) { list.RemoveAt(i); continue; }

            p.lifetime -= Time.deltaTime;
            if (p.lifetime <= 0f)
            {
                Destroy(p.go);
                list.RemoveAt(i);
                continue;
            }

            float t = 1f - (p.lifetime / p.maxLifetime); // 0→1

            // Gravity
            p.velocity += Vector2.down * p.gravity * Time.deltaTime;

            // Move
            p.go.transform.position += (Vector3)(p.velocity * Time.deltaTime);

            // Scale
            float size = Mathf.Lerp(p.startSize, p.endSize, t);
            p.go.transform.localScale = Vector3.one * size;

            // Rotate
            if (p.rotationSpeed != 0f)
                p.go.transform.Rotate(0, 0, p.rotationSpeed * Time.deltaTime);

            // Fade — delayed start for more punch
            if (p.sr != null)
            {
                float fadeT = t <= p.fadeStart ? 0f : (t - p.fadeStart) / (1f - p.fadeStart);
                Color c = p.sr.color;
                c.a = Mathf.Lerp(c.a, 0f, fadeT * fadeT);
                p.sr.color = c;
            }
        }
    }

    //  PROCEDURAL SPRITES 

    private static Sprite _circleSprite;
    private static Sprite GenerateCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int S = 16;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float r = S * 0.5f - 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 1f - Mathf.Clamp01((d - r + 1.5f) / 1.5f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _circleSprite;
    }

    private static Sprite _softCircleSprite;
    private static Sprite GenerateSoftCircleSprite()
    {
        if (_softCircleSprite != null) return _softCircleSprite;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float r = S * 0.45f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float t = Mathf.Clamp01(d / r);
                float a = Mathf.Clamp01(1f - t * t);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _softCircleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _softCircleSprite;
    }

    private static Sprite _ringSprite;
    private static Sprite GenerateRingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float outerR = S * 0.48f, innerR = S * 0.36f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;
                if (d >= innerR && d <= outerR)
                {
                    float mid = (innerR + outerR) * 0.5f;
                    float hw = (outerR - innerR) * 0.5f;
                    a = 1f - Mathf.Abs(d - mid) / hw;
                }
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px); tex.Apply();
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _ringSprite;
    }

    private static Sprite _sparkSprite;
    private static Sprite GenerateSparkSprite()
    {
        if (_sparkSprite != null) return _sparkSprite;
        const int W = 20, H = 6;
        var tex = new Texture2D(W, H, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float tx = x / (float)(W - 1);
                float ty = Mathf.Abs(y - H * 0.5f) / (H * 0.5f);
                // Tapered streak: bright at left, fading right, thin vertically
                float a = (1f - ty * ty) * (1f - tx * tx * 0.6f);
                px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px); tex.Apply();
        _sparkSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), W);
        return _sparkSprite;
    }

    //  HELPERS 

    private class Particle
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float lifetime, maxLifetime;
        public float startSize, endSize;
        public float gravity;
        public float rotationSpeed;
        public float fadeStart; // 0-1: when fading begins (0 = immediate, 0.5 = halfway)
    }

    private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    private static float EaseOutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }
}


/// Fades and destroys a scorch mark over time.

public class ScorchFader : MonoBehaviour
{
    private SpriteRenderer sr;
    private float duration;
    private float timer;
    private float startAlpha;

    public void Initialize(SpriteRenderer renderer, float fadeDuration)
    {
        sr = renderer;
        duration = fadeDuration;
        startAlpha = sr.color.a;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = timer / duration;
        if (t >= 1f) { Destroy(gameObject); return; }
        Color c = sr.color;
        c.a = startAlpha * (1f - t * t);
        sr.color = c;
    }
}
