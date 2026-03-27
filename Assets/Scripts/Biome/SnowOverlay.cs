using UnityEngine;
using System.Collections.Generic;


public class SnowOverlay : MonoBehaviour
{
    //  AIRBORNE SNOW

    [Header("Airborne Snow")]
    public int particleCount = 3500;
    public float fallSpeed = 0.5f;
    public float drift = 0.6f;
    public float spawnHeight = 30f;
    public float spawnRadius = 60f;

    [Header("Flake Types")]
    [Range(0f, 0.5f)] public float powderRatio = 0.35f;
    [Range(0f, 0.3f)] public float clumpRatio = 0.12f;
    [Range(0f, 0.15f)] public float crystalFlakeRatio = 0.08f;

    //  GROUND SNOW 

    [Header("Ground Snow")]
    public int groundCount = 150000;
    public float groundRadius = 60f;
    public float groundCoreExclusion = 1.5f;

    //  SNOWDRIFTS 

    [Header("Snowdrifts")]
    public int driftCount = 280;
    public float driftMinLength = 0.4f;
    public float driftMaxLength = 1.8f;
    public float driftMinWidth = 0.08f;
    public float driftMaxWidth = 0.3f;
    public float driftWindAlignment = 0.7f;
    public float driftWindAngle = -20f;
    public Color driftColorBright = new Color(0.96f, 0.98f, 1.0f, 0.92f);
    public Color driftColorShadow = new Color(0.78f, 0.83f, 0.92f, 0.80f);

    //  ICE CRYSTAL PATCHES 

    [Header("Ice Crystal Patches")]
    public int icePatchCount = 1200;
    public float icePatchMinSize = 0.015f;
    public float icePatchMaxSize = 0.06f;
    public Color iceColorBase = new Color(0.85f, 0.92f, 1.0f, 0.70f);
    public Color iceColorGlint = new Color(1.0f, 1.0f, 1.0f, 0.95f);

    //  FROST VEGETATION 

    [Header("Frost Vegetation")]
    public int frostVegCount = 600;
    public float frostVegMinHeight = 0.06f;
    public float frostVegMaxHeight = 0.18f;
    public float frostVegWidth = 0.008f;
    public Color frostVegBase = new Color(0.25f, 0.22f, 0.18f, 0.85f);
    public Color frostVegTip = new Color(0.75f, 0.82f, 0.92f, 0.70f);
    public Color frostVegIce = new Color(0.88f, 0.93f, 1.0f, 0.60f);

    //  APPEARANCE & COLORS

    [Header("Flake Sizes")]
    public float flakeMinSize = 0.02f;
    public float flakeMaxSize = 0.08f;
    public float groundPatchMinSize = 0.05f;
    public float groundPatchMaxSize = 0.16f;

    [Header("Colors")]
    public Color colorBright = new Color(0.95f, 0.97f, 1.0f, 0.90f);
    public Color colorMid = new Color(0.82f, 0.86f, 0.92f, 0.75f);
    public Color colorShadow = new Color(0.62f, 0.68f, 0.78f, 0.60f);
    public Color groundTint = new Color(0.88f, 0.91f, 0.96f, 0.50f);

    //  WIND

    [Header("Wind")]
    public float windStrength = 1.8f;
    public float windSpeed = 1.5f;
    public float gustStrength = 0.8f;
    public float gustSpeed = 0.4f;
    public float turbulence = 1.8f;
    public float swirl = 0.6f;

    //  SORTING

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = -1;


    //  INTERNALS

    private const int MAX_VERTS_PER_MESH = 60000;

    private const int GROUND_VERTS = 8;
    private const int GROUND_TRIS_IDX = 18;

    private const int FLAKE_VERTS = 4;
    private const int FLAKE_TRIS_IDX = 6;

    private const int DRIFT_VERTS = 12;
    private const int DRIFT_TRIS_IDX = 33; // 11 tris × 3

    private const int ICE_VERTS = 7;
    private const int ICE_TRIS_IDX = 18;

    private const int FVEG_VERTS = 7;
    private const int FVEG_TRIS_IDX = 15;

    // Ground layer
    private List<Mesh> groundMeshes = new List<Mesh>();
    private List<GameObject> groundObjects = new List<GameObject>();
    private Material groundMaterial;

    // Snowdrift layer
    private List<Mesh> driftMeshes = new List<Mesh>();
    private List<GameObject> driftObjects = new List<GameObject>();
    private Material driftMaterial;

    // Ice crystal layer
    private List<Mesh> iceMeshes = new List<Mesh>();
    private List<GameObject> iceObjects = new List<GameObject>();
    private Material iceMaterial;

    // Frost vegetation layer
    private List<Mesh> frostVegMeshes = new List<Mesh>();
    private List<GameObject> frostVegObjects = new List<GameObject>();
    private Material frostVegMaterial;

    // Airborne layer
    private Mesh flakeMesh;
    private GameObject flakeObject;
    private Material flakeMaterial;

    // Per-flake CPU state
    private Vector2[] flakePos;
    private float[] flakeSpeed;
    private float[] flakePhase;
    private float[] flakeSize;
    private float[] flakeDepth;
    private int[] flakeType;
    private float[] flakeRotation;
    private float[] flakeRotSpeed;
    private Vector3[] flakeVerts;
    private float killHeight;


    void Start() => GenerateSnow();

    [ContextMenu("Regenerate Snow")]
    public void GenerateSnow()
    {
        Cleanup();
        CreateMaterials();
        GenerateGroundSnow();
        GenerateSnowdrifts();
        GenerateIceCrystals();
        GenerateFrostVegetation();
        GenerateAirborneSnow();
    }


    //  MATERIALS

    void CreateMaterials()
    {
        Shader snowShader = Shader.Find("Custom/SnowWind");
        if (snowShader == null || snowShader.name == "Hidden/InternalErrorShader")
        {
            snowShader = Shader.Find("Sprites/Default");
            Debug.LogWarning("[SnowOverlay] Custom/SnowWind shader not found, using fallback.");
        }

        groundMaterial = new Material(snowShader);
        driftMaterial = new Material(snowShader);
        iceMaterial = new Material(snowShader);
        frostVegMaterial = new Material(snowShader);
        flakeMaterial = new Material(snowShader);

        // Ice material gets extra sparkle
        iceMaterial.SetFloat("_ShimmerStrength", 0.25f);
        iceMaterial.SetFloat("_ShimmerSpeed", 4.0f);
        iceMaterial.SetFloat("_CrystalGlint", 1.0f);

        // Drift material gets softer edges
        driftMaterial.SetFloat("_Softness", 0.45f);
    }


    //  GROUND SNOW 

    void GenerateGroundSnow()
    {
        int patchesPerMesh = MAX_VERTS_PER_MESH / GROUND_VERTS;
        int meshCount = Mathf.CeilToInt((float)groundCount / patchesPerMesh);
        int remaining = groundCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(patchesPerMesh, remaining);
            Mesh mesh = BuildGroundMesh(count, offset);
            groundMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"SnowGround_{m}", mesh, groundMaterial, sortingOrder);
            groundObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    Mesh BuildGroundMesh(int count, int seedOffset)
    {
        int maxV = count * GROUND_VERTS;
        int maxT = count * GROUND_TRIS_IDX;

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;

        float windRad = driftWindAngle * Mathf.Deg2Rad;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 7919);

            Vector2 pos = GetRandomDiscPosition(groundRadius, groundCoreExclusion);

            float distNorm = pos.magnitude / groundRadius;
            float sizeBase = Mathf.Lerp(groundPatchMaxSize, groundPatchMinSize, distNorm * 0.7f);
            float size = sizeBase * Random.Range(0.4f, 1.6f);

            float angleOffset = Random.Range(0f, Mathf.PI * 2f);
            float phase = Random.value;

            // Multi-octave Perlin for natural layered snow patterns
            float n1 = Mathf.PerlinNoise(pos.x * 0.3f + 100f, pos.y * 0.3f + 100f);
            float n2 = Mathf.PerlinNoise(pos.x * 0.8f + 200f, pos.y * 0.8f + 200f);
            float n3 = Mathf.PerlinNoise(pos.x * 1.5f + 300f, pos.y * 1.5f + 300f);
            float combined = n1 * 0.5f + n2 * 0.3f + n3 * 0.2f;

            Color c;
            if (combined < 0.3f)
            {
                c = Color.Lerp(colorShadow, groundTint, Random.Range(0f, 0.4f));
                c.a *= Random.Range(0.55f, 0.85f);
            }
            else if (combined < 0.55f)
            {
                c = Color.Lerp(groundTint, colorMid, Random.Range(0.2f, 0.6f));
                c.a *= Random.Range(0.50f, 0.90f);
            }
            else if (combined < 0.8f)
            {
                c = Color.Lerp(colorMid, colorBright, Random.Range(0.3f, 0.7f));
                c.a *= Random.Range(0.60f, 1.0f);
            }
            else
            {
                c = Color.Lerp(colorBright, new Color(0.92f, 0.95f, 1.0f, 0.95f), Random.Range(0.2f, 0.5f));
                c.a *= Random.Range(0.70f, 1.0f);
            }

            // Cold blue tint in shadows
            float shadowInfluence = Mathf.Clamp01(1f - combined);
            c.b += shadowInfluence * 0.04f;
            c.r -= shadowInfluence * 0.02f;

            c.a *= Random.Range(0.45f, 1.0f);

            // Center vertex
            verts[vi] = V3(pos);
            cols[vi] = c;
            uvs[vi] = new Vector2(0.5f, 0.5f);
            uv2s[vi] = new Vector2(0f, phase);

            // 7 perimeter verts with wind-aligned stretching
            for (int p = 0; p < 7; p++)
            {
                float a = angleOffset + (p / 7f) * Mathf.PI * 2f;

                float windBias = 1f + 0.2f * Mathf.Abs(Mathf.Cos(a - windRad));
                float r = size * (0.6f + Random.Range(0f, 0.7f)) * windBias;
                Vector2 pv = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

                Vector2 localDir = (pv - pos).normalized;
                verts[vi + 1 + p] = V3(pv);

                Color edgeC = c;
                edgeC.a *= Random.Range(0.15f, 0.50f);
                cols[vi + 1 + p] = edgeC;

                uvs[vi + 1 + p] = new Vector2(0.5f + localDir.x * 0.5f, 0.5f + localDir.y * 0.5f);
                uv2s[vi + 1 + p] = new Vector2(0f, phase);
            }

            for (int p = 0; p < 6; p++)
            {
                tris[ti++] = vi;
                tris[ti++] = vi + 1 + p;
                tris[ti++] = vi + 1 + ((p + 1) % 7);
            }

            vi += GROUND_VERTS;
        }

        return BuildMesh("SnowGroundMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  SNOWDRIFTS

    void GenerateSnowdrifts()
    {
        int driftsPerMesh = MAX_VERTS_PER_MESH / DRIFT_VERTS;
        int meshCount = Mathf.CeilToInt((float)driftCount / driftsPerMesh);
        int remaining = driftCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(driftsPerMesh, remaining);
            Mesh mesh = BuildDriftMesh(count, offset);
            driftMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"SnowDrift_{m}", mesh, driftMaterial, sortingOrder + 1);
            driftObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    Mesh BuildDriftMesh(int count, int seedOffset)
    {
        int maxV = count * DRIFT_VERTS;
        int maxT = count * DRIFT_TRIS_IDX;

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;
        float windRad = driftWindAngle * Mathf.Deg2Rad;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 31337);

            Vector2 pos = GetRandomDiscPosition(groundRadius * 0.9f, groundCoreExclusion * 1.5f);
            float phase = Random.value;

            float length = Random.Range(driftMinLength, driftMaxLength);
            float width = Random.Range(driftMinWidth, driftMaxWidth);

            // Perpendicular to wind = ridge direction
            float baseAngle = windRad + Mathf.PI * 0.5f;
            float randomAngle = Random.Range(-Mathf.PI * 0.5f, Mathf.PI * 0.5f);
            float angle = Mathf.Lerp(randomAngle, baseAngle, driftWindAlignment);

            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);

            float distNorm = pos.magnitude / groundRadius;
            Color bright = Color.Lerp(driftColorBright, driftColorShadow, distNorm * 0.3f);
            Color shadow = Color.Lerp(driftColorShadow, colorShadow, distNorm * 0.2f);

            bright.b = Mathf.Min(bright.b + 0.02f, 1f);
            shadow.b = Mathf.Min(shadow.b + 0.04f, 1f);

            // Center vertex
            Color centerCol = Color.Lerp(bright, shadow, 0.3f);
            centerCol.a = Random.Range(0.75f, 0.95f);
            verts[vi] = V3(pos);
            cols[vi] = centerCol;
            uvs[vi] = new Vector2(0.5f, 0.5f);
            uv2s[vi] = new Vector2(0f, phase);

            int perimCount = DRIFT_VERTS - 1;
            for (int p = 0; p < perimCount; p++)
            {
                float t = (float)p / perimCount * Mathf.PI * 2f;

                float localX = Mathf.Cos(t) * length * 0.5f;
                float localY = Mathf.Sin(t) * width * 0.5f;

                // Organic irregularity
                float noise = 1f + Mathf.Sin(t * 3f + phase * 20f) * 0.15f
                             + Mathf.Sin(t * 7f + phase * 40f) * 0.08f;
                localX *= noise;
                localY *= noise;

                float wx = localX * cosA - localY * sinA;
                float wy = localX * sinA + localY * cosA;

                Vector2 pv = pos + new Vector2(wx, wy);
                verts[vi + 1 + p] = V3(pv);

                // Windward side bright, leeward dark
                float windDot = Mathf.Cos(t - windRad) * 0.5f + 0.5f;
                Color edgeCol = Color.Lerp(shadow, bright, windDot);
                edgeCol.a *= Random.Range(0.10f, 0.35f);

                cols[vi + 1 + p] = edgeCol;

                Vector2 localDir = (pv - pos).normalized;
                uvs[vi + 1 + p] = new Vector2(0.5f + localDir.x * 0.5f, 0.5f + localDir.y * 0.5f);
                uv2s[vi + 1 + p] = new Vector2(0f, phase);
            }

            // Fan triangles
            for (int p = 0; p < perimCount - 1; p++)
            {
                tris[ti++] = vi;
                tris[ti++] = vi + 1 + p;
                tris[ti++] = vi + 1 + p + 1;
            }
            tris[ti++] = vi;
            tris[ti++] = vi + perimCount;
            tris[ti++] = vi + 1;

            vi += DRIFT_VERTS;
        }

        return BuildMesh("SnowDriftMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  ICE CRYSTAL PATCHES 

    void GenerateIceCrystals()
    {
        int patchesPerMesh = MAX_VERTS_PER_MESH / ICE_VERTS;
        int meshCount = Mathf.CeilToInt((float)icePatchCount / patchesPerMesh);
        int remaining = icePatchCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(patchesPerMesh, remaining);
            Mesh mesh = BuildIceMesh(count, offset);
            iceMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"SnowIce_{m}", mesh, iceMaterial, sortingOrder + 1);
            iceObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    Mesh BuildIceMesh(int count, int seedOffset)
    {
        int maxV = count * ICE_VERTS;
        int maxT = count * ICE_TRIS_IDX;

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 54321);

            Vector2 pos = GetRandomDiscPosition(groundRadius * 0.85f, groundCoreExclusion);

            float size = Random.Range(icePatchMinSize, icePatchMaxSize);
            float phase = Random.value;
            float angleOff = Random.Range(0f, Mathf.PI / 3f);

            Color centerCol = Color.Lerp(iceColorBase, iceColorGlint, Random.Range(0.3f, 0.8f));
            centerCol.a = Random.Range(0.50f, 0.85f);

            verts[vi] = V3(pos);
            cols[vi] = centerCol;
            uvs[vi] = new Vector2(0.5f, 0.5f);
            uv2s[vi] = new Vector2(1f, phase); // depth=1 signals ice crystal to shader

            for (int p = 0; p < 6; p++)
            {
                float a = angleOff + (p / 6f) * Mathf.PI * 2f;
                float r = size * (0.8f + Random.Range(0f, 0.4f));
                Vector2 pv = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

                verts[vi + 1 + p] = V3(pv);

                Color edgeCol = iceColorBase;
                edgeCol.a *= Random.Range(0.15f, 0.45f);
                cols[vi + 1 + p] = edgeCol;

                Vector2 localDir = (pv - pos).normalized;
                uvs[vi + 1 + p] = new Vector2(0.5f + localDir.x * 0.5f, 0.5f + localDir.y * 0.5f);
                uv2s[vi + 1 + p] = new Vector2(1f, phase);
            }

            for (int p = 0; p < 5; p++)
            {
                tris[ti++] = vi;
                tris[ti++] = vi + 1 + p;
                tris[ti++] = vi + 2 + p;
            }
            tris[ti++] = vi;
            tris[ti++] = vi + 6;
            tris[ti++] = vi + 1;

            vi += ICE_VERTS;
        }

        return BuildMesh("SnowIceMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  FROST VEGETATION 

    void GenerateFrostVegetation()
    {
        int vegsPerMesh = MAX_VERTS_PER_MESH / FVEG_VERTS;
        int meshCount = Mathf.CeilToInt((float)frostVegCount / vegsPerMesh);
        int remaining = frostVegCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(vegsPerMesh, remaining);
            Mesh mesh = BuildFrostVegMesh(count, offset);
            frostVegMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"SnowFrostVeg_{m}", mesh, frostVegMaterial, sortingOrder + 1);
            frostVegObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    Mesh BuildFrostVegMesh(int count, int seedOffset)
    {
        int maxV = count * FVEG_VERTS;
        int maxT = count * FVEG_TRIS_IDX;

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 99991);

            Vector2 pos = GetRandomDiscPosition(groundRadius * 0.8f, groundCoreExclusion * 1.2f);

            float h = Random.Range(frostVegMinHeight, frostVegMaxHeight);
            float w = frostVegWidth * Random.Range(0.6f, 1.5f);
            float lean = Random.Range(-0.3f, 0.3f);
            float curve = Random.Range(-0.15f, 0.15f) * h;
            float phase = Random.value;

            bool hasIce = Random.value < 0.4f;

            float[] segH = { 0f, 0.3f, 0.7f, 1.0f };
            float[] segW = { 1.0f, 0.7f, 0.35f, 0.0f };
            float[] segCurve = { 0f, 0.1f, 0.5f, 1.0f };

            Vector2 perp = new Vector2(1f, 0f);

            for (int s = 0; s < 4; s++)
            {
                float t = segH[s];
                float sy = t * h;
                float sx = lean * t + curve * segCurve[s];

                Vector2 center = pos + new Vector2(sx, sy);

                Color segCol;
                if (t < 0.4f)
                    segCol = Color.Lerp(frostVegBase, frostVegTip, t * 2f);
                else
                    segCol = Color.Lerp(frostVegTip, hasIce ? frostVegIce : frostVegTip, (t - 0.4f) * 1.5f);

                float frostNoise = Mathf.PerlinNoise(pos.x * 5f + t * 10f, pos.y * 5f);
                if (hasIce && frostNoise > 0.5f)
                    segCol = Color.Lerp(segCol, frostVegIce, (frostNoise - 0.5f) * 0.8f);

                if (s < 3)
                {
                    float sw = w * segW[s] * 0.5f;
                    verts[vi + s * 2 + 0] = V3(center - perp * sw);
                    verts[vi + s * 2 + 1] = V3(center + perp * sw);
                    cols[vi + s * 2 + 0] = segCol;
                    cols[vi + s * 2 + 1] = segCol;
                    uvs[vi + s * 2 + 0] = new Vector2(0f, t);
                    uvs[vi + s * 2 + 1] = new Vector2(1f, t);
                    uv2s[vi + s * 2 + 0] = new Vector2(0f, phase);
                    uv2s[vi + s * 2 + 1] = new Vector2(0f, phase);
                }
                else
                {
                    int tipIdx = vi + 6;
                    verts[tipIdx] = V3(center);
                    Color tipCol = segCol;
                    tipCol.a *= 0.6f;
                    cols[tipIdx] = tipCol;
                    uvs[tipIdx] = new Vector2(0.5f, 1f);
                    uv2s[tipIdx] = new Vector2(0f, phase);
                }
            }

            for (int s = 0; s < 2; s++)
            {
                int bl = vi + s * 2;
                int br = bl + 1;
                int tl = bl + 2;
                int tr = bl + 3;
                tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
                tris[ti++] = br; tris[ti++] = tl; tris[ti++] = tr;
            }
            {
                int lastPair = vi + 4;
                int tipIdx = vi + 6;
                tris[ti++] = lastPair;
                tris[ti++] = tipIdx;
                tris[ti++] = lastPair + 1;
            }

            vi += FVEG_VERTS;
        }

        return BuildMesh("SnowFrostVegMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  AIRBORNE SNOW — multiple flake types with rotation

    void GenerateAirborneSnow()
    {
        killHeight = -spawnHeight;

        flakePos = new Vector2[particleCount];
        flakeSpeed = new float[particleCount];
        flakePhase = new float[particleCount];
        flakeSize = new float[particleCount];
        flakeDepth = new float[particleCount];
        flakeType = new int[particleCount];
        flakeRotation = new float[particleCount];
        flakeRotSpeed = new float[particleCount];

        int vertCount = particleCount * FLAKE_VERTS;
        flakeVerts = new Vector3[vertCount];
        Color[] cols = new Color[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        Vector2[] uv2s = new Vector2[vertCount];
        int[] tris = new int[particleCount * FLAKE_TRIS_IDX];

        int powderCount = Mathf.RoundToInt(particleCount * powderRatio);
        int clumpCountVal = Mathf.RoundToInt(particleCount * clumpRatio);
        int crystalCount = Mathf.RoundToInt(particleCount * crystalFlakeRatio);

        for (int i = 0; i < particleCount; i++)
        {
            float depth = Random.value;
            flakeDepth[i] = depth;

            int type = 0;
            if (i < powderCount) type = 1;
            else if (i < powderCount + clumpCountVal) type = 2;
            else if (i < powderCount + clumpCountVal + crystalCount) type = 3;
            flakeType[i] = type;

            flakePos[i] = new Vector2(
                Random.Range(-spawnRadius, spawnRadius),
                Random.Range(killHeight, spawnHeight)
            );

            float speedMult, sizeMult;
            Color c;

            switch (type)
            {
                case 1: // POWDER — tiny, fast, dense
                    speedMult = Mathf.Lerp(1.6f, 0.6f, depth) * Random.Range(0.9f, 1.3f);
                    sizeMult = 0.35f * Random.Range(0.5f, 1.2f);
                    c = Color.Lerp(colorBright, colorMid, depth * 0.4f);
                    c.a = Mathf.Lerp(0.70f, 0.25f, depth);
                    break;

                case 2: // CLUMP — big, slow, fluffy
                    speedMult = Mathf.Lerp(0.7f, 0.2f, depth) * Random.Range(0.7f, 1.1f);
                    sizeMult = 2.2f * Random.Range(0.7f, 1.5f);
                    c = Color.Lerp(colorBright, colorMid, depth * 0.3f);
                    c.a = Mathf.Lerp(0.65f, 0.30f, depth);
                    break;

                case 3: // CRYSTAL — medium, glinting
                    speedMult = Mathf.Lerp(1.1f, 0.4f, depth) * Random.Range(0.8f, 1.2f);
                    sizeMult = 0.8f * Random.Range(0.6f, 1.4f);
                    c = Color.Lerp(new Color(0.95f, 0.98f, 1.0f, 0.95f), colorMid, depth * 0.3f);
                    c.a = Mathf.Lerp(0.92f, 0.40f, depth);
                    break;

                default: // NORMAL
                    speedMult = Mathf.Lerp(1.3f, 0.3f, depth) * Random.Range(0.8f, 1.2f);
                    sizeMult = 1f * Random.Range(0.6f, 1.4f);
                    c = Color.Lerp(colorBright, colorShadow, depth * 0.65f);
                    if (depth < 0.25f) c = Color.Lerp(c, colorBright, 0.5f);
                    c.a = Mathf.Lerp(0.88f, 0.35f, depth);
                    break;
            }

            flakeSpeed[i] = speedMult;
            flakePhase[i] = Random.Range(0f, Mathf.PI * 2f);
            flakeRotation[i] = Random.Range(0f, Mathf.PI * 2f);
            flakeRotSpeed[i] = Random.Range(-1.5f, 1.5f) * (type == 3 ? 2f : type == 2 ? 0.3f : 1f);

            float s = Mathf.Lerp(flakeMaxSize, flakeMinSize, depth) * sizeMult;
            flakeSize[i] = s;

            float cVar = Random.Range(-0.04f, 0.04f);
            c.r = Mathf.Clamp01(c.r + cVar);
            c.g = Mathf.Clamp01(c.g + cVar);
            c.b = Mathf.Clamp01(c.b + cVar * 0.5f);

            int vi = i * 4;
            cols[vi + 0] = c; cols[vi + 1] = c;
            cols[vi + 2] = c; cols[vi + 3] = c;

            uvs[vi + 0] = new Vector2(0f, 0f);
            uvs[vi + 1] = new Vector2(1f, 0f);
            uvs[vi + 2] = new Vector2(1f, 1f);
            uvs[vi + 3] = new Vector2(0f, 1f);

            float phaseNorm = flakePhase[i] / (Mathf.PI * 2f);
            uv2s[vi + 0] = new Vector2(depth, phaseNorm);
            uv2s[vi + 1] = new Vector2(depth, phaseNorm);
            uv2s[vi + 2] = new Vector2(depth, phaseNorm);
            uv2s[vi + 3] = new Vector2(depth, phaseNorm);

            int triIdx = i * 6;
            tris[triIdx + 0] = vi; tris[triIdx + 1] = vi + 2; tris[triIdx + 2] = vi + 1;
            tris[triIdx + 3] = vi; tris[triIdx + 4] = vi + 3; tris[triIdx + 5] = vi + 2;
        }

        RefreshFlakeVerts();

        flakeMesh = new Mesh();
        flakeMesh.name = "SnowFlakeMesh";
        flakeMesh.vertices = flakeVerts;
        flakeMesh.colors = cols;
        flakeMesh.uv = uvs;
        flakeMesh.uv2 = uv2s;
        flakeMesh.triangles = tris;
        flakeMesh.RecalculateNormals();
        flakeMesh.bounds = new Bounds(Vector3.zero, Vector3.one * spawnRadius * 4f);

        flakeObject = new GameObject("SnowFalling");
        flakeObject.transform.SetParent(transform);
        flakeObject.transform.localPosition = Vector3.zero;
        flakeObject.transform.localScale = Vector3.one;

        flakeObject.AddComponent<MeshFilter>().mesh = flakeMesh;
        MeshRenderer mr = flakeObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = flakeMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder + 2;
    }


    //  ANIMATION

    void Update()
    {
        if (flakePos == null || flakeMesh == null) return;

        float dt = Time.deltaTime;
        float t = Time.time;
        float spawnW = spawnRadius * 1.2f;

        for (int i = 0; i < particleCount; i++)
        {
            float depth = flakeDepth[i];
            float phase = flakePhase[i];
            float speed = flakeSpeed[i];
            int type = flakeType[i];

            // Update visual rotation
            flakeRotation[i] += flakeRotSpeed[i] * dt;

            // 1. CONSTANT WIND
            float windX = -windStrength * Mathf.Lerp(1.0f, 0.25f, depth);

            // Type-specific wind response
            float typeDriftMult = 1f;
            float typeFallMult = 1f;
            switch (type)
            {
                case 1: typeDriftMult = 1.4f; typeFallMult = 1.2f; break;
                case 2: typeDriftMult = 0.6f; typeFallMult = 0.7f; break;
                case 3: typeDriftMult = 1.2f; typeFallMult = 1.0f; break;
            }

            // 2. SINE DRIFT
            float driftFreq = 0.4f + Mathf.Abs(Mathf.Sin(phase)) * 1.2f;
            float driftX = Mathf.Sin(t * windSpeed * driftFreq + phase) * drift * 0.5f * typeDriftMult;

            // 3. GUSTS
            float gx = flakePos[i].x * 0.15f;
            float gy = flakePos[i].y * 0.15f;
            float gt = t * gustSpeed;
            float gust = Mathf.Sin(gx * 0.7f + gy * 0.4f + gt) * 0.5f
                       + Mathf.Sin(gx * 0.3f - gy * 0.9f + gt * 1.3f) * 0.3f
                       + Mathf.Sin(gx * 0.5f + gy * 0.6f - gt * 0.5f) * 0.2f;

            float gustPulse = 0.3f + 0.7f * (0.5f + 0.5f * Mathf.Sin(gt * 0.2f + flakePos[i].x * 0.06f));
            float gustTotal = gust * gustStrength * gustPulse;

            // 4. TURBULENCE
            float turbScale = turbulence * typeDriftMult;
            float turbX = Mathf.Sin(t * turbScale * 3.2f + phase * 2.1f) * turbScale * 0.04f;
            float turbY = Mathf.Cos(t * turbScale * 2.5f + phase * 1.7f) * turbScale * 0.025f;

            // 5. SWIRL
            float swirlX = Mathf.Sin(t * swirl * 1.3f + phase) * swirl * 0.12f;
            float swirlY = Mathf.Cos(t * swirl * 0.9f + phase * 1.4f) * swirl * 0.06f;

            // 6. FALL
            float fallY = -fallSpeed * speed * typeFallMult;

            // Combine
            float totalX = windX + driftX + gustTotal + turbX + swirlX;
            float totalY = fallY + turbY + swirlY;

            flakePos[i].x += totalX * dt;
            flakePos[i].y += totalY * dt;

            // 7. RESPAWN
            bool respawn = false;

            if (flakePos[i].x < -spawnW)
            {
                flakePos[i].x = spawnW - Random.Range(0f, 2f);
                respawn = true;
            }
            else if (flakePos[i].x > spawnW)
            {
                flakePos[i].x = -spawnW + Random.Range(0f, 2f);
                respawn = true;
            }

            if (flakePos[i].y < killHeight)
            {
                flakePos[i].y = spawnHeight + Random.Range(0f, 3f);
                flakePos[i].x = Random.Range(-spawnW, spawnW);
                respawn = true;
            }
            else if (flakePos[i].y > spawnHeight + 4f)
            {
                flakePos[i].y = killHeight + Random.Range(0f, 2f);
                respawn = true;
            }

            if (respawn)
            {
                flakePhase[i] = Random.Range(0f, Mathf.PI * 2f);
            }
        }

        RefreshFlakeVerts();
        flakeMesh.vertices = flakeVerts;
    }

    void RefreshFlakeVerts()
    {
        for (int i = 0; i < particleCount; i++)
        {
            float s = flakeSize[i];
            float px = flakePos[i].x;
            float py = flakePos[i].y;
            int vi = i * 4;

            int type = flakeType[i];
            if (type == 2 || type == 3) // clumps and crystals rotate
            {
                float rot = flakeRotation[i];
                float cr = Mathf.Cos(rot);
                float sr = Mathf.Sin(rot);

                float dx0 = (-s) * cr - (-s) * sr;
                float dy0 = (-s) * sr + (-s) * cr;
                float dx1 = (s) * cr - (-s) * sr;
                float dy1 = (s) * sr + (-s) * cr;
                float dx2 = (s) * cr - (s) * sr;
                float dy2 = (s) * sr + (s) * cr;
                float dx3 = (-s) * cr - (s) * sr;
                float dy3 = (-s) * sr + (s) * cr;

                flakeVerts[vi + 0] = new Vector3(px + dx0, py + dy0, 0f);
                flakeVerts[vi + 1] = new Vector3(px + dx1, py + dy1, 0f);
                flakeVerts[vi + 2] = new Vector3(px + dx2, py + dy2, 0f);
                flakeVerts[vi + 3] = new Vector3(px + dx3, py + dy3, 0f);
            }
            else
            {
                flakeVerts[vi + 0] = new Vector3(px - s, py - s, 0f);
                flakeVerts[vi + 1] = new Vector3(px + s, py - s, 0f);
                flakeVerts[vi + 2] = new Vector3(px + s, py + s, 0f);
                flakeVerts[vi + 3] = new Vector3(px - s, py + s, 0f);
            }
        }
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

    Mesh BuildMesh(string name, Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s, int[] tris, int vi, int ti)
    {
        Mesh mesh = new Mesh();
        mesh.name = name;
        if (vi > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = TrimArray(verts, vi);
        mesh.colors = TrimArray(cols, vi);
        mesh.uv = TrimArray(uvs, vi);
        mesh.uv2 = TrimArray(uv2s, vi);
        mesh.triangles = TrimArray(tris, ti);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Bounds b = mesh.bounds; b.Expand(2f); mesh.bounds = b;
        return mesh;
    }

    void Cleanup()
    {
        CleanupList(groundObjects, groundMeshes);
        CleanupList(driftObjects, driftMeshes);
        CleanupList(iceObjects, iceMeshes);
        CleanupList(frostVegObjects, frostVegMeshes);

        if (flakeObject != null) DestroyImmediate(flakeObject);
        if (flakeMesh != null) DestroyImmediate(flakeMesh);

        DestroyMat(groundMaterial);
        DestroyMat(driftMaterial);
        DestroyMat(iceMaterial);
        DestroyMat(frostVegMaterial);
        DestroyMat(flakeMaterial);

        flakeObject = null;
        flakeMesh = null;
        groundMaterial = null;
        driftMaterial = null;
        iceMaterial = null;
        frostVegMaterial = null;
        flakeMaterial = null;
    }

    void CleanupList(List<GameObject> objects, List<Mesh> meshes)
    {
        foreach (var go in objects)
            if (go != null) DestroyImmediate(go);
        objects.Clear();
        foreach (var m in meshes)
            if (m != null) DestroyImmediate(m);
        meshes.Clear();
    }

    void DestroyMat(Material m)
    {
        if (m != null) DestroyImmediate(m);
    }

    void OnDestroy() => Cleanup();

    Vector3 V3(Vector2 v) => new Vector3(v.x, v.y, 0f);

    Vector2 GetRandomDiscPosition(float radius, float exclusion)
    {
        Vector2 pos;
        int safety = 0;
        do
        {
            pos = Random.insideUnitCircle * radius;
            safety++;
        } while (pos.magnitude < exclusion && safety < 30);
        return pos;
    }

    T[] TrimArray<T>(T[] src, int len)
    {
        if (src.Length == len) return src;
        T[] r = new T[len];
        System.Array.Copy(src, r, len);
        return r;
    }
}
