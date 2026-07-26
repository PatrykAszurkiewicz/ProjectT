using UnityEngine;

// Mort - a ranged artillery enemy.
//
// This one component covers everything Mort-specific:
//   • Fires its shell (MortOrb) from the MOUTH at the configured hit frame,
//     mirroring the muzzle when the sprite faces left.
//   • Lets MortOrb work as a shell even though it ships as a pure VFX prefab
//     (particles/trail/light) with no MortarProjectile.
//   • Gives the Mort its floating "artillery platform" look: a decorative plate
//     underneath plus a gentle hover.
//
// The 00->44 shot animation itself needs no code here — it's driven by the
// Mort's EnemyData (idle = frame 0, attack = frames 0..44, hitFrame = 44) and
// played by the shared EnemyAnimationController.
[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(EnemyStats))]
public class MortController : MonoBehaviour
{
    [Header("Mortar Shell")]
    [Tooltip("Prefab fired from the Mort's mouth. Assign MortOrb here. It does " +
             "NOT need to already carry a MortarProjectile component — if it's " +
             "missing one, this controller adds and configures it at runtime " +
             "(the orb's own particles / trail / light stay as the visuals). " +
             "Add a MortarProjectile in the Inspector only if you want to tune " +
             "its blast radius / arc per-Mort.")]
    [SerializeField] private GameObject mortarPrefab;

    [Tooltip("Seconds the shell spends in the air before it lands. This IS the " +
             "dodge window — longer = easier to escape, shorter = harder. The " +
             "blast lands wherever the target was when the shell was released.")]
    [SerializeField] private float flightTime = 1.1f;

    [Tooltip("MANUAL fallback muzzle offset, used only when 'Auto Fit Muzzle To " +
             "Sprite' is off. Measured from the Mort's origin with the sprite facing " +
             "RIGHT (its default); X is mirrored automatically when it faces left. " +
             "Its Z is always used.")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0.6f, 0.1f, 0f);

    [Tooltip("When on (recommended), the muzzle is derived from the CURRENT sprite's " +
             "own bounds using 'Muzzle Normalized' below, so it lands on the mouth " +
             "no matter what pixels-per-unit, pivot or transform scale the Mort uses. " +
             "'Spawn Offset' above is then ignored (except its Z). Turn off to go back " +
             "to hand-tuning Spawn Offset in world units.")]
    [SerializeField] private bool autoFitMuzzleToSprite = true;

    [Tooltip("Where the mouth sits inside the sprite's rect, as a 0..1 fraction " +
             "measured from the BOTTOM-LEFT of the source PNG. The defaults are " +
             "measured from 44.png: the glowing orb's centre sits at 79.5% across " +
             "and 58.9% up. Only touch this if the art is re-cropped.")]
    [SerializeField] private Vector2 muzzleNormalized = new Vector2(0.795f, 0.589f);

    [Tooltip("Extra nudge (world units) applied after the auto-fit, for taste.")]
    [SerializeField] private Vector3 muzzleFineTune = Vector3.zero;

    [Tooltip("If true (default), and the assigned prefab has no MortarProjectile, " +
             "one is added at runtime so MortOrb works as a shell out of the box. " +
             "Turn off if you deliberately want a non-MortarProjectile prefab.")]
    [SerializeField] private bool autoAddProjectileComponent = true;

    [Tooltip("Safety cap (seconds) before an in-flight shell self-destructs " +
             "(detonating where it is), so a shell can never leak.")]
    [SerializeField] private float shellMaxLifetime = 6f;

    [Tooltip("Optional aim lead. 0 = aim exactly where the target is now " +
             "(most dodge-able). 1 = aim where the target would be after the " +
             "full flight if it kept its current velocity (much harder to dodge, " +
             "needs a Rigidbody2D on the target to read velocity).")]
    [Range(0f, 1f)]
    [SerializeField] private float aimLead = 0f;

    [Header("Movement")]
    [Tooltip("Scales the Mort's walk speed at spawn. 1 = exactly what the EnemyData " +
             "asset says; 0.7 = 30% slower. Applied to this Mort's own cloned copy of " +
             "EnemyData, so it never leaks onto other enemies sharing the asset. " +
             "Artillery should lumber — it wants to stop and shell, not chase.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float moveSpeedMultiplier = 0.65f;

    [Header("Shot Animation")]
    [Tooltip("Total frames in the shot animation (00.png .. 44.png = 45). Used both " +
             "by the validator and by the safety net below.")]
    [SerializeField] private int totalShotFrames = 45;

    [Tooltip("Seconds per frame for the shot. 45 frames at 0.05 = a 2.25s shell. " +
             "Applied to EnemyData.attack.speedOverride when that is left at 0, so " +
             "it does not disturb the Mort's idle or death timing.")]
    [SerializeField] private float shotSecondsPerFrame = 0.05f;

    [Tooltip("Safety net. If the EnemyData asset's frame ranges don't describe the " +
             "45-frame shot, rewrite them at spawn (idle = frame 0, attack = 0..44, " +
             "hitFrame = 44) so the Mort animates correctly regardless. A warning is " +
             "logged when this fires — the asset is still the right place to fix it.")]
    [SerializeField] private bool autoConfigureFrames = true;

    [Header("Shot Squeeze")]
    [Tooltip("Inflate the Mort through the wind-up and pop him back as the shell " +
             "leaves, so the orb reads as being squeezed out of his body.")]
    [SerializeField] private bool enableShotSqueeze = true;

    [Tooltip("Peak scale gain just before release. 0.16 = 16% bigger at full charge.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float inflateAmount = 0.16f;

    [Tooltip("How back-loaded the swell is. 1 = grows evenly across the whole " +
             "wind-up; 3 = stays near normal size and balloons in the last third. " +
             "Higher reads as 'building pressure'.")]
    [Range(1f, 6f)]
    [SerializeField] private float inflateCurvePower = 3f;

    [Tooltip("How much wider than taller he swells. 0 = uniform, 1 = all width. " +
             "A little anisotropy keeps him from looking like a balloon.")]
    [Range(0f, 1f)]
    [SerializeField] private float bulgeAnisotropy = 0.35f;

    [Tooltip("Inward velocity applied at the instant of release. Higher = a harder " +
             "snap that overshoots past normal size into a visible squeeze.")]
    [SerializeField] private float releaseKick = 2.6f;

    [Tooltip("Wobble frequency (Hz) of the post-shot settle.")]
    [SerializeField] private float releaseSpringFrequency = 3.2f;

    [Tooltip("Settle damping. Below 1 wobbles, 1 stops dead. 0.45 gives two or " +
             "three decaying jiggles.")]
    [Range(0.05f, 1.5f)]
    [SerializeField] private float releaseDamping = 0.45f;

    [Header("Plate")]
    [Tooltip("The MortPlate sprite the enemy rides on. If left empty, this " +
             "component looks for an existing child called 'Plate Child Name' " +
             "and animates that instead.")]
    [SerializeField] private Sprite plateSprite;

    [Tooltip("Name of the plate child. Reused if it already exists; otherwise " +
             "one is created from 'plateSprite'.")]
    [SerializeField] private string plateChildName = "MortPlate";

    [Tooltip("Local position of the plate relative to the Mort (put it slightly " +
             "below the body so the enemy sits ON it).")]
    [SerializeField] private Vector3 plateLocalPosition = new Vector3(0f, -0.35f, 0f);

    [Tooltip("Uniform scale applied to the plate sprite.")]
    [SerializeField] private float plateScale = 1f;

    [Tooltip("How many sorting-order steps BEHIND the body the plate renders.")]
    [SerializeField] private int plateSortingOffset = 1;

    [Header("Body Levitation")]
    [Tooltip("Peak vertical bob of the Mort's body, in world units. Small is " +
             "better — this reads as a hover, not a jump. The collider bobs with " +
             "it, so keep it subtle (~0.1).")]
    [SerializeField] private float bodyBobAmplitude = 0.10f;

    [Tooltip("Bob cycles per second for the body.")]
    [SerializeField] private float bodyBobSpeed = 0.9f;

    [Header("Plate Levitation")]
    [Tooltip("If true, the plate keeps its own float independent of the body's " +
             "bob (nice parallax). If false it just rides along with the body.")]
    [SerializeField] private bool decouplePlateFromBodyBob = true;

    [Tooltip("Peak vertical bob of the plate itself, in world units.")]
    [SerializeField] private float plateBobAmplitude = 0.05f;

    [Tooltip("Bob cycles per second for the plate.")]
    [SerializeField] private float plateBobSpeed = 0.7f;

    [Tooltip("Phase offset (0..1 of a cycle) between plate and body bobs so they " +
             "don't move in lockstep. 0.5 = perfectly out of phase.")]
    [Range(0f, 1f)]
    [SerializeField] private float plateBobPhaseOffset = 0.5f;

    private const float TAU = Mathf.PI * 2f;

    private EnemyController enemyController;
    private EnemyStats stats;
    private SmoothSpriteFlip spriteFlip;   // resolved lazily; drives mouth mirroring

    private SpriteRenderer bodyRenderer;
    private Transform plate;
    private SpriteRenderer plateRenderer;

    // Vertical offset currently baked into the root transform by the hover. We
    // only ever apply the *difference* from this, so the bob is a pure bounded
    // oscillation layered on top of the velocity-driven movement — no drift.
    private float appliedBodyOffset;

    // Per-instance phase so a pack of Morts doesn't hover in unison.
    private float phaseSeed;

    private EnemyAnimationController animController;

    // Squeeze state. Like the hover, the scale is applied as a DELTA against what
    // we put in last frame, so SmoothSpriteFlip (which owns the sign / smooth
    // turn of localScale.x) is never stomped on.
    private float appliedScaleX = 1f;
    private float appliedScaleY = 1f;
    private float squeezeValue;    // current scale gain, can go negative (squeezed)
    private float squeezeVelocity;
    private bool isReleasing;      // true from the shot until the spring settles

    // Latches the first frame we notice this Mort is dead or dying. Once set it
    // never clears, so nothing can un-kill him back into firing.
    private bool hasDied;

    private void Awake()
    {
        // Assign the attack override in Awake so it's in place before the first
        // attack cycle can run — same ordering rationale as PitcherController.
        enemyController = GetComponent<EnemyController>();
        stats = GetComponent<EnemyStats>();
        bodyRenderer = GetComponent<SpriteRenderer>();
        animController = GetComponent<EnemyAnimationController>();
        phaseSeed = Random.value * TAU;

        if (enemyController != null)
            enemyController.AttackHandlerOverride = FireMortar;

        // Must happen in Awake: EnemyController.Start() caches hitFrame into
        // resolvedHitFrame, and every Awake on a GameObject runs before any
        // Start, so fixing the data here is guaranteed to be seen.
        ApplyFrameConfigFallback();

        EnsurePlate();
    }

    private void Start()
    {
        // EnemyStats.Awake() has already replaced enemyData with a per-instance
        // CLONE, so scaling it here only slows THIS Mort — the shared asset and
        // every other enemy using it are untouched. Start (not Awake) guarantees
        // that clone has happened regardless of component execution order.
        if (stats != null && stats.enemyData != null
            && !Mathf.Approximately(moveSpeedMultiplier, 1f))
        {
            stats.enemyData.moveSpeed *= moveSpeedMultiplier;
        }

        ValidateShotAnimation();
    }

    // Rewrites the Mort's ATTACK/IDLE frame ranges to describe the 45-frame shot
    // when the EnemyData asset doesn't already. A fresh EnemyData asset defaults
    // every range to a single frame, which is exactly the "attack.frameCount is 1"
    // case: nothing to animate, so the Mort sits frozen on sprite 00.
    //
    // DELIBERATELY does NOT touch data.death. EnemyStats.Die() branches on
    // death.frameCount > 0: at 0 the enemy is destroyed immediately, above 0 it
    // goes down the DelayedDeath() path instead — a completely different death
    // lifecycle (deferred destroy, deferred wave-counter decrement, a window where
    // the corpse still counts as a living enemy). Rewriting it silently changed
    // that lifecycle, so death configuration stays exclusively the asset's business.
    // Parry frames are left alone for the same reason.
    private void ApplyFrameConfigFallback()
    {
        if (!autoConfigureFrames) return;

        var data = stats != null ? stats.enemyData : null;
        if (data == null) return;

        int frames = Mathf.Max(2, totalShotFrames);
        int wantHit = frames - 1;

        bool alreadyGood = data.attack.startFrame == 0
                        && data.attack.frameCount == frames
                        && data.hitFrame == wantHit;
        if (alreadyGood) return;

        Debug.LogWarning(
            $"[Mort] {name}: EnemyData '{data.name}' described attack " +
            $"{data.attack.startFrame}..{data.attack.startFrame + data.attack.frameCount - 1} " +
            $"with hitFrame {data.hitFrame}. Rewriting to the {frames}-frame shot " +
            $"(attack 0..{wantHit}, hitFrame {wantHit}). Death/parry config is left " +
            $"untouched. Set these on the asset to silence this.");

        var atk = data.attack;
        atk.startFrame = 0;
        atk.frameCount = frames;
        if (atk.speedOverride <= 0f && shotSecondsPerFrame > 0f)
            atk.speedOverride = shotSecondsPerFrame;
        data.attack = atk;

        // Idle is a single frame — sprite 00, mouth open and empty. Any longer and
        // the idle loop would replay the orb charging up while he stands around.
        var idle = data.idle;
        idle.startFrame = 0;
        idle.frameCount = 1;
        data.idle = idle;

        data.hitFrame = wantHit;
    }

    // The 00->44 shot is one long, single-pass animation and it only reads
    // correctly if the EnemyData asset agrees. Duplicating another enemy's
    // EnemyData is the usual way this breaks: the attack range stays at that
    // enemy's frames, so the Mort plays a slice out of the middle of its
    // wind-up, never reaches the frame that releases the orb, and looks like
    // its attack animation "isn't playing". Fail loudly instead of silently.
    private void ValidateShotAnimation()
    {
        var data = stats != null ? stats.enemyData : null;
        if (data == null)
        {
            Debug.LogError($"[Mort] {name} has no EnemyData — no animation can play.");
            return;
        }

        if (data.attack.frameCount <= 1)
        {
            Debug.LogError($"[Mort] {name}: EnemyData.attack.frameCount is " +
                           $"{data.attack.frameCount}. The shot needs the full range — " +
                           $"set attack.startFrame = 0, attack.frameCount = {totalShotFrames} " +
                           $"(or turn on Auto Configure Frames).");
        }

        if (data.hitFrame >= data.attack.frameCount)
        {
            Debug.LogError($"[Mort] {name}: hitFrame {data.hitFrame} is outside the attack " +
                           $"range (frameCount {data.attack.frameCount}), so the orb would " +
                           $"never be released. Set attack.frameCount = 45, hitFrame = 44.");
        }

        if (data.idle.startFrame == data.attack.startFrame && data.idle.frameCount > 1)
        {
            Debug.LogWarning($"[Mort] {name}: idle covers {data.idle.frameCount} frames " +
                             $"starting at the same frame as the attack, so the Mort will " +
                             $"look like it is charging its orb while standing around. " +
                             $"Set idle.frameCount = 1.");
        }
    }

    private void OnDestroy()
    {
        // Drop the delegate so nothing holds a stale reference to this
        // (now destroyed) component.
        if (enemyController != null)
            enemyController.AttackHandlerOverride = null;
    }

    private void OnDisable()
    {
        // Remove any hover offset we baked in so the physics core is left exactly
        // where it should be (matters if the object is ever pooled/reused).
        if (!Mathf.Approximately(appliedBodyOffset, 0f))
        {
            transform.position -= Vector3.up * appliedBodyOffset;
            appliedBodyOffset = 0f;
        }

        ClearSqueezeScale();
    }

    // ---------------------------------------------------------------- Firing --

    // World position of the mouth muzzle, mirrored to match the current facing.
    // The Mort art faces right by default, so a positive spawnOffset.x is the
    // right-facing mouth; when the animation flips the sprite to face left we
    // mirror X so the shell still exits the mouth rather than the tail.
    private Vector3 GetMuzzleWorldPosition()
    {
        if (spriteFlip == null)
            spriteFlip = GetComponent<SmoothSpriteFlip>();

        float xSign = (spriteFlip != null && spriteFlip.IsFacingLeft) ? -1f : 1f;

        Vector3 local = spawnOffset;

        if (autoFitMuzzleToSprite && bodyRenderer != null && bodyRenderer.sprite != null)
        {
            // Sprite.bounds is in LOCAL units and already accounts for both the
            // pivot and pixels-per-unit, so a normalised (u,v) inside the rect
            // converts straight to a local offset. That means the muzzle keeps
            // sitting on the mouth even if the art is re-imported at a different
            // PPU, re-pivoted, or the prefab is scaled.
            Bounds b = bodyRenderer.sprite.bounds;
            local = new Vector3(
                b.min.x + muzzleNormalized.x * b.size.x,
                b.min.y + muzzleNormalized.y * b.size.y,
                spawnOffset.z);

            // Respect prefab scaling, but take the magnitude on X: a flip done by
            // negating localScale.x would otherwise cancel the xSign below and
            // fire the shell out of the tail.
            Vector3 s = transform.localScale;
            local = new Vector3(local.x * Mathf.Abs(s.x), local.y * s.y, local.z);
            local += muzzleFineTune;
        }

        Vector3 offset = new Vector3(local.x * xSign, local.y, local.z);

        // Apply the body's current tilt so the muzzle tracks the sprite while the
        // Mort is still levelling out at the start of a shot.
        return transform.position + transform.rotation * offset;
    }

    // A dead Mort must never release a shell. EnemyController.AttackCycle is a
    // COROUTINE, and EnemyStats.DelayedDeath() only does `controller.enabled =
    // false` — which in Unity does not stop running coroutines. So the in-flight
    // attack cycle keeps counting down over the corpse and still calls
    // PerformHit() -> AttackHandlerOverride -> FireMortar. That is the shot that
    // appears to come out of a Mort you already killed.
    //
    // Several independent signals are checked because the kill can land anywhere
    // in the cycle, and whichever one trips first wins.
    private bool IsDeadOrDying
    {
        get
        {
            if (hasDied) return true;

            // Health is the earliest signal — set inside CharacterStats.TakeDamage
            // before Die() has done anything.
            if (stats == null || stats.currentHealth <= 0f) { hasDied = true; return true; }

            // Death animation has begun.
            if (animController != null && animController.IsDying) { hasDied = true; return true; }

            // DelayedDeath() has disabled the controller.
            if (enemyController != null && !enemyController.enabled) { hasDied = true; return true; }

            return false;
        }
    }

    // Invoked by EnemyController.PerformHit() at the configured hit frame of the
    // attack animation (frame 44 for the Mort — the moment the shot completes).
    // 'target' is whatever the controller currently has locked: player, a tower,
    // or the core.
    private void FireMortar(Transform target)
    {
        // Checked first, before the target test, so a shot is suppressed even when
        // the cycle still holds a perfectly valid target. Returning here (rather
        // than clearing AttackHandlerOverride) also means PerformHit stops at the
        // override and never falls through to the default melee damage — a corpse
        // deals nothing at all.
        if (IsDeadOrDying)
        {
            // Kill any wind-up bulge still on the body so he doesn't die inflated.
            ClearSqueezeScale();
            return;
        }

        if (target == null) return;

        if (mortarPrefab == null)
        {
            Debug.LogWarning($"[Mort] {name} has no mortarPrefab assigned — no shell fired.");
            return;
        }

        Vector3 spawn = GetMuzzleWorldPosition();

        // Hand the body its recoil now, on the same frame the shell exists, so
        // the pop is locked to the release rather than to a timer.
        TriggerReleaseSqueeze();

        // Capture the landing spot once
        Vector3 landing = target.position;
        if (aimLead > 0f)
        {
            var targetRb = target.GetComponent<Rigidbody2D>();
            if (targetRb != null)
                landing += (Vector3)(targetRb.linearVelocity * (flightTime * aimLead));
        }

        // Spawn pointing roughly at the landing spot (cosmetic; the shell
        // re-orients itself along its arc each frame anyway).
        Vector3 dir = landing - spawn;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (float.IsNaN(angle) || float.IsInfinity(angle)) angle = 0f;

        GameObject shellObj = Instantiate(
            mortarPrefab, spawn, Quaternion.AngleAxis(angle, Vector3.forward));

        // Launch SFX (enemy Mort). Mirrors the player mortar's shot sound.
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.mortarShot.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.mortarShot, spawn);
        }

        // MortOrb ships as a pure VFX prefab (particles + trail + light) with no
        // MortarProjectile. Add one on the fly so it arcs + explodes exactly like
        // the classic shell, while keeping the orb's own visuals. RequireComponent
        // on MortarProjectile pulls in a SpriteRenderer automatically; it simply
        // draws no sprite, which is fine — the orb is drawn by its particles.
        var shell = shellObj.GetComponent<MortarProjectile>();
        if (shell == null && autoAddProjectileComponent)
            shell = shellObj.AddComponent<MortarProjectile>();

        if (shell != null)
        {
            // Hand the firing controller to the shell so that, on detonation, it
            // can reuse EnemyController.ApplyDamageToTarget for each victim.
            float damage = stats != null ? stats.Damage : 0f;
            shell.Initialize(enemyController, landing, damage, flightTime, shellMaxLifetime);
        }
        else
        {
            Debug.LogWarning($"[Mort] mortarPrefab '{mortarPrefab.name}' has no " +
                             $"MortarProjectile component and auto-add is disabled.");
        }
    }

    // ------------------------------------------------------ Shot squeeze --

    // Hands the swell over to a damped spring aimed at zero, with a hard inward
    // velocity. The value carries its current (inflated) magnitude into the
    // spring, so it snaps down through normal size into a genuine squeeze before
    // wobbling back — the body visibly expelling the shell.
    private void TriggerReleaseSqueeze()
    {
        if (!enableShotSqueeze) return;

        isReleasing = true;
        squeezeVelocity = -releaseKick * Mathf.Max(inflateAmount, 0.02f);
    }

    // Returns this frame's scale gain: a back-loaded swell while the shot winds
    // up, then a spring settle once the shell is away.
    private float EvaluateSqueeze(float dt)
    {
        if (!enableShotSqueeze) return 0f;

        int frame = animController != null ? animController.CurrentAttackFrame : -1;
        int hit = stats != null && stats.enemyData != null ? stats.enemyData.hitFrame : 0;

        // A fresh wind-up cancels any leftover wobble from the previous shot.
        if (isReleasing && frame >= 0 && hit > 0 && frame < hit)
        {
            isReleasing = false;
            squeezeVelocity = 0f;
        }

        if (isReleasing)
        {
            // Semi-implicit Euler on a damped harmonic oscillator targeting 0.
            float omega = TAU * Mathf.Max(0.1f, releaseSpringFrequency);
            squeezeVelocity += (-squeezeValue * omega * omega
                                - 2f * releaseDamping * omega * squeezeVelocity) * dt;
            squeezeValue += squeezeVelocity * dt;

            if (Mathf.Abs(squeezeValue) < 0.001f && Mathf.Abs(squeezeVelocity) < 0.01f)
            {
                squeezeValue = 0f;
                squeezeVelocity = 0f;
                isReleasing = false;
            }
            return squeezeValue;
        }

        // Charging. CurrentAttackFrame is -1 whenever no attack is playing, which
        // also covers the walk and the post-attack idle hold.
        float target = 0f;
        if (frame >= 0 && hit > 0)
        {
            float t = Mathf.Clamp01((float)frame / hit);
            target = inflateAmount * Mathf.Pow(t, Mathf.Max(1f, inflateCurvePower));
        }

        // Ease toward the target so a stopped / interrupted attack deflates
        // smoothly instead of popping back to normal size in one frame.
        squeezeValue = Mathf.MoveTowards(squeezeValue, target,
                                         Mathf.Max(0.5f, inflateAmount * 8f) * dt);
        squeezeVelocity = 0f;
        return squeezeValue;
    }

    // Writes the gain onto localScale as a delta against last frame's factors, so
    // whatever SmoothSpriteFlip is doing to localScale.x survives untouched.
    private void ApplySqueezeScale(float gain)
    {
        float fx = 1f + gain * (1f + bulgeAnisotropy);
        float fy = 1f + gain * (1f - bulgeAnisotropy);

        fx = Mathf.Max(0.05f, fx);
        fy = Mathf.Max(0.05f, fy);

        if (Mathf.Approximately(fx, appliedScaleX) && Mathf.Approximately(fy, appliedScaleY))
            return;

        Vector3 ls = transform.localScale;
        if (Mathf.Abs(appliedScaleX) > 0.0001f) ls.x /= appliedScaleX;
        if (Mathf.Abs(appliedScaleY) > 0.0001f) ls.y /= appliedScaleY;
        ls.x *= fx;
        ls.y *= fy;
        transform.localScale = ls;

        appliedScaleX = fx;
        appliedScaleY = fy;
    }

    private void ClearSqueezeScale()
    {
        if (Mathf.Approximately(appliedScaleX, 1f) && Mathf.Approximately(appliedScaleY, 1f))
            return;

        Vector3 ls = transform.localScale;
        if (Mathf.Abs(appliedScaleX) > 0.0001f) ls.x /= appliedScaleX;
        if (Mathf.Abs(appliedScaleY) > 0.0001f) ls.y /= appliedScaleY;
        transform.localScale = ls;

        appliedScaleX = 1f;
        appliedScaleY = 1f;
        squeezeValue = 0f;
        squeezeVelocity = 0f;
        isReleasing = false;
    }

    // ------------------------------------------------------ Plate & hover --

    private void EnsurePlate()
    {
        // Reuse a pre-authored child if the designer made one.
        Transform existing = transform.Find(plateChildName);
        if (existing != null)
        {
            plate = existing;
            plateRenderer = plate.GetComponent<SpriteRenderer>();
            if (plateRenderer == null)
                plateRenderer = plate.gameObject.AddComponent<SpriteRenderer>();
            if (plateRenderer.sprite == null && plateSprite != null)
                plateRenderer.sprite = plateSprite;
        }
        else if (plateSprite != null)
        {
            // Build one from the assigned sprite.
            var go = new GameObject(plateChildName);
            go.transform.SetParent(transform, false);
            plate = go.transform;
            plateRenderer = go.AddComponent<SpriteRenderer>();
            plateRenderer.sprite = plateSprite;
        }

        if (plate != null)
        {
            plate.localPosition = plateLocalPosition;
            plate.localScale = Vector3.one * plateScale;

            if (plateRenderer != null && bodyRenderer != null)
            {
                plateRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
                plateRenderer.sortingOrder = bodyRenderer.sortingOrder - plateSortingOffset;
            }
        }
    }

    private void LateUpdate()
    {
        // Freeze the hover once the Mort is dead so it settles cleanly instead of
        // bobbing through its death/destroy. Same signal the firing guard uses, so
        // the body can't still be mid-inflate while the shot is being suppressed.
        bool dying = IsDeadOrDying;
        float time = Time.time;

        // ---- Shot squeeze ----
        // Runs before the plate block so plateScaleComp below sees this frame's
        // factors. Freezes on death so he doesn't inflate through his own corpse.
        if (dying) ClearSqueezeScale();
        else ApplySqueezeScale(EvaluateSqueeze(Time.deltaTime));

        // ---- Body bob (drift-free delta on the physics root) ----
        // Only the change since last frame is applied, so the Rigidbody2D's own
        // velocity movement is untouched and the enemy never drifts.
        float bodyBob = dying ? 0f
            : bodyBobAmplitude * Mathf.Sin(phaseSeed + time * bodyBobSpeed * TAU);

        float delta = bodyBob - appliedBodyOffset;
        if (!Mathf.Approximately(delta, 0f))
        {
            transform.position += Vector3.up * delta;
            appliedBodyOffset = bodyBob;
        }

        // ---- Plate float + sorting ----
        if (plate != null)
        {
            float plateBob = dying ? 0f
                : plateBobAmplitude *
                  Mathf.Sin(phaseSeed + plateBobPhaseOffset * TAU + time * plateBobSpeed * TAU);

            // The plate is a child of the (already-bobbing) root. To float it
            // independently, cancel the body's offset first, then add its own.
            float localY = plateLocalPosition.y + plateBob;
            if (decouplePlateFromBodyBob)
                localY -= appliedBodyOffset;

            Vector3 lp = plate.localPosition;
            lp.x = plateLocalPosition.x / appliedScaleX;
            lp.y = localY / appliedScaleY;
            lp.z = plateLocalPosition.z;
            plate.localPosition = lp;

            // The plate is scenery the Mort rides on, not part of his body, so
            // cancel the parent's squeeze out of it. Without this it would
            // balloon and slide with every shot.
            plate.localScale = new Vector3(
                plateScale / appliedScaleX,
                plateScale / appliedScaleY,
                plateScale);

            // Keep the plate just behind the body every frame, since the body's
            // order is re-derived from Y by YSortEntity each frame.
            if (plateRenderer != null && bodyRenderer != null)
            {
                plateRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
                plateRenderer.sortingOrder = bodyRenderer.sortingOrder - plateSortingOffset;
            }
        }
    }
}


