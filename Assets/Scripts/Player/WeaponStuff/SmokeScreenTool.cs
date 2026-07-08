using System.Collections.Generic;
using UnityEngine;

//  SMOKE SCREEN TOOL
//  A right-click utility tool, aimed exactly like the Mortar. Left-/right-click lobs an arcing canister to the aimed spot; on
//  impact it bursts into an expanding, semi-transparent smoke cloud 
public static class SmokeBlind
{
    /// True if a smoke cloud sits on the sightline between 'from' and 'to'.
    public static bool Blocks(Vector2 from, Vector2 to)
        => SmokeScreenCloud.BlocksSegment(from, to);

    /// A gentle velocity for a smoke-blinded enemy. Averages
    /// to close to zero so the enemy holds position while reading as confused/waiting.
    public static Vector2 ShuffleVelocity(float phase, float moveSpeed)
    {
        float t = Time.time * 2.5f + phase;
        return new Vector2(Mathf.Sin(t), Mathf.Cos(t * 1.27f)) * (moveSpeed * 0.18f);
    }

    /// A random per-enemy phase so a group of blinded enemies doesn't shuffle in
    /// lock-step. Call once at spawn and cache it.
    public static float NewPhase() => Random.Range(0f, 6.2831853f);
}


/// Owns the Smoke Screen's cooldown. Like the Time Clock, the timer lives on
/// PlayerToolCooldownStore (NOT on this system) so scrolling away from the tool
/// and back doesn't wipe the cooldown — the store ticks it independently every
/// frame regardless of which tool is equipped.
public class SmokeScreenSystem
{
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private PlayerToolCooldownStore store;

    public SmokeScreenSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        store = PlayerToolCooldownStore.GetOrCreate(weapon);
    }

    private PlayerToolCooldownStore Store
    {
        get
        {
            if (store == null) store = PlayerToolCooldownStore.GetOrCreate(weapon);
            return store;
        }
    }

    public bool IsOnCooldown
    {
        get { var s = Store; return s != null && s.smokeCooldownTimer > 0f; }
    }

    public bool IsReady => !IsOnCooldown;

    /// 0..1 over the cooldown (0 = just spent, 1 = ready). 1 when not cooling down.
    public float CooldownNormalized
    {
        get
        {
            var s = Store;
            if (s == null || s.smokeCooldownTimer <= 0f || s.smokeCooldownTotal <= 0f) return 1f;
            return 1f - Mathf.Clamp01(s.smokeCooldownTimer / s.smokeCooldownTotal);
        }
    }

    /// Arm the cooldown for `duration` seconds (the asset's attackCooldown — 5s).
    public void StartCooldown(float duration)
    {
        var s = Store;
        if (s == null) return;
        s.smokeCooldownTotal = Mathf.Max(0.0001f, CooldownModifier.Apply(duration));
        s.smokeCooldownTimer = s.smokeCooldownTotal;
    }

    // The cooldown intentionally persists on the store across un-equip, so there
    // is nothing to tear down here. Present for parity with the other systems.
    public void Cleanup() { }
}


//  The arcing canister. Mirrors PlayerMortarProjectile's flight, but on landing
//  it spawns a SmokeScreenCloud instead of dealing AoE damage.
[RequireComponent(typeof(SpriteRenderer))]
public class SmokeScreenProjectile : MonoBehaviour
{
    private float cloudRadius;
    private float cloudDuration;
    private Color cloudColor;
    private bool showTelegraph;
    private Color telegraphColor;

    private Vector3 spawnPos;
    private Vector3 landingPos;
    private float travelTime;
    private float arcHeight;
    private float maxLifetime;
    private float age;

    private bool initialized;
    private bool burst;
    private Vector3 lastPos;
    private GameObject telegraphObj;


    private const float TelegraphScale = 0.42f;

    public void Initialize(Vector3 landingPosition, float travelTime, float arcHeight,
                           float cloudRadius, float cloudDuration, Color cloudColor,
                           bool showTelegraph, Color telegraphColor, float maxLifetime = 8f)
    {
        this.landingPos = landingPosition;
        this.travelTime = Mathf.Max(0.1f, travelTime);
        this.arcHeight = Mathf.Max(0f, arcHeight);
        this.cloudRadius = Mathf.Max(0.3f, cloudRadius);
        this.cloudDuration = Mathf.Max(0.1f, cloudDuration);
        this.cloudColor = cloudColor;
        this.showTelegraph = showTelegraph;
        this.telegraphColor = telegraphColor;

        this.spawnPos = transform.position;
        this.maxLifetime = Mathf.Max(this.travelTime + 0.5f, maxLifetime);
        this.lastPos = transform.position;
        initialized = true;

        // Launch SFX (Smoke Screen tool).
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.smokeShot.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.smokeShot, spawnPos);
        }

        SpawnTelegraph();
    }

    private void Update()
    {
        if (!initialized || burst) return;

        age += Time.deltaTime;

        // Safety cap so a canister can never leak.
        if (age >= maxLifetime)
        {
            Burst();
            return;
        }

        float t = Mathf.Clamp01(age / travelTime);

        // Linear ground travel + parabolic visual lift (peaks at the midpoint).
        Vector3 groundPos = Vector3.Lerp(spawnPos, landingPos, t);
        float height = arcHeight * 4f * t * (1f - t);
        Vector3 newPos = groundPos + Vector3.up * height;

        Vector3 travelDelta = newPos - lastPos;
        transform.position = newPos;
        lastPos = newPos;

        // Face direction of travel.
        if (travelDelta.sqrMagnitude > 1e-8f)
        {
            float angle = Mathf.Atan2(travelDelta.y, travelDelta.x) * Mathf.Rad2Deg;
            if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        if (t >= 1f)
            Burst();
    }

    private void Burst()
    {
        if (burst) return;
        burst = true;

        var root = new GameObject("SmokeScreenCloud");
        root.transform.position = landingPos;
        root.AddComponent<SmokeScreenCloud>().Initialize(cloudRadius, cloudDuration, cloudColor);

        DestroyTelegraph();
        Destroy(gameObject);
    }

    private void SpawnTelegraph()
    {
        if (!showTelegraph) return;

        // Reuse the mortar's ground telegraph so the landing footprint reads
        // identically (just a different, bluer tint).
        telegraphObj = new GameObject("SmokeScreenTelegraph");
        telegraphObj.transform.position = landingPos;
        telegraphObj.AddComponent<MortarTelegraph>()
                    .Initialize(cloudRadius * TelegraphScale, travelTime, telegraphColor);
    }

    private void DestroyTelegraph()
    {
        if (telegraphObj != null) Destroy(telegraphObj);
    }

    private void OnDestroy()
    {
        DestroyTelegraph();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.55f, 0.85f, 1f);
        Vector3 c = Application.isPlaying ? landingPos : transform.position;
        Gizmos.DrawWireSphere(c, cloudRadius);
    }
#endif
}


//  The lingering cloud. Maintains a static registry of all active clouds and
//  exposes BlocksSegment(a, b) for enemies to test their sightlines against.
public class SmokeScreenCloud : MonoBehaviour
{
    // Every live cloud registers here so EnemyController can test sightlines
    // without any physics-layer wiring.
    private static readonly List<SmokeScreenCloud> Active = new List<SmokeScreenCloud>();

    private float maxRadius = 3f;
    private float duration = 6f;
    private float elapsed;
    private float currentRadius;

    // The blocking radius ramps from 0 to full over this many seconds after the
    // burst, so the "wall" forms as the cloud visibly expands rather than
    // snapping on at full size the instant the canister lands.
    private const float RampInTime = 0.45f;

    public Vector2 Center => transform.position;
    public float CurrentRadius => currentRadius;

    public void Initialize(float radius, float duration, Color color)
    {
        this.maxRadius = Mathf.Max(0.3f, radius);
        this.duration = Mathf.Max(0.1f, duration);
        this.currentRadius = 0f;

        // Build the procedural expanding grey particle cloud (no art needed).
        var visual = gameObject.GetComponent<SmokeScreenVisual>();
        if (visual == null) visual = gameObject.AddComponent<SmokeScreenVisual>();
        visual.Play(maxRadius, this.duration, color);
    }

    private void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private void OnDestroy()
    {
        Active.Remove(this);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        // Block fully once ramped in, and keep blocking for the whole lifetime.
        float ramp = Mathf.Clamp01(elapsed / RampInTime);
        currentRadius = maxRadius * ramp;

        if (elapsed >= duration)
            Destroy(gameObject);
    }

    // True if the segment a->b passes within any active cloud's blocking radius
    // (i.e. the smoke sits on that sightline)
    public static bool BlocksSegment(Vector2 a, Vector2 b)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            var c = Active[i];
            if (c == null) continue;
            float r = c.currentRadius;
            if (r <= 0.01f) continue;
            if (SegmentIntersectsCircle(a, b, c.Center, r)) return true;
        }
        return false;
    }

    /// True if any point of segment a->b lies within `radius` of `center`.
    private static bool SegmentIntersectsCircle(Vector2 a, Vector2 b, Vector2 center, float radius)
    {
        Vector2 ab = b - a;
        float abLenSq = ab.sqrMagnitude;
        if (abLenSq < 1e-8f)
            return (center - a).sqrMagnitude <= radius * radius;

        float t = Vector2.Dot(center - a, ab) / abLenSq;
        t = Mathf.Clamp01(t);
        Vector2 closest = a + ab * t;
        return (center - closest).sqrMagnitude <= radius * radius;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.62f, 0.66f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, currentRadius);
    }
#endif
}


//  Procedural expanding grey "particle" cloud, built from soft-disc sprites so
//  it needs no art assets (same approach as the mortar's explosion VFX)
public class SmokeScreenVisual : MonoBehaviour
{
    private const int PuffCount = 16;
    private const float ExpandTime = 1.0f;   // seconds to reach full spread
    private const float FadeInTime = 0.5f;
    private const float FadeOutTime = 1.2f;  // dissipate over the final stretch
    private const int SortOrder = 2700;      // above projectiles (2500), below reticle
    private const string SortLayer = "Default";

    private struct Puff
    {
        public SpriteRenderer sr;
        public Vector2 dir;
        public float dist;       // final offset distance from centre
        public float size;       // world diameter at full expansion
        public float startSize;  // world diameter at spawn
        public float rotSpeed;
        public float bobPhase;
        public float tone;       // brightness variation
    }

    private Puff[] puffs;
    private SpriteRenderer baseDisc;
    private float duration = 6f;
    private float maxRadius = 3f;
    private float elapsed;
    private Color baseColor = new Color(0.62f, 0.64f, 0.66f, 0.42f);
    private bool playing;

    public void Play(float radius, float duration, Color color)
    {
        this.maxRadius = Mathf.Max(0.3f, radius);
        this.duration = Mathf.Max(0.1f, duration);
        this.baseColor = color;
        Build();
        playing = true;
    }

    private void Build()
    {
        // Soft footprint disc under the puffs to fill the centre.
        var discGo = new GameObject("SmokeBase");
        discGo.transform.SetParent(transform, false);
        baseDisc = discGo.AddComponent<SpriteRenderer>();
        baseDisc.sprite = Boss2VFXSprites.GetSoftDisc();
        baseDisc.sortingLayerName = SortLayer;
        baseDisc.sortingOrder = SortOrder - 1;

        puffs = new Puff[PuffCount];
        for (int i = 0; i < PuffCount; i++)
        {
            var go = new GameObject("SmokePuff" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Boss2VFXSprites.GetSoftDisc();
            sr.sortingLayerName = SortLayer;
            sr.sortingOrder = SortOrder + (i % 3);

            float ang = Random.Range(0f, Mathf.PI * 2f);
            // sqrt(random) gives a uniform area distribution so the disc fills
            // evenly rather than clumping at the centre.
            float dist = Mathf.Sqrt(Random.value) * maxRadius * 0.85f;

            puffs[i] = new Puff
            {
                sr = sr,
                dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)),
                dist = dist,
                size = maxRadius * Random.Range(0.55f, 0.95f),
                startSize = maxRadius * Random.Range(0.15f, 0.30f),
                rotSpeed = Random.Range(-25f, 25f),
                bobPhase = Random.Range(0f, Mathf.PI * 2f),
                tone = Random.Range(0.82f, 1.05f),
            };
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }
    }

    private void Update()
    {
        if (!playing) return;
        elapsed += Time.deltaTime;

        float growT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / ExpandTime));
        float alphaIn = Mathf.Clamp01(elapsed / FadeInTime);
        float alphaOut = Mathf.Clamp01((duration - elapsed) / FadeOutTime);
        float envelope = Mathf.Min(alphaIn, alphaOut);

        // Base footprint disc.
        if (baseDisc != null)
        {
            float d = maxRadius * 2f * Mathf.Lerp(0.4f, 1f, growT);
            baseDisc.transform.localScale = Vector3.one * d;
            Color bc = baseColor;
            bc.a = baseColor.a * 0.55f * envelope;
            baseDisc.color = bc;
        }

        if (puffs == null) return;
        for (int i = 0; i < puffs.Length; i++)
        {
            var p = puffs[i];
            if (p.sr == null) continue;

            float diameter = Mathf.Lerp(p.startSize, p.size, growT);
            p.sr.transform.localScale = Vector3.one * diameter;

            float bob = Mathf.Sin(elapsed * 1.5f + p.bobPhase) * 0.08f * maxRadius;
            Vector2 offset = p.dir * (p.dist * growT + bob);
            p.sr.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            p.sr.transform.Rotate(0f, 0f, p.rotSpeed * Time.deltaTime);

            Color c = baseColor;
            c.r = Mathf.Clamp01(c.r * p.tone);
            c.g = Mathf.Clamp01(c.g * p.tone);
            c.b = Mathf.Clamp01(c.b * p.tone);
            c.a = baseColor.a * envelope;
            p.sr.color = c;
        }

        if (elapsed >= duration)
            playing = false;
    }
}

