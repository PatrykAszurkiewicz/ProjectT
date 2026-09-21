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

    [Tooltip("Whiff tolerance. Attack range is only tested when the attack STARTS, so a " +
             "target that walks away during the wind-up still takes full damage across " +
             "the gap - and because ShieldSystem runs its own proximity test on the " +
             "attacker, that hit also cannot be blocked or parried. At the moment the " +
             "hit lands the range is re-tested against attackRange * this value, and the " +
             "swing simply misses if the target left. 1 = exact range (attacks whiff " +
             "constantly against a strafing player), ~1.25 = a little grace, higher = " +
             "stickier. Set 0 to disable the re-check and keep the old hit-anyway " +
             "behaviour for this enemy (e.g. a boss whose slam should never miss).")]
    [SerializeField] private float attackWhiffTolerance = 1.25f;

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

    [Tooltip("Minimum time an enemy commits to a slide direction before the " +
             "'the way is clear again' early release may fire. Stops the heading " +
             "flickering on and off while scraping past a wall face.")]
    [SerializeField] private float stuckMinCommitTime = 0.35f;

    [Tooltip("How much of the straight-line pull toward the target is KEPT while " +
             "sliding around an obstacle. 0 = the old behaviour (completely blind " +
             "to the target for the whole slide). ~0.35 makes the enemy curve back " +
             "in the moment it has room, instead of overshooting past the corner.")]
    [SerializeField] private float stuckTargetPull = 0.35f;

    // How long one committed slide lasts. Was a magic 2f inside EnterStuckMode;
    // HandleStuckDetection now needs it too, to know how long we have been sliding.
    private const float STUCK_MODE_DURATION = 2f;

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

    [Header("Crowd Separation")]
    // Other creatures are NOT obstacles. They must never reach the stuck /
    // wall-avoidance path (that system blanks out the target direction and commits
    // to a fixed heading, which is what made packs scatter). They get their own
    // gentle, always-on push instead, folded into the same smoothed heading, so a
    // crowd spreads out while every member keeps steering at its target.
    [Tooltip("Push enemies apart so a pack spreads into a front instead of " +
             "collapsing into one column and jamming. Off = bodies overlap and " +
             "the ones at the back make no progress.")]
    [SerializeField] private bool crowdSeparationEnabled = true;

    [Tooltip("Neighbour scan radius, as a multiple of this enemy's own collider " +
             "radius. 2 covers the ring of bodies actually touching us.")]
    [SerializeField] private float crowdRadiusFactor = 2f;

    [Tooltip("The gap each enemy defends, as a multiple of the two colliders' " +
             "combined radii. 1.0 = push only while physically overlapping, which " +
             "settles them exactly touching. ~1.15 leaves a visible sliver.")]
    [SerializeField] private float personalSpaceFactor = 1.15f;

    [Tooltip("How hard the crowd push bends the heading. This competes with the " +
             "pull toward the target, so keep it BELOW avoidanceStrength: walls " +
             "must always win over neighbours.")]
    [SerializeField] private float crowdSeparationStrength = 1f;

    [Tooltip("Fraction of the crowd push that still applies while attacking. Do " +
             "NOT set 0 — velocity is zeroed during an attack, so with no push at " +
             "all two enemies on the same target fuse and never come apart.")]
    [Range(0f, 1f)][SerializeField] private float crowdSeparationWhileAttacking = 0.4f;

    [Tooltip("Max world units per physics step of DIRECT position correction when " +
             "bodies actually overlap. Steering alone cannot undo an overlap while " +
             "velocity is being assigned (which wipes Box2D's contact impulse). " +
             "0 disables.")]
    [SerializeField] private float crowdDepenetrationPerStep = 0.015f;

    [Tooltip("Physics steps between neighbour scans. The scan is the most " +
             "expensive thing an enemy does per step and a crowd does not " +
             "rearrange itself in 20 ms, so 2-3 is visually identical to 1 and " +
             "cuts the cost proportionally on big waves. Scans are phase-offset " +
             "per enemy so a wave never scans in lock-step on one frame.")]
    [Range(1, 4)][SerializeField] private int crowdScanInterval = 2;

    private int crowdScanCounter;
    private int crowdScanPhase;
    private Vector2 lastCrowdPush;

    // Set by a companion that runs its OWN separation (e.g. SplitterController) so
    // the two don't both push and double the force.
    [HideInInspector] public bool SuppressCrowdSeparation = false;

    // Written by ComputeCrowdSeparation, consumed by ApplyCrowdDepenetration in the
    // same physics step.
    private Vector2 crowdPushDir;
    private float crowdOverlapDepth;
    private Collider2D selfCollider;
    private static readonly Collider2D[] _crowdScan = new Collider2D[24];

    // PERF: per-collider component caches.
    // ComputeCrowdSeparation did GetComponentInParent<EnemyStats>() for every
    // neighbour of every enemy on every scan, and AccumulateBlockerContact did
    // GetComponentInParent<CharacterStats>() for every contact pair on EVERY
    // physics step (OnCollisionStay2D). In a packed wave of 100 that is thousands
    // of hierarchy walks per step, and the step runs several times per frame once
    // the framerate drops. Which components sit above a collider never changes
    // for its lifetime (pooled enemies keep theirs), so resolve once and reuse.
    // Liveness (IsDead) is still checked live on every use.
    //
    // Each entry also stores the exact Collider2D REFERENCE it was built for.
    // UnityEngine.Object equality (used by the Dictionary) compares instance IDs,
    // so if an ID were ever reused by a newly spawned collider, the lookup could
    // land on a dead entry. The ReferenceEquals check below turns that into a
    // plain cache miss instead of a wrong answer.
    private struct ColliderCacheEntry<T>
    {
        public Collider2D Owner;
        public T Value;
    }

    private static readonly Dictionary<Collider2D, ColliderCacheEntry<EnemyStats>> _enemyStatsByCollider =
        new Dictionary<Collider2D, ColliderCacheEntry<EnemyStats>>(256);
    private static readonly Dictionary<Collider2D, ColliderCacheEntry<bool>> _isCreatureByCollider =
        new Dictionary<Collider2D, ColliderCacheEntry<bool>>(256);
    // Destroyed colliders stay as dead keys; flushing the whole cache now and then
    // bounds memory at the cost of one re-lookup per live collider.
    private const int COLLIDER_CACHE_LIMIT = 4096;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetColliderCaches()
    {
        _enemyStatsByCollider.Clear();
        _isCreatureByCollider.Clear();
    }

    private static EnemyStats GetEnemyStatsCached(Collider2D col)
    {
        if (_enemyStatsByCollider.TryGetValue(col, out var cached)
            && ReferenceEquals(cached.Owner, col))
            return cached.Value;
        if (_enemyStatsByCollider.Count > COLLIDER_CACHE_LIMIT) _enemyStatsByCollider.Clear();
        var found = col.GetComponentInParent<EnemyStats>();
        _enemyStatsByCollider.Remove(col); // drop a stale entry whose key compares equal
        _enemyStatsByCollider[col] = new ColliderCacheEntry<EnemyStats> { Owner = col, Value = found };
        return found;
    }

    private static bool IsCreatureCached(Collider2D col)
    {
        if (_isCreatureByCollider.TryGetValue(col, out var cached)
            && ReferenceEquals(cached.Owner, col))
            return cached.Value;
        if (_isCreatureByCollider.Count > COLLIDER_CACHE_LIMIT) _isCreatureByCollider.Clear();
        bool found = col.GetComponentInParent<CharacterStats>() != null;
        _isCreatureByCollider.Remove(col); // drop a stale entry whose key compares equal
        _isCreatureByCollider[col] = new ColliderCacheEntry<bool> { Owner = col, Value = found };
        return found;
    }
    private static readonly ContactFilter2D _crowdFilter = BuildCrowdFilter();

    private static ContactFilter2D BuildCrowdFilter()
    {
        // NoFilter() turns useTriggers ON. Trigger colliders are everywhere in a
        // tower defence (aggro ranges, pickup zones, VFX volumes) and would eat
        // slots in the 24-entry scan buffer, silently hiding the real bodies behind
        // them — exactly in the dense crowds this system exists to fix.
        ContactFilter2D f = new ContactFilter2D().NoFilter();
        f.useTriggers = false;
        return f;
    }

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

    // Optional "my hit actually connected" notification. Fired by
    // ApplyDamageToTarget AFTER a hit has genuinely reduced a target's health (or
    // damaged a tower), with the amount that actually landed. Same opt-in
    // composition idiom as the hooks above: null for every existing prefab, so
    // nothing changes unless a companion component subscribes (WolfController
    // uses it for lifesteal).
    //
    // Load-bearing details for subscribers:
    //   * The float is the health the target REALLY lost - post-armor, after
    //     god-mode, with overkill excluded - not this enemy's nominal Damage.
    //   * It never fires for a hit that was blocked, parried, or swallowed by a
    //     parry stun, because those paths return before damage is applied. An
    //     on-hit effect built on this therefore gets denied by a successful
    //     parry for free.
    //   * It does NOT fire for enemies that bypass ApplyDamageToTarget entirely
    //     (Eye AOE, RedEye laser, Bomber explosion, boss specials), for the same
    //     reason NotifyPlayerDamaged had to be hoisted out into a static.
    public System.Action<Transform, float> OnDamageDealt;

    //  Companion-driven movement suspension 
    // While true, this controller runs NONE of its own movement, steering,
    // stuck-detection or attack-trigger logic for the frame — a sibling
    // behaviour (e.g. InsectController during its burrow / underground / emerge
    // phases) is driving the body directly and we must not fight it. Same
    // composition idiom as PriorityTargetProvider / AttackHandlerOverride above:
    // it defaults false and is only ever flipped by an opt-in companion, so
    // every existing enemy behaves exactly as before. The companion hands
    // control BACK (sets it false) for the actual strike so the normal attack
    // cycle — hit frames, parry window, SFX, damage, retarget-after-kill — is
    // reused rather than re-implemented.
    public bool ExternalMovementControl = false;

    // Read-only view of the configured melee attack range, so a companion
    // behaviour can align its own "close enough to surface and strike" distance
    // with the range at which this controller will actually open an attack.
    public float AttackRange => attackRange;

    // Lets a companion component (e.g. BerserkController) supply the per-enemy attack
    // sound from code when it isn't assigned on the prefab, reusing the existing
    // PlayAttackSound path so it stays in sync with the hit frame. A value already set
    // in the inspector wins, so this never clobbers explicit prefab wiring.
    // Read-only access to the per-enemy attack sound for companions that replace
    // PerformHit via AttackHandlerOverride (which skips PlayAttackSound), e.g.
    // BruteController, so they can still play the sound wired on the prefab.
    // Pure getter: no behaviour change for any other enemy.
    public EventReference AttackSoundOverride => attackSoundOverride;

    public void SetAttackSoundOverrideIfUnset(EventReference ev)
    {
        if (attackSoundOverride.IsNull) attackSoundOverride = ev;
    }

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

        // Cache boss status once, BEFORE it is used. This used to be computed into a
        // local that shadowed the field inside the Y-sort block, then recomputed
        // into the field a few lines later - same value, two GetComponent calls, and
        // a shadowed name that read as if the field were already set.
        // (smoke-blinding is skipped for bosses.)
        isBoss = boss1 != null || GetComponent<BaseBossStats>() != null;

        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;

            ysort.sortYOffset = isBoss ? -1.0f : -0.2f;
        }

        // Random phase so a group of smoke-blinded enemies doesn't shuffle in lock-step.
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

    // Companion to the guard in ApplyKnockback. That one covers "knocked back while
    // already disabled"; this covers "disabled WHILE knocked back", which is the case
    // that actually strands enemies: ConfusedEnemy.Initialize and
    // BerserkEnemy.Initialize both disable this controller to take over movement (see
    // MortController's comment about exactly that), and EnemyStats.DelayedDeath does
    // too. Disabled mid-knockback, FixedUpdate stops running, the velocity is never
    // decayed, and the body coasts away at whatever speed it had - the
    // 'no_enemy_adrift_off_map' symptom. Clearing the state here makes that
    // impossible regardless of who does the disabling or when.
    // Reseed the stuck-detection baseline every time this controller becomes
    // active.
    //
    // lastKnownPosition used to be left at its default (0,0) — it was never
    // assigned in Start — so the FIRST HandleStuckDetection measured progress from
    // the WORLD ORIGIN rather than from the enemy. For any enemy whose position
    // projects onto the negative side of the target direction, that reads as "no
    // progress" on every single step (the baseline is never refreshed because the
    // refresh lives in the branch that check gates), so the enemy was pinned in
    // stuck mode from half a second after spawn until it physically crossed the
    // origin plane — steering on a frozen heading the whole way.
    //
    // OnEnable rather than Start because ConfusedEnemy / BerserkEnemy disable this
    // component to take over movement and hand it back later, and pooled enemies
    // are re-enabled at a brand new position.
    private void OnEnable()
    {
        lastKnownPosition = transform.position;
        timeSinceLastMovement = 0f;
        isInStuckMode = false;
        stuckModeTimer = 0f;
        smoothHeading = Vector2.zero;
        crowdScanPhase = Mathf.Abs(GetInstanceID());
        crowdScanCounter = 0;
        lastCrowdPush = Vector2.zero;
    }

    private void OnDisable()
    {
        if (!isKnockedBack) return;

        isKnockedBack = false;
        knockbackTimer = 0f;
        knockbackVelocity = Vector2.zero;

        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            rb.linearVelocity = Vector2.zero;
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

    /// Re-read the hit / parry frame numbers from EnemyData.
    ///
    /// EnemyData stays the single source of truth - this only re-syncs the cached
    /// copy. A companion component that adjusts this enemy's own (per-instance,
    /// cloned) EnemyData during Start() calls this so it doesn't matter whether
    /// EnemyController.Start() happened to run first. Harmless to call at any
    /// time; a no-op when nothing changed.
    public void RefreshFrameConfig() => ResolveFrameConfig();

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

        // Companion (e.g. InsectController while burrowing) owns the body this
        // phase — bail out WITHOUT touching velocity so we don't cancel the
        // motion it's driving. Placed after the freeze / parry-stun / game-over
        // gates so those still take priority (a frozen burrower still stops).
        if (ExternalMovementControl) return;

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
                SettleInCrowd(); // stopped, but still must not fuse with neighbours
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
                SettleInCrowd(); // stopped, but still must not fuse with neighbours
                return;
            }
        }

        HandleStuckDetection();
        Vector2 direction = (currentTarget.position - transform.position).normalized;
        direction = ComputeSteeredDirection(direction);

        // Lured enemies move slightly slower (confused)
        float speedMultiplier = isLuredByDecoy ? 0.8f : 1f;
        rb.linearVelocity = direction.normalized * stats.MoveSpeed * speedMultiplier;

        // Assigning linearVelocity above wipes the contact impulse Box2D applied for
        // any overlap, so velocity alone can never separate two bodies that are
        // already inside each other. Nudge the transform directly, hard-capped.
        ApplyCrowdDepenetration();
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

        // Two independent ways to count as "moving".
        //
        //   madeProgress — strict: closing on the target. Sliding along a wall
        //                  scores ~0, which is what we want, because sliding along
        //                  a wall IS being stuck.
        //   travelled    — raw distance covered, used only as an escape hatch once
        //                  we are ALREADY in stuck mode. Stuck mode deliberately
        //                  steers sideways, so it produces ~0 target-ward progress
        //                  by construction: without this, the mode's own behaviour
        //                  guarantees its own exit condition can never be met and
        //                  an enemy could stay latched to it indefinitely.
        bool madeProgress = progress > minMovementThreshold;
        float travelGate = minMovementThreshold * 6f;
        bool travelled = displacement.sqrMagnitude > travelGate * travelGate;

        if (madeProgress || (isInStuckMode && travelled))
        {
            timeSinceLastMovement = 0f;
            lastKnownPosition = transform.position;
            if (madeProgress) isInStuckMode = false;
        }
        else
        {
            timeSinceLastMovement += Time.fixedDeltaTime;
            if (timeSinceLastMovement > stuckCheckTime && !isInStuckMode)
            {
                if (HasRealBlockerNearby())
                {
                    EnterStuckMode();
                }
                else
                {
                    // No world geometry in the way — we are simply packed in with
                    // other enemies. Crowding must NOT enter stuck mode: that path
                    // drops the target direction from the steering entirely and
                    // commits to a fixed heading for two seconds, which is exactly
                    // the "whole pack runs off in random directions" symptom.
                    // Keep seeking and let crowd separation open the jam up.
                    timeSinceLastMovement = 0f;
                    lastKnownPosition = transform.position;
                }
            }
        }

        if (isInStuckMode)
        {
            stuckModeTimer -= Time.fixedDeltaTime;

            // Release EARLY the moment the line to the target is genuinely open, so
            // the enemy turns back in as soon as it has rounded the corner instead
            // of running out the full two seconds. The commit window keeps this from
            // chattering on and off while still scraping along the wall.
            float elapsed = STUCK_MODE_DURATION - stuckModeTimer;
            if (elapsed >= stuckMinCommitTime && PathToTargetClear())
            {
                isInStuckMode = false;
                timeSinceLastMovement = 0f;
                lastKnownPosition = transform.position;
                return;
            }

            if (stuckModeTimer <= 0f)
            {
                // Only release when we've actually cleared the obstacle. If we're
                // still pressed against one (e.g. a long wall whose end we haven't
                // rounded yet), refresh the slide direction — we might have reached
                // a corner where the previously blocked side is now clear.
                if (HasRealBlockerNearby())
                {
                    EnterStuckMode();
                    // Reset the movement-progress baseline so we don't immediately
                    // re-trigger the "no progress" path.
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
        // Remember the side we were already committed to (0 on a fresh entry) so a
        // refresh halfway around a long wall keeps going the same way instead of
        // reversing and grinding back into the corner it just left.
        Vector2 previousSlide = isInStuckMode ? stuckAvoidanceDirection : Vector2.zero;

        isInStuckMode = true;
        stuckModeTimer = STUCK_MODE_DURATION;

        Vector2 selfPos = transform.position;
        Vector2 toTarget = (Vector2)currentTarget.position - selfPos;
        toTarget = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;

        // Find what we're actually pressed against, so we slide ALONG the wall
        // (perpendicular to its surface normal) rather than perpendicular to
        // "direction to target", which is only correct when the wall and target
        // happen to be axis-aligned with each other.
        Collider2D wall = Physics2D.OverlapCircle(selfPos, BlockerProbeRadius, CombinedBlockerMask);

        Vector2 slideA;

        if (wall != null || HasRecentBlockerContact)
        {
            Vector2 wallNormal = Vector2.zero;

            if (wall != null)
            {
                Vector2 closest = wall.ClosestPoint(selfPos);
                wallNormal = selfPos - closest;

                // If we're inside the collider, ClosestPoint may return our own
                // position. Fall back to the collider centre direction.
                if (wallNormal.sqrMagnitude < 0.0001f)
                    wallNormal = selfPos - (Vector2)wall.transform.position;
            }

            // Nothing usable on the configured layers, but we ARE physically
            // touching something (e.g. an untagged tree): use the real contact
            // normal so the slide is computed against the actual blocker.
            if (wallNormal.sqrMagnitude < 0.0001f && HasRecentBlockerContact)
                wallNormal = _contactNormal;

            slideA = wallNormal.sqrMagnitude > 0.0001f
                ? Perp(wallNormal.normalized)   // along the wall face
                : Perp(toTarget);               // degenerate: sidestep instead
        }
        else
        {
            // No world blocker anywhere near us. Sidestep LATERALLY, keeping our
            // distance to the target roughly constant.
            //
            // The old code built a fake "wall normal" perpendicular to the target
            // here and then took ITS perpendicular — which algebraically cancels
            // back to exactly +/- the direction to the target. The clearance probe
            // then rejected the toward-target option (the tower we are trying to
            // reach sits on the blocker layers, so the probe lands inside it) and
            // committed the enemy to running DIRECTLY AWAY from its own target for
            // two full seconds. Perpendicular-to-target is the only sane pair here.
            //
            // In practice this branch is now nearly unreachable: HandleStuckDetection
            // refuses to enter stuck mode at all without a real blocker. It stays as
            // a safety net for the frame where a blocker vanishes mid-slide.
            slideA = Perp(toTarget);
        }

        Vector2 slideB = -slideA;

        // Probe further than avoidDistance: on a long segmented wall, a short probe
        // lands inside another segment of the same wall and reports "blocked" on
        // both sides. 3x avoidDistance reaches past segment boundaries so we can
        // pick the genuinely-clear direction.
        float probeDist = avoidDistance * 3f;
        bool aClear = !Physics2D.OverlapCircle(selfPos + slideA * probeDist, 0.3f, CombinedBlockerMask);
        bool bClear = !Physics2D.OverlapCircle(selfPos + slideB * probeDist, 0.3f, CombinedBlockerMask);

        // Score both sides instead of the old if/else-if/coin-flip chain:
        //   • clearance dominates — weight 3 exceeds the 2-unit span of a dot
        //     product, so a clear side always beats a blocked one;
        //   • then "which way still faces the target", so we never choose the side
        //     that walks away from it when the two are equally clear (the old
        //     tie-break compared probe endpoints, which are near-equidistant when
        //     the enemy is beside its target — a float-noise coin flip, committed
        //     for two seconds, and different for every enemy in a pack);
        //   • then hysteresis toward the side we were already sliding.
        float aScore = (aClear ? 3f : 0f) + Vector2.Dot(slideA, toTarget)
                                          + Vector2.Dot(slideA, previousSlide) * 0.75f;
        float bScore = (bClear ? 3f : 0f) + Vector2.Dot(slideB, toTarget)
                                          + Vector2.Dot(slideB, previousSlide) * 0.75f;

        stuckAvoidanceDirection = (aScore >= bScore) ? slideA : slideB;
    }

    private static Vector2 Perp(Vector2 v) => new Vector2(-v.y, v.x);

    // "Is there real world geometry in the way?" — the gate that keeps crowding out
    // of the stuck / wall-avoidance system. Other creatures are deliberately absent
    // from both CombinedBlockerMask (a layer choice) and the contact normal
    // (AccumulateBlockerContact skips anything with CharacterStats), so a pack
    // jammed shoulder-to-shoulder in open ground answers false here and keeps
    // seeking its target.
    private bool HasRealBlockerNearby()
    {
        if (HasRecentBlockerContact) return true;
        int mask = CombinedBlockerMask;
        if (mask == 0) return false;

        return Physics2D.OverlapCircle(transform.position, BlockerProbeRadius, mask) != null;
    }

    // Can we head straight at the target again? Used to release a slide the instant
    // we've rounded the obstacle, rather than burning the full commit timer.
    // Deliberately probes only as far as we can plan for — a distant wall between us
    // and the core is the next slide's problem, not this one's.
    private bool PathToTargetClear()
    {
        if (currentTarget == null) return true;
        int mask = CombinedBlockerMask;
        if (mask == 0) return true;

        Vector2 from = GetBodyCentre(transform);
        Vector2 to = GetBodyCentre(currentTarget);
        Vector2 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= 0.0001f) return true;

        const float R = 0.12f;
        float probe = Mathf.Min(dist, lookAheadDistance * 2.5f);
        if (probe <= R) return true;

        Vector2 dir = delta / dist;
        return Physics2D.CircleCast(from + dir * R, R, dir, probe - R, mask).collider == null;
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

    // One radius for "is there world geometry here". HasRealBlockerNearby gates
    // entry into stuck mode and EnterStuckMode looks up the wall it will slide
    // along — if those two disagreed, the gate could pass on a wall the lookup then
    // failed to find, dropping us into the no-wall fallback for no reason.
    private float BlockerProbeRadius => Mathf.Max(avoidDistance, lookAheadDistance * 0.75f);

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

        // Neighbour push, computed once and folded into BOTH branches below. This is
        // the whole point of the crowd fix: a pack spreads sideways into a front
        // while every member keeps steering at its own target, instead of the back
        // ranks stalling, tripping the stuck detector, and peeling off on frozen
        // headings.
        Vector2 crowd = ComputeCrowdSeparation(selfPos);

        if (isInStuckMode)
        {
            // Wide arc: slide ALONG the wall, bleed in the away-from-wall push so we
            // swing OUT and round it rather than scraping its face, and KEEP a
            // fraction of the pull toward the target so the arc curves back in as
            // soon as there is room. That last term is new: without it the enemy is
            // completely blind to its target for the whole slide, which is what made
            // a mis-triggered slide look like it had simply forgotten where it was
            // going.
            Vector2 offWall = ComputeAvoidanceVector(selfPos, out _);
            goal = stuckAvoidanceDirection
                 + offWall.normalized * stuckArcWidth
                 + desired * Mathf.Max(0f, stuckTargetPull);
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

        // Neighbours bend the heading, but never as hard as walls: crowdSeparation-
        // Strength is meant to sit below avoidanceStrength so geometry always wins.
        if (crowd.sqrMagnitude > 0.0001f)
        {
            float crowdFactor = isAttackingCycle
                ? Mathf.Clamp01(crowdSeparationWhileAttacking)
                : 1f;
            goal += crowd * (crowdSeparationStrength * crowdFactor);
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

    // Averaged push away from nearby ENEMIES, magnitude 0..1, strongest at full
    // overlap and zero at the defended gap.
    //
    // Layer-agnostic on purpose (scan everything, keep what has EnemyStats) so this
    // needs no inspector setup and cannot be silently disabled by a layer mistake —
    // the same approach SplitterController already uses.
    //
    // The current target is excluded even when it IS an enemy (BerserkController
    // hunts other enemies): pushing away from the thing we're trying to reach would
    // stop us ever reaching it.
    private Vector2 ComputeCrowdSeparation(Vector2 selfPos)
    {
        if (!crowdSeparationEnabled || SuppressCrowdSeparation)
        {
            crowdPushDir = Vector2.zero;
            crowdOverlapDepth = 0f;
            return lastCrowdPush = Vector2.zero;
        }

        // Throttled + phase-offset. On a skipped step we reuse the previous push
        // (and the previous overlap depth) rather than reporting "no neighbours",
        // which would make the push stutter on and off.
        if (crowdScanInterval > 1)
        {
            crowdScanCounter++;
            if ((crowdScanCounter % crowdScanInterval) != (crowdScanPhase % crowdScanInterval))
                return lastCrowdPush;
        }

        crowdPushDir = Vector2.zero;
        crowdOverlapDepth = 0f;

        if (selfCollider == null) selfCollider = GetComponent<Collider2D>();
        float myR = selfCollider != null
            ? Mathf.Max(selfCollider.bounds.extents.x, 0.05f)
            : 0.25f;

        float scanR = myR * Mathf.Max(1f, crowdRadiusFactor);
        int hits = Physics2D.OverlapCircle(selfPos, scanR, _crowdFilter, _crowdScan);
        if (hits <= 1) return lastCrowdPush = Vector2.zero;

        Vector2 push = Vector2.zero;
        int counted = 0;

        for (int i = 0; i < hits; i++)
        {
            Collider2D other = _crowdScan[i];
            if (other == null || other == selfCollider || other.isTrigger) continue;
            if (other.transform == transform) continue;

            EnemyStats otherStats = GetEnemyStatsCached(other);
            if (otherStats == null || otherStats == stats) continue;
            if (otherStats.IsDead()) continue;
            if (currentTarget != null && otherStats.transform == currentTarget) continue;

            Vector2 delta = selfPos - (Vector2)other.transform.position;
            float d = delta.magnitude;

            if (d < 0.0001f)
            {
                // Perfectly co-located — the degenerate case physics can't resolve.
                // Compare instance IDs so the two bodies deterministically pick
                // OPPOSITE directions; Random here would make both of them jitter.
                bool first = GetInstanceID() < otherStats.GetInstanceID();
                push += first ? Vector2.right : Vector2.left;
                counted++;
                crowdOverlapDepth = Mathf.Max(crowdOverlapDepth, myR);
                continue;
            }

            float otherR = Mathf.Max(other.bounds.extents.x, 0.05f);

            // Defend a GAP, not just non-overlap. At personalSpaceFactor = 1 the
            // force reaches zero the instant the colliders touch, so bodies settle
            // perfectly tangent and read as one merged mass.
            float desiredGap = (myR + otherR) * Mathf.Max(1f, personalSpaceFactor);
            if (d >= desiredGap) continue;

            crowdOverlapDepth = Mathf.Max(crowdOverlapDepth, (myR + otherR) - d);
            push += (delta / d) * (1f - d / desiredGap); // linear falloff
            counted++;
        }

        if (counted == 0) return lastCrowdPush = Vector2.zero;

        push /= counted;
        if (push.sqrMagnitude > 0.0001f) crowdPushDir = push.normalized;
        return lastCrowdPush = push;
    }

    // Direct position correction for bodies that are already inside each other.
    // Needed because FixedUpdate ASSIGNS linearVelocity, which erases the contact
    // impulse Box2D generated for the overlap — so velocity alone can never undo it.
    // Hard-capped per step so nobody is teleported through a wall.
    private void ApplyCrowdDepenetration()
    {
        if (!crowdSeparationEnabled || SuppressCrowdSeparation) return;
        if (crowdDepenetrationPerStep <= 0f) return;
        if (crowdOverlapDepth <= 0f || crowdPushDir.sqrMagnitude < 0.0001f) return;
        if (rb == null || rb.bodyType == RigidbodyType2D.Static) return;
        if (isKnockedBack || isBeingGrappled || isFrozen) return;

        float factor = isAttackingCycle ? Mathf.Clamp01(crowdSeparationWhileAttacking) : 1f;
        float step = Mathf.Min(crowdOverlapDepth * 0.5f, crowdDepenetrationPerStep) * factor;
        if (step <= 0f) return;

        Vector2 dest = rb.position + crowdPushDir * step;

        // This write bypasses collision resolution, so a push aimed at a wall would
        // embed the enemy in it. Give up the nudge rather than clip geometry — the
        // steering push is still applied, and the overlap resolves as soon as the
        // pair rotates off the wall.
        int mask = CombinedBlockerMask;
        if (mask != 0 && Physics2D.OverlapPoint(dest, mask) != null) return;

        rb.position = dest;
    }

    // Standing still (attacking, or milling at a decoy) still has to unstick bodies
    // from each other, or two enemies on the same target slide together and fuse.
    private void SettleInCrowd()
    {
        ComputeCrowdSeparation(transform.position);
        ApplyCrowdDepenetration();
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
        if (IsCreatureCached(collision.collider)) return;

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

        // BUGFIX: the knockback timer used to be decremented HERE as well as in
        // FixedUpdate, so it drained at ~2x real time and a 0.25s knockback lasted
        // ~0.125s (the exact amount drifting with framerate vs fixed timestep).
        // FixedUpdate owns the knockback entirely - it is the only place that also
        // decays knockbackVelocity and writes it to the Rigidbody - so the decrement
        // belongs there and nowhere else.

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

        // Companion is driving this phase (e.g. Insect burrowing / travelling
        // underground): never open an attack cycle until it hands control back.
        if (ExternalMovementControl) return;

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

    // Editor-only verbose parry tracing. Off by default; flip it at runtime from
    // another script or a debug menu when you need to see the window maths.
#if UNITY_EDITOR
    public static bool LogParryChecks = false;
#endif

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

        // BUGFIX: this used to log unconditionally. It fires on EVERY parry
        // adjudication - i.e. every time any enemy connects on a shielded player -
        // and the interpolated string is built even when nothing reads it. Gated
        // behind an editor-only switch you can flip from the Console/inspector when
        // you actually need to debug parry timing.
#if UNITY_EDITOR
        if (LogParryChecks)
        {
            Debug.Log($"[PARRY CHECK] {gameObject.name}: shieldRaise={shieldRaiseTime:F3} now={Time.time:F3} " +
                      $"parryWindow=[{parryWindowStart:F3}-{parryWindowEnd:F3}] " +
                      $"frames={pStart}-{pEnd} hit={hit} " +
                      $"raisedDuring={raisedDuringWindow} => {(isParry ? "PARRY!" : "BLOCK")}");
        }
#endif

        return isParry;
    }

    // Returns true if this enemy is currently mid-attack and the current time falls within its parry frames. 

    // BUGFIX: this took no player index and read the GLOBAL
    // ParryUpgrades.ExtraParryFrames, while IsInParryWindow (the method that
    // actually adjudicates the parry) reads the per-player ExtraParryFramesFor().
    // In split screen the two disagreed, so anything driven off this - indicators,
    // telemetry - showed a window that did not match what the game would accept.
    // Defaults to player 0, matching the 1-arg IsInParryWindow overload, so every
    // existing zero-arg call site keeps compiling and keeps its old single-player
    // behaviour.
    public bool IsCurrentlyInParryFrames() => IsCurrentlyInParryFrames(0);

    public bool IsCurrentlyInParryFrames(int parryingIndex)
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
        int effParryStart = Mathf.Max(0, pStart - ParryUpgrades.ExtraParryFramesFor(parryingIndex));
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

        // Re-test range AT THE MOMENT OF THE HIT.
        // Range is otherwise only checked where the attack cycle is started, which
        // leaves the whole wind-up unguarded: the target can back off and still be hit
        // from well outside attackRange. That hit is also unblockable and unparryable,
        // because ShieldSystem does its own proximity test against the attacker and
        // fails it - so from the player's side it reads as damage from nowhere that the
        // shield inexplicably ignored. Slower wind-ups make it far more visible, since
        // they give the target more time to drift out.
        //
        // The sounds above deliberately still play: a miss should be audible as a swing.
        // NOTE this guards the standard melee path only. Projectiles (which resolve
        // their own hit on impact) and AoE attacks that call ApplyDamageToTarget
        // directly - e.g. BruteController.ApplySlamDamage, whose slam radius is its own
        // and larger than attackRange - intentionally bypass it.
        if (attackWhiffTolerance > 0f && target != null)
        {
            float dist = Vector2.Distance(transform.position, target.position);
            if (dist > attackRange * attackWhiffTolerance)
                return;   // target left during the wind-up - the swing misses
        }

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

            // Sample health either side of the hit so OnDamageDealt can report what
            // the target ACTUALLY lost rather than the nominal swing: armor
            // mitigation, DebugCheats god-mode and overkill on a killing blow all
            // make those two numbers differ.
            float healthBefore = stats.currentHealth;
            stats.TakeDamage(damageAmount);
            float healthLost = Mathf.Max(0f, healthBefore - stats.currentHealth);

            if (playerStats != null)
            {
                CombatJuice.OnEnemyHitPlayer(target.GetComponentInParent<PlayerRef>());


                // Hoisted into the static NotifyPlayerDamaged below so the enemies
                // that do NOT route through this method (Eye AOE, RedEye laser, Bomber
                // explosion, boss specials) can fire the same augments. Behaviour here
                // is unchanged: same two components, same arguments, same order.
                NotifyPlayerDamaged(playerStats, damageAmount, gameObject);
            }

            // Opt-in on-hit hook (see field comment). Fired before the
            // retarget-after-kill below so a subscriber still sees the victim.
            if (healthLost > 0f)
                OnDamageDealt?.Invoke(target, healthLost);

            if (stats.IsDead())
                RetargetAfterKill();

            return;
        }

        var consumer = target.GetComponent<IEnergyConsumer>();
        if (consumer != null && EnergyManager.Instance != null)
        {
            float structureDamage = this.stats.Damage;
            bool wasDestroyed = EnergyManager.Instance.DamageEnergyConsumer(
                consumer, structureDamage, gameObject);

            // Structures don't expose a health delta, so this reports the nominal
            // damage. Subscribers that care about the difference should treat
            // non-CharacterStats targets separately (WolfController does).
            if (structureDamage > 0f)
                OnDamageDealt?.Invoke(target, structureDamage);

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

    // =========================================================================
    //  PLAYER ON-HIT AUGMENT REACTIONS  (shared, static)
    // -------------------------------------------------------------------------
    //  Damage Reflection and Ice Armor used to fire from exactly ONE place: the
    //  melee/projectile hit in ApplyDamageToTarget above. Every attack that does
    //  NOT route through EnemyController — the Eye's AOE pulse, the RedEye's
    //  laser, the Bomber's explosion, and all of the boss specials — silently
    //  skipped both augments, so a player holding Damage Reflection got nothing
    //  back from the attacks that hurt most.
    //
    //  DOUBLE-FIRE SAFETY: ApplyDamageToTarget now calls NotifyPlayerDamaged
    //  instead of doing the work inline. Do NOT re-add the inline block, and do
    //  NOT call these from a path that already goes through ApplyDamageToTarget
    //  (e.g. BruteController's slams, which deliberately reuse that method).
    //
    //  `damage` must be the amount actually handed to TakeDamage, so the reflected
    //  fraction matches what the player received.
    // =========================================================================

    /// Fire the on-hit augment reactions for a player damaged by `attacker`.
    /// No-op when the player holds neither augment.
    public static void NotifyPlayerDamaged(PlayerStats playerStats, float damage, GameObject attacker)
    {
        if (playerStats == null || attacker == null || damage <= 0f) return;

        var reflectionEffect = playerStats.GetComponent<DamageReflectionEffect>();
        if (reflectionEffect != null)
            reflectionEffect.ReflectDamage(damage, attacker);

        var iceArmorEffect = playerStats.GetComponent<IceArmorEffect>();
        if (iceArmorEffect != null)
            iceArmorEffect.FreezeAttacker(attacker);
    }

    /// GameObject overload for callers that only have the hit object.
    public static void NotifyPlayerDamaged(GameObject playerObject, float damage, GameObject attacker)
    {
        if (playerObject == null) return;

        var playerStats = playerObject.GetComponent<PlayerStats>();
        if (playerStats == null) playerStats = playerObject.GetComponentInParent<PlayerStats>();

        NotifyPlayerDamaged(playerStats, damage, attacker);
    }

    /// Convenience for AoE paths that iterate CharacterStats: fires only when the
    /// hit character is actually a player, so enemies caught in the same sweep (and
    /// the attacker itself) are ignored.
    public static void NotifyCharacterDamaged(CharacterStats victim, float damage, GameObject attacker)
    {
        NotifyPlayerDamaged(victim as PlayerStats, damage, attacker);
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

    // PERF: IsValidTarget runs 2-3 times per enemy per frame (FixedUpdate and
    // Update). The Tower component on a given target never changes, so look it up
    // once per target instead of GetComponent<Tower>() on every call.
    private Transform _validityCacheTarget;
    private Tower _validityCacheTower;

    private bool IsValidTarget(Transform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
            return false;
        if (!ReferenceEquals(target, _validityCacheTarget))
        {
            _validityCacheTarget = target;
            _validityCacheTower = target.GetComponent<Tower>();
        }
        var tower = _validityCacheTower;
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
    // PERF: GetComponents<T>() returns a NEW array on every call. This runs up to
    // four times per enemy per physics step (SmokeBlocksTarget, HasLineOfSightTo-
    // Target, PathToTargetClear - once for self, once for the target) and again in
    // Update, so at 100 enemies it was thousands of small garbage arrays per frame.
    // The List<T> overload fills a reused buffer instead. Same result, zero alloc.
    // Safe as a static: Unity scripts run on the main thread and nothing below
    // re-enters this method while the buffer is being read.
    private static readonly List<Collider2D> _bodyColliderScratch = new List<Collider2D>(4);

    private static Vector2 GetBodyCentre(Transform t)
    {
        if (t == null) return Vector2.zero;
        t.GetComponents(_bodyColliderScratch);
        for (int i = 0; i < _bodyColliderScratch.Count; i++)
        {
            var c = _bodyColliderScratch[i];
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
    /// Is a knockback currently in progress? Exposed so tests and effects can ask
    /// directly instead of inferring it from rb.linearVelocity — the physics solver
    /// zeroes velocity when the body hits a wall, which looks identical to "the
    /// knockback ended" from the outside and is not.
    public bool IsKnockedBack => isKnockedBack;

    public void ApplyKnockback(Vector2 direction, float force, float duration = 0.25f)
    {
        // BUGFIX (adrift enemies): knockback is decayed ONLY by this component's
        // FixedUpdate. If the controller is not running, nothing will ever decay
        // knockbackVelocity or clear isKnockedBack, and a Dynamic body with no drag
        // coasts at a constant speed forever - straight off the map, still alive,
        // keeping the wave from completing. Refuse the push instead of stranding it.
        if (!isActiveAndEnabled) return;

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



