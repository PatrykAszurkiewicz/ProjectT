using UnityEngine;
using System.Collections;
using System.Collections.Generic;   // PERF: for the shared sprite-folder cache

public class EnemyAnimationController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private SmoothSpriteFlip smoothFlip;
    private EnemyStats enemyStats;
    private EnemyData enemyData;
    private Sprite[] sprites;

    /// The frames this controller resolved, whichever source they came from
    /// (direct EnemyData.frames, the multi-folder concatenation, injected sprites,
    /// or the legacy Resources load).
    ///
    /// Exposed so callers stop doing their own Resources.LoadAll to fetch a single
    /// frame. Boss1 and Boss2 both re-loaded the ENTIRE sprite folder synchronously
    /// inside their death handler just to pick idle.startFrame — during the boss-kill
    /// freeze, the worst possible moment for a blocking disk read. Those reads also
    /// broke outright once the sprites moved out of Resources.
    public Sprite[] Sprites => sprites;

    /// Safe indexed access into the resolved frames. Returns null when the index is
    /// out of range or nothing loaded, so callers can fall back without a try/catch.
    public Sprite GetFrame(int index)
        => (sprites != null && index >= 0 && index < sprites.Length) ? sprites[index] : null;

    // When true, `sprites` was supplied at runtime via SetSpritesDirectly() (e.g.
    // a procedurally generated sprite), so Start() must NOT try to load a PNG
    // folder from Resources. Existing PNG-driven enemies never set this, so their
    // path is unchanged.
    private bool useInjectedSprites = false;

    // When false, this controller stops writing transform.rotation (the walk-lean)
    // and stops issuing flip commands, leaving orientation entirely to another
    // component. The Bomber turns this off so it can roll like a boulder without
    // the lean fighting its spin. Defaults true → every existing enemy is unchanged.
    private bool driveOrientation = true;

    private Coroutine currentAnimationCoroutine;
    private bool isAttacking = false;
    private bool isDying = false;

    // Warn only once per enemy that the attack range overruns the loaded sprite
    // sheet, instead of every swing — companions (e.g. InsectController) may drive
    // the attack timeline length from a separate sprite folder on purpose.
    private bool warnedAttackRangePastSprites = false;

    /// Where the parry-stun stagger pose comes from. See parryFreezePoseMode.
    public enum ParryFreezePoseMode
    {
        /// Hold whatever frame was showing when the stun landed.
        InPlace = 0,

        /// Snap to parryFreezePoseFrame of the ATTACK animation.
        AttackFrame = 1,

        /// Snap to parryFreezePoseFrame of the IDLE / walk animation. Reach for
        /// this when the attack animation has no frame that reads as "staggered"
        /// — a neutral walk pose usually does.
        IdleFrame = 2,
    }

    private enum AnimationState { Idle, Attack, LaserAttack, Death }
    private AnimationState currentState = AnimationState.Idle;
    private bool isLaserAttacking = false;

    // Explicit melee attack flag
    // Set by EnemyController when it starts/ends an attack cycle.

    private bool isMeleeAttacking = false;

    // When true, velocity-based attack auto-detection is permanently disabled. 
    private bool disableAutoAttackDetection = false;

    // PERF: cached component + transform references
    // The per-frame Update path used to call GetComponent<Rigidbody2D>() and
    // GetComponent<EnemyController>() every frame for every enemy, and
    // FaceAttackTarget() called GameObject.FindGameObjectWithTag twice per frame
    // for every enemy that was mid-melee. Both are resolved once and reused below.
    private Rigidbody2D cachedRb;
    private EnemyController cachedEnemyController;
    private Transform cachedPlayerTf;
    private Transform cachedCoreTf;

    // PERF: shared sprite-folder cache 
    // Resources.LoadAll<Sprite>(folder) is synchronous — it loads AND decodes every
    // PNG in the folder on the calling frame, and this controller then sorts the
    // result. Done in each enemy's Start(), that put a main-thread stall on the
    // frame every NEW enemy type first appeared (a big part of the "first few
    // seconds" freeze, and of the summon-burst freeze the Boss2 prewarm comment
    // already describes). We now do the load+sort ONCE per folder for the whole run
    // and hand every enemy the same array. The sprites are only ever READ during
    // animation, so sharing one array reference between instances is safe and also
    // saves the per-instance array allocation.
    // Domain-reload-disabled safety: Unity keeps the underlying sprites alive across
    // scene loads, but on play-mode exit (with fast enter-play-mode) the static dict
    // would still point at objects from the previous session. Clear it, mirroring
    // the pattern UIProceduralSprites uses for its cached textures.

    /// <summary>
    /// Loads one Resources sprite folder ONCE (LoadAll + ordinal sort) and caches
    /// the result for the rest of the run. Returns an empty array (never null) on a
    /// miss so callers can treat failure uniformly. The returned array is shared and
    /// must be treated as READ-ONLY.
    ///
    /// Ordinal sort (rather than culture-aware CompareTo) keeps zero-padded frame
    /// names — 00000, 00001, 00002 … — in numeric order regardless of the platform's
    /// current culture. For the equal-length padded names the project uses this is
    /// identical to the old CompareTo ordering, just culture-proof.
    /// </summary>

    /// <summary>
    /// PERF hook for a loading-screen pre-warmer. Call this for every enemy/boss
    /// sprite folder (and the boss laser folder) BEFORE the first wave so the very
    /// first spawn of each type also hits a warm cache instead of stalling. It is a
    /// no-op for folders already cached, so calling it more than once is free.
    /// </summary>

    /// True once this folder is in the cache, so a caller can skip the async warm.
    ///
    /// WHY: Resources.LoadAll is SYNCHRONOUS. It blocks the main thread until every
    /// texture in the folder is decoded and uploaded. A boot profile caught this
    /// costing 11.7 SECONDS for a single enemy:
    ///     [PERF] warm→ Slime LoadAll begin + 11693 ms
    /// and that stall is what tripped StageTransitionOverlay's reveal watchdog
    /// ("force-clearing a stuck black screen") — the overlay was fine, it was simply
    /// starved of frames by the loader.
    ///
    /// Shrinking the textures is the real fix (see EnemySpriteImportOptimizer), but a
    /// loader that cannot stall the main thread is worth having regardless: it turns a
    /// future oversized folder into a slow warm rather than a frozen game.

    /// <summary>
    /// Public hook for support-style enemies (e.g. Scarecrow) to opt out of
    /// the velocity-based auto-attack heuristic. These enemies never play an
    /// "attack" animation — their damage comes from auras or other systems —
    /// so the heuristic would just cause idle/attack flicker when they stand
    /// near the player. Call once at Start() before any animation begins.
    /// </summary>
    public void SetAutoAttackDetectionEnabled(bool enabled)
    {
        disableAutoAttackDetection = !enabled;
    }

    // Sprite orientation settings
    [Header("Sprite Orientation")]
    [SerializeField] private float maxRotationAngle = 20f;
    [SerializeField] private float orientationSmoothSpeed = 10f;

    private Quaternion targetRotation = Quaternion.identity;

    // FRAME EVENT CALLBACK
    // Fired once per frame during attack animation with the current 0-based
    // frame index (relative to attack.startFrame). EnemyController subscribes
    // to this to know exactly when the hit frame / parry window is reached.

    /// Invoked each frame of the attack animation with the 0-based frame index
    /// relative to the attack range. Subscribe in EnemyController to react to
    /// specific frames (hit, parry open, parry close, etc.).
    public System.Action<int> OnAttackFrame;

    /// The 0-based frame index currently being displayed during an attack animation.
    /// -1 when not attacking. Used by debug overlays and ParryIndicator.
    public int CurrentAttackFrame { get; private set; } = -1;


    /// Called by EnemyController.AttackCycle() to start the melee attack animation.
    /// hitFrame: which 0-based frame to fire the onHitFrame callback on (-1 = no callback).
    /// onHitFrame: called synchronously when the animation reaches hitFrame.
    public void PlayMeleeAttackAnimationOLD(int hitFrame = -1, System.Action onHitFrame = null)
    {
        if (isDying || isLaserAttacking || isAnimationFrozen) return;
        isMeleeAttacking = true;

        if (currentState != AnimationState.Attack)
        {
            currentState = AnimationState.Attack;
            if (currentAnimationCoroutine != null)
                StopCoroutine(currentAnimationCoroutine);

            currentAnimationCoroutine = StartCoroutine(PlayMeleeAttackOnce(hitFrame, onHitFrame));
        }
    }


    public void PlayMeleeAttackAnimation(int hitFrame = -1, System.Action onHitFrame = null)
    {
        if (isDying || isLaserAttacking || isAnimationFrozen) return;

        // If we are already in the middle of a melee attack coroutine, stop it to start the new one from frame 0.
        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
        }

        isMeleeAttacking = true;
        currentState = AnimationState.Attack;
        currentAnimationCoroutine = StartCoroutine(PlayMeleeAttackOnce(hitFrame, onHitFrame));
    }

    // Called by EnemyController when the attack cycle ends.
    public void StopMeleeAttackAnimation()
    {
        isMeleeAttacking = false;

    }

    private IEnumerator PlayMeleeAttackOnce(int hitFrame, System.Action onHitFrame)
    {
        bool hitFired = false;

        int frameCount = enemyData.attack.frameCount;
        int startFrame = enemyData.attack.startFrame;
        float spf = Mathf.Max(0.0001f, enemyData.GetAnimSpeed(enemyData.attack));

        if (frameCount <= 0)
        {
            Debug.LogError($"[{name}] EnemyData '{enemyData.name}' has attack.frameCount = " +
                           $"{frameCount}. The attack animation cannot play. Set the attack " +
                           $"range on the EnemyData asset (e.g. startFrame 0 / frameCount 45).");
            onHitFrame?.Invoke();
            yield break;
        }

        if (startFrame + frameCount > sprites.Length && !warnedAttackRangePastSprites)
        {
            warnedAttackRangePastSprites = true;
            Debug.LogWarning($"[{name}] attack range {startFrame}..{startFrame + frameCount - 1} " +
                             $"runs past the {sprites.Length} loaded sprites — the tail of the " +
                             $"animation will not be drawn.");
        }

        // A hitFrame at or past the end of the range can never be reached by the
        // loop, so onHitFrame would silently never fire (this is what makes a
        // ranged enemy play its wind-up and then release nothing). Clamp it to
        // the last real frame and say so, loudly, once per attack.
        int safeHitFrame = hitFrame;
        if (safeHitFrame >= frameCount)
        {
            Debug.LogWarning($"[{name}] hitFrame {hitFrame} is outside the attack range " +
                             $"(frameCount {frameCount}). Clamping to {frameCount - 1}. " +
                             $"Check EnemyData.attack.frameCount vs EnemyData.hitFrame.");
            safeHitFrame = frameCount - 1;
        }

        // Frames are driven off ELAPSED TIME rather than one WaitForSeconds per
        // frame. WaitForSeconds always rounds up to the next render frame, so a
        // long animation (the Mort's is 45 frames) accumulates hundreds of ms of
        // drift and finishes well after the timeline EnemyController.AttackCycle
        // is using — which desyncs the projectile release from its hit frame.
        // Sampling against Time.time keeps sprite and damage on one clock.
        float startTime = Time.time;
        int shown = -1;

        while (shown < frameCount - 1)
        {
            int i = Mathf.Min(frameCount - 1, Mathf.FloorToInt((Time.time - startTime) / spf));

            if (i > shown)
            {
                // Step through every frame we passed, so a hitch can never skip
                // over the hit frame or a frame event.
                for (int f = shown + 1; f <= i; f++)
                {
                    int frameIndex = startFrame + f;
                    if (frameIndex >= 0 && frameIndex < sprites.Length)
                        spriteRenderer.sprite = sprites[frameIndex];

                    CurrentAttackFrame = f;
                    OnAttackFrame?.Invoke(f);

                    if (!hitFired && safeHitFrame >= 0 && f >= safeHitFrame)
                    {
                        hitFired = true;
                        onHitFrame?.Invoke();
                    }
                }
                shown = i;
            }

            yield return null;
        }

        // Let the final frame actually be on screen for its full duration
        // instead of being replaced by idle the instant it is assigned.
        float endOfLastFrame = startTime + frameCount * spf;
        while (Time.time < endOfLastFrame)
            yield return null;

        // Safety net: if hitFrame was negative or something odd happened above,
        // still deliver the hit so a shot is never swallowed.
        if (!hitFired && safeHitFrame >= 0)
        {
            hitFired = true;
            onHitFrame?.Invoke();
        }

        CurrentAttackFrame = -1;

        // Show idle sprite so we don't freeze on last attack frame
        int idleFrame = enemyData.idle.startFrame;
        if (idleFrame < sprites.Length)
            spriteRenderer.sprite = sprites[idleFrame];

        // Hold until EnemyController calls StopMeleeAttackAnimation()
        // Keeping currentState as Attack blocks auto-detection for non-boss enemies
        while (isMeleeAttacking)
            yield return null;

        // Transition back to idle (no need to set currentState here —
        // PlayIdleAnimation handles it, and currentState is already Attack)
        PlayIdleAnimation();
    }


    // LASER ATTACK
    public void PlayLaserAttackAnimation()
    {
        if (currentState == AnimationState.LaserAttack || isDying) return;
        currentState = AnimationState.LaserAttack;
        isLaserAttacking = true;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(PlayLaserAttackCoroutine());
    }

    private IEnumerator PlayLaserAttackCoroutine()
    {
        // PERF: one WaitForSeconds reused across the loop. The per-frame speed is
        // constant here, so allocating a new WaitForSeconds every iteration was pure
        // GC churn (one throwaway object per laser animation frame).
        var wait = new WaitForSeconds(enemyData.GetAnimSpeed(enemyData.laserAttack));

        for (int i = 0; i < enemyData.laserAttack.frameCount; i++)
        {
            int frameIndex = enemyData.laserAttack.startFrame + i;
            if (frameIndex < sprites.Length)
                spriteRenderer.sprite = sprites[frameIndex];
            yield return wait;
        }

        while (isLaserAttacking)
            yield return null;

        PlayIdleAnimation();
    }

    public void StopLaserAttackAnimation()
    {
        isLaserAttacking = false;
    }

    public bool IsPlayingLaserAttack() => isLaserAttacking;
    public bool IsPlayingMeleeAttack() => isMeleeAttacking;

    // True from the moment PlayDeathAnimation() is called. Companion controllers
    // (MortController etc.) read this to suppress anything that would otherwise
    // fire out of a corpse — attack coroutines on EnemyController keep running
    // after death, because disabling a MonoBehaviour does not stop its coroutines.
    public bool IsDying => isDying;

    //  Animation Freeze (used by ParryStunEffect) 
    private bool isAnimationFrozen = false;

    [Header("Parry Stun")]
    [Tooltip("What this enemy does when a parry stun freezes it.\n\n" +
             "In Place (default): hold whatever frame was showing. Original " +
             "behaviour, right for most enemies.\n\n" +
             "Attack Frame / Idle Frame: snap to a chosen frame of that animation " +
             "instead. Use this when freezing in place looks wrong — the Brute is " +
             "parried ON its impact frame, so holding it leaves a slam shockwave " +
             "hanging motionless in mid-air.")]
    [SerializeField] private ParryFreezePoseMode parryFreezePoseMode = ParryFreezePoseMode.InPlace;

    [Tooltip("Which frame of the animation chosen above, 0-based RELATIVE TO THAT " +
             "ANIMATION — not an absolute index into the sprite folder.\n\n" +
             "That distinction matters for single-folder enemies. If idle is " +
             "sprites 0-13 and attack is 14-21, then Attack Frame 0 is sprite 14, " +
             "NOT sprite 00. To hold sprite 00 there, pick Idle Frame 0.\n\n" +
             "Multi-folder enemies read naturally: Attack Frame 0 is the first PNG " +
             "in the Attack folder.")]
    [SerializeField] private int parryFreezePoseFrame = 0;

    // Freezes the animation on the current frame. Stops all animation coroutines. The sprite stays on whatever frame it was showing when frozen.
    public void FreezeAnimation()
    {
        isAnimationFrozen = true;
        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
            currentAnimationCoroutine = null;
        }

        // Only pose enemies that were actually mid-attack. currentState is the
        // reliable test here: isMeleeAttacking depends on exactly when the stun
        // arrives relative to the attack coroutine's own bookkeeping.
        holdParryFreezePose = parryFreezePoseMode != ParryFreezePoseMode.InPlace
                              && currentState == AnimationState.Attack;

        SnapToParryFreezePose();
    }

    // Optional stagger pose, see parryFreezePoseMode.
    //
    // This has to be HELD, not set once. The parry stun is applied synchronously
    // from inside PlayMeleeAttackOnce's own frame loop (OnAttackFrame -> the
    // enemy's slam -> ApplyDamageToTarget -> ShieldSystem -> ParryStunEffect ->
    // here). StopCoroutine does not unwind the C# stack, so that loop carries on
    // after this returns and assigns spriteRenderer.sprite again before it finally
    // dies at its next yield — overwriting a one-shot snap with the impact frame.
    // Re-asserting it from Update sidesteps the ordering entirely.
    private bool holdParryFreezePose = false;

    private void SnapToParryFreezePose()
    {
        if (!holdParryFreezePose) return;
        if (spriteRenderer == null || sprites == null || enemyData == null) return;

        // Frames are addressed within a RANGE, because that is how every other
        // frame number on an enemy works (hitFrame, parry frames). The ranges are
        // filled in from the folders at load time for multi-folder enemies, and
        // read off EnemyData for single-folder ones.
        AnimationFrameRange range = (parryFreezePoseMode == ParryFreezePoseMode.IdleFrame)
            ? enemyData.idle
            : enemyData.attack;

        if (range.frameCount <= 0) return;

        int index = range.startFrame + Mathf.Clamp(parryFreezePoseFrame, 0, range.frameCount - 1);
        if (index >= 0 && index < sprites.Length)
            spriteRenderer.sprite = sprites[index];
    }

    // Unfreezes the animation, allowing it to resume. Returns to idle if not in an active attack state.

    public void UnfreezeAnimation()
    {
        if (!isAnimationFrozen) return;
        isAnimationFrozen = false;
        holdParryFreezePose = false;

        // Don't try to start coroutines on inactive/destroyed objects
        if (this == null || !gameObject.activeInHierarchy) return;

        // FreezeAnimation stopped whatever coroutine was running, and a stopped
        // PlayMeleeAttackOnce never reaches its trailing PlayIdleAnimation(). So a
        // parried enemy used to stand on the frozen frame until its NEXT attack
        // cycle happened to restart the animation — walking around on a static
        // sprite in between. isMeleeAttacking is cleared because that coroutine is
        // gone and will not finish; EnemyController's StopMeleeAttackAnimation()
        // will simply clear it again, harmlessly.
        if (currentAnimationCoroutine == null && !isLaserAttacking)
        {
            isMeleeAttacking = false;
            currentState = AnimationState.Attack; // force reset so PlayIdleAnimation works
            PlayIdleAnimation();
            return;
        }

        // If no attack is active, return to idle
        if (!isMeleeAttacking && !isLaserAttacking)
        {
            currentState = AnimationState.Attack; // force reset so PlayIdleAnimation works
            PlayIdleAnimation();
        }
    }


    // LIFECYCLE
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        smoothFlip = GetComponent<SmoothSpriteFlip>();
        if (smoothFlip == null)
            smoothFlip = gameObject.AddComponent<SmoothSpriteFlip>();
        enemyStats = GetComponent<EnemyStats>();

        // PERF: resolve the components the per-frame path needs ONCE. These never
        // change on an enemy at runtime, so re-fetching them every Update was waste.
        cachedRb = GetComponent<Rigidbody2D>();
        cachedEnemyController = GetComponent<EnemyController>();

        if (enemyStats == null || enemyStats.enemyData == null)
        {
            Debug.LogError($"No EnemyStats or EnemyData on {gameObject.name}");
            enabled = false;
            return;
        }

        enemyData = enemyStats.enemyData;

        // Bosses must use explicit PlayMeleeAttackAnimation() 
        if (GetComponent<Boss1>() != null)
            disableAutoAttackDetection = true;

        // Skip the Resources/PNG pipeline entirely when a sprite has been injected
        // in code (e.g. the procedural Bomber). Otherwise behave exactly as before.
        if (!useInjectedSprites)
        {
            if (enemyData.useAnimationFolders)
            {
                // Multi-folder mode: each animation lives in its own subfolder with
                // its own 0-based numbering. The loader concatenates them and fills
                // in the idle/attack/death ranges automatically.
                LoadSpritesFromFolders();
            }
            else if (enemyData.frames == null || enemyData.frames.Length == 0)
            {
                // FIX: this used to test only spriteFolderPath. After migration a fully
                // valid asset has frames[] populated and an EMPTY path — that would have
                // disabled the animator on every migrated enemy.
                // No direct frames. Injected sprites (SetSpritesDirectly) may still
                // arrive from a sibling component, so let LoadSprites decide.
                LoadSprites();
            }
            else
            {
                LoadSprites();
            }
        }

        if (sprites != null && sprites.Length > 0)
        {
            spriteRenderer.sprite = sprites[0];
            StartCoroutine(DelayedStartAnimation());
        }
        else
        {
            enabled = false;
        }
    }

    /// <summary>
    /// Inject a ready-made sprite array (e.g. one generated procedurally in code)
    /// so this controller animates and orients it WITHOUT loading a PNG folder
    /// from Resources. Pass a single sprite for a one-frame enemy like the Bomber:
    /// every animation range then resolves to that same frame, and the usual
    /// walk-lean / flip / state-machine behaviour is fully preserved.
    ///
    /// Call this BEFORE this component's Start() runs — e.g. from a sibling
    /// component's Awake() with a later execution order (see BomberController).
    /// </summary>
    public void SetSpritesDirectly(Sprite[] injected)
    {
        if (injected == null || injected.Length == 0) return;

        sprites = injected;
        useInjectedSprites = true;

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) spriteRenderer.sprite = sprites[0];
    }

    /// <summary>
    /// Enable/disable this controller's velocity-based orientation (walk-lean +
    /// flip). Turn OFF when another component drives rotation directly — e.g. the
    /// Bomber, which rolls the sprite about its centre while it travels. When off,
    /// this controller never writes transform.rotation, so the two never fight.
    /// </summary>
    public void SetOrientationDrivingEnabled(bool enabled)
    {
        driveOrientation = enabled;
        if (!enabled)
            transform.rotation = Quaternion.identity; // hand it over from a clean slate
    }

    private void LoadSprites()
    {
        // PREFERRED PATH: direct Sprite[] on the asset. No Resources, no LoadAll, no
        // decode at spawn time — Unity already loaded these with the scene because the
        // prefab → EnemyStats → EnemyData → Sprite chain keeps them in the dependency
        // graph. This is also what lets the atlas packer strip the source textures.
        if (enemyData.frames != null && enemyData.frames.Length > 0)
        {
            sprites = enemyData.frames;
            return;
        }

        // No frames on the asset. Nothing else to try: the Resources fallback that
        // used to live here is gone, along with the sprite folders it read from.
        //
        // This is NOT necessarily an error — several enemies legitimately supply their
        // art another way and never populate EnemyData.frames:
        //   Berserk  — BerserkVisual computes frames on a worker thread and calls
        //              SetSpritesDirectly()
        //   Bomber   — single procedural sprite, also via SetSpritesDirectly()
        //   Splitter / Vortex / Goblins / Orcs — driven by their own components
        // Those all inject BEFORE this runs, so `sprites` is already set and LoadSprites
        // is never reached. Landing here means genuinely no art from any source.
        if (!useInjectedSprites)
            Debug.LogWarning($"[{gameObject.name}] EnemyData '{enemyData.name}' has no frames and no " +
                             "sprites were injected. Assign Frames on the EnemyData asset, or call " +
                             "SetSpritesDirectly() from a sibling component before Start().");
    }

    /// <summary>
    /// Multi-folder loader. Loads the idle, attack and (optional) death folders
    /// declared on EnemyData, each sorted independently by frame name, and lays
    /// them end to end into the single flat `sprites` array the rest of this
    /// controller already expects:
    ///
    ///     [ idle frames | attack frames | death frames ]
    ///
    /// It then rewrites EnemyData's idle/attack/death ranges to match that
    /// layout, so every existing consumer (attack timing, hit frame, parry
    /// window, death animation) keeps working unchanged — the designer just
    /// points at folders instead of hand-counting indices.
    ///
    /// Sorting PER FOLDER (rather than globally) is the crucial bit: it lets two
    /// clips both number their PNGs from 00000 without interleaving, which a
    /// single global Resources.LoadAll + sort would do.
    ///
    /// This mutates enemyData, which is safe because EnemyStats.Awake has already
    /// replaced it with a per-enemy clone; the shared asset is never touched.
    /// </summary>
    private void LoadSpritesFromFolders()
    {
        var all = new System.Collections.Generic.List<Sprite>();
        int idleCount, attackCount, deathCount;

        // PREFERRED PATH: direct arrays. Concatenation order must match the legacy
        // branch exactly (idle → attack → death), because the frame RANGES rewritten
        // below index into this combined array.
        if (enemyData.idleFrames != null && enemyData.idleFrames.Length > 0 ||
            enemyData.attackFrames != null && enemyData.attackFrames.Length > 0)
        {
            idleCount = Append(all, enemyData.idleFrames);
            attackCount = Append(all, enemyData.attackFrames);
            deathCount = Append(all, enemyData.deathFrames);
        }
        else
        {
            // The Resources folder branch that used to be here is gone. A multi-folder
            // enemy with no direct arrays has no art at all.
            idleCount = attackCount = deathCount = 0;
            Debug.LogWarning($"[{gameObject.name}] EnemyData '{enemyData.name}' uses animation folders " +
                             "but Idle/Attack/Death Frames are all empty.");
        }

        if (all.Count == 0)
        {
            Debug.LogError($"[{name}] useAnimationFolders is ON but no sprites were " +
                           $"loaded. Check idle/attack/death folder paths on EnemyData " +
                           $"'{enemyData.name}' (paths are relative to a Resources folder).");
            return;
        }

        sprites = all.ToArray();

        // Rewrite the ranges to match the concatenation. Mutate the struct's
        // fields in place so any per-clip speedOverride the designer set on the
        // asset is preserved.
        AnimationFrameRange idle = enemyData.idle;
        idle.startFrame = 0;
        idle.frameCount = idleCount;
        enemyData.idle = idle;

        AnimationFrameRange atk = enemyData.attack;
        atk.startFrame = idleCount;
        atk.frameCount = attackCount;
        enemyData.attack = atk;

        // death.frameCount == 0 makes EnemyStats.Die() skip the death animation
        // and remove the enemy immediately (or play its Death VFX). That's exactly
        // what we want when no death folder is supplied.
        AnimationFrameRange death = enemyData.death;
        death.startFrame = idleCount + attackCount;
        death.frameCount = deathCount;
        enemyData.death = death;
    }

    /// <summary>
    /// Loads one animation folder from Resources, sorts its frames by name, and
    /// appends them to `dest`. Returns how many frames were added (0 if the path
    /// is empty or nothing was found).
    /// </summary>
    /// Append a direct Sprite[] (nulls skipped — an array with a hole would shift every
    /// later frame index and silently desync hitFrame / the parry window).
    private int Append(System.Collections.Generic.List<Sprite> dest, Sprite[] src)
    {
        if (src == null || src.Length == 0) return 0;

        int added = 0;
        for (int i = 0; i < src.Length; i++)
        {
            if (src[i] == null)
            {
                Debug.LogWarning($"[{name}] EnemyData '{enemyData.name}' has a NULL entry at " +
                                 $"index {i}. Skipping it — but re-run the migration, because a " +
                                 "hole shifts every later frame index and desyncs hitFrame.");
                continue;
            }
            dest.Add(src[i]);
            added++;
        }
        return added;
    }




    private IEnumerator DelayedStartAnimation()
    {
        yield return null;

        // currentState is Idle from its field initializer, and PlayIdleAnimation
        // early-returns when it is already Idle — so this call did nothing and the
        // idle/walk loop never started at spawn. Enemies sat on sprites[0] until
        // their FIRST attack ended, because PlayMeleeAttackOnce finishes by calling
        // PlayIdleAnimation() while currentState is Attack, which is the only path
        // that ever got past the guard.
        //
        // Invisible while enemies were single-frame or their walk cycles were
        // subtle; obvious the moment a 20-frame walk was added. Forcing the state
        // first is the same idiom UnfreezeAnimation already uses two functions up.
        currentState = AnimationState.Attack;
        PlayIdleAnimation();
    }



    void Update()
    {
        if (sprites == null || isDying) return;

        // When animation is frozen (parry stun), skip all updates
        if (isAnimationFrozen)
        {
            // Re-assert the stagger pose; see SnapToParryFreezePose for why once
            // is not enough. No-op unless parryFreezePoseMode is set.
            SnapToParryFreezePose();
            return;
        }

        // Update sprite orientation (skip during laser attack). When another
        // component owns rotation (driveOrientation == false, e.g. the rolling
        // Bomber), don't touch transform.rotation or the flip at all.
        if (driveOrientation)
        {
            if (!isLaserAttacking)
            {
                if (isMeleeAttacking)
                    FaceAttackTarget();  // Face the target during melee attacks
                else
                    UpdateSpriteOrientation();
            }
            else
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.identity,
                    Time.deltaTime * orientationSmoothSpeed);
        }

        // Don't auto-switch animation if laser or explicit melee is active
        if (isLaserAttacking || isMeleeAttacking) return;

        // Velocity-based fallback for non-boss enemies that don't use
        // explicit melee attack calls. Disabled for bosses — they use PlayMeleeAttackAnimation().
        if (disableAutoAttackDetection) return;

        bool shouldBeAttacking = IsEnemyAttacking();
        if (shouldBeAttacking != isAttacking)
        {
            isAttacking = shouldBeAttacking;
            if (isAttacking) PlayAttackAnimation();
            else PlayIdleAnimation();
        }
    }

    // PERF: cached Player / Core lookups.
    // FaceAttackTarget used to call GameObject.FindGameObjectWithTag("Player") and
    // ("Core") every frame for every enemy that was mid-melee — an O(n) tag scan,
    // twice per frame, per attacker. These resolve the tagged object once and
    // refresh only if the cached reference has been destroyed (e.g. player death /
    // respawn), so the common case does zero scanning.
    private Transform ResolvePlayerTransform()
    {
        if (cachedPlayerTf == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            cachedPlayerTf = (go != null) ? go.transform : null;
        }
        return cachedPlayerTf;
    }

    private Transform ResolveCoreTransform()
    {
        if (cachedCoreTf == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Core");
            cachedCoreTf = (go != null) ? go.transform : null;
        }
        return cachedCoreTf;
    }

    // During melee attacks, face the nearest valid target (player, tower, or core). This ensures the sprite doesn't face backwards while attacking.
    private void FaceAttackTarget()
    {
        if (spriteRenderer == null) return;

        // Find the most likely attack target
        Transform target = null;

        Transform player = ResolvePlayerTransform();
        if (player != null)
        {
            float dist = Vector2.Distance(transform.position, player.position);
            if (dist < 5f) // reasonable melee range check
                target = player;
        }

        if (target == null)
        {
            Transform core = ResolveCoreTransform();
            if (core != null)
                target = core;
        }

        if (target != null)
        {
            float dx = target.position.x - transform.position.x;
            if (dx < -0.1f)
                ApplyFacingLeft(true);
            else if (dx > 0.1f)
                ApplyFacingLeft(false);
        }

        // Reset rotation during attack
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.identity,
            Time.deltaTime * orientationSmoothSpeed);
    }

    // Routes every facing decision through the per-enemy "art faces left" flag.
    // For art drawn facing RIGHT (every existing enemy, spriteFacesLeft == false)
    // this is a straight pass-through — behaviour is unchanged. For art drawn
    // facing LEFT (e.g. the Pitcher) it inverts the flip so "face right" actually
    // shows right-facing pixels. Crucially, smoothFlip.IsFacingLeft then still
    // reports the ACTUAL mirrored state, so the walk-lean sign flip in
    // UpdateSpriteOrientation stays correct without any extra bookkeeping.
    private void ApplyFacingLeft(bool faceLeft)
    {
        if (smoothFlip == null) return;
        bool artFacesLeft = (enemyData != null) && enemyData.spriteFacesLeft;
        smoothFlip.SetFacingLeft(faceLeft ^ artFacesLeft);
    }

    private void UpdateSpriteOrientation()
    {
        // PERF: use the cached Rigidbody2D rather than GetComponent every frame.
        Rigidbody2D rb = cachedRb;
        if (rb == null) return;

        Vector2 velocity = rb.linearVelocity;

        if (velocity.magnitude < 0.1f)
        {
            targetRotation = Quaternion.identity;
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,
                Time.deltaTime * orientationSmoothSpeed);
            return;
        }

        // The flip decision needs to reject collision-jitter without
        // breaking when an enemy is genuinely slowed (e.g. ice debuff).
        // We require BOTH:
        //   1. |velocity.x| above a low absolute floor (rejects micro-jitter)
        //   2. |velocity.x| is at least 30% of total motion (rejects the
        //      "wedged enemy bouncing sideways while really trying to push
        //      into the wall" case where intended motion is mostly Y).
        // This is independent of MoveSpeed, so slowed enemies still flip.
        const float FLIP_X_FLOOR = 0.15f;
        const float FLIP_X_FRACTION = 0.3f;
        if (Mathf.Abs(velocity.x) > FLIP_X_FLOOR
            && Mathf.Abs(velocity.x) > velocity.magnitude * FLIP_X_FRACTION)
        {
            if (velocity.x < 0f) ApplyFacingLeft(true);
            else ApplyFacingLeft(false);
        }

        float angle = 0f;
        if (Mathf.Abs(velocity.x) > 0.1f || Mathf.Abs(velocity.y) > 0.1f)
        {
            angle = Mathf.Atan2(velocity.y, Mathf.Abs(velocity.x)) * Mathf.Rad2Deg;
            angle = Mathf.Clamp(angle, -maxRotationAngle, maxRotationAngle);
            if (smoothFlip.IsFacingLeft)
                angle = -angle;
        }

        targetRotation = Quaternion.Euler(0, 0, angle);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,
            Time.deltaTime * orientationSmoothSpeed);
    }

    // Decides whether the velocity-based auto-attack animation should play for non-boss enemies. 
    private bool IsEnemyAttacking()
    {
        // Authoritative path: ask the controller if a real attack cycle is running.
        // PERF: cached controller reference (resolved once in Start).
        var enemyController = cachedEnemyController;
        if (enemyController != null)
            return enemyController.IsAttacking;

        // Fallback (rare): no controller — keep velocity heuristic but require
        // proximity to a real target. Prevents collision-stops from reading as attacks.
        Rigidbody2D rb = cachedRb;
        if (rb == null || rb.linearVelocity.magnitude >= 0.1f)
            return false;

        var nearest = Utilities.GetClosestAttackableTarget(transform.position);
        if (nearest == null) return false;
        const float MELEE_PROXIMITY = 2.0f; // generous enough for any vanilla enemy
        return Vector2.Distance(transform.position, nearest.transform.position) <= MELEE_PROXIMITY;
    }

    private void PlayIdleAnimation()
    {
        if (currentState == AnimationState.Idle || isDying || isAnimationFrozen) return;
        currentState = AnimationState.Idle;
        CurrentAttackFrame = -1;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer, sprites, true,
            enemyData.idle.frameCount,
            enemyData.idle.startFrame,
            enemyData.GetAnimSpeed(enemyData.idle)));
    }

    private void PlayAttackAnimation()
    {
        if (currentState == AnimationState.Attack || isDying || isAnimationFrozen) return;
        currentState = AnimationState.Attack;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer, sprites, true,
            enemyData.attack.frameCount,
            enemyData.attack.startFrame,
            enemyData.GetAnimSpeed(enemyData.attack)));
    }

    // ── Boss5 (Bellkeeper) ──────────────────────────────────────────────
    // Public entry points so a boss can HOLD either loop indefinitely, instead of
    // relying on the velocity heuristic (Update) or a one-shot melee swing.
    //
    // Both force `currentState` to the OTHER state first, because PlayIdleAnimation
    // and PlayAttackAnimation each early-out when they are already in the state being
    // requested. That is the same idiom DelayedStartAnimation and UnfreezeAnimation
    // already use, and the bug DelayedStartAnimation's comment describes.
    //
    // Purely additive: no existing caller reaches these two methods, so every other
    // enemy animates exactly as it did before.

    /// <summary>Loop the IDLE / walk range until told otherwise.</summary>
    public void PlayLoopingIdleAnimation()
    {
        if (isDying || isAnimationFrozen) return;
        isMeleeAttacking = false;
        isLaserAttacking = false;
        currentState = AnimationState.Attack;   // force the guard in PlayIdleAnimation to pass
        PlayIdleAnimation();
    }

    /// <summary>Loop the ATTACK range until told otherwise.</summary>
    public void PlayLoopingAttackAnimation()
    {
        if (isDying || isAnimationFrozen) return;
        isMeleeAttacking = false;
        isLaserAttacking = false;
        currentState = AnimationState.Idle;     // force the guard in PlayAttackAnimation to pass
        PlayAttackAnimation();
    }

    /// <summary>True while a parry stun is holding this enemy's animation.
    /// Read-only view of the existing freeze flag.</summary>
    public bool IsAnimationFrozen => isAnimationFrozen;

    /// <summary>True when the idle / walk loop is the clip currently playing.
    /// Lets a boss notice that something (a parry stun recovering, or an
    /// EnemyController attack cycle finishing) dropped it back to idle, and
    /// re-arm its own loop.</summary>
    public bool IsPlayingIdleLoop => currentState == AnimationState.Idle;

    public void PlayDeathAnimation()
    {
        if (isDying) return;
        isDying = true;
        currentState = AnimationState.Death;
        CurrentAttackFrame = -1;
        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);
        transform.rotation = Quaternion.identity;
        StartCoroutine(PlayDeathAnimationCoroutine());
    }

    private IEnumerator PlayDeathAnimationCoroutine()
    {
        // PERF: one reused WaitForSeconds instead of allocating per frame.
        var wait = new WaitForSeconds(enemyData.GetAnimSpeed(enemyData.death));

        for (int i = 0; i < enemyData.death.frameCount; i++)
        {
            int frameIndex = enemyData.death.startFrame + i;
            if (frameIndex < sprites.Length)
                spriteRenderer.sprite = sprites[frameIndex];
            yield return wait;
        }
    }

    void OnDisable()
    {
        CurrentAttackFrame = -1;
    }
}


