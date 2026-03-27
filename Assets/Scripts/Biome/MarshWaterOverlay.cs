using UnityEngine;
using System.Collections.Generic;


public class MarshWaterOverlay : MonoBehaviour
{
    // ── Distribution 
    [Header("Puddle Distribution")]
    public int puddleCount = 900;
    public float spawnRadius = 60f;
    public float coreExclusionRadius = 1.5f;

    [Header("Puddle Clusters")]
    public int clusterCount = 140;
    public float clusterSpread = 4.0f;
    [Range(0f, 1f)] public float freeScatterRatio = 0.12f;

    // ── Puddle Shape 
    [Header("Puddle Size")]
    public float puddleMinRadius = 0.25f;
    public float puddleMaxRadius = 1.6f;
    public int largePuddleCount = 40;
    public float largePuddleMinRadius = 2.0f;
    public float largePuddleMaxRadius = 5.0f;

    [Header("Puddle Shape")]
    [Range(8, 32)] public int puddleSegments = 24;
    [Range(0f, 0.6f)] public float shapeDistortion = 0.35f;
    [Range(1f, 3f)] public float maxElongation = 2.0f;
    [Range(0f, 0.4f)] public float concavityChance = 0.25f;
    [Range(0f, 0.5f)] public float concavityDepth = 0.35f;

    // ── Water Colours 
    [Header("Water Colors")]
    public Color waterShallow = new Color(0.10f, 0.22f, 0.20f, 0.82f);
    public Color waterDeep = new Color(0.04f, 0.10f, 0.14f, 0.92f);
    public Color waterEdge = new Color(0.08f, 0.18f, 0.14f, 0.50f);
    public Color reflectionColor = new Color(0.45f, 0.58f, 0.68f, 0.40f);
    public Color specularHighlight = new Color(0.85f, 0.92f, 1.00f, 0.55f);

    [Header("Mud / Shore Colors")]
    public Color mudDark = new Color(0.08f, 0.06f, 0.03f, 0.80f);
    public Color mudLight = new Color(0.16f, 0.13f, 0.07f, 0.60f);
    public Color wetGround = new Color(0.04f, 0.12f, 0.06f, 0.55f);
    [Tooltip("Width of the muddy shore ring around each puddle, as a fraction of puddle radius")]
    [Range(0.05f, 0.5f)] public float shoreBandWidth = 0.25f;

    // ── Animation 
    [Header("Water Surface Animation")]
    [Tooltip("Gentle edge wobble amplitude (world units)")]
    public float edgeWobbleStrength = 0.025f;
    public float edgeWobbleSpeed = 1.5f;
    [Tooltip("Colour shimmer — how much the surface colour shifts over time")]
    [Range(0f, 0.15f)] public float colorShimmerStrength = 0.06f;
    public float colorShimmerSpeed = 0.8f;
    [Tooltip("Breathing alpha variation")]
    [Range(0f, 0.15f)] public float breatheStrength = 0.06f;
    public float breatheSpeed = 0.5f;

    // ── Ripples 
    [Header("Ripple Animation")]
    public int ripplesPerPuddle = 3;
    public float rippleSpeed = 1.2f;
    public float rippleWidth = 0.025f;
    public Color rippleColor = new Color(0.50f, 0.65f, 0.72f, 0.45f);

    // ── Caustics 
    [Header("Caustic Highlights")]
    public int causticCount = 2000;
    public float causticMinSize = 0.03f;
    public float causticMaxSize = 0.14f;
    public Color causticColor = new Color(0.60f, 0.78f, 0.65f, 0.30f);
    public float causticDriftSpeed = 0.25f;

    // ── Lily Pads 
    [Header("Surface Details")]
    public int lilyPadCount = 80;
    public float lilyPadMinSize = 0.10f;
    public float lilyPadMaxSize = 0.28f;
    public Color lilyPadColor = new Color(0.10f, 0.35f, 0.08f, 0.92f);
    public Color lilyPadDark = new Color(0.05f, 0.20f, 0.04f, 0.95f);

    [Header("Water Reeds")]
    public int reedCount = 300;
    public float reedMinHeight = 0.12f;
    public float reedMaxHeight = 0.35f;
    public float reedWidth = 0.010f;
    public Color reedBase = new Color(0.12f, 0.25f, 0.06f, 0.92f);
    public Color reedTip = new Color(0.25f, 0.38f, 0.14f, 0.70f);

    // ── Sorting 
    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 0;

    // ═══════════════  INTERNAL STATE  

    // Child GOs (one per visual layer)
    private List<GameObject> childGOs = new List<GameObject>();
    private List<Mesh> allMeshes = new List<Mesh>();
    private List<Material> allMats = new List<Material>();

    // Animated layers — we keep direct refs for Update()
    private Mesh waterSmallMesh, waterLargeMesh;
    private Mesh causticMesh, rippleMesh;

    // Base snapshots for animation
    private Vector3[] waterSmallBaseVerts, waterLargeBaseVerts;
    private Color[] waterSmallBaseColors, waterLargeBaseColors;
    private Vector3[] causticBaseVerts;
    private Color[] causticBaseColors;
    private Vector3[] rippleVerts;
    private Color[] rippleCols;

    // Per-puddle data for edge wobble
    private List<PuddleAnimData> smallPuddleAnims = new List<PuddleAnimData>();
    private List<PuddleAnimData> largePuddleAnims = new List<PuddleAnimData>();

    // Ripple / caustic tracking
    private List<RippleData> activeRipples = new List<RippleData>();
    private List<CausticData> activeCaustics = new List<CausticData>();

    // All puddles (for placing details on water)
    private List<PuddleRecord> allPuddles = new List<PuddleRecord>();

    // ── structs ──
    struct PuddleRecord { public Vector2 center; public float radius; }

    struct PuddleAnimData
    {
        public int vertStart;  // first edge vertex index (center is vertStart-1 if fan)
        public int edgeVerts;  // number of edge vertices
        public float phase;      // random phase offset
        public Vector2 center;
    }

    struct RippleData
    {
        public Vector2 center;
        public float puddleRadius;
        public float phase, speed;
        public int vertStart, segments;
    }

    struct CausticData
    {
        public Vector2 basePos;
        public float driftAngle, driftRadius, phase;
        public int vertStart;
    }

    // ═══════════════  HASH HELPERS  
    static uint WH(uint s)
    {
        s = (s ^ 61) ^ (s >> 16); s *= 9;
        s = s ^ (s >> 4); s *= 0x27d4eb2d;
        s = s ^ (s >> 15); return s;
    }
    static float H01(uint s) => (WH(s) & 0x00FFFFFF) / (float)0x00FFFFFF;
    static float HS(uint s) => H01(s) * 2f - 1f;
    static Vector2 HDir(uint s)
    {
        float a = H01(s) * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }
    static Vector2 HDisk(uint s1, uint s2)
    {
        float a = H01(s1) * Mathf.PI * 2f;
        float r = Mathf.Sqrt(H01(s2));
        return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
    }

    /// Simple value-noise (lattice-based). Returns ~[−1,1].
    static float ValueNoise(float x, float y, uint seed)
    {
        int ix = Mathf.FloorToInt(x); int iy = Mathf.FloorToInt(y);
        float fx = x - ix; float fy = y - iy;
        // Smoothstep
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float n00 = HS(WH((uint)ix * 374761393u + (uint)iy * 668265263u + seed));
        float n10 = HS(WH((uint)(ix + 1) * 374761393u + (uint)iy * 668265263u + seed));
        float n01 = HS(WH((uint)ix * 374761393u + (uint)(iy + 1) * 668265263u + seed));
        float n11 = HS(WH((uint)(ix + 1) * 374761393u + (uint)(iy + 1) * 668265263u + seed));
        float nx0 = Mathf.Lerp(n00, n10, fx);
        float nx1 = Mathf.Lerp(n01, n11, fx);
        return Mathf.Lerp(nx0, nx1, fy);
    }

    /// Fractional Brownian motion — 3 octaves of value noise.
    static float FBM3(float x, float y, uint seed)
    {
        float v = ValueNoise(x, y, seed) * 0.5f;
        v += ValueNoise(x * 2.13f, y * 2.13f, seed + 777u) * 0.3f;
        v += ValueNoise(x * 4.51f, y * 4.51f, seed + 1543u) * 0.2f;
        return v; // ~[−0.5, 0.5]
    }

    // ═══════════════  PUBLIC API  ════════════════════════════════

    [ContextMenu("Generate Marsh Water")]
    public void GenerateWater()
    {
        Cleanup();

        Vector2[] clusters = GenerateClusters();

        // Layers rendered bottom → top
        BuildMudLayer(clusters);                       // sort +0
        BuildWaterLayer(clusters, false);              // sort +1  (small puddles)
        BuildWaterLayer(clusters, true);               // sort +1  (large ponds)
        BuildReflectionLayer();                        // sort +2
        BuildCausticLayer();                           // sort +3
        BuildRippleLayer();                            // sort +4
        BuildLilyPadLayer();                           // sort +5
        BuildReedLayer();                              // sort +6

        Debug.Log($"[MarshWaterOverlay] {allPuddles.Count} puddles, " +
                  $"{activeCaustics.Count} caustics, {activeRipples.Count} ripples");
    }

    // ═══════════════  UPDATE — ANIMATION  ════════════════════════

    void Update()
    {
        float t = Time.time;

        // ── Water surface: edge wobble + colour shimmer + breathing alpha ──
        AnimateWaterSurface(waterSmallMesh, waterSmallBaseVerts, waterSmallBaseColors, smallPuddleAnims, t);
        AnimateWaterSurface(waterLargeMesh, waterLargeBaseVerts, waterLargeBaseColors, largePuddleAnims, t);

        // ── Caustics: drift + alpha pulse ──
        if (causticMesh != null && causticBaseVerts != null)
        {
            Vector3[] cv = causticMesh.vertices;
            Color[] cc = causticMesh.colors;
            for (int i = 0; i < activeCaustics.Count; i++)
            {
                var c = activeCaustics[i];
                float dx = Mathf.Sin(t * causticDriftSpeed + c.phase) * c.driftRadius;
                float dy = Mathf.Cos(t * causticDriftSpeed * 0.7f + c.phase * 1.3f) * c.driftRadius * 0.6f;
                float pulse = Mathf.Clamp01(0.35f + 0.65f * Mathf.Sin(t * 1.5f + c.phase * 2.1f));
                for (int v = 0; v < 4; v++)
                {
                    int vi = c.vertStart + v;
                    if (vi >= cv.Length) break;
                    Vector3 bv = causticBaseVerts[vi];
                    cv[vi] = new Vector3(bv.x + Mathf.Cos(c.driftAngle) * dx, bv.y + dy, bv.z);
                    Color bc = causticBaseColors[vi];
                    cc[vi] = new Color(bc.r, bc.g, bc.b, bc.a * pulse);
                }
            }
            causticMesh.vertices = cv; causticMesh.colors = cc;
        }

        // ── Ripples: expanding + fading rings ──
        if (rippleMesh != null && rippleVerts != null)
        {
            for (int i = 0; i < activeRipples.Count; i++)
            {
                var r = activeRipples[i];
                float cycle = Mathf.Repeat(t * r.speed + r.phase, 1f);
                float expand = cycle * r.puddleRadius * 0.7f;
                float alpha = (1f - cycle); alpha *= alpha;
                for (int s = 0; s <= r.segments; s++)
                {
                    float ang = (s / (float)r.segments) * Mathf.PI * 2f;
                    Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    int inner = r.vertStart + s * 2;
                    int outer = inner + 1;
                    if (outer >= rippleVerts.Length) break;
                    float ri = Mathf.Max(0f, expand - rippleWidth);
                    float ro = expand + rippleWidth;
                    rippleVerts[inner] = new Vector3(r.center.x + d.x * ri, r.center.y + d.y * ri, 0f);
                    rippleVerts[outer] = new Vector3(r.center.x + d.x * ro, r.center.y + d.y * ro, 0f);
                    Color rc = rippleColor; rc.a *= alpha;
                    rippleCols[inner] = rc;
                    rippleCols[outer] = new Color(rc.r, rc.g, rc.b, 0f);
                }
            }
            rippleMesh.vertices = rippleVerts; rippleMesh.colors = rippleCols;
        }
    }

    void AnimateWaterSurface(Mesh mesh, Vector3[] baseV, Color[] baseC,
                             List<PuddleAnimData> anims, float t)
    {
        if (mesh == null || baseV == null) return;

        Vector3[] v = mesh.vertices;
        Color[] c = mesh.colors;

        for (int p = 0; p < anims.Count; p++)
        {
            var a = anims[p];

            // Global breathe for this puddle (slow alpha pulse)
            float breathe = Mathf.Sin(t * breatheSpeed + a.phase) * breatheStrength;
            // Colour shimmer offset
            float shimmer = Mathf.Sin(t * colorShimmerSpeed + a.phase * 1.7f) * colorShimmerStrength;

            for (int e = 0; e < a.edgeVerts; e++)
            {
                int vi = a.vertStart + e;
                if (vi >= v.Length) break;

                // Edge wobble: each vertex gets a unique phase from its angle index
                float wobblePhase = a.phase + e * 0.73f;
                float wobbleX = Mathf.Sin(t * edgeWobbleSpeed + wobblePhase) * edgeWobbleStrength;
                float wobbleY = Mathf.Cos(t * edgeWobbleSpeed * 0.9f + wobblePhase * 1.4f) * edgeWobbleStrength * 0.7f;

                Vector3 bv = baseV[vi];
                v[vi] = new Vector3(bv.x + wobbleX, bv.y + wobbleY, bv.z);

                Color bc = baseC[vi];
                float newR = Mathf.Clamp01(bc.r + shimmer * 0.5f);
                float newG = Mathf.Clamp01(bc.g + shimmer);
                float newB = Mathf.Clamp01(bc.b + shimmer * 0.8f);
                float newA = Mathf.Clamp01(bc.a + breathe);
                c[vi] = new Color(newR, newG, newB, newA);
            }

            // Also shimmer the center vertex
            int ci = a.vertStart - 1; // center is placed right before edge verts
            if (ci >= 0 && ci < v.Length)
            {
                Color bc2 = baseC[ci];
                c[ci] = new Color(
                    Mathf.Clamp01(bc2.r + shimmer * 0.3f),
                    Mathf.Clamp01(bc2.g + shimmer * 0.6f),
                    Mathf.Clamp01(bc2.b + shimmer * 0.5f),
                    Mathf.Clamp01(bc2.a + breathe * 0.5f));
            }
        }

        mesh.vertices = v; mesh.colors = c;
    }

    // ═══════════════  CLUSTER / POSITION HELPERS  ════════════════

    Vector2[] GenerateClusters()
    {
        Vector2[] c = new Vector2[clusterCount];
        for (int i = 0; i < clusterCount; i++)
        {
            uint s = (uint)i * 48271u ^ 0xA341u;
            Vector2 pos; int safe = 0;
            do { pos = HDisk(s + (uint)safe * 3u, s + (uint)safe * 3u + 1u) * spawnRadius * 0.92f; safe++; }
            while (pos.magnitude < coreExclusionRadius && safe < 30);
            c[i] = pos;
        }
        return c;
    }

    Vector2 PickPosition(uint seed, Vector2[] clusters)
    {
        if (H01(seed) < freeScatterRatio || clusters.Length == 0)
        {
            Vector2 p = HDisk(seed + 1u, seed + 2u) * spawnRadius;
            if (p.magnitude < coreExclusionRadius) p = p.normalized * (coreExclusionRadius + 0.3f);
            return p;
        }
        int ci = (int)(WH(seed + 3u) % (uint)clusters.Length);
        Vector2 o = HDisk(seed + 4u, seed + 5u) * clusterSpread;
        Vector2 q = clusters[ci] + o;
        if (q.magnitude > spawnRadius) q = q.normalized * spawnRadius * 0.95f;
        if (q.magnitude < coreExclusionRadius) q = q.normalized * (coreExclusionRadius + 0.3f);
        return q;
    }

    Vector2 PickOnPuddle(uint seed, out float puddleR)
    {
        if (allPuddles.Count == 0) { puddleR = 1f; return HDisk(seed, seed + 1u) * 5f; }
        int idx = (int)(WH(seed) % (uint)allPuddles.Count);
        PuddleRecord pr = allPuddles[idx];
        puddleR = pr.radius;
        return pr.center + HDisk(seed + 10u, seed + 11u) * pr.radius * 0.6f;
    }

    // ═══════════════  ORGANIC PUDDLE SHAPE  ═════════════════════
    //
    // Generates the radii for a single organic puddle outline.
    // Uses FBM noise + random elongation + random concavity notches.

    float[] GeneratePuddleRadii(int segments, float baseRadius, uint seed)
    {
        float[] radii = new float[segments + 1]; // +1 for wraparound duplicate

        // Random elongation: stretch along one axis
        float elongation = 1f + H01(seed + 500u) * (maxElongation - 1f);
        float elongAngle = H01(seed + 501u) * Mathf.PI; // stretch direction

        // Concavity notch (natural bays/inlets)
        bool hasConcavity = H01(seed + 502u) < concavityChance;
        float concaveAngle = H01(seed + 503u) * Mathf.PI * 2f;
        float concaveWidth = 0.3f + H01(seed + 504u) * 0.5f; // radians
        float concaveStrength = concavityDepth * (0.5f + H01(seed + 505u) * 0.5f);

        // Second optional concavity
        bool hasConcavity2 = H01(seed + 506u) < concavityChance * 0.5f;
        float concaveAngle2 = concaveAngle + Mathf.PI * (0.4f + H01(seed + 507u) * 1.2f);
        float concaveWidth2 = 0.2f + H01(seed + 508u) * 0.4f;
        float concaveStrength2 = concavityDepth * (0.3f + H01(seed + 509u) * 0.4f);

        // Per-puddle noise seed
        uint noiseSeed = WH(seed + 600u);

        for (int s = 0; s <= segments; s++)
        {
            float angle = (s / (float)segments) * Mathf.PI * 2f;

            // 1. Base circle
            float r = baseRadius;

            // 2. Elongation: scale radius by cos of angle relative to stretch axis
            float relAngle = angle - elongAngle;
            float stretch = Mathf.Sqrt(
                Mathf.Pow(Mathf.Cos(relAngle) * elongation, 2f) +
                Mathf.Pow(Mathf.Sin(relAngle), 2f));
            r *= stretch / elongation; // normalise so area is roughly preserved

            // 3. FBM noise displacement (the main organic wobble)
            float nx = Mathf.Cos(angle) * 2.5f;
            float ny = Mathf.Sin(angle) * 2.5f;
            float noise = FBM3(nx, ny, noiseSeed);
            r *= 1f + noise * shapeDistortion;

            // 4. Concavity notches
            if (hasConcavity)
            {
                float diff = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, concaveAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float concave = Mathf.Exp(-(diff * diff) / (2f * concaveWidth * concaveWidth));
                r *= 1f - concave * concaveStrength;
            }
            if (hasConcavity2)
            {
                float diff2 = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, concaveAngle2 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float concave2 = Mathf.Exp(-(diff2 * diff2) / (2f * concaveWidth2 * concaveWidth2));
                r *= 1f - concave2 * concaveStrength2;
            }

            // 5. Tiny high-frequency jitter for rough shoreline
            r *= 1f + HS(seed + (uint)s * 7u + 700u) * 0.04f;

            radii[s] = Mathf.Max(r, baseRadius * 0.15f); // don't collapse to zero
        }

        return radii;
    }

    // ═══════════════  LAYER BUILDERS  ════════════════════════════

    // ── Mud / Shore Ring ──
    void BuildMudLayer(Vector2[] clusters)
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();

        // A mud ring around every puddle position (slightly larger)
        int total = puddleCount + largePuddleCount;
        for (int i = 0; i < total; i++)
        {
            uint seed = (uint)i * 95731u ^ 0xBEEFu;
            Vector2 pos = PickPosition(seed, clusters);
            bool large = i >= puddleCount;
            float baseR = large
                ? Mathf.Lerp(largePuddleMinRadius, largePuddleMaxRadius, H01(seed + 20u))
                : Mathf.Lerp(puddleMinRadius, puddleMaxRadius, H01(seed + 20u));
            float mudR = baseR * (1f + shoreBandWidth + 0.05f);

            Color col = Color.Lerp(mudDark, mudLight, H01(seed + 3u));
            Color outer = wetGround; outer.a *= 0.25f;

            // Use the same noise seed as the water layer so mud wraps the puddle
            float[] radii = GeneratePuddleRadii(12, mudR, seed + 30u);
            AddRadiiCircle(V, T, C, pos, radii, 12, col, outer);
        }

        Mesh m = BuildMesh("MarshMud", V, T, C);
        StoreAndCreate("Marsh_Mud", m, sortingOrder);
    }

    // ── Water Puddles (small + large) ──
    void BuildWaterLayer(Vector2[] clusters, bool large)
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();
        var anims = large ? largePuddleAnims : smallPuddleAnims;
        anims.Clear();

        int count = large ? largePuddleCount : puddleCount;
        float minR = large ? largePuddleMinRadius : puddleMinRadius;
        float maxR = large ? largePuddleMaxRadius : puddleMaxRadius;
        uint seedBase = large ? 0xDEADu : 0xCAFEu;
        int segs = large ? 28 : puddleSegments;

        for (int i = 0; i < count; i++)
        {
            uint seed = (uint)i * 127849u ^ seedBase;
            Vector2 pos = PickPosition(seed, clusters);
            float radius = Mathf.Lerp(minR, maxR, H01(seed + 20u));

            float depthFactor = H01(seed + 21u);
            Color centerCol = Color.Lerp(waterShallow, waterDeep, 0.3f + depthFactor * 0.7f);
            Color edgeCol = waterEdge;

            // Generate organic shape
            float[] radii = GeneratePuddleRadii(segs, radius, seed + 30u);

            // Intermediate ring: shallow water band between edge and deep center
            // We build 3 rings: center (deep) → inner ring (shallow) → outer ring (edge)

            int v0 = V.Count;

            // Center vertex
            V.Add(new Vector3(pos.x, pos.y, 0f));
            C.Add(centerCol);

            // Inner ring (40% of radius → shallow colour)
            Color shallowCol = Color.Lerp(centerCol, waterShallow, 0.6f);
            int innerSegs = segs;
            for (int s = 0; s <= innerSegs; s++)
            {
                float angle = (s / (float)innerSegs) * Mathf.PI * 2f;
                float r = radii[Mathf.Min(s, segs)] * 0.45f;
                V.Add(new Vector3(pos.x + Mathf.Cos(angle) * r, pos.y + Mathf.Sin(angle) * r, 0f));
                // Slight variation per vert
                Color sc = shallowCol;
                sc.g = Mathf.Clamp01(sc.g + HS(seed + (uint)s + 800u) * 0.02f);
                C.Add(sc);
            }

            // Outer ring (full radius → edge colour)
            int outerStart = V.Count;
            for (int s = 0; s <= segs; s++)
            {
                float angle = (s / (float)segs) * Mathf.PI * 2f;
                float r = radii[s];
                V.Add(new Vector3(pos.x + Mathf.Cos(angle) * r, pos.y + Mathf.Sin(angle) * r, 0f));
                Color ec = edgeCol;
                ec.r = Mathf.Clamp01(ec.r + HS(seed + (uint)s + 900u) * 0.02f);
                C.Add(ec);
            }

            // Triangles: center → inner ring (fan)
            int innerStart = v0 + 1;
            for (int s = 0; s < innerSegs; s++)
            {
                T.Add(v0);
                T.Add(innerStart + s);
                T.Add(innerStart + s + 1);
            }

            // Triangles: inner ring → outer ring (quad strip)
            for (int s = 0; s < segs; s++)
            {
                int iA = innerStart + s;
                int iB = innerStart + Mathf.Min(s + 1, innerSegs);
                int oA = outerStart + s;
                int oB = outerStart + s + 1;
                T.Add(iA); T.Add(oA); T.Add(iB);
                T.Add(iB); T.Add(oA); T.Add(oB);
            }

            // Track for animation (edge verts = outer ring)
            PuddleAnimData pad;
            pad.vertStart = outerStart;
            pad.edgeVerts = segs + 1;
            pad.phase = H01(seed + 22u) * Mathf.PI * 2f;
            pad.center = pos;
            anims.Add(pad);

            allPuddles.Add(new PuddleRecord { center = pos, radius = radius });
        }

        string label = large ? "Marsh_Ponds" : "Marsh_Puddles";
        Mesh mesh = BuildMesh(label, V, T, C);

        if (large) { waterLargeMesh = mesh; waterLargeBaseVerts = mesh.vertices; waterLargeBaseColors = mesh.colors; }
        else { waterSmallMesh = mesh; waterSmallBaseVerts = mesh.vertices; waterSmallBaseColors = mesh.colors; }

        StoreAndCreate(label, mesh, sortingOrder + 1);
    }

    // ── Reflections ──
    void BuildReflectionLayer()
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();

        for (int i = 0; i < allPuddles.Count; i++)
        {
            uint seed = (uint)i * 83641u ^ 0xF00Du;
            PuddleRecord pr = allPuddles[i];
            float reflR = pr.radius * (0.12f + H01(seed + 40u) * 0.22f);

            Vector2 pos = pr.center + new Vector2(
                pr.radius * 0.12f * HS(seed + 41u),
                pr.radius * 0.10f + pr.radius * 0.04f * H01(seed + 42u));

            Color refC = reflectionColor;
            refC.a *= (0.2f + H01(seed + 43u) * 0.5f);

            // Soft reflection blob
            AddSimpleCircle(V, T, C, pos, reflR, refC,
                new Color(refC.r, refC.g, refC.b, 0f), 8, seed + 50u);

            // Specular hotspot
            float specR = reflR * 0.35f;
            Color specC = specularHighlight;
            specC.a *= (0.12f + H01(seed + 44u) * 0.35f);
            AddSimpleCircle(V, T, C, pos + new Vector2(0.01f, 0.015f), specR, specC,
                new Color(specC.r, specC.g, specC.b, 0f), 6, seed + 60u);
        }

        Mesh m = BuildMesh("MarshRefl", V, T, C);
        StoreAndCreate("Marsh_Reflections", m, sortingOrder + 2);
    }

    // ── Caustics ──
    void BuildCausticLayer()
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();
        activeCaustics.Clear();

        for (int i = 0; i < causticCount; i++)
        {
            uint seed = (uint)i * 56891u ^ 0xDADAu;
            float pr; Vector2 pos = PickOnPuddle(seed, out pr);
            float size = Mathf.Lerp(causticMinSize, causticMaxSize, H01(seed + 60u));

            CausticData cd;
            cd.basePos = pos;
            cd.driftAngle = H01(seed + 61u) * Mathf.PI * 2f;
            cd.driftRadius = size * 0.6f;
            cd.phase = H01(seed + 62u) * Mathf.PI * 2f;
            cd.vertStart = V.Count;
            activeCaustics.Add(cd);

            Color cc = causticColor;
            cc.a *= (0.3f + H01(seed + 63u) * 0.7f);
            Color ce = cc; ce.a *= 0.12f;
            float hs = size * 0.5f;
            int v0 = V.Count;
            V.Add(new Vector3(pos.x - hs, pos.y - hs, 0f));
            V.Add(new Vector3(pos.x + hs, pos.y - hs, 0f));
            V.Add(new Vector3(pos.x + hs, pos.y + hs, 0f));
            V.Add(new Vector3(pos.x - hs, pos.y + hs, 0f));
            C.Add(ce); C.Add(ce); C.Add(cc); C.Add(ce);
            T.Add(v0); T.Add(v0 + 2); T.Add(v0 + 1);
            T.Add(v0); T.Add(v0 + 3); T.Add(v0 + 2);
        }

        causticMesh = BuildMesh("MarshCaustics", V, T, C);
        causticBaseVerts = causticMesh.vertices;
        causticBaseColors = causticMesh.colors;
        StoreAndCreate("Marsh_Caustics", causticMesh, sortingOrder + 3);
    }

    // ── Ripples ──
    void BuildRippleLayer()
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();
        activeRipples.Clear();

        int total = largePuddleCount * ripplesPerPuddle;
        for (int i = 0; i < total; i++)
        {
            uint seed = (uint)i * 37159u ^ 0xABCDu;
            float pr; Vector2 pos = PickOnPuddle(seed, out pr);
            Vector2 rc = pos + HDisk(seed + 71u, seed + 72u) * pr * 0.3f;
            int segs = 28;

            RippleData rd;
            rd.center = rc; rd.puddleRadius = pr;
            rd.phase = H01(seed + 73u);
            rd.speed = rippleSpeed * (0.5f + H01(seed + 74u) * 1.0f);
            rd.vertStart = V.Count; rd.segments = segs;
            activeRipples.Add(rd);

            for (int s = 0; s <= segs; s++)
            {
                V.Add(new Vector3(rc.x, rc.y, 0f));
                V.Add(new Vector3(rc.x, rc.y, 0f));
                C.Add(rippleColor);
                C.Add(new Color(rippleColor.r, rippleColor.g, rippleColor.b, 0f));
            }
            for (int s = 0; s < segs; s++)
            {
                int v0 = rd.vertStart + s * 2;
                T.Add(v0); T.Add(v0 + 2); T.Add(v0 + 1);
                T.Add(v0 + 1); T.Add(v0 + 2); T.Add(v0 + 3);
            }
        }

        rippleMesh = BuildMesh("MarshRipples", V, T, C);
        rippleVerts = rippleMesh.vertices;
        rippleCols = rippleMesh.colors;
        StoreAndCreate("Marsh_Ripples", rippleMesh, sortingOrder + 4);
    }

    // ── Lily Pads ──
    void BuildLilyPadLayer()
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();

        for (int i = 0; i < lilyPadCount; i++)
        {
            uint seed = (uint)i * 69857u ^ 0xFACEu;
            float pr; Vector2 pos = PickOnPuddle(seed, out pr);
            float size = Mathf.Lerp(lilyPadMinSize, lilyPadMaxSize, H01(seed + 80u));
            int segs = 12;
            float gapStart = H01(seed + 81u) * Mathf.PI * 2f;
            float gapAngle = 0.40f + H01(seed + 84u) * 0.25f;

            int v0 = V.Count;
            Color padC = Color.Lerp(lilyPadColor, lilyPadDark, H01(seed + 82u));
            V.Add(new Vector3(pos.x, pos.y, 0f)); C.Add(padC);

            for (int s = 0; s <= segs; s++)
            {
                float t = (float)s / segs;
                float ang = gapStart + gapAngle + t * (Mathf.PI * 2f - gapAngle);
                float r = size * (0.86f + HS(seed + 83u + (uint)s) * 0.14f);
                V.Add(new Vector3(pos.x + Mathf.Cos(ang) * r, pos.y + Mathf.Sin(ang) * r, 0f));
                Color ec = padC; ec.r += 0.04f; ec.g += 0.06f; C.Add(ec);
            }
            for (int s = 0; s < segs; s++) { T.Add(v0); T.Add(v0 + 1 + s); T.Add(v0 + 2 + s); }
        }

        Mesh m = BuildMesh("MarshLily", V, T, C);
        StoreAndCreate("Marsh_LilyPads", m, sortingOrder + 5);
    }

    // ── Reeds ──
    void BuildReedLayer()
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();

        for (int i = 0; i < reedCount; i++)
        {
            uint seed = (uint)i * 43891u ^ 0x1234u;
            float pr; Vector2 pos = PickOnPuddle(seed, out pr);
            pos += HDir(seed + 99u) * pr * (0.7f + H01(seed + 98u) * 0.35f);

            float h = Mathf.Lerp(reedMinHeight, reedMaxHeight, H01(seed + 90u));
            float lean = HS(seed + 91u) * 0.4f;
            float hw = reedWidth * 0.5f * (0.8f + H01(seed + 92u) * 0.4f);

            int v0 = V.Count;
            V.Add(new Vector3(pos.x - hw, pos.y, 0f));
            V.Add(new Vector3(pos.x + hw, pos.y, 0f));
            C.Add(reedBase); C.Add(reedBase);

            float midH = h * 0.5f; float ml = lean * 0.3f;
            V.Add(new Vector3(pos.x - hw * 0.7f + ml, pos.y + midH, 0f));
            V.Add(new Vector3(pos.x + hw * 0.7f + ml, pos.y + midH, 0f));
            Color mc = Color.Lerp(reedBase, reedTip, 0.4f); C.Add(mc); C.Add(mc);

            V.Add(new Vector3(pos.x + lean, pos.y + h, 0f)); C.Add(reedTip);

            T.Add(v0); T.Add(v0 + 2); T.Add(v0 + 1);
            T.Add(v0 + 1); T.Add(v0 + 2); T.Add(v0 + 3);
            T.Add(v0 + 2); T.Add(v0 + 4); T.Add(v0 + 3);
        }

        Mesh m = BuildMesh("MarshReeds", V, T, C);
        StoreAndCreate("Marsh_Reeds", m, sortingOrder + 6);
    }

    // ═══════════════  MESH HELPERS  ══════════════════════════════

    /// Fan circle from precomputed radii array (for mud that matches puddle outline).
    void AddRadiiCircle(List<Vector3> V, List<int> T, List<Color> C,
                        Vector2 cen, float[] radii, int segs,
                        Color centerC, Color edgeC)
    {
        int v0 = V.Count;
        V.Add(new Vector3(cen.x, cen.y, 0f)); C.Add(centerC);
        for (int s = 0; s <= segs; s++)
        {
            float ang = (s / (float)segs) * Mathf.PI * 2f;
            // Scale radii index to segment count
            int ri = Mathf.RoundToInt((s / (float)segs) * (radii.Length - 1));
            float r = radii[Mathf.Min(ri, radii.Length - 1)];
            V.Add(new Vector3(cen.x + Mathf.Cos(ang) * r, cen.y + Mathf.Sin(ang) * r, 0f));
            C.Add(edgeC);
        }
        for (int s = 0; s < segs; s++) { T.Add(v0); T.Add(v0 + 1 + s); T.Add(v0 + 2 + s); }
    }

    /// Simple distorted circle (for reflections and other accents).
    void AddSimpleCircle(List<Vector3> V, List<int> T, List<Color> C,
                         Vector2 cen, float radius, Color centerC, Color edgeC,
                         int segs, uint seed)
    {
        int v0 = V.Count;
        V.Add(new Vector3(cen.x, cen.y, 0f)); C.Add(centerC);
        for (int s = 0; s <= segs; s++)
        {
            float ang = (s / (float)segs) * Mathf.PI * 2f;
            float r = radius * (1f + HS(seed + (uint)s + 300u) * 0.12f);
            V.Add(new Vector3(cen.x + Mathf.Cos(ang) * r, cen.y + Mathf.Sin(ang) * r, 0f));
            C.Add(edgeC);
        }
        for (int s = 0; s < segs; s++) { T.Add(v0); T.Add(v0 + 1 + s); T.Add(v0 + 2 + s); }
    }

    Mesh BuildMesh(string name, List<Vector3> V, List<int> T, List<Color> C)
    {
        Mesh m = new Mesh { name = name };
        if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(V); m.SetTriangles(T, 0); m.SetColors(C);
        m.RecalculateBounds(); return m;
    }

    void StoreAndCreate(string name, Mesh mesh, int order)
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        allMeshes.Add(mesh); allMats.Add(mat);

        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = order;
        childGOs.Add(go);
    }

    // ═══════════════  CLEANUP  

    void Cleanup()
    {
        activeRipples.Clear(); activeCaustics.Clear();
        smallPuddleAnims.Clear(); largePuddleAnims.Clear();
        allPuddles.Clear();
        waterSmallBaseVerts = waterLargeBaseVerts = causticBaseVerts = rippleVerts = null;
        waterSmallBaseColors = waterLargeBaseColors = causticBaseColors = rippleCols = null;
        waterSmallMesh = waterLargeMesh = causticMesh = rippleMesh = null;

        foreach (var go in childGOs) { if (go != null) DestroyImmediate(go); }
        childGOs.Clear();
        foreach (var m in allMeshes) { if (m != null) DestroyImmediate(m); }
        allMeshes.Clear();
        foreach (var mt in allMats) { if (mt != null) DestroyImmediate(mt); }
        allMats.Clear();

        // Catch any stragglers from a previous run
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var ch = transform.GetChild(i).gameObject;
            if (ch.name.StartsWith("Marsh_")) DestroyImmediate(ch);
        }
    }

    void OnDisable() => Cleanup();
    void OnDestroy() => Cleanup();
}


