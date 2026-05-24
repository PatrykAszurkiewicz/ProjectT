using System.Collections.Generic;
using UnityEngine;

// Visual layer for the Scarecrow stasis aura
public class ScarecrowAuraVisual : MonoBehaviour
{
    private float radius = 4f;

    [Header("Disc")]
    // NOTE: alpha here drives the additive intensity, not transparency, since
    // we render the disc with additive blending. Higher alpha = more glow.
    // Keep low so the disc doesn't blow out the underlying art.
    [SerializeField] private Color discColorInner = new Color(0.55f, 0.20f, 0.95f, 0.45f);
    [SerializeField] private Color discColorOuter = new Color(0.30f, 0.10f, 0.80f, 0f);
    [SerializeField] private float pulseSpeed = 2.5f;
    [SerializeField] private float pulseAmplitude = 0.10f; // % radius

    [Header("Arcs")]
    [SerializeField] private int arcCount = 6;
    [SerializeField] private int arcSegments = 12;
    [SerializeField] private float arcJitter = 0.18f;
    [SerializeField] private float arcWidth = 0.06f;
    [SerializeField] private Color arcColor = new Color(0.85f, 0.6f, 1f, 1f);
    [SerializeField] private float arcFlickerHz = 22f;

    private MeshRenderer discRenderer;
    private MeshFilter discFilter;
    private Material discMaterial;

    private LineRenderer[] arcs;
    private bool active = false;
    private float arcFlickerTimer = 0f;

    public void Configure(float radius)
    {
        this.radius = radius;
        BuildDisc();
        BuildArcs();
        SetActive(false);
    }

    public void SetActive(bool on)
    {
        active = on;
        if (discRenderer != null) discRenderer.enabled = on;
        if (arcs != null)
        {
            foreach (var lr in arcs)
                if (lr != null) lr.enabled = on;
        }
    }

    private void BuildDisc()
    {
        // Build a child object so we don't fight the parent's SpriteRenderer
        // (the aura GameObject itself is the gameplay aura's host).
        var discGO = new GameObject("Disc");
        discGO.transform.SetParent(transform, false);
        discGO.transform.localPosition = Vector3.zero;
        // Z-offset slightly into the background so the disc doesn't render
        // on top of the scarecrow sprite.
        discGO.transform.localPosition = new Vector3(0f, 0f, 0.01f);

        discFilter = discGO.AddComponent<MeshFilter>();
        discRenderer = discGO.AddComponent<MeshRenderer>();

        Mesh mesh = BuildDiscMesh(radius, 64);
        discFilter.mesh = mesh;

        // Use Unity's built-in Sprites/Default shader as a base. 
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        discMaterial = new Material(sh);
        discMaterial.mainTexture = Texture2D.whiteTexture;
        discMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        discMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        discMaterial.SetInt("_ZWrite", 0);
        discRenderer.material = discMaterial;

        // Render below the scarecrow's sprite.
        discRenderer.sortingLayerName = "Default";
        discRenderer.sortingOrder = 100;
    }

    private static Mesh BuildDiscMesh(float radius, int segments)
    {
        Mesh m = new Mesh { name = "ScarecrowAuraDisc" };

        Vector3[] verts = new Vector3[segments + 1];
        Color[] cols = new Color[segments + 1];
        int[] tris = new int[segments * 3];

        verts[0] = Vector3.zero;
        // Colors filled at runtime, but seed them so the mesh isn't ugly
        // on the first frame before SetActive(true).
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

        m.vertices = verts;
        m.colors = cols;
        m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    private void BuildArcs()
    {
        arcs = new LineRenderer[arcCount];
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        Material arcMaterial = new Material(sh);
        // Same fix as the disc: white texture so vertex color survives, and
        // additive blend so the lightning glows brightly over whatever is
        // underneath rather than alpha-blending into a dim smudge.
        arcMaterial.mainTexture = Texture2D.whiteTexture;
        arcMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        arcMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        arcMaterial.SetInt("_ZWrite", 0);

        for (int i = 0; i < arcCount; i++)
        {
            var arcGO = new GameObject("Arc_" + i);
            arcGO.transform.SetParent(transform, false);
            arcGO.transform.localPosition = Vector3.zero;

            var lr = arcGO.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = arcMaterial;
            lr.startWidth = arcWidth;
            lr.endWidth = arcWidth * 0.5f;
            lr.positionCount = arcSegments;
            lr.startColor = arcColor;
            lr.endColor = new Color(arcColor.r, arcColor.g, arcColor.b, arcColor.a * 0.2f);
            lr.numCornerVertices = 2;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 110; // above disc, below scarecrow (~1000+)

            arcs[i] = lr;
        }
    }

    private void Update()
    {
        if (!active) return;

        //  Disc pulse: animate alpha via vertex colors 
        if (discFilter != null && discFilter.mesh != null)
        {
            float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // 0..1
            float scale = 1f + pulse * pulseAmplitude;
            discFilter.transform.localScale = new Vector3(scale, scale, 1f);

            Color inner = discColorInner;
            Color outer = discColorOuter;
            inner.a = discColorInner.a * Mathf.Lerp(0.7f, 1.1f, pulse);

            var mesh = discFilter.mesh;
            var cols = mesh.colors;
            if (cols != null && cols.Length > 0)
            {
                cols[0] = inner;
                for (int i = 1; i < cols.Length; i++) cols[i] = outer;
                mesh.colors = cols;
            }
        }

        //  Arcs: rebuild positions every few frames so they flicker 
        arcFlickerTimer += Time.deltaTime;
        float flickerPeriod = 1f / Mathf.Max(1f, arcFlickerHz);
        if (arcFlickerTimer >= flickerPeriod && arcs != null)
        {
            arcFlickerTimer = 0f;
            for (int i = 0; i < arcs.Length; i++)
            {
                var lr = arcs[i];
                if (lr == null) continue;

                // Pick a random direction this tick — arcs lash out to a
                // random point on the aura perimeter.
                float angle = Random.Range(0f, Mathf.PI * 2f);
                Vector2 end = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * Random.Range(0.6f, 1f);

                Vector3 start = Vector3.zero;
                Vector3 endV = new Vector3(end.x, end.y, 0f);

                for (int s = 0; s < arcSegments; s++)
                {
                    float t = s / (float)(arcSegments - 1);
                    Vector3 p = Vector3.Lerp(start, endV, t);
                    // Don't jitter the endpoints — keeps arc anchored at the scarecrow.
                    if (s > 0 && s < arcSegments - 1)
                    {
                        p.x += Random.Range(-arcJitter, arcJitter);
                        p.y += Random.Range(-arcJitter, arcJitter);
                    }
                    lr.SetPosition(s, p);
                }

                // Vary alpha per-arc so they don't all flicker in unison.
                float a = Random.Range(0.4f, 1f);
                Color sc = arcColor; sc.a = arcColor.a * a;
                Color ec = arcColor; ec.a = arcColor.a * a * 0.2f;
                lr.startColor = sc;
                lr.endColor = ec;
            }
        }
    }

    private void OnDestroy()
    {
        if (discMaterial != null) Destroy(discMaterial);
    }
}
