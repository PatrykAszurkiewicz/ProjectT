using System.Collections;
using System.Collections.Generic;
using UnityEngine;


// Berserk enemy behaviour - hunts other nearby enemies, grows larger and stronger.


[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyController))]
// Run our Update/LateUpdate after other gameplay components — after
// YSortEntity, which rewrites transform.localScale every frame to manage sprite
// sorting and was resetting our growth back to the prefab scale. 
[DefaultExecutionOrder(10000)]
public class BerserkController : MonoBehaviour
{
    [Header("Growth Per Kill")]
    [Tooltip("Max-health multiplier gained per enemy eaten. 0.15 = +15% per kill (compounding).")]
    [SerializeField] private float healthGrowthPerKill = 0.15f;

    [Tooltip("Fraction of (new) max health restored per kill. 0.25 = heal 25% of max " +
             "on each kill. Set to 1 for a full refill, 0 to gain max-HP capacity " +
             "without any healing.")]
    [Range(0f, 1f)]
    [SerializeField] private float healFractionPerKill = 0.25f;

    [Tooltip("Damage multiplier gained per enemy eaten. 0.15 = +15% per kill (compounding). " +
             "Applied to the cloned EnemyData.damage so it never touches the shared asset.")]
    [SerializeField] private float damageGrowthPerKill = 0.15f;

    [Tooltip("Sprite/transform scale multiplier gained per enemy eaten. 0.25 = +25% bigger per kill (compounding).")]
    [SerializeField] private float scaleGrowthPerKill = 0.25f;

    [Tooltip("Safety cap on total scale relative to the prefab's authored scale. " +
             "Prevents a Berserk on a kill streak from filling the whole screen. " +
             "e.g. 4 = at most 4x the prefab's starting size.")]
    [SerializeField] private float maxScaleMultiplier = 4f;

    [Header("Hunting")]
    [Tooltip("Physics layers to scan for huntable enemies. Leave at 0 (Nothing) " +
             "to fall back to scanning all EnemyStats in the scene (slower but " +
             "works without layer setup).")]
    [SerializeField] private LayerMask enemyScanLayers;

    [Header("Eat / Inflate VFX")]
    [Tooltip("Duration of the inflation squash-and-stretch pop when an enemy is eaten.")]
    [SerializeField] private float inflateDuration = 0.35f;

    [Tooltip("Peak overshoot of the inflation pop, as a fraction above the new resting scale. " +
             "0.4 = briefly balloons to 1.4x the target before settling.")]
    [SerializeField] private float inflateOvershoot = 0.4f;

    [Tooltip("Number of particles spat out when an enemy is eaten.")]
    [SerializeField] private int eatParticleCount = 18;

    [Tooltip("Tint of the eat-burst particles and the brief feed flash.")]
    [SerializeField] private Color eatParticleColor = new Color(1f, 0.35f, 0.15f, 1f);

    [Tooltip("If true, logs growth state to the Console on each kill.")]
    [SerializeField] private bool debugLogs = false;

    private EnemyStats stats;
    private EnemyController controller;
    private EnemyAnimationController animController;
    private SmoothSpriteFlip smoothFlip;
    private SpriteRenderer spriteRenderer;

    // The enemy we are currently hunting, observed so we can tell when it dies.
    private Transform huntTarget;
    private EnemyStats huntTargetStats;

    private int kills = 0;

    // The resting scale
    private Vector3 restingScale;
    private Vector3 prefabScale;
    // The transform we actually scale — the one holding the visible
    // SpriteRenderer, which may be a child of the root.
    private Transform scaleTarget;
    // The scale we assert each LateUpdate. Equals restingScale at rest; the
    // inflate pop animates it above restingScale and back.
    private Vector3 displayScale;
    // Set when restingScale changes (a kill) so we sync the flip's base scale
    // once on the next LateUpdate rather than every frame.
    private bool restingScaleDirty = false;
    private Coroutine inflateCoroutine;

    // Reusable overlap buffer to avoid per-scan allocations.
    private static readonly Collider2D[] _scanBuffer = new Collider2D[64];

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        controller = GetComponent<EnemyController>();
        animController = GetComponent<EnemyAnimationController>();

        // Drive the controller's targeting through the composition hook. 
        controller.PriorityTargetProvider = GetNearestEnemyTarget;
    }

    private void Start()
    {

        ResolveScaleTarget();
    }

    private void ResolveScaleTarget()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        scaleTarget = (spriteRenderer != null) ? spriteRenderer.transform : transform;
        smoothFlip = scaleTarget.GetComponent<SmoothSpriteFlip>();

        prefabScale = scaleTarget.localScale;
        restingScale = prefabScale;
        displayScale = prefabScale;

        if (debugLogs)
        {
            Debug.Log($"[Berserk] Scale target resolved to '{scaleTarget.name}' " +
                      $"(isRoot={scaleTarget == transform}, startScale={prefabScale}, " +
                      $"flipOnTarget={(smoothFlip != null)})");
        }
    }

    // Enforce our scale AFTER SmoothSpriteFlip's LateUpdate 
    private void LateUpdate()
    {
        if (scaleTarget == null) return;


        if (smoothFlip == null)
            smoothFlip = scaleTarget.GetComponent<SmoothSpriteFlip>();


        if (restingScaleDirty && smoothFlip != null)
        {

            scaleTarget.localScale = restingScale;
            smoothFlip.RecaptureBaseScale();
            restingScaleDirty = false;
        }


        if (smoothFlip != null && smoothFlip.IsFlipping)
            return;


        scaleTarget.localScale = displayScale;
    }

    private void Update()
    {

        if (debugLogs && scaleTarget != null && kills > 0)
        {
            Vector3 live = scaleTarget.localScale;
            if ((live - displayScale).sqrMagnitude > 0.0000001f)
            {
                Debug.LogWarning($"[Berserk] STILL overwritten despite execution order. " +
                                 $"wrote={displayScale} live={live}. YSortEntity (or another " +
                                 $"component) writes localScale after order 10000 or re-reads it. " +
                                 $"Child-visual fallback needed.");
            }
            else
            {
                Debug.Log($"[Berserk] Scale holding at {live} ✓ (growth is sticking)");
            }
        }

        // Detect a kill: we were hunting something and it has now died/vanished.
        if (huntTargetStats != null)
        {
            if (huntTargetStats.IsDead() || huntTarget == null || huntTarget.gameObject == null
                || !huntTarget.gameObject.activeInHierarchy)
            {
                // Only count it as OUR kill if it actually died (not just wandered
                // out of existence due to scene teardown). IsDead() covers the
                // "we damaged it to death" case; a destroyed-but-not-dead object
                // (rare) is treated conservatively as a kill too, since we were
                // the one engaging it.
                OnEnemyEaten();
                huntTarget = null;
                huntTargetStats = null;
            }
        }

        // Keep our observed hunt target in sync 
        if (huntTargetStats == null)
            RefreshObservedTarget();
    }

    // Mirrors the controller's current target when that target is an enemy
    private void RefreshObservedTarget()
    {
        Transform ct = controller.CurrentTarget;
        if (ct == null) { return; }

        var es = ct.GetComponent<EnemyStats>();
        if (es != null && es != stats && !es.IsDead())
        {
            huntTarget = ct;
            huntTargetStats = es;
        }
    }


    // Priority-target provider handed to EnemyController. Returns the nearest
    // living enemy within detect range, or null if there isn't one (letting
    // the controller fall back to player/tower/core).

    private Transform GetNearestEnemyTarget()
    {
        float range = controller.DetectRange;
        Transform best = null;
        float bestSqr = float.MaxValue;

        if (enemyScanLayers.value != 0)
        {
            var filter = new ContactFilter2D { useLayerMask = true, layerMask = enemyScanLayers, useTriggers = true };
            int count = Physics2D.OverlapCircle(transform.position, range, filter, _scanBuffer);
            for (int i = 0; i < count; i++)
            {
                var col = _scanBuffer[i];
                if (col == null) continue;
                var es = col.GetComponentInParent<EnemyStats>();
                Transform cand = EvaluateCandidate(es);
                if (cand == null) continue;
                float d = ((Vector2)cand.position - (Vector2)transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = cand; }
            }
        }
        else
        {
            // Fallback: scan all enemies in the scene. Fine for the modest
            // enemy counts in this game; avoids requiring a layer set up.
            var all = Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);
            float rangeSqr = range * range;
            foreach (var es in all)
            {
                Transform cand = EvaluateCandidate(es);
                if (cand == null) continue;
                float d = ((Vector2)cand.position - (Vector2)transform.position).sqrMagnitude;
                if (d <= rangeSqr && d < bestSqr) { bestSqr = d; best = cand; }
            }
        }

        return best;
    }

    // Returns the transform of a valid huntable enemy, or null if the candidate
    // is us, dead, or another Berserk (Berserks don't eat each other).
    private Transform EvaluateCandidate(EnemyStats es)
    {
        if (es == null) return null;
        if (es == stats) return null;                          // never target self
        if (es.IsDead()) return null;
        if (es.GetComponent<BerserkController>() != null) return null; // don't hunt other Berserks
        return es.transform;
    }

    //  Growth on kill 

    private void OnEnemyEaten()
    {
        kills++;

        //  Health: +15% max per kill (compounding), then heal a fraction of max. 
        float oldMax = stats.maxHealth;
        float newMax = oldMax * (1f + healthGrowthPerKill);
        stats.maxHealth = newMax;

        // Heal a fraction of the new max (clamped to max). 
        if (healFractionPerKill > 0f)
            stats.currentHealth = Mathf.Min(stats.currentHealth + newMax * healFractionPerKill, newMax);

        // Push the new ceiling
        var bar = stats.GetHealthBar();
        if (bar != null)
            bar.SetMaxHealth(stats.maxHealth, stats.currentHealth);

        //  Damage: +15% per kill on the CLONED EnemyData (safe to mutate). 

        if (stats.enemyData != null)
        {
            stats.enemyData.damage *= (1f + damageGrowthPerKill);
        }

        //  Scale: +25% per kill (compounding), capped. 
        float targetMult = Mathf.Min(
            Mathf.Pow(1f + scaleGrowthPerKill, kills),
            maxScaleMultiplier);
        restingScale = prefabScale * targetMult;
        restingScaleDirty = true; // recapture flip base once, next LateUpdate

        if (debugLogs)
        {
            Debug.Log($"[Berserk] Ate enemy #{kills}: maxHP={stats.maxHealth:F0} " +
                      $"dmg={stats.enemyData?.damage:F1} scaleMult={targetMult:F2}");
        }

        //  Juice 
        if (inflateCoroutine != null) StopCoroutine(inflateCoroutine);
        inflateCoroutine = StartCoroutine(InflatePop());
        SpawnEatParticles();
        StartCoroutine(FeedFlash());
    }

    /// Squash-and-stretch inflation toward the new resting scale
    private IEnumerator InflatePop()
    {
        Vector3 from = displayScale;
        float t = 0f;
        float dur = Mathf.Max(0.05f, inflateDuration);

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            // Overshoot envelope: rise past target, then settle.
            // sin(pi*k) gives a 0->1->0 hump for the overshoot component.
            float overshoot = Mathf.Sin(k * Mathf.PI) * inflateOvershoot;
            float ease = 1f - (1f - k) * (1f - k); // quadratic ease-out toward target

            Vector3 baseScaleNow = Vector3.Lerp(from, restingScale, ease);

            // Squash & stretch: stretch wider than tall on the way up, sells the gulp.
            float stretchX = 1f + overshoot;
            float stretchY = 1f + overshoot * 0.6f;
            displayScale = new Vector3(
                baseScaleNow.x * stretchX,
                baseScaleNow.y * stretchY,
                baseScaleNow.z);

            yield return null;
        }

        // Settle exactly on the resting scale.
        displayScale = restingScale;
        inflateCoroutine = null;
    }

    // Brief warm flash on the sprite to punctuate the feed.
    private IEnumerator FeedFlash()
    {
        if (spriteRenderer == null) yield break;
        Color original = spriteRenderer.color;
        Color flash = Color.Lerp(original, eatParticleColor, 0.6f);

        float dur = 0.12f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            spriteRenderer.color = Color.Lerp(flash, original, t / dur);
            yield return null;
        }
        spriteRenderer.color = original;
    }

    // Particles 

    private void SpawnEatParticles()
    {
        if (eatParticleCount <= 0) return;

        var host = new GameObject("[BerserkEatVFX]");
        host.transform.position = transform.position;

        var vfx = host.AddComponent<BerserkEatParticles>();
        int order = spriteRenderer != null ? spriteRenderer.sortingOrder + 1 : 1001;
        string layer = spriteRenderer != null ? spriteRenderer.sortingLayerName : "Default";
        float spread = Mathf.Max(0.5f, restingScale.magnitude); // bigger Berserk → wider burst
        vfx.Emit(eatParticleCount, eatParticleColor, order, layer, spread);
    }

    private void OnDestroy()
    {
        // Drop our hook so a pooled/teardown controller doesn't keep calling us.
        if (controller != null && controller.PriorityTargetProvider == GetNearestEnemyTarget)
            controller.PriorityTargetProvider = null;
    }

    private void OnDrawGizmosSelected()
    {
        // Visualise the hunt scan radius (matches the controller's detect range
        // when playing; falls back to a nominal value in edit mode).
        float r = (controller != null) ? controller.DetectRange : 3f;
        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, r);
    }
}

/// Tiny self-contained particle burst for the Berserk's eat effect. 
public class BerserkEatParticles : MonoBehaviour
{
    private struct Mote
    {
        public Transform tr;
        public SpriteRenderer sr;
        public Vector2 vel;
        public float life;
        public float maxLife;
        public float startScale;
        public Color color;
    }

    private readonly List<Mote> _motes = new List<Mote>();
    private float _elapsed = 0f;
    private float _maxLifeAll = 0f;

    private static Sprite _moteSprite;

    public void Emit(int count, Color color, int sortingOrder, string sortingLayer, float spread)
    {
        Sprite sprite = GetMoteSprite();

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("mote");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;
            sr.sortingLayerName = sortingLayer;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(1.5f, 4.5f) * Mathf.Clamp(spread, 0.5f, 3f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            float life = Random.Range(0.35f, 0.7f);
            _maxLifeAll = Mathf.Max(_maxLifeAll, life);

            // Slight per-mote color variation around the requested tint.
            Color c = color;
            c.r = Mathf.Clamp01(c.r * Random.Range(0.85f, 1.1f));
            c.g = Mathf.Clamp01(c.g * Random.Range(0.85f, 1.1f));
            sr.color = c;

            float startScale = Random.Range(0.08f, 0.18f) * Mathf.Clamp(spread, 0.5f, 3f);
            go.transform.localScale = Vector3.one * startScale;

            _motes.Add(new Mote
            {
                tr = go.transform,
                sr = sr,
                vel = dir * speed,
                life = 0f,
                maxLife = life,
                startScale = startScale,
                color = c
            });
        }
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        for (int i = 0; i < _motes.Count; i++)
        {
            var m = _motes[i];
            if (m.tr == null) continue;

            m.life += Time.deltaTime;
            float t = Mathf.Clamp01(m.life / m.maxLife);

            m.vel.y -= 4f * Time.deltaTime;  // gentle gravity
            m.vel *= 0.94f;                  // drag
            m.tr.position += (Vector3)m.vel * Time.deltaTime;

            float s = Mathf.Lerp(m.startScale, 0f, t * t);
            m.tr.localScale = Vector3.one * Mathf.Max(0.001f, s);

            Color c = m.color;
            c.a = 1f - t * t;
            m.sr.color = c;

            _motes[i] = m;
        }

        if (_elapsed >= _maxLifeAll + 0.05f)
            Destroy(gameObject);
    }

    private static Sprite GetMoteSprite()
    {
        if (_moteSprite != null) return _moteSprite;

        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[size * size];
        Vector2 ctr = new Vector2(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), ctr) / (size * 0.5f));
                px[y * size + x] = new Color(1f, 1f, 1f, v * v);
            }
        tex.SetPixels(px);
        tex.Apply();
        _moteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 32f);
        return _moteSprite;
    }
}

