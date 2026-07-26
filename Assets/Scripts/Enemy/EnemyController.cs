using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;

public class EnemyController : MonoBehaviour
{
    // Per-enemy melee attack sound. Assign this on the prefab (e.g. InsectAttack
    // on the Insect, SlimeAttack on the Slime, WolfAttack on the Wolf). When left
    // empty the enemy falls back to the shared FMODEvents.enemyAttack, so existing
    // enemies are unaffected. Pitcher and other ranged enemies use an attack
    // override and play their own sound instead (see PerformHit / PitcherController).
    [Header("Attack SFX (optional per-enemy override)")]
    [SerializeField] private EventReference attackSoundOverride;
    private EnemyStats stats;
    private Rigidbody2D rb;

    private bool isKnockedBack = false;
    private float knockbackTimer = 0f;

    [SerializeField] private float detectRange = 5f;
    private Transform coreTarget;
    private Transform currentTarget;

    [SerializeField] private float attackRange = 1.7f;
    [SerializeField] private float attackCooldown = 1f;

    private float attackTimer = 0f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float avoidDistance = 1f;
    [SerializeField] private LayerMask obstacleLayer;

    [Tooltip("EXTRA layers (beyond Obstacle Layer) that the steering and stuck " +
             "systems treat as solid things to arc around — e.g. the layer your " +
             "towers sit on, so a DESTROYED tower's rubble is bypassed in a wide " +
             "arc instead of trapping enemies against it. Leave as Nothing to " +
             "change nothing. If your towers are already on the Obstacle Layer, " +
             "you don't need this.")]
    [SerializeField] private LayerMask blockerLayers;

    [Tooltip("If true, the enemy will not start an attack cycle when a layout " +
             "obstacle (wall/building) is between it and its target. Prevents " +
             "enemies stuck on the wrong side of a wide wall from playing the " +
             "attack animation forever against an unreachable target.")]
    [SerializeField] private bool requireLineOfSightToAttack = true;

    [Header("Stuck Prevention")]
    [SerializeField] private float stuckCheckTime = 0.5f;
    [Tooltip("Progress toward target (in world units) required during stuckCheckTime to avoid being marked stuck. " +
             "Measures progress along the direction to the target, so sliding along a wall counts as no progress.")]
    [SerializeField] private float minMovementThreshold = 0.05f;

    [Header("Smooth Steering & Avoidance")]
    [Tooltip("Master switch for the smoothed, multi-obstacle steering. When OFF " +
             "the enemy uses the original single-obstacle perpendicular avoidance " +
             "(GetOptimalMovementDirectionLegacy), so you can A/B the old feel.")]
    [SerializeField] private bool smoothSteeringEnabled = true;

    [Tooltip("How far around itself the enemy senses obstacles for steering. " +
             "Bigger = it starts curving away EARLIER and takes a WIDER berth, " +
             "instead of scraping the wall. Usually a touch larger than " +
             "avoidDistance.")]
    [SerializeField] private float lookAheadDistance = 1.6f;

    [Tooltip("How hard nearby obstacles push the heading away from them. Higher = " +
             "wider detours around walls and out of tight gaps between two " +
             "obstacles. Too high looks evasive; ~1.5-2.5 is natural.")]
    [SerializeField] private float avoidanceStrength = 1.8f;

    [Tooltip("How quickly the heading turns toward where it WANTS to go. This is " +
             "the anti-jitter knob: LOWER = smoother, lazier, wider arcs; HIGHER " +
             "= snappier turns. Heading is eased every physics step, so direction " +
             "never snaps (which is what reads as shaking).")]
    [SerializeField] private float steerResponsiveness = 6f;

    [Tooltip("Strength of a slow, organic wander layered on top of movement so " +
             "paths look natural and a pack doesn't walk a single line. Driven by " +
             "smooth Perlin noise (continuous), NOT per-frame Random, so it never " +
             "jitters. 0 = perfectly straight pathing.")]
    [SerializeField] private float wanderStrength = 0.25f;

    [Tooltip("How fast the wander direction drifts. LOW = long, lazy sways; HIGH " +
             "= busier weaving. Keep low for the 'smooth, larger movement' feel.")]
    [SerializeField] private float wanderFrequency = 0.4f;

    [Tooltip("When genuinely stuck, how far the slide also pulls OFF the wall " +
             "(away from its surface) for a wider berth, instead of grinding " +
             "along the face. Bigger = the enemy swings out further to round the " +
             "obstacle.")]
    [SerializeField] private float stuckArcWidth = 0.6f;

    // Persistent heading we ease toward the goal each FixedUpdate. Keeping this
    // between frames (rather than recomputing velocity from scratch) is what
    // makes turns smooth and wide instead of jittery. Zero = "needs reseeding".
    private Vector2 smoothHeading = Vector2.zero;

    // Per-enemy offset into the Perlin field so wanders aren't synchronized.
    private float wanderSeed;

    // Reused scratch + filter for the multi-obstacle avoidance scan (no GC).
    private static readonly Collider2D[] _avoidScan = new Collider2D[16];
    private ContactFilter2D _avoidFilter;
    private bool _avoidFilterReady;

    // Layer-independent blocker contact. Captured from real physics collisions so
    // we can peel off any solid we touch — even one a designer forgot to put on
    // the Obstacle/Blocker layers (the classic "stuck behind a tree" case). The
    // normal points AWAY from the blocker; it's remembered briefly so the steer
    // stays smooth across the frames between contacts.
    private Vector2 _contactNormal;
    private float _lastBlockerContactTime = -999f;
    private const float BLOCKER_CONTACT_MEMORY = 0.2f;
    private bool HasRecentBlockerContact =>
        (Time.time - _lastBlockerContactTime) <= BLOCKER_CONTACT_MEMORY
        && _contactNormal.sqrMagnitude > 0.0001f;

    [Header("Grappling")]
    private bool isBeingGrappled = false;
    private float grapplingEndTime = 0f;

    private Vector2 lastKnownPosition;
    private float timeSinceLastMovement = 0f;
    private bool isInStuckMode = false;
    private float stuckModeTimer = 0f;
    private Vector2 stuckAvoidanceDirection;

    //  Smoke Screen (vision blocking) 
    // When a SmokeScreenCloud sits on the sightline between this enemy and its
    // current target, the enemy "loses sight" and mills in place until the
    // smoke clears (see SmokeBlocksTarget / DoSmokeShuffle). Bosses are exempt
    // so their scripted attack patterns aren't disrupted.
    private float smokeShufflePhase;
    private bool isBoss;

    // Cached Boss1 reference. Boss1 is present from spawn on boss objects and is
    // never added at runtime, so it's safe to cache once (unlike status effects
    // such as ParryStunEffect, which come and go and must still be probed live).
    // This removes a GetComponent<Boss1>() from FixedUpdate — a per-physics-frame,
    // per-enemy cost that did nothing for the (overwhelmingly common) non-boss case.
    private Boss1 boss1;

    [Header("Freeze System")]
    private bool isFrozen = false;
    private float freezeTimeRemaining = 0f;
    private Color originalColor;
    private SpriteRenderer spriteRenderer;

    private bool isAttackingCycle = false;
    private EnemyAnimationController animController;

    // Knockback direct velocity
    private Vector2 knockbackVelocity;

    //  Attack timing state 
    private float attackCycleStartTime = -999f; // Time.time when current attack cycle began

    //  Resolved frame config (from EnemyData) 
    private int resolvedHitFrame;
    private int resolvedParryStart;
    private int resolvedParryEnd;

    /// The Time.time when the current attack cycle started.
    /// Used by ParryIndicator to synchronize timing with IsInParryWindow().
    public float AttackCycleStartTime => attackCycleStartTime;
    public bool IsAttacking => isAttackingCycle;

    //  Decoy lure system 
    private Transform decoyTarget;
    private bool isLuredByDecoy = false;

    // How close to the decoy the enemy will walk before stopping
    private const float DECOY_STOP_DISTANCE = 0.6f;

    public float GetAttackCooldown() => attackCooldown;

    // Detection radius is configured per-prefab; expose it so companion
    // behaviours (e.g. BerserkController) can scan the same range the
    // controller uses for target acquisition without duplicating the value.
    public float DetectRange => detectRange;

    // The transform this enemy is currently moving toward / attacking.
    // Read-only; lets companion behaviours observe what the controller picked
    // (e.g. to notice when a hunted enemy dies). Null when no valid target.
    public Transform CurrentTarget => currentTarget;

    // Optional priority target hook. Returns null by default — vanilla enemies
    // keep their normal player/tower/core selection. Override (or have a
    // companion component drive this via a delegate) to redirect targeting,
    // e.g. so the Berserk hunts other enemies before anything else.
    protected virtual Transform GetPriorityTarget()
    {
        return PriorityTargetProvider != null ? PriorityTargetProvider() : null;
    }

    // Composition-friendly alternative to subclassing: a companion component
    // on the same GameObject can assign this to inject a priority target.
    // Kept null for every existing prefab, so behaviour is unchanged.
    public System.Func<Transform> PriorityTargetProvider;

    // Optional attack hook. When assigned (by a companion component, e.g.
    // PitcherController), it REPLACES the default melee hit at the moment the
    // attack lands — same composition idiom as PriorityTargetProvider above.
    // Ranged enemies use this to spawn a projectile instead of dealing instant
    // melee damage, while still reusing all of EnemyController's movement,
    // target acquisition, stop-at-range and attack-cycle/animation timing.
    // Null for every existing prefab, so behaviour is unchanged.
    public System.Action<Transform> AttackHandlerOverride;

    // True when this enemy delivers its hit through a projectile (Mort, Pitcher,
    // …) rather than an instant melee connect. Used to suppress the melee-style
    // head parry indicator on ranged enemies — their shots are parried in flight
    // by the projectile-parry path, not by reacting to the throw animation.
    public bool HasAttackOverride => AttackHandlerOverride != null;

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        animController = GetComponent<EnemyAnimationController>();

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;

        // Cache Boss1 once (see field comment). Also reused for the isBoss checks
        // below so we don't GetComponent<Boss1> three separate times at startup.
        boss1 = GetComponent<Boss1>();

        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;

            bool isBoss = boss1 != null || GetComponent<BaseBossStats>() != null;
            ysort.sortYOffset = isBoss ? -1.0f : -0.2f;
        }

        // Cache boss status (smoke-blinding is skipped for bosses) and a random
        // phase so a group of smoke-blinded enemies doesn't shuffle in lock-step.
        isBoss = boss1 != null || GetComponent<BaseBossStats>() != null;
        smokeShufflePhase = Random.value * 6.2831853f;

        // Per-enemy wander offset, and a cached contact filter for the
        // multi-obstacle avoidance scan (built once; rebuilt lazily if the
        // obstacle layer is assigned later).
        wanderSeed = Random.value * 1000f;
        BuildAvoidFilter();

        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
            coreTarget = core.transform;

        currentTarget = coreTarget;
        InvokeRepeating(nameof(UpdateTarget), 0f, 0.5f);

        // Resolve frame config once at start
        ResolveFrameConfig();
    }

    private void OnDestroy()
    {
    }

    /// Reads frame config from EnemyData. All hit/parry frame configuration lives on the EnemyData ScriptableObject — one place, no duplication.
    private void ResolveFrameConfig()
    {
        EnemyData data = stats?.enemyData;

        if (data != null)
        {
            resolvedHitFrame = Mathf.Max(data.hitFrame, 0);
            resolvedParryStart = Mathf.Max(data.parryFrameStart, 0);
            resolvedParryEnd = Mathf.Max(data.parryFrameEnd, 0);
        }
        else
        {
            resolvedHitFrame = 0;
            resolvedParryStart = 0;
            resolvedParryEnd = 0;
        }

        if (resolvedParryEnd < resolvedParryStart)
            resolvedParryEnd = resolvedParryStart;
    }

    //  Decoy target API (called by DecoyDevice) 
    // Called by DecoyDevice to lure this enemy towards the decoy.
    // While lured, normal target selection is overridden.

    public void SetDecoyTarget(Transform decoy)
    {
        decoyTarget = decoy;
        isLuredByDecoy = true;
        currentTarget = decoy;
    }

    // Called by DecoyDevice when the decoy expires or is replaced.
    // Enemy returns to normal target selection.
    public void ClearDecoyTarget()
    {
        decoyTarget = null;
        isLuredByDecoy = false;
        currentTarget = coreTarget;
        UpdateTarget();
    }

    public bool IsLuredByDecoy() => isLuredByDecoy;

    public void SetGrapplingState(bool isGrappled, float duration = 2f)
    {
        isBeingGrappled = isGrappled;
        if (isGrappled)
            grapplingEndTime = Time.time + duration;
    }

    private void FixedUpdate()
    {
        if (isBeingGrappled && Time.time > grapplingEndTime)
            isBeingGrappled = false;

        if (isFrozen)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        // Parry stun — completely freeze movement (only while the stun freeze
        // phase is active; a longer Powerful-Parry damage debuff must NOT keep the
        // enemy frozen after the stun itself has ended).
        var parryStunMove = GetComponent<ParryStunEffect>();
        if (parryStunMove != null && parryStunMove.IsStunActive)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        if (EnergyManager.Instance != null && EnergyManager.Instance.IsGameOver())
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        // Uses the cached Boss1 reference (assigned in Start, before the first
        // FixedUpdate) instead of a per-physics-frame GetComponent<Boss1>().
        if (boss1 != null)
        {
            if (animController != null && animController.IsPlayingLaserAttack())
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                return;
            }
        }

        if (isBeingGrappled)
        {
            if (rb != null) rb.linearVelocity *= 0.95f;
            return;
        }

        // Knockback direct velocity with decay
        if (isKnockedBack)
        {
            if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
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

        // If lured by decoy, validate the decoy still exists
        if (isLuredByDecoy)
        {
            if (decoyTarget == null || decoyTarget.gameObject == null || !decoyTarget.gameObject.activeInHierarchy)
            {
                ClearDecoyTarget();
            }
            else
            {
                currentTarget = decoyTarget;
            }
        }

        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            if (!IsValidTarget(currentTarget))
                UpdateTarget();
            return;
        }

        // Smoke Screen: if a smoke cloud blocks our sightline to the target, we
        // "lose sight" of it. Rather than pathing around (it's a vision wall,
        // not a solid one — we don't know where to go), mill in place until the
        // smoke clears. Bosses are exempt.
        if (!isBoss && SmokeBlocksTarget(currentTarget))
        {
            DoSmokeShuffle();
            return;
        }

        float distance = Vector2.Distance(transform.position, currentTarget.position);

        // When lured by decoy, use a much tighter stop distance so they cluster around it
        if (isLuredByDecoy && currentTarget == decoyTarget)
        {
            if (distance <= DECOY_STOP_DISTANCE)
            {
                rb.linearVelocity = Vector2.zero;
                smoothHeading = Vector2.zero; // re-seed heading when we move again
                return;
            }
        }
        else if (distance <= attackRange || isAttackingCycle)
        {
            // Only freeze in place if we can actually reach the target.
            // If a wall is blocking line-of-sight, fall through to the
            // movement code below so stuck-detection can route us around it.
            if (isAttackingCycle || HasLineOfSightToTarget(currentTarget))
            {
                rb.linearVelocity = Vector2.zero;
                smoothHeading = Vector2.zero; // re-seed heading when we move again
                return;
            }
        }

        HandleStuckDetection();
        Vector2 direction = (currentTarget.position - transform.position).normalized;
        direction = ComputeSteeredDirection(direction);

        // Lured enemies move slightly slower (confused)
        float speedMultiplier = isLuredByDecoy ? 0.8f : 1f;
        rb.linearVelocity = direction.normalized * stats.MoveSpeed * speedMultiplier;
    }

    public bool IsBeingGrappled() => isBeingGrappled;

    private void HandleStuckDetection()
    {
        // Measure progress TOWARD the target, not raw movement.
        // An enemy sliding along a wall has high raw distance but ~0 progress
        // toward the target, so we still detect it as stuck.
        Vector2 displacement = (Vector2)transform.position - lastKnownPosition;
        Vector2 toTargetRaw = (Vector2)currentTarget.position - lastKnownPosition;
        float progress;
        if (toTargetRaw.sqrMagnitude > 0.0001f)
            progress = Vector2.Dot(displacement, toTargetRaw.normalized);
        else
            progress = displacement.magnitude;

        if (progress > minMovementThreshold)
        {
            timeSinceLastMovement = 0f;
            lastKnownPosition = transform.position;
            isInStuckMode = false;
        }
        else
        {
            timeSinceLastMovement += Time.fixedDeltaTime;
            if (timeSinceLastMovement > stuckCheckTime && !isInStuckMode)
                EnterStuckMode();
        }

        if (isInStuckMode)
        {
            stuckModeTimer -= Time.fixedDeltaTime;
            if (stuckModeTimer <= 0f)
            {
                // Only release stuck mode when we've actually cleared the
                // obstacle. If we're still pressed against one (e.g. a long
                // wall whose end we haven't rounded yet), refresh the slide
                // direction — we might have reached a corner where the
                // previously blocked side is now clear.
                bool stillBlocked = Physics2D.OverlapCircle(
                    transform.position, avoidDistance, CombinedBlockerMask) != null;
                if (stillBlocked)
                {
                    EnterStuckMode();
                    // Reset the movement-progress baseline so we don't
                    // immediately re-trigger the "no progress" path.
                    timeSinceLastMovement = 0f;
                    lastKnownPosition = transform.position;
                }
                else
                {
                    isInStuckMode = false;
                }
            }
        }
    }

    private void EnterStuckMode()
    {
        isInStuckMode = true;
        stuckModeTimer = 2f;

        // Find what we're actually pressed against, so we slide ALONG the
        // wall (perpendicular to its surface normal) rather than perpendicular
        // to "direction to target", which is only correct when the wall and
        // target happen to be axis-aligned with each other.
        Collider2D wall = Physics2D.OverlapCircle(
            transform.position, avoidDistance, CombinedBlockerMask);

        Vector2 wallNormal;
        if (wall != null)
        {
            Vector2 selfPos = transform.position;
            Vector2 closest = wall.ClosestPoint(selfPos);
            wallNormal = selfPos - closest;

            // If we're inside the collider, ClosestPoint may return our own
            // position. Fall back to the collider centre direction.
            if (wallNormal.sqrMagnitude < 0.0001f)
                wallNormal = selfPos - (Vector2)wall.transform.position;

            if (wallNormal.sqrMagnitude < 0.0001f)
            {
                // Total fallback — treat target as the reference.
                Vector2 toT = ((Vector2)currentTarget.position - selfPos).normalized;
                wallNormal = new Vector2(-toT.y, toT.x);
            }
            else
            {
                wallNormal = wallNormal.normalized;
            }
        }
        else
        {
            // No obstacle found on the configured layers. If we're physically
            // touching something (e.g. an untagged tree), use that real contact
            // normal so the slide is computed against the actual blocker. Only
            // when we have neither do we fall back to perpendicular-to-target.
            if (HasRecentBlockerContact)
            {
                wallNormal = _contactNormal;
            }
            else
            {
                // Stuck without a nearby wall (e.g. jammed between enemies).
                Vector2 toT = (currentTarget.position - transform.position).normalized;
                wallNormal = new Vector2(-toT.y, toT.x);
            }
        }

        // Two ways to slide along the wall (perpendicular to its normal).
        Vector2 slideA = new Vector2(-wallNormal.y, wallNormal.x);
        Vector2 slideB = -slideA;

        // Probe further than avoidDistance: on a long segmented wall, a short
        // probe lands inside another segment of the same wall and reports
        // "blocked" on both sides. 3x avoidDistance reaches past segment
        // boundaries so we can pick the genuinely-clear direction.
        float probeDist = avoidDistance * 3f;
        Vector2 selfPos2 = transform.position;
        bool aClear = !Physics2D.OverlapCircle(selfPos2 + slideA * probeDist, 0.3f, CombinedBlockerMask);
        bool bClear = !Physics2D.OverlapCircle(selfPos2 + slideB * probeDist, 0.3f, CombinedBlockerMask);

        if (aClear && !bClear) stuckAvoidanceDirection = slideA;
        else if (bClear && !aClear) stuckAvoidanceDirection = slideB;
        else
        {
            // Both clear or both blocked: pick the side whose probe lands
            // closer to the target. Prevents systematically drifting the
            // wrong way along a long wall.
            Vector2 aEnd = selfPos2 + slideA * probeDist;
            Vector2 bEnd = selfPos2 + slideB * probeDist;
            float aDistSq = Vector2.SqrMagnitude((Vector2)currentTarget.position - aEnd);
            float bDistSq = Vector2.SqrMagnitude((Vector2)currentTarget.position - bEnd);
            stuckAvoidanceDirection = (aDistSq <= bDistSq) ? slideA : slideB;
        }
    }

    // Builds (or rebuilds) the contact filter used by the multi-obstacle
    // avoidance scan. Safe to call repeatedly.
    private void BuildAvoidFilter()
    {
        int mask = CombinedBlockerMask;
        _avoidFilter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true
        };
        _avoidFilter.SetLayerMask(mask);
        _avoidFilterReady = mask != 0;
    }

    // Obstacle Layer plus the optional extra Blocker Layers, as one mask. Used
    // everywhere the enemy needs to know "what is solid and should be arced
    // around" — steering AND stuck-mode — so dead-tower rubble is handled the
    // same way by both.
    private int CombinedBlockerMask => obstacleLayer.value | blockerLayers.value;

    // Smooth, multi-obstacle steering. Produces a heading that:
    //   • aims at the current target,
    //   • is pushed away from ALL nearby obstacles at once (so a gap between two
    //     colliders no longer traps the enemy — both walls repel it out),
    //   • carries a slow Perlin wander so motion looks organic, not robotic,
    //   • and is eased frame-to-frame so turns are wide and smooth, never a snap
    //     (the snapping is what previously read as jitter / shaking).
    // Falls back to the original behaviour when smoothSteeringEnabled is off.
    private Vector2 ComputeSteeredDirection(Vector2 desired)
    {
        if (!smoothSteeringEnabled)
            return GetOptimalMovementDirectionLegacy(desired);

        if (desired.sqrMagnitude < 0.0001f) desired = smoothHeading;
        Vector2 selfPos = transform.position;
        Vector2 goal;

        if (isInStuckMode)
        {
            // Wide arc: slide ALONG the wall but also bleed in the away-from-wall
            // push so we swing OUT and round it, rather than scraping its face.
            Vector2 offWall = ComputeAvoidanceVector(selfPos, out _);
            goal = stuckAvoidanceDirection + offWall.normalized * stuckArcWidth;
            if (goal.sqrMagnitude < 0.0001f) goal = stuckAvoidanceDirection;
        }
        else
        {
            Vector2 avoid = ComputeAvoidanceVector(selfPos, out bool blocked);
            if (blocked)
            {
                goal = desired + avoid * avoidanceStrength;

                // Obstacle dead ahead and the push cancelled our desire: commit
                // to the tangent that best preserves progress toward the target,
                // so we slip past the corner instead of stalling head-on.
                if (goal.sqrMagnitude < 0.0001f)
                {
                    Vector2 n = avoid.sqrMagnitude > 0.0001f
                        ? avoid.normalized
                        : new Vector2(-desired.y, desired.x);
                    Vector2 t1 = new Vector2(-n.y, n.x);
                    Vector2 t2 = -t1;
                    goal = (Vector2.Dot(t1, desired) >= Vector2.Dot(t2, desired)) ? t1 : t2;
                }
            }
            else
            {
                goal = desired;
            }
        }

        // Slow organic wander (continuous Perlin noise → no twitching).
        if (wanderStrength > 0f)
        {
            float n = Mathf.PerlinNoise(wanderSeed, Time.time * wanderFrequency) - 0.5f;
            goal = Rotate(goal, n * wanderStrength);
        }

        if (goal.sqrMagnitude < 0.0001f) goal = desired;
        if (goal.sqrMagnitude < 0.0001f) goal = smoothHeading;
        if (goal.sqrMagnitude > 0.0001f) goal.Normalize();

        // Seed on first use / after a stop so we don't ease out of a stale zero.
        if (smoothHeading.sqrMagnitude < 0.0001f) smoothHeading = goal;

        // Frame-rate-independent ease toward the goal heading. Lower
        // steerResponsiveness = wider, lazier, smoother arcs.
        float k = 1f - Mathf.Exp(-steerResponsiveness * Time.fixedDeltaTime);
        smoothHeading = Vector2.Lerp(smoothHeading, goal, k);

        if (smoothHeading.sqrMagnitude < 0.0001f) smoothHeading = goal;
        return smoothHeading.normalized;
    }

    // Sums a "push away" vector from every obstacle within lookAheadDistance,
    // weighted by closeness (closer = stronger, smooth quadratic falloff). This
    // is what lets the enemy thread—or back out of—a gap between two obstacles:
    // both contribute, so the resultant points cleanly out of the pinch.
    //
    // Two sources are combined:
    //   1) An overlap scan on the configured layers (Obstacle + Blocker).
    //   2) A LAYER-INDEPENDENT push from whatever the body is physically touching
    //      (captured in OnCollision*). This is the safety net for solids a
    //      designer never tagged — e.g. trees: even with no layer set up, if the
    //      enemy bumps a trunk we still know which way to peel off.
    private Vector2 ComputeAvoidanceVector(Vector2 selfPos, out bool blocked)
    {
        blocked = false;
        Vector2 sum = Vector2.zero;

        if (CombinedBlockerMask != 0)
        {
            if (!_avoidFilterReady) BuildAvoidFilter();

            int count = Physics2D.OverlapCircle(selfPos, lookAheadDistance, _avoidFilter, _avoidScan);
            for (int i = 0; i < count; i++)
            {
                var col = _avoidScan[i];
                if (col == null) continue;

                // NOTE: we intentionally do NOT skip destroyed towers here. If the
                // physics scan returned this collider, it is still enabled and solid,
                // so it physically blocks the enemy's body — dead-tower rubble very
                // much included. Skipping it (the old behaviour) is exactly what let
                // an enemy wedge against a wrecked tower with no push to escape. A
                // tower whose collider was actually removed on death simply isn't
                // returned by the scan, so nothing to do there.

                Vector2 closest = col.ClosestPoint(selfPos);
                Vector2 away = selfPos - closest;
                float d = away.magnitude;
                if (d < 0.0001f)
                {
                    // Overlapping / inside the collider — push from its centre.
                    away = selfPos - (Vector2)col.transform.position;
                    d = Mathf.Max(away.magnitude, 0.0001f);
                }

                float w = Mathf.Clamp01(1f - d / lookAheadDistance);
                w *= w; // soft ramp so distant obstacles barely nudge us
                if (w <= 0f) continue;

                sum += (away / d) * w;
                blocked = true;
            }
        }

        // Layer-independent contact push. We're literally touching a solid that
        // isn't another creature — peel away from it no matter what layer it's on.
        if (HasRecentBlockerContact)
        {
            sum += _contactNormal; // unit length; full weight since we're in contact
            blocked = true;
        }

        return sum;
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians);
        float s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // Physics tells us, layer-agnostically, what we're physically pressed against.
    // We record the direction AWAY from that surface so the steering can peel off
    // it — this is what rescues enemies stuck on untagged solids like trees.
    private void OnCollisionEnter2D(Collision2D collision) => AccumulateBlockerContact(collision);
    private void OnCollisionStay2D(Collision2D collision) => AccumulateBlockerContact(collision);

    private void AccumulateBlockerContact(Collision2D collision)
    {
        if (collision == null || collision.collider == null) return;

        // Only static-ish world solids count as "walls" to arc around. Ignore
        // other creatures (enemies / the player) so crowding isn't mistaken for a
        // wall — their separation is handled by normal seek movement, and we must
        // never treat the player we're chasing as an obstacle.
        if (collision.collider.GetComponentInParent<CharacterStats>() != null) return;

        // Average an away-from-surface direction from the contact points. Deriving
        // it from (self - contactPoint) sidesteps any ambiguity in the contact
        // normal's sign.
        Vector2 self = transform.position;
        Vector2 away = Vector2.zero;
        int n = collision.contactCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = collision.GetContact(i).point;
            Vector2 d = self - p;
            if (d.sqrMagnitude > 0.0001f) away += d.normalized;
        }

        if (away.sqrMagnitude < 0.0001f) return;
        _contactNormal = away.normalized;
        _lastBlockerContactTime = Time.time;
    }

    // Re-acquire a target the instant the current one dies, so the enemy heads
    // for the next thing (another tower, the player, or the core) instead of
    // freezing where the kill happened. Crucially it routes through UpdateTarget,
    // which honours GetPriorityTarget() — so special enemies keep their rules:
    // an Insect re-picks the nearest structure, a Berserk re-picks the nearest
    // enemy to eat, and a plain/ranged enemy re-picks nearest player/tower/core.
    private void RetargetAfterKill()
    {
        currentTarget = null;      // force a clean re-pick (skips the dead target)
        UpdateTarget();
        if (currentTarget == null) // absolute fallback so we never idle
            currentTarget = coreTarget;

        // Reset the stuck baseline (we just teleported our "intent" to a new
        // target) and drop the smoothed heading so we set off cleanly toward it.
        timeSinceLastMovement = 0f;
        lastKnownPosition = transform.position;
        isInStuckMode = false;
        smoothHeading = Vector2.zero;
    }

    private Vector2 GetOptimalMovementDirectionLegacy(Vector2 desiredDirection)
    {
        if (isInStuckMode) return stuckAvoidanceDirection;

        Collider2D obstacle = Physics2D.OverlapCircle(
            transform.position, avoidDistance, obstacleLayer);

        if (obstacle != null)
        {
            var tower = obstacle.GetComponent<Tower>();
            if (tower != null && tower.IsDestroyed())
                return desiredDirection;
            // Use the CLOSEST POINT on the collider rather than its centre.
            Vector2 selfPos = transform.position;
            Vector2 closestOnObstacle = obstacle.ClosestPoint(selfPos);

            // Defensive fallback
            Vector2 toObstacle = closestOnObstacle - selfPos;
            if (toObstacle.sqrMagnitude < 0.0001f)
                toObstacle = (Vector2)obstacle.transform.position - selfPos;

            toObstacle = toObstacle.normalized;
            Vector2 perpLeft = new Vector2(-toObstacle.y, toObstacle.x);
            Vector2 perpRight = new Vector2(toObstacle.y, -toObstacle.x);
            float leftDot = Vector2.Dot(perpLeft, desiredDirection);
            float rightDot = Vector2.Dot(perpRight, desiredDirection);
            Vector2 chosenDirection = (leftDot > rightDot) ? perpLeft : perpRight;
            // Small bias toward desired direction so the enemy curves around
            // the obstacle rather than orbiting it. 0.1 is gentle enough that
            // on long walls we don't keep getting pulled back into the wall
            // each frame (which used to delay stuck-mode triggering).
            return Vector2.Lerp(chosenDirection, desiredDirection, 0.1f);
        }

        return desiredDirection;
    }

    private void UpdateTarget()
    {
        // If lured by decoy, don't change target
        if (isLuredByDecoy && decoyTarget != null && decoyTarget.gameObject != null && decoyTarget.gameObject.activeInHierarchy)
        {
            currentTarget = decoyTarget;
            return;
        }

        // If decoy reference went stale, clear it
        if (isLuredByDecoy)
            ClearDecoyTarget();

        if (currentTarget != null &&
            (currentTarget.gameObject == null || !currentTarget.gameObject.activeInHierarchy))
            currentTarget = null;

        // Optional priority target supplied by a derived/companion behaviour
        // (e.g. BerserkController, which hunts other enemies). Default
        // implementation returns null, so vanilla enemies are unaffected and
        // fall through to the normal player/tower/core selection below.
        Transform priority = GetPriorityTarget();
        if (priority != null)
        {
            currentTarget = priority;
            return;
        }

        if (coreTarget == null) return;

        // Stealth Cloak: while the player is invisible, enemies must not
        // acquire the player as a target. Fall through to towers / core.
        if (!PlayerCloakEffect.IsActive)
        {
            // Co-op: acquire the nearest alive player within detectRange. The
            // global cloak gate above already handles invisibility, so we pass
            // includeCloaked:true and let NearestAlive pick purely by range.
            // With one player this is identical to the old single lookup.
            var nearestPlayer = PlayerRegistry.Instance.NearestAlive(
                transform.position, detectRange, includeCloaked: true);
            if (nearestPlayer != null)
            {
                currentTarget = nearestPlayer.transform;
                return;
            }
        }

        // Nearest tower within detectRange. Was GameObject.FindGameObjectsWithTag(
        // "Tower") — which allocated a fresh array and GetComponent<Tower>'d every
        // result, per enemy, every 0.5s. Now we index the shared Tower.ActiveTowers
        // list (no allocation, no per-entry GetComponent) and compare squared
        // distances to avoid the sqrt in Vector2.Distance. Same filtering as before:
        // null / inactive / IsDestroyed() towers are skipped.
        var towers = Tower.ActiveTowers;
        Vector2 selfPos = transform.position;
        float detectRangeSq = detectRange * detectRange;
        float closestSq = Mathf.Infinity;
        Transform closestTower = null;

        for (int i = 0; i < towers.Count; i++)
        {
            Tower tower = towers[i];
            if (tower == null || !tower.gameObject.activeInHierarchy) continue;
            if (tower.IsDestroyed()) continue;

            Vector2 towerPos = tower.transform.position;
            float distSq = (towerPos - selfPos).sqrMagnitude;
            if (distSq < closestSq && distSq < detectRangeSq)
            {
                closestSq = distSq;
                closestTower = tower.transform;
            }
        }

        if (closestTower != null)
        {
            currentTarget = closestTower;
            return;
        }

        currentTarget = coreTarget;
    }

    private void Update()
    {
        if (isFrozen)
        {
            freezeTimeRemaining -= Time.deltaTime;
            if (freezeTimeRemaining <= 0f)
                UnfreezeEnemy();
        }

        if (isKnockedBack)
        {
            knockbackTimer -= Time.deltaTime;
            if (knockbackTimer <= 0f)
                isKnockedBack = false;
        }

        if (currentTarget != null && !IsValidTarget(currentTarget))
        {
            currentTarget = coreTarget;
            return;
        }

        // Stealth Cloak: if the player turns invisible while this enemy is
        // already targeting them, re-acquire a target immediately instead of
        // waiting up to 0.5s for the next UpdateTarget() tick. UpdateTarget()
        // already skips the player while the cloak is active, so this hands
        // the enemy off to a tower / core right away.
        if (PlayerCloakEffect.IsActive && currentTarget != null
            && currentTarget.CompareTag("Player"))
        {
            UpdateTarget();
        }

        if (currentTarget != null && !isFrozen && !isAttackingCycle
            && (GetComponent<ParryStunEffect>()?.IsStunActive != true)
            && (EnergyManager.Instance == null || !EnergyManager.Instance.IsGameOver()))
        {
            // Boss1 owns the sprite while its laser routine runs. Opening a melee
            // cycle here would call PlayMeleeAttackAnimation(), which early-outs
            // during a laser — the damage would land on an invisible swing. Hold
            // the melee until the laser finishes; Boss1 already refuses to start a
            // laser mid-swing, so the two attacks strictly take turns.
            if (boss1 != null && boss1.IsPerformingLaserAttack)
            {
                attackTimer = 0f;
                return;
            }

            float distance = Vector2.Distance(transform.position, currentTarget.position);

            if (distance <= attackRange)
            {
                // Don't attack the decoy — just mill around it
                if (isLuredByDecoy && currentTarget == decoyTarget)
                    return;

                // If a wall/building is between us and the target, we're not
                // really "in range" — moving around the obstacle is the right
                // response, not standing here flailing the attack animation.
                if (!HasLineOfSightToTarget(currentTarget))
                {
                    attackTimer = 0f;
                    return;
                }

                // Smoke Screen blocks the sightline too — can't attack a target
                // we can't see through the smoke.
                if (!isBoss && SmokeBlocksTarget(currentTarget))
                {
                    attackTimer = 0f;
                    return;
                }

                attackTimer -= Time.deltaTime;
                if (attackTimer <= 0f)
                {
                    StartCoroutine(AttackCycle(currentTarget));
                }
            }
            else
            {
                attackTimer = 0f;
            }
        }
    }

    private IEnumerator AttackCycle(Transform target)
    {
        isAttackingCycle = true;
        attackCycleStartTime = Time.time;
        bool hitDelivered = false;

        // Calculate attack timing from EnemyData
        float animDuration = 0f;
        float animSpeed = 0f;
        if (stats != null && stats.enemyData != null)
        {
            animDuration = stats.enemyData.AttackDuration;
            animSpeed = stats.enemyData.AttackAnimSpeed;
        }

        if (animController != null && resolvedHitFrame > 0)
        {
            // Frame-driven hit: the animation coroutine calls PerformHit
            // synchronously when it reaches the hit frame. One coroutine,
            // one timeline, no drift between sprite and damage.
            animController.PlayMeleeAttackAnimation(resolvedHitFrame, () =>
            {
                if (!hitDelivered && target != null)
                {
                    hitDelivered = true;
                    PerformHit(target);
                }
            });

            // Safety fallback: if animation gets interrupted (freeze, stun, death)
            // and the callback never fired, deliver hit at the expected time via timer.
            float hitDelay = animSpeed * resolvedHitFrame;
            yield return new WaitForSeconds(hitDelay);

            if (!hitDelivered && target != null)
            {
                hitDelivered = true;
                PerformHit(target);
            }

            // Wait remaining time
            //float remainingWait = Mathf.Max(animDuration, attackCooldown) - hitDelay;
            float remainingWait = animDuration - hitDelay;

            if (remainingWait > 0f)
                yield return new WaitForSeconds(remainingWait);
        }
        else
        {
            // Instant damage at animation start (hitFrame = 0)
            if (animController != null)
                animController.PlayMeleeAttackAnimation();
            PerformHit(target);
            hitDelivered = true;

            float waitTime = Mathf.Max(animDuration, attackCooldown);
            yield return new WaitForSeconds(waitTime);
        }

        if (animController != null)
            animController.StopMeleeAttackAnimation();

        isAttackingCycle = false;
        attackTimer = attackCooldown;

    }

    // Returns true if the player's parry attempt overlaps this enemy's parry window for the current attack cycle.
    // A parry succeeds if EITHER: The shield was RAISED (right-click pressed) during the parry frames, OR The shield is currently held AND the hit lands during the parry frames.
    // Called by ShieldSystem.TryBlockOrParry().

    public bool IsInParryWindow(float shieldRaiseTime)
        => IsInParryWindow(shieldRaiseTime, 0);

    // Phase 8: the window-widening augment (332) is read for the PARRYING player,
    // so P1's "Longer Parry Window" only widens P1's parries. The old 1-arg form
    // above routes here with player 0 (single-player back-compat).
    public bool IsInParryWindow(float shieldRaiseTime, int parryingIndex)
    {
        if (!isAttackingCycle || attackCycleStartTime < 0f) return false;
        if (stats == null || stats.enemyData == null) return false;

        float animSpeed = stats.enemyData.AttackAnimSpeed;
        if (animSpeed <= 0f) return false;

        int pStart = resolvedParryStart;
        int pEnd = resolvedParryEnd;
        int hit = resolvedHitFrame;

        // If nothing configured, use a default 0.2s window before hit
        if (pStart == 0 && pEnd == 0 && hit == 0)
        {
            // Fallback: parry if shield was raised within 0.2s
            return (Time.time - shieldRaiseTime) <= 0.2f;
        }

        // Calculate absolute times for parry window.
        // Augment 332 "Longer Parry Window" makes the window OPEN earlier by
        // ExtraParryFrames. The parry is adjudicated AT the hit frame, so the
        // closing edge (pEnd+1) already sits at/after the hit — pushing it later
        // does nothing. The binding edge is the START: opening it earlier is what
        // lets a slightly-early (mistimed) raise still register as a parry.
        // Clamp the earlier edge at frame 0 — the parry window can't open before
        // the attack animation begins, so the augment's benefit is naturally
        // capped at this enemy's parryFrameStart (matches ParryIndicator).
        int effParryStart = Mathf.Max(0, pStart - ParryUpgrades.ExtraParryFramesFor(parryingIndex));
        float parryWindowStart = attackCycleStartTime + effParryStart * animSpeed;
        float parryWindowEnd = attackCycleStartTime + (pEnd + 1) * animSpeed;

        // (a) Shield was pressed (raised) during the parry window
        bool raisedDuringWindow = shieldRaiseTime >= parryWindowStart && shieldRaiseTime <= parryWindowEnd;

        // Parry = shield was PRESSED (raised) during the parry window.
        // Holding shield from before the window is just a block, not a parry.
        bool isParry = raisedDuringWindow;

        Debug.Log($"[PARRY CHECK] {gameObject.name}: shieldRaise={shieldRaiseTime:F3} now={Time.time:F3} " +
                  $"parryWindow=[{parryWindowStart:F3}-{parryWindowEnd:F3}] " +
                  $"frames={pStart}-{pEnd} hit={hit} " +
                  $"raisedDuring={raisedDuringWindow} => {(isParry ? "PARRY!" : "BLOCK")}");

        return isParry;
    }

    // Returns true if this enemy is currently mid-attack and the current time falls within its parry frames. 

    public bool IsCurrentlyInParryFrames()
    {
        if (!isAttackingCycle || attackCycleStartTime < 0f) return false;
        if (stats == null || stats.enemyData == null) return false;

        float animSpeed = stats.enemyData.AttackAnimSpeed;
        if (animSpeed <= 0f) return false;

        int pStart = resolvedParryStart;
        int pEnd = resolvedParryEnd;
        int hit = resolvedHitFrame;

        if (pStart == 0 && pEnd == 0 && hit == 0) return false;

        // Augment 332 "Longer Parry Window" opens the window earlier (see
        // IsInParryWindow). Mirror that here so any visual/telemetry consumer
        // of this method reflects the widened window too.
        // Clamp the earlier edge at frame 0 — the parry window can't open before
        // the attack animation begins, so the augment's benefit is naturally
        // capped at this enemy's parryFrameStart (matches ParryIndicator).
        int effParryStart = Mathf.Max(0, pStart - ParryUpgrades.ExtraParryFrames);
        float parryWindowStart = attackCycleStartTime + effParryStart * animSpeed;
        float parryWindowEnd = attackCycleStartTime + (pEnd + 1) * animSpeed;

        return Time.time >= parryWindowStart && Time.time <= parryWindowEnd;
    }

    private void PerformHit(Transform target)
    {
        // Ranged / custom attack hook. When a companion component has assigned
        // AttackHandlerOverride (e.g. PitcherController), it fully replaces the
        // default melee hit below — it fires on the same frame the melee hit
        // would have landed (driven by EnemyData.hitFrame), so projectile
        // release stays in sync with the attack animation.
        if (AttackHandlerOverride != null)
        {
            AttackHandlerOverride(target);
            return;
        }

        PlayAttackSound();

        // Boss1 plays an additional ground-hit sound on melee connect (cached ref)
        if (boss1 != null)
            boss1.PlayGroundHitSound();

        ApplyDamageToTarget(target);
    }

    public void ApplyDamageToTarget(Transform target, bool viaProjectile = false)
    {
        if (target == null) return;

        //  Shield block / parry check 
        // If the target is the player and they have an active shield, check blocking.
        var playerStats = target.GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            // Check for parry stun — if this enemy is still in the stun FREEZE
            // phase, skip the attack entirely. (A lingering damage debuff from
            // Powerful Parry does not silence the enemy once the freeze is over.)
            var parryStun = GetComponent<ParryStunEffect>();
            if (parryStun != null && parryStun.IsStunActive) return;

            // Projectile-delivered damage has ALREADY resolved any shield
            // interaction during flight (block / parry handled by the projectile
            // against the shot's own position + timing). Re-running the melee
            // shield check here would wrongly evaluate the parry against this
            // enemy's body/animation — the exact bug that let a Mort be
            // "melee-parried" via its throw. So skip it for projectile hits.
            if (!viaProjectile)
            {
                var weapon = target.GetComponentInChildren<Weapon>();
                if (weapon != null)
                {
                    var shield = weapon.GetShieldSystem();
                    if (shield != null && shield.TryBlockOrParry(gameObject))
                        return; // Attack was blocked or parried — no damage applied
                }
            }
        }

        var stats = target.GetComponent<CharacterStats>();
        if (stats != null)
        {
            float damageAmount = this.stats.Damage;
            stats.TakeDamage(damageAmount);

            if (playerStats != null)
            {
                CombatJuice.OnEnemyHitPlayer(target.GetComponentInParent<PlayerRef>());


                var reflectionEffect = playerStats.GetComponent<DamageReflectionEffect>();
                if (reflectionEffect != null)
                    reflectionEffect.ReflectDamage(damageAmount, gameObject);

                var iceArmorEffect = playerStats.GetComponent<IceArmorEffect>();
                if (iceArmorEffect != null)
                    iceArmorEffect.FreezeAttacker(gameObject);
            }

            if (stats.IsDead())
                RetargetAfterKill();

            return;
        }

        var consumer = target.GetComponent<IEnergyConsumer>();
        if (consumer != null && EnergyManager.Instance != null)
        {
            bool wasDestroyed = EnergyManager.Instance.DamageEnergyConsumer(
                consumer, this.stats.Damage, gameObject);

            if (wasDestroyed)
            {
                // Don't stop dead on the spot — immediately seek the next target
                // (another tower, the player, or the core). This is the fix for
                // ranged enemies (Mort/Pitcher) that previously lingered where a
                // tower used to be: their projectile routes the kill through here,
                // so they now move on to the core just like melee enemies.
                RetargetAfterKill();
            }
        }
    }

    private void PlayAttackSound()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;

        // Per-enemy override if assigned (Insect / Slime / Wolf / …), otherwise the
        // shared generic enemy attack sound.
        EventReference ev = !attackSoundOverride.IsNull
            ? attackSoundOverride
            : FMODEvents.instance.enemyAttack;

        if (!ev.IsNull)
            AudioManager.instance.PlayOneShot(ev, transform.position);
    }

    public void ApplyFreeze(float duration)
    {
        isFrozen = true;
        freezeTimeRemaining = duration;
        if (spriteRenderer != null)
            spriteRenderer.color = Color.cyan;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    private void UnfreezeEnemy()
    {
        isFrozen = false;
        freezeTimeRemaining = 0f;
        if (spriteRenderer != null)
            spriteRenderer.color = originalColor;
    }

    private bool IsValidTarget(Transform target)
    {
        if (target == null || target.gameObject == null || !target.gameObject.activeInHierarchy)
            return false;
        var tower = target.GetComponent<Tower>();
        if (tower != null && tower.IsDestroyed())
            return false;
        return true;
    }

    // Returns true when nothing on the obstacle layer sits between this enemy
    // and the target. Prevents enemies from "attacking through" a wide wall

    private bool HasLineOfSightToTarget(Transform target)
    {
        if (target == null) return false;
        if (!requireLineOfSightToAttack) return true;
        if (obstacleLayer.value == 0) return true; // no obstacle layer configured

        // Cast from the BODY centre, not transform.position. Some prefabs
        // (e.g. Slime) have CircleCollider2D offsets that move the actual
        // physical body off the transform pivot. Using transform.position
        // here would offset the LoS check by the collider offset, producing
        // false positives where the line slips around the wall corner that
        // the body is actually touching. Same on the target side.
        Vector2 from = GetBodyCentre(transform);
        Vector2 to = GetBodyCentre(target);

        Vector2 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= 0.0001f) return true;
        Vector2 dir = delta / dist;

        // Use a CIRCLECAST with a small radius
        const float LOS_PROBE_RADIUS = 0.08f;
        if (dist <= LOS_PROBE_RADIUS) return true;
        Vector2 origin = from + dir * LOS_PROBE_RADIUS;
        float castDist = dist - LOS_PROBE_RADIUS;

        RaycastHit2D hit = Physics2D.CircleCast(origin, LOS_PROBE_RADIUS, dir, castDist, obstacleLayer);
        return hit.collider == null;
    }

    // Returns the world-space centre of the first non-trigger 2D collider on
    // the given Transform, accounting for collider offset and scale. Falls
    // back to transform.position if no collider is found.
    private static Vector2 GetBodyCentre(Transform t)
    {
        if (t == null) return Vector2.zero;
        var colliders = t.GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (c != null && !c.isTrigger)
                return c.bounds.center;
        }
        return t.position;
    }

    // True when an active SmokeScreenCloud lies on the sightline between this
    // enemy's body and the target's body. Uses the same body-centre logic as
    // the obstacle LoS check so collider offsets don't skew the line.
    private bool SmokeBlocksTarget(Transform target)
    {
        if (target == null) return false;
        Vector2 from = GetBodyCentre(transform);
        Vector2 to = GetBodyCentre(target);
        return SmokeScreenCloud.BlocksSegment(from, to);
    }

    // Smoke-blinded behaviour: hold roughly in place with a gentle wander so
    // the enemy reads as "milling / waiting" rather than frozen, and keep the
    // stuck-detection baseline reset so it doesn't trip the wall-avoidance
    // slide the instant the smoke clears.
    private void DoSmokeShuffle()
    {
        if (rb != null)
        {
            float speed = stats != null ? stats.MoveSpeed : 2f;
            float t = Time.time * 2.5f + smokeShufflePhase;
            Vector2 jitter = new Vector2(Mathf.Sin(t), Mathf.Cos(t * 1.27f));
            rb.linearVelocity = jitter * (speed * 0.18f);
        }

        timeSinceLastMovement = 0f;
        lastKnownPosition = transform.position;
        isInStuckMode = false;
        attackTimer = 0f;
    }

    /// SAFE knockback — checks rigidbody type before setting velocity.
    /// Bosses with static/kinematic bodies won't crash.
    public void ApplyKnockback(Vector2 direction, float force, float duration = 0.25f)
    {
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
        {
            //Debug.Log($"[CombatFeel] KNOCKBACK SKIPPED on {gameObject.name} (non-dynamic rigidbody)");
            return;
        }

        isKnockedBack = true;
        knockbackTimer = duration;
        knockbackVelocity = direction.normalized * force;
        rb.linearVelocity = knockbackVelocity;
        //Debug.Log($"[CombatFeel] KNOCKBACK {gameObject.name} dir={direction} force={force}");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (currentTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, avoidDistance);
    }

}



