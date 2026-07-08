using UnityEngine;
using System.Collections.Generic;


public class StonesOverlay : MonoBehaviour
{
    // GROUND 

    [Header("Ground — General")]
    public int groundElementCount = 80000;
    public float groundRadius = 60f;
    public float groundCoreExclusion = 1.5f;

    [Header("Ground — Pebble Scatter")]
    [Range(0f, 0.6f)] public float pebbleRatio = 0.45f;
    public float pebbleMinSize = 0.005f;
    public float pebbleMaxSize = 0.022f;

    [Header("Ground — Gravel Streaks")]
    [Range(0f, 0.4f)] public float gravelStreakRatio = 0.15f;
    public float gravelStreakMinLength = 0.15f;
    public float gravelStreakMaxLength = 0.6f;
    public float gravelStreakWidth = 0.008f;

    [Header("Ground — Lichen Patches")]
    [Range(0f, 0.3f)] public float lichenRatio = 0.08f;
    public float lichenMinSize = 0.03f;
    public float lichenMaxSize = 0.12f;

    [Header("Ground — Dust Film")]
    [Range(0f, 0.3f)] public float dustFilmRatio = 0.12f;
    public float dustFilmMinSize = 0.3f;
    public float dustFilmMaxSize = 1.0f;

    [Header("Ground — Cracks")]
    [Range(0f, 0.3f)] public float crackRatio = 0.10f;
    public float crackMinLength = 0.08f;
    public float crackMaxLength = 0.4f;
    public float crackWidth = 0.003f;

    // COLORS

    [Header("Stone Colors")]
    public Color stoneDark = new Color(0.38f, 0.36f, 0.33f, 0.65f);
    public Color stoneMid = new Color(0.52f, 0.50f, 0.46f, 0.55f);
    public Color stoneLight = new Color(0.68f, 0.65f, 0.60f, 0.45f);
    public Color stoneShadow = new Color(0.28f, 0.26f, 0.24f, 0.50f);

    [Header("Lichen Colors")]
    public Color lichenGreen = new Color(0.35f, 0.42f, 0.28f, 0.30f);
    public Color lichenYellow = new Color(0.55f, 0.52f, 0.30f, 0.22f);
    public Color lichenGray = new Color(0.55f, 0.55f, 0.52f, 0.18f);

    [Header("Crack / Dust Colors")]
    public Color crackColor = new Color(0.22f, 0.20f, 0.18f, 0.35f);
    public Color dustFilmColor = new Color(0.60f, 0.57f, 0.50f, 0.04f);
    public Color gravelColor = new Color(0.48f, 0.45f, 0.40f, 0.15f);

    // DUST MOTES (airborne subtle particles)

    [Header("Dust Motes")]
    public int dustMoteCount = 1200;
    public float dustMoteSpawnRadius = 60f;
    public float dustMoteMinHeight = -60f;
    public float dustMoteMaxHeight = 60f;
    public float dustMoteMinSize = 0.015f;
    public float dustMoteMaxSize = 0.06f;
    public Color dustMoteColor = new Color(0.58f, 0.55f, 0.48f, 0.10f);

    // WIND

    [Header("Wind")]
    public float windStrength = 0.8f;
    public float windSpeed = 1.2f;
    public float windAngle = 5f;
    public float dustMoteDrift = 0.08f;
    public float dustMoteSwirl = 0.15f;

    // SORTING

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    // Base order for the ground decals. Must be > the background's order (-1) or the
    // meshes tie with the background and the camera's Y-axis transparency sort shows
    // them only in a central square. 0 sits just above background, below tower slots
    // (1)/paths (500)/units (500+). Sub-layers add their offsets on top of this.
    public int sortingOrder = 0;


    private const int MAX_VERTS_PER_MESH = 60000;

    // Pebble: 5 verts (center + 4 diamond)
    private const int PEBBLE_VERTS = 5;
    private const int PEBBLE_TRIS_IDX = 12;

    // Gravel streak: 8 verts (4-segment ribbon)
    private const int STREAK_VERTS = 8;
    private const int STREAK_TRIS_IDX = 18;

    // Lichen: 8 verts (center + 7 blob)
    private const int LICHEN_VERTS = 8;
    private const int LICHEN_TRIS_IDX = 21;

    // Dust film: 8 verts (center + 7 blob)
    private const int FILM_VERTS = 8;
    private const int FILM_TRIS_IDX = 21;

    // Crack: 8 verts (4-segment ribbon)
    private const int CRACK_VERTS = 8;
    private const int CRACK_TRIS_IDX = 18;

    // Quad for particles
    private const int QUAD_VERTS = 4;
    private const int QUAD_TRIS_IDX = 6;

    // Ground meshes
    private List<Mesh> groundMeshes = new List<Mesh>();
    private List<GameObject> groundObjects = new List<GameObject>();
    private Material groundMaterial;

    // Dust motes
    private Mesh dustMoteMesh;
    private GameObject dustMoteObject;
    private Material dustMoteMaterial;
    private Vector2[] dustMotePos;
    private float[] dustMoteSpeed, dustMotePhase, dustMoteSize, dustMoteDepth;
    private Vector3[] dustMoteVerts;

    // Precomputed wind direction
    private Vector2 windDir, windPerp;


    private bool _generated;

    void Start()
    {
        // BiomeManager calls GenerateStones() right after AddComponent; Unity then fires
        // Start() a frame later. Without this guard we build and immediately discard
        // the entire mesh set twice per biome.
        if (!_generated) GenerateStones();
    }

    [ContextMenu("Regenerate Stones")]
    public void GenerateStones()
    {
        _generated = true;
        Cleanup();

        float rad = windAngle * Mathf.Deg2Rad;
        windDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
        windPerp = new Vector2(-windDir.y, windDir.x);

        CreateMaterials();
        GenerateGround();
        GenerateDustMotes();
    }

    void CreateMaterials()
    {
        // Try to use a simple sand shader if available, otherwise Sprites/Default
        Shader sh = Shader.Find("Custom/SandWind");
        if (sh == null || sh.name == "Hidden/InternalErrorShader")
            sh = Shader.Find("Sprites/Default");

        groundMaterial = new Material(sh);
        groundMaterial.SetFloat("_StretchAmount", 0f);
        groundMaterial.SetFloat("_SparkleStrength", 0.03f);
        groundMaterial.SetFloat("_HazeStrength", 0f);
        groundMaterial.SetFloat("_Softness", 0.2f);

        dustMoteMaterial = new Material(sh);
        dustMoteMaterial.SetFloat("_StretchAmount", 0.2f);
        dustMoteMaterial.SetFloat("_SparkleStrength", 0.01f);
        dustMoteMaterial.SetFloat("_HazeStrength", 0.04f);
        dustMoteMaterial.SetFloat("_Softness", 0.4f);
        dustMoteMaterial.SetFloat("_DustGlow", 0.05f);
    }


    //  GROUND

    private int MaxVertsPerElement()
    {
        return Mathf.Max(PEBBLE_VERTS, Mathf.Max(STREAK_VERTS, Mathf.Max(LICHEN_VERTS, Mathf.Max(FILM_VERTS, CRACK_VERTS))));
    }
    private int MaxTrisPerElement()
    {
        return Mathf.Max(PEBBLE_TRIS_IDX, Mathf.Max(STREAK_TRIS_IDX, Mathf.Max(LICHEN_TRIS_IDX, Mathf.Max(FILM_TRIS_IDX, CRACK_TRIS_IDX))));
    }

    void GenerateGround()
    {
        int elemsPerMesh = MAX_VERTS_PER_MESH / MaxVertsPerElement();
        int meshCount = Mathf.CeilToInt((float)groundElementCount / elemsPerMesh);
        int remaining = groundElementCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(elemsPerMesh, remaining);
            Mesh mesh = BuildGroundMesh(count, offset);
            groundMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"StonesGround_{m}", mesh, groundMaterial, sortingOrder);
            groundObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    enum GroundType { Pebble, GravelStreak, Lichen, DustFilm, Crack }

    Mesh BuildGroundMesh(int count, int seedOffset)
    {
        int maxV = count * MaxVertsPerElement();
        int maxT = count * MaxTrisPerElement();

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;

        int pebbleCount = Mathf.RoundToInt(count * pebbleRatio);
        int streakCount = Mathf.RoundToInt(count * gravelStreakRatio);
        int lichenCount = Mathf.RoundToInt(count * lichenRatio);
        int filmCount = Mathf.RoundToInt(count * dustFilmRatio);
        int crackCount = Mathf.RoundToInt(count * crackRatio);
        int remainder = count - pebbleCount - streakCount - lichenCount - filmCount - crackCount;
        pebbleCount += Mathf.Max(0, remainder);

        int typeIdx = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 55555);

            GroundType type;
            if (typeIdx < filmCount) type = GroundType.DustFilm;
            else if (typeIdx < filmCount + lichenCount) type = GroundType.Lichen;
            else if (typeIdx < filmCount + lichenCount + streakCount) type = GroundType.GravelStreak;
            else if (typeIdx < filmCount + lichenCount + streakCount + crackCount) type = GroundType.Crack;
            else type = GroundType.Pebble;
            typeIdx++;

            switch (type)
            {
                case GroundType.Pebble:
                    BuildPebble(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
                case GroundType.GravelStreak:
                    BuildGravelStreak(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
                case GroundType.Lichen:
                    BuildLichen(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
                case GroundType.DustFilm:
                    BuildDustFilm(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
                case GroundType.Crack:
                    BuildCrack(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
            }
        }

        return BuildMeshFromArrays("StonesGroundMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  PEBBLE 

    void BuildPebble(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                     int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius, groundCoreExclusion);
        float size = Random.Range(pebbleMinSize, pebbleMaxSize);
        float phase = Random.value;

        // Noise-based color variation for natural look
        float n = Mathf.PerlinNoise(pos.x * 0.4f + 100f, pos.y * 0.4f + 100f);
        Color c;
        float cr = Random.value;
        if (cr < 0.25f) c = Color.Lerp(stoneDark, stoneShadow, Random.Range(0f, 0.5f));
        else if (cr < 0.6f) c = Color.Lerp(stoneMid, stoneDark, Random.Range(0f, 0.4f));
        else c = Color.Lerp(stoneLight, stoneMid, Random.Range(0f, 0.5f));
        c = Color.Lerp(c, stoneShadow, n * 0.2f);
        c.r += Random.Range(-0.03f, 0.03f);
        c.g += Random.Range(-0.02f, 0.02f);
        c = ClampColor(c);

        verts[vi] = V3(pos);
        cols[vi] = c;
        uvs[vi] = new Vector2(0.5f, 0.5f);
        uv2s[vi] = new Vector2(0f, phase);

        for (int p = 0; p < 4; p++)
        {
            float angle = (p / 4f) * Mathf.PI * 2f + Random.Range(-0.4f, 0.4f);
            float rv = size * Random.Range(0.6f, 1.4f);
            Vector2 pv = pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rv;
            verts[vi + 1 + p] = V3(pv);

            Color edgeC = c;
            edgeC.a *= 0.4f;
            cols[vi + 1 + p] = edgeC;
            uvs[vi + 1 + p] = new Vector2(0.5f, 0.5f);
            uv2s[vi + 1 + p] = new Vector2(0f, phase);
        }

        for (int p = 0; p < 4; p++)
        {
            tris[ti++] = vi;
            tris[ti++] = vi + 1 + p;
            tris[ti++] = vi + 1 + ((p + 1) % 4);
        }

        vi += PEBBLE_VERTS;
    }


    //  GRAVEL STREAK 

    void BuildGravelStreak(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                           int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius, groundCoreExclusion);
        float length = Random.Range(gravelStreakMinLength, gravelStreakMaxLength);
        float halfW = gravelStreakWidth * Random.Range(0.5f, 1.5f);
        float phase = Random.value;

        // Mostly wind-aligned
        float rotJitter = Random.Range(-20f, 20f) * Mathf.Deg2Rad;
        Vector2 dir = Rotate2D(windDir, rotJitter);
        Vector2 perp = new Vector2(-dir.y, dir.x);
        float curveAmt = length * Random.Range(-0.1f, 0.1f);

        Color c = gravelColor;
        float n = Mathf.PerlinNoise(pos.x * 0.3f + 50f, pos.y * 0.3f + 50f);
        c = Color.Lerp(c, stoneMid, n * 0.15f);
        c.r += Random.Range(-0.02f, 0.02f);

        float[] segT = { 0f, 0.33f, 0.66f, 1.0f };
        float[] segWidth = { 0.2f, 1.0f, 1.0f, 0.15f };
        Vector2[] spine = new Vector2[4];

        for (int s = 0; s < 4; s++)
        {
            float t = segT[s];
            float along = (t - 0.5f) * length;
            float curveOffset = curveAmt * Mathf.Sin(t * Mathf.PI);
            spine[s] = pos + dir * along + perp * curveOffset;
        }

        for (int s = 0; s < 4; s++)
        {
            float w = halfW * segWidth[s];
            Vector2 localPerp;
            if (s == 0) localPerp = (spine[1] - spine[0]).normalized;
            else if (s == 3) localPerp = (spine[3] - spine[2]).normalized;
            else localPerp = (spine[s + 1] - spine[s - 1]).normalized;
            localPerp = new Vector2(-localPerp.y, localPerp.x);

            verts[vi + s * 2 + 0] = V3(spine[s] - localPerp * w);
            verts[vi + s * 2 + 1] = V3(spine[s] + localPerp * w);

            Color sc = c;
            sc.a *= segWidth[s] * Random.Range(0.7f, 1.0f);
            cols[vi + s * 2 + 0] = sc;
            cols[vi + s * 2 + 1] = sc;

            uvs[vi + s * 2 + 0] = new Vector2(0f, segT[s]);
            uvs[vi + s * 2 + 1] = new Vector2(1f, segT[s]);
            uv2s[vi + s * 2 + 0] = new Vector2(0f, phase);
            uv2s[vi + s * 2 + 1] = new Vector2(0f, phase);
        }

        for (int s = 0; s < 3; s++)
        {
            int bl = vi + s * 2;
            int br = bl + 1;
            int tl = bl + 2;
            int tr = bl + 3;
            tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
            tris[ti++] = br; tris[ti++] = tl; tris[ti++] = tr;
        }

        vi += STREAK_VERTS;
    }


    //  LICHEN PATCH 

    void BuildLichen(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                     int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius * 0.8f, groundCoreExclusion * 1.5f);
        float size = Random.Range(lichenMinSize, lichenMaxSize);
        float phase = Random.value;

        // Pick a lichen color type
        Color c;
        float typeRoll = Random.value;
        if (typeRoll < 0.4f) c = lichenGreen;
        else if (typeRoll < 0.7f) c = lichenYellow;
        else c = lichenGray;
        c.r += Random.Range(-0.03f, 0.03f);
        c.g += Random.Range(-0.03f, 0.03f);
        c.a *= Random.Range(0.6f, 1.0f);

        verts[vi] = V3(pos);
        cols[vi] = c;
        uvs[vi] = new Vector2(0.5f, 0.5f);
        uv2s[vi] = new Vector2(0f, phase);

        for (int p = 0; p < 7; p++)
        {
            float angle = (p / 7f) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            float rv = size * Random.Range(0.5f, 1.4f); // irregular blob
            Vector2 pv = pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rv;
            verts[vi + 1 + p] = V3(pv);

            Color edgeC = c;
            edgeC.a *= Random.Range(0.1f, 0.35f);
            cols[vi + 1 + p] = edgeC;
            uvs[vi + 1 + p] = new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f);
            uv2s[vi + 1 + p] = new Vector2(0f, phase);
        }

        for (int p = 0; p < 7; p++)
        {
            tris[ti++] = vi;
            tris[ti++] = vi + 1 + p;
            tris[ti++] = vi + 1 + ((p + 1) % 7);
        }

        vi += LICHEN_VERTS;
    }


    //  DUST FILM 

    void BuildDustFilm(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                       int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius * 0.85f, groundCoreExclusion);
        float size = Random.Range(dustFilmMinSize, dustFilmMaxSize);
        float phase = Random.value;

        Color c = dustFilmColor;
        float n = Mathf.PerlinNoise(pos.x * 0.1f + 200f, pos.y * 0.1f + 200f);
        c.a *= Mathf.Lerp(0.5f, 1.0f, n);

        verts[vi] = V3(pos);
        cols[vi] = c;
        uvs[vi] = new Vector2(0.5f, 0.5f);
        uv2s[vi] = new Vector2(0f, phase);

        for (int p = 0; p < 7; p++)
        {
            float angle = (p / 7f) * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
            float rv = size * Random.Range(0.7f, 1.3f);
            Vector2 pv = pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rv;
            verts[vi + 1 + p] = V3(pv);

            Color edgeC = c;
            edgeC.a *= Random.Range(0.02f, 0.12f);
            cols[vi + 1 + p] = edgeC;
            uvs[vi + 1 + p] = new Vector2(0.5f, 0.5f);
            uv2s[vi + 1 + p] = new Vector2(0f, phase);
        }

        for (int p = 0; p < 7; p++)
        {
            tris[ti++] = vi;
            tris[ti++] = vi + 1 + p;
            tris[ti++] = vi + 1 + ((p + 1) % 7);
        }

        vi += FILM_VERTS;
    }


    //  CRACK 

    void BuildCrack(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                    int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius * 0.7f, groundCoreExclusion * 1.5f);
        float length = Random.Range(crackMinLength, crackMaxLength);
        float halfW = crackWidth * Random.Range(0.5f, 1.5f);
        float phase = Random.value;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 perp = new Vector2(-dir.y, dir.x);
        float jag = length * Random.Range(-0.2f, 0.2f);

        Color c = crackColor;
        c.r += Random.Range(-0.02f, 0.02f);
        c.a *= Random.Range(0.6f, 1.0f);

        float[] segT = { 0f, 0.33f, 0.66f, 1.0f };
        float[] segWidth = { 0.3f, 1.0f, 0.8f, 0.1f };
        Vector2[] spine = new Vector2[4];

        for (int s = 0; s < 4; s++)
        {
            float t = segT[s];
            float along = (t - 0.5f) * length;
            float jagOffset = jag * Mathf.Sin(t * Mathf.PI * 2f);
            spine[s] = pos + dir * along + perp * jagOffset;
        }

        for (int s = 0; s < 4; s++)
        {
            float w = halfW * segWidth[s];
            Vector2 localPerp;
            if (s == 0) localPerp = (spine[1] - spine[0]).normalized;
            else if (s == 3) localPerp = (spine[3] - spine[2]).normalized;
            else localPerp = (spine[s + 1] - spine[s - 1]).normalized;
            localPerp = new Vector2(-localPerp.y, localPerp.x);

            verts[vi + s * 2 + 0] = V3(spine[s] - localPerp * w);
            verts[vi + s * 2 + 1] = V3(spine[s] + localPerp * w);

            Color sc = c;
            sc.a *= segWidth[s];
            cols[vi + s * 2 + 0] = sc;
            cols[vi + s * 2 + 1] = sc;

            uvs[vi + s * 2 + 0] = new Vector2(0f, segT[s]);
            uvs[vi + s * 2 + 1] = new Vector2(1f, segT[s]);
            uv2s[vi + s * 2 + 0] = new Vector2(0f, phase);
            uv2s[vi + s * 2 + 1] = new Vector2(0f, phase);
        }

        for (int s = 0; s < 3; s++)
        {
            int bl = vi + s * 2;
            int br = bl + 1;
            int tl = bl + 2;
            int tr = bl + 3;
            tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
            tris[ti++] = br; tris[ti++] = tl; tris[ti++] = tr;
        }

        vi += CRACK_VERTS;
    }


    //  DUST MOTES

    void GenerateDustMotes()
    {
        dustMotePos = new Vector2[dustMoteCount];
        dustMoteSpeed = new float[dustMoteCount];
        dustMotePhase = new float[dustMoteCount];
        dustMoteSize = new float[dustMoteCount];
        dustMoteDepth = new float[dustMoteCount];

        int vertCount = dustMoteCount * QUAD_VERTS;
        dustMoteVerts = new Vector3[vertCount];
        Color[] dCols = new Color[vertCount];
        Vector2[] dUvs = new Vector2[vertCount];
        Vector2[] dUv2s = new Vector2[vertCount];
        int[] dTris = new int[dustMoteCount * QUAD_TRIS_IDX];

        for (int i = 0; i < dustMoteCount; i++)
        {
            float depth = Random.value;
            dustMoteDepth[i] = depth;
            dustMotePos[i] = new Vector2(
                Random.Range(-dustMoteSpawnRadius, dustMoteSpawnRadius),
                Random.Range(dustMoteMinHeight, dustMoteMaxHeight)
            );
            dustMoteSpeed[i] = Mathf.Lerp(0.6f, 0.15f, depth) * Random.Range(0.7f, 1.3f);
            dustMotePhase[i] = Random.Range(0f, Mathf.PI * 2f);
            dustMoteSize[i] = Mathf.Lerp(dustMoteMaxSize, dustMoteMinSize, depth) * Random.Range(0.5f, 1.5f);

            Color c = dustMoteColor;
            c.a *= Mathf.Lerp(1.0f, 0.25f, depth) * Random.Range(0.5f, 1.0f);
            c = ClampColor(c);

            int v = i * 4;
            dCols[v] = c; dCols[v + 1] = c; dCols[v + 2] = c; dCols[v + 3] = c;
            dUvs[v] = new Vector2(0, 0); dUvs[v + 1] = new Vector2(1, 0);
            dUvs[v + 2] = new Vector2(1, 1); dUvs[v + 3] = new Vector2(0, 1);
            float pn = dustMotePhase[i] / (Mathf.PI * 2f);
            dUv2s[v] = new Vector2(depth, pn); dUv2s[v + 1] = new Vector2(depth, pn);
            dUv2s[v + 2] = new Vector2(depth, pn); dUv2s[v + 3] = new Vector2(depth, pn);

            int t = i * 6;
            dTris[t] = v; dTris[t + 1] = v + 2; dTris[t + 2] = v + 1;
            dTris[t + 3] = v; dTris[t + 4] = v + 3; dTris[t + 5] = v + 2;
        }

        RefreshQuadVerts(dustMotePos, dustMoteSize, dustMoteVerts, dustMoteCount);

        dustMoteMesh = new Mesh { name = "DustMoteMesh" };
        dustMoteMesh.vertices = dustMoteVerts; dustMoteMesh.colors = dCols;
        dustMoteMesh.uv = dUvs; dustMoteMesh.uv2 = dUv2s; dustMoteMesh.triangles = dTris;
        dustMoteMesh.RecalculateNormals();
        dustMoteMesh.bounds = new Bounds(Vector3.zero, Vector3.one * dustMoteSpawnRadius * 4f);

        dustMoteObject = new GameObject("StoneDustMotes");
        dustMoteObject.transform.SetParent(transform);
        dustMoteObject.transform.localPosition = Vector3.zero;
        dustMoteObject.transform.localScale = Vector3.one;
        dustMoteObject.AddComponent<MeshFilter>().mesh = dustMoteMesh;
        MeshRenderer mr = dustMoteObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = dustMoteMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder + 2;
    }


    //  ANIMATION

    void Update()
    {
        float dt = Time.deltaTime;
        float t = Time.time;
        AnimateDustMotes(dt, t);
    }

    void AnimateDustMotes(float dt, float t)
    {
        if (dustMotePos == null || dustMoteMesh == null) return;
        float spawnW = dustMoteSpawnRadius * 1.2f;

        for (int i = 0; i < dustMoteCount; i++)
        {
            float depth = dustMoteDepth[i];
            float phase = dustMotePhase[i];
            float speed = dustMoteSpeed[i];

            float wx = windStrength * Mathf.Lerp(1.0f, 0.3f, depth) * speed * 0.5f;
            dustMotePos[i].x += (wx * windDir.x
                + Mathf.Sin(t * 0.3f + phase) * dustMoteDrift
                + Mathf.Sin(t * dustMoteSwirl * 0.5f + phase * 2f) * dustMoteSwirl * 0.04f) * dt;
            dustMotePos[i].y += (wx * windDir.y * 0.05f
                + Mathf.Cos(t * 0.25f + phase * 1.3f) * dustMoteDrift * 0.3f
                + Mathf.Cos(t * dustMoteSwirl * 0.4f + phase * 1.5f) * dustMoteSwirl * 0.03f) * dt;

            if (dustMotePos[i].x > spawnW)
            {
                dustMotePos[i].x = -spawnW + Random.Range(0f, 2f);
                dustMotePos[i].y = Random.Range(dustMoteMinHeight, dustMoteMaxHeight);
            }
            else if (dustMotePos[i].x < -spawnW)
            {
                dustMotePos[i].x = spawnW - Random.Range(0f, 2f);
                dustMotePos[i].y = Random.Range(dustMoteMinHeight, dustMoteMaxHeight);
            }
            if (dustMotePos[i].y > dustMoteMaxHeight + 2f) dustMotePos[i].y = dustMoteMinHeight;
            else if (dustMotePos[i].y < dustMoteMinHeight - 2f) dustMotePos[i].y = dustMoteMaxHeight;
        }

        RefreshQuadVerts(dustMotePos, dustMoteSize, dustMoteVerts, dustMoteCount);
        dustMoteMesh.vertices = dustMoteVerts;
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

    Mesh BuildMeshFromArrays(string name, Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s, int[] tris, int vi, int ti)
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
        Bounds b = mesh.bounds; b.Expand(3f); mesh.bounds = b;
        return mesh;
    }

    void Cleanup()
    {
        foreach (var go in groundObjects) if (go != null) DestroyImmediate(go);
        groundObjects.Clear();
        foreach (var m in groundMeshes) if (m != null) DestroyImmediate(m);
        groundMeshes.Clear();

        if (dustMoteObject != null) DestroyImmediate(dustMoteObject);
        if (dustMoteMesh != null) DestroyImmediate(dustMoteMesh);
        if (groundMaterial != null) DestroyImmediate(groundMaterial);
        if (dustMoteMaterial != null) DestroyImmediate(dustMoteMaterial);

        dustMoteObject = null; dustMoteMesh = null;
        groundMaterial = null; dustMoteMaterial = null;
    }

    void OnDestroy() => Cleanup();

    Vector3 V3(Vector2 v) => new Vector3(v.x, v.y, 0f);

    Vector2 Rotate2D(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians); float s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
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

    Vector2 GetRandomDiscPosition(float radius, float exclusion)
    {
        Vector2 pos; int safety = 0;
        do { pos = Random.insideUnitCircle * radius; safety++; }
        while (pos.magnitude < exclusion && safety < 30);
        return pos;
    }

    Color ClampColor(Color c)
    {
        c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g);
        c.b = Mathf.Clamp01(c.b); c.a = Mathf.Clamp01(c.a);
        return c;
    }

    T[] TrimArray<T>(T[] src, int len)
    {
        if (src.Length == len) return src;
        T[] r = new T[len]; System.Array.Copy(src, r, len); return r;
    }
}

