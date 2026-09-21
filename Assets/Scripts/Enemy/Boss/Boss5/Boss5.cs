using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

//  BOSS 5 — THE BELLKEEPER
//  A massive bell that alternates between two states:
//    STATE A (Passive)  — walks the path like any other enemy, attackable by the
//                         player AND by towers. Takes 25% reduced damage from
//                         towers (pure balance lever, see towerDamageMultiplier).
//
//    STATE B (Active)   — rings, tints to its "active" colour, stops dead, and
//                         becomes UNTARGETABLE by towers. One of three challenge
//                         mechanics runs. Failing it fires the Kaboom: a massive
//                         global / AoE blast.
//  The mechanics need to tell PLAYER damage apart from TOWER damage, and this
//  project has no damage-source parameter on CharacterStats.TakeDamage. Rather
//  than change that signature (which every enemy and every damage source in the
//  game depends on), Boss5 classifies incoming damage itself:
//    * OnTriggerEnter2D  — WeaponProjectile => Player, Projectile => Tower.
//                          Exactly the split Boss2 already relies on.
//    * TakeDamageFrom()  — explicit, for any caller that knows the source.
//    * TakeDamage(float) — the legacy signature. Classified by
//                          unattributedDamagePolicy (Auto by default: a player
//                          standing inside melee reach means "player").


public enum BossDamageSource
{
    /// Source not stated by the caller. Resolved via unattributedDamagePolicy.
    Unknown = 0,
    /// A player weapon (melee swing, WeaponProjectile, player-owned AoE).
    Player = 1,
    /// A tower / turret / trap the player built.
    Tower = 2,
}

/// How Boss5 classifies a plain TakeDamage(float) call with no stated source.
public enum UnattributedDamagePolicy
{
    /// Treat as Player if any alive player is within meleeAttributionRadius,
    /// otherwise treat as Tower. Correct for the overwhelming majority of cases
    /// and needs no edits anywhere else. This is the default.
    Auto = 0,
    /// Always treat unattributed damage as coming from a player.
    AlwaysPlayer = 1,
    /// Always treat unattributed damage as coming from a tower.
    AlwaysTower = 2,
    /// Apply the damage in full and attribute it to NEITHER side: no tower penalty,
    /// and it never feeds a challenge mechanic. This is the default now that Tower,
    /// Projectile, WeaponProjectile and Weapon all tag their hits explicitly — the
    /// only damage still arriving untagged is a DoT, hazard or augment tick, and
    /// guessing at those is actively harmful: a poison tick landing while the player
    /// happens to stand nearby would be read as a player attack, fill the Mechanic 2
    /// meter and detonate a Kaboom the player did nothing to cause.
    Neutral = 3,
}

public enum Boss5Mechanic
{
    MotionPenalty = 0,   // don't move
    AttackPenalty = 1,   // don't attack the boss
    SafeZone = 2,        // get to the circle in time
}

[DisallowMultipleComponent]
public class Boss5 : BaseBossStats
{
    //  CONFIG — BODY
    [Header("Boss5 — Fallback Stats (used only when no EnemyData is assigned)")]
    [SerializeField] private float bossMaxHealth = 1200f;
    [SerializeField] private float bossMaxArmor = 900f;

    [Header("Boss5 — Collider / Physics")]
    [Tooltip("Size the collider from the SPRITE at spawn, so resizing the bell art " +
             "(or its transform scale) keeps the hit box in step automatically. This " +
             "is the reason the player could walk into the bell after you enlarged " +
             "it: the collider was a fixed 2.005 authored against the old art.\n\n" +
             "Overrides Apply Collider Override when both are on.")]
    [SerializeField] private bool autoFitColliderToSprite = true;

    [Tooltip("Collider radius as a fraction of the sprite's half-WIDTH. 1 = exactly " +
             "the sprite's bounding circle. A bell is wider at the skirt than the " +
             "crown, so a little under 1 keeps the body solid without the empty " +
             "corners of the image blocking the player.")]
    [Range(0.3f, 1.5f)][SerializeField] private float colliderFitFraction = 0.92f;

    [Tooltip("OFF = keep whatever CircleCollider2D the prefab already has, untouched. " +
             "Use this if you tuned the radius/offset by eye in the prefab — Boss2 " +
             "always overwrote them at runtime, which silently discarded any X offset.")]
    [SerializeField] private bool applyColliderOverride = false;
    [SerializeField] private float bossColliderRadius = 2f;
    [SerializeField] private float bossColliderOffsetX = 0f;
    [SerializeField] private float bossColliderOffsetY = 0f;
    [SerializeField] private float bossRigidbodyMass = 140f;
    [SerializeField] private float bossLinearDrag = 5f;

    [Header("Boss5 — Health Bar")]
    [Tooltip("Horizontal offset in world units. Flips with the boss sprite's facing.")]
    [SerializeField] private float healthBarXOffset = 0f;
    [Tooltip("Pulls the bar DOWN from its natural spot (sprite top + extra padding). " +
             "The script always clamps the result to sit above the sprite top.")]
    [SerializeField] private float healthBarYReduction = 0f;
    [Tooltip("Extra spacing between the sprite top and the bottom of the health bar.")]
    [SerializeField] private float healthBarExtraYPadding = 0.5f;
    private float healthBarYOffset;

    [Header("Boss5 — Death")]
    [SerializeField] private float disintegrationDuration = 1.8f;

    //  CONFIG — STATE MACHINE
    [Header("State A (Passive) — walking, attackable by everything")]
    [Tooltip("Seconds spent in State A at FULL health. The boss walks and fights normally.")]
    [Min(0.5f)] public float passiveDurationAtFullHp = 14f;

    [Tooltip("Seconds spent in State A at NEAR DEATH. Enrage interpolates from the " +
             "full-health value down to this one, so challenges come much faster late.")]
    [Min(0.5f)] public float passiveDurationAtLowHp = 4f;

    [Tooltip("Damage multiplier applied to TOWER damage while the boss is in State A. " +
             "0.75 = towers deal 25% less (the spec's balance buff). Player damage is " +
             "never reduced.")]
    [Range(0f, 1f)] public float towerDamageMultiplier = 0.75f;

    [Header("State B (Active) — stationary challenge")]
    [Tooltip("Fallback tint for State B, used when Tint Per Mechanic is off.")]
    public Color activeStateColor = new Color(0.72f, 0.25f, 1f, 1f);

    [Tooltip("Give each mechanic its OWN body shade instead of one purple for all " +
             "three. The bell's colour then tells you WHICH rule is live before you " +
             "read any of the detail, which is the fastest read the fight has.")]
    public bool tintPerMechanic = true;

    [Tooltip("Body shade while 'don't move' is running. These multiply the grey bell " +
             "sprite, so keep them light and desaturated — a fully saturated colour " +
             "turns the boss into a silhouette.")]
    public Color motionStateColor = new Color(1f, 0.74f, 0.32f, 1f);   // amber

    [Tooltip("Body shade while 'don't attack' is running.")]
    public Color attackStateColor = new Color(1f, 0.42f, 0.36f, 1f);   // red

    [Tooltip("Body shade while the safe-zone dash is running.")]
    public Color safeZoneStateColor = new Color(0.55f, 1f, 0.68f, 1f); // green

    [Tooltip("Seconds for the ring -> colour shift -> full stop transition at FULL health.")]
    [Min(0.05f)] public float transitionDurationAtFullHp = 1.2f;

    [Tooltip("Seconds for that same transition at NEAR DEATH. Shorter = less reaction time.")]
    [Min(0.05f)] public float transitionDurationAtLowHp = 0.35f;

    [Tooltip("Which mechanics may be drawn. Untick one to remove it from the rotation. " +
             "If all three are unticked the boss simply never enters State B.")]
    public bool enableMotionPenalty = true;
    public bool enableAttackPenalty = true;
    public bool enableSafeZone = true;

    [Tooltip("Never draw the same mechanic twice in a row (as long as another is enabled).")]
    public bool avoidRepeatingMechanic = true;

    [Tooltip("What the bell DOES with its body during State B.\n\n" +
             "• Toll Then Stand — plays the attack clip once (the bell swings and " +
             "tolls), then holds the walk loop while standing still. RECOMMENDED.\n" +
             "• Loop Bell Swing — repeats the attack clip for the whole challenge. " +
             "Because your attack clip is 36 frames / 3.6s of the bell closing over " +
             "itself, looping it reads as the boss 'hiding in the bell' the entire " +
             "time, which hides the state change rather than announcing it.\n" +
             "• Keep Walking — walk loop throughout; the purple tint, toll rings and " +
             "countdown carry the state change on their own.")]
    public Boss5ActivePose activePose = Boss5ActivePose.TollThenStand;

    [Header("Enrage")]
    [Tooltip("Pool fraction LOST at which enrage reaches its maximum. 0.85 = the boss is " +
             "fully enraged once 85% of its combined armour+health pool is gone; the last " +
             "15% is fought at maximum aggression rather than only at the very last hit.")]
    [Range(0.1f, 1f)] public float enrageCompleteAtLostFraction = 0.85f;

    //  CONFIG — PER-MECHANIC TUNING
    //  Each mechanic's numbers live in its own serializable block so the inspector
    //  groups them, and so a challenge class reads exactly one object. Defined in
    //  Boss5Challenges.cs next to the class that consumes them.
    [Header("Mechanic 1 — Motion Penalty (don't move)")]
    public Boss5MotionPenaltySettings motionPenalty = new Boss5MotionPenaltySettings();

    [Header("Mechanic 2 — Attack Penalty (don't attack)")]
    public Boss5AttackPenaltySettings attackPenalty = new Boss5AttackPenaltySettings();

    [Header("Mechanic 3 — Safe Zone (evade)")]
    public Boss5SafeZoneSettings safeZone = new Boss5SafeZoneSettings();

    /// Apply the icon's LOOK to a prefab saved before those fields existed.
    ///
    /// Cosmetics only, on purpose. This method used to rewrite the Mechanic 1
    /// timings as well, which quietly made "don't move" punish far sooner than the
    /// inspector said it would. Difficulty is not something a menu item should
    /// change behind your back \u2014 the timing fields on the component are the only
    /// place that lives now.
    [ContextMenu("Apply Recommended Icon Look (Mechanic 2)")]
    private void ApplyRecommendedIconLook()
    {
        attackPenalty ??= new Boss5AttackPenaltySettings();

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Apply Recommended Icon Look");
#endif

        attackPenalty.iconOffset = new Vector2(0f, 2.6f);
        attackPenalty.iconSize = 0.7f;
        attackPenalty.plateColor = new Color(0.87f, 0.79f, 0.98f, 0.92f);
        attackPenalty.fillColor = new Color(0.70f, 0.24f, 0.92f, 0.9f);
        attackPenalty.slashColor = new Color(0.90f, 0.12f, 0.10f, 0.92f);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Debug.Log("[Boss5] Applied Mechanic 2 icon look (no balance changed).", this);
    }

    //  CONFIG — KABOOM (failure punish)
    [Header("Kaboom — failure explosion")]
    [Tooltip("Damage dealt to every player when a mechanic is failed. Scaled by the " +
             "usual boss special-attack multiplier (difficulty, and per-stage if the run " +
             "opts bosses into stage scaling).")]
    public float kaboomPlayerDamage = 70f;

    [Tooltip("Radius in world units for the tower/core half of the blast. Set to 0 for a " +
             "truly GLOBAL blast that hits every tower and the core wherever they are.")]
    [Min(0f)] public float kaboomBuildingRadius = 0f;

    [Tooltip("Damage dealt to towers and the core. Set to 0 to punish players only.")]
    public float kaboomBuildingDamage = 45f;

    [Tooltip("Players are hit wherever they are (the spec calls this a GLOBAL attack). " +
             "Untick to make the player half radius-limited too, using Kaboom Building Radius.")]
    public bool kaboomHitsPlayersGlobally = true;

    [Tooltip("Visual radius of the shockwave rings. Cosmetic only — damage uses the " +
             "fields above.")]
    [Min(1f)] public float kaboomVisualRadius = 14f;

    [Tooltip("End State B immediately when a mechanic is failed. Off = the challenge " +
             "keeps running (and can Kaboom again) until its timer expires.")]
    public bool endStateBOnFailure = true;

    // =========================================================================
    //  CONFIG — DAMAGE CLASSIFICATION
    // =========================================================================
    [Header("Damage source classification")]
    [Tooltip("How a plain TakeDamage(float) call with no stated source is classified. " +
             "Auto is correct for melee weapons and tower beams alike and needs no " +
             "changes anywhere else in the project.")]
    public UnattributedDamagePolicy unattributedDamagePolicy = UnattributedDamagePolicy.Neutral;

    [Tooltip("Auto policy only: a live player within this many world units of the boss " +
             "means an unattributed hit is treated as a PLAYER hit. Set it a little above " +
             "your longest melee reach.")]
    [Min(0.5f)] public float meleeAttributionRadius = 4f;

    // =========================================================================
    //  CONFIG — AUDIO
    // =========================================================================
    [Header("Audio")]
    [Tooltip("The bell. Played on entering AND on leaving State B. Leave empty to fall " +
             "back to FMODEvents.instance.bossBellRing if that field exists in your bank, " +
             "and to silence if it does not.")]
    [SerializeField] private EventReference bellRingEvent;

    [Tooltip("One-shot for the Kaboom. Leave empty to reuse the Boss2 explosion event.")]
    [SerializeField] private EventReference kaboomEvent;

    //  CONFIG — VISUALS
    [Header("Melee Parry")]
    [Tooltip("Write the hit / parry frame timings below into this boss's EnemyData at " +
             "spawn. EnemyStats clones the asset per enemy, so this only affects this " +
             "Bellkeeper - the shared Boss5Data asset is never modified.\n\n" +
             "Only the MELEE swing is parryable. The Kaboom and the three challenge " +
             "mechanics are not attack cycles at all, so they can never raise the mark.")]
    public bool configureMeleeParry = true;

    [Tooltip("Frame of the ATTACK clip where the bell actually connects (0-based). " +
             "Watch the swing and pick the frame the skirt meets the ground.")]
    [Min(0)] public int meleeHitFrame = 24;

    [Tooltip("First parryable frame. Must sit far enough before the hit that a player " +
             "can see the wind-up and react - roughly 0.35-0.5s of lead is comfortable.")]
    [Min(0)] public int meleeParryFrameStart = 17;

    [Tooltip("Last parryable frame, inclusive. Clamped to at least the hit frame, " +
             "because EnemyController adjudicates the parry ON the hit frame - a window " +
             "that closed earlier could never succeed.")]
    [Min(0)] public int meleeParryFrameEnd = 24;

    [Tooltip("Seconds per frame for the ATTACK clip only (0 = leave the asset alone). " +
             "The 36-frame clip at the asset's 0.1s runs 3.6 SECONDS, far too slow to " +
             "read as a swing; 0.06 brings it to ~2.2s and puts the parry window where " +
             "the player expects it.")]
    [Min(0f)] public float meleeAttackFrameSeconds = 0.06f;

    [Tooltip("Add a ParryIndicator at spawn if the prefab has none, so the \"!\" mark " +
             "appears over the bell during the window. Untick if you add and tune " +
             "ParryIndicator on the prefab yourself.")]
    public bool autoAddParryIndicator = true;

    [Header("Visuals")]
    [Tooltip("World radius the bell's toll rings expand to. Purely cosmetic.")]
    [Min(1f)] public float bellResonanceRadius = 6f;

    [Tooltip("Add the Boss5BellImpact component automatically at spawn, giving the " +
             "melee attack a wind-up, lunge, squash and ground shockwave. Untick to " +
             "manage it yourself on the prefab, or if you prefer your existing " +
             "EnemyAttackLunge (which Boss5BellImpact defers to automatically anyway).")]
    public bool autoAddBellImpact = true;

    //  RUNTIME STATE
    private SpriteRenderer bossSprite;
    private SmoothSpriteFlip bossSmoothFlip;
    private EnemyAnimationController animController;
    private EnemyController enemyController;
    private Rigidbody2D body;

    private bool isDying;
    private Color restingColor = Color.white;

    private Boss5State state = Boss5State.Passive;
    // Which animation loop State B currently wants. Read by ReassertStateAnimation,
    // which re-arms it whenever something else (a parry stun recovering, a lingering
    // EnemyController attack cycle) knocks the boss back to the wrong clip.
    private bool _wantBellSwingLoop;
    // The shade the body is wearing for the CURRENT State B. Also drives the toll
    // rings and the Kaboom, so every visual in one activation agrees on a colour.
    private Color _activeTint = Color.white;
    private ParryIndicator parryIndicator;
    private Boss5Mechanic? lastMechanic;
    private Boss5Challenge activeChallenge;
    private Coroutine stateMachineRoutine;

    private enum Boss5State { Passive, Transitioning, Active }

    /// True while towers must leave this boss alone (State B and both transitions).
    public bool IsUntargetableByTowers => state != Boss5State.Passive;

    /// True only while a challenge mechanic is actually running.
    public bool IsChallengeActive => state == Boss5State.Active && activeChallenge != null;

    /// The body shade the boss is currently wearing for State B. Companion visuals
    /// (the bell impact, the toll) read this so one activation looks coherent.
    public Color CurrentStateColor => _activeTint;

    /// The mechanic currently running, or null.
    public Boss5Mechanic? CurrentMechanic => activeChallenge?.Mechanic;

    /// 0 at full pool, 1 once enrageCompleteAtLostFraction of the pool is gone.
    public float Enrage
    {
        get
        {
            float max = TotalMaxPool;
            if (max <= 0f) return 0f;
            float lost = 1f - Mathf.Clamp01(TotalCurrentPool / max);
            return Mathf.Clamp01(lost / Mathf.Max(0.01f, enrageCompleteAtLostFraction));
        }
    }

    /// Multiplier every Boss5 special attack runs through — the inherited boss
    /// difficulty / stage gate. Exposed so the challenge classes can use it too.
    public float SpecialDamageMultiplier => BossStageDamageMultiplier;

    // =========================================================================
    //  INITIALIZATION
    // =========================================================================
    protected override void Awake()
    {
        // Same contract as Boss2: EnemyData is the source of truth when present,
        // the serialized fallbacks only apply when it is not.
        if (enemyData != null)
        {
            maxHealth = enemyData.maxHealth;
            maxArmor = enemyData.maxArmor;
        }
        else
        {
            maxHealth = bossMaxHealth;
            maxArmor = bossMaxArmor;
            // EnemyStats.Awake skips its whole init block without EnemyData, so seed
            // currentHealth here or the boss spawns on CharacterStats' default 100.
            currentHealth = maxHealth;
        }

        base.Awake();

        // Must run AFTER base.Awake(): that is where EnemyStats clones enemyData, so
        // this writes to the CLONE and never touches the shared asset. It must also
        // run in Awake rather than Start, because EnemyController.Start() calls
        // ResolveFrameConfig() and CACHES these numbers - writing them later would be
        // ignored for the whole fight.
        ApplyMeleeParryConfig();
    }

    protected override void Start()
    {
        if (healthBarPrefab == null)
            healthBarPrefab = BorrowHealthBarPrefab();

        base.Start();

        bossSprite = GetComponent<SpriteRenderer>();
        if (bossSprite != null) restingColor = bossSprite.color;

        bossSmoothFlip = GetComponent<SmoothSpriteFlip>();
        if (bossSmoothFlip == null) bossSmoothFlip = gameObject.AddComponent<SmoothSpriteFlip>();
        // Minimal mode: the flip must not fight damage-flash / state colour writes.
        bossSmoothFlip.SetMinimalMode(true);

        animController = GetComponent<EnemyAnimationController>();
        enemyController = GetComponent<EnemyController>();
        body = GetComponent<Rigidbody2D>();

        // The Bellkeeper drives its own animation states, so the velocity-based
        // auto-attack heuristic must not fight it. Existing public API — no edit.
        if (animController != null) animController.SetAutoAttackDetectionEnabled(false);

        ConfigureRigidbody();
        ConfigureBossCollider();   // needs bossSprite, resolved above
        InitializeBossHealthBar();

        // Build the ring textures ONE PER FRAME while the boss walks in. Baking
        // them all in a single call is what froze the game for ~2s at spawn; baking
        // them lazily just moved the stall to the first Kaboom. Spread over ~14
        // frames it is invisible.
        StartCoroutine(Boss5Sprites.PrewarmRoutine());

        // Attack weight: wind-up, lunge, squash and a ground shockwave on the hit
        // frame. Added here rather than required on the prefab so upgrading needs no
        // re-authoring; it defers to EnemyAttackLunge if that is present.
        if (autoAddBellImpact && GetComponent<Boss5BellImpact>() == null)
            gameObject.AddComponent<Boss5BellImpact>();

        // Parry telegraph. Added here rather than required on the prefab so the
        // mark works out of the box; add ParryIndicator manually if you want to tune
        // its yOffset / indicatorSize in the inspector.
        parryIndicator = GetComponent<ParryIndicator>();
        if (parryIndicator == null && autoAddParryIndicator)
            parryIndicator = gameObject.AddComponent<ParryIndicator>();

        stateMachineRoutine = StartCoroutine(StateMachine());
    }

    private GameObject BorrowHealthBarPrefab()
    {
        foreach (var s in FindObjectsByType<EnemyStats>(FindObjectsSortMode.None))
        {
            if (s == this || s.healthBarPrefab == null) continue;
            Debug.LogWarning($"Boss5: healthBarPrefab not assigned. Borrowed one from " +
                             $"{s.gameObject.name}. Assign it explicitly on the Boss5 prefab.");
            return s.healthBarPrefab;
        }
        Debug.LogWarning("Boss5: healthBarPrefab not assigned and nothing to borrow from.");
        return null;
    }

    private void ConfigureRigidbody()
    {
        if (body == null) return;
        float mass = (enemyData != null) ? enemyData.mass : bossRigidbodyMass;
        body.mass = Mathf.Max(mass, 50f);
        body.linearDamping = bossLinearDrag;
    }

    /// Writes the melee parry timings into this boss's CLONED EnemyData.
    ///
    /// Boss5Data ships with hitFrame / parryFrameStart / parryFrameEnd all at 0, and
    /// EnemyController treats "all three zero" as unconfigured and falls back to
    /// "parry succeeds if the shield went up within the last 0.2s" - which is not a
    /// real window, just a reflex check with no telegraph. Setting real frames turns
    /// the boss's swing into something the player can actually read and answer.
    private void ApplyMeleeParryConfig()
    {
        if (!configureMeleeParry || enemyData == null) return;

        // The authored range's frameCount is not final until EnemyAnimationController
        // rewrites it at Start, so measure from the sprite array instead - that IS
        // final at Awake and is the true clip length.
        int frames = (enemyData.attackFrames != null && enemyData.attackFrames.Length > 0)
            ? enemyData.attackFrames.Length
            : Mathf.Max(1, enemyData.attack.frameCount);
        int last = frames - 1;

        int hit = Mathf.Clamp(meleeHitFrame, 0, last);
        int start = Mathf.Clamp(meleeParryFrameStart, 0, last);
        int end = Mathf.Clamp(meleeParryFrameEnd, start, last);

        // The parry is adjudicated ON the hit frame (EnemyController.IsInParryWindow
        // closes the window at pEnd+1), so a window ending before the hit could never
        // register. Extend rather than silently fail.
        if (end < hit) end = hit;

        enemyData.hitFrame = hit;
        enemyData.parryFrameStart = start;
        enemyData.parryFrameEnd = end;

        if (meleeAttackFrameSeconds > 0f)
        {
            // Mutate the struct through a local: AnimationFrameRange is a value type,
            // so assigning to enemyData.attack.speedOverride directly would not compile.
            var atk = enemyData.attack;
            atk.speedOverride = meleeAttackFrameSeconds;
            enemyData.attack = atk;
        }

        float sec = meleeAttackFrameSeconds > 0f
            ? meleeAttackFrameSeconds
            : enemyData.GetAnimSpeed(enemyData.attack);
        Debug.Log($"[Boss5] Melee parry - clip {frames} frames @ {sec:F3}s " +
                  $"({frames * sec:F2}s total). Hit on frame {hit} ({hit * sec:F2}s in). " +
                  $"Parry window frames {start}-{end} = {(end - start + 1) * sec:F2}s, " +
                  $"opening {(hit - start) * sec:F2}s before the hit.");
    }

    /// The "!" mark must never appear outside a real melee swing.
    ///
    /// EnemyController only sets IsAttacking during an attack cycle, and Boss5
    /// disables that controller for the whole of State B, so the mark is already
    /// suppressed there. This makes it explicit rather than emergent - a lingering
    /// attack-cycle coroutine finishing just after the bell tolls could otherwise
    /// flash the mark for a frame while the boss is standing still, telegraphing a
    /// parry the player cannot take.
    private void SetParryIndicatorEnabled(bool on)
    {
        if (parryIndicator != null) parryIndicator.enabled = on;
    }

    private void ConfigureBossCollider()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = false;

        // Preferred path: derive the hit box from the art, so the collider tracks
        // any change to the sprite or the transform scale with no re-authoring.
        if (autoFitColliderToSprite && bossSprite != null && bossSprite.sprite != null)
        {
            // sprite.bounds is in LOCAL space and CircleCollider2D.radius is too, so
            // no division by lossyScale here — the transform scales both together.
            Bounds b = bossSprite.sprite.bounds;
            float halfWidth = b.extents.x;
            float halfHeight = b.extents.y;

            // Fit to the WIDER axis so the skirt is never left poking out of the
            // collider, which is exactly how the player ends up inside the bell.
            col.radius = Mathf.Max(0.05f, Mathf.Max(halfWidth, halfHeight * 0.75f)
                                          * colliderFitFraction);
            col.offset = new Vector2(b.center.x, b.center.y);
            return;
        }

        if (!applyColliderOverride) return;   // trust the prefab

        col.radius = bossColliderRadius;
        col.offset = new Vector2(bossColliderOffsetX, bossColliderOffsetY);
    }

    private void InitializeBossHealthBar()
    {
        if (HealthBar == null) return;

        // Bosses feed the bar the COMBINED armour+health pool (see BaseBossStats).
        HealthBar.Initialize(transform, TotalMaxPool);

        const float MIN_HEADROOM = 0.1f;
        healthBarYOffset = healthBarExtraYPadding;
        if (bossSprite != null && bossSprite.sprite != null)
        {
            float topAboveBoss = bossSprite.bounds.max.y - transform.position.y;
            float computed = topAboveBoss + healthBarExtraYPadding - healthBarYReduction;
            healthBarYOffset = Mathf.Max(computed, topAboveBoss + MIN_HEADROOM);
        }
        UpdateHealthBarOffset();

        // 4000 clears the whole cartoon-grass band (~400..1600) while staying under
        // fog (5000) and the night overlay (6000). Same value Boss2 uses.
        var canvas = HealthBar.GetComponentInChildren<Canvas>(true)
                     ?? HealthBar.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 4000;
        }
    }

    private void UpdateHealthBarOffset()
    {
        if (HealthBar == null) return;
        float x = (bossSprite != null && bossSprite.flipX) ? -healthBarXOffset : healthBarXOffset;
        HealthBar.SetOffset(new Vector3(x, healthBarYOffset, 0f));
    }

    private void Update()
    {
        if (isDying) return;
        UpdateHealthBarOffset();
        // Keep the targeting gate in sync every frame. Cheap (a HashSet add/remove
        // only when the flag actually changes) and immune to a coroutine being
        // stopped mid-state, which would otherwise strand the boss untargetable.
        BossTargetingGate.Set(gameObject, IsUntargetableByTowers);

        ReassertStateAnimation();
    }

    /// Re-arms the State B attack loop if something else knocked the animation back
    /// to idle. Two things legitimately do that and neither is under our control:
    ///
    ///   * A parry stun. FreezeAnimation / UnfreezeAnimation ends by restoring the
    ///     IDLE loop, so a boss parried during State B would stand there playing its
    ///     walk cycle until the state ended.
    ///   * A lingering EnemyController.AttackCycle coroutine. It is started before
    ///     we disable the controller and keeps running to completion (disabling a
    ///     MonoBehaviour does not stop coroutines), finishing with
    ///     StopMeleeAttackAnimation() and a return to idle.
    ///
    /// Re-calling PlayLoopingAttackAnimation only when it is actually showing idle
    /// means the common case costs one bool comparison and never restarts the clip
    /// mid-swing (which would visibly stutter back to frame 0).
    private void ReassertStateAnimation()
    {
        if (state == Boss5State.Passive) return;
        if (animController == null || !animController.enabled) return;
        if (animController.IsAnimationFrozen) return;   // let the parry stun hold its pose

        bool showingIdle = animController.IsPlayingIdleLoop;

        if (_wantBellSwingLoop && showingIdle)
            animController.PlayLoopingAttackAnimation();
        else if (!_wantBellSwingLoop && !showingIdle)
            animController.PlayLoopingIdleAnimation();
    }

    //  STATE MACHINE
    private IEnumerator StateMachine()
    {
        // Small grace period so the boss walks in before the first challenge.
        yield return new WaitForSeconds(Mathf.Min(3f, passiveDurationAtFullHp * 0.4f));

        while (!isDying)
        {
            // ---- STATE A 
            EnterPassive();

            float passiveFor = Mathf.Lerp(passiveDurationAtFullHp, passiveDurationAtLowHp, Enrage);
            float t = 0f;
            while (t < passiveFor && !isDying)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (isDying) yield break;

            Boss5Mechanic? pick = PickMechanic();
            if (pick == null)
            {
                // Nothing enabled — stay passive forever rather than spin.
                yield return new WaitForSeconds(1f);
                continue;
            }

            // ---- TRANSITION IN 
            // Resolve the shade BEFORE the transition so the colour tween, the toll
            // rings and the challenge's own visuals all start from the same value.
            _activeTint = ResolveStateColor(pick.Value);

            float transitionFor = Mathf.Lerp(transitionDurationAtFullHp,
                                             transitionDurationAtLowHp, Enrage);
            yield return TransitionInto(transitionFor);
            if (isDying) yield break;

            // ---- STATE B 
            state = Boss5State.Active;
            lastMechanic = pick;
            activeChallenge = CreateChallenge(pick.Value);

            if (activeChallenge != null)
                yield return activeChallenge.Run();

            activeChallenge = null;
            if (isDying) yield break;

            // ---- TRANSITION OUT 
            yield return TransitionOutOf(transitionFor);
        }
    }

    private Boss5Mechanic? PickMechanic()
    {
        var pool = new List<Boss5Mechanic>(3);
        if (enableMotionPenalty) pool.Add(Boss5Mechanic.MotionPenalty);
        if (enableAttackPenalty) pool.Add(Boss5Mechanic.AttackPenalty);
        if (enableSafeZone) pool.Add(Boss5Mechanic.SafeZone);
        if (pool.Count == 0) return null;

        if (avoidRepeatingMechanic && lastMechanic.HasValue && pool.Count > 1)
            pool.Remove(lastMechanic.Value);

        return pool[Random.Range(0, pool.Count)];
    }

    /// Which shade the body wears for a given mechanic.
    private Color ResolveStateColor(Boss5Mechanic mechanic)
    {
        if (!tintPerMechanic) return activeStateColor;
        switch (mechanic)
        {
            case Boss5Mechanic.MotionPenalty: return motionStateColor;
            case Boss5Mechanic.AttackPenalty: return attackStateColor;
            case Boss5Mechanic.SafeZone: return safeZoneStateColor;
        }
        return activeStateColor;
    }

    private Boss5Challenge CreateChallenge(Boss5Mechanic mechanic)
    {
        switch (mechanic)
        {
            case Boss5Mechanic.MotionPenalty: return new Boss5MotionPenaltyChallenge(this);
            case Boss5Mechanic.AttackPenalty: return new Boss5AttackPenaltyChallenge(this);
            case Boss5Mechanic.SafeZone: return new Boss5SafeZoneChallenge(this);
        }
        return null;
    }

    private void EnterPassive()
    {
        state = Boss5State.Passive;
        _wantBellSwingLoop = false;
        SetParryIndicatorEnabled(true);
        BossTargetingGate.Set(gameObject, false);

        if (enemyController != null) enemyController.enabled = true;
        if (animController != null) animController.PlayLoopingIdleAnimation();
        SetBodyTint(restingColor);
    }

    private IEnumerator TransitionInto(float duration)
    {
        state = Boss5State.Transitioning;
        BossTargetingGate.Set(gameObject, true);
        SetParryIndicatorEnabled(false);

        RingBell();

        // Hand movement authority over IMMEDIATELY, before the tween.
        // This must happen at the START of the transition, not the end. Disabling a
        // MonoBehaviour does NOT stop its running coroutines, and EnemyController's
        // AttackCycle calls animController.PlayMeleeAttackAnimation() — which would
        // stomp the State B loop below and, when that one-shot finished, drop the
        // boss back onto its WALK frames while it is supposed to be standing still
        // ringing. Killing the controller first means no NEW cycle can start.
        //
        // The visible "winds down to a stop" beat is preserved because the velocity
        // lerp below is now the only thing writing to the body.
        StopDead();

        // Body language for State B. See `activePose` for why looping the attack
        // clip forever is not the default.
        if (animController != null)
        {
            if (activePose == Boss5ActivePose.KeepWalking)
            {
                _wantBellSwingLoop = false;
                animController.PlayLoopingIdleAnimation();
            }
            else
            {
                _wantBellSwingLoop = true;
                animController.PlayLoopingAttackAnimation();

                if (activePose == Boss5ActivePose.TollThenStand)
                    StartCoroutine(EndTollAfterOneClip());
            }
        }

        // Colour shift + a gradual slide to a full stop, so "the bell winds down"
        // reads clearly rather than the boss snapping in place.
        float t = 0f;
        Vector2 startVel = body != null ? body.linearVelocity : Vector2.zero;
        while (t < duration && !isDying)
        {
            float k = Mathf.Clamp01(t / duration);
            SetBodyTint(Color.Lerp(restingColor, _activeTint, k));
            if (body != null && body.bodyType != RigidbodyType2D.Static)
                body.linearVelocity = Vector2.Lerp(startVel, Vector2.zero, k);
            t += Time.deltaTime;
            yield return null;
        }

        SetBodyTint(_activeTint);
        StopDead();
    }

    /// One pass of the attack clip = the bell swinging and tolling. After that we
    /// settle onto the walk loop so the boss visibly STANDS for the rest of the
    /// challenge instead of endlessly re-swinging.
    private IEnumerator EndTollAfterOneClip()
    {
        float clip = 1f;
        if (enemyData != null && enemyData.attack.frameCount > 0)
            clip = enemyData.attack.frameCount * enemyData.GetAnimSpeed(enemyData.attack);

        yield return new WaitForSeconds(Mathf.Clamp(clip, 0.2f, 6f));

        // Bail if the challenge already ended while the toll was playing.
        if (isDying || state == Boss5State.Passive) yield break;

        _wantBellSwingLoop = false;
        if (animController != null) animController.PlayLoopingIdleAnimation();
    }

    private IEnumerator TransitionOutOf(float duration)
    {
        state = Boss5State.Transitioning;
        RingBell();

        float t = 0f;
        while (t < duration && !isDying)
        {
            SetBodyTint(Color.Lerp(_activeTint, restingColor, Mathf.Clamp01(t / duration)));
            t += Time.deltaTime;
            yield return null;
        }
        SetBodyTint(restingColor);
    }

    private void StopDead()
    {
        // Same idiom Boss2 uses while casting: hand movement authority to this
        // script by switching the controller off, and zero any residual velocity.
        if (enemyController != null) enemyController.enabled = false;
        if (body != null && body.bodyType != RigidbodyType2D.Static)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    /// Writes the State B tint.
    /// This cooperates with EnemyStats' damage flash rather than fighting it. That
    /// flash captures whatever colour is showing when it STARTS and restores to it
    /// when it ends (see EnemyStats.preFlashColor — the comment there explains the
    /// white-latch bug that motivated the design). Because we only write the tint
    /// during the transition tweens and once on entry, a hit taken mid-State-B
    /// blinks and then correctly returns to the active colour, exactly like the
    /// freeze / confusion tints the base class was built to preserve.
    private void SetBodyTint(Color c)
    {
        if (bossSprite == null) return;
        bossSprite.color = c;
    }

    private void RingBell()
    {
        // Visible toll: concentric rings expanding from the bell, plus a light pulse
        // that wobbles as it decays. Fires on entering AND leaving State B, so the
        // player can read the state change without relying on audio.
        Boss5BellResonance.Play(transform.position, _activeTint, bellResonanceRadius);

        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.18f, 0.22f);

        if (AudioManager.instance == null) return;

        EventReference ev = bellRingEvent;
        if (ev.IsNull && FMODEvents.instance != null)
        {
            // Reuse an existing boss event so the bell is audible before the
            // dedicated one is authored in FMOD Studio.
            ev = FMODEvents.instance.boss2Spawn;
        }
        if (!ev.IsNull) AudioManager.instance.PlayOneShot(ev, transform.position);
    }

    // =========================================================================
    //  DAMAGE INTAKE
    // =========================================================================

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isDying) return;

        // PLAYER RANGED — handled here ONLY when WeaponProjectile will not do it.
        //
        // WeaponProjectile.OnTriggerEnter2D damages whatever it hits, but only for
        // colliders tagged "Enemy". So there are two cases and this must behave
        // correctly in both, because the prefab's tag is not knowable from here:
        //
        //   tagged "Enemy"      -> WeaponProjectile applies the hit. Stepping in as
        //                          well would DOUBLE it, and would double-count the
        //                          Mechanic 2 meter so ranged weapons filled it twice
        //                          as fast as melee. So we stand back.
        //   not tagged "Enemy"  -> WeaponProjectile destroys the bullet WITHOUT
        //                          damaging us, so we must apply the hit ourselves.
        //
        // Either way the damage lands exactly once, tagged as Player.
        if (!CompareTag("Enemy"))
        {
            var wp = other.GetComponent<WeaponProjectile>();
            if (wp != null)
            {
                float dmg = wp.GetDamage();
                TakeDamageFrom(dmg, BossDamageSource.Player);
                CombatStats.ReportPlayerDamageDealt(wp.GetOwner(), dmg, transform.position);
                Destroy(other.gameObject);
                return;
            }
        }

        // TOWER PROJECTILES ARE DELIBERATELY NOT HANDLED HERE.
        //
        // Projectile.HitTarget already applies its own damage, reports its own
        // telemetry and releases itself to the pool. The projectile's collider is a
        // trigger and ours is not, so Unity raises OnTriggerEnter2D on BOTH objects —
        // intercepting here meant the boss took every tower shot TWICE (which
        // cancelled the 25% State A penalty and then some), double-counted telemetry,
        // and called Destroy() on a POOLED projectile that PrefabPool still tracks.
        //
        // Projectile.HitTarget now routes through BossDamageRouting.FromTower, so the
        // hit arrives exactly once and correctly tagged. See Projectile.cs.
    }

    /// Legacy entry point. Every damage source in the project that does not know
    /// about Boss5 lands here; the source is inferred (see unattributedDamagePolicy).
    public override void TakeDamage(float amount)
    {
        TakeDamageFrom(amount, BossDamageSource.Unknown);
    }

    /// Source-aware damage intake. This is where every Bellkeeper damage rule lives.
    public void TakeDamageFrom(float amount, BossDamageSource source)
    {
        if (isDying) return;
        if (DebugCheats.DamageBlocked(this)) return;
        if (amount <= 0f) return;

        if (source == BossDamageSource.Unknown)
        {
            // Neutral: apply in full, credit nobody. Deliberately bypasses BOTH the
            // State A tower penalty and the State B challenge intercepts, so a DoT
            // can neither be silently reduced nor fill a mechanic's meter.
            if (unattributedDamagePolicy == UnattributedDamagePolicy.Neutral)
            {
                ApplyDamageToPools(amount);
                return;
            }

            source = ClassifyUnattributed();
        }

        // ---- STATE B: the challenge decides what a player hit means -----------
        if (state != Boss5State.Passive)
        {
            if (source == BossDamageSource.Tower)
            {
                // Towers are supposed to have stopped firing. Anything still in
                // flight (a projectile launched a frame before the state flipped, or
                // a tower type that has not been hooked into BossTargetingGate) is
                // absorbed rather than counted. This is what makes the tower rule
                // hold even if the targeting hook is missing.
                return;
            }

            // Player damage during a challenge. The Attack Penalty mechanic nullifies
            // it and fills its meter instead; the other two let it through normally.
            if (activeChallenge != null && activeChallenge.InterceptPlayerDamage(amount))
                return;
        }
        else if (source == BossDamageSource.Tower)
        {
            // ---- STATE A: towers deal reduced damage -------------------------
            amount *= Mathf.Clamp01(towerDamageMultiplier);
            if (amount <= 0f) return;
        }

        ApplyDamageToPools(amount);
    }

    /// Armour-then-health application, matching the Boss1/Boss2 hand-rolled shape so
    /// the combined-pool health bar stays consistent.
    private void ApplyDamageToPools(float amount)
    {
        if (!armorDestroyed && bossArmor > 0f)
        {
            bossArmor -= amount;
            if (bossArmor <= 0f)
            {
                float overflow = -bossArmor;
                bossArmor = 0f;
                OnArmorDestroyed();
                currentHealth -= overflow;
            }
        }
        else
        {
            currentHealth -= amount;
        }

        CallStartDamageFlash();
        UpdateBossHealthBar();

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            ExecuteBossDeath();
        }
    }

    private BossDamageSource ClassifyUnattributed()
    {
        switch (unattributedDamagePolicy)
        {
            case UnattributedDamagePolicy.AlwaysPlayer: return BossDamageSource.Player;
            case UnattributedDamagePolicy.AlwaysTower: return BossDamageSource.Tower;
        }

        // Auto: a live player inside melee reach means this is almost certainly a
        // player swing. Otherwise it is a tower beam / trap / aura.
        var reg = PlayerRegistry.Instance;
        if (reg != null)
        {
            foreach (var ps in reg.AllAliveInRadius(transform.position, meleeAttributionRadius))
                if (ps != null) return BossDamageSource.Player;
        }
        return BossDamageSource.Tower;
    }



    /// Fires the massive global / AoE blast. Public so the challenge classes can
    /// call it, and so it can be triggered from a debug menu.
    public void FireKaboom()
    {
        if (isDying) return;

        Vector3 pos = transform.position;

        // --- VFX + feel ---
        Boss5KaboomVFX.Play(pos, kaboomVisualRadius, _activeTint);
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.75f, 0.5f);

        if (AudioManager.instance != null)
        {
            EventReference ev = kaboomEvent;
            if (ev.IsNull && FMODEvents.instance != null) ev = FMODEvents.instance.boss2Explosion;
            if (!ev.IsNull) AudioManager.instance.PlayOneShot(ev, pos);
        }

        float mult = SpecialDamageMultiplier;

        // --- Players ---
        float playerDamage = kaboomPlayerDamage * mult;
        if (playerDamage > 0f)
        {
            var reg = PlayerRegistry.Instance;
            if (reg != null)
            {
                float radius = kaboomHitsPlayersGlobally
                    ? 100000f
                    : Mathf.Max(0.01f, kaboomBuildingRadius);

                // ToList-style copy: TakeDamage can down a player, which may mutate
                // the registry's view mid-iteration.
                var victims = new List<PlayerStats>(reg.AllAliveInRadius(pos, radius));
                foreach (var ps in victims)
                {
                    if (ps == null) continue;
                    ps.TakeDamage(playerDamage);
                    // Player-side on-hit augments (Damage Reflection / Ice Armor).
                    EnemyController.NotifyCharacterDamaged(ps, playerDamage, gameObject);
                }
            }
        }

        // --- Towers + core ---
        float buildingDamage = kaboomBuildingDamage * mult;
        if (buildingDamage > 0f && EnergyManager.Instance != null)
        {
            bool global = kaboomBuildingRadius <= 0f;
            float r = global ? float.MaxValue : kaboomBuildingRadius;
            var already = new HashSet<IEnergyConsumer>();

            var towers = Tower.ActiveTowers;
            for (int i = towers.Count - 1; i >= 0; i--)   // backward: damage may remove entries
            {
                var tower = towers[i];
                if (tower != null)
                    DamageBuilding(tower, tower.gameObject, pos, r, buildingDamage, already);
            }

            var coreObj = GameObject.FindGameObjectWithTag("Core");
            if (coreObj != null)
            {
                var coreConsumer = coreObj.GetComponentInParent<IEnergyConsumer>();
                if (coreConsumer != null)
                    DamageBuilding(coreConsumer, coreObj, pos, r, buildingDamage, already);
            }
        }
    }

    private void DamageBuilding(IEnergyConsumer consumer, GameObject go, Vector3 pos,
                                float radius, float damage, HashSet<IEnergyConsumer> already)
    {
        if (consumer == null || go == null) return;

        if (radius < float.MaxValue)
        {
            var col = go.GetComponent<Collider2D>();
            float dist = (col != null)
                ? Vector2.Distance(pos, col.ClosestPoint(pos))   // surface distance (0 inside)
                : Vector2.Distance(pos, go.transform.position);
            if (dist > radius) return;
        }

        if (!already.Add(consumer)) return;
        EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
    }

    /// Called by a challenge when the player fails it.
    internal void OnChallengeFailed(Boss5Challenge challenge)
    {
        FireKaboom();
        if (endStateBOnFailure) challenge?.RequestEnd();
    }


    public override void Die()
    {
        if (isDying) return;
        currentHealth = 0f;
        ExecuteBossDeath();
    }

    private void ExecuteBossDeath()
    {
        isDying = true;
        if (!gameObject.scene.isLoaded) return;

        // Release everything the challenge owns BEFORE StopAllCoroutines, because
        // the challenge coroutine may never get another frame to clean up itself.
        // Time.timeScale in particular MUST be released or the game freezes forever.
        if (activeChallenge != null)
        {
            activeChallenge.AbortAndCleanup();
            activeChallenge = null;
        }
        BossTargetingGate.Set(gameObject, false);

        transform.rotation = Quaternion.identity;
        CombatJuice.OnBossKilled(gameObject);

        if (bossSprite != null) bossSprite.color = restingColor;

        if (HealthBar != null) Destroy(HealthBar.gameObject);

        StopAllCoroutines();
        stateMachineRoutine = null;

        if (body != null) body.simulated = false;
        foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;
        if (enemyController != null) enemyController.enabled = false;
        if (animController != null) animController.enabled = false;

        bossArmor = 0f;
        armorDestroyed = true;

        Vector3 deathPos = transform.position;

        // Reward ring, same shape as Boss1/Boss2.
        for (int i = 0; i < 10; i++)
        {
            float angle = (360f / 10) * i * Mathf.Deg2Rad;
            Vector3 spawnPos = deathPos + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 1.5f;
            int energyValue = (EnergyDropManager.Instance != null)
                ? EnergyDropManager.Instance.defaultEnergyValue : 10;
            EnergyDrop.CreateEnergyDrop(spawnPos, energyValue);
        }

        RollBlueprintDrop(deathPos);

        // Wave counter -> augment-335 tithe -> EnergyManager kill event -> attribution
        // cleanup, in the one order that is load-bearing. Drops are deliberately NOT
        // routed through here — the ring above already paid them.
        EnemyStats.FireCommonDeathHooks(gameObject);

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: disintegrationDuration,
            onComplete: () =>
            {
                if (AudioManager.instance != null && FMODEvents.instance != null)
                    AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, deathPos);
            });

        // Guaranteed teardown if the VFX never completes — otherwise
        // GameOrchestrator.WaitForBossDead() would spin on the corpse forever.
        ScheduleDeathFailsafe(disintegrationDuration);
    }

    protected override void OnDestroy()
    {
        // Safety net for paths that never reach ExecuteBossDeath (scene unload
        // mid-challenge, boss destroyed outright). Every step is idempotent, and
        // releasing the time-stop here is what stops a mid-Mechanic-3 scene change
        // from leaving the game frozen at timeScale 0.
        if (activeChallenge != null)
        {
            activeChallenge.AbortAndCleanup();
            activeChallenge = null;
        }
        BossTargetingGate.Set(gameObject, false);

        if (HealthBar != null) Destroy(HealthBar.gameObject);

        base.OnDestroy();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, kaboomVisualRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, meleeAttributionRadius);
    }
#endif
}


/// What the Bellkeeper's body does while a State B challenge is running.
public enum Boss5ActivePose
{
    /// Swing and toll once, then stand still on the walk loop. Recommended.
    TollThenStand = 0,
    /// Repeat the attack clip for the whole challenge (reads as "hiding in the bell").
    LoopBellSwing = 1,
    /// Stay on the walk loop throughout; tint and rings carry the state change.
    KeepWalking = 2,
}


