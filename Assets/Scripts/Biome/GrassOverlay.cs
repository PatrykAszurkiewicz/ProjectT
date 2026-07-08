using UnityEngine;
using System.Collections.Generic;


public class GrassOverlay : MonoBehaviour
{
    [Header("Grass Distribution")]
    public float spawnRadius = 22f;
    public int bladeCount = 50000;
    public float coreExclusionRadius = 1.5f;

    [Header("Clumping")]
    public int clumpCount = 450;
    public float clumpSpread = 0.5f;
    [Range(0f, 1f)]
    public float freeScatterRatio = 0.07f;
    [Tooltip("How strongly blades in a clump lean outward from clump center (0 = random, 1 = fully outward)")]
    [Range(0f, 1f)]
    public float clumpLeanCoherence = 0.5f;

    [Header("Blade Appearance")]
    public float bladeHeight = 0.22f;
    public float heightVariation = 0.5f;
    public float bladeWidth = 0.02f;
    public float widthVariation = 0.35f;
    [Range(0f, 1f)]
    public float bladeCurvature = 0.4f;
    [Tooltip("How much the tip counter-curves back (S-shape)")]
    [Range(0f, 0.5f)]
    public float tipCounterCurve = 0.15f;
    [Tooltip("Organic width wobble along blade length")]
    [Range(0f, 0.3f)]
    public float widthWobble = 0.12f;

    [Header("Blade Type Mix")]
    [Range(0f, 0.5f)]
    public float shortBladeRatio = 0.22f;
    [Range(0f, 0.3f)]
    public float tallBladeRatio = 0.12f;
    [Tooltip("Dead/dry yellowed blades mixed in")]
    [Range(0f, 0.15f)]
    public float deadBladeRatio = 0.06f;

    [Header("Color Palette")]
    public Color colorDarkBase = new Color(0.09f, 0.30f, 0.05f, 1.0f);
    public Color colorMidBlade = new Color(0.15f, 0.46f, 0.09f, 0.93f);
    public Color colorBrightTip = new Color(0.28f, 0.64f, 0.15f, 0.72f);
    public Color colorGroundCover = new Color(0.07f, 0.25f, 0.04f, 1.0f);
    [Tooltip("Yellow-brown for dead/dry blades")]
    public Color colorDeadBase = new Color(0.28f, 0.24f, 0.08f, 0.95f);
    public Color colorDeadTip = new Color(0.40f, 0.34f, 0.12f, 0.75f);
    public float colorVariation = 0.07f;

    [Header("Wind Animation")]
    public float windStrength = 0.12f;
    public float windSpeed = 2.0f;
    public float windTurbulence = 2.0f;
    public float gustStrength = 0.15f;
    public float gustScale = 0.25f;
    public float gustSpeed = 0.7f;

    [Header("Shading")]
    public float shadowDarken = 0.18f;
    public float highlightBrighten = 0.12f;
    public float ambientOcclusion = 0.22f;
    public float tipHighlight = 0.12f;
    public float lightAngle = 135f;
    public float subsurfaceStrength = 0.15f;
    public Color subsurfaceColor = new Color(0.4f, 0.7f, 0.15f, 1f);
    public float windColorShift = 0.1f;
    public float patchScale = 0.15f;
    public float patchStrength = 0.12f;

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    // Base order for the ground decals. Must be > the background's order (-1) or the
    // meshes tie with the background and the camera's Y-axis transparency sort shows
    // them only in a central square. 0 sits just above background, below tower slots
    // (1)/paths (500)/units (500+). Sub-layers add their offsets on top of this.
    public int sortingOrder = 0;

    // Internal
    private List<Mesh> grassMeshes = new List<Mesh>();
    private Material grassMaterial;
    private List<GameObject> grassObjects = new List<GameObject>();
    private Vector2[] clumpCenters;

    private const int MAX_VERTS_PER_MESH = 60000;
    // Standard blades: 7 verts / 5 tris. Tall blades: 9 verts / 7 tris.
    private const int VERTS_PER_BLADE_STD = 7;
    private const int TRIS_PER_BLADE_STD = 5;
    private const int VERTS_PER_BLADE_TALL = 9;
    private const int TRIS_PER_BLADE_TALL = 7;

    private bool _generated;

    void Start()
    {
        // BiomeManager calls GenerateGrass() right after AddComponent; Unity then fires
        // Start() a frame later. Without this guard we build and immediately discard
        // the entire mesh set twice per biome.
        if (!_generated) GenerateGrass();
    }

    [ContextMenu("Regenerate Grass")]
    public void GenerateGrass()
    {
        _generated = true;
        foreach (var go in grassObjects)
            if (go != null) DestroyImmediate(go);
        grassObjects.Clear();
        foreach (var m in grassMeshes)
            if (m != null) DestroyImmediate(m);
        grassMeshes.Clear();
        if (grassMaterial != null) DestroyImmediate(grassMaterial);

        GenerateClumpCenters();
        CreateGrassMaterial();
        CreateGrassMeshes();

        //Debug.Log($"[GrassOverlay] {bladeCount} blades, {grassObjects.Count} mesh(es).");
    }

    void GenerateClumpCenters()
    {
        clumpCenters = new Vector2[clumpCount];
        for (int i = 0; i < clumpCount; i++)
        {
            Vector2 pos;
            int safety = 0;
            do
            {
                pos = Random.insideUnitCircle * spawnRadius * 0.95f;
                safety++;
            } while (pos.magnitude < coreExclusionRadius && safety < 30);
            clumpCenters[i] = pos;
        }
    }

    void CreateGrassMaterial()
    {
        Shader shader = Shader.Find("Custom/GrassWind");
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
        {
            shader = Shader.Find("Sprites/Default");
            Debug.LogWarning("[GrassOverlay] Custom/GrassWind shader not found.");
        }

        grassMaterial = new Material(shader);
        grassMaterial.SetFloat("_WindStrength", windStrength);
        grassMaterial.SetFloat("_WindSpeed", windSpeed);
        grassMaterial.SetFloat("_WindTurbulence", windTurbulence);
        grassMaterial.SetFloat("_GustStrength", gustStrength);
        grassMaterial.SetFloat("_GustScale", gustScale);
        grassMaterial.SetFloat("_GustSpeed", gustSpeed);
        grassMaterial.SetFloat("_ShadowDarken", shadowDarken);
        grassMaterial.SetFloat("_HighlightBrighten", highlightBrighten);
        grassMaterial.SetFloat("_AmbientOcclusion", ambientOcclusion);
        grassMaterial.SetFloat("_TipHighlight", tipHighlight);
        grassMaterial.SetFloat("_LightAngle", lightAngle);
        grassMaterial.SetFloat("_SubsurfaceStrength", subsurfaceStrength);
        grassMaterial.SetColor("_SubsurfaceColor", subsurfaceColor);
        grassMaterial.SetFloat("_WindColorShift", windColorShift);
        grassMaterial.SetFloat("_PatchScale", patchScale);
        grassMaterial.SetFloat("_PatchStrength", patchStrength);


    }

    void CreateGrassMeshes()
    {
        // Budget with worst case (all tall = 9 verts)
        int bladesPerMesh = MAX_VERTS_PER_MESH / VERTS_PER_BLADE_TALL;
        int meshCount = Mathf.CeilToInt((float)bladeCount / bladesPerMesh);
        int bladesRemaining = bladeCount;
        int bladeOffset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int bladesInThis = Mathf.Min(bladesPerMesh, bladesRemaining);
            Mesh mesh = BuildBladeMesh(bladesInThis, bladeOffset);
            grassMeshes.Add(mesh);

            GameObject go = new GameObject($"GrassOverlay_{m}");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;

            go.AddComponent<MeshFilter>().mesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = grassMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = sortingOrder;

            grassObjects.Add(go);
            bladesRemaining -= bladesInThis;
            bladeOffset += bladesInThis;
        }
    }

    enum BladeType { Short, Normal, Tall, Dead }

    // Track which clump a blade belongs to for coherent lean
    private int lastClumpIndex = 0;

    Mesh BuildBladeMesh(int count, int seedOffset)
    {
        // Worst case allocation
        int maxVerts = count * VERTS_PER_BLADE_TALL;
        int maxTris = count * TRIS_PER_BLADE_TALL * 3;

        Vector3[] verts = new Vector3[maxVerts];
        Color[] cols = new Color[maxVerts];
        Vector2[] uvs = new Vector2[maxVerts];
        Vector2[] uv2s = new Vector2[maxVerts];
        int[] tris = new int[maxTris];

        int vi = 0, ti = 0;
        int shortCount = Mathf.RoundToInt(count * shortBladeRatio);
        int tallCount = Mathf.RoundToInt(count * tallBladeRatio);
        int deadCount = Mathf.RoundToInt(count * deadBladeRatio);
        int freeCount = Mathf.RoundToInt(count * freeScatterRatio);

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 42);

            // Determine type
            BladeType type = BladeType.Normal;
            if (i < shortCount) type = BladeType.Short;
            else if (i < shortCount + tallCount) type = BladeType.Tall;
            else if (i < shortCount + tallCount + deadCount) type = BladeType.Dead;

            // Position
            Vector2 pos;
            Vector2 clumpCenter = Vector2.zero;
            bool isClumped = (i >= freeCount);

            if (isClumped)
            {
                lastClumpIndex = Random.Range(0, clumpCenters.Length);
                clumpCenter = clumpCenters[lastClumpIndex];
                float r = Random.value * Random.value;
                Vector2 offset = Random.insideUnitCircle.normalized * r * clumpSpread;
                pos = clumpCenter + offset;
                if (pos.magnitude > spawnRadius) pos = pos.normalized * spawnRadius * 0.98f;
                if (pos.magnitude < coreExclusionRadius) pos = pos.normalized * (coreExclusionRadius + 0.2f);
            }
            else
            {
                pos = GetRandomPosition();
            }

            // Type-dependent parameters
            float hMult = 1f, wMult = 1f, curveMult = 1f;
            bool useTallGeometry = false;

            switch (type)
            {
                case BladeType.Short:
                    hMult = 0.4f; wMult = 1.5f; curveMult = 0.4f;
                    break;
                case BladeType.Tall:
                    hMult = 1.7f; wMult = 0.55f; curveMult = 1.5f;
                    useTallGeometry = true;
                    break;
                case BladeType.Dead:
                    hMult = 0.8f; wMult = 0.9f; curveMult = 1.8f; // dead blades droop more
                    break;
            }

            float h = bladeHeight * hMult * (1f + Random.Range(-heightVariation, heightVariation));
            float w = bladeWidth * wMult * (1f + Random.Range(-widthVariation, widthVariation));

            // === LEAN DIRECTION ===
            float leanDegrees;
            if (isClumped && clumpLeanCoherence > 0f)
            {
                // Radiate outward from clump center
                Vector2 outDir = (pos - clumpCenter);
                if (outDir.sqrMagnitude < 0.001f) outDir = Random.insideUnitCircle;
                float outAngle = Mathf.Atan2(outDir.x, outDir.y) * Mathf.Rad2Deg;
                float randomLean = Random.Range(-45f, 45f);
                leanDegrees = Mathf.Lerp(randomLean, outAngle, clumpLeanCoherence);
                leanDegrees = Mathf.Clamp(leanDegrees, -60f, 60f);
            }
            else
            {
                leanDegrees = Random.Range(-45f, 45f);
            }

            float leanNormalized = Mathf.Clamp(leanDegrees / 60f, -1f, 1f);
            float leanAngle = leanDegrees * Mathf.Deg2Rad;
            float sinA = Mathf.Sin(leanAngle);
            float cosA = Mathf.Cos(leanAngle);

            float curveDir = Mathf.Sign(leanAngle + Random.Range(-0.1f, 0.1f));
            float curveAmount = bladeCurvature * curveMult * h * curveDir;
            // S-curve
            float counterCurve = -tipCounterCurve * h * curveDir * Random.Range(0.5f, 1.5f);

            // BUILD SPINE
            int segCount = useTallGeometry ? 5 : 4;
            float[] segH, segW, segCurve;

            if (useTallGeometry)
            {
                segH = new float[] { 0f, 0.2f, 0.45f, 0.75f, 1.0f };
                segW = new float[] { 1.0f, 0.75f, 0.45f, 0.18f, 0.0f };
                segCurve = new float[] { 0f, 0.04f, 0.22f, 0.65f, 1.0f };
            }
            else
            {
                segH = new float[] { 0f, 0.25f, 0.6f, 1.0f };
                segW = new float[] { 1.0f, 0.7f, 0.3f, 0.0f };
                segCurve = new float[] { 0f, 0.07f, 0.38f, 1.0f };

                if (type == BladeType.Short)
                    segW = new float[] { 1.0f, 0.85f, 0.5f, 0.15f };
            }

            Vector2[] spine = new Vector2[segCount];
            for (int s = 0; s < segCount; s++)
            {
                float t = segH[s];
                float sh = t * h;
                // Main curve + S-curve counter at top
                float sCurveBlend = Mathf.Clamp01((t - 0.6f) / 0.4f); // kicks in at 60% height
                float totalCurveOffset = curveAmount * segCurve[s] + counterCurve * sCurveBlend;

                float cx = sinA * sh + totalCurveOffset;
                float cy = cosA * sh;
                spine[s] = new Vector2(pos.x + cx, pos.y + cy);
            }

            // COLORS 
            float cShift = Random.Range(-colorVariation, colorVariation);
            float cShift2 = Random.Range(-colorVariation * 0.5f, colorVariation * 0.5f);

            Color cBase, cMid, cTip;

            if (type == BladeType.Dead)
            {
                cBase = colorDeadBase;
                cMid = Color.Lerp(colorDeadBase, colorDeadTip, 0.5f);
                cTip = colorDeadTip;
            }
            else if (type == BladeType.Short)
            {
                cBase = colorGroundCover;
                cMid = Color.Lerp(colorGroundCover, colorDarkBase, 0.5f);
                cTip = Color.Lerp(colorDarkBase, colorMidBlade, 0.3f);
            }
            else if (type == BladeType.Tall)
            {
                cBase = Color.Lerp(colorDarkBase, colorMidBlade, 0.3f);
                cMid = colorMidBlade;
                cTip = Color.Lerp(colorBrightTip, new Color(0.38f, 0.72f, 0.20f, 0.68f), 0.3f);
            }
            else
            {
                cBase = colorDarkBase;
                cMid = colorMidBlade;
                cTip = colorBrightTip;
            }

            cBase.r += cShift2; cBase.g += cShift; cBase.b += cShift2;
            cMid.r += cShift2; cMid.g += cShift; cMid.b += cShift2;
            cTip.r += cShift2; cTip.g += cShift; cTip.b += cShift2;

            cBase.a = Mathf.Max(cBase.a, 0.85f);
            cMid.a = Mathf.Max(cMid.a, 0.78f);
            cTip.a = Mathf.Max(cTip.a, 0.62f);

            cBase = ClampMin(cBase, 0.03f);
            cMid = ClampMin(cMid, 0.04f);
            cTip = ClampMin(cTip, 0.06f);

            Vector2 perp = new Vector2(-cosA, sinA);

            // Width wobble seed
            float wobbleSeed = Random.value * 100f;

            // BUILD VERTICES
            // Pairs at each segment level, single point at tip
            int vertCount = (segCount - 1) * 2 + 1;
            int triCount = (segCount - 2) * 2 + 1;

            for (int s = 0; s < segCount; s++)
            {
                float t = segH[s];
                Color segColor;
                if (t < 0.5f)
                    segColor = Color.Lerp(cBase, cMid, t * 2f);
                else
                    segColor = Color.Lerp(cMid, cTip, (t - 0.5f) * 2f);

                if (s < segCount - 1)
                {
                    // Paired vertices
                    float segWidth = w * segW[s] * 0.5f;
                    // Organic wobble
                    float wobble = 1f + Mathf.Sin(wobbleSeed + t * 8f) * widthWobble;
                    segWidth *= wobble;

                    verts[vi + s * 2 + 0] = V3(spine[s] - perp * segWidth);
                    verts[vi + s * 2 + 1] = V3(spine[s] + perp * segWidth);
                    cols[vi + s * 2 + 0] = segColor;
                    cols[vi + s * 2 + 1] = segColor;
                    uvs[vi + s * 2 + 0] = new Vector2(0f, t);
                    uvs[vi + s * 2 + 1] = new Vector2(1f, t);
                    uv2s[vi + s * 2 + 0] = new Vector2(leanNormalized, 0f);
                    uv2s[vi + s * 2 + 1] = new Vector2(leanNormalized, 0f);
                }
                else
                {
                    // Tip vertex
                    int tipIdx = vi + (segCount - 1) * 2;
                    verts[tipIdx] = V3(spine[s]);
                    cols[tipIdx] = cTip;
                    uvs[tipIdx] = new Vector2(0.5f, 1f);
                    uv2s[tipIdx] = new Vector2(leanNormalized, 0f);
                }
            }

            // BUILD TRIANGLES
            // Quads between segment pairs, triangle at top
            for (int s = 0; s < segCount - 2; s++)
            {
                int bl = vi + s * 2;
                int br = bl + 1;
                int tl = bl + 2;
                int tr = bl + 3;

                tris[ti + 0] = bl; tris[ti + 1] = tl; tris[ti + 2] = br;
                tris[ti + 3] = br; tris[ti + 4] = tl; tris[ti + 5] = tr;
                ti += 6;
            }

            // Top triangle
            {
                int lastPairBase = vi + (segCount - 2) * 2;
                int tipIdx = vi + (segCount - 1) * 2;
                tris[ti + 0] = lastPairBase;
                tris[ti + 1] = tipIdx;
                tris[ti + 2] = lastPairBase + 1;
                ti += 3;
            }

            vi += vertCount;
        }

        // Trim arrays to actual used size
        Mesh mesh = new Mesh();
        mesh.name = "GrassBladeMesh";
        if (vi > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = TrimArray(verts, vi);
        mesh.colors = TrimArray(cols, vi);
        mesh.uv = TrimArray(uvs, vi);
        mesh.uv2 = TrimArray(uv2s, vi);
        mesh.triangles = TrimArray(tris, ti);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Bounds b = mesh.bounds;
        b.Expand(3f);
        mesh.bounds = b;
        return mesh;
    }

    T[] TrimArray<T>(T[] source, int length)
    {
        if (source.Length == length) return source;
        T[] result = new T[length];
        System.Array.Copy(source, result, length);
        return result;
    }

    Color ClampMin(Color c, float min)
    {
        c.r = Mathf.Max(c.r, min);
        c.g = Mathf.Max(c.g, min);
        c.b = Mathf.Max(c.b, min);
        return c;
    }

    Vector3 V3(Vector2 v) => new Vector3(v.x, v.y, 0f);

    Vector2 GetRandomPosition()
    {
        Vector2 pos;
        int safety = 0;
        do
        {
            pos = Random.insideUnitCircle * spawnRadius;
            safety++;
        } while (pos.magnitude < coreExclusionRadius && safety < 30);
        return pos;
    }

    void OnDestroy()
    {
        foreach (var m in grassMeshes)
            if (m != null) DestroyImmediate(m);
        if (grassMaterial != null) DestroyImmediate(grassMaterial);
    }
}

