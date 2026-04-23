using UnityEngine;
using UnityEngine.Rendering.Universal;


// Animated fireplace — works with ObstacleGenerator drag-and-drop slots.
// ObstacleGenerator.SpawnSinglePrefab() Instantiates the prefab, sets its
// scale, and adds a BoxCollider2D + YSortEntity automatically.
public class Fireplace : MonoBehaviour
{
    [Header("Fire")]
    [Tooltip("Where flames start (0 = sprite bottom, 1 = sprite top)")]
    [Range(0f, 1f)]
    public float fireOriginY = 0.55f;

    [Tooltip("Flame height relative to sprite height")]
    [Range(0.2f, 2f)]
    public float flameHeightFraction = 0.6f;

    public int maxFlameParticles = 18;
    public int maxEmberParticles = 6;

    [Header("Point Light 2D")]
    public float lightRadius = 1.5f;
    [Range(0f, 2f)]
    public float lightIntensity = 0.7f;
    public Color lightColor = new Color(1.0f, 0.65f, 0.25f, 1f);

    [Header("Flicker")]
    public float flickerSpeed = 5f;
    [Range(0f, 0.4f)]
    public float flickerAmount = 0.15f;

    // Runtime
    private Light2D pointLight;
    private float baseIntensity;
    private float flickerOffset;
    private int currentSortOrder;
    private ParticleSystemRenderer fireRenderer;
    private ParticleSystemRenderer emberRenderer;
    private SpriteRenderer sr;

    // Use Start (not Awake) so ObstacleGenerator has already set our scale
    void Start()
    {
        flickerOffset = Random.Range(0f, 100f);

        sr = GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
        {
            Debug.LogError("[Fireplace] No SpriteRenderer/Sprite found.");
            return;
        }

        // Measure actual world bounds (after ObstacleGenerator set our scale)
        Bounds wb = sr.bounds;
        float sprW = wb.size.x;
        float sprH = wb.size.y;

        // Fire origin in world Y, then convert to local offset
        float fireWorldY = wb.min.y + sprH * fireOriginY;
        float fireLocalY = transform.InverseTransformPoint(new Vector3(0f, fireWorldY, 0f)).y;
        Vector3 fireLocal = new Vector3(0f, fireLocalY, 0f);

        CreateFireParticles(fireLocal, sprW, sprH);
        CreateEmberParticles(fireLocal, sprW, sprH);
        CreateLight(fireLocal);
        UpdateSortOrder();
    }

    void LateUpdate()
    {
        // Flicker
        if (pointLight != null)
        {
            float t = Time.time * flickerSpeed + flickerOffset;
            float flicker = 1f
                + Mathf.Sin(t) * flickerAmount
                + Mathf.Sin(t * 2.37f) * flickerAmount * 0.5f
                + Mathf.Sin(t * 5.13f) * flickerAmount * 0.2f;
            pointLight.intensity = baseIntensity * flicker;
        }

        // Keep particle sortingOrder in sync with YSortEntity
        UpdateSortOrder();
    }

    void UpdateSortOrder()
    {
        if (sr == null) return;
        int order = sr.sortingOrder;
        if (order == currentSortOrder) return;
        currentSortOrder = order;
        if (fireRenderer != null) fireRenderer.sortingOrder = order + 1;
        if (emberRenderer != null) emberRenderer.sortingOrder = order + 2;
    }

    //  Fire 

    void CreateFireParticles(Vector3 localPos, float sprW, float sprH)
    {
        GameObject go = new GameObject("FireParticles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        float flameH = sprH * flameHeightFraction;
        float flameW = sprW * 0.25f;
        float pSize = Mathf.Max(0.02f, sprW * 0.14f);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        main.startSpeed = 0f;
        main.maxParticles = maxFlameParticles;
        main.startSize = new ParticleSystem.MinMaxCurve(pSize * 0.5f, pSize);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.55f, 0.05f, 0.85f),
            new Color(1f, 0.85f, 0.15f, 0.85f));

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = maxFlameParticles / 0.3f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = flameW * 0.4f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-flameW * 0.2f, flameW * 0.2f);
        vel.y = new ParticleSystem.MinMaxCurve(flameH * 1.5f, flameH * 2.5f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); // must match x/y curve mode

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sc = new AnimationCurve();
        sc.AddKey(0f, 0.6f);
        sc.AddKey(0.2f, 1.0f);
        sc.AddKey(1f, 0f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, sc);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.75f, 0.15f), 0f),
                new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.5f),
                new GradientColorKey(new Color(0.2f, 0.06f, 0.02f), 1f),
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.8f, 0.08f),
                new GradientAlphaKey(0.6f, 0.5f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        fireRenderer = go.GetComponent<ParticleSystemRenderer>();
        fireRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        fireRenderer.material = CreateAdditiveMaterial();
        fireRenderer.sortingOrder = 1000; // updated each frame by UpdateSortOrder
    }

    //  Embers 

    void CreateEmberParticles(Vector3 localPos, float sprW, float sprH)
    {
        GameObject go = new GameObject("EmberParticles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        float eSize = Mathf.Max(0.008f, sprW * 0.025f);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
        main.startSpeed = 0f;
        main.maxParticles = maxEmberParticles;
        main.startSize = new ParticleSystem.MinMaxCurve(eSize * 0.5f, eSize);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.6f, 0.1f, 1f),
            new Color(1f, 0.9f, 0.3f, 1f));

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 4f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = sprW * 0.08f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-sprW * 0.15f, sprW * 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(sprH * 0.5f, sprH * 1.0f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); // must match x/y curve mode

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.7f, 0.2f), 0f),
                new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f),
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        emberRenderer = go.GetComponent<ParticleSystemRenderer>();
        emberRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        emberRenderer.material = CreateAdditiveMaterial();
        emberRenderer.sortingOrder = 1000; // updated each frame by UpdateSortOrder
    }

    //  Light 

    void CreateLight(Vector3 localPos)
    {
        GameObject go = new GameObject("FireLight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        pointLight = go.AddComponent<Light2D>();
        pointLight.lightType = Light2D.LightType.Point;
        pointLight.color = lightColor;
        pointLight.intensity = lightIntensity;
        pointLight.pointLightOuterRadius = lightRadius;
        pointLight.pointLightInnerRadius = lightRadius * 0.2f;
        pointLight.pointLightInnerAngle = 360f;
        pointLight.pointLightOuterAngle = 360f;
        baseIntensity = lightIntensity;
    }

    //  Material 

    Material CreateAdditiveMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 1f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetTexture("_MainTex", GenerateSoftDot());
        mat.renderQueue = 3500;
        return mat;
    }

    static Texture2D _sharedDot;
    Texture2D GenerateSoftDot()
    {
        if (_sharedDot != null) return _sharedDot;
        int sz = 32;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float c = sz * 0.5f;
        for (int y = 0; y < sz; y++)
            for (int x = 0; x < sz; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d * d)));
            }
        tex.Apply();
        _sharedDot = tex;
        return tex;
    }
}
