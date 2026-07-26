using UnityEngine;

// RedEye — the ELITE variant of the Eye.
//   PULSATING RED HEAD — an additive overlay SpriteRenderer that mirrors the
//      body sprite every LateUpdate and breathes red. It never writes to the body
//      renderer, so EnemyStats' damage flash (which snapshots and restores
//      spriteRenderer.color) keeps working exactly as before.
//   PERIODIC RED LASER — reuses the project's existing LaserBeam VFX component
//      (LaserBeam.cs, previously unreferenced) exactly the way the Laser Tower's
//      beam works: charge-up, ignition, hold, power-down. Damage is applied in
//      ticks through the same paths the Eye's AOE already uses
//      (EnemyDamageSystem for the player, EnergyManager for towers/core).


[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyController))]
[DisallowMultipleComponent]
// Runs LATE on purpose. EyeChains re-pins the eye body's sorting every LateUpdate
// (bodySR.sortingOrder = eyeBodyOrder) and YSortEntity writes a biome Y-sorted
// order to the enemy's renderers. Sorting the glow from a late LateUpdate means we
// always get the last word instead of racing them.
[DefaultExecutionOrder(500)]
public class RedEye : MonoBehaviour
{
    // ELITE STATS
    [Header("Elite Stats")]
    [Tooltip("Multiplies maxHealth after EnemyStats has read it from EnemyData. " +
             "Safe: maxHealth is a plain field on the component, not on the asset. " +
             "Set to 1 if the RedEye has its own EnemyData asset with elite HP.")]
    [SerializeField] private float eliteHealthMultiplier = 2.5f;

    [Tooltip("Multiplies melee/AOE damage. NOTE: this writes to the per-enemy CLONE of " +
             "EnemyData that EnemyStats.Awake creates, the same way the Berserk's growth " +
             "buff does. Left at 1 by default — the cleanest way to give the RedEye its " +
             "own damage is to duplicate the Eye's EnemyData asset (Enemies/EnemyData) " +
             "and assign it on the prefab. The laser damage below is independent of this.")]
    [SerializeField] private float eliteDamageMultiplier = 1f;

    // RED PULSE
    [Header("Pulsating Red Head")]
    [SerializeField] private bool pulseHead = true;

    [Tooltip("Glow colour. Drawn ADDITIVELY over the eye sprite, so it brightens the " +
             "head towards red rather than repainting it flat.")]
    [SerializeField] private Color pulseColor = new Color(1f, 0.09f, 0.06f, 1f);

    [Tooltip("Pulses per second.")]
    [SerializeField] private float pulseHz = 1.4f;

    [Range(0f, 1f)][SerializeField] private float pulseMinAlpha = 0.25f;
    [Range(0f, 1f)][SerializeField] private float pulseMaxAlpha = 0.95f;

    [Tooltip("Extra glow multiplier while the laser is charging / firing, so the head " +
             "visibly 'powers up' before the beam appears.")]
    [SerializeField] private float pulseFiringBoost = 1.7f;

    [Header("Glow Sorting (above grass, below fog)")]
    [Tooltip("Sorting orders ABOVE the eye body (EyeChains.eyeBodyOrder, normally 3000). " +
             "Keep small: the front chains sit at body + chainDepthRange (~+24), so 1 puts " +
             "the glow over the eye and the back chains but still UNDER the front chains.\n\n" +
             "This is deliberately taken from EyeChains rather than read off the body " +
             "renderer: EyeChains re-pins the body every LateUpdate and YSortEntity writes a " +
             "biome Y-sorted order (400-1600) to it, so copying the body's live order left the " +
             "glow inside the generative grass band, i.e. invisible.")]
    [SerializeField] private int glowSortingOffset = 1;

    [Tooltip("Used only when there is NO EyeChains to take the eye body order from. " +
             "Mirrors the biome grass formula (order = base + -y * precision) so the glow " +
             "tracks the local grass height instead of sitting at a fixed order that gets " +
             "buried further down the map.")]
    [SerializeField] private bool glowYSortToBiome = true;

    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase (BiomeManager sets 1000).")]
    [SerializeField] private int biomeSortBase = 1000;

    [Tooltip("Must match GrassCartoonOverlay.sortPrecision (BiomeManager sets 10).")]
    [SerializeField] private float biomeSortPrecision = 10f;

    [Tooltip("How far above the LOCAL grass the glow sits when Y-sorting to the biome. " +
             "The grass band peaks around 1600, so a 1600 bias lands near the eye body's " +
             "usual 3000.")]
    [SerializeField] private int glowAboveGrassBias = 1600;

    [Tooltip("Hard ceiling. The fog overlay is at 5000 and the night overlay at 6000 " +
             "(BiomeManager), so the glow must stay below 5000 or it punches through the fog.")]
    [SerializeField] private int glowMaxSortingOrder = 4900;

    [SerializeField] private string glowFallbackSortingLayer = "Default";

    // LASER
    [Header("Laser")]
    [SerializeField] private bool enableLaser = true;

    [Tooltip("World units. The eye keeps its normal short detect/attack range for the " +
             "chains; the laser reaches much further.")]
    [SerializeField] private float laserRange = 8f;

    [Tooltip("Seconds between laser attacks (measured from the end of the last one).")]
    [SerializeField] private float laserInterval = 6f;

    [Tooltip("Grace period after spawning before the first laser can fire.")]
    [SerializeField] private float firstLaserDelay = 3f;

    [Tooltip("How long the beam is HELD after it ignites. Total on-screen time is " +
             "this + Beam Charge Time.")]
    [SerializeField] private float laserBeamDuration = 1.1f;

    [Tooltip("Damage per second while the beam is actually firing (the charge-up " +
             "deals nothing, so the player can break line of sight / kill the eye).")]
    [SerializeField] private float laserDamagePerSecond = 14f;

    [Tooltip("Damage is applied in discrete ticks rather than every frame, so shields, " +
             "the damage flash and the screen vignette read properly instead of being " +
             "retriggered 60x a second.")]
    [SerializeField] private float laserTickInterval = 0.25f;

    [Tooltip("If true the laser can also burn towers and the core. If false it only " +
             "ever targets the player.")]
    [SerializeField] private bool laserHitsBuildings = true;

    [Tooltip("Prefer the player over buildings when both are in range.")]
    [SerializeField] private bool laserTargetsPlayerFirst = true;

    [Tooltip("Muzzle = the PUPIL of the eye, given in the eye's LOCAL space (sprite units, " +
             "relative to the transform pivot, BEFORE the 0.25 scale) — NOT world units. " +
             "The default sits on the pupil; nudge it if your art differs. x is the eye's " +
             "own left/right and MIRRORS with the sprite flip, so the beam stays on the " +
             "pupil at BOTH facings; y is up/down. This point is a FIXED spot on the art, " +
             "carried through the eye's lean and scale, so it stays glued as the eye tilts " +
             "and flips.\n\n" +
             "Deliberately a fixed pupil offset and NOT sprite.bounds.center: the bounds " +
             "centre is the visible-pixel box centre, which sits near the pivot (well below " +
             "the pupil) AND shifts every animation frame — that per-frame wobble is what " +
             "made the old muzzle float.")]
    [SerializeField] private Vector2 muzzleOffset = new Vector2(-0.30f, 1.09f);

    [Tooltip("If the EnemyData has a laserAttack frame range authored (frameCount > 0), " +
             "play it while the beam is up. Leave OFF unless you actually have laser " +
             "sprites — the animation controller locks orientation while it plays.")]
    [SerializeField] private bool playLaserAnimation = false;

    [Tooltip("Play FMODEvents.redEyeLaser (the 'Laser' event) while the beam is on screen. " +
             "The sound now starts the instant the beam begins CHARGING — coincident with " +
             "the visual — and swells to full as it ignites, instead of only kicking in at " +
             "ignition (which left the beam visible for ~Beam Charge Time before any sound). " +
             "Needs a loop region in FMOD Studio; stopped with ALLOWFADEOUT on power-down.")]
    [SerializeField] private bool playLaserSound = true;

    [Tooltip("Seconds for the beam sound to swell from its startup volume/pitch to full. " +
             "Set roughly equal to Beam Charge Time so it winds up across the charge and " +
             "hits full power exactly as the beam ignites.")]
    [SerializeField] private float laserSoundRiseTime = 0.45f;

    [Tooltip("Pitch the beam sound starts at as the eye begins charging, sweeping up to 1 " +
             "at ignition. <1 gives a rising 'power-up' whine; set to 1 to disable the sweep.")]
    [Range(0.2f, 1.5f)][SerializeField] private float laserSoundStartPitch = 0.7f;

    [Tooltip("Volume the beam sound starts at on charge (0..1), swelling to full at ignition. " +
             "A small non-zero value makes the wind-up immediately audible.")]
    [Range(0f, 1f)][SerializeField] private float laserSoundStartVolume = 0.2f;

    [Header("Laser Look")]
    [ColorUsage(true, true)][SerializeField] private Color beamColor = new Color(1f, 0.12f, 0.09f, 1f);
    [Range(0.25f, 4f)][SerializeField] private float beamIntensity = 1.8f;
    [Tooltip("Wind-up before the beam ignites. This is the player's warning window.")]
    [SerializeField] private float beamChargeTime = 0.45f;
    [SerializeField] private float beamCoreWidth = 0.05f;
    [SerializeField] private float beamGlowWidth = 0.17f;
    [SerializeField] private float beamAuraWidth = 0.42f;
    [Tooltip("Must beat the grass Y-sort (~1600) and the fog (5000). 32000 is safe.")]
    [SerializeField] private int beamSortingOrder = 32000;
    [SerializeField] private string beamSortingLayer = "VFX";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    // Cached refs
    private EnemyStats stats;
    private EnemyAnimationController animController;
    private SpriteRenderer bodySR;
    private EyeChains chains;   // read-only: the eye body's authoritative sort order

    // Red glow overlay
    private SpriteRenderer glowSR;
    private float pulsePhase;
    private bool loggedSorting;

    // Laser state. Driven from Update (NOT a coroutine) on purpose: disabling a
    // MonoBehaviour does not stop its coroutines, so a coroutine-driven beam would
    // keep firing out of a corpse after EnemyDeathVFX disables everything.
    private GameObject beamHost;
    private LaserBeam beam;
    private Transform laserTarget;
    private float cooldownTimer;
    private float fireTimer = -1f;
    private float tickTimer;

    // Held instance for the beam sound, kept on the muzzle while it plays. Started
    // when the beam begins charging and swelled across the charge (see UpdateLaserSfx).
    private readonly SpatialLoopSfx laserSfx = new SpatialLoopSfx("RedEye laser");
    private float laserSfxLevel; // 0..1 smoothed swell, drives volume + pitch

    private bool Firing => fireTimer > 0f;

    // World-space point the beam is emitted from — the pupil of the eye. It is a FIXED
    // spot on the sprite (muzzleOffset, in local sprite units from the pivot), pushed out
    // to world space through the eye's own transform. Because the point is rigid on the
    // art and the transform carries the lean and scale, the muzzle stays welded to the
    // pupil as the eye tilts and turns instead of floating.
    //
    // Two things move the eye but are handled explicitly:
    //   * FLIP — SpriteRenderer.flipX mirrors the sprite about the pivot to face the
    //            player. It is NOT a transform op, so we mirror the local x by hand
    //            (flipXsign). The pupil sits LEFT of the pivot (x < 0), so this is what
    //            keeps the beam on the pupil at BOTH facings.
    //   * LEAN + SCALE — TransformPoint runs the (already flip-mirrored) local point
    //            through transform.rotation (the ±maxRotationAngle lean) and the 0.25
    //            scale, so it orbits the pivot exactly as the rendered sprite does.
    //
    // NOTE: this is intentionally a fixed pupil offset, not sprite.bounds.center. The
    // bounds centre is the visible-pixel box centre — it sits near the pivot (below the
    // pupil) and, worse, jitters every animation frame, which is what made the muzzle
    // float before.
    private Vector3 MuzzleWorld
    {
        get
        {
            // Defensive: if the body renderer never resolved, fall back to a plain
            // pivot-plus-offset so the beam still has a sane origin.
            if (bodySR == null)
                return transform.position + (Vector3)muzzleOffset;

            float flipXsign = bodySR.flipX ? -1f : 1f;
            Vector3 local = new Vector3(muzzleOffset.x * flipXsign, muzzleOffset.y, 0f);
            return transform.TransformPoint(local);
        }
    }

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        animController = GetComponent<EnemyAnimationController>();
        chains = GetComponent<EyeChains>();

        bodySR = GetComponent<SpriteRenderer>();
        if (bodySR == null) bodySR = GetComponentInChildren<SpriteRenderer>();

        // Desync a group of RedEyes so they don't breathe in lock-step.
        pulsePhase = Random.value * Mathf.PI * 2f;
    }

    private void Start()
    {
        ApplyEliteStats();
        BuildGlow();
        cooldownTimer = Mathf.Max(0f, firstLaserDelay);
    }

    // ELITE STATS
    // Runs in Start, i.e. after EnemyStats.Awake has read EnemyData and applied the
    // augment / stage / difficulty multipliers, so this composes on top of them.
    // The health bar is handled both ways round, because the order of two Start()
    // methods on the same GameObject is not guaranteed:
    //   - our Start first  -> EnemyStats.Start creates the bar with the new max
    //   - EnemyStats first -> the bar already exists and we resize it here
    private void ApplyEliteStats()
    {
        if (stats == null) return;

        if (eliteHealthMultiplier > 0f && !Mathf.Approximately(eliteHealthMultiplier, 1f))
        {
            stats.maxHealth *= eliteHealthMultiplier;
            stats.currentHealth = stats.maxHealth;

            var bar = stats.GetHealthBar();
            if (bar != null) bar.SetMaxHealth(stats.maxHealth, stats.currentHealth);
        }

        if (eliteDamageMultiplier > 0f && !Mathf.Approximately(eliteDamageMultiplier, 1f)
            && stats.enemyData != null)
        {
            // stats.enemyData is the per-enemy clone made in EnemyStats.Awake.
            stats.enemyData.damage *= eliteDamageMultiplier;
        }
    }

    // RED GLOW OVERLAY
    private void BuildGlow()
    {
        if (!pulseHead || bodySR == null) return;

        var go = new GameObject("RedEyeGlow");
        go.transform.SetParent(bodySR.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        glowSR = go.AddComponent<SpriteRenderer>();
        glowSR.sharedMaterial = GlowMaterial;
        glowSR.enabled = false;
    }

    // One shared additive material for every RedEye. SpriteRenderer.color is a
    // vertex colour, not a material property, so sharing costs nothing and each
    // eye still pulses independently.
    //
    // Built from LaserVfxAssets.AdditiveShader — the project's own resolver, which
    // prefers a REAL additive shader (Legacy Shaders/Particles/Additive) and only
    // falls back to Sprites/Default. Setting _SrcBlend/_DstBlend on Sprites/Default
    // does nothing: that shader hard-codes its blend and exposes no such properties,
    // so the glow came out as a flat alpha tint rather than a glow. The SpriteRenderer
    // binds the sprite's own texture to _MainTex, so the material needs no texture.
    private static Material _glowMat;
    private static Material GlowMaterial
    {
        get
        {
            if (_glowMat != null) return _glowMat;

            _glowMat = new Material(LaserVfxAssets.AdditiveShader) { name = "RedEyeGlowAdditive" };
            LaserVfxAssets.Tint(_glowMat, Color.white);   // legacy particle shaders default to grey
            _glowMat.hideFlags = HideFlags.HideAndDontSave;
            return _glowMat;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _glowMat = null; }

    // LateUpdate so we copy the sprite AFTER the animation controller has set it
    // and after the flip / sorting have settled for this frame.
    private void LateUpdate()
    {
        if (glowSR == null) return;

        if (!pulseHead || bodySR == null || bodySR.sprite == null || !bodySR.enabled || IsDying())
        {
            glowSR.enabled = false;
            return;
        }

        glowSR.enabled = true;
        glowSR.sprite = bodySR.sprite;
        glowSR.flipX = bodySR.flipX;
        glowSR.flipY = bodySR.flipY;
        glowSR.maskInteraction = bodySR.maskInteraction;
        ApplyGlowSorting();

        float wave = 0.5f - 0.5f * Mathf.Cos(Time.time * pulseHz * Mathf.PI * 2f + pulsePhase);
        float a = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, wave);
        if (Firing) a *= Mathf.Max(1f, pulseFiringBoost);

        Color c = pulseColor;
        c.a = Mathf.Clamp01(a);
        glowSR.color = c;
    }

    // Pin the glow into the band between the generative grass and the fog.
    //
    // Reference orders (BiomeManager / GrassCartoonOverlay):
    //   grass  = sortOrderBase(1000) + -y * sortPrecision(10)  ->  ~400 .. 1600
    //   eye    = EyeChains.eyeBodyOrder                        ->  3000
    //   fog    = 5000,  night overlay = 6000
    //
    // The glow must never be derived from bodySR.sortingOrder: that value is
    // contested every frame (YSortEntity writes the grass-band order, EyeChains
    // pins it back to 3000), so whoever ran last decided whether the glow was
    // visible. Taking EyeChains.EyeBodyOrder directly is deterministic.
    private void ApplyGlowSorting()
    {
        string layer;
        int order;

        if (chains != null)
        {
            layer = chains.ClusterSortingLayer;
            order = chains.EyeBodyOrder + glowSortingOffset;
        }
        else if (glowYSortToBiome)
        {
            layer = string.IsNullOrEmpty(glowFallbackSortingLayer) ? "Default" : glowFallbackSortingLayer;
            int grassHere = biomeSortBase + Mathf.RoundToInt(-transform.position.y * biomeSortPrecision);
            order = grassHere + glowAboveGrassBias;
        }
        else
        {
            layer = string.IsNullOrEmpty(glowFallbackSortingLayer) ? "Default" : glowFallbackSortingLayer;
            order = biomeSortBase + glowAboveGrassBias;
        }

        order = Mathf.Clamp(order, biomeSortBase, Mathf.Max(biomeSortBase, glowMaxSortingOrder));

        glowSR.sortingLayerName = layer;
        glowSR.sortingOrder = order;

        if (debugLogs && !loggedSorting)
        {
            loggedSorting = true;
            int grassHere = biomeSortBase + Mathf.RoundToInt(-transform.position.y * biomeSortPrecision);
            Debug.Log($"[RedEye] Glow sorting: layer={layer} order={order} " +
                      $"(local grass ~{grassHere}, eye body " +
                      $"{(chains != null ? chains.EyeBodyOrder.ToString() : "n/a")}, fog 5000)", this);
        }
    }

    // LASER
    private void Update()
    {
        // Dead or dying: kill the beam and stop. EnemyDeathVFX disables every
        // MonoBehaviour on the enemy, which stops Update() — but the Eye can also
        // die through the animated path (EyeStats.DelayedEyeDeath), where this
        // component stays enabled on a corpse for a second or so.
        if (IsDying())
        {
            StopBeam(resetCooldown: false);
            return;
        }

        if (!enableLaser) return;

        float dt = Time.deltaTime;

        if (Firing)
        {
            fireTimer -= dt;

            if (!IsTargetStillValid())
            {
                StopBeam(resetCooldown: true);
                return;
            }

            EnsureBeam();
            beam.Fire(MuzzleWorld, laserTarget.position);

            // Sound starts with the VISIBLE beam (charge-up), not at ignition — see
            // UpdateLaserSfx. Damage still waits for ignition, below.
            UpdateLaserSfx();

            // Damage only once the beam has actually ignited — the charge-up is
            // a free warning window.
            if (beam.IsFiring)
            {
                tickTimer -= dt;
                if (tickTimer <= 0f)
                {
                    tickTimer = Mathf.Max(0.02f, laserTickInterval);
                    ApplyLaserDamage(laserDamagePerSecond * tickTimer);
                }
            }

            if (fireTimer <= 0f) StopBeam(resetCooldown: true);
            return;
        }

        cooldownTimer -= dt;
        if (cooldownTimer > 0f) return;

        Transform t = AcquireLaserTarget();
        if (t == null)
        {
            cooldownTimer = 0.25f;   // nothing in range — re-scan shortly
            return;
        }

        laserTarget = t;
        fireTimer = Mathf.Max(0.05f, beamChargeTime) + Mathf.Max(0.05f, laserBeamDuration);
        tickTimer = 0f;              // first tick lands the moment the beam ignites

        if (playLaserAnimation && animController != null
            && stats != null && stats.enemyData != null
            && stats.enemyData.laserAttack.frameCount > 0)
        {
            animController.PlayLaserAttackAnimation();
        }

        if (debugLogs) Debug.Log($"[RedEye] Laser at {laserTarget.name} on {name}");
    }

    // Rising edge of "emitting" starts the loop, falling edge stops it, and while it
    // plays the sound is kept on the muzzle — the eye drifts, and a laser pinned to
    // wherever it happened to be when it ignited reads as wrong immediately.
    // Beam sound lifecycle + wind-up. Called every frame while the eye is in its fire
    // window (charge OR emit). The OLD version gated on beam.IsFiring, which is only
    // true AFTER the ~beamChargeTime charge-up — so the beam was on screen for nearly
    // half a second in silence. Here the sound starts the moment the beam becomes
    // visible (the charge) and its volume + pitch swell across the charge, hitting full
    // as the beam ignites. That both removes the delay and gives the "power-up" onset.
    private void UpdateLaserSfx()
    {
        if (!playLaserSound || FMODEvents.instance == null) return;

        // Firing == inside the fireTimer window; beam != null == the VFX exists. This
        // goes true the same frame we begin calling beam.Fire(), i.e. charge-start.
        bool beamOnScreen = Firing && beam != null;

        if (beamOnScreen && !laserSfx.IsActive)
        {
            if (laserSfx.Play(FMODEvents.instance.redEyeLaser, MuzzleWorld))
            {
                laserSfxLevel = 0f;
                laserSfx.SetVolume(laserSoundStartVolume);
                laserSfx.SetPitch(laserSoundStartPitch);
            }
        }
        else if (!beamOnScreen && laserSfx.IsActive)
        {
            laserSfx.Stop(immediate: false); // power-down fade
            laserSfxLevel = 0f;
            return;
        }

        if (!laserSfx.IsActive) return;

        laserSfx.SetPosition(MuzzleWorld);

        // Swell toward full. During the charge the target sits just under 1 so there is
        // a small audible "kick" the moment the beam ignites (beam.IsFiring) and the
        // target jumps to 1.
        float target = beam.IsFiring ? 1f : 0.85f;
        float rate = 1f / Mathf.Max(0.01f, laserSoundRiseTime);
        laserSfxLevel = Mathf.MoveTowards(laserSfxLevel, target, rate * Time.deltaTime);

        laserSfx.SetVolume(Mathf.Lerp(laserSoundStartVolume, 1f, laserSfxLevel));
        laserSfx.SetPitch(Mathf.Lerp(laserSoundStartPitch, 1f, laserSfxLevel));
    }

    private void StopBeam(bool resetCooldown)
    {
        if (beam != null) beam.StopFiring();

        // ALLOWFADEOUT: the beam VFX powers down rather than vanishing, so chopping
        // the sound at exactly fireTimer == 0 would end it before the picture does.
        laserSfx.Stop(immediate: false);
        laserSfxLevel = 0f;

        if (playLaserAnimation && animController != null && animController.IsPlayingLaserAttack())
            animController.StopLaserAttackAnimation();

        laserTarget = null;
        fireTimer = -1f;
        if (resetCooldown) cooldownTimer = Mathf.Max(0.1f, laserInterval);
    }

    // The beam VFX lives on its OWN root object, not under the eye — the eye is
    // scaled 0.25 on the prefab and LineRenderer widths / sprite glows would
    // inherit that. Same trick EyeChains uses for its chain root.
    private void EnsureBeam()
    {
        if (beam != null) return;

        beamHost = new GameObject(gameObject.name + "_RedEyeLaser");
        // Configure BEFORE Awake runs: LaserBeam bakes beamColor into its particle
        // systems when it builds them.
        beamHost.SetActive(false);

        beam = beamHost.AddComponent<LaserBeam>();
        beam.beamColor = beamColor;
        beam.intensity = beamIntensity;
        beam.chargeTime = beamChargeTime;
        beam.coreWidth = beamCoreWidth;
        beam.glowWidth = beamGlowWidth;
        beam.auraWidth = beamAuraWidth;
        beam.sortingLayer = beamSortingLayer;
        beam.sortingOrder = beamSortingOrder;

        beamHost.SetActive(true);
    }

    private bool IsDying()
    {
        if (stats == null) return true;
        if (stats.IsDead()) return true;
        return animController != null && animController.IsDying;
    }

    private bool IsTargetStillValid()
    {
        if (laserTarget == null || !laserTarget.gameObject.activeInHierarchy) return false;
        // Small grace factor so a target drifting just past the edge doesn't
        // stutter the beam off mid-burn.
        float maxDist = laserRange * 1.25f;
        return (laserTarget.position - transform.position).sqrMagnitude <= maxDist * maxDist;
    }

    // TARGETING
    // Deliberately independent of EnemyController.CurrentTarget: the chains use
    // the controller's short attack range, the laser reaches much further.
    private Transform AcquireLaserTarget()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, laserRange);
        if (hits == null || hits.Length == 0) return null;

        Transform player = null;
        Transform bestBuilding = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            // Never target other enemies.
            if (col.GetComponentInParent<EnemyStats>() != null) continue;

            GameObject go = ResolveTarget(col);
            if (go == null) continue;

            if (go.CompareTag("Player"))
            {
                player = go.transform;
                continue;
            }

            if (!laserHitsBuildings) continue;

            bool isBuilding = go.GetComponent<IEnergyConsumer>() != null
                              || go.CompareTag("Tower") || go.CompareTag("Core");
            if (!isBuilding) continue;

            float d = (go.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; bestBuilding = go.transform; }
        }

        if (laserTargetsPlayerFirst && player != null) return player;
        return bestBuilding != null ? bestBuilding : player;
    }

    // Walks up to the tagged root / stats holder, exactly like Eye.FireAOE does,
    // so a tower with several child colliders resolves to one damageable object.
    private static GameObject ResolveTarget(Collider2D col)
    {
        if (col == null) return null;

        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag("Player") || t.CompareTag("Core") || t.CompareTag("Tower"))
                return t.gameObject;
            if (t.GetComponent<CharacterStats>() != null) return t.gameObject;
            if (t.GetComponent<IEnergyConsumer>() != null) return t.gameObject;
            t = t.parent;
        }
        return col.gameObject;
    }

    // DAMAGE — the same three routes the Eye's AOE already uses. Nothing new.
    private void ApplyLaserDamage(float amount)
    {
        if (amount <= 0f || laserTarget == null) return;

        GameObject go = laserTarget.gameObject;

        if (go.CompareTag("Player"))
        {
            if (EnemyDamageSystem.Instance != null)
                EnemyDamageSystem.Instance.DamageTarget(go, amount, gameObject);
            else
            {
                var cs = go.GetComponent<CharacterStats>();
                if (cs != null) cs.TakeDamage(amount);
            }
            return;
        }

        var consumer = go.GetComponent<IEnergyConsumer>();
        if (consumer != null)
        {
            if (EnergyManager.Instance != null)
                EnergyManager.Instance.DamageEnergyConsumer(consumer, amount, gameObject);
            return;
        }

        var stats2 = go.GetComponent<CharacterStats>();
        if (stats2 != null) stats2.TakeDamage(amount);
    }

    // CLEANUP
    private void OnDisable()
    {
        // Killed, pooled or disabled mid-burn: no orphaned beam, and no laser droning
        // on out of a corpse. Hard cut here — the picture is gone this frame, so a
        // fade-out tail would be a sound with nothing making it.
        if (beam != null) beam.StopImmediate();
        laserSfx.Stop(immediate: true);
        laserTarget = null;
        fireTimer = -1f;
    }

    private void OnDestroy()
    {
        // Idempotent: OnDisable normally got here first, but an object destroyed
        // while already inactive never ran it.
        laserSfx.Stop(immediate: true);
        if (beamHost != null) Destroy(beamHost);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.15f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, laserRange);
    }
}

