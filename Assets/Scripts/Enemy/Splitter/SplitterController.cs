using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// SPLITTER CONTROLLER
//
// A purple soft-body blob that bursts into smaller blobs on death. Children are
// launched outward, then arc around the target for a beat so the pack converges
// from several sides instead of stacking into one column.
//
// Reuses EnemyController wholesale (targeting, steering, stuck handling, attack
// cycle, parry window) and only touches two hooks:
//   • PriorityTargetProvider — temporary flank marker while dispersing
//   • AttackHandlerOverride  — a wind-up + lunge instead of an instant melee tap
//
// Also carries crowd separation. EnemyController.FixedUpdate ends by ASSIGNING
// rb.linearVelocity, which erases the separation impulse Box2D applied when two
// colliders touched — enemies in this game physically cannot push each other
// apart. Because this component runs at execution order 10000 (after both
// EnemyController and YSortEntity), it can ADD a repulsion term to the velocity
// that was just written, and re-apply our per-generation scale that YSortEntity
// stomps every frame. Same reason BerserkController uses a late execution order.
[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(Rigidbody2D))]
[DefaultExecutionOrder(10000)]
public class SplitterController : MonoBehaviour
{
    [Header("Splitting")]
    [Tooltip("Prefab instantiated for each child. Point this at the Splitter " +
             "prefab ITSELF. Never leave it null — a null prefab silently turns " +
             "the Splitter into an ordinary enemy.")]
    [SerializeField] private GameObject childPrefab;

    [Tooltip("Children per split. 2 is the classic; 3 turns a wave into a mess.")]
    [SerializeField] private int splitCount = 2;

    [Tooltip("How many times a lineage may split. 0 = never splits. 2 = the " +
             "original plus two more tiers, i.e. up to 1 + 2 + 4 = 7 blobs.")]
    [SerializeField] private int maxGeneration = 2;

    [Tooltip("Scale of each child relative to its parent. 0.62 keeps the total " +
             "silhouette area roughly constant across a split, which makes it read " +
             "as 'it divided' rather than 'it duplicated'.")]
    [SerializeField] private float childScale = 0.62f;

    [Tooltip("Child max health as a fraction of the parent's. 0.5 keeps the " +
             "lineage's total effective HP flat across generations.")]
    [Range(0.05f, 1f)][SerializeField] private float childHealthFraction = 0.5f;

    [Tooltip("Child move speed multiplier. Smaller blobs are faster — that's the " +
             "escalation that makes splitting scary instead of a relief.")]
    [SerializeField] private float childSpeedGain = 1.25f;

    [Tooltip("Child damage multiplier. Below 1 so seven tiny blobs don't erase " +
             "the player.")]
    [SerializeField] private float childDamageFraction = 0.75f;

    [Tooltip("Child separation at birth, as a MULTIPLE of the child's own WORLD " +
             "radius (read from collider bounds, so prefab scale can't break it). " +
             "Below ~1.5 the two circles spawn nearly concentric — a degenerate " +
             "contact the solver cannot resolve, and they stay fused.")]
    [SerializeField] private float separationFactor = 1.8f;

    [Tooltip("Seconds newly split siblings phase through each other while they " +
             "separate.")]
    [SerializeField] private float siblingPhaseDuration = 0.4f;

    [Header("Burst")]
    [Tooltip("Outward launch speed at birth. NOTE: EnemyController decays " +
             "knockbackVelocity by x0.82 EVERY physics step, so the distance " +
             "actually travelled is about speed x 0.02 x 5.5 — roughly a ninth of " +
             "speed x duration. 11 gives ~1.2 world units per child.")]
    [SerializeField] private float burstSpeed = 11f;

    [SerializeField] private float burstDuration = 0.35f;

    [Tooltip("Random jitter (degrees) on each child's launch angle, so a split " +
             "never looks mechanically symmetrical.")]
    [SerializeField] private float spreadJitter = 22f;

    [Header("Dispersal / Flanking")]
    [Tooltip("Seconds a child steers toward an offset point beside the target " +
             "instead of the target itself, so the pack surrounds rather than " +
             "queues. Vanilla targeting resumes when it expires.")]
    [SerializeField] private float flankDuration = 1.6f;

    [Tooltip("How far to the side of the target the flank point starts. Shrinks to " +
             "zero over flankDuration, so children spiral inward. MUST exceed " +
             "EnemyController.attackRange, or a child arrives, trips " +
             "'distance <= attackRange' and freezes on the spot.")]
    [SerializeField] private float flankRadius = 3.5f;

    [Header("Crowd Separation")]
    [Tooltip("Turn off only if you want blobs to merge into each other. " +
             "EnemyController assigns rb.linearVelocity every physics step, which " +
             "wipes Box2D's contact impulse; this adds the push back on top.")]
    [SerializeField] private bool separateFromNeighbours = true;

    [Tooltip("Neighbour scan radius, as a multiple of this enemy's own world " +
             "collider radius.")]
    [SerializeField] private float neighbourRadiusFactor = 2.2f;

    [Tooltip("The gap each blob defends, as a multiple of the two colliders' " +
             "combined radii. 1.0 = push only while physically overlapping, which " +
             "means they settle EXACTLY TOUCHING and read as one merged blob. " +
             "1.3 keeps a visible sliver of background between them.")]
    [SerializeField] private float personalSpace = 1.3f;

    [Tooltip("Push strength in world units/second at full overlap. Roughly match " +
             "MoveSpeed: much higher and they ping-pong, much lower and they still " +
             "merge under crowd pressure.")]
    [SerializeField] private float separationSpeed = 2.2f;

    [Tooltip("Cap on the added velocity as a fraction of MoveSpeed. Stops a mob of " +
             "twelve from launching anyone across the map.")]
    [SerializeField] private float maxPushFraction = 1.2f;

    [Tooltip("How much separation still applies WHILE attacking, 0-1. Do NOT set 0: " +
             "EnemyController zeroes velocity during an attack, so with separation " +
             "fully off two blobs attacking the same target slide together and stay " +
             "fused forever. 0.45 keeps them jostling without leaving attack range.")]
    [Range(0f, 1f)][SerializeField] private float attackSeparationFactor = 0.45f;

    [Tooltip("Max world units per physics step of DIRECT position correction when " +
             "colliders actually overlap. Velocity alone can't fix an overlap that " +
             "EnemyController is busy zeroing every step. 0 disables.")]
    [SerializeField] private float maxDepenetrationPerStep = 0.02f;

    [Header("Wind-up Attack")]
    [Tooltip("Replaces the instant melee tap with: compress, rear back, lunge, " +
             "damage. NOTE: assigning an attack override sets " +
             "EnemyController.HasAttackOverride, which suppresses the melee " +
             "ParryIndicator '!' (that flag exists for ranged enemies). Turn this " +
             "off, or exempt the Splitter in ParryIndicator's check.")]
    [SerializeField] private bool useWindupAttack = true;

    [Tooltip("Seconds from attack-cycle start to the hit. MUST be shorter than " +
             "EnemyData.AttackDuration (animationSpeed x attack.frameCount), or the " +
             "cycle ends before the blow lands. Keep it inside the parry window: " +
             "ParryStartTimeOffset <= this <= ParryEndTimeOffset.")]
    [SerializeField] private float windupTime = 0.30f;

    [SerializeField] private float windupPull = 3.5f;
    [SerializeField] private float lungeStrength = 9f;

    [Tooltip("Anticipation squash along the attack axis: the blob goes short and " +
             "wide as it gathers. 0.2-0.35.")]
    [Range(0f, 0.6f)][SerializeField] private float windupSquash = 0.28f;

    [Tooltip("Release stretch along the attack axis: long and thin as it lunges.")]
    [Range(0f, 0.9f)][SerializeField] private float lungeStretch = 0.40f;

    [Tooltip("Camera shake on connect. Ignored if no CameraShake exists in scene.")]
    [SerializeField] private float lungeCameraShake = 0.05f;

    [Tooltip("Directional goo spray at the contact point when the lunge lands.")]
    [SerializeField] private bool spawnImpactSplatter = true;

    [Header("Death")]
    [Tooltip("How hard the membrane tears apart on death. Scales debris velocity.")]
    [SerializeField] private float disintegrateForce = 3.2f;

    // ---- runtime ----
    private EnemyStats stats;
    private EnemyController controller;
    private Rigidbody2D rb;
    private Collider2D col;
    private ProceduralBlob blob;

    private int generation;
    private bool isChild;
    private float flankAngleDeg;
    private float flankTimer;
    private Transform flankMarker;
    private Transform coreTarget;
    private bool bursting;

    private Vector3 lockedScale;
    private bool scaleLocked;

    // Reused so the neighbour query never allocates.
    private static readonly Collider2D[] _scan = new Collider2D[24];
    private static readonly ContactFilter2D _scanFilter = new ContactFilter2D().NoFilter();

    public int Generation => generation;
    public float DisintegrateForce => disintegrateForce;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        controller = GetComponent<EnemyController>();
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        blob = GetComponent<ProceduralBlob>();
        if (blob == null) blob = gameObject.AddComponent<ProceduralBlob>();

        // The blob's own splash replaces the sprite-shatter, which needs a sprite
        // we don't have.
        if (stats != null) stats.ConfigureDeathVfx(0f);

        // Assigned in Awake so it's live before EnemyController.Start schedules its
        // first UpdateTarget tick.
        if (controller != null && useWindupAttack)
            controller.AttackHandlerOverride = WindupHit;
    }

    private void Start()
    {
        if (!scaleLocked) { lockedScale = transform.localScale; scaleLocked = true; }

        if (isChild)
        {
            flankTimer = flankDuration;
            SetupFlankMarker();
            StartCoroutine(BirthBurst());
        }
    }

    private void OnDestroy()
    {
        if (controller != null)
        {
            controller.AttackHandlerOverride = null;
            controller.PriorityTargetProvider = null;
        }
        if (flankMarker != null) Destroy(flankMarker.gameObject);
    }

    /// Called on a freshly instantiated child AFTER Awake, BEFORE Start.
    public void ConfigureAsChild(int gen, Vector3 scale, float launchAngleDeg)
    {
        generation = gen;
        isChild = true;
        flankAngleDeg = launchAngleDeg;
        lockedScale = scale;
        scaleLocked = true;
        transform.localScale = scale;
    }

    // YSortEntity rewrites localScale every frame. We run last and put it back.
    private void LateUpdate()
    {
        if (scaleLocked) transform.localScale = lockedScale;
    }

    // CROWD SEPARATION
    //
    // Runs after EnemyController.FixedUpdate has already assigned the pursuit
    // velocity. We ADD to it — never assign — so steering, stuck detection,
    // knockback and the attack freeze all keep working untouched.
    private void FixedUpdate()
    {
        if (!separateFromNeighbours) return;
        if (rb == null || stats == null || stats.IsDead()) return;
        if (rb.bodyType != RigidbodyType2D.Dynamic) return;
        if (bursting) return;                       // don't mush the birth launch

        if (controller != null && controller.IsBeingGrappled()) return;

        float myR = col != null ? col.bounds.extents.x : 0.5f;
        float scanR = myR * neighbourRadiusFactor;
        if (scanR <= 0.001f) return;

        int hits = Physics2D.OverlapCircle(transform.position, scanR, _scanFilter, _scan);
        if (hits <= 1) return;

        Vector2 push = Vector2.zero;
        Vector2 me = rb.position;
        int counted = 0;
        float deepestOverlap = 0f;

        for (int i = 0; i < hits; i++)
        {
            var other = _scan[i];
            if (other == null || other == col) continue;

            var otherStats = other.GetComponentInParent<EnemyStats>();
            if (otherStats == null || otherStats == stats) continue;
            if (otherStats.IsDead()) continue;

            Vector2 delta = me - (Vector2)other.transform.position;
            float d = delta.magnitude;

            // Perfectly co-located bodies give a zero-length normal — the exact
            // degenerate case Box2D can't resolve. Pick a direction so they always
            // come apart.
            if (d < 0.0001f)
            {
                push += Random.insideUnitCircle.normalized;
                counted++;
                deepestOverlap = Mathf.Max(deepestOverlap, myR);
                continue;
            }

            float otherR = other.bounds.extents.x;
            deepestOverlap = Mathf.Max(deepestOverlap, (myR + otherR) - d);

            // Defend a gap, not just non-overlap. With personalSpace = 1 the force
            // reaches zero the instant the colliders touch, so blobs settle perfectly
            // tangent — which renders as a single peanut-shaped mass.
            float desired = (myR + otherR) * Mathf.Max(1f, personalSpace);
            if (d >= desired) continue;

            // Linear falloff: hardest at full overlap, zero at the desired gap.
            push += (delta / d) * (1f - d / desired);
            counted++;
        }

        if (counted == 0) return;

        push /= counted;
        Vector2 dir = push.normalized;

        // Scale down (never off) while attacking. EnemyController zeroes velocity in
        // the attack branch, so if we skipped entirely, two blobs on the same target
        // would fuse and never come apart.
        float factor = (controller != null && controller.IsAttacking) ? attackSeparationFactor : 1f;

        rb.linearVelocity += Vector2.ClampMagnitude(push * (separationSpeed * factor),
                                                    stats.MoveSpeed * maxPushFraction);

        // Velocity can't undo an existing overlap when something else keeps zeroing
        // it. Nudge the body directly, capped so it never teleports through walls.
        if (deepestOverlap > 0f && maxDepenetrationPerStep > 0f)
            rb.position += dir * Mathf.Min(deepestOverlap * 0.5f, maxDepenetrationPerStep);
    }

    // BIRTH

    private IEnumerator BirthBurst()
    {
        bursting = true;

        // One frame so EnemyController.Start has cached its Rigidbody2D —
        // ApplyKnockback silently no-ops on a null rb.
        yield return null;
        if (this == null || controller == null) { bursting = false; yield break; }

        float rad = flankAngleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        controller.ApplyKnockback(dir, burstSpeed, burstDuration);

        if (blob != null)
        {
            blob.Pulse(4f);              // pop outward as it separates
            blob.Impulse(dir, 7f);       // and trail behind its own launch
        }

        yield return new WaitForSeconds(burstDuration);
        bursting = false;
    }

    // DISPERSAL

    private void SetupFlankMarker()
    {
        var go = new GameObject($"{name}_FlankPoint");
        flankMarker = go.transform;

        // Position it IMMEDIATELY. EnemyController's UpdateTarget (InvokeRepeating
        // with a 0s delay) can fire before our first Update, and a marker sitting at
        // world origin would send the child walking to (0,0).
        Transform anchor = ResolveAnchor();
        float rad = flankAngleDeg * Mathf.Deg2Rad;
        Vector2 offset = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * flankRadius;
        flankMarker.position = (anchor != null ? anchor.position : transform.position)
                               + (Vector3)offset;

        if (controller != null)
            controller.PriorityTargetProvider =
                () => (flankTimer > 0f && flankMarker != null) ? flankMarker : null;
    }

    private void Update()
    {
        if (flankTimer <= 0f) return;

        flankTimer -= Time.deltaTime;

        if (flankTimer <= 0f)
        {
            // Hand targeting back to EnemyController — it knows about towers,
            // decoys and line of sight, none of which we want to reimplement.
            if (controller != null) controller.PriorityTargetProvider = null;
            if (flankMarker != null) Destroy(flankMarker.gameObject);
            flankMarker = null;
            return;
        }

        Transform anchor = ResolveAnchor();
        if (anchor == null || flankMarker == null) return;

        // Offset shrinks to zero over the flank window, so the blob arcs wide and
        // spirals in rather than turning on a dime.
        float k = flankTimer / Mathf.Max(0.01f, flankDuration);
        float rad = flankAngleDeg * Mathf.Deg2Rad;
        Vector2 offset = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (flankRadius * k);
        flankMarker.position = anchor.position + (Vector3)offset;
    }

    private Transform ResolveAnchor()
    {
        var pr = PlayerRegistry.Instance;
        if (pr != null)
        {
            var nearest = pr.NearestAlive(transform.position, includeCloaked: true);
            if (nearest != null) return nearest.transform;
        }

        if (coreTarget == null)
        {
            GameObject core = GameObject.FindGameObjectWithTag("Core");
            if (core != null) coreTarget = core.transform;
        }
        return coreTarget;
    }

    // ATTACK

    // EnemyController.PerformHit calls this INSTEAD of the default melee hit (and
    // skips its own attack sound), at attack-cycle start.
    private void WindupHit(Transform target)
    {
        if (target == null) return;
        StartCoroutine(WindupRoutine(target));
    }

    private IEnumerator WindupRoutine(Transform target)
    {
        Vector2 dir = target != null
            ? ((Vector2)(target.position - transform.position)).normalized
            : Vector2.right;

        if (blob != null)
        {
            // Anticipation: squash ALONG the attack axis (it gathers itself, going
            // wide and short), lean away from the target, and hold the breath by
            // imploding slightly. Stretch() deforms the anchor ring, so the membrane
            // physically chases it — this is motion, not a scale keyframe.
            blob.Stretch(dir, -windupSquash);
            blob.Pulse(-3.5f);
            blob.Impulse(-dir, windupPull);
        }

        yield return new WaitForSeconds(windupTime);

        // May have died, been parry-stunned, or lost its target mid-wind-up.
        if (this == null || stats == null || stats.IsDead() || target == null) yield break;

        if (blob != null)
        {
            // Release: stretch along the axis (long and thin), throw the membrane
            // forward, and pop the pressure.
            blob.Stretch(dir, lungeStretch);
            blob.Pulse(2.5f);
            blob.Impulse(dir, lungeStrength);
        }

        PlayAttackSound();

        if (lungeCameraShake > 0f && CameraShake.Instance != null)
            CameraShake.Instance.Shake(lungeCameraShake, 0.10f);

        if (blob != null && spawnImpactSplatter)
        {
            // Splatter at the contact point — between us and the target, on their skin.
            float reach = col != null ? col.bounds.extents.x : 0.4f;
            Vector3 contact = transform.position + (Vector3)(dir * reach);
            BlobImpactVFX.Spawn(contact, dir, blob.WorldRadius, blob.CoreColor, blob.EdgeColor);
        }

        // Routes through the shared, parry-aware, shield-aware damage path.
        if (controller != null) controller.ApplyDamageToTarget(target);
    }

    private void PlayAttackSound()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;

        // Dedicated lunge sound if wired; otherwise fall back to the shared generic
        // enemy attack so the Splitter is never silent (same override idiom
        // EnemyController.PlayAttackSound uses).
        var ev = !FMODEvents.instance.splitterAttack.IsNull
            ? FMODEvents.instance.splitterAttack
            : FMODEvents.instance.enemyAttack;

        if (!ev.IsNull)
            AudioManager.instance.PlayOneShot(ev, transform.position);
    }

    private void PlaySplitSound()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (FMODEvents.instance.splitterSplit.IsNull) return;
        AudioManager.instance.PlayOneShot(FMODEvents.instance.splitterSplit, transform.position);
    }

    // SPLITTING

    /// Called by SplitterStats.Die() before the base class tears this object down.
    public void SpawnChildren()
    {
        if (generation >= maxGeneration) return;

        if (childPrefab == null)
        {
            Debug.LogWarning($"[Splitter] {name} has no childPrefab assigned — it will not split.");
            return;
        }

        // Past the guards: this is a genuine split, so fire the tearing sound exactly
        // once, here, rather than in Die() (which can be re-entered and also runs on
        // the final generation that never splits).
        PlaySplitSound();

        // Derive the spawn offset from the ACTUAL collider, in world units.
        // bounds.extents already includes lossyScale, so no prefab-scale math can go
        // wrong here — this is what previously spawned children inside each other.
        float parentWorldR = col != null ? col.bounds.extents.x : 0.5f;
        float sep = Mathf.Max(0.05f, parentWorldR * childScale * separationFactor);

        float baseAngle = Random.Range(0f, 360f);
        Vector3 parentScale = scaleLocked ? lockedScale : transform.localScale;

        var spawnedColliders = new List<Collider2D>(splitCount);

        for (int i = 0; i < splitCount; i++)
        {
            float ang = baseAngle + (360f / splitCount) * i + Random.Range(-spreadJitter, spreadJitter);
            float rad = ang * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);

            GameObject go = Instantiate(childPrefab, transform.position + dir * sep, Quaternion.identity);

            // Awake has now run on the child (EnemyData is already cloned per
            // instance by EnemyStats.Awake, so mutating it below is safe and never
            // touches the shared asset). Start has NOT run yet — which is why the
            // health bar, built in Start, sees the reduced maxHealth.
            var cc = go.GetComponent<SplitterController>();
            if (cc != null) cc.ConfigureAsChild(generation + 1, parentScale * childScale, ang);
            else go.transform.localScale = parentScale * childScale;

            var cs = go.GetComponent<SplitterStats>();
            if (cs != null)
            {
                cs.SetupAsChild(childHealthFraction);

                // Only the original drops energy. Otherwise a Splitter is a farm.
                cs.DisableEnergyDrops();

                if (cs.enemyData != null)
                {
                    cs.enemyData.moveSpeed *= childSpeedGain;
                    cs.enemyData.damage *= childDamageFraction;
                }
            }

            var childCol = go.GetComponent<Collider2D>();
            if (childCol != null)
            {
                spawnedColliders.Add(childCol);
                // The corpse is still solid for the rest of this frame.
                if (col != null) Physics2D.IgnoreCollision(childCol, col, true);
            }
        }

        // Siblings phase through each other while they separate. Two near-concentric
        // circles produce a degenerate contact normal, and the solver would rather
        // hold them fused than push them apart.
        for (int a = 0; a < spawnedColliders.Count; a++)
            for (int b = a + 1; b < spawnedColliders.Count; b++)
                SiblingPhase.Begin(spawnedColliders[a], spawnedColliders[b], siblingPhaseDuration);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var c = GetComponent<Collider2D>();
        float r = c != null ? c.bounds.extents.x : 0.5f;

        Gizmos.color = new Color(0.78f, 0.33f, 0.94f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, r * childScale * separationFactor);

        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, r * neighbourRadiusFactor);

        Gizmos.color = new Color(0.55f, 0.2f, 0.75f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, flankRadius);
    }
#endif
}


// SIBLING COLLISION PHASING
// Temporarily disables collision between two colliders, then restores it — but
// only if both still exist. Lives on its own GameObject so it survives either
// collider being destroyed mid-burst.
public class SiblingPhase : MonoBehaviour
{
    public static void Begin(Collider2D a, Collider2D b, float duration)
    {
        if (a == null || b == null || duration <= 0f) return;

        Physics2D.IgnoreCollision(a, b, true);

        var go = new GameObject("SiblingPhase");
        go.AddComponent<SiblingPhase>().StartCoroutine(Restore(go, a, b, duration));
    }

    private static IEnumerator Restore(GameObject host, Collider2D a, Collider2D b, float duration)
    {
        yield return new WaitForSeconds(duration);
        if (a != null && b != null) Physics2D.IgnoreCollision(a, b, false);
        Destroy(host);
    }
}
