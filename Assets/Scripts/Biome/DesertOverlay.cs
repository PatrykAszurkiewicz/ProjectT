using UnityEngine;
using System.Collections.Generic;


public class DesertOverlay : MonoBehaviour
{
    //  GROUND 

    [Header("Ground — General")]
    public int groundElementCount = 120000;
    public float groundRadius = 60f;
    public float groundCoreExclusion = 1.5f;
    [Tooltip("Wind direction angle in degrees (0 = right, 90 = up)")]
    public float windAngle = 10f;

    [Header("Ground — Wind Streaks")]
    [Range(0f, 0.6f)] public float streakRatio = 0.45f;
    public float streakMinLength = 0.30f;
    public float streakMaxLength = 1.20f;
    public float streakWidth = 0.012f;
    [Range(0f, 1f)] public float streakCurvature = 0.35f;

    [Header("Ground — Sand Drifts")]
    [Range(0f, 0.4f)] public float driftRatio = 0.08f;
    public float driftMinSize = 0.06f;
    public float driftMaxSize = 0.20f;
    public int driftClumpCount = 180;
    public float driftClumpSpread = 1.2f;

    [Header("Ground — Pebble Scatter")]
    [Range(0f, 0.4f)] public float pebbleRatio = 0.15f;
    public float pebbleMinSize = 0.006f;
    public float pebbleMaxSize = 0.018f;

    [Header("Ground — Color Undulation")]
    [Range(0f, 0.3f)] public float undulationRatio = 0.07f;
    public float undulationMinSize = 0.5f;
    public float undulationMaxSize = 1.8f;
    public float undulationNoiseScale = 0.08f;

    //  SAND RIPPLES 

    [Header("Sand Ripples")]
    public int rippleCount = 400;
    public float rippleMinLength = 0.6f;
    public float rippleMaxLength = 2.5f;
    public float rippleWidth = 0.025f;
    public float rippleWindAlignment = 0.85f;
    public Color rippleCrest = new Color(0.95f, 0.86f, 0.62f, 0.30f);
    public Color rippleTrough = new Color(0.68f, 0.56f, 0.35f, 0.22f);

    //  CRACKED EARTH 

    [Header("Cracked Earth")]
    public int crackedEarthCount = 180;
    public float crackedEarthMinSize = 0.12f;
    public float crackedEarthMaxSize = 0.35f;
    public float crackWidth = 0.004f;
    public Color crackColor = new Color(0.30f, 0.24f, 0.16f, 0.40f);
    public Color crackedSurface = new Color(0.78f, 0.68f, 0.48f, 0.25f);

    //  DRIED SCRUB 

    [Header("Dried Scrub")]
    public int scrubCount = 350;
    public float scrubMinHeight = 0.04f;
    public float scrubMaxHeight = 0.14f;
    public float scrubWidth = 0.006f;
    public Color scrubBase = new Color(0.32f, 0.26f, 0.15f, 0.80f);
    public Color scrubTip = new Color(0.50f, 0.42f, 0.25f, 0.55f);
    public Color scrubDead = new Color(0.42f, 0.35f, 0.22f, 0.65f);

    //  HEAT SHIMMER WISPS 

    [Header("Heat Shimmer Wisps")]
    public int shimmerWispCount = 120;
    public float shimmerWispMinSize = 0.15f;
    public float shimmerWispMaxSize = 0.5f;
    public float shimmerRiseSpeed = 0.3f;
    public Color shimmerWispColor = new Color(0.92f, 0.85f, 0.65f, 0.06f);

    //  SALTATION 

    [Header("Saltation (Low Sand Streaks)")]
    public int saltationCount = 5000;
    public float saltationSpawnRadius = 60f;
    public float saltationMinHeight = -60f;
    public float saltationMaxHeight = 60f;
    [Range(0f, 1f)] public float saltationGroundBias = 0.75f;
    public float saltationMinSize = 0.01f;
    public float saltationMaxSize = 0.05f;

    //  DUST HAZE 

    [Header("Dust Haze (High Atmosphere)")]
    public int dustCount = 800;
    public float dustSpawnRadius = 60f;
    public float dustMinHeight = -50f;
    public float dustMaxHeight = 60f;
    public float dustMinSize = 0.06f;
    public float dustMaxSize = 0.20f;

    //  DUST DEVILS 

    [Header("Dust Devils")]
    public int dustDevilCount = 3;
    public int dustDevilParticles = 80;
    public float dustDevilRadius = 0.4f;
    public float dustDevilHeight = 2.5f;
    public float dustDevilSpinSpeed = 4.0f;
    public float dustDevilDriftSpeed = 0.3f;
    public Color dustDevilColor = new Color(0.85f, 0.75f, 0.50f, 0.35f);

    //  COLORS

    [Header("Sand Colors")]
    public Color sandBright = new Color(0.92f, 0.82f, 0.58f, 0.85f);
    public Color sandMid = new Color(0.82f, 0.70f, 0.45f, 0.70f);
    public Color sandDark = new Color(0.62f, 0.50f, 0.30f, 0.55f);
    public Color sandShadow = new Color(0.45f, 0.38f, 0.25f, 0.45f);

    [Header("Ground Detail Colors")]
    public Color streakColor = new Color(0.88f, 0.78f, 0.52f, 0.12f);
    public Color driftLeeColor = new Color(0.95f, 0.87f, 0.65f, 0.15f);
    public Color driftWindwardColor = new Color(0.80f, 0.70f, 0.45f, 0.06f);
    public Color pebbleDark = new Color(0.35f, 0.28f, 0.18f, 0.35f);
    public Color pebbleLight = new Color(0.55f, 0.48f, 0.35f, 0.22f);
    public Color undulationWarm = new Color(0.90f, 0.75f, 0.48f, 0.05f);
    public Color undulationCool = new Color(0.72f, 0.65f, 0.50f, 0.04f);
    public Color dustColor = new Color(0.90f, 0.82f, 0.60f, 0.18f);

    //  WIND

    [Header("Wind")]
    public float windStrength = 3.5f;
    public float windSpeed = 2.0f;
    public float gustStrength = 1.5f;
    public float gustSpeed = 0.5f;
    public float turbulence = 2.2f;

    [Header("Saltation Physics")]
    public float bounceHeight = 0.4f;
    public float bounceSpeed = 3.5f;
    public float streakDrift = 0.3f;

    [Header("Dust Physics")]
    public float dustDrift = 0.15f;
    public float dustWindMult = 0.25f;
    public float dustSwirl = 0.3f;

    //  SORTING

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = -1;



    private const int MAX_VERTS_PER_MESH = 60000;

    // Wind streak: 8 verts (4-segment curved ribbon)
    private const int STREAK_VERTS = 8;
    private const int STREAK_TRIS_IDX = 18;

    // Drift: 10 verts (center + 9 perimeter teardrop)
    private const int DRIFT_VERTS = 10;
    private const int DRIFT_TRIS_IDX = 27;

    // Pebble: 5 verts (center + 4 diamond)
    private const int PEBBLE_VERTS = 5;
    private const int PEBBLE_TRIS_IDX = 12;

    // Undulation: 8 verts (center + 7 blob)
    private const int UNDUL_VERTS = 8;
    private const int UNDUL_TRIS_IDX = 18;

    // Ripple: 8 verts (4-segment curved ribbon like streak)
    private const int RIPPLE_VERTS = 8;
    private const int RIPPLE_TRIS_IDX = 18;

    // Cracked earth: 13 verts (center + 12 for double hex)
    private const int CRACK_VERTS = 13;
    private const int CRACK_TRIS_IDX = 54; // 6 inner fan + 12 outer ring = 18 tris

    // Scrub: 7 verts (3 pairs + tip, like frost veg)
    private const int SCRUB_VERTS = 7;
    private const int SCRUB_TRIS_IDX = 15;

    // Quad for particles
    private const int QUAD_VERTS = 4;
    private const int QUAD_TRIS_IDX = 6;

    // Ground meshes
    private List<Mesh> groundMeshes = new List<Mesh>();
    private List<GameObject> groundObjects = new List<GameObject>();
    private Material groundMaterial;

    // Ripple layer
    private List<Mesh> rippleMeshes = new List<Mesh>();
    private List<GameObject> rippleObjects = new List<GameObject>();
    private Material rippleMaterial;

    // Cracked earth layer
    private List<Mesh> crackMeshes = new List<Mesh>();
    private List<GameObject> crackObjects = new List<GameObject>();
    private Material crackMaterial;

    // Scrub layer
    private List<Mesh> scrubMeshes = new List<Mesh>();
    private List<GameObject> scrubObjects = new List<GameObject>();
    private Material scrubMaterial;

    // Heat shimmer wisps
    private Mesh shimmerMesh;
    private GameObject shimmerObject;
    private Material shimmerMaterial;
    private Vector2[] shimmerPos;
    private float[] shimmerPhase, shimmerSize, shimmerRisePhase;
    private Vector3[] shimmerVerts;

    // Saltation
    private Mesh saltMesh;
    private GameObject saltObject;
    private Material saltMaterial;
    private Vector2[] saltPos;
    private float[] saltSpeed, saltPhase, saltSize, saltDepth, saltBouncePhase;
    private Vector3[] saltVerts;

    // Dust
    private Mesh dustMesh;
    private GameObject dustObject;
    private Material dustMaterial;
    private Vector2[] dustPos;
    private float[] dustSpeed, dustPhase, dustSize, dustDepth;
    private Vector3[] dustVerts;

    // Dust devils
    private Mesh devilMesh;
    private GameObject devilObject;
    private Material devilMaterial;
    private Vector2[] devilCenter;        // center position of each devil
    private float[] devilDriftPhase;      // drift movement phase
    private float[] devilSpinPhase;
    private int[] devilParticleStart;     // index into particle arrays
    private Vector2[] devilPartPos;       // all devil particles
    private float[] devilPartPhase, devilPartHeight, devilPartSize;
    private Vector3[] devilVerts;
    private int totalDevilParticles;

    // Drift clump centers
    private Vector2[] driftClumpCenters;

    // Precomputed wind direction
    private Vector2 windDir, windPerp;


    void Start() => GenerateDesert();

    [ContextMenu("Regenerate Desert")]
    public void GenerateDesert()
    {
        Cleanup();

        float rad = windAngle * Mathf.Deg2Rad;
        windDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
        windPerp = new Vector2(-windDir.y, windDir.x);

        GenerateDriftClumps();
        CreateMaterials();
        GenerateGround();
        GenerateSandRipples();
        GenerateCrackedEarth();
        GenerateDriedScrub();
        GenerateHeatShimmerWisps();
        GenerateSaltation();
        GenerateDustHaze();
        GenerateDustDevils();
    }

    void GenerateDriftClumps()
    {
        driftClumpCenters = new Vector2[driftClumpCount];
        for (int i = 0; i < driftClumpCount; i++)
        {
            Vector2 pos;
            int safety = 0;
            do { pos = Random.insideUnitCircle * groundRadius * 0.9f; safety++; }
            while (pos.magnitude < groundCoreExclusion && safety < 30);
            driftClumpCenters[i] = pos;
        }
    }

    void CreateMaterials()
    {
        Shader sandShader = Shader.Find("Custom/SandWind");
        if (sandShader == null || sandShader.name == "Hidden/InternalErrorShader")
        {
            sandShader = Shader.Find("Sprites/Default");
            Debug.LogWarning("[DesertOverlay] Custom/SandWind shader not found, using fallback.");
        }

        groundMaterial = new Material(sandShader);
        groundMaterial.SetFloat("_StretchAmount", 0f);
        groundMaterial.SetFloat("_SparkleStrength", 0.06f);
        groundMaterial.SetFloat("_HazeStrength", 0f);
        groundMaterial.SetFloat("_Softness", 0.3f);

        rippleMaterial = new Material(sandShader);
        rippleMaterial.SetFloat("_StretchAmount", 0f);
        rippleMaterial.SetFloat("_SparkleStrength", 0.12f);
        rippleMaterial.SetFloat("_HazeStrength", 0f);
        rippleMaterial.SetFloat("_Softness", 0.35f);
        rippleMaterial.SetFloat("_WarmShadow", 0.08f);

        crackMaterial = new Material(sandShader);
        crackMaterial.SetFloat("_StretchAmount", 0f);
        crackMaterial.SetFloat("_SparkleStrength", 0.02f);
        crackMaterial.SetFloat("_HazeStrength", 0f);
        crackMaterial.SetFloat("_Softness", 0.15f);

        scrubMaterial = new Material(sandShader);
        scrubMaterial.SetFloat("_StretchAmount", 0f);
        scrubMaterial.SetFloat("_SparkleStrength", 0.0f);
        scrubMaterial.SetFloat("_HazeStrength", 0f);
        scrubMaterial.SetFloat("_Softness", 0.2f);

        shimmerMaterial = new Material(sandShader);
        shimmerMaterial.SetFloat("_StretchAmount", 0f);
        shimmerMaterial.SetFloat("_SparkleStrength", 0.0f);
        shimmerMaterial.SetFloat("_HazeStrength", 0.15f);
        shimmerMaterial.SetFloat("_Softness", 0.5f);
        shimmerMaterial.SetFloat("_DustGlow", 0.05f);

        saltMaterial = new Material(sandShader);
        saltMaterial.SetFloat("_StretchAmount", 1.2f);
        saltMaterial.SetFloat("_SparkleStrength", 0.22f);
        saltMaterial.SetFloat("_HazeStrength", 0.03f);

        dustMaterial = new Material(sandShader);
        dustMaterial.SetFloat("_StretchAmount", 0.4f);
        dustMaterial.SetFloat("_SparkleStrength", 0.02f);
        dustMaterial.SetFloat("_HazeStrength", 0.10f);
        dustMaterial.SetFloat("_DustGlow", 0.15f);
        dustMaterial.SetFloat("_Softness", 0.45f);

        devilMaterial = new Material(sandShader);
        devilMaterial.SetFloat("_StretchAmount", 0.3f);
        devilMaterial.SetFloat("_SparkleStrength", 0.15f);
        devilMaterial.SetFloat("_HazeStrength", 0.08f);
        devilMaterial.SetFloat("_Softness", 0.4f);
        devilMaterial.SetFloat("_DustGlow", 0.10f);
    }


    //  GROUND — mixed element types (original system preserved)

    private int MaxVertsPerElement()
    {
        return Mathf.Max(STREAK_VERTS, Mathf.Max(DRIFT_VERTS, Mathf.Max(PEBBLE_VERTS, UNDUL_VERTS)));
    }
    private int MaxTrisPerElement()
    {
        return Mathf.Max(STREAK_TRIS_IDX, Mathf.Max(DRIFT_TRIS_IDX, Mathf.Max(PEBBLE_TRIS_IDX, UNDUL_TRIS_IDX)));
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

            GameObject go = CreateMeshObject($"DesertGround_{m}", mesh, groundMaterial, sortingOrder);
            groundObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    enum GroundType { WindStreak, SandDrift, Pebble, Undulation }

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

        int streakCount = Mathf.RoundToInt(count * streakRatio);
        int dCount = Mathf.RoundToInt(count * driftRatio);
        int pebbleCount = Mathf.RoundToInt(count * pebbleRatio);
        int undulCount = Mathf.RoundToInt(count * undulationRatio);
        int remainder = count - streakCount - dCount - pebbleCount - undulCount;
        pebbleCount += Mathf.Max(0, remainder);

        int typeIdx = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 31337);

            GroundType type;
            if (typeIdx < undulCount) type = GroundType.Undulation;
            else if (typeIdx < undulCount + dCount) type = GroundType.SandDrift;
            else if (typeIdx < undulCount + dCount + streakCount) type = GroundType.WindStreak;
            else type = GroundType.Pebble;
            typeIdx++;

            switch (type)
            {
                case GroundType.WindStreak:
                    BuildWindStreak(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
                case GroundType.SandDrift:
                    BuildSandDrift(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
                case GroundType.Pebble:
                    BuildPebble(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
                case GroundType.Undulation:
                    BuildUndulation(verts, cols, uvs, uv2s, tris, ref vi, ref ti);
                    break;
            }
        }

        return BuildMeshFromArrays("DesertGroundMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    // WIND STREAKS 

    void BuildWindStreak(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                         int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius, groundCoreExclusion);

        float length = Random.Range(streakMinLength, streakMaxLength);
        float halfW = streakWidth * Random.Range(0.6f, 1.5f);

        float rotJitter = Random.Range(-15f, 15f) * Mathf.Deg2Rad;
        Vector2 dir = Rotate2D(windDir, rotJitter);
        Vector2 perp = new Vector2(-dir.y, dir.x);

        float curveAmt = streakCurvature * length * Random.Range(-1f, 1f);

        float[] segT = { 0f, 0.33f, 0.66f, 1.0f };
        Vector2[] spine = new Vector2[4];
        for (int s = 0; s < 4; s++)
        {
            float t = segT[s];
            float along = (t - 0.5f) * length;
            float curveOffset = curveAmt * Mathf.Sin(t * Mathf.PI);
            spine[s] = pos + dir * along + perp * curveOffset;
        }

        Color c = streakColor;
        float noise = Mathf.PerlinNoise(pos.x * 0.25f + 77f, pos.y * 0.25f + 77f);
        c = Color.Lerp(c, sandBright, noise * 0.15f);
        c.r += Random.Range(-0.03f, 0.03f);
        c.g += Random.Range(-0.02f, 0.02f);

        float phase = Random.value;
        float[] segWidth = { 0.3f, 1.0f, 1.0f, 0.2f };

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
            sc.a *= segWidth[s] * Random.Range(0.8f, 1.0f);
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


    // SAND DRIFTS 

    void BuildSandDrift(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                        int[] tris, ref int vi, ref int ti)
    {
        int clumpIdx = Random.Range(0, driftClumpCenters.Length);
        Vector2 clumpCenter = driftClumpCenters[clumpIdx];
        float r = Random.value * Random.value * driftClumpSpread;
        Vector2 pos = clumpCenter + Random.insideUnitCircle.normalized * r;

        if (pos.magnitude > groundRadius) pos = pos.normalized * groundRadius * 0.95f;
        if (pos.magnitude < groundCoreExclusion) pos = pos.normalized * (groundCoreExclusion + 0.3f);

        float size = Random.Range(driftMinSize, driftMaxSize);

        float rotJitter = Random.Range(-20f, 20f) * Mathf.Deg2Rad;
        Vector2 localDir = Rotate2D(windDir, rotJitter);
        Vector2 localPerp = new Vector2(-localDir.y, localDir.x);

        float phase = Random.value;

        verts[vi] = V3(pos);
        Color cCenter = driftLeeColor;
        float noise = Mathf.PerlinNoise(pos.x * 0.2f + 33f, pos.y * 0.2f + 33f);
        cCenter = Color.Lerp(cCenter, sandBright, noise * 0.2f);
        cCenter.r += Random.Range(-0.02f, 0.02f);
        cCenter.g += Random.Range(-0.01f, 0.01f);
        cCenter.a *= Random.Range(0.5f, 0.8f);
        cols[vi] = cCenter;
        uvs[vi] = new Vector2(0.5f, 0.5f);
        uv2s[vi] = new Vector2(0f, phase);

        for (int p = 0; p < 9; p++)
        {
            float angle = (p / 9f) * Mathf.PI * 2f;
            Vector2 circleDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            float windDot = Vector2.Dot(circleDir, localDir);
            float perpDot = Mathf.Abs(Vector2.Dot(circleDir, localPerp));

            float radiusScale;
            if (windDot > 0f)
                radiusScale = 1.0f + windDot * 0.6f;
            else
                radiusScale = 0.5f + windDot * 0.4f;
            radiusScale *= (1.0f - perpDot * 0.6f);
            radiusScale *= 0.85f + Random.Range(0f, 0.3f);
            float dist = size * radiusScale;

            Vector2 pv = pos + circleDir * dist;
            verts[vi + 1 + p] = V3(pv);

            Color edgeC = driftWindwardColor;
            if (windDot > 0f)
                edgeC = Color.Lerp(driftWindwardColor, driftLeeColor, windDot * 0.5f);
            edgeC.a *= 0.15f + 0.25f * Mathf.Clamp01(radiusScale);
            edgeC.a *= Random.Range(0.5f, 1.0f);
            cols[vi + 1 + p] = edgeC;

            uvs[vi + 1 + p] = new Vector2(0.5f + circleDir.x * 0.5f, 0.5f + circleDir.y * 0.5f);
            uv2s[vi + 1 + p] = new Vector2(0f, phase);
        }

        for (int p = 0; p < 9; p++)
        {
            tris[ti++] = vi;
            tris[ti++] = vi + 1 + p;
            tris[ti++] = vi + 1 + ((p + 1) % 9);
        }

        vi += DRIFT_VERTS;
    }


    // PEBBLE SCATTER 

    void BuildPebble(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                     int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius, groundCoreExclusion);

        float size = Random.Range(pebbleMinSize, pebbleMaxSize);
        float phase = Random.value;

        float coverage = Mathf.PerlinNoise(pos.x * 0.3f + 150f, pos.y * 0.3f + 150f);
        Color c = Color.Lerp(pebbleDark, pebbleLight, Random.Range(0f, 0.6f));
        c.a *= Mathf.Lerp(0.5f, 0.12f, coverage);
        c.r += Random.Range(-0.04f, 0.04f);
        c.g += Random.Range(-0.03f, 0.03f);
        c = ClampColor(c);

        verts[vi] = V3(pos);
        cols[vi] = c;
        uvs[vi] = new Vector2(0.5f, 0.5f);
        uv2s[vi] = new Vector2(0f, phase);

        for (int p = 0; p < 4; p++)
        {
            float angle = (p / 4f) * Mathf.PI * 2f + Random.Range(-0.4f, 0.4f);
            float rv = size * Random.Range(0.6f, 1.3f);
            Vector2 pv = pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rv;
            verts[vi + 1 + p] = V3(pv);

            Color edgeC = c;
            edgeC.a *= 0.5f;
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


    // COLOR UNDULATION 

    void BuildUndulation(Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s,
                         int[] tris, ref int vi, ref int ti)
    {
        Vector2 pos = GetRandomDiscPosition(groundRadius * 0.85f, groundCoreExclusion);

        float size = Random.Range(undulationMinSize, undulationMaxSize);
        float phase = Random.value;

        float n = Mathf.PerlinNoise(pos.x * undulationNoiseScale + 300f,
                                    pos.y * undulationNoiseScale + 300f);
        Color c = Color.Lerp(undulationCool, undulationWarm, n);
        c.a *= Random.Range(0.5f, 1.0f);

        verts[vi] = V3(pos);
        cols[vi] = c;
        uvs[vi] = new Vector2(0.5f, 0.5f);
        uv2s[vi] = new Vector2(0f, phase);

        for (int p = 0; p < 7; p++)
        {
            float angle = (p / 7f) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            float rv = size * Random.Range(0.65f, 1.35f);
            Vector2 pv = pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rv;
            verts[vi + 1 + p] = V3(pv);

            Color edgeC = c;
            edgeC.a *= Random.Range(0.08f, 0.25f);
            cols[vi + 1 + p] = edgeC;
            uvs[vi + 1 + p] = new Vector2(0.5f, 0.5f);
            uv2s[vi + 1 + p] = new Vector2(0f, phase);
        }

        for (int p = 0; p < 6; p++)
        {
            tris[ti++] = vi;
            tris[ti++] = vi + 1 + p;
            tris[ti++] = vi + 1 + ((p + 1) % 7);
        }

        vi += UNDUL_VERTS;
    }


    //  SAND RIPPLES 

    void GenerateSandRipples()
    {
        int ripplesPerMesh = MAX_VERTS_PER_MESH / RIPPLE_VERTS;
        int meshCount = Mathf.CeilToInt((float)rippleCount / ripplesPerMesh);
        int remaining = rippleCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(ripplesPerMesh, remaining);
            Mesh mesh = BuildRippleMesh(count, offset);
            rippleMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"DesertRipple_{m}", mesh, rippleMaterial, sortingOrder);
            rippleObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    Mesh BuildRippleMesh(int count, int seedOffset)
    {
        int maxV = count * RIPPLE_VERTS;
        int maxT = count * RIPPLE_TRIS_IDX;

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;

        // Ripples run perpendicular to wind
        Vector2 rippleDir = windPerp;
        float windRad = windAngle * Mathf.Deg2Rad;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 77777);

            Vector2 pos = GetRandomDiscPosition(groundRadius * 0.85f, groundCoreExclusion * 1.2f);

            float length = Random.Range(rippleMinLength, rippleMaxLength);
            float halfW = rippleWidth * Random.Range(0.6f, 1.5f);

            // Slight rotation jitter — ripples aren't perfectly parallel
            float rotJitter = Random.Range(-8f, 8f) * Mathf.Deg2Rad;
            Vector2 dir = Rotate2D(rippleDir, rotJitter);
            Vector2 perp = new Vector2(-dir.y, dir.x); // this points along wind

            float phase = Random.value;

            // Gentle curvature
            float curveAmt = length * Random.Range(-0.15f, 0.15f);

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

                // Crest on windward side, trough on lee
                Color crestSide = rippleCrest;
                Color troughSide = rippleTrough;
                crestSide.a *= segWidth[s];
                troughSide.a *= segWidth[s];

                cols[vi + s * 2 + 0] = troughSide; // lee side
                cols[vi + s * 2 + 1] = crestSide;  // windward crest

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

            vi += RIPPLE_VERTS;
        }

        return BuildMeshFromArrays("DesertRippleMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  CRACKED EARTH 

    void GenerateCrackedEarth()
    {
        int cracksPerMesh = MAX_VERTS_PER_MESH / CRACK_VERTS;
        int meshCount = Mathf.CeilToInt((float)crackedEarthCount / cracksPerMesh);
        int remaining = crackedEarthCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(cracksPerMesh, remaining);
            Mesh mesh = BuildCrackedEarthMesh(count, offset);
            crackMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"DesertCrack_{m}", mesh, crackMaterial, sortingOrder);
            crackObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    Mesh BuildCrackedEarthMesh(int count, int seedOffset)
    {
        int maxV = count * CRACK_VERTS;
        int maxT = count * CRACK_TRIS_IDX;

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 88888);

            // Place cracked earth in sheltered areas (areas with less wind coverage)
            Vector2 pos = GetRandomDiscPosition(groundRadius * 0.7f, groundCoreExclusion * 2f);

            float size = Random.Range(crackedEarthMinSize, crackedEarthMaxSize);
            float phase = Random.value;
            float angleOff = Random.Range(0f, Mathf.PI / 3f);

            // Center — surface color
            Color surfCol = crackedSurface;
            float n = Mathf.PerlinNoise(pos.x * 0.5f + 500f, pos.y * 0.5f + 500f);
            surfCol = Color.Lerp(surfCol, sandDark, n * 0.3f);
            surfCol.a *= Random.Range(0.6f, 1.0f);

            verts[vi] = V3(pos);
            cols[vi] = surfCol;
            uvs[vi] = new Vector2(0.5f, 0.5f);
            uv2s[vi] = new Vector2(0f, phase);

            // Inner ring (6 verts) — the surface between cracks
            for (int p = 0; p < 6; p++)
            {
                float a = angleOff + (p / 6f) * Mathf.PI * 2f;
                float r = size * 0.5f * (0.8f + Random.Range(0f, 0.4f));
                Vector2 pv = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

                verts[vi + 1 + p] = V3(pv);

                // Slightly darker near cracks
                Color innerCol = surfCol;
                innerCol = Color.Lerp(innerCol, crackColor, 0.15f);
                innerCol.a *= Random.Range(0.5f, 0.8f);
                cols[vi + 1 + p] = innerCol;

                Vector2 localDir = (pv - pos).normalized;
                uvs[vi + 1 + p] = new Vector2(0.5f + localDir.x * 0.5f, 0.5f + localDir.y * 0.5f);
                uv2s[vi + 1 + p] = new Vector2(0f, phase);
            }

            // Outer ring (6 verts) — crack edges, very dark and thin
            for (int p = 0; p < 6; p++)
            {
                float a = angleOff + (p / 6f) * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                float r = size * (0.85f + Random.Range(0f, 0.3f));
                Vector2 pv = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

                verts[vi + 7 + p] = V3(pv);

                Color outerCol = crackColor;
                outerCol.a *= Random.Range(0.15f, 0.35f);
                cols[vi + 7 + p] = outerCol;

                Vector2 localDir = (pv - pos).normalized;
                uvs[vi + 7 + p] = new Vector2(0.5f + localDir.x * 0.5f, 0.5f + localDir.y * 0.5f);
                uv2s[vi + 7 + p] = new Vector2(0f, phase);
            }

            // Inner fan (center to inner ring)
            for (int p = 0; p < 6; p++)
            {
                tris[ti++] = vi;
                tris[ti++] = vi + 1 + p;
                tris[ti++] = vi + 1 + ((p + 1) % 6);
            }

            // Outer ring (inner to outer)
            for (int p = 0; p < 6; p++)
            {
                int i0 = vi + 1 + p;
                int i1 = vi + 1 + ((p + 1) % 6);
                int o0 = vi + 7 + p;
                int o1 = vi + 7 + ((p + 1) % 6);

                tris[ti++] = i0; tris[ti++] = o0; tris[ti++] = i1;
                tris[ti++] = i1; tris[ti++] = o0; tris[ti++] = o1;
            }

            vi += CRACK_VERTS;
        }

        return BuildMeshFromArrays("DesertCrackMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  DRIED SCRUB 

    void GenerateDriedScrub()
    {
        int scrubsPerMesh = MAX_VERTS_PER_MESH / SCRUB_VERTS;
        int meshCount = Mathf.CeilToInt((float)scrubCount / scrubsPerMesh);
        int remaining = scrubCount;
        int offset = 0;

        for (int m = 0; m < meshCount; m++)
        {
            int count = Mathf.Min(scrubsPerMesh, remaining);
            Mesh mesh = BuildScrubMesh(count, offset);
            scrubMeshes.Add(mesh);

            GameObject go = CreateMeshObject($"DesertScrub_{m}", mesh, scrubMaterial, sortingOrder + 1);
            scrubObjects.Add(go);
            remaining -= count;
            offset += count;
        }
    }

    Mesh BuildScrubMesh(int count, int seedOffset)
    {
        int maxV = count * SCRUB_VERTS;
        int maxT = count * SCRUB_TRIS_IDX;

        Vector3[] verts = new Vector3[maxV];
        Color[] cols = new Color[maxV];
        Vector2[] uvs = new Vector2[maxV];
        Vector2[] uv2s = new Vector2[maxV];
        int[] tris = new int[maxT];

        int vi = 0, ti = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOffset + i + 66666);

            Vector2 pos = GetRandomDiscPosition(groundRadius * 0.75f, groundCoreExclusion * 1.5f);

            float h = Random.Range(scrubMinHeight, scrubMaxHeight);
            float w = scrubWidth * Random.Range(0.5f, 1.8f);
            float lean = Random.Range(-0.5f, 0.5f); // desert scrub leans more from wind
            float curve = Random.Range(-0.3f, 0.3f) * h;
            float phase = Random.value;

            // Scrub leans in wind direction
            lean += windDir.x * 0.3f;

            float[] segH = { 0f, 0.35f, 0.7f, 1.0f };
            float[] segW = { 1.0f, 0.6f, 0.25f, 0.0f };
            float[] segCurve = { 0f, 0.15f, 0.55f, 1.0f };

            Vector2 perp = new Vector2(1f, 0f);

            for (int s = 0; s < 4; s++)
            {
                float t = segH[s];
                float sy = t * h;
                float sx = lean * t + curve * segCurve[s];

                Vector2 center = pos + new Vector2(sx, sy);

                Color segCol;
                if (t < 0.3f)
                    segCol = scrubBase;
                else if (t < 0.7f)
                    segCol = Color.Lerp(scrubBase, scrubDead, (t - 0.3f) * 2.5f);
                else
                    segCol = Color.Lerp(scrubDead, scrubTip, (t - 0.7f) * 3.3f);

                // Add subtle color variation
                segCol.r += Random.Range(-0.03f, 0.03f);
                segCol.g += Random.Range(-0.02f, 0.02f);

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
                    tipCol.a *= 0.5f;
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

            vi += SCRUB_VERTS;
        }

        return BuildMeshFromArrays("DesertScrubMesh", verts, cols, uvs, uv2s, tris, vi, ti);
    }


    //  HEAT SHIMMER WISPS 

    void GenerateHeatShimmerWisps()
    {
        shimmerPos = new Vector2[shimmerWispCount];
        shimmerPhase = new float[shimmerWispCount];
        shimmerSize = new float[shimmerWispCount];
        shimmerRisePhase = new float[shimmerWispCount];

        int vertCount = shimmerWispCount * QUAD_VERTS;
        shimmerVerts = new Vector3[vertCount];
        Color[] sCols = new Color[vertCount];
        Vector2[] sUvs = new Vector2[vertCount];
        Vector2[] sUv2s = new Vector2[vertCount];
        int[] sTris = new int[shimmerWispCount * QUAD_TRIS_IDX];

        for (int i = 0; i < shimmerWispCount; i++)
        {
            shimmerPos[i] = new Vector2(
                Random.Range(-groundRadius * 0.8f, groundRadius * 0.8f),
                Random.Range(-1f, 3f)
            );
            shimmerPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            shimmerSize[i] = Random.Range(shimmerWispMinSize, shimmerWispMaxSize);
            shimmerRisePhase[i] = Random.Range(0f, Mathf.PI * 2f);

            Color c = shimmerWispColor;
            c.a *= Random.Range(0.4f, 1.0f);

            int v = i * 4;
            sCols[v] = c; sCols[v + 1] = c; sCols[v + 2] = c; sCols[v + 3] = c;
            sUvs[v] = new Vector2(0, 0); sUvs[v + 1] = new Vector2(1, 0);
            sUvs[v + 2] = new Vector2(1, 1); sUvs[v + 3] = new Vector2(0, 1);
            float pn = shimmerPhase[i] / (Mathf.PI * 2f);
            sUv2s[v] = new Vector2(0.3f, pn); sUv2s[v + 1] = new Vector2(0.3f, pn);
            sUv2s[v + 2] = new Vector2(0.3f, pn); sUv2s[v + 3] = new Vector2(0.3f, pn);

            int t = i * 6;
            sTris[t] = v; sTris[t + 1] = v + 2; sTris[t + 2] = v + 1;
            sTris[t + 3] = v; sTris[t + 4] = v + 3; sTris[t + 5] = v + 2;
        }

        RefreshQuadVerts(shimmerPos, shimmerSize, shimmerVerts, shimmerWispCount);

        shimmerMesh = new Mesh { name = "ShimmerWispMesh" };
        shimmerMesh.vertices = shimmerVerts; shimmerMesh.colors = sCols;
        shimmerMesh.uv = sUvs; shimmerMesh.uv2 = sUv2s; shimmerMesh.triangles = sTris;
        shimmerMesh.RecalculateNormals();
        shimmerMesh.bounds = new Bounds(Vector3.zero, Vector3.one * groundRadius * 3f);

        shimmerObject = new GameObject("HeatShimmer");
        shimmerObject.transform.SetParent(transform);
        shimmerObject.transform.localPosition = Vector3.zero;
        shimmerObject.transform.localScale = Vector3.one;
        shimmerObject.AddComponent<MeshFilter>().mesh = shimmerMesh;
        MeshRenderer mr = shimmerObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = shimmerMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder + 2;
    }



    //  SALTATION 

    void GenerateSaltation()
    {
        saltPos = new Vector2[saltationCount];
        saltSpeed = new float[saltationCount];
        saltPhase = new float[saltationCount];
        saltSize = new float[saltationCount];
        saltDepth = new float[saltationCount];
        saltBouncePhase = new float[saltationCount];

        int vertCount = saltationCount * QUAD_VERTS;
        saltVerts = new Vector3[vertCount];
        Color[] sCols = new Color[vertCount];
        Vector2[] sUvs = new Vector2[vertCount];
        Vector2[] sUv2s = new Vector2[vertCount];
        int[] sTris = new int[saltationCount * QUAD_TRIS_IDX];

        for (int i = 0; i < saltationCount; i++)
        {
            float depth = Random.value;
            saltDepth[i] = depth;

            float heightT = Mathf.Pow(Random.value, 1f + saltationGroundBias * 3f);
            saltPos[i] = new Vector2(
                Random.Range(-saltationSpawnRadius, saltationSpawnRadius),
                Mathf.Lerp(saltationMinHeight, saltationMaxHeight, heightT)
            );

            saltSpeed[i] = Mathf.Lerp(1.4f, 0.5f, depth) * Random.Range(0.8f, 1.2f);
            saltPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            saltBouncePhase[i] = Random.Range(0f, Mathf.PI * 2f);
            saltSize[i] = Mathf.Lerp(saltationMaxSize, saltationMinSize, depth) * Random.Range(0.6f, 1.4f);

            Color c;
            float cr = Random.value;
            if (cr < 0.3f) c = Color.Lerp(sandBright, sandMid, Random.Range(0f, 0.4f));
            else if (cr < 0.7f) c = Color.Lerp(sandMid, sandDark, Random.Range(0f, 0.5f));
            else c = Color.Lerp(sandDark, sandShadow, Random.Range(0f, 0.3f));
            c.a = Mathf.Lerp(0.85f, 0.30f, depth);
            c = ClampColor(c);

            int v = i * 4;
            sCols[v] = c; sCols[v + 1] = c; sCols[v + 2] = c; sCols[v + 3] = c;
            sUvs[v] = new Vector2(0, 0); sUvs[v + 1] = new Vector2(1, 0);
            sUvs[v + 2] = new Vector2(1, 1); sUvs[v + 3] = new Vector2(0, 1);
            float pn = saltPhase[i] / (Mathf.PI * 2f);
            sUv2s[v] = new Vector2(depth, pn); sUv2s[v + 1] = new Vector2(depth, pn);
            sUv2s[v + 2] = new Vector2(depth, pn); sUv2s[v + 3] = new Vector2(depth, pn);

            int t = i * 6;
            sTris[t] = v; sTris[t + 1] = v + 2; sTris[t + 2] = v + 1;
            sTris[t + 3] = v; sTris[t + 4] = v + 3; sTris[t + 5] = v + 2;
        }

        RefreshQuadVerts(saltPos, saltSize, saltVerts, saltationCount);

        saltMesh = new Mesh { name = "SaltationMesh" };
        saltMesh.vertices = saltVerts; saltMesh.colors = sCols;
        saltMesh.uv = sUvs; saltMesh.uv2 = sUv2s; saltMesh.triangles = sTris;
        saltMesh.RecalculateNormals();
        saltMesh.bounds = new Bounds(Vector3.zero, Vector3.one * saltationSpawnRadius * 4f);

        saltObject = new GameObject("SandSaltation");
        saltObject.transform.SetParent(transform);
        saltObject.transform.localPosition = Vector3.zero;
        saltObject.transform.localScale = Vector3.one;
        saltObject.AddComponent<MeshFilter>().mesh = saltMesh;
        MeshRenderer mr = saltObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = saltMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder + 1;
    }


    //  DUST HAZE 

    void GenerateDustHaze()
    {
        dustPos = new Vector2[dustCount];
        dustSpeed = new float[dustCount];
        dustPhase = new float[dustCount];
        dustSize = new float[dustCount];
        dustDepth = new float[dustCount];

        int vertCount = dustCount * QUAD_VERTS;
        dustVerts = new Vector3[vertCount];
        Color[] dCols = new Color[vertCount];
        Vector2[] dUvs = new Vector2[vertCount];
        Vector2[] dUv2s = new Vector2[vertCount];
        int[] dTris = new int[dustCount * QUAD_TRIS_IDX];

        for (int i = 0; i < dustCount; i++)
        {
            float depth = Random.value;
            dustDepth[i] = depth;
            dustPos[i] = new Vector2(
                Random.Range(-dustSpawnRadius, dustSpawnRadius),
                Random.Range(dustMinHeight, dustMaxHeight)
            );
            dustSpeed[i] = Mathf.Lerp(0.8f, 0.2f, depth) * Random.Range(0.7f, 1.3f);
            dustPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            dustSize[i] = Mathf.Lerp(dustMaxSize, dustMinSize, depth) * Random.Range(0.5f, 1.5f);

            Color c = dustColor;
            c.a *= Mathf.Lerp(1.0f, 0.3f, depth) * Random.Range(0.5f, 1.0f);
            c = ClampColor(c);

            int v = i * 4;
            dCols[v] = c; dCols[v + 1] = c; dCols[v + 2] = c; dCols[v + 3] = c;
            dUvs[v] = new Vector2(0, 0); dUvs[v + 1] = new Vector2(1, 0);
            dUvs[v + 2] = new Vector2(1, 1); dUvs[v + 3] = new Vector2(0, 1);
            float pn = dustPhase[i] / (Mathf.PI * 2f);
            dUv2s[v] = new Vector2(depth, pn); dUv2s[v + 1] = new Vector2(depth, pn);
            dUv2s[v + 2] = new Vector2(depth, pn); dUv2s[v + 3] = new Vector2(depth, pn);

            int t = i * 6;
            dTris[t] = v; dTris[t + 1] = v + 2; dTris[t + 2] = v + 1;
            dTris[t + 3] = v; dTris[t + 4] = v + 3; dTris[t + 5] = v + 2;
        }

        RefreshQuadVerts(dustPos, dustSize, dustVerts, dustCount);

        dustMesh = new Mesh { name = "DustHazeMesh" };
        dustMesh.vertices = dustVerts; dustMesh.colors = dCols;
        dustMesh.uv = dUvs; dustMesh.uv2 = dUv2s; dustMesh.triangles = dTris;
        dustMesh.RecalculateNormals();
        dustMesh.bounds = new Bounds(Vector3.zero, Vector3.one * dustSpawnRadius * 4f);

        dustObject = new GameObject("DustHaze");
        dustObject.transform.SetParent(transform);
        dustObject.transform.localPosition = Vector3.zero;
        dustObject.transform.localScale = Vector3.one;
        dustObject.AddComponent<MeshFilter>().mesh = dustMesh;
        MeshRenderer mr = dustObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = dustMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder + 3;
    }


    //  DUST DEVILS 

    void GenerateDustDevils()
    {
        if (dustDevilCount <= 0) return;

        totalDevilParticles = dustDevilCount * dustDevilParticles;
        devilCenter = new Vector2[dustDevilCount];
        devilDriftPhase = new float[dustDevilCount];
        devilSpinPhase = new float[dustDevilCount];
        devilParticleStart = new int[dustDevilCount];

        devilPartPos = new Vector2[totalDevilParticles];
        devilPartPhase = new float[totalDevilParticles];
        devilPartHeight = new float[totalDevilParticles];
        devilPartSize = new float[totalDevilParticles];

        int vertCount = totalDevilParticles * QUAD_VERTS;
        devilVerts = new Vector3[vertCount];
        Color[] dCols = new Color[vertCount];
        Vector2[] dUvs = new Vector2[vertCount];
        Vector2[] dUv2s = new Vector2[vertCount];
        int[] dTris = new int[totalDevilParticles * QUAD_TRIS_IDX];

        int pIdx = 0;
        for (int d = 0; d < dustDevilCount; d++)
        {
            devilCenter[d] = GetRandomDiscPosition(groundRadius * 0.6f, groundCoreExclusion * 3f);
            devilDriftPhase[d] = Random.Range(0f, Mathf.PI * 2f);
            devilSpinPhase[d] = Random.Range(0f, Mathf.PI * 2f);
            devilParticleStart[d] = pIdx;

            for (int p = 0; p < dustDevilParticles; p++)
            {
                devilPartPhase[pIdx] = Random.Range(0f, Mathf.PI * 2f);
                devilPartHeight[pIdx] = Random.value; // 0=bottom, 1=top
                devilPartSize[pIdx] = Random.Range(0.01f, 0.04f);

                // Color: darker at bottom, lighter/more transparent at top
                Color c = dustDevilColor;
                float heightT = devilPartHeight[pIdx];
                c.a *= Mathf.Lerp(0.5f, 0.12f, heightT);
                c = Color.Lerp(c, sandBright, heightT * 0.3f);
                c = ClampColor(c);

                int v = pIdx * 4;
                dCols[v] = c; dCols[v + 1] = c; dCols[v + 2] = c; dCols[v + 3] = c;
                dUvs[v] = new Vector2(0, 0); dUvs[v + 1] = new Vector2(1, 0);
                dUvs[v + 2] = new Vector2(1, 1); dUvs[v + 3] = new Vector2(0, 1);
                float pn = devilPartPhase[pIdx] / (Mathf.PI * 2f);
                dUv2s[v] = new Vector2(heightT, pn); dUv2s[v + 1] = new Vector2(heightT, pn);
                dUv2s[v + 2] = new Vector2(heightT, pn); dUv2s[v + 3] = new Vector2(heightT, pn);

                int t = pIdx * 6;
                dTris[t] = v; dTris[t + 1] = v + 2; dTris[t + 2] = v + 1;
                dTris[t + 3] = v; dTris[t + 4] = v + 3; dTris[t + 5] = v + 2;

                pIdx++;
            }
        }

        // Initial positions
        RefreshDevilVerts(0f);

        devilMesh = new Mesh { name = "DustDevilMesh" };
        devilMesh.vertices = devilVerts; devilMesh.colors = dCols;
        devilMesh.uv = dUvs; devilMesh.uv2 = dUv2s; devilMesh.triangles = dTris;
        devilMesh.RecalculateNormals();
        devilMesh.bounds = new Bounds(Vector3.zero, Vector3.one * groundRadius * 3f);

        devilObject = new GameObject("DustDevils");
        devilObject.transform.SetParent(transform);
        devilObject.transform.localPosition = Vector3.zero;
        devilObject.transform.localScale = Vector3.one;
        devilObject.AddComponent<MeshFilter>().mesh = devilMesh;
        MeshRenderer mr = devilObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = devilMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder + 2;
    }

    void RefreshDevilVerts(float t)
    {
        for (int d = 0; d < dustDevilCount; d++)
        {
            // Devil center drifts slowly
            float dx = Mathf.Sin(t * dustDevilDriftSpeed * 0.3f + devilDriftPhase[d]) * 3f;
            float dy = Mathf.Cos(t * dustDevilDriftSpeed * 0.2f + devilDriftPhase[d] * 1.3f) * 2f;
            Vector2 center = devilCenter[d] + new Vector2(dx, dy);

            int startIdx = devilParticleStart[d];
            for (int p = 0; p < dustDevilParticles; p++)
            {
                int idx = startIdx + p;
                float heightT = devilPartHeight[idx];
                float phase = devilPartPhase[idx];

                // Radius widens toward top (funnel shape)
                float radius = dustDevilRadius * (0.3f + heightT * 0.7f);

                // Spin angle
                float spinAngle = t * dustDevilSpinSpeed * (1f + heightT * 0.5f) + phase + devilSpinPhase[d];
                float px = center.x + Mathf.Cos(spinAngle) * radius;
                float py = center.y + heightT * dustDevilHeight;

                // Add slight wobble
                px += Mathf.Sin(t * 2f + phase * 5f) * 0.05f;
                py += Mathf.Cos(t * 1.5f + phase * 3f) * 0.03f;

                float s = devilPartSize[idx];
                int vi = idx * 4;
                devilVerts[vi + 0] = new Vector3(px - s, py - s, 0f);
                devilVerts[vi + 1] = new Vector3(px + s, py - s, 0f);
                devilVerts[vi + 2] = new Vector3(px + s, py + s, 0f);
                devilVerts[vi + 3] = new Vector3(px - s, py + s, 0f);
            }
        }
    }


    //  ANIMATION

    void Update()
    {
        float dt = Time.deltaTime;
        float t = Time.time;
        AnimateSaltation(dt, t);
        AnimateDust(dt, t);
        AnimateShimmerWisps(dt, t);
        AnimateDustDevils(t);
    }

    void AnimateSaltation(float dt, float t)
    {
        if (saltPos == null || saltMesh == null) return;
        float spawnW = saltationSpawnRadius * 1.2f;

        for (int i = 0; i < saltationCount; i++)
        {
            float depth = saltDepth[i];
            float phase = saltPhase[i];
            float speed = saltSpeed[i];

            float wx = windStrength * Mathf.Lerp(1.0f, 0.3f, depth) * speed;
            float moveX = wx * windDir.x;
            float moveY = wx * windDir.y * 0.15f;

            float gx = saltPos[i].x * 0.12f;
            float gy = saltPos[i].y * 0.12f;
            float gt = t * gustSpeed;
            float gust = Mathf.Sin(gx * 0.7f + gy * 0.4f + gt) * 0.5f
                       + Mathf.Sin(gx * 0.4f - gy * 0.8f + gt * 1.4f) * 0.3f
                       + Mathf.Sin(gx * 0.6f + gy * 0.5f - gt * 0.6f) * 0.2f;
            float gustPulse = 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Sin(gt * 0.15f + saltPos[i].x * 0.04f));
            moveX += gust * gustStrength * gustPulse * windDir.x;

            float turbX = Mathf.Sin(t * turbulence * 3.5f + phase * 2.3f) * turbulence * 0.03f;
            float turbY = Mathf.Cos(t * turbulence * 2.8f + phase * 1.9f) * turbulence * 0.015f;

            float bounceT = Mathf.Abs(Mathf.Sin(t * bounceSpeed * speed + saltBouncePhase[i]));
            float bounceY = bounceT * bounceHeight * (1f - depth * 0.7f);

            float driftP = Mathf.Sin(t * 0.8f + phase * 3f) * streakDrift * (1f - depth * 0.5f);

            saltPos[i].x += (moveX + turbX + driftP * windPerp.x) * dt;
            saltPos[i].y += (moveY + turbY + driftP * windPerp.y) * dt;

            if (saltPos[i].x > spawnW)
            {
                saltPos[i].x = -spawnW + Random.Range(0f, 2f);
                saltPos[i].y = Mathf.Lerp(saltationMinHeight, saltationMaxHeight,
                    Mathf.Pow(Random.value, 1f + saltationGroundBias * 3f));
                saltPhase[i] = Random.Range(0f, Mathf.PI * 2f);
                saltBouncePhase[i] = Random.Range(0f, Mathf.PI * 2f);
            }
            else if (saltPos[i].x < -spawnW)
            {
                saltPos[i].x = spawnW - Random.Range(0f, 2f);
                saltPos[i].y = Mathf.Lerp(saltationMinHeight, saltationMaxHeight,
                    Mathf.Pow(Random.value, 1f + saltationGroundBias * 3f));
                saltPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            }
            if (saltPos[i].y > saltationMaxHeight + 2f || saltPos[i].y < saltationMinHeight - 2f)
                saltPos[i].y = Mathf.Lerp(saltationMinHeight, saltationMaxHeight,
                    Mathf.Pow(Random.value, 1f + saltationGroundBias * 3f));

            float fy = saltPos[i].y + bounceY;
            float px = saltPos[i].x;
            float s = saltSize[i];
            int vi = i * 4;
            saltVerts[vi] = new Vector3(px - s, fy - s * 0.6f, 0f);
            saltVerts[vi + 1] = new Vector3(px + s, fy - s * 0.6f, 0f);
            saltVerts[vi + 2] = new Vector3(px + s, fy + s * 0.6f, 0f);
            saltVerts[vi + 3] = new Vector3(px - s, fy + s * 0.6f, 0f);
        }
        saltMesh.vertices = saltVerts;
    }

    void AnimateDust(float dt, float t)
    {
        if (dustPos == null || dustMesh == null) return;
        float spawnW = dustSpawnRadius * 1.2f;

        for (int i = 0; i < dustCount; i++)
        {
            float depth = dustDepth[i];
            float phase = dustPhase[i];
            float speed = dustSpeed[i];

            float wx = windStrength * dustWindMult * Mathf.Lerp(1.0f, 0.3f, depth) * speed;
            dustPos[i].x += (wx * windDir.x + Mathf.Sin(t * 0.4f + phase) * dustDrift
                           + Mathf.Sin(t * dustSwirl * 0.7f + phase * 2f) * dustSwirl * 0.08f) * dt;
            dustPos[i].y += (wx * windDir.y * 0.1f + Mathf.Cos(t * 0.3f + phase * 1.3f) * dustDrift * 0.5f
                           + Mathf.Cos(t * dustSwirl * 0.5f + phase * 1.5f) * dustSwirl * 0.06f) * dt;

            if (dustPos[i].x > spawnW) { dustPos[i].x = -spawnW + Random.Range(0f, 3f); dustPos[i].y = Random.Range(dustMinHeight, dustMaxHeight); }
            else if (dustPos[i].x < -spawnW) { dustPos[i].x = spawnW - Random.Range(0f, 3f); dustPos[i].y = Random.Range(dustMinHeight, dustMaxHeight); }
            if (dustPos[i].y > dustMaxHeight + 3f) dustPos[i].y = dustMinHeight;
            else if (dustPos[i].y < dustMinHeight - 3f) dustPos[i].y = dustMaxHeight;
        }
        RefreshQuadVerts(dustPos, dustSize, dustVerts, dustCount);
        dustMesh.vertices = dustVerts;
    }

    void AnimateShimmerWisps(float dt, float t)
    {
        if (shimmerPos == null || shimmerMesh == null) return;

        for (int i = 0; i < shimmerWispCount; i++)
        {
            float phase = shimmerPhase[i];

            // Rise slowly with lateral wobble
            shimmerPos[i].y += shimmerRiseSpeed * dt * (0.5f + 0.5f * Mathf.Sin(t * 0.3f + phase));
            shimmerPos[i].x += Mathf.Sin(t * 0.8f + phase * 3f) * 0.15f * dt;

            // Fade cycle: rise then reset
            if (shimmerPos[i].y > 5f)
            {
                shimmerPos[i].y = Random.Range(-1f, 0.5f);
                shimmerPos[i].x = Random.Range(-groundRadius * 0.8f, groundRadius * 0.8f);
                shimmerPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            }
        }
        RefreshQuadVerts(shimmerPos, shimmerSize, shimmerVerts, shimmerWispCount);
        shimmerMesh.vertices = shimmerVerts;
    }

    void AnimateDustDevils(float t)
    {
        if (devilVerts == null || devilMesh == null) return;
        RefreshDevilVerts(t);
        devilMesh.vertices = devilVerts;
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
        CleanupList(groundObjects, groundMeshes);
        CleanupList(rippleObjects, rippleMeshes);
        CleanupList(crackObjects, crackMeshes);
        CleanupList(scrubObjects, scrubMeshes);

        DestroyObj(shimmerObject); DestroyMesh(shimmerMesh);
        DestroyObj(saltObject); DestroyMesh(saltMesh);
        DestroyObj(dustObject); DestroyMesh(dustMesh);
        DestroyObj(devilObject); DestroyMesh(devilMesh);

        DestroyMat(groundMaterial); DestroyMat(rippleMaterial);
        DestroyMat(crackMaterial); DestroyMat(scrubMaterial);
        DestroyMat(shimmerMaterial);
        DestroyMat(saltMaterial); DestroyMat(dustMaterial);
        DestroyMat(devilMaterial);

        shimmerObject = null; saltObject = null; dustObject = null; devilObject = null;
        shimmerMesh = null; saltMesh = null; dustMesh = null; devilMesh = null;
        groundMaterial = null; rippleMaterial = null; crackMaterial = null;
        scrubMaterial = null; shimmerMaterial = null;
        saltMaterial = null; dustMaterial = null; devilMaterial = null;
    }

    void CleanupList(List<GameObject> objects, List<Mesh> meshes)
    {
        foreach (var go in objects) if (go != null) DestroyImmediate(go);
        objects.Clear();
        foreach (var m in meshes) if (m != null) DestroyImmediate(m);
        meshes.Clear();
    }

    void DestroyObj(GameObject go) { if (go != null) DestroyImmediate(go); }
    void DestroyMesh(Mesh m) { if (m != null) DestroyImmediate(m); }
    void DestroyMat(Material m) { if (m != null) DestroyImmediate(m); }

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
