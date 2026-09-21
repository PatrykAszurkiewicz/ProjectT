using System.Collections;
using UnityEngine;

//  BOSS 5 — BELL IMPACT
//  Gives the Bellkeeper's melee attack weight

[DisallowMultipleComponent]
public class Boss5BellImpact : MonoBehaviour
{
    [Header("Swing (pendulum)")]
    [Tooltip("Swing the bell about its hanging point on the attack's hit frame. " +
             "Automatically disabled if an EnemyAttackLunge component is present.")]
    public bool enableLunge = true;

    [Tooltip("Height of the pivot ABOVE the sprite's centre, in world units — the " +
             "ring the bell hangs from. The whole body rotates about this point, so " +
             "the skirt travels far while the crown barely moves, which is what makes " +
             "it read as a bell rather than a sliding sprite. Roughly half the " +
             "sprite's visible height is a good starting point.")]
    [Min(0.05f)] public float swingPivotHeight = 1.6f;

    [Tooltip("Degrees the bell rocks BACK before the strike.")]
    [Range(0f, 60f)] public float windUpAngle = 14f;

    [Tooltip("Degrees the bell swings THROUGH on the strike, past vertical.")]
    [Range(0f, 80f)] public float strikeAngle = 26f;

    [Tooltip("Small forward travel added on top of the rotation, world units. Keep " +
             "this SMALL — the swing should carry the attack, not a slide.")]
    [Min(0f)] public float lungeDistance = 0.25f;

    [Tooltip("Seconds spent rocking back before the strike.")]
    [Min(0.02f)] public float windUpDuration = 0.14f;

    [Tooltip("Seconds spent swinging through into the target.")]
    [Min(0.02f)] public float strikeDuration = 0.08f;

    [Tooltip("Seconds of damped rocking as the bell settles back to rest.")]
    [Min(0.02f)] public float recoverDuration = 0.55f;

    [Tooltip("How many times the bell rocks back and forth while settling.")]
    [Range(1f, 5f)] public float settleOscillations = 2.4f;

    [Header("Squash / stretch")]
    [Tooltip("How much the bell squashes on impact. 0 = rigid. A bell is metal, so " +
             "keep this subtle — 0.10-0.18 reads as weight, more reads as rubber.")]
    [Range(0f, 0.4f)] public float squashAmount = 0.14f;

    [Header("Impact")]
    [Tooltip("Radius of the ground shockwave ring drawn at the impact point.")]
    [Min(0.2f)] public float impactRadius = 3.0f;

    [Tooltip("Colour of the impact shockwave. Defaults to the boss's active colour " +
             "when left at pure white.")]
    public Color impactColor = Color.white;

    [Tooltip("Camera shake on impact: amount, then duration.")]
    public float shakeAmount = 0.28f;
    public float shakeDuration = 0.16f;

    [Header("Idle")]
    [Tooltip("Gentle rocking sway while walking, so the bell reads as a heavy " +
             "swinging mass rather than a sliding sprite.\n\n" +
             "OFF BY DEFAULT, and deliberately so: EnemyAnimationController's " +
             "UpdateSpriteOrientation already writes transform.rotation every frame " +
             "to produce its walk-lean, and two writers would fight. Turn this on " +
             "ONLY after calling animController.SetOrientationDrivingEnabled(false), " +
             "which hands rotation over cleanly — that is the same switch the rolling " +
             "Bomber uses.")]
    [Range(0f, 8f)] public float idleSwayDegrees = 0f;

    [Tooltip("Sway cycles per second.")]
    [Min(0f)] public float idleSwaySpeed = 1.4f;

    // ── runtime 
    private Boss5 _boss;
    private EnemyAnimationController _anim;
    private EnemyData _data;
    private SpriteRenderer _sprite;

    private Vector3 _lungeOffset;       // display-only offset, added then removed
    private float _swingAngle;          // display-only pendulum angle, degrees
    private bool _ownsRotation;
    private Vector3 _restingScale = Vector3.one;
    private bool _restingScaleCaptured;
    private Coroutine _swingRoutine;
    private int _lastFiredFrame = -1;
    private int _prevFrame = int.MaxValue;   // forces a re-arm on the first frame seen
    private bool _lungeSuppressed;

    private void Awake()
    {
        _boss = GetComponent<Boss5>();
        _anim = GetComponent<EnemyAnimationController>();
        _sprite = GetComponent<SpriteRenderer>();

        var stats = GetComponent<EnemyStats>();
        _data = stats != null ? stats.enemyData : null;

        // Detection moved to Start() — EnemyAttackLunge may be added by another
        // component's Awake, which is not guaranteed to have run yet.
    }

    private void OnEnable()
    {
        if (_anim != null) _anim.OnAttackFrame += HandleAttackFrame;
    }

    private void OnDisable()
    {
        if (_anim != null) _anim.OnAttackFrame -= HandleAttackFrame;
        // Never leave the boss displaced, rotated or squashed if this component is
        // switched off (or the boss dies) mid-swing.
        ClearLunge();
        TakeRotationControl(false);
    }

    private void Start()
    {
        _restingScale = transform.localScale;
        _restingScaleCaptured = true;

        // The EnemyData on the stats is a per-enemy CLONE created in EnemyStats.Awake,
        // so re-read it here rather than caching the shared asset.
        var stats = GetComponent<EnemyStats>();
        if (stats != null) _data = stats.enemyData;

        // Defer to the project's existing lunge system if this object has one, so the
        // two never write position in the same frame. Checked in Start rather than
        // Awake because another component's Awake may be what adds it. The string
        // lookup keeps this file compiling in a project without EnemyAttackLunge.
        _lungeSuppressed = GetComponent("EnemyAttackLunge") != null;

        int hf = _data != null ? _data.hitFrame : 0;
        int frames = _data != null ? _data.attack.frameCount : 0;
        float clip = (_data != null && frames > 0)
            ? frames * _data.GetAnimSpeed(_data.attack) : 0f;
        Debug.Log($"[Boss5] BellImpact ready — hitFrame {hf} of {frames} " +
                  $"({clip:F2}s clip), lunge {(_lungeSuppressed ? "SUPPRESSED (EnemyAttackLunge present)" : (enableLunge ? "on" : "off"))}");
    }

    /// Fires once per frame of the attack clip. We act only on the hit frame.
    ///
    /// THE LATCH BUG THIS FIXES: the previous version only cleared its "already
    /// fired" latch when `frameIndex < hitFrame`. Your EnemyData has hitFrame = 0,
    /// so that condition could never be true — the latch stuck after the very first
    /// swing and the boss never lunged again. We now detect the clip RESTARTING (the
    /// frame index going backwards or repeating 0), which works for any hitFrame
    /// including 0.
    private void HandleAttackFrame(int frameIndex)
    {
        int hitFrame = _data != null ? Mathf.Max(0, _data.hitFrame) : 0;

        // A new pass of the clip has begun: re-arm.
        if (frameIndex <= _prevFrame) _lastFiredFrame = -1;
        _prevFrame = frameIndex;

        if (frameIndex != hitFrame) return;
        if (_lastFiredFrame == hitFrame) return;
        _lastFiredFrame = hitFrame;

        // The Bellkeeper is stationary and untargetable during State B; a swing there
        // would be an EnemyController coroutine finishing after we disabled it, and
        // slamming the ground mid-challenge would misread as part of the mechanic.
        if (_boss != null && _boss.IsUntargetableByTowers) return;

        if (_swingRoutine != null) StopCoroutine(_swingRoutine);
        _swingRoutine = StartCoroutine(Swing());
    }

    private IEnumerator Swing()
    {
        Vector3 dir = ResolveStrikeDirection();

        // Positive Z rotation tips the bell's skirt to the LEFT, so swinging at a
        // target on the right needs a negative angle. dir.x carries the sign.
        float sign = dir.x >= 0f ? -1f : 1f;

        // Hand rotation over for the duration. EnemyAnimationController writes
        // transform.rotation every frame for its walk-lean, and two writers would
        // fight; this is the same handover the rolling Bomber uses.
        TakeRotationControl(true);

        // Rock back. The hit frame has already arrived, so this is a fast snap —
        // the slow tell was the earlier frames of the clip.
        yield return SwingTo(0f, sign * -windUpAngle, windUpDuration * 0.55f, EaseOut, 0f, 0f);

        // Swing through.
        yield return SwingTo(sign * -windUpAngle, sign * strikeAngle,
                             strikeDuration, EaseIn, 0f, lungeDistance);

        // Land.
        SpawnImpact(transform.position + dir * (lungeDistance + swingPivotHeight * 0.35f), dir);
        StartCoroutine(SquashPop());

        // Settle with damped rocking rather than a single return — a struck bell
        // keeps swinging, and this is the detail that sells the whole motion.
        float settle = Mathf.Max(0.05f, recoverDuration);
        float t = 0f;
        float startAngle = sign * strikeAngle;
        while (t < settle)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / settle);
            float decay = Mathf.Pow(1f - k, 2.2f);
            float osc = Mathf.Cos(k * Mathf.PI * 2f * settleOscillations);
            _swingAngle = startAngle * decay * osc;
            _lungeOffset = Vector3.Lerp(dir * lungeDistance, Vector3.zero, k);
            yield return null;
        }

        _swingAngle = 0f;
        ClearLunge();
        TakeRotationControl(false);
        _swingRoutine = null;
    }

    /// Tween the swing angle (and optionally a small forward slide) over time.
    private IEnumerator SwingTo(float fromAngle, float toAngle, float duration,
                                System.Func<float, float> ease,
                                float fromSlide, float toSlide)
    {
        if (_lungeSuppressed || !enableLunge) yield break;

        duration = Mathf.Max(0.02f, duration);
        Vector3 dir = ResolveStrikeDirection();
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = ease(Mathf.Clamp01(t / duration));
            _swingAngle = Mathf.LerpUnclamped(fromAngle, toAngle, k);
            _lungeOffset = dir * Mathf.LerpUnclamped(fromSlide, toSlide, k);
            yield return null;
        }
        _swingAngle = toAngle;
        _lungeOffset = dir * toSlide;
    }

    /// Borrow / return transform.rotation from the animation controller.
    private void TakeRotationControl(bool take)
    {
        if (_anim == null || _ownsRotation == take) return;
        _ownsRotation = take;
        _anim.SetOrientationDrivingEnabled(!take);
        if (!take) transform.rotation = Quaternion.identity;
    }

    /// Direction of the swing: toward whatever the boss is attacking, falling back to
    /// its facing if no target resolves.
    private Vector3 ResolveStrikeDirection()
    {
        GameObject target = Utilities.GetClosestAttackableTarget(transform.position);
        if (target != null)
        {
            Vector3 d = target.transform.position - transform.position;
            d.z = 0f;
            if (d.sqrMagnitude > 0.0001f) return d.normalized;
        }

        // No target — swing the way the sprite is facing.
        bool facingLeft = _sprite != null && _sprite.flipX;
        return facingLeft ? Vector3.left : Vector3.right;
    }

    private static float EaseIn(float k) => k * k;                        // accelerate into the hit
    private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);    // decelerate into the wind-up
    private IEnumerator SquashPop()
    {
        if (!_restingScaleCaptured || squashAmount <= 0f) yield break;

        float life = 0.22f, t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            // One damped bounce: squat and widen, then recover.
            float s = Mathf.Sin(k * Mathf.PI) * (1f - k) * squashAmount;
            transform.localScale = new Vector3(
                _restingScale.x * (1f + s),
                _restingScale.y * (1f - s),
                _restingScale.z);
            yield return null;
        }
        transform.localScale = _restingScale;
    }

    private void SpawnImpact(Vector3 pos, Vector3 dir)
    {
        pos.z = 0f;
        Color col = impactColor;
        // Pure white means "use the boss's colour", so the impact matches whatever
        // the Bellkeeper's active tint is set to in the inspector.
        if (_boss != null && col == Color.white) col = _boss.CurrentStateColor;

        Boss5ImpactVFX.Play(pos, impactRadius, col, dir);

        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(shakeAmount, shakeDuration);
    }

    private void ClearLunge()
    {
        _lungeOffset = Vector3.zero;
        _swingAngle = 0f;
        if (_restingScaleCaptured) transform.localScale = _restingScale;
    }

    // Applied AFTER everything else has moved the boss this frame, and removed at
    // the start of the next one, so the pose is pure display and never accumulates.
    private Vector3 _appliedOffset;

    private void LateUpdate()
    {
        // Undo last frame's display offset before reading the true resting position.
        if (_appliedOffset != Vector3.zero)
        {
            transform.position -= _appliedOffset;
            _appliedOffset = Vector3.zero;
        }

        if (Mathf.Abs(_swingAngle) > 0.01f)
        {
            // Rotate the whole body about a pivot ABOVE its centre. Displacing the
            // transform by (pivot + R*(pos-pivot) - pos) makes the sprite orbit that
            // point, so the skirt sweeps a wide arc while the crown stays put — the
            // difference between a bell tolling and a sprite tilting in place.
            Vector3 rest = transform.position;
            Vector3 pivot = rest + Vector3.up * swingPivotHeight;
            Quaternion rot = Quaternion.Euler(0f, 0f, _swingAngle);

            Vector3 swung = pivot + rot * (rest - pivot);
            Vector3 total = (swung - rest) + _lungeOffset;

            transform.rotation = rot;
            transform.position += total;
            _appliedOffset = total;
        }
        else if (_lungeOffset != Vector3.zero)
        {
            transform.position += _lungeOffset;
            _appliedOffset = _lungeOffset;
        }

        // Idle sway — a heavy bell rocking as it walks. Skipped while stationary in
        // State B (the boss is meant to be dead still) and while swinging.
        if (idleSwayDegrees > 0f && _swingRoutine == null && _ownsRotation
            && (_boss == null || !_boss.IsUntargetableByTowers))
        {
            float ang = Mathf.Sin(Time.time * idleSwaySpeed * Mathf.PI * 2f) * idleSwayDegrees;
            transform.rotation = Quaternion.Euler(0f, 0f, ang);
        }
    }

}


// =============================================================================
//  IMPACT VFX — the bell slamming into a tower or the core
// -----------------------------------------------------------------------------
//  WHY THE LAST VERSION WAS INVISIBLE, since the instinct is to blame sorting:
//  it wasn't sorting. These sit at orders 3198-3203 on "Default", the same band as
//  the Mechanic 1 ground circle, which renders fine. The problem was pure contrast
//  — mid-brown dust at 0.40 alpha over mid-green grass works out to about 0.06 of
//  effective contrast, which is below the threshold of noticing. Ground effects on
//  a saturated biome need to be either much LIGHTER or much DARKER than the ground,
//  never a mid-tone.
//
//  So the palette is now split deliberately:
//    · dust is PALE — warm near-white, the colour of pulverised stone catching
//      light, at high alpha. It reads instantly on grass, snow and wasteland alike.
//    · cracks and debris are NEAR-BLACK. The dark half carries the read on pale
//      biomes where the light dust washes out.
//  Having both means one effect works everywhere without sampling the biome.
// =============================================================================
public class Boss5ImpactVFX : MonoBehaviour
{
    private const int Order = 3200;

    public static void Play(Vector3 pos, float radius, Color tint, Vector3 dir)
    {
        var go = new GameObject("Boss5_BellImpact");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.AddComponent<Boss5ImpactVFX>().Run(Mathf.Max(0.2f, radius), tint, dir);
    }

    private void Run(float radius, Color tint, Vector3 dir)
    {
        float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        StartCoroutine(Scuff(radius));
        StartCoroutine(ContactFlash(radius, tint));

        // Dark radial fractures — the high-contrast half of the effect.
        const int CRACKS = 7;
        for (int i = 0; i < CRACKS; i++)
            StartCoroutine(Crack(baseAngle + Random.Range(-110f, 110f), radius));

        // Pale dust skirt. Released ALL AT ONCE from a ring at the contact edge —
        // no per-puff delay, no per-puff origin jitter. Angles are evenly spaced with
        // only a hair of variation, because the front should look connected.
        const int PUFFS = 22;
        float birth = radius * 0.30f;
        for (int i = 0; i < PUFFS; i++)
        {
            float spread = (360f / PUFFS) * i + Random.Range(-4f, 4f);
            float weight = 1f + 0.55f * Mathf.Cos((spread - baseAngle) * Mathf.Deg2Rad);
            StartCoroutine(DustSkirt(spread, radius * Mathf.Max(0.45f, weight), birth));
        }

        for (int i = 0; i < 3; i++)
            StartCoroutine(DustBillow(radius));

        const int CHUNKS = 11;
        for (int i = 0; i < CHUNKS; i++)
            StartCoroutine(Debris(baseAngle + Random.Range(-85f, 85f), radius));

        var nl = Boss5Sprites.AddNightLight(gameObject, tint, radius * 1.3f, 1.0f, 0.35f);
        if (nl != null) StartCoroutine(FadeLight(nl));

        Destroy(gameObject, 2.6f);
    }

    private IEnumerator FadeLight(NightLight nl)
    {
        float life = 0.25f, t = 0f;
        while (t < life && nl != null)
        {
            t += Time.deltaTime;
            nl.intensity = Mathf.Lerp(1.0f, 0f, t / life);
            yield return null;
        }
    }


    private IEnumerator DustSkirt(float angleDeg, float reach, float birthRadius)
    {
        var sr = Boss5Fx.Child(transform, "Dust", Boss5Sprites.GetRadialGlow(), Order);
        float rad = angleDeg * Mathf.Deg2Rad;
        var dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);

        // Born on the rim of the contact, not at its centre.
        Vector3 birth = dir * birthRadius;

        float dist = reach * Random.Range(0.55f, 0.95f);
        float life = Random.Range(0.85f, 1.35f);
        float size = reach * Random.Range(0.26f, 0.38f);
        float rise = Random.Range(0.10f, 0.30f);

        // Pale and warm, only lightly varied — a dust cloud is one material, and
        // heavy per-puff colour variation is what made it look like separate objects.
        float v = Random.Range(0.90f, 0.98f);
        var shade = new Color(v, v * 0.965f, v * 0.90f);

        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);

            float outward = 1f - Mathf.Pow(1f - k, 3.2f);
            Vector3 p = birth + dir * dist * outward;
            p.y += rise * Mathf.Sqrt(k);
            sr.transform.localPosition = p;

            // Stretched ALONG the front (perpendicular to travel) early on, so
            // neighbours merge into a wall; relaxes toward round as it disperses.
            float d = size * (0.7f + k * 1.7f);
            float stretch = Mathf.Lerp(2.3f, 1.05f, k);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg + 90f);
            sr.transform.localScale = new Vector3(d * stretch, d * 0.85f, 1f);

            Boss5Fx.Tint(sr, shade, 0.58f * Mathf.Pow(1f - k, 1.4f));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// A soft billow of dust lifting straight up out of the contact, which gives the
    /// skirt some vertical volume instead of a flat pancake.
    private IEnumerator DustBillow(float radius)
    {
        var sr = Boss5Fx.Child(transform, "Billow", Boss5Sprites.GetRadialGlow(), Order + 1);
        float life = Random.Range(1.1f, 1.6f);
        float size = radius * Random.Range(0.55f, 0.8f);
        float drift = Random.Range(-0.25f, 0.25f);
        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            sr.transform.localPosition = new Vector3(drift * k, radius * 0.55f * Mathf.Sqrt(k), 0f);
            float d = size * (0.5f + k * 1.9f);
            sr.transform.localScale = new Vector3(d, d * 0.85f, 1f);
            Boss5Fx.Tint(sr, new Color(0.95f, 0.93f, 0.87f), 0.34f * Mathf.Pow(1f - k, 1.6f));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// A dark disturbance mark under the point of contact.
    private IEnumerator Scuff(float radius)
    {
        var sr = Boss5Fx.Child(transform, "Scuff", Boss5Sprites.GetEdgeGradientDisc(), Order - 2);
        float w = radius * Random.Range(1.6f, 2.1f);
        float h = w * Random.Range(0.42f, 0.58f);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        float life = 1.8f, t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float g = 0.85f + 0.15f * Mathf.Clamp01(k * 6f);
            sr.transform.localScale = new Vector3(w * g, h * g, 1f);
            Boss5Fx.Tint(sr, new Color(0.10f, 0.08f, 0.06f), 0.55f * Mathf.Pow(1f - k, 1.3f));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// A thin dark fracture snapping outward from the point of contact.
    private IEnumerator Crack(float angleDeg, float radius)
    {
        var sr = Boss5Fx.Child(transform, "Crack", Boss5Sprites.GetSpark(), Order - 1);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

        var dir = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                              Mathf.Sin(angleDeg * Mathf.Deg2Rad), 0f);
        float len = radius * Random.Range(0.8f, 1.6f);
        float thick = radius * Random.Range(0.035f, 0.075f);
        float life = Random.Range(1.1f, 1.7f);

        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            // Snaps to full length almost instantly, then simply fades.
            float grow = Mathf.Clamp01(k / 0.08f);
            sr.transform.localScale = new Vector3(len * grow, thick, 1f);
            // Anchored at the impact, so it extends outward rather than sliding.
            sr.transform.localPosition = dir * (len * grow * 0.5f);
            Boss5Fx.Tint(sr, new Color(0.07f, 0.06f, 0.05f), 0.7f * Mathf.Pow(1f - k, 1.5f));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// Bronze-on-stone spark: small, bright, gone in a blink.
    private IEnumerator ContactFlash(float radius, Color tint)
    {
        var sr = Boss5Fx.Child(transform, "Contact", Boss5Sprites.GetRadialGlow(), Order + 3);
        float life = 0.13f, t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float d = radius * (0.6f + k * 0.6f);
            sr.transform.localScale = new Vector3(d, d * 0.75f, 1f);
            Boss5Fx.Tint(sr, Color.Lerp(Color.white, tint, k), 0.9f * (1f - k));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// Dark dirt chunks on ballistic arcs, tumbling, settling back down.
    private IEnumerator Debris(float angleDeg, float radius)
    {
        var sr = Boss5Fx.Child(transform, "Debris", Boss5Sprites.GetSpark(), Order + 2);

        var dir = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                              Mathf.Sin(angleDeg * Mathf.Deg2Rad), 0f);
        float dist = radius * Random.Range(0.6f, 1.5f);
        float life = Random.Range(0.6f, 1.05f);
        float size = radius * Random.Range(0.07f, 0.15f);
        float apex = radius * Random.Range(0.4f, 0.95f);
        float spin = Random.Range(-720f, 720f);
        var shade = new Color(Random.Range(0.13f, 0.26f),
                              Random.Range(0.11f, 0.21f),
                              Random.Range(0.08f, 0.16f));

        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);

            Vector3 p = dir * dist * (1f - Mathf.Pow(1f - k, 2f));
            p.y += apex * 4f * k * (1f - k);          // parabola, lands at k = 1
            sr.transform.localPosition = p;

            sr.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg + spin * k);
            sr.transform.localScale = new Vector3(size, size * 0.7f, 1f);
            Boss5Fx.Tint(sr, shade, k < 0.85f ? 1f : 1f - (k - 0.85f) / 0.15f);
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }
}


