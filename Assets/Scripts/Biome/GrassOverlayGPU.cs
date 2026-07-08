using UnityEngine;
using System.Runtime.InteropServices;


public class GrassOverlayGPU : MonoBehaviour
{
    // Distribution 
    [Header("Grass Distribution")]
    public float spawnRadius = 22f;
    public int bladeCount = 5000000;
    public float coreExclusionRadius = 1.5f;

    [Header("Clumping")]
    public int clumpCount = 44500;
    public float clumpSpread = 0.1f;
    [Range(0f, 1f)] public float freeScatterRatio = 0.07f;
    [Range(0f, 1f)] public float clumpLeanCoherence = 0.5f;

    // Blade Shape 
    [Header("Blade Appearance")]
    public float bladeHeight = 0.22f;
    public float heightVariation = 0.5f;
    public float bladeWidth = 0.02f;
    public float widthVariation = 0.35f;
    [Range(0f, 1f)] public float bladeCurvature = 0.4f;
    [Range(0f, 0.5f)] public float tipCounterCurve = 0.15f;
    [Range(0f, 0.3f)] public float widthWobble = 0.12f;

    [Header("Blade Type Mix")]
    [Range(0f, 0.5f)] public float shortBladeRatio = 0.22f;
    [Range(0f, 0.3f)] public float tallBladeRatio = 0.12f;
    [Range(0f, 0.15f)] public float deadBladeRatio = 0.06f;

    // Colors 
    [Header("Color Palette")]
    public Color colorDarkBase = new Color(0.09f, 0.30f, 0.05f, 1.0f);
    public Color colorMidBlade = new Color(0.15f, 0.46f, 0.09f, 0.93f);
    public Color colorBrightTip = new Color(0.28f, 0.64f, 0.15f, 0.72f);
    public Color colorGroundCover = new Color(0.07f, 0.25f, 0.04f, 1.0f);
    public Color colorDeadBase = new Color(0.28f, 0.24f, 0.08f, 0.95f);
    public Color colorDeadTip = new Color(0.40f, 0.34f, 0.12f, 0.75f);
    public float colorVariation = 0.07f;

    // Wind 
    [Header("Wind Animation")]
    public float windStrength = 0.12f;
    public float windSpeed = 2.0f;
    public float windTurbulence = 2.0f;
    public float gustStrength = 0.15f;
    public float gustScale = 0.25f;
    public float gustSpeed = 0.7f;

    // Shading
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

    // Sorting 
    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = -1;

    // GPU structs

    [StructLayout(LayoutKind.Sequential)]
    struct GrassBlade
    {
        public Vector3 position;
        public float height;
        public float width;
        public float lean;
        public float curvature;
        public float phase;
        public uint packedType;
        public float padding;
        public Vector4 colorBase;
        public Vector4 colorTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ClumpCenter
    {
        public Vector2 position;
    }

    // Internal state 

    private ComputeBuffer bladeBuffer;
    private ComputeBuffer clumpBuffer;
    private ComputeBuffer argsBuffer;
    private Material instanceMaterial;
    private Mesh bladeMesh;
    private Bounds renderBounds;
    private bool isInitialized;


    //  PUBLIC API


    private bool _generated;

    void Start()
    {
        // BiomeManager calls GenerateGrass() right after AddComponent; Unity then fires
        // Start() a frame later. Without this guard we build a 4M-blade buffer + run the
        // generate compute twice per biome.
        if (!_generated) GenerateGrass();
    }

    [ContextMenu("Regenerate Grass")]
    public void GenerateGrass()
    {
        _generated = true;
        Cleanup();

        // 1. Shared blade mesh (7 verts — trivial)
        bladeMesh = BuildSharedBladeMesh();

        // 2. Allocate GPU buffers
        int bladeStride = Marshal.SizeOf<GrassBlade>();
        bladeBuffer = new ComputeBuffer(bladeCount, bladeStride);

        // 3. Generate clump centers on CPU 
        GenerateClumpsOnCPU();

        // 4. Run compute shader to fill blade buffer on GPU 
        RunGenerateCompute();

        // 5. Free clump buffer 
        if (clumpBuffer != null) { clumpBuffer.Release(); clumpBuffer = null; }

        // 6. Indirect draw args
        uint[] args = new uint[] {
            (uint)bladeMesh.GetIndexCount(0),
            (uint)bladeCount,
            0, 0, 0
        };
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        // 7. Rendering material
        CreateMaterial();

        renderBounds = new Bounds(Vector3.zero, Vector3.one * spawnRadius * 3f);
        isInitialized = true;

        Debug.Log($"[GrassGPU] spawnRadius={spawnRadius:F1} bladeCount={bladeCount} " +
                  $"clumpCount={clumpCount} renderBounds={renderBounds.size} | the grass disc " +
                  $"must reach the background's coverage radius or a bare border shows outside it.");
    }

    void Update()
    {
        if (!isInitialized) return;

        instanceMaterial.SetFloat("_GameTime", Time.time);
        instanceMaterial.SetFloat("_WindStrength", windStrength);
        instanceMaterial.SetFloat("_WindSpeed", windSpeed);
        instanceMaterial.SetFloat("_WindTurbulence", windTurbulence);
        instanceMaterial.SetFloat("_GustStrength", gustStrength);
        instanceMaterial.SetFloat("_GustScale", gustScale);
        instanceMaterial.SetFloat("_GustSpeed", gustSpeed);

        Graphics.DrawMeshInstancedIndirect(
            bladeMesh, 0, instanceMaterial, renderBounds, argsBuffer,
            0, null,
            UnityEngine.Rendering.ShadowCastingMode.Off, false
        );
    }


    //  CLUMP GENERATION (CPU)


    void GenerateClumpsOnCPU()
    {
        ClumpCenter[] clumps = new ClumpCenter[clumpCount];
        for (int i = 0; i < clumpCount; i++)
        {
            Vector2 pos;
            int safety = 0;
            do
            {
                pos = Random.insideUnitCircle * spawnRadius * 0.95f;
                safety++;
            } while (pos.magnitude < coreExclusionRadius && safety < 30);
            clumps[i] = new ClumpCenter { position = pos };
        }

        int clumpStride = Marshal.SizeOf<ClumpCenter>();
        clumpBuffer = new ComputeBuffer(clumpCount, clumpStride);
        clumpBuffer.SetData(clumps);
    }


    //  COMPUTE SHADER DISPATCH generates all blades on GPU


    void RunGenerateCompute()
    {
        ComputeShader cs = Resources.Load<ComputeShader>("GrassGenerate");
        if (cs == null)
        {
            Debug.LogError("[GrassOverlayGPU] GrassGenerate.compute not found in Resources! " +
                           "Falling back to CPU generation.");
            FallbackCPUGenerate();
            return;
        }

        int kernel = cs.FindKernel("CSGenerateBlades");

        // Bind buffers
        cs.SetBuffer(kernel, "_Blades", bladeBuffer);
        cs.SetBuffer(kernel, "_Clumps", clumpBuffer);

        // Set parameters
        cs.SetInt("_BladeCount", bladeCount);
        cs.SetInt("_ClumpCount", clumpCount);
        cs.SetFloat("_SpawnRadius", spawnRadius);
        cs.SetFloat("_CoreExclusion", coreExclusionRadius);
        cs.SetFloat("_ClumpSpread", clumpSpread);
        cs.SetFloat("_FreeScatterRatio", freeScatterRatio);
        cs.SetFloat("_ClumpLeanCoherence", clumpLeanCoherence);

        cs.SetFloat("_BladeHeight", bladeHeight);
        cs.SetFloat("_HeightVariation", heightVariation);
        cs.SetFloat("_BladeWidth", bladeWidth);
        cs.SetFloat("_WidthVariation", widthVariation);
        cs.SetFloat("_BladeCurvature", bladeCurvature);

        int shortCount = Mathf.RoundToInt(bladeCount * shortBladeRatio);
        int tallCount = Mathf.RoundToInt(bladeCount * tallBladeRatio);
        int deadCount = Mathf.RoundToInt(bladeCount * deadBladeRatio);
        int freeCount = Mathf.RoundToInt(bladeCount * freeScatterRatio);

        cs.SetInt("_ShortCount", shortCount);
        cs.SetInt("_TallCount", tallCount);
        cs.SetInt("_DeadCount", deadCount);
        cs.SetInt("_FreeCount", freeCount);

        cs.SetVector("_ColorDarkBase", (Vector4)colorDarkBase);
        cs.SetVector("_ColorMidBlade", (Vector4)colorMidBlade);
        cs.SetVector("_ColorBrightTip", (Vector4)colorBrightTip);
        cs.SetVector("_ColorGroundCover", (Vector4)colorGroundCover);
        cs.SetVector("_ColorDeadBase", (Vector4)colorDeadBase);
        cs.SetVector("_ColorDeadTip", (Vector4)colorDeadTip);
        cs.SetFloat("_ColorVariation", colorVariation);

        // Dispatch: 256 threads per group
        int threadGroups = Mathf.CeilToInt(bladeCount / 256f);
        cs.Dispatch(kernel, threadGroups, 1, 1);

        // GPU executes asynchronously 
    }


    //  CPU FALLBACK


    static uint WangHash(uint seed)
    {
        seed = (seed ^ 61) ^ (seed >> 16);
        seed *= 9;
        seed = seed ^ (seed >> 4);
        seed *= 0x27d4eb2d;
        seed = seed ^ (seed >> 15);
        return seed;
    }

    static float HashFloat01(uint seed) => (WangHash(seed) & 0x00FFFFFF) / (float)0x00FFFFFF;
    static float HashSigned(uint seed) => HashFloat01(seed) * 2f - 1f;
    static Vector2 HashDir(uint seed)
    {
        float a = HashFloat01(seed) * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }
    static Vector2 HashCircle(uint s1, uint s2)
    {
        float a = HashFloat01(s1) * Mathf.PI * 2f;
        float r = Mathf.Sqrt(HashFloat01(s2));
        return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
    }

    void FallbackCPUGenerate()
    {
        // Read clump data back from GPU buffer
        ClumpCenter[] clumps = new ClumpCenter[clumpCount];
        clumpBuffer.GetData(clumps);

        GrassBlade[] blades = new GrassBlade[bladeCount];

        int freeCount = Mathf.RoundToInt(bladeCount * freeScatterRatio);
        int shortCount = Mathf.RoundToInt(bladeCount * shortBladeRatio);
        int tallCount = Mathf.RoundToInt(bladeCount * tallBladeRatio);
        int deadCount = Mathf.RoundToInt(bladeCount * deadBladeRatio);

        for (int i = 0; i < bladeCount; i++)
        {
            uint seed = (uint)i * 73856093u ^ 19349663u;
            GrassBlade b;
            b.padding = 0f;

            uint type = 0;
            if (i < shortCount) type = 1;
            else if (i < shortCount + tallCount) type = 2;
            else if (i < shortCount + tallCount + deadCount) type = 3;
            b.packedType = type;

            Vector2 pos;
            Vector2 cc = Vector2.zero;
            bool isClumped = (i >= freeCount) && clumpCount > 0;

            if (isClumped)
            {
                int ci = (int)(WangHash(seed + 1u) % (uint)clumpCount);
                cc = clumps[ci].position;
                float r = HashFloat01(seed + 2u) * HashFloat01(seed + 3u);
                pos = cc + HashDir(seed + 4u) * r * clumpSpread;
                if (pos.magnitude > spawnRadius) pos = pos.normalized * spawnRadius * 0.98f;
                if (pos.magnitude < coreExclusionRadius) pos = pos.normalized * (coreExclusionRadius + 0.2f);
            }
            else
            {
                pos = HashCircle(seed + 5u, seed + 6u) * spawnRadius;
                if (pos.magnitude < coreExclusionRadius) pos = pos.normalized * (coreExclusionRadius + 0.2f);
            }

            b.position = new Vector3(pos.x, pos.y, HashFloat01(seed + 7u) * -0.05f);

            float hM = 1f, wM = 1f, cM = 1f;
            switch (type)
            {
                case 1: hM = 0.4f; wM = 1.5f; cM = 0.4f; break;
                case 2: hM = 1.7f; wM = 0.55f; cM = 1.5f; break;
                case 3: hM = 0.8f; wM = 0.9f; cM = 1.8f; break;
            }

            b.height = bladeHeight * hM * (1f + HashSigned(seed + 8u) * heightVariation);
            b.width = bladeWidth * wM * (1f + HashSigned(seed + 9u) * widthVariation);

            float leanDeg;
            if (isClumped && clumpLeanCoherence > 0f)
            {
                Vector2 od = pos - cc;
                if (od.sqrMagnitude < 0.001f) od = HashDir(seed + 10u);
                float oa = Mathf.Atan2(od.x, od.y) * Mathf.Rad2Deg;
                leanDeg = Mathf.Lerp(HashSigned(seed + 11u) * 45f, oa, clumpLeanCoherence);
                leanDeg = Mathf.Clamp(leanDeg, -60f, 60f);
            }
            else
            {
                leanDeg = HashSigned(seed + 11u) * 45f;
            }
            b.lean = leanDeg * Mathf.Deg2Rad;
            b.curvature = bladeCurvature * cM * b.height * Mathf.Sign(b.lean + HashSigned(seed + 12u) * 0.1f);
            b.phase = HashFloat01(seed + 13u) * Mathf.PI * 2f;

            float cs1 = HashSigned(seed + 14u) * colorVariation;
            float cs2 = cs1 * 0.5f;
            Color cB, cT;
            switch (type)
            {
                case 3: cB = colorDeadBase; cT = colorDeadTip; break;
                case 1: cB = colorGroundCover; cT = Color.Lerp(colorDarkBase, colorMidBlade, 0.3f); break;
                case 2:
                    cB = Color.Lerp(colorDarkBase, colorMidBlade, 0.3f);
                    cT = Color.Lerp(colorBrightTip, new Color(0.38f, 0.72f, 0.20f, 0.68f), 0.3f); break;
                default: cB = colorDarkBase; cT = colorBrightTip; break;
            }
            cB.r += cs2; cB.g += cs1; cB.b += cs2;
            cT.r += cs2; cT.g += cs1; cT.b += cs2;

            b.colorBase = new Vector4(Mathf.Max(cB.r, 0.03f), Mathf.Max(cB.g, 0.03f), Mathf.Max(cB.b, 0.03f), Mathf.Max(cB.a, 0.85f));
            b.colorTip = new Vector4(Mathf.Max(cT.r, 0.06f), Mathf.Max(cT.g, 0.06f), Mathf.Max(cT.b, 0.06f), Mathf.Max(cT.a, 0.62f));

            blades[i] = b;
        }

        bladeBuffer.SetData(blades);
    }


    //  SHARED BLADE MESH


    Mesh BuildSharedBladeMesh()
    {
        float[] segH = { 0f, 0.25f, 0.6f, 1.0f };
        float[] segW = { 1.0f, 0.7f, 0.3f, 0.0f };

        Vector3[] verts = new Vector3[7];
        Vector2[] uvs = new Vector2[7];

        for (int s = 0; s < 3; s++)
        {
            float hw = segW[s] * 0.5f;
            verts[s * 2 + 0] = new Vector3(-hw, segH[s], 0f);
            verts[s * 2 + 1] = new Vector3(hw, segH[s], 0f);
            uvs[s * 2 + 0] = new Vector2(0f, segH[s]);
            uvs[s * 2 + 1] = new Vector2(1f, segH[s]);
        }
        verts[6] = new Vector3(0f, 1f, 0f);
        uvs[6] = new Vector2(0.5f, 1f);

        int[] tris = new int[15];
        int ti = 0;
        for (int s = 0; s < 2; s++)
        {
            int bl = s * 2;
            tris[ti++] = bl; tris[ti++] = bl + 2; tris[ti++] = bl + 1;
            tris[ti++] = bl + 1; tris[ti++] = bl + 2; tris[ti++] = bl + 3;
        }
        tris[ti++] = 4; tris[ti++] = 6; tris[ti++] = 5;

        Mesh mesh = new Mesh { name = "GrassBladeUnit" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        return mesh;
    }


    //  MATERIAL


    void CreateMaterial()
    {
        Shader shader = Shader.Find("Custom/GrassInstanced");
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
        {
            shader = Shader.Find("Sprites/Default");
            Debug.LogWarning("[GrassOverlayGPU] Custom/GrassInstanced shader not found!");
        }

        instanceMaterial = new Material(shader);
        instanceMaterial.SetBuffer("_BladeBuffer", bladeBuffer);

        instanceMaterial.SetFloat("_ShadowDarken", shadowDarken);
        instanceMaterial.SetFloat("_HighlightBrighten", highlightBrighten);
        instanceMaterial.SetFloat("_AmbientOcclusion", ambientOcclusion);
        instanceMaterial.SetFloat("_TipHighlight", tipHighlight);
        instanceMaterial.SetFloat("_LightAngle", lightAngle);
        instanceMaterial.SetFloat("_SubsurfaceStrength", subsurfaceStrength);
        instanceMaterial.SetColor("_SubsurfaceColor", subsurfaceColor);
        instanceMaterial.SetFloat("_WindColorShift", windColorShift);
        instanceMaterial.SetFloat("_PatchScale", patchScale);
        instanceMaterial.SetFloat("_PatchStrength", patchStrength);
    }


    //  CLEANUP


    void Cleanup()
    {
        isInitialized = false;
        if (bladeBuffer != null) { bladeBuffer.Release(); bladeBuffer = null; }
        if (clumpBuffer != null) { clumpBuffer.Release(); clumpBuffer = null; }
        if (argsBuffer != null) { argsBuffer.Release(); argsBuffer = null; }
        if (instanceMaterial != null) { DestroyImmediate(instanceMaterial); instanceMaterial = null; }
        if (bladeMesh != null) { DestroyImmediate(bladeMesh); bladeMesh = null; }
    }

    void OnDisable() => Cleanup();
    void OnDestroy() => Cleanup();
}

