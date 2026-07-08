using UnityEngine;
using System.Collections.Generic;


public class WastelandOverlay : MonoBehaviour
{
    //  GROUND 


    [Header("Ground — General")]
    public int groundElementCount = 180000;
    public float groundRadius = 60f;
    public float groundCoreExclusion = 1.5f;

    [Header("Ground — Cracks")]
    [Range(0f, 0.5f)] public float crackRatio = 0.22f;
    public float crackMinLength = 0.25f;
    public float crackMaxLength = 1.60f;
    public float crackMinWidth = 0.006f;
    public float crackMaxWidth = 0.025f;
    [Range(0f, 1f)] public float crackJaggedness = 0.60f;

    [Header("Ground — Dead Twigs")]
    [Range(0f, 0.3f)] public float twigRatio = 0.10f;
    public float twigMinLength = 0.05f;
    public float twigMaxLength = 0.18f;
    public float twigWidth = 0.005f;

    [Header("Ground — Puddle Stains")]
    [Range(0f, 0.2f)] public float puddleRatio = 0.05f;
    public float puddleMinSize = 0.12f;
    public float puddleMaxSize = 0.45f;

    [Header("Ground — Rubble")]
    [Range(0f, 0.4f)] public float rubbleRatio = 0.20f;
    public float rubbleMinSize = 0.007f;
    public float rubbleMaxSize = 0.024f;

    [Header("Ground — Scorch")]
    [Range(0f, 0.2f)] public float scorchRatio = 0.06f;
    public float scorchMinLength = 0.10f;
    public float scorchMaxLength = 0.40f;
    public float scorchWidth = 0.035f;

    //  TOXIC POOLS 

    [Header("Toxic Pools")]
    public int toxicPoolCount = 200;
    public float toxicPoolMinSize = 0.3f;
    public float toxicPoolMaxSize = 1.2f;
    public Color toxicPoolCenter = new Color(0.18f, 0.32f, 0.08f, 0.45f);
    public Color toxicPoolEdge = new Color(0.28f, 0.38f, 0.10f, 0.15f);

    //  RUST STAINS 

    [Header("Rust Stains")]
    public int rustStainCount = 500;
    public float rustStainMinSize = 0.15f;
    public float rustStainMaxSize = 0.6f;
    public Color rustColorDark = new Color(0.35f, 0.14f, 0.06f, 0.50f);
    public Color rustColorLight = new Color(0.52f, 0.28f, 0.10f, 0.30f);

    //  BONE/DEBRIS SCATTER 

    [Header("Bone/Debris Scatter")]
    public int boneCount = 400;
    public float boneMinLength = 0.06f;
    public float boneMaxLength = 0.25f;
    public float boneWidth = 0.012f;
    public Color boneColor = new Color(0.62f, 0.56f, 0.45f, 0.70f);
    public Color boneTip = new Color(0.48f, 0.42f, 0.35f, 0.45f);

    //  DUST STORM

    [Header("Dust Storm (Main Effect)")]
    public int dustStormCount = 4500;
    public float dustStormSpawnRadius = 60f;
    public float dustStormMinHeight = -60f;
    public float dustStormMaxHeight = 60f;
    public float dustStormMinSize = 0.03f;
    public float dustStormMaxSize = 0.14f;

    //  ASH FLECKS

    [Header("Ash Flecks (Low Chaotic)")]
    public int ashCount = 6000;
    public float ashSpawnRadius = 60f;
    public float ashMinHeight = -60f;
    public float ashMaxHeight = 60f;
    public float ashMinSize = 0.015f;
    public float ashMaxSize = 0.045f;

    //  EMBER PARTICLES 

    [Header("Ember Particles")]
    public int emberCount = 1500;
    public float emberSpawnRadius = 60f;
    public float emberMinHeight = -60f;
    public float emberMaxHeight = 60f;
    public float emberMinSize = 0.02f;
    public float emberMaxSize = 0.07f;
    public Color emberColorHot = new Color(1.0f, 0.50f, 0.10f, 0.92f);
    public Color emberColorDim = new Color(0.70f, 0.28f, 0.05f, 0.55f);
    public float emberFlickerSpeed = 5.0f;

    //  SMOKE WISPS 

    [Header("Smoke Wisps")]
    public int smokeWispCount = 200;
    public float smokeMinSize = 0.25f;
    public float smokeMaxSize = 0.8f;
    public float smokeRiseSpeed = 0.15f;
    public Color smokeColor = new Color(0.12f, 0.10f, 0.08f, 0.18f);

    //  HIGH HAZE

    [Header("High Haze (Fog Layer)")]
    public int hazeCount = 350;
    public float hazeSpawnRadius = 60f;
    public float hazeMinHeight = -40f;
    public float hazeMaxHeight = 60f;
    public float hazeMinSize = 0.20f;
    public float hazeMaxSize = 0.70f;

    //  COLORS

    [Header("Ground Colors")]
    public Color crackDark = new Color(0.06f, 0.05f, 0.04f, 0.60f);
    public Color crackLight = new Color(0.16f, 0.14f, 0.11f, 0.35f);
    public Color twigColor = new Color(0.10f, 0.07f, 0.03f, 0.55f);
    public Color puddleColor = new Color(0.18f, 0.22f, 0.12f, 0.14f);
    public Color rubbleDark = new Color(0.08f, 0.07f, 0.05f, 0.48f);
    public Color rubbleLight = new Color(0.22f, 0.20f, 0.16f, 0.30f);
    public Color scorchColor = new Color(0.05f, 0.04f, 0.03f, 0.20f);

    [Header("Airborne Colors")]
    public Color dustStormNear = new Color(0.48f, 0.44f, 0.38f, 0.50f);
    public Color dustStormFar = new Color(0.55f, 0.52f, 0.46f, 0.15f);
    public Color ashNear = new Color(0.12f, 0.10f, 0.08f, 0.65f);
    public Color ashFar = new Color(0.28f, 0.25f, 0.22f, 0.25f);
    public Color hazeColor = new Color(0.50f, 0.48f, 0.44f, 0.08f);

    //  WIND

    [Header("Wind")]
    public float windStrength = 1.8f;
    public float windAngle = 8f;
    public float windSpeed = 1.2f;
    public float gustStrength = 1.0f;
    public float gustSpeed = 0.35f;
    public float turbulence = 2.0f;

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    // Base order for the ground decals. Must be > the background's order (-1) or the
    // meshes tie with the background and the camera's Y-axis transparency sort shows
    // them only in a central square. 0 sits just above background, below tower slots
    // (1)/paths (500)/units (500+). Sub-layers add their offsets on top of this.
    public int sortingOrder = 0;


    //  INTERNALS

    private const int MAX_VERTS = 60000;
    private const int CRACK_V = 8, TWIG_V = 6, PUDDLE_V = 8, RUBBLE_V = 5, SCORCH_V = 6;
    private const int POOL_V = 8, POOL_TI = 18;
    private const int RUST_V = 8, RUST_TI = 18;
    private const int BONE_V = 6, BONE_TI = 12;
    private const int QUAD_V = 4, QUAD_TI = 6;
    private int MaxV() => Mathf.Max(CRACK_V, Mathf.Max(TWIG_V, Mathf.Max(PUDDLE_V, Mathf.Max(RUBBLE_V, SCORCH_V))));

    private List<Mesh> groundMeshes = new List<Mesh>();
    private List<GameObject> groundObjects = new List<GameObject>();
    private Material groundMat, dustStormMat, ashMat, hazeMat, emberMat, smokeMat;

    // Toxic pools
    private List<Mesh> poolMeshes = new List<Mesh>();
    private List<GameObject> poolObjects = new List<GameObject>();
    private Material poolMat;

    // Rust stains
    private List<Mesh> rustMeshes = new List<Mesh>();
    private List<GameObject> rustObjects = new List<GameObject>();
    private Material rustMat;

    // Bone/debris
    private List<Mesh> boneMeshes = new List<Mesh>();
    private List<GameObject> boneObjects = new List<GameObject>();
    private Material boneMaterial;

    // Dust storm
    private Mesh dsMesh; private GameObject dsObj;
    private Vector2[] dsPos; private float[] dsSpd, dsPh, dsSz, dsDp;
    private Vector3[] dsVerts;

    // Ash
    private Mesh ashMesh; private GameObject ashObj;
    private Vector2[] ashPos; private float[] ashSpd, ashPh, ashSz, ashDp;
    private Vector3[] ashVerts;

    // Embers
    private Mesh embMesh; private GameObject embObj;
    private Vector2[] embPos; private float[] embSpd, embPh, embSz, embDp, embFlicker;
    private Vector3[] embVerts;

    // Smoke wisps
    private Mesh smkMesh; private GameObject smkObj;
    private Vector2[] smkPos; private float[] smkPh, smkSz;
    private Vector3[] smkVerts;

    // Haze
    private Mesh hzMesh; private GameObject hzObj;
    private Vector2[] hzPos; private float[] hzSpd, hzPh, hzSz, hzDp;
    private Vector3[] hzVerts;

    private Vector2 windDir, windPerp;

    private bool _generated;

    void Start()
    {
        // BiomeManager calls GenerateWasteland() right after AddComponent; Unity then fires
        // Start() a frame later. Without this guard we build and immediately discard
        // the entire mesh set twice per biome.
        if (!_generated) GenerateWasteland();
    }

    [ContextMenu("Regenerate Wasteland")]
    public void GenerateWasteland()
    {
        _generated = true;
        Cleanup();
        float rad = windAngle * Mathf.Deg2Rad;
        windDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
        windPerp = new Vector2(-windDir.y, windDir.x);
        CreateMaterials();
        GenerateGround();
        GenerateToxicPools();
        GenerateRustStains();
        GenerateBoneDebris();
        GenerateDustStorm();
        GenerateAshFlecks();
        GenerateEmbers();
        GenerateSmokeWisps();
        GenerateHighHaze();
    }

    void CreateMaterials()
    {
        Shader sh = Shader.Find("Custom/WastelandWind");
        if (sh == null || sh.name == "Hidden/InternalErrorShader")
        {
            sh = Shader.Find("Sprites/Default");
            Debug.LogWarning("[WastelandOverlay] Custom/WastelandWind not found.");
        }

        groundMat = new Material(sh);
        groundMat.SetFloat("_GritAmount", 0.12f);
        groundMat.SetFloat("_Desaturation", 0.5f);
        groundMat.SetFloat("_Brightness", 0.9f);
        groundMat.SetFloat("_ToxicStrength", 0.03f);

        poolMat = new Material(sh);
        poolMat.SetFloat("_GritAmount", 0.06f);
        poolMat.SetFloat("_Softness", 0.40f);
        poolMat.SetFloat("_Desaturation", 0.05f);
        poolMat.SetFloat("_Brightness", 0.9f);
        poolMat.SetFloat("_ToxicStrength", 0.25f);
        poolMat.SetFloat("_ToxicSpeed", 1.2f);
        poolMat.SetFloat("_AcidHaze", 0.08f);

        rustMat = new Material(sh);
        rustMat.SetFloat("_GritAmount", 0.18f);
        rustMat.SetFloat("_Softness", 0.30f);
        rustMat.SetFloat("_Desaturation", 0.10f);
        rustMat.SetFloat("_Brightness", 1.0f);
        rustMat.SetFloat("_ToxicStrength", 0.0f);
        rustMat.SetFloat("_CorrosionEdge", 0.20f);

        boneMaterial = new Material(sh);
        boneMaterial.SetFloat("_GritAmount", 0.08f);
        boneMaterial.SetFloat("_Desaturation", 0.6f);
        boneMaterial.SetFloat("_Brightness", 1.0f);
        boneMaterial.SetFloat("_ToxicStrength", 0.0f);

        dustStormMat = new Material(sh);
        dustStormMat.SetFloat("_GritAmount", 0.10f);
        dustStormMat.SetFloat("_Softness", 0.35f);
        dustStormMat.SetFloat("_Desaturation", 0.35f);
        dustStormMat.SetFloat("_Brightness", 0.92f);
        dustStormMat.SetFloat("_ToxicStrength", 0.08f);
        dustStormMat.SetFloat("_AcidHaze", 0.06f);
        dustStormMat.SetFloat("_FlickerStrength", 0.06f);

        ashMat = new Material(sh);
        ashMat.SetFloat("_GritAmount", 0.22f);
        ashMat.SetFloat("_GritFrequency", 12f);
        ashMat.SetFloat("_Desaturation", 0.35f);
        ashMat.SetFloat("_Brightness", 0.80f);
        ashMat.SetFloat("_ToxicStrength", 0.04f);

        emberMat = new Material(sh);
        emberMat.SetFloat("_GritAmount", 0.03f);
        emberMat.SetFloat("_Softness", 0.20f);
        emberMat.SetFloat("_Desaturation", 0.0f);
        emberMat.SetFloat("_Brightness", 1.5f);
        emberMat.SetFloat("_ToxicStrength", 0.0f);
        emberMat.SetFloat("_EmberGlow", 0.8f);
        emberMat.SetFloat("_FlickerStrength", 0.12f);
        emberMat.SetFloat("_FlickerSpeed", 6.0f);

        smokeMat = new Material(sh);
        smokeMat.SetFloat("_GritAmount", 0.04f);
        smokeMat.SetFloat("_Softness", 0.45f);
        smokeMat.SetFloat("_Desaturation", 0.65f);
        smokeMat.SetFloat("_Brightness", 0.5f);
        smokeMat.SetFloat("_ToxicStrength", 0.04f);
        smokeMat.SetFloat("_AcidHaze", 0.05f);

        hazeMat = new Material(sh);
        hazeMat.SetFloat("_GritAmount", 0.04f);
        hazeMat.SetFloat("_Softness", 0.55f);
        hazeMat.SetFloat("_Desaturation", 0.6f);
        hazeMat.SetFloat("_Brightness", 0.85f);
        hazeMat.SetFloat("_ToxicStrength", 0.06f);
    }


    //  GROUND 

    void GenerateGround()
    {
        int epm = MAX_VERTS / MaxV();
        int mc = Mathf.CeilToInt((float)groundElementCount / epm);
        int rem = groundElementCount, off = 0;
        for (int m = 0; m < mc; m++)
        {
            int cnt = Mathf.Min(epm, rem);
            Mesh mesh = BuildGroundMesh(cnt, off);
            groundMeshes.Add(mesh);
            GameObject go = MkObj($"WastelandGround_{m}", mesh, groundMat, sortingOrder);
            groundObjects.Add(go);
            rem -= cnt; off += cnt;
        }
    }

    enum GT { Crack, Twig, Puddle, Rubble, Scorch }

    Mesh BuildGroundMesh(int count, int seed)
    {
        int maxVt = count * MaxV(), maxTi = count * 18;
        Vector3[] v = new Vector3[maxVt]; Color[] c = new Color[maxVt];
        Vector2[] u = new Vector2[maxVt], u2 = new Vector2[maxVt];
        int[] tri = new int[maxTi];
        int vi = 0, ti = 0;

        int nCr = Mathf.RoundToInt(count * crackRatio);
        int nTw = Mathf.RoundToInt(count * twigRatio);
        int nPu = Mathf.RoundToInt(count * puddleRatio);
        int nSc = Mathf.RoundToInt(count * scorchRatio);

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seed + i + 66613);
            GT t;
            if (i < nCr) t = GT.Crack;
            else if (i < nCr + nTw) t = GT.Twig;
            else if (i < nCr + nTw + nPu) t = GT.Puddle;
            else if (i < nCr + nTw + nPu + nSc) t = GT.Scorch;
            else t = GT.Rubble;

            switch (t)
            {
                case GT.Crack: MkCrack(v, c, u, u2, tri, ref vi, ref ti); break;
                case GT.Twig: MkTwig(v, c, u, u2, tri, ref vi, ref ti); break;
                case GT.Puddle: MkPuddle(v, c, u, u2, tri, ref vi, ref ti); break;
                case GT.Rubble: MkRubble(v, c, u, u2, tri, ref vi, ref ti); break;
                case GT.Scorch: MkScorch(v, c, u, u2, tri, ref vi, ref ti); break;
            }
        }

        Mesh m = new Mesh { name = "WastelandGround" };
        if (vi > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = Trim(v, vi); m.colors = Trim(c, vi);
        m.uv = Trim(u, vi); m.uv2 = Trim(u2, vi);
        m.triangles = Trim(tri, ti);
        m.RecalculateNormals(); m.RecalculateBounds();
        Bounds b = m.bounds; b.Expand(3f); m.bounds = b;
        return m;
    }

    void MkCrack(Vector3[] v, Color[] c, Vector2[] u, Vector2[] u2, int[] tri, ref int vi, ref int ti)
    {
        Vector2 pos = RandDisc(groundRadius, groundCoreExclusion);
        float len = Random.Range(crackMinLength, crackMaxLength);
        float halfW = Random.Range(crackMinWidth, crackMaxWidth) * 0.5f;
        float baseA = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(baseA), Mathf.Sin(baseA));
        float ph = Random.value;
        float thickT = Mathf.InverseLerp(crackMinWidth, crackMaxWidth, halfW * 2f);
        Color col = Color.Lerp(crackLight, crackDark, thickT * 0.7f + Random.Range(0f, 0.3f));
        col.a *= Random.Range(0.7f, 1.0f);

        Vector2[] spine = new Vector2[4]; spine[0] = pos;
        float segL = len / 3f;
        for (int s = 1; s < 4; s++)
        { float jag = Random.Range(-65f, 65f) * crackJaggedness * Mathf.Deg2Rad; dir = Rot2D(dir, jag); spine[s] = spine[s - 1] + dir * segL * Random.Range(0.7f, 1.3f); }

        float[] wMul = { 0.35f, 1.0f, 0.85f, 0.15f };
        for (int s = 0; s < 4; s++)
        {
            float w = halfW * wMul[s];
            Vector2 tan = s == 0 ? (spine[1] - spine[0]).normalized : s == 3 ? (spine[3] - spine[2]).normalized : (spine[s + 1] - spine[s - 1]).normalized;
            Vector2 perp = new Vector2(-tan.y, tan.x);
            v[vi + s * 2] = V3(spine[s] - perp * w); v[vi + s * 2 + 1] = V3(spine[s] + perp * w);
            Color sc = col; sc.a *= wMul[s];
            c[vi + s * 2] = sc; c[vi + s * 2 + 1] = sc;
            u[vi + s * 2] = new Vector2(0f, s / 3f); u[vi + s * 2 + 1] = new Vector2(1f, s / 3f);
            u2[vi + s * 2] = new Vector2(0f, ph); u2[vi + s * 2 + 1] = new Vector2(0f, ph);
        }
        for (int s = 0; s < 3; s++) { int bl = vi + s * 2; tri[ti++] = bl; tri[ti++] = bl + 2; tri[ti++] = bl + 1; tri[ti++] = bl + 1; tri[ti++] = bl + 2; tri[ti++] = bl + 3; }
        vi += CRACK_V;
    }

    void MkTwig(Vector3[] v, Color[] c, Vector2[] u, Vector2[] u2, int[] tri, ref int vi, ref int ti)
    {
        Vector2 pos = RandDisc(groundRadius, groundCoreExclusion);
        float len = Random.Range(twigMinLength, twigMaxLength); float hw = twigWidth * Random.Range(0.5f, 1.5f);
        float a = Random.Range(0f, 360f) * Mathf.Deg2Rad; float ph = Random.value;
        Color col = twigColor; col.r += Random.Range(-0.03f, 0.03f); col.a *= Random.Range(0.5f, 1f);
        Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        Vector2[] sp = { pos, pos + d*len*0.5f + Rot2D(d,Mathf.PI*0.5f)*Random.Range(-0.015f,0.015f),
                         pos + d*len*0.5f + Rot2D(d, Random.Range(-20f,20f)*Mathf.Deg2Rad)*len*0.5f*Random.Range(0.6f,1f) };
        float[] wM = { 0.5f, 1f, 0.1f };
        for (int s = 0; s < 3; s++)
        {
            float w = hw * wM[s];
            Vector2 tan = s < 2 ? (sp[Mathf.Min(s + 1, 2)] - sp[s]).normalized : (sp[2] - sp[1]).normalized;
            Vector2 perp = new Vector2(-tan.y, tan.x);
            v[vi + s * 2] = V3(sp[s] - perp * w); v[vi + s * 2 + 1] = V3(sp[s] + perp * w);
            Color sc = col; sc.a *= wM[s];
            c[vi + s * 2] = sc; c[vi + s * 2 + 1] = sc;
            u[vi + s * 2] = new Vector2(0f, s / 2f); u[vi + s * 2 + 1] = new Vector2(1f, s / 2f);
            u2[vi + s * 2] = new Vector2(0f, ph); u2[vi + s * 2 + 1] = new Vector2(0f, ph);
        }
        for (int s = 0; s < 2; s++) { int bl = vi + s * 2; tri[ti++] = bl; tri[ti++] = bl + 2; tri[ti++] = bl + 1; tri[ti++] = bl + 1; tri[ti++] = bl + 2; tri[ti++] = bl + 3; }
        vi += TWIG_V;
    }

    void MkPuddle(Vector3[] v, Color[] c, Vector2[] u, Vector2[] u2, int[] tri, ref int vi, ref int ti)
    {
        Vector2 pos = RandDisc(groundRadius, groundCoreExclusion);
        float sz = Random.Range(puddleMinSize, puddleMaxSize); float ph = Random.value;
        Color col = puddleColor; col.g += Random.Range(0f, 0.04f); col.a *= Random.Range(0.5f, 1f);
        v[vi] = V3(pos); c[vi] = col; u[vi] = new Vector2(0.5f, 0.5f); u2[vi] = new Vector2(0f, ph);
        for (int p = 0; p < 7; p++)
        {
            float ang = (p / 7f) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            float r = sz * Random.Range(0.5f, 1.4f);
            Vector2 pv = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
            v[vi + 1 + p] = V3(pv); Color ec = col; ec.a *= Random.Range(0.1f, 0.3f);
            c[vi + 1 + p] = ec; u[vi + 1 + p] = new Vector2(0.5f, 0.5f); u2[vi + 1 + p] = new Vector2(0f, ph);
        }
        for (int p = 0; p < 6; p++) { tri[ti++] = vi; tri[ti++] = vi + 1 + p; tri[ti++] = vi + 1 + ((p + 1) % 7); }
        vi += PUDDLE_V;
    }

    void MkRubble(Vector3[] v, Color[] c, Vector2[] u, Vector2[] u2, int[] tri, ref int vi, ref int ti)
    {
        Vector2 pos = RandDisc(groundRadius, groundCoreExclusion);
        float sz = Random.Range(rubbleMinSize, rubbleMaxSize); float ph = Random.value;
        Color col = Color.Lerp(rubbleDark, rubbleLight, Random.Range(0f, 0.6f)); col.a *= Random.Range(0.4f, 1f);
        v[vi] = V3(pos); c[vi] = col; u[vi] = new Vector2(0.5f, 0.5f); u2[vi] = new Vector2(0f, ph);
        for (int p = 0; p < 4; p++)
        {
            float ang = (p / 4f) * Mathf.PI * 2f + Random.Range(-0.5f, 0.5f);
            float r = sz * Random.Range(0.4f, 1.4f);
            v[vi + 1 + p] = V3(pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
            Color ec = col; ec.a *= 0.4f;
            c[vi + 1 + p] = ec; u[vi + 1 + p] = new Vector2(0.5f, 0.5f); u2[vi + 1 + p] = new Vector2(0f, ph);
        }
        for (int p = 0; p < 4; p++) { tri[ti++] = vi; tri[ti++] = vi + 1 + p; tri[ti++] = vi + 1 + ((p + 1) % 4); }
        vi += RUBBLE_V;
    }

    void MkScorch(Vector3[] v, Color[] c, Vector2[] u, Vector2[] u2, int[] tri, ref int vi, ref int ti)
    {
        Vector2 pos = RandDisc(groundRadius, groundCoreExclusion);
        float len = Random.Range(scorchMinLength, scorchMaxLength); float hw = scorchWidth * 0.5f * Random.Range(0.5f, 1.5f);
        float a = Random.Range(0f, 360f) * Mathf.Deg2Rad; float ph = Random.value;
        Color col = scorchColor; col.r += Random.Range(0f, 0.03f); col.a *= Random.Range(0.5f, 1f);
        Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        Vector2[] sp = { pos - d * len * 0.5f, pos + Rot2D(d, Mathf.PI * 0.5f) * Random.Range(-0.015f, 0.015f), pos + d * len * 0.5f };
        float[] wM = { 0.25f, 1f, 0.2f };
        for (int s = 0; s < 3; s++)
        {
            float w = hw * wM[s];
            Vector2 tan = s < 2 ? (sp[Mathf.Min(s + 1, 2)] - sp[s]).normalized : (sp[2] - sp[1]).normalized;
            Vector2 perp = new Vector2(-tan.y, tan.x);
            v[vi + s * 2] = V3(sp[s] - perp * w); v[vi + s * 2 + 1] = V3(sp[s] + perp * w);
            Color sc = col; sc.a *= wM[s];
            c[vi + s * 2] = sc; c[vi + s * 2 + 1] = sc;
            u[vi + s * 2] = new Vector2(0f, s / 2f); u[vi + s * 2 + 1] = new Vector2(1f, s / 2f);
            u2[vi + s * 2] = new Vector2(0f, ph); u2[vi + s * 2 + 1] = new Vector2(0f, ph);
        }
        for (int s = 0; s < 2; s++) { int bl = vi + s * 2; tri[ti++] = bl; tri[ti++] = bl + 2; tri[ti++] = bl + 1; tri[ti++] = bl + 1; tri[ti++] = bl + 2; tri[ti++] = bl + 3; }
        vi += SCORCH_V;
    }


    //  TOXIC POOLS

    void GenerateToxicPools()
    {
        int perMesh = MAX_VERTS / POOL_V;
        int mc = Mathf.CeilToInt((float)toxicPoolCount / perMesh);
        int rem = toxicPoolCount, off = 0;
        for (int m = 0; m < mc; m++)
        {
            int cnt = Mathf.Min(perMesh, rem);
            Mesh mesh = BuildFanMesh("ToxicPool", cnt, off, toxicPoolMinSize, toxicPoolMaxSize, 7, toxicPoolCenter, toxicPoolEdge, groundRadius * 0.7f, groundCoreExclusion * 2f, 55555);
            poolMeshes.Add(mesh);
            poolObjects.Add(MkObj($"ToxicPool_{m}", mesh, poolMat, sortingOrder));
            rem -= cnt; off += cnt;
        }
    }


    //  RUST STAINS

    void GenerateRustStains()
    {
        int perMesh = MAX_VERTS / RUST_V;
        int mc = Mathf.CeilToInt((float)rustStainCount / perMesh);
        int rem = rustStainCount, off = 0;
        for (int m = 0; m < mc; m++)
        {
            int cnt = Mathf.Min(perMesh, rem);
            Mesh mesh = BuildFanMesh("RustStain", cnt, off, rustStainMinSize, rustStainMaxSize, 7, rustColorDark, rustColorLight, groundRadius * 0.85f, groundCoreExclusion, 44444);
            rustMeshes.Add(mesh);
            rustObjects.Add(MkObj($"RustStain_{m}", mesh, rustMat, sortingOrder));
            rem -= cnt; off += cnt;
        }
    }


    //  BONE/DEBRIS SCATTER

    void GenerateBoneDebris()
    {
        int perMesh = MAX_VERTS / BONE_V;
        int mc = Mathf.CeilToInt((float)boneCount / perMesh);
        int rem = boneCount, off = 0;
        for (int m = 0; m < mc; m++)
        {
            int cnt = Mathf.Min(perMesh, rem);
            Mesh mesh = BuildBoneMesh(cnt, off);
            boneMeshes.Add(mesh);
            boneObjects.Add(MkObj($"BoneDebris_{m}", mesh, boneMaterial, sortingOrder + 1));
            rem -= cnt; off += cnt;
        }
    }

    Mesh BuildBoneMesh(int count, int seedOff)
    {
        int maxVt = count * BONE_V, maxTi = count * BONE_TI;
        Vector3[] v = new Vector3[maxVt]; Color[] c = new Color[maxVt];
        Vector2[] uv = new Vector2[maxVt], uv2 = new Vector2[maxVt];
        int[] tri = new int[maxTi];
        int vi = 0, ti = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOff + i + 33333);
            Vector2 pos = RandDisc(groundRadius * 0.75f, groundCoreExclusion * 1.5f);
            float len = Random.Range(boneMinLength, boneMaxLength);
            float hw = boneWidth * Random.Range(0.4f, 1.6f);
            float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float ph = Random.value;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 perp = new Vector2(-d.y, d.x);

            Vector2[] sp = { pos - d * len * 0.5f, pos, pos + d * len * 0.5f };
            float[] wM = { 0.4f, 1.0f, 0.3f };
            Color cBase = boneColor; cBase.r += Random.Range(-0.04f, 0.04f); cBase.a *= Random.Range(0.5f, 1f);

            for (int s = 0; s < 3; s++)
            {
                float w = hw * wM[s];
                Color sc = s == 2 ? Color.Lerp(cBase, boneTip, 0.6f) : cBase;
                sc.a *= wM[s];
                v[vi + s * 2] = V3(sp[s] - perp * w); v[vi + s * 2 + 1] = V3(sp[s] + perp * w);
                c[vi + s * 2] = sc; c[vi + s * 2 + 1] = sc;
                uv[vi + s * 2] = new Vector2(0f, s / 2f); uv[vi + s * 2 + 1] = new Vector2(1f, s / 2f);
                uv2[vi + s * 2] = new Vector2(0f, ph); uv2[vi + s * 2 + 1] = new Vector2(0f, ph);
            }
            for (int s = 0; s < 2; s++) { int bl = vi + s * 2; tri[ti++] = bl; tri[ti++] = bl + 2; tri[ti++] = bl + 1; tri[ti++] = bl + 1; tri[ti++] = bl + 2; tri[ti++] = bl + 3; }
            vi += BONE_V;
        }

        return FinishMesh("BoneDebrisMesh", v, c, uv, uv2, tri, vi, ti);
    }


    //  DUST STORM 

    void GenerateDustStorm()
    {
        dsPos = new Vector2[dustStormCount]; dsSpd = new float[dustStormCount];
        dsPh = new float[dustStormCount]; dsSz = new float[dustStormCount]; dsDp = new float[dustStormCount];
        int vc = dustStormCount * QUAD_V;
        dsVerts = new Vector3[vc];
        Color[] cols = new Color[vc]; Vector2[] uvs = new Vector2[vc], uv2s = new Vector2[vc];
        int[] tris = new int[dustStormCount * QUAD_TI];

        for (int i = 0; i < dustStormCount; i++)
        {
            float dp = Random.value; dsDp[i] = dp;
            dsPos[i] = new Vector2(Random.Range(-dustStormSpawnRadius, dustStormSpawnRadius), Random.Range(dustStormMinHeight, dustStormMaxHeight));
            dsSpd[i] = Mathf.Lerp(1.3f, 0.3f, dp) * Random.Range(0.7f, 1.3f);
            dsPh[i] = Random.Range(0f, Mathf.PI * 2f);
            dsSz[i] = Mathf.Lerp(dustStormMaxSize, dustStormMinSize, dp) * Random.Range(0.5f, 1.5f);
            Color c = Color.Lerp(dustStormNear, dustStormFar, dp); c.a *= Random.Range(0.6f, 1.0f);
            c.r += Random.Range(-0.03f, 0.03f); c.g += Random.Range(-0.02f, 0.02f); c = Clamp(c);
            int vi = i * 4;
            cols[vi] = c; cols[vi + 1] = c; cols[vi + 2] = c; cols[vi + 3] = c;
            FillQuadUVs(uvs, uv2s, vi, dp, dsPh[i]);
            FillQuadTris(tris, i);
        }
        RefreshQ(dsPos, dsSz, dsVerts, dustStormCount);
        dsMesh = MkParticleMesh("DustStormMesh", dsVerts, cols, uvs, uv2s, tris, dustStormSpawnRadius);
        dsObj = MkObj("DustStorm", dsMesh, dustStormMat, sortingOrder + 2);
    }


    //  ASH FLECKS

    void GenerateAshFlecks()
    {
        ashPos = new Vector2[ashCount]; ashSpd = new float[ashCount];
        ashPh = new float[ashCount]; ashSz = new float[ashCount]; ashDp = new float[ashCount];
        int vc = ashCount * QUAD_V;
        ashVerts = new Vector3[vc];
        Color[] cols = new Color[vc]; Vector2[] uvs = new Vector2[vc], uv2s = new Vector2[vc];
        int[] tris = new int[ashCount * QUAD_TI];

        for (int i = 0; i < ashCount; i++)
        {
            float dp = Random.value; ashDp[i] = dp;
            float hBias = Mathf.Pow(Random.value, 0.6f);
            ashPos[i] = new Vector2(Random.Range(-ashSpawnRadius, ashSpawnRadius), Mathf.Lerp(ashMinHeight, ashMaxHeight, hBias));
            ashSpd[i] = Mathf.Lerp(0.8f, 0.2f, dp) * Random.Range(0.6f, 1.4f);
            ashPh[i] = Random.Range(0f, Mathf.PI * 2f);
            ashSz[i] = Mathf.Lerp(ashMaxSize, ashMinSize, dp) * Random.Range(0.5f, 1.5f);
            Color c = Color.Lerp(ashNear, ashFar, dp); c.a *= Random.Range(0.5f, 1f); c = Clamp(c);
            int vi = i * 4;
            cols[vi] = c; cols[vi + 1] = c; cols[vi + 2] = c; cols[vi + 3] = c;
            FillQuadUVs(uvs, uv2s, vi, dp, ashPh[i]);
            FillQuadTris(tris, i);
        }
        RefreshQ(ashPos, ashSz, ashVerts, ashCount);
        ashMesh = MkParticleMesh("AshFleckMesh", ashVerts, cols, uvs, uv2s, tris, ashSpawnRadius);
        ashObj = MkObj("AshFlecks", ashMesh, ashMat, sortingOrder + 1);
    }


    //  EMBERS 

    void GenerateEmbers()
    {
        embPos = new Vector2[emberCount]; embSpd = new float[emberCount];
        embPh = new float[emberCount]; embSz = new float[emberCount];
        embDp = new float[emberCount]; embFlicker = new float[emberCount];
        int vc = emberCount * QUAD_V;
        embVerts = new Vector3[vc];
        Color[] cols = new Color[vc]; Vector2[] uvs = new Vector2[vc], uv2s = new Vector2[vc];
        int[] tris = new int[emberCount * QUAD_TI];

        for (int i = 0; i < emberCount; i++)
        {
            float dp = Random.value; embDp[i] = dp;
            embPos[i] = new Vector2(Random.Range(-emberSpawnRadius, emberSpawnRadius), Random.Range(emberMinHeight, emberMaxHeight));
            embSpd[i] = Mathf.Lerp(0.6f, 0.15f, dp) * Random.Range(0.6f, 1.4f);
            embPh[i] = Random.Range(0f, Mathf.PI * 2f);
            embSz[i] = Mathf.Lerp(emberMaxSize, emberMinSize, dp) * Random.Range(0.5f, 1.5f);
            embFlicker[i] = Random.Range(0f, Mathf.PI * 2f);

            Color c = Color.Lerp(emberColorHot, emberColorDim, dp * 0.7f + Random.Range(0f, 0.3f));
            c.a *= Random.Range(0.5f, 1f); c = Clamp(c);
            int vi = i * 4;
            cols[vi] = c; cols[vi + 1] = c; cols[vi + 2] = c; cols[vi + 3] = c;
            FillQuadUVs(uvs, uv2s, vi, dp, embPh[i]);
            FillQuadTris(tris, i);
        }
        RefreshQ(embPos, embSz, embVerts, emberCount);
        embMesh = MkParticleMesh("EmberMesh", embVerts, cols, uvs, uv2s, tris, emberSpawnRadius);
        embObj = MkObj("Embers", embMesh, emberMat, sortingOrder + 3);
    }


    //  SMOKE WISPS 

    void GenerateSmokeWisps()
    {
        smkPos = new Vector2[smokeWispCount];
        smkPh = new float[smokeWispCount]; smkSz = new float[smokeWispCount];
        int vc = smokeWispCount * QUAD_V;
        smkVerts = new Vector3[vc];
        Color[] cols = new Color[vc]; Vector2[] uvs = new Vector2[vc], uv2s = new Vector2[vc];
        int[] tris = new int[smokeWispCount * QUAD_TI];

        for (int i = 0; i < smokeWispCount; i++)
        {
            smkPos[i] = new Vector2(Random.Range(-groundRadius * 0.8f, groundRadius * 0.8f), Random.Range(-4f, 8f));
            smkPh[i] = Random.Range(0f, Mathf.PI * 2f);
            smkSz[i] = Random.Range(smokeMinSize, smokeMaxSize);
            Color c = smokeColor; c.a *= Random.Range(0.4f, 1.0f); c = Clamp(c);
            int vi = i * 4;
            cols[vi] = c; cols[vi + 1] = c; cols[vi + 2] = c; cols[vi + 3] = c;
            FillQuadUVs(uvs, uv2s, vi, 0.5f, smkPh[i]);
            FillQuadTris(tris, i);
        }
        RefreshQ(smkPos, smkSz, smkVerts, smokeWispCount);
        smkMesh = MkParticleMesh("SmokeWispMesh", smkVerts, cols, uvs, uv2s, tris, groundRadius);
        smkObj = MkObj("SmokeWisps", smkMesh, smokeMat, sortingOrder + 2);
    }


    //  HIGH HAZE 

    void GenerateHighHaze()
    {
        hzPos = new Vector2[hazeCount]; hzSpd = new float[hazeCount];
        hzPh = new float[hazeCount]; hzSz = new float[hazeCount]; hzDp = new float[hazeCount];
        int vc = hazeCount * QUAD_V;
        hzVerts = new Vector3[vc];
        Color[] cols = new Color[vc]; Vector2[] uvs = new Vector2[vc], uv2s = new Vector2[vc];
        int[] tris = new int[hazeCount * QUAD_TI];

        for (int i = 0; i < hazeCount; i++)
        {
            float dp = Random.value; hzDp[i] = dp;
            hzPos[i] = new Vector2(Random.Range(-hazeSpawnRadius, hazeSpawnRadius), Random.Range(hazeMinHeight, hazeMaxHeight));
            hzSpd[i] = Mathf.Lerp(0.4f, 0.1f, dp) * Random.Range(0.6f, 1.4f);
            hzPh[i] = Random.Range(0f, Mathf.PI * 2f);
            hzSz[i] = Mathf.Lerp(hazeMaxSize, hazeMinSize, dp) * Random.Range(0.5f, 1.5f);
            Color c = hazeColor; c.a *= Mathf.Lerp(1f, 0.3f, dp) * Random.Range(0.4f, 1f); c = Clamp(c);
            int vi = i * 4;
            cols[vi] = c; cols[vi + 1] = c; cols[vi + 2] = c; cols[vi + 3] = c;
            FillQuadUVs(uvs, uv2s, vi, dp, hzPh[i]);
            FillQuadTris(tris, i);
        }
        RefreshQ(hzPos, hzSz, hzVerts, hazeCount);
        hzMesh = MkParticleMesh("HighHazeMesh", hzVerts, cols, uvs, uv2s, tris, hazeSpawnRadius);
        hzObj = MkObj("HighHaze", hzMesh, hazeMat, sortingOrder + 4);
    }


    //  ANIMATION

    void Update()
    {
        float dt = Time.deltaTime, t = Time.time;
        AnimDustStorm(dt, t);
        AnimAsh(dt, t);
        AnimEmbers(dt, t);
        AnimSmoke(dt, t);
        AnimHaze(dt, t);
    }

    void AnimDustStorm(float dt, float t)
    {
        if (dsPos == null || dsMesh == null) return;
        float sw = dustStormSpawnRadius * 1.2f;
        for (int i = 0; i < dustStormCount; i++)
        {
            float dp = dsDp[i], ph = dsPh[i], sp = dsSpd[i];
            float wx = windStrength * Mathf.Lerp(1f, 0.25f, dp) * sp;
            float gx = dsPos[i].x * 0.1f, gy = dsPos[i].y * 0.1f, gt = t * gustSpeed;
            float gust = Mathf.Sin(gx * 0.6f + gy * 0.3f + gt) * 0.5f + Mathf.Sin(gx * 0.3f - gy * 0.8f + gt * 1.3f) * 0.3f;
            float gustP = 0.2f + 0.8f * (0.5f + 0.5f * Mathf.Sin(gt * 0.12f + dsPos[i].x * 0.03f));
            float gustT = gust * gustStrength * gustP;
            float turbX = Mathf.Sin(t * turbulence * 2.8f + ph * 2.1f) * turbulence * 0.035f;
            float turbY = Mathf.Cos(t * turbulence * 2.2f + ph * 1.7f) * turbulence * 0.02f;
            float driftY = Mathf.Sin(t * 0.6f + ph) * 0.15f;
            float fall = -0.08f * sp;
            dsPos[i].x += (wx * windDir.x + gustT * windDir.x + turbX) * dt;
            dsPos[i].y += (fall + driftY + turbY) * dt;
            if (dsPos[i].x > sw) { dsPos[i].x = -sw + Random.Range(0f, 2f); dsPh[i] = Random.Range(0f, Mathf.PI * 2f); }
            else if (dsPos[i].x < -sw) { dsPos[i].x = sw - Random.Range(0f, 2f); dsPh[i] = Random.Range(0f, Mathf.PI * 2f); }
            if (dsPos[i].y < dustStormMinHeight - 2f) { dsPos[i].y = dustStormMaxHeight + Random.Range(0f, 2f); dsPos[i].x = Random.Range(-sw, sw); }
            else if (dsPos[i].y > dustStormMaxHeight + 4f) dsPos[i].y = dustStormMinHeight + Random.Range(0f, 2f);
        }
        RefreshQ(dsPos, dsSz, dsVerts, dustStormCount);
        dsMesh.vertices = dsVerts;
    }

    void AnimAsh(float dt, float t)
    {
        if (ashPos == null || ashMesh == null) return;
        float sw = ashSpawnRadius * 1.2f;
        for (int i = 0; i < ashCount; i++)
        {
            float dp = ashDp[i], ph = ashPh[i], sp = ashSpd[i];
            float wx = windStrength * 0.5f * Mathf.Lerp(1f, 0.2f, dp) * sp;
            float tumbX = Mathf.Sin(t * turbulence * 1.8f + ph * 3f) * 0.12f;
            float tumbY = Mathf.Cos(t * turbulence * 1.3f + ph * 2f) * 0.08f;
            float fall = -0.2f * sp;
            float thermal = Mathf.Max(0f, Mathf.Sin(t * 0.3f + ph * 5f)) * 0.15f * (1f - dp);
            ashPos[i].x += (wx * windDir.x + tumbX) * dt;
            ashPos[i].y += (fall + tumbY + thermal) * dt;
            if (ashPos[i].x > sw) { ashPos[i].x = -sw + Random.Range(0f, 2f); ashPh[i] = Random.Range(0f, Mathf.PI * 2f); }
            else if (ashPos[i].x < -sw) { ashPos[i].x = sw - Random.Range(0f, 2f); ashPh[i] = Random.Range(0f, Mathf.PI * 2f); }
            if (ashPos[i].y < ashMinHeight - 2f) { ashPos[i].y = ashMaxHeight + Random.Range(0f, 2f); ashPos[i].x = Random.Range(-sw, sw); }
            else if (ashPos[i].y > ashMaxHeight + 3f) ashPos[i].y = ashMinHeight + Random.Range(0f, 2f);
        }
        RefreshQ(ashPos, ashSz, ashVerts, ashCount);
        ashMesh.vertices = ashVerts;
    }

    void AnimEmbers(float dt, float t)
    {
        if (embPos == null || embMesh == null) return;
        float sw = emberSpawnRadius * 1.2f;
        for (int i = 0; i < emberCount; i++)
        {
            float dp = embDp[i], ph = embPh[i], sp = embSpd[i];

            // Embers drift with wind but also rise from thermals
            float wx = windStrength * 0.3f * Mathf.Lerp(1f, 0.3f, dp) * sp;
            float rise = 0.25f * (1f - dp * 0.5f);
            float wobX = Mathf.Sin(t * 2.5f + ph * 4f) * 0.08f;
            float wobY = Mathf.Cos(t * 1.8f + ph * 3f) * 0.05f;

            embPos[i].x += (wx * windDir.x + wobX) * dt;
            embPos[i].y += (rise + wobY) * dt;

            if (embPos[i].x > sw) { embPos[i].x = -sw + Random.Range(0f, 2f); embPh[i] = Random.Range(0f, Mathf.PI * 2f); }
            else if (embPos[i].x < -sw) { embPos[i].x = sw - Random.Range(0f, 2f); embPh[i] = Random.Range(0f, Mathf.PI * 2f); }
            if (embPos[i].y > emberMaxHeight + 3f) { embPos[i].y = emberMinHeight; embPos[i].x = Random.Range(-sw, sw); }
            else if (embPos[i].y < emberMinHeight - 2f) { embPos[i].y = emberMaxHeight; }

            // Flicker size
            float flicker = 0.7f + 0.3f * Mathf.Sin(t * emberFlickerSpeed + embFlicker[i]);
            float s = embSz[i] * flicker;
            float px = embPos[i].x, py = embPos[i].y;
            int vi = i * 4;
            embVerts[vi] = new Vector3(px - s, py - s, 0f); embVerts[vi + 1] = new Vector3(px + s, py - s, 0f);
            embVerts[vi + 2] = new Vector3(px + s, py + s, 0f); embVerts[vi + 3] = new Vector3(px - s, py + s, 0f);
        }
        embMesh.vertices = embVerts;
    }

    void AnimSmoke(float dt, float t)
    {
        if (smkPos == null || smkMesh == null) return;
        for (int i = 0; i < smokeWispCount; i++)
        {
            float ph = smkPh[i];
            smkPos[i].y += smokeRiseSpeed * dt * (0.5f + 0.5f * Mathf.Sin(t * 0.2f + ph));
            smkPos[i].x += Mathf.Sin(t * 0.5f + ph * 3f) * 0.08f * dt;
            smkPos[i].x += windDir.x * windStrength * 0.05f * dt;

            if (smkPos[i].y > 20f)
            {
                smkPos[i].y = Random.Range(-4f, 2f);
                smkPos[i].x = Random.Range(-groundRadius * 0.8f, groundRadius * 0.8f);
                smkPh[i] = Random.Range(0f, Mathf.PI * 2f);
            }
        }
        RefreshQ(smkPos, smkSz, smkVerts, smokeWispCount);
        smkMesh.vertices = smkVerts;
    }

    void AnimHaze(float dt, float t)
    {
        if (hzPos == null || hzMesh == null) return;
        float sw = hazeSpawnRadius * 1.2f;
        for (int i = 0; i < hazeCount; i++)
        {
            float dp = hzDp[i], ph = hzPh[i], sp = hzSpd[i];
            float wx = windStrength * 0.12f * Mathf.Lerp(1f, 0.3f, dp) * sp;
            float swirlX = Mathf.Sin(t * 0.2f + ph * 2f) * 0.04f;
            float swirlY = Mathf.Cos(t * 0.15f + ph * 1.5f) * 0.025f;
            hzPos[i].x += (wx * windDir.x + swirlX) * dt;
            hzPos[i].y += swirlY * dt;
            if (hzPos[i].x > sw) { hzPos[i].x = -sw + Random.Range(0f, 3f); hzPos[i].y = Random.Range(hazeMinHeight, hazeMaxHeight); }
            else if (hzPos[i].x < -sw) { hzPos[i].x = sw - Random.Range(0f, 3f); hzPos[i].y = Random.Range(hazeMinHeight, hazeMaxHeight); }
        }
        RefreshQ(hzPos, hzSz, hzVerts, hazeCount);
        hzMesh.vertices = hzVerts;
    }


    //  HELPERS

    // Generic fan mesh builder 
    Mesh BuildFanMesh(string name, int count, int seedOff, float minSz, float maxSz, int perimVerts,
                      Color centerColor, Color edgeColor, float radius, float exclusion, int seedBase)
    {
        int vertsPerElem = perimVerts + 1;
        int trisPerElem = perimVerts * 3;

        int trisIdxPerElem = (perimVerts - 1) * 3;

        int maxVt = count * vertsPerElem;
        int maxTi = count * trisIdxPerElem;

        Vector3[] v = new Vector3[maxVt]; Color[] c = new Color[maxVt];
        Vector2[] uv = new Vector2[maxVt], uv2 = new Vector2[maxVt];
        int[] tri = new int[maxTi];
        int vi = 0, ti = 0;

        for (int i = 0; i < count; i++)
        {
            Random.InitState(seedOff + i + seedBase);
            Vector2 pos = RandDisc(radius, exclusion);
            float sz = Random.Range(minSz, maxSz);
            float ph = Random.value;
            float angleOff = Random.Range(0f, Mathf.PI * 2f);

            Color cc = centerColor;
            float n = Mathf.PerlinNoise(pos.x * 0.3f + seedBase, pos.y * 0.3f + seedBase);
            cc = Color.Lerp(cc, edgeColor, n * 0.3f);
            cc.a *= Random.Range(0.5f, 1.0f);

            v[vi] = V3(pos); c[vi] = cc;
            uv[vi] = new Vector2(0.5f, 0.5f); uv2[vi] = new Vector2(0f, ph);

            for (int p = 0; p < perimVerts; p++)
            {
                float ang = angleOff + (p / (float)perimVerts) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                float r = sz * Random.Range(0.5f, 1.4f);
                Vector2 pv = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                v[vi + 1 + p] = V3(pv);
                Color ec = edgeColor; ec.a *= Random.Range(0.1f, 0.35f);
                c[vi + 1 + p] = ec;
                uv[vi + 1 + p] = new Vector2(0.5f, 0.5f);
                uv2[vi + 1 + p] = new Vector2(0f, ph);
            }

            for (int p = 0; p < perimVerts - 1; p++)
            {
                tri[ti++] = vi;
                tri[ti++] = vi + 1 + p;
                tri[ti++] = vi + 1 + ((p + 1) % perimVerts);
            }

            vi += vertsPerElem;
        }

        return FinishMesh(name, v, c, uv, uv2, tri, vi, ti);
    }

    Mesh FinishMesh(string name, Vector3[] v, Color[] c, Vector2[] uv, Vector2[] uv2, int[] tri, int vi, int ti)
    {
        Mesh m = new Mesh { name = name };
        if (vi > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = Trim(v, vi); m.colors = Trim(c, vi);
        m.uv = Trim(uv, vi); m.uv2 = Trim(uv2, vi);
        m.triangles = Trim(tri, ti);
        m.RecalculateNormals(); m.RecalculateBounds();
        Bounds b = m.bounds; b.Expand(3f); m.bounds = b;
        return m;
    }

    Mesh MkParticleMesh(string name, Vector3[] verts, Color[] cols, Vector2[] uvs, Vector2[] uv2s, int[] tris, float boundsRadius)
    {
        Mesh m = new Mesh { name = name };
        m.vertices = verts; m.colors = cols; m.uv = uvs; m.uv2 = uv2s; m.triangles = tris;
        m.RecalculateNormals();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * boundsRadius * 4f);
        return m;
    }

    GameObject MkObj(string name, Mesh mesh, Material mat, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform); go.transform.localPosition = Vector3.zero; go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>().mesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false; mr.sortingLayerName = sortingLayerName; mr.sortingOrder = order;
        return go;
    }

    void FillQuadUVs(Vector2[] uvs, Vector2[] uv2s, int vi, float depth, float phase)
    {
        uvs[vi] = new Vector2(0, 0); uvs[vi + 1] = new Vector2(1, 0);
        uvs[vi + 2] = new Vector2(1, 1); uvs[vi + 3] = new Vector2(0, 1);
        float pn = phase / (Mathf.PI * 2f);
        uv2s[vi] = new Vector2(depth, pn); uv2s[vi + 1] = new Vector2(depth, pn);
        uv2s[vi + 2] = new Vector2(depth, pn); uv2s[vi + 3] = new Vector2(depth, pn);
    }

    void FillQuadTris(int[] tris, int quadIdx)
    {
        int vi = quadIdx * 4, ti = quadIdx * 6;
        tris[ti] = vi; tris[ti + 1] = vi + 2; tris[ti + 2] = vi + 1;
        tris[ti + 3] = vi; tris[ti + 4] = vi + 3; tris[ti + 5] = vi + 2;
    }

    void Cleanup()
    {
        CleanupList(groundObjects, groundMeshes);
        CleanupList(poolObjects, poolMeshes);
        CleanupList(rustObjects, rustMeshes);
        CleanupList(boneObjects, boneMeshes);
        DestroyPair(dsObj, dsMesh); DestroyPair(ashObj, ashMesh);
        DestroyPair(embObj, embMesh); DestroyPair(smkObj, smkMesh);
        DestroyPair(hzObj, hzMesh);
        DestroyMat(groundMat); DestroyMat(poolMat); DestroyMat(rustMat); DestroyMat(boneMaterial);
        DestroyMat(dustStormMat); DestroyMat(ashMat); DestroyMat(emberMat); DestroyMat(smokeMat); DestroyMat(hazeMat);
        dsObj = null; ashObj = null; embObj = null; smkObj = null; hzObj = null;
        dsMesh = null; ashMesh = null; embMesh = null; smkMesh = null; hzMesh = null;
        groundMat = null; poolMat = null; rustMat = null; boneMaterial = null;
        dustStormMat = null; ashMat = null; emberMat = null; smokeMat = null; hazeMat = null;
    }

    void CleanupList(List<GameObject> objs, List<Mesh> meshes)
    {
        foreach (var go in objs) if (go != null) DestroyImmediate(go); objs.Clear();
        foreach (var m in meshes) if (m != null) DestroyImmediate(m); meshes.Clear();
    }

    void DestroyPair(GameObject go, Mesh m) { if (go != null) DestroyImmediate(go); if (m != null) DestroyImmediate(m); }
    void DestroyMat(Material m) { if (m != null) DestroyImmediate(m); }

    void OnDestroy() => Cleanup();

    Vector3 V3(Vector2 v) => new Vector3(v.x, v.y, 0f);
    Vector2 Rot2D(Vector2 v, float r) { float c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }

    void RefreshQ(Vector2[] p, float[] sz, Vector3[] vt, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float s = sz[i]; float px = p[i].x, py = p[i].y; int vi = i * 4;
            vt[vi] = new Vector3(px - s, py - s, 0f); vt[vi + 1] = new Vector3(px + s, py - s, 0f);
            vt[vi + 2] = new Vector3(px + s, py + s, 0f); vt[vi + 3] = new Vector3(px - s, py + s, 0f);
        }
    }

    Vector2 RandDisc(float r, float exc)
    { Vector2 p; int s = 0; do { p = Random.insideUnitCircle * r; s++; } while (p.magnitude < exc && s < 30); return p; }

    Color Clamp(Color c)
    { c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b); c.a = Mathf.Clamp01(c.a); return c; }

    T[] Trim<T>(T[] src, int len)
    { if (src.Length == len) return src; T[] r = new T[len]; System.Array.Copy(src, r, len); return r; }
}

