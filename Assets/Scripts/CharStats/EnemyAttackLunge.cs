using UnityEngine;
using System.Collections.Generic;

//  ENEMY ATTACK LUNGE

[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(EnemyController))]
public class EnemyAttackLunge : MonoBehaviour
{
    public enum Preset { Custom = 0, Slime = 1, SmallSlime = 2, Wolf = 3 }

    [Header("Preset")]
    [Tooltip("Convenience only — nothing reads this at runtime. Pick one, then use " +
             "the component's context menu ▸ 'Apply Preset Values' to fill the fields " +
             "below with the tuned numbers for that enemy. Leave it on Custom once " +
             "you have hand-tuned something, as a note to the next person that these " +
             "numbers are not a preset.")]
    public Preset preset = Preset.Custom;

    // Remembers which preset the numbers below actually came from, so OnValidate can
    // tell "the dropdown was just changed" from "someone is editing a field". Hidden
    // because it is bookkeeping, not a setting.
    [SerializeField, HideInInspector] private Preset appliedPreset = Preset.Custom;

#if UNITY_EDITOR
    // Changing the dropdown fills the fields in immediately — no context menu, no
    // extra click. It fires only when the dropdown VALUE changes, so hand-tuning a
    // field afterwards is never clobbered. Switch to Custom and nothing is touched.
    private void OnValidate()
    {
        if (preset == appliedPreset) return;

        appliedPreset = preset;
        if (preset == Preset.Custom) return;

        ApplyPresetValues();
    }
#endif

    [Header("Parry sync")]
    [Tooltip("ON: the coil finishes exactly as the parry window opens (the moment " +
             "ParryIndicator raises the '!'), holds fully coiled for as long as the " +
             "window is open, and the snap commits inside it. This is what makes the " +
             "wind-up read as a telegraph rather than as generic idle motion.\n\n" +
             "OFF: the coil is timed off the hit frame alone and ignores the window. " +
             "Only useful for an enemy that has no parry window at all.")]
    public bool alignWithParryWindow = true;

    [Tooltip("Finish the coil this many seconds BEFORE the window opens. 0 = exactly " +
             "on the frame the '!' appears. A small positive value (0.02–0.05) makes " +
             "the enemy settle into its coiled pose a hair early, so the '!' lands on " +
             "an already-tense silhouette instead of arriving together with the motion " +
             "— usually the clearer read of the two.")]
    [Range(0f, 0.2f)] public float telegraphLead = 0.03f;

    [Tooltip("Augment 332 ('Longer Parry Window') opens the window earlier, and " +
             "ParryIndicator shows the '!' earlier to match. ON: shift the coil " +
             "earlier by the same amount, so the pose and the '!' stay together for " +
             "an upgraded player. Reads player 0's upgrades, matching the default " +
             "EnemyController.IsCurrentlyInParryFrames() uses.")]
    public bool mirrorParryWindowAugment = true;

    [Tooltip("Which sprite the lunge peaks on, as the number the frame ruler prints " +
             "(#17, not 'attack frame 3'). -1 = peak on hitFrame.\n\n" +
             "Set this when the frame where the art visibly CONNECTS is not the frame " +
             "where damage RESOLVES. That is a legitimate setup: the strike lands at " +
             "the top of the parry window and the damage is applied at the bottom of " +
             "it, so the player has the whole window to answer a blow they can already " +
             "see. hitFrame is the resolution frame, and a body that lunges on it looks " +
             "like it is biting well after the mouth already shut.\n\n" +
             "This moves the VISUAL only. Damage timing, the parry window and the " +
             "EnemyData asset are all untouched.")]
    public int visualImpactSpriteIndex = -1;

    [Header("Motion along the attack axis")]
    [Tooltip("How far the enemy coils BACK during the wind-up, in world units. This " +
             "is the part the player actually reads as 'it is about to bite', so it " +
             "buys more readability per unit than the forward lunge does.")]
    public float windupPullback = 0.18f;

    [Tooltip("How far PAST its resting spot the enemy snaps at the moment the hit " +
             "lands, in world units. The total visible travel of the strike is this " +
             "plus Windup Pullback.")]
    public float lungeDistance = 0.30f;

    [Tooltip("How far the enemy drifts back again just after the bite, in world " +
             "units, before settling. The 'retract' half of the motion — small " +
             "values are plenty.")]
    public float postHitRecoil = 0.06f;

    [Tooltip("Seconds spent snapping from the coiled pose to the full lunge. Short " +
             "reads as a snap. Automatically shortened if the parry window is " +
             "narrower than this, so the snap never begins before the '!' appears.")]
    [Range(0.02f, 0.4f)] public float strikeDuration = 0.07f;

    [Tooltip("Seconds to ease back to rest after the hit frame.")]
    [Range(0.05f, 1f)] public float recoveryDuration = 0.18f;

    [Header("Deform — squash & stretch")]
    [Tooltip("Compression along the attack axis at full coil, as a fraction of " +
             "scale. 0.15 = 15% shorter along that axis.")]
    [Range(0f, 0.6f)] public float windupSquash = 0.12f;

    [Tooltip("Stretch along the attack axis at the moment of the hit, as a fraction " +
             "of scale.")]
    [Range(0f, 0.6f)] public float strikeStretch = 0.16f;

    [Tooltip("How much the perpendicular axis compensates so the silhouette keeps " +
             "roughly its area. 1 = full rubbery squash-and-stretch (right for the " +
             "slimes). Lower it for anything that should read as solid, like the Wolf, " +
             "where a fully volume-preserving deform looks like jelly rather than fur.")]
    [Range(0f, 1f)] public float volumePreservation = 0.8f;

    [Header("Wind-up shake")]
    [Tooltip("Peak tremble offset in world units. Reached as the coil completes and " +
             "held for as long as the parry window is open, so the shake and the '!' " +
             "are telling the player the same thing at the same time. Perlin-driven, " +
             "so it shivers instead of strobing. 0 disables it.")]
    public float shakeAmplitude = 0.035f;

    [Tooltip("Tremble speed. Higher reads as more agitated.")]
    public float shakeFrequency = 26f;

    [Header("Settle wobble")]
    [Tooltip("Damped oscillation of the DEFORM after the hit, as a fraction of scale. " +
             "This is what makes the slimes read as gelatinous. Position is " +
             "deliberately not wobbled — that looks like sliding, not jiggling. " +
             "0 disables it.")]
    [Range(0f, 0.4f)] public float wobbleAmplitude = 0.06f;

    [Tooltip("Wobble oscillations per second.")]
    public float wobbleFrequency = 9f;

    [Tooltip("How fast the wobble dies out. Higher = fewer visible bounces.")]
    public float wobbleDamping = 10f;

    [Header("Limits")]
    [Tooltip("Sanity clamp on the pullback, lunge and recoil, as a fraction of this " +
             "enemy's attackRange. Purely a guard against a mistyped value throwing " +
             "the sprite across the screen — the offset is render-only, so this is " +
             "not a balance control.")]
    [Range(0.05f, 0.6f)] public float maxOffsetFractionOfAttackRange = 0.3f;

    [Tooltip("Turn off to keep the deform but not the movement.")]
    public bool affectPosition = true;

    [Tooltip("Turn off to keep the movement but not the squash/stretch.")]
    public bool affectScale = true;

    [Header("Debug logging")]
    [Tooltip("Prints the resolved attack timeline — parry window, coil, hold, snap, " +
             "hit — to the Console, so the numbers can be checked against what " +
             "ParryIndicator is actually drawing.\n\n" +
             "Once Per Enemy Type: one block the first time any Slime attacks, one " +
             "the first time any Wolf attacks, and so on. A wave of twenty slimes " +
             "logs once. This is the useful default.\n" +
             "Once Per Instance: one block per spawned enemy.\n" +
             "Every Attack: one block per bite. Noisy; use it when chasing a bug.\n\n" +
             "Stripped from non-development builds entirely.")]
    public TimelineLogMode timelineLogging = TimelineLogMode.OncePerEnemyType;

    [Tooltip("Logs each phase change live — COIL, HOLD, SNAP, HIT, SETTLE — with the " +
             "elapsed milliseconds AND whether EnemyController currently reports this " +
             "enemy as inside its parry frames. That last column is the direct check: " +
             "HOLD should begin at or just before the frame parry goes True, and SNAP " +
             "should still be inside it.\n\n" +
             "Very noisy with several enemies attacking — turn it on for one prefab at " +
             "a time.")]
    public bool logPhaseTransitions = false;

    [Tooltip("Logs every attack animation frame as it is DISPLAYED, with its index, " +
             "its timestamp, the lunge phase running at that moment, and markers for " +
             "the hit frame and the parry window edges.\n\n" +
             "This is the tool for 'the sprite bites before the body lunges': play, " +
             "pause the editor on the frame where the mouth actually closes on the " +
             "target, and read the index off the last logged line. That index is what " +
             "hitFrame should be. Slowing the clip down first (EnemyData ▸ attack ▸ " +
             "Speed Override = 0.4) makes it easy to stop on the right frame.")]
    public bool logAttackFrames = false;

    public enum TimelineLogMode { Off, OncePerEnemyType, OncePerInstance, EveryAttack }

    // ── Cached refs 

    private EnemyController controller;
    private EnemyStats stats;
    private ParryStunEffect parryStun;
    private EnemyAnimationController animController;

    // ── The pose every other system believes in 

    private Vector3 truePosition;
    private Vector3 trueScale;
    private Vector3 lastWrittenPosition;
    private Vector3 lastWrittenScale;
    private bool isDisplaced;

    // ── Timeline, resolved once per attack cycle. Seconds since the cycle opened.

    private float hitTime;          // damage resolves
    private float impactTime;       // the art visibly connects. Equals hitTime unless
                                    // visualImpactSpriteIndex says otherwise.
    private int impactFrame;        // impactTime as an attack-relative frame index
    private float peakTime;         // the visual peak of the lunge. Equals hitTime for
                                    // any enemy with a wind-up; for a hitFrame == 0
                                    // enemy (damage already applied on frame one) it is
                                    // pushed out by strikeTime so the bite eases in
                                    // instead of teleporting to full extension.
    private float parryOpenTime;    // '!' appears; negative when there is no window
    private float parryCloseTime;   // '!' clears;  negative when there is no window
    private float coilEndTime;      // coil fully wound
    private float strikeTime;       // duration of the snap (it starts at peakTime - this)
    private float effectDuration;   // fully settled

    // ── Live effect state 

    private bool wasAttacking;
    private bool active;
    private bool settling;
    private float cycleStartTime;
    private Vector2 attackDir = Vector2.right;

    private float curForward;       // world units along attackDir (negative = coiled back)
    private float curDeform;        // + stretched along attackDir, - squashed
    private Vector2 curShake;

    private float noiseSeed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private enum Phase { None, Coil, Hold, Snap, Settle }
    private Phase lastPhase = Phase.None;

    private bool warnedAboutTiming;
    private bool loggedThisInstance;

    // Keyed by enemy name so a wave of twenty slimes produces one timeline block
    // rather than twenty. Cleared on domain reload like any other static.
    private static readonly HashSet<string> LoggedEnemyTypes = new HashSet<string>();
    private static readonly HashSet<string> AnnouncedEnemyTypes = new HashSet<string>();

    // Statics normally die on domain reload, but Project Settings ▸ Editor ▸ Enter
    // Play Mode Settings can switch that off — and then these sets would survive from
    // one play session to the next and the logs would appear exactly once per editor
    // session. This runs on every entry into play mode either way.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLogGuards()
    {
        LoggedEnemyTypes.Clear();
        AnnouncedEnemyTypes.Clear();
    }
#endif

    private void Awake()
    {
        controller = GetComponent<EnemyController>();
        stats = GetComponent<EnemyStats>();
        parryStun = GetComponent<ParryStunEffect>();
        animController = GetComponent<EnemyAnimationController>();

        truePosition = lastWrittenPosition = transform.position;
        trueScale = lastWrittenScale = transform.localScale;

        noiseSeed = Random.value * 100f;
    }

    private void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // One line per enemy type at spawn. Without it, "no logs at all" is ambiguous
        // between "the component is not attached" and "this enemy never got to swing".
        if (timelineLogging == TimelineLogMode.Off || !AnnouncedEnemyTypes.Add(TypeKey()))
            return;

        EnemyData d = stats != null ? stats.enemyData : null;
        Debug.Log($"[EnemyAttackLunge] {name} active — EnemyData " +
                  $"'{(d != null ? d.name : "MISSING")}'. Full timeline logs on its " +
                  "first attack.", this);
#endif
    }

    // Both of these run before anything else in their phase (execution order -1000),
    // which is the whole trick: the displaced pose exists only between LateUpdate and
    // the render, never while gameplay code is looking.
    private void FixedUpdate() => RestoreTruePose();
    private void Update() => RestoreTruePose();

    private void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // OnAttackFrame is a plain Action field, so this composes with whatever else
        // has subscribed. Debug-only: if something ever reassigns the field wholesale
        // we simply stop logging, and nothing functional depends on it.
        if (animController != null) animController.OnAttackFrame += LogAttackFrame;
#endif
    }

    private void OnDisable()
    {
        // Death, pooling, or the component being switched off mid-bite. Never leave
        // a body holding a displaced pose.
        RestoreTruePose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (animController != null) animController.OnAttackFrame -= LogAttackFrame;
#endif
        active = false;
        settling = false;
        wasAttacking = false;
        curForward = 0f;
        curDeform = 0f;
        curShake = Vector2.zero;
    }

    private void LateUpdate()
    {
        // Our own Update already restored, so whatever the transform says now is the
        // pose every other system produced this frame — i.e. the rest pose. (The
        // guard covers the one frame where this component was enabled after Update.)
        if (isDisplaced) RestoreTruePose();
        truePosition = transform.position;
        trueScale = transform.localScale;

        bool attacking = controller != null && controller.IsAttacking;

        // Rising edge of the attack cycle. EnemyController sets isAttackingCycle and
        // attackCycleStartTime together at the top of AttackCycle, so by the time we
        // observe the flag the start time is already valid — and it is the same
        // anchor ParryIndicator and IsInParryWindow measure from.
        if (attacking && !wasAttacking && !MustAbort())
            BeginLunge();
        wasAttacking = attacking;

        if (active)
        {
            float t = Time.time - cycleStartTime;

            if (MustAbort() || (!attacking && t < peakTime))
            {
                // Another system has taken the body, or the cycle ended before the
                // strike (freeze, stun, target lost). Ease out instead of finishing
                // a bite that is not happening.
                active = false;
                settling = true;
            }
            else
            {
                EvaluateCurve(t);
                if (t >= effectDuration)
                {
                    active = false;
                    settling = true;   // guarantees a clean run down to exactly zero
                }
            }
        }
        else if (settling)
        {
            // Framerate-independent exponential run-down, ~0.1s.
            float k = 1f - Mathf.Exp(-Time.deltaTime * 20f);
            curForward = Mathf.Lerp(curForward, 0f, k);
            curDeform = Mathf.Lerp(curDeform, 0f, k);
            curShake = Vector2.Lerp(curShake, Vector2.zero, k);

            if (Mathf.Abs(curForward) < 0.001f && Mathf.Abs(curDeform) < 0.001f
                && curShake.sqrMagnitude < 0.000001f)
            {
                curForward = 0f;
                curDeform = 0f;
                curShake = Vector2.zero;
                settling = false;
            }
        }

        ApplyVisualPose();
    }

    // ── Cycle setup 

    private void BeginLunge()
    {
        cycleStartTime = controller.AttackCycleStartTime > 0f
            ? controller.AttackCycleStartTime
            : Time.time;

        ResolveTimeline();

        Transform target = controller.CurrentTarget;
        if (target != null)
        {
            Vector2 d = (Vector2)(target.position - transform.position);
            if (d.sqrMagnitude > 0.0001f) attackDir = d.normalized;
        }
        // No target: keep the previous direction rather than snapping to an arbitrary
        // one. Rare — a cycle only opens against a target in the first place.

        active = true;
        settling = false;
    }

    // Rebuilt every cycle rather than cached at Start, because the frame numbers are
    // not immutable: WolfController writes its overrides into a per-instance EnemyData
    // clone, and the augment shift below can change between cycles.
    private void ResolveTimeline()
    {
        EnemyData data = stats != null ? stats.enemyData : null;

        float animSpeed;
        float cycleDuration;

        if (data != null)
        {
            animSpeed = Mathf.Max(0.0001f, data.AttackAnimSpeed);
            hitTime = animSpeed * Mathf.Max(0, data.hitFrame);
            cycleDuration = Mathf.Max(data.AttackDuration, hitTime);
        }
        else
        {
            // No EnemyData (shouldn't happen for these prefabs) — pick something
            // plausible rather than dividing by zero.
            animSpeed = 0.1f;
            hitTime = 0.12f;
            cycleDuration = 0.35f;
        }

        hitTime = Mathf.Clamp(hitTime, 0f, cycleDuration);

        ResolveImpactFrame(data, animSpeed, cycleDuration);
        ResolveParryWindow(data, animSpeed);
        ResolveCoilAndStrike();

        // Let the wobble ring past the positional recovery, but cap it so a very low
        // damping value can never keep one bite's wobble alive into the next one.
        float wobbleTail = (wobbleAmplitude > 0.001f && wobbleDamping > 0.01f)
            ? Mathf.Min(0.8f, 4f / wobbleDamping)
            : 0f;

        effectDuration = peakTime + Mathf.Max(recoveryDuration, wobbleTail);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        lastPhase = Phase.None;
        MaybeLogTimeline(data);
#endif
        WarnIfTimelineUnreadable();
    }

    // The frame the BODY should peak on. Defaults to hitFrame, which is right whenever
    // damage resolves on the same frame the art connects. When the override is set it
    // wins — and only for the visual; hitTime is left alone and still drives nothing
    // here except the report.
    private void ResolveImpactFrame(EnemyData data, float animSpeed, float cycleDuration)
    {
        if (data == null)
        {
            impactFrame = 0;
            impactTime = hitTime;
            return;
        }

        int frame = Mathf.Max(0, data.hitFrame);

        if (visualImpactSpriteIndex >= 0)
            frame = AttackRelativeFrame(data, visualImpactSpriteIndex);

        int lastFrame = Mathf.Max(0, data.attack.frameCount - 1);
        impactFrame = Mathf.Clamp(frame, 0, lastFrame);
        impactTime = Mathf.Clamp(animSpeed * impactFrame, 0f, cycleDuration);
    }

    // Inverse of GlobalSpriteIndex: sprite number as the ruler prints it → the index
    // the animation frame loop counts in.
    private static int AttackRelativeFrame(EnemyData d, int spriteIndex)
    {
        bool multiFolder = d.attackFrames != null && d.attackFrames.Length > 0;
        return multiFolder ? spriteIndex : spriteIndex - d.attack.startFrame;
    }

    // Mirrors EnemyController.IsCurrentlyInParryFrames() exactly: the same degenerate
    // -config bail-out, the same (parryFrameEnd + 1) closing edge, and the same augment
    // shift on the opening edge clamped at frame 0. If that method ever changes, this
    // one has to change with it — that is the price of the two staying in lockstep.
    private void ResolveParryWindow(EnemyData data, float animSpeed)
    {
        parryOpenTime = -1f;
        parryCloseTime = -1f;

        if (!alignWithParryWindow || data == null) return;

        // ParryIndicator disables itself when there is no wind-up to react to.
        if (data.attack.frameCount <= 1) return;

        int pStart = Mathf.Max(0, data.parryFrameStart);
        int pEnd = Mathf.Max(pStart, data.parryFrameEnd);
        int hit = Mathf.Max(0, data.hitFrame);

        // All zero → EnemyController falls back to the generic "shield raised within
        // 0.2s" parry and no '!' is ever shown. Nothing to align to.
        if (pStart == 0 && pEnd == 0 && hit == 0) return;

        int effStart = pStart;
        if (mirrorParryWindowAugment)
            effStart = Mathf.Max(0, pStart - ParryUpgrades.ExtraParryFramesFor(0));

        float open = animSpeed * effStart;
        float close = animSpeed * (pEnd + 1);

        parryOpenTime = open;
        parryCloseTime = close;
    }

    private void ResolveCoilAndStrike()
    {
        float desiredStrike = Mathf.Max(0.02f, strikeDuration);

        if (impactTime <= 0.0001f)
        {
            // The impact is on frame one, so there is no wind-up to animate and a coil
            // would telegraph a blow that has already landed. Snapping straight to full
            // extension is a visible teleport, though, so the peak is pushed out by one
            // strike length and the enemy eases into it. Damage timing is untouched.
            strikeTime = desiredStrike;
            peakTime = desiredStrike;
            coilEndTime = 0f;
            return;
        }

        peakTime = impactTime;

        // Align to the window only if it opens BEFORE the body strikes. When the window
        // opens on the impact frame itself — a legitimate design where the '!' goes up
        // as the blow visibly lands and the player has until the damage frame to answer
        // it — there is nothing to hold through, so the coil simply runs up to the snap.
        if (parryOpenTime >= 0f && parryOpenTime < peakTime)
        {
            // Keep the whole snap inside the window: it may be shortened, never started
            // early. The player must not see the enemy commit before the '!' has given
            // them the chance to answer it.
            float windowToPeak = peakTime - parryOpenTime;
            strikeTime = Mathf.Clamp(desiredStrike, 0.02f, windowToPeak);

            coilEndTime = Mathf.Max(0f, parryOpenTime - telegraphLead);
            coilEndTime = Mathf.Min(coilEndTime, peakTime - strikeTime);
        }
        else
        {
            // Coil across the whole wind-up, keeping at least 20% of it as a ramp.
            strikeTime = Mathf.Min(desiredStrike, peakTime * 0.8f);
            coilEndTime = Mathf.Max(0f, peakTime - strikeTime);
        }

        // A window opening on frame 0 would otherwise mean "already fully coiled at
        // t = 0", which pops. Spend a few frames getting there instead: 60ms of
        // lead-in is far less jarring than a one-frame teleport, and the hold phase
        // still covers the rest of the window.
        float snapStart = peakTime - strikeTime;
        float minRamp = Mathf.Min(0.06f, Mathf.Max(0f, snapStart) * 0.5f);
        coilEndTime = Mathf.Max(coilEndTime, minRamp);
    }

    private bool MustAbort()
    {
        if (controller == null) return true;
        if (stats != null && stats.IsDead()) return true;
        if (controller.IsKnockedBack || controller.IsBeingGrappled()) return true;
        if (controller.ExternalMovementControl) return true;
        if (parryStun != null && parryStun.IsStunActive) return true;
        return false;
    }

    // ── The curve 

    private void EvaluateCurve(float t)
    {
        float maxOffset = MaxOffset();
        float lunge = Mathf.Clamp(lungeDistance, 0f, maxOffset);
        float pullback = Mathf.Clamp(windupPullback, 0f, maxOffset);
        float recoil = Mathf.Clamp(postHitRecoil, 0f, maxOffset);

        float snapStart = peakTime - strikeTime;

        // With no wind-up there is nothing to coil back from, so the snap starts at
        // rest rather than at full coil — otherwise frame one shows the enemy jerking
        // backwards for no reason.
        bool hasWindup = coilEndTime > 0.0001f;
        float coilBack = hasWindup ? pullback : 0f;
        float coilSquash = hasWindup ? windupSquash : 0f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (logPhaseTransitions)
        {
            Phase p = t < coilEndTime ? Phase.Coil
                    : t < snapStart ? Phase.Hold
                    : t < peakTime ? Phase.Snap
                    : Phase.Settle;
            if (p != lastPhase)
            {
                lastPhase = p;
                bool inParryFrames = controller != null && controller.IsCurrentlyInParryFrames();
                Debug.Log($"[EnemyAttackLunge] {name}  {p.ToString().ToUpper(),-6} " +
                          $"at {t * 1000f:F0}ms   parryFrames={inParryFrames}", this);
            }
        }
#endif

        if (t < coilEndTime)
        {
            // COIL — wind back, squash, tremble harder as the '!' approaches.
            float u = Mathf.Clamp01(t / coilEndTime);
            float e = u * u * (3f - 2f * u);                  // smoothstep

            curForward = -coilBack * e;
            curDeform = -coilSquash * e;
            curShake = SampleShake(e);
        }
        else if (t < snapStart)
        {
            // HOLD — fully coiled, trembling at max, for exactly as long as the parry
            // window is open and the enemy has not yet committed. This is the pose the
            // '!' is drawn over, and the reason the telegraph reads as a threat rather
            // than as an icon that happens to be floating there.
            curForward = -coilBack;
            curDeform = -coilSquash;
            curShake = SampleShake(1f);
        }
        else if (t < peakTime)
        {
            // STRIKE — accelerate so the peak lands exactly on the hit frame.
            float u = strikeTime > 0.0001f ? Mathf.Clamp01((t - snapStart) / strikeTime) : 1f;
            float e = u * u;                                  // ease-in, reads as a snap

            curForward = Mathf.Lerp(-coilBack, lunge, e);
            curDeform = Mathf.Lerp(-coilSquash, strikeStretch, e);
            curShake = Vector2.zero;
        }
        else
        {
            // SETTLE — ease home, dip back once, and let the deform ring out.
            float tau = t - peakTime;
            float u = recoveryDuration > 0.0001f ? Mathf.Clamp01(tau / recoveryDuration) : 1f;
            float e = 1f - (1f - u) * (1f - u) * (1f - u);     // ease-out cubic

            // sin(pi*u) starts and ends at zero, so the recoil is a clean bump that
            // cannot leave a residual offset behind.
            curForward = Mathf.Lerp(lunge, 0f, e) - recoil * Mathf.Sin(Mathf.PI * u);

            float settle = Mathf.Lerp(strikeStretch, 0f, e);
            float wobble = wobbleAmplitude
                           * Mathf.Sin(tau * wobbleFrequency * 2f * Mathf.PI)
                           * Mathf.Exp(-tau * Mathf.Max(0.01f, wobbleDamping));

            curDeform = settle + wobble;
            curShake = Vector2.zero;
        }
    }

    private float MaxOffset()
    {
        float range = (controller != null) ? controller.AttackRange : 2f;
        return Mathf.Max(0.01f, range * maxOffsetFractionOfAttackRange);
    }

    private Vector2 SampleShake(float ramp)
    {
        if (shakeAmplitude <= 0.0001f) return Vector2.zero;

        float s = Time.time * shakeFrequency;
        float nx = (Mathf.PerlinNoise(noiseSeed, s) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(noiseSeed + 37.7f, s) - 0.5f) * 2f;
        return new Vector2(nx, ny) * (shakeAmplitude * ramp);
    }

    // ── Pose application / restoration 

    private void ApplyVisualPose()
    {
        Vector2 offset = affectPosition ? (attackDir * curForward + curShake) : Vector2.zero;
        float deform = affectScale ? curDeform : 0f;

        if (offset.sqrMagnitude <= 0.0000001f && Mathf.Abs(deform) <= 0.0001f)
        {
            RestoreTruePose();
            return;
        }

        // Deform along the ATTACK AXIS rather than blindly along X, so a bite aimed
        // upwards stretches vertically. attackDir is unit length, so |x| and |y| are
        // exactly how much of the motion sits on each axis.
        float hx = Mathf.Abs(attackDir.x);
        float hy = Mathf.Abs(attackDir.y);
        float vp = Mathf.Clamp01(volumePreservation);

        float mulX = 1f + deform * hx - deform * hy * vp;
        float mulY = 1f + deform * hy - deform * hx * vp;

        // A mistuned value must never invert or collapse the sprite.
        mulX = Mathf.Clamp(mulX, 0.4f, 1.8f);
        mulY = Mathf.Clamp(mulY, 0.4f, 1.8f);

        // Multiplying trueScale preserves its SIGN, so an enemy currently mirrored by
        // SmoothSpriteFlip stays mirrored and a flip mid-bite still tweens normally.
        transform.localScale = new Vector3(trueScale.x * mulX, trueScale.y * mulY, trueScale.z);
        transform.position = truePosition + (Vector3)offset;

        lastWrittenPosition = transform.position;
        lastWrittenScale = transform.localScale;
        isDisplaced = true;
    }

    private void RestoreTruePose()
    {
        if (!isDisplaced) return;

        // If anything wrote the transform AFTER we displaced it — a LateUpdate that
        // sorts later than ours, a physics step, a knockback — that write is the new
        // truth and must survive the restore, otherwise we would silently revert
        // another system's work once per frame.
        Vector3 pos = transform.position;
        if ((pos - lastWrittenPosition).sqrMagnitude > 1e-10f)
            truePosition += pos - lastWrittenPosition;   // external motion is a delta

        Vector3 scl = transform.localScale;
        if ((scl - lastWrittenScale).sqrMagnitude > 1e-8f)
            trueScale = scl;                             // scale writers set absolutes

        transform.position = truePosition;
        transform.localScale = trueScale;
        isDisplaced = false;
    }

    // ── Editor tooling 

    // Costs nothing in a build. Catches the configurations where the motion cannot
    // read as a telegraph, in the same spirit as WolfController.WarnIfParryWindowUnusable.
    private void WarnIfTimelineUnreadable()
    {
#if UNITY_EDITOR
        if (warnedAboutTiming || !alignWithParryWindow) return;

        if (parryOpenTime < 0f)
        {
            warnedAboutTiming = true;
            Debug.LogWarning($"[EnemyAttackLunge] {name}: no usable parry window, so " +
                "there is no wind-up to animate and the enemy telegraphs nothing the " +
                "player can answer. The bite falls back to a short snap-and-recover. " +
                "Check the timeline log above: 'anim … × 1 frames' means the attack " +
                "range itself is one frame long, and hit 0 / parry 0–0 means the frame " +
                "events were never set on the EnemyData.", this);
            return;
        }

        float hold = (peakTime - strikeTime) - coilEndTime;
        if (hold < 0.03f && parryOpenTime < peakTime)
        {
            warnedAboutTiming = true;
            Debug.LogWarning($"[EnemyAttackLunge] {name}: only " +
                $"{(peakTime - parryOpenTime) * 1000f:F0}ms between the parry window " +
                $"opening and the lunge peak, so the coiled pose is held for just " +
                $"{Mathf.Max(0f, hold) * 1000f:F0}ms before the snap. The snap has been " +
                "shortened to stay inside the window, but widen the gap between " +
                "parryFrameStart and the impact frame if the tell reads as too fast.", this);
        }
#endif
    }

    // Attack frames are numbered from the START OF THE ATTACK, but the sprites live in
    // one flat array indexed from zero. This is the translation between the two, and
    // it is the number you need to find the actual PNG.
    private static int GlobalSpriteIndex(EnemyData d, int attackRelativeFrame)
    {
        bool multiFolder = d.attackFrames != null && d.attackFrames.Length > 0;
        return multiFolder ? attackRelativeFrame : d.attack.startFrame + attackRelativeFrame;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // One line per displayed attack frame. The point of it is the middle column: it
    // puts the sprite index and the lunge phase on the same timeline, so "the mouth
    // closed while the body was still COILing" becomes a thing you can read rather
    // than something you have to catch by eye.
    private void LogAttackFrame(int frame)
    {
        if (!logAttackFrames) return;

        EnemyData data = stats != null ? stats.enemyData : null;
        if (data == null) return;

        float t = Time.time - cycleStartTime;
        float snapStart = peakTime - strikeTime;
        string phase = t < coilEndTime ? "COIL"
                     : t < snapStart ? "HOLD"
                     : t < peakTime ? "SNAP"
                     : "SETTLE";

        string marker = "";
        if (frame == data.hitFrame) marker += "  ← hitFrame: DAMAGE APPLIED";
        if (frame == data.parryFrameStart) marker += "  ← parry window opens ('!')";
        if (frame == data.parryFrameEnd) marker += "  ← parry window closes";

        Debug.Log($"[EnemyAttackLunge] {name}  attack frame {frame,2}  " +
                  $"= sprite #{GlobalSpriteIndex(data, frame),2}  " +
                  $"{t * 1000f,5:F0}ms  body={phase}{marker}", this);
    }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Called from ResolveTimeline, i.e. at the top of an attack cycle, once the frame
    // numbers are final. Deliberately not called from Start(): the Wolf's overrides
    // and the parry augment are only settled by the time it actually swings.
    private void MaybeLogTimeline(EnemyData data)
    {
        switch (timelineLogging)
        {
            case TimelineLogMode.Off:
                return;

            case TimelineLogMode.OncePerEnemyType:
                if (!LoggedEnemyTypes.Add(TypeKey())) return;
                break;

            case TimelineLogMode.OncePerInstance:
                if (loggedThisInstance) return;
                break;
        }

        loggedThisInstance = true;
        Debug.Log(BuildTimelineReport(data), this);
    }

    // Prefab name minus the "(Clone)" every spawned instance carries, so all twenty
    // slimes in a wave share one key.
    private string TypeKey()
    {
        string n = name;
        int idx = n.IndexOf("(Clone)");
        return idx >= 0 ? n.Substring(0, idx).Trim() : n;
    }
#endif

    private string BuildTimelineReport(EnemyData data)
    {
        float snapStart = peakTime - strikeTime;
        float hold = Mathf.Max(0f, snapStart - coilEndTime);

        string window = parryOpenTime >= 0f
            ? $"{parryOpenTime * 1000f:F0} → {parryCloseTime * 1000f:F0}ms"
            : "NONE — generic 0.2s fallback parry, no '!' is shown";

        // AttackCycle runs the whole attack animation and only THEN starts the
        // cooldown, so the real gap between two bites is the animation plus the
        // cooldown — not the cooldown alone.
        float cooldown = controller != null ? controller.GetAttackCooldown() : 0f;
        float biteSpacing = Mathf.Max(data.AttackDuration, cooldown) + cooldown;
        string spacingNote = (effectDuration > biteSpacing)
            ? "  ⚠ longer than the gap between bites — they will overlap their settle"
            : "";

        string peakLine = (Mathf.Abs(peakTime - hitTime) > 0.0005f || impactFrame != data.hitFrame)
            ? $"  LUNGE PEAK     {peakTime * 1000f:F0}ms = sprite " +
              $"#{GlobalSpriteIndex(data, impactFrame)} (attack frame {impactFrame})\n"
            : "";

        string alignNote = (parryOpenTime >= 0f && parryOpenTime < peakTime)
            ? "coil holds through it"
            : (parryOpenTime >= 0f ? "opens on/after the peak — coil runs straight to the snap"
                                   : "");

        return
            $"[EnemyAttackLunge] {name} — attack timeline, ms from cycle start\n" +
            BuildSpriteLayoutLines(data) +
            $"  anim           {data.AttackAnimSpeed * 1000f:F0}ms/frame × " +
            $"{data.attack.frameCount} frames = {data.AttackDuration * 1000f:F0}ms\n" +
            $"  frames         hit {data.hitFrame}, parry {data.parryFrameStart}–{data.parryFrameEnd}\n" +
            $"  parry window   {window}" + (alignNote.Length > 0 ? $"  ({alignNote})" : "") + "\n" +
            $"  COIL           0 → {coilEndTime * 1000f:F0}ms\n" +
            $"  HOLD           {coilEndTime * 1000f:F0} → {snapStart * 1000f:F0}ms ({hold * 1000f:F0}ms)\n" +
            $"  SNAP           {snapStart * 1000f:F0} → {peakTime * 1000f:F0}ms ({strikeTime * 1000f:F0}ms)\n" +
            peakLine +
            $"  DAMAGE         {hitTime * 1000f:F0}ms = sprite " +
            $"#{GlobalSpriteIndex(data, data.hitFrame)} (hitFrame {data.hitFrame})\n" +
            $"  settled by     {effectDuration * 1000f:F0}ms " +
            $"(next bite no sooner than {biteSpacing * 1000f:F0}ms){spacingNote}";
    }

    // The sprite bookkeeping that the timing numbers alone cannot show: how many
    // sprites actually exist, which slice of them each clip claims, and — the useful
    // one — which sprite index hitFrame resolves to, so it can be opened and looked at.
    private string BuildSpriteLayoutLines(EnemyData data)
    {
        bool multiFolder = data.attackFrames != null && data.attackFrames.Length > 0;
        int total = multiFolder
            ? data.attackFrames.Length
            : (data.frames != null ? data.frames.Length : 0);

        if (total <= 0) return "  sprites        NONE assigned\n";

        int last = total - 1;
        string overrun = "";

        if (!multiFolder)
        {
            int idleEnd = data.idle.startFrame + data.idle.frameCount - 1;
            int atkEnd = data.attack.startFrame + data.attack.frameCount - 1;
            int deathEnd = data.death.startFrame + data.death.frameCount - 1;

            if (idleEnd > last) overrun += $"  ⚠ idle overruns the array by {idleEnd - last}\n";
            if (atkEnd > last) overrun += $"  ⚠ attack overruns the array by {atkEnd - last}\n";
            if (deathEnd > last) overrun += $"  ⚠ death overruns the array by {deathEnd - last}\n";

            return
                $"  sprites        {total} total (#0–#{last})\n" +
                $"  clip ranges    idle #{data.idle.startFrame}–#{idleEnd}, " +
                $"attack #{data.attack.startFrame}–#{atkEnd}, " +
                $"death #{data.death.startFrame}–#{deathEnd}\n" +
                overrun +
                $"  hitFrame {data.hitFrame} = SPRITE #{GlobalSpriteIndex(data, data.hitFrame)}" +
                "  ← open this one; the jaws should be shut in it\n";
        }

        return
            $"  sprites        {total} attack frames (#0–#{last}), multi-folder mode\n" +
            $"  hitFrame {data.hitFrame} = SPRITE #{GlobalSpriteIndex(data, data.hitFrame)}" +
            "  ← open this one; the jaws should be shut in it\n";
    }

    // Same report on demand, from the component's ⋮ menu in the Inspector header.
    // Useful outside play mode, where no attack cycle ever runs.
    [ContextMenu("Log Resolved Attack Timeline")]
    private void LogResolvedTimeline()
    {
        if (stats == null) stats = GetComponent<EnemyStats>();
        if (controller == null) controller = GetComponent<EnemyController>();

        EnemyData data = stats != null ? stats.enemyData : null;
        if (data == null)
        {
            Debug.LogWarning($"[EnemyAttackLunge] {name}: no EnemyData assigned, so " +
                "there is no timeline to resolve.", this);
            return;
        }

        // ResolveTimeline would auto-log too; suppress that so the menu prints once.
        TimelineLogMode saved = timelineLogging;
        timelineLogging = TimelineLogMode.Off;
        ResolveTimeline();
        timelineLogging = saved;

        Debug.Log(BuildTimelineReport(data), this);
    }

    [ContextMenu("Apply Preset Values")]
    private void ApplyPresetValues()
    {
        switch (preset)
        {
            case Preset.Slime:
                // Heavy and gooey: a big coil, a modest lunge, lots of jiggle.
                windupPullback = 0.20f; lungeDistance = 0.26f; postHitRecoil = 0.07f;
                strikeDuration = 0.06f; recoveryDuration = 0.24f;
                windupSquash = 0.16f; strikeStretch = 0.20f; volumePreservation = 1f;
                shakeAmplitude = 0.030f; shakeFrequency = 22f;
                wobbleAmplitude = 0.09f; wobbleFrequency = 8f; wobbleDamping = 7f;
                telegraphLead = 0.03f;
                break;

            case Preset.SmallSlime:
                // Same material, less mass: quicker, tighter, springier.
                windupPullback = 0.14f; lungeDistance = 0.20f; postHitRecoil = 0.05f;
                strikeDuration = 0.05f; recoveryDuration = 0.19f;
                windupSquash = 0.14f; strikeStretch = 0.18f; volumePreservation = 1f;
                shakeAmplitude = 0.025f; shakeFrequency = 26f;
                wobbleAmplitude = 0.10f; wobbleFrequency = 10f; wobbleDamping = 8f;
                telegraphLead = 0.02f;
                break;

            case Preset.Wolf:
                // Predator: the longest coil and the sharpest snap, but it must read
                // as muscle, so the deform stays small and only partly volume-preserved.
                windupPullback = 0.26f; lungeDistance = 0.40f; postHitRecoil = 0.08f;
                strikeDuration = 0.055f; recoveryDuration = 0.20f;
                windupSquash = 0.10f; strikeStretch = 0.12f; volumePreservation = 0.65f;
                shakeAmplitude = 0.045f; shakeFrequency = 30f;
                wobbleAmplitude = 0.035f; wobbleFrequency = 11f; wobbleDamping = 12f;
                telegraphLead = 0.03f;
                break;
        }
    }
}


