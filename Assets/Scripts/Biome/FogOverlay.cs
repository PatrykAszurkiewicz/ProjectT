using UnityEngine;
using System.Collections.Generic;


public class FogOverlay : MonoBehaviour
{
    [Header("Global Fog")]
    [Range(0f, 2f)] public float fogDensity = 0.7f;
    public Color fogColor = new Color(0.78f, 0.82f, 0.86f, 1.0f);
    public Color fogColorDeep = new Color(0.58f, 0.63f, 0.70f, 1.0f);
    public float fogRadius = 65f;

    [Header("Visibility Reduction")]
    public bool enableVisibilityReduction = true;
    [Range(0f, 0.6f)] public float visibilityReductionStrength = 0.35f;
    public Color visibilityColor = new Color(0.72f, 0.77f, 0.82f, 1.0f);

    [Header("Deep Fog Field (background haze)")]
    public int deepFogCount = 10;
    public float deepFogMinSize = 30f;
    public float deepFogMaxSize = 55f;
    [Range(0f, 1.5f)] public float deepFogDensity = 0.6f;
    public float deepScrollSpeed = 0.2f;
    public float deepNoiseScale = 0.35f;
    public float deepWarpStrength = 1.0f;

    [Header("Mid Fog Banks (main visible clouds)")]
    public int fogBankCount = 16;
    public float fogBankMinSize = 12f;
    public float fogBankMaxSize = 28f;
    [Range(0f, 1.5f)] public float fogBankDensity = 0.7f;
    public float fogBankDriftSpeed = 0.25f;
    public float bankScrollSpeed = 0.35f;
    public float bankNoiseScale = 0.6f;
    public float bankWarpStrength = 1.2f;
    public float fogBankVerticalDrift = 0.08f;

    [Header("Smoke Columns (rising steam pillars)")]
    public int smokeColumnCount = 30;
    public float smokeMinHeight = 20f;
    public float smokeMaxHeight = 50f;
    public float smokeMinWidth = 6f;
    public float smokeMaxWidth = 16f;
    public float smokeSpawnRadius = 60f;
    [Range(0f, 1.5f)] public float smokeDensity = 0.75f;
    public float smokeRiseSpeed = 0.2f;
    public float smokeBillowSpeed = 0.4f;
    public float smokeBillowAmount = 1.2f;
    public float smokeDissipation = 0.7f;
    public float smokeWindBend = 0.4f;
    public Color smokeColor = new Color(0.72f, 0.76f, 0.82f, 1.0f);
    public Color smokeDarkCore = new Color(0.48f, 0.52f, 0.58f, 1.0f);

    [Header("Near Wisps (foreground streaks)")]
    public int nearWispCount = 14;
    public float nearWispMinSize = 6f;
    public float nearWispMaxSize = 16f;
    [Range(0f, 1.5f)] public float nearWispDensity = 0.45f;
    public float nearWispDriftSpeed = 0.7f;
    public float nearScrollSpeed = 0.5f;
    public float nearNoiseScale = 1.0f;
    public float nearWarpStrength = 1.5f;

    [Header("Moisture Motes")]
    public int moistureCount = 600;
    public float moistureSpawnRadius = 60f;
    public float moistureMinHeight = -60f;
    public float moistureMaxHeight = 60f;
    public float moistureMinSize = 0.008f;
    public float moistureMaxSize = 0.03f;
    [Range(0f, 0.15f)] public float moistureAlpha = 0.05f;
    public float moistureDrift = 0.06f;

    [Header("Wind")]
    public float windAngle = 15f;
    public float windStrength = 1.0f;

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 50;



    private Vector2 windDir, windPerp;

    // Batched layers: one GameObject + one Mesh + one Material per layer
    private GameObject deepGO, bankGO, smokeGO, nearGO, moistGO;
    private Mesh deepMesh, bankMesh, smokeMesh, nearMesh, moistMesh;
    private Material deepMat, bankMat, smokeMat, nearMat, moistMat;

    // Per-element drift data
    private Vector2[] deepPos;
    private float[] deepPhase, deepSize;

    private Vector2[] bankPos;
    private float[] bankPhase, bankSize;

    private Vector2[] nearPos;
    private float[] nearPhase, nearSize;

    // Moisture motes
    private Vector2[] moistPos;
    private float[] moistSpeed, moistPhase, moistSize, moistDepth;
    private Vector3[] moistVerts;
    private Color[] moistCols;
    private float[] moistBaseAlpha;

    // Cached vertex arrays for batched fog layers (avoid re-alloc each frame)
    private Vector3[] deepVerts, bankVerts, nearVerts;

    private const int GRID_SIZE = 7;
    private const int VERTS_PER_QUAD = GRID_SIZE * GRID_SIZE; // 49


    void Start() => GenerateFog();

    [ContextMenu("Regenerate Fog")]
    public void GenerateFog()
    {
        Cleanup();

        float rad = windAngle * Mathf.Deg2Rad;
        windDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
        windPerp = new Vector2(-windDir.y, windDir.x);

        GenerateDeepFog();
        GenerateFogBanks();
        GenerateSmokeColumns();
        GenerateNearWisps();
        GenerateMoistureMotes();
    }


    //  MATERIAL FACTORY (one material per layer)

    Material CreateFogMaterial(float density, float noiseScale, float scrollSpeed,
                                float warpStrength, float wispiness, Color tint,
                                float densityFloor, float threshLow, float threshHigh)
    {
        Shader sh = Shader.Find("Custom/VolumetricFog2D");
        if (sh == null || sh.name == "Hidden/InternalErrorShader")
        {
            sh = Shader.Find("Sprites/Default");
            Debug.LogWarning("[FogOverlay] Custom/VolumetricFog2D shader not found!");
        }

        Material m = new Material(sh);
        m.SetColor("_FogColor", Color.Lerp(fogColor, tint, 0.3f));
        m.SetColor("_FogColorDeep", Color.Lerp(fogColorDeep, tint, 0.2f));
        m.SetFloat("_Density", density * fogDensity);
        m.SetFloat("_NoiseScale", noiseScale);
        m.SetFloat("_ScrollSpeed", scrollSpeed);
        m.SetVector("_WindDir", new Vector4(windDir.x, windDir.y, 0, 0));
        m.SetFloat("_WarpStrength", warpStrength);
        m.SetFloat("_WarpSpeed", 0.5f);
        m.SetFloat("_DetailScale", 2.5f);
        m.SetFloat("_DetailStrength", 0.4f);
        m.SetFloat("_Wispiness", wispiness);
        m.SetFloat("_ThresholdLow", threshLow);
        m.SetFloat("_ThresholdHigh", threshHigh);
        m.SetFloat("_DensityFloor", densityFloor);
        m.SetFloat("_PulseSpeed", 0.3f);
        m.SetFloat("_PulseAmount", 0.18f);
        m.SetFloat("_Phase", 0f); // per-vertex phase via UV2 now
        return m;
    }

    Material CreateSmokeMaterial()
    {
        Shader sh = Shader.Find("Custom/SmokeColumn2D");
        if (sh == null || sh.name == "Hidden/InternalErrorShader")
        {
            sh = Shader.Find("Sprites/Default");
            Debug.LogWarning("[FogOverlay] Custom/SmokeColumn2D shader not found!");
        }

        Material m = new Material(sh);
        m.SetColor("_SmokeColor", smokeColor);
        m.SetColor("_SmokeDark", smokeDarkCore);
        m.SetFloat("_Density", smokeDensity * fogDensity);
        m.SetFloat("_NoiseScale", 1.2f);
        m.SetFloat("_RiseSpeed", smokeRiseSpeed);
        m.SetFloat("_BillowSpeed", smokeBillowSpeed);
        m.SetFloat("_BillowAmount", smokeBillowAmount);
        m.SetFloat("_Dissipation", smokeDissipation);
        m.SetFloat("_WindBend", smokeWindBend);
        m.SetVector("_WindDir", new Vector4(windDir.x, windDir.y, 0, 0));
        m.SetFloat("_DetailScale", 3.0f);
        m.SetFloat("_DetailStrength", 0.4f);
        m.SetFloat("_InternalLight", 0.15f);
        m.SetVector("_LightDir", new Vector4(-0.7f, 0.7f, 0, 0));
        m.SetFloat("_Phase", 0f); // per-vertex phase via UV2
        m.SetFloat("_ThresholdLow", 0.08f);
        m.SetFloat("_ThresholdHigh", 0.72f);
        return m;
    }

    //  BATCHED FOG QUAD BUILDER Merges N fog quads into a single mesh (1 draw call).
    Mesh BuildBatchedFogMesh(Vector2[] centers, float[] sizes, float[] phases,
                              int count, out Vector3[] outVerts)
    {
        int trisPerQuad = (GRID_SIZE - 1) * (GRID_SIZE - 1) * 6;
        int totalVerts = count * VERTS_PER_QUAD;
        int totalTris = count * trisPerQuad;

        Vector3[] verts = new Vector3[totalVerts];
        Color[] cols = new Color[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        Vector2[] uv2s = new Vector2[totalVerts];
        int[] tris = new int[totalTris];

        for (int q = 0; q < count; q++)
        {
            float size = sizes[q];
            float phase = phases[q];
            float cx = centers[q].x;
            float cy = centers[q].y;
            int vBase = q * VERTS_PER_QUAD;
            int tBase = q * trisPerQuad;

            for (int y = 0; y < GRID_SIZE; y++)
            {
                for (int x = 0; x < GRID_SIZE; x++)
                {
                    int idx = vBase + y * GRID_SIZE + x;
                    float fx = (float)x / (GRID_SIZE - 1);
                    float fy = (float)y / (GRID_SIZE - 1);

                    verts[idx] = new Vector3(
                        cx + (fx - 0.5f) * size,
                        cy + (fy - 0.5f) * size * 0.7f,
                        0f
                    );

                    uvs[idx] = new Vector2(fx, fy);
                    uv2s[idx] = new Vector2(0f, phase);

                    float dx = fx - 0.5f;
                    float dy = fy - 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float alpha = 1f - Mathf.SmoothStep(0.5f, 1.0f, dist);

                    cols[idx] = new Color(1f, 1f, 1f, alpha);
                }
            }

            int ti = tBase;
            for (int y = 0; y < GRID_SIZE - 1; y++)
            {
                for (int x = 0; x < GRID_SIZE - 1; x++)
                {
                    int bl = vBase + y * GRID_SIZE + x;
                    int br = bl + 1;
                    int tl = bl + GRID_SIZE;
                    int tr = tl + 1;
                    tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
                    tris[ti++] = br; tris[ti++] = tl; tris[ti++] = tr;
                }
            }
        }

        Mesh mesh = new Mesh { name = "BatchedFogQuads" };
        if (totalVerts > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.uv = uvs;
        mesh.uv2 = uv2s;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * fogRadius * 4f);

        outVerts = verts;
        return mesh;
    }


    //  DEEP FOG (batched into 1 draw call)


    void GenerateDeepFog()
    {
        if (!enableVisibilityReduction) return;

        int count = deepFogCount;
        deepPos = new Vector2[count];
        deepPhase = new float[count];
        deepSize = new float[count];

        for (int i = 0; i < count; i++)
        {
            deepPos[i] = new Vector2(
                Random.Range(-fogRadius * 0.5f, fogRadius * 0.5f),
                Random.Range(-fogRadius * 0.35f, fogRadius * 0.35f)
            );
            deepPhase[i] = Random.Range(0f, 100f);
            deepSize[i] = Random.Range(deepFogMinSize, deepFogMaxSize);
        }

        float dens = deepFogDensity * visibilityReductionStrength;
        deepMat = CreateFogMaterial(dens, deepNoiseScale, deepScrollSpeed,
                                     deepWarpStrength, 1.8f, visibilityColor,
                                     0.12f, 0.08f, 0.60f);
        deepMesh = BuildBatchedFogMesh(deepPos, deepSize, deepPhase, count, out deepVerts);
        deepGO = CreateMeshObject("DeepFog_Batched", deepMesh, deepMat, sortingOrder - 2);
    }


    //  FOG BANKS (batched into 1 draw call)

    void GenerateFogBanks()
    {
        int count = fogBankCount;
        bankPos = new Vector2[count];
        bankPhase = new float[count];
        bankSize = new float[count];

        for (int i = 0; i < count; i++)
        {
            bankPos[i] = new Vector2(
                Random.Range(-fogRadius * 0.9f, fogRadius * 0.9f),
                Random.Range(-fogRadius * 0.5f, fogRadius * 0.5f)
            );
            bankPhase[i] = Random.Range(0f, 100f);
            bankSize[i] = Random.Range(fogBankMinSize, fogBankMaxSize);
        }

        bankMat = CreateFogMaterial(fogBankDensity, bankNoiseScale, bankScrollSpeed,
                                     bankWarpStrength, 1.4f, fogColor,
                                     0.06f, 0.12f, 0.75f);
        bankMesh = BuildBatchedFogMesh(bankPos, bankSize, bankPhase, count, out bankVerts);
        bankGO = CreateMeshObject("FogBanks_Batched", bankMesh, bankMat, sortingOrder - 1);
    }


    //  SMOKE COLUMNS (batched into 1 draw call)

    // Horizontal subdivisions for smoke columns — more columns = softer edges.
    // The old value of 2 (left + right only) caused sharp triangular edges.
    private const int SMOKE_H_SUBDIVS = 5;

    void GenerateSmokeColumns()
    {
        int count = smokeColumnCount;
        int segments = 12;
        int vertsPerCol = (segments + 1) * SMOKE_H_SUBDIVS;
        int trisPerCol = segments * (SMOKE_H_SUBDIVS - 1) * 6;
        int totalVerts = count * vertsPerCol;
        int totalTris = count * trisPerCol;

        Vector3[] verts = new Vector3[totalVerts];
        Color[] cols = new Color[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        Vector2[] uv2s = new Vector2[totalVerts];
        int[] tris = new int[totalTris];

        for (int c = 0; c < count; c++)
        {
            Vector2 basePos = new Vector2(
                Random.Range(-smokeSpawnRadius, smokeSpawnRadius),
                Random.Range(-smokeSpawnRadius * 0.8f, smokeSpawnRadius * 0.8f)
            );

            float height = Random.Range(smokeMinHeight, smokeMaxHeight);
            float width = Random.Range(smokeMinWidth, smokeMaxWidth);
            float phase = Random.Range(0f, 100f);

            int vBase = c * vertsPerCol;
            int tBase = c * trisPerCol;

            for (int s = 0; s <= segments; s++)
            {
                float t = (float)s / segments;
                float widthAtH = width * (0.4f + t * 1.6f);
                float halfW = widthAtH * 0.5f;
                float y = t * height;

                // Vertical alpha (fade in at bottom, solid middle, fade out at top)
                float vertAlpha;
                if (t < 0.1f) vertAlpha = t / 0.1f;
                else if (t < 0.75f) vertAlpha = 1.0f;
                else vertAlpha = 1.0f - (t - 0.75f) / 0.25f;
                vertAlpha = Mathf.Clamp01(vertAlpha);

                for (int h = 0; h < SMOKE_H_SUBDIVS; h++)
                {
                    float fx = (float)h / (SMOKE_H_SUBDIVS - 1); // 0..1 across width
                    int vi = vBase + s * SMOKE_H_SUBDIVS + h;

                    verts[vi] = new Vector3(
                        basePos.x + (fx - 0.5f) * widthAtH,
                        basePos.y + y,
                        0f
                    );

                    uvs[vi] = new Vector2(fx, t);
                    uv2s[vi] = new Vector2(widthAtH, phase);

                    // Horizontal edge falloff — smooth fade at left/right edges
                    float horzDist = Mathf.Abs(fx - 0.5f) * 2f; // 0 at center, 1 at edge
                    float horzAlpha = 1f - Mathf.SmoothStep(0.4f, 1.0f, horzDist);

                    cols[vi] = new Color(1f, 1f, 1f, vertAlpha * horzAlpha);
                }
            }

            int ti = tBase;
            for (int s = 0; s < segments; s++)
            {
                for (int h = 0; h < SMOKE_H_SUBDIVS - 1; h++)
                {
                    int bl = vBase + s * SMOKE_H_SUBDIVS + h;
                    int br = bl + 1;
                    int tl = bl + SMOKE_H_SUBDIVS;
                    int tr = tl + 1;
                    tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
                    tris[ti++] = br; tris[ti++] = tl; tris[ti++] = tr;
                }
            }
        }

        smokeMesh = new Mesh { name = "SmokeColumns_Batched" };
        if (totalVerts > 65535)
            smokeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        smokeMesh.vertices = verts;
        smokeMesh.colors = cols;
        smokeMesh.uv = uvs;
        smokeMesh.uv2 = uv2s;
        smokeMesh.triangles = tris;
        smokeMesh.RecalculateNormals();
        smokeMesh.bounds = new Bounds(Vector3.zero, Vector3.one * smokeSpawnRadius * 4f);

        smokeMat = CreateSmokeMaterial();
        smokeGO = CreateMeshObject("SmokeColumns_Batched", smokeMesh, smokeMat, sortingOrder);
    }


    //  NEAR WISPS (batched into 1 draw call)

    void GenerateNearWisps()
    {
        int count = nearWispCount;
        nearPos = new Vector2[count];
        nearPhase = new float[count];
        nearSize = new float[count];

        for (int i = 0; i < count; i++)
        {
            nearPos[i] = new Vector2(
                Random.Range(-fogRadius * 1.0f, fogRadius * 1.0f),
                Random.Range(-fogRadius * 0.4f, fogRadius * 0.4f)
            );
            nearPhase[i] = Random.Range(0f, 100f);
            nearSize[i] = Random.Range(nearWispMinSize, nearWispMaxSize);
        }

        nearMat = CreateFogMaterial(nearWispDensity, nearNoiseScale, nearScrollSpeed,
                                     nearWarpStrength, 1.1f, fogColor,
                                     0.05f, 0.15f, 0.78f);
        nearMesh = BuildBatchedFogMesh(nearPos, nearSize, nearPhase, count, out nearVerts);
        nearGO = CreateMeshObject("NearWisps_Batched", nearMesh, nearMat, sortingOrder + 1);
    }


    //  MOISTURE MOTES

    void GenerateMoistureMotes()
    {
        Shader sh = Shader.Find("Sprites/Default");
        moistMat = new Material(sh);

        int count = moistureCount;
        moistPos = new Vector2[count];
        moistSpeed = new float[count];
        moistPhase = new float[count];
        moistSize = new float[count];
        moistDepth = new float[count];

        int vertCount = count * 4;
        moistVerts = new Vector3[vertCount];
        moistCols = new Color[vertCount];
        moistBaseAlpha = new float[count];
        Vector2[] mUvs = new Vector2[vertCount];
        int[] mTris = new int[count * 6];

        for (int i = 0; i < count; i++)
        {
            float depth = Random.value;
            moistDepth[i] = depth;
            moistPos[i] = new Vector2(
                Random.Range(-moistureSpawnRadius, moistureSpawnRadius),
                Random.Range(moistureMinHeight, moistureMaxHeight)
            );
            moistSpeed[i] = Mathf.Lerp(0.5f, 0.1f, depth) * Random.Range(0.7f, 1.3f);
            moistPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            moistSize[i] = Mathf.Lerp(moistureMaxSize, moistureMinSize, depth) * Random.Range(0.5f, 1.5f);

            float baseAlpha = moistureAlpha * fogDensity * Mathf.Lerp(1.0f, 0.2f, depth) * Random.Range(0.5f, 1.0f);
            moistBaseAlpha[i] = baseAlpha;

            Color c = Color.Lerp(fogColor, Color.white, Random.Range(0.15f, 0.4f));
            c.a = baseAlpha;

            int v = i * 4;
            moistCols[v] = c; moistCols[v + 1] = c; moistCols[v + 2] = c; moistCols[v + 3] = c;
            mUvs[v] = new Vector2(0, 0); mUvs[v + 1] = new Vector2(1, 0);
            mUvs[v + 2] = new Vector2(1, 1); mUvs[v + 3] = new Vector2(0, 1);

            int t = i * 6;
            mTris[t] = v; mTris[t + 1] = v + 2; mTris[t + 2] = v + 1;
            mTris[t + 3] = v; mTris[t + 4] = v + 3; mTris[t + 5] = v + 2;
        }

        RefreshQuadVerts(moistPos, moistSize, moistVerts, count);

        moistMesh = new Mesh { name = "MoistureMesh" };
        moistMesh.vertices = moistVerts; moistMesh.colors = moistCols;
        moistMesh.uv = mUvs; moistMesh.triangles = mTris;
        moistMesh.RecalculateNormals();
        moistMesh.bounds = new Bounds(Vector3.zero, Vector3.one * moistureSpawnRadius * 4f);

        moistGO = CreateMeshObject("FogMoisture", moistMesh, moistMat, sortingOrder + 2);
    }


    //  ANIMATION — drift fog by moving vertices directly

    void Update()
    {
        float dt = Time.deltaTime;
        float t = Time.time;

        DriftBatchedLayer(deepMesh, deepVerts, deepPos, deepPhase, deepSize,
                          deepFogCount, 0.06f, 0.03f, fogRadius * 1.4f, dt, t);
        DriftBatchedLayer(bankMesh, bankVerts, bankPos, bankPhase, bankSize,
                          fogBankCount, fogBankDriftSpeed, fogBankVerticalDrift, fogRadius * 1.4f, dt, t);
        DriftBatchedLayer(nearMesh, nearVerts, nearPos, nearPhase, nearSize,
                          nearWispCount, nearWispDriftSpeed, 0.12f, fogRadius * 1.3f, dt, t);
        AnimateMoisture(dt, t);
    }

    void DriftBatchedLayer(Mesh mesh, Vector3[] verts, Vector2[] positions, float[] phases,
                            float[] sizes, int count, float driftSpeed, float vertDrift,
                            float boundary, float dt, float t)
    {
        if (mesh == null || verts == null || positions == null) return;

        for (int i = 0; i < count; i++)
        {
            float ph = phases[i];

            // Drift position
            positions[i].x += driftSpeed * windDir.x * windStrength * dt;
            positions[i].y += driftSpeed * windDir.y * windStrength * 0.15f * dt;
            positions[i].x += Mathf.Sin(t * 0.08f + ph) * 0.15f * dt;
            positions[i].y += Mathf.Sin(t * 0.06f + ph * 1.3f) * vertDrift * dt;

            // Wrap
            if (positions[i].x > boundary) positions[i].x -= boundary * 2f;
            else if (positions[i].x < -boundary) positions[i].x += boundary * 2f;
            if (positions[i].y > boundary * 0.6f) positions[i].y -= boundary * 1.2f;
            else if (positions[i].y < -boundary * 0.6f) positions[i].y += boundary * 1.2f;

            // Update vertices for this quad in the batched array
            float size = sizes[i];
            float cx = positions[i].x;
            float cy = positions[i].y;
            int vBase = i * VERTS_PER_QUAD;

            for (int y = 0; y < GRID_SIZE; y++)
            {
                for (int x = 0; x < GRID_SIZE; x++)
                {
                    int idx = vBase + y * GRID_SIZE + x;
                    float fx = (float)x / (GRID_SIZE - 1);
                    float fy = (float)y / (GRID_SIZE - 1);
                    verts[idx].x = cx + (fx - 0.5f) * size;
                    verts[idx].y = cy + (fy - 0.5f) * size * 0.7f;
                }
            }
        }

        mesh.vertices = verts;
    }

    void AnimateMoisture(float dt, float t)
    {
        if (moistPos == null || moistMesh == null) return;
        float spawnW = moistureSpawnRadius * 1.2f;

        for (int i = 0; i < moistureCount; i++)
        {
            float depth = moistDepth[i];
            float phase = moistPhase[i];
            float speed = moistSpeed[i];

            float wx = windStrength * Mathf.Lerp(1.0f, 0.3f, depth) * speed * 0.4f;
            moistPos[i].x += (wx * windDir.x + Mathf.Sin(t * 0.3f + phase) * moistureDrift) * dt;
            moistPos[i].y += (wx * windDir.y * 0.05f + Mathf.Cos(t * 0.25f + phase * 1.3f) * moistureDrift * 0.4f) * dt;

            if (moistPos[i].x > spawnW) moistPos[i].x -= spawnW * 2f;
            else if (moistPos[i].x < -spawnW) moistPos[i].x += spawnW * 2f;
            if (moistPos[i].y > moistureMaxHeight + 2f) moistPos[i].y = moistureMinHeight;
            else if (moistPos[i].y < moistureMinHeight - 2f) moistPos[i].y = moistureMaxHeight;

            float shimmer = 0.3f + 0.7f * Mathf.Sin(t * 1.5f + phase * 7f);
            int v = i * 4;
            float a = moistBaseAlpha[i] * shimmer;
            moistCols[v].a = a; moistCols[v + 1].a = a;
            moistCols[v + 2].a = a; moistCols[v + 3].a = a;
        }

        RefreshQuadVerts(moistPos, moistSize, moistVerts, moistureCount);
        moistMesh.vertices = moistVerts;
        moistMesh.colors = moistCols;
    }


    //  HELPERS

    GameObject CreateMeshObject(string name, Mesh mesh, Material mat, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>().mesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = order;
        return go;
    }

    void RefreshQuadVerts(Vector2[] positions, float[] sizes, Vector3[] v, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float s = sizes[i]; float px = positions[i].x; float py = positions[i].y;
            int vi = i * 4;
            v[vi] = new Vector3(px - s, py - s, 0f); v[vi + 1] = new Vector3(px + s, py - s, 0f);
            v[vi + 2] = new Vector3(px + s, py + s, 0f); v[vi + 3] = new Vector3(px - s, py + s, 0f);
        }
    }

    void Cleanup()
    {
        SafeDestroy(deepGO); SafeDestroy(deepMesh); SafeDestroy(deepMat);
        SafeDestroy(bankGO); SafeDestroy(bankMesh); SafeDestroy(bankMat);
        SafeDestroy(smokeGO); SafeDestroy(smokeMesh); SafeDestroy(smokeMat);
        SafeDestroy(nearGO); SafeDestroy(nearMesh); SafeDestroy(nearMat);
        SafeDestroy(moistGO); SafeDestroy(moistMesh); SafeDestroy(moistMat);

        deepGO = bankGO = smokeGO = nearGO = moistGO = null;
        deepMesh = bankMesh = smokeMesh = nearMesh = moistMesh = null;
        deepMat = bankMat = smokeMat = nearMat = moistMat = null;
        deepVerts = bankVerts = nearVerts = null;
    }

    void SafeDestroy(Object obj)
    {
        if (obj != null) DestroyImmediate(obj);
    }

    void OnDestroy() => Cleanup();
}
