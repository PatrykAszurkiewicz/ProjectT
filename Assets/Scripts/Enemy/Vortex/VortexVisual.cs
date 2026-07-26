using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// PROCEDURAL BLACK-HOLE VISUAL
// Five generated mesh layers plus a swarm of infalling particles. No sprites, no
// spritesheets, no shaders, no particle system.
//   glow          soft halo bleeding past the disk
//   accretionDisk turbulent annulus, the star of the show
//   photonRing    thin hot ring hugging the horizon
//   eventHorizon  black disc that eats everything behind it
//   particles     matter spiralling in, obeying angular momentum

[DisallowMultipleComponent]
public class VortexVisual : MonoBehaviour
{
    [Header("Geometry")]
    [Tooltip("Radius of the black event horizon, in world units.")]
    [SerializeField] private float horizonRadius = 0.55f;

    [Tooltip("Outer edge of the accretion disk. Enemies are birthed from around " +
             "here, so it doubles as the visual spawn ring.")]
    [SerializeField] private float diskRadius = 2.2f;

    [Tooltip("Angular resolution. 96 is smooth at any sane zoom.")]
    [Range(24, 192)][SerializeField] private int segments = 96;

    [Tooltip("Vertical squash. 1 = a flat ring seen from straight above. 0.5 = a " +
             "tilted disk seen at an angle. This is what gives it depth in a 2D game.")]
    [Range(0.15f, 1f)][SerializeField] private float verticalSquash = 0.55f;

    [Header("Rotation & Turbulence")]
    [Tooltip("Disk angular speed, radians/sec. Negative reverses the spin.")]
    [SerializeField] private float rotationSpeed = 1.6f;

    [Tooltip("Number of hot spiral arms sweeping around the disk.")]
    [Range(1, 8)][SerializeField] private int armCount = 3;

    [Tooltip("How sharply the arms stand out against the background disk. 0 = a " +
             "uniform ring, 1 = hard stripes.")]
    [Range(0f, 1f)][SerializeField] private float armContrast = 0.55f;

    [Tooltip("Perlin churn on the disk's outer edge. 0 = a perfect circle.")]
    [Range(0f, 0.35f)][SerializeField] private float turbulence = 0.12f;

    [SerializeField] private float turbulenceSpeed = 0.8f;

    [Header("Relativistic Beaming")]
    [Tooltip("How much brighter the approaching limb is than the receding one. " +
             "This is the detail that makes it read as a black hole. 0 kills it.")]
    [Range(0f, 1f)][SerializeField] private float dopplerStrength = 0.55f;

    [Tooltip("Which side is approaching, in degrees. 0 = right, 90 = up.")]
    [SerializeField] private float dopplerAngle = 180f;

    [Header("Palette")]
    [SerializeField] private Color horizonColor = new Color(0.02f, 0.00f, 0.04f, 1f);
    [SerializeField] private Color photonColor = new Color(1.00f, 0.85f, 1.00f, 0.95f);
    [SerializeField] private Color diskInnerColor = new Color(1.00f, 0.72f, 1.00f, 1f);
    [SerializeField] private Color diskMidColor = new Color(0.72f, 0.28f, 0.95f, 0.9f);
    [SerializeField] private Color diskOuterColor = new Color(0.30f, 0.08f, 0.50f, 0.5f);
    [SerializeField] private Color glowColor = new Color(0.55f, 0.20f, 0.85f, 0.28f);

    [Tooltip("Tint added to the approaching limb — a nod to relativistic blueshift.")]
    [SerializeField] private Color blueshiftTint = new Color(0.75f, 0.85f, 1.00f, 1f);

    [Range(0f, 1.5f)][SerializeField] private float glowWidth = 0.7f;

    [Header("Infalling Matter")]
    [Range(0, 48)][SerializeField] private int particleCount = 24;

    [Tooltip("How fast matter falls inward, world units/sec at the disk edge. " +
             "It accelerates on its own as it approaches — that's the physics.")]
    [SerializeField] private float infallSpeed = 0.55f;

    [Tooltip("Angular momentum constant. Orbital speed is this divided by radius, " +
             "so particles whip around near the horizon.")]
    [SerializeField] private float angularMomentum = 2.2f;

    [SerializeField] private float particleSize = 0.10f;

    [Header("Sorting")]
    [Tooltip("The grass bakes at sortingOrder 1000 + round(-y*10) on the Default " +
             "sorting layer (see GrassCartoonOverlay). The disk self-sorts in that " +
             "same space so it layers correctly against the field, then biases UP by " +
             "aboveGrassOffset so a levitating disk clears the blades around its base " +
             "instead of being sliced by them. The vortex has no sprite of its own, so " +
             "there is nothing else to drive this.")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int grassSortBase = 1000;
    [SerializeField] private float grassSortPrecision = 10f;
    [Tooltip("How far above the grass at the vortex's own Y-line to draw. ~30 keeps " +
             "the whole disk and its glow in front of nearby grass. Raise if the very " +
             "bottom of the glow still clips into tall blades.")]
    [SerializeField] private int aboveGrassOffset = 30;

    [Header("Levitation")]
    [Tooltip("The rift hangs in the air and drifts, so it should never sit flat on " +
             "the ground. Vertical bob height in world units. 0 disables the bob.")]
    [SerializeField] private float bobAmplitude = 0.12f;
    [Tooltip("Bob speed. Higher = quicker up/down.")]
    [SerializeField] private float bobSpeed = 1.6f;
    [Tooltip("Gentle lateral drift so the hover doesn't feel locked to an axis. " +
             "0 = pure vertical bob.")]
    [SerializeField] private float swayAmplitude = 0.05f;
    [SerializeField] private float swaySpeed = 0.6f;

    private Mesh glowMesh, diskMesh, photonMesh, horizonMesh;
    private MeshRenderer glowMR, diskMR, photonMR, horizonMR;
    private Vector3[] vGlow, vDisk, vPhoton, vHorizon;
    private Color[] cGlow, cDisk, cPhoton, cHorizon;

    private struct Particle { public float angle, radius, size, seed; }
    private Particle[] particles;
    private Transform[] particleT;
    private SpriteRenderer[] particleSR;

    private static Material _sharedMat;
    private SpriteRenderer sortSource;
    private Transform visualRoot;   // all render layers hang here so we can bob it
    private int grassSortLayerId;

    private float spin, seed, flare, intensity = 1f;

    // Spit pulse: winds down (implode) then punches out past resting size, then
    // settles. Drives the whole disk geometry via rh/rd in Sync(). 't' walks 0→1.
    private float spitT = -1f;
    private float spitDuration = 0.5f;
    private float spitAmount = 0.35f;
    private bool built, collapsed;

    public float DiskRadius => diskRadius;
    public float HorizonRadius => horizonRadius;
    public Color DiskInnerColor => diskInnerColor;
    public Color DiskOuterColor => diskOuterColor;

    private void Start() { EnsureBuilt(); }

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        seed = Random.value * 100f;
        segments = Mathf.Max(24, segments);

        if (_sharedMat == null)
            _sharedMat = new Material(Shader.Find("Sprites/Default")) { name = "VortexVertexColor" };

        sortSource = GetComponentInParent<SpriteRenderer>();
        grassSortLayerId = SortingLayer.NameToID(sortingLayerName);

        // Everything the vortex draws hangs off this child so the whole disk can
        // levitate together without moving the root (which owns the collider, the
        // spawn ring and the Y-sort position).
        var vr = new GameObject("VortexVisualRoot");
        vr.transform.SetParent(transform, false);
        vr.transform.localPosition = Vector3.zero;
        visualRoot = vr.transform;

        BuildAnnulus("VortexGlow", 2, out glowMesh, out glowMR, out vGlow, out cGlow);
        BuildAnnulus("VortexDisk", 4, out diskMesh, out diskMR, out vDisk, out cDisk);
        BuildAnnulus("VortexPhotonRing", 2, out photonMesh, out photonMR, out vPhoton, out cPhoton);
        BuildFan("VortexHorizon", out horizonMesh, out horizonMR, out vHorizon, out cHorizon);

        BuildParticles();
        Sync();
    }

    // MESH CONSTRUCTION

    // 'rings' concentric loops, quad-stripped together. Radii and colours are
    // rewritten every frame; only the topology is fixed.
    private void BuildAnnulus(string layerName, int rings, out Mesh mesh, out MeshRenderer mr,
                              out Vector3[] verts, out Color[] cols)
    {
        var go = NewLayer(layerName, out var mf, out mr);

        int N = segments;
        verts = new Vector3[rings * N];
        cols = new Color[rings * N];

        var tris = new List<int>((rings - 1) * N * 6);
        for (int r = 0; r < rings - 1; r++)
            for (int j = 0; j < N; j++)
            {
                int jn = (j + 1) % N;
                int a = r * N + j, b = r * N + jn;
                int c = (r + 1) * N + j, d = (r + 1) * N + jn;
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(b);
            }

        mesh = new Mesh { name = layerName + "Mesh" };
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.triangles = tris.ToArray();
        mf.mesh = mesh;
    }

    private void BuildFan(string layerName, out Mesh mesh, out MeshRenderer mr,
                          out Vector3[] verts, out Color[] cols)
    {
        var go = NewLayer(layerName, out var mf, out mr);

        int N = segments;
        verts = new Vector3[N + 1];
        cols = new Color[N + 1];

        var tris = new int[N * 3];
        for (int j = 0; j < N; j++)
        {
            tris[j * 3 + 0] = 0;
            tris[j * 3 + 1] = j + 1;
            tris[j * 3 + 2] = (j + 1) % N + 1;
        }

        mesh = new Mesh { name = layerName + "Mesh" };
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.triangles = tris;
        mf.mesh = mesh;
    }

    private GameObject NewLayer(string layerName, out MeshFilter mf, out MeshRenderer mr)
    {
        var go = new GameObject(layerName);
        go.transform.SetParent(visualRoot, false);
        mf = go.AddComponent<MeshFilter>();
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _sharedMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    private void BuildParticles()
    {
        particles = new Particle[Mathf.Max(0, particleCount)];
        particleT = new Transform[particles.Length];
        particleSR = new SpriteRenderer[particles.Length];

        for (int i = 0; i < particles.Length; i++)
        {
            var go = new GameObject($"Infall{i}");
            go.transform.SetParent(visualRoot, false);
            particleT[i] = go.transform;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BlobSprites.SoftDisc();   // reuses the blob's cached disc
            particleSR[i] = sr;

            RespawnParticle(i, Random.Range(horizonRadius * 1.2f, diskRadius * 1.15f));
        }
    }

    private void RespawnParticle(int i, float startRadius = -1f)
    {
        particles[i] = new Particle
        {
            angle = Random.Range(0f, Mathf.PI * 2f),
            radius = startRadius > 0f ? startRadius : diskRadius * Random.Range(0.95f, 1.25f),
            size = Random.Range(0.6f, 1.5f),
            seed = Random.value * 10f
        };
    }

    // FRAME

    private void Update()
    {
        if (!built) EnsureBuilt();
        if (collapsed) return;

        UpdateLevitation();

        spin += rotationSpeed * Time.deltaTime;
        flare = Mathf.Lerp(flare, 0f, Time.deltaTime * 3.5f);

        // Advance the spit pulse. The disk also whirls faster while spitting — it's
        // straining to expel the enemy.
        if (spitT >= 0f)
        {
            spitT += Time.deltaTime / Mathf.Max(0.01f, spitDuration);
            if (spitT >= 1f) spitT = -1f;
            else spin += rotationSpeed * 2.5f * SpitSpin() * Time.deltaTime;
        }

        TickParticles(Time.deltaTime);
        Sync();
        SyncSorting();
    }

    // A slow hover: vertical bob plus a whisper of lateral drift, so the rift reads
    // as hanging in the air rather than painted on the ground. Applied to visualRoot
    // only — the root transform (collider, spawn ring, sort position) stays put, so
    // gameplay is unaffected.
    private void UpdateLevitation()
    {
        if (visualRoot == null) return;
        float t = Time.time;
        float y = bobAmplitude != 0f ? Mathf.Sin(t * bobSpeed + seed) * bobAmplitude : 0f;
        float x = swayAmplitude != 0f
            ? (Mathf.PerlinNoise(seed, t * swaySpeed) - 0.5f) * 2f * swayAmplitude
            : 0f;
        visualRoot.localPosition = new Vector3(x, y, 0f);
    }

    // Scale offset applied to the whole disk during a spit. Negative early
    // (implosion / anticipation), a sharp positive punch at release, then settle.
    // Returns 0 when no spit is active.
    private float SpitCurve()
    {
        if (spitT < 0f) return 0f;
        float t = Mathf.Clamp01(spitT);
        // Anticipation: dip to -1 around t=0.3. Release: overshoot to +1 near t=0.55.
        // Settle: decay back to 0 by t=1.
        float anticipation = -Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI) * 0.6f;
        float release = 0f;
        if (t > 0.35f)
        {
            float rt = (t - 0.35f) / 0.65f;                 // 0→1 over the release
            release = Mathf.Sin(rt * Mathf.PI) * (1f - rt); // punch then settle
        }
        return anticipation + release * 1.6f;
    }

    // Extra spin weight, strongest during the wind-up.
    private float SpitSpin()
    {
        if (spitT < 0f) return 0f;
        return Mathf.Sin(Mathf.Clamp01(spitT / 0.4f) * Mathf.PI);
    }

    private void TickParticles(float dt)
    {
        for (int i = 0; i < particles.Length; i++)
        {
            var p = particles[i];

            // Angular momentum is conserved: ω = L / r. As r shrinks, the particle
            // whips around faster. This is why the infall looks violent rather than
            // like something easing toward a point.
            float w = angularMomentum / Mathf.Max(0.12f, p.radius);
            p.angle += w * dt * Mathf.Sign(rotationSpeed == 0f ? 1f : rotationSpeed);

            // Infall accelerates as it approaches the horizon.
            float accel = 1f + 1.6f * Mathf.Clamp01(1f - p.radius / diskRadius);
            p.radius -= infallSpeed * accel * dt;

            if (p.radius <= horizonRadius * 0.85f)
            {
                RespawnParticle(i);
                p = particles[i];
            }

            particles[i] = p;

            float t = Mathf.InverseLerp(diskRadius, horizonRadius, p.radius); // 0 out → 1 in
            Vector3 pos = new Vector3(Mathf.Cos(p.angle), Mathf.Sin(p.angle) * verticalSquash, 0f) * p.radius;

            var tr = particleT[i];
            tr.localPosition = pos;

            // Stretch tangentially — a smear along the orbit, not a dot.
            float smear = 1f + 2.2f * t;
            float s = particleSize * p.size * (1f - 0.35f * t);
            tr.localScale = new Vector3(s * smear, s / smear, 1f);
            tr.localRotation = Quaternion.AngleAxis(p.angle * Mathf.Rad2Deg + 90f, Vector3.forward);

            // Hotter and brighter the deeper it falls; fades in at the outer edge.
            Color c = Color.Lerp(diskOuterColor, diskInnerColor, t);
            c = Color.Lerp(c, Color.white, t * t * 0.6f);
            float edgeFade = Mathf.Clamp01((diskRadius * 1.25f - p.radius) * 2f);
            c.a = Mathf.Clamp01((0.35f + 0.65f * t) * edgeFade * intensity);
            particleSR[i].color = c;
        }
    }

    private void Sync()
    {
        int N = segments;
        float spit = SpitCurve() * spitAmount;
        float rh = horizonRadius * (1f + 0.12f * flare + spit);
        float rd = diskRadius * (1f + 0.08f * flare + spit);

        Vector2 dop = new Vector2(Mathf.Cos(dopplerAngle * Mathf.Deg2Rad),
                                  Mathf.Sin(dopplerAngle * Mathf.Deg2Rad));

        // --- event horizon: pure black, slightly restless ---
        vHorizon[0] = Vector3.zero;
        cHorizon[0] = horizonColor;
        for (int j = 0; j < N; j++)
        {
            float a = j / (float)N * Mathf.PI * 2f;
            float wob = 1f + 0.02f * Mathf.Sin(a * 5f + spin * 2f);
            vHorizon[j + 1] = Polar(a, rh * wob);
            cHorizon[j + 1] = horizonColor;
        }
        Push(horizonMesh, vHorizon, cHorizon);

        // --- photon ring: a thin hot lip clinging to the horizon ---
        for (int j = 0; j < N; j++)
        {
            float a = j / (float)N * Mathf.PI * 2f;
            float beam = 1f + dopplerStrength * Mathf.Cos(a - Mathf.Atan2(dop.y, dop.x));

            Color hot = photonColor;
            hot = Color.Lerp(hot, blueshiftTint, Mathf.Clamp01((beam - 1f) * 0.6f));
            hot.a = Mathf.Clamp01(photonColor.a * beam * 0.7f * intensity + flare * 0.4f);

            vPhoton[j] = Polar(a, rh * 1.01f);
            cPhoton[j] = hot;

            Color fade = hot; fade.a = 0f;
            vPhoton[N + j] = Polar(a, rh * 1.22f);
            cPhoton[N + j] = fade;
        }
        Push(photonMesh, vPhoton, cPhoton);

        // --- accretion disk: 4 rings, arms, turbulence, Doppler ---
        float tNoise = Time.time * turbulenceSpeed + seed;
        float rInner = rh * 1.24f;

        for (int j = 0; j < N; j++)
        {
            float a = j / (float)N * Mathf.PI * 2f;

            // Perlin churn on the outer edge only, so the inner lip stays crisp.
            float n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.6f + tNoise, Mathf.Sin(a) * 1.6f) - 0.5f;
            float rOuter = rd * (1f + turbulence * n * 2f);

            // Hot arms sweeping around: brightness = sin(arms·θ − ωt).
            float arm = 0.5f + 0.5f * Mathf.Sin(a * armCount - spin * 2.2f);
            float armK = Mathf.Lerp(1f - armContrast, 1f, arm);

            // Relativistic beaming: the approaching limb is brighter and bluer.
            float beam = 1f + dopplerStrength * Mathf.Cos(a - Mathf.Atan2(dop.y, dop.x));

            float bright = armK * beam * intensity + flare * 0.8f;

            Color c0 = Tint(diskInnerColor, beam, bright);
            Color c1 = Tint(diskMidColor, beam, bright);
            Color c2 = Tint(diskOuterColor, beam, bright * 0.8f);
            Color c3 = c2; c3.a = 0f;

            vDisk[0 * N + j] = Polar(a, rInner); cDisk[0 * N + j] = c0;
            vDisk[1 * N + j] = Polar(a, Mathf.Lerp(rInner, rOuter, 0.35f)); cDisk[1 * N + j] = c1;
            vDisk[2 * N + j] = Polar(a, rOuter); cDisk[2 * N + j] = c2;
            vDisk[3 * N + j] = Polar(a, rOuter * 1.06f); cDisk[3 * N + j] = c3;   // feathered edge
        }
        Push(diskMesh, vDisk, cDisk);

        //  glow halo 
        for (int j = 0; j < N; j++)
        {
            float a = j / (float)N * Mathf.PI * 2f;
            Color g = glowColor;
            g.a = Mathf.Clamp01(glowColor.a * (intensity + flare * 1.5f));
            Color g0 = g; g0.a = 0f;

            vGlow[j] = Polar(a, rd * 1.02f); cGlow[j] = g;
            vGlow[N + j] = Polar(a, rd * (1f + glowWidth)); cGlow[N + j] = g0;
        }
        Push(glowMesh, vGlow, cGlow);
    }

    private Vector3 Polar(float angle, float r)
        => new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * verticalSquash, 0f);

    private Color Tint(Color baseCol, float beam, float bright)
    {
        Color c = Color.Lerp(baseCol, blueshiftTint, Mathf.Clamp01((beam - 1f) * 0.45f));
        c.a = Mathf.Clamp01(baseCol.a * bright);
        return c;
    }

    private static void Push(Mesh m, Vector3[] v, Color[] c)
    {
        m.vertices = v;
        m.colors = c;
        m.RecalculateBounds();
    }

    private void SyncSorting()
    {
        // A real, sprited SpriteRenderer parent (an intentional YSortEntity setup)
        // wins if one exists. The vortex itself is pure mesh with no sprite, and a
        // phantom sprite-less SpriteRenderer (e.g. one auto-added by a Y-sort helper)
        // is deliberately ignored — inheriting its order would drop the disk to ~1000
        // and sink it into the grass, which is the bug this method exists to avoid.
        int layerId, baseOrder;
        if (sortSource != null && sortSource.sprite != null)
        {
            layerId = sortSource.sortingLayerID;
            baseOrder = sortSource.sortingOrder;
        }
        else
        {
            // Self-sort in the grass's own space (base 1000 + round(-y*precision) on
            // the grass layer), then bias UP so the hovering disk clears the blades
            // around its base. Uses the root Y (not the bobbing visualRoot) so the
            // order stays steady while it levitates.
            layerId = grassSortLayerId;
            baseOrder = grassSortBase
                      + Mathf.RoundToInt(-transform.position.y * grassSortPrecision)
                      + aboveGrassOffset;
        }

        SetOrder(glowMR, layerId, baseOrder + 0);
        SetOrder(diskMR, layerId, baseOrder + 1);
        SetOrder(photonMR, layerId, baseOrder + 2);
        SetOrder(horizonMR, layerId, baseOrder + 3);

        // Particles orbit in front of the horizon — close enough for a 2D game.
        for (int i = 0; i < particleSR.Length; i++)
        {
            if (particleSR[i] == null) continue;
            particleSR[i].sortingLayerID = layerId;
            particleSR[i].sortingOrder = baseOrder + 4;
        }
    }

    private static void SetOrder(MeshRenderer mr, int layerId, int order)
    {
        if (mr == null) return;
        mr.sortingLayerID = layerId;
        mr.sortingOrder = order;
    }

    // PUBLIC API

    /// A bright surge — call it when the vortex disgorges a wave.
    public void Flare(float strength = 1f)
    {
        if (!built) EnsureBuilt();
        flare = Mathf.Max(flare, Mathf.Clamp01(strength));
    }

    /// Kicks off the inflate/deflate "spit": the disk winds down, then punches out
    /// past its resting size, then settles. Time the enemy's launch to SpitPeakTime.
    public void Spit(float duration = 0.5f, float amount = 0.35f)
    {
        if (!built) EnsureBuilt();
        spitDuration = Mathf.Max(0.05f, duration);
        spitAmount = Mathf.Max(0f, amount);
        spitT = 0f;
        Flare(0.6f);   // a little brightness on the heave
    }

    /// Seconds from a Spit() call to the moment the disk punches outward — the
    /// instant an enemy should be launched so it rides the expansion.
    public float SpitPeakTime => spitDuration * 0.55f;

    /// Overall brightness, 0-1. Drop it as the vortex loses health for a dying star.
    public void SetIntensity(float value) => intensity = Mathf.Clamp01(value);

    /// A point on the disk edge, world space. Used as the enemy birth position.
    public Vector3 DiskPoint(float angleRad, float radiusScale = 1f)
        => transform.TransformPoint(Polar(angleRad, diskRadius * radiusScale));

    /// Kills the render immediately and hands off to the collapse VFX.
    public void Collapse()
    {
        if (collapsed) return;
        collapsed = true;

        VortexCollapseVFX.Spawn(transform.position, diskRadius, verticalSquash,
                                diskInnerColor, diskOuterColor, glowColor);

        if (glowMR != null) glowMR.enabled = false;
        if (diskMR != null) diskMR.enabled = false;
        if (photonMR != null) photonMR.enabled = false;
        if (horizonMR != null) horizonMR.enabled = false;
        for (int i = 0; i < particleSR.Length; i++)
            if (particleSR[i] != null) particleSR[i].enabled = false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, diskRadius);
        Gizmos.color = new Color(0.1f, 0.05f, 0.15f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, horizonRadius);
    }
#endif
}


// COLLAPSE VFX
public class VortexCollapseVFX : MonoBehaviour
{
    private const int RingOrder = 5250;
    private const int DebrisOrder = 5300;
    private const int CoreOrder = 5400;

    private class Mote
    {
        public Transform t;
        public SpriteRenderer sr;
        public float angle, radius, spin;
        public Color hot, cool;
    }
    private class Debris
    {
        public Transform t;
        public SpriteRenderer sr;
        public Vector2 vel;
        public float spin, life, age;
        public Vector3 s0;
        public Color c0;
    }

    private readonly List<Mote> motes = new List<Mote>();
    private readonly List<Debris> debris = new List<Debris>();

    private float squash, radius;
    private Color inner, outer, glow;

    private SpriteRenderer core, ring, afterglow;
    private float age;

    // Timeline (seconds).
    private const float T_IMPLODE = 0.45f;
    private const float T_SINGULARITY = 0.12f;
    private const float T_DETONATE = 0.55f;
    private const float T_AFTERGLOW = 0.7f;
    private float Total => T_IMPLODE + T_SINGULARITY + T_DETONATE + T_AFTERGLOW;

    private static Material _mat;
    private static Material Mat()
    {
        if (_mat == null) _mat = new Material(Shader.Find("Sprites/Default")) { name = "VortexCollapseMat" };
        return _mat;
    }

    public static void Spawn(Vector3 at, float radius, float squash,
                             Color inner, Color outer, Color glow)
    {
        var go = new GameObject("VortexCollapseVFX");
        go.transform.position = at;
        go.AddComponent<VortexCollapseVFX>().Init(radius, squash, inner, outer, glow);
    }

    private void Init(float radius, float squash, Color inner, Color outer, Color glow)
    {
        this.radius = Mathf.Max(0.2f, radius);
        this.squash = squash;
        this.inner = inner;
        this.outer = outer;
        this.glow = glow;

        BuildMotes();
        BuildCoreAndRing();
    }

    private SpriteRenderer NewSprite(string n, int order, Color c, float scale)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = BlobSprites.SoftDisc();
        sr.sortingOrder = order;
        sr.color = c;
        return sr;
    }

    // The disk's matter, ready to be sucked in.
    private void BuildMotes()
    {
        int n = 26;
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Mote");
            go.transform.SetParent(transform, false);

            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rad = radius * Random.Range(0.5f, 1.25f);
            go.transform.localScale = Vector3.one * (radius * Random.Range(0.10f, 0.20f));

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BlobSprites.SoftDisc();
            sr.sortingOrder = DebrisOrder;

            Color hot = Random.value < 0.5f ? inner : outer;
            motes.Add(new Mote
            {
                t = go.transform,
                sr = sr,
                angle = ang,
                radius = rad,
                spin = 3.2f / Mathf.Max(0.15f, rad),
                hot = hot,
                cool = Color.Lerp(hot, Color.white, 0.85f)
            });
        }
    }

    private void BuildCoreAndRing()
    {
        core = NewSprite("Core", CoreOrder, new Color(1, 1, 1, 0f), radius * 0.3f);
        ring = NewSprite("Shock", RingOrder, new Color(1, 1, 1, 0f), radius * 0.2f);
        afterglow = NewSprite("Afterglow", RingOrder - 1, new Color(glow.r, glow.g, glow.b, 0f), radius * 1.2f);
    }

    private void SpawnDetonationDebris()
    {
        int n = 18;
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(transform, false);

            float sz = radius * Random.Range(0.08f, 0.18f);
            go.transform.localScale = Vector3.one * sz;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BlobSprites.SoftDisc();
            sr.sortingOrder = DebrisOrder;
            Color c = Random.value < 0.4f ? Color.Lerp(inner, Color.white, 0.5f) : outer;
            sr.color = c;

            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * squash);
            debris.Add(new Debris
            {
                t = go.transform,
                sr = sr,
                vel = dir * (radius * Random.Range(6f, 13f)),
                spin = Random.Range(-540f, 540f),
                life = Random.Range(0.4f, 0.75f),
                s0 = go.transform.localScale,
                c0 = c
            });
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;

        float t1 = T_IMPLODE;
        float t2 = t1 + T_SINGULARITY;
        float t3 = t2 + T_DETONATE;

        if (age < t1) PhaseImplode(age / T_IMPLODE);
        else if (age < t2) PhaseSingularity();
        else if (age < t3) PhaseDetonate((age - t2) / T_DETONATE, dt);
        else PhaseAfterglow((age - t3) / T_AFTERGLOW);

        // Debris runs across detonation + afterglow.
        for (int i = debris.Count - 1; i >= 0; i--)
        {
            var d = debris[i];
            if (d.t == null) { debris.RemoveAt(i); continue; }
            d.age += dt;
            if (d.age >= d.life) { Destroy(d.t.gameObject); debris.RemoveAt(i); continue; }
            d.vel *= 0.90f;
            d.t.position += (Vector3)(d.vel * dt);
            d.t.Rotate(0, 0, d.spin * dt);
            float k = d.age / d.life;
            d.t.localScale = d.s0 * (1f - 0.6f * k);
            Color c = d.c0; c.a = 1f - k; d.sr.color = c;
        }

        if (age >= Total && debris.Count == 0) Destroy(gameObject);
    }

    // Matter whips inward, accelerating, brightening to white as it's crushed.
    private void PhaseImplode(float k)
    {
        float ease = k * k;                         // accelerate into the hole
        foreach (var m in motes)
        {
            if (m.t == null) continue;
            m.angle += m.spin * Time.deltaTime * (1f + 3f * k);   // spin up hard
            float r = Mathf.Lerp(m.radius, radius * 0.04f, ease);
            m.t.localPosition = new Vector3(Mathf.Cos(m.angle) * r,
                                            Mathf.Sin(m.angle) * r * squash, 0f);
            m.t.localScale = Vector3.one * (radius * 0.16f * (1f - 0.5f * k));
            m.sr.color = Color.Lerp(m.hot, m.cool, k);
        }
        // Core glows brighter as everything piles in.
        var c = core.color; c.a = k * 0.6f; core.color = c;
        core.transform.localScale = Vector3.one * (radius * Mathf.Lerp(0.3f, 0.5f, k));
    }

    private void PhaseSingularity()
    {
        foreach (var m in motes) if (m.t != null) m.sr.enabled = false;
        var c = core.color; c.a = 0f; core.color = c;      // hard black beat
    }

    // The horizon fails: flash, shockwave, debris.
    private void PhaseDetonate(float k, float dt)
    {
        if (debris.Count == 0 && k < 0.1f)
        {
            SpawnDetonationDebris();
            if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.22f, 0.3f);
        }

        // Core flash: instant white, fading.
        float flash = 1f - k;
        core.color = new Color(1f, 0.95f, 1f, flash);
        core.transform.localScale = Vector3.one * (radius * Mathf.Lerp(0.4f, 2.6f, k));

        // Shockwave ring expands and fades.
        float ringK = 1f - (1f - k) * (1f - k);
        ring.transform.localScale = new Vector3(
            radius * Mathf.Lerp(0.3f, 4.5f, ringK),
            radius * Mathf.Lerp(0.3f, 4.5f, ringK) * squash, 1f);
        Color rc = Color.Lerp(inner, Color.white, 0.6f);
        rc.a = (1f - k) * 0.8f;
        ring.color = rc;
    }

    private void PhaseAfterglow(float k)
    {
        core.color = new Color(1, 1, 1, 0f);
        ring.color = new Color(1, 1, 1, 0f);
        // A fading bruise of light.
        float a = (1f - k) * 0.5f;
        afterglow.color = new Color(glow.r, glow.g, glow.b, a);
        afterglow.transform.localScale = Vector3.one * (radius * Mathf.Lerp(1.2f, 2.2f, k));
    }
}

