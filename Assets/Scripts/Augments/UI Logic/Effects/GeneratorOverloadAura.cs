using UnityEngine;

//  345 — "Overload Aura"
//  Each time a generator tower produces energy, it also deals damage to every
//  enemy within its generation range, and pulses a stasis field.
public static class GeneratorAoeDamage
{
    public static bool Enabled = false;
    public static float DamageMultiplier = 1f;

    // Which physics layers count as enemies. ~0 (everything) is fine because we
    // additionally require an EnemyStats component before applying damage.
    public static LayerMask EnemyMask = ~0;

    // HOOK: Tower.GenerateEnergy calls this right after giving the player energy.
    public static void Apply(Tower generator, float energyGenerated)
    {
        if (!Enabled || generator == null) return;
        if (!generator.isEnergyGenerator) return;

        float radius = Mathf.Max(0.1f, generator.generationRange);

        // Visual pulses every generation tick (even at 0 dmg) so the field reads
        // as a continuous overload shimmer while the augment is active.
        GeneratorOverloadVisual.PulseOn(generator, radius);

        float damage = energyGenerated * DamageMultiplier;
        if (damage <= 0f) return;

        var hits = Physics2D.OverlapCircleAll(generator.transform.position, radius, EnemyMask);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;
            var stats = hits[i].GetComponent<EnemyStats>();
            if (stats == null) continue;

            // Tower-sourced damage, so it counts toward tower-kill augments.
            TowerKillAttribution.MarkTowerHit(stats.gameObject);
            stats.TakeDamage(damage);
        }
    }
}


// Stasis visual for the generator overload — modelled on ScarecrowAuraVisual
// (additive disc + flickering electric arcs). Pulse-driven: PulseOn is called
// each generation tick, so the field stays lit while generating, then fades.
// Self-contained: lazily added to the generator, builds its own meshes/arcs,
// renders below the tower sprite.
public class GeneratorOverloadVisual : MonoBehaviour
{
    private static readonly Color DiscInner = new Color(0.35f, 0.65f, 1.0f, 0.40f); // electric cyan-blue
    private static readonly Color DiscOuter = new Color(0.30f, 0.20f, 0.85f, 0f);   // fades to violet
    private static readonly Color ArcColor = new Color(0.7f, 0.85f, 1f, 1f);

    private const int DiscSegments = 64;
    private const int ArcCount = 5;
    private const int ArcSegments = 10;
    private const float ArcWidth = 0.06f;
    private const float ArcJitter = 0.16f;
    private const float ArcFlickerHz = 24f;
    private const float PulseFade = 0.30f; // seconds to fade after pulsing stops

    private float radius = 4f;
    private float pulseTimer = 0f;
    private float arcFlickerTimer = 0f;

    private MeshRenderer discRenderer;
    private MeshFilter discFilter;
    private Material discMaterial;
    private LineRenderer[] arcs;
    private Material arcMaterial;
    private bool built = false;

    public static void PulseOn(Tower generator, float radius)
    {
        if (generator == null) return;
        var vis = generator.GetComponent<GeneratorOverloadVisual>();
        if (vis == null) vis = generator.gameObject.AddComponent<GeneratorOverloadVisual>();
        vis.Pulse(radius);
    }

    public void Pulse(float newRadius)
    {
        if (!built || !Mathf.Approximately(newRadius, radius))
        {
            radius = Mathf.Max(0.1f, newRadius);
            Build();
        }
        pulseTimer = PulseFade;
        SetRenderersEnabled(true);
    }

    private void SetRenderersEnabled(bool on)
    {
        if (discRenderer != null) discRenderer.enabled = on;
        if (arcs != null)
            foreach (var lr in arcs)
                if (lr != null) lr.enabled = on;
    }

    private void Build()
    {
        if (built) TearDown();
        BuildDisc();
        BuildArcs();
        built = true;
        SetRenderersEnabled(false);
    }

    private void TearDown()
    {
        if (discFilter != null) Destroy(discFilter.gameObject);
        if (arcs != null)
            foreach (var lr in arcs)
                if (lr != null) Destroy(lr.gameObject);
        if (discMaterial != null) Destroy(discMaterial);
        if (arcMaterial != null) Destroy(arcMaterial);
        arcs = null;
        built = false;
    }

    private static Material MakeAdditiveMaterial()
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        var mat = new Material(sh);
        mat.mainTexture = Texture2D.whiteTexture;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        return mat;
    }

    private void BuildDisc()
    {
        var discGO = new GameObject("OverloadDisc");
        discGO.transform.SetParent(transform, false);
        discGO.transform.localPosition = new Vector3(0f, 0f, 0.01f); // behind tower sprite

        discFilter = discGO.AddComponent<MeshFilter>();
        discRenderer = discGO.AddComponent<MeshRenderer>();
        discFilter.mesh = BuildDiscMesh(radius, DiscSegments);

        discMaterial = MakeAdditiveMaterial();
        discRenderer.material = discMaterial;
        discRenderer.sortingLayerName = "Default";
        discRenderer.sortingOrder = 100;
    }

    private static Mesh BuildDiscMesh(float radius, int segments)
    {
        var m = new Mesh { name = "GeneratorOverloadDisc" };
        var verts = new Vector3[segments + 1];
        var cols = new Color[segments + 1];
        var tris = new int[segments * 3];

        verts[0] = Vector3.zero;
        cols[0] = new Color(1, 1, 1, 0.3f);
        for (int i = 0; i < segments; i++)
        {
            float t = (i / (float)segments) * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius, 0f);
            cols[i + 1] = new Color(1, 1, 1, 0f);
        }
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % segments + 1;
        }
        m.vertices = verts; m.colors = cols; m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    private void BuildArcs()
    {
        arcs = new LineRenderer[ArcCount];
        arcMaterial = MakeAdditiveMaterial();

        for (int i = 0; i < ArcCount; i++)
        {
            var arcGO = new GameObject("OverloadArc_" + i);
            arcGO.transform.SetParent(transform, false);
            arcGO.transform.localPosition = Vector3.zero;

            var lr = arcGO.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = arcMaterial;
            lr.startWidth = ArcWidth;
            lr.endWidth = ArcWidth * 0.5f;
            lr.positionCount = ArcSegments;
            lr.startColor = ArcColor;
            lr.endColor = new Color(ArcColor.r, ArcColor.g, ArcColor.b, ArcColor.a * 0.2f);
            lr.numCornerVertices = 2;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 110;
            arcs[i] = lr;
        }
    }

    private void Update()
    {
        if (!built) return;

        if (pulseTimer <= 0f)
        {
            if (discRenderer != null && discRenderer.enabled) SetRenderersEnabled(false);
            return;
        }

        pulseTimer -= Time.deltaTime;
        float life = Mathf.Clamp01(pulseTimer / PulseFade); // 1 -> 0 as it fades

        if (discFilter != null && discFilter.mesh != null)
        {
            float scale = 1f + (1f - life) * 0.12f;
            discFilter.transform.localScale = new Vector3(scale, scale, 1f);

            Color inner = DiscInner; inner.a = DiscInner.a * life;
            Color outer = DiscOuter;

            var mesh = discFilter.mesh;
            var cols = mesh.colors;
            if (cols != null && cols.Length > 0)
            {
                cols[0] = inner;
                for (int i = 1; i < cols.Length; i++) cols[i] = outer;
                mesh.colors = cols;
            }
        }

        arcFlickerTimer += Time.deltaTime;
        float flickerPeriod = 1f / Mathf.Max(1f, ArcFlickerHz);
        if (arcFlickerTimer >= flickerPeriod && arcs != null)
        {
            arcFlickerTimer = 0f;
            for (int i = 0; i < arcs.Length; i++)
            {
                var lr = arcs[i];
                if (lr == null) continue;

                float angle = Random.Range(0f, Mathf.PI * 2f);
                Vector2 end = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                              * radius * Random.Range(0.6f, 1f);
                Vector3 start = Vector3.zero;
                Vector3 endV = new Vector3(end.x, end.y, 0f);

                for (int s = 0; s < ArcSegments; s++)
                {
                    float t = s / (float)(ArcSegments - 1);
                    Vector3 p = Vector3.Lerp(start, endV, t);
                    if (s > 0 && s < ArcSegments - 1)
                    {
                        p.x += Random.Range(-ArcJitter, ArcJitter);
                        p.y += Random.Range(-ArcJitter, ArcJitter);
                    }
                    lr.SetPosition(s, p);
                }

                float a = Random.Range(0.4f, 1f) * life;
                Color sc = ArcColor; sc.a = ArcColor.a * a;
                Color ec = ArcColor; ec.a = ArcColor.a * a * 0.2f;
                lr.startColor = sc;
                lr.endColor = ec;
            }
        }
    }

    private void OnDestroy()
    {
        if (discMaterial != null) Destroy(discMaterial);
        if (arcMaterial != null) Destroy(arcMaterial);
    }
}
