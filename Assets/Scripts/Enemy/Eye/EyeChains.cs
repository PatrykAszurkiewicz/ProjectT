using UnityEngine;
using System.Collections.Generic;

// EyeChains
//
// The chains behave as a CONICAL PENDULUM ring hanging from a small hub under
// the eye:
//
//   IDLE / MOVING  -> no spin, the cone collapses to straight down, and the cord
//                     ends are FREE, so the chains hang loose and dangle in the
//                     wind (the original hanging-chain look).
//   ATTACKING      -> the ring spins up, centrifugal force opens the cone, and
//                     the ends get pinned onto a perspective-squashed ELLIPSE
//                     that they sweep around the eye.
//
// The cord length is a FIXED number of equal links in BOTH states, so the chains
// are exactly the same length hanging as they are spinning - the cone angle only
// changes WHERE the end sits, never how long the cord is.
//
// Front of the ellipse (nearest camera) sits low/large/bright and draws in front
// of the eye; the back sits high/small/dark and draws behind it.

[DisallowMultipleComponent]
public class EyeChains : MonoBehaviour
{
    [Header("Ring")]
    [SerializeField] private int chainCount = 5;

    [Tooltip("Cord length in WORLD units. Identical in the hanging and spinning states - spinning only lifts " +
             "the ends outward on the cone, it never shortens the chain.")]
    [SerializeField] private float cordLength = 0.95f;

    [Range(0f, 0.4f)]
    [Tooltip("Random +/- variation in cord length so the ring isn't perfectly mechanical.")]
    [SerializeField] private float lengthJitter = 0.1f;

    [Tooltip("Radius of the small hub ring the cords hang from, in WORLD units. The cords fan out from here.")]
    [SerializeField] private float hubRadius = 0.12f;

    [Tooltip("Height of the hub ring relative to the eye pivot, world units (negative = below the pivot).")]
    [SerializeField] private float hubCenterY = -0.08f;

    [Tooltip("Isometric squash of the ring (minor/major). 1 = seen head-on (a circle); small = seen almost " +
             "edge-on from above (a thin ellipse). ~0.5 is a classic 2:1 isometric ellipse.")]
    [Range(0.12f, 0.9f)][SerializeField] private float orbitTilt = 0.5f;

    [Header("Spin (attack only)")]
    [Tooltip("Only spin the chains while the eye is ATTACKING. When false the ring spins constantly.")]
    [SerializeField] private bool spinOnlyWhenAttacking = true;

    [Tooltip("Ring rotation speed in degrees/second at full spin (360 = one turn per second). Sign flips the " +
             "spin direction.")]
    [SerializeField] private float rotationSpeed = 180f;

    [Tooltip("How far the cords swing OUT from vertical at full spin, in degrees - the cone half-angle. 0 = " +
             "they stay hanging straight down even while spinning; 55 gives a wide, clearly elliptical sweep. " +
             "This is the main 'how dramatic is the attack' knob.")]
    [Range(0f, 80f)][SerializeField] private float maxConeAngle = 55f;

    [Tooltip("Seconds to ramp UP from hanging to full spin when an attack starts. Short = a snappy wind-up.")]
    [SerializeField] private float spinUpTime = 0.30f;

    [Tooltip("Seconds to wind DOWN from full spin back to hanging when the attack ends. Longer than spin-up so " +
             "the chains coast to a stop and settle instead of snapping straight.")]
    [SerializeField] private float spinDownTime = 0.85f;

    [Tooltip("Extra centrifugal bow in the cord's belly while spinning (scaled by how spun-up it is). Adds a " +
             "flung, whippy feel on top of the cone. 0 = perfectly straight cords.")]
    [Range(0f, 18f)][SerializeField] private float spinSplay = 6.5f;

    [Header("Centering")]
    [Tooltip("Centre the ring on the eye SPRITE's visual (tight-mesh) centre instead of the raw transform " +
             "pivot. Computed live and FLIP-AWARE, so it stays correct when the eye mirrors to face the player.")]
    [SerializeField] private bool centerOnSpriteBounds = true;

    [Tooltip("Manual fine nudge of the ring in the eye's LOCAL space (sprite units, before the eye's scale). " +
             "x = the eye's own left/right, so it MIRRORS with the sprite flip and corrects both facings at " +
             "once. Use this to dial out any residual sideways offset; y = up/down.")]
    [SerializeField] private Vector2 chainCenterOffset = Vector2.zero;

    [Header("Perspective (front bigger/brighter, back smaller/darker)")]
    [Range(0.3f, 1f)][SerializeField] private float backScale = 0.66f;
    [Range(1f, 1.7f)][SerializeField] private float frontScale = 1.18f;
    [Range(0f, 1f)][SerializeField] private float backShade = 0.42f;

    [Header("Chain Look")]
    [SerializeField] private float chainWidth = 0.20f;
    [SerializeField] private float linkSpacing = 0.13f;
    [Range(1f, 2.5f)][SerializeField] private float linkLengthMultiplier = 1.55f;
    [SerializeField] private Color linkTint = Color.white;
    [SerializeField] private bool barbedLinks = true;
    [SerializeField] private Color glowColor = new Color(0.72f, 0.22f, 1f, 0.5f);

    [Header("Link Roll")]
    [SerializeField] private float twistSpeed = 4.5f;
    [Range(0.1f, 1f)][SerializeField] private float edgeOnThinness = 0.18f;
    [Range(0f, 4f)][SerializeField] private float motionTwist = 2.6f;
    [SerializeField] private float twistWavePerLink = 1.3f;

    [Header("Sorting (band: above grass, below fog)")]
    [Tooltip("Must be \"Default\" - the biome overlays (grass, fog, night) are all on that layer and sorting " +
             "order only compares within a single layer.")]
    [SerializeField] private string sortingLayerName = "Default";

    [Tooltip("Sorting order of the EYE BODY - the centre of the band. Grass Y-sorts to ~1600 and fog is at " +
             "5000, so ~3000 keeps the cluster ABOVE the grass and BELOW the fog.")]
    [SerializeField] private int eyeBodyOrder = 3000;

    [Tooltip("How many orders the front/back cords spread around the eye body. Back cords are occluded by the " +
             "eye = the 'goes behind the eye' read.")]
    [SerializeField] private int chainDepthRange = 24;

    [Tooltip("Pin the eye body onto the layer/order above every frame so it's guaranteed above grass, below fog.")]
    [SerializeField] private bool pinEyeSorting = true;

    [Header("Physics")]
    [SerializeField] private float gravity = 9f;
    [Range(0.8f, 0.999f)][SerializeField] private float damping = 0.99f;
    [Range(1, 40)][SerializeField] private int stiffness = 20;
    [Range(1, 4)][SerializeField] private int substeps = 2;

    [Header("Idle Motion (the loose hanging dangle)")]
    [SerializeField] private float windStrength = 1.4f;
    [SerializeField] private float windSpeed = 2.4f;
    [SerializeField] private float gustStrength = 1.0f;
    [SerializeField] private float gustSpeed = 0.9f;

    [Header("Attack Reaction")]
    [Tooltip("A brief smooth DOWNWARD tension tug when an attack starts. Never lifts or inflates the chains.")]
    [SerializeField] private bool reactOnAttack = true;
    [SerializeField] private float attackReactTime = 0.22f;
    [Range(0f, 30f)][SerializeField] private float attackTugStrength = 6f;

    // --- Public geometry: lets the Eye's ground dust match the spinning ring ---
    public float OrbitTilt => orbitTilt;

    // Footprint at FULL spin (what the dust should ring), world units.
    public float OrbitRadiusXWorld =>
        hubRadius + cordLength * Mathf.Sin(maxConeAngle * Mathf.Deg2Rad);

    // Height the cord ends reach at FULL spin - i.e. the ground level of the ring.
    public Vector3 OrbitCenterWorld =>
        RingBase() + new Vector3(0f, hubCenterY - cordLength * Mathf.Cos(maxConeAngle * Mathf.Deg2Rad), 0f);

    public string ClusterSortingLayer => string.IsNullOrEmpty(sortingLayerName) ? "Default" : sortingLayerName;
    public int EyeBodyOrder => eyeBodyOrder;

    private class Chain
    {
        public Vector3[] pos;
        public Vector3[] prev;
        public float baseAngle;
        public float windPhase;
        public float twistPhase;
        public float length;    // fixed cord length - same hanging or spinning
        public float segLen;    // length / (nodes-1), constant
        public float depth;     // +1 front (near) .. -1 back (far)
        public SpriteRenderer[] links;
    }

    private readonly List<Chain> chains = new List<Chain>();
    private Transform root;
    private EnemyController controller;
    private SpriteRenderer bodySR;

    private bool wasAttacking;
    private float attackTimer;
    private float attackPulse01;

    private float spin01;       // 0 = hanging loose, 1 = full spin
    private float ringAngleRad; // accumulated, so speed changes never cause a jump

    private Sprite linkSprite;
    private float linkSpriteW, linkSpriteH;

    private void Start()
    {
        if (chainCount <= 0) { enabled = false; return; }

        controller = GetComponent<EnemyController>();
        bodySR = GetComponent<SpriteRenderer>();
        if (bodySR == null) bodySR = GetComponentInChildren<SpriteRenderer>();

        BuildLinkAsset();

        // Root is NOT parented to the eye: the eye is scaled 0.25 and a parented
        // child would inherit that, shrinking the links. Positions are world-space.
        root = new GameObject(gameObject.name + "_EyeChainsRoot").transform;
        root.position = Vector3.zero;
        root.rotation = Quaternion.identity;
        root.localScale = Vector3.one;

        for (int c = 0; c < chainCount; c++)
            chains.Add(BuildChain(c));
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private Chain BuildChain(int index)
    {
        var chain = new Chain();

        chain.baseAngle = (chainCount == 1)
            ? Mathf.PI * 0.5f
            : (index / (float)chainCount) * Mathf.PI * 2f;

        chain.windPhase = index * 1.7f + Random.value * 6.283f;
        chain.twistPhase = index * 0.9f + Random.value * 6.283f;
        chain.length = cordLength * (1f + Random.Range(-lengthJitter, lengthJitter));

        int nodes = Mathf.Clamp(Mathf.RoundToInt(chain.length / Mathf.Max(0.05f, linkSpacing)) + 1, 4, 48);

        // Constant segment length -> the cord is the SAME total length in every
        // state. Spin only moves where the end hangs, never how long it is.
        chain.segLen = chain.length / (nodes - 1);

        GetSpoke(chain, out Vector3 hub, out Vector3 tip, out float depth);
        chain.depth = depth;

        chain.pos = new Vector3[nodes];
        chain.prev = new Vector3[nodes];
        for (int i = 0; i < nodes; i++)
        {
            float t = i / (float)(nodes - 1);
            Vector3 p = Vector3.Lerp(hub, tip, t);
            chain.pos[i] = p;
            chain.prev[i] = p;
        }

        int segCount = nodes - 1;
        chain.links = new SpriteRenderer[segCount];
        for (int i = 0; i < segCount; i++)
        {
            var go = new GameObject("Link_" + index + "_" + i);
            go.layer = gameObject.layer;
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = linkSprite;
            sr.color = linkTint;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            chain.links[i] = sr;
        }

        return chain;
    }

    // Hub (top) and the cord end's TARGET position for the current spin state.
    //
    // Conical pendulum: the cord is length L and makes a cone half-angle `cone`
    // with vertical. cone scales with spin, so:
    //   spin01 = 0 -> cone 0   -> target is straight down at distance L (a plain
    //                             hang; the end is also released, so it dangles)
    //   spin01 = 1 -> cone max -> the end rides a wide ellipse of radius
    //                             hubRadius + L*sin(cone), lifted to -L*cos(cone)
    // Because L never changes, the chain is the same length in both states.
    private void GetSpoke(Chain ch, out Vector3 hub, out Vector3 tip, out float depth)
    {
        float phi = ch.baseAngle + ringAngleRad;
        float cosP = Mathf.Cos(phi);
        float sinP = Mathf.Sin(phi);
        depth = sinP;                                   // +1 front, -1 back

        Vector3 basePos = RingBase();

        hub = basePos + new Vector3(cosP * hubRadius,
                                    hubCenterY - sinP * hubRadius * orbitTilt, 0f);

        float cone = maxConeAngle * Mathf.Deg2Rad * spin01;
        float L = ch.length;
        float r = hubRadius + L * Mathf.Sin(cone);       // ring radius at the end
        float drop = L * Mathf.Cos(cone);                // vertical reach

        tip = basePos + new Vector3(cosP * r,
                                    hubCenterY - drop - sinP * r * orbitTilt, 0f);
    }

    // Centre of the ring in world space, corrected to the eye sprite's visual
    // centre and mirrored with the sprite flip (the eye mirrors to face the
    // player, which swaps the visual centre to the other side of the pivot).
    private Vector3 RingBase()
    {
        Vector3 p = transform.position;
        if (bodySR == null)
            return new Vector3(p.x + chainCenterOffset.x, p.y + chainCenterOffset.y, p.z);

        float flipXsign = bodySR.flipX ? -1f : 1f;

        float localX = chainCenterOffset.x;
        if (centerOnSpriteBounds && bodySR.sprite != null)
            localX += bodySR.sprite.bounds.center.x;

        float worldX = p.x + localX * transform.lossyScale.x * flipXsign;
        float worldY = p.y + chainCenterOffset.y * transform.lossyScale.y;
        return new Vector3(worldX, worldY, p.z);
    }

    private void LateUpdate()
    {
        if (chains.Count == 0) return;

        if (pinEyeSorting && bodySR != null)
        {
            bodySR.sortingLayerName = ClusterSortingLayer;
            bodySR.sortingOrder = eyeBodyOrder;
        }

        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (dt <= 0f) return;

        bool attacking = controller != null && controller.IsAttacking;

        // Attack reaction pulse (a smooth 1->0 envelope, not an impulse).
        if (reactOnAttack && controller != null)
        {
            if (attacking && !wasAttacking) attackTimer = attackReactTime;
            wasAttacking = attacking;
        }
        attackPulse01 = (reactOnAttack && attackReactTime > 0f)
            ? Mathf.Clamp01(attackTimer / attackReactTime) : 0f;
        if (attackTimer > 0f) attackTimer -= Time.deltaTime;

        // Spin envelope: ramp up on attack, coast down after. Everything about the
        // spin (cone angle, ring speed, centrifugal bow, whether the ends are
        // pinned) is scaled by this, so idle really is a plain loose hang.
        float target = (!spinOnlyWhenAttacking || attacking) ? 1f : 0f;
        float ramp = target > spin01 ? spinUpTime : spinDownTime;
        spin01 = ramp <= 0f ? target : Mathf.MoveTowards(spin01, target, dt / ramp);

        // Accumulate the ring angle so changing speed/spin01 never snaps the ring.
        ringAngleRad += Mathf.Deg2Rad * rotationSpeed * spin01 * dt;
        if (ringAngleRad > Mathf.PI * 2f) ringAngleRad -= Mathf.PI * 2f;
        else if (ringAngleRad < -Mathf.PI * 2f) ringAngleRad += Mathf.PI * 2f;

        float subDt = dt / substeps;
        for (int s = 0; s < substeps; s++)
            for (int i = 0; i < chains.Count; i++)
                StepChain(chains[i], subDt);

        for (int i = 0; i < chains.Count; i++)
            RenderChain(chains[i]);
    }

    private void StepChain(Chain ch, float dt)
    {
        int n = ch.pos.Length;
        float time = Time.time;

        GetSpoke(ch, out Vector3 hub, out Vector3 tip, out float depth);
        ch.depth = depth;

        // The cord end is only pinned to the ellipse while spinning. At rest it's
        // a free node, so the chain hangs and dangles like a real loose chain.
        bool pinTip = spin01 > 0.02f;
        int lastFree = pinTip ? n - 2 : n - 1;

        float cosP = Mathf.Cos(ch.baseAngle + ringAngleRad);
        float attackTug = attackPulse01 * attackTugStrength;
        float dmp = Mathf.Lerp(damping, 0.9f, attackPulse01);
        float dt2 = dt * dt;

        for (int i = 1; i <= lastFree; i++)
        {
            float s = i / (float)(n - 1);
            float bell = Mathf.Sin(s * Mathf.PI);

            // Wind: the loose hanging dangle. Strongest toward the free end.
            float swayPhase = time * windSpeed + ch.windPhase + i * 0.35f;
            float sway = (Mathf.Sin(swayPhase) + 0.5f * Mathf.Sin(swayPhase * 2.3f + 1.1f)) * windStrength;
            float gust = (Mathf.PerlinNoise(time * gustSpeed, ch.windPhase) - 0.5f) * 2f * gustStrength;
            float lateral = (sway + gust) * s;

            // Centrifugal bow - only while actually spinning.
            lateral += cosP * spinSplay * bell * spin01;

            Vector3 accel = new Vector3(lateral, -gravity - attackTug * bell, 0f);

            Vector3 cur = ch.pos[i];
            Vector3 vel = (ch.pos[i] - ch.prev[i]) * dmp;
            ch.prev[i] = cur;
            ch.pos[i] = cur + vel + accel * dt2;
        }

        // Distance solve. The hub is always pinned; the tip only while spinning.
        for (int k = 0; k < stiffness; k++)
        {
            ch.pos[0] = hub;
            if (pinTip) ch.pos[n - 1] = tip;

            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = ch.pos[i];
                Vector3 b = ch.pos[i + 1];
                Vector3 delta = b - a;
                float d = delta.magnitude;
                if (d < 1e-5f) continue;
                float diff = (d - ch.segLen) / d;

                bool aPinned = (i == 0);
                bool bPinned = pinTip && (i + 1 == n - 1);

                if (aPinned && bPinned) continue;
                else if (aPinned) ch.pos[i + 1] = b - delta * diff;
                else if (bPinned) ch.pos[i] = a + delta * diff;
                else
                {
                    Vector3 shift = delta * (0.5f * diff);
                    ch.pos[i] = a + shift;
                    ch.pos[i + 1] = b - shift;
                }
            }
        }

        ch.pos[0] = hub;
        ch.prev[0] = hub;
        if (pinTip)
        {
            ch.pos[n - 1] = tip;
            ch.prev[n - 1] = tip;
        }
    }

    private void RenderChain(Chain ch)
    {
        float time = Time.time;
        float prevAngle = 0f;

        // While hanging, the ring isn't turning, so fade the depth perspective out
        // and let all the chains render evenly instead of some staying dark.
        float front01 = (ch.depth + 1f) * 0.5f;
        float depthWidth = Mathf.Lerp(1f, Mathf.Lerp(backScale, frontScale, front01), spin01);
        float depthShade = Mathf.Lerp(1f, Mathf.Lerp(backShade, 1f, front01), spin01);

        string layer = ClusterSortingLayer;
        int baseOrder = eyeBodyOrder + Mathf.RoundToInt(ch.depth * chainDepthRange * spin01);

        float spinSign = rotationSpeed >= 0f ? 1f : -1f;

        for (int i = 0; i < ch.links.Length; i++)
        {
            Vector3 a = ch.pos[i];
            Vector3 b = ch.pos[i + 1];
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            float bend = i == 0 ? 0f : Mathf.Abs(Mathf.DeltaAngle(prevAngle, angle)) * Mathf.Deg2Rad;
            prevAngle = angle;

            float phase = time * twistSpeed * spinSign
                        + i * twistWavePerLink * spinSign
                        + ch.twistPhase
                        + bend * motionTwist;
            float broad = Mathf.Abs(Mathf.Cos(phase));
            float thin = Mathf.Lerp(edgeOnThinness, 1f, broad);

            var sr = ch.links[i];
            Transform lt = sr.transform;
            lt.position = mid;
            lt.rotation = Quaternion.Euler(0f, 0f, angle);

            float lengthAlong = ch.segLen * linkLengthMultiplier;
            lt.localScale = new Vector3(lengthAlong / linkSpriteW,
                                        (chainWidth * thin * depthWidth) / linkSpriteH, 1f);

            float shade = Mathf.Lerp(0.72f, 1f, broad) * depthShade;
            sr.color = new Color(linkTint.r * shade, linkTint.g * shade, linkTint.b * shade, linkTint.a);

            sr.sortingLayerName = layer;
            sr.sortingOrder = baseOrder + (i & 1);
        }
    }

    // Public trigger for the attack reaction (e.g. from an animation event).
    public void Lash(float strength = 1f)
    {
        if (attackReactTime > 0f) attackTimer = attackReactTime;
    }

    // ---------------------------------------------------------------- asset
    private void BuildLinkAsset()
    {
        var tex = BuildLinkTexture();
        linkSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                   new Vector2(0.5f, 0.5f), 100f);
        linkSpriteW = linkSprite.bounds.size.x;
        linkSpriteH = linkSprite.bounds.size.y;
    }

    private Texture2D BuildLinkTexture()
    {
        const int W = 64, H = 40;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);

        PaintRing(px, W, H, W * 0.5f, H * 0.5f, W * 0.40f, H * 0.30f);

        if (barbedLinks)
        {
            PaintBarb(px, W, H, W * 0.5f, H * 0.5f - H * 0.30f, 4, -1);
            PaintBarb(px, W, H, W * 0.5f, H * 0.5f + H * 0.30f, 4, 1);
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private void PaintRing(Color[] px, int W, int H, float cx, float cy, float rx, float ry)
    {
        Color outline = new Color(0.04f, 0.03f, 0.06f, 1f);
        Color metalDark = new Color(0.22f, 0.22f, 0.27f, 1f);
        Color metalLite = new Color(0.48f, 0.48f, 0.56f, 1f);
        Color rim = new Color(0.82f, 0.26f, 1f, 1f);
        Color glint = new Color(0.97f, 0.93f, 1f, 1f);

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float dx = (x - cx) / rx;
                float dy = (y - cy) / ry;
                float e = Mathf.Sqrt(dx * dx + dy * dy);

                Color c; float a;
                if (e < 0.44f) continue;
                else if (e < 0.55f) { c = outline; a = 1f; }
                else if (e < 0.90f)
                {
                    float shade = Mathf.InverseLerp(1f, -1f, dy);
                    c = Color.Lerp(metalDark, metalLite, shade);
                    if (dx < -0.15f && dy < -0.15f && e > 0.6f && e < 0.85f)
                        c = Color.Lerp(c, glint, 0.5f);
                    a = 1f;
                }
                else if (e < 1.00f) { c = rim; a = 1f; }
                else if (e < 1.14f) { c = outline; a = 1f; }
                else if (e < 1.9f)
                {
                    float f = 1f - Mathf.InverseLerp(1.14f, 1.9f, e);
                    c = glowColor; a = glowColor.a * f * f;
                }
                else continue;

                Over(px, y * W + x, new Color(c.r, c.g, c.b, a));
            }
        }
    }

    private static void PaintBarb(Color[] px, int W, int H, float cx, float tipY, int len, int dir)
    {
        Color body = new Color(0.06f, 0.05f, 0.08f, 1f);
        Color tip = new Color(0.82f, 0.26f, 1f, 1f);
        for (int s = 0; s < len; s++)
        {
            int y = Mathf.RoundToInt(tipY + dir * s);
            if (y < 0 || y >= H) continue;
            int half = Mathf.Max(0, (len - s) / 2);
            Color c = s < 2 ? tip : body;
            for (int x = -half; x <= half; x++)
            {
                int xx = Mathf.RoundToInt(cx) + x;
                if (xx < 0 || xx >= W) continue;
                Over(px, y * W + xx, c);
            }
        }
    }

    private static void Over(Color[] px, int idx, Color s)
    {
        Color d = px[idx];
        float outA = s.a + d.a * (1f - s.a);
        if (outA <= 0.0001f) { px[idx] = new Color(0, 0, 0, 0); return; }
        float inv = 1f / outA;
        px[idx] = new Color(
            (s.r * s.a + d.r * d.a * (1f - s.a)) * inv,
            (s.g * s.a + d.g * d.a * (1f - s.a)) * inv,
            (s.b * s.a + d.b * d.a * (1f - s.a)) * inv,
            outA);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 basePos = EditorRingBase();
        float coneRad = maxConeAngle * Mathf.Deg2Rad;
        float fullR = hubRadius + cordLength * Mathf.Sin(coneRad);
        float fullY = hubCenterY - cordLength * Mathf.Cos(coneRad);

        // Ellipse the cord ends sweep at FULL spin.
        Gizmos.color = new Color(0.85f, 0.2f, 1f, 0.6f);
        DrawEllipseGizmo(basePos, fullY, fullR);

        // Hub ring.
        Gizmos.color = new Color(0.5f, 0.6f, 1f, 0.5f);
        DrawEllipseGizmo(basePos, hubCenterY, hubRadius);

        // Where the ends rest when hanging (no spin).
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.5f);
        DrawEllipseGizmo(basePos, hubCenterY - cordLength, hubRadius);
    }

    private Vector3 EditorRingBase()
    {
        Vector3 p = transform.position;
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
            return new Vector3(p.x + chainCenterOffset.x, p.y + chainCenterOffset.y, p.z);

        float flipXsign = sr.flipX ? -1f : 1f;
        float localX = chainCenterOffset.x;
        if (centerOnSpriteBounds && sr.sprite != null)
            localX += sr.sprite.bounds.center.x;

        float worldX = p.x + localX * transform.lossyScale.x * flipXsign;
        float worldY = p.y + chainCenterOffset.y * transform.lossyScale.y;
        return new Vector3(worldX, worldY, p.z);
    }

    private void DrawEllipseGizmo(Vector3 basePos, float centerY, float radiusX)
    {
        const int SEG = 48;
        Vector3 prev = Vector3.zero;
        for (int s = 0; s <= SEG; s++)
        {
            float th = (s / (float)SEG) * Mathf.PI * 2f;
            float x = Mathf.Cos(th) * radiusX;
            float y = centerY - Mathf.Sin(th) * radiusX * orbitTilt;
            Vector3 w = basePos + new Vector3(x, y, 0f);
            if (s > 0) Gizmos.DrawLine(prev, w);
            prev = w;
        }
    }
}


