using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  BOSS 4 — "THE DEVOURER"
//  A walking, biting boss that farms its own buffs.
//
//  THE LOOP:
//    1. Every `spawnInterval` seconds it spawns `spawnCount` minions, chosen only
//       from the trait types it has NOT yet eaten. They arrive frozen in ice and
//       stand still until the player shoots one or walks too close.
//    2. The Devourer hunts those minions in preference to everything else. When
//       it reaches one it bites, and the bite EATS it — permanently absorbing
//       that minion's trait (see ApplyTrait).
//    3. Once every enabled trait is absorbed it ENRAGES: big stat boosts, the
//       multi-phase Kaboom, and a straight march at the core.
//
//  ARCHITECTURE:
//    Movement, target acquisition, attack-cycle timing and hit-frame dispatch are
//    all delegated to EnemyController — the same as Boss1. This boss only
//    supplies two hooks:
//        PriorityTargetProvider -> what to hunt (a minion, or the core once enraged)
//        AttackHandlerOverride  -> what a bite DOES (eat / melee / cone / circle)
//    That is the documented composition idiom in EnemyController, so nothing in
//    the shared movement code has to change for this boss to exist.
//
//  ART: EnemyData.useAnimationFolders with idleFrames = Boss4/Walk (40 frames)
//    and attackFrames = Boss4/Bite (20 frames). Set spriteFacesLeft = true — the
//    Devourer art is drawn facing LEFT, and that flag is exactly what makes the
//    flip system mirror it correctly. See Boss4_SETUP.md.
//
//  EXECUTION ORDER: 10000, the same value BerserkController uses and for the same
//    reason — the Brute trait grows the boss, and YSortEntity rewrites
//    transform.localScale every frame. We have to assert our scale AFTER it.

[DefaultExecutionOrder(10000)]
public class Boss4 : BaseBossStats, ISpritePrewarm, IParryStunStarsAnchor
{
    //  STATS / BODY
    //  ART  — drag the PNGs straight in; no EnemyData asset needed
    //  Select all 40 Walk PNGs in the Project window and drag them onto the Walk
    //  Frames header, then all 20 Bite PNGs onto Bite Frames. That is the entire
    //  art setup.
    //
    //  These are DIRECT Sprite[] references, which is the path EnemyData.cs asks
    //  for: a Resources path string cannot be statically analysed, so Unity
    //  force-includes everything under Resources/ in the build and can never strip
    //  it. Direct references put the art back in the dependency graph
    //  (Prefab -> Boss4 -> Sprite -> Texture) so it loads with the scene and the
    //  atlas packer can strip the originals.
    [Header("Art — drag the PNGs in (no EnemyData asset required)")]
    [Tooltip("Assets/Art/EnemySprites/Boss4/Walk — all 40 frames, in order. " +
             "Doubles as the idle loop.")]
    [SerializeField] private Sprite[] walkFrames;

    [Tooltip("Assets/Art/EnemySprites/Boss4/Bite — all 20 frames, in order.")]
    [SerializeField] private Sprite[] biteFrames;

    [Tooltip("Optional death clip. Leave EMPTY to use the sprite-shatter death VFX " +
             "instead (which is what the other bosses do).")]
    [SerializeField] private Sprite[] deathFrames;

    [Tooltip("Frame of the BITE clip where the jaws connect, 0-based. Damage timing, " +
             "the parry window and the bite telegraph are all derived from this, so " +
             "it is the one number worth scrubbing the clip to confirm. 20-frame " +
             "bite -> 10 is the middle.")]
    [SerializeField] private int biteHitFrame = 10;

    [Tooltip("Tick ONLY if the Bite frames are drawn facing the opposite direction " +
             "from the Walk frames. EnemyData.spriteFacesLeft is one flag for the " +
             "whole asset, so mismatched clips cannot be fixed any other way.\n\n" +
             "Quick test: if the boss looks correct while walking but faces backwards " +
             "during the bite, this is the cause and this toggle fixes it.")]
    [SerializeField] private bool invertFacingDuringBite = false;

    [Tooltip("Seconds per frame. 0.055 over 40 walk frames is a ~2.2s stride cycle.")]
    [SerializeField] private float walkFrameTime = 0.055f;
    [SerializeField] private float biteFrameTime = 0.05f;

    [Header("Boss4 Stats")]
    [Tooltip("ON (default): the Devourer builds its own EnemyData at runtime from " +
             "the numbers below, IGNORING whatever asset is in the Enemy Data slot. " +
             "That is what lets you duplicate another boss's prefab without having " +
             "to clear or replace its EnemyData asset.\n\n" +
             "Turn OFF only if you want to author a real EnemyData asset instead.")]
    [SerializeField] private bool useInspectorStats = true;
    [SerializeField] private float bossMaxHealth = 1400f;
    [SerializeField] private float bossMaxArmor = 800f;
    [SerializeField] private float bossMoveSpeed = 1.5f;
    [SerializeField] private float bossMass = 220f;

    [Header("Boss4 Body")]
    [Tooltip("Scales the whole boss — sprite, collider, shadow, dust, spawn ring — " +
             "on top of the prefab's own Transform scale. 1.6 = 60% bigger.\n\n" +
             "Change this rather than the prefab's Transform scale: everything the " +
             "boss derives from its body size reads the LIVE scale, so they all " +
             "follow automatically. Note it does NOT touch EnemyController's " +
             "attackRange, which is an absolute world distance — scale that yourself " +
             "if you want the reach to grow with the body.")]
    [SerializeField] private float sizeMultiplier = 1f;

    [Tooltip("ON: the collider already on the prefab is left exactly as authored. " +
             "OFF: it is rebuilt from the radius/offset below.\n\n" +
             "Default ON, because rebuilding it silently discarded whatever you sized " +
             "by hand in the scene view — which is how the Devourer ended up with a " +
             "1.1 collider under a much bigger body and the player could walk into it.")]
    [SerializeField] private bool useColliderFromPrefab = true;

    [Tooltip("Only used when Use Collider From Prefab is OFF.")]
    [SerializeField] private float bossColliderRadius = 1.8f;
    [SerializeField] private float bossColliderOffsetY = 0.6f;

    [Header("Health Bar (world bar; the big top-of-screen bar is automatic)")]
    [SerializeField] private float healthBarExtraYPadding = 0.6f;
    [SerializeField] private float healthBarYReduction = 0f;

    //  MINION POOL
    [Header("Minion Pool — Z unique types")]
    [Tooltip("One row per minion type. Assign your EXISTING enemy prefabs; the " +
             "Devourer freezes them on spawn and reverts them cleanly on wake, so " +
             "no special 'frozen' prefab variant is needed.\n\n" +
             "Only ENABLED rows with a prefab count toward the ultimate phase, so " +
             "you can ship the boss with a subset and it will still enrage.")]
    [SerializeField]
    private List<DevourerMinionEntry> minionPool = new List<DevourerMinionEntry>();

    [Header("Spawning")]
    [Tooltip("X — seconds between spawn waves.")]
    [SerializeField] private float spawnInterval = 14f;

    [Tooltip("Y — minions spawned per wave. Capped by how many unconsumed types " +
             "are left, so the boss never spawns a type it has already absorbed.")]
    [SerializeField] private int spawnCount = 2;

    [Tooltip("Seconds before the first wave, so the fight opens with the boss " +
             "alone rather than an immediate crowd.")]
    [SerializeField] private float firstSpawnDelay = 3f;

    [Tooltip("Radius of the ring the minions materialise on, around the boss.")]
    [SerializeField] private float spawnRingRadius = 4.5f;

    [Tooltip("Warning time between the telegraph appearing and the minions " +
             "actually arriving.")]
    [SerializeField] private float spawnTelegraphTime = 1.1f;

    [Tooltip("Minimum clear space around a spawn point, in world units. Candidate " +
             "points that land on an existing enemy are rejected and re-rolled, so " +
             "a new wave never materialises inside the statues left from the last " +
             "one. Scale this with your world: it should be roughly the width of " +
             "your minions.")]
    [SerializeField] private float minSpawnSeparation = 1.0f;

    [Tooltip("Skip types that already have a living minion on the field, so the " +
             "arena doesn't fill up with duplicates the boss hasn't gotten to yet.")]
    [SerializeField] private bool avoidDuplicateLiveTypes = true;

    [Tooltip("Minions spawned by the boss drop energy when the PLAYER kills them. " +
             "OFF by default — they respawn on a timer, so leaving this on turns " +
             "the boss into an energy farm.")]
    [SerializeField] private bool minionsDropEnergy = false;

    [Header("Minion Stasis")]
    [Tooltip("Seconds a freshly spawned minion is OFF THE MENU. The Devourer will " +
             "not hunt or eat it until this expires, which is the player's window to " +
             "kill it and deny the buff. This is the main difficulty dial for the " +
             "whole fight — raise it to make denial easier.")]
    [SerializeField] private float minionGraceSeconds = 8f;

    [Tooltip("Seconds the Devourer must wait after a meal before hunting the next " +
             "one. It goes back to attacking the player and towers in between, so " +
             "the fight isn't just watching it snack.")]
    [SerializeField] private float eatCooldownSeconds = 6f;

    [Tooltip("Wake a frozen minion when it takes damage. This is the intended way " +
             "to wake one: shoot it.")]
    [SerializeField] private bool minionsWakeOnDamage = true;

    [Tooltip("Wake a frozen minion when a player walks near it. OFF by default — " +
             "with this on, minions un-freeze just by being approached, which reads " +
             "as them never having been frozen at all.")]
    [SerializeField] private bool minionsWakeOnProximity = false;

    [Tooltip("Only used when Minions Wake On Proximity is ON.")]
    [SerializeField] private float minionWakeRadius = 3.5f;

    [Tooltip("On boss death, wake any minions still frozen so they finish the " +
             "fight as ordinary enemies. Leaving statues in the scene would look " +
             "broken; this is the tidier option and keeps stage-clear logic honest.")]
    [SerializeField] private bool wakeMinionsOnBossDeath = true;

    //  BITE
    [Header("Bite")]
    [Tooltip("Base bite damage before difficulty / stage scaling and before the " +
             "Scarecrow trait's multiplier.")]
    [SerializeField] private float biteDamage = 22f;

    [Tooltip("ON: the AOE bite radius is derived from EnemyController's attack range " +
             "(x Aoe Reach Factor), so the circle can never be wildly bigger than the " +
             "distance the boss actually attacks from.\n\n" +
             "This is on by default because the two drifting apart is what makes the " +
             "fight feel like a boss parked in a permanent damage field: an AOE many " +
             "times wider than its reach has no dodgeable edge near the body.")]
    [SerializeField] private bool deriveAoeFromReach = true;

    [SerializeField] private float aoeReachFactor = 1.25f;

    [Tooltip("Hard ceiling on the AOE radius, in body-radii. Whatever the traits " +
             "stack to, the circle stops here.")]
    [SerializeField] private float maxAoeInBodies = 3.5f;

    [Tooltip("Hard ceiling on attack range, in body-radii. A melee boss that can bite " +
             "from further than this leaves no space to fight it in.")]
    [SerializeField] private float maxReachInBodies = 3.5f;

    [Tooltip("Damage multiplier for the AOE bites (circle and cone) relative to a " +
             "single-target bite. Below 1 so the wide attacks trade punch for " +
             "coverage — a cone that hits everything for full melee damage is " +
             "strictly better than the bite it replaced, which is why it felt so " +
             "brutal once unlocked.")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float aoeDamageMultiplier = 0.6f;

    [Tooltip("Base radius of the AOE bite shapes. Used only when Derive Aoe From " +
             "Reach is OFF.")]
    [SerializeField] private float biteAoeRadius = 3.2f;

    [Tooltip("Half-angle of the forward cone bite unlocked by the Pitcher trait. " +
             "55 gives a 110-degree wedge. The angle is FIXED — traits make the cone " +
             "longer, never wider.")]
    [SerializeField] private float biteConeHalfAngle = 55f;

    [Tooltip("How much further the cone reaches than the circle AOE. 1.5 = half again " +
             "as long.\n\n" +
             "A cone is a directional attack: it should trade coverage for reach, so " +
             "it wants to be noticeably longer than the circle centred on the same " +
             "boss. This multiplies the cone's radius only — its damage and angle are " +
             "unaffected. Raise it if the cone reads as too short.")]
    [SerializeField] private float coneRadiusMultiplier = 1.5f;

    [Tooltip("Do the AOE bites damage towers and the core as well as players?")]
    [SerializeField] private bool biteHitsBuildings = true;

    [Tooltip("How far outside its attack range the boss can still connect, as a " +
             "multiplier. 1.15 gives a small grace band so a bite that visually " +
             "clips you still lands; anything beyond it whiffs.\n\n" +
             "This is what makes dodging work: without a check at the moment of " +
             "impact, the bite commits its damage the instant the swing STARTS and " +
             "lands however far you have run in the meantime.")]
    [SerializeField] private float biteReachTolerance = 1.15f;

    [Header("Bite Lunge")]
    [Tooltip("Make the bite a physical movement: the boss rears back, then throws its " +
             "head forward on the hit frame and recovers.\n\n" +
             "Off, the attack is just the sprite clip playing while the body sits " +
             "still, which reads as the boss standing there rather than attacking you.")]
    [SerializeField] private bool biteLungeEnabled = true;

    [Tooltip("How far it lunges, in body-radii. Kept modest — this is a head snap, " +
             "not a charge. 0.9 is about half a body length.")]
    [SerializeField] private float biteLungeBodyRadii = 0.9f;

    [Tooltip("How far it rears BACK first, as a fraction of the lunge. The pull-back " +
             "is what makes the forward snap read as force rather than a slide.")]
    [Range(0f, 0.8f)]
    [SerializeField] private float biteWindBackFraction = 0.35f;

    [Tooltip("Sideways tremble during the wind-up, in body-radii. Very small — it is " +
             "muscle tension, not a rumble. 0 disables it.")]
    [SerializeField] private float biteShakeAmount = 0.06f;

    [Tooltip("Vertical squash/stretch through the lunge. 0.10 = 10% crouch on the " +
             "wind-up and 10% stretch on the snap.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float biteSquashAmount = 0.10f;

    [Tooltip("Seconds to settle back after the bite lands.")]
    [SerializeField] private float biteRecoverTime = 0.22f;

    [Tooltip("Seconds of wind-up before a bite lands — the player's dodge window.\n\n" +
             "Boss4 converts this into EnemyData.hitFrame at startup, so the damage " +
             "frame, the parry window and the ground telegraph all derive from this " +
             "one number and cannot drift apart. Set 0 to keep whatever Bite Hit " +
             "Frame says instead.")]
    [SerializeField] private float biteWindUpSeconds = 0.55f;

    [Tooltip("Draw a ground telegraph for EVERY bite, including the plain melee one " +
             "before any AOE traits are absorbed.\n\n" +
             "Off, the early fight has no warning at all: the boss walks up and bites " +
             "with nothing on screen to read, which feels like damage on contact.")]
    [SerializeField] private bool alwaysTelegraphBite = true;

    [Tooltip("Half-angle of the plain (pre-trait) bite telegraph. Widen this to make " +
             "the early-fight cone read bigger without changing the boss's reach.")]
    [SerializeField] private float plainBiteTelegraphHalfAngle = 42f;

    [Tooltip("Visual-only length multiplier for the PLAIN bite telegraph.\n\n" +
             "1.0 draws it exactly at the hit/whiff boundary, which is the honest " +
             "size. Above 1 makes the marker easier to see but starts promising " +
             "damage further out than the bite can actually reach, so keep it close " +
             "to 1. Cone Radius Multiplier does NOT apply here — that one is for the " +
             "Pitcher trait's AOE cone.")]
    [SerializeField] private float plainBiteTelegraphScale = 1f;

    [Header("Bite Parry")]
    [Tooltip("How many bite frames BEFORE the hit frame the parry window opens. The " +
             "window always closes on the hit frame itself. 3 matches the old " +
             "hard-coded value; raise it for a more forgiving parry.\n\n" +
             "Augment 332 (Longer Parry Window) opens it earlier still, on top of this.")]
    [SerializeField] private int biteParryLeadFrames = 3;

    [Tooltip("Show the yellow '!' above the Devourer during the bite's parry frames, " +
             "for players with a shield equipped — the same mark every other melee " +
             "enemy shows.\n\n" +
             "Added at runtime, so there is nothing to put on the prefab. If you DO add " +
             "a ParryIndicator to the prefab by hand, that one is used instead.")]
    [SerializeField] private bool showParryIndicator = true;

    [Tooltip("World units between the top of the Devourer's sprite and the '!'.")]
    [SerializeField] private float parryIndicatorHeadPadding = 0.2f;

    [Tooltip("World size of the '!'. Regular enemies use 0.5; the boss is bigger.")]
    [SerializeField] private float parryIndicatorSize = 0.7f;

    [Tooltip("World units between the top of the Devourer's sprite and the CENTRE of the " +
             "orbiting parry-stun stars. The ring's lowest (front, largest) stars reach " +
             "about 0.4 below its centre, so 0.35 keeps them just clear of the spike tips. " +
             "Raise to lift the stars higher; lower to tuck them closer to the head.")]
    [SerializeField] private float parryStunStarsPadding = 0.35f;

    [Tooltip("Colour of the bite telegraph at rest, and at the moment it lands.")]
    [SerializeField] private Color telegraphIdleColor = new Color(0.42f, 0.10f, 0.70f, 0.32f);
    [SerializeField] private Color telegraphHotColor = new Color(1f, 0.18f, 0.28f, 0.60f);

    //  TRAIT TUNING
    [Header("Spike Volley (fired while distracted)")]
    [Tooltip("While the Devourer is walking to a meal or mid-swallow it is looking " +
             "away from you, which made approaching it completely free. It now fires " +
             "a spread of spikes at whoever comes near.\n\n" +
             "The spikes PROTRUDE from its hide first, pointing at the target, then " +
             "launch — so the volley is read off the boss's silhouette rather than " +
             "from a ground marker you might be standing on.")]
    [SerializeField] private bool spikeVolleyEnabled = true;

    [Tooltip("Only fire while distracted (hunting or swallowing a minion). Off = it " +
             "also fires during normal combat, which stacks with the bite.")]
    [SerializeField] private bool spikesOnlyWhenDistracted = true;

    [Tooltip("Seconds between volleys.")]
    [SerializeField] private float spikeInterval = 3.5f;

    [Tooltip("Spikes per volley. 3-5 reads as a shotgun spread; more becomes a wall " +
             "with no gap to slip through.")]
    [SerializeField] private int spikeCount = 3;

    [Tooltip("Total spread of the fan, in degrees. Wider = easier to dodge sideways, " +
             "harder to escape by backing straight off.")]
    [SerializeField] private float spikeSpreadDegrees = 26f;

    [Tooltip("How long the spikes stick out before firing. This is the dodge window.")]
    [SerializeField] private float spikeTelegraphTime = 0.55f;

    [Tooltip("Random angle wobble applied to each pellet on top of its fan slot, in " +
             "degrees. 0 = a perfectly even comb the player learns once; a few degrees " +
             "keeps the gaps moving so each volley has to be read.")]
    [SerializeField] private float spikeAngleJitter = 3.5f;

    [Tooltip("Per-pellet speed variance (fraction). 0.15 = +/-15%. Makes the volley " +
             "arrive as a ragged burst instead of one wall.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float spikeSpeedVariance = 0.15f;

    [Tooltip("Seconds between individual pellets leaving. Very small (0.02-0.05). A " +
             "real barrel does not release them in lockstep.")]
    [SerializeField] private float spikeStagger = 0.03f;

    [SerializeField] private float spikeSpeed = 7f;
    [SerializeField] private float spikeDamage = 9f;
    [SerializeField] private float spikeLifetime = 3f;

    [Tooltip("Maximum distance at which a volley will be fired.")]
    [SerializeField] private float spikeRange = 9f;

    [Tooltip("OPTIONAL, and only used when Spike Count is 1. A prefab with an " +
             "EnemyProjectile component (the Pitcher's dart works).\n\n" +
             "Not needed for shield interaction any more: the procedural spikes used " +
             "for spreads can be blocked, and parried back into the boss with the " +
             "Projectile Parry augment, on their own (see Spike Shield Interaction).")]
    [SerializeField] private GameObject spikeProjectilePrefab;

    [Header("Spike Shield Interaction")]
    [Tooltip("Let a raised shield BLOCK spikes (reduced damage), and — with Augment 325 " +
             "(Projectile Parry) — PARRY them straight back into the Devourer, stunning it " +
             "exactly like a melee parry.")]
    [SerializeField] private bool spikesParryable = true;

    [Tooltip("How close (world units) a spike must get to the player before the shield " +
             "can catch it and the '!' prompt appears. Same meaning and default as " +
             "EnemyProjectile's Parry React Radius.")]
    [SerializeField] private float spikeParryReactRadius = 2f;

    [Tooltip("Half-width of the lane (world units) a spike must be flying down to count " +
             "as 'coming at' the player. Stops the shield from catching the outer pellets " +
             "of a spread that would have missed anyway — a block still deals reduced " +
             "damage, so catching a miss would PUNISH the player for defending.")]
    [SerializeField] private float spikeParryCatchWidth = 0.9f;

    [Tooltip("Reflected spike damage = spike damage x this (x the parry-stun bonus).")]
    [SerializeField] private float spikeParryReflectMultiplier = 2f;

    [Tooltip("Speed multiplier on the return trip after a parry.")]
    [SerializeField] private float spikeParryReturnSpeedMultiplier = 1.5f;

    [SerializeField] private Color spikeColor = new Color(0.85f, 0.45f, 1f);

    [Header("Trait — Bomber (random mini-explosions)")]
    [SerializeField] private float bomberInterval = 3.2f;
    [SerializeField] private float bomberBlastRadius = 1.8f;
    [SerializeField] private float bomberBlastDamage = 14f;
    [SerializeField] private float bomberScatterRadius = 5.5f;
    [SerializeField] private float bomberTelegraphTime = 0.65f;

    [Header("Trait — Insect (periodic cloak)")]
    [Tooltip("X — seconds between cloaks.")]
    [SerializeField] private float cloakInterval = 9f;
    [Tooltip("Y — seconds each cloak lasts.")]
    [SerializeField] private float cloakDuration = 2.5f;
    [Range(0f, 1f)]
    [Tooltip("Sprite alpha while cloaked. Values below ~0.25 make the boss read as " +
             "having vanished outright for a few seconds, which is disorienting " +
             "rather than threatening. Its ground shadow stays fully opaque " +
             "regardless, so the boss is always trackable even at 0.")]
    [SerializeField] private float cloakAlpha = 0.32f;
    [SerializeField] private float cloakFadeTime = 0.35f;

    [Tooltip("Warning time before the cloak engages. The boss shimmers and stutters " +
             "through partial transparency first, like a predator's camouflage " +
             "spooling up, so vanishing is something the player SAW coming rather " +
             "than something that just happened to them.")]
    [SerializeField] private float cloakTelegraphTime = 1.1f;

    [Header("Trait — Pitcher (reach + cone)")]
    [Tooltip("ON: reach traits MULTIPLY the base attack range instead of adding an " +
             "absolute number of world units.\n\n" +
             "Absolute bonuses are what let reach explode: +2.2 and +2.6 on a boss " +
             "whose body is 0.8 units across nearly tripled its range, and the AOE " +
             "(derived from range) grew with it. A multiplier stays proportional to " +
             "the boss whatever it is scaled to.")]
    [SerializeField] private bool multiplicativeReachTraits = true;

    [Tooltip("Reach multiplier from the Pitcher trait. Used when Multiplicative " +
             "Reach Traits is ON.")]
    [SerializeField] private float pitcherReachMultiplier = 1.25f;

    [Tooltip("Absolute reach bonus. Used only when Multiplicative Reach Traits is OFF.")]
    [SerializeField] private float pitcherRangeBonus = 2.2f;

    [Header("Trait — Brute (bulk)")]
    [SerializeField] private float bruteHealthMultiplier = 1.35f;
    [SerializeField] private float bruteScaleMultiplier = 1.25f;
    [SerializeField] private float bruteGrowDuration = 0.5f;

    [Header("Trait — Poisoner (pools + DoT)")]
    [SerializeField] private float poisonPoolInterval = 4.5f;
    [SerializeField] private float poisonPoolRadius = 1.7f;
    [SerializeField] private float poisonPoolDps = 7f;
    [SerializeField] private float poisonPoolLifetime = 7f;
    [SerializeField] private float poisonPoolScatter = 4.5f;
    [Tooltip("Let the attack DoT tick on towers and the CORE.\n\n" +
             "OFF by default. A DoT that refreshes on every hit and ticks on the core " +
             "drains it continuously with nothing the player can do about it — in " +
             "testing this is what ended the run, not the boss's actual attacks. " +
             "Pools and direct hits still damage buildings; only the lingering DoT " +
             "is restricted.")]
    [SerializeField] private bool poisonAffectsBuildings = false;

    [Tooltip("DoT applied by the Devourer's own attacks.")]
    [SerializeField] private float poisonAttackDps = 6f;
    [SerializeField] private float poisonAttackDuration = 4f;

    [Header("Trait — Mortar (reach + AOE size)")]
    [Tooltip("Reach multiplier from the Mortar trait. Used when Multiplicative " +
             "Reach Traits is ON.")]
    [SerializeField] private float mortarReachMultiplier = 1.25f;

    [Tooltip("Absolute reach bonus. Used only when Multiplicative Reach Traits is OFF.")]
    [SerializeField] private float mortarRangeBonus = 2.6f;

    [SerializeField] private float mortarAoeScale = 1.3f;

    [Header("Trait — Scarecrow (regen + damage)")]
    [SerializeField] private float scarecrowRegenPerSecond = 9f;
    [SerializeField] private float scarecrowDamageMultiplier = 1.3f;

    [Header("Trait — Gremlin (speed)")]
    [SerializeField] private float gremlinSpeedMultiplier = 1.35f;

    //  ULTIMATE / ENRAGE
    [Header("Ultimate Phase (Enrage)")]
    [Tooltip("Damage multiplier applied when the boss enrages.")]
    [SerializeField] private float enrageDamageMultiplier = 1.6f;
    [SerializeField] private float enrageSpeedMultiplier = 1.4f;
    [Range(0f, 1f)]
    [Tooltip("Fraction of max health restored on enraging.")]
    [SerializeField] private float enrageHealFraction = 0.25f;
    [Tooltip("Seconds the boss roars in place before the ultimate phase begins. " +
             "This is the player's cue to reposition.")]
    [SerializeField] private float enrageRoarDuration = 1.6f;

    [Header("Ultimate — Kaboom (chasing explosion barrage)")]
    [Tooltip("Seconds between barrages.")]
    [SerializeField] private float kaboomCooldown = 11f;

    [Tooltip("Explosions per barrage. Each one re-reads the player's position, so " +
             "the barrage CHASES them across the arena.")]
    [SerializeField] private int kaboomBlastCount = 5;

    [Tooltip("Damage radius of each blast. Also drives how big the explosion art " +
             "is drawn.")]
    [SerializeField] private float kaboomBlastRadius = 3.2f;

    [Tooltip("Radius growth per blast within one barrage. 1.12 = each is 12% wider " +
             "than the last, so the barrage escalates.")]
    [SerializeField] private float kaboomBlastGrowth = 1.12f;

    [SerializeField] private float kaboomBlastDamage = 30f;

    [Tooltip("Warning-ring time before each blast lands. This is the dodge window, " +
             "so it is the knob to raise if the barrage feels unfair.")]
    [SerializeField] private float kaboomTelegraphTime = 0.9f;

    [Tooltip("Gap between one blast detonating and the next warning ring appearing.")]
    [SerializeField] private float kaboomBlastInterval = 0.45f;

    [Tooltip("How far ahead of the player each blast leads their movement. 0 = drops " +
             "exactly where they stand (easy to walk out of); ~0.4 makes standing " +
             "still safe but running in a straight line dangerous.")]
    [SerializeField] private float kaboomLeadFactor = 0.35f;

    [Tooltip("Floor on the warning time, whatever Kaboom Telegraph Time says. This " +
             "is the guaranteed dodge window for every blast in the barrage — the " +
             "ring is on the ground this long before anything detonates.")]
    [SerializeField] private float kaboomMinTelegraph = 1.0f;

    [Tooltip("Minimum gap between consecutive craters. If the player barely moved, " +
             "the next blast is pushed this far along their heading instead of " +
             "stacking on the same spot — that is what turns the barrage into a " +
             "walking carpet rather than a pile of explosions on one tile.")]
    [SerializeField] private float kaboomMinBlastSpacing = 2.0f;

    [Tooltip("Hard cap on how long one barrage may run. Prevents a long chase from " +
             "turning into an endless bombardment the player can never step out of.")]
    [SerializeField] private float kaboomMaxBarrageSeconds = 9f;

    [Tooltip("Extra size multiplier on the explosion ART only — does not change the " +
             "damage radius. Tune so the sprite visually covers the blast.")]
    [SerializeField] private float kaboomArtScale = 1f;

    [Header("Explosion Art — 6 colour variants, picked at random")]
    [Tooltip("One row per variant folder: Assets/Art/EnemySprites/Boss4/Explosions/V1 " +
             "… V6. Select all the PNGs in a folder and drag them onto a row's Frames " +
             "array. Rows left empty are skipped, and if ALL are empty the barrage " +
             "falls back to the procedural shock rings — so this is optional.")]
    [SerializeField]
    private List<DevourerExplosionVariant> explosionVariants = new List<DevourerExplosionVariant>();

    [Tooltip("Seconds per explosion frame. 34 frames at 0.033 is a ~1.1s blast.")]
    [SerializeField] private float explosionFrameTime = 0.033f;

    [Tooltip("Playback speed multiplier for the explosion art. 1.7 plays it 1.7x " +
             "faster (a ~1.1s blast becomes ~0.66s). Separate from Explosion Frame " +
             "Time so the per-frame timing stays readable while the overall punch " +
             "is tuned.")]
    [SerializeField] private float explosionSpeedMultiplier = 1.7f;

    [Tooltip("SHORTCUT for filling Explosion Variants above.\n\n" +
             "Instead of six separate drags, select the PNGs of ALL six folders at " +
             "once (V1 through V6) and drop them here. They are split into variants " +
             "of Frames Per Variant each, in order.\n\n" +
             "Only used when Explosion Variants is empty, so an explicit setup always " +
             "wins.")]
    [SerializeField] private Sprite[] explosionFramesFlat;

    [Tooltip("Frames in each explosion folder. V1..V6 are 34 frames (00000-00033).")]
    [SerializeField] private int framesPerVariant = 34;

    //  DEATH / AUDIO
    [Header("Death")]
    [SerializeField] private float disintegrationDuration = 1.5f;
    [SerializeField] private int deathEnergyDropCount = 10;

    [Header("Audio — assign FMOD events when you have them (empty = silent)")]
    [SerializeField] private bool playAudio = true;
    [SerializeField] private FMODUnity.EventReference biteSound;
    [SerializeField] private FMODUnity.EventReference eatSound;
    [SerializeField] private FMODUnity.EventReference spawnSound;
    [SerializeField] private FMODUnity.EventReference enrageSound;
    [SerializeField] private FMODUnity.EventReference kaboomSound;
    [SerializeField] private FMODUnity.EventReference spikeShotSound;

    [Header("Obstacle Avoidance")]
    [Tooltip("How far ahead the boss looks for obstacles, as a multiple of its body " +
             "radius. Higher = starts steering earlier and takes wider berths.")]
    [SerializeField] private float avoidanceLookAheadFactor = 2.2f;

    [Tooltip("How hard it steers sideways around a blocker. 1 = mostly sideways, " +
             "0.3 = a gentle lean. Too high and it circles instead of arriving.")]
    [SerializeField] private float avoidanceStrength = 1f;

    // Effective seconds-per-frame after the speed multiplier.
    private float ExplosionFrameTime =>
        explosionFrameTime / Mathf.Max(0.05f, explosionSpeedMultiplier);

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    //  RUNTIME
    private EnemyController controller;
    private EnemyAnimationController animController;
    private SpriteRenderer bossSprite;
    private SmoothSpriteFlip smoothFlip;
    private DevourerDustEmitter dust;
    private DevourerGroundAura groundAura;

    private bool isDying;
    private bool enraged;
    private bool enrageSequenceRunning;

    private readonly HashSet<DevourerTrait> consumed = new HashSet<DevourerTrait>();
    private readonly List<DevourerFrozenMinion> liveMinions = new List<DevourerFrozenMinion>();
    private readonly List<GameObject> activeSpawnTelegraphs = new List<GameObject>();

    private Coroutine spawnRoutine, bomberRoutine, cloakRoutine, poisonRoutine, kaboomRoutine, enrageRoutine;
    private DevourerBiteTelegraph activeBiteTelegraph;

    // Trait-derived state.
    private bool traitCircleBite, traitConeBite, traitPoison, traitRegen;
    private float traitRangeBonus;
    private float traitReachMultiplier = 1f;
    private float traitAoeScale = 1f;
    private float baseAttackRange;

    // Scale bookkeeping (see the execution-order note at the top of the file).
    private Vector3 prefabScale = Vector3.one;
    private Vector3 restingScale = Vector3.one;
    private Vector3 displayScale = Vector3.one;
    private bool restingScaleDirty;

    // Cloak bookkeeping. We only ever write the ALPHA channel so the damage flash's
    // RGB still reads through — see LateUpdate.
    private float currentCloakAlpha = 1f;
    private bool cloakActive;

    private bool wasAttackingLastFrame;
    private bool devouring;

    private float trailTimer;
    private bool kaboomActive;
    private Coroutine spikeRoutine;
    private Coroutine biteLungeRoutine;

    // A stand-in transform placed at whatever the boss is currently focused on.
    //
    // Needed because returning `transform` as the priority target (the old way of
    // holding position) made the controller target the boss ITSELF, which produces a
    // zero-length facing vector and undefined flip. A proxy keeps the boss facing
    // the thing it is eating while still not walking anywhere.
    private Transform focusProxy;
    private bool drivingOwnFacing;
    private Transform huntTarget;
    private Vector3 devourFacingPos;
    private GameObject mealPassThrough;
    private float nextHuntAllowedTime;

    // The traits the Devourer has absorbed so far. Exposed for UI / debugging.
    public IReadOnlyCollection<DevourerTrait> ConsumedTraits => consumed;
    public bool IsEnraged => enraged;

    //  LIFECYCLE

    protected override void Awake()
    {
        // Build our own EnemyData before anything reads it. This is what removes
        // the "author an asset" step: duplicate any boss prefab, drop Boss4 on it,
        // and whatever EnemyData the donor prefab carried is simply replaced.
        //
        // Must happen BEFORE base.Awake(), because EnemyStats.Awake clones
        // enemyData and seeds maxHealth / currentHealth / armour from it.
        if (useInspectorStats)
            enemyData = BuildRuntimeEnemyData();

        if (enemyData != null)
        {
            maxHealth = enemyData.maxHealth;
            maxArmor = enemyData.maxArmor;
        }
        else
        {
            maxHealth = bossMaxHealth;
            maxArmor = bossMaxArmor;
            currentHealth = bossMaxHealth;
        }

        base.Awake();   // BaseBossStats: difficulty scaling + seeds bossArmor

        // AFTER base.Awake(), enemyData is a per-instance CLONE, so writing the
        // animation setup here can never touch a shared asset — the same guarantee
        // BerserkController relies on when it grows its own damage value.
        //
        // Still before EnemyAnimationController.Start(), which is where the clips
        // are actually read (it does `enemyData = enemyStats.enemyData` there).
        InjectAnimationClips();
    }

    // A throwaway EnemyData built from the inspector fields. Never saved, never
    // shared: EnemyStats.Awake immediately clones it and the original is garbage.
    private EnemyData BuildRuntimeEnemyData()
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.name = "Boss4_Devourer_Runtime";
        d.enemyName = "Devourer";
        d.maxHealth = bossMaxHealth;
        d.maxArmor = bossMaxArmor;
        d.moveSpeed = bossMoveSpeed;
        d.damage = biteDamage;
        d.mass = bossMass;
        return d;
    }

    // Writes the Walk / Bite clips into the cloned EnemyData and configures the
    // multi-folder animation mode.
    //
    // `useAnimationFolders` is the key flag: the loader concatenates
    // idle -> attack -> death into one array and REWRITES the frame ranges itself
    // from the array lengths, so no frame indices are ever hand-counted. All we
    // supply is hitFrame, which is 0-based relative to the BITE clip.
    private void InjectAnimationClips()
    {
        if (enemyData == null) return;
        if (walkFrames == null || walkFrames.Length == 0) return;   // nothing to inject

        if (biteFrames == null || biteFrames.Length == 0)
        {
            Debug.LogWarning("[Boss4] Walk Frames are assigned but Bite Frames are empty. " +
                             "The Devourer will walk but its bite will have no animation.");
        }

        enemyData.idleFrames = walkFrames;
        enemyData.attackFrames = biteFrames;
        enemyData.deathFrames = deathFrames;

        enemyData.useAnimationFolders = true;

        // The Devourer art is drawn facing LEFT. This flag is what makes the flip
        // system mirror it so the boss faces its target, and keeps the walk-lean
        // the right way round.
        enemyData.spriteFacesLeft = true;

        enemyData.animationSpeed = walkFrameTime;

        int walkCount = walkFrames.Length;
        int biteCount = biteFrames != null ? biteFrames.Length : 0;
        int deathCount = deathFrames != null ? deathFrames.Length : 0;

        enemyData.idle = new AnimationFrameRange(0, walkCount) { speedOverride = walkFrameTime };
        enemyData.attack = new AnimationFrameRange(walkCount, biteCount) { speedOverride = biteFrameTime };
        enemyData.death = new AnimationFrameRange(walkCount + biteCount, deathCount);

        // Clamped so a bad inspector value can't push the hit past the end of the
        // clip, which would mean the bite silently never dealt damage.
        int maxIndex = Mathf.Max(0, biteCount - 1);
        enemyData.hitFrame = Mathf.Clamp(biteHitFrame, 0, maxIndex);
        enemyData.parryFrameStart = Mathf.Clamp(enemyData.hitFrame - Mathf.Max(0, biteParryLeadFrames), 0, maxIndex);
        enemyData.parryFrameEnd = Mathf.Clamp(enemyData.hitFrame, 0, maxIndex);
    }

    protected override void Start()
    {
        if (healthBarPrefab == null)
            healthBarPrefab = FindAnyHealthBarPrefab();

        base.Start();   // EnemyStats builds the world bar; BaseBossStats shows the top bar

        bossSprite = GetComponent<SpriteRenderer>();
        smoothFlip = GetComponent<SmoothSpriteFlip>();
        controller = GetComponent<EnemyController>();
        animController = GetComponent<EnemyAnimationController>();

        ConfigureBossCollider();
        InitializeBossHealthBar();

        prefabScale = transform.localScale * Mathf.Max(0.01f, sizeMultiplier);
        restingScale = prefabScale;
        displayScale = prefabScale;

        // Apply immediately and hand the new base to the flip system, so the very
        // first frame is already the right size and SmoothSpriteFlip mirrors around
        // it rather than around the prefab's original scale.
        transform.localScale = prefabScale;
        if (smoothFlip != null) smoothFlip.RecaptureBaseScale();

        BuildExplosionVariantsFromFlatList();
        SetupDust();
        SetupGroundAura();
        HookController();
        ValidatePool();

        ApplyBiteWindUp();
        SetupParryIndicator();
        if (spikeVolleyEnabled) spikeRoutine = StartCoroutine(SpikeVolleyLoop());
        WarnOnScaleMismatch();
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    private GameObject FindAnyHealthBarPrefab()
    {
        var others = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);
        foreach (var s in others)
            if (s != this && s.healthBarPrefab != null) return s.healthBarPrefab;
        return null;
    }

    // World-space radius of the body, used for the shadow, the spawn ring and the
    // dust offset. Reads the LIVE collider so it stays correct whether the collider
    // came from the prefab or from the fields here.
    private float BodyRadius
    {
        get
        {
            var col = GetComponent<CircleCollider2D>();
            if (col != null) return col.radius * Mathf.Abs(transform.lossyScale.x);
            return bossColliderRadius;
        }
    }

    private void ConfigureBossCollider()
    {
        var col = GetComponent<CircleCollider2D>();

        if (col != null && useColliderFromPrefab)
        {
            // Respect what the prefab says. Only force the one property that would
            // actually break gameplay if it were wrong.
            col.isTrigger = false;
            return;
        }

        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = false;
        col.radius = bossColliderRadius;
        col.offset = new Vector2(0f, bossColliderOffsetY);
    }

    private void InitializeBossHealthBar()
    {
        if (HealthBar == null) return;

        HealthBar.Initialize(transform, maxHealth + maxArmor);

        float yOffset = healthBarExtraYPadding;
        if (bossSprite != null && bossSprite.sprite != null)
            yOffset = (bossSprite.bounds.max.y - transform.position.y) + healthBarExtraYPadding - healthBarYReduction;
        HealthBar.SetOffset(new Vector3(0f, yOffset, 0f));

        const int HEALTH_BAR_SORTING_ORDER = 4000;
        Canvas canvas = HealthBar.GetComponentInChildren<Canvas>(true);
        if (canvas == null) canvas = HealthBar.GetComponentInParent<Canvas>();
        if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = HEALTH_BAR_SORTING_ORDER; }
    }

    // Splits the flat drag-everything list into per-folder variants.
    private void BuildExplosionVariantsFromFlatList()
    {
        bool alreadyConfigured = false;
        if (explosionVariants != null)
            for (int i = 0; i < explosionVariants.Count; i++)
                if (explosionVariants[i] != null && explosionVariants[i].HasFrames) alreadyConfigured = true;

        if (alreadyConfigured) return;
        if (explosionFramesFlat == null || explosionFramesFlat.Length == 0)
        {
            Debug.LogWarning("[Boss4] No explosion art assigned. The Kaboom barrage and the Bomber " +
                             "mini-blasts will fall back to the procedural blast.\n" +
                             "Fill 'Explosion Variants', or select every PNG in " +
                             "Assets/Art/EnemySprites/Boss4/Explosions/V1..V6 and drop them all on " +
                             "'Explosion Frames Flat'.");
            return;
        }

        int per = Mathf.Max(1, framesPerVariant);
        if (explosionVariants == null) explosionVariants = new List<DevourerExplosionVariant>();
        explosionVariants.Clear();

        var flat = explosionFramesFlat;

        // Two different drag styles produce two different orderings, and guessing
        // wrong shuffles the frames into nonsense:
        //
        //   GROUPED     — you dragged folder by folder, or selected inside one folder
        //                 at a time:  V1/00000..00033, V2/00000..00033, ...
        //   INTERLEAVED — you used the Project search box (t:Sprite) to select across
        //                 all six folders at once. Search results sort by NAME, so it
        //                 comes out 00000 x6, 00001 x6, ... and slicing it into
        //                 chunks of 34 would build six explosions each made of six
        //                 different variants' first frames.
        //
        // Detect which by counting how many sprites at the head share one name: a run
        // of six identical names means interleaved, six variants deep.
        int runLength = 1;
        for (int i = 1; i < flat.Length; i++)
        {
            if (flat[i] == null || flat[0] == null) break;
            if (flat[i].name != flat[0].name) break;
            runLength++;
        }

        bool interleaved = runLength > 1 && flat.Length % runLength == 0;

        if (interleaved)
        {
            int variantCount = runLength;
            int framesEach = flat.Length / variantCount;

            for (int v = 0; v < variantCount; v++)
            {
                var frames = new Sprite[framesEach];
                for (int f = 0; f < framesEach; f++)
                    frames[f] = flat[f * variantCount + v];

                explosionVariants.Add(new DevourerExplosionVariant { frames = frames });
            }

            if (debugLogs)
                Debug.Log($"[Boss4] Explosion list looked interleaved ({variantCount} variants " +
                          $"x {framesEach} frames); de-interleaved it.");
        }
        else
        {
            for (int start = 0; start < flat.Length; start += per)
            {
                int count = Mathf.Min(per, flat.Length - start);

                // A trailing partial chunk means the list length isn't a clean
                // multiple — usually a missed frame. Keep it only if substantial, so
                // one stray sprite can't become a one-frame "explosion".
                if (count < per / 2) break;

                var v = new DevourerExplosionVariant { frames = new Sprite[count] };
                System.Array.Copy(flat, start, v.frames, 0, count);
                explosionVariants.Add(v);
            }
        }

        if (debugLogs)
            Debug.Log($"[Boss4] Built {explosionVariants.Count} explosion variant(s) " +
                      $"from {explosionFramesFlat.Length} sprites at {per} frames each.");
    }

    private void SetupGroundAura()
    {
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        float width = BodyRadius * 2.2f;
        groundAura = DevourerGroundAura.Attach(gameObject, width, -BodyRadius * 0.55f, layer);
    }

    private void SetupDust()
    {
        dust = GetComponent<DevourerDustEmitter>();
        if (dust == null) dust = gameObject.AddComponent<DevourerDustEmitter>();
        dust.sortingLayer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        dust.footOffsetY = -BodyRadius * 0.4f;
        dust.sizeMultiplier = 1.6f;                 // it's a big animal
        dust.distancePerPuff = 0.5f;
    }

    // Install the two composition hooks. Everything this boss does to steer
    // EnemyController goes through here — no shared movement code is modified.
    private void HookController()
    {
        if (controller == null)
        {
            Debug.LogError("[Boss4] No EnemyController on the prefab. The Devourer relies on it " +
                           "for movement, targeting and attack-cycle timing. Add one.");
            return;
        }

        baseAttackRange = controller.AttackRange;

        controller.PriorityTargetProvider = ResolveHuntTarget;
        controller.AttackHandlerOverride = OnBiteLanded;

        if (!biteSound.IsNull) controller.SetAttackSoundOverrideIfUnset(biteSound);
    }

    // Converts biteWindUpSeconds into a hit frame, then tells EnemyController to
    // re-read it.
    //
    // Must run in Start, and late. EnemyAnimationController.Start loads the clips and
    // REWRITES EnemyData.attack via `new AnimationFrameRange(start, count)` — whose
    // constructor resets speedOverride to 0. So the real seconds-per-frame is only
    // knowable after that has happened, and computing the hit frame in Awake would
    // use a number that no longer applies.
    //
    // EnemyController.ResolveFrameConfig() runs in ITS Start and caches hitFrame, so
    // changing EnemyData afterwards would be ignored — RefreshFrameConfig() exists
    // for exactly this case and its own comment says so: "a companion component that
    // adjusts this enemy's own cloned EnemyData during Start() calls this".
    private void ApplyBiteWindUp()
    {
        if (biteWindUpSeconds <= 0f || enemyData == null || controller == null) return;

        int frameCount = enemyData.attackFrames != null ? enemyData.attackFrames.Length : 0;
        if (frameCount < 2) return;

        float perFrame = enemyData.AttackAnimSpeed;
        if (perFrame <= 0.0001f) return;

        int wanted = Mathf.RoundToInt(biteWindUpSeconds / perFrame);
        int maxFrame = frameCount - 1;

        if (wanted > maxFrame)
        {
            // The clip is simply too short to hold the requested wind-up. Slow the
            // attack animation down to fit rather than silently delivering the hit
            // sooner than asked — a dodge window that quietly shrinks is worse than
            // a slightly slower animation.
            float needed = biteWindUpSeconds / maxFrame;
            enemyData.attack = new AnimationFrameRange(enemyData.attack.startFrame, frameCount)
            { speedOverride = needed };
            wanted = maxFrame;

            if (debugLogs)
                Debug.Log($"[Boss4] Bite clip too short for a {biteWindUpSeconds:F2}s wind-up; " +
                          $"slowed it to {needed:F3}s/frame.");
        }

        enemyData.hitFrame = Mathf.Clamp(wanted, 1, maxFrame);

        // Parry window closes on the hit frame and opens a few frames earlier, so the
        // parry is always available during the telegraph the player can see.
        enemyData.parryFrameStart = Mathf.Clamp(enemyData.hitFrame - Mathf.Max(0, biteParryLeadFrames), 0, maxFrame);
        enemyData.parryFrameEnd = enemyData.hitFrame;

        controller.RefreshFrameConfig();

        if (debugLogs)
            Debug.Log($"[Boss4] Bite wind-up {enemyData.HitTimeOffset:F2}s " +
                      $"(hitFrame {enemyData.hitFrame} of {frameCount}, {perFrame:F3}s/frame).");
    }

    //  PARRY

    // True while a successful parry (melee bite OR a reflected spike) has the boss
    // frozen. ParryStunEffect only freezes what EnemyController and the animation
    // controller own; everything Boss4 drives itself (lunge, telegraph, spike volley)
    // checks this so a parried boss actually stops attacking.
    private bool IsParryStunned
    {
        get
        {
            var stun = GetComponent<ParryStunEffect>();
            return stun != null && stun.IsStunActive;
        }
    }

    // The standard ParryIndicator, installed at runtime.
    //
    // ParryIndicator switches itself off for any enemy with an AttackHandlerOverride,
    // because that override normally means "ranged enemy, parry the projectile
    // instead". Boss4's override is NOT that: the plain bite still goes through
    // EnemyController.ApplyDamageToTarget, and the AOE bites go through TryShieldBite
    // below — both are real, parryable melee hits. So the mark is honest here, which
    // is exactly the case ShowDespiteAttackOverride exists for (the Brute uses it too).
    //
    // Runs after ApplyBiteWindUp so the parry frames are final. ParryIndicator.Start
    // runs next frame, after the fields below are set, and it re-reads the frame
    // config on every attack anyway.
    private void SetupParryIndicator()
    {
        if (!showParryIndicator) return;

        var indicator = GetComponent<ParryIndicator>();
        if (indicator == null)
        {
            indicator = gameObject.AddComponent<ParryIndicator>();
            indicator.Configure(ParryIndicatorHeight(), parryIndicatorSize);
        }

        indicator.ShowDespiteAttackOverride = true;
        indicator.ShowCondition = BiteIsParryable;
        indicator.YOffsetProvider = ParryIndicatorHeight;
    }

    // Is the attack cycle currently running a bite a shield can actually meet? Keeps
    // the '!' from promising a parry on cycles that never deliver a hit.
    private bool BiteIsParryable()
    {
        if (isDying || devouring || kaboomActive || enrageSequenceRunning) return false;
        if (controller == null) return false;

        var target = controller.CurrentTarget;
        if (target == null) return false;

        // Biting a frozen minion is a MEAL (ConsumeMinion), not an attack.
        if (target.GetComponentInParent<DevourerFrozenMinion>() != null) return false;

        return true;
    }

    // World height of the '!' above the pivot.
    private float ParryIndicatorHeight() => SpriteTopHeight() + parryIndicatorHeadPadding;

    // IParryStunStarsAnchor: ParryStunEffect's default puts the stars 0.7 above the
    // pivot, which is sized for regular enemies and lands in the middle of the
    // Devourer's head. Centre them just above the top of the sprite instead.
    public float ParryStunStarsHeight => SpriteTopHeight() + parryStunStarsPadding;

    // World height of the top of the sprite above the pivot. Read from the first Walk
    // frame rather than the live sprite so it doesn't jitter between frames (or jump
    // when the parry stun freezes a bite frame), and multiplied by the LIVE scale so it
    // follows Size Multiplier and the Brute growth.
    private float SpriteTopHeight()
    {
        Sprite reference = (walkFrames != null && walkFrames.Length > 0 && walkFrames[0] != null)
            ? walkFrames[0]
            : (bossSprite != null ? bossSprite.sprite : null);

        if (reference == null) return BodyRadius * 2.2f;

        return reference.bounds.max.y * Mathf.Abs(transform.lossyScale.y);
    }

    // Shield check for the AOE bites (Eye circle / Pitcher cone). Returns true when
    // the target is a player whose shield BLOCKED or PARRIED the bite, so no damage is
    // applied — exactly what EnemyController.ApplyDamageToTarget does for the plain
    // bite. Without this the AOE bites went straight through DevourerDamage and
    // ignored shields entirely, so the bite stopped being parryable the moment the
    // boss ate an Eye or a Pitcher.
    //
    // The shield's arc test is measured to this boss's position, which for a shape
    // centred on the boss is the right origin. A successful parry stuns the boss via
    // ShieldSystem.ApplyParry; any other player in the same bite is then spared too,
    // matching how a parry-stunned enemy's hits are skipped in ApplyDamageToTarget.
    private bool TryShieldBite(GameObject targetGO)
    {
        if (targetGO == null) return false;

        var playerStats = targetGO.GetComponent<PlayerStats>();
        if (playerStats == null) return false;             // towers / core: no shield

        if (IsParryStunned) return true;                   // an earlier parry cancelled it

        var weapon = targetGO.GetComponentInChildren<Weapon>();
        if (weapon == null) return false;

        var shield = weapon.GetShieldSystem();
        return shield != null && shield.TryBlockOrParry(gameObject);
    }

    // Sanity-checks the tuning against the boss's ACTUAL world size.
    //
    // Every distance on this component is in world units, but the prefab is authored
    // at a transform scale of 0.25 — so a 2.0 collider is only 0.5 world units of
    // body, and a 2.5 attack range is five body-radii of reach. The numbers look
    // reasonable in the Inspector and play completely wrong, which is exactly the
    // kind of mismatch that is invisible until someone measures it. So: measure it.
    private void WarnOnScaleMismatch()
    {
        float body = BodyRadius;
        if (body <= 0.01f) return;

        float reach = controller != null ? controller.AttackRange : 0f;
        float reachInBodies = reach / body;

        if (reachInBodies > 3.5f)
        {
            Debug.LogWarning(
                $"[Boss4] Attack range is {reach:F2} world units against a body radius of " +
                $"{body:F2} — that is {reachInBodies:F1} body-widths of reach, which will feel " +
                $"like the boss is biting from across the room.\n" +
                $"This prefab is scaled to {transform.lossyScale.x:F2}, so all distances need to " +
                $"be small. Try EnemyController.attackRange ≈ {body * 2.2f:F2}.");
        }

        if (biteAoeRadius > body * 4f)
        {
            Debug.LogWarning(
                $"[Boss4] Bite AOE radius is {biteAoeRadius:F2} against a body radius of " +
                $"{body:F2}. Try ≈ {body * 2.6f:F2}.");
        }

        if (minSpawnSeparation > spawnRingRadius * 0.9f)
        {
            Debug.LogWarning("[Boss4] Min Spawn Separation is nearly as large as Spawn Ring Radius; " +
                             "spawn points will be hard to place. Lower the separation.");
        }
    }

    private void ValidatePool()
    {
        int usable = 0;
        for (int i = 0; i < minionPool.Count; i++)
        {
            var e = minionPool[i];
            if (e == null) continue;
            if (!e.enabled) continue;
            if (e.prefab == null)
            {
                Debug.LogWarning($"[Boss4] Minion pool row '{e.trait}' is enabled but has no prefab. " +
                                 "It will be skipped, and is NOT required for the ultimate phase.");
                continue;
            }
            usable++;
        }

        if (usable == 0)
        {
            Debug.LogWarning("[Boss4] The minion pool is empty. The Devourer cannot absorb any traits, " +
                             "so it will enrage immediately and fight as a plain melee boss.");
        }
        else if (debugLogs)
        {
            Debug.Log($"[Boss4] Minion pool ready: {usable} usable trait type(s).");
        }
    }

    // ISpritePrewarm. The body frames are direct references on EnemyData (loaded
    // with the scene), so all that is left is to build the procedural VFX
    // textures once, up front, rather than mid-fight.
    public void PrewarmSpriteFolders()
    {
        DevourerSprites.Prewarm();
    }

    //  TARGETING

    // Fed to EnemyController.PriorityTargetProvider, so it pre-empts the normal
    // player/tower/core selection. Returning null hands targeting straight back
    // to the controller's default behaviour.
    private Transform ResolveHuntTarget()
    {
        if (isDying) { huntTarget = null; return null; }

        // Ultimate phase: nothing matters except the base. This is the "marches
        // straight toward the player's base" beat from the design.
        if (enraged)
        {
            huntTarget = null;
            var core = GameObject.FindGameObjectWithTag("Core");
            return core != null ? core.transform : null;
        }

        // Hold still during the enrage roar, and while mid-swallow — a boss that
        // keeps walking through its own eat animation undercuts the whole beat.
        // Both point at a proxy rather than at `transform`, so facing stays defined.
        if (devouring) return focusProxy != null ? focusProxy : transform;
        if (enrageSequenceRunning) return EnsureFocusProxy(transform.position + (Vector3)CurrentFacing());

        // Digesting. The boss ignores food for a while after a meal and fights
        // normally instead, so the player gets breathing room between absorptions
        // and the fight isn't just watching it graze.
        if (Time.time < nextHuntAllowedTime) { huntTarget = null; return null; }

        PruneMinions();

        Transform best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < liveMinions.Count; i++)
        {
            var m = liveMinions[i];
            if (m == null) continue;

            // IsHuntable, not IsEdible: a minion inside its grace period is off the
            // menu entirely. That grace window is the player's chance to kill it and
            // deny the buff, and it only means anything if the boss actually leaves
            // it alone for the duration.
            if (!m.IsHuntable) continue;
            if (consumed.Contains(m.Trait)) continue;   // already absorbed — leave it be

            float d = ((Vector2)m.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = m.transform; }
        }

        // Remember it ourselves. Facing must not depend on reading
        // EnemyController.CurrentTarget back: the controller re-acquires on its own
        // schedule and can briefly hold something else, and every frame it does, the
        // facing code would hand control back to the animation controller — which
        // faces the player. That flicker is what makes the boss snap backwards
        // mid-bite.
        huntTarget = best;
        return best;
    }

    private Transform EnsureFocusProxy(Vector3 at)
    {
        if (focusProxy == null)
        {
            var go = new GameObject("Boss4_FocusProxy");
            focusProxy = go.transform;
        }
        focusProxy.position = new Vector3(at.x, at.y, 0f);
        return focusProxy;
    }

    private void PruneMinions()
    {
        for (int i = liveMinions.Count - 1; i >= 0; i--)
            if (liveMinions[i] == null || !liveMinions[i].IsEdible)
                liveMinions.RemoveAt(i);
    }

    //  UPDATE

    private void Update()
    {
        if (isDying) return;

        TickRegen();
        TickBiteTelegraph();
        TickHuntFacing();
        UpdateMealCollisionPass();
    }

    // Scarecrow trait: passive regeneration.
    //
    // Deliberately NOT routed through EnemyStats.Heal(). That helper pushes
    // healthBar.UpdateHealth(currentHealth), but a boss world bar is initialised
    // with a capacity of maxHealth + maxArmor and expects currentHealth + bossArmor.
    // Healing through it would make the bar visibly DIP by the armour amount on
    // every regen tick. UpdateBossHealthBar owns both bars and gets it right.
    private void TickRegen()
    {
        if (!traitRegen) return;
        if (currentHealth <= 0f || currentHealth >= maxHealth) return;

        currentHealth = Mathf.Min(currentHealth + scarecrowRegenPerSecond * Time.deltaTime, maxHealth);
        UpdateBossHealthBar();
    }

    // Watches for the rising edge of an attack cycle and drops the ground
    // telegraph for whichever bite shape is currently unlocked. Polling the
    // controller's public IsAttacking is how Eye.cs does the same job, and it
    // keeps the telegraph in sync with the animation without a second timer.
    private void TickBiteTelegraph()
    {
        if (controller == null) return;

        bool attacking = controller.IsAttacking;

        // Rising edge of the attack cycle. Polling IsAttacking rather than hooking
        // the animation keeps the marker tied to the cycle that actually delivers
        // damage — the same thing Eye.cs does.
        //
        // Boss4 runs at execution order 10000, so by the time this polls, the cycle
        // that started this frame is already flagged: the telegraph goes up on the
        // same frame the wind-up begins, not one frame late.
        if (attacking && !wasAttackingLastFrame)
        {
            ShowBiteTelegraph();
            StartBiteLunge();
        }

        // Parried: EnemyController keeps the cycle flagged while it waits out the
        // clip, so the edge check below would leave the marker up for the whole stun.
        if (activeBiteTelegraph != null && IsParryStunned)
        {
            activeBiteTelegraph.Cancel();
            activeBiteTelegraph = null;
        }

        // Cycle ended early (target died, boss stunned): pull the marker so it can't
        // sit on the ground promising a bite that is no longer coming.
        if (!attacking && wasAttackingLastFrame && activeBiteTelegraph != null)
        {
            activeBiteTelegraph.Cancel();
            activeBiteTelegraph = null;
        }

        wasAttackingLastFrame = attacking;
    }

    // Obstacle avoidance, run in FixedUpdate.
    //
    // MUST be FixedUpdate, and Boss4 MUST run late (execution order 10000): the
    // controller writes rb.linearVelocity in ITS FixedUpdate, so anything we set from
    // Update is overwritten before it ever reaches the physics step. The earlier
    // Update-based nudge was being silently discarded for exactly this reason, which
    // is why the only thing that appeared to help was switching collisions off.
    //
    // EnemyController steers dead straight at its target with no avoidance, and a
    // frozen minion is immovable (solid collider, FreezeAll, never steps aside), so
    // anything between the boss and its goal is a wall it will grind against. We look
    // ahead along the current velocity, and if something solid is in the way we blend
    // in a sideways component — picking the side with more clearance so it slides
    // around rather than into a corner.
    private void FixedUpdate()
    {
        if (isDying || devouring || enrageSequenceRunning) return;
        if (controller == null || controller.ExternalMovementControl) return;

        var rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;

        Vector2 vel = rb.linearVelocity;
        if (vel.sqrMagnitude < 0.0004f) return;      // not trying to move

        Vector2 dir = vel.normalized;
        float speed = vel.magnitude;
        float bodyR = Mathf.Max(0.1f, BodyRadius);
        float lookAhead = bodyR * avoidanceLookAheadFactor;

        if (!TryFindBlocker(dir, bodyR, lookAhead, out Vector2 blockerPos)) return;

        // Which way around? Compare clearance on each side and take the roomier one.
        Vector2 left = new Vector2(-dir.y, dir.x);
        bool leftClear = !TryFindBlocker((dir * 0.5f + left).normalized, bodyR, lookAhead, out _);
        bool rightClear = !TryFindBlocker((dir * 0.5f - left).normalized, bodyR, lookAhead, out _);

        Vector2 side;
        if (leftClear && !rightClear) side = left;
        else if (rightClear && !leftClear) side = -left;
        else
        {
            // Both (or neither) clear: go around the side the obstacle ISN'T on, so
            // the choice is stable frame to frame instead of jittering.
            Vector2 toBlocker = blockerPos - (Vector2)transform.position;
            side = Vector2.Dot(toBlocker, left) > 0f ? -left : left;
        }

        // Keep some forward intent so it still makes progress while sliding.
        Vector2 steered = (dir * 0.35f + side * avoidanceStrength).normalized;
        rb.linearVelocity = steered * speed;
    }

    // Nearest solid thing in the path, ignoring ourselves and the meal we are
    // deliberately allowed to walk into.
    private bool TryFindBlocker(Vector2 dir, float radius, float distance, out Vector2 blockerPos)
    {
        blockerPos = Vector2.zero;

        var hits = Physics2D.CircleCastAll(transform.position, radius * 0.85f, dir, distance);
        float best = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null || col.isTrigger) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            // The current meal is pass-through by design.
            if (mealPassThrough != null &&
                (col.transform == mealPassThrough.transform ||
                 col.transform.IsChildOf(mealPassThrough.transform))) continue;

            // Any solid collider is an obstacle — enemies, buildings AND map geometry.
            // Naming component types here missed layout obstacles entirely (they carry
            // only a collider), which is why the boss ground against rocks instead of
            // stepping round them.
            //
            // The player is excluded: the boss is supposed to walk INTO the player.
            if (col.CompareTag("Player") || col.GetComponentInParent<PlayerStats>() != null) continue;

            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                blockerPos = col.bounds.center;
                found = true;
            }
        }

        return found;
    }

    // While hunting a minion, drive the flip ourselves (see DriveFacingTowards).    // While hunting a minion, drive the flip ourselves (see DriveFacingTowards).
    // Released the moment the target is anything the animation controller can
    // reason about on its own.
    private void TickHuntFacing()
    {
        if (devouring) return;   // DevourSequence owns facing for the whole swallow

        if (huntTarget == null) { ReleaseFacing(); return; }

        DriveFacingTowards(huntTarget.position);
    }

    private void ShowBiteTelegraph()
    {
        if (kaboomActive) return;    // barrage owns the screen; see OnBiteLanded

        // Every bite is telegraphed, not just the AOE ones.
        //
        // Previously this returned early unless the Eye or Pitcher trait was
        // absorbed, so the entire early fight had no warning: the boss walked up and
        // damage appeared. Nothing was wrong with the timing — there is a real
        // wind-up — but a wind-up the player cannot SEE is indistinguishable from
        // damage on contact.
        bool aoe = traitCircleBite || traitConeBite;
        if (!aoe && !alwaysTelegraphBite) return;

        float radius;
        float halfAngle;

        if (traitCircleBite)
        {
            radius = EffectiveAoeRadius();
            halfAngle = 180f;
        }
        else if (traitConeBite)
        {
            radius = EffectiveConeRadius();
            halfAngle = EffectiveConeHalfAngle();
        }
        else
        {
            // Plain bite — the cone you see BEFORE any Pitcher has been eaten.
            //
            // Sized to AttackRange x Bite Reach Tolerance, which is the exact boundary
            // OnBiteLanded uses to decide hit-or-whiff. The marker therefore shows the
            // true edge: step outside the drawn cone and the bite genuinely misses.
            //
            // Note this ignores Cone Radius Multiplier by design — that knob belongs to
            // the Pitcher's AOE cone. To make THIS cone bigger, raise the attack range
            // (it is a real reach change, not just a drawing) or widen Plain Bite
            // Telegraph Half Angle.
            float reach = controller != null ? controller.AttackRange : BodyRadius * 2f;
            radius = reach * Mathf.Max(1f, biteReachTolerance) * plainBiteTelegraphScale;
            halfAngle = plainBiteTelegraphHalfAngle;
        }

        // Wind-up straight off the EnemyData, so the marker can never drift out of
        // sync with the frame the damage actually lands on.
        float windUp = 0.35f;
        if (enemyData != null && enemyData.HitTimeOffset > 0.01f)
            windUp = enemyData.HitTimeOffset;

        Vector2 facing = CurrentFacing();
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        // Centre the marker on the projected IMPACT point, not on the boss.
        //
        // The lunge carries the head forward before the hit lands, so a telegraph
        // drawn where the boss is standing would under-report the danger by the whole
        // lunge distance — and the range check at impact measures from the lunged
        // position. Offsetting keeps the drawn shape and the real hit box the same
        // thing, which is the only reason the telegraph is worth trusting.
        Vector3 impactCentre = transform.position + (Vector3)(facing * LungeDistance());

        if (activeBiteTelegraph != null) activeBiteTelegraph.Cancel();
        activeBiteTelegraph = DevourerBiteTelegraph.Spawn(
            impactCentre, radius, halfAngle, facing, windUp,
            telegraphIdleColor, telegraphHotColor, layer);

        // A second cue on the BOSS itself, so the warning is readable even when the
        // ground marker is under the player or off the bottom of the screen.
        StartCoroutine(BiteChargeCue(windUp));
    }

    // How far the head travels on the snap, in world units.
    private float LungeDistance()
        => biteLungeEnabled ? BodyRadius * Mathf.Max(0f, biteLungeBodyRadii) : 0f;

    private void StartBiteLunge()
    {
        if (!biteLungeEnabled || isDying || devouring || kaboomActive) return;

        // A minion bite is handled by DevourSequence, which does its own (much bigger)
        // pounce. Running both would stack two movements on one attack.
        if (huntTarget != null) return;

        if (biteLungeRoutine != null) StopCoroutine(biteLungeRoutine);
        biteLungeRoutine = StartCoroutine(BiteLungeRoutine());
    }

    // Rear back, snap forward on the hit frame, recover.
    //
    // Timing is derived from the SAME wind-up the telegraph and the damage frame use,
    // so the head arrives forward exactly as the bite lands — the movement and the hit
    // are one event rather than an animation playing near a damage tick.
    //
    // Movement goes through ExternalMovementControl and MovePosition, like the devour
    // pounce: EnemyController owns this rigidbody's velocity every frame, so writing
    // position without taking the wheel would just be overwritten. Control is released
    // in a finally block, so an interrupted bite (death, stun, target lost) can never
    // strand the boss unable to move.
    private IEnumerator BiteLungeRoutine()
    {
        float windUp = 0.35f;
        if (enemyData != null && enemyData.HitTimeOffset > 0.01f)
            windUp = enemyData.HitTimeOffset;

        // Take the wheel UNCONDITIONALLY, and always hand it back in the finally.
        //
        // Taking it only when it was free looked tidier but soft-locks the boss: if a
        // previous lunge was cut short by StopCoroutine (which does not run finally
        // blocks) the flag is still set, the next lunge sees it held, declines to take
        // it — and therefore never releases it either. The boss stops moving for good.
        // Nothing else contends for it here: the devour pounce is the only other user
        // and StartBiteLunge refuses to run while devouring.
        if (controller != null) controller.ExternalMovementControl = true;

        var rb = GetComponent<Rigidbody2D>();
        Vector3 anchorPos = transform.position;
        Vector3 restScale = restingScale;

        try
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;

            Vector2 facing = CurrentFacing();
            float lunge = LungeDistance();
            float back = lunge * biteWindBackFraction;

            // PHASE 1 — rear back and crouch, trembling harder as it loads up.
            float windBackTime = windUp * 0.72f;
            float t = 0f;
            while (t < windBackTime && !isDying && !IsParryStunned)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / windBackTime);
                float eased = 1f - (1f - p) * (1f - p);

                // Re-read facing: the target can circle during a 0.4s wind-up, and a
                // lunge that commits early would swing at where they used to be.
                facing = CurrentFacing();

                Vector3 offset = (Vector3)(-facing * back * eased);

                if (biteShakeAmount > 0f)
                {
                    float amp = BodyRadius * biteShakeAmount * p;
                    Vector2 perp = new Vector2(-facing.y, facing.x);
                    offset += (Vector3)(perp * Mathf.Sin(t * 46f) * amp);
                }

                MoveBody(rb, anchorPos + offset);
                displayScale = new Vector3(restScale.x,
                                           restScale.y * (1f - biteSquashAmount * eased),
                                           restScale.z);
                yield return null;
            }

            // PHASE 2 — the snap. Ends exactly on the hit frame.
            Vector3 from = transform.position;
            Vector3 to = anchorPos + (Vector3)(facing * lunge);
            float snapTime = Mathf.Max(0.04f, windUp - windBackTime);
            t = 0f;
            // No stun check here on purpose: a melee parry lands ON the hit frame, i.e.
            // at the very end of this phase, and the snap + dust must play exactly as
            // they did before parry support. A reflected spike during the wind-up
            // already exits Phase 1 early; letting this short snap finish is harmless.
            while (t < snapTime && !isDying)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / snapTime);
                float eased = p * p;                     // accelerate into the target

                MoveBody(rb, Vector3.Lerp(from, to, eased));
                displayScale = new Vector3(restScale.x,
                                           restScale.y * (1f + biteSquashAmount * eased),
                                           restScale.z);
                yield return null;
            }

            // Impact dust at the feet — a heavy head landing should move ground.
            if (dust != null && lunge > 0.01f)
            {
                string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
                DevourerRoarPulse.Play(transform.position + Vector3.down * BodyRadius * 0.5f,
                                       BodyRadius * 0.8f, new Color(0.6f, 0.5f, 0.45f), layer);
            }

            // PHASE 3 — recover to where it started, easing out.
            Vector3 recoverFrom = transform.position;
            float recover = Mathf.Max(0.05f, biteRecoverTime);
            t = 0f;
            while (t < recover && !isDying)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / recover);
                float eased = 1f - (1f - p) * (1f - p);

                MoveBody(rb, Vector3.Lerp(recoverFrom, anchorPos, eased));

                // Slight overshoot below rest before settling, so the recovery has
                // weight instead of gliding back.
                float wobble = Mathf.Sin(p * Mathf.PI) * biteSquashAmount * 0.4f;
                displayScale = new Vector3(restingScale.x,
                                           restingScale.y * (1f - wobble),
                                           restingScale.z);
                yield return null;
            }
        }
        finally
        {
            displayScale = restingScale;
            if (controller != null) controller.ExternalMovementControl = false;
            biteLungeRoutine = null;
        }
    }

    // Motes gathering into the jaws through the wind-up, brightening as the bite
    // approaches. Ground telegraphs are easy to miss in a busy fight; a cue on the
    // body itself is not.
    private IEnumerator BiteChargeCue(float windUp)
    {
        float dur = Mathf.Max(0.05f, windUp);
        float t = 0f;
        float next = 0f;
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        while (t < dur && !isDying)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);

            if (t >= next)
            {
                next = t + Mathf.Lerp(0.07f, 0.025f, p);   // accelerates toward the hit

                Vector2 facing = CurrentFacing();
                Vector3 mouth = transform.position + (Vector3)(facing * BodyRadius * 0.85f);

                float ang = Random.Range(0f, Mathf.PI * 2f);
                float dist = Mathf.Lerp(BodyRadius * 2.2f, BodyRadius * 0.9f, p);
                Vector3 from = mouth + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.7f, 0f) * dist;

                var go = new GameObject("Devourer_BiteCharge");
                go.transform.position = from;

                var sr = DevourerSprites.NewSprite(go.transform, "Sprite",
                                                   Boss2VFXSprites.GetSoftDisc(),
                                                   layer, DevourerFXOrder.Air + 3,
                                                   Color.Lerp(new Color(0.75f, 0.3f, 1f),
                                                              new Color(1f, 0.35f, 0.3f), p));
                sr.sharedMaterial = DevourerSprites.Additive;

                go.AddComponent<DevourerSuckMote>()
                  .Play(sr, mouth, Mathf.Lerp(0.22f, 0.12f, p), BodyRadius * Random.Range(0.10f, 0.20f));
            }

            yield return null;
        }
    }

    // The direction the boss is facing, honouring the fact that the Devourer art
    // is drawn facing LEFT. SmoothSpriteFlip.IsFacingLeft reports the ACTUAL
    // mirrored state (EnemyAnimationController.ApplyFacingLeft already folded the
    // spriteFacesLeft flag in), so this needs no extra correction.
    // Points the sprite at `worldPos`, taking orientation away from
    // EnemyAnimationController for as long as we need it.
    //
    // This is necessary because that controller's FaceAttackTarget() only ever
    // considers the PLAYER (within 5 units) or the CORE — it has no concept of an
    // enemy target. When the Devourer bites a minion standing opposite the player,
    // the controller dutifully faced it at the player instead, so the boss ate
    // backwards. Driving the flip here is a local fix; changing FaceAttackTarget
    // would alter facing for every melee enemy in the game.
    private void DriveFacingTowards(Vector3 worldPos)
    {
        // EnemyAnimationController creates SmoothSpriteFlip in ITS Start. Boss4 runs
        // at execution order 10000 so Start ordering is normally fine, but caching it
        // once and giving up forever if it happened to be null is fragile — re-acquire.
        if (smoothFlip == null) smoothFlip = GetComponent<SmoothSpriteFlip>();

        if (!drivingOwnFacing)
        {
            if (animController != null) animController.SetOrientationDrivingEnabled(false);
            drivingOwnFacing = true;
        }

        float dx = worldPos.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.05f) return;      // too close to call; keep current facing

        bool targetIsLeft = dx < 0f;
        bool artFacesLeft = enemyData != null && enemyData.spriteFacesLeft;

        // The Bite clip may be drawn facing the opposite way from the Walk clip.
        // spriteFacesLeft is a single flag for the whole asset, so if the two clips
        // disagree there is no code fix — only this per-clip correction.
        if (invertFacingDuringBite && controller != null && controller.IsAttacking)
            artFacesLeft = !artFacesLeft;

        // Same XOR the animation controller applies in ApplyFacingLeft(): art drawn
        // facing LEFT means "face left" == DON'T mirror.
        bool mirror = targetIsLeft ^ artFacesLeft;

        if (smoothFlip != null) smoothFlip.SetFacingLeft(mirror);
        else if (bossSprite != null) bossSprite.flipX = mirror;   // last-resort fallback
    }

    private void ReleaseFacing()
    {
        if (!drivingOwnFacing) return;
        drivingOwnFacing = false;
        if (animController != null) animController.SetOrientationDrivingEnabled(true);
    }

    private Vector2 CurrentFacing()
    {
        // Prefer the live target: a cone that tracks what the boss is biting is far
        // more readable than one derived from a jittery velocity vector.
        if (controller != null && controller.CurrentTarget != null)
        {
            Vector2 to = (Vector2)controller.CurrentTarget.position - (Vector2)transform.position;
            if (to.sqrMagnitude > 0.01f) return to.normalized;
        }
        if (smoothFlip != null) return smoothFlip.IsFacingLeft ? Vector2.left : Vector2.right;
        return Vector2.right;
    }

    // Asserts our scale and cloak alpha AFTER every other component has had its
    // turn — YSortEntity rewrites localScale each frame, and would otherwise undo
    // the Brute trait's growth (the exact bug BerserkController documents).
    private void LateUpdate()
    {
        if (isDying) return;

        // Re-assert facing every frame of the swallow. Setting it once at the start
        // is not enough — the boss physically moves during the pounce, and anything
        // that re-enables orientation driving in between would face it at the player.
        if (devouring) DriveFacingTowards(devourFacingPos);

        if (restingScaleDirty && smoothFlip != null)
        {
            // Give the flip system the new base scale before we assert ours, or the
            // next flip will snap the boss back to the pre-growth size.
            transform.localScale = restingScale;
            smoothFlip.RecaptureBaseScale();
            restingScaleDirty = false;
        }

        // X IS LEFT ALONE. SmoothSpriteFlip owns it completely.
        //
        // The previous version read the sign off localScale.x and rewrote it as
        // |displayScale.x| * sign. That looks safe and is not: a smooth flip works by
        // animating x DOWN THROUGH ZERO and back out negative, so forcing the
        // magnitude back to full every frame destroyed the animation, and at the
        // moment x passed through 0 the sign read was meaningless — Mathf.Sign(0)
        // returns 1, snapping the boss to face right mid-flip. That is the
        // "flips backwards while eating": the flip could never complete.
        //
        // Squash and stretch therefore run on Y only, which still reads correctly
        // (crouch = shorter, lunge = taller). X changes go through restingScale +
        // RecaptureBaseScale above, which is a one-shot handoff rather than a
        // per-frame fight.
        var ls = transform.localScale;
        transform.localScale = new Vector3(ls.x, displayScale.y, displayScale.z);

        if (bossSprite != null && currentCloakAlpha < 0.999f)
        {
            // Alpha only. The damage flash writes RGB, and stomping it here would
            // make hits on a cloaked boss unreadable.
            var c = bossSprite.color;
            c.a = currentCloakAlpha;
            bossSprite.color = c;
        }
    }

    //  MINION SPAWNING

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, firstSpawnDelay));

        while (!isDying && !enraged)
        {
            // Nothing left to teach us — enrage instead of spawning.
            if (RemainingTraitCount() == 0)
            {
                TryBeginEnrage();
                yield break;
            }

            yield return StartCoroutine(SpawnWave());
            yield return new WaitForSeconds(Mathf.Max(1f, spawnInterval));
        }
    }

    private int RemainingTraitCount()
    {
        int n = 0;
        for (int i = 0; i < minionPool.Count; i++)
        {
            var e = minionPool[i];
            if (e == null || !e.enabled || e.prefab == null) continue;
            if (!consumed.Contains(e.trait)) n++;
        }
        return n;
    }

    // Types we still need, minus any that already have a living minion on the
    // field (when avoidDuplicateLiveTypes is on).
    private List<DevourerMinionEntry> BuildSpawnCandidates()
    {
        PruneMinions();

        var live = new HashSet<DevourerTrait>();
        if (avoidDuplicateLiveTypes)
            for (int i = 0; i < liveMinions.Count; i++)
                if (liveMinions[i] != null) live.Add(liveMinions[i].Trait);

        var candidates = new List<DevourerMinionEntry>();
        for (int i = 0; i < minionPool.Count; i++)
        {
            var e = minionPool[i];
            if (e == null || !e.enabled || e.prefab == null) continue;
            if (consumed.Contains(e.trait)) continue;
            if (live.Contains(e.trait)) continue;
            candidates.Add(e);
        }
        return candidates;
    }

    private IEnumerator SpawnWave()
    {
        var candidates = BuildSpawnCandidates();
        if (candidates.Count == 0) yield break;

        // Fisher-Yates so a wave is a random SUBSET of the remaining types rather
        // than always the first N in inspector order.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int count = Mathf.Min(Mathf.Max(1, spawnCount), candidates.Count);

        // Resolve every spawn point BEFORE the telegraph, and reuse those exact
        // positions for the spawn — that is what guarantees the warning circles
        // never lie about where a minion will appear (Boss2's summon does the same).
        var positions = new Vector3[count];
        float bossR = BodyRadius;
        float safeRadius = Mathf.Max(spawnRingRadius, bossR + 1f);

        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            float idealAngle = (360f / count) * i;

            if (!TryFindClearSpawnPoint(idealAngle, safeRadius, positions, placed, out Vector3 spot))
            {
                if (debugLogs)
                    Debug.Log($"[Boss4] No clear spawn point for {candidates[i].trait}; " +
                              "skipping it this wave.");
                continue;
            }

            positions[placed] = spot;
            candidates[placed] = candidates[i];   // keep entry aligned with its point
            placed++;
        }

        count = placed;
        if (count == 0) yield break;

        ShowSpawnTelegraphs(positions, spawnTelegraphTime);

        yield return new WaitForSeconds(Mathf.Max(0f, spawnTelegraphTime));
        ClearSpawnTelegraphs();

        if (isDying) yield break;

        for (int i = 0; i < count; i++)
        {
            SpawnMinion(candidates[i], positions[i]);

            // One frame between spawns so the engine isn't asked to instantiate
            // several enemies and run all their Awake/Start chains in one hitch.
            if (i < count - 1) yield return null;
        }
    }

    // Finds somewhere on (or near) the spawn ring that isn't already occupied.
    //
    // Without this, a wave drops minions at fixed angles regardless of what is
    // already standing there — and since frozen minions don't move, the previous
    // wave is still parked on those exact spots. The result is enemies spawning
    // inside each other.
    //
    // Walks outward from the ideal angle in alternating directions, then tries a
    // wider ring, and only gives up (taking the ideal point anyway) after exhausting
    // the search — a slightly overlapped spawn is much better than no spawn.
    private bool TryFindClearSpawnPoint(float idealAngleDeg, float ringRadius,
                                        Vector3[] taken, int takenCount, out Vector3 result)
    {
        result = Vector3.zero;
        float sep = Mathf.Max(0.1f, minSpawnSeparation);

        // 32 attempts across four progressively wider rings. Generous, because the
        // cost of failing is a skipped spawn and the cost of succeeding badly is a
        // minion stuck in the scenery.
        for (int attempt = 0; attempt < 32; attempt++)
        {
            // Alternate left/right of the ideal angle, widening each pair, and push
            // the ring out slightly every full lap.
            int step = (attempt + 1) / 2;
            float sign = (attempt % 2 == 0) ? 1f : -1f;
            float angle = idealAngleDeg + sign * step * 17f + Random.Range(-5f, 5f);
            float radius = ringRadius * (1f + (attempt / 8) * 0.22f);

            float rad = angle * Mathf.Deg2Rad;
            Vector3 candidate = transform.position
                                + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;

            if (!IsSpawnPointClear(candidate, sep, taken, takenCount)) continue;

            result = candidate;
            return true;
        }

        // Nothing clear anywhere on or near the ring.
        //
        // The old fallback returned the ideal point regardless, which is exactly how a
        // minion ended up embedded in a rock on a crowded map. Report failure instead:
        // the caller skips this minion, and because its trait is still unconsumed it
        // simply comes back in the next wave. A missing minion for one cycle is a far
        // smaller problem than one permanently stuck inside an obstacle.
        return false;
    }

    private bool IsSpawnPointClear(Vector3 at, float sep, Vector3[] taken, int takenCount)
    {
        // Against points already chosen in THIS wave (they aren't spawned yet, so a
        // physics query can't see them).
        for (int i = 0; i < takenCount; i++)
            if ((taken[i] - at).sqrMagnitude < sep * sep) return false;

        // Against anything already standing there.
        //
        // The rule is now simply "any SOLID collider blocks", rather than a list of
        // component types. The old version only looked for EnemyStats and
        // IEnergyConsumer, so it happily dropped minions inside map geometry: layout
        // obstacles are plain GameObjects on the Obstacle layer carrying a
        // CircleCollider2D and nothing else, so they matched neither test and were
        // invisible to the check. A minion spawned inside a rock is stuck in it.
        //
        // Triggers are ignored (pickup and detection volumes are not obstructions),
        // and the player is ignored — they move, so refusing to spawn near them would
        // let a player camp the ring and suppress spawns entirely.
        var hits = Physics2D.OverlapCircleAll(at, sep);
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null || col.isTrigger) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
            if (col.CompareTag("Player") || col.GetComponentInParent<PlayerStats>() != null) continue;

            return false;
        }

        return true;
    }

    private void SpawnMinion(DevourerMinionEntry entry, Vector3 at)
    {
        if (entry == null || entry.prefab == null) return;

        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        // Frost burst at the arrival point.
        DevourerBlast.Play(at, 1.6f, DevourerBlastPalette.Frost, layer, withDebris: false);
        PlayOneShot(spawnSound, at);

        GameObject minion = Instantiate(entry.prefab, at, Quaternion.identity);

        // Y-sort immediately. A freshly instantiated enemy sits at sortingOrder 0
        // until its own Start runs, which puts it UNDERNEATH the cartoon-grass
        // overlay (orders ~1000-1600) — it reads as invisible. Boss2 hit exactly
        // this with its summoned slimes; configuring the sort here closes the gap.
        EnsureMinionYSort(minion);

        var mStats = minion.GetComponent<EnemyStats>();
        if (mStats != null && !minionsDropEnergy)
            mStats.DisableEnergyDrops();

        var frozen = minion.AddComponent<DevourerFrozenMinion>();
        frozen.Initialize(this, entry.trait, minionGraceSeconds, minionWakeRadius,
                          minionsWakeOnDamage, minionsWakeOnProximity, entry.frozenPose);

        liveMinions.Add(frozen);

        if (debugLogs) Debug.Log($"[Boss4] Spawned frozen {entry.trait} minion at {at}.");
    }

    // Collision between the boss and ONE minion, toggled.
    //
    // Blanket-ignoring every minion (the previous fix for getting stuck) was too
    // blunt: it made the Devourer walk straight through its own statues, which looks
    // like they have no colliders at all. They do — they are solid to the player and
    // to everything else, and they stay solid to the boss too.
    //
    // The only exception is the ONE minion the boss is currently eating. It has to
    // be able to close the distance and put its jaws on the thing; with a solid
    // collider in the way it can never reach and just grinds against it forever.
    // Everything else it has to walk around, which is what the avoidance steering
    // in FixedUpdate is for.
    private void SetMinionCollisionIgnored(GameObject minion, bool ignore)
    {
        if (minion == null) return;

        var mine = GetComponentsInChildren<Collider2D>();
        var theirs = minion.GetComponentsInChildren<Collider2D>();

        for (int i = 0; i < mine.Length; i++)
        {
            if (mine[i] == null) continue;
            for (int j = 0; j < theirs.Length; j++)
            {
                if (theirs[j] == null) continue;
                Physics2D.IgnoreCollision(mine[i], theirs[j], ignore);
            }
        }
    }

    // Keeps the pass-through limited to the current meal, and restores collision the
    // moment the boss loses interest.
    private void UpdateMealCollisionPass()
    {
        GameObject want = huntTarget != null ? huntTarget.gameObject : null;
        if (want == mealPassThrough) return;

        if (mealPassThrough != null) SetMinionCollisionIgnored(mealPassThrough, false);
        mealPassThrough = want;
        if (mealPassThrough != null) SetMinionCollisionIgnored(mealPassThrough, true);
    }

    private void EnsureMinionYSort(GameObject minion)
    {
        const float sortPrecision = 10f;
        const int sortOrderBase = 1000;
        const float sortYOffset = -0.2f;   // EnemyController's non-boss value

        var ysort = minion.GetComponent<YSortEntity>();
        if (ysort == null) ysort = minion.AddComponent<YSortEntity>();
        ysort.sortPrecision = sortPrecision;
        ysort.sortOrderBase = sortOrderBase;
        ysort.sortYOffset = sortYOffset;

        var sr = minion.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.sortingOrder = sortOrderBase + Mathf.RoundToInt(-(minion.transform.position.y + sortYOffset) * sortPrecision);
    }

    private void ShowSpawnTelegraphs(Vector3[] positions, float duration)
    {
        ClearSpawnTelegraphs();
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        foreach (var pos in positions)
        {
            // World-pinned, NOT parented to the boss, so each marker stays on the
            // spot the minion will actually appear even as the boss walks away.
            // Deep cold blue, NOT near-white. An additive marker with high RGB and
            // high alpha blows out to a flat white disc over light terrain, which is
            // exactly what made spawns read as "a white circle appeared".
            var tel = DevourerBiteTelegraph.Spawn(
                pos, 1.6f, 180f, Vector2.right, duration,
                new Color(0.18f, 0.42f, 0.95f, 0.30f),
                new Color(0.45f, 0.80f, 1f, 0.55f), layer);
            if (tel != null) activeSpawnTelegraphs.Add(tel.gameObject);
        }
    }

    private void ClearSpawnTelegraphs()
    {
        for (int i = 0; i < activeSpawnTelegraphs.Count; i++)
            if (activeSpawnTelegraphs[i] != null) Destroy(activeSpawnTelegraphs[i]);
        activeSpawnTelegraphs.Clear();
    }

    // Callbacks from DevourerFrozenMinion.
    public void OnMinionWoke(DevourerFrozenMinion minion)
    {
        if (debugLogs && minion != null) Debug.Log($"[Boss4] {minion.Trait} minion woke up.");
    }

    public void OnMinionRemoved(DevourerFrozenMinion minion)
    {
        liveMinions.Remove(minion);
    }

    //  BITE — the single attack hook

    // Called by EnemyController at the hit frame, in place of its default melee
    // hit. One of three things happens: we eat a minion, or we deliver an AOE
    // bite, or we fall back to an ordinary single-target chomp.
    private void OnBiteLanded(Transform target)
    {
        if (isDying || target == null) return;

        // Mid-swallow the controller may still run an attack cycle against the focus
        // proxy. Ignore it: the devour sequence is the attack.
        if (devouring) return;

        // Parry-stunned (e.g. a reflected spike landed during the wind-up). The
        // controller's safety timer still delivers the hit frame, so drop it here —
        // a parried boss must not bite, and the AOE branches would otherwise still
        // hit buildings and unshielded players.
        if (IsParryStunned)
        {
            if (activeBiteTelegraph != null) { activeBiteTelegraph.Cancel(); activeBiteTelegraph = null; }
            return;
        }

        // During a barrage the boss does not also bite.
        //
        // The Eye trait's AOE fires on every attack cycle, so while a barrage was
        // running the player got shockwaves around the boss interleaved with blasts
        // around themselves — two damage sources with different origins going off at
        // once, which is unreadable. One attack at a time: the barrage IS the attack.
        if (kaboomActive)
        {
            if (activeBiteTelegraph != null) { activeBiteTelegraph.Cancel(); activeBiteTelegraph = null; }
            return;
        }

        // 1. Is this a meal?
        var minion = target.GetComponentInParent<DevourerFrozenMinion>();
        if (minion != null && minion.IsHuntable && !consumed.Contains(minion.Trait))
        {
            ConsumeMinion(minion);
            return;
        }

        PlayOneShot(biteSound, transform.position);

        float damage = BiteDamage();
        float aoeDamage = damage * Mathf.Max(0.05f, aoeDamageMultiplier);
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        var dedupe = new HashSet<GameObject>();

        // 2. AOE shapes, if unlocked. Circle and cone can BOTH be active (Eye and
        //    Pitcher both eaten); the shared dedupe set stops a target inside both
        //    shapes taking the bite twice.
        bool didAoe = false;

        if (traitCircleBite)
        {
            float r = EffectiveAoeRadius();
            DevourerDamage.ApplyRadial(transform.position, r, aoeDamage, gameObject,
                                       biteHitsBuildings, dedupe, OnBiteHitTarget,
                                       intercept: TryShieldBite);

            // A SHOCKWAVE, drawn procedurally — never the explosion PNGs.
            //
            // This is the single biggest source of "explosions keep going off on the
            // boss": the Eye bite fires on every attack cycle, which during the
            // ultimate phase means one 34-frame blast sprite on the Devourer every
            // couple of seconds, on top of the barrage. It also isn't an explosion —
            // it is the boss's own bite widened into a ring around its body, so it
            // should read as force radiating out of the boss, not as ordnance landing
            // on it.
            DevourerShockwave.Play(transform.position, r, new Color(0.85f, 0.35f, 1f), layer);
            didAoe = true;
        }

        if (traitConeBite)
        {
            float r = EffectiveConeRadius();
            float halfAngle = EffectiveConeHalfAngle();
            DevourerDamage.ApplyCone(transform.position, r, CurrentFacing(), halfAngle,
                                     aoeDamage, gameObject, biteHitsBuildings, dedupe, OnBiteHitTarget,
                                     intercept: TryShieldBite);
            didAoe = true;
        }

        if (didAoe)
        {
            DevourerSlash.Play(transform.position, CurrentFacing(), EffectiveAoeRadius(),
                               new Color(0.85f, 0.35f, 1f), layer);
            if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.12f, 0.1f);
            return;
        }

        // 3. No AOE traits yet — an ordinary chomp.
        //
        // RANGE IS RE-CHECKED HERE, at the moment of impact. EnemyController picks
        // the target when the swing begins and ApplyDamageToTarget never looks at
        // distance again — it resolves shields and parries, then applies damage
        // wherever the target now is. So a bite started at melee range connected even
        // if the player had sprinted clear during the wind-up, which is exactly the
        // "I dodged and still got hit" case: the dodge was real, the hit just wasn't
        // checking.
        //
        // The AOE branches above never had this problem — they query a radius around
        // the boss at impact time, so leaving the circle already worked.
        if (controller == null) return;

        float reach = controller.AttackRange * Mathf.Max(1f, biteReachTolerance);
        float distSqr = ((Vector2)target.position - (Vector2)transform.position).sqrMagnitude;

        if (distSqr > reach * reach)
        {
            // Whiffed. Snap the jaws on empty air so the miss is legible — otherwise
            // a successful dodge looks identical to the boss doing nothing.
            DevourerSlash.Play(transform.position, CurrentFacing(), BodyRadius * 1.4f,
                               new Color(0.55f, 0.55f, 0.65f), layer);
            if (debugLogs) Debug.Log("[Boss4] Bite whiffed — target left reach during the wind-up.");
            return;
        }

        // Routed through the controller's own damage path so shield block, parry and
        // the on-hit hooks behave exactly as they do for every other melee enemy.
        controller.ApplyDamageToTarget(target);
        OnBiteHitTarget(target.gameObject);

        DevourerSlash.Play(transform.position, CurrentFacing(), BodyRadius * 1.6f,
                           new Color(0.9f, 0.5f, 1f), layer);
    }

    // Per-target follow-up for a bite that connected. Currently just the Poisoner
    // DoT; kept separate so future on-hit traits have an obvious home.
    private void OnBiteHitTarget(GameObject target)
    {
        if (!traitPoison || target == null) return;

        // Buildings opt out by default — see poisonAffectsBuildings.
        if (!poisonAffectsBuildings && !target.CompareTag("Player")) return;

        DevourerPoisonDebuff.Apply(target, ScaleSpecialDamage(poisonAttackDps),
                                   poisonAttackDuration, gameObject);
    }

    // Bite damage: the EnemyData damage (which already carries augment, stage and
    // difficulty scaling via EnemyStats.Damage) if the asset defines one, else the
    // serialized fallback scaled the same way.
    private float BiteDamage()
    {
        float raw = (enemyData != null && enemyData.damage > 0f) ? Damage : ScaleDamage(biteDamage);
        return raw;
    }

    // Scales a hard-coded special-attack number. Boss specials use
    // BossStageDamageMultiplier, matching Boss1's laser and Boss3's tree hands.
    private float ScaleSpecialDamage(float raw) => raw * BossStageDamageMultiplier;

    private static Color PaletteColor(DevourerBlastPalette p)
    {
        switch (p)
        {
            case DevourerBlastPalette.Frost: return new Color(0.5f, 0.8f, 1f);
            case DevourerBlastPalette.Void: return new Color(0.85f, 0.35f, 1f);
            default: return new Color(1f, 0.5f, 0.15f);
        }
    }

    // The cone's ANGLE is fixed. Only its radius grows.
    //
    // This used to be biteConeHalfAngle * traitAoeScale, so eating the Mortar (which
    // sets traitAoeScale to 1.45) turned a 110-degree wedge into a 160-degree one —
    // nearly a full circle, at a longer range, for the same damage. That is why the
    // cone was fine after one meal and enormous after another.
    //
    // Mortar's job is "bigger AOE", and bigger means further, not wider: a cone that
    // widens toward a circle stops being a directional attack you can flank at all.
    private float EffectiveConeHalfAngle() => Mathf.Clamp(biteConeHalfAngle, 10f, 80f);

    // Unclamped size the AOE shapes are built from.
    private float AoeBasis()
    {
        float basis = biteAoeRadius;

        if (deriveAoeFromReach && controller != null)
            basis = controller.AttackRange * Mathf.Max(0.5f, aoeReachFactor);

        return basis * traitAoeScale;
    }

    // The circular (Eye) AOE.
    private float EffectiveAoeRadius()
    {
        float ceiling = BodyRadius * Mathf.Max(1.5f, maxAoeInBodies);
        return Mathf.Min(AoeBasis(), ceiling);
    }

    // The forward (Pitcher) cone, which reaches further than the circle.
    //
    // Its ceiling is scaled by the same multiplier, otherwise the clamp would quietly
    // flatten the cone back to circle length and the multiplier would do nothing —
    // which is exactly what made the cone shrink to almost nothing: one ceiling,
    // applied to both shapes, sized for the circle.
    private float EffectiveConeRadius()
    {
        float mul = Mathf.Max(1f, coneRadiusMultiplier);
        float ceiling = BodyRadius * Mathf.Max(1.5f, maxAoeInBodies) * mul;
        return Mathf.Min(AoeBasis() * mul, ceiling);
    }

    //  CONSUMPTION + TRAITS

    private void ConsumeMinion(DevourerFrozenMinion minion)
    {
        if (minion == null || !minion.IsEdible) return;

        DevourerTrait trait = minion.Trait;
        Vector3 at = minion.transform.position;
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        // Remove the minion WITHOUT killing it: eating is not a kill, so it pays no
        // energy, credits no tower, and — importantly — never triggers
        // GremlinController.Die(), which would split the Gremlin into two more.
        minion.ConsumeAndDestroy(transform, pullDuration: 0.22f);
        liveMinions.Remove(minion);

        // Digest before hunting again.
        nextHuntAllowedTime = Time.time + Mathf.Max(0f, eatCooldownSeconds);

        PlayOneShot(eatSound, transform.position);

        if (consumed.Add(trait))
        {
            // The trait lands on the GULP, not here — see DevourSequence. Applying
            // it at the peak of the swallow is what ties the mechanical change to a
            // visible beat instead of it beginning silently mid-animation.
            StartCoroutine(DevourSequence(trait, at, layer));
        }
    }

    // The swallow: anticipation, pounce, gulp, settle.
    //
    // The boss genuinely LEAVES THE GROUND for this. EnemyController exposes
    // ExternalMovementControl precisely so a companion behaviour can take the wheel
    // for a scripted move; with it set, the controller stops writing velocity and we
    // can arc the body without the two fighting over the rigidbody every frame.
    // It is restored in a finally-style cleanup so an interrupted devour (the boss
    // dies mid-pounce) can never leave the Devourer permanently unable to move.
    //
    // The scale work is squash-and-stretch, kept subtle: a real animal compresses
    // before it springs, stretches through the air, and its body swells only briefly
    // as the meal goes down. Big cartoon inflation reads as a balloon, not a
    // predator, so the gulp peaks at 1.12x rather than the 1.26x it used to.
    private IEnumerator DevourSequence(DevourerTrait trait, Vector3 mealPos, string layer)
    {
        devouring = true;
        EnsureFocusProxy(mealPos);
        DriveFacingTowards(mealPos);
        devourFacingPos = mealPos;

        bool tookControl = false;
        if (controller != null) { controller.ExternalMovementControl = true; tookControl = true; }

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        Vector3 from = transform.position;

        // Stop short of the meal so the jaws land on it rather than the body
        // sitting on top of it.
        Vector3 toMeal = mealPos - from;
        float dist = toMeal.magnitude;
        float stop = Mathf.Min(dist * 0.55f, Mathf.Max(0f, dist - BodyRadius * 0.75f));
        Vector3 landing = from + (dist > 0.01f ? toMeal / dist : Vector3.right) * stop;

        Vector3 rest = restingScale;
        Color tint = TraitColor(trait);

        // 1. CROUCH — compress. Weight settling before the spring.
        //    (Only Y is applied; X belongs to the flip system. See LateUpdate.)
        yield return ScaleTo(new Vector3(rest.x, rest.y * 0.84f, rest.z), 0.13f);

        // 2. POUNCE — an actual arc: forward along the ground, up and back down.
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.10f, 0.08f);
        float hop = Mathf.Max(0.35f, BodyRadius * 0.55f);
        float airTime = 0.20f;
        float t = 0f;
        while (t < airTime && !isDying)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / airTime);

            Vector3 ground = Vector3.Lerp(from, landing, p);
            ground.y += Mathf.Sin(p * Mathf.PI) * hop;      // parabola
            MoveBody(rb, ground);

            // Afterimages: a fading ghost of the boss's own sprite, laid down along
            // the arc. It is the cheapest way to make a fast move read as fast —
            // without it the pounce is over in 12 frames and the eye barely
            // registers that anything moved.
            trailTimer -= Time.deltaTime;
            if (trailTimer <= 0f)
            {
                trailTimer = 0.03f;
                EmitAfterimage(0.55f * (1f - p));
            }

            // Stretched thin at the apex, compressing again on the way down.
            float stretch = Mathf.Sin(p * Mathf.PI);
            displayScale = new Vector3(rest.x,
                                       rest.y * Mathf.Lerp(1f, 1.16f, stretch),
                                       rest.z);
            yield return null;
        }
        MoveBody(rb, landing);

        // 3. IMPACT — landing squash, dust, and the meal is taken.
        DevourerEatBurst.Play(mealPos, tint, layer);
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.26f, 0.16f);
        yield return ScaleTo(new Vector3(rest.x, rest.y * 0.86f, rest.z), 0.07f);

        // 4. GULP — the body swells briefly around the meal. Trait lands here, on a
        //    frame the player can actually see.
        DevourerEatBurst.Play(transform.position, tint, layer, moteCount: 20);

        // Deliberately NOT an explosion.
        //
        // Routing every effect through PlayExplosion put a full 34-frame blast on the
        // boss the instant it swallowed something — a big, loud detonation that dealt
        // no damage and threatened nobody, right where the player was standing next to
        // it. A swallow is matter being removed; the eat-vortex above already reads as
        // that. Anything that LOOKS like an explosion has to actually be one.
        DevourerRoarPulse.Play(transform.position, 1.4f, new Color(0.7f, 0.3f, 1f), layer);

        ApplyTrait(trait);
        if (debugLogs) Debug.Log($"[Boss4] Absorbed {trait}. {RemainingTraitCount()} remaining.\n{BuffSummary()}");

        rest = restingScale;   // Brute may have just changed this under us
        yield return ScaleTo(new Vector3(rest.x, rest.y * 1.14f, rest.z), 0.12f);

        // 5. SETTLE — a shrinking wobble, so the mass reads heavy and full rather
        //    than snapping back like rubber.
        rest = restingScale;
        yield return ScaleTo(new Vector3(rest.x, rest.y * 0.95f, rest.z), 0.11f);
        rest = restingScale;
        yield return ScaleTo(rest, 0.16f);

        if (tookControl && controller != null) controller.ExternalMovementControl = false;
        ReleaseFacing();
        devouring = false;

        if (RemainingTraitCount() == 0) TryBeginEnrage();
    }

    // A frozen copy of the current sprite left behind at this position, fading out.
    private void EmitAfterimage(float alpha)
    {
        if (bossSprite == null || bossSprite.sprite == null || alpha <= 0.02f) return;

        var go = new GameObject("Devourer_Afterimage");
        go.transform.position = transform.position;
        go.transform.rotation = transform.rotation;
        go.transform.localScale = transform.lossyScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = bossSprite.sprite;
        sr.flipX = bossSprite.flipX;
        sr.flipY = bossSprite.flipY;
        sr.sortingLayerName = bossSprite.sortingLayerName;
        sr.sortingOrder = bossSprite.sortingOrder - 1;   // behind the real body
        sr.color = new Color(0.75f, 0.45f, 1f, alpha);

        go.AddComponent<DevourerFadeSprite>().PlayPreserveScale(sr, 0.22f, 1f, 0.97f);
    }

    // Moves the body during a scripted lunge. Uses Rigidbody2D.MovePosition where
    // possible so the move is still resolved against colliders — the boss should
    // shove into walls, not through them.
    private void MoveBody(Rigidbody2D rb, Vector3 to)
    {
        if (rb != null && rb.bodyType != RigidbodyType2D.Static) rb.MovePosition(to);
        else transform.position = to;
    }

    private IEnumerator ScaleTo(Vector3 target, float duration)
    {
        Vector3 from = displayScale;
        float dur = Mathf.Max(0.02f, duration);
        float t = 0f;

        while (t < dur && !isDying)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            float eased = 1f - (1f - p) * (1f - p);
            displayScale = Vector3.Lerp(from, target, eased);
            yield return null;
        }
        if (!isDying) displayScale = target;
    }

    // Permanently applies one trait. Every branch is written to be idempotent-safe
    // (it is only ever reached once per trait, because `consumed` is a HashSet).
    private void ApplyTrait(DevourerTrait trait)
    {
        switch (trait)
        {
            case DevourerTrait.Eye:
                // Bite becomes a 360-degree AOE centred on the boss.
                traitCircleBite = true;
                break;

            case DevourerTrait.Bomber:
                if (bomberRoutine == null) bomberRoutine = StartCoroutine(BomberLoop());
                break;

            case DevourerTrait.Insect:
                if (cloakRoutine == null) cloakRoutine = StartCoroutine(CloakLoop());
                break;

            case DevourerTrait.Pitcher:
                traitConeBite = true;
                if (multiplicativeReachTraits) traitReachMultiplier *= Mathf.Max(1f, pitcherReachMultiplier);
                else traitRangeBonus += pitcherRangeBonus;
                PushAttackRange();
                break;

            case DevourerTrait.Brute:
                GrowFromBrute();
                break;

            case DevourerTrait.Poisoner:
                traitPoison = true;
                if (poisonRoutine == null) poisonRoutine = StartCoroutine(PoisonPoolLoop());
                break;

            case DevourerTrait.Mortar:
                if (multiplicativeReachTraits) traitReachMultiplier *= Mathf.Max(1f, mortarReachMultiplier);
                else traitRangeBonus += mortarRangeBonus;
                traitAoeScale *= Mathf.Max(1f, mortarAoeScale);
                PushAttackRange();
                break;

            case DevourerTrait.Scarecrow:
                traitRegen = true;
                MultiplyDamage(scarecrowDamageMultiplier);
                break;

            case DevourerTrait.Gremlin:
                MultiplyMoveSpeed(gremlinSpeedMultiplier);
                break;
        }
    }

    private static Color TraitColor(DevourerTrait t)
    {
        switch (t)
        {
            case DevourerTrait.Eye: return new Color(0.85f, 0.3f, 1f);
            case DevourerTrait.Bomber: return new Color(1f, 0.5f, 0.15f);
            case DevourerTrait.Insect: return new Color(0.6f, 1f, 0.7f);
            case DevourerTrait.Pitcher: return new Color(1f, 0.85f, 0.3f);
            case DevourerTrait.Brute: return new Color(1f, 0.35f, 0.3f);
            case DevourerTrait.Poisoner: return new Color(0.45f, 1f, 0.3f);
            case DevourerTrait.Mortar: return new Color(0.95f, 0.6f, 0.2f);
            case DevourerTrait.Scarecrow: return new Color(0.7f, 0.45f, 1f);
            case DevourerTrait.Gremlin: return new Color(0.4f, 1f, 0.95f);
        }
        return Color.white;
    }

    // stat mutation helpers
    //
    //  These write to enemyData, which is SAFE: EnemyStats.Awake replaced the asset
    //  with a per-instance clone, so nothing here can leak into the shared
    //  ScriptableObject or into another enemy. BerserkController mutates its clone
    //  the same way for exactly the same reason.

    private void MultiplyDamage(float mul)
    {
        if (enemyData == null || mul <= 0f) return;
        enemyData.damage *= mul;
    }

    private void MultiplyMoveSpeed(float mul)
    {
        if (enemyData == null || mul <= 0f) return;
        enemyData.moveSpeed *= mul;
    }

    private void GrowFromBrute()
    {
        // +max HP, and heal by the gained amount so the boss doesn't just get a
        // longer bar with the same absolute health in it.
        float gained = maxHealth * (bruteHealthMultiplier - 1f);
        maxHealth *= bruteHealthMultiplier;
        currentHealth = Mathf.Min(currentHealth + Mathf.Max(0f, gained), maxHealth);

        if (HealthBar != null) HealthBar.SetMaxHealth(maxHealth + maxArmor, currentHealth + bossArmor);
        UpdateBossHealthBar();

        restingScale = prefabScale * bruteScaleMultiplier;
        restingScaleDirty = true;

        // Both this and DevourSequence drive displayScale, and Brute is granted
        // mid-swallow — running them together would make the two coroutines fight
        // for the same value every frame. The swallow's settle wobbles already ease
        // into the new restingScale, so the growth is covered.
        if (!devouring) StartCoroutine(GrowScale(restingScale));

        // Bigger body, bigger footprints.
        if (dust != null) dust.sizeMultiplier = 1.6f * bruteScaleMultiplier;
    }

    private IEnumerator GrowScale(Vector3 target)
    {
        Vector3 from = displayScale;
        float dur = Mathf.Max(0.05f, bruteGrowDuration);
        float t = 0f;

        while (t < dur && !isDying)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // Overshoot then settle — a swelling gulp rather than a linear resize.
            float overshoot = Mathf.Sin(k * Mathf.PI) * 0.12f;
            float eased = 1f - (1f - k) * (1f - k);
            Vector3 baseNow = Vector3.Lerp(from, target, eased);
            displayScale = new Vector3(baseNow.x * (1f + overshoot),
                                       baseNow.y * (1f + overshoot * 0.6f),
                                       baseNow.z);
            yield return null;
        }
        displayScale = target;
    }

    // Pushes the trait range bonus into EnemyController's attack range.
    //
    // EnemyController stores attackRange as a PRIVATE serialized field with only a
    // read-only property, so this uses reflection. That is a deliberate trade:
    // reflection here means ZERO edits to shared movement code, and therefore zero
    // chance of regressing every other enemy in the game. It runs at most twice
    // per fight (Pitcher, Mortar), so the cost is irrelevant.
    //
    // If the field is ever renamed this degrades gracefully — a warning, and the
    // boss simply keeps its base reach. See Boss4_SETUP.md for the optional
    // two-line patch that replaces this with a clean public setter.
    private void PushAttackRange()
    {
        if (controller == null) return;

        float target = multiplicativeReachTraits
            ? baseAttackRange * traitReachMultiplier
            : baseAttackRange + traitRangeBonus;

        // Hard ceiling. Whatever the traits stack to, a melee boss must never be able
        // to bite from further than this, or there is no space left to fight it in.
        float ceiling = BodyRadius * Mathf.Max(2f, maxReachInBodies);
        if (target > ceiling)
        {
            if (debugLogs)
                Debug.Log($"[Boss4] Reach clamped {target:F2} -> {ceiling:F2} ({maxReachInBodies}x body).");
            target = ceiling;
        }

        var field = typeof(EnemyController).GetField(
            "attackRange",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field != null && field.FieldType == typeof(float))
        {
            field.SetValue(controller, target);
            if (debugLogs) Debug.Log($"[Boss4] Attack range -> {target:F2}");
            return;
        }

        Debug.LogWarning("[Boss4] Could not find EnemyController.attackRange; the Devourer's reach " +
                         "traits (Pitcher / Mortar) will not extend its bite range. The AOE size " +
                         "traits are unaffected.");
    }

    //  TRAIT BEHAVIOUR LOOPS

    // Fires a fan of spikes at a nearby player.
    //
    // Exists to close an obvious hole: while hunting or swallowing a minion the boss
    // ignores the player entirely, so walking up and hitting it was risk-free. This
    // gives it a way to punish that without interrupting the meal.
    private IEnumerator SpikeVolleyLoop()
    {
        while (!isDying)
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, spikeInterval));
            if (isDying) yield break;

            // The barrage owns the screen; don't layer a second projectile source.
            if (kaboomActive) continue;

            // Frozen by a parry: no volley. That's the reward for the parry.
            if (IsParryStunned) continue;

            if (spikesOnlyWhenDistracted && huntTarget == null && !devouring) continue;

            var player = PlayerRegistry.Instance != null
                ? PlayerRegistry.Instance.NearestAlive(transform.position, spikeRange, includeCloaked: false)
                : null;
            if (player == null) continue;

            yield return StartCoroutine(FireSpikeVolley(player.transform));
        }
    }

    private IEnumerator FireSpikeVolley(Transform target)
    {
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        Vector2 aim = ((Vector2)target.position - (Vector2)transform.position);
        if (aim.sqrMagnitude < 0.001f) yield break;
        aim.Normalize();

        int count = Mathf.Clamp(spikeCount, 1, 12);
        float spread = Mathf.Max(0f, spikeSpreadDegrees);
        float baseAngle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;

        var dirs = new Vector2[count];
        var speeds = new float[count];
        var spikes = new DevourerSpikeTelegraph[count];

        for (int i = 0; i < count; i++)
        {
            // Even fan across the spread, plus per-pellet jitter.
            //
            // Real buckshot is not a neat arc: the pellets leave on slightly random
            // headings and at slightly different speeds, so the pattern opens up and
            // loosens with distance. Without the jitter the fan is a rigid comb you
            // learn once and slip through the same gap every time; with it, the gaps
            // move, so you have to actually read each volley.
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float ang = baseAngle + Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t);
            ang += Random.Range(-spikeAngleJitter, spikeAngleJitter);

            float rad = ang * Mathf.Deg2Rad;
            dirs[i] = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            // Speed variance makes the volley arrive as a ragged burst rather than a
            // single wall, which is what sells it as a shotgun rather than a laser
            // grid — and it means a pellet you dodged is not automatically all of them.
            speeds[i] = 1f + Random.Range(-spikeSpeedVariance, spikeSpeedVariance);

            spikes[i] = DevourerSpikeTelegraph.Spawn(
                transform, dirs[i], BodyRadius, spikeColor, layer,
                Mathf.Max(0.05f, spikeTelegraphTime),
                bossSprite != null ? bossSprite.sortingOrder : DevourerFXOrder.Air);
        }

        yield return new WaitForSeconds(Mathf.Max(0.05f, spikeTelegraphTime));

        // Aborted mid-wind-up (dead, or parried while the spikes were coming out):
        // retract rather than fire.
        if (isDying || IsParryStunned)
        {
            for (int i = 0; i < count; i++) if (spikes[i] != null) spikes[i].Cancel();
            yield break;
        }

        // One sound per volley, on the launch frame (after the spikes protrude).
        PlayOneShot(spikeShotSound, transform.position);

        for (int i = 0; i < count; i++)
        {
            // Launch from a point on the hide along that pellet's own line, so the
            // pattern is already diverging as it leaves rather than starting from one
            // point and only separating later.
            Vector3 origin = transform.position + (Vector3)(dirs[i] * BodyRadius * 1.05f);

            if (spikes[i] != null) spikes[i].Release();
            LaunchSpike(origin, dirs[i], target, layer, speeds[i]);

            // A few milliseconds between pellets — a real barrel does not release them
            // in perfect lockstep, and the stagger reads as force rather than a grid.
            if (spikeStagger > 0f && i < count - 1)
                yield return new WaitForSeconds(spikeStagger);
        }

        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.10f, 0.08f);
    }

    private static bool _warnedSpikePrefabSpread;

    private void LaunchSpike(Vector3 origin, Vector2 dir, Transform target, string layer,
                             float speedScale)
    {
        float dmg = ScaleSpecialDamage(spikeDamage);
        float speed = spikeSpeed * speedScale;

        // EnemyProjectile can only be used for a SINGLE aimed shot.
        //
        // Its Initialize() recomputes the heading itself — `flightDirection =
        // (target.position - transform.position).normalized` — and discards whatever
        // rotation we spawned it with. Fine for the Pitcher, which fires one dart, but
        // it means every pellet of a spread re-aims at the player and they all
        // converge into a single line. That is exactly the "they all come straight at
        // me" behaviour.
        //
        // So: prefab path only when there is one pellet; the spread uses our own spike,
        // which honours its launch direction and damages anything it touches along the
        // way rather than only at the target point.
        bool singleShot = Mathf.Clamp(spikeCount, 1, 12) == 1;

        if (spikeProjectilePrefab != null && singleShot)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var go = Instantiate(spikeProjectilePrefab, origin,
                                 Quaternion.AngleAxis(angle, Vector3.forward));

            var proj = go.GetComponent<EnemyProjectile>();
            if (proj != null)
            {
                proj.Initialize(controller, target, dmg, speed, spikeLifetime, homing: false);
                return;
            }

            Destroy(go);
            Debug.LogWarning($"[Boss4] Spike Projectile Prefab '{spikeProjectilePrefab.name}' has no " +
                             "EnemyProjectile component; using procedural spikes.");
        }
        else if (spikeProjectilePrefab != null && !_warnedSpikePrefabSpread)
        {
            _warnedSpikePrefabSpread = true;
            Debug.Log("[Boss4] Spike Projectile Prefab is ignored while Spike Count > 1: " +
                      "EnemyProjectile re-aims each shot at the target, which collapses a " +
                      "spread into one line. Using procedural spikes instead. Set Spike Count " +
                      "to 1 if you want the parryable dart.");
        }

        var spike = DevourerSpike.Launch(origin, dir, speed, dmg, spikeLifetime,
                                         gameObject, biteHitsBuildings, spikeColor, layer);
        if (spike != null)
            spike.ConfigureParry(spikesParryable, spikeParryReactRadius, spikeParryCatchWidth,
                                 spikeParryReflectMultiplier, spikeParryReturnSpeedMultiplier);
    }

    // Bomber trait: telegraphed mini-explosions at random points around the boss.
    private IEnumerator BomberLoop()
    {
        while (!isDying)
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, bomberInterval));
            if (isDying) yield break;

            StartCoroutine(MiniBlast(PickMiniBlastTarget()));
        }
    }

    // Mini-blasts land near the player, scattered around them rather than around the
    // boss.
    //
    // Scattering around the boss meant most of them detonated in empty ground the
    // player was nowhere near — noise that threatened nothing while still competing
    // with the real telegraphs for attention. Every explosion this boss draws should
    // be a threat the player has to answer.
    //
    // The scatter is deliberately wide enough that they are dodgeable rather than
    // homing: it puts blasts AROUND you, not on you.
    private Vector3 PickMiniBlastTarget()
    {
        var player = PlayerRegistry.Instance != null
            ? PlayerRegistry.Instance.NearestAlive(transform.position, bomberScatterRadius * 2.5f,
                                                   includeCloaked: true)
            : null;

        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector3 dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);

        if (player != null)
        {
            float spread = Mathf.Max(0.8f, bomberBlastRadius * 1.6f);
            return player.transform.position + dir * Random.Range(0f, spread);
        }

        // Nobody in reach: fall back to a scatter around the boss, which at least
        // still covers the ground a player would have to cross to approach.
        float dist = Random.Range(1f, Mathf.Max(1.2f, bomberScatterRadius));
        return transform.position + dir * dist;
    }

    private IEnumerator MiniBlast(Vector3 at)
    {
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        float radius = bomberBlastRadius * traitAoeScale;

        // Telegraph first: every damaging area this boss creates is signalled
        // before it lands, so a hit is always the player's read, not a surprise.
        var tel = DevourerBiteTelegraph.Spawn(
            at, radius, 180f, Vector2.right, Mathf.Max(0.1f, bomberTelegraphTime),
            new Color(1f, 0.55f, 0.15f, 0.28f), new Color(1f, 0.35f, 0.1f, 0.6f), layer);

        yield return new WaitForSeconds(Mathf.Max(0.1f, bomberTelegraphTime));
        if (tel != null) tel.Cancel();
        if (isDying) yield break;

        PlayExplosion(at, radius, DevourerBlastPalette.Fire, layer);

        DevourerDamage.ApplyRadial(at, radius, ScaleSpecialDamage(bomberBlastDamage),
                                   gameObject, biteHitsBuildings, null, OnBiteHitTarget);

        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.1f, 0.08f);
    }

    // Insect trait: fade out for `cloakDuration` every `cloakInterval`.
    //
    // This is a VISUAL cloak. It deliberately does NOT make the boss untargetable:
    // tower targeting lives in shared code, and making a boss untargetable from
    // here would need changes there — the kind of change that quietly breaks other
    // enemies. The read for the player is "hard to see", not "immune".
    private IEnumerator CloakLoop()
    {
        while (!isDying)
        {
            yield return new WaitForSeconds(Mathf.Max(1f, cloakInterval));
            if (isDying) yield break;

            cloakActive = true;

            // TELEGRAPH — camouflage spooling up. The boss flickers between solid
            // and partly transparent at an accelerating rate, with a chromatic
            // shimmer washing over it. Reads as a system engaging, and gives the
            // player a beat to mark its position before it goes.
            yield return StartCoroutine(CloakSpoolUp());
            if (isDying) yield break;

            yield return StartCoroutine(FadeCloak(currentCloakAlpha, Mathf.Clamp01(cloakAlpha), cloakFadeTime));
            yield return new WaitForSeconds(Mathf.Max(0.1f, cloakDuration));

            // Decloak with a shimmer too, so reappearing is equally readable.
            EmitCloakShimmer(1f);
            yield return StartCoroutine(FadeCloak(Mathf.Clamp01(cloakAlpha), 1f, cloakFadeTime));
            cloakActive = false;

            // Restore full opacity exactly, so a rounding drift can never leave the
            // boss permanently slightly transparent.
            currentCloakAlpha = 1f;
            if (bossSprite != null)
            {
                var c = bossSprite.color; c.a = 1f; bossSprite.color = c;
            }
        }
    }

    private IEnumerator CloakSpoolUp()
    {
        float dur = Mathf.Max(0f, cloakTelegraphTime);
        if (dur <= 0.01f) yield break;

        float t = 0f;
        float nextBlink = 0f;
        bool dipped = false;
        float nextShimmer = 0f;

        while (t < dur && !isDying)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);

            // Blink rate accelerates toward the vanish — the classic "charging up"
            // read, and it means the last second is unmistakable.
            if (t >= nextBlink)
            {
                float period = Mathf.Lerp(0.22f, 0.06f, p);
                nextBlink = t + period;
                dipped = !dipped;
                // Dips get deeper as it spools up, but never below halfway, so the
                // silhouette stays trackable through the whole telegraph.
                currentCloakAlpha = dipped ? Mathf.Lerp(0.85f, 0.5f, p) : 1f;
            }

            if (t >= nextShimmer)
            {
                nextShimmer = t + Mathf.Lerp(0.14f, 0.05f, p);
                EmitCloakShimmer(p);
            }

            yield return null;
        }

        currentCloakAlpha = 1f;
    }

    // A vertical band of light sweeping over the body — the visual shorthand for
    // active camouflage. Drawn from the boss's own current sprite so it always
    // matches the pose, and additive so it reads as light rather than paint.
    private void EmitCloakShimmer(float intensity)
    {
        if (bossSprite == null || bossSprite.sprite == null) return;

        var go = new GameObject("Devourer_CloakShimmer");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = bossSprite.sprite;
        sr.flipX = bossSprite.flipX;
        sr.flipY = bossSprite.flipY;
        sr.sharedMaterial = DevourerSprites.Additive;
        sr.sortingLayerName = bossSprite.sortingLayerName;
        sr.sortingOrder = bossSprite.sortingOrder + 1;

        // Cyan-white, brightening as the vanish approaches.
        float a = Mathf.Lerp(0.12f, 0.38f, intensity);
        sr.color = new Color(0.55f, 0.9f, 1f, a);

        var fade = go.AddComponent<DevourerFadeSprite>();
        fade.PlayPreserveScale(sr, Mathf.Lerp(0.22f, 0.12f, intensity), 1f, 1.04f);
    }

    private IEnumerator FadeCloak(float from, float to, float duration)
    {
        float t = 0f;
        float dur = Mathf.Max(0.05f, duration);
        while (t < dur && !isDying)
        {
            t += Time.deltaTime;
            currentCloakAlpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            yield return null;
        }
        currentCloakAlpha = to;
    }

    // Poisoner trait: drops lingering pools as the boss moves around.
    private IEnumerator PoisonPoolLoop()
    {
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        while (!isDying)
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, poisonPoolInterval));
            if (isDying) yield break;

            // Pools are area denial, so they go where the player has to walk: near
            // them when they are close, around the boss otherwise so approaching it
            // still costs something.
            var target = PlayerRegistry.Instance != null
                ? PlayerRegistry.Instance.NearestAlive(transform.position, poisonPoolScatter,
                                                       includeCloaked: true)
                : null;

            Vector3 origin = target != null ? target.transform.position : transform.position;
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(0f, Mathf.Max(0.5f, poisonPoolScatter * 0.5f));
            Vector3 at = origin + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * dist;

            DevourerPoisonPool.Spawn(at, poisonPoolRadius * traitAoeScale,
                                     ScaleSpecialDamage(poisonPoolDps), poisonPoolLifetime,
                                     gameObject, layer, biteHitsBuildings);
        }
    }

    //  ULTIMATE PHASE

    private void TryBeginEnrage()
    {
        if (enraged || enrageSequenceRunning || isDying) return;
        enrageRoutine = StartCoroutine(EnrageSequence());
    }

    private IEnumerator EnrageSequence()
    {
        enrageSequenceRunning = true;

        // Stop spawning: the pool is exhausted.
        if (spawnRoutine != null) { StopCoroutine(spawnRoutine); spawnRoutine = null; }
        ClearSpawnTelegraphs();

        PlayOneShot(enrageSound, transform.position);

        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";

        // Roar: a stack of expanding rings while the boss holds still. The
        // PriorityTargetProvider returns our own transform during this window, so
        // EnemyController parks the boss without us fighting it for the velocity.
        float t = 0f;
        float dur = Mathf.Max(0.2f, enrageRoarDuration);
        float nextRing = 0f;
        while (t < dur && !isDying)
        {
            t += Time.deltaTime;
            if (t >= nextRing)
            {
                nextRing = t + 0.22f;

                // Pressure rings, not explosions. This is a roar: it deals no damage
                // and the player cannot dodge it, so drawing seven fireballs on the
                // boss taught them to fear something harmless while burying the
                // telegraphs that DO matter.
                DevourerRoarPulse.Play(transform.position, Mathf.Lerp(1.6f, 4.5f, t / dur),
                                       new Color(1f, 0.4f, 0.2f), layer);
            }
            if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.05f, 0.05f);
            yield return null;
        }

        if (isDying) { enrageSequenceRunning = false; yield break; }

        // Massive stat boosts.
        MultiplyDamage(enrageDamageMultiplier);
        MultiplyMoveSpeed(enrageSpeedMultiplier);

        if (enrageHealFraction > 0f)
        {
            currentHealth = Mathf.Min(currentHealth + maxHealth * enrageHealFraction, maxHealth);
            UpdateBossHealthBar();   // owns BOTH bars; never poke HealthBar directly here
        }

        enraged = true;
        enrageSequenceRunning = false;
        if (groundAura != null) groundAura.SetEnraged(true);

        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.5f, 0.35f);
        if (debugLogs) Debug.Log("[Boss4] ULTIMATE PHASE — marching on the core.");

        kaboomRoutine = StartCoroutine(KaboomLoop());
    }

    private IEnumerator KaboomLoop()
    {
        // A full cooldown before the first one, so enraging isn't instantly lethal.
        yield return new WaitForSeconds(Mathf.Max(2f, kaboomCooldown));

        while (!isDying)
        {
            yield return StartCoroutine(PerformKaboom());
            yield return new WaitForSeconds(Mathf.Max(2f, kaboomCooldown));
        }
    }

    // The chasing barrage. Each blast re-reads the player's position at the moment
    // its warning ring appears, so the sequence walks across the arena after them
    // instead of detonating harmlessly where they used to be.
    //
    // Every blast is ring-telegraphed first, same contract as Boss2's meteor: the
    // ring shows exactly where damage will land, and the dodge window is
    // kaboomTelegraphTime. Blasts are ANCHORED where the ring appeared — once it is
    // down, that spot is committed and running out of it always works.
    private IEnumerator PerformKaboom()
    {
        if (isDying) yield break;

        kaboomActive = true;
        try { yield return RunKaboom(); }
        finally { kaboomActive = false; }
    }

    private IEnumerator RunKaboom()
    {
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        int blasts = Mathf.Max(1, kaboomBlastCount);

        // One warning time for the whole barrage, floored so no blast can ever be
        // quicker to land than the player can react to.
        float telegraph = Mathf.Max(kaboomTelegraphTime, kaboomMinTelegraph);

        float started = Time.time;
        bool havePrevious = false;
        Vector3 previous = Vector3.zero;

        for (int i = 0; i < blasts && !isDying; i++)
        {
            // Bounded: a barrage cannot outlive its budget however far the chase goes.
            if (Time.time - started > kaboomMaxBarrageSeconds)
            {
                if (debugLogs) Debug.Log("[Boss4] Kaboom barrage hit its time budget; ending early.");
                yield break;
            }

            float radius = kaboomBlastRadius
                           * Mathf.Pow(Mathf.Max(1f, kaboomBlastGrowth), i)
                           * traitAoeScale;

            if (!TryPickBlastTarget(i, blasts, out Vector3 at))
            {
                // No living player to chase — end the barrage rather than dropping
                // blasts on ourselves.
                if (debugLogs) Debug.Log("[Boss4] Kaboom found no player to chase; ending barrage.");
                yield break;
            }

            // Space it off the last crater so the run WALKS across the ground.
            // Stacking blast after blast on a stationary player is not a carpet
            // bombing, it is a single spot detonating repeatedly — and it is
            // unreadable, because the second ring lands inside the first one's art.
            if (havePrevious)
            {
                Vector3 delta = at - previous;
                float gap = delta.magnitude;
                if (gap < kaboomMinBlastSpacing)
                {
                    Vector3 push = gap > 0.01f ? delta / gap : Random.insideUnitCircle.normalized;
                    at = previous + push * kaboomMinBlastSpacing;
                }
            }

            previous = at;
            havePrevious = true;

            if (debugLogs)
            {
                var p = PlayerRegistry.Instance != null
                    ? PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true)
                    : null;
                float dToPlayer = p != null ? Vector2.Distance(at, p.transform.position) : -1f;
                float dToBoss = Vector2.Distance(at, transform.position);
                Debug.Log($"[Boss4] Kaboom {i + 1}/{blasts} at {at} — {dToPlayer:F2} from player, " +
                          $"{dToBoss:F2} from boss, r={radius:F2}");
            }

            var ring = DevourerBiteTelegraph.Spawn(
                at, radius, 180f, Vector2.right, telegraph,
                new Color(1f, 0.35f, 0.2f, 0.26f), new Color(1f, 0.12f, 0.1f, 0.6f), layer);

            PlayOneShot(kaboomSound, at);

            yield return new WaitForSeconds(telegraph);
            if (ring != null) ring.Cancel();
            if (isDying) yield break;

            DetonateBlast(at, radius, layer);

            if (i < blasts - 1)
                yield return new WaitForSeconds(Mathf.Max(0.05f, kaboomBlastInterval));
        }
    }

    // Where the next blast lands: on the player, led by their movement.
    //
    // Returns false when there is no living player to chase. The old fallback
    // scattered blasts around the BOSS, which is how a barrage ended up detonating on
    // top of the Devourer over and over — threatening nobody, obscuring everything,
    // and reading as if the boss were bombing itself. A blast with no target is a
    // blast that should not happen.
    private bool TryPickBlastTarget(int index, int total, out Vector3 at)
    {
        at = Vector3.zero;

        var player = PlayerRegistry.Instance != null
            ? PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true)
            : null;

        if (player == null) return false;

        at = player.transform.position;

        var prb = player.GetComponent<Rigidbody2D>();
        if (prb != null && kaboomLeadFactor > 0f)
        {
            // Lead by the SAME window the player actually gets, so raising the dodge
            // time never makes the prediction overshoot and start landing ahead of
            // where anyone would run.
            float telegraph = Mathf.Max(kaboomTelegraphTime, kaboomMinTelegraph);
            at += (Vector3)(prb.linearVelocity * telegraph * kaboomLeadFactor);
        }

        // A little scatter so a barrage never lands as a perfect stack on one spot.
        float jitter = Mathf.Lerp(0.2f, 1.1f, index / Mathf.Max(1f, total - 1f));
        at += new Vector3(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter), 0f);

        return true;
    }

    private void DetonateBlast(Vector3 at, float radius, string layer)
    {
        // Art first, so the flash is already covering the spot on the damage frame.
        PlayExplosion(at, radius, DevourerBlastPalette.Fire, layer);

        DevourerDamage.ApplyRadial(at, radius, ScaleSpecialDamage(kaboomBlastDamage),
                                   gameObject, biteHitsBuildings, null, OnBiteHitTarget);

        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.24f, 0.16f);
    }

    // THE single place any Devourer explosion is drawn.
    //
    // Every explosion draws a random variant from the art wired into Explosion
    // Variants — the Kaboom barrage, the Bomber mini-blasts, the Eye trait's circular
    // bite, the enrage roar and the devour gulp all come through here. One look, one
    // random pool, instead of some attacks using the art and others falling back to
    // procedural shapes that read as a different effect entirely.
    //
    // The procedural blast is ONLY a fallback for a project with no art assigned yet,
    // so the boss stays readable either way.
    private void PlayExplosion(Vector3 at, float radius, DevourerBlastPalette fallback, string layer)
    {
        // Explosion ART is for things landing AT A DISTANCE, on the player. It never
        // draws on the boss itself, whatever calls this.
        //
        // A belt-and-braces rule rather than a fix for one call site: the sprites are
        // large and opaque, and stacked on the Devourer they hide the boss, its
        // telegraphs and everything the player needs to read — and they falsely
        // advertise damage where the boss is standing. Anything genuinely centred on
        // the body gets the procedural shockwave instead, which reads as force coming
        // OUT of the boss rather than ordnance landing on it.
        float fromBoss = ((Vector2)at - (Vector2)transform.position).magnitude;
        if (fromBoss < BodyRadius * 1.5f)
        {
            DevourerShockwave.Play(at, radius, PaletteColor(fallback), layer);
            return;
        }

        Sprite[] frames = PickExplosionVariant();
        if (frames != null)
            DevourerExplosionAnim.Play(at, frames, ExplosionFrameTime,
                                       radius * kaboomArtScale, layer, DevourerFXOrder.Air);
        else
            DevourerBlast.Play(at, radius, fallback, layer);
    }

    // Random non-empty variant. Returns null when nothing is assigned at all, which
    // is the caller's cue to fall back to the procedural ring.
    private Sprite[] PickExplosionVariant()
    {
        if (explosionVariants == null || explosionVariants.Count == 0) return null;

        // Collect the usable rows first rather than retrying a random index, so a
        // list where only one of six rows is filled still picks that row instantly.
        int usable = 0;
        for (int i = 0; i < explosionVariants.Count; i++)
            if (explosionVariants[i] != null && explosionVariants[i].HasFrames) usable++;

        if (usable == 0) return null;

        int pick = Random.Range(0, usable);
        for (int i = 0; i < explosionVariants.Count; i++)
        {
            var v = explosionVariants[i];
            if (v == null || !v.HasFrames) continue;
            if (pick-- == 0) return v.frames;
        }
        return null;
    }

    //  DAMAGE / DEATH

    public override void TakeDamage(float amount)
    {
        if (DebugCheats.DamageBlocked(this)) return;
        if (isDying) return;

        // Armour pool first, then health — identical to Boss1/Boss2/Boss3 so the
        // shared top-of-screen boss bar reads correctly.
        if (!armorDestroyed && bossArmor > 0)
        {
            bossArmor -= amount;
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

        // Clear anything mid-flight so nothing freezes on screen or keeps damaging
        // after the boss is gone.
        ClearSpawnTelegraphs();
        if (activeBiteTelegraph != null) { activeBiteTelegraph.Cancel(); activeBiteTelegraph = null; }

        // Release the controller hooks BEFORE stopping coroutines. A dangling
        // delegate on a destroyed boss would throw the next time the controller
        // tried to pick a target.
        if (controller != null)
        {
            // Hand movement back before disabling, so a boss killed mid-pounce can
            // never leave the flag stuck on.
            controller.ExternalMovementControl = false;
            controller.PriorityTargetProvider = null;
            controller.AttackHandlerOverride = null;
            controller.enabled = false;
        }
        ReleaseFacing();
        if (mealPassThrough != null) { SetMinionCollisionIgnored(mealPassThrough, false); mealPassThrough = null; }
        if (focusProxy != null) { Destroy(focusProxy.gameObject); focusProxy = null; }

        StopAllCoroutines();
        spawnRoutine = bomberRoutine = cloakRoutine = poisonRoutine = kaboomRoutine = enrageRoutine = null;
        spikeRoutine = null;

        // StopAllCoroutines does not reliably run finally blocks, so clear this by
        // hand rather than trusting the wrapper in PerformKaboom.
        kaboomActive = false;

        // Wake any surviving statues so the player isn't left with a field of
        // frozen props that never do anything. They become ordinary enemies and
        // die through the ordinary path, so wave / stage counting stays honest.
        if (wakeMinionsOnBossDeath)
        {
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                var m = liveMinions[i];
                if (m != null && m.IsFrozen) m.Wake();
            }
        }
        liveMinions.Clear();

        if (HealthBar != null) Destroy(HealthBar.gameObject);

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
        foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;

        bossArmor = 0f;
        armorDestroyed = true;

        Vector3 deathPos = transform.position;

        // Boss energy reward ring — identical to Boss1/Boss2/Boss3.
        int drops = Mathf.Max(0, deathEnergyDropCount);
        for (int i = 0; i < drops; i++)
        {
            float angle = (360f / drops) * i * Mathf.Deg2Rad;
            Vector3 spawnPos = deathPos + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 1.5f;
            int energyValue = EnergyDropManager.Instance != null ? EnergyDropManager.Instance.defaultEnergyValue : 10;
            EnergyDrop.CreateEnergyDrop(spawnPos, energyValue);
        }

        RollBlueprintDrop(deathPos);

        // Shared death book-keeping: wave counter -> augment 335 tithe ->
        // EnergyManager kill event -> attribution cleanup. Drops are deliberately
        // NOT routed through it, because the reward ring above already covers them.
        EnemyStats.FireCommonDeathHooks(gameObject);

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: disintegrationDuration,
            onComplete: () =>
            {
                if (AudioManager.instance != null && FMODEvents.instance != null)
                    AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, deathPos);
            });

        // Guaranteed teardown if that VFX never completes. Bosses never reach
        // CharacterStats.Die() -> Destroy(gameObject), so without this a failed VFX
        // would leave the object alive and GameOrchestrator.WaitForBossDead() would
        // spin on it forever.
        ScheduleDeathFailsafe(disintegrationDuration);
    }

    protected override void OnDestroy()
    {
        // Drop the hooks so nothing holds a delegate into a destroyed object.
        if (controller != null)
        {
            controller.PriorityTargetProvider = null;
            controller.AttackHandlerOverride = null;
        }

        ClearSpawnTelegraphs();
        if (activeBiteTelegraph != null) activeBiteTelegraph.Cancel();
        if (HealthBar != null) Destroy(HealthBar.gameObject);
        if (focusProxy != null) Destroy(focusProxy.gameObject);

        base.OnDestroy();   // unregisters from EnemyStatModifierManager
    }

    //  UTILITIES

    // A one-line dump of every live buff. Turn on debugLogs and read this after
    // each meal to confirm a trait actually took effect, rather than inferring it
    // from behaviour.
    public string BuffSummary()
    {
        float speed = enemyData != null ? enemyData.moveSpeed : 0f;
        float dmg = enemyData != null ? enemyData.damage : 0f;
        float range = controller != null ? controller.AttackRange : 0f;

        return $"  dmg {dmg:F1} (x{DamageMultiplier:F2} scaling) | speed {speed:F2} | " +
               $"reach {range:F2} | aoe x{traitAoeScale:F2}\n" +
               $"  circle r={EffectiveAoeRadius():F2} | cone r={EffectiveConeRadius():F2} " +
               $"({EffectiveConeHalfAngle() * 2f:F0}deg) | body r={BodyRadius:F2}\n" +
               $"  hp {currentHealth:F0}/{maxHealth:F0} | armor {bossArmor:F0}\n" +
               $"  circleBite={traitCircleBite} coneBite={traitConeBite} poison={traitPoison} " +
               $"regen={traitRegen} scale={restingScale.y / Mathf.Max(0.0001f, prefabScale.y):F2}x";
    }

    private void PlayOneShot(FMODUnity.EventReference ev, Vector3 at)
    {
        if (!playAudio) return;
        if (ev.IsNull) return;                       // no sound wired yet — silent
        if (AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShot(ev, at);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Circle AOE (magenta) and cone reach (yellow), as they will actually be at
        // the current settings — so sizes can be judged in the scene view instead of
        // by playing until the trait is absorbed.
        Gizmos.color = new Color(1f, 0.3f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, Application.isPlaying
            ? EffectiveAoeRadius()
            : biteAoeRadius * traitAoeScale);

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, Application.isPlaying
            ? EffectiveConeRadius()
            : biteAoeRadius * traitAoeScale * Mathf.Max(1f, coneRadiusMultiplier));

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, spawnRingRadius);

        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, bomberScatterRadius);
    }
#endif
}

//  POISON DoT  (Poisoner trait, applied by the Devourer's attacks)
//  Attached to whatever the Devourer hit. Ticks damage through the same routing
//  helper every other Devourer attack uses, so a poisoned player still gets their
//  Damage Reflection / Ice Armor reactions and a poisoned tower is still credited
//  to the boss.
//
//  Re-applying refreshes rather than stacking: a second component on the same
//  victim would double the DPS invisibly, which is the classic way a DoT quietly
//  becomes the strongest damage source in a fight.
public class DevourerPoisonDebuff : MonoBehaviour
{
    private float dps;
    private float remaining;
    private GameObject attacker;
    private float accumulator;

    public static void Apply(GameObject target, float dps, float duration, GameObject attacker)
    {
        if (target == null || dps <= 0f || duration <= 0f) return;

        // Only poison things that can actually take damage.
        bool damageable = target.CompareTag("Player")
                          || target.GetComponent<IEnergyConsumer>() != null
                          || target.GetComponent<CharacterStats>() != null;
        if (!damageable) return;

        var existing = target.GetComponent<DevourerPoisonDebuff>();
        if (existing == null) existing = target.AddComponent<DevourerPoisonDebuff>();
        existing.Refresh(dps, duration, attacker);
    }

    private void Refresh(float dps, float duration, GameObject attacker)
    {
        // Keep the stronger tick, extend to the longer remaining time.
        this.dps = Mathf.Max(this.dps, dps);
        this.remaining = Mathf.Max(this.remaining, duration);
        this.attacker = attacker;
    }

    private void Update()
    {
        remaining -= Time.deltaTime;
        if (remaining <= 0f) { Destroy(this); return; }

        accumulator += dps * Time.deltaTime;
        if (accumulator < 1f) return;

        int whole = Mathf.FloorToInt(accumulator);
        accumulator -= whole;

        DevourerDamage.ApplySingle(gameObject, whole, attacker, hitBuildings: true);
    }
}


//  DEVOURER (Boss4) — SHARED TYPES
//  Split out of Boss4.cs so the minion component, the VFX classes and the editor
//  tool can all reference the trait enum without pulling in the whole boss.

// The nine distinct minion flavours the Devourer can absorb. The enum VALUE is
// what gets stored in the consumed-set, so the ORDER MAY BE REARRANGED FREELY but
// entries must never be renumbered while a save exists that stores them.
public enum DevourerTrait
{
    Eye = 0,        // bite becomes a 360 circular AOE
    Bomber = 1,     // random mini-explosions around the boss
    Insect = 2,     // periodic transparency / cloak
    Pitcher = 3,    // longer reach + forward cone AOE bite
    Brute = 4,      // +max HP, +25% visual scale
    Poisoner = 5,   // poison pools + DoT on attacks
    Mortar = 6,     // further reach + bigger AOE hitboxes
    Scarecrow = 7,  // health regen + flat damage multiplier
    Gremlin = 8,    // +move speed
}

// One entry in the Devourer's minion pool: the prefab to spawn and the trait the
// Devourer absorbs by eating it.
[System.Serializable]
public class DevourerMinionEntry
{
    [Tooltip("Which trait the Devourer absorbs when it eats this minion.")]
    public DevourerTrait trait;

    [Tooltip("The enemy prefab to spawn. Use your EXISTING enemy prefabs — the " +
             "Devourer freezes them on spawn and reverts them cleanly when woken, " +
             "so no bespoke variant is needed.")]
    public GameObject prefab;

    [Tooltip("OPTIONAL. The exact sprite to freeze this minion on.\n\n" +
             "Only needed for enemies whose body is driven at runtime rather than by " +
             "EnemyData — the Insect is the example: its EnemyData holds a single " +
             "placeholder frame and its real visuals come from InsectAnimator, so " +
             "there is no sensible idle frame to pick automatically. Drag one of its " +
             "above-ground frames here.\n\n" +
             "Leave empty and the statue keeps whatever it was already showing.")]
    public Sprite frozenPose;

    [Tooltip("Turn a single minion type off without removing its row (handy for " +
             "testing one trait in isolation). A disabled entry is never spawned, " +
             "and — importantly — is NOT required for the ultimate phase, so the " +
             "boss can still enrage.")]
    public bool enabled = true;
}

//  SHARED DAMAGE ROUTING
//  Every AOE the Devourer owns (bite circle, bite cone, mini-blasts, Kaboom
//  rings, poison pools) has to hit players, towers and the core through exactly
//  the same channels the rest of the game uses. Getting this wrong is the classic
//  source of "my augment does nothing against this boss" bugs, so it lives in ONE
//  place rather than being re-typed per attack.
//
//  This mirrors, call for call, what Eye.FireAOE() does:
//    * Player  -> EnemyDamageSystem (so shield-block gets its interception), then
//                 EnemyController.NotifyPlayerDamaged so Damage Reflection / Ice
//                 Armor fire. Boss specials do NOT route through
//                 EnemyController.ApplyDamageToTarget, which is exactly why that
//                 static notify helper exists.
//    * Core / towers -> EnergyManager.DamageEnergyConsumer with the boss as the
//                 attacker, so kill attribution stays correct.
//    * Never damages other enemies — the Devourer's own minions must survive its
//      AOE, otherwise it would blow up the pool it needs to eat.
public static class DevourerDamage
{
    // Applies `damage` to everything hostile inside `radius` of `centre`.
    // `attacker` is the boss GameObject (used for kill attribution).
    // Returns the number of distinct targets actually hit.
    //
    // `intercept` (optional): called for each resolved target BEFORE damage. Return
    // true to skip that target — Boss4 uses it for shield block / parry on its bites.
    public static int ApplyRadial(
        Vector2 centre, float radius, float damage, GameObject attacker,
        bool hitBuildings, HashSet<GameObject> dedupe = null,
        System.Action<GameObject> onHit = null,
        System.Func<GameObject, bool> intercept = null)
    {
        return ApplyShape(centre, radius, damage, attacker, hitBuildings,
                          useCone: false, coneDirection: Vector2.right, coneHalfAngleDeg: 180f,
                          dedupe: dedupe, onHit: onHit, intercept: intercept);
    }

    // Cone variant: same routing, but a target must also lie within
    // `coneHalfAngleDeg` of `coneDirection` measured from `centre`.
    public static int ApplyCone(
        Vector2 centre, float radius, Vector2 coneDirection, float coneHalfAngleDeg,
        float damage, GameObject attacker, bool hitBuildings,
        HashSet<GameObject> dedupe = null, System.Action<GameObject> onHit = null,
        System.Func<GameObject, bool> intercept = null)
    {
        return ApplyShape(centre, radius, damage, attacker, hitBuildings,
                          useCone: true, coneDirection: coneDirection,
                          coneHalfAngleDeg: coneHalfAngleDeg,
                          dedupe: dedupe, onHit: onHit, intercept: intercept);
    }

    private static int ApplyShape(
        Vector2 centre, float radius, float damage, GameObject attacker,
        bool hitBuildings, bool useCone, Vector2 coneDirection, float coneHalfAngleDeg,
        HashSet<GameObject> dedupe, System.Action<GameObject> onHit,
        System.Func<GameObject, bool> intercept = null)
    {
        if (damage <= 0f || radius <= 0f) return 0;

        Collider2D[] hits = Physics2D.OverlapCircleAll(centre, radius);
        if (hits == null || hits.Length == 0) return 0;

        // A tower can carry several colliders; dedupe by resolved GameObject so a
        // single pulse can never double-dip on one target.
        HashSet<GameObject> seen = dedupe ?? new HashSet<GameObject>();
        int count = 0;

        Vector2 coneDir = coneDirection.sqrMagnitude > 0.0001f
            ? coneDirection.normalized
            : Vector2.right;
        float cosHalf = Mathf.Cos(Mathf.Clamp(coneHalfAngleDeg, 0f, 180f) * Mathf.Deg2Rad);

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            // Never hit the attacker itself or anything parented under it.
            if (attacker != null &&
                (col.transform == attacker.transform || col.transform.IsChildOf(attacker.transform)))
                continue;

            // Never hit other enemies. The Devourer's minions are food, not targets,
            // and friendly-fire AOE would let the boss delete its own trait pool.
            if (col.GetComponentInParent<EnemyStats>() != null) continue;

            GameObject targetGO = ResolveDamageTarget(col);
            if (targetGO == null) continue;
            if (!seen.Add(targetGO)) continue;

            if (useCone)
            {
                Vector2 toTarget = (Vector2)targetGO.transform.position - centre;
                if (toTarget.sqrMagnitude > 0.0001f &&
                    Vector2.Dot(toTarget.normalized, coneDir) < cosHalf)
                {
                    seen.Remove(targetGO);   // outside the wedge — leave it eligible
                    continue;
                }
            }

            // Blocked / parried (or otherwise vetoed by the caller). Stays in `seen`
            // so a second shape in the same attack can't hit it either.
            if (intercept != null && intercept(targetGO)) continue;

            if (ApplySingle(targetGO, damage, attacker, hitBuildings))
            {
                count++;
                onHit?.Invoke(targetGO);
            }
        }

        return count;
    }

    // Damages ONE already-resolved target through the correct channel.
    // Returns true when damage was actually delivered.
    public static bool ApplySingle(GameObject targetGO, float damage, GameObject attacker, bool hitBuildings)
    {
        if (targetGO == null || damage <= 0f) return false;

        if (targetGO.CompareTag("Player"))
        {
            if (EnemyDamageSystem.Instance != null)
            {
                EnemyDamageSystem.Instance.DamageTarget(targetGO, damage, attacker);
            }
            else
            {
                var cs = targetGO.GetComponent<CharacterStats>();
                if (cs == null) return false;
                cs.TakeDamage(damage);
            }

            // Player-side on-hit augments. Boss specials bypass
            // EnemyController.ApplyDamageToTarget, so without this the player's
            // Damage Reflection / Ice Armor would silently do nothing against
            // every Devourer attack.
            EnemyController.NotifyPlayerDamaged(targetGO, damage, attacker);
            return true;
        }

        if (!hitBuildings) return false;

        var consumer = targetGO.GetComponent<IEnergyConsumer>();
        if (consumer != null)
        {
            if (EnergyManager.Instance == null) return false;
            EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, attacker);
            return true;
        }

        // Anything else that can take damage (destructible props).
        var stats = targetGO.GetComponent<CharacterStats>();
        if (stats != null)
        {
            stats.TakeDamage(damage);
            return true;
        }

        return false;
    }

    // Walks up from a collider to the GameObject that actually owns the health /
    // energy pool. Colliders are frequently on a child (hitbox) object.
    public static GameObject ResolveDamageTarget(Collider2D col)
    {
        if (col == null) return null;

        if (col.CompareTag("Player")) return col.gameObject;

        var playerStats = col.GetComponentInParent<PlayerStats>();
        if (playerStats != null) return playerStats.gameObject;

        // Interface lookup. GetComponentInParent<MonoBehaviour>() would return the
        // FIRST MonoBehaviour on the object, which is very often not the consumer.
        var consumer = col.GetComponentInParent<IEnergyConsumer>();
        if (consumer is Component consumerComponent) return consumerComponent.gameObject;

        var tower = col.GetComponentInParent<Tower>();
        if (tower != null) return tower.gameObject;

        if (col.transform.root != null && col.transform.root.CompareTag("Core"))
            return col.transform.root.gameObject;
        if (col.CompareTag("Core")) return col.gameObject;

        var charStats = col.GetComponentInParent<CharacterStats>();
        if (charStats != null) return charStats.gameObject;

        return null;
    }
}


//  DEVOURER (Boss4) — FROZEN MINION
//  Attached at runtime to each minion the Devourer spawns. The minion is put into
//  STASIS: encased in ice, animation held on one frame, every driving behaviour
//  switched off. It wakes when the player attacks it or walks close enough.
//
//  DESIGN RULE — REVERSIBILITY:
//  This component must be able to hand back a minion that is *bit-for-bit* the
//  enemy the prefab describes, because the minions ARE the ordinary enemy
//  prefabs. So it never edits values (no speed zeroing, no damage clearing, no
//  destroyed components); it only DISABLES things and records exactly what it
//  disabled, then re-enables precisely that set. Anything it cannot restore
//  perfectly, it does not touch.
//
//  WHY NOT EnemyController.ApplyFreeze()?
//  That is a timed slow/stun: it expires on its own, tints the sprite cyan
//  (clobbering the tint bookkeeping EnemyStats.preFlashColor relies on) and only
//  stops EnemyController — leaving self-driving enemies (Bomber, Parfumer,
//  Insect, Scarecrow) running their own coroutines. Stasis has to be indefinite
//  and total, so it is its own mechanism.
//
//  WHY NOT EnemyStats.SuspendBehaviourControllers()?
//  It is the right idea but a deliberately partial list — its own comment says it
//  is "non-exhaustive is safe, just incomplete", and it covers four controller
//  types. A minion pool of nine enemy types needs all of them stopped, so this
//  sweeps every MonoBehaviour outside an explicit keep-list instead.

[DisallowMultipleComponent]
public class DevourerFrozenMinion : MonoBehaviour
{
    // configuration (set by the boss on spawn)
    private Boss4 owner;
    private float wakeRadius = 3.5f;
    private bool wakeOnDamage = true;
    private bool wakeOnProximity = false;
    private float graceSeconds = 8f;
    private float spawnTime;
    private Sprite frozenPoseOverride;

    // state
    private bool frozen;
    private bool consumed;

    // The trait the Devourer absorbs by eating this minion.
    public DevourerTrait Trait { get; private set; }

    // False once the minion has woken up. The boss still happily eats a woken
    // minion — waking is a threat to the player, not a rescue for the minion.
    public bool IsFrozen => frozen;

    // True while this minion is still a valid meal (alive and not already eaten).
    public bool IsEdible => !consumed && this != null && stats != null && !stats.IsDead();

    // True once the grace period has expired AND the minion is still alive. The
    // boss checks THIS, not IsEdible, so a freshly spawned minion is genuinely
    // untouchable for its first few seconds — that window is the player's chance
    // to kill it and deny the buff.
    public bool IsHuntable => IsEdible && Time.time - spawnTime >= graceSeconds;

    // 0..1 through the grace period. Drives the ice tint so the player can read
    // how long they have left.
    public float GraceProgress =>
        graceSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - spawnTime) / graceSeconds);

    private EnemyStats stats;
    private EnemyAnimationController animController;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private DevourerIceEncasement encasement;

    // Exactly the behaviours this component switched off, so wake-up re-enables
    // that set and nothing else. A behaviour that was ALREADY disabled on the
    // prefab is never touched, and therefore never wrongly switched on.
    private readonly List<Behaviour> suspended = new List<Behaviour>();

    private RigidbodyConstraints2D originalConstraints;
    private bool constraintsCaptured;

    // GrassCartoonOverlay Y-sorts with the SAME base (1000) and precision (10) as
    // entities, so blades standing slightly in front of an enemy legitimately draw
    // over it. On a moving enemy that reads as grass it is walking through; on a
    // minion frozen in place for ten seconds it just reads as broken, because the
    // eye has time to study it.
    //
    // While frozen we lift the whole statue a little up the sort band so the local
    // grass tuft sits behind it. Restored exactly on wake, so a woken minion sorts
    // like every other enemy again.
    private YSortEntity ysort;
    private int originalSortBase;
    private bool sortBoosted;

    // Scale as it was at the moment of freezing. Insurance: if a suspended component
    // was midway through its own scale tween when we froze it (BerserkController and
    // the Brute both grow over time), it can never finish, and the minion would be
    // handed back stuck at whatever partial size it happened to be on that frame.
    private Vector3 frozenScale = Vector3.one;
    private bool scaleCaptured;
    private const int FrozenSortBoost = 16;

    // Every renderer we chilled, with the colour it had before we touched it.
    private readonly List<SpriteRenderer> frostedRenderers = new List<SpriteRenderer>();
    private readonly List<Color> frostedOriginals = new List<Color>();
    private readonly List<SpriteRenderer> hiddenRenderers = new List<SpriteRenderer>();
    private readonly List<Collider2D> disabledColliders = new List<Collider2D>();

    // Multiplied into each renderer's existing colour rather than replacing it, so
    // a red enemy freezes to a cold red and a green one to a cold green. Flatly
    // overwriting with one blue would erase the colour identity that tells the
    // player which enemy they are looking at.
    private static readonly Color FrostMultiply = new Color(0.34f, 0.58f, 1.20f, 1f);

    // Components that MUST keep running while frozen. Everything else on the
    // minion (and its children) is suspended.
    //   EnemyStats            — the minion must still be damageable, and its
    //                           OnDamaged event is our wake trigger.
    //   EnemyAnimationController — kept enabled but told to hold its frame, so
    //                           the sprite stays posed rather than blanking.
    //   SpriteRenderer/YSort/Flip — rendering must keep working.
    //   DevourerFrozenMinion  — us. Obviously.
    private bool ShouldKeepRunning(MonoBehaviour mb)
    {
        if (mb == null) return true;
        if (mb == this) return true;
        if (mb is EnemyStats) return true;
        if (mb is EnemyAnimationController) return true;
        if (mb is YSortEntity) return true;
        if (mb is SmoothSpriteFlip) return true;
        if (mb is DevourerIceEncasement) return true;
        if (mb is DevourerFadeSprite) return true;

        // The floating health bar is usually a separate root object, but if a
        // prefab parents one under the enemy we leave it alone.
        if (mb is EnemyHealthBar) return true;

        return false;
    }

    // setup
    public void Initialize(Boss4 owner, DevourerTrait trait, float graceSeconds, float wakeRadius,
                           bool wakeOnDamage, bool wakeOnProximity, Sprite frozenPose = null)
    {
        this.frozenPoseOverride = frozenPose;
        this.owner = owner;
        this.Trait = trait;
        this.graceSeconds = Mathf.Max(0f, graceSeconds);
        this.wakeRadius = Mathf.Max(0.5f, wakeRadius);
        this.wakeOnDamage = wakeOnDamage;
        this.wakeOnProximity = wakeOnProximity;
        this.spawnTime = Time.time;

        stats = GetComponent<EnemyStats>();
        animController = GetComponent<EnemyAnimationController>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();

        // Freeze on the NEXT frame, not this one.
        //
        // Instantiate has run Awake, but Start has NOT. Several enemies build their
        // visuals in Start rather than Awake — EyeChains creates every chain-link
        // SpriteRenderer there, the Scarecrow builds its aura child, and so on.
        // Disabling those components before Start runs means Start never fires, so
        // the enemy spawns half-built: an Eye with no chains. Re-enabling on wake
        // then runs Start late, which is why the chains "appeared" only after the
        // minion activated.
        //
        // Waiting one frame lets every Start complete, so we freeze a fully formed
        // enemy and hand back a fully formed one. The cost is a single frame in
        // which the minion is technically live — harmless, since it spawns on a
        // ring away from the player with a fresh attack cooldown, and we zero its
        // velocity immediately below.
        if (rb != null && rb.bodyType != RigidbodyType2D.Static)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        StartCoroutine(FreezeAfterStart());
    }

    private System.Collections.IEnumerator FreezeAfterStart()
    {
        yield return null;   // every Start() on this enemy has now run

        // The player may have already killed it, or the boss eaten it, in that frame.
        if (consumed || this == null) yield break;
        if (stats != null && stats.IsDead()) yield break;

        Freeze();
    }

    private void Freeze()
    {
        if (frozen) return;
        frozen = true;

        // 1. Suspend every driving behaviour, on this object AND its children
        //    (the Scarecrow's aura, for instance, lives on a child).
        var all = GetComponentsInChildren<MonoBehaviour>(includeInactive: false);
        for (int i = 0; i < all.Length; i++)
        {
            var mb = all[i];
            if (mb == null) continue;
            if (ShouldKeepRunning(mb)) continue;
            if (!mb.enabled) continue;              // already off — leave it off

            // Stopping Update is not enough. Disabling a MonoBehaviour does NOT stop
            // coroutines it already started — they keep running to completion.
            //
            // That is why a summoned Scarecrow screamed and then vanished: its Start
            // kicks off DeferredInitialHide(), which waits one frame and then hides
            // it and launches CycleLoop(). Both survived being "frozen", so the
            // statue promptly played its whole disappear routine. The Bomber's
            // charge and the Parfumer's cloud timer are the same shape.
            mb.StopAllCoroutines();
            mb.enabled = false;
            suspended.Add(mb);
        }

        // 2. Stop the body dead. `simulated` stays TRUE so incoming projectile
        //    triggers still register — a minion you cannot shoot would be a trap,
        //    since shooting it is the intended way to wake it.
        if (rb != null)
        {
            if (rb.bodyType != RigidbodyType2D.Static)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            originalConstraints = rb.constraints;
            constraintsCaptured = true;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        frozenScale = transform.localScale;
        scaleCaptured = true;

        // 3. Lift out of the grass band (see FrozenSortBoost).
        ysort = GetComponent<YSortEntity>();
        if (ysort != null)
        {
            originalSortBase = ysort.sortOrderBase;
            ysort.sortOrderBase = originalSortBase + FrozenSortBoost;
            sortBoosted = true;
        }
        else if (spriteRenderer != null)
        {
            // No Y-sorter on this prefab: nudge the renderer directly instead.
            originalSortBase = spriteRenderer.sortingOrder;
            spriteRenderer.sortingOrder = originalSortBase + FrozenSortBoost;
            sortBoosted = true;
        }

        // 4. Snap to a known-good pose, THEN hold it.
        //
        // FreezeAnimation() holds whatever frame is showing, which is wrong when we
        // catch an enemy mid-special. The Insect is the clear case: freeze it during
        // its burrow and it holds a digging frame — and because its burrow clips fail
        // to load at all (see the InsectAnimator warnings in the log), that frame can
        // be stale garbage from another animation entirely. Forcing a mid-idle frame
        // gives every minion a stable, recognisable statue pose.
        SnapToRestPose();
        if (animController != null) animController.FreezeAnimation();

        // 5. Colliders back on.
        //
        // Enemies that go intangible as part of a special disable their collider —
        // the Insect does exactly this while underground. Freeze it mid-burrow and
        // the statue has no collider: shots pass through, the boss walks through it,
        // and it can never be woken by damage. Force them on, remembering which we
        // touched so the enemy resumes in the state it chose.
        ForceCollidersOn();

        // 6. Frost the WHOLE enemy, not just its body sprite.
        //    Chain links, auras, weapon sprites and any other child renderer all
        //    get chilled, so nothing on the minion is still showing warm prefab
        //    colours while the rest of it is iced over. Originals are recorded per
        //    renderer so wake-up restores exactly what the prefab specified rather
        //    than guessing white.
        //
        //    Runs AFTER Start (see FreezeAfterStart), so renderers created in Start
        //    — the Eye's chain links, for instance — are included.
        FrostAllRenderers();

        // 7. Ice shell. Sized from the body collider so a Brute and a Gremlin each
        //    get a shell that fits.
        float bodyRadius = EstimateBodyRadius();
        string layer = spriteRenderer != null ? spriteRenderer.sortingLayerName : "Default";
        int order = spriteRenderer != null ? spriteRenderer.sortingOrder + 1 : DevourerFXOrder.Air;
        encasement = DevourerIceEncasement.Attach(gameObject, bodyRadius, layer, order);

        // 8. Wake on being shot. CharacterStats.OnDamaged only fires when health
        //    was ACTUALLY lost, so a fully-armour-absorbed ping won't wake it —
        //    which is the behaviour we want (chip it properly to break the ice).
        if (wakeOnDamage && stats != null)
            stats.OnDamaged += HandleDamaged;
    }

    // Puts a stable, recognisable frame on the body for the statue.
    //
    // Resolution order, and the reasoning behind each step:
    //
    //   1. The pool entry's Frozen Pose, if the designer set one. Always wins.
    //   2. idleFrames, mid-clip.
    //   3. frames, mid-clip — but ONLY if there is more than one.
    //   4. Otherwise: change nothing.
    //
    // Step 3's length check is the important one. InsectData.frames holds exactly ONE
    // sprite and Insect.prefab's SpriteRenderer has no sprite at all, because the
    // Insect's body is driven entirely at runtime by InsectAnimator. That single
    // entry is a leftover placeholder, not an idle clip — and forcing it onto the
    // renderer is what put stale legacy art on the frozen Insect.
    //
    // A one-frame array is therefore treated as "this enemy's visuals live somewhere
    // else", and we leave the live sprite alone rather than overriding it with
    // something the enemy never displays.
    private void SnapToRestPose()
    {
        if (spriteRenderer == null) return;

        if (frozenPoseOverride != null)
        {
            spriteRenderer.sprite = frozenPoseOverride;
            return;
        }

        if (stats == null) return;
        var data = stats.enemyData;
        if (data == null) return;

        Sprite pose = null;

        if (data.idleFrames != null && data.idleFrames.Length > 1)
        {
            pose = data.idleFrames[data.idleFrames.Length / 2];
        }
        else if (data.frames != null && data.frames.Length > 1)
        {
            int start = Mathf.Clamp(data.idle.startFrame, 0, data.frames.Length - 1);
            int count = data.idle.frameCount > 0 ? data.idle.frameCount : data.frames.Length - start;
            int mid = Mathf.Clamp(start + count / 2, 0, data.frames.Length - 1);
            pose = data.frames[mid];
        }

        // Never blank the renderer: a null pose means "leave whatever is showing".
        if (pose != null) { spriteRenderer.sprite = pose; return; }

        // No usable clip, and no override. Say so, precisely — this is otherwise a
        // mystery that looks like a Boss4 bug.
        //
        // EnemyAnimationController.Start does `spriteRenderer.sprite = sprites[0]`
        // with `sprites = enemyData.frames`. For an enemy whose body is really driven
        // by its own component (the Insect, via InsectAnimator), EnemyData holds a
        // single leftover placeholder frame — so that placeholder is what gets stamped
        // on the renderer, and freezing preserves it. Nothing in Boss4 can guess the
        // right frame for such an enemy, so the pool row has to name it.
        WarnMissingFrozenPose();
    }

    private static readonly HashSet<string> _posewarned = new HashSet<string>();

    private void WarnMissingFrozenPose()
    {
        string key = stats != null && stats.enemyData != null
            ? stats.enemyData.enemyName
            : gameObject.name;
        if (string.IsNullOrEmpty(key)) key = gameObject.name;

        // Once per enemy type per run; this fires on every spawn otherwise.
        if (!_posewarned.Add(key)) return;

        Debug.LogWarning(
            $"[Boss4] '{key}' has no usable idle clip on its EnemyData " +
            $"(frames/idleFrames hold 0 or 1 sprite), so its body is driven at runtime " +
            $"by another component. The frozen statue will show whatever was last " +
            $"stamped on its renderer — usually the placeholder frame from " +
            $"EnemyData.frames.\n" +
            $"FIX: on the Boss4 minion pool row for '{key}', drag an above-ground " +
            $"frame into 'Frozen Pose'.");
    }

    // Make sure the statue is visible — WITHOUT revealing things that are hidden on
    // purpose.
    //
    // The first version force-enabled every disabled SpriteRenderer, which is far too
    // broad. Enemies keep hidden child renderers for their own effects:
    // InsectAnimator builds a "BurrowMoundOverlay" child and leaves it disabled until
    // the insect actually digs. Switching that on produced a frozen Insect wearing a
    // patch of soil — the stray sprite that looked like leftover legacy art.
    //
    // The rule now: if ANY renderer is already visible, the enemy is showing what it
    // means to show, so touch nothing. Only when the whole thing is invisible (a
    // Scarecrow that just hid itself, an Insect frozen underground) do we reveal
    // exactly one renderer — the largest, i.e. the body.
    private void EnsureSomethingVisible()
    {
        var all = GetComponentsInChildren<SpriteRenderer>(true);

        SpriteRenderer biggest = null;
        float bestArea = -1f;

        for (int i = 0; i < all.Length; i++)
        {
            var sr = all[i];
            if (sr == null || sr.sprite == null) continue;
            if (sr.GetComponentInParent<DevourerIceEncasement>() != null) continue;

            // Something is already on screen — leave the hierarchy exactly as it is.
            if (sr.enabled && sr.color.a > 0.05f) return;

            var size = sr.sprite.bounds.size;
            float area = size.x * size.y;
            if (area > bestArea) { bestArea = area; biggest = sr; }
        }

        if (biggest == null) return;

        if (!biggest.enabled)
        {
            biggest.enabled = true;
            hiddenRenderers.Add(biggest);
        }

        if (biggest.color.a < 0.99f)
        {
            var c = biggest.color;
            c.a = 1f;
            biggest.color = c;
        }
    }

    private void ForceCollidersOn()
    {
        disabledColliders.Clear();

        var cols = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null || c.enabled) continue;
            c.enabled = true;
            disabledColliders.Add(c);
        }
    }

    private void RestoreColliderStates()
    {
        for (int i = 0; i < disabledColliders.Count; i++)
            if (disabledColliders[i] != null) disabledColliders[i].enabled = false;
        disabledColliders.Clear();
    }

    private void FrostAllRenderers()
    {
        frostedRenderers.Clear();
        frostedOriginals.Clear();
        hiddenRenderers.Clear();

        EnsureSomethingVisible();

        var all = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        for (int i = 0; i < all.Length; i++)
        {
            var sr = all[i];
            if (sr == null) continue;

            // Skip our own effect sprites — they are already ice-coloured, and
            // chilling them twice would wash them out.
            if (sr.GetComponentInParent<DevourerIceEncasement>() != null) continue;

            frostedRenderers.Add(sr);
            frostedOriginals.Add(sr.color);

            var c = sr.color;
            sr.color = new Color(c.r * FrostMultiply.r,
                                 c.g * FrostMultiply.g,
                                 Mathf.Min(1f, c.b * FrostMultiply.b),
                                 c.a);
        }
    }

    private void UnfrostAllRenderers()
    {
        for (int i = 0; i < frostedRenderers.Count; i++)
        {
            var sr = frostedRenderers[i];
            if (sr == null) continue;
            sr.color = frostedOriginals[i];
        }

        // Anything we force-showed goes back to hidden, so an enemy that had
        // deliberately hidden itself resumes in the state it chose.
        for (int i = 0; i < hiddenRenderers.Count; i++)
            if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = false;

        frostedRenderers.Clear();
        frostedOriginals.Clear();
        hiddenRenderers.Clear();
    }

    // How big to build the ice, in world units.
    //
    // This used to read Collider2D.bounds.extents, which is WORLD space and was left
    // unclamped. A big enemy with a 3-unit collider produced a 12-unit ground patch
    // and an 8-unit glow — a screen-filling blob that swallowed the enemy whole —
    // while an enemy with a small trigger collider got ice so tiny it was invisible.
    // That single line is why the effect looked wildly different from enemy to
    // enemy, and why one of them turned into a giant coloured smear.
    //
    // Now it measures the VISIBLE sprite (what the ice has to cover) rather than the
    // physics shape (which has no reliable relationship to the art), takes the
    // smaller of half-width and half-height so wide sprites don't inflate it, and
    // clamps hard. The clamp is the important part: no enemy in the game should get
    // ice outside this band, whatever its collider or art happens to be.
    private const float MinIceRadius = 0.35f;
    private const float MaxIceRadius = 1.40f;

    private float EstimateBodyRadius()
    {
        float r = 0f;

        var sr = spriteRenderer;
        if (sr == null || sr.sprite == null)
        {
            // Body renderer may live on a child; pick the largest one.
            var all = GetComponentsInChildren<SpriteRenderer>(true);
            float best = -1f;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].sprite == null) continue;
                var sz = all[i].bounds.size;
                float area = sz.x * sz.y;
                if (area > best) { best = area; sr = all[i]; }
            }
        }

        if (sr != null && sr.sprite != null)
        {
            var e = sr.bounds.extents;
            r = Mathf.Min(e.x, e.y);          // min, not max — wide art shouldn't inflate it
        }

        if (r <= 0.01f)
        {
            var col = GetComponent<Collider2D>();
            if (col != null) r = Mathf.Min(col.bounds.extents.x, col.bounds.extents.y);
        }

        if (r <= 0.01f) r = 0.5f;
        return Mathf.Clamp(r, MinIceRadius, MaxIceRadius);
    }

    // wake triggers
    private void HandleDamaged(float amount)
    {
        if (!frozen || consumed) return;
        Wake();
    }

    private void Update()
    {
        if (consumed) return;

        // Killed while frozen? Strip the ice at once.
        //
        // Without this the shell outlives the body: EnemyStats hides or swaps the
        // sprite during its death handling, but the frost overlays are ours and keep
        // drawing the last pose — a floating blue ghost of an enemy that is no longer
        // there. Waking is the correct teardown path here, since it restores tint,
        // sort order and scale as well as shattering the shell.
        if (stats != null && stats.IsDead())
        {
            if (encasement != null) { encasement.HostDied(); encasement = null; }
            if (frozen) Wake();
            enabled = false;
            return;
        }

        // Feed the deny-window countdown to the ice shell: it shifts from cold blue
        // to a warning amber as the Devourer's grace period runs out, so the player
        // can see at a glance which minion is about to be eaten.
        if (frozen && encasement != null)
            encasement.SetGraceProgress(GraceProgress);

        if (!frozen) return;
        if (!wakeOnProximity) return;
        if (PlayerRegistry.Instance == null) return;

        // NearestAlive with includeCloaked:false — a cloaked player sneaking past
        // a statue should stay unnoticed. That is a genuine use for the cloak, and
        // it costs the player nothing if they'd rather just shoot it awake.
        var near = PlayerRegistry.Instance.NearestAlive(transform.position, wakeRadius, includeCloaked: false);
        if (near != null) Wake();
    }

    // Bring the minion out of stasis: restore everything Freeze() changed, then
    // shatter the ice. Safe to call repeatedly.
    public void Wake()
    {
        if (!frozen) return;
        frozen = false;

        if (wakeOnDamage && stats != null)
            stats.OnDamaged -= HandleDamaged;

        // Re-enable exactly what we disabled — but never revive a corpse. If the
        // minion died while frozen, its death routine deliberately switched its
        // controller off and turning it back on would let it walk and swing again.
        bool dead = stats != null && stats.IsDead();
        for (int i = 0; i < suspended.Count; i++)
        {
            if (suspended[i] == null) continue;
            if (dead) continue;
            suspended[i].enabled = true;
        }
        suspended.Clear();

        if (rb != null && constraintsCaptured)
            rb.constraints = originalConstraints;

        if (sortBoosted)
        {
            if (ysort != null) ysort.sortOrderBase = originalSortBase;
            else if (spriteRenderer != null) spriteRenderer.sortingOrder = originalSortBase;
            sortBoosted = false;
        }

        // Snap the scale back to what it was when we froze it, so an interrupted
        // growth tween can't leave a woken minion permanently oversized.
        if (scaleCaptured) transform.localScale = frozenScale;

        RestoreColliderStates();

        if (animController != null) animController.UnfreezeAnimation();

        // Restore every prefab tint. EnemyStats.StartDamageFlash captures whatever
        // colour is showing when a flash begins, so putting the real colours back
        // here is what stops a woken minion latching to the frozen blue.
        UnfrostAllRenderers();

        if (encasement != null) { encasement.Shatter(); encasement = null; }

        // We stopped their coroutines to freeze them, and re-enabling a component
        // does NOT re-run Start, so any loop that lived in a coroutine is gone.
        // Update-driven enemies resume fine; coroutine-driven ones (the Scarecrow's
        // appear/disappear cycle) need a nudge. This is a no-op unless a component
        // implements the handler — see Boss4_SETUP.md for the four-line Scarecrow
        // opt-in. Deliberately NOT SendMessage("Start"): Scarecrow.Start subscribes
        // to OnHealthChanged and builds its aura, so re-running it would double both.
        BroadcastMessage("OnDevourerStasisEnded", SendMessageOptions.DontRequireReceiver);

        owner?.OnMinionWoke(this);
    }

    // consumption
    // Called by the boss when it swallows this minion. Removes the object
    // WITHOUT going through Die(): being eaten is not a kill, so it must not pay
    // out energy, must not credit a tower, and — critically — must not trigger
    // GremlinController.Die(), which splits the Gremlin into two more Gremlins.
    public void ConsumeAndDestroy(Transform mouth = null, float pullDuration = 0.22f)
    {
        if (consumed) return;
        consumed = true;

        if (frozen && wakeOnDamage && stats != null)
            stats.OnDamaged -= HandleDamaged;
        frozen = false;

        // Shatter the shell FIRST. It detaches its overlays from this transform,
        // which matters because EnemyDeathVFX snapshots
        // GetComponentInChildren<SpriteRenderer>() — with the frost overlay still
        // parented it could shatter the overlay instead of the actual enemy.
        if (encasement != null) { encasement.Shatter(); encasement = null; }

        DestroyCompanionVisuals();

        // The floating health bar is a separate root object that follows this
        // transform; without this it would be stranded in the scene forever.
        var bar = stats != null ? stats.GetHealthBar() : null;
        if (bar != null) Destroy(bar.gameObject);

        StartCoroutine(DevouredRoutine(mouth, pullDuration));
    }

    // Kills off any visuals a suspended component owns OUTSIDE this transform.
    //
    // The Eye is the case that exposed this. EyeChains deliberately builds its links
    // on an UNPARENTED root — its own comment explains why: the eye is scaled 0.25 and
    // parented links would inherit that and shrink. It cleans that root up in
    // OnDestroy. But we froze the component, so the chains stopped following the eye,
    // and then we dragged the body into the boss's jaws while the chains hung there in
    // mid-air — the head got eaten and the chains stayed put.
    //
    // Destroying the components (rather than waiting for the GameObject to go) fires
    // their OnDestroy now, so anything well-behaved tears its own world objects down
    // with it. Safe because the object is being destroyed moments later anyway: this
    // just runs the same cleanup a beat early, before the pull starts.
    private void DestroyCompanionVisuals()
    {
        // Destroy in DEPENDENCY ORDER, dependents first.
        //
        // The naive version destroyed everything in whatever order it was suspended,
        // which included EnemyController — and BruteController, WolfController, Eye,
        // MortController, InsectController and PitcherController all declare
        // [RequireComponent(typeof(EnemyController))]. Unity refuses the removal and
        // logs "Can't remove EnemyController because X depends on it", once per enemy
        // type eaten. Harmless, but it is the engine telling us we asked for
        // something invalid, and it buried the rest of the log.
        //
        // So: repeatedly destroy whatever nothing still on the object requires. Each
        // pass frees more up — kill MortController and EnemyController becomes
        // removable. Anything still locked when progress stops is left disabled: the
        // GameObject dies moments later anyway, and the only reason to destroy
        // components early is to fire OnDestroy on the ones owning world-space
        // visuals (EyeChains), which are leaf components with nothing depending on
        // them.
        var doomed = new HashSet<Component>();

        bool progress = true;
        while (progress)
        {
            progress = false;

            for (int i = 0; i < suspended.Count; i++)
            {
                var mb = suspended[i];
                if (mb == null || doomed.Contains(mb)) continue;
                if (IsRequiredByLivingComponent(mb, doomed)) continue;

                doomed.Add(mb);
                Destroy(mb);
                progress = true;
            }
        }

        suspended.Clear();
    }

    // True when a component that is still alive (not already queued for destruction)
    // declares [RequireComponent] for this component's type, or a base of it.
    //
    // The `doomed` set matters: Destroy() is deferred to end of frame, so a component
    // we just destroyed is still non-null and would otherwise keep blocking the thing
    // it depends on for the rest of the loop.
    private bool IsRequiredByLivingComponent(Component candidate, HashSet<Component> doomed)
    {
        if (candidate == null) return false;

        System.Type candidateType = candidate.GetType();
        var all = candidate.gameObject.GetComponents<Component>();

        for (int i = 0; i < all.Length; i++)
        {
            var other = all[i];
            if (other == null || other == candidate) continue;
            if (doomed.Contains(other)) continue;

            var attrs = other.GetType().GetCustomAttributes(typeof(RequireComponent), true);
            for (int a = 0; a < attrs.Length; a++)
            {
                var req = (RequireComponent)attrs[a];
                if (Requires(req.m_Type0, candidateType)) return true;
                if (Requires(req.m_Type1, candidateType)) return true;
                if (Requires(req.m_Type2, candidateType)) return true;
            }
        }

        return false;
    }

    private static bool Requires(System.Type required, System.Type candidateType)
        => required != null && required.IsAssignableFrom(candidateType);

    // Dragged off its feet into the jaws, then torn apart.
    //
    // The disintegration is EnemyDeathVFX in DisintegrateOnly mode — the same
    // sprite-shatter the game already uses for deaths, so a devoured minion comes
    // apart exactly like a killed one rather than inventing a second visual
    // language. DisintegrateOnly (not the blast variant) because the dust puff
    // belongs to an explosion, not to something being swallowed.
    private System.Collections.IEnumerator DevouredRoutine(Transform mouth, float pullDuration)
    {
        Vector3 start = transform.position;
        Vector3 startScale = transform.localScale;
        float dur = Mathf.Max(0.05f, pullDuration);
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            float eased = p * p;                       // accelerates into the mouth

            if (mouth != null)
                transform.position = Vector3.Lerp(start, mouth.position, eased);

            // Squeezed thin as it goes in, and spun for a bit of violence.
            transform.localScale = new Vector3(
                startScale.x * Mathf.Lerp(1f, 0.45f, eased),
                startScale.y * Mathf.Lerp(1f, 0.75f, eased),
                startScale.z);
            transform.Rotate(0f, 0f, 220f * Time.deltaTime);

            yield return null;
        }

        // Hand off to the shared shatter, which destroys this GameObject for us.
        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: 0.55f,
            onComplete: null,
            sourceTexturePath: null,
            style: DeathVfxStyle.DisintegrateOnly);
    }

    private void OnDestroy()
    {
        // Always detach the event, even on an unexpected teardown — a dangling
        // subscription to a destroyed component throws on the next damage tick.
        if (stats != null) stats.OnDamaged -= HandleDamaged;

        owner?.OnMinionRemoved(this);
    }
}


//  DEVOURER (Boss4) — VISUAL EFFECTS
//  Everything here is PROCEDURAL: textures are generated once, cached statically
//  and shared. No art assets, no Resources folder, no prefab wiring — which is
//  the same approach Boss2's summon FX and Boss3's blink FX already take, so the
//  Devourer needs nothing but its Walk/Bite PNGs to look finished.
//
//  SORTING, the trap worth knowing about: every Y-sorted sprite in this project
//  uses  sortingOrder = 1000 + round(-footY * 10), so the band runs ~400-1600.
//  Ground decals must sit BELOW that band and airborne flashes ABOVE it, or they
//  will pop in and out as the boss walks up and down the screen. The two
//  constants below are the only orders used by this file.

public static class DevourerFXOrder
{
    // The project-wide Y-sort convention, shared by EnemyController AND by
    // GrassCartoonOverlay (sortOrderBase 1000, sortPrecision 10):
    //     order = 1000 + round(-y * 10)
    // So at y = 0 both an enemy and the grass around it sit at ~1000, and the band
    // spans roughly 400..1600 across a normal map.
    public const int SortBase = 1000;
    public const float SortPrecision = 10f;

    // Ground decals MUST be Y-sorted, not pinned to a constant.
    //
    // A fixed low order (the obvious first guess) puts the decal underneath every
    // blade of cartoon grass at a lower Y — because the grass overlay uses this
    // exact same base of 1000. A telegraph drawn at 620 is invisible anywhere the
    // grass is dense. Sorting by the decal's own Y instead makes it interleave
    // correctly: over the grass behind it, under the entity standing on it.
    public static int GroundAt(float worldY)
        => SortBase + Mathf.RoundToInt(-worldY * SortPrecision) - 2;

    // Airborne FX: above the whole entity/grass band, below the biome's cloud
    // layer (4000), fog (5000) and night tint (6000), and below health bars
    // (4000) — so an explosion never covers the UI but is never buried by terrain.
    public const int Air = 3000;

    // Slightly under Air, for glows that should sit behind the main flash.
    public const int AirUnder = 2990;
}

//  Cached procedural sprites
public static class DevourerSprites
{
    private static Sprite _softDisc;
    private static Sprite _thinRing;
    private static Sprite _shard;

    // Radial glow, opaque at the centre fading to nothing at the rim.
    public static Sprite SoftDisc
    {
        get
        {
            if (_softDisc != null) return _softDisc;
            const int S = 64;
            var tex = NewTex(S);
            float c = (S - 1) * 0.5f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);           // smoothstep falloff
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            _softDisc = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
            return _softDisc;
        }
    }

    // Hollow ring with a crisp thin stroke — the expanding shockwave shape.
    public static Sprite ThinRing
    {
        get
        {
            if (_thinRing != null) return _thinRing;
            const int S = 128;
            var tex = NewTex(S);
            float c = (S - 1) * 0.5f;
            float outerR = c - 1f;
            float strokeHalf = outerR * 0.055f;
            float centerR = outerR - strokeHalf;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float fromRing = Mathf.Abs(d - centerR);
                    float a;
                    if (fromRing <= strokeHalf - 1.2f) a = 1f;
                    else if (fromRing >= strokeHalf + 1.2f) a = 0f;
                    else a = Mathf.Clamp01((strokeHalf + 1.2f - fromRing) / 2.4f);
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            _thinRing = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
            return _thinRing;
        }
    }

    // A tapered ice shard: wide at the base, pointed at the top, with a bright
    // inner core so it reads as translucent crystal rather than a grey triangle.
    public static Sprite Shard
    {
        get
        {
            if (_shard != null) return _shard;
            const int W = 32, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                float t = y / (float)(H - 1);              // 0 = base, 1 = tip
                float halfWidth = Mathf.Lerp(W * 0.42f, W * 0.04f, t * t);
                for (int x = 0; x < W; x++)
                {
                    float dx = Mathf.Abs(x - (W - 1) * 0.5f);
                    float a = dx <= halfWidth ? 1f : 0f;
                    if (a > 0f && dx > halfWidth - 1.5f)
                        a = Mathf.Clamp01((halfWidth - dx) / 1.5f);   // soft edge

                    // Bright core down the centre line -> crystalline highlight.
                    float core = 1f - Mathf.Clamp01(dx / Mathf.Max(0.001f, halfWidth));
                    float lum = Mathf.Lerp(0.72f, 1f, core * core);
                    px[y * W + x] = new Color(lum, lum, 1f, a);
                }
            }
            tex.SetPixels(px); tex.Apply();
            // Pivot at the BASE so a shard can be rotated outward around its root.
            _shard = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 64f);
            return _shard;
        }
    }

    // One shared additive material for every glow/frost sprite that needs it.
    // Touching SpriteRenderer.material instead would instantiate a fresh copy per
    // renderer, and Unity does not clean those up automatically — with minions
    // spawning all fight, that is a slow leak.
    private static Material _additive;
    public static Material Additive
    {
        get
        {
            if (_additive != null) return _additive;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            // Sprites/Default hardcodes `Blend One OneMinusSrcAlpha` and exposes no
            // blend properties, so this material is really "premultiplied, fed with
            // straight colours" — which lands close to additive for bright colours.
            // That is fine, and wanted, for small hot sparks: embers, motes, the
            // rage glow. It is NOT usable for anything that covers a large area,
            // because straight RGB goes in at full strength no matter the alpha and
            // the result saturates to white. Large-area frost uses plain alpha
            // blending instead, which is why it no longer blows out.
            _additive = new Material(sh) { name = "DevourerGlowAdditive" };
            return _additive;
        }
    }

    private static Sprite _spike;

    // A shaded spike matching the Devourer's own art: near-black at the root, deep
    // violet through the body, hot magenta at the tip, with a soft highlight down one
    // side and a dark edge on the other.
    //
    // The flat two-tone shard used before read as a foreign object stuck to the boss.
    // The gradient is what blends it: the root is the same value as the hide, so where
    // it leaves the body there is no hard seam, and the eye only registers the bright
    // tip — which is the part that matters.
    public static Sprite Spike
    {
        get
        {
            if (_spike != null) return _spike;

            const int W = 48, H = 160;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            // Root -> tip palette, sampled from the boss art.
            Color root = new Color(0.13f, 0.10f, 0.16f);
            Color mid = new Color(0.40f, 0.13f, 0.58f);
            Color tip = new Color(0.86f, 0.36f, 1.00f);

            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                float t = y / (float)(H - 1);          // 0 root, 1 tip

                // Concave taper: fat at the base, drawing to a fine point. A linear
                // taper reads as a triangle; the curve reads as a claw.
                float halfWidth = Mathf.Lerp(W * 0.40f, W * 0.015f, Mathf.Pow(t, 0.72f));

                Color shaft = t < 0.55f
                    ? Color.Lerp(root, mid, t / 0.55f)
                    : Color.Lerp(mid, tip, (t - 0.55f) / 0.45f);

                for (int x = 0; x < W; x++)
                {
                    float dx = x - (W - 1) * 0.5f;
                    float ax = Mathf.Abs(dx);

                    float a = ax <= halfWidth ? 1f : 0f;
                    if (a > 0f && ax > halfWidth - 1.6f)
                        a = Mathf.Clamp01((halfWidth - ax) / 1.6f);   // soft silhouette

                    if (a <= 0f) { px[y * W + x] = new Color(0, 0, 0, 0); continue; }

                    // Cross-section shading: lit on the left, dark rim on the right.
                    float across = halfWidth > 0.001f ? dx / halfWidth : 0f;   // -1..1
                    float lightIntensity = Mathf.Clamp01(0.5f - across * 0.55f);
                    Color c = Color.Lerp(shaft * 0.55f, shaft, lightIntensity);

                    // Specular streak just left of centre.
                    float spec = Mathf.Exp(-Mathf.Pow((across + 0.35f) * 3.2f, 2f));
                    c = Color.Lerp(c, Color.Lerp(c, Color.white, 0.55f), spec * 0.5f * t);

                    // Dark contact edge on the right so it separates from the hide.
                    if (across > 0.55f) c *= Mathf.Lerp(1f, 0.45f, (across - 0.55f) / 0.45f);

                    // Root fades to transparent so it melts into the body instead of
                    // ending in a cut-off stump.
                    if (t < 0.12f) a *= Mathf.SmoothStep(0f, 1f, t / 0.12f);

                    px[y * W + x] = new Color(c.r, c.g, c.b, a);
                }
            }

            tex.SetPixels(px); tex.Apply();
            _spike = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 160f);
            return _spike;
        }
    }

    private static Sprite _frostPatch;

    // An irregular rime patch: a blob whose radius wobbles with angle, with a
    // brighter crystalline core and spiky fringes. Used for the frozen puddle a
    // minion stands in.
    //
    // Angular noise rather than a clean circle matters here — a perfect disc reads
    // as "a UI marker", while a ragged edge reads as ice that spread across the
    // ground. Same reason the crack spokes are drawn: they give the eye something
    // to identify the shape as frost rather than a glow.
    public static Sprite FrostPatch
    {
        get
        {
            if (_frostPatch != null) return _frostPatch;

            const int S = 128;
            var tex = NewTex(S);
            var px = new Color[S * S];
            float c = (S - 1) * 0.5f;

            // Fixed seed: every patch shares one texture, so the shape must be
            // deterministic rather than depending on whenever it first got built.
            var rng = new System.Random(20260826);
            const int Lobes = 9;
            var lobePhase = new float[Lobes];
            var lobeAmp = new float[Lobes];
            for (int i = 0; i < Lobes; i++)
            {
                lobePhase[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
                lobeAmp[i] = 0.04f + (float)rng.NextDouble() * 0.10f;
            }

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);

                    // Wobble the outline.
                    float edge = 0.78f;
                    for (int i = 0; i < Lobes; i++)
                        edge += Mathf.Sin(ang * (i + 2) + lobePhase[i]) * lobeAmp[i] / (i + 1);

                    float a = 0f;
                    if (d < edge)
                    {
                        a = Mathf.Clamp01((edge - d) / 0.30f);
                        a = a * a * (3f - 2f * a);

                        // Radial cracks: thin bright spokes through the patch.
                        float spoke = Mathf.Abs(Mathf.Sin(ang * 7f + 0.7f));
                        if (spoke > 0.93f && d > 0.12f)
                            a = Mathf.Min(1f, a + 0.35f);
                    }

                    // Brighter toward the middle so it reads as thicker ice.
                    float lum = Mathf.Lerp(0.80f, 1f, Mathf.Clamp01(1f - d / Mathf.Max(0.01f, edge)));
                    px[y * S + x] = new Color(lum, lum, 1f, a);
                }
            }

            tex.SetPixels(px); tex.Apply();
            _frostPatch = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
            return _frostPatch;
        }
    }

    private static Texture2D NewTex(int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

    // Touch every cached sprite so the first spawn doesn't pay the texture-build
    // cost mid-combat. Called from Boss4.PrewarmSpriteFolders().
    public static void Prewarm()
    {
        var _ = SoftDisc; var __ = ThinRing; var ___ = Shard;
        var ____ = Additive; var _____ = FrostPatch; var ______ = Spike;
    }

    // Shared helper: a child GameObject carrying a configured SpriteRenderer.
    public static SpriteRenderer NewSprite(Transform parent, string name, Sprite sprite,
                                           string sortingLayer, int order, Color color)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = order;
        return sr;
    }
}

//  Walk dust — puffs kicked up under the boss's feet while it moves
// Attach to the boss. Emits a small ground puff every `interval` world-units of
// travel, so the cadence follows actual movement rather than a fixed timer (a
// stationary boss emits nothing, a fast one emits more). Heavier when the boss
// has grown from the Brute trait.
public class DevourerDustEmitter : MonoBehaviour
{
    [Tooltip("World-units of travel between puffs. Lower = more dust.")]
    public float distancePerPuff = 0.55f;

    [Tooltip("Ground offset from the transform pivot to the feet.")]
    public float footOffsetY = -0.15f;

    public Color dustColor = new Color(0.50f, 0.44f, 0.38f, 0.40f);
    public float puffLifetime = 0.55f;
    public float puffStartScale = 0.35f;
    public float puffEndScale = 1.15f;
    public string sortingLayer = "Default";

    // Scales puff size with the boss's own scale so a Brute-grown Devourer kicks
    // up proportionally bigger clouds.
    public float sizeMultiplier = 1f;

    private Vector3 _lastPos;
    private float _accumulated;
    private bool _started;

    private void Start()
    {
        _lastPos = transform.position;
        _started = true;
    }

    private void Update()
    {
        if (!_started) return;

        float moved = Vector3.Distance(transform.position, _lastPos);
        _lastPos = transform.position;

        // Teleport / respawn guard: a huge single-frame jump must not dump a
        // hundred puffs along a line the boss never walked.
        if (moved > 3f) return;

        _accumulated += moved;
        if (_accumulated < Mathf.Max(0.05f, distancePerPuff)) return;
        _accumulated = 0f;

        SpawnPuff();
    }

    private void SpawnPuff()
    {
        Vector3 at = transform.position + new Vector3(Random.Range(-0.22f, 0.22f), footOffsetY, 0f);
        var go = new GameObject("Devourer_DustPuff");
        go.transform.position = at;

        var sr = DevourerSprites.NewSprite(go.transform, "Sprite", Boss2VFXSprites.GetSoftDisc(),
                                           sortingLayer, DevourerFXOrder.GroundAt(at.y), dustColor);
        var fade = go.AddComponent<DevourerFadeSprite>();
        fade.Play(sr, puffLifetime,
                  puffStartScale * sizeMultiplier, puffEndScale * sizeMultiplier,
                  drift: new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(0.12f, 0.4f)));

        // Every few steps, kick a couple of grit chunks. A creature this heavy
        // should displace ground, not just raise a haze — the chunks are what make
        // the footfall read as weight.
        if (Random.value > 0.55f) return;
        int grit = Random.Range(1, 3);
        for (int i = 0; i < grit; i++)
        {
            var gGO = new GameObject("Devourer_Grit");
            gGO.transform.position = at;
            var gsr = DevourerSprites.NewSprite(gGO.transform, "Sprite", Boss2VFXSprites.GetRockChunk(),
                                                sortingLayer, DevourerFXOrder.GroundAt(at.y) + 1,
                                                new Color(dustColor.r * 0.8f, dustColor.g * 0.8f,
                                                          dustColor.b * 0.8f, 0.85f));
            float a = Random.Range(0f, Mathf.PI * 2f);
            gGO.AddComponent<DevourerFadeSprite>().Play(
                gsr, Random.Range(0.3f, 0.55f),
                0.10f * sizeMultiplier, 0.07f * sizeMultiplier,
                drift: new Vector2(Mathf.Cos(a) * Random.Range(0.6f, 1.6f), Random.Range(0.5f, 1.4f)),
                spinDegPerSec: Random.Range(-400f, 400f));
        }
    }
}

//  Generic "grow + fade + drift then self-destruct" driver
// Used by nearly every effect in this file. Keeping one driver means a single
// place can be tuned, and every effect is guaranteed to clean itself up — no
// orphaned VFX objects accumulating across a long fight.
public class DevourerFadeSprite : MonoBehaviour
{
    private SpriteRenderer _sr;
    private float _life, _t, _from, _to;
    private Vector2 _drift;
    private Color _base;
    private float _spinDegPerSec;
    private bool _running;

    // When set, _from/_to are MULTIPLIERS of the scale the sprite already had, and
    // the original (possibly non-uniform) scale is preserved.
    private bool _preserveScale;
    private Vector3 _baseScale = Vector3.one;

    // Absolute uniform scaling. Correct for effects we create ourselves at scale 1
    // (dust, motes, blast puffs), where "size 0.3" means 0.3 world units.
    public void Play(SpriteRenderer sr, float lifetime, float fromScale, float toScale,
                     Vector2 drift = default, float spinDegPerSec = 0f)
    {
        Init(sr, lifetime, fromScale, toScale, drift, spinDegPerSec);
        _preserveScale = false;
        if (_sr != null) _sr.transform.localScale = Vector3.one * _from;
    }

    // Scale-RELATIVE playback, for sprites that already have a meaningful scale of
    // their own.
    //
    // This exists because the absolute version above was being used on the frost
    // overlays, and it is why an eaten minion flashed up huge for a frame. The
    // overlay is a copy of the enemy's sprite parented under it; the enemy prefabs
    // are authored at a small transform scale (the Devourer itself sits at 0.25).
    // Shatter() detaches the overlay with SetParent(null, true), which converts that
    // small inherited scale into its local scale — and then Play() overwrote it with
    // Vector3.one, inflating a full copy of the enemy sprite by 4x for the length of
    // the fade. Non-uniform scales (the ground frost patch is squashed on Y, the
    // shards are tall and thin) were distorted by the same line.
    public void PlayPreserveScale(SpriteRenderer sr, float lifetime, float fromMul, float toMul,
                                  Vector2 drift = default, float spinDegPerSec = 0f)
    {
        Init(sr, lifetime, fromMul, toMul, drift, spinDegPerSec);
        _preserveScale = true;
        _baseScale = _sr != null ? _sr.transform.localScale : Vector3.one;
        if (_sr != null) _sr.transform.localScale = _baseScale * _from;
    }

    private void Init(SpriteRenderer sr, float lifetime, float from, float to,
                      Vector2 drift, float spin)
    {
        _sr = sr;
        _life = Mathf.Max(0.01f, lifetime);
        _from = from; _to = to;
        _drift = drift;
        _spinDegPerSec = spin;
        _base = sr != null ? sr.color : Color.white;
        _t = 0f;
        _running = true;
    }

    private void Update()
    {
        if (!_running) return;
        if (_sr == null) { Destroy(gameObject); return; }

        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);

        float eased = 1f - (1f - k) * (1f - k);
        float sc = Mathf.Lerp(_from, _to, eased);
        _sr.transform.localScale = _preserveScale ? _baseScale * sc : Vector3.one * sc;

        var c = _base; c.a = _base.a * (1f - k);
        _sr.color = c;

        transform.position += (Vector3)(_drift * Time.deltaTime);
        if (!Mathf.Approximately(_spinDegPerSec, 0f))
            _sr.transform.Rotate(0f, 0f, _spinDegPerSec * Time.deltaTime);

        if (k >= 1f) Destroy(gameObject);
    }
}

public static class DevourerEatBurst
{
    public static void Play(Vector3 at, Color tint, string sortingLayer, int moteCount = 16)
    {
        var root = new GameObject("Devourer_EatBurst");
        root.transform.position = new Vector3(at.x, at.y, 0f);

        // Dark core: a swallow is matter being REMOVED, so the centre goes dark and
        // the energy spirals into it. A bright flash would read as an explosion —
        // the opposite event.
        var voidCore = DevourerSprites.NewSprite(root.transform, "VoidCore",
                                                 Boss2VFXSprites.GetSoftDisc(),
                                                 sortingLayer, DevourerFXOrder.Air + 2,
                                                 new Color(0.05f, 0.01f, 0.09f, 0.9f));
        root.AddComponent<DevourerFadeSprite>().Play(voidCore, 0.30f, 1.1f, 0.05f);

        var ringGO = new GameObject("EatRing");
        ringGO.transform.position = root.transform.position;
        var ring = DevourerSprites.NewSprite(ringGO.transform, "Sprite",
                                             Boss2WarningSprites.GetRing(0.03f),
                                             sortingLayer, DevourerFXOrder.Air + 1,
                                             new Color(tint.r, tint.g, tint.b, 0.7f));
        ringGO.AddComponent<DevourerFadeSprite>().Play(ring, 0.34f, 2.6f, 0.2f, spinDegPerSec: 120f);

        // Motes spiral in rather than travelling straight. A straight radial line
        // reads as a starburst played backwards; angular momentum is what makes it
        // read as suction.
        for (int i = 0; i < moteCount; i++)
        {
            float ang = (i / (float)moteCount) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
            float dist = Random.Range(0.9f, 2.1f);
            Vector3 startPos = root.transform.position
                               + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f, 0f) * dist;

            var moteGO = new GameObject("EatMote");
            moteGO.transform.position = startPos;
            var sr = DevourerSprites.NewSprite(moteGO.transform, "Sprite",
                                               Boss2VFXSprites.GetSoftDisc(),
                                               sortingLayer, DevourerFXOrder.Air + 3,
                                               Color.Lerp(tint, Color.white, Random.Range(0f, 0.4f)));
            sr.sharedMaterial = DevourerSprites.Additive;

            var suck = moteGO.AddComponent<DevourerSuckMote>();
            suck.Play(sr, root.transform.position, Random.Range(0.26f, 0.46f),
                      Random.Range(0.14f, 0.30f));
        }

        Object.Destroy(root, 1.2f);
    }
}

// A mote that accelerates toward a point and winks out on arrival.
public class DevourerSuckMote : MonoBehaviour
{
    private SpriteRenderer _sr;
    private Vector3 _target;
    private float _dur, _t, _scale;
    private float _startRadius, _startAngle, _spin;
    private Color _base;

    public void Play(SpriteRenderer sr, Vector3 target, float duration, float scale)
    {
        _sr = sr; _target = target;
        _dur = Mathf.Max(0.05f, duration); _scale = scale;
        _base = sr != null ? sr.color : Color.white;

        Vector3 offset = transform.position - target;
        _startRadius = offset.magnitude;
        _startAngle = Mathf.Atan2(offset.y, offset.x);
        // Half a turn to a full turn on the way in, direction randomised.
        _spin = Random.Range(Mathf.PI * 0.6f, Mathf.PI * 1.4f) * (Random.value < 0.5f ? -1f : 1f);

        if (_sr != null) _sr.transform.localScale = Vector3.one * _scale;
    }

    private void Update()
    {
        if (_sr == null) { Destroy(gameObject); return; }

        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _dur);
        float eased = k * k;                                  // accelerate inward

        // Polar interpolation: the radius collapses while the angle keeps winding,
        // which traces a spiral instead of a straight line.
        float radius = Mathf.Lerp(_startRadius, 0f, eased);
        float angle = _startAngle + _spin * eased;
        transform.position = _target + new Vector3(Mathf.Cos(angle) * radius,
                                                   Mathf.Sin(angle) * radius * 0.8f, 0f);

        var c = _base; c.a = _base.a * (1f - k * k);
        _sr.color = c;
        _sr.transform.localScale = Vector3.one * Mathf.Lerp(_scale, _scale * 0.25f, k);

        if (k >= 1f) Destroy(gameObject);
    }
}


//  Bite telegraph — the ground marking that shows WHERE the bite will land
// Draws either a full disc (Eye trait) or a forward wedge (Pitcher trait) as a
// procedural mesh, then fills it up over the wind-up so the player can read the
// timing as well as the shape. Self-destructs when the window closes.
//
// Mesh rather than a sprite because a cone's angle is configurable at runtime
// and expands with the Mortar trait — no single sprite could cover that.
public class DevourerBiteTelegraph : MonoBehaviour
{
    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private MeshFilter _fillFilter;
    private MeshRenderer _fillRenderer;
    private Material _material;
    private float _life, _t;
    private Color _idle, _hot;

    // The alpha weighting baked into each vertex by BuildWedgeMesh (dim fill,
    // bright rim, transparent falloff). Cached ONCE: reading it back off the mesh
    // every frame would multiply the weight into itself and fade the telegraph to
    // nothing within a second.
    private float[] _baseAlpha;
    private float[] _fillBaseAlpha;

    public static DevourerBiteTelegraph Spawn(
        Vector3 centre, float radius, float halfAngleDeg, Vector2 facing,
        float duration, Color idle, Color hot, string sortingLayer)
    {
        var go = new GameObject("Devourer_BiteTelegraph");
        go.transform.position = new Vector3(centre.x, centre.y, 0f);

        float faceDeg = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.Euler(0f, 0f, faceDeg);

        var tel = go.AddComponent<DevourerBiteTelegraph>();
        tel.Build(radius, halfAngleDeg, duration, idle, hot, sortingLayer);
        return tel;
    }

    private void Build(float radius, float halfAngleDeg, float duration,
                       Color idle, Color hot, string sortingLayer)
    {
        _life = Mathf.Max(0.05f, duration);
        _idle = idle; _hot = hot;

        _filter = gameObject.AddComponent<MeshFilter>();
        _renderer = gameObject.AddComponent<MeshRenderer>();
        _filter.mesh = BuildWedgeMesh(radius, halfAngleDeg, 48);
        _baseAlpha = CaptureAlphas(_filter);

        // Sprites/Default with additive blending, exactly as ScarecrowAuraVisual
        // does: a white main texture so the vertex colours survive, and additive
        // blend so the decal glows over the ground instead of muddying it.
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        // No _SrcBlend/_DstBlend writes here: Sprites/Default hardcodes
        // `Blend One OneMinusSrcAlpha` and exposes no blend properties, so those
        // calls were no-ops. TintMesh premultiplies instead, which makes that fixed
        // blend behave as proper alpha blending.
        _material = new Material(sh);
        _material.mainTexture = Texture2D.whiteTexture;
        _renderer.material = _material;
        _renderer.sortingLayerName = sortingLayer;
        _renderer.sortingOrder = DevourerFXOrder.GroundAt(transform.position.y);

        // Countdown fill: an identical shape, scaled 0 -> 1 over the wind-up. This
        // is what turns the marker from "something happens here" into "something
        // happens here, NOW" — the player reads the remaining time off the sweep
        // instead of having to learn the boss's timings by dying to them.
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(transform, false);
        fillGO.transform.localPosition = Vector3.zero;
        fillGO.transform.localRotation = Quaternion.identity;
        fillGO.transform.localScale = Vector3.zero;

        _fillFilter = fillGO.AddComponent<MeshFilter>();
        _fillRenderer = fillGO.AddComponent<MeshRenderer>();
        _fillFilter.mesh = BuildWedgeMesh(radius, halfAngleDeg, 48);
        _fillBaseAlpha = CaptureAlphas(_fillFilter);
        _fillRenderer.material = _material;          // shares the additive material
        _fillRenderer.sortingLayerName = sortingLayer;
        _fillRenderer.sortingOrder = _renderer.sortingOrder + 1;
    }

    // Fan mesh centred on +X, spanning +/-halfAngleDeg. A 180 half-angle produces a
    // full disc, so the circle and the cone share one code path.
    //
    // FIVE concentric rings with a smooth alpha curve, plus ANGULAR falloff on the
    // cone's straight sides.
    //
    // The angular falloff is the important part. A wedge built with uniform alpha
    // across its span has two hard radial edges, and those edges are what read as
    // "weird lines" rather than as a shape — the eye sees two strokes and a boundary
    // instead of a lit area. Fading the alpha out over the last ~18% of the sweep
    // turns the sides into soft gradients, so the cone reads as a beam of light on
    // the ground. The full disc skips this (it has no sides to soften).
    //
    // The radial curve is likewise a gradient, not a stripe: brightest just inside
    // the rim and easing both inward and outward, which gives a glowing edge without
    // a drawn outline.
    private static Mesh BuildWedgeMesh(float radius, float halfAngleDeg, int segments)
    {
        halfAngleDeg = Mathf.Clamp(halfAngleDeg, 1f, 180f);
        bool full = halfAngleDeg >= 179.9f;

        var m = new Mesh { name = "DevourerWedge" };

        // Radial profile: (fraction of radius, alpha weight).
        float[] ringR = { 0.00f, 0.45f, 0.78f, 0.93f, 1.00f };
        float[] ringA = { 0.30f, 0.26f, 0.34f, 0.85f, 0.00f };
        int rings = ringR.Length;

        int rim = segments + 1;
        var verts = new Vector3[rings * rim];
        var cols = new Color[verts.Length];

        float startRad = -halfAngleDeg * Mathf.Deg2Rad;
        float sweep = (full ? 360f : halfAngleDeg * 2f) * Mathf.Deg2Rad;

        for (int r = 0; r < rings; r++)
        {
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;             // 0..1 across the sweep
                float a = startRad + sweep * u;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

                int idx = r * rim + i;
                verts[idx] = dir * radius * ringR[r];

                float alpha = ringA[r];

                if (!full)
                {
                    // Soften both angular edges. Distance from the nearer side,
                    // normalised, smoothstepped over the outer 18% of the sweep.
                    float edgeDist = Mathf.Min(u, 1f - u) / 0.18f;
                    edgeDist = Mathf.Clamp01(edgeDist);
                    alpha *= edgeDist * edgeDist * (3f - 2f * edgeDist);
                }

                cols[idx] = new Color(1f, 1f, 1f, alpha);
            }
        }

        var tris = new System.Collections.Generic.List<int>(segments * (rings - 1) * 6);
        for (int r = 0; r < rings - 1; r++)
        {
            for (int i = 0; i < segments; i++)
            {
                int a0 = r * rim + i, a1 = r * rim + i + 1;
                int b0 = (r + 1) * rim + i, b1 = (r + 1) * rim + i + 1;

                tris.Add(a0); tris.Add(b0); tris.Add(b1);
                tris.Add(a0); tris.Add(b1); tris.Add(a1);
            }
        }

        m.vertices = verts;
        m.colors = cols;
        m.triangles = tris.ToArray();
        m.RecalculateBounds();
        return m;
    }

    private void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);

        // Outline: ramps from calm to hot, with a pulse that quickens so the beat
        // peaks exactly as the attack lands.
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * Mathf.Lerp(8f, 30f, k));
        Color edge = Color.Lerp(_idle, _hot, k * 0.75f + pulse * 0.25f * k);
        TintMesh(_filter, _baseAlpha, edge, edge.a);

        // Fill sweeps outward to meet the rim.
        if (_fillFilter != null)
        {
            float eased = k * k;                      // slow start, rushes at the end
            _fillFilter.transform.localScale = new Vector3(eased, eased, 1f);
            Color fill = Color.Lerp(_idle, _hot, k);
            TintMesh(_fillFilter, _fillBaseAlpha, fill, fill.a * 0.75f);
        }

        if (k >= 1f) Destroy(gameObject);
    }

    private static float[] CaptureAlphas(MeshFilter mf)
    {
        if (mf == null || mf.mesh == null) return null;
        var cols = mf.mesh.colors;
        if (cols == null) return null;

        var a = new float[cols.Length];
        for (int i = 0; i < cols.Length; i++) a[i] = cols[i].a;
        return a;
    }

    // Writes one colour across the mesh, re-applying the CACHED per-vertex alpha
    // weighting so the glow band stays a band and the fill stays dim.
    //
    // Colours are written PREMULTIPLIED (rgb * a). Unity's Sprites/Default shader
    // has `Blend One OneMinusSrcAlpha` hardcoded — it does not expose _SrcBlend
    // /_DstBlend at all, so setting those properties (as the older code did, and as
    // ScarecrowAuraVisual does) silently changes nothing. Feeding straight colours
    // into a premultiplied blend adds full-strength RGB regardless of alpha, which
    // is why bright telegraphs washed out into flat glare. Premultiplying here makes
    // that same fixed blend behave as correct alpha blending, so a 0.3-alpha
    // telegraph actually looks 30% opaque.
    private static void TintMesh(MeshFilter mf, float[] baseAlpha, Color c, float alphaScale)
    {
        if (mf == null || mf.mesh == null || baseAlpha == null) return;

        var mesh = mf.mesh;
        var cols = mesh.colors;
        if (cols == null || cols.Length != baseAlpha.Length) return;

        for (int i = 0; i < cols.Length; i++)
        {
            float a = Mathf.Clamp01(baseAlpha[i] * alphaScale);
            cols[i] = new Color(c.r * a, c.g * a, c.b * a, a);
        }

        mesh.colors = cols;
    }

    // Cancel early (the boss died mid-wind-up, or the attack was aborted).
    public void Cancel() { if (this != null) Destroy(gameObject); }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}

//  Shock ring — the expanding blast wave used by mini-blasts and Kaboom
// Layered blast VFX, replacing the old single expanding hoop.
//
// Boss2MeteorVFX's own comment names the problem exactly: flat dust rings and
// scorch discs "read as ugly circles". A real detonation is a stack of short,
// overlapping events — a white bloom at t=0, a THIN fast pressure ring, a
// billowing core that cools through a colour gradient, embers arcing off it, and
// dust settling afterwards. No single element is a thick hoop.
//
// This deliberately reuses Boss2VFXSprites / Boss2WarningSprites rather than
// growing a parallel set of procedural textures, so the Devourer's blasts share
// the exact visual vocabulary of the meteors the player has already learned to
// read, and there is one place to change if that style is ever retuned.
public enum DevourerBlastPalette { Fire, Frost, Void }

public static class DevourerBlast
{
    public static void Play(Vector3 at, float radius, DevourerBlastPalette palette,
                            string sortingLayer, bool withDebris = true)
    {
        var root = new GameObject("Devourer_Blast");
        root.transform.position = new Vector3(at.x, at.y, 0f);
        var b = root.AddComponent<DevourerBlastRunner>();
        b.Run(radius, palette, sortingLayer, withDebris);
    }
}

public class DevourerBlastRunner : MonoBehaviour
{
    private float _r;
    private string _layer;
    private DevourerBlastPalette _palette;

    private Color _hot, _mid, _cool, _dust;

    public void Run(float radius, DevourerBlastPalette palette, string sortingLayer, bool withDebris)
    {
        _r = Mathf.Max(0.3f, radius);
        _layer = sortingLayer;
        _palette = palette;
        ResolvePalette();

        BloomFlash();
        PressureRing(0.030f, 0.24f, _r * 1.00f, 0.55f, 0f);
        PressureRing(0.018f, 0.34f, _r * 1.22f, 0.28f, 0.05f);
        Core();
        Billow();
        Embers();
        if (withDebris) Debris();

        Destroy(gameObject, 2.6f);
    }

    private void ResolvePalette()
    {
        switch (_palette)
        {
            case DevourerBlastPalette.Frost:
                _hot = new Color(0.92f, 0.98f, 1f);
                _mid = new Color(0.55f, 0.80f, 1f);
                _cool = new Color(0.20f, 0.38f, 0.80f);
                _dust = new Color(0.72f, 0.84f, 0.95f);
                break;
            case DevourerBlastPalette.Void:
                _hot = new Color(1f, 0.90f, 1f);
                _mid = new Color(0.78f, 0.30f, 1f);
                _cool = new Color(0.26f, 0.06f, 0.42f);
                _dust = new Color(0.50f, 0.38f, 0.62f);
                break;
            default: // Fire
                _hot = new Color(1f, 0.95f, 0.80f);
                _mid = new Color(1f, 0.55f, 0.10f);
                _cool = new Color(0.55f, 0.10f, 0.05f);
                _dust = new Color(0.62f, 0.52f, 0.42f);
                break;
        }
    }

    private SpriteRenderer Make(string name, Sprite sprite, int order, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = c;
        sr.sortingLayerName = _layer;
        sr.sortingOrder = order;
        return sr;
    }

    // t=0 white pop. Very short — it is the "camera flinch", not the explosion.
    private void BloomFlash()
    {
        var sr = Make("Bloom", Boss2VFXSprites.GetSoftDisc(), DevourerFXOrder.Air + 4,
                      new Color(_hot.r, _hot.g, _hot.b, 0.95f));
        StartCoroutine(Scale(sr, 0.14f, _r * 0.5f, _r * 1.5f, 0.95f, 0f));
    }

    // Thin, fast, faint. Boss2 uses 0.04/0.025 thickness fractions for the same
    // reason: any thicker and it stops reading as a pressure wave and starts
    // reading as a drawn hoop.
    private void PressureRing(float thickness, float life, float maxR, float alpha, float delay)
    {
        var sr = Make("Pressure", Boss2WarningSprites.GetRing(thickness), DevourerFXOrder.Air + 3,
                      new Color(_hot.r, _hot.g, _hot.b, alpha));
        StartCoroutine(Scale(sr, life, _r * 0.25f, maxR * 2f, alpha, delay));
    }

    // The bright pinpoint that lingers a beat longer than the bloom.
    private void Core()
    {
        var sr = Make("Core", Boss2VFXSprites.GetSoftDisc(), DevourerFXOrder.Air + 2,
                      new Color(_hot.r, _hot.g, _hot.b, 1f));
        StartCoroutine(CoreRoutine(sr));
    }

    private IEnumerator CoreRoutine(SpriteRenderer sr)
    {
        float life = 0.34f, t = 0f;
        while (t < life)
        {
            if (sr == null) yield break;
            t += Time.deltaTime;
            float p = t / life;
            Color c = Color.Lerp(_hot, _mid, p);
            c.a = 1f - p;
            sr.color = c;
            float k = Mathf.Lerp(_r * 0.7f, _r * 0.25f, p);
            sr.transform.localScale = Vector3.one * k;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    // Overlapping puffs that expand and cool. This is the body of the explosion —
    // several offset blobs rather than one disc, so the silhouette is irregular.
    private void Billow()
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(_r * 3f), 5, 12);
        for (int i = 0; i < count; i++)
        {
            float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.35f, 0.35f);
            float dist = _r * Random.Range(0.08f, 0.52f);

            var sr = Make("Billow_" + i, Boss2VFXSprites.GetSoftDisc(),
                          DevourerFXOrder.Air + 1, _mid);
            sr.transform.localPosition = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.75f, 0f) * dist;
            StartCoroutine(BillowRoutine(sr, ang, Random.Range(0.45f, 0.95f)));
        }
    }

    private IEnumerator BillowRoutine(SpriteRenderer sr, float ang, float life)
    {
        float t = 0f;
        float from = _r * Random.Range(0.30f, 0.55f);
        float to = _r * Random.Range(0.85f, 1.35f);
        Vector3 drift = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f + 0.35f, 0f)
                        * _r * Random.Range(0.15f, 0.4f);
        Vector3 start = sr.transform.localPosition;

        while (t < life)
        {
            if (sr == null) yield break;
            t += Time.deltaTime;
            float p = t / life;

            // Hot -> mid -> cool: the gradient is what makes it read as fire
            // cooling into smoke rather than a coloured circle fading out.
            Color c = p < 0.28f ? Color.Lerp(_hot, _mid, p / 0.28f)
                                : Color.Lerp(_mid, _cool, (p - 0.28f) / 0.72f);
            c.a = (1f - p) * 0.85f;
            sr.color = c;

            float eased = 1f - (1f - p) * (1f - p);
            sr.transform.localScale = Vector3.one * Mathf.Lerp(from, to, eased);
            sr.transform.localPosition = start + drift * eased;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private void Embers()
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(_r * 4f), 6, 18);
        for (int i = 0; i < count; i++)
        {
            var sr = Make("Ember_" + i, Boss2VFXSprites.GetSoftDisc(), DevourerFXOrder.Air + 3,
                          Color.Lerp(_hot, _mid, Random.value));
            StartCoroutine(EmberRoutine(sr));
        }
    }

    // Embers arc: thrown out fast, then dragged down. A straight radial line reads
    // as a starburst decal; adding gravity is what makes them read as thrown matter.
    private IEnumerator EmberRoutine(SpriteRenderer sr)
    {
        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector2 vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f + 0.5f)
                      * _r * Random.Range(1.6f, 3.4f);
        float life = Random.Range(0.45f, 0.9f), t = 0f;
        float size = _r * Random.Range(0.05f, 0.11f);
        Color baseC = sr.color;
        Vector3 pos = Vector3.zero;

        while (t < life)
        {
            if (sr == null) yield break;
            t += Time.deltaTime;
            float p = t / life;

            vel.y -= 9f * Time.deltaTime;          // gravity drag
            vel *= 0.96f;
            pos += (Vector3)(vel * Time.deltaTime);

            sr.transform.localPosition = pos;
            sr.transform.localScale = Vector3.one * size * (1f - p * 0.6f);

            Color c = Color.Lerp(baseC, _cool, p);
            c.a = 1f - p;
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private void Debris()
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(_r * 2f), 3, 9);
        for (int i = 0; i < count; i++)
        {
            var sr = Make("Debris_" + i, Boss2VFXSprites.GetRockChunk(), DevourerFXOrder.Air + 2,
                          Color.Lerp(_dust, _cool, Random.Range(0f, 0.5f)));
            StartCoroutine(DebrisRoutine(sr));
        }
    }

    private IEnumerator DebrisRoutine(SpriteRenderer sr)
    {
        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector2 vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.7f + 0.7f)
                      * _r * Random.Range(1.2f, 2.6f);
        float spin = Random.Range(-540f, 540f);
        float life = Random.Range(0.6f, 1.1f), t = 0f;
        float size = _r * Random.Range(0.10f, 0.22f);
        Color baseC = sr.color;
        Vector3 pos = Vector3.zero;

        sr.transform.localScale = Vector3.one * size;

        while (t < life)
        {
            if (sr == null) yield break;
            t += Time.deltaTime;
            float p = t / life;

            vel.y -= 14f * Time.deltaTime;
            pos += (Vector3)(vel * Time.deltaTime);

            sr.transform.localPosition = pos;
            sr.transform.Rotate(0f, 0f, spin * Time.deltaTime);

            Color c = baseC; c.a = 1f - p * p;
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private IEnumerator Scale(SpriteRenderer sr, float life, float from, float to,
                              float alpha, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float t = 0f;
        Color baseC = sr != null ? sr.color : Color.white;

        while (t < life)
        {
            if (sr == null) yield break;
            t += Time.deltaTime;
            float p = t / life;
            float eased = 1f - (1f - p) * (1f - p);
            sr.transform.localScale = Vector3.one * Mathf.Lerp(from, to, eased);
            Color c = baseC; c.a = alpha * (1f - p);
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }
}

public class DevourerPoisonPool : MonoBehaviour
{
    private float _radius = 1.6f;
    private float _dps = 6f;
    private float _life = 6f;
    private GameObject _attacker;
    private bool _hitsBuildings = true;

    private float _age;
    private SpriteRenderer _body;
    private readonly List<SpriteRenderer> _bubbles = new List<SpriteRenderer>();

    private readonly Dictionary<CharacterStats, float> _playerAcc = new Dictionary<CharacterStats, float>();
    private readonly HashSet<CharacterStats> _insideThisFrame = new HashSet<CharacterStats>();
    private readonly List<CharacterStats> _accScratch = new List<CharacterStats>();
    private float _buildingAcc;
    private float _bubbleTimer;
    private string _sortingLayer = "Default";
    private int _order;

    private static readonly Color PoolColor = new Color(0.35f, 0.85f, 0.25f, 0.5f);

    public static DevourerPoisonPool Spawn(Vector3 at, float radius, float dps, float lifetime,
                                           GameObject attacker, string sortingLayer,
                                           bool hitsBuildings = true)
    {
        var go = new GameObject("Devourer_PoisonPool");
        go.transform.position = new Vector3(at.x, at.y, 0f);
        var pool = go.AddComponent<DevourerPoisonPool>();
        pool.Init(radius, dps, lifetime, attacker, sortingLayer, hitsBuildings);
        return pool;
    }

    private void Init(float radius, float dps, float lifetime, GameObject attacker,
                      string sortingLayer, bool hitsBuildings)
    {
        _radius = Mathf.Max(0.2f, radius);
        _dps = Mathf.Max(0f, dps);
        _life = Mathf.Max(0.5f, lifetime);
        _attacker = attacker;
        _hitsBuildings = hitsBuildings;

        int order = DevourerFXOrder.GroundAt(transform.position.y);
        _sortingLayer = sortingLayer;
        _order = order;

        // The irregular FrostPatch silhouette, recoloured. A soft disc reads as a
        // glow; a ragged outline reads as spilled liquid that found the low ground.
        // Squashed on Y so it lies on the ground plane rather than facing camera.
        _body = DevourerSprites.NewSprite(transform, "PoolBody", DevourerSprites.FrostPatch,
                                          sortingLayer, order, PoolColor);
        _body.transform.localScale = new Vector3(_radius * 2.3f, _radius * 1.5f, 1f);
        _body.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        // Overlapping lobes at varied rotations break the outline up further, so
        // repeated pools never stamp the same shape twice.
        for (int i = 0; i < 4; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float d = Random.Range(0.15f, 0.55f) * _radius;
            var blob = DevourerSprites.NewSprite(transform, "PoolLobe", DevourerSprites.FrostPatch,
                                                 sortingLayer, order,
                                                 new Color(PoolColor.r, PoolColor.g, PoolColor.b, PoolColor.a * 0.75f));
            blob.transform.localPosition = new Vector3(Mathf.Cos(ang) * d, Mathf.Sin(ang) * d * 0.6f, 0f);
            blob.transform.localScale = new Vector3(_radius * Random.Range(0.8f, 1.5f),
                                                    _radius * Random.Range(0.5f, 0.95f), 1f);
            blob.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            _bubbles.Add(blob);
        }
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _life) { Destroy(gameObject); return; }

        // Fade out over the last 25% of life so it doesn't vanish abruptly.
        float fade = Mathf.Clamp01((_life - _age) / (_life * 0.25f));
        float breathe = 1f + Mathf.Sin(_age * 2.2f) * 0.04f;

        if (_body != null)
        {
            var c = PoolColor; c.a = PoolColor.a * fade;
            _body.color = c;
            _body.transform.localScale = Vector3.one * (_radius * 2f * breathe);
        }
        for (int i = 0; i < _bubbles.Count; i++)
        {
            if (_bubbles[i] == null) continue;
            var c = _bubbles[i].color; c.a = PoolColor.a * 0.8f * fade;
            _bubbles[i].color = c;
        }

        TickBubbles(fade);
        TickPlayers();
        if (_hitsBuildings) TickBuildings();
    }

    // Gas bubbles swelling up out of the pool and popping. Cheap, but it is the
    // difference between a static decal and something that looks caustic and alive.
    private void TickBubbles(float fade)
    {
        if (fade < 0.35f) return;              // stop bubbling as it dries up

        _bubbleTimer -= Time.deltaTime;
        if (_bubbleTimer > 0f) return;
        _bubbleTimer = Random.Range(0.14f, 0.34f);

        float ang = Random.Range(0f, Mathf.PI * 2f);
        float d = Random.Range(0f, 0.8f) * _radius;
        Vector3 at = transform.position + new Vector3(Mathf.Cos(ang) * d, Mathf.Sin(ang) * d * 0.6f, 0f);

        var go = new GameObject("PoisonBubble");
        go.transform.position = at;
        var sr = DevourerSprites.NewSprite(go.transform, "Sprite", Boss2VFXSprites.GetSoftDisc(),
                                           _sortingLayer, _order + 2,
                                           new Color(0.55f, 1f, 0.35f, 0.55f));
        go.AddComponent<DevourerFadeSprite>().Play(
            sr, Random.Range(0.5f, 0.9f),
            _radius * 0.10f, _radius * Random.Range(0.28f, 0.5f),
            drift: new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.25f, 0.6f)));
    }

    private void TickPlayers()
    {
        if (_dps <= 0f || PlayerRegistry.Instance == null) return;

        _insideThisFrame.Clear();
        foreach (var stats in PlayerRegistry.Instance.AllAliveInRadius(transform.position, _radius))
        {
            if (stats == null) continue;
            _insideThisFrame.Add(stats);

            float acc = _playerAcc.TryGetValue(stats, out var prev) ? prev : 0f;
            acc += _dps * Time.deltaTime;
            if (acc >= 1f)
            {
                int whole = Mathf.FloorToInt(acc);
                acc -= whole;
                DevourerDamage.ApplySingle(stats.gameObject, whole, _attacker, hitBuildings: false);
            }
            _playerAcc[stats] = acc;
        }

        // Anyone who stepped out (or died) loses their accumulator, so re-entry
        // never dumps a stored spike on frame one.
        if (_playerAcc.Count > 0)
        {
            _accScratch.Clear();
            foreach (var kv in _playerAcc)
                if (!_insideThisFrame.Contains(kv.Key)) _accScratch.Add(kv.Key);
            for (int i = 0; i < _accScratch.Count; i++) _playerAcc.Remove(_accScratch[i]);
        }
    }

    private void TickBuildings()
    {
        if (_dps <= 0f) return;

        _buildingAcc += _dps * Time.deltaTime;
        if (_buildingAcc < 1f) return;

        int whole = Mathf.FloorToInt(_buildingAcc);
        _buildingAcc -= whole;

        // Buildings are static, so a slower sweep is plenty and keeps the pool
        // from running a full overlap query every single frame.
        var hits = Physics2D.OverlapCircleAll(transform.position, _radius);
        var seen = new HashSet<GameObject>();
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            if (col.GetComponentInParent<EnemyStats>() != null) continue;

            var go = DevourerDamage.ResolveDamageTarget(col);
            if (go == null || go.CompareTag("Player")) continue;
            if (!seen.Add(go)) continue;

            DevourerDamage.ApplySingle(go, whole, _attacker, hitBuildings: true);
        }
    }
}

//  Ice encasement — the visual for a frozen (stasis) minion
// Builds a small cluster of crystal shards around the host, plus a cold glow and
// a slow inner shimmer. Purely cosmetic: DevourerFrozenMinion owns the gameplay
// side. Call Shatter() to play the break-apart and remove itself.
public class DevourerIceEncasement : MonoBehaviour
{
    // The old build put a big white glow disc and a ring of shards at
    // (host order + 1) — ON TOP of the minion. That is why a spawn read as "a white
    // circle appears, then the enemy appears": the ice was covering the enemy, and
    // the enemy only became visible when the shell shattered.
    //
    // The rebuild keeps the silhouette readable at all times:
    //    order - 2 : ground frost decal (Y-sorted, so grass cannot bury it)
    //    order - 1 : back shards + cold backlight
    //    order + 1 : a FROST OVERLAY that mirrors the host's current sprite,
    //                tinted icy and additively blended, so the minion looks
    //                encased in ice rather than hidden behind it
    //    order + 2 : a few small foreground shards at the feet only
    private readonly List<SpriteRenderer> _backShards = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _frontShards = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _rimeCrust = new List<SpriteRenderer>();
    private SpriteRenderer _backGlow;
    private SpriteRenderer _groundFrost;
    private SpriteRenderer _groundFrostInner;
    private readonly List<SpriteRenderer> _frostOverlays = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _frostSources = new List<SpriteRenderer>();
    private SpriteRenderer _hostSprite;

    private string _sortingLayer = "Default";
    private int _order = DevourerFXOrder.Air;
    private float _phase;
    private float _bodyRadius = 0.5f;

    private static readonly Color IceTint = new Color(0.55f, 0.82f, 1f, 1f);
    private static readonly Color IceDeep = new Color(0.30f, 0.55f, 0.95f, 1f);
    private static readonly Color BackGlowTint = new Color(0.28f, 0.55f, 1f, 0.22f);
    private static readonly Color GroundFrostTint = new Color(0.62f, 0.86f, 1f, 0.34f);

    // Deny-window signal.
    //
    // This used to shift the ice toward amber, which was a mistake twice over: an
    // orange "frozen" enemy is a contradiction the eye refuses to read, and the warm
    // tint on an additively-blended overlay is exactly what produced the glowing
    // orange smear. The signal is now carried by BRIGHTNESS and PULSE RATE within
    // the cold palette — the ice goes from deep blue to near-white and beats faster
    // as the window closes, which is legible without ever leaving "this is ice".
    private static readonly Color IceWarnTint = new Color(0.92f, 0.99f, 1f, 1f);
    private static readonly Color GlowWarnTint = new Color(0.70f, 0.90f, 1f, 0.40f);

    // 0 = just spawned (safe), 1 = grace expired and the boss is coming.
    private float graceProgress;

    public void SetGraceProgress(float t) => graceProgress = Mathf.Clamp01(t);

    public static DevourerIceEncasement Attach(GameObject host, float bodyRadius, string sortingLayer, int order)
    {
        var go = new GameObject("Devourer_IceEncasement");
        go.transform.SetParent(host.transform, false);
        go.transform.localPosition = Vector3.zero;
        var ice = go.AddComponent<DevourerIceEncasement>();
        ice.Build(host, bodyRadius, sortingLayer, order);
        return ice;
    }

    private void Build(GameObject host, float bodyRadius, string sortingLayer, int order)
    {
        _sortingLayer = sortingLayer;
        _order = order;
        _phase = Random.Range(0f, Mathf.PI * 2f);
        _bodyRadius = Mathf.Max(0.25f, bodyRadius);
        // Several enemies mount their body SpriteRenderer on a CHILD, not the root
        // (EyeChains itself does `GetComponent ?? GetComponentInChildren`). Looking
        // only at the root left _hostSprite null on those, and a null host meant the
        // frost overlay never got a sprite assigned — so the minion showed shards
        // and a puddle but the body itself was never frosted.
        _hostSprite = host != null ? host.GetComponent<SpriteRenderer>() : null;
        if (_hostSprite == null && host != null)
        {
            var candidates = host.GetComponentsInChildren<SpriteRenderer>(true);
            float best = -1f;
            for (int i = 0; i < candidates.Length; i++)
            {
                var sr = candidates[i];
                if (sr == null || sr.sprite == null) continue;
                float area = sr.sprite.bounds.size.x * sr.sprite.bounds.size.y;
                if (area > best) { best = area; _hostSprite = sr; }
            }
        }

        float r = _bodyRadius;

        // Ground frost: a pale rime patch the minion is rooted into. Y-sorted so it
        // interleaves with the cartoon grass instead of vanishing beneath it.
        _groundFrost = DevourerSprites.NewSprite(transform, "GroundFrost", DevourerSprites.FrostPatch,
                                                 sortingLayer,
                                                 DevourerFXOrder.GroundAt(transform.position.y),
                                                 GroundFrostTint);
        _groundFrost.transform.localPosition = new Vector3(0f, -r * 0.80f, 0f);
        // Squashed on Y so the patch lies flat on the ground plane instead of
        // standing up like a wall, and rotated at random so repeated spawns don't
        // stamp an identical shape.
        _groundFrost.transform.localScale = new Vector3(r * 3.0f, r * 1.5f, 1f);
        _groundFrost.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        // A second, smaller and brighter patch offset slightly — two overlapping
        // irregular shapes read as spreading rime, one reads as a decal.
        _groundFrostInner = DevourerSprites.NewSprite(transform, "GroundFrostInner", DevourerSprites.FrostPatch,
                                                      sortingLayer,
                                                      DevourerFXOrder.GroundAt(transform.position.y) + 1,
                                                      new Color(0.80f, 0.93f, 1f, 0.30f));
        _groundFrostInner.transform.localPosition = new Vector3(Random.Range(-0.1f, 0.1f) * r, -r * 0.72f, 0f);
        _groundFrostInner.transform.localScale = new Vector3(r * 1.9f, r * 0.95f, 1f);
        _groundFrostInner.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        // Cold backlight, well behind and much softer than the old version.
        _backGlow = DevourerSprites.NewSprite(transform, "BackGlow", DevourerSprites.SoftDisc,
                                              sortingLayer, order - 1, BackGlowTint);
        _backGlow.transform.localScale = Vector3.one * (r * 2.0f);

        // Frost overlays mirror the enemy's own sprites each frame, tinted ice-blue
        // and additive. This is what actually sells "frozen": the minion keeps its
        // silhouette and colours but gains a hard cold sheen over the top.
        //
        // One overlay PER significant renderer, each parented to that renderer's own
        // transform at zero offset. A single overlay parented to the encasement
        // could not stay aligned with a body sprite mounted on a child (which several
        // enemies use), and could not cover secondary sprites at all. Parenting to
        // the renderer inherits its exact position, rotation, flip and scale for free.
        BuildFrostOverlays(host);

        // Back shards: the big spikes, splayed wide behind the body.
        BuildShardRing(_backShards, 8, order - 1, r, 1.0f, 2.0f, backdrop: true);

        // Front shards: small, low, at the feet only — they frame the minion
        // without covering the part of it you need to see to identify it.
        BuildShardRing(_frontShards, 6, order + 2, r, 0.55f, 1.05f, backdrop: false);

        // Rime crust: small irregular frost patches stuck ONTO the body, in front of
        // it. The overlay gives an even sheen, which alone can read as "tinted blue"
        // rather than "iced over"; broken-up crust across the silhouette is what
        // makes it read as a physical coating that formed on the surface.
        BuildRimeCrust(order + 3, r);
    }

    private void BuildShardRing(List<SpriteRenderer> into, int count, int order, float r,
                                float minLen, float maxLen, bool backdrop)
    {
        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) / count;

            // Back shards fan across the upper half, front shards hug the base.
            float ang = backdrop
                ? Mathf.Lerp(200f, 340f, t) + Random.Range(-8f, 8f)
                : Mathf.Lerp(200f, 340f, t) + Random.Range(-14f, 14f);
            float rad = ang * Mathf.Deg2Rad;

            var sr = DevourerSprites.NewSprite(transform, (backdrop ? "BackShard_" : "FrontShard_") + i,
                                               DevourerSprites.Shard, _sortingLayer, order,
                                               backdrop ? IceDeep : IceTint);

            float dist = r * Random.Range(0.5f, 1.0f);
            sr.transform.localPosition = new Vector3(
                Mathf.Cos(rad) * dist,
                -r * 0.7f + Mathf.Abs(Mathf.Sin(rad)) * r * 0.15f,
                0f);

            // Lean away from centre so the cluster reads as growing out of the ground.
            float lean = -Mathf.Cos(rad) * Random.Range(14f, 30f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, lean);
            sr.transform.localScale = new Vector3(Random.Range(0.5f, 0.85f) * r * 1.3f,
                                                  Random.Range(minLen, maxLen) * r * 1.5f, 1f);
            into.Add(sr);
        }
    }

    // Picks the renderers worth overlaying: the body, plus anything of comparable
    // size. Tiny sprites (individual chain links, small props) are skipped — they
    // already get the colour-multiply frosting, and giving each of ~60 chain links
    // its own extra renderer would double the draw calls for no visible gain.
    private void BuildFrostOverlays(GameObject host)
    {
        if (host == null) return;

        var all = host.GetComponentsInChildren<SpriteRenderer>(true);

        float largest = 0f;
        for (int i = 0; i < all.Length; i++)
        {
            var sr = all[i];
            if (sr == null || sr.sprite == null) continue;
            if (sr.GetComponentInParent<DevourerIceEncasement>() != null) continue;
            var sz = sr.sprite.bounds.size;
            largest = Mathf.Max(largest, sz.x * sz.y);
        }
        if (largest <= 0f) return;

        for (int i = 0; i < all.Length; i++)
        {
            var src = all[i];
            if (src == null || src.sprite == null) continue;
            if (src.GetComponentInParent<DevourerIceEncasement>() != null) continue;

            var sz = src.sprite.bounds.size;
            if (sz.x * sz.y < largest * 0.20f) continue;   // too small to be worth it

            var go = new GameObject("FrostOverlay");
            go.transform.SetParent(src.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var ov = go.AddComponent<SpriteRenderer>();
            ov.sprite = src.sprite;
            // ALPHA blend, deliberately not additive. Additive adds light, so an
            // overlay on top of already-bright art drives every channel to 1 and the
            // enemy turns into a white-hot smear — worse on big sprites, where the
            // overlay and the rime crust stack on the same pixels. Alpha blending
            // cannot exceed the colours being mixed, so it tints instead of blowing
            // out, and it looks the same on a dark enemy as on a bright one.
            ov.sortingLayerName = src.sortingLayerName;
            ov.sortingOrder = src.sortingOrder + 1;
            ov.color = new Color(IceTint.r, IceTint.g, IceTint.b, 0.62f);

            _frostOverlays.Add(ov);
            _frostSources.Add(src);
        }
    }

    private void SyncFrostOverlays()
    {
        for (int i = 0; i < _frostOverlays.Count; i++)
        {
            var ov = _frostOverlays[i];
            var src = _frostSources[i];
            if (ov == null || src == null) continue;

            ov.sprite = src.sprite;
            ov.flipX = src.flipX;
            ov.flipY = src.flipY;
            ov.sortingLayerName = src.sortingLayerName;
            ov.sortingOrder = src.sortingOrder + 1;
        }
    }

    // Cold vapour curling off the ice. Emitted on a timer rather than per-frame so
    // a field of frozen minions doesn't spawn hundreds of objects a second.
    private float _vaporTimer;

    private void EmitVapor()
    {
        var go = new GameObject("FrostVapor");
        go.transform.position = transform.position
            + new Vector3(Random.Range(-_bodyRadius, _bodyRadius) * 0.8f, -_bodyRadius * 0.5f, 0f);

        int order = _hostSprite != null ? _hostSprite.sortingOrder + 2 : _order;
        var sr = DevourerSprites.NewSprite(go.transform, "Sprite", DevourerSprites.SoftDisc,
                                           _sortingLayer, order,
                                           new Color(0.78f, 0.92f, 1f, 0.30f));
        go.AddComponent<DevourerFadeSprite>().Play(
            sr, Random.Range(0.9f, 1.5f),
            _bodyRadius * 0.35f, _bodyRadius * 0.9f,
            drift: new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0.25f, 0.55f)));
    }

    private void BuildRimeCrust(int order, float r)
    {
        int count = 7;
        for (int i = 0; i < count; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float dist = r * Random.Range(0.05f, 0.62f);

            var sr = DevourerSprites.NewSprite(transform, "Rime_" + i, DevourerSprites.FrostPatch,
                                               _sortingLayer, order,
                                               new Color(0.86f, 0.95f, 1f, Random.Range(0.30f, 0.55f)));
            sr.transform.localPosition = new Vector3(Mathf.Cos(ang) * dist,
                                                     Mathf.Sin(ang) * dist * 1.15f, 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            sr.transform.localScale = Vector3.one * r * Random.Range(0.22f, 0.48f);
            _rimeCrust.Add(sr);
        }
    }

    // The host died while encased. EnemyStats hides or replaces the body sprite as
    // part of its death handling, but the frost overlays are OUR objects and would
    // keep drawing the last pose — leaving a floating blue ghost with no enemy
    // inside it. That is the "ice only" leftover. Tear the shell down immediately.
    public void HostDied()
    {
        Shatter();
    }

    private void Update()
    {
        _vaporTimer -= Time.deltaTime;
        if (_vaporTimer <= 0f)
        {
            _vaporTimer = Random.Range(0.35f, 0.75f);
            EmitVapor();
        }

        // Keep every overlay locked to whatever frame its source is posed on.
        // The minion is frozen so sprites rarely change, but they CAN (the animator
        // settles on frame 0 a beat after spawn), and a stale overlay would sit there
        // as a ghost of the previous pose.
        SyncFrostOverlays();
        if (_backGlow != null && _hostSprite != null)
            _backGlow.sortingOrder = _hostSprite.sortingOrder - 1;
        if (_hostSprite != null)
            for (int i = 0; i < _rimeCrust.Count; i++)
                if (_rimeCrust[i] != null) _rimeCrust[i].sortingOrder = _hostSprite.sortingOrder + 3;

        float t = Time.time + _phase;

        // Blend toward the warning colour, and beat faster as the window closes.
        Color shardBase = Color.Lerp(IceTint, IceWarnTint, graceProgress);
        Color deepBase = Color.Lerp(IceDeep, IceWarnTint, graceProgress * 0.8f);
        Color glowBase = Color.Lerp(BackGlowTint, GlowWarnTint, graceProgress);
        float rate = Mathf.Lerp(1.6f, 7f, graceProgress);

        for (int i = 0; i < _backShards.Count; i++)
        {
            var sr = _backShards[i];
            if (sr == null) continue;
            float s = 0.5f + 0.5f * Mathf.Sin(t * rate + i * 0.9f);
            var c = deepBase; c.a = Mathf.Lerp(0.75f, 1f, s);
            sr.color = c;
        }
        for (int i = 0; i < _frontShards.Count; i++)
        {
            var sr = _frontShards[i];
            if (sr == null) continue;
            float s = 0.5f + 0.5f * Mathf.Sin(t * rate + i * 1.3f);
            var c = shardBase; c.a = Mathf.Lerp(0.7f, 1f, s);
            sr.color = c;
        }

        {
            float sPulse = 0.5f + 0.5f * Mathf.Sin(t * rate * 0.8f);
            var c = Color.Lerp(IceTint, IceWarnTint, graceProgress);
            c.a = Mathf.Lerp(0.52f, 0.72f, sPulse);
            for (int i = 0; i < _frostOverlays.Count; i++)
                if (_frostOverlays[i] != null) _frostOverlays[i].color = c;
        }
        if (_backGlow != null)
        {
            float g = 0.5f + 0.5f * Mathf.Sin(t * rate * 0.7f);
            var c = glowBase; c.a = Mathf.Lerp(glowBase.a * 0.7f, glowBase.a * 1.25f, g);
            _backGlow.color = c;
        }
        for (int i = 0; i < _rimeCrust.Count; i++)
        {
            var sr = _rimeCrust[i];
            if (sr == null) continue;
            float s2 = 0.5f + 0.5f * Mathf.Sin(t * rate + i * 2.1f);
            var c = Color.Lerp(new Color(0.86f, 0.95f, 1f), IceWarnTint, graceProgress);
            c.a = Mathf.Lerp(0.30f, 0.60f, s2);
            sr.color = c;
        }

        if (_groundFrost != null)
        {
            var c = Color.Lerp(GroundFrostTint, GlowWarnTint, graceProgress);
            c.a = GroundFrostTint.a;
            _groundFrost.color = c;
        }
    }

    // Break-apart: shards detach, fly outward and fade; the overlay flashes white
    // and dies. The encasement removes itself immediately so nothing keeps
    // following the host once it starts walking.
    public void Shatter()
    {
        Vector3 origin = transform.position;

        ShatterList(_backShards);
        ShatterList(_frontShards);
        ShatterList(_rimeCrust);

        // Overlays pop as a bright freeze-flash rather than just switching off.
        for (int i = 0; i < _frostOverlays.Count; i++)
        {
            var ov = _frostOverlays[i];
            if (ov == null) continue;
            var go = ov.gameObject;
            go.transform.SetParent(null, true);
            ov.color = new Color(0.9f, 0.98f, 1f, 0.9f);
            go.AddComponent<DevourerFadeSprite>().PlayPreserveScale(ov, 0.22f, 1f, 1.10f);
        }
        _frostOverlays.Clear();
        _frostSources.Clear();

        if (_groundFrost != null)
        {
            var go = _groundFrost.gameObject;
            go.transform.SetParent(null, true);
            go.AddComponent<DevourerFadeSprite>()
              .PlayPreserveScale(_groundFrost, 0.5f, 1f, 1.25f);
            _groundFrost = null;
        }
        if (_groundFrostInner != null)
        {
            var go = _groundFrostInner.gameObject;
            go.transform.SetParent(null, true);
            go.AddComponent<DevourerFadeSprite>()
              .PlayPreserveScale(_groundFrostInner, 0.42f, 1f, 1.2f);
            _groundFrostInner = null;
        }

        // Frost vapour puff where the shell was.
        for (int i = 0; i < 10; i++)
        {
            var pGO = new GameObject("IceMote");
            pGO.transform.position = origin;
            var sr = DevourerSprites.NewSprite(pGO.transform, "Sprite", DevourerSprites.SoftDisc,
                                               _sortingLayer, _order,
                                               new Color(0.82f, 0.94f, 1f, 0.9f));
            float ang = Random.Range(0f, Mathf.PI * 2f);
            pGO.AddComponent<DevourerFadeSprite>().Play(
                sr, Random.Range(0.3f, 0.6f), 0.4f, 0.05f,
                drift: new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(1f, 2.8f));
        }

        Destroy(gameObject);
    }

    private void ShatterList(List<SpriteRenderer> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var sr = list[i];
            if (sr == null) continue;

            // Detach BEFORE animating: the host is about to start walking, and a
            // parented shard would ride along with it instead of falling behind.
            var go = sr.gameObject;
            go.transform.SetParent(null, true);

            float ang = Random.Range(0f, Mathf.PI * 2f);
            go.AddComponent<DevourerFadeSprite>().PlayPreserveScale(
                sr, Random.Range(0.35f, 0.65f), 1f, 0.55f,
                drift: new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(1.4f, 3.4f),
                spinDegPerSec: Random.Range(-420f, 420f));
        }
        list.Clear();
    }
}


// One colour variant of the Kaboom explosion: the frames of a single
// Assets/Art/EnemySprites/Boss4/Explosions/Vn folder, in order.
//
// A serializable wrapper rather than a Sprite[][] because Unity does not
// serialize jagged arrays — this is the standard way to get a list-of-lists into
// the inspector, and it gives each variant a foldout you can name.
[System.Serializable]
public class DevourerExplosionVariant
{
    [Tooltip("Select every PNG in one Explosions/Vn folder and drag them here.")]
    public Sprite[] frames;

    public bool HasFrames => frames != null && frames.Length > 0;
}

// Plays a one-shot sprite-sequence explosion at a world position, then removes
// itself. Sized so the ART matches the blast's damage radius rather than whatever
// the source PNG happened to be exported at.
public class DevourerExplosionAnim : MonoBehaviour
{
    private SpriteRenderer _sr;
    private Sprite[] _frames;
    private float _frameTime;

    // How many explosion sprites may be on screen at once.
    //
    // The barrage, the Bomber trait, the Eye bite and the enrage roar can all fire
    // within the same second once the boss is fed. Each one is a large, mostly-opaque
    // 34-frame sprite, so a handful overlapping turns the screen into an unreadable
    // wall — and the telegraphs the player needs to read are UNDER them. Past the cap
    // the art is skipped; the damage, the ring and the shake all still happen, so
    // nothing about the fight changes, it just stays legible.
    private const int MaxConcurrent = 5;
    private static int _active;

    public static DevourerExplosionAnim Play(
        Vector3 at, Sprite[] frames, float frameTime, float worldRadius,
        string sortingLayer, int sortingOrder)
    {
        if (frames == null || frames.Length == 0) return null;
        if (_active >= MaxConcurrent) return null;

        var go = new GameObject("Devourer_Explosion");
        go.transform.position = new Vector3(at.x, at.y, 0f);

        var anim = go.AddComponent<DevourerExplosionAnim>();
        anim.Init(frames, frameTime, worldRadius, sortingLayer, sortingOrder);
        return anim;
    }

    private void Init(Sprite[] frames, float frameTime, float worldRadius,
                      string sortingLayer, int sortingOrder)
    {
        _frames = frames;
        _frameTime = Mathf.Max(0.008f, frameTime);

        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = frames[0];
        _sr.sortingLayerName = sortingLayer;
        _sr.sortingOrder = sortingOrder;

        // Scale the art so its WIDTH covers the blast diameter. The source PNGs are
        // imported at 250 pixels-per-unit and are far wider than they are tall
        // (they are drawn as a ground burst), so fitting to width and letting the
        // height follow keeps the proportions the artist drew.
        float spriteWidth = frames[0].bounds.size.x;
        if (spriteWidth > 0.0001f)
        {
            float k = (worldRadius * 2f) / spriteWidth;
            transform.localScale = new Vector3(k, k, 1f);
        }

        // Random mirroring so repeated blasts in one barrage don't read as the same
        // stamp over and over.
        if (Random.value < 0.5f) _sr.flipX = true;

        _counted = true;
        _active++;
        StartCoroutine(PlayFrames());
    }

    private bool _counted;

    private IEnumerator PlayFrames()
    {
        for (int i = 0; i < _frames.Length; i++)
        {
            if (_sr == null) yield break;
            if (_frames[i] != null) _sr.sprite = _frames[i];
            yield return new WaitForSeconds(_frameTime);
        }
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Decremented here rather than at the end of PlayFrames, so a destroyed or
        // interrupted explosion can never leak a slot and permanently lower the cap.
        if (_counted) { _counted = false; _active = Mathf.Max(0, _active - 1); }
    }
}

// A soft contact shadow under the Devourer. Cheap, but it is the single biggest
// thing separating a sprite that "sits on" the ground from one that "floats above"
// it — and the Devourer is large enough that floating is very obvious.
//
// Also carries the enrage aura, because both are ground-anchored glows that follow
// the boss and want the same Y-sorted ordering.
public class DevourerGroundAura : MonoBehaviour
{
    private SpriteRenderer _shadow;
    private SpriteRenderer _rageGlow;
    private Transform _follow;
    private float _footOffsetY;
    private float _baseWidth = 2.2f;
    private string _layer = "Default";

    private bool _enraged;
    private float _emberTimer;

    private static readonly Color ShadowTint = new Color(0f, 0f, 0.05f, 0.34f);
    private static readonly Color RageTint = new Color(1f, 0.22f, 0.10f, 0.30f);

    public static DevourerGroundAura Attach(GameObject host, float width, float footOffsetY, string layer)
    {
        var go = new GameObject("Devourer_GroundAura");
        go.transform.SetParent(host.transform, false);
        go.transform.localPosition = Vector3.zero;

        var a = go.AddComponent<DevourerGroundAura>();
        a._follow = host.transform;
        a._baseWidth = Mathf.Max(0.5f, width);
        a._footOffsetY = footOffsetY;
        a._layer = layer;

        a._shadow = DevourerSprites.NewSprite(go.transform, "Shadow", DevourerSprites.SoftDisc,
                                              layer, DevourerFXOrder.GroundAt(host.transform.position.y),
                                              ShadowTint);
        a._shadow.transform.localPosition = new Vector3(0f, footOffsetY, 0f);

        a._rageGlow = DevourerSprites.NewSprite(go.transform, "RageGlow", DevourerSprites.SoftDisc,
                                                layer, DevourerFXOrder.GroundAt(host.transform.position.y) + 1,
                                                new Color(RageTint.r, RageTint.g, RageTint.b, 0f));
        a._rageGlow.transform.localPosition = new Vector3(0f, footOffsetY, 0f);
        a._rageGlow.sharedMaterial = DevourerSprites.Additive;

        return a;
    }

    public void SetEnraged(bool on) => _enraged = on;

    private void LateUpdate()
    {
        if (_follow == null) return;

        // Re-sort every frame: the boss walks up and down the screen, and a shadow
        // pinned to its spawn Y would slide over or under the grass as it moved.
        int order = DevourerFXOrder.GroundAt(_follow.position.y);

        // Counter the parent's scale so the Brute trait growing the boss doesn't
        // also balloon the shadow to twice the intended size — it should grow, but
        // with the body, not squared.
        float parentScale = Mathf.Abs(_follow.lossyScale.x);
        float inv = parentScale > 0.001f ? 1f / parentScale : 1f;
        float w = _baseWidth * Mathf.Sqrt(Mathf.Max(0.01f, parentScale)) * inv;

        if (_shadow != null)
        {
            _shadow.sortingOrder = order;
            _shadow.transform.localScale = new Vector3(w, w * 0.42f, 1f);
        }

        if (_rageGlow != null)
        {
            _rageGlow.sortingOrder = order + 1;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
            var c = RageTint;
            c.a = _enraged ? Mathf.Lerp(0.18f, 0.38f, pulse) : 0f;
            _rageGlow.color = c;
            _rageGlow.transform.localScale = new Vector3(w * 1.5f, w * 0.7f, 1f);
        }

        if (!_enraged) return;

        _emberTimer -= Time.deltaTime;
        if (_emberTimer > 0f) return;
        _emberTimer = Random.Range(0.05f, 0.14f);

        EmitEmber(order);
    }

    // Embers rising off an enraged Devourer. Small, warm and short-lived, so they
    // add heat haze without competing with the attack telegraphs for attention.
    private void EmitEmber(int groundOrder)
    {
        var go = new GameObject("RageEmber");
        go.transform.position = _follow.position
            + new Vector3(Random.Range(-_baseWidth, _baseWidth) * 0.45f,
                          _footOffsetY + Random.Range(0f, 0.4f), 0f);

        var sr = DevourerSprites.NewSprite(go.transform, "Sprite", DevourerSprites.SoftDisc,
                                           _layer, DevourerFXOrder.Air - 1,
                                           new Color(1f, Random.Range(0.35f, 0.65f), 0.15f, 0.9f));
        sr.sharedMaterial = DevourerSprites.Additive;

        go.AddComponent<DevourerFadeSprite>().Play(
            sr, Random.Range(0.5f, 1.0f), 0.22f, 0.02f,
            drift: new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.8f, 1.8f)));
    }
}


// A crescent slash left by the bite.
//
// Built as an arc mesh rather than a sprite so its radius and sweep can follow the
// bite's actual hitbox, which grows with the Mortar trait. It reads as the arc the
// jaws travelled through, which is the thing a bite AOE was previously missing: the
// damage landed, the ground marker vanished, and nothing showed the swing itself.
public class DevourerSlash : MonoBehaviour
{
    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private Material _material;
    private float[] _baseAlpha;
    private Color _tint;
    private float _life = 0.26f, _t;

    public static void Play(Vector3 at, Vector2 facing, float radius, Color tint, string sortingLayer)
    {
        var go = new GameObject("Devourer_Slash");
        go.transform.position = new Vector3(at.x, at.y, 0f);
        float deg = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.Euler(0f, 0f, deg);

        go.AddComponent<DevourerSlash>().Build(radius, tint, sortingLayer);
    }

    private void Build(float radius, Color tint, string sortingLayer)
    {
        _tint = tint;

        _filter = gameObject.AddComponent<MeshFilter>();
        _renderer = gameObject.AddComponent<MeshRenderer>();
        _filter.mesh = BuildArc(radius, 100f, 0.62f, 28);

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        _material = new Material(sh) { mainTexture = Texture2D.whiteTexture };
        _renderer.material = _material;
        _renderer.sortingLayerName = sortingLayer;
        _renderer.sortingOrder = DevourerFXOrder.Air + 2;

        var cols = _filter.mesh.colors;
        _baseAlpha = new float[cols.Length];
        for (int i = 0; i < cols.Length; i++) _baseAlpha[i] = cols[i].a;
    }

    // A crescent: an annulus segment that is thickest in the middle of the sweep and
    // tapers to nothing at both ends, like a blade trail.
    private static Mesh BuildArc(float outerRadius, float sweepDeg, float innerFrac, int segments)
    {
        var m = new Mesh { name = "DevourerSlashArc" };

        int rim = segments + 1;
        var verts = new Vector3[rim * 2];
        var cols = new Color[verts.Length];
        var tris = new System.Collections.Generic.List<int>(segments * 6);

        float start = -sweepDeg * 0.5f * Mathf.Deg2Rad;
        float sweep = sweepDeg * Mathf.Deg2Rad;

        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float a = start + sweep * u;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

            // Taper: full thickness at the middle of the sweep, zero at the tips.
            float taper = Mathf.Sin(u * Mathf.PI);
            float inner = Mathf.Lerp(outerRadius, outerRadius * innerFrac, taper);

            verts[i] = dir * inner;
            verts[rim + i] = dir * outerRadius;

            float a2 = taper * taper;
            cols[i] = new Color(1f, 1f, 1f, 0f);          // inner edge fades out
            cols[rim + i] = new Color(1f, 1f, 1f, a2);    // outer edge carries it
        }

        for (int i = 0; i < segments; i++)
        {
            int a0 = i, a1 = i + 1, b0 = rim + i, b1 = rim + i + 1;
            tris.Add(a0); tris.Add(b0); tris.Add(b1);
            tris.Add(a0); tris.Add(b1); tris.Add(a1);
        }

        m.vertices = verts; m.colors = cols; m.triangles = tris.ToArray();
        m.RecalculateBounds();
        return m;
    }

    private void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);

        // Snaps out fast and thins as it goes, like a trail dissipating.
        float scale = Mathf.Lerp(0.82f, 1.12f, 1f - (1f - k) * (1f - k));
        transform.localScale = new Vector3(scale, scale, 1f);

        if (_filter != null && _filter.mesh != null && _baseAlpha != null)
        {
            var mesh = _filter.mesh;
            var cols = mesh.colors;
            float fade = 1f - k;
            for (int i = 0; i < cols.Length && i < _baseAlpha.Length; i++)
            {
                float a = _baseAlpha[i] * fade;
                // Premultiplied, for the same reason the telegraph is: Sprites/Default
                // hardcodes Blend One OneMinusSrcAlpha.
                cols[i] = new Color(_tint.r * a, _tint.g * a, _tint.b * a, a);
            }
            mesh.colors = cols;
        }

        if (k >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}


// A non-damaging shockwave pulse: thin expanding rings and a little dust, no
// fireball, no debris.
//
// Exists so the boss has a way to look powerful WITHOUT looking explosive. The
// distinction matters for readability: in this fight an explosion sprite is a promise
// that something in that circle is about to hurt, and spending that vocabulary on a
// swallow or a roar — neither of which deals damage — trains the player to ignore it.
public static class DevourerRoarPulse
{
    public static void Play(Vector3 at, float radius, Color tint, string sortingLayer)
    {
        var root = new GameObject("Devourer_RoarPulse");
        root.transform.position = new Vector3(at.x, at.y, 0f);

        // Two thin rings, the second chasing the first.
        SpawnRing(root.transform, radius, tint, sortingLayer, 0.030f, 0.45f, 0.55f, 0f);
        SpawnRing(root.transform, radius * 1.25f, tint, sortingLayer, 0.018f, 0.55f, 0.30f, 0.07f);

        // A low skirt of dust so it reads as air being pushed, not just a drawn circle.
        int puffs = Mathf.Clamp(Mathf.RoundToInt(radius * 2f), 3, 8);
        for (int i = 0; i < puffs; i++)
        {
            float ang = (i / (float)puffs) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            Vector3 dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.55f, 0f);

            var go = new GameObject("RoarDust");
            go.transform.position = root.transform.position + dir * radius * 0.35f;

            var sr = DevourerSprites.NewSprite(go.transform, "Sprite", Boss2VFXSprites.GetSoftDisc(),
                                               sortingLayer,
                                               DevourerFXOrder.GroundAt(go.transform.position.y),
                                               new Color(0.55f, 0.48f, 0.42f, 0.35f));
            go.AddComponent<DevourerFadeSprite>().Play(
                sr, Random.Range(0.35f, 0.6f), radius * 0.18f, radius * 0.45f,
                drift: new Vector2(dir.x, dir.y) * radius * Random.Range(0.5f, 1.1f));
        }

        Object.Destroy(root, 1f);
    }

    private static void SpawnRing(Transform parent, float radius, Color tint, string layer,
                                  float thickness, float life, float alpha, float delay)
    {
        var go = new GameObject("RoarRing");
        go.transform.SetParent(parent, false);

        var sr = DevourerSprites.NewSprite(go.transform, "Sprite",
                                           Boss2WarningSprites.GetRing(thickness),
                                           layer, DevourerFXOrder.Air,
                                           new Color(tint.r, tint.g, tint.b, alpha));

        var runner = go.AddComponent<DevourerRoarRing>();
        runner.Play(sr, radius, life, alpha, delay);
    }
}

public class DevourerRoarRing : MonoBehaviour
{
    private SpriteRenderer _sr;
    private float _radius, _life, _alpha, _delay, _t;

    public void Play(SpriteRenderer sr, float radius, float life, float alpha, float delay)
    {
        _sr = sr; _radius = radius; _life = Mathf.Max(0.05f, life);
        _alpha = alpha; _delay = delay;
        if (_sr != null) _sr.transform.localScale = Vector3.zero;
    }

    private void Update()
    {
        if (_sr == null) { Destroy(gameObject); return; }

        _t += Time.deltaTime;
        if (_t < _delay) return;

        float k = Mathf.Clamp01((_t - _delay) / _life);
        float eased = 1f - (1f - k) * (1f - k);

        // The ring sprite is 1 world unit across at scale 1, so scale == diameter.
        _sr.transform.localScale = Vector3.one * (_radius * 2f * eased);

        var c = _sr.color;
        c.a = _alpha * (1f - k);
        _sr.color = c;

        if (k >= 1f) Destroy(gameObject);
    }
}


// A damaging AOE drawn as force radiating OUT of a body: a hard leading ring that
// snaps to the exact damage radius, a brief core flash, and debris thrown outward.
//
// Distinct from DevourerRoarPulse (which is soft, slow and harmless) and from the
// explosion PNGs (which mark ordnance landing somewhere at a distance). This is the
// third case: real damage, centred on the boss, expanding outward.
//
// The leading ring reaching EXACTLY the damage radius at the moment of impact is the
// point of it — the player learns the safe distance by watching where the ring stops,
// so it has to be honest.
public static class DevourerShockwave
{
    public static void Play(Vector3 at, float radius, Color tint, string sortingLayer)
    {
        var root = new GameObject("Devourer_Shockwave");
        root.transform.position = new Vector3(at.x, at.y, 0f);
        root.AddComponent<DevourerShockwaveRunner>().Run(radius, tint, sortingLayer);
    }
}

public class DevourerShockwaveRunner : MonoBehaviour
{
    private float _r;
    private Color _tint;
    private string _layer;

    public void Run(float radius, Color tint, string layer)
    {
        _r = Mathf.Max(0.2f, radius);
        _tint = tint;
        _layer = layer;

        LeadingRing();
        TrailingRing();
        CoreFlash();
        GroundDust();
        Debris();

        Destroy(gameObject, 1.6f);
    }

    private SpriteRenderer Make(string name, Sprite sprite, int order, Color c, bool additive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = c;
        sr.sortingLayerName = _layer;
        sr.sortingOrder = order;
        if (additive) sr.sharedMaterial = DevourerSprites.Additive;
        return sr;
    }

    // Snaps out fast and stops dead ON the damage radius.
    private void LeadingRing()
    {
        var sr = Make("Lead", Boss2WarningSprites.GetRing(0.035f), DevourerFXOrder.Air + 2,
                      new Color(_tint.r, _tint.g, _tint.b, 0.95f), true);
        StartCoroutine(Expand(sr, 0.20f, 0.1f, _r * 2f, 0.95f, 0f, hardStop: true));
    }

    // A softer second ring drifting slightly past it, so the edge has some thickness
    // without the leading ring lying about the radius.
    private void TrailingRing()
    {
        var sr = Make("Trail", Boss2WarningSprites.GetRing(0.02f), DevourerFXOrder.Air + 1,
                      new Color(_tint.r, _tint.g, _tint.b, 0.45f), true);
        StartCoroutine(Expand(sr, 0.36f, 0.1f, _r * 2.25f, 0.45f, 0.05f, hardStop: false));
    }

    private void CoreFlash()
    {
        var sr = Make("Core", Boss2VFXSprites.GetSoftDisc(), DevourerFXOrder.Air,
                      new Color(_tint.r, _tint.g, _tint.b, 0.55f), true);
        StartCoroutine(Expand(sr, 0.18f, _r * 1.1f, _r * 0.2f, 0.55f, 0f, hardStop: false));
    }

    // Dust pushed outward along the ground, Y-sorted so the grass interleaves.
    private void GroundDust()
    {
        int puffs = Mathf.Clamp(Mathf.RoundToInt(_r * 3f), 4, 12);
        for (int i = 0; i < puffs; i++)
        {
            float ang = (i / (float)puffs) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
            Vector3 dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.55f, 0f);
            Vector3 spot = transform.position + dir * _r * 0.4f;

            var go = new GameObject("ShockDust");
            go.transform.position = spot;
            var sr = DevourerSprites.NewSprite(go.transform, "Sprite", Boss2VFXSprites.GetSoftDisc(),
                                               _layer, DevourerFXOrder.GroundAt(spot.y),
                                               new Color(0.55f, 0.48f, 0.44f, 0.40f));
            go.AddComponent<DevourerFadeSprite>().Play(
                sr, Random.Range(0.3f, 0.55f), _r * 0.16f, _r * 0.42f,
                drift: new Vector2(dir.x, dir.y) * _r * Random.Range(1.2f, 2.2f));
        }
    }

    private void Debris()
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(_r * 2f), 3, 8);
        for (int i = 0; i < count; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.7f + 0.35f, 0f);

            var go = new GameObject("ShockDebris");
            go.transform.position = transform.position;
            var sr = DevourerSprites.NewSprite(go.transform, "Sprite", Boss2VFXSprites.GetRockChunk(),
                                               _layer, DevourerFXOrder.Air,
                                               new Color(0.45f, 0.40f, 0.46f, 0.9f));
            go.AddComponent<DevourerFadeSprite>().Play(
                sr, Random.Range(0.4f, 0.7f), _r * 0.10f, _r * 0.06f,
                drift: new Vector2(dir.x, dir.y) * _r * Random.Range(1.6f, 3f),
                spinDegPerSec: Random.Range(-420f, 420f));
        }
    }

    private IEnumerator Expand(SpriteRenderer sr, float life, float from, float to,
                               float alpha, float delay, bool hardStop)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        float t = 0f;
        while (t < life)
        {
            if (sr == null) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);

            // Fast out, decelerating — a pressure front losing energy.
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            sr.transform.localScale = Vector3.one * Mathf.Lerp(from, to, eased);

            var c = sr.color;
            // The leading ring holds full brightness until near the end so the edge
            // stays legible right up to where the damage actually stops.
            c.a = alpha * (hardStop ? Mathf.Clamp01(1f - k * k * k) : 1f - k);
            sr.color = c;

            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }
}


// A spike pushing out of the boss's hide, pointing along the line it will fire.
//
// Parented to the boss so it tracks the body if it keeps moving, and grown from zero
// so the wind-up is legible on the silhouette itself. Ground telegraphs get lost under
// the player's own character; a spike sprouting out of the thing about to shoot you
// does not.
public class DevourerSpikeTelegraph : MonoBehaviour
{
    private SpriteRenderer _sr;
    private SpriteRenderer _glint;
    private float _life, _t, _length;
    private Color _tint;
    private bool _released;

    public static DevourerSpikeTelegraph Spawn(Transform host, Vector2 dir, float bodyRadius,
                                               Color tint, string sortingLayer, float windUp,
                                               int bodySortingOrder)
    {
        var go = new GameObject("Devourer_Spike");
        go.transform.SetParent(host, false);

        // Rotate so the spike's +Y (its tip) points along the firing line. The sprite
        // is authored with its pivot at the ROOT and its point upward.
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

        // Counter the boss's own scale so spike length is a world measurement and does
        // not balloon when Size Multiplier changes.
        float parent = Mathf.Abs(host.lossyScale.x);
        if (parent > 0.001f) go.transform.localScale = Vector3.one / parent;

        var t = go.AddComponent<DevourerSpikeTelegraph>();
        t.Build(bodyRadius, tint, sortingLayer, windUp, bodySortingOrder);
        return t;
    }

    private void Build(float bodyRadius, Color tint, string sortingLayer, float windUp,
                       int bodySortingOrder)
    {
        _tint = tint;
        _life = Mathf.Max(0.05f, windUp);
        _length = Mathf.Max(0.35f, bodyRadius * 1.6f);

        var srGO = new GameObject("Sprite");
        srGO.transform.SetParent(transform, false);

        // Start the root INSIDE the body and grow outward, so the spike slides out
        // from under the hide instead of appearing on top of it.
        srGO.transform.localPosition = new Vector3(0f, -bodyRadius * 0.55f, 0f);

        _sr = srGO.AddComponent<SpriteRenderer>();
        _sr.sprite = DevourerSprites.Spike;
        _sr.color = Color.white;
        _sr.sortingLayerName = sortingLayer;

        // BEHIND the boss sprite. This is what makes it read as emerging from the
        // creature: the body occludes the buried part, so only what has actually
        // pushed through is visible, and there is no seam where the two sprites meet.
        _sr.sortingOrder = bodySortingOrder - 1;

        srGO.transform.localScale = new Vector3(1f, 0f, 1f);

        // A small hot glint at the exit wound, in front of the body — the only part of
        // the effect that sits above the boss, and it is what draws the eye to which
        // direction the pellet is leaving on.
        var glintGO = new GameObject("Glint");
        glintGO.transform.SetParent(transform, false);
        glintGO.transform.localPosition = new Vector3(0f, bodyRadius * 0.45f, 0f);

        _glint = glintGO.AddComponent<SpriteRenderer>();
        _glint.sprite = Boss2VFXSprites.GetSoftDisc();
        _glint.sharedMaterial = DevourerSprites.Additive;
        _glint.sortingLayerName = sortingLayer;
        _glint.sortingOrder = bodySortingOrder + 1;
        _glint.color = new Color(tint.r, tint.g, tint.b, 0f);
        glintGO.transform.localScale = Vector3.one * bodyRadius * 0.5f;
    }

    private void Update()
    {
        if (_released || _sr == null) return;

        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);

        // Pushes out fast then creeps the last of the way, so "fully out" is a
        // distinct moment rather than the end of a constant slide.
        float eased = 1f - (1f - k) * (1f - k);
        _sr.transform.localScale = new Vector3(1f, _length * eased, 1f);

        // Darker while buried, brightening as more of the lit tip clears the body.
        _sr.color = Color.Lerp(new Color(0.55f, 0.5f, 0.6f), Color.white, eased);

        if (_glint != null)
        {
            var c = _tint;
            c.a = Mathf.Lerp(0f, 0.7f, k * k);
            _glint.color = c;
            _glint.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.5f, k) * _length;
        }
    }

    // Fired: the stub snaps back into the body.
    public void Release()
    {
        if (_released) return;
        _released = true;
        StartCoroutine(Retract(0.10f));
    }

    // Aborted: same retraction, no shot.
    public void Cancel() => Release();

    private IEnumerator Retract(float dur)
    {
        if (_sr == null) { Destroy(gameObject); yield break; }

        Vector3 from = _sr.transform.localScale;
        float t = 0f;
        while (t < dur)
        {
            if (_sr == null) { Destroy(gameObject); yield break; }
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            _sr.transform.localScale = Vector3.Lerp(from, new Vector3(from.x, 0f, 1f), k);

            if (_glint != null)
            {
                var c = _glint.color;
                c.a = Mathf.Lerp(0.7f, 0f, k);
                _glint.color = c;
            }
            yield return null;
        }
        Destroy(gameObject);
    }
}


// Self-contained straight-flying spike, fired by the Devourer's spike volley.
//
// Deliberately non-homing: it commits to its launch heading, which is the entire
// reason the volley is dodgeable.
//
// SHIELD INTERACTION mirrors EnemyProjectile (same ShieldSystem / ProjectileParry
// calls, same outcomes), so a Devourer spike behaves like every other enemy shot:
//   * Shield held, spike inbound  -> BLOCKED: reduced damage, spike destroyed.
//   * Fresh press + Augment 325   -> PARRIED: the spike turns gold, flies back into
//                                    the boss for reflected damage, and the boss is
//                                    parry-stunned with the parrying player's upgrades.
//   * Fresh press, no augment     -> treated as a block, exactly like EnemyProjectile.
// It can't simply BE an EnemyProjectile: that class re-aims every shot at its target
// on launch (collapsing a spread into one line) and only ever damages that target.
public class DevourerSpike : MonoBehaviour
{
    private Vector2 _dir;
    private float _speed, _damage, _life, _t;
    private GameObject _attacker;
    private bool _hitsBuildings;
    private SpriteRenderer _sr;

    private const float HitRadius = 0.35f;

    // Shield / parry (set by ConfigureParry; off until then).
    private bool _parryable;
    private float _parryReactRadius = 2f;
    private float _catchHalfWidth = 0.9f;
    private float _reflectMultiplier = 2f;
    private float _returnSpeedMultiplier = 1.5f;

    private bool _parried;
    private Collider2D _returnCollider;
    private ProjectileParryIndicator _parryPrompt;

    public static DevourerSpike Launch(Vector3 origin, Vector2 dir, float speed, float damage,
                                       float lifetime, GameObject attacker, bool hitsBuildings,
                                       Color tint, string sortingLayer)
    {
        var go = new GameObject("Devourer_SpikeShot");
        go.transform.position = new Vector3(origin.x, origin.y, 0f);

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        var s = go.AddComponent<DevourerSpike>();
        s.Init(dir, speed, damage, lifetime, attacker, hitsBuildings, tint, sortingLayer);
        return s;
    }

    public void ConfigureParry(bool parryable, float reactRadius, float catchHalfWidth,
                               float reflectMultiplier, float returnSpeedMultiplier)
    {
        _parryable = parryable;
        _parryReactRadius = Mathf.Max(0.1f, reactRadius);
        _catchHalfWidth = Mathf.Max(0.05f, catchHalfWidth);
        _reflectMultiplier = Mathf.Max(0f, reflectMultiplier);
        _returnSpeedMultiplier = Mathf.Max(0.1f, returnSpeedMultiplier);
    }

    private void Init(Vector2 dir, float speed, float damage, float lifetime,
                      GameObject attacker, bool hitsBuildings, Color tint, string sortingLayer)
    {
        _dir = dir.normalized;
        _speed = speed;
        _damage = damage;
        _life = Mathf.Max(0.2f, lifetime);
        _attacker = attacker;
        _hitsBuildings = hitsBuildings;

        var srGO = new GameObject("Sprite");
        srGO.transform.SetParent(transform, false);
        _sr = srGO.AddComponent<SpriteRenderer>();
        _sr.sprite = DevourerSprites.Spike;
        _sr.color = Color.white;          // the sprite carries its own shading now
        _sr.sortingLayerName = sortingLayer;
        _sr.sortingOrder = DevourerFXOrder.Air + 1;
        srGO.transform.localScale = new Vector3(0.7f, 0.9f, 1f);

        // Short motion trail so a fast pellet still reads as a spike rather than a
        // flicker between frames.
        ProjectileDart.Attach(transform, tint, 0.09f, 0.10f);
    }

    private void Update()
    {
        _t += Time.deltaTime;
        if (_t >= _life) { Destroy(gameObject); return; }

        // Bounced: fly home into the boss and hurt nothing else on the way.
        if (_parried)
        {
            TickReturnFlight();
            return;
        }

        // Shield check BEFORE moving/damaging, like EnemyProjectile. True means the
        // spike was blocked (destroyed) or parried (now returning) this frame.
        if (_parryable && TryShieldInteraction()) return;

        transform.position += (Vector3)(_dir * _speed * Time.deltaTime);

        // ApplyRadial already skips enemies and the attacker, so a spike cannot clip
        // the boss's own minions on the way out.
        int hits = DevourerDamage.ApplyRadial(transform.position, HitRadius, _damage,
                                              _attacker, _hitsBuildings);
        if (hits > 0)
        {
            Impact();
            Destroy(gameObject);
        }
    }

    //  SHIELD

    private bool TryShieldInteraction()
    {
        if (!ProjectileParry.TryResolve(transform.position, out var shield, out var playerT,
                                        out int parryingIndex) || playerT == null)
        {
            HideParryPrompt();
            return false;
        }

        Vector2 toPlayer = (Vector2)playerT.position - (Vector2)transform.position;

        // Out of reaction range.
        if (toPlayer.sqrMagnitude > _parryReactRadius * _parryReactRadius)
        {
            HideParryPrompt();
            return false;
        }

        // Only a spike actually coming AT this player can be caught. One that has
        // already passed them, or an outer pellet of the spread sailing wide, would
        // miss anyway — and a block still costs stamina and deals reduced damage, so
        // catching it would punish the player for defending.
        float along = Vector2.Dot(toPlayer, _dir);
        float lateral = Mathf.Abs(_dir.x * toPlayer.y - _dir.y * toPlayer.x);
        if (along <= 0f || lateral > _catchHalfWidth)
        {
            HideParryPrompt();
            return false;
        }

        // Per-player unlock: only the intercepting player's Augment 325 counts.
        bool parryUnlocked = ProjectileParry.UnlockedFor(parryingIndex);

        // The "!" advertises the bounce-back, so it only shows once it's unlocked.
        if (parryUnlocked) ShowParryPrompt();
        else HideParryPrompt();

        var result = shield.TryInterceptProjectile(transform.position);
        if (result == ShieldSystem.ProjectileInterception.None) return false;

        if (result == ShieldSystem.ProjectileInterception.Parried && parryUnlocked)
        {
            shield.PlayProjectileParryFeedback(transform.position);

            // Boss already gone: the parry just neutralises the spike.
            if (_attacker == null)
            {
                HideParryPrompt();
                Destroy(gameObject);
                return true;
            }

            // Stun + damage debuff on the boss, using the PARRYING player's upgrades
            // (330 Longer Parry Stun / 331 Powerful Parry) — same call as a melee parry.
            ParryStunEffect.ApplyOrRefresh(_attacker, parryingIndex);
            BecomeParried();
            return true;
        }

        // Blocked, or a parry-timed press without the augment → reduced damage.
        shield.PlayProjectileBlockFeedback(transform.position);
        DamagePlayer(playerT, _damage * shield.BlockDamageMultiplier);
        HideParryPrompt();
        Impact();
        Destroy(gameObject);
        return true;
    }

    private void DamagePlayer(Transform playerT, float amount)
    {
        if (playerT == null || amount <= 0f) return;

        var ps = playerT.GetComponent<PlayerStats>();
        if (ps == null) ps = playerT.GetComponentInParent<PlayerStats>();
        if (ps == null) ps = playerT.GetComponentInChildren<PlayerStats>();
        GameObject go = ps != null ? ps.gameObject : playerT.gameObject;

        // Same channel an unblocked spike uses, so armour / damage systems and the
        // player's on-hit augments treat a blocked spike consistently.
        if (go.CompareTag("Player"))
        {
            DevourerDamage.ApplySingle(go, amount, _attacker, hitBuildings: false);
            return;
        }

        var cs = go.GetComponent<CharacterStats>();
        if (cs != null) cs.TakeDamage(amount);
    }

    private void BecomeParried()
    {
        HideParryPrompt();

        _parried = true;
        _t = 0f;                                   // fresh lifetime for the trip home
        _speed *= _returnSpeedMultiplier;
        _returnCollider = _attacker != null ? _attacker.GetComponent<Collider2D>() : null;

        // Gold = "this one is yours now", so a returning spike can't be misread as
        // another incoming one.
        if (_sr != null) _sr.color = new Color(1f, 0.88f, 0.4f);
    }

    private void TickReturnFlight()
    {
        if (_attacker == null) { Destroy(gameObject); return; }

        var bossStats = _attacker.GetComponent<CharacterStats>();
        if (bossStats == null || bossStats.IsDead()) { Destroy(gameObject); return; }

        // Home on the body's collider centre (the boss's collider is offset upward from
        // its pivot), and connect on reaching its edge rather than its exact centre.
        Vector2 aim = _attacker.transform.position;
        float catchRadius = HitRadius;
        if (_returnCollider != null)
        {
            Bounds b = _returnCollider.bounds;
            aim = b.center;
            catchRadius = Mathf.Max(HitRadius, Mathf.Min(b.extents.x, b.extents.y) * 0.8f);
        }

        Vector2 to = aim - (Vector2)transform.position;
        float dist = to.magnitude;

        if (dist <= catchRadius)
        {
            float dmg = _damage * _reflectMultiplier;

            // Respect the parry-stun damage bonus, same as EnemyProjectile.DamageFirer.
            var stun = _attacker.GetComponent<ParryStunEffect>();
            if (stun != null) dmg *= stun.DamageMultiplier;

            bossStats.TakeDamage(dmg);
            CombatJuice.OnPlayerHitEnemy(_attacker, isMelee: false);

            Impact();
            Destroy(gameObject);
            return;
        }

        _dir = to / dist;
        transform.position += (Vector3)(_dir * Mathf.Min(_speed * Time.deltaTime, dist));

        float angle = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void ShowParryPrompt()
    {
        if (_parryPrompt == null)
            _parryPrompt = ProjectileParryIndicator.Attach(transform, yOffset: 0.5f, size: 0.4f);
    }

    private void HideParryPrompt()
    {
        if (_parryPrompt != null)
        {
            Destroy(_parryPrompt.gameObject);
            _parryPrompt = null;
        }
    }

    private void Impact()
    {
        var go = new GameObject("SpikeImpact");
        go.transform.position = transform.position;

        var sr = DevourerSprites.NewSprite(go.transform, "Sprite", Boss2VFXSprites.GetSoftDisc(),
                                           _sr != null ? _sr.sortingLayerName : "Default",
                                           DevourerFXOrder.Air + 2,
                                           _sr != null ? _sr.color : Color.white);
        sr.sharedMaterial = DevourerSprites.Additive;
        go.AddComponent<DevourerFadeSprite>().Play(sr, 0.18f, 0.5f, 0.05f);
    }
}





