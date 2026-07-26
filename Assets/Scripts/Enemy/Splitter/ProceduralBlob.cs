using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// PROCEDURAL SOFT-BODY BLOB  (v3)
//
// A pressurized mass-spring ring, simulated in WORLD space, rendered as five
// generated meshes. No sprites, no spritesheets, no imported meshes.
//
// PHYSICS (real, not a sine wobble):
//   • edge springs      keep neighbouring points at their rest separation
//   • pressure          an ideal-gas term (P ∝ restArea/area - 1) pushing every
//                       edge along its outward normal. Squash the blob and it
//                       bulges out the sides instead of just getting thinner.
//   • anchor springs    pull each point toward its ideal slot on a ring around
//                       transform.position. They're SPRINGS, and the points carry
//                       momentum, so the membrane LAGS behind the rigidbody:
//                       accelerating squashes it, stopping makes it slosh.
//   • heartbeat         a double-thump curve swells the rest radius AND fires a
//                       radial impulse at systole. The overshoot is the spring
//                       system responding, not a scripted scale animation.
//   • squash & stretch  Stretch() deforms the ANCHOR ring along an axis, so the
//                       membrane has to chase the new shape. Anticipation and
//                       impact become physical, not keyframed.
//
// RENDERING (why it isn't polygonal):
//   • 40 simulated points → Catmull-Rom spline → 120 render points
//   • ring stack: glow / core / mid / rim(+rim light) / outline / feather
//   • the feather ring is same-colour, alpha 0 — a resolution-independent
//     anti-aliased edge, geometry rather than texels
//   • the OUTLINE is what keeps two touching blobs legible as two blobs
[DisallowMultipleComponent]
public class ProceduralBlob : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("Simulated membrane points. 32-48. The rendered silhouette is this x " +
             "rimSubdivisions, so the sim stays cheap and the curve stays smooth.")]
    [SerializeField] private int pointCount = 40;

    [Tooltip("Rest radius in LOCAL units. World size = this x transform.lossyScale.")]
    [SerializeField] private float radius = 0.55f;

    [Tooltip("Spline samples between each pair of simulated points. This is what " +
             "removes the polygonal look — raise it, not pointCount.")]
    [Range(1, 6)][SerializeField] private int rimSubdivisions = 3;

    [Range(0f, 0.4f)][SerializeField] private float irregularity = 0.13f;
    [SerializeField] private float wobbleSpeed = 0.5f;

    [Header("Heartbeat")]
    [SerializeField] private float pulsePeriod = 1.5f;
    [Range(0f, 0.3f)][SerializeField] private float pulseAmount = 0.07f;

    [Tooltip("Radial impulse at each systole peak. Makes the beat OVERSHOOT and " +
             "settle instead of easing. Set to 0 and the pulse goes lifeless.")]
    [SerializeField] private float pulseKick = 1.8f;

    [Range(0f, 0.8f)][SerializeField] private float nucleusCounterPulse = 0.30f;

    [Header("Soft Body")]
    [SerializeField] private float edgeStiffness = 190f;
    [SerializeField] private float edgeDamping = 9f;

    [Tooltip("The inertia knob. LOWER = trails further behind when walking, sloshes " +
             "more on stops. Too low and it visually detaches from the body.")]
    [SerializeField] private float anchorStiffness = 38f;

    [SerializeField] private float anchorDamping = 5f;

    [Tooltip("Internal pressure — preserves volume. 0 = a floppy sack.")]
    [SerializeField] private float pressure = 150f;

    [SerializeField] private float drag = 4.5f;
    [Range(1, 6)][SerializeField] private int substeps = 3;
    [SerializeField] private float minRadiusFactor = 0.30f;
    [SerializeField] private float maxRadiusFactor = 2.20f;

    [Header("Squash & Stretch")]
    [Tooltip("How fast a Stretch() decays back to round, per second. Low = the " +
             "blob holds its deformed shape longer. 6-10 feels rubbery.")]
    [SerializeField] private float stretchDecay = 7f;

    [Header("Palette")]
    [SerializeField] private Color coreColor = new Color(0.86f, 0.55f, 1.00f, 1f);
    [SerializeField] private Color midColor = new Color(0.62f, 0.24f, 0.86f, 1f);
    [SerializeField] private Color edgeColor = new Color(0.26f, 0.08f, 0.40f, 1f);
    [SerializeField] private Color rimLightColor = new Color(0.95f, 0.62f, 1.00f, 1f);
    [SerializeField] private Color nucleusColor = new Color(1.00f, 0.72f, 1.00f, 0.55f);
    [SerializeField] private Color cellColor = new Color(0.96f, 0.60f, 1.00f, 0.42f);
    [SerializeField] private Color highlightColor = new Color(1.00f, 0.95f, 1.00f, 0.50f);

    [Tooltip("Soft halo behind the body. Sells 'this thing is glowing from within'. " +
             "Alpha 0 disables.")]
    [SerializeField] private Color glowColor = new Color(0.62f, 0.24f, 0.92f, 0.30f);

    [Range(0f, 1f)][SerializeField] private float glowWidth = 0.40f;

    [Tooltip("World-space light direction. Drives the rim light, the inner shadow " +
             "on the opposite side, and where the sheen sits.")]
    [SerializeField] private Vector2 lightDirection = new Vector2(-0.5f, 0.85f);

    [Header("Silhouette")]
    [Tooltip("Dark rim just past the membrane. Without it, two blobs resting " +
             "against each other read as one peanut.")]
    [SerializeField] private Color outlineColor = new Color(0.08f, 0.02f, 0.14f, 1f);

    [Range(0f, 0.25f)][SerializeField] private float outlineWidth = 0.08f;

    [Tooltip("Transparent feather past the OUTLINE. The anti-aliased edge.")]
    [Range(0f, 0.25f)][SerializeField] private float featherWidth = 0.07f;

    [Range(0.2f, 0.9f)][SerializeField] private float midStop = 0.58f;
    [Range(0.1f, 0.9f)][SerializeField] private float nucleusScale = 0.50f;

    [Header("Internal Cells")]
    [Range(0, 10)][SerializeField] private int cellCount = 5;
    [SerializeField] private float cellSize = 0.13f;
    [SerializeField] private float cellDriftSpeed = 0.35f;

    [Header("Damage Flash")]
    [SerializeField] private Color flashColor = new Color(1f, 0.85f, 1f, 1f);
    [SerializeField] private float flashDuration = 0.10f;

    [Header("Disintegration")]
    [Tooltip("How many wedges the membrane tears into on death. 7-11.")]
    [Range(3, 16)][SerializeField] private int shatterWedges = 9;

    [Tooltip("Seconds the goo wedges live before they've fully soaked away.")]
    [SerializeField] private float shatterLifetime = 1.15f;

    // ---- simulation state (world space) ----
    private Vector2[] p, v, anchor;
    private float[] restF;

    // ---- render state ----
    private int rimCount;
    private Vector2[] rim;
    private Mesh glowMesh, bodyMesh, nucleusMesh, cellMesh;
    private MeshRenderer glowMR, bodyMR, nucleusMR, cellMR, hlMR;
    private Transform hlT;
    private Vector3[] vGlow, vBody, vNucleus, vCell;
    private Color[] cGlow, cBody, cNucleus, cCell;

    private struct Cell { public float angle, orbit, phase, size, speed; }
    private Cell[] cells;

    private static Material _sharedMat;
    private SpriteRenderer sortSource;
    private float seed, flashT, beat, prevBeat;
    private bool built, dead;

    private Vector2 stretchDirLocal = Vector2.up;
    private float stretchAmount;

    public float Radius => radius;
    public float WorldRadius => radius * Mathf.Abs(transform.lossyScale.x);
    public Color CoreColor => coreColor;
    public Color EdgeColor => edgeColor;
    public float Beat => beat;

    /// Centre of mass of the membrane — differs from transform.position while the
    /// blob is sloshing. Use this when you want the visual centre.
    public Vector2 MembraneCenter
    {
        get
        {
            if (!built) return transform.position;
            Vector2 c = Vector2.zero;
            for (int i = 0; i < p.Length; i++) c += p[i];
            return c / p.Length;
        }
    }

    public void SetRadius(float r) { radius = Mathf.Max(0.05f, r); }

    private void Start() { EnsureBuilt(); }

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        seed = Random.value * 100f;
        pointCount = Mathf.Max(12, pointCount);
        rimCount = pointCount * Mathf.Max(1, rimSubdivisions);

        p = new Vector2[pointCount];
        v = new Vector2[pointCount];
        restF = new float[pointCount];
        anchor = new Vector2[pointCount];
        rim = new Vector2[rimCount];

        for (int i = 0; i < pointCount; i++) { restF[i] = 1f; p[i] = IdealAnchor(i); }

        if (_sharedMat == null)
            _sharedMat = new Material(Shader.Find("Sprites/Default")) { name = "BlobVertexColor" };

        sortSource = GetComponentInParent<SpriteRenderer>();

        BuildGlowMesh();
        BuildBodyMesh();
        BuildNucleusMesh();
        BuildCells();
        hlMR = BuildHighlight(out hlT);

        SyncMeshes();
    }

    // MESH CONSTRUCTION

    // Halo: a ring from the silhouette outward, fading to nothing. Drawn behind
    // everything, so it reads as light bleeding out of the body.
    private void BuildGlowMesh()
    {
        NewLayer("BlobGlow", out var mf, out glowMR);

        int N = rimCount;
        vGlow = new Vector3[2 * N];
        cGlow = new Color[2 * N];

        var tris = new int[N * 6];
        for (int j = 0; j < N; j++)
        {
            int jn = (j + 1) % N;
            int t = j * 6;
            tris[t + 0] = j; tris[t + 1] = N + j; tris[t + 2] = N + jn;
            tris[t + 3] = j; tris[t + 4] = N + jn; tris[t + 5] = jn;
        }

        glowMesh = new Mesh { name = "BlobGlowMesh" };
        glowMesh.MarkDynamic();
        glowMesh.vertices = vGlow;
        glowMesh.colors = cGlow;
        glowMesh.triangles = tris;
        mf.mesh = glowMesh;
    }

    // Ring layout: [0] centre, [1..N] mid, [N+1..2N] rim, [2N+1..3N] outline,
    // [3N+1..4N] feather.
    private void BuildBodyMesh()
    {
        NewLayer("BlobBody", out var mf, out bodyMR);

        int N = rimCount;
        vBody = new Vector3[1 + 4 * N];
        cBody = new Color[1 + 4 * N];

        var tris = new List<int>(N * 21);
        int mid = 1, rimI = 1 + N, outl = 1 + 2 * N, feath = 1 + 3 * N;

        for (int j = 0; j < N; j++)
        {
            int jn = (j + 1) % N;
            tris.Add(0); tris.Add(mid + j); tris.Add(mid + jn);
            Strip(tris, mid + j, mid + jn, rimI + j, rimI + jn);
            Strip(tris, rimI + j, rimI + jn, outl + j, outl + jn);
            Strip(tris, outl + j, outl + jn, feath + j, feath + jn);
        }

        bodyMesh = new Mesh { name = "BlobBodyMesh" };
        bodyMesh.MarkDynamic();
        bodyMesh.vertices = vBody;
        bodyMesh.colors = cBody;
        bodyMesh.triangles = tris.ToArray();
        mf.mesh = bodyMesh;
    }

    private static void Strip(List<int> t, int a, int b, int c, int d)
    {
        t.Add(a); t.Add(c); t.Add(d);
        t.Add(a); t.Add(d); t.Add(b);
    }

    private void BuildNucleusMesh()
    {
        NewLayer("BlobNucleus", out var mf, out nucleusMR);

        int N = rimCount;
        vNucleus = new Vector3[1 + N];
        cNucleus = new Color[1 + N];

        var tris = new int[N * 3];
        for (int j = 0; j < N; j++)
        {
            tris[j * 3 + 0] = 0;
            tris[j * 3 + 1] = 1 + j;
            tris[j * 3 + 2] = 1 + (j + 1) % N;
        }

        nucleusMesh = new Mesh { name = "BlobNucleusMesh" };
        nucleusMesh.MarkDynamic();
        nucleusMesh.vertices = vNucleus;
        nucleusMesh.colors = cNucleus;
        nucleusMesh.triangles = tris;
        mf.mesh = nucleusMesh;
    }

    private const int CELL_SEGS = 10;

    private void BuildCells()
    {
        cells = new Cell[Mathf.Max(0, cellCount)];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new Cell
            {
                angle = Random.Range(0f, Mathf.PI * 2f),
                orbit = Random.Range(0.12f, 0.52f),
                phase = Random.Range(0f, Mathf.PI * 2f),
                size = Random.Range(0.65f, 1.35f),
                speed = Random.Range(-1f, 1f) * cellDriftSpeed
            };

        if (cells.Length == 0) return;

        NewLayer("BlobCells", out var mf, out cellMR);

        int per = 1 + CELL_SEGS;
        vCell = new Vector3[cells.Length * per];
        cCell = new Color[cells.Length * per];

        var tris = new int[cells.Length * CELL_SEGS * 3];
        for (int c = 0; c < cells.Length; c++)
        {
            int b = c * per;
            for (int j = 0; j < CELL_SEGS; j++)
            {
                int t = (c * CELL_SEGS + j) * 3;
                tris[t + 0] = b;
                tris[t + 1] = b + 1 + j;
                tris[t + 2] = b + 1 + (j + 1) % CELL_SEGS;
            }
        }

        cellMesh = new Mesh { name = "BlobCellsMesh" };
        cellMesh.MarkDynamic();
        cellMesh.vertices = vCell;
        cellMesh.colors = cCell;
        cellMesh.triangles = tris;
        mf.mesh = cellMesh;
    }

    private MeshRenderer BuildHighlight(out Transform t)
    {
        var go = NewLayer("BlobHighlight", out var mf, out var mr);
        t = go.transform;

        const int N = 20;
        var mesh = new Mesh { name = "BlobHighlightMesh" };
        var vs = new Vector3[N + 1];
        var cs = new Color[N + 1];
        vs[0] = Vector3.zero; cs[0] = highlightColor;

        for (int i = 0; i < N; i++)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            vs[i + 1] = new Vector3(Mathf.Cos(a) * 1.35f, Mathf.Sin(a), 0f);
            Color c = highlightColor; c.a = 0f;
            cs[i + 1] = c;
        }

        var tris = new int[N * 3];
        for (int i = 0; i < N; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % N + 1;
        }
        mesh.vertices = vs; mesh.colors = cs; mesh.triangles = tris;
        mf.mesh = mesh;
        return mr;
    }

    private GameObject NewLayer(string layerName, out MeshFilter mf, out MeshRenderer mr)
    {
        var go = new GameObject(layerName);
        go.transform.SetParent(transform, false);
        mf = go.AddComponent<MeshFilter>();
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _sharedMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    // Ideal ring slot for point i, with squash-and-stretch folded in. Deforming the
    // ANCHORS (not the vertices) means the membrane has to physically chase the new
    // shape — anticipation and impact land as motion, not as a scale keyframe.
    private Vector2 IdealAnchor(int i)
    {
        float a = i / (float)pointCount * Mathf.PI * 2f;
        Vector2 dirL = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

        if (Mathf.Abs(stretchAmount) > 0.001f)
        {
            // Volume-preserving: scale along the axis, inverse-scale across it.
            Vector2 sd = stretchDirLocal;
            float along = Vector2.Dot(dirL, sd);
            Vector2 perp = dirL - sd * along;
            float k = 1f + stretchAmount;
            dirL = sd * (along * k) + perp * (1f / k);
        }

        Vector3 local = (Vector3)(dirL * (radius * restF[i]));
        return transform.TransformPoint(local);
    }

    // SIMULATION

    // Double-thump cardiac curve: a sharp systole, then a softer, later dicrotic
    // bump. Two gaussians. Far more alive than a sine.
    private static float Heartbeat(float x)
    {
        float a = Mathf.Exp(-Mathf.Pow((x - 0.12f) / 0.055f, 2f));
        float b = 0.55f * Mathf.Exp(-Mathf.Pow((x - 0.30f) / 0.075f, 2f));
        return Mathf.Clamp01(a + b);
    }

    private void FixedUpdate()
    {
        if (dead) return;
        if (!built) EnsureBuilt();

        prevBeat = beat;
        beat = pulsePeriod > 0.01f
            ? Heartbeat(Mathf.Repeat(Time.time / pulsePeriod + seed, 1f))
            : 0f;

        if (pulseKick > 0f && beat > 0.7f && prevBeat <= 0.7f) Pulse(pulseKick);

        stretchAmount = Mathf.Lerp(stretchAmount, 0f,
                                   Mathf.Clamp01(stretchDecay * Time.fixedDeltaTime));

        float t = Time.time * wobbleSpeed + seed;
        float swell = 1f + pulseAmount * beat;
        for (int i = 0; i < pointCount; i++)
        {
            float n = Mathf.PerlinNoise(i * 0.65f, t) - 0.5f;
            restF[i] = (1f + irregularity * n * 2f) * swell;
        }

        float h = Time.fixedDeltaTime / substeps;
        for (int s = 0; s < substeps; s++) Step(h);

        if (flashT > 0f) flashT -= Time.fixedDeltaTime;
    }

    private void Step(float h)
    {
        float wr = WorldRadius;
        if (wr <= 0.0001f) return;

        for (int i = 0; i < pointCount; i++) anchor[i] = IdealAnchor(i);

        Vector2 center = Vector2.zero;
        for (int i = 0; i < pointCount; i++) center += p[i];
        center /= pointCount;

        float area = 0f;
        for (int i = 0; i < pointCount; i++)
        {
            int j = (i + 1) % pointCount;
            area += p[i].x * p[j].y - p[j].x * p[i].y;
        }
        area = Mathf.Abs(area) * 0.5f;
        float restArea = Mathf.PI * wr * wr;
        float pDiff = area > 0.0001f ? (restArea - area) / restArea : 1f;
        pDiff = Mathf.Clamp(pDiff, -1.5f, 1.5f);

        for (int i = 0; i < pointCount; i++)
        {
            int j = (i + 1) % pointCount;

            Vector2 e = p[j] - p[i];
            float len = e.magnitude;
            if (len < 0.0001f) continue;
            Vector2 dir = e / len;

            float restLen = Vector2.Distance(anchor[i], anchor[j]);

            float relVel = Vector2.Dot(v[j] - v[i], dir);
            float f = (len - restLen) * edgeStiffness + relVel * edgeDamping;
            Vector2 fv = dir * f;
            v[i] += fv * h;
            v[j] -= fv * h;

            // CCW winding, so rotating the edge -90° gives the outward normal.
            Vector2 n = new Vector2(dir.y, -dir.x);
            Vector2 pf = n * (pressure * pDiff * len * 0.5f);
            v[i] += pf * h;
            v[j] += pf * h;
        }

        // Damp against the BODY's velocity, not zero — damping against zero makes
        // the blob feel glued to the ground while its rigidbody walks away.
        Vector2 bodyVel = Vector2.zero;
        var rb = GetComponentInParent<Rigidbody2D>();
        if (rb != null) bodyVel = rb.linearVelocity;

        for (int i = 0; i < pointCount; i++)
        {
            Vector2 toAnchor = anchor[i] - p[i];
            Vector2 rel = v[i] - bodyVel;
            v[i] += (toAnchor * anchorStiffness - rel * anchorDamping) * h;
            v[i] -= v[i] * Mathf.Min(1f, drag * h);
            p[i] += v[i] * h;
        }

        for (int i = 0; i < pointCount; i++)
        {
            Vector2 d = p[i] - center;
            float dist = d.magnitude;
            if (dist < 0.0001f) { p[i] = center + Vector2.up * (wr * minRadiusFactor); continue; }

            float min = wr * minRadiusFactor;
            float max = wr * maxRadiusFactor;
            if (dist < min) { p[i] = center + d / dist * min; v[i] *= 0.5f; }
            else if (dist > max) { p[i] = center + d / dist * max; v[i] *= 0.5f; }
        }
    }

    // RENDERING

    private void LateUpdate()
    {
        if (!built || dead) return;
        BuildRimSpline();
        SyncMeshes();
        SyncSorting();
    }

    // Resample the simulated ring through a closed Catmull-Rom spline. This is why
    // a 40-point sim renders as a smooth curve rather than a visible 40-gon.
    private void BuildRimSpline()
    {
        int sub = Mathf.Max(1, rimSubdivisions);
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 p0 = p[(i - 1 + pointCount) % pointCount];
            Vector2 p1 = p[i];
            Vector2 p2 = p[(i + 1) % pointCount];
            Vector2 p3 = p[(i + 2) % pointCount];

            for (int s = 0; s < sub; s++)
                rim[i * sub + s] = CatmullRom(p0, p1, p2, p3, s / (float)sub);
        }
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1)
                     + (-p0 + p2) * t
                     + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                     + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private void SyncMeshes()
    {
        int N = rimCount;

        Vector2 wc = Vector2.zero;
        for (int i = 0; i < N; i++) wc += rim[i];
        wc /= N;
        Vector3 lc = transform.InverseTransformPoint(wc);

        float flash = flashT > 0f ? Mathf.Clamp01(flashT / flashDuration) : 0f;
        Color core = Color.Lerp(coreColor, flashColor, flash);
        Color midC = Color.Lerp(midColor, flashColor, flash * 0.85f);
        Color edge = Color.Lerp(edgeColor, flashColor, flash * 0.7f);
        Color rimL = Color.Lerp(rimLightColor, flashColor, flash);
        Color outlineC = Color.Lerp(outlineColor, flashColor, flash * 0.5f);
        Color nucC = Color.Lerp(nucleusColor, flashColor, flash);

        // The halo breathes with the heartbeat and flares on a hit.
        Color glowC = glowColor;
        glowC.a *= (0.75f + 0.35f * beat) + flash * 1.2f;

        Vector2 lightDir = lightDirection.sqrMagnitude > 0.0001f
            ? lightDirection.normalized : Vector2.up;

        int mid = 1, rimI = 1 + N, outl = 1 + 2 * N, feath = 1 + 3 * N;

        vBody[0] = lc; cBody[0] = core;
        vNucleus[0] = lc; cNucleus[0] = nucC;

        float nucR = nucleusScale * (1f - nucleusCounterPulse * beat);
        float glowOuter = 1f + outlineWidth + featherWidth + glowWidth;

        for (int j = 0; j < N; j++)
        {
            Vector3 lp = transform.InverseTransformPoint(rim[j]);
            Vector3 fromC = lp - lc;

            Vector2 outward = ((Vector2)fromC).normalized;
            float lit = Mathf.Clamp01(Vector2.Dot(outward, lightDir));
            Color rimCol = Color.Lerp(edge, rimL, lit * lit * 0.85f);

            float shade = Mathf.Clamp01(-Vector2.Dot(outward, lightDir));
            Color midCol = Color.Lerp(midC, edge, shade * 0.45f);

            vBody[mid + j] = lc + fromC * midStop;
            cBody[mid + j] = midCol;

            vBody[rimI + j] = lp;
            cBody[rimI + j] = rimCol;

            vBody[outl + j] = lc + fromC * (1f + outlineWidth);
            cBody[outl + j] = outlineC;

            vBody[feath + j] = lc + fromC * (1f + outlineWidth + featherWidth);
            Color f = outlineC; f.a = 0f;
            cBody[feath + j] = f;

            vGlow[j] = lc + fromC * (1f + outlineWidth);
            cGlow[j] = glowC;
            vGlow[N + j] = lc + fromC * glowOuter;
            Color g = glowC; g.a = 0f;
            cGlow[N + j] = g;

            vNucleus[1 + j] = lc + fromC * nucR;
            Color nr = nucC; nr.a = 0f;
            cNucleus[1 + j] = nr;
        }

        glowMesh.vertices = vGlow; glowMesh.colors = cGlow; glowMesh.RecalculateBounds();
        bodyMesh.vertices = vBody; bodyMesh.colors = cBody; bodyMesh.RecalculateBounds();
        nucleusMesh.vertices = vNucleus; nucleusMesh.colors = cNucleus; nucleusMesh.RecalculateBounds();

        SyncCells(lc, flash);

        float squash = Mathf.Clamp(MeasureSquash(), 0.5f, 1.5f);
        hlT.localPosition = lc + (Vector3)(lightDir * (radius * 0.40f));
        hlT.localScale = Vector3.one * (radius * 0.19f * squash * (1f + 0.18f * beat));
    }

    private void SyncCells(Vector3 lc, float flash)
    {
        if (cells == null || cells.Length == 0 || cellMesh == null) return;

        Color baseCol = Color.Lerp(cellColor, flashColor, flash);
        float t = Time.time;
        int per = 1 + CELL_SEGS;

        for (int c = 0; c < cells.Length; c++)
        {
            var cell = cells[c];
            float ang = cell.angle + t * cell.speed;

            // Bob on an independent slow cycle, plus a shove outward on each beat —
            // organelles pushed around by the pump.
            float bob = 0.85f + 0.15f * Mathf.Sin(t * 0.9f + cell.phase);
            float orbit = cell.orbit * bob * (1f + 0.12f * beat);

            Vector3 pos = lc + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * (orbit * radius);
            float r = cellSize * cell.size * radius * (1f - 0.15f * beat);

            int b = c * per;
            vCell[b] = pos;
            Color hot = baseCol; hot.a = baseCol.a * (0.7f + 0.3f * beat);
            cCell[b] = hot;

            for (int j = 0; j < CELL_SEGS; j++)
            {
                float a = j / (float)CELL_SEGS * Mathf.PI * 2f;
                vCell[b + 1 + j] = pos + new Vector3(Mathf.Cos(a) * 1.15f, Mathf.Sin(a) * 0.85f, 0f) * r;
                Color fade = hot; fade.a = 0f;
                cCell[b + 1 + j] = fade;
            }
        }

        cellMesh.vertices = vCell;
        cellMesh.colors = cCell;
        cellMesh.RecalculateBounds();
    }

    private float MeasureSquash()
    {
        float area = 0f;
        for (int i = 0; i < pointCount; i++)
        {
            int j = (i + 1) % pointCount;
            area += p[i].x * p[j].y - p[j].x * p[i].y;
        }
        area = Mathf.Abs(area) * 0.5f;
        float wr = WorldRadius;
        float rest = Mathf.PI * wr * wr;
        return rest > 0.0001f ? area / rest : 1f;
    }

    // Layers stack in front of whatever sorting order YSortEntity assigned to the
    // (empty) SpriteRenderer on the root, so the blob sorts against every other
    // enemy for free.
    private void SyncSorting()
    {
        int baseOrder = 1000, layerId = 0;
        if (sortSource != null)
        {
            baseOrder = sortSource.sortingOrder;
            layerId = sortSource.sortingLayerID;
        }
        SetOrder(glowMR, layerId, baseOrder + 0);
        SetOrder(bodyMR, layerId, baseOrder + 1);
        SetOrder(nucleusMR, layerId, baseOrder + 2);
        SetOrder(cellMR, layerId, baseOrder + 3);
        SetOrder(hlMR, layerId, baseOrder + 4);
    }

    private static void SetOrder(MeshRenderer mr, int layerId, int order)
    {
        if (mr == null) return;
        mr.sortingLayerID = layerId;
        mr.sortingOrder = order;
    }

    // PUBLIC JUICE API

    /// Kicks the membrane. Points facing 'dir' take the full impulse, the far side
    /// almost none — a hit from the left dents the left flank, and the pressure term
    /// makes the right flank bulge on its own. World-space direction.
    public void Impulse(Vector2 dir, float strength, float uniform = 0.15f)
    {
        if (!built) EnsureBuilt();
        if (dead) return;
        if (dir.sqrMagnitude < 0.0001f) dir = Random.insideUnitCircle.normalized;
        dir.Normalize();

        Vector2 center = MembraneCenter;
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 outward = (p[i] - center).normalized;
            float w = Mathf.Max(0f, Vector2.Dot(outward, dir));
            v[i] += dir * (strength * (w + uniform));
        }
    }

    /// Radial pulse — inflate (positive) or implode (negative).
    public void Pulse(float strength)
    {
        if (!built) EnsureBuilt();
        if (dead) return;
        Vector2 center = MembraneCenter;
        for (int i = 0; i < pointCount; i++)
            v[i] += (p[i] - center).normalized * strength;
    }

    /// Volume-preserving squash and stretch along a world axis. Positive = stretch
    /// along the axis (and pinch across it); negative = squash along it.
    /// Deforms the anchors, so the membrane physically chases the shape.
    public void Stretch(Vector2 worldAxis, float amount)
    {
        if (!built) EnsureBuilt();
        if (dead || worldAxis.sqrMagnitude < 0.0001f) return;

        Vector3 local = transform.InverseTransformDirection(worldAxis.normalized);
        stretchDirLocal = new Vector2(local.x, local.y).normalized;
        stretchAmount = Mathf.Clamp(amount, -0.6f, 0.9f);
    }

    public void Flash() { flashT = flashDuration; }

    public Vector2[] SampleMembrane() => (Vector2[])p.Clone();

    /// Tears the CURRENT membrane silhouette into wedges of goo and hands them to a
    /// standalone VFX object (this GameObject is about to be destroyed). Because the
    /// wedges are cut from the live spline, the debris matches whatever shape the
    /// blob happened to be in when it died — mid-lunge, mid-squash, whatever.
    public void Disintegrate(Vector2 blastDir, float force)
    {
        if (!built) EnsureBuilt();
        if (dead) return;
        dead = true;

        BuildRimSpline();

        BlobShatterVFX.Spawn(
            rim, MembraneCenter, WorldRadius,
            coreColor, midColor, edgeColor, outlineColor, glowColor,
            blastDir, force, shatterWedges, shatterLifetime);

        // Hide ourselves immediately — the VFX has taken over the silhouette.
        if (glowMR != null) glowMR.enabled = false;
        if (bodyMR != null) bodyMR.enabled = false;
        if (nucleusMR != null) nucleusMR.enabled = false;
        if (cellMR != null) cellMR.enabled = false;
        if (hlMR != null) hlMR.enabled = false;
    }
}


// PROCEDURAL SPRITE CACHE
// One soft radial disc, generated once. 128px + smoothstep falloff, so droplets
// never read as pixel mush.
public static class BlobSprites
{
    private static Sprite _softDisc;

    public static Sprite SoftDisc()
    {
        if (_softDisc != null) return _softDisc;

        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var px = new Color32[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (S * 0.5f);
                float a = 1f - Mathf.SmoothStep(0.62f, 1f, d);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }

        tex.SetPixels32(px);
        tex.Apply();
        _softDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _softDisc;
    }
}


// BLOB DISINTEGRATION
//
// Structured after EnemyDeathVFX: one Update() ticking flat lists of chunks and
// embers, no coroutine per particle. But EnemyDeathVFX itself cannot be reused
// here — it bails on `sprite == null`, and its _pixelCache is keyed on
// sprite.name and never evicted, so a runtime-baked blob sprite would either
// leak a Color[] per death or make every blob shatter as the first one's shape.
//
// Instead the particles are seeded from the blob's LIVE membrane polygon:
//
//   1. grid-sample the membrane's bounding box
//   2. keep cells whose centre passes a point-in-polygon test against the spline
//   3. each surviving cell becomes a goo particle, tinted by its radial depth
//
// So the debris cloud is the exact silhouette the blob died in — kill it
// mid-lunge and the cloud is elongated. On top of that: a few large mesh wedges
// that relax into droplets under surface tension, rising embers, a shockwave,
// and a stain that outlives everything.
public class BlobShatterVFX : MonoBehaviour
{
    private const int StainOrder = 4900;
    private const int GlowOrder = 5100;
    private const int WedgeOrder = 5300;
    private const int ChunkOrder = 5350;
    private const int EmberOrder = 5450;

    // Mesh shard of goo. Relaxes toward an equal-area circle as it flies —
    // surface tension pulling torn goo back into a droplet.
    private class Wedge
    {
        public Transform t;
        public Mesh mesh;
        public Vector3[] torn, round, work;
        public Color[] cols;
        public Vector2 vel;
        public float spin, landY, delay;
    }

    // Small particle cut from the blob's interior.
    private class Chunk
    {
        public Transform t;
        public SpriteRenderer sr;
        public Vector2 vel;
        public float spin, delay, life, grav, age;
        public Vector3 s0;
        public Color c0;
    }

    // Bright mote that rises and cools instead of falling.
    private class Ember
    {
        public Transform t;
        public SpriteRenderer sr;
        public Vector2 vel;
        public float grav, delay, life, age, s0;
        public Color c0, c1;
    }

    private readonly List<Wedge> wedges = new List<Wedge>();
    private readonly List<Chunk> chunks = new List<Chunk>();
    private readonly List<Ember> embers = new List<Ember>();

    private float life, age;

    private static Material _mat;
    private static Material Mat()
    {
        if (_mat == null) _mat = new Material(Shader.Find("Sprites/Default")) { name = "BlobShatterMat" };
        return _mat;
    }

    public static void Spawn(Vector2[] rim, Vector2 center, float worldRadius,
                             Color core, Color mid, Color edge, Color outline, Color glow,
                             Vector2 blastDir, float force, int wedgeCount, float lifetime)
    {
        if (rim == null || rim.Length < 6) return;

        var go = new GameObject("BlobShatterVFX");
        go.transform.position = center;
        go.AddComponent<BlobShatterVFX>()
          .Build(rim, center, worldRadius, core, mid, edge, outline, glow,
                 blastDir, force, Mathf.Clamp(wedgeCount, 0, 16), Mathf.Max(0.3f, lifetime));
    }

    private void Build(Vector2[] rim, Vector2 center, float worldRadius,
                       Color core, Color mid, Color edge, Color outline, Color glow,
                       Vector2 blastDir, float force, int wedgeCount, float lifetime)
    {
        life = lifetime + 0.5f;   // outlive the longest chunk

        if (blastDir.sqrMagnitude < 0.0001f) blastDir = Vector2.up;
        blastDir.Normalize();

        BuildParticles(rim, center, worldRadius, core, mid, edge, blastDir, force);
        BuildWedges(rim, center, worldRadius, core, mid, edge, blastDir, force, wedgeCount);
        BuildEmbers(center, worldRadius, core, glow, force);
        BuildFlash(center, worldRadius, core, glow);
        BuildStain(center, worldRadius, outline, lifetime);
    }

    // PARTICLES — the silhouette, diced

    private void BuildParticles(Vector2[] rim, Vector2 center, float r,
                                Color core, Color mid, Color edge,
                                Vector2 blastDir, float force)
    {
        // Resolution scales with size, so a tiny gen-2 blob doesn't emit 60 motes.
        int grid = Mathf.Clamp(Mathf.RoundToInt(r * 16f), 4, 11);
        float cell = (r * 2f) / grid;

        for (int gy = 0; gy < grid; gy++)
            for (int gx = 0; gx < grid; gx++)
            {
                Vector2 pt = center + new Vector2(
                    -r + (gx + 0.5f) * cell,
                    -r + (gy + 0.5f) * cell);

                if (!InPolygon(pt, rim)) continue;   // exact membrane shape

                var go = new GameObject("Goo");
                go.transform.SetParent(transform, false);
                go.transform.position = pt;

                float size = cell * Random.Range(0.75f, 1.25f);
                go.transform.localScale = Vector3.one * size;
                go.transform.rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = BlobSprites.SoftDisc();
                sr.sortingOrder = ChunkOrder + Random.Range(0, 4);

                // Depth-tinted: particles from the core are bright, the rim is dark.
                float depth = Mathf.Clamp01(Vector2.Distance(pt, center) / Mathf.Max(0.001f, r));
                sr.color = depth < 0.55f
                    ? Color.Lerp(core, mid, depth / 0.55f)
                    : Color.Lerp(mid, edge, (depth - 0.55f) / 0.45f);

                Vector2 outward = pt - center;
                if (outward.sqrMagnitude < 0.0001f) outward = Random.insideUnitCircle;
                outward = (outward.normalized * 0.65f + Random.insideUnitCircle * 0.35f).normalized;

                chunks.Add(new Chunk
                {
                    t = go.transform,
                    sr = sr,
                    // Deeper particles are launched harder — the core detonates.
                    vel = (outward * Random.Range(1.4f, 4.2f) * (1.35f - depth * 0.5f)
                           + blastDir * 1.1f) * (force * 0.32f),
                    spin = Random.Range(-320f, 320f),
                    delay = Random.Range(0f, 0.10f) + depth * 0.05f,   // rim leaves first
                    life = Random.Range(0.55f, 1.15f),
                    grav = Random.Range(-3.2f, -0.6f),
                    s0 = go.transform.localScale,
                    c0 = sr.color
                });
            }
    }

    // Even-odd ray cast. The membrane is a closed CCW polygon, so this is exact.
    private static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    // WEDGES — large goo shards with surface tension

    private void BuildWedges(Vector2[] rim, Vector2 center, float worldRadius,
                             Color core, Color mid, Color edge,
                             Vector2 blastDir, float force, int wedgeCount)
    {
        if (wedgeCount <= 0) return;

        int N = rim.Length;
        int per = Mathf.Max(2, N / wedgeCount);

        for (int w = 0; w < wedgeCount; w++)
        {
            int start = w * per;
            int count = (w == wedgeCount - 1) ? (N - start) : per;
            if (count < 2) continue;

            int vc = count + 2;
            var world = new Vector3[vc];
            world[0] = center;
            for (int i = 0; i <= count; i++) world[i + 1] = rim[(start + i) % N];

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < vc; i++) centroid += world[i];
            centroid /= vc;

            var go = new GameObject($"Shard{w}");
            go.transform.SetParent(transform, false);
            go.transform.position = centroid;

            var wd = new Wedge { t = go.transform };

            wd.torn = new Vector3[vc];
            for (int i = 0; i < vc; i++) wd.torn[i] = world[i] - centroid;

            // Equal-area circle: what surface tension will pull this scrap into.
            float area = PolyArea(wd.torn);
            float rr = Mathf.Sqrt(Mathf.Max(0.0001f, area) / Mathf.PI);
            wd.round = new Vector3[vc];
            wd.round[0] = Vector3.zero;
            for (int i = 1; i < vc; i++)
            {
                float a = (i - 1) / (float)(vc - 2) * Mathf.PI * 2f;
                wd.round[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * rr;
            }

            wd.work = new Vector3[vc];
            wd.cols = new Color[vc];
            wd.cols[0] = core;
            for (int i = 1; i < vc; i++) wd.cols[i] = Color.Lerp(mid, edge, i / (float)vc);

            var tris = new int[(vc - 2) * 3];
            for (int i = 0; i < vc - 2; i++)
            {
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }

            wd.mesh = new Mesh { name = "ShardMesh" };
            wd.mesh.MarkDynamic();
            wd.mesh.vertices = wd.torn;
            wd.mesh.colors = wd.cols;
            wd.mesh.triangles = tris;

            go.AddComponent<MeshFilter>().mesh = wd.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat();
            mr.sortingOrder = WedgeOrder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector2 outward = ((Vector2)centroid - center);
            if (outward.sqrMagnitude < 0.0001f) outward = Random.insideUnitCircle.normalized;
            outward.Normalize();

            wd.vel = (outward * Random.Range(0.7f, 1.5f) + blastDir * 0.75f) * force;
            wd.vel.y += force * 0.35f;
            wd.spin = Random.Range(-420f, 420f);
            wd.landY = centroid.y - worldRadius * Random.Range(0.4f, 1.1f);
            wd.delay = Random.Range(0f, 0.05f);

            wedges.Add(wd);
        }
    }

    private static float PolyArea(Vector3[] v)
    {
        float a = 0f;
        for (int i = 0; i < v.Length; i++)
        {
            int j = (i + 1) % v.Length;
            a += v[i].x * v[j].y - v[j].x * v[i].y;
        }
        return Mathf.Abs(a) * 0.5f;
    }

    // EMBERS — the only things that rise

    private void BuildEmbers(Vector2 center, float r, Color core, Color glow, float force)
    {
        int n = Mathf.RoundToInt(Mathf.Lerp(6f, 16f, Mathf.Clamp01(r * 1.6f)));
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Ember");
            go.transform.SetParent(transform, false);
            go.transform.position = center + Random.insideUnitCircle * (r * 0.7f);

            float s = r * Random.Range(0.05f, 0.13f);
            go.transform.localScale = Vector3.one * s;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BlobSprites.SoftDisc();
            sr.sortingOrder = EmberOrder;

            Color hot = Color.Lerp(core, Color.white, 0.45f); hot.a = 0.9f;
            Color cool = glow; cool.a = 0f;
            sr.color = hot;

            embers.Add(new Ember
            {
                t = go.transform,
                sr = sr,
                vel = Random.insideUnitCircle * (force * 0.25f) + Vector2.up * (force * 0.3f),
                grav = Random.Range(0.6f, 1.8f),      // positive: they float up
                delay = Random.Range(0f, 0.25f),
                life = Random.Range(0.7f, 1.4f),
                s0 = s,
                c0 = hot,
                c1 = cool
            });
        }
    }

    private void BuildFlash(Vector2 center, float r, Color core, Color glow)
    {
        var go = new GameObject("Flash");
        go.transform.SetParent(transform, false);
        go.transform.position = center;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = BlobSprites.SoftDisc();
        sr.sortingOrder = GlowOrder;
        Color c = Color.Lerp(core, glow, 0.35f); c.a = 0.85f;
        sr.color = c;

        StartCoroutine(ExpandFade(go.transform, sr, r * 0.8f, r * 3.0f, 0.28f));
    }

    private void BuildStain(Vector2 center, float r, Color outline, float lifetime)
    {
        var go = new GameObject("Stain");
        go.transform.SetParent(transform, false);
        go.transform.position = (Vector3)center + Vector3.down * (r * 0.35f);
        go.transform.localScale = new Vector3(r * 2.2f, r * 1.1f, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = BlobSprites.SoftDisc();
        sr.sortingOrder = StainOrder;
        Color c = outline; c.a = 0.5f;
        sr.color = c;

        StartCoroutine(FadeOut(sr, lifetime * 0.9f, lifetime * 0.15f));
    }

    // TICK — one loop, like EnemyDeathVFX

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;

        TickWedges(dt);
        TickChunks(dt);
        TickEmbers(dt);

        if (age >= life) Destroy(gameObject);
    }

    private void TickWedges(float dt)
    {
        // Surface tension: sqrt easing, so scraps round off fast then settle.
        float tension = Mathf.Sqrt(Mathf.Clamp01(age / (life * 0.5f)));
        float k = Mathf.Clamp01(age / life);
        const float gravity = -13f;

        for (int i = 0; i < wedges.Count; i++)
        {
            var w = wedges[i];
            if (w.t == null) continue;
            if (w.delay > 0f) { w.delay -= dt; continue; }

            bool landed = w.t.position.y <= w.landY;
            if (!landed)
            {
                w.vel.y += gravity * dt;
                w.t.position += (Vector3)(w.vel * dt);
                w.t.Rotate(0f, 0f, w.spin * dt);
            }
            else
            {
                // Goo doesn't bounce. It slumps.
                w.vel *= 0.82f;
                w.vel.y = 0f;
                w.t.position += (Vector3)(w.vel * dt);
                w.spin = Mathf.Lerp(w.spin, 0f, dt * 8f);
            }

            float squish = landed
                ? Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((age - life * 0.3f) / (life * 0.5f)))
                : 1f;

            for (int j = 0; j < w.work.Length; j++)
            {
                Vector3 shape = Vector3.Lerp(w.torn[j], w.round[j], tension);
                shape.y *= squish;
                w.work[j] = shape * (1f - 0.35f * k);
            }
            w.mesh.vertices = w.work;

            float alpha = 1f - Mathf.SmoothStep(0.55f, 1f, k);
            for (int j = 0; j < w.cols.Length; j++)
            {
                Color c = w.cols[j]; c.a = alpha; w.cols[j] = c;
            }
            w.mesh.colors = w.cols;
            w.mesh.RecalculateBounds();
        }
    }

    private void TickChunks(float dt)
    {
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            var c = chunks[i];
            if (c.t == null) { chunks.RemoveAt(i); continue; }

            if (c.delay > 0f) { c.delay -= dt; continue; }

            c.age += dt;
            if (c.age >= c.life) { Destroy(c.t.gameObject); chunks.RemoveAt(i); continue; }

            c.vel.y += c.grav * dt;
            c.t.position += (Vector3)(c.vel * dt);
            c.t.Rotate(0f, 0f, c.spin * dt);

            float k = c.age / c.life;
            // Shrink and smear along travel — goo, not gravel.
            float stretch = 1f + Mathf.Clamp01(c.vel.magnitude * 0.06f);
            float shrink = 1f - 0.75f * k * k;
            c.t.localScale = new Vector3(c.s0.x / stretch, c.s0.y * stretch, 1f) * shrink;

            Color col = c.c0;
            col.a = c.c0.a * (1f - Mathf.SmoothStep(0.45f, 1f, k));
            c.sr.color = col;
        }
    }

    private void TickEmbers(float dt)
    {
        for (int i = embers.Count - 1; i >= 0; i--)
        {
            var e = embers[i];
            if (e.t == null) { embers.RemoveAt(i); continue; }

            if (e.delay > 0f) { e.delay -= dt; continue; }

            e.age += dt;
            if (e.age >= e.life) { Destroy(e.t.gameObject); embers.RemoveAt(i); continue; }

            e.vel.y += e.grav * dt;           // rises
            e.vel *= 0.985f;                  // and slows in the air
            e.t.position += (Vector3)(e.vel * dt);

            float k = e.age / e.life;
            e.t.localScale = Vector3.one * (e.s0 * (1f - 0.6f * k));
            e.sr.color = Color.Lerp(e.c0, e.c1, k * k);
        }
    }

    private IEnumerator ExpandFade(Transform t, SpriteRenderer sr, float from, float to, float dur)
    {
        float e = 0f;
        Color baseCol = sr.color;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / dur);
            float eased = 1f - (1f - k) * (1f - k);
            if (t != null) t.localScale = Vector3.one * Mathf.Lerp(from, to, eased);
            if (sr != null) { Color c = baseCol; c.a = baseCol.a * (1f - k); sr.color = c; }
            yield return null;
        }
        if (sr != null) { Color c = sr.color; c.a = 0f; sr.color = c; }
    }

    private IEnumerator FadeOut(SpriteRenderer sr, float dur, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float e = 0f;
        Color baseCol = sr != null ? sr.color : Color.clear;
        while (e < dur && sr != null)
        {
            e += Time.deltaTime;
            Color c = baseCol; c.a = baseCol.a * (1f - Mathf.Clamp01(e / dur));
            sr.color = c;
            yield return null;
        }
    }
}


// IMPACT SPLATTER
// A short directional spray for the moment a lunge connects. Cone of droplets
// along the blow, plus a hard bright pop at the contact point.
public class BlobImpactVFX : MonoBehaviour
{
    private const int Order = 5500;

    public static void Spawn(Vector3 at, Vector2 dir, float radius, Color core, Color edge)
    {
        var go = new GameObject("BlobImpactVFX");
        go.transform.position = at;
        go.AddComponent<BlobImpactVFX>().Play(dir, Mathf.Max(0.1f, radius), core, edge);
    }

    private void Play(Vector2 dir, float radius, Color core, Color edge)
    {
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        dir.Normalize();

        // Bright contact pop.
        var flash = new GameObject("Pop");
        flash.transform.SetParent(transform, false);
        var fsr = flash.AddComponent<SpriteRenderer>();
        fsr.sprite = BlobSprites.SoftDisc();
        fsr.sortingOrder = Order;
        Color hot = Color.Lerp(core, Color.white, 0.55f); hot.a = 0.9f;
        fsr.color = hot;
        StartCoroutine(Pop(flash.transform, fsr, radius * 0.35f, radius * 1.4f, 0.16f));

        // Cone of goo along the blow.
        int n = 7;
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Spray");
            go.transform.SetParent(transform, false);
            float size = radius * Random.Range(0.10f, 0.22f);
            go.transform.localScale = Vector3.one * size;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BlobSprites.SoftDisc();
            sr.sortingOrder = Order;
            sr.color = Random.value < 0.5f ? core : edge;

            float spread = Random.Range(-38f, 38f) * Mathf.Deg2Rad;
            Vector2 d = new Vector2(
                dir.x * Mathf.Cos(spread) - dir.y * Mathf.Sin(spread),
                dir.x * Mathf.Sin(spread) + dir.y * Mathf.Cos(spread));

            StartCoroutine(Fly(go.transform, sr, d * (radius * Random.Range(4f, 8f))));
        }

        Destroy(gameObject, 0.7f);
    }

    private IEnumerator Pop(Transform t, SpriteRenderer sr, float from, float to, float dur)
    {
        float e = 0f;
        Color b = sr.color;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / dur);
            t.localScale = Vector3.one * Mathf.Lerp(from, to, 1f - (1f - k) * (1f - k));
            Color c = b; c.a = b.a * (1f - k); sr.color = c;
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    private IEnumerator Fly(Transform t, SpriteRenderer sr, Vector2 vel)
    {
        float lifeT = Random.Range(0.18f, 0.34f);
        float e = 0f;
        Color b = sr.color;
        Vector3 s0 = t.localScale;
        while (e < lifeT)
        {
            float dt = Time.deltaTime;
            e += dt;
            vel *= 0.90f;                               // goo doesn't carry far
            t.position += (Vector3)(vel * dt);
            float k = Mathf.Clamp01(e / lifeT);
            t.localScale = s0 * (1f - 0.6f * k);
            Color c = b; c.a = b.a * (1f - k); sr.color = c;
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }
}

