using UnityEngine;
using System.Collections.Generic;

public class MarshWaterOverlay : MonoBehaviour
{
    [Header("Puddle Distribution")]
    public int puddleCount = 900;
    public float spawnRadius = 60f;
    public float coreExclusionRadius = 2.0f;
    [Header("Puddle Clusters")]
    public int clusterCount = 140;
    public float clusterSpread = 4.0f;
    [Range(0f, 1f)] public float freeScatterRatio = 0.12f;

    [Header("Puddle Size — Small")]
    public float puddleMinRadius = 0.08f;
    public float puddleMaxRadius = 0.3f;
    [Header("Puddle Size — Medium")]
    public int mediumPuddleCount = 80;
    public float mediumMinRadius = 0.25f;
    public float mediumMaxRadius = 0.7f;
    [Header("Wetland Chains")]
    public int wetlandChainCount = 30;
    public int wetlandMinLobes = 3;
    public int wetlandMaxLobes = 5;
    public float wetlandLobeMinRadius = 0.2f;
    public float wetlandLobeMaxRadius = 0.6f;
    [Tooltip("How much lobes overlap (0=touching, 1=fully overlapping)")]
    public float wetlandLobeSpacing = 0.55f;

    [Header("Puddle Shape")]
    [Range(8, 32)] public int puddleSegments = 24;
    [Range(0f, 0.6f)] public float shapeDistortion = 0.35f;
    [Range(1f, 3f)] public float maxElongation = 2.0f;
    [Range(0f, 0.4f)] public float concavityChance = 0.25f;
    [Range(0f, 0.5f)] public float concavityDepth = 0.35f;

    [Header("Water Colors")]
    public Color waterShallow = new Color(0.10f, 0.22f, 0.20f, 0.82f);
    public Color waterMid = new Color(0.06f, 0.15f, 0.17f, 0.88f);
    public Color waterDeep = new Color(0.04f, 0.10f, 0.14f, 0.92f);
    public Color waterEdge = new Color(0.08f, 0.18f, 0.14f, 0.50f);
    public Color reflectionColor = new Color(0.45f, 0.58f, 0.68f, 0.40f);
    public Color specularHighlight = new Color(0.85f, 0.92f, 1.00f, 0.55f);

    [Header("Mud / Shore")]
    public Color mudDark = new Color(0.08f, 0.06f, 0.03f, 0.80f);
    public Color mudLight = new Color(0.16f, 0.13f, 0.07f, 0.60f);
    public Color wetGround = new Color(0.04f, 0.12f, 0.06f, 0.55f);
    [Range(0.05f, 0.5f)] public float shoreBandWidth = 0.25f;
    [Header("Edge Foam")]
    public Color foamColor = new Color(0.35f, 0.38f, 0.30f, 0.45f);
    [Range(0.02f, 0.15f)] public float foamWidth = 0.06f;

    [Header("Water Surface Animation")]
    public float edgeWobbleStrength = 0.025f;
    public float edgeWobbleSpeed = 1.5f;
    [Range(0f, 0.15f)] public float colorShimmerStrength = 0.06f;
    public float colorShimmerSpeed = 0.8f;
    [Range(0f, 0.15f)] public float breatheStrength = 0.06f;
    public float breatheSpeed = 0.5f;
    public float waveStrength = 0.012f;
    public float waveSpeed = 0.6f;
    public float waveScale = 3.0f;

    [Header("Ripples")]
    public int ripplesPerPuddle = 3;
    public float rippleSpeed = 1.2f;
    public float rippleWidth = 0.025f;
    public Color rippleColor = new Color(0.50f, 0.65f, 0.72f, 0.45f);

    [Header("Dimple Splashes")]
    public int dimpleSlots = 40;
    public float dimpleInterval = 2.5f;
    public float dimpleLifetime = 1.8f;
    public Color dimpleColor = new Color(0.55f, 0.70f, 0.75f, 0.50f);

    [Header("Caustics")]
    public int causticCount = 800;
    public float causticMinSize = 0.03f;
    public float causticMaxSize = 0.14f;
    public Color causticColor = new Color(0.60f, 0.78f, 0.65f, 0.30f);
    public float causticDriftSpeed = 0.25f;

    [Header("Sediment")]
    public int sedimentCount = 1500;
    public float sedimentMinSize = 0.005f;
    public float sedimentMaxSize = 0.02f;
    public Color sedimentColor = new Color(0.10f, 0.08f, 0.04f, 0.35f);

    [Header("Surface Film")]
    public int filmPatchCount = 30;
    public float filmMinSize = 0.15f;
    public float filmMaxSize = 0.6f;
    public Color filmColor1 = new Color(0.25f, 0.40f, 0.50f, 0.12f);
    public Color filmColor2 = new Color(0.45f, 0.30f, 0.50f, 0.10f);
    public float filmShimmerSpeed = 0.6f;

    [Header("Lily Pads")]
    public int lilyPadCount = 80;
    public float lilyPadMinSize = 0.10f;
    public float lilyPadMaxSize = 0.28f;
    public Color lilyPadColor = new Color(0.10f, 0.35f, 0.08f, 0.92f);
    public Color lilyPadDark = new Color(0.05f, 0.20f, 0.04f, 0.95f);
    public Color lilyVeinColor = new Color(0.06f, 0.28f, 0.05f, 0.70f);

    [Header("Water Reeds")]
    public int reedCount = 300;
    public float reedMinHeight = 0.12f;
    public float reedMaxHeight = 0.35f;
    public float reedWidth = 0.010f;
    public Color reedBase = new Color(0.12f, 0.25f, 0.06f, 0.92f);
    public Color reedTip = new Color(0.25f, 0.38f, 0.14f, 0.70f);

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 0;


    private List<GameObject> childGOs = new List<GameObject>();
    private List<Mesh> allMeshes = new List<Mesh>();
    private List<Material> allMats = new List<Material>();

    struct PlacedBody { public Vector2 center; public float radius; }
    private List<PlacedBody> placedBodies = new List<PlacedBody>();

    // Merged puddle data: each entry = one renderable shape (merged chain or single puddle)
    // center = centroid, radius = approximate, radii = precomputed outline
    struct MergedPuddle { public Vector2 center; public float radius; public float[] radii; public int segs; public uint seed; public bool isLarge; }
    private List<MergedPuddle> mergedPuddles = new List<MergedPuddle>();

    private Mesh rippleMesh, dimpleMesh;
    private Vector3[] ripV, dimV; private Color[] ripC, dimC;
    private List<RippleData> activeRipples = new List<RippleData>();
    private DimpleSlot[] dimpleSlotArr;

    struct RippleData { public Vector2 center; public float puddleRadius, phase, speed; public float[] warp; public int vertStart, segments; }
    struct DimpleSlot { public Vector2 center; public float puddleR, birthTime, lifetime, noisePhase, elongAngle; public int vertStart, ringVerts; public bool active; }

    //  HASH / NOISE  
    static uint WH(uint s) { s = (s ^ 61) ^ (s >> 16); s *= 9; s = s ^ (s >> 4); s *= 0x27d4eb2d; s = s ^ (s >> 15); return s; }
    static float H01(uint s) => (WH(s) & 0x00FFFFFF) / (float)0x00FFFFFF;
    static float HS(uint s) => H01(s) * 2f - 1f;
    static Vector2 HDir(uint s) { float a = H01(s) * Mathf.PI * 2f; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
    static Vector2 HDisk(uint a, uint b) { float ang = H01(a) * Mathf.PI * 2f; float r = Mathf.Sqrt(H01(b)); return new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r); }
    static float VNoise(float x, float y, uint seed)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        return Mathf.Lerp(Mathf.Lerp(HS(WH((uint)ix * 374761393u + (uint)iy * 668265263u + seed)), HS(WH((uint)(ix + 1) * 374761393u + (uint)iy * 668265263u + seed)), fx),
            Mathf.Lerp(HS(WH((uint)ix * 374761393u + (uint)(iy + 1) * 668265263u + seed)), HS(WH((uint)(ix + 1) * 374761393u + (uint)(iy + 1) * 668265263u + seed)), fx), fy);
    }
    static float FBM4(float x, float y, uint seed, bool ridged = false) { float v = 0f, amp = 0.45f, freq = 1f; for (int o = 0; o < 4; o++) { float n = VNoise(x * freq, y * freq, seed + (uint)o * 777u); if (ridged) n = 1f - Mathf.Abs(n); v += n * amp; amp *= 0.45f; freq *= 2.17f; } return v; }

    // PLACEMENT  

    Vector2[] GenerateClusters()
    {
        Vector2[] c = new Vector2[clusterCount];
        for (int i = 0; i < clusterCount; i++) { uint s = (uint)i * 48271u ^ 0xA341u; Vector2 pos; int safe = 0; do { pos = HDisk(s + (uint)safe * 3u, s + (uint)safe * 3u + 1u) * spawnRadius * 0.92f; safe++; } while (pos.magnitude < coreExclusionRadius && safe < 30); c[i] = pos; }
        return c;
    }

    Vector2 PickPos(uint seed, Vector2[] clusters)
    {
        if (H01(seed) < freeScatterRatio || clusters.Length == 0) { Vector2 p = HDisk(seed + 1u, seed + 2u) * spawnRadius; if (p.magnitude < coreExclusionRadius) p = p.normalized * (coreExclusionRadius + 0.5f); return p; }
        int ci = (int)(WH(seed + 3u) % (uint)clusters.Length); Vector2 q = clusters[ci] + HDisk(seed + 4u, seed + 5u) * clusterSpread;
        if (q.magnitude > spawnRadius) q = q.normalized * spawnRadius * 0.95f; if (q.magnitude < coreExclusionRadius) q = q.normalized * (coreExclusionRadius + 0.5f); return q;
    }

    bool BodyFits(Vector2 center, float radius)
    {
        if (center.magnitude - radius < coreExclusionRadius) return false;
        float padding = 0.15f;
        for (int i = 0; i < placedBodies.Count; i++)
            if ((center - placedBodies[i].center).magnitude < radius + placedBodies[i].radius + padding) return false;
        return true;
    }

    Vector2 PickOnMerged(uint seed, out float pr)
    {
        if (mergedPuddles.Count == 0) { pr = 1f; return HDir(seed) * (coreExclusionRadius + 2f); }
        int idx = (int)(WH(seed) % (uint)mergedPuddles.Count); var rec = mergedPuddles[idx]; pr = rec.radius;
        Vector2 p = rec.center + HDisk(seed + 10u, seed + 11u) * rec.radius * 0.5f;
        if (p.magnitude < coreExclusionRadius) p = p.normalized * (coreExclusionRadius + 0.1f); return p;
    }

    /// Merge multiple overlapping lobe circles into a single outline. Samples angles from centroid; at each angle, finds the max reach of any lobe.
    float[] MergeLobesIntoRadii(Vector2 centroid, List<Vector2> centers, List<float> radii, int segs, uint noiseSeed)
    {
        float[] result = new float[segs + 1];
        for (int s = 0; s <= segs; s++)
        {
            float angle = (s / (float)segs) * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            // For ray from centroid, find the furthest edge of any lobe
            float maxR = 0f;
            for (int L = 0; L < centers.Count; L++)
            {
                // Closest point on this ray to lobe center
                Vector2 lobeOff = centers[L] - centroid;
                float proj = Vector2.Dot(lobeOff, dir);
                Vector2 closest = dir * Mathf.Max(0f, proj);
                float distToCenter = (closest - lobeOff).magnitude;

                if (distToCenter < radii[L])
                {
                    // Ray intersects this lobe — find the far intersection
                    float halfChord = Mathf.Sqrt(radii[L] * radii[L] - distToCenter * distToCenter);
                    float reach = proj + halfChord;
                    if (reach > maxR) maxR = reach;
                }
            }

            // Add noise distortion for organic edge
            float noise = FBM4(Mathf.Cos(angle) * 2.5f, Mathf.Sin(angle) * 2.5f, noiseSeed, H01(noiseSeed + 1u) > 0.6f);
            maxR *= 1f + noise * shapeDistortion * 0.5f;
            // High-freq jitter
            maxR *= 1f + HS(noiseSeed + (uint)s * 7u + 700u) * 0.03f;
            result[s] = Mathf.Max(maxR, 0.05f);
        }
        return result;
    }

    void PreplacePuddles(Vector2[] clusters)
    {
        placedBodies.Clear();
        mergedPuddles.Clear();

        // Tier 1: Wetland chains
        int chainSegs = 32;
        for (int i = 0; i < wetlandChainCount; i++)
        {
            uint seed = (uint)i * 234571u ^ 0xBEADu;
            Vector2 startPos = PickPos(seed, clusters);
            int lobeCount = wetlandMinLobes + (int)(H01(seed + 10u) * (wetlandMaxLobes - wetlandMinLobes + 1));

            float walkAngle = H01(seed + 11u) * Mathf.PI * 2f;
            List<Vector2> lobeCenters = new List<Vector2>();
            List<float> lobeRadii = new List<float>();
            Vector2 cursor = startPos;

            for (int L = 0; L < lobeCount; L++)
            {
                uint lseed = seed + (uint)L * 7919u;
                float r = Mathf.Lerp(wetlandLobeMinRadius, wetlandLobeMaxRadius, H01(lseed + 20u));
                if (cursor.magnitude - r < coreExclusionRadius)
                    cursor = cursor.normalized * (coreExclusionRadius + r + 0.3f);
                lobeCenters.Add(cursor);
                lobeRadii.Add(r);
                float stepDist = r * (1f - wetlandLobeSpacing) + Mathf.Lerp(wetlandLobeMinRadius, wetlandLobeMaxRadius, H01(lseed + 21u)) * (1f - wetlandLobeSpacing) * 0.4f;
                walkAngle += HS(lseed + 22u) * 0.9f;
                cursor += new Vector2(Mathf.Cos(walkAngle), Mathf.Sin(walkAngle)) * stepDist;
            }

            // Compute centroid and bounding radius
            Vector2 centroid = Vector2.zero;
            for (int L = 0; L < lobeCenters.Count; L++) centroid += lobeCenters[L];
            centroid /= lobeCenters.Count;
            float bcR = 0f;
            for (int L = 0; L < lobeCenters.Count; L++)
            {
                float d = (lobeCenters[L] - centroid).magnitude + lobeRadii[L];
                if (d > bcR) bcR = d;
            }

            if (!BodyFits(centroid, bcR)) continue;

            // Merge lobes into single outline
            float[] merged = MergeLobesIntoRadii(centroid, lobeCenters, lobeRadii, chainSegs, WH(seed + 600u));
            placedBodies.Add(new PlacedBody { center = centroid, radius = bcR });
            mergedPuddles.Add(new MergedPuddle { center = centroid, radius = bcR, radii = merged, segs = chainSegs, seed = seed, isLarge = true });
        }

        // Tier 2: Medium isolated puddles
        int mSegs = 20;
        int mPlaced = 0;
        for (int i = 0; i < mediumPuddleCount * 3 && mPlaced < mediumPuddleCount; i++)
        {
            uint seed = (uint)i * 127849u ^ 0xFADEu;
            Vector2 pos = PickPos(seed, clusters);
            float radius = Mathf.Lerp(mediumMinRadius, mediumMaxRadius, H01(seed + 20u));
            if (!BodyFits(pos, radius)) continue;
            float[] radii = GenRadii(mSegs, radius, seed + 30u);
            placedBodies.Add(new PlacedBody { center = pos, radius = radius });
            mergedPuddles.Add(new MergedPuddle { center = pos, radius = radius, radii = radii, segs = mSegs, seed = seed, isLarge = true });
            mPlaced++;
        }

        // Tier 3: Small puddles
        int sSegs = Mathf.Min(puddleSegments, 14);
        int sPlaced = 0;
        for (int i = 0; i < puddleCount * 3 && sPlaced < puddleCount; i++)
        {
            uint seed = (uint)i * 95731u ^ 0xCAFEu;
            Vector2 pos = PickPos(seed, clusters);
            float radius = Mathf.Lerp(puddleMinRadius, puddleMaxRadius, H01(seed + 20u));
            if (!BodyFits(pos, radius)) continue;
            float[] radii = GenRadii(sSegs, radius, seed + 30u);
            placedBodies.Add(new PlacedBody { center = pos, radius = radius });
            mergedPuddles.Add(new MergedPuddle { center = pos, radius = radius, radii = radii, segs = sSegs, seed = seed, isLarge = false });
            sPlaced++;
        }
    }

    float[] GenRadii(int segs, float baseR, uint seed)
    {
        float[] radii = new float[segs + 1];
        float elong = 1f + H01(seed + 500u) * (maxElongation - 1f); float elongA = H01(seed + 501u) * Mathf.PI;
        bool hasC1 = H01(seed + 502u) < concavityChance; float cA1 = H01(seed + 503u) * Mathf.PI * 2f; float cW1 = 0.3f + H01(seed + 504u) * 0.5f; float cS1 = concavityDepth * (0.5f + H01(seed + 505u) * 0.5f);
        bool hasC2 = H01(seed + 506u) < concavityChance * 0.5f; float cA2 = cA1 + Mathf.PI * (0.4f + H01(seed + 507u) * 1.2f); float cW2 = 0.2f + H01(seed + 508u) * 0.4f; float cS2 = concavityDepth * (0.3f + H01(seed + 509u) * 0.4f);
        uint ns = WH(seed + 600u); bool useR = H01(seed + 601u) > 0.6f;
        for (int s = 0; s <= segs; s++)
        {
            float angle = (s / (float)segs) * Mathf.PI * 2f; float r = baseR;
            float rel = angle - elongA; r *= Mathf.Sqrt(Mathf.Pow(Mathf.Cos(rel) * elong, 2f) + Mathf.Pow(Mathf.Sin(rel), 2f)) / elong;
            r *= 1f + FBM4(Mathf.Cos(angle) * 2.5f, Mathf.Sin(angle) * 2.5f, ns, useR) * shapeDistortion;
            if (hasC1) { float d = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, cA1 * Mathf.Rad2Deg) * Mathf.Deg2Rad; r *= 1f - Mathf.Exp(-(d * d) / (2f * cW1 * cW1)) * cS1; }
            if (hasC2) { float d = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, cA2 * Mathf.Rad2Deg) * Mathf.Deg2Rad; r *= 1f - Mathf.Exp(-(d * d) / (2f * cW2 * cW2)) * cS2; }
            r *= 1f + HS(seed + (uint)s * 7u + 700u) * 0.04f; radii[s] = Mathf.Max(r, baseR * 0.15f);
        }
        return radii;
    }
    float RAtAng(float[] radii, int segs, float angle) { float t2 = Mathf.Repeat((angle / (Mathf.PI * 2f)) * segs, segs); int i0 = Mathf.FloorToInt(t2); int i1 = (i0 + 1) % (segs + 1); return Mathf.Lerp(radii[Mathf.Min(i0, segs)], radii[Mathf.Min(i1, segs)], t2 - i0); }

    // PUBLIC API  
    [ContextMenu("Generate Marsh Water")]
    public void GenerateWater()
    {
        Cleanup();
        Vector2[] clusters = GenerateClusters();
        PreplacePuddles(clusters);
        BuildMudLayer(); BuildFoamLayer(); BuildWaterLayer(true); BuildWaterLayer(false);
        BuildSedimentLayer(); BuildFilmLayer(); BuildReflectionLayer(); BuildCausticLayer();
        BuildRippleLayer(); BuildDimpleLayer(); BuildLilyPadLayer(); BuildReedLayer();
        Debug.Log($"[MarshWaterOverlay] {mergedPuddles.Count} merged shapes in {placedBodies.Count} bodies, {activeRipples.Count} ripples");
    }


    void Update()
    {
        float t = Time.time;
        if (rippleMesh != null && ripV != null) { AnimateRipples(t); rippleMesh.vertices = ripV; rippleMesh.colors = ripC; }
        if (dimpleMesh != null && dimV != null) { if (AnimateDimples(t)) { dimpleMesh.vertices = dimV; dimpleMesh.colors = dimC; } }
    }

    void AnimateRipples(float t)
    {
        for (int i = 0; i < activeRipples.Count; i++)
        {
            var r = activeRipples[i]; float cycle = Mathf.Repeat(t * r.speed + r.phase, 1f);
            float expand = cycle * r.puddleRadius * 0.7f; float alpha = (1f - cycle); alpha *= alpha;
            float alpha2 = Mathf.Clamp01(1f - cycle * 1.5f); alpha2 *= alpha2 * 0.4f; float ua = alpha + alpha2;
            for (int s = 0; s <= r.segments; s++)
            {
                float ang = (s / (float)r.segments) * Mathf.PI * 2f;
                float we = expand * (1f + r.warp[s]); float cs2 = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                int inner = r.vertStart + s * 2, outer = inner + 1; if (outer >= ripV.Length) break;
                ripV[inner] = new Vector3(r.center.x + cs2 * Mathf.Max(0f, we - rippleWidth), r.center.y + sn * Mathf.Max(0f, we - rippleWidth), 0f);
                ripV[outer] = new Vector3(r.center.x + cs2 * (we + rippleWidth), r.center.y + sn * (we + rippleWidth), 0f);
                Color rc = rippleColor; rc.a *= ua; ripC[inner] = rc; ripC[outer] = new Color(rc.r, rc.g, rc.b, 0f);
            }
        }
    }

    bool AnimateDimples(float t)
    {
        if (dimpleSlotArr == null) return false;
        int segs = 10; int vpr = (segs + 1) * 2; int splashV = segs + 2; bool any = false;
        for (int i = 0; i < dimpleSlotArr.Length; i++)
        {
            var ds = dimpleSlotArr[i];
            if (!ds.active && t > ds.birthTime + ds.lifetime + dimpleInterval * (0.5f + H01(WH((uint)i + 99u))))
            { uint seed = WH((uint)i * 31337u + (uint)(t * 100f)); float pr; ds.center = PickOnMerged(seed, out pr); ds.puddleR = pr; ds.birthTime = t; ds.lifetime = dimpleLifetime * (0.6f + H01(seed + 5u) * 0.8f); ds.noisePhase = H01(seed + 6u) * Mathf.PI * 2f; ds.elongAngle = H01(seed + 7u) * Mathf.PI * 2f; ds.active = true; dimpleSlotArr[i] = ds; }
            if (!ds.active) continue; any = true;
            float life01 = Mathf.Clamp01((t - ds.birthTime) / ds.lifetime);
            if (life01 >= 1f) { ds.active = false; dimpleSlotArr[i] = ds; int tot = vpr * 3 + splashV; for (int j = 0; j < tot; j++) { int vi = ds.vertStart + j; if (vi < dimV.Length) { dimV[vi] = Vector3.zero; dimC[vi] = Color.clear; } } continue; }
            float maxR = ds.puddleR * 0.12f;
            float[] rExp = { life01 * maxR, life01 * maxR * 0.65f, life01 * maxR * 0.35f };
            float[] rAlp = { (1f - life01) * (1f - life01), Mathf.Clamp01(1f - life01 * 1.3f) * 0.6f, Mathf.Clamp01(1f - life01 * 1.8f) * 0.3f };
            float[] rW = { 0.010f, 0.007f, 0.005f };
            for (int ring = 0; ring < 3; ring++)
            {
                float exp2 = rExp[ring], alph = rAlp[ring], rw = rW[ring]; int rs = ds.vertStart + ring * vpr;
                for (int s = 0; s <= segs; s++)
                {
                    float ang = (s / (float)segs) * Mathf.PI * 2f; float noise = HS(WH((uint)(i * 200 + ring * 50 + s))) * 0.25f; float ef = 1f + 0.15f * Mathf.Cos(ang - ds.elongAngle); float wr = exp2 * ef * (1f + noise); float cs2 = Mathf.Cos(ang), sn = Mathf.Sin(ang); int inner = rs + s * 2, outer = inner + 1; if (outer >= dimV.Length) break;
                    dimV[inner] = new Vector3(ds.center.x + cs2 * Mathf.Max(0f, wr - rw), ds.center.y + sn * Mathf.Max(0f, wr - rw), 0f); dimV[outer] = new Vector3(ds.center.x + cs2 * (wr + rw), ds.center.y + sn * (wr + rw), 0f);
                    Color dc = dimpleColor; dc.a *= alph; dimC[inner] = dc; dimC[outer] = new Color(dc.r, dc.g, dc.b, 0f);
                }
            }
            int ss2 = ds.vertStart + 3 * vpr; float splAlp = Mathf.Clamp01(1f - life01 * 4f); float splR = 0.012f * (1f + life01 * 2f);
            if (ss2 < dimV.Length)
            {
                dimV[ss2] = new Vector3(ds.center.x, ds.center.y, 0f); Color sc = new Color(Mathf.Clamp01(dimpleColor.r + 0.2f), Mathf.Clamp01(dimpleColor.g + 0.15f), Mathf.Clamp01(dimpleColor.b + 0.1f), splAlp * 0.8f); dimC[ss2] = sc;
                for (int s = 0; s <= segs; s++) { int vi = ss2 + 1 + s; if (vi >= dimV.Length) break; float ang = (s / (float)segs) * Mathf.PI * 2f; dimV[vi] = new Vector3(ds.center.x + Mathf.Cos(ang) * splR, ds.center.y + Mathf.Sin(ang) * splR, 0f); dimC[vi] = new Color(sc.r, sc.g, sc.b, 0f); }
            }
        }
        return any;
    }

    // STATIC LAYER BUILDERS  

    void BuildMudLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < mergedPuddles.Count; i++) { var pp = mergedPuddles[i]; float[] mudR = new float[pp.radii.Length]; for (int s = 0; s < mudR.Length; s++) mudR[s] = pp.radii[s] * (1f + shoreBandWidth + 0.05f); Color col = Color.Lerp(mudDark, mudLight, H01(pp.seed + 3u)); Color outer = wetGround; outer.a *= 0.25f; AddRadiiCircle(V, T, C, pp.center, mudR, pp.segs, col, outer); } SC("Marsh_Mud", BM(V, T, C), sortingOrder); }

    void BuildFoamLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < mergedPuddles.Count; i++) { var pp = mergedPuddles[i]; int segs = pp.segs; Color fc = foamColor; fc.a *= (0.3f + H01(pp.seed + 3u) * 0.7f); Color fcO = fc; fcO.a *= 0.1f; int v0 = V.Count; for (int s = 0; s <= segs; s++) { float angle = (s / (float)segs) * Mathf.PI * 2f; float r = pp.radii[s]; float rI = r * (1f - foamWidth); float rO = r * (1f + foamWidth * 0.3f); float pa = Mathf.Sin(angle * 3.7f + H01(pp.seed + (uint)s + 400u) * 5f) * 0.5f + 0.5f; Color inner = fc; inner.a *= pa; Color outerC = fcO; outerC.a *= pa * 0.3f; V.Add(new Vector3(pp.center.x + Mathf.Cos(angle) * rI, pp.center.y + Mathf.Sin(angle) * rI, 0f)); V.Add(new Vector3(pp.center.x + Mathf.Cos(angle) * rO, pp.center.y + Mathf.Sin(angle) * rO, 0f)); C.Add(inner); C.Add(outerC); } for (int s = 0; s < segs; s++) { int b = v0 + s * 2; T.Add(b); T.Add(b + 2); T.Add(b + 1); T.Add(b + 1); T.Add(b + 2); T.Add(b + 3); } } SC("Marsh_Foam", BM(V, T, C), sortingOrder + 1); }

    void BuildWaterLayer(bool large)
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();
        for (int i = 0; i < mergedPuddles.Count; i++)
        {
            var pp = mergedPuddles[i]; if (pp.isLarge != large) continue;
            int segs = pp.segs; float depth01 = H01(pp.seed + 21u);
            Color deepC = Color.Lerp(waterMid, waterDeep, 0.3f + depth01 * 0.7f); Color midC = Color.Lerp(waterShallow, waterMid, 0.5f + depth01 * 0.3f); Color shalC = waterShallow; Color edgeC = waterEdge;
            int pv = V.Count;
            V.Add(new Vector3(pp.center.x, pp.center.y, 0f)); C.Add(deepC);
            int ms = V.Count; for (int s = 0; s <= segs; s++) { float ang = (s / (float)segs) * Mathf.PI * 2f; float r = pp.radii[s] * 0.25f; V.Add(new Vector3(pp.center.x + Mathf.Cos(ang) * r, pp.center.y + Mathf.Sin(ang) * r, 0f)); Color mc = midC; mc.g = Mathf.Clamp01(mc.g + HS(pp.seed + (uint)s + 800u) * 0.015f); C.Add(mc); }
            int ss = V.Count; for (int s = 0; s <= segs; s++) { float ang = (s / (float)segs) * Mathf.PI * 2f; float r = pp.radii[s] * 0.55f; V.Add(new Vector3(pp.center.x + Mathf.Cos(ang) * r, pp.center.y + Mathf.Sin(ang) * r, 0f)); Color sc2 = shalC; sc2.g = Mathf.Clamp01(sc2.g + HS(pp.seed + (uint)s + 850u) * 0.02f); C.Add(sc2); }
            int es = V.Count; for (int s = 0; s <= segs; s++) { float ang = (s / (float)segs) * Mathf.PI * 2f; float r = pp.radii[s]; V.Add(new Vector3(pp.center.x + Mathf.Cos(ang) * r, pp.center.y + Mathf.Sin(ang) * r, 0f)); Color ec = edgeC; ec.r = Mathf.Clamp01(ec.r + HS(pp.seed + (uint)s + 900u) * 0.02f); C.Add(ec); }
            for (int s = 0; s < segs; s++) { T.Add(pv); T.Add(ms + s); T.Add(ms + s + 1); }
            for (int s = 0; s < segs; s++) { T.Add(ms + s); T.Add(ss + s); T.Add(ms + s + 1); T.Add(ms + s + 1); T.Add(ss + s); T.Add(ss + s + 1); }
            for (int s = 0; s < segs; s++) { T.Add(ss + s); T.Add(es + s); T.Add(ss + s + 1); T.Add(ss + s + 1); T.Add(es + s); T.Add(es + s + 1); }
        }
        SC(large ? "Marsh_Ponds" : "Marsh_Puddles", BM(V, T, C), sortingOrder + 2);
    }

    void BuildSedimentLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < sedimentCount; i++) { uint seed = (uint)i * 81239u ^ 0x5EDDu; float pr; Vector2 pos = PickOnMerged(seed, out pr); float size = Mathf.Lerp(sedimentMinSize, sedimentMaxSize, H01(seed + 60u)); Color sc = sedimentColor; sc.a *= (0.3f + H01(seed + 61u) * 0.7f); float hs = size * 0.5f; float rot = H01(seed + 62u) * Mathf.PI * 2f; float cs2 = Mathf.Cos(rot) * hs, sn = Mathf.Sin(rot) * hs; int v0 = V.Count; V.Add(new Vector3(pos.x - cs2 + sn, pos.y - sn - cs2, 0f)); V.Add(new Vector3(pos.x + cs2 + sn, pos.y + sn - cs2, 0f)); V.Add(new Vector3(pos.x + cs2 - sn, pos.y + sn + cs2, 0f)); V.Add(new Vector3(pos.x - cs2 - sn, pos.y - sn + cs2, 0f)); C.Add(sc); C.Add(sc); C.Add(sc); C.Add(sc); T.Add(v0); T.Add(v0 + 2); T.Add(v0 + 1); T.Add(v0); T.Add(v0 + 3); T.Add(v0 + 2); } SC("Marsh_Sediment", BM(V, T, C), sortingOrder + 3); }
    void BuildFilmLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < filmPatchCount; i++) { uint seed = (uint)i * 54321u ^ 0xF11Au; float pr; Vector2 pos = PickOnMerged(seed, out pr); float size = Mathf.Lerp(filmMinSize, filmMaxSize, H01(seed + 70u)); Color cent = Color.Lerp(filmColor1, filmColor2, H01(seed + 72u)); Color edge = cent; edge.a *= 0.1f; AddSimpleCircle(V, T, C, pos, size, cent, edge, 8, seed + 80u); } SC("Marsh_Film", BM(V, T, C), sortingOrder + 4); }
    void BuildReflectionLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < mergedPuddles.Count; i++) { var pp = mergedPuddles[i]; uint seed = (uint)i * 83641u ^ 0xF00Du; float reflR = pp.radius * (0.08f + H01(seed + 40u) * 0.15f); Vector2 pos = pp.center + new Vector2(pp.radius * 0.10f * HS(seed + 41u), pp.radius * 0.06f + pp.radius * 0.03f * H01(seed + 42u)); Color rc = reflectionColor; rc.a *= (0.15f + H01(seed + 43u) * 0.45f); AddSimpleCircle(V, T, C, pos, reflR, rc, new Color(rc.r, rc.g, rc.b, 0f), 6, seed + 50u); float specR = reflR * 0.3f; Color sc2 = specularHighlight; sc2.a *= (0.1f + H01(seed + 44u) * 0.3f); AddSimpleCircle(V, T, C, pos + new Vector2(0.01f, 0.012f), specR, sc2, new Color(sc2.r, sc2.g, sc2.b, 0f), 5, seed + 60u); } SC("Marsh_Reflections", BM(V, T, C), sortingOrder + 5); }
    void BuildCausticLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < causticCount; i++) { uint seed = (uint)i * 56891u ^ 0xDADAu; float pr; Vector2 pos = PickOnMerged(seed, out pr); float size = Mathf.Lerp(causticMinSize, causticMaxSize, H01(seed + 60u)); int cv = 5 + (int)(H01(seed + 64u) * 2f); Color cc = causticColor; cc.a *= (0.3f + H01(seed + 63u) * 0.7f); Color ce = cc; ce.a *= 0.1f; int v0 = V.Count; V.Add(new Vector3(pos.x, pos.y, 0f)); C.Add(cc); for (int s = 0; s < cv; s++) { float ang = (s / (float)cv) * Mathf.PI * 2f; float r = size * 0.5f * (0.7f + H01(seed + (uint)s * 13u + 65u) * 0.6f); V.Add(new Vector3(pos.x + Mathf.Cos(ang) * r, pos.y + Mathf.Sin(ang) * r, 0f)); C.Add(ce); } for (int s = 0; s < cv; s++) { T.Add(v0); T.Add(v0 + 1 + s); T.Add(v0 + 1 + (s + 1) % cv); } } SC("Marsh_Caustics", BM(V, T, C), sortingOrder + 6); }

    void BuildRippleLayer()
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); activeRipples.Clear();
        int segs = 16; int ri2 = 0;
        for (int i = 0; i < mergedPuddles.Count; i++)
        {
            var pp = mergedPuddles[i]; if (!pp.isLarge) continue;
            for (int rr = 0; rr < ripplesPerPuddle; rr++, ri2++)
            {
                uint seed = (uint)ri2 * 37159u ^ 0xABCDu; Vector2 rc = pp.center + HDisk(seed + 71u, seed + 72u) * pp.radius * 0.3f;
                float[] warp = new float[segs + 1]; for (int s = 0; s <= segs; s++) { float ang = (s / (float)segs) * Mathf.PI * 2f; warp[s] = VNoise(Mathf.Cos(ang) * 3f + H01(seed + 75u) * 10f, Mathf.Sin(ang) * 3f, WH((uint)(ri2 * 100 + s))) * 0.15f; }
                RippleData rd; rd.center = rc; rd.puddleRadius = pp.radius; rd.phase = H01(seed + 73u); rd.speed = rippleSpeed * (0.5f + H01(seed + 74u) * 1.0f); rd.warp = warp; rd.vertStart = V.Count; rd.segments = segs; activeRipples.Add(rd);
                for (int s = 0; s <= segs; s++) { V.Add(Vector3.zero); V.Add(Vector3.zero); C.Add(rippleColor); C.Add(new Color(rippleColor.r, rippleColor.g, rippleColor.b, 0f)); }
                for (int s = 0; s < segs; s++) { int b = rd.vertStart + s * 2; T.Add(b); T.Add(b + 2); T.Add(b + 1); T.Add(b + 1); T.Add(b + 2); T.Add(b + 3); }
            }
        }
        rippleMesh = BM(V, T, C); ripV = (Vector3[])rippleMesh.vertices.Clone(); ripC = (Color[])rippleMesh.colors.Clone(); SC("Marsh_Ripples", rippleMesh, sortingOrder + 7);
    }

    void BuildDimpleLayer()
    {
        var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>();
        dimpleSlotArr = new DimpleSlot[dimpleSlots]; int segs = 10; int vpr = (segs + 1) * 2; int splashV = segs + 2;
        for (int i = 0; i < dimpleSlots; i++)
        {
            uint seed = (uint)i * 19937u ^ 0xD14Bu; float pr; DimpleSlot ds;
            ds.center = PickOnMerged(seed, out pr); ds.puddleR = pr; ds.birthTime = -999f; ds.lifetime = dimpleLifetime;
            ds.noisePhase = H01(seed + 6u) * Mathf.PI * 2f; ds.elongAngle = H01(seed + 7u) * Mathf.PI * 2f;
            ds.vertStart = V.Count; ds.ringVerts = vpr; ds.active = false; dimpleSlotArr[i] = ds;
            for (int ring = 0; ring < 3; ring++) for (int s = 0; s <= segs; s++) { V.Add(Vector3.zero); V.Add(Vector3.zero); C.Add(Color.clear); C.Add(Color.clear); }
            V.Add(Vector3.zero); C.Add(Color.clear); for (int s = 0; s <= segs; s++) { V.Add(Vector3.zero); C.Add(Color.clear); }
            for (int ring = 0; ring < 3; ring++) { int rs = ds.vertStart + ring * vpr; for (int s = 0; s < segs; s++) { int b = rs + s * 2; T.Add(b); T.Add(b + 2); T.Add(b + 1); T.Add(b + 1); T.Add(b + 2); T.Add(b + 3); } }
            int sS = ds.vertStart + 3 * vpr; for (int s = 0; s < segs; s++) { T.Add(sS); T.Add(sS + 1 + s); T.Add(sS + 2 + s); }
        }
        dimpleMesh = BM(V, T, C); dimV = (Vector3[])dimpleMesh.vertices.Clone(); dimC = (Color[])dimpleMesh.colors.Clone(); SC("Marsh_Dimples", dimpleMesh, sortingOrder + 8);
    }

    void BuildLilyPadLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < lilyPadCount; i++) { uint seed = (uint)i * 69857u ^ 0xFACEu; float pr; Vector2 pos = PickOnMerged(seed, out pr); float size = Mathf.Lerp(lilyPadMinSize, lilyPadMaxSize, H01(seed + 80u)); int segs = 10; float gapStart = H01(seed + 81u) * Mathf.PI * 2f; float gapAngle = 0.35f + H01(seed + 84u) * 0.30f; Vector2 so = new Vector2(size * 0.06f, -size * 0.08f); Color shC = new Color(0.02f, 0.05f, 0.02f, 0.35f); int sv0 = V.Count; V.Add(new Vector3(pos.x + so.x, pos.y + so.y, 0f)); C.Add(shC); for (int s = 0; s <= segs; s++) { float t2 = (float)s / segs; float ang = gapStart + gapAngle + t2 * (Mathf.PI * 2f - gapAngle); V.Add(new Vector3(pos.x + so.x + Mathf.Cos(ang) * size * 1.08f, pos.y + so.y + Mathf.Sin(ang) * size * 1.08f, 0f)); C.Add(new Color(shC.r, shC.g, shC.b, 0f)); } for (int s = 0; s < segs; s++) { T.Add(sv0); T.Add(sv0 + 1 + s); T.Add(sv0 + 2 + s); } int v0 = V.Count; Color padC = Color.Lerp(lilyPadColor, lilyPadDark, H01(seed + 82u)); V.Add(new Vector3(pos.x, pos.y, 0f)); C.Add(padC); for (int s = 0; s <= segs; s++) { float t2 = (float)s / segs; float ang = gapStart + gapAngle + t2 * (Mathf.PI * 2f - gapAngle); float r = size * (0.86f + HS(seed + 83u + (uint)s) * 0.14f); V.Add(new Vector3(pos.x + Mathf.Cos(ang) * r, pos.y + Mathf.Sin(ang) * r, 0f)); Color ec = padC; ec.r += 0.03f; ec.g += 0.05f; C.Add(ec); } for (int s = 0; s < segs; s++) { T.Add(v0); T.Add(v0 + 1 + s); T.Add(v0 + 2 + s); } int vc = 5 + (int)(H01(seed + 85u) * 3f); for (int vi = 0; vi < vc; vi++) { float vAng = gapStart + gapAngle + ((vi + 0.5f) / vc) * (Mathf.PI * 2f - gapAngle); float vLen = size * (0.5f + H01(seed + 86u + (uint)vi) * 0.35f); float vHW = 0.004f; Vector2 dir = new Vector2(Mathf.Cos(vAng), Mathf.Sin(vAng)); Vector2 perp = new Vector2(-dir.y, dir.x); int vv0 = V.Count; V.Add(new Vector3(pos.x + perp.x * vHW, pos.y + perp.y * vHW, 0f)); V.Add(new Vector3(pos.x - perp.x * vHW, pos.y - perp.y * vHW, 0f)); V.Add(new Vector3(pos.x + dir.x * vLen, pos.y + dir.y * vLen, 0f)); Color vcc = lilyVeinColor; C.Add(vcc); C.Add(vcc); C.Add(new Color(vcc.r, vcc.g, vcc.b, vcc.a * 0.3f)); T.Add(vv0); T.Add(vv0 + 2); T.Add(vv0 + 1); } } SC("Marsh_LilyPads", BM(V, T, C), sortingOrder + 9); }

    void BuildReedLayer() { var V = new List<Vector3>(); var T = new List<int>(); var C = new List<Color>(); for (int i = 0; i < reedCount; i++) { uint seed = (uint)i * 43891u ^ 0x1234u; float pr; Vector2 pos = PickOnMerged(seed, out pr); pos += HDir(seed + 99u) * pr * (0.7f + H01(seed + 98u) * 0.35f); float h = Mathf.Lerp(reedMinHeight, reedMaxHeight, H01(seed + 90u)); float lean = HS(seed + 91u) * 0.4f; float hw = reedWidth * 0.5f * (0.8f + H01(seed + 92u) * 0.4f); int v0 = V.Count; V.Add(new Vector3(pos.x - hw, pos.y, 0f)); V.Add(new Vector3(pos.x + hw, pos.y, 0f)); C.Add(reedBase); C.Add(reedBase); float mh = h * 0.5f; float ml = lean * 0.3f; V.Add(new Vector3(pos.x - hw * 0.7f + ml, pos.y + mh, 0f)); V.Add(new Vector3(pos.x + hw * 0.7f + ml, pos.y + mh, 0f)); Color mc = Color.Lerp(reedBase, reedTip, 0.4f); C.Add(mc); C.Add(mc); V.Add(new Vector3(pos.x + lean, pos.y + h, 0f)); C.Add(reedTip); T.Add(v0); T.Add(v0 + 2); T.Add(v0 + 1); T.Add(v0 + 1); T.Add(v0 + 2); T.Add(v0 + 3); T.Add(v0 + 2); T.Add(v0 + 4); T.Add(v0 + 3); } SC("Marsh_Reeds", BM(V, T, C), sortingOrder + 10); }

    //  MESH HELPERS  
    void AddRadiiCircle(List<Vector3> V, List<int> T, List<Color> C, Vector2 cen, float[] radii, int segs, Color cenC, Color edgeC) { int v0 = V.Count; V.Add(new Vector3(cen.x, cen.y, 0f)); C.Add(cenC); for (int s = 0; s <= segs; s++) { float ang = (s / (float)segs) * Mathf.PI * 2f; int ri = Mathf.RoundToInt((s / (float)segs) * (radii.Length - 1)); float r = radii[Mathf.Min(ri, radii.Length - 1)]; V.Add(new Vector3(cen.x + Mathf.Cos(ang) * r, cen.y + Mathf.Sin(ang) * r, 0f)); C.Add(edgeC); } for (int s = 0; s < segs; s++) { T.Add(v0); T.Add(v0 + 1 + s); T.Add(v0 + 2 + s); } }
    void AddSimpleCircle(List<Vector3> V, List<int> T, List<Color> C, Vector2 cen, float radius, Color cenC, Color edgeC, int segs, uint seed) { int v0 = V.Count; V.Add(new Vector3(cen.x, cen.y, 0f)); C.Add(cenC); for (int s = 0; s <= segs; s++) { float ang = (s / (float)segs) * Mathf.PI * 2f; float r = radius * (1f + HS(seed + (uint)s + 300u) * 0.12f); V.Add(new Vector3(cen.x + Mathf.Cos(ang) * r, cen.y + Mathf.Sin(ang) * r, 0f)); C.Add(edgeC); } for (int s = 0; s < segs; s++) { T.Add(v0); T.Add(v0 + 1 + s); T.Add(v0 + 2 + s); } }
    Mesh BM(List<Vector3> V, List<int> T, List<Color> C) { Mesh m = new Mesh(); if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; m.SetVertices(V); m.SetTriangles(T, 0); m.SetColors(C); m.RecalculateBounds(); return m; }
    void SC(string name, Mesh mesh, int order) { Material mat = new Material(Shader.Find("Sprites/Default")); allMeshes.Add(mesh); allMats.Add(mat); GameObject go = new GameObject(name); go.transform.SetParent(transform); go.transform.localPosition = Vector3.zero; go.transform.localScale = Vector3.one; go.AddComponent<MeshFilter>().sharedMesh = mesh; MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; mr.sortingLayerName = sortingLayerName; mr.sortingOrder = order; childGOs.Add(go); }

    /// Returns true if the given world position is inside any placed water body.
    public bool IsOverWater(Vector2 pos)
    {
        for (int i = 0; i < mergedPuddles.Count; i++)
        {
            var mp = mergedPuddles[i];
            Vector2 diff = pos - mp.center;
            float dist = diff.magnitude;
            if (dist > mp.radius * 1.5f) continue; // quick reject with generous margin
            float angle = Mathf.Atan2(diff.y, diff.x);
            if (angle < 0f) angle += Mathf.PI * 2f;
            float r = RAtAng(mp.radii, mp.segs, angle);
            if (dist < r * 1.1f) return true; // 10% forgiveness
        }
        return false;
    }

    /// Returns the radius of the puddle at the given position (0 if not over water).
    public float GetPuddleRadius(Vector2 pos)
    {
        for (int i = 0; i < mergedPuddles.Count; i++)
        {
            var mp = mergedPuddles[i];
            Vector2 diff = pos - mp.center;
            float dist = diff.magnitude;
            if (dist > mp.radius * 1.3f) continue;
            float angle = Mathf.Atan2(diff.y, diff.x);
            if (angle < 0f) angle += Mathf.PI * 2f;
            float r = RAtAng(mp.radii, mp.segs, angle);
            if (dist < r) return mp.radius;
        }
        return 0f;
    }

    void Cleanup()
    {
        activeRipples.Clear(); placedBodies.Clear(); mergedPuddles.Clear(); ripV = dimV = null; ripC = dimC = null; rippleMesh = dimpleMesh = null; dimpleSlotArr = null;
        foreach (var go in childGOs) { if (go != null) DestroyImmediate(go); }
        childGOs.Clear();
        foreach (var m in allMeshes) { if (m != null) DestroyImmediate(m); }
        allMeshes.Clear();
        foreach (var mt in allMats) { if (mt != null) DestroyImmediate(mt); }
        allMats.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--) { var ch = transform.GetChild(i).gameObject; if (ch.name.StartsWith("Marsh_")) DestroyImmediate(ch); }
    }
    void OnDisable() => Cleanup();
    void OnDestroy() => Cleanup();
}
