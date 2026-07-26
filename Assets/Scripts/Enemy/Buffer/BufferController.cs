using UnityEngine;

// Controller for the Buffer enemy. Walks toward the nearest non-Buffer enemy on the field. On a fixed cadence (fogDropInterval), spawns a BufferFog patch at its current position.

[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(Rigidbody2D))]
public class BufferController : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("Radius the Buffer scans for ally enemies to walk toward. " +
             "Large by default — Buffer is meant to seek out the pack.")]
    [SerializeField] private float allySearchRadius = 30f;

    [Tooltip("How close (world units) the Buffer wants to be to its ally " +
             "target before stopping. Keeps it from grinding into the ally's " +
             "collider.")]
    [SerializeField] private float stoppingDistance = 1.2f;

    [Tooltip("If true, when no ally enemy is in range the Buffer falls back " +
             "to walking toward the player (and then the core if no player " +
             "exists). Prevents the Buffer from idling in place when it's " +
             "the last enemy alive — which would soft-lock the wave because " +
             "the Buffer deals no direct damage and never finishes off the " +
             "player/core on its own. Strongly recommended ON.")]
    [SerializeField] private bool fallbackToPlayerOrCoreIfNoAlly = true;

    [Header("Fog")]
    [Tooltip("Prefab spawned as the lingering fog patch. If left null, a " +
             "GameObject is created procedurally at runtime (with " +
             "BufferFog + BufferFogVisual attached).")]
    [SerializeField] private GameObject fogPrefab;

    [Tooltip("Seconds between fog drops.")]
    [SerializeField] private float fogDropInterval = 2.0f;

    [Tooltip("Lifetime of each spawned fog patch.")]
    [SerializeField] private float fogDuration = 5f;

    [Tooltip("Radius of each spawned fog patch.")]
    [SerializeField] private float fogRadius = 2.5f;

    [Tooltip("Damage multiplier applied to enemies standing in the fog. " +
             "Same semantics as ScarecrowStasisAura.damageBuff.")]
    [SerializeField] private float fogDamageBuff = 1.25f;

    [Tooltip("Damage per second dealt to the player while inside the fog.")]
    [SerializeField] private float fogPlayerDamagePerSecond = 6f;

    [Header("Fog Visual Systems")]
    [Tooltip("Toggle the soft purple mist body. Off = no fog cloud, only " +
             "the other enabled visuals (if any).")]
    [SerializeField] private bool fogEnableMist = true;

    [Tooltip("Toggle the curling pale tendrils sprouting from the cloud.")]
    [SerializeField] private bool fogEnableWisps = false;

    [Tooltip("Toggle the rare bright lightning flash.")]
    [SerializeField] private bool fogEnableLightning = true;

    [Tooltip("Toggle the continuous subtle electric threads (stasis storm).")]
    [SerializeField] private bool fogEnableStasisStorm = true;

    [Header("Visuals")]
    [Tooltip("If true, the Buffer flips its sprite to face its current ally " +
             "target via SmoothSpriteFlip (when present). The component is " +
             "optional — flipping is skipped silently if not attached.")]
    [SerializeField] private bool flipToFaceTarget = true;

    [Tooltip("Duration of the disintegration VFX played when the Buffer dies. " +
             "Set at runtime via EnemyStats.ConfigureDeathVfx() so we don't " +
             "depend on the prefab inspector having the right value. " +
             "Below 1.0 = 'classic chunks' disintegration; 1.0+ = boss-style " +
             "sprite-shatter. 0 disables entirely.")]
    [SerializeField] private float deathVfxDuration = 0.7f;

    // Cached references
    private EnemyStats stats;
    private Rigidbody2D rb;
    private SmoothSpriteFlip spriteFlip;

    // Reused buffer for Physics2D queries; avoids per-frame allocation.
    private static readonly Collider2D[] _allyScanBuffer = new Collider2D[64];
    private static readonly ContactFilter2D _allyScanFilter = new ContactFilter2D().NoFilter();

    private float fogDropTimer;
    private Transform currentTarget;
    private float smokeShufflePhase;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteFlip = GetComponent<SmoothSpriteFlip>();

        // Auto-add Y-sort entity 
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
            ysort.sortYOffset = -0.2f;
        }

        // Configure the death VFX on the EnemyStats
        if (stats != null && deathVfxDuration > 0f)
        {
            stats.ConfigureDeathVfx(deathVfxDuration, destroyHealthBarBeforeVfx: true);
        }

        // Stagger the first drop slightly so a wave of Buffers doesn't fire
        // their initial patches all on the same frame.
        fogDropTimer = Random.Range(0f, fogDropInterval * 0.5f);
        smokeShufflePhase = SmokeBlind.NewPhase();
    }

    private void Update()
    {
        if (stats == null || stats.IsDead()) return;

        UpdateTarget();
        UpdateFacing();

        fogDropTimer += Time.deltaTime;
        if (fogDropTimer >= fogDropInterval)
        {
            fogDropTimer = 0f;
            SpawnFog();
        }
    }

    private void FixedUpdate()
    {
        if (stats == null || stats.IsDead() || rb == null) return;

        if (currentTarget == null)
        {
            // No ally to support — hold position. Killing residual velocity
            // here keeps the Buffer from drifting after a nudge.
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Smoke Screen: if a smoke cloud blocks our sightline to the target, we
        // lose sight of it and mill in place until the smoke clears.
        if (SmokeBlind.Blocks(transform.position, currentTarget.position))
        {
            rb.linearVelocity = SmokeBlind.ShuffleVelocity(smokeShufflePhase, stats.MoveSpeed);
            return;
        }

        Vector2 toTarget = (Vector2)currentTarget.position - rb.position;
        float dist = toTarget.magnitude;

        if (dist <= stoppingDistance)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 dir = toTarget / dist; // normalized without re-sqrt
        rb.linearVelocity = dir * stats.MoveSpeed;
    }

    private void UpdateTarget()
    {
        // Re-scan every frame. The search isn't free, but Buffer counts are
        // expected to be low and the buffer is reused to avoid GC churn.
        int hits = Physics2D.OverlapCircle(
            transform.position, allySearchRadius, _allyScanFilter, _allyScanBuffer);

        Transform best = null;
        float bestSqr = float.PositiveInfinity;

        for (int i = 0; i < hits; i++)
        {
            var col = _allyScanBuffer[i];
            if (col == null) continue;

            var es = col.GetComponentInParent<EnemyStats>();
            if (es == null) continue;
            if (es == stats) continue;          // not self
            if (es.IsDead()) continue;
            // Don't chase another Buffer — they shouldn't clump on each other.
            if (es.GetComponent<BufferController>() != null) continue;
            // Don't chase Gremlins — they flee the player and aren't proper
            // combat allies. Same exclusion ScarecrowStasisAura uses.
            if (es.GetComponent<GremlinController>() != null) continue;

            float sqr = ((Vector2)es.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = es.transform;
            }
        }

        // Fallback: if no ally was found and the option is on, walk toward
        // the player (preferred) or the core.
        if (best == null && fallbackToPlayerOrCoreIfNoAlly)
        {
            // Co-op: nearest alive player (retargets if one goes down).
            // includeCloaked:true preserves the Buffer's original cloak-agnostic
            // fallback. With one player this is identical to the old single lookup.
            var nearestPlayer = PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true);
            if (nearestPlayer != null)
            {
                best = nearestPlayer.transform;
            }
            else
            {
                GameObject coreGO = GameObject.FindGameObjectWithTag("Core");
                if (coreGO != null) best = coreGO.transform;
            }
        }

        currentTarget = best;
    }

    private void UpdateFacing()
    {
        if (!flipToFaceTarget || spriteFlip == null || currentTarget == null) return;

        float dx = currentTarget.position.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.05f) return; // ignore micro-jitter
        // SmoothSpriteFlip's public API is SetFacingLeft(bool). It's already
        // idempotent and debounced, so calling it every frame is safe.
        // dx < 0 → target is to our left → face left.
        spriteFlip.SetFacingLeft(dx < 0f);
    }

    private void PlaySmokeSound()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (FMODEvents.instance.bufferSmoke.IsNull) return;
        AudioManager.instance.PlayOneShot(FMODEvents.instance.bufferSmoke, transform.position);
    }

    private void SpawnFog()
    {
        PlaySmokeSound();

        GameObject fogGO;
        if (fogPrefab != null)
        {
            fogGO = Instantiate(fogPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            // Procedural fallback: build a fog GameObject from scratch so the
            // designer doesn't need to wire a prefab to get the enemy working.
            fogGO = new GameObject("BufferFog");
            fogGO.transform.position = transform.position;
            fogGO.AddComponent<BufferFogVisual>();
            fogGO.AddComponent<BufferFog>();
        }

        var fog = fogGO.GetComponent<BufferFog>();
        if (fog == null) fog = fogGO.AddComponent<BufferFog>();

        var visual = fogGO.GetComponent<BufferFogVisual>();
        if (visual == null) visual = fogGO.AddComponent<BufferFogVisual>();

        // Configure the fog. Pass our own gameObject as attacker so any
        // "killed by" tracking attributes player damage to the Buffer.
        fog.Configure(
            radius: fogRadius,
            duration: fogDuration,
            damageBuff: fogDamageBuff,   // multiplier on allies' already-scaled damage — not scaled here
                                         // Nightmare's +30% reaches the fog's direct player damage too, matching melee.
            playerDamagePerSecond: fogPlayerDamagePerSecond * EnemyStatModifierManager.DifficultyDamageMultiplier,
            attacker: this.gameObject);

        visual.Configure(
            radius: fogRadius,
            duration: fogDuration,
            enableMist: fogEnableMist,
            enableWisps: fogEnableWisps,
            enableLightning: fogEnableLightning,
            enableStasisStorm: fogEnableStasisStorm);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.2f, 0.9f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, allySearchRadius);
        Gizmos.color = new Color(0.35f, 0.1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, fogRadius);
    }
#endif
}

