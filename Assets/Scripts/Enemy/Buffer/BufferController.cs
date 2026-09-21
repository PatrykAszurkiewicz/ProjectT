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

    // ── Obstacle avoidance ─────────────────────────────────────────────────
    // BUGFIX: the Buffer had NONE of this either. Its FixedUpdate computed
    // `dir = toTarget.normalized` and wrote it straight to linearVelocity, so it
    // homed in a dead straight line and simply pressed into anything in the way.
    // Every other walker (EnemyController) steers around obstacles, which is why
    // they get past walls the Buffer parks against.
    //
    // The failure is worst on obstacles PERPENDICULAR to the approach — a
    // horizontal wall hit while heading straight up produces a contact with zero
    // tangential component, so the physics slide that rescues glancing hits does
    // nothing at all and he stalls forever.
    //
    // This is a compact version of EnemyController's steering: repel from every
    // nearby blocker at once, and when a wall is dead ahead commit to one tangent
    // instead of grinding into it.

    [Header("Obstacle Avoidance")]
    [Tooltip("Layers the Buffer steers around. SET THIS to the same mask as " +
             "EnemyController's Obstacle Layer (plus its Blocker Layers if you " +
             "use them). Left empty, avoidance is disabled and the Buffer walks " +
             "in straight lines exactly as it did before.")]
    [SerializeField] private LayerMask obstacleLayers;

    [Tooltip("How far around itself the Buffer senses obstacles. Should be a " +
             "little larger than its own collider radius.")]
    [SerializeField] private float avoidRadius = 1.6f;

    [Tooltip("How hard nearby obstacles push the heading away. ~1.5-2.5 reads " +
             "as natural; higher looks evasive.")]
    [SerializeField] private float avoidStrength = 1.8f;

    [Tooltip("Heading easing. Higher turns faster; lower gives wider, smoother arcs.")]
    [SerializeField] private float steerSmoothing = 8f;

    [Tooltip("Dot product past which an obstacle counts as DEAD AHEAD and the " +
             "Buffer commits to sliding along it rather than pushing into it. " +
             "-0.85 is roughly 32 degrees either side of head-on.")]
    [Range(-1f, 0f)][SerializeField] private float headOnThreshold = -0.85f;

    private Vector2 smoothHeading = Vector2.zero;
    private float committedTangent = 0f;   // 0 = not currently rounding anything
    private static readonly Collider2D[] _avoidScan = new Collider2D[16];
    private ContactFilter2D _avoidFilter;
    private bool _avoidFilterReady;

    //  Procedural-visual hooks (consumed by BufferVisual) 
    // Read-only windows onto state the visual needs and nothing else does.
    // None of these change behaviour; with no BufferVisual attached the
    // provider stays null, the event has no subscribers, and SpawnFog runs
    // exactly as it did before.

    /// 0 → 1 progress toward the next fog drop. Drives the charge telegraph
    /// (eye glow, sigil spin-up, shards pulling inward) so the player can see
    /// a cloud coming and walk out of where it will land.
    public float FogCharge01 =>
        fogDropInterval <= 0.0001f ? 1f : Mathf.Clamp01(fogDropTimer / fogDropInterval);

    /// The ally (or fallback player/core) the Buffer is currently walking to.
    /// Lets the visual keep facing its support target while standing still,
    /// where there is no velocity to derive a facing from.
    public Transform CurrentTarget => currentTarget;

    /// Radius of the clouds this Buffer drops, so the drop ring can preview the
    /// real hitbox rather than an invented size.
    public float FogRadius => fogRadius;

    /// Optional override for where fog is spawned. BufferVisual points this at
    /// its censer so the cloud visibly falls out of the thurible. Left null the
    /// Buffer's own transform is used, i.e. the original behaviour.
    [System.NonSerialized] public System.Func<Vector3> fogEmitPointProvider;

    /// Fired immediately after a fog patch is configured, with the world
    /// position it was spawned at.
    public event System.Action<Vector3> OnFogSpawned;

    // Crowd-control state.
    // BUGFIX: the Buffer previously had NONE of this. Its FixedUpdate gated only on
    // IsDead() and then wrote rb.linearVelocity directly, which meant it walked
    // straight through freeze effects (Ice Armor), overwrote any knockback on the
    // same physics step it was applied, kept moving while parry-stunned, and carried
    // on patrolling after the core died and the run was over. Every other enemy
    // (EnemyController, BomberController) gates on all four. Mirrors the same
    // surface so callers can drive it the same way.
    private bool isFrozen = false;
    private float freezeTimeRemaining = 0f;
    private bool isKnockedBack = false;
    private float knockbackTimer = 0f;
    private Vector2 knockbackVelocity;
    private Color restingColor = Color.white;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteFlip = GetComponent<SmoothSpriteFlip>();

        // Resting tint, captured before anything can recolour it, so Unfreeze()
        // restores the prefab's own colour rather than a hardcoded white.
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) restingColor = spriteRenderer.color;

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

        // Tick the freeze timer. Knockback is timed in FixedUpdate, which is also
        // where its velocity is decayed and written - deliberately NOT here as well,
        // since decrementing the same timer in both callbacks is what made knockback
        // last half its configured duration on EnemyController and BomberController.
        if (isFrozen)
        {
            freezeTimeRemaining -= Time.deltaTime;
            if (freezeTimeRemaining <= 0f) Unfreeze();
        }

        // A frozen or parry-stunned Buffer stops working, not just stops walking:
        // it must not keep laying fog patches while it is meant to be disabled.
        // Same for after the run has ended.
        if (isFrozen) return;
        if (IsParryStunned()) return;
        if (EnergyManager.Instance != null && EnergyManager.Instance.IsGameOver()) return;

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

        // Crowd-control gates, in the same priority order EnemyController uses.
        // BUGFIX: none of these existed, so the Buffer ignored freeze, knockback,
        // parry stun and game-over entirely.

        // Frozen: stop dead.
        if (isFrozen)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Parry stun - IsStunActive, NOT mere component presence. Powerful Parry
        // (331) leaves the component alive after the freeze as a damage debuff, so
        // testing for the component would pin the Buffer forever.
        if (IsParryStunned())
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Run is over: stop moving instead of patrolling a dead map.
        if (EnergyManager.Instance != null && EnergyManager.Instance.IsGameOver())
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Knockback: let it play out before resuming our own steering, otherwise the
        // seek below overwrites the knockback velocity on the very frame it lands.
        if (isKnockedBack)
        {
            if (rb.bodyType == RigidbodyType2D.Dynamic)
            {
                knockbackTimer -= Time.fixedDeltaTime;
                knockbackVelocity *= 0.82f;
                rb.linearVelocity = knockbackVelocity;

                if (knockbackTimer <= 0f)
                {
                    isKnockedBack = false;
                    rb.linearVelocity = Vector2.zero;
                }
            }
            else
            {
                isKnockedBack = false;
            }
            return;
        }

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
        dir = SteerAroundObstacles(rb.position, dir);
        rb.linearVelocity = dir * stats.MoveSpeed;
    }

    // Blends the straight-line desire with a push away from every blocker in
    // range. Probes from rb.position, which is the same point the rest of the
    // codebase treats as "where this enemy is".
    private Vector2 SteerAroundObstacles(Vector2 selfPos, Vector2 desired)
    {
        if (obstacleLayers.value == 0)
        {
            smoothHeading = desired;   // avoidance off: behave exactly as before
            return desired;
        }

        if (!_avoidFilterReady)
        {
            _avoidFilter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
            _avoidFilter.SetLayerMask(obstacleLayers);
            _avoidFilterReady = true;
        }

        int hits = Physics2D.OverlapCircle(selfPos, avoidRadius, _avoidFilter, _avoidScan);

        Vector2 push = Vector2.zero;
        for (int i = 0; i < hits; i++)
        {
            var col = _avoidScan[i];
            if (col == null) continue;
            if (col.attachedRigidbody == rb) continue;   // never repel from self

            Vector2 closest = col.ClosestPoint(selfPos);
            Vector2 away = selfPos - closest;
            float d = away.magnitude;

            // Already overlapping: ClosestPoint returns our own position, so
            // fall back to the collider's centre for a usable direction.
            if (d < 0.0001f)
            {
                away = selfPos - (Vector2)col.transform.position;
                d = away.magnitude;
                if (d < 0.0001f) continue;
            }

            // Quadratic falloff — distant walls barely register, close ones dominate.
            float w = 1f - Mathf.Clamp01(d / avoidRadius);
            push += (away / d) * (w * w);
        }

        Vector2 goal;
        if (push.sqrMagnitude < 0.000001f)
        {
            committedTangent = 0f;       // clear of everything; forget the last wall
            goal = desired;
        }
        else
        {
            Vector2 n = push.normalized;

            // Dead ahead? Adding the push would just cancel the desire and leave
            // a zero-length heading — which is precisely the horizontal-wall
            // stall. Slide along the surface instead.
            if (Vector2.Dot(desired, n) <= headOnThreshold)
            {
                Vector2 tangent = new Vector2(-n.y, n.x);

                // Commit to one side for the whole encounter. Re-choosing every
                // frame is what makes an enemy jitter left-right against a wall
                // instead of actually rounding it.
                if (committedTangent == 0f)
                    committedTangent = Vector2.Dot(tangent, desired) >= 0f ? 1f : -1f;

                goal = tangent * committedTangent + n * 0.35f;
            }
            else
            {
                committedTangent = 0f;
                goal = desired + push * avoidStrength;
            }
        }

        if (goal.sqrMagnitude < 0.000001f) goal = desired;
        goal = goal.normalized;

        // Frame-rate independent easing, so turns are wide arcs rather than snaps.
        if (smoothHeading.sqrMagnitude < 0.000001f) smoothHeading = goal;
        float t = 1f - Mathf.Exp(-steerSmoothing * Time.fixedDeltaTime);
        smoothHeading = Vector2.Lerp(smoothHeading, goal, t).normalized;

        return smoothHeading;
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

    // Crowd-control public API. Mirrors the surface on EnemyController and
    // BomberController so freeze / knockback callers can drive the Buffer the same
    // way. NOTE: the Buffer has no EnemyController, so a caller that reaches for
    // GetComponent<EnemyController>() will still miss it - see the note in the
    // review about hoisting this onto an ICrowdControllable interface.
    public void ApplyFreeze(float duration)
    {
        isFrozen = true;
        freezeTimeRemaining = Mathf.Max(freezeTimeRemaining, duration);
        if (spriteRenderer != null) spriteRenderer.color = Color.cyan;
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    private void Unfreeze()
    {
        isFrozen = false;
        freezeTimeRemaining = 0f;
        if (spriteRenderer != null) spriteRenderer.color = restingColor;
    }

    public void ApplyKnockback(Vector2 direction, float force, float duration = 0.25f)
    {
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;
        isKnockedBack = true;
        knockbackTimer = duration;
        knockbackVelocity = direction.normalized * force;
        rb.linearVelocity = knockbackVelocity;
    }

    public bool IsFrozen() => isFrozen;

    private bool IsParryStunned()
    {
        var parryStun = GetComponent<ParryStunEffect>();
        return parryStun != null && parryStun.IsStunActive;
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

        // Spawn point: the censer, when a procedural visual has told us where
        // that is. The offset is a small fraction of fogRadius, so this is
        // visually meaningful and gameplay-neutral.
        Vector3 spawnPos = transform.position;
        if (fogEmitPointProvider != null)
        {
            Vector3 p = fogEmitPointProvider();
            // Keep the cloud on the gameplay plane even if the visual rig has
            // been pushed toward the camera by a Z offset.
            spawnPos = new Vector3(p.x, p.y, transform.position.z);
        }

        GameObject fogGO;
        if (fogPrefab != null)
        {
            fogGO = Instantiate(fogPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            // Procedural fallback: build a fog GameObject from scratch so the
            // designer doesn't need to wire a prefab to get the enemy working.
            fogGO = new GameObject("BufferFog");
            fogGO.transform.position = spawnPos;
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
                                         // ScaleDamage gives the fog's DIRECT player damage the same
                                         // augment x per-stage x difficulty multiplier melee gets. It
                                         // previously applied difficulty ONLY, so the enemy-damage
                                         // augment and per-stage scaling silently skipped it.
            playerDamagePerSecond: stats != null
                ? stats.ScaleDamage(fogPlayerDamagePerSecond)
                : fogPlayerDamagePerSecond,
            attacker: this.gameObject);

        visual.Configure(
            radius: fogRadius,
            duration: fogDuration,
            enableMist: fogEnableMist,
            enableWisps: fogEnableWisps,
            enableLightning: fogEnableLightning,
            enableStasisStorm: fogEnableStasisStorm);

        // Fired last, so a listener that inspects the world sees a fully
        // configured cloud rather than a half-built one.
        OnFogSpawned?.Invoke(spawnPos);
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


