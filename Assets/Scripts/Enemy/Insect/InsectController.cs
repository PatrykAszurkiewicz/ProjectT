using UnityEngine;

// Insect — a burrowing enemy. It ignores the player and hunts the nearest
// structure (any live tower, or the central Core). Instead of walking there on
// the surface it now:
//   DIGS IN  — the sprite sinks and fades out while a mound of earth rises
//      and dirt sprays; once it's under, the insect itself is INVISIBLE.
//   TUNNELS   — it travels underground (slower than a surface walk) toward the
//      nearest structure. Only a moving mound of raised earth + a churned dirt
//      trail mark its path. Its body collider is off, so it's untouchable and
//      passes under everything while burrowed.
//   LEAPS OUT — a short distance from the target it erupts from the ground in
//      an arc (with a ground shadow), fading back into view, and lands in range.
//   ATTACKS   — it hits the structure repeatedly using the normal
//      EnemyController attack cycle (hit frames, parry window, SFX, damage,
//      retarget-after-kill are all reused, not re-implemented).

[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(EnemyStats))]
public class InsectController : MonoBehaviour
{
    [Header("Dig / tunnel timing")]
    [Tooltip("Seconds for the dig-in (sprite sinks & fades out, mound rises, dirt sprays).")]
    [SerializeField] private float diveDuration = 0.5f;

    [Tooltip("Underground travel speed as a fraction of the enemy's normal MoveSpeed. " +
             "Below 1 = digging is slower than walking (deliberate, readable).")]
    [SerializeField] private float undergroundSpeedMultiplier = 0.85f;

    [Header("Leap out")]
    [Tooltip("How far from the target the insect surfaces before leaping. It pops out " +
             "'from some distance' and jumps the rest of the way in.")]
    [SerializeField] private float emergeDistanceFromTarget = 3f;

    [Tooltip("Seconds the leap-out arc takes.")]
    [SerializeField] private float jumpDuration = 0.5f;

    [Tooltip("Peak height of the leap arc, as a multiple of the sprite height.")]
    [SerializeField] private float jumpHeightFactor = 1.6f;

    [Tooltip("Where the leap lands, as a fraction of AttackRange from the target " +
             "(0.85 = just inside striking range).")]
    [SerializeField] private float landingRangeFactor = 0.85f;

    [Header("Re-burrow")]
    [Tooltip("Extra reach added to AttackRange. After a kill, if the next structure is " +
             "farther than (AttackRange + this), the insect digs and tunnels to it " +
             "instead of walking there on the surface.")]
    [SerializeField] private float reBurrowExtraDistance = 2f;

    [Header("Spawn")]
    [Tooltip("Random delay before the first dig, so a spawned pack doesn't burrow in lockstep.")]
    [SerializeField] private float maxSpawnDelay = 0.5f;

    [Header("Attack animation")]
    [Tooltip("Make the attack combat timeline (swing length + hit/parry frame indices) " +
             "match the number of frames in the Attacking sprite folder, so the folder is " +
             "the single source of truth for the attack length. With this on you only set " +
             "hitFrame / parryFrameStart / parryFrameEnd on the EnemyData as indices into the " +
             "attack animation (0 = first attack frame). Turn off to keep the EnemyData's own " +
             "attack.frameCount.")]
    [SerializeField] private bool syncAttackTimelineToFolder = true;

    private enum Phase { SpawnIdle, Diving, Underground, Jumping, Attacking, Idle }

    private EnemyController enemyController;
    private EnemyStats stats;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private InsectBurrowVFX vfx;
    private EnemyAnimationController animCtrl;   // used to detect death so we never leave the body pinned underground

    // Multi-folder sprite animator for the four burrow states. When it loaded its
    // sprites (useSpriteAnims == true) it OWNS the visual through dive / tunnel /
    // emerge / attack, and we drop the alpha-fade + procedural covering mound the
    // old placeholder visuals used (the sprites now depict the earth themselves).
    // If the folders are missing, we fall straight back to the legacy visuals.
    private InsectAnimator insectAnim;

    // ── Burrow sprite frames ─────────────────────────────────────────────────
    // Assign these on Insect.prefab from
    //   Assets/.../EnemySprites/Insect/{FromAboveToUnder, MovingUnderground,
    //                                    FromUnderToAbove, Attacking}
    // Leave empty to keep the legacy Resources paths baked into InsectAnimator.
    [Header("Burrow Sprite Frames (direct references — preferred)")]
    [SerializeField] private Sprite[] diveFrames;
    [SerializeField] private Sprite[] undergroundFrames;
    [SerializeField] private Sprite[] emergeFrames;
    [SerializeField] private Sprite[] attackFrames;

    private bool HasBurrowFrames =>
        (diveFrames != null && diveFrames.Length > 0) ||
        (undergroundFrames != null && undergroundFrames.Length > 0) ||
        (emergeFrames != null && emergeFrames.Length > 0);
    private bool useSpriteAnims;
    private bool loggedAttackDiag;   // one-shot diagnostic on first attack

    [Header("Attack clip window")]
    [Tooltip("First attack frame to play (0-based). Usually 0.")]
    [SerializeField] private int attackWindowStart = 0;
    [Tooltip("How many attack frames to play from the start. 0 = the whole clip. " +
             "Set e.g. 12 to play only the first 12 frames and skip duplicate/held tail frames.")]
    [SerializeField] private int attackWindowCount = 0;

    [Header("Attack two-speed profile (optional)")]
    [Tooltip("First N attack frames play at base speed; the rest play sped up. 0 = uniform speed (fitted to the bite cadence).")]
    [SerializeField] private int attackNormalFrames = 0;
    [Tooltip("Base seconds-per-frame for the leading (normal-speed) attack frames.")]
    [SerializeField] private float attackBaseSecPerFrame = 0.08f;
    [Tooltip("How many times faster the tail (biting) frames play. 1 = same speed, e.g. 6 = 6x faster.")]
    [SerializeField] private float attackTailSpeedup = 1f;
    private float lastInsectSwingStart = -999f;  // AttackCycleStartTime of the last swing we played
    private float insectSwingRestTime;           // Time.time at which the current strike clip has played out
    private bool insectAttackPlaying;            // a strike clip is mid-play (vs resting on the ready frame)

    private Transform coreTarget;
    private Collider2D[] bodyColliders;   // non-trigger colliders toggled for underground invulnerability
    private float spriteHeight = 1f;

    // Y-sort control. While tunnelling we pin the body sprite BELOW the terrain
    // (grass + obstacles) via this entity's override, so the "digging" sprite no
    // longer paints on top of walls / rocks it passes under. Resolved lazily
    // because EnemyController adds the YSortEntity in its own Start().
    private YSortEntity ySort;
    private int undergroundSortingOrder = int.MinValue;   // resolved once, cached

    private Phase phase = Phase.SpawnIdle;
    private float phaseTimer;
    private float spawnDelay;

    // Leap state
    private Vector2 jumpStart, jumpEnd;
    private Vector3 emergePoint;   // fixed ground spot the mound recedes at while we fly
    private float jumpTimer;

    private void Awake()
    {
        // Assign the priority provider in Awake so it's in place before
        // EnemyController.Start schedules its first UpdateTarget tick — the very
        // first target resolution already skips the player and picks a structure.
        enemyController = GetComponent<EnemyController>();
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        // Immovable body: Insect / EliteInsect are stationary ambush enemies. A
        // Kinematic body cannot be shoved by the player walking into it, by
        // knockback, or by the grappling hook, while still moving fully under
        // script control (tunnel/leap/land use linearVelocity + MovePosition,
        // which Kinematic bodies honor). Forced here so it holds regardless of the
        // prefab's serialized Body Type. EnemyDeathVFX still switches it to Static
        // on death, and EnemyController already skips knockback for non-Dynamic
        // bodies, so nothing else changes.
        if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;
        spriteRenderer = GetComponent<SpriteRenderer>();
        animCtrl = GetComponent<EnemyAnimationController>();

        if (enemyController != null)
            enemyController.PriorityTargetProvider = GetClosestStructure;

        // Cache the physical (non-trigger) colliders so we can switch the body
        // off while burrowed (pass under towers + untouchable underground) and
        // back on to be hittable while surfaced and attacking.
        var cols = GetComponents<Collider2D>();
        int n = 0;
        for (int i = 0; i < cols.Length; i++) if (!cols[i].isTrigger) n++;
        bodyColliders = new Collider2D[n];
        int k = 0;
        for (int i = 0; i < cols.Length; i++) if (!cols[i].isTrigger) bodyColliders[k++] = cols[i];
    }

    private void Start()
    {
        CacheCore();

        if (spriteRenderer != null && spriteRenderer.sprite != null)
            spriteHeight = Mathf.Max(0.25f, spriteRenderer.bounds.size.y);

        // Attach + initialise the ground-effects helper.
        vfx = GetComponent<InsectBurrowVFX>();
        if (vfx == null) vfx = gameObject.AddComponent<InsectBurrowVFX>();
        Bounds b = (spriteRenderer != null && spriteRenderer.sprite != null)
            ? spriteRenderer.bounds
            : new Bounds(transform.position, new Vector3(1f, 1f, 0f));
        vfx.Initialize(transform, b);

        // Attach + probe the multi-folder sprite animator. If its folders loaded,
        // it takes over the burrow visuals and we suppress the legacy covering mound.
        insectAnim = GetComponent<InsectAnimator>();
        if (insectAnim == null)
        {
            insectAnim = gameObject.AddComponent<InsectAnimator>();

            // InsectAnimator is added AT RUNTIME, so it is not on Insect.prefab and the
            // prefab migration tool could never see it — its four burrow folders exist
            // only as C# field initializers, with no serialized data to read. Hand over
            // the direct references from THIS component, which IS on the prefab.
            // (EliteInsect avoided this because EliteInsectVisuals sits on its prefab and
            // passes its own arrays through Configure().)
            if (HasBurrowFrames)
                insectAnim.Configure(null, null, null, null,
                                     diveFrames, undergroundFrames, emergeFrames, attackFrames);
        }
        // Trim the attack clip to the configured window (drops duplicate/held tail
        // frames without re-exporting). 0 count = play the whole clip.
        if (attackWindowCount != 0 || attackWindowStart != 0)
            insectAnim.SetAttackWindow(attackWindowStart, attackWindowCount);
        if (attackNormalFrames > 0 && attackTailSpeedup > 1f)
            insectAnim.SetAttackSpeedProfile(attackNormalFrames, attackBaseSecPerFrame, attackTailSpeedup);
        useSpriteAnims = insectAnim.Ready;

        if (useSpriteAnims)
        {
            vfx?.SetBurrowedVisual(false, 0f);                 // never show the procedural mound
            insectAnim.SetFacing(DirToTravelTarget());          // face the core it will head for
            insectAnim.ShowFirstFrame(InsectAnimator.Clip.Dive); // surfaced pre-dive pose

            SyncAttackTimelineToFolder();
        }

        // Enter spawn-idle: hold briefly (controller suspended so it doesn't walk),
        // then dig. Randomised so a group doesn't move as one.
        spawnDelay = Random.Range(0f, maxSpawnDelay);
        phase = Phase.SpawnIdle;
        phaseTimer = 0f;
        SuspendController(true);
    }

    // Make the combat attack timeline exactly as long as the authored attack
    // animation. The hit and parry windows are time-based — computed as
    // frameIndex * AttackAnimSpeed from the cycle start (see EnemyController.
    // IsInParryWindow / AttackCycle) — so aligning attack.frameCount with the
    // folder's frame count means hitFrame / parryFrameStart / parryFrameEnd on the
    // EnemyData index directly into the animation you see (frame 0 = first attack
    // frame). stats.enemyData is a per-instance clone (EnemyStats.Awake), so this
    // never mutates the shared asset. speedOverride is preserved, so you still tune
    // swing pace via EnemyData.attack.speedOverride (or the global animationSpeed).
    private void SyncAttackTimelineToFolder()
    {
        if (!syncAttackTimelineToFolder) return;
        if (insectAnim == null || !insectAnim.HasAttack) return;
        if (stats == null || stats.enemyData == null) return;

        int frames = insectAnim.AttackFrameCount;
        if (frames <= 0) return;

        AnimationFrameRange a = stats.enemyData.attack;   // struct copy
        if (a.startFrame == 0 && a.frameCount == frames) return; // already aligned

        a.startFrame = 0;
        a.frameCount = frames;                            // speedOverride preserved by the copy
        stats.enemyData.attack = a;
    }

    private void OnDestroy()
    {
        // Drop the delegate so nothing holds a stale reference to this (destroyed)
        // component, restore visibility (in case of pooling), and don't leave the
        // controller suspended for a reused object.
        if (enemyController != null)
        {
            enemyController.PriorityTargetProvider = null;
            enemyController.ExternalMovementControl = false;
        }
        ExitUndergroundSorting();   // don't leave a reused/pooled body pinned underground
        SetSpriteAlpha(1f);
    }

    //  Phase driving 

    private void Update()
    {
        // If the enemy is dying/disabled, stop steering it.
        if (enemyController == null || !enemyController.enabled) return;

        phaseTimer += Time.deltaTime;

        switch (phase)
        {
            case Phase.SpawnIdle:
                if (phaseTimer >= spawnDelay) BeginDive();
                break;

            case Phase.Diving:
                {
                    float t = Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, diveDuration));
                    if (useSpriteAnims)
                        vfx?.EmitDigDust(GroundPoint());        // extra kicked-up dirt while digging in
                    else
                        vfx?.SetBurrowedVisual(true, t);        // legacy: mound rises as the bug sinks
                    if (t >= 1f) EnterUnderground();
                    break;
                }

            case Phase.Underground:
                if (useSpriteAnims)
                    vfx?.EmitDigDust(GroundPoint());            // dust churning up along the tunnel
                else
                    vfx?.SetBurrowedVisual(true, 1f);           // legacy: travelling mound bump
                break;

            case Phase.Jumping:
                // Visuals (mound recede at the exit hole) driven from FixedUpdate
                // where the arc is integrated; nothing to do here.
                break;

            case Phase.Attacking:
                TickAttacking();
                break;

            case Phase.Idle:
                if (GetTravelTarget() != null) BeginDive();
                break;
        }
    }

    // Set whenever FixedUpdate bailed because the EnemyController was disabled, so
    // the first frame back can repair any state that assumes we ran every frame.
    private bool wasSuspendedLastFixedUpdate;

    private void FixedUpdate()
    {
        if (rb == null || enemyController == null || !enemyController.enabled)
        {
            // Something else owns the body right now — a death routine, or a crowd
            // control effect (ConfusedEnemy / BerserkEnemy suspend EnemyController and
            // drive the Rigidbody2D themselves). Remember it: see the repair below.
            wasSuspendedLastFixedUpdate = true;
            return;
        }

        if (wasSuspendedLastFixedUpdate)
        {
            wasSuspendedLastFixedUpdate = false;
            RecoverFromSuspension();
        }

        switch (phase)
        {
            case Phase.SpawnIdle:
            case Phase.Diving:
            case Phase.Idle:
                rb.linearVelocity = Vector2.zero;           // stationary; we own the body
                break;

            case Phase.Underground:
                DriveUnderground();
                break;

            case Phase.Jumping:
                DriveJump();
                break;

                // Phase.Attacking: controller owns the body (ExternalMovementControl == false)
        }
    }

    // Keep the sprite's alpha honest AFTER the animation controller has had its say
    // this frame — that's what makes the bug actually disappear underground instead
    // of looking half-there. We only manage alpha during the burrow phases so hit
    // flashes etc. are untouched the rest of the time.
    private void LateUpdate()
    {
        // If the insect is dying, make sure it's not left pinned underground — the
        // death animation must sort normally so it's actually visible. Runs before
        // the controller-disabled early-out below, since death may disable it.
        if (animCtrl != null && animCtrl.IsDying)
        {
            ExitUndergroundSorting();
            vfx?.ShowTunnelCrest(false);
        }

        if (spriteRenderer == null) return;
        if (enemyController == null || !enemyController.enabled) { return; }

        // Sprite-anim mode: the dive / tunnel / emerge CLIPS carry the appear &
        // disappear themselves, so we simply keep the insect fully opaque — an
        // alpha of 0 here would hide the tunnelling frames. The body stays
        // untouchable underground via its colliders (SetBodyEnabled), not alpha.
        if (useSpriteAnims)
        {
            switch (phase)
            {
                case Phase.Diving:
                case Phase.Underground:
                case Phase.Jumping:
                    SetSpriteAlpha(1f);
                    break;
            }
            return;
        }

        // Legacy placeholder visuals: fade the flat sprite in/out around the mound.
        switch (phase)
        {
            case Phase.Diving:
                {
                    float t = Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, diveDuration));
                    SetSpriteAlpha(1f - t);                     // fade out as it digs in
                    break;
                }
            case Phase.Underground:
                SetSpriteAlpha(0f);                         // fully gone
                break;
            case Phase.Jumping:
                {
                    float f = Mathf.Clamp01(jumpTimer / Mathf.Max(0.02f, jumpDuration * 0.25f));
                    SetSpriteAlpha(f);                          // burst back into view as it erupts
                    break;
                }
        }
    }

    //  Transitions 

    private void BeginDive()
    {
        phase = Phase.Diving;
        phaseTimer = 0f;

        SuspendController(true);
        SetBodyEnabled(false);      // untouchable + passes under structures while burrowed
        ShowHealthBar(false);
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // Still at the surface (sinking in) — keep normal Y-sort. The crest is a
        // tunnelling cue only, so it's off here.
        ExitUndergroundSorting();
        vfx?.ShowTunnelCrest(false);

        vfx?.Burst(GroundPoint(), 1f);

        if (useSpriteAnims)
        {
            insectAnim.ShowMoundPinned(transform.position, DirToTravelTarget()); // sand stays at the hole, angled toward travel
            insectAnim.SetFacing(DirToTravelTarget());   // upright, facing where it'll head
            SetSpriteAlpha(1f);
            insectAnim.PlayOnce(InsectAnimator.Clip.Dive, diveDuration);  // sink-in over the dive
        }
        else
        {
            vfx?.SetBurrowedVisual(true, 0f);
        }
    }

    private void EnterUnderground()
    {
        phase = Phase.Underground;
        phaseTimer = 0f;

        // Now truly under the map: pin the body sprite below the terrain so it can
        // never draw on top of the walls / rocks it tunnels beneath, and raise the
        // above-grass soil crest so the tunnel path stays clearly visible. In legacy
        // mode the procedural mound already provides that bump, so the crest is only
        // needed when the authored underground art is the (now-buried) body sprite.
        EnterUndergroundSorting();
        //vfx?.ShowTunnelCrest(useSpriteAnims);
        vfx?.ShowTunnelCrest(false);


        if (useSpriteAnims)
        {
            insectAnim.HideMound();   // the MovingUnderground clip already shows a mound
            SetSpriteAlpha(1f);
            insectAnim.PlayLoop(InsectAnimator.Clip.Underground);         // tunnelling mound loop
        }
        else
        {
            SetSpriteAlpha(0f);
        }
    }

    private void BeginJump()
    {
        phase = Phase.Jumping;
        phaseTimer = 0f;
        jumpTimer = 0f;

        Transform target = GetTravelTarget();
        Vector2 here = transform.position;
        Vector2 tgt = target != null ? (Vector2)target.position : here;

        Vector2 to = tgt - here;
        float dist = to.magnitude;
        Vector2 dir = dist > 0.0001f ? to / dist : Vector2.right;

        float landingDist = Mathf.Max(0.4f, enemyController.AttackRange * landingRangeFactor);
        float travel = Mathf.Max(0f, dist - landingDist);   // stop short so we land in range

        jumpStart = here;
        jumpEnd = here + dir * travel;
        emergePoint = GroundPoint();                        // mound stays & sinks at the exit hole

        // Breaking the surface: restore normal Y-sort (it's above ground now) and
        // drop the tunnel crest.
        ExitUndergroundSorting();
        vfx?.ShowTunnelCrest(false);

        vfx?.Erupt(emergePoint, dir);                       // directional soil geyser + shock ring
        vfx?.Burst(emergePoint, 1f);                        // eruption at the hole

        if (useSpriteAnims)
        {
            // Keep the buried-tail patch HIDDEN through the whole leap. The exit
            // hole is already covered by the eruption (Erupt/Burst) and the pinned
            // burrow-cover VFX in DriveJump, so the patch isn't needed here — and
            // showing it pinned at the hole is exactly what made it visibly "shift
            // forward" to the attack spot on landing. It reappears fresh at the
            // attack position in EnterAttacking, masked by the SoilBurst.
            insectAnim.HideMound();
            insectAnim.SetFacing(DirToTravelTarget());   // upright rear, facing the target
            SetSpriteAlpha(1f);
            insectAnim.PlayOnce(InsectAnimator.Clip.Emerge, jumpDuration); // erupt over the arc
        }
    }

    private void EnterAttacking()
    {
        phase = Phase.Attacking;
        phaseTimer = 0f;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(jumpEnd);                        // land exactly in range
        }

        SetSpriteAlpha(1f);
        SetBodyEnabled(true);       // hittable again
        ShowHealthBar(true);
        ExitUndergroundSorting();   // defensive: back to normal Y-sort on the surface
        vfx?.ShowTunnelCrest(false);
        vfx?.SetBurrowedVisual(false, 0f);
        vfx?.HideJumpShadow();
        // Broad soil eruption from the ground. Doubles as the landing impact AND
        // masks the buried-tail patch swapping from its pinned emergence pose to
        // following the body (that swap otherwise reads as a small back-and-forth pop).
        vfx?.SoilBurst(GroundPoint(), 1.15f);
        vfx?.Burst(GroundPoint(), 0.8f);                    // landing thud of dust

        // Loop the attack animation continuously for as long as we're surfaced and
        // attacking. The actual damage / parry timing is driven separately by
        // EnemyController's attack cycle (time-based, see AttackCycle / IsInParryWindow),
        // so the loop is purely visual — this keeps the insect animating through the
        // cooldown between swings instead of freezing on a single frame. It loops at
        // the authored attack speed so one loop matches one swing's length.
        if (useSpriteAnims)
        {
            if (!loggedAttackDiag)
            {
                loggedAttackDiag = true;
                Debug.Log($"[Insect] attack setup — useSpriteAnims={useSpriteAnims}, " +
                          $"HasAttack={insectAnim.HasAttack}, attackFrames={insectAnim.AttackFrameCount}. " +
                          "If HasAttack is False the attack folder didn't load and the freeze/no-angle come from the main sheet.");
            }

            insectAnim.SetAim(DirToTravelTarget());        // angle the strike toward the target
            insectAnim.ShowMoundFollowing();               // tail stays buried in the ground patch
            if (insectAnim.HasAttack)
                insectAnim.ShowFrame(InsectAnimator.Clip.Attack, 0);   // ready pose; the swing itself is driven per-cycle in TickAttacking
            else
                StartAttackLoop();                         // no attack folder: looping stand-in (never the frozen main sheet)
            lastInsectSwingStart = -999f;                  // let the current/next combat cycle trigger the first strike
            insectAttackPlaying = false;
        }

        SuspendController(false);   // hand the body back: the real attack cycle takes over
    }

    // Loops the attack animation at the authored attack speed. Clamped to a sane
    // visible range so a mis-set animationSpeed on the EnemyData can't make the loop
    // crawl (which would look like a freeze on one frame). Purely visual — combat
    // hit/parry timing is unaffected (that's driven by EnemyController.AttackCycle).
    private void StartAttackLoop()
    {
        if (!useSpriteAnims || insectAnim == null) return;
        float spf = (stats != null && stats.enemyData != null)
            ? stats.enemyData.AttackAnimSpeed
            : 0.08f;
        spf = Mathf.Clamp(spf, 0.02f, 0.12f);

        if (insectAnim.HasAttack)
        {
            insectAnim.PlayLoop(InsectAnimator.Clip.Attack, spf);
            return;
        }

        // No attack folder loaded. Do NOT hand the sprite back to the shared main-
        // sheet controller — it plays its attack once per swing and holds a frame
        // through the cooldown, which reads as a ~2s freeze between swings. Instead
        // keep OUR animator in control with a looping stand-in (the reared emerge
        // pose reads best) so the insect stays animated. (The diagnostic in
        // EnterAttacking still logs that the attack folder didn't load.)
        if (insectAnim.Has(InsectAnimator.Clip.Emerge))
            insectAnim.PlayLoop(InsectAnimator.Clip.Emerge, spf);
        else if (insectAnim.Has(InsectAnimator.Clip.Underground))
            insectAnim.PlayLoop(InsectAnimator.Clip.Underground, spf);
        else if (insectAnim.Has(InsectAnimator.Clip.Dive))
            insectAnim.PlayLoop(InsectAnimator.Clip.Dive, spf);
    }

    // How long to play ONE insect strike clip. The insect's strike is a multi-frame
    // FOLDER animation that is independent of the main sprite sheet. Crucially, many
    // EnemyData attacks are a 1-frame instant-damage placeholder (frameCount = 1 →
    // AttackDuration ≈ 0.1s); timing the 25-frame strike to that makes it flash past
    // invisibly and the insect looks frozen. So size the strike to the real bite
    // cadence (attack cooldown) — floored so it's always clearly animated, capped so
    // it stays snappy — and only prefer the authored attack duration if it's actually
    // long enough to display the clip.
    private float InsectSwingDuration()
    {
        float cadence = (enemyController != null) ? enemyController.GetAttackCooldown() : 0.6f;
        float mainDur = (stats != null && stats.enemyData != null) ? stats.enemyData.AttackDuration : 0f;
        return Mathf.Clamp(Mathf.Max(cadence, mainDur), 0.4f, 2f);
    }

    private void EnterIdle()
    {
        phase = Phase.Idle;
        phaseTimer = 0f;
        SuspendController(true);
        SetBodyEnabled(true);
        ShowHealthBar(true);
        SetSpriteAlpha(1f);
        ExitUndergroundSorting();
        vfx?.ShowTunnelCrest(false);
        vfx?.SetBurrowedVisual(false, 0f);
        vfx?.HideJumpShadow();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (useSpriteAnims)
        {
            insectAnim.HideMound();
            insectAnim.SetFacing(DirToTravelTarget());
            insectAnim.ShowFirstFrame(InsectAnimator.Clip.Dive); // surfaced, waiting
        }
    }

    //  Per-phase behaviour 

    private void DriveUnderground()
    {
        Transform target = GetTravelTarget();
        if (target == null) { EnterIdle(); return; }

        Vector2 to = (Vector2)target.position - (Vector2)transform.position;
        float dist = to.magnitude;

        // Surface a set distance out, then leap the rest of the way.
        if (dist <= emergeDistanceFromTarget) { BeginJump(); return; }

        Vector2 dir = dist > 0.0001f ? to / dist : Vector2.zero;
        float speed = (stats != null ? stats.MoveSpeed : 2f) * Mathf.Max(0.1f, undergroundSpeedMultiplier);
        rb.linearVelocity = dir * speed;

        if (useSpriteAnims) insectAnim.SetAim(dir);   // rotate the mound toward the target

        vfx?.EmitTrail(GroundPoint(), dir);
        vfx?.SetTunnelCrest(GroundPoint(), dir);        // travelling soil bump rides the tunnel
        vfx?.EmitUndergroundRipple(GroundPoint(), dir); // rhythmic ripples + forward soil spray
    }

    /// Repair state after FixedUpdate was suspended for one or more physics steps.
    ///
    /// THE BUG THIS FIXES. DriveJump() is a MovePosition substitute: every FixedUpdate
    /// it sets the exact velocity needed to reach the next point of a PRE-COMPUTED arc
    /// in a single step — `(desired - rb.position) / Time.fixedDeltaTime`. That is only
    /// valid while we run every step, because it assumes rb.position is still where the
    /// arc left it.
    ///
    /// Crowd control breaks that assumption. ConfusedEnemy / BerserkEnemy (augments 76
    /// and 29) disable EnemyController and steer the body somewhere else entirely. Our
    /// FixedUpdate bails for the whole duration, freezing the leap mid-arc, and when the
    /// effect ends the controller comes back and DriveJump resumes against the ORIGINAL
    /// jumpStart/jumpEnd. The position delta is now metres instead of centimetres, and
    /// dividing it by a 0.02s step yields a velocity ~50x the intended speed — the
    /// Insect rockets across the map, finishes the arc, lands, and calmly walks back to
    /// the Core. That is exactly the "flew away fast, then came back" behaviour.
    ///
    /// Re-anchoring the arc to where the body actually is keeps the leap's remaining
    /// shape and duration while removing the impossible catch-up velocity.
    private void RecoverFromSuspension()
    {
        if (phase != Phase.Jumping) return;

        // Shift the whole arc by however far the body was displaced while we were out.
        float u = Mathf.Clamp01(jumpTimer / Mathf.Max(0.05f, jumpDuration));
        float ease = 1f - (1f - u) * (1f - u);
        Vector2 expectedGround = Vector2.Lerp(jumpStart, jumpEnd, ease);
        Vector2 drift = rb.position - expectedGround;

        if (drift.sqrMagnitude > 0.0001f)
        {
            jumpStart += drift;
            jumpEnd += drift;
        }
    }

    // Integrate the leap arc: horizontal ease from jumpStart->jumpEnd plus a
    // sin() hop in +Y for air time. We move by velocity (not MovePosition) so the
    // animation controller still sees a sensible facing direction.
    private void DriveJump()
    {
        jumpTimer += Time.fixedDeltaTime;
        float u = Mathf.Clamp01(jumpTimer / Mathf.Max(0.05f, jumpDuration));

        float ease = 1f - (1f - u) * (1f - u);              // ease-out horizontal
        Vector2 ground = Vector2.Lerp(jumpStart, jumpEnd, ease);
        float hop = Mathf.Sin(Mathf.PI * u) * (spriteHeight * Mathf.Max(0f, jumpHeightFactor));
        Vector2 desired = ground + Vector2.up * hop;

        Vector2 catchUp = (desired - rb.position) / Time.fixedDeltaTime;

        // Hard ceiling, independent of the re-anchoring above. Any single step of this
        // arc can only ever need roughly (arc length / duration) plus the hop, so
        // anything far above that means rb.position was moved by something outside this
        // controller. Without the clamp that becomes a launch: the velocity is applied
        // to a Kinematic body, which has no drag and no collision response, so it
        // carries the Insect straight through the level. 4x leaves ample headroom for
        // the ease-out peak and the sine hop while still catching a real desync.
        float arcSpeed = Vector2.Distance(jumpStart, jumpEnd) / Mathf.Max(0.05f, jumpDuration);
        float maxSpeed = Mathf.Max(1f, arcSpeed + spriteHeight / Mathf.Max(0.05f, jumpDuration)) * 4f;
        if (catchUp.sqrMagnitude > maxSpeed * maxSpeed)
            catchUp = catchUp.normalized * maxSpeed;

        rb.linearVelocity = catchUp;

        // Leap ground-shadow removed: the squashed ellipse read as a stray,
        // perpendicular blob beside the (aimed) soil sprite. HideJumpShadow (called
        // in EnterAttacking / EnterIdle) keeps the shadow renderer off, and with no
        // SetJumpShadow call it is never activated in the first place.

        // Light dust motes trailing through the air so the leap arc reads.
        vfx?.EmitAirTrail(transform.position);

        // Legacy only: recede the procedural mound at the fixed exit hole over the
        // first ~60% of the arc. In sprite-anim mode the Emerge clip shows the exit.
        if (!useSpriteAnims)
        {
            float moundCover = 1f - Mathf.Clamp01(jumpTimer / Mathf.Max(0.05f, jumpDuration * 0.6f));
            vfx?.SetBurrowedVisualPinned(true, moundCover, emergePoint);
        }

        if (u >= 1f) EnterAttacking();
    }

    private void TickAttacking()
    {
        Transform target = GetTravelTarget();
        if (target == null) { EnterIdle(); return; }

        // Keep the strike pointed at the target (it may have retargeted to a new
        // structure after a kill, or been shoved around).
        if (useSpriteAnims)
        {
            Vector2 d = (Vector2)target.position - (Vector2)transform.position;
            if (d.sqrMagnitude > 0.0001f) insectAnim.SetAim(d);   // angle toward the target

            if (insectAnim.HasAttack)
            {
                if (insectAnim.HasAttackProfile)
                {
                    // Two-speed profile → run it as a CONTINUOUS LOOP (wind-up → fast
                    // bite → wind-up → …) the whole time the insect is up and attacking.
                    // This never rests on a frozen frame between bites, which is what
                    // caused the multi-second freeze on the ready frame.
                    if (!insectAnim.IsLoopingAttackProfile)
                        insectAnim.PlayAttackProfiledLoop();
                }
                else
                {
                    // No profile → slave a single uniform strike to each real combat
                    // cycle (keyed off AttackCycleStartTime), then rest on frame 0.
                    // Rest is by TIME so a boss that holds its attack state can't strand
                    // the sprite on the last frame.
                    float cyc = enemyController != null ? enemyController.AttackCycleStartTime : -999f;
                    bool attacking = enemyController != null && enemyController.IsAttacking;

                    if (attacking && !insectAttackPlaying && cyc > lastInsectSwingStart + 0.0001f)
                    {
                        lastInsectSwingStart = cyc;
                        float dur = InsectSwingDuration();
                        insectAnim.PlayOnce(InsectAnimator.Clip.Attack, dur);
                        insectSwingRestTime = Time.time + dur;
                        insectAttackPlaying = true;
                    }

                    if (insectAttackPlaying && Time.time >= insectSwingRestTime)
                    {
                        insectAnim.ShowFrame(InsectAnimator.Clip.Attack, 0);   // ready pose between swings
                        insectAttackPlaying = false;
                    }
                }
            }
            else if (!insectAnim.IsLoopingAnything)
            {
                // No attack folder: keep the looping stand-in alive so the sprite
                // never gets stuck on the shared main sheet between swings.
                StartAttackLoop();
            }
        }

        float dist = Vector2.Distance(transform.position, target.position);
        float reBurrowDistance = enemyController.AttackRange + Mathf.Max(0f, reBurrowExtraDistance);

        // If the current target died, the controller has already retargeted to the
        // next nearest structure. If that one is far, tunnel to it — but only once
        // the current swing has finished, so we never cut an attack short.
        if (dist > reBurrowDistance && !enemyController.IsAttacking)
            BeginDive();
    }

    //  Target resolution 

    private Transform GetTravelTarget()
    {
        Transform t = enemyController != null ? enemyController.CurrentTarget : null;
        if (IsLiveStructure(t)) return t;
        return GetClosestStructure();
    }

    // Normalized direction from the insect to whatever it's heading for / biting,
    // used to aim & face the sprite. Falls back to "right" (the art's authored
    // forward) when there's no target yet.
    private Vector2 DirToTravelTarget()
    {
        Transform t = GetTravelTarget();
        if (t == null) return Vector2.right;
        Vector2 d = (Vector2)t.position - (Vector2)transform.position;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.right;
    }

    private static bool IsLiveStructure(Transform t)
    {
        if (t == null || t.gameObject == null || !t.gameObject.activeInHierarchy) return false;
        var tower = t.GetComponent<Tower>();
        if (tower != null && tower.IsDestroyed()) return false;
        return true;
    }

    // Returns the closest live structure: any non-destroyed tower or the core.
    private Transform GetClosestStructure()
    {
        float bestDist = Mathf.Infinity;
        Transform best = null;

        GameObject[] towers = GameObject.FindGameObjectsWithTag("Tower");
        foreach (var t in towers)
        {
            if (t == null || !t.activeInHierarchy) continue;

            var tower = t.GetComponent<Tower>();
            if (tower != null && tower.IsDestroyed()) continue;

            float d = Vector2.Distance(transform.position, t.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = t.transform;
            }
        }

        if (coreTarget == null) CacheCore();

        if (coreTarget != null)
        {
            float dc = Vector2.Distance(transform.position, coreTarget.position);
            if (dc < bestDist)
            {
                bestDist = dc;
                best = coreTarget;
            }
        }

        return best != null ? best : coreTarget;
    }

    private void CacheCore()
    {
        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null) coreTarget = core.transform;
    }

    //  Helpers 

    private void SuspendController(bool suspended)
    {
        if (enemyController != null)
            enemyController.ExternalMovementControl = suspended;
    }

    private void SetBodyEnabled(bool on)
    {
        if (bodyColliders == null) return;
        for (int i = 0; i < bodyColliders.Length; i++)
            if (bodyColliders[i] != null) bodyColliders[i].enabled = on;
    }

    private void SetSpriteAlpha(float a)
    {
        if (spriteRenderer == null) return;
        Color c = spriteRenderer.color;
        c.a = Mathf.Clamp01(a);
        spriteRenderer.color = c;
    }

    private void ShowHealthBar(bool show)
    {
        var bar = stats != null ? stats.GetHealthBar() : null;
        if (bar != null && bar.gameObject.activeSelf != show)
            bar.gameObject.SetActive(show);
    }

    // A point near the insect's feet where earth should erupt from (rather than
    // its centre), so bursts and the tunnel trail read as ground-level.
    private Vector3 GroundPoint()
    {
        return transform.position + Vector3.down * (spriteHeight * 0.32f);
    }

    //  Underground Y-sort override 

    // Resolve (once) a sorting order guaranteed to sit BELOW the entire grass
    // field and every Y-sorted obstacle, yet ABOVE the background tiles. Grass and
    // blocking obstacles share one Y-sort band (base +/- spawnRadius*precision),
    // so no single value can be "above grass AND below obstacles"; we take the
    // approved fallback — under the whole terrain field, over the background — and
    // rely on the above-grass burrow VFX (crest, dust, rings) for readability.
    private int UndergroundSortingOrder()
    {
        if (undergroundSortingOrder != int.MinValue) return undergroundSortingOrder;

        int baseOrder = 1000;
        float precision = 10f;
        float radius = 60f;

        var grass = FindAnyObjectByType<GrassCartoonOverlay>();
        if (grass != null)
        {
            baseOrder = grass.sortOrderBase;
            precision = grass.sortPrecision;
            radius = grass.spawnRadius;
        }
        else if (ySort != null)
        {
            baseOrder = ySort.sortOrderBase;
            precision = ySort.sortPrecision;
        }

        // Lowest order any grass blade / obstacle can reach.
        int grassFloor = baseOrder - Mathf.CeilToInt(radius * Mathf.Max(1f, precision));
        // A few steps under that, but never so low it collides with the
        // background tiles (BackgroundTiler uses -100; some map renderers use -1).
        undergroundSortingOrder = Mathf.Clamp(grassFloor - 4, 5, baseOrder - 1);
        return undergroundSortingOrder;
    }

    private void EnsureYSort()
    {
        if (ySort == null) ySort = GetComponent<YSortEntity>();
    }

    // Pin the body sprite (and any child sorted relative to it) below the terrain.
    private void EnterUndergroundSorting()
    {
        EnsureYSort();
        if (ySort != null) ySort.SetSortingOverride(UndergroundSortingOrder());
    }

    // Return to normal Y-sorting (surfaced / airborne).
    private void ExitUndergroundSorting()
    {
        EnsureYSort();
        if (ySort != null) ySort.ClearSortingOverride();
    }
}


