using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// BRUTE CONTROLLER
[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(EnemyAnimationController))]
public class BruteController : MonoBehaviour
{
    [Header("Slam Cadence")]
    [Tooltip("Seconds between consecutive fist slams. Larger = slower, heavier " +
             "rhythm. Each slam alternates to the other fist.")]
    [SerializeField] private float slamInterval = 0.5f;

    [Tooltip("Delay before the FIRST slam after the Brute engages. Gives the " +
             "player a beat to react / parry the wind-up before the ground hits " +
             "begin.")]
    [SerializeField] private float slamStartDelay = 0.3f;

    [Tooltip("How long (seconds) the Brute can stop 'attacking' before the slam " +
             "loop ends. Bridges the tiny gaps between EnemyController attack " +
             "cycles so the rhythm stays smooth, while still stopping promptly " +
             "when the Brute leaves range to chase.")]
    [SerializeField] private float attackStateGrace = 0.4f;

    [Header("Fist Placement (left / right of the Brute)")]
    [Tooltip("Sideways (left/right) distance from the Brute's body to each fist " +
             "impact, in world units. The two slams always land horizontally to " +
             "the Brute's left and right — they never rotate up/down toward the " +
             "target — so smaller = the two slams sit closer to the body and " +
             "closer together.")]
    [SerializeField] private float fistLateralOffset = 0.35f;

    [Tooltip("Extra vertical nudge of the impact point in world units (negative = " +
             "toward the ground / the Brute's feet). Tune so the explosion sits on " +
             "the floor rather than at body height.")]
    [SerializeField] private float fistGroundOffset = -0.35f;

    [Tooltip("If true the first slam of an engagement is the LEFT fist, then it " +
             "alternates right/left/right...")]
    [SerializeField] private bool firstSlamIsLeft = true;

    [Header("Slam Damage")]
    [Tooltip("World radius of each slam's damage area. Everything damageable " +
             "inside this radius (player, towers, core) is hit by that slam.")]
    [SerializeField] private float slamDamageRadius = 1.2f;

    // Each slam deals EnemyData.damage (routed through the shared,
    // parry-aware EnemyController.ApplyDamageToTarget path). Because the Brute
    // lands several slams while engaged, keep EnemyData.damage modest so the
    // total isn't overwhelming.

    [Header("Slam VFX")]
    [Tooltip("World radius of the explosion visual (purely cosmetic; the damage " +
             "area is 'slamDamageRadius').")]
    [SerializeField] private float slamVfxRadius = 0.8f;

    [Tooltip("Warm dust tint for the slam explosion.")]
    [SerializeField] private Color slamVfxColor = new Color(0.85f, 0.6f, 0.32f, 1f);

    [Tooltip("Small camera shake per slam for feel (0 = none). Safely ignored if " +
             "no CameraShake instance exists in the scene.")]
    [SerializeField] private float slamCameraShake = 0.06f;

    [Header("Death")]
    [Tooltip("Disintegration VFX duration on death. Values >= 1.0 use the full " +
             "sprite-shatter; below 1.0 use the lighter chunk disintegration. " +
             "0 disables (not recommended for the Brute).")]
    [SerializeField] private float deathVfxDuration = 1.2f;

    // Cached refs
    private EnemyStats stats;
    private EnemyController controller;
    private EnemyAnimationController animController;

    // Slam loop state
    private Coroutine slamLoop;
    private float lastAttackingTime = -999f;
    private int slamParity = 0; // even = firstSlamIsLeft, odd = other fist

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();

        // Enable the disintegration death VFX from code so we don't depend on
        // the prefab inspector value being set. No-op if duration <= 0.
        if (stats != null && deathVfxDuration > 0f)
            stats.ConfigureDeathVfx(deathVfxDuration, destroyHealthBarBeforeVfx: true);
    }

    private void Start()
    {
        controller = GetComponent<EnemyController>();
        animController = GetComponent<EnemyAnimationController>();

        // Suppress the default single-target melee hit. EnemyController.PerformHit
        // calls this override INSTEAD of dealing melee damage (and skips the
        // default attack sound), so the Brute's only damage source is its slams.
        if (controller != null)
            controller.AttackHandlerOverride = _ => { /* Brute damages via slams */ };
    }

    private void Update()
    {
        if (controller == null) return;

        // Track the most recent moment we were in an attack cycle. IsAttacking
        // is true for (almost) the whole time the Brute is stopped at its target;
        // the brief gaps between cycles are bridged by 'attackStateGrace'.
        if (controller.IsAttacking)
            lastAttackingTime = Time.time;

        bool engaged = (Time.time - lastAttackingTime) <= attackStateGrace;

        if (engaged && slamLoop == null)
            slamLoop = StartCoroutine(SlamLoop());
    }

    private IEnumerator SlamLoop()
    {
        // Reset alternation so each engagement starts on the configured fist.
        slamParity = 0;

        // Wind-up beat before the first ground hit (also the parry window).
        float waited = 0f;
        while (waited < slamStartDelay)
        {
            if (!IsEngaged()) { slamLoop = null; yield break; }
            waited += Time.deltaTime;
            yield return null;
        }

        while (IsEngaged())
        {
            // Don't slam while parry-stunned — wait it out, then resume.
            if (GetComponent<ParryStunEffect>() != null)
            {
                yield return null;
                continue;
            }

            bool leftFist = (slamParity % 2 == 0) ? firstSlamIsLeft : !firstSlamIsLeft;
            slamParity++;

            DoSlam(leftFist);

            // Wait the interval (re-checking engagement so we stop promptly).
            float t = 0f;
            while (t < slamInterval)
            {
                if (!IsEngaged()) { slamLoop = null; yield break; }
                t += Time.deltaTime;
                yield return null;
            }
        }

        slamLoop = null;
    }

    private bool IsEngaged()
    {
        if (controller == null) return false;
        if (controller.IsAttacking) lastAttackingTime = Time.time;
        return (Time.time - lastAttackingTime) <= attackStateGrace;
    }

    private void DoSlam(bool leftFist)
    {
        Vector3 impact = GetSlamCenter(leftFist);

        // Visual explosion (reuses the Hammer's procedural rock/dust sprites).
        BruteSlamVFX.Spawn(impact, slamVfxRadius, slamVfxColor);

        // A little punch.
        if (slamCameraShake > 0f && CameraShake.Instance != null)
            CameraShake.Instance.Shake(slamCameraShake, 0.12f);

        PlaySlamSound(impact);

        // AoE damage.
        ApplySlamDamage(impact);
    }

    // The world-space impact point for this slam. The two fists land purely to
    // the Brute's LEFT and RIGHT (horizontal), with a downward ground offset.
    private Vector3 GetSlamCenter(bool leftFist)
    {
        Vector3 origin = transform.position;
        float side = leftFist ? -1f : 1f;
        return new Vector3(
            origin.x + side * fistLateralOffset,
            origin.y + fistGroundOffset,
            origin.z);
    }

    private void ApplySlamDamage(Vector3 center)
    {
        if (controller == null) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, slamDamageRadius);
        if (hits == null || hits.Length == 0) return;

        // De-dupe: an enemy/tower can carry several colliders; hit each root once.
        var damaged = new HashSet<GameObject>();

        foreach (var h in hits)
        {
            if (h == null) continue;

            Transform targetT = null;

            // Characters with health (the player). Skip enemies (no friendly
            // fire / self damage) — EnemyStats derives from CharacterStats.
            var cs = h.GetComponentInParent<CharacterStats>();
            if (cs != null)
            {
                if (cs is EnemyStats) continue;
                targetT = cs.transform;
            }
            else
            {
                // Energy consumers (towers / core).
                var consumer = h.GetComponentInParent<IEnergyConsumer>();
                if (consumer != null)
                {
                    var comp = consumer as Component;
                    if (comp != null) targetT = comp.transform;
                }
            }

            if (targetT == null) continue;
            if (!damaged.Add(targetT.gameObject)) continue;

            // Reuse the controller's existing damage path. 
            controller.ApplyDamageToTarget(targetT);
        }
    }

    private void PlaySlamSound(Vector3 at)
    {
        // Reuse the generic enemy-attack sound, guarded so missing audio never throws.
        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.enemyAttack, at);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Visualize the two fist impact points (left / right of the body) and
        // their damage radius.
        Vector3 left = transform.position + new Vector3(-fistLateralOffset, fistGroundOffset, 0f);
        Vector3 right = transform.position + new Vector3(fistLateralOffset, fistGroundOffset, 0f);

        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.85f);
        Gizmos.DrawWireSphere(left, slamDamageRadius);
        Gizmos.DrawWireSphere(right, slamDamageRadius);
    }
#endif
}


// SMALL GROUND-SLAM EXPLOSION VFX
public class BruteSlamVFX : MonoBehaviour
{
    // Render above the player (whose YSort sits around ~1000), like the
    // hammer's dust/debris layers.
    private const int DustOrder = 5200;
    private const int DebrisOrder = 5400;
    private const int FlashOrder = 5600;

    public static void Spawn(Vector3 position, float radius, Color tint)
    {
        var go = new GameObject("BruteSlamVFX");
        go.transform.position = position;
        go.AddComponent<BruteSlamVFX>().Play(Mathf.Max(0.2f, radius), tint);
    }

    private float _radius;
    private Color _tint;

    private void Play(float radius, Color tint)
    {
        _radius = radius;
        _tint = tint;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        BuildDustDisc();
        BuildCoreFlash();
        BuildDebris();

        // Live long enough for the longest child coroutine (debris) to finish.
        yield return new WaitForSeconds(1.2f);
        Destroy(gameObject);
    }

    // Soft expanding ground-hugging dust cloud.
    private void BuildDustDisc()
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingOrder = DustOrder;
        Color c = _tint; c.a = 0.7f;
        sr.color = c;
        StartCoroutine(ExpandFade(go.transform, sr, _radius * 1.1f, _radius * 2.0f, 0.45f));
    }

    // Sharp bright central pop.
    private void BuildCoreFlash()
    {
        var go = new GameObject("Flash");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingOrder = FlashOrder;
        Color hot = Color.Lerp(_tint, Color.white, 0.7f); hot.a = 0.95f;
        sr.color = hot;
        StartCoroutine(ExpandFade(go.transform, sr, _radius * 0.5f, _radius * 1.1f, 0.22f));
    }

    // A few arcing rock chunks.
    private void BuildDebris()
    {
        int chunks = Random.Range(4, 7);
        for (int i = 0; i < chunks; i++)
        {
            var go = new GameObject("Chunk");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            float size = _radius * Random.Range(0.12f, 0.22f);
            go.transform.localScale = Vector3.one * size;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = HammerSlamSystem.GetRockChunkSprite();
            sr.sortingOrder = DebrisOrder;
            sr.color = Color.Lerp(_tint, Color.black, 0.25f);

            // Launch up and outward.
            float ang = Random.Range(20f, 160f) * Mathf.Deg2Rad;
            float speed = _radius * Random.Range(2.2f, 3.6f);
            Vector2 vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed;
            if (Random.value < 0.5f) vel.x = -vel.x;

            StartCoroutine(DebrisArc(go.transform, sr, vel));
        }
    }

    private IEnumerator ExpandFade(Transform t, SpriteRenderer sr, float fromScale, float toScale, float dur)
    {
        float e = 0f;
        Color baseCol = sr.color;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / dur);
            float eased = 1f - (1f - k) * (1f - k); // ease-out
            float s = Mathf.Lerp(fromScale, toScale, eased);
            if (t != null) t.localScale = Vector3.one * s;
            if (sr != null)
            {
                Color c = baseCol; c.a = baseCol.a * (1f - k);
                sr.color = c;
            }
            yield return null;
        }
        if (sr != null) { Color c = sr.color; c.a = 0f; sr.color = c; }
    }

    private IEnumerator DebrisArc(Transform t, SpriteRenderer sr, Vector2 vel)
    {
        const float gravity = -14f;
        float life = Random.Range(0.55f, 0.9f);
        float e = 0f;
        Vector3 pos = t != null ? t.localPosition : Vector3.zero;
        float spin = Random.Range(-360f, 360f);
        Color baseCol = sr != null ? sr.color : Color.white;

        while (e < life)
        {
            float dt = Time.deltaTime;
            e += dt;
            vel.y += gravity * dt;
            pos += (Vector3)(vel * dt);
            // Settle on the "ground" (impact Y) and stop falling further.
            if (pos.y < 0f) { pos.y = 0f; vel = Vector2.zero; }
            if (t != null)
            {
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(0f, 0f, t.localEulerAngles.z + spin * dt);
            }
            // Fade out over the last third of life.
            if (sr != null)
            {
                float fadeK = Mathf.InverseLerp(life * 0.6f, life, e);
                Color c = baseCol; c.a = baseCol.a * (1f - fadeK);
                sr.color = c;
            }
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }
}

