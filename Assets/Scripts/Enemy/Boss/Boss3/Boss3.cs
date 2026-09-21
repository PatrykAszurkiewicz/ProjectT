using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Boss3 — "The Shade": a glitchy, slim shadow that teleports around the arena and
// lashes out with ranged, tree-like hands. Design notes:
//   Visuals come from Boss3Visual (procedural silhouette + RGB glitch + fiery eyes).
//   Movement is GLITCH-TELEPORTATION, not pathfinding — Boss3 does NOT use
//     EnemyController. It blinks between stand-off spots around whatever it is about
//     to hit. So no NavMesh / steering wiring is needed.
//   Attacks are TELEGRAPHED ranged tree-hands (Boss3TreeHand): the branch grows out
//     thin + faint at a LOCKED point first, giving the player a dodge window, then
//     snaps out and deals damage — the same "signal, then strike" contract Boss2's
//     meteor uses.
//   Targets cycle: player → tower → core → player … so it threatens the whole base,
//     not just the player.
//   It inherits BaseBossStats, so the big top-of-screen boss bar (armour + health,
//     same as Boss1/Boss2) is registered automatically — no extra wiring.
public class Boss3 : BaseBossStats, ISpritePrewarm
{
    [Header("Boss3 Stats")]
    [SerializeField] private float bossMaxHealth = 900f;
    [SerializeField] private float bossMaxArmor = 700f;

    [Header("Boss3 Body")]
    [SerializeField] private float bossColliderRadius = 0.64f;
    [SerializeField] private float bossColliderOffsetY = 0.96f;  // collider around the torso, not the feet pivot
    [SerializeField] private float bossRigidbodyMass = 100f;
    [SerializeField] private float bossLinearDrag = 6f;

    [Header("Underwater Float")]
    [Tooltip("Vertical bob amplitude of the whole body, world units. Owned here (not the " +
             "visual) so it moves the transform around the teleport anchor without fighting blinks.")]
    [SerializeField] private float floatBobAmplitude = 0.16f;
    [SerializeField] private float floatBobSpeed = 1.3f;
    [Tooltip("Horizontal drift amplitude of the whole body, world units.")]
    [SerializeField] private float floatSwayAmplitude = 0.1f;
    [SerializeField] private float floatSwaySpeed = 0.8f;

    [Header("Health Bar (world-space floating bar; the big top bar is automatic)")]
    [SerializeField] private float healthBarExtraYPadding = 0.5f;
    [SerializeField] private float healthBarYReduction = 0f;
    private float healthBarYOffset;

    [Header("Arena Bounds (teleport clamp)")]
    [SerializeField] private float mapBoundsMin = -45f;
    [SerializeField] private float mapBoundsMax = 45f;

    [Header("Teleport")]
    [Tooltip("Seconds the boss holds after arriving before it begins an attack.")]
    [SerializeField] private float postTeleportPause = 0.25f;
    [Tooltip("How long the dissolve-out / reform-in each take.")]
    [SerializeField] private float teleportOutDuration = 0.22f;
    [SerializeField] private float teleportInDuration = 0.28f;
    [Tooltip("Stand-off distance from the target when blinking in (so the ranged hands reach).")]
    [SerializeField] private float teleportStandoffMin = 6f;
    [SerializeField] private float teleportStandoffMax = 9f;
    [Tooltip("Chance per cycle to blink to a random spot instead of near the target (unpredictability).")]
    [Range(0f, 1f)][SerializeField] private float randomBlinkChance = 0.25f;

    [Header("Teleport — Placement Safety")]
    [Tooltip("The boss will not materialise within this distance of a layout obstacle " +
             "(walls, rocks, buildings). Roughly its body radius plus breathing room.")]
    [SerializeField] private float teleportObstacleClearance = 1.8f;
    [Tooltip("Layers treated as solid when picking a blink spot. Left empty it resolves " +
             "to the map's own obstacle layer at runtime, so it tracks TowerDefenseMap.")]
    [SerializeField] private LayerMask teleportBlockLayers;
    [Tooltip("How many candidate spots to test before giving up and using the roomiest " +
             "one found. Cheap — these are OverlapCircle checks, once per blink.")]
    [SerializeField] private int teleportPlacementAttempts = 32;
    [Tooltip("Also keep blinks inside the map's playable radius (read from " +
             "TowerDefenseMap). Off = only the square Arena Bounds above apply.")]
    [SerializeField] private bool clampTeleportToMapRadius = true;
    [Tooltip("Margin kept inside the map edge when the clamp above is on.")]
    [SerializeField] private float mapEdgeMargin = 1.5f;

    [Header("Teleport — Structure Standoff")]
    [Tooltip("The boss never materialises closer than this to the CORE or to a TOWER, " +
             "measured from the edge of their footprint. A branch thrown from " +
             "point-blank is a stub: almost none of it has a hitbox on it, and it sits " +
             "inside the boss's own body so every shot aimed at it hits the boss " +
             "instead — the player has no way to break it. Applies to random blinks and " +
             "to the spawn nudge as well as to targeted ones.")]
    [SerializeField] private float minStructureStandoff = 5.5f;
    [Tooltip("Shortest branch he will throw at a BUILDING. If the core/tower he was " +
             "about to hit is closer than this, he switches to a target with room, or " +
             "skips the attack and blinks away rather than throwing something " +
             "unbreakable. Players are exempt — they choose their own distance.")]
    [SerializeField] private float minBranchLength = 4.5f;

    [Header("Attack")]
    [Tooltip("Reach of a tree-hand strike, world units.")]
    [SerializeField] private float attackReach = 11f;
    [Tooltip("Damage per tree-hand hit (before difficulty/stage scaling).")]
    [SerializeField] private float handDamage = 10f;
    [Tooltip("Radius of the strike's damage at the tip.")]
    [SerializeField] private float handImpactRadius = 1.3f;
    [Tooltip("Hands thrown per attack when targeting the player (a small spread to dodge).")]
    [SerializeField] private int playerHandCount = 2;
    [Tooltip("Spread of multi-hand player attacks, world units around the locked point.")]
    [SerializeField] private float playerHandSpread = 1.6f;
    [Tooltip("How many attacks the boss performs from each teleport spot before " +
             "blinking away. Higher = he stays put longer, giving the player a bigger " +
             "window to damage him (he's hard to hit while teleporting).")]
    [SerializeField] private int attacksPerTeleport = 2;
    [Tooltip("Pause between the repeated attacks at a single teleport spot.")]
    [SerializeField] private float betweenAttackPause = 0.45f;
    [Tooltip("Seconds of recovery after the LAST attack at a spot resolves, before the next teleport.")]
    [SerializeField] private float attackRecover = 0.6f;

    [Header("Hand Attack — Interrupt Counterplay")]
    [Tooltip("If on, the player can INTERRUPT a wind-up: deal enough damage to the boss " +
             "before the hands strike and the attack fizzles (no damage) + the boss staggers. " +
             "During the wind-up the boss shows cyan 'burst me' rings; the target ring shows where it will hit.")]
    [SerializeField] private bool handAttackInterruptible = true;
    [Tooltip("Damage the boss must take DURING a single wind-up to interrupt it. " +
             "Lower = easier to interrupt. Tune to your weapons' DPS over the telegraph window. " +
             "NOTE: this is the CHEAP interrupt (short stagger). Keep it ABOVE Hand Health, " +
             "or a player aiming at a branch trips this first with the shots that miss the " +
             "branch and hit the body — the volley fizzles, the branch never shatters, and " +
             "they lose the long prone window they earned.")]
    [SerializeField] private float handInterruptThreshold = 75f;
    [SerializeField] private Boss3TreeHand.Settings handSettings = new Boss3TreeHand.Settings();
    [SerializeField] private LayerMask damageLayers;

    [Header("Hand Attack — Destructible Hands")]
    [Tooltip("If on, each extended tree-hand is a target in its own right: shoot or hit " +
             "the branch itself and it shatters into pieces, cancelling that strike. " +
             "Turn OFF to restore the pre-feature behaviour exactly (no hand hitboxes).")]
    [SerializeField] private bool handsAreDestructible = true;
    [Tooltip("ON (default): a branch breaks after a fixed NUMBER OF HITS rather than a " +
             "damage total, so it always takes the same number of shots no matter what " +
             "weapon, augments or stage scaling are in play. This is the setting that " +
             "makes breaking a hand feel reliable. OFF uses the Hand Health pool below.")]
    [SerializeField] private bool handBreakByHitCount = true;
    [Tooltip("Hits needed to snap one branch. Applies per branch, and a volley throws " +
             "two (three in phase 2) — but breaking any ONE of them interrupts the " +
             "whole attack, so this is the real number that matters.")]
    [SerializeField] private int handHitsToBreak = 3;
    [Tooltip("Only used when Hand Break By Hit Count is OFF. Damage a single branch " +
             "absorbs before it breaks. Keep it BELOW Hand Interrupt Threshold or the " +
             "cheap body-burst interrupt fires first and steals the volley.")]
    [SerializeField] private float handHealth = 40f;
    [Tooltip("Log every branch hit, so you can see which branch a shot actually landed " +
             "on and how many hits it has left.")]
    [SerializeField] private bool handDebugLog = false;
    [Tooltip("How many hands of the same volley must be destroyed to interrupt the " +
             "attack. 1 = destroying ANY extended hand interrupts (recommended).")]
    [SerializeField] private int handsBrokenToInterrupt = 1;
    [Tooltip("Radius of each round hit-spot strung along a branch. Bigger = easier to " +
             "hit, but the branch also intercepts more shots that were aimed past it.")]
    [SerializeField] private float handHitboxRadius = 0.38f;
    [Tooltip("Number of hit-spots along a branch.")]
    [SerializeField] private int handHitboxSpots = 6;
    [Tooltip("Fraction of the branch nearest the BOSS left with no hitbox, so shots " +
             "aimed at the boss's body aren't swallowed by the stub of the branch.")]
    [Range(0f, 0.6f)][SerializeField] private float handHitboxStartFraction = 0.22f;
    [Tooltip("Let TOWER fire break hands too. OFF by default: towers currently put all " +
             "their damage into the boss, and letting branches intercept it would " +
             "silently change tower balance in this fight.")]
    [SerializeField] private bool towerShotsCanBreakHands = false;
    [Tooltip("Seconds the branch spends LOCKING ON before it starts travelling — the " +
             "ring converges and the branch buds out of the hand, but goes nowhere yet.")]
    [SerializeField] private float handLockOnDuration = 0.55f;
    [Tooltip("Seconds the branch spends CRAWLING to its locked point. This is the whole " +
             "counterplay window: long enough to turn, aim at the branch and break it, " +
             "not just sidestep. Damage only lands if it arrives.")]
    [SerializeField] private float handCrawlDuration = 2.2f;
    [Tooltip("Seconds the boss lies PRONE after a hand is destroyed — stationary, " +
             "sagging, and taking vulnerableDamageMultiplier damage. The payoff.")]
    [SerializeField] private float handBreakProneDuration = 5f;
    [Tooltip("Extra damage the boss takes for EACH hand destroyed since his last blink " +
             "(0.10 = +10% per stack). Stacks multiply the punish for a player who " +
             "keeps breaking hands instead of only dodging. Resets when he teleports.")]
    [SerializeField] private float handBreakDamageAmpPerStack = 0.10f;
    [Tooltip("Cap on the stacks above, so a long stationary phase can't spiral.")]
    [SerializeField] private int handBreakDamageAmpMaxStacks = 5;
    [Tooltip("How far the boss sags toward the ground while prone, world units.")]
    [SerializeField] private float proneSagAmount = 0.55f;
    [Tooltip("Seconds before a destroyed hand's arm has fully grown back. The boss " +
             "will not throw a branch from a stump, so keep this shorter than a full " +
             "prone + teleport cycle or he'll skip attacks.")]
    [SerializeField] private float armRegrowDuration = 1.6f;

    [Header("Death")]
    [SerializeField] private float disintegrationDuration = 1.4f;

    [Header("Phase 2 (triggered on armour break)")]
    [Tooltip("Teleport/pause durations are multiplied by this in phase 2 (smaller = faster, more relentless).")]
    [SerializeField] private float phase2SpeedMul = 0.68f;
    [Tooltip("Extra tree-hands added to each player attack in phase 2.")]
    [SerializeField] private int phase2ExtraHands = 1;
    [Tooltip("Telegraph (dodge-window) duration multiplier in phase 2 — tighter windows.")]
    [SerializeField] private float phase2TelegraphMul = 0.82f;

    [Header("Punish Window (destabilised)")]
    [Tooltip("After finishing its attacks at a spot, the boss destabilises: it stops, " +
             "glitches, and takes bonus damage for this long — the reward for surviving.")]
    [SerializeField] private float destabilizeDuration = 1.15f;
    [Tooltip("Damage multiplier while destabilised (the punish).")]
    [SerializeField] private float vulnerableDamageMultiplier = 1.7f;
    [Range(0f, 1f)]
    [Tooltip("Chance to destabilise after a normal attack sequence (always on interrupt).")]
    [SerializeField] private float destabilizeChance = 0.85f;

    [Header("Second Attack — Root Burst (DISABLED by default)")]
    [Tooltip("Chance a stop uses the radial root-burst instead of the tree-hands. " +
             "0 = OFF (boss only uses the tree-hand attacks). Raise it if you ever want the ring back.")]
    [Range(0f, 1f)][SerializeField] private float rootBurstChance = 0f;
    [Tooltip("Number of spikes in the ring.")]
    [SerializeField] private int rootBurstSpikes = 10;
    [SerializeField] private float rootBurstRadius = 3.6f;
    [Tooltip("Angular width of the SAFE gap in the ring (degrees) the player runs to.")]
    [SerializeField] private float rootBurstGapDegrees = 60f;
    [SerializeField] private float rootBurstDamage = 15f;
    [SerializeField] private float rootBurstTelegraph = 0.8f;

    [Header("Third Attack — Dark Orbs")]
    [Tooltip("Master switch. OFF restores the pre-feature fight exactly (hands + blinks only).")]
    [SerializeField] private bool darkOrbsEnabled = true;
    [Tooltip("Chance a stop becomes an orb emission INSTEAD of that stop's hand attacks. " +
             "The boss then blinks away immediately and resumes the hands from the next " +
             "spot, so the orbs are still chasing while the branches come back.")]
    [Range(0f, 1f)][SerializeField] private float darkOrbChance = 0.4f;
    [Tooltip("Orbs thrown per emission.")]
    [SerializeField] private int darkOrbCount = 3;
    [Tooltip("Damage one orb deals when it pops (before difficulty/stage scaling).")]
    [SerializeField] private float darkOrbDamage = 14f;
    [Tooltip("Blast radius of the pop. Slightly wider than the orb's contact radius so " +
             "the burst covers what it visibly reached.")]
    [SerializeField] private float darkOrbPopRadius = 1.4f;
    [Tooltip("Minimum seconds between emissions. The orbs live ~13s, so this is what " +
             "stops the arena filling up with them.")]
    [SerializeField] private float darkOrbCooldown = 14f;
    [Tooltip("Seconds after the boss spawns before the first emission can happen — the " +
             "fight opens on the hands the player already knows.")]
    [SerializeField] private float darkOrbFirstDelay = 10f;
    [Tooltip("Wind-up: how long the well visibly gathers between his hands before the " +
             "orbs are thrown. This is the tell.")]
    [SerializeField] private float darkOrbEmitDuration = 0.9f;
    [Tooltip("Beat between each orb leaving the well.")]
    [SerializeField] private float darkOrbSpacing = 0.12f;
    [Tooltip("Angular spread of the throw, degrees. They fan out and then curve back in.")]
    [SerializeField] private float darkOrbLaunchSpread = 70f;
    [Tooltip("Extra orbs per emission in phase 2.")]
    [SerializeField] private int phase2ExtraOrbs = 1;
    [Tooltip("Emission chance multiplier in phase 2.")]
    [SerializeField] private float phase2OrbChanceMul = 1.35f;
    [Tooltip("OFF (default): an orb popping next to a tower or the core damages only " +
             "characters. The orbs hunt players, so letting a stray pop chip buildings " +
             "would add tower damage the fight was never tuned for.")]
    [SerializeField] private bool darkOrbsDamageBuildings = false;
    [SerializeField] private Boss3DarkOrb.Settings darkOrbSettings = new Boss3DarkOrb.Settings();

    [Header("Teleport Rift Telegraph")]
    [Tooltip("How long a rift marks the destination BEFORE the boss materialises there, " +
             "so the player can read the blink and pre-position.")]
    [SerializeField] private float riftLeadTime = 0.3f;

    [Header("Tower Corruption (channel) — DISABLED by default")]
    [Tooltip("Chance a tower-targeted stop becomes a corruption channel instead of a strike. " +
             "0 = OFF (the tether/lightning look is disabled; boss only uses the tree-hands).")]
    [Range(0f, 1f)][SerializeField] private float towerCorruptChance = 0f;
    [Tooltip("Channel length. The boss is STATIONARY and takes bonus damage the whole time — " +
             "damage it enough to interrupt and save the tower.")]
    [SerializeField] private float corruptChannelTime = 2.5f;
    [Tooltip("Damage per second inflicted on the tethered tower during the channel.")]
    [SerializeField] private float corruptDamagePerSecond = 60f;
    [Tooltip("Total damage the boss must take during a channel to interrupt it.")]
    [SerializeField] private float corruptInterruptThreshold = 85f;

    [Header("Audio — assign your own FMOD events (empty = silent)")]
    [Tooltip("Master toggle. Even when on, nothing plays unless you assign the events below, " +
             "so the boss is SILENT until you wire up e.g. your Boss3Attack event.")]
    [SerializeField] private bool playAudio = true;
    [Tooltip("Played when a tree-hand attack winds up. Assign your Boss3Attack event here.")]
    [SerializeField] private FMODUnity.EventReference attackSound;
    [Tooltip("Played when a tree-hand strike lands.")]
    [SerializeField] private FMODUnity.EventReference impactSound;
    [Tooltip("Played on each blink/teleport.")]
    [SerializeField] private FMODUnity.EventReference teleportSound;
    [Tooltip("Played when the boss enters phase 2 (armour break).")]
    [SerializeField] private FMODUnity.EventReference phaseSound;
    [Tooltip("Played when the player interrupts a wind-up / the boss destabilises.")]
    [SerializeField] private FMODUnity.EventReference interruptSound;
    [Tooltip("Played when the player destroys an extended tree-hand. Leave empty to " +
             "fall back to the interrupt event; leave both empty for silence.")]
    [SerializeField] private FMODUnity.EventReference handBreakSound;
    [Tooltip("Played as the dark orbs are gathered. Empty falls back to the attack event.")]
    [SerializeField] private FMODUnity.EventReference orbEmitSound;
    [Tooltip("Played when an orb pops on a player. Empty falls back to the impact event.")]
    [SerializeField] private FMODUnity.EventReference orbPopSound;

    // ── runtime ──
    private SpriteRenderer bossSprite;
    private Boss3Visual visual;
    private Transform currentTarget;
    private bool isDying;
    private Coroutine _routine;
    private readonly List<Boss3TreeHand> _activeHands = new List<Boss3TreeHand>();

    // The attack event (e.g. Boss3Branches) is a LOOPING / sustained FMOD event, so it
    // can't go through PlayOneShot — a one-shot is released immediately and there is no
    // handle left to stop it, which is why it used to play forever. It is held here as a
    // tracked instance instead and stopped when the branches land, when the attack is
    // interrupted, or when the boss dies. The token stops a late callback from an OLD
    // volley killing the sound of the NEXT one.
    private FMOD.Studio.EventInstance _attackLoop;
    private int _attackLoopToken;

    private enum TargetKind { Player, Tower, Core }
    private int _cycle;   // rotates the target kind each attack

    // The boss floats/drifts around this anchor every frame; teleport moves the anchor.
    // This is the fix for the old "stands in place" bug: the visual must NOT drive the
    // root transform, so the whole-body float lives here where it can't fight a blink.
    private Vector3 _anchor;
    private bool _anchorSet;
    private float _floatPhase;

    // Phase / punish / channel state.
    private bool _phase2;
    private bool _vulnerable;
    private bool _prone;          // vulnerable AND visibly down (post hand-break)
    private float _proneLerp;     // smoothed 0..1 sag applied to the root transform
    private bool _channeling;
    private float _damageTakenDuringChannel;
    private Boss3CorruptTether _activeTether;   // tracked so death mid-channel can clean it up

    // Hand-attack interrupt window.
    private bool _attackInterruptible;
    private float _attackDamageAccum;
    private bool _lastAttackInterrupted;

    // Destructible hands: how many branches of the CURRENT volley the player has
    // broken. Reset at the start of every PerformHandAttack.
    private int _handsBrokenThisAttack;

    // Earliest Time.time at which another dark-orb emission may happen. Seeded in
    // Start() with darkOrbFirstDelay so the fight opens on the familiar hand attacks.
    private float _nextOrbTime;

    // Damage amplification earned by breaking hands. Accumulates across the whole
    // stop (not just one volley) and is wiped the moment he blinks away, so it rewards
    // pressing the advantage rather than being a permanent debuff.
    private int _handBreakAmpStacks;

    /// Multiplier applied to incoming damage from the hand-break stacks (1 = none).
    private float HandBreakAmpMultiplier =>
        1f + Mathf.Max(0f, handBreakDamageAmpPerStack) * _handBreakAmpStacks;

    // Phase-scaled timing helpers.
    private float SpeedMul => _phase2 ? phase2SpeedMul : 1f;


    protected override void Awake()
    {
        // Match Boss2: pull pools from EnemyData if present, else the serialized fields.
        if (enemyData != null)
        {
            maxHealth = enemyData.maxHealth;
            maxArmor = enemyData.maxArmor;
        }
        else
        {
            maxHealth = bossMaxHealth;
            maxArmor = bossMaxArmor;
            // EnemyStats.Awake only seeds currentHealth from EnemyData; with no asset
            // we seed it here so an inspector edit to bossMaxHealth can't desync it.
            // BaseBossStats.Awake then scales maxHealth AND currentHealth together.
            currentHealth = bossMaxHealth;
        }

        base.Awake();   // BaseBossStats applies difficulty scaling + seeds bossArmor

        if (damageLayers == 0)
            damageLayers = ~LayerMask.GetMask("Enemy");
    }

    protected override void Start()
    {
        // If no world-bar prefab was assigned, borrow one (same convenience as Boss2).
        if (healthBarPrefab == null)
            healthBarPrefab = FindAnyHealthBarPrefab();

        base.Start();   // EnemyStats makes the world bar; BaseBossStats shows the big top bar

        bossSprite = GetComponent<SpriteRenderer>();
        visual = GetComponent<Boss3Visual>();
        if (visual == null) visual = gameObject.AddComponent<Boss3Visual>();

        ConfigureRigidbody();
        ConfigureBossCollider();
        EnsureYSort();
        InitializeBossHealthBar();

        // The orchestrator picks the spawn point, not us, and on an obstacle-heavy
        // layout that can land inside a wall or rock. Nudge to the nearest spot with
        // real clearance BEFORE the anchor is taken, so the float and every later
        // teleport work from a legal position. A no-op when the spawn is already clear.
        Vector3 spawn = NearestPlaceablePoint(transform.position);
        if ((spawn - transform.position).sqrMagnitude > 0.0001f)
        {
            Debug.Log($"[Boss3] Spawn point was obstructed — nudged " +
                      $"{Vector3.Distance(spawn, transform.position):F2}u to clear ground.");
            transform.position = spawn;
        }

        _anchor = transform.position;
        _anchorSet = true;
        _floatPhase = Random.Range(0f, Mathf.PI * 2f);
        _nextOrbTime = Time.time + Mathf.Max(0f, darkOrbFirstDelay);

        _routine = StartCoroutine(BossRoutine());
    }

    private GameObject FindAnyHealthBarPrefab()
    {
        var others = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);
        foreach (var s in others)
            if (s != this && s.healthBarPrefab != null) return s.healthBarPrefab;
        return null;   // fine — the top-of-screen bar works without a world bar
    }

    private void ConfigureRigidbody()
    {
        var rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;
        // Kinematic: the boss teleports and floats by driving its transform directly, so
        // we don't want dynamic physics fighting those writes. Trigger damage intake
        // still works (kinematic body + trigger projectile still raises OnTriggerEnter2D).
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;   // keep trigger intake firing vs kinematic projectiles
        rb.mass = Mathf.Max((enemyData != null) ? enemyData.mass : bossRigidbodyMass, 50f);
        rb.linearDamping = bossLinearDrag;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private void ConfigureBossCollider()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = false;
        col.radius = bossColliderRadius;
        col.offset = new Vector2(0f, bossColliderOffsetY);
    }

    // Boss3 has no EnemyController, so nothing would add a YSortEntity for it. Add one
    // ourselves so the silhouette sorts inside the grass Y-band instead of at order 0
    // (the "invisible under the grass" trap documented on Boss2's summoned slimes).
    private void EnsureYSort()
    {
        const float sortPrecision = 10f;
        const int sortOrderBase = 1000;
        const float sortYOffset = -0.2f;

        var ysort = GetComponent<YSortEntity>();
        if (ysort == null) ysort = gameObject.AddComponent<YSortEntity>();
        ysort.sortPrecision = sortPrecision;
        ysort.sortOrderBase = sortOrderBase;
        ysort.sortYOffset = sortYOffset;

        if (bossSprite != null)
            bossSprite.sortingOrder = sortOrderBase +
                Mathf.RoundToInt(-(transform.position.y + sortYOffset) * sortPrecision);
    }

    private void InitializeBossHealthBar()
    {
        if (HealthBar == null) return;   // no world bar assigned/borrowed — top bar still shows

        HealthBar.Initialize(transform, maxHealth + maxArmor);

        healthBarYOffset = healthBarExtraYPadding;
        if (bossSprite != null && bossSprite.sprite != null)
        {
            float spriteTopWorld = bossSprite.bounds.max.y;
            healthBarYOffset = (spriteTopWorld - transform.position.y) + healthBarExtraYPadding - healthBarYReduction;
        }
        HealthBar.SetOffset(new Vector3(0f, healthBarYOffset, 0f));

        const int HEALTH_BAR_SORTING_ORDER = 4000;
        Canvas canvas = HealthBar.GetComponentInChildren<Canvas>(true);
        if (canvas == null) canvas = HealthBar.GetComponentInParent<Canvas>();
        if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = HEALTH_BAR_SORTING_ORDER; }
    }

    // Boss3's art is fully procedural, so there are no sprite folders to warm. The
    // silhouette/glow textures are built on first use and cached statically; touch them
    // here so the very first spawn doesn't pay the build cost mid-combat.
    public void PrewarmSpriteFolders()
    {
        var _ = Boss3Sprites.SoftDot;
        var __ = Boss3Sprites.Spark;
    }

    // Gentle underwater float around the anchor. Owned here (not the visual) so it moves
    // the whole boss without ever resetting the transform back over a teleport.
    private void Update()
    {
        if (isDying || !_anchorSet) return;

        // Prone sag. Boss3 owns the root transform (the visual is forbidden from
        // touching it), so the "he's down" read has to be applied here, folded into the
        // same float that already drives the body. Smoothed both ways so he slumps and
        // then hauls himself back up rather than snapping.
        _proneLerp = Mathf.MoveTowards(_proneLerp, _prone ? 1f : 0f,
                                       Time.deltaTime * (_prone ? 3.5f : 1.6f));

        float t = Time.time + _floatPhase;
        // The float all but stops while he's down — a boss still bobbing serenely does
        // not read as vulnerable.
        float damp = 1f - _proneLerp * 0.85f;
        float bob = Mathf.Sin(t * floatBobSpeed) * floatBobAmplitude * damp;
        float sway = Mathf.Sin(t * floatSwaySpeed * 0.9f) * floatSwayAmplitude * damp;

        // A slow, heavy heave on top of the sag, like laboured breathing.
        float heave = _proneLerp * Mathf.Sin(t * 2.4f) * 0.05f;

        transform.position = _anchor
            + new Vector3(sway, bob - proneSagAmount * _proneLerp + heave, 0f);

        if (_attackLoop.isValid())
            _attackLoop.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
    }


    private IEnumerator BossRoutine()
    {
        // Let Start() finish on every component (visual build, bars, etc.).
        yield return null;

        while (!isDying)
        {
            // Respect a parry-stun if the game's parry system ever applies one to us:
            // freeze, and be vulnerable while frozen (a free punish for the player).
            if (IsParryStunned())
            {
                SetVulnerable(true);
                while (IsParryStunned() && !isDying) yield return null;
                SetVulnerable(false);
                continue;
            }

            FindTarget(NextTargetKind());
            if (currentTarget == null)
            {
                yield return TeleportTo(ChooseRandomPoint());
                yield return WaitScaled(0.6f);
                continue;
            }

            bool targetIsTower = IsTower(currentTarget);
            bool targetIsPlayer = IsPlayer(currentTarget);

            Vector3 dest = (Random.value < randomBlinkChance)
                ? ChooseRandomPoint()
                : ChooseStandoffPoint(currentTarget.position);

            yield return TeleportTo(dest);
            yield return WaitScaled(postTeleportPause);
            if (isDying) yield break;

            // ── choose what to do at this spot ──
            // Dark orbs. Seeds a handful of slow chasers and then falls straight through
            // to the top of the loop, which blinks him somewhere else and resumes the
            // hand attacks — so the player is dodging branches AND being hunted at the
            // same time. Deliberately REPLACES this stop's attacks rather than adding to
            // them: the hands are unchanged, there are just fewer volleys in the runs
            // where he throws orbs instead.
            if (ShouldEmitDarkOrbs())
            {
                yield return PerformDarkOrbs();
                yield return WaitScaled(attackRecover);
                continue;
            }

            // Tower stop → sometimes a corruption channel (defend-your-build moment).
            if (targetIsTower && currentTarget != null && Random.value < towerCorruptChance)
            {
                yield return PerformTowerCorruption(currentTarget);
                // The channel has its own vulnerability handling + ending, then recover.
                yield return WaitScaled(attackRecover);
                continue;
            }

            // Otherwise a burst of attacks from this spot (dwell so the player can hit back).
            bool didNormalSequence = false;
            int attacks = Mathf.Max(1, attacksPerTeleport);
            for (int a = 0; a < attacks && !isDying; a++)
            {
                if (a > 0) FindTarget(NextTargetKind());

                // Last line of defence for the branch counterplay. The blink already
                // stands him off, but the mid-stop retarget above can swing onto a
                // building he happens to be parked next to — and a stub branch thrown
                // from point-blank cannot be broken. Prefer a target with room; if
                // there isn't one, stop attacking from here and blink instead.
                EnsureStrikeRoom();

                if (currentTarget == null) break;
                if (IsParryStunned()) break;

                // Radial root-burst mix-up (forces movement instead of a side-step),
                // biased to when the player is the target and more likely in phase 2.
                bool useBurst = IsPlayer(currentTarget) &&
                                Random.value < (rootBurstChance * (_phase2 ? 1.4f : 1f));

                if (useBurst) yield return PerformRootBurst();
                else yield return PerformHandAttack(currentTarget);

                didNormalSequence = true;

                // If the player interrupted the wind-up, that already fizzled the attack
                // AND ran a punish window — stop attacking from this spot and blink away.
                if (_lastAttackInterrupted) break;

                if (a < attacks - 1) yield return WaitScaled(betweenAttackPause);
            }

            // ── punish window ──
            // Skip if an interrupt already destabilised the boss this stop (no double-punish).
            if (didNormalSequence && !isDying && !_lastAttackInterrupted &&
                (Random.value < destabilizeChance || _phase2))
            {
                yield return DestabilizeWindow();
            }

            yield return WaitScaled(attackRecover);
        }
    }

    // Wait that scales with phase (phase 2 is more relentless). Unscaled-safe via realtime? No —
    // combat uses Time.deltaTime elsewhere, so keep normal WaitForSeconds semantics.
    private IEnumerator WaitScaled(float seconds)
    {
        float t = 0f;
        float dur = seconds * SpeedMul;
        while (t < dur && !isDying) { t += Time.deltaTime; yield return null; }
    }

    private bool IsParryStunned()
    {
        var ps = GetComponent<ParryStunEffect>();
        return ps != null && ps.IsStunActive;
    }

    // ─ punish window 
    // The boss stops, tears with glitch, and takes bonus damage: the reward for
    // surviving its attack string. This is the heart of the "threat → dodge → punish"
    // loop that makes the fight feel fair and skilful.
    /// <param name="duration">Seconds to stay exposed. Defaults to destabilizeDuration.</param>
    /// <param name="prone">When true the boss also SAGS and stops floating — the heavy
    /// "he's down, go hit him" read used after a hand is destroyed.</param>
    private IEnumerator DestabilizeWindow(float duration = -1f, bool prone = false)
    {
        float dur = duration > 0f ? duration : destabilizeDuration;

        SetVulnerable(true, prone);
        if (visual != null) visual.Glitch(1f);
        TryPlay(interruptSound);

        float t = 0f;
        while (t < dur && !isDying)
        {
            // Keep a jittery, unstable read the whole time.
            if (visual != null && Random.value < 0.15f) visual.Glitch(Random.Range(0.4f, 0.8f));
            t += Time.deltaTime;
            yield return null;
        }
        SetVulnerable(false);
    }

    private void SetVulnerable(bool on, bool prone = false)
    {
        _vulnerable = on;
        _prone = on && prone;
        if (visual != null) visual.SetVulnerable(on);
    }

    private TargetKind NextTargetKind()
    {
        _cycle = (_cycle + 1) % 3;
        return (TargetKind)_cycle;
    }

    // Resolve the requested kind, falling back through the others so the boss always
    // has SOMETHING to do even if (say) all towers are dead.
    private void FindTarget(TargetKind preferred)
    {
        for (int i = 0; i < 3; i++)
        {
            TargetKind kind = (TargetKind)(((int)preferred + i) % 3);
            Transform t = ResolveTarget(kind);
            if (t != null) { currentTarget = t; return; }
        }
        currentTarget = null;
    }

    private Transform ResolveTarget(TargetKind kind)
    {
        switch (kind)
        {
            case TargetKind.Player:
                if (!PlayerCloakEffect.IsActive)
                {
                    var p = PlayerRegistry.Instance != null
                        ? PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true)
                        : null;
                    if (p != null) return p.transform;
                }
                return null;

            case TargetKind.Tower:
                return NearestTower();

            case TargetKind.Core:
                var core = GameObject.FindGameObjectWithTag("Core");
                return core != null ? core.transform : null;
        }
        return null;
    }

    /// True if a branch thrown at `t` would be long enough to be a real target in its
    /// own right. Players are always true: they pick their own distance, and a player
    /// who walks into the boss's face has made that trade knowingly. Buildings cannot
    /// move, so a boss parked against one would throw a stub nobody can break.
    private bool HasStrikeRoom(Transform t)
    {
        if (t == null) return false;
        if (IsPlayer(t)) return true;

        float sign = (t.position.x >= transform.position.x) ? 1f : -1f;
        Vector3 origin = visual != null ? visual.HandOrigin(sign) : transform.position;
        float min = Mathf.Max(0f, minBranchLength);
        return (t.position - origin).sqrMagnitude >= min * min;
    }

    /// Swap off a target that is too close to strike properly. Tries the other target
    /// kinds first — the player is usually elsewhere and perfectly strikeable — and
    /// only gives up (null) if nothing has room, which makes the caller stop attacking
    /// from this spot and blink away instead.
    private void EnsureStrikeRoom()
    {
        if (currentTarget == null || HasStrikeRoom(currentTarget)) return;

        Transform original = currentTarget;
        for (int i = 1; i <= 2; i++)
        {
            Transform t = ResolveTarget((TargetKind)((_cycle + i) % 3));
            if (t != null && t != original && HasStrikeRoom(t))
            {
                currentTarget = t;
                return;
            }
        }

        currentTarget = null;
    }

    private Transform NearestTower()
    {
        var towers = Tower.ActiveTowers;
        if (towers == null) return null;
        Transform best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < towers.Count; i++)
        {
            var tw = towers[i];
            if (tw == null) continue;
            float d = ((Vector2)tw.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = tw.transform; }
        }
        return best;
    }

    //  teleport 

    private IEnumerator TeleportTo(Vector3 dest)
    {
        if (isDying) yield break;

        // Face + dissolve out with a glitch burst.
        if (visual != null) visual.Glitch(1f);
        SpawnBlinkFlash(transform.position);
        TryPlay(teleportSound);

        // Spawn a RIFT at the destination NOW, so the player can read where the boss is
        // about to appear and pre-position. This turns the teleport into counterplay
        // rather than pure randomness.
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        float lead = Mathf.Max(0f, riftLeadTime * SpeedMul);
        Boss3RiftMarker rift = Boss3RiftMarker.Spawn(dest, lead + teleportOutDuration * SpeedMul, layer);

        float outDur = teleportOutDuration * SpeedMul;
        float t = 0f;
        while (t < outDur)
        {
            t += Time.deltaTime;
            if (visual != null) visual.Reveal = 1f - Mathf.Clamp01(t / outDur);
            yield return null;
        }
        if (visual != null) visual.Reveal = 0f;

        // Hold invisible at the rift for the remaining lead so the tell has time to read.
        if (lead > 0f) yield return new WaitForSeconds(lead);

        // Blink.
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        transform.position = dest;
        _anchor = dest;                 // float around the NEW spot, not the old one

        // He shakes off the hand-break damage amplification as he reforms — the window
        // to cash it in is the prone + recovery period, not the rest of the fight.
        _handBreakAmpStacks = 0;
        FaceToward(currentTarget != null ? currentTarget.position : dest + Vector3.right);
        EnsureYSort();

        // Reform in.
        if (rift != null) rift.Pop();   // rift bursts as the boss materialises
        SpawnBlinkFlash(visual != null ? visual.BodyCentre() : dest);
        if (visual != null) visual.Glitch(1f);

        float inDur = teleportInDuration * SpeedMul;
        t = 0f;
        while (t < inDur)
        {
            t += Time.deltaTime;
            if (visual != null) visual.Reveal = Mathf.Clamp01(t / inDur);
            yield return null;
        }
        if (visual != null) visual.Reveal = 1f;
    }

    private void FaceToward(Vector3 worldPos)
    {
        float sign = worldPos.x >= transform.position.x ? 1f : -1f;
        if (visual != null) visual.SetFacing(sign);
        if (bossSprite != null) bossSprite.flipX = sign < 0f;
    }

    private Vector3 ChooseStandoffPoint(Vector3 targetPos)
    {
        // Score candidates instead of taking the first in-bounds one, so a spot that is
        // merely legal never wins over one that is actually clear. Previously this
        // returned the first InBounds() hit, which is why the boss could materialise
        // standing inside a layout wall or rock.
        RefreshStructures();

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;

        // Second-choice pile: spots that are physically fine but sit too close to a
        // building. Only used if NOTHING respects the standoff, so a map packed wall to
        // wall with towers still gets a blink instead of freezing the fight.
        Vector3 relaxed = Vector3.zero;
        float relaxedScore = float.NegativeInfinity;

        int attempts = Mathf.Max(4, teleportPlacementAttempts);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float dist = Random.Range(teleportStandoffMin, teleportStandoffMax);
            Vector3 p = targetPos + (Vector3)(dir * dist);
            if (!IsPlaceable(p)) continue;

            float score = PlacementClearance(p);

            // Keeping clear of the target itself is not enough: a DIFFERENT tower can
            // be sitting exactly where we were about to land, and the mid-stop retarget
            // would then throw an unbreakable stub at it.
            if (!RespectsStructureStandoff(p))
            {
                if (score > relaxedScore) { relaxedScore = score; relaxed = p; }
                continue;
            }

            if (score >= teleportObstacleClearance) return p;   // good enough, stop early
            if (score > bestScore) { bestScore = score; best = p; }
        }

        if (bestScore > float.NegativeInfinity) return best;
        if (relaxedScore > float.NegativeInfinity) return relaxed;

        // Nothing clear anywhere — fall back to the old behaviour rather than refusing
        // to teleport, which would freeze the fight. The distance is pushed out to the
        // standoff minimum so even this last resort is not point-blank.
        float fallbackDist = Mathf.Max(teleportStandoffMin, minStructureStandoff);
        Vector3 fb = targetPos + (Vector3)(Random.insideUnitCircle.normalized * fallbackDist);
        return ClampToBounds(fb);
    }

    private Vector3 ChooseRandomPoint()
    {
        Vector3 c = transform.position;
        var core = GameObject.FindGameObjectWithTag("Core");
        if (core != null) c = core.transform.position;

        RefreshStructures();

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        Vector3 relaxed = Vector3.zero;
        float relaxedScore = float.NegativeInfinity;

        float minR = Mathf.Max(1f, minStructureStandoff);
        float maxR = Mathf.Max(minR + 1f, teleportStandoffMax + 6f);

        int attempts = Mathf.Max(4, teleportPlacementAttempts);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            // BUG FIX: this used `Random.insideUnitCircle * radius`, whose magnitude is
            // anywhere in [0, radius] — so a random blink could put the boss ON the
            // core. He would then throw a zero-length branch at whatever he retargeted
            // and the player had nothing to shoot. Now it samples a proper ANNULUS
            // starting at the standoff distance.
            Vector2 off = Random.insideUnitCircle.normalized * Random.Range(minR, maxR);
            Vector3 p = c + (Vector3)off;
            if (!IsPlaceable(p)) continue;

            float score = PlacementClearance(p);

            // Away from the core is not away from the TOWERS.
            if (!RespectsStructureStandoff(p))
            {
                if (score > relaxedScore) { relaxedScore = score; relaxed = p; }
                continue;
            }

            if (score >= teleportObstacleClearance) return p;
            if (score > bestScore) { bestScore = score; best = p; }
        }

        if (bestScore > float.NegativeInfinity) return best;
        if (relaxedScore > float.NegativeInfinity) return relaxed;
        return ClampToBounds(c + (Vector3)(Random.insideUnitCircle.normalized * minR));
    }

    // ── blink placement validity ─────────────────────────────────────────────

    // Cached once; the map is a scene singleton that outlives the boss.
    private TowerDefenseMap _map;
    private bool _mapSearched;

    private TowerDefenseMap Map
    {
        get
        {
            if (!_mapSearched)
            {
                _mapSearched = true;
                _map = FindFirstObjectByType<TowerDefenseMap>();
            }
            return _map;
        }
    }

    /// Inside the arena box, and (optionally) inside the map's playable circle.
    private bool IsPlaceable(Vector3 p)
    {
        if (!InBounds(p)) return false;

        if (clampTeleportToMapRadius)
        {
            var map = Map;
            if (map != null && map.mapRadius > 0f)
            {
                float limit = Mathf.Max(1f, map.mapRadius - mapEdgeMargin);
                if (((Vector2)p - (Vector2)map.transform.position).sqrMagnitude > limit * limit)
                    return false;
            }
        }
        return true;
    }

    // ── structure standoff ───────────────────────────────────────────────────
    // Blink spots are kept clear of the core and the towers. Rebuilt once per blink
    // rather than queried per candidate: a placement search tries up to 32 spots, and
    // walking every tower's collider hierarchy that many times is pointless when the
    // buildings do not move between candidates.
    private readonly List<Vector2> _structPos = new List<Vector2>();
    private readonly List<float> _structRadius = new List<float>();

    private void RefreshStructures()
    {
        _structPos.Clear();
        _structRadius.Clear();

        var towers = Tower.ActiveTowers;
        if (towers != null)
        {
            for (int i = 0; i < towers.Count; i++)
            {
                var tw = towers[i];
                if (tw == null) continue;
                _structPos.Add(tw.transform.position);
                _structRadius.Add(StructureRadius(tw.gameObject));
            }
        }

        var core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
        {
            _structPos.Add(core.transform.position);
            _structRadius.Add(StructureRadius(core));
        }
    }

    // Footprint radius, so the standoff is measured from a building's EDGE. The core is
    // a big object — measuring from its centre would let the boss stand on its rim and
    // still pass a centre-distance test.
    private static float StructureRadius(GameObject go)
    {
        if (go == null) return 0f;
        var col = go.GetComponentInChildren<Collider2D>();
        if (col == null) return 0.75f;
        Vector3 e = col.bounds.extents;
        return Mathf.Max(e.x, e.y);
    }

    /// Distance from `p` to the nearest building's edge. Positive infinity when there
    /// are no buildings, so the check is a no-op on an empty map.
    private float StructureClearance(Vector3 p)
    {
        float best = float.PositiveInfinity;
        Vector2 q = p;
        for (int i = 0; i < _structPos.Count; i++)
        {
            float d = Vector2.Distance(_structPos[i], q) - _structRadius[i];
            if (d < best) best = d;
        }
        return best;
    }

    private bool RespectsStructureStandoff(Vector3 p) =>
        StructureClearance(p) >= minStructureStandoff;

    /// Distance from `p` to the nearest solid obstacle, capped at the clearance we
    /// actually care about. Used to RANK candidates, so even a cramped map still yields
    /// the roomiest spot available rather than a random wall.
    private float PlacementClearance(Vector3 p)
    {
        int mask = ResolveTeleportBlockMask();
        if (mask == 0) return teleportObstacleClearance;   // nothing to avoid

        float want = Mathf.Max(0.1f, teleportObstacleClearance);
        var hit = Physics2D.OverlapCircle(p, want, mask);
        if (hit == null) return want;                      // fully clear

        Vector2 closest = hit.ClosestPoint(p);
        return Vector2.Distance(closest, p);
    }

    // Resolved lazily so an unset LayerMask picks up the map's own obstacle layer name
    // rather than silently avoiding nothing.
    private int _resolvedBlockMask = -1;
    private int ResolveTeleportBlockMask()
    {
        if (_resolvedBlockMask >= 0) return _resolvedBlockMask;

        if (teleportBlockLayers.value != 0)
        {
            _resolvedBlockMask = teleportBlockLayers.value;
            return _resolvedBlockMask;
        }

        var map = Map;
        string layerName = (map != null && !string.IsNullOrEmpty(map.obstacleLayerName))
            ? map.obstacleLayerName : "Obstacle";

        int idx = LayerMask.NameToLayer(layerName);
        _resolvedBlockMask = idx >= 0 ? (1 << idx) : 0;

        if (_resolvedBlockMask == 0)
            Debug.LogWarning($"[Boss3] No '{layerName}' layer found — blink spots will not " +
                             "avoid obstacles. Assign Teleport Block Layers explicitly.");
        return _resolvedBlockMask;
    }

    /// Walk outward from `origin` until a spot with real clearance is found. Used for
    /// the SPAWN position, which Boss3 doesn't choose — the orchestrator drops him
    /// wherever, and on an obstacle-heavy layout that could be inside a wall.
    private Vector3 NearestPlaceablePoint(Vector3 origin)
    {
        RefreshStructures();

        bool originOk = IsPlaceable(origin) && RespectsStructureStandoff(origin);
        float originScore = originOk ? PlacementClearance(origin) : float.NegativeInfinity;
        if (originOk && originScore >= teleportObstacleClearance) return origin;

        Vector3 best = origin;
        float bestScore = originScore;

        const int RINGS = 6;
        const int SAMPLES = 12;
        float step = Mathf.Max(0.75f, teleportObstacleClearance);

        for (int r = 1; r <= RINGS; r++)
        {
            float radius = step * r;
            // Offset each ring so samples don't line up radially and miss narrow gaps.
            float phase = r * (Mathf.PI * 2f / (SAMPLES * 2f));
            for (int s = 0; s < SAMPLES; s++)
            {
                float a = phase + s * (Mathf.PI * 2f / SAMPLES);
                Vector3 p = origin + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                if (!IsPlaceable(p)) continue;
                // The orchestrator can drop him anywhere, including on the core itself.
                if (!RespectsStructureStandoff(p)) continue;

                float score = PlacementClearance(p);
                if (score >= teleportObstacleClearance) return p;
                if (score > bestScore) { bestScore = score; best = p; }
            }
        }
        return best;
    }

    private bool InBounds(Vector3 p) =>
        p.x >= mapBoundsMin && p.x <= mapBoundsMax && p.y >= mapBoundsMin && p.y <= mapBoundsMax;

    private Vector3 ClampToBounds(Vector3 p) => new Vector3(
        Mathf.Clamp(p.x, mapBoundsMin, mapBoundsMax),
        Mathf.Clamp(p.y, mapBoundsMin, mapBoundsMax), 0f);

    // A quick chromatic ring/flash marking a blink endpoint.
    private void SpawnBlinkFlash(Vector3 pos)
    {
        var go = new GameObject("Boss3_BlinkFlash");
        go.transform.position = pos;
        var fx = go.AddComponent<Boss3BlinkFlash>();
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        fx.Play(layer);
    }

    //  attack 

    private IEnumerator PerformHandAttack(Transform target)
    {
        if (target == null) yield break;

        bool isPlayer = IsPlayer(target);

        // Co-op aware: when targeting players, gather up to two nearest so the boss can
        // split its hands across both and can't be trivially tanked by one player.
        var lockPoints = new List<Vector3>();
        if (isPlayer)
        {
            foreach (var ps in AlivePlayersNearest(2))
                lockPoints.Add(ps.transform.position);
        }
        if (lockPoints.Count == 0) lockPoints.Add(target.position);

        FaceToward(lockPoints[0]);

        int baseHands = isPlayer ? Mathf.Max(1, playerHandCount) : 1;
        int hands = baseHands + (isPlayer && _phase2 ? Mathf.Max(0, phase2ExtraHands) : 0);

        float sign = (lockPoints[0].x >= transform.position.x) ? 1f : -1f;
        var settings = ScaledHandSettings();

        // Plays for as long as the branches are extending. Stopped by whichever comes
        // first: the LAST branch of the volley landing on its target, an interrupt, or
        // death. (A safety stop at the end of this coroutine covers anything else.)
        int loopToken = StartAttackLoop(attackSound);
        int pendingImpacts = hands;

        _lastAttackInterrupted = false;
        _handsBrokenThisAttack = 0;
        var volley = new List<Boss3TreeHand>();

        for (int i = 0; i < hands; i++)
        {
            float handSign = (i % 2 == 0) ? sign : -sign;

            // Never throw a branch from an arm that is still growing back — it would
            // sprout from the middle of the stump. Use the intact arm instead.
            if (visual != null && visual.IsArmBroken(handSign) && !visual.IsArmBroken(-handSign))
                handSign = -handSign;

            Vector3 origin = visual != null ? visual.HandOrigin(handSign)
                                            : transform.position + Vector3.up * 1.0f;

            // Round-robin across the (one or two) target points.
            Vector3 baseLock = lockPoints[i % lockPoints.Count];
            Vector3 tip = baseLock;

            // Spread hands that share a target point so there's a gap to slip through.
            int perPoint = Mathf.CeilToInt(hands / (float)lockPoints.Count);
            if (perPoint > 1)
            {
                int localIdx = i / lockPoints.Count;
                float o = (localIdx - (perPoint - 1) * 0.5f) * playerHandSpread;
                Vector3 perp = Vector3.Cross((baseLock - origin).normalized, Vector3.forward);
                tip = baseLock + perp * o;
            }

            Vector3 dir = tip - origin;
            if (dir.magnitude > attackReach) tip = origin + dir.normalized * attackReach;

            if (visual != null) visual.AimArm(handSign, tip);

            float capturedSign = handSign;
            System.Func<Vector3> originProvider = () =>
                visual != null ? visual.HandOrigin(capturedSign) : origin;

            bool playStrike = (i == 0);   // one strike cue per volley, not per hand
            string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
            var hand = Boss3TreeHand.Spawn(originProvider, tip, settings, layer,
                impactPos =>
                {
                    ApplyHandDamage(impactPos, handImpactRadius, handDamage);
                    if (playStrike) TryPlay(impactSound);

                    // Hands are staggered slightly, so wait for the final one to land
                    // before cutting the branch sound.
                    pendingImpacts--;
                    if (pendingImpacts <= 0) StopAttackLoop(loopToken);
                },
                onBroken: OnHandDestroyed,
                // The hitbox sits on the boss's own physics layer, so the project's
                // existing collision matrix (player shots vs enemies) already covers it.
                hurtboxLayer: handsAreDestructible ? gameObject.layer : -1,
                towerShotsCanBreak: towerShotsCanBreakHands,
                armSign: handSign);
            _activeHands.Add(hand);
            volley.Add(hand);

            if (hands > 1) yield return new WaitForSeconds(0.06f);
        }

        // ── interruptible wind-up ──
        // During the telegraph the player can burst the boss to cancel the whole attack.
        // The hands are still telegraphing (thin/faint at the locked point) and haven't
        // struck yet, so cancelling here fizzles them with no damage dealt.
        bool interrupted = false;
        if (handAttackInterruptible)
        {
            _attackInterruptible = true;
            _attackDamageAccum = 0f;
            if (visual != null) visual.SetCharging(true);
        }

        // The interruptible window is the ENTIRE approach — lock-on plus the long crawl.
        // That is the point of the rework: the player has seconds to break the branch,
        // not a reaction-test sliver.
        float tele = settings.telegraphDuration + settings.crawlDuration;
        float t = 0f;
        while (t < tele && !isDying)
        {
            if (ShouldInterruptAttack()) { interrupted = true; break; }
            t += Time.deltaTime;
            yield return null;
        }

        _attackInterruptible = false;
        if (visual != null) visual.SetCharging(false);

        // Not interrupted during the wind-up — let the strike + hold + retract play out.
        //
        // The interrupt check CONTINUES through this window, because a branch stays
        // destroyable right up until its own impact fires. Cutting a hand down inside
        // that last snap still robs it of its hit, and the player has earned the same
        // payoff they'd get from a wind-up interrupt. Only a destroyed hand can flip the
        // check here — the damage-threshold half is already switched off above.
        if (!interrupted)
        {
            float rest = settings.strikeDuration + settings.holdDuration + settings.retractDuration + 0.1f;
            t = 0f;
            while (t < rest && !isDying)
            {
                if (ShouldInterruptAttack()) { interrupted = true; break; }
                t += Time.deltaTime;
                yield return null;
            }
        }

        if (interrupted)
        {
            // Fizzle every surviving hand — the reward for the burst / for cutting a
            // branch down. Cancel() on a branch that already landed just retracts it,
            // so this is safe for a late interrupt too.
            foreach (var h in volley) if (h != null) h.Cancel();
            _activeHands.RemoveAll(h => h == null);
            _lastAttackInterrupted = true;

            // Player cut the attack short — the branches are no longer extending.
            StopAttackLoop(loopToken);

            if (visual != null) { visual.Glitch(1f); visual.RelaxArms(); }
            if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.25f, 0.16f);

            // Breaking a HAND is the headline counterplay, so it buys the long prone
            // window (handBreakProneDuration). Bursting the boss down mid-wind-up is the
            // older, cheaper route and keeps its original short stagger.
            bool byHandBreak = _handsBrokenThisAttack > 0;
            yield return DestabilizeWindow(byHandBreak ? handBreakProneDuration : destabilizeDuration,
                                           prone: byHandBreak);
            yield break;
        }

        // Safety net: normally already stopped by the final impact above.
        StopAttackLoop(loopToken);

        if (visual != null) visual.RelaxArms();
        _activeHands.RemoveAll(h => h == null);
    }

    /// Should the in-flight tree-hand volley be cancelled? Two independent routes:
    ///   • the player burst the BOSS hard enough during the wind-up (the pre-existing
    ///     handInterruptThreshold mechanic), or
    ///   • the player destroyed enough of the extended HANDS themselves.
    private bool ShouldInterruptAttack()
    {
        if (_attackInterruptible && _attackDamageAccum >= handInterruptThreshold)
            return true;

        return handsAreDestructible &&
               _handsBrokenThisAttack >= Mathf.Max(1, handsBrokenToInterrupt);
    }

    /// A tree-hand was cut down mid-attack. Called from Boss3TreeHand.Shatter() while
    /// the branch is still a valid reference but is already committed to dying, so this
    /// must not touch the hand beyond identifying it.
    ///
    /// This only records + reacts. The actual interrupt is picked up by the attack
    /// coroutine's ShouldInterruptAttack() poll, which keeps every cancellation flowing
    /// through the one code path that already knows how to fizzle a volley and stagger.
    private void OnHandDestroyed(Boss3TreeHand hand)
    {
        if (isDying) return;

        _handsBrokenThisAttack++;
        _activeHands.Remove(hand);

        // Each broken hand makes him take more damage until he blinks out. Capped so a
        // long stationary phase can't spiral into a one-shot.
        _handBreakAmpStacks = Mathf.Min(Mathf.Max(0, handBreakDamageAmpMaxStacks),
                                        _handBreakAmpStacks + 1);

        // Sever the matching arm — it visibly snaps back to a stump and knits over
        // armRegrowDuration, so "the hand grew back" is readable before the next volley.
        if (visual != null)
        {
            visual.BreakArm(hand != null ? hand.ArmSign : 1f, armRegrowDuration);
            visual.Glitch(1f);
        }

        TryPlay(handBreakSound.IsNull ? interruptSound : handBreakSound);
        // A heavier hit than a normal stagger — this is the fight's big moment.
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.35f, 0.22f);
    }

    // Always returns a COPY now, because the destructible fields below are derived from
    // Boss3's own inspector values and must not be written back into the serialized
    // `handSettings` asset. (It used to return the shared instance when not in phase 2.)
    private Boss3TreeHand.Settings ScaledHandSettings()
    {
        var s = handSettings.Clone();

        // Boss3 owns the approach timing now, so the crawl rework can't be silently
        // undone by a stale telegraphDuration serialized on the prefab.
        s.telegraphDuration = Mathf.Max(0.05f, handLockOnDuration);
        s.crawlDuration = Mathf.Max(0.1f, handCrawlDuration);

        // Phase 2 tightens the WHOLE approach, not just the lock-on — otherwise the
        // enraged phase would still hand the player the same long break window.
        if (_phase2)
        {
            s.telegraphDuration *= phase2TelegraphMul;
            s.crawlDuration *= phase2TelegraphMul;
        }

        s.destructible = handsAreDestructible;
        s.breakByHitCount = handBreakByHitCount;
        s.hitsToBreak = Mathf.Max(1, handHitsToBreak);
        s.handHealth = Mathf.Max(1f, handHealth);
        s.hitboxRadius = Mathf.Max(0.05f, handHitboxRadius);
        s.hitboxCount = Mathf.Max(1, handHitboxSpots);
        s.hitboxStartFraction = Mathf.Clamp(handHitboxStartFraction, 0f, 0.6f);
        s.debugLog = handDebugLog;
        return s;
    }

    private List<CharacterStats> AlivePlayersNearest(int maxCount)
    {
        var result = new List<CharacterStats>();
        if (PlayerRegistry.Instance == null) return result;
        foreach (var ps in PlayerRegistry.Instance.AllAliveInRadius(transform.position, 9999f))
            if (ps != null && !result.Contains(ps)) result.Add(ps);
        result.Sort((a, b) =>
            ((Vector2)a.transform.position - (Vector2)transform.position).sqrMagnitude
            .CompareTo(((Vector2)b.transform.position - (Vector2)transform.position).sqrMagnitude));
        if (result.Count > maxCount) result.RemoveRange(maxCount, result.Count - maxCount);
        return result;
    }

    //  second attack: radial root burst 
    // Thorned branches erupt from the ground in a ring around the boss, with ONE safe
    // gap. The player can't just side-step — they must read the gap and run to it. This
    // adds movement pressure the single tree-hand can't, and reuses the branch visuals.
    private IEnumerator PerformRootBurst()
    {
        if (visual != null) visual.Glitch(0.8f);
        int loopToken = StartAttackLoop(attackSound);

        Vector3 centre = visual != null ? visual.BodyCentre() : transform.position;
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        int spikes = Mathf.Max(3, rootBurstSpikes);
        float gapHalf = Mathf.Clamp(rootBurstGapDegrees, 0f, 200f) * 0.5f;
        // Orient the safe gap toward where the player currently is NOT — i.e. a random
        // direction, then remove spikes that fall inside the gap so a clear lane exists.
        float gapCentre = Random.Range(0f, 360f);
        float telegraph = rootBurstTelegraph * (_phase2 ? phase2TelegraphMul : 1f);

        var spikeObjs = new List<Boss3RootSpike>();
        for (int i = 0; i < spikes; i++)
        {
            float ang = (360f / spikes) * i;
            float delta = Mathf.Abs(Mathf.DeltaAngle(ang, gapCentre));
            if (delta < gapHalf) continue;   // leave the safe lane open

            float rad = ang * Mathf.Deg2Rad;
            Vector3 spot = centre + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * rootBurstRadius;
            spot = ClampToBounds(spot);

            var spike = Boss3RootSpike.Spawn(spot, telegraph, handSettings, layer,
                impactPos => ApplyHandDamage(impactPos, handImpactRadius, rootBurstDamage));
            spikeObjs.Add(spike);
        }

        // Wait telegraph + strike; the spikes fire themselves on their own clocks.
        float total = telegraph + 0.5f;
        float t = 0f;
        bool struck = false;
        while (t < total && !isDying)
        {
            if (!struck && t >= telegraph)
            {
                struck = true;
                StopAttackLoop(loopToken);   // spikes have erupted
                TryPlay(impactSound);
                if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.22f, 0.14f);
            }
            t += Time.deltaTime;
            yield return null;
        }
        StopAttackLoop(loopToken);
    }

    //  third attack: dark orbs 
    // He gathers a well of dark matter between both hands, throws it out as several
    // slow orbs that hunt the players, and then blinks away and goes back to the tree
    // hands. The orbs are pure movement pressure — much slower than a walking player,
    // so they are always avoidable, but they do not expire quickly and they follow you
    // through the next few hand volleys.

    private bool ShouldEmitDarkOrbs()
    {
        if (!darkOrbsEnabled || isDying) return false;
        if (darkOrbCount <= 0 || darkOrbChance <= 0f) return false;
        if (Time.time < _nextOrbTime) return false;

        // Nobody to hunt (all dead / downed / cloaked) — do the normal attacks instead
        // rather than wasting the stop on orbs with nothing to chase.
        if (PlayerRegistry.Instance == null) return false;
        if (PlayerRegistry.Instance.NearestAlive(transform.position) == null) return false;

        float chance = darkOrbChance * (_phase2 ? Mathf.Max(0f, phase2OrbChanceMul) : 1f);
        return Random.value < chance;
    }

    private IEnumerator PerformDarkOrbs()
    {
        // Claim the cooldown up front, so an emission cut short by death or a parry
        // stun still can't be immediately retried.
        _nextOrbTime = Time.time + Mathf.Max(1f, darkOrbCooldown) * SpeedMul;

        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        // Face whoever is nearest so the wind-up reads as aimed at the team.
        var nearest = PlayerRegistry.Instance != null
            ? PlayerRegistry.Instance.NearestAlive(transform.position) : null;
        Vector3 facePoint = nearest != null ? nearest.transform.position
                                            : transform.position + Vector3.right;
        FaceToward(facePoint);
        float faceSign = (facePoint.x >= transform.position.x) ? 1f : -1f;

        // The well forms in FRONT of his chest, and both arms come up to cup it — a
        // distinct silhouette from the one-armed branch throw, so the player can tell
        // the two attacks apart before either lands.
        System.Func<Vector3> wellAt = () =>
            (visual != null ? visual.BodyCentre() : transform.position)
            + new Vector3(0.55f * faceSign, 0.15f, 0f);

        if (visual != null)
        {
            Vector3 cup = wellAt();
            visual.AimArm(1f, cup);
            visual.AimArm(-1f, cup);
            visual.Glitch(0.9f);
        }

        float emit = Mathf.Max(0.15f, darkOrbEmitDuration * SpeedMul);
        var charge = Boss3OrbCharge.Spawn(wellAt, emit, darkOrbSettings, layer);
        // A dedicated orb event plays as before. The FALLBACK is the looping attack
        // event, so that one is tracked and stopped when the orbs are thrown.
        int orbLoopToken = -1;   // -1 = nothing tracked, so the stops below are no-ops
        if (orbEmitSound.IsNull) orbLoopToken = StartAttackLoop(attackSound);
        else TryPlay(orbEmitSound);

        float t = 0f;
        while (t < emit && !isDying) { t += Time.deltaTime; yield return null; }

        if (isDying)
        {
            StopAttackLoop(orbLoopToken);
            if (charge != null) charge.Cancel();
            if (visual != null) visual.RelaxArms();
            yield break;
        }

        StopAttackLoop(orbLoopToken);
        if (charge != null) charge.Burst();
        if (visual != null) visual.Glitch(1f);
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.2f, 0.14f);

        int count = Mathf.Max(1, darkOrbCount) + (_phase2 ? Mathf.Max(0, phase2ExtraOrbs) : 0);
        Vector3 origin = wellAt();
        Vector2 aim = (Vector2)(facePoint - origin);
        if (aim.sqrMagnitude < 0.0001f) aim = Vector2.right * faceSign;
        aim.Normalize();

        // Fan the throw. Each orb then curves back in on its own randomly chosen
        // player (see Boss3DarkOrb.PickTarget), which is what makes them split across
        // the team in co-op instead of all converging on one person.
        float spread = Mathf.Max(0f, darkOrbLaunchSpread);
        for (int i = 0; i < count && !isDying; i++)
        {
            float f = count > 1 ? (i / (float)(count - 1)) - 0.5f : 0f;
            float a = f * spread * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(aim.x * Mathf.Cos(a) - aim.y * Mathf.Sin(a),
                                      aim.x * Mathf.Sin(a) + aim.y * Mathf.Cos(a));

            Boss3DarkOrb.Spawn(origin, dir, i, count, darkOrbSettings, layer,
                               owner: this,
                               abort: () => isDying,
                               onPop: OnDarkOrbPop);

            if (i < count - 1 && darkOrbSpacing > 0f)
            {
                float w = 0f, span = darkOrbSpacing * SpeedMul;
                while (w < span && !isDying) { w += Time.deltaTime; yield return null; }
                origin = wellAt();   // he keeps floating while he throws
            }
        }

        if (visual != null) visual.RelaxArms();

        // A short beat so the throw reads before he dissolves out; the caller then
        // teleports him away and the hand attacks resume from the new spot.
        yield return WaitScaled(0.25f);
    }

    /// An orb reached someone (or burned out) and detonated. Routed through the boss's
    /// own damage path so orb hits behave exactly like every other Boss3 attack —
    /// same layer mask, same dedup, same on-hit augment notification.
    private void OnDarkOrbPop(Vector2 pos)
    {
        if (isDying) return;

        ApplyHandDamage(pos, Mathf.Max(0.2f, darkOrbPopRadius), darkOrbDamage,
                        hitBuildings: darkOrbsDamageBuildings);

        // Only make noise for a pop the player can actually feel. A fuse burning out
        // in an empty corner of the map should not shake the camera.
        if (AnyPlayerWithin(pos, darkOrbPopRadius + 0.75f))
        {
            TryPlay(orbPopSound.IsNull ? impactSound : orbPopSound);
            if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.18f, 0.12f);
        }
    }

    private static bool AnyPlayerWithin(Vector2 pos, float radius)
    {
        var reg = PlayerRegistry.Instance;
        if (reg == null) return false;
        var all = reg.All;
        if (all == null) return false;

        float r2 = radius * radius;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null || p.Stats == null || p.Stats.IsDead()) continue;
            if (((Vector2)p.transform.position - pos).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    //  tower defense hook: corruption channel 
    // The boss tethers a corrupt thread to a tower and channels. If it completes, the
    // tower takes heavy damage. The boss is STATIONARY and VULNERABLE the whole time —
    // damage it enough (corruptInterruptThreshold) to sever the tether and save the
    // tower, which also destabilises the boss. Defend-your-build meets counterplay.
    private IEnumerator PerformTowerCorruption(Transform tower)
    {
        if (tower == null) yield break;

        FaceToward(tower.position);
        if (visual != null) visual.AimArm((tower.position.x >= transform.position.x) ? 1f : -1f, tower.position);

        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        System.Func<Vector3> from = () => visual != null ? visual.HandOrigin(
            (tower != null && tower.position.x >= transform.position.x) ? 1f : -1f) : transform.position;
        System.Func<Vector3> to = () => tower != null ? tower.position : transform.position;

        var tether = Boss3CorruptTether.Spawn(from, to, layer);
        _activeTether = tether;
        int loopToken = StartAttackLoop(attackSound);

        // Enter the vulnerable channel.
        _channeling = true;
        _damageTakenDuringChannel = 0f;
        SetVulnerable(true);

        var consumer = tower.GetComponentInParent<IEnergyConsumer>();
        float t = 0f;
        bool interrupted = false;

        while (t < corruptChannelTime && !isDying)
        {
            if (tower == null || consumer == null) break;   // tower already gone

            // Interrupt check — the player damaged the boss enough during the channel.
            if (_damageTakenDuringChannel >= corruptInterruptThreshold)
            {
                interrupted = true;
                break;
            }

            // Channel DoT on the tethered tower.
            if (EnergyManager.Instance != null)
                EnergyManager.Instance.DamageEnergyConsumer(
                    consumer, corruptDamagePerSecond * BossStageDamageMultiplier * Time.deltaTime, gameObject);

            if (tether != null) tether.SetProgress(t / corruptChannelTime);
            t += Time.deltaTime;
            yield return null;
        }

        _channeling = false;
        StopAttackLoop(loopToken);
        if (tether != null) tether.Dissipate(interrupted);
        _activeTether = null;
        if (visual != null) visual.RelaxArms();

        if (interrupted)
        {
            // Staggered: a full punish window as the reward for the save.
            if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.3f, 0.18f);
            yield return DestabilizeWindow();
        }
        else
        {
            SetVulnerable(false);
            if (!isDying && CameraShake.Instance != null) CameraShake.Instance.Shake(0.18f, 0.12f);
        }
    }

    //  phase 2 (armour break) 
    private void EnterPhase2()
    {
        if (_phase2) return;
        _phase2 = true;

        if (visual != null) { visual.SetPhase2(true); visual.Glitch(1f); }
        TryPlay(phaseSound);
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.5f, 0.35f);

        // One-off shockwave so the gear-shift is unmistakable.
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        Boss3Shockwave.Spawn(visual != null ? visual.BodyCentre() : transform.position, layer);
    }

    private bool IsPlayer(Transform t)
    {
        if (t == null) return false;
        var cs = t.GetComponentInParent<CharacterStats>();
        return cs != null && !(cs is EnemyStats);
    }

    private bool IsTower(Transform t)
    {
        if (t == null) return false;
        if (t.GetComponentInParent<EnemyStats>() != null) return false;
        // A tower is an energy consumer that isn't the core.
        if (t.CompareTag("Core")) return false;
        return t.GetComponentInParent<IEnergyConsumer>() != null;
    }

    //  audio (guarded; every event is optional and unassigned by default) 
    private void TryPlay(FMODUnity.EventReference e)
    {
        if (!playAudio) return;
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (e.IsNull) return;
        AudioManager.instance.PlayOneShot(e, transform.position);
    }

    /// Starts `e` as a tracked, stoppable instance (replacing any attack sound still
    /// playing). Returns a token to hand back to StopAttackLoop. Uses the same guards
    /// as TryPlay, so an unassigned event / disabled audio is still silent.
    private int StartAttackLoop(FMODUnity.EventReference e)
    {
        StopAttackLoop();
        int token = ++_attackLoopToken;

        if (!playAudio || e.IsNull) return token;
        if (AudioManager.instance == null || FMODEvents.instance == null) return token;

        _attackLoop = FMODUnity.RuntimeManager.CreateInstance(e);
        _attackLoop.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
        _attackLoop.start();
        return token;
    }

    /// Stops the tracked attack sound. With a token, only stops it if it still belongs
    /// to that attack; with no token (0), stops whatever is playing. ALLOWFADEOUT lets
    /// any fade-out / AHDSR release authored in FMOD Studio play instead of a hard cut.
    private void StopAttackLoop(int token = 0)
    {
        if (token != 0 && token != _attackLoopToken) return;
        if (!_attackLoop.isValid()) return;

        _attackLoop.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _attackLoop.release();
        _attackLoop = default;
    }

    // Resolve + apply damage at a struck point. Mirrors Boss2's meteor damage routing:
    // players via CharacterStats, towers/core via the energy system, with safety nets
    // so buildings are hit regardless of the collider layer mask. Deduped per victim.
    /// <param name="hitBuildings">Defaults to TRUE, which is the original behaviour for
    /// every existing caller (tree-hands, root spikes). The dark orbs pass false: they
    /// hunt players, and a stray pop next to a tower should not be chipping the base.</param>
    private void ApplyHandDamage(Vector2 pos, float radius, float damage, bool hitBuildings = true)
    {
        if (isDying) return;   // stray root-spike/hand callbacks after death must no-op
        float dmg = damage * BossStageDamageMultiplier;

        var damagedChars = new HashSet<CharacterStats>();
        var damagedConsumers = new HashSet<IEnergyConsumer>();

        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, radius, damageLayers);
        if (hits != null)
        {
            foreach (var hit in hits)
            {
                if (hit == null || hit.gameObject == gameObject) continue;
                if (hit.GetComponentInParent<EnemyStats>() != null) continue;   // never hit other enemies / self

                var cs = hit.GetComponentInParent<CharacterStats>();
                if (cs != null && damagedChars.Add(cs))
                {
                    cs.TakeDamage(dmg);
                    // Player-side on-hit augments (Damage Reflection / Ice Armor). These
                    // fired ONLY from EnemyController before, so boss specials reflected
                    // nothing. damagedChars already dedups, so this cannot double-fire.
                    EnemyController.NotifyCharacterDamaged(cs, dmg, gameObject);
                    continue;
                }

                if (!hitBuildings) continue;

                var consumer = hit.GetComponentInParent<IEnergyConsumer>();
                if (consumer != null && damagedConsumers.Add(consumer) && EnergyManager.Instance != null)
                    EnergyManager.Instance.DamageEnergyConsumer(consumer, dmg, gameObject);
            }
        }

        // Safety net — players in radius (covers wrong-layer colliders).
        if (PlayerRegistry.Instance != null)
        {
            foreach (var ps in PlayerRegistry.Instance.AllAliveInRadius(pos, radius))
                if (ps != null && damagedChars.Add(ps))
                {
                    ps.TakeDamage(dmg);
                    EnemyController.NotifyCharacterDamaged(ps, dmg, gameObject);
                }
        }

        if (!hitBuildings) return;

        // Safety net — towers + core, walked directly so a narrow layer mask can't
        // spare a building the hand visibly speared.
        var towers = Tower.ActiveTowers;
        if (towers != null)
            for (int i = towers.Count - 1; i >= 0; i--)
                if (towers[i] != null)
                    DamageConsumerIfInRadius(towers[i], towers[i].gameObject, pos, radius, dmg, damagedConsumers);

        var coreObj = GameObject.FindGameObjectWithTag("Core");
        if (coreObj != null)
        {
            var coreConsumer = coreObj.GetComponentInParent<IEnergyConsumer>();
            if (coreConsumer != null)
                DamageConsumerIfInRadius(coreConsumer, coreObj, pos, radius, dmg, damagedConsumers);
        }
    }

    private void DamageConsumerIfInRadius(IEnergyConsumer consumer, GameObject go, Vector2 pos,
                                          float radius, float damage, HashSet<IEnergyConsumer> already)
    {
        if (consumer == null || go == null) return;
        var col = go.GetComponent<Collider2D>();
        float dist = col != null ? Vector2.Distance(pos, col.ClosestPoint(pos))
                                 : Vector2.Distance(pos, go.transform.position);
        if (dist > radius) return;
        if (!already.Add(consumer)) return;
        if (EnergyManager.Instance != null)
            EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
    }

    //  damage intake 

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isDying) return;

        var wp = other.GetComponent<WeaponProjectile>();
        if (wp != null)
        {
            TakeDamage(wp.GetDamage());
            CombatStats.ReportPlayerDamageDealt(wp.GetOwner(), wp.GetDamage(), transform.position);
            Destroy(other.gameObject);
            return;
        }

        var proj = other.GetComponent<Projectile>();
        if (proj != null)
        {
            TakeDamage(proj.damage);
            CombatStats.ReportTowerDamageDealt(proj.damage);
            Destroy(other.gameObject);
        }
    }

    public override void TakeDamage(float amount)
    {
        if (DebugCheats.DamageBlocked(this)) return;
        if (isDying) return;

        // Punish window / channel: bonus damage while destabilised or channeling.
        float incoming = amount;
        if (_vulnerable) incoming *= vulnerableDamageMultiplier;

        // Hand-break amplification, on top. Applied to `incoming` only — the two
        // interrupt accumulators below deliberately track RAW damage, so a big amp
        // can't make their thresholds easier to reach than they were tuned for.
        if (_handBreakAmpStacks > 0) incoming *= HandBreakAmpMultiplier;

        // Track raw damage during a corruption channel so the player can interrupt it.
        if (_channeling) _damageTakenDuringChannel += amount;

        // Track raw damage during a hand-attack wind-up so the player can interrupt that too.
        if (_attackInterruptible) _attackDamageAccum += amount;

        if (!armorDestroyed && bossArmor > 0)
        {
            bossArmor -= incoming;
            if (bossArmor <= 0)
            {
                float overflow = -bossArmor;
                bossArmor = 0;
                OnArmorDestroyed();
                currentHealth -= overflow;
            }
        }
        else
        {
            currentHealth -= incoming;
        }

        CallStartDamageFlash();
        if (visual != null)
        {
            visual.Glitch(_vulnerable ? 0.9f : 0.6f);
            visual.HitFlash(_vulnerable ? 1f : 0.6f);
        }
        // A crisp hit spark at the boss so ranged players feel their shots land through the glitch.
        Boss3HitSpark.Spawn(visual != null ? visual.BodyCentre() : transform.position,
                            bossSprite != null ? bossSprite.sortingLayerName : "Default", _vulnerable);
        UpdateBossHealthBar();

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            ExecuteBossDeath();
        }
    }

    protected override void OnArmorDestroyed()
    {
        base.OnArmorDestroyed();
        EnterPhase2();
    }

    //  death 

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

        transform.rotation = Quaternion.identity;
        CombatJuice.OnBossKilled(gameObject);

        // Cancel any in-flight tree-hands so none freeze on screen.
        foreach (var h in _activeHands) if (h != null) h.Cancel();
        _activeHands.Clear();

        // StopAllCoroutines below skips the attack's own stop calls, so stop it here.
        StopAttackLoop();

        // Same for any dark orbs still hunting: they fade harmlessly rather than
        // outliving the fight. Filtered by owner, so this can never clear orbs
        // belonging to a second Boss3 that is still alive.
        Boss3DarkOrb.DissipateAllFrom(this);

        // Sever any active corruption tether so it stops reading the (dying) boss's hand.
        if (_activeTether != null) { _activeTether.Dissipate(false); _activeTether = null; }

        // Hide ghosts/arms/threads so only the body silhouette shatters.
        if (visual != null) visual.OnDeath();

        if (HealthBar != null) Destroy(HealthBar.gameObject);

        if (_routine != null) StopCoroutine(_routine);
        StopAllCoroutines();

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
        foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;

        bossArmor = 0f;
        armorDestroyed = true;

        Vector3 deathPos = transform.position;

        // Boss energy reward ring — identical pattern to Boss1/Boss2.
        for (int i = 0; i < 10; i++)
        {
            float angle = (360f / 10) * i * Mathf.Deg2Rad;
            Vector3 spawnPos = deathPos + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 1.5f;
            int energyValue = EnergyDropManager.Instance != null ? EnergyDropManager.Instance.defaultEnergyValue : 10;
            EnergyDrop.CreateEnergyDrop(spawnPos, energyValue);
        }

        // Permanent weapon/tool blueprint roll — every boss inherits this.
        RollBlueprintDrop(deathPos);

        // Shared death book-keeping. This used to be a bare EnergyManager call, which
        // meant a boss killed by a tower paid NO augment-335 tithe and left a stale
        // TowerKillAttribution entry behind. FireCommonDeathHooks runs the exact same
        // sequence EnemyStats.PerformDeath uses (wave counter -> tithe -> EnergyManager
        // -> attribution cleanup) and still raises OnEnemyKilledEvent, so lifesteal and
        // health-on-kill behave exactly as before.
        //
        // The wave-counter notify balances the enemiesAlive++ that
        // WaveSpawner.SpawnEnemyPublic did when GameOrchestrator.SpawnBoss spawned us.
        // Orchestrator mode never reads that counter, so this changes nothing there; it
        // stops a boss placed in a plain WaveConfig from soft-locking standalone mode.
        //
        // NOTE: drops are deliberately NOT routed through the hook — this boss already
        // spawns its own reward ring above, and adding the standard roll would double it.
        EnemyStats.FireCommonDeathHooks(gameObject);

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: disintegrationDuration,
            onComplete: () =>
            {
                if (AudioManager.instance != null && FMODEvents.instance != null)
                    AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, deathPos);
            });

        // Guaranteed teardown if the VFX above never finishes. Bosses never reach
        // CharacterStats.Die() -> Destroy(gameObject), so a failed VFX would leave this
        // object alive forever and GameOrchestrator.WaitForBossDead() would spin on it
        // for the rest of the session. Fires well after the VFX should have completed,
        // so the normal death path is untouched.
        ScheduleDeathFailsafe(disintegrationDuration);
    }

    // OVERRIDE (was a private declaration that HID EnemyStats.OnDestroy).
    // Unity dispatches only the most-derived OnDestroy, so while this was private
    // the boss never unregistered from EnemyStatModifierManager and never released
    // its damage-flash material. base.OnDestroy() runs LAST so this class's own
    // teardown happens first, exactly as it did before.
    protected override void OnDestroy()
    {
        if (HealthBar != null) Destroy(HealthBar.gameObject);
        foreach (var h in _activeHands) if (h != null) h.Cancel();
        Boss3DarkOrb.DissipateAllFrom(this);
        StopAttackLoop();   // never leave a looping event orphaned (scene unload, etc.)

        base.OnDestroy();
    }
}

// A small self-contained chromatic burst played at each blink endpoint: a bright core
// flash plus split red/cyan rings that expand and fade — the "digital pop" of the
// teleport. Procedural, no assets, destroys itself when done.
public class Boss3BlinkFlash : MonoBehaviour
{
    private SpriteRenderer _core, _red, _cyan;
    private float _t;
    private const float LIFE = 0.35f;

    public void Play(string sortingLayer)
    {
        _core = Make("Core", new Color(1f, 0.5f, 0.4f, 1f), 2201, sortingLayer, Boss3Sprites.Spark);
        _red = Make("Red", new Color(1f, 0.18f, 0.18f, 0.9f), 2200, sortingLayer, Boss3Sprites.SoftDot);
        _cyan = Make("Cyan", new Color(0.3f, 0.75f, 1f, 0.9f), 2200, sortingLayer, Boss3Sprites.SoftDot);
    }

    private SpriteRenderer Make(string name, Color c, int order, string layer, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = c;
        sr.sortingOrder = order;
        if (!string.IsNullOrEmpty(layer)) sr.sortingLayerName = layer;
        return sr;
    }

    private void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / LIFE);
        float ease = 1f - (1f - k) * (1f - k);

        // Core: sharp shrink-out.
        if (_core != null)
        {
            float s = Mathf.Lerp(2.2f, 0.2f, ease);
            _core.transform.localScale = new Vector3(s, s * 1.4f, 1f);   // slightly tall, like a slit
            var c = _core.color; c.a = 1f - k; _core.color = c;
        }
        // Split rings drift apart horizontally as they grow — the chromatic tear.
        AnimateRing(_red, ease, k, -1f);
        AnimateRing(_cyan, ease, k, +1f);

        if (k >= 1f) Destroy(gameObject);
    }

    private void AnimateRing(SpriteRenderer sr, float ease, float k, float dir)
    {
        if (sr == null) return;
        float s = Mathf.Lerp(0.6f, 3.4f, ease);
        sr.transform.localScale = new Vector3(s, s, 1f);
        sr.transform.localPosition = new Vector3(dir * ease * 0.6f, 0f, 0f);
        var c = sr.color; c.a = (1f - k) * 0.8f; sr.color = c;
    }
}



