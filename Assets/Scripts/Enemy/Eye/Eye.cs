using UnityEngine;
using System.Collections.Generic;
using FMODUnity;

// Eye enemy. Reuses:
//   - EnemyController (movement, targeting, attack cycle, parry window)
//   - EnemyAnimationController.OnAttackFrame (frame-perfect AOE timing)
//   - EnemyStats (health, damage flash, death animation, energy drop)
//   - ParryIndicator (the "!" appears automatically — it reads parry frames
//     from EnemyData and watches IsAttacking)
//   - EnemyDamageSystem.DamageTarget (the same damage path used elsewhere)

[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyController))]
public class Eye : MonoBehaviour
{
    [Header("AOE Attack")]

    [Tooltip("Wind-up in seconds between the eye STARTING its attack and the AOE actually landing. The ring " +
             "telegraphs and the chains spin up during this window, giving the player time to land a melee hit " +
             "and dodge out before damage applies. Damage is tested when the AOE FIRES, not when it was " +
             "scheduled, so dodging out of range during the wind-up avoids it. 0 = old behaviour (instant " +
             "damage on the first attack frame).")]
    [SerializeField] private float attackWindUp = 0.5f;

    [Tooltip("Draw the purple AOE telegraph circle around the eye. OFF by default: this ring always existed, " +
             "but it used to render under the generative grass so you never saw it - raising the eye cluster " +
             "above the grass made it appear as a big purple circle. Turn it ON if you want a visible tell for " +
             "the attack wind-up window.")]
    [SerializeField] private bool showAoeTelegraph = false;

    [Tooltip("Radius of the tentacle AOE around the eye, in world units.")]
    [SerializeField] private float aoeRadius = 2.0f;

    [Tooltip("If true, the AOE also damages towers and the core (anything " +
             "the eye's tentacles can physically reach). If false, only the " +
             "player is damaged. Default: true.")]
    [SerializeField] private bool aoeHitsBuildings = true;

    [Header("AOE Visual")]
    [Tooltip("Color of the AOE telegraph ring. Drawn during the parry/wind-up frames " +
             "so the player can see the danger area before the hit lands.")]
    [SerializeField] private Color telegraphColor = new Color(0.85f, 0.2f, 1f, 0.55f);

    [Tooltip("Color of the AOE strike flash on the hit frame.")]
    [SerializeField] private Color strikeColor = new Color(1f, 0.3f, 1f, 0.9f);

    [Tooltip("How long the strike flash lingers after the hit frame, in seconds.")]
    [SerializeField] private float strikeFlashDuration = 0.25f;

    [Header("Attack Dust")]
    [Tooltip("If true, the eye kicks up a small dust shockwave on the hit frame — " +
             "same style as the boss death dust, scaled down for a small enemy. " +
             "Reuses HammerSlamSystem.GetSoftDiscSprite() so the look matches.")]
    [SerializeField] private bool emitAttackDust = true;

    [Tooltip("How far the dust puffs travel from the eye, in world units. Kept tight so the " +
             "dust footprint matches the reach of the chains rather than blowing out to the AOE radius.")]
    [SerializeField] private float dustMaxRadius = 1.2f;

    [Tooltip("Number of dust puffs in the ring. More, smaller puffs read as grit/dust; " +
             "few big ones read as a puff of smoke.")]
    [SerializeField] private int dustPuffCount = 11;

    [Tooltip("Shape the dust ring to match the CHAIN orbit: same flat perspective ellipse and a footprint " +
             "sized to the chains rather than the big AOE circle. Requires an EyeChains component on the Eye. " +
             "When off (or no chains), falls back to dustMaxRadius / earthDustMaxRadius with a generic flat aspect.")]
    [SerializeField] private bool alignDustToChains = true;

    [Tooltip("Footprint size relative to the chain tip-ellipse radius. 1 = exactly the ellipse the chain ends " +
             "trace; slightly above lets the dust ring just past them. Only used when 'Align Dust To Chains' is on.")]
    [SerializeField] private float dustRadiusScale = 1.15f;

    [Range(0.1f, 1f)]
    [Tooltip("Perspective aspect (minor/major) used for the dust ellipse when NOT aligning to chains. " +
             "Lower = flatter / more top-down-isometric. Ignored when aligning (the chain orbit tilt is used).")]
    [SerializeField] private float dustAspectFallback = 0.5f;

    [Header("Ground Dust Sorting (above grass, below fog)")]
    [Tooltip("How many sorting orders BELOW the eye body the dust sits, when an EyeChains is present (the usual " +
             "case). The eye body is ~3000; 800 puts the dust at ~2200 - on the ground behind the chains, still " +
             "well ABOVE the generative grass (~1600) and far BELOW the fog (5000).")]
    [SerializeField] private int dustOrderBelowEye = 800;

    [Tooltip("Sort the ground effects using the SAME Y-sort formula the biome grass uses, instead of a fixed " +
             "order. The generative grass computes its order as (base + -y * precision), so a FIXED dust order " +
             "gets buried once the eye stands far enough down the map - the taller chains still show, which is " +
             "exactly the 'chains visible but no dust' symptom. With this on, the dust order tracks the eye's Y " +
             "so it is always the same distance above the local grass.")]
    [SerializeField] private bool dustYSortToBiome = true;

    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase (default 1000).")]
    [SerializeField] private int biomeSortBase = 1000;

    [Tooltip("Must match GrassCartoonOverlay.sortPrecision (default 10).")]
    [SerializeField] private float biomeSortPrecision = 10f;

    [Tooltip("How far ABOVE the local grass the ground effects sit. Raise if dust still hides in tall grass.")]
    [SerializeField] private int dustAboveGrassBias = 400;

    [Header("Debris")]
    [Tooltip("Throw solid debris chunks along with the dust. Chunks are opaque and tumble, so they read far " +
             "better against busy grass than soft translucent puffs do.")]
    [SerializeField] private bool emitDebris = true;

    [SerializeField] private int debrisCount = 10;

    [Tooltip("Debris chunk size in world units.")]
    [SerializeField] private float debrisSize = 0.09f;

    [SerializeField] private Color debrisColor = new Color(0.30f, 0.26f, 0.22f, 1f);

    [Tooltip("How high the chunks pop, in world units.")]
    [SerializeField] private float debrisPopHeight = 0.42f;

    [Tooltip("How far BELOW the chain tip-ellipse the ground dust sits, in world units (negative = lower). " +
             "Used when 'Align Dust To Chains' is on. The dust used to spawn at the eye's pivot, where it " +
             "overlapped the sprite; anchoring it to the chain tips and dropping it a little puts it on the " +
             "ground under the eye. Make this more negative to push the dust further down.")]
    [SerializeField] private float dustGroundDrop = -0.18f;

    [Tooltip("Extra drop of the EARTH (brown) dust below the primary dust layer, in world units. Keeps the " +
             "heavier ground layer slightly under the purple one so they read as two stacked strata.")]
    [SerializeField] private float earthDustExtraDrop = -0.12f;

    [Tooltip("Fallback dust layer, used only if there's no EyeChains to sync with. Must be the biome layer.")]
    [SerializeField] private string groundEffectSortingLayer = "Default";

    [Tooltip("Fallback dust order, used only if there's no EyeChains to sync with. Keep it between the grass " +
             "Y-sort peak (~1600) and the fog (5000).")]
    [SerializeField] private int groundEffectSortingOrder = 2200;

    [Tooltip("Color of the dust. A pale, desaturated lavender-grey - dusty rather than a saturated " +
             "purple cloud.")]
    [SerializeField] private Color dustColor = new Color(0.70f, 0.64f, 0.74f, 1f);

    [Tooltip("If true, spawns a second layer of earth-colored dust underneath the " +
             "primary dust. Reads as ground being kicked up by the tentacle strike — " +
             "complements the magical purple primary dust with something grounded.")]
    [SerializeField] private bool emitEarthDust = true;

    [Tooltip("Color of the earth dust layer. A pale, dry tan - kicked-up soil, not mud.")]
    [SerializeField] private Color earthDustColor = new Color(0.62f, 0.53f, 0.41f, 1f);

    [Tooltip("Number of earth-dust puffs in the lower ring. Usually a touch fewer " +
             "than the primary puffs so it looks like a separate layer, not a duplicate.")]
    [SerializeField] private int earthDustPuffCount = 8;

    [Tooltip("Radius the earth dust travels to, in world units. Kept shorter than " +
             "dustMaxRadius so the earth layer hugs the base of the chains.")]
    [SerializeField] private float earthDustMaxRadius = 0.85f;

    [Tooltip("Vertical offset for the EARTH dust origin — negative pulls the brown " +
             "layer below the pivot so it reads as ground dust rising up. " +
             "Independent of dustYOffset (which moves the primary purple dust).")]
    [SerializeField] private float earthDustYOffset = -0.5f;

    [Tooltip("Vertical offset for the PRIMARY (purple) dust origin in world units. " +
             "0 = dust spawns at the eye's pivot. Positive shifts the dust upward, " +
             "negative downward. Tune if the dust looks misaligned with the sprite.")]
    [SerializeField] private float dustYOffset = 0.0f;

    [Tooltip("Logs to Console every time the dust is spawned. Use to confirm the " +
             "AOE is actually firing if you can't see the dust on screen — if you " +
             "see the log but no dust, it's a sorting/scale problem; no log = " +
             "OnAttackFrame isn't reaching the hitFrame.")]
    [SerializeField] private bool debugLogs = false;

    // Cached refs
    private EnemyStats stats;
    private EnemyController controller;
    private EnemyAnimationController animController;

    // Frame config copied from EnemyData on Start. Kept here so we don't read
    // through EnemyData every OnAttackFrame call (which fires once per frame).
    private int hitFrame;
    private int parryFrameStart;
    private int parryFrameEnd;

    // Visual ring (created on demand the first time the eye telegraphs).
    private GameObject ringGO;
    private LineRenderer ringLR;
    private float ringFlashRemaining = 0f;
    private bool ringTelegraphing = false;

    // Wind-up: >0 means an AOE has been scheduled and is charging. The damage
    // test happens when it reaches 0, so the player can dodge out during it.
    private float pendingAOETimer = -1f;

    // Fallback path: when attack.frameCount == 0 the animation controller's
    // frame loop never runs and OnAttackFrame never fires. In that case we
    // watch EnemyController.IsAttacking and fire the AOE on the rising edge.
    // Set in Start(); checked in Update().
    private bool useTimerFallback = false;
    private bool wasAttackingLastFrame = false;

    // Continuous attack loop (EyeAttack). Started while the Eye is attacking and
    // stopped when it stops attacking or dies. The FMOD event should be authored
    // as a looping event; this just controls when it plays.
    private FMOD.Studio.EventInstance attackLoop;
    private bool attackLoopActive = false;

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        controller = GetComponent<EnemyController>();
        animController = GetComponent<EnemyAnimationController>();

        // ---- Take exclusive ownership of this enemy's damage -----------------
        // THE instant-damage bug: EnemyController runs its own melee attack cycle
        // in parallel with the Eye's AOE. Its EnemyData.hitFrame is 0, which sends
        // it down the "instant damage at animation start" branch, so it hit the
        // player the very frame they entered attackRange - completely bypassing
        // any wind-up configured here. That is why raising attackWindUp to 20 (or
        // anything else) changed nothing: the damage was never coming from Eye.cs.
        //
        // EnemyController.PerformHit() returns early when AttackHandlerOverride is
        // assigned, so assigning it suppresses the default melee damage outright.
        // Rather than an empty lambda (which would silently drop the Eye's damage
        // if the frame-event/timer paths ever failed to fire), we point the hook at
        // ScheduleAOE: the controller's hit moment now merely SCHEDULES the AOE,
        // which lands attackWindUp seconds later. ScheduleAOE is re-entrancy
        // guarded, so this coexists with the OnAttackFrame and timer paths without
        // double-firing. Result: exactly one damage source, always telegraphed.
        if (controller != null)
        {
            controller.AttackHandlerOverride = (target) => ScheduleAOE();
        }

        int atkCount = (stats != null && stats.enemyData != null) ? stats.enemyData.attack.frameCount : -1;

        // Validation
        bool framesConfigured = atkCount > 0;
        if (!framesConfigured)
        {
            Debug.LogWarning($"[Eye] EnemyData.attack.frameCount is {atkCount} on {gameObject.name}. " +
                             "Frame events will not fire, so the Eye will use a TIMER-based fallback " +
                             "to spawn the AOE/dust on each attack cycle. " +
                             "Once you add attack sprites, set attack.frameCount to the real number " +
                             "and the frame-event path will take over automatically.");
            useTimerFallback = true;
        }

        // Read frame config from the EnemyData asset (same source ParryIndicator
        // and EnemyController use). One source of truth.
        if (stats != null && stats.enemyData != null)
        {
            hitFrame = Mathf.Max(stats.enemyData.hitFrame, 0);
            parryFrameStart = Mathf.Max(stats.enemyData.parryFrameStart, 0);
            parryFrameEnd = Mathf.Max(stats.enemyData.parryFrameEnd, 0);
            if (parryFrameEnd < parryFrameStart) parryFrameEnd = parryFrameStart;

            // Degenerate case: with a single attack frame, every frame index is
            // both the "telegraph" frame and the "hit" frame. Showing the ring
            // is pointless (no wind-up to react to) and looks like a permanent
            // glow. Disable the telegraph until there's a real wind-up.
            // -1 = "no valid parry window" sentinel.
            if (stats.enemyData.attack.frameCount <= 1)
            {
                parryFrameStart = -1;
                parryFrameEnd = -1;
            }
        }

        // Subscribe to per-frame events from the animation controller. The
        // controller fires this once per frame during the attack animation,
        // and stops firing if the animation is interrupted (e.g. parry stun
        // or death) — which gives us free "abort on parry" behaviour.
        if (animController != null)
        {
            animController.OnAttackFrame += HandleAttackFrame;
        }
        else
        {
            Debug.LogError($"[Eye] No EnemyAnimationController on {gameObject.name} — frame events will not fire and no dust/AOE will trigger.");
        }

        BuildRing();
    }

    private void OnDestroy()
    {
        if (animController != null)
            animController.OnAttackFrame -= HandleAttackFrame;

        StopAttackLoop();
    }

    private void OnDisable()
    {
        // Dying/pooling disables the object — never leave the loop droning on.
        StopAttackLoop();

        // Cancel a charging AOE: if the player kills the eye during the wind-up,
        // the hit should not land. This is what makes the window a real trade.
        pendingAOETimer = -1f;
    }

    // Starts the loop on the rising edge of IsAttacking and stops it on the
    // falling edge. Also keeps the 3D position on the Eye while it plays.
    private void UpdateAttackLoop()
    {
        bool attackingNow = controller != null && controller.IsAttacking;

        if (attackingNow && !attackLoopActive) StartAttackLoop();
        else if (!attackingNow && attackLoopActive) StopAttackLoop();

        if (attackLoopActive && attackLoop.isValid())
            attackLoop.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
    }

    private void StartAttackLoop()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (FMODEvents.instance.eyeAttack.IsNull) return;

        attackLoop = FMODUnity.RuntimeManager.CreateInstance(FMODEvents.instance.eyeAttack);
        attackLoop.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
        attackLoop.start();
        attackLoopActive = true;
    }

    private void StopAttackLoop()
    {
        if (!attackLoopActive) return;
        attackLoopActive = false;

        if (attackLoop.isValid())
        {
            attackLoop.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            attackLoop.release();
        }
    }

    private void Update()
    {
        // Continuous attack sound: play while the Eye is attacking, stop the moment
        // it stops (or dies — OnDisable/OnDestroy also stop it). Runs on both the
        // frame-event and timer-fallback paths since it keys off IsAttacking.
        UpdateAttackLoop();

        // Wind-up charging. The AOE was scheduled but hasn't landed yet: pulse the
        // ring from telegraph colour to strike colour so the player can read how
        // much time is left, then fire. The damage test runs at FIRE time, so
        // stepping out of range during the wind-up genuinely avoids the hit.
        if (pendingAOETimer > 0f)
        {
            pendingAOETimer -= Time.deltaTime;
            if (pendingAOETimer <= 0f)
            {
                pendingAOETimer = -1f;
                FireAOE();
                ringTelegraphing = false;
                ringFlashRemaining = strikeFlashDuration;
                SetRingVisible(true);
                SetRingColor(strikeColor, 1f);
            }
            else
            {
                float charge = 1f - Mathf.Clamp01(pendingAOETimer / Mathf.Max(0.0001f, attackWindUp));
                SetRingColor(Color.Lerp(telegraphColor, strikeColor, charge),
                             Mathf.Lerp(0.55f, 1f, charge));
            }
        }

        // Linger flash after the hit, then return to invisible.
        if (ringFlashRemaining > 0f)
        {
            ringFlashRemaining -= Time.deltaTime;
            float t = Mathf.Clamp01(ringFlashRemaining / strikeFlashDuration);
            SetRingColor(Color.Lerp(telegraphColor, strikeColor, t), t);
            if (ringFlashRemaining <= 0f)
                SetRingVisible(false);
        }

        // Fallback path
        if (useTimerFallback && controller != null)
        {
            bool nowAttacking = controller.IsAttacking;
            if (nowAttacking && !wasAttackingLastFrame)
            {
                if (debugLogs) Debug.Log($"[Eye] Timer-fallback AOE scheduled on {gameObject.name}");
                ScheduleAOE();
            }
            wasAttackingLastFrame = nowAttacking;
        }
    }

    // Called every frame of the attack animation with the 0-based frame index
    // relative to the attack animation's start frame.
    private void HandleAttackFrame(int frameIndex)
    {
        if (debugLogs) Debug.Log($"[Eye] HandleAttackFrame({frameIndex}) — hitFrame={hitFrame}");
        // Telegraph the AOE during the parry window
        if (frameIndex >= parryFrameStart && frameIndex <= parryFrameEnd)
        {
            if (!ringTelegraphing)
            {
                ringTelegraphing = true;
                SetRingVisible(true);
                SetRingColor(telegraphColor, 1f);
            }
        }

        // Schedule the AOE on the configured hit frame - it lands attackWindUp
        // seconds later, giving the player a hit-and-dodge window.
        if (frameIndex == hitFrame)
        {
            ScheduleAOE();
        }
        else if (frameIndex > hitFrame && ringTelegraphing)
        {
            // Past the hit but still inside the attack animation — kill the
            // telegraph so it doesn't keep glowing on follow-through frames.
            ringTelegraphing = false;
            if (ringFlashRemaining <= 0f) SetRingVisible(false);
        }
    }

    // Begins the attack wind-up: shows the telegraph ring now, and lands the AOE
    // attackWindUp seconds later. Damage is resolved in FireAOE() at the END of
    // the window, so the player can trade a melee hit and dodge clear.
    private void ScheduleAOE()
    {
        // Already charging - don't restart or stack the window.
        if (pendingAOETimer > 0f) return;

        if (attackWindUp <= 0f)
        {
            // No wind-up configured: behave exactly as before.
            FireAOE();
            ringTelegraphing = false;
            ringFlashRemaining = strikeFlashDuration;
            SetRingVisible(true);
            SetRingColor(strikeColor, 1f);
            return;
        }

        pendingAOETimer = attackWindUp;
        ringTelegraphing = true;
        SetRingVisible(true);
        SetRingColor(telegraphColor, 0.55f);
        if (debugLogs) Debug.Log($"[Eye] AOE winding up for {attackWindUp}s on {gameObject.name}");
    }

    private void FireAOE()
    {
        // Spawn the dust ring 
        if (emitAttackDust)
            SpawnAttackDust();

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, aoeRadius);
        if (hits == null || hits.Length == 0) return;

        // Deduplicate: a tower might have multiple colliders. Track by GO.
        var hitGOs = new HashSet<GameObject>();

        float damage = stats != null ? stats.Damage : 0f;
        if (damage <= 0f) return;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            // Skip self.
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            // Skip other enemies (the eye shouldn't kill its allies with AOE).
            var otherEnemy = col.GetComponentInParent<EnemyStats>();
            if (otherEnemy != null) continue;

            GameObject targetGO = ResolveDamageTarget(col);
            if (targetGO == null) continue;
            if (!hitGOs.Add(targetGO)) continue; // already hit this GO this pulse

            // Player: route through EnemyDamageSystem so shield-block helper
            // gets a chance to intercept (same path used by everything else).
            if (targetGO.CompareTag("Player"))
            {

                if (useTimerFallback) continue;

                if (EnemyDamageSystem.Instance != null)
                    EnemyDamageSystem.Instance.DamageTarget(targetGO, damage, gameObject);
                else
                {
                    var cs = targetGO.GetComponent<CharacterStats>();
                    if (cs != null) cs.TakeDamage(damage);
                }
                continue;
            }

            // Buildings (towers / core). Optional.
            if (!aoeHitsBuildings) continue;

            // Energy consumers (Core, towers that implement IEnergyConsumer)
            var consumer = targetGO.GetComponent<IEnergyConsumer>();
            if (consumer != null)
            {
                if (EnergyManager.Instance != null)
                    EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
                continue;
            }

            // Anything else with a CharacterStats (e.g. a destructible prop)
            var stats2 = targetGO.GetComponent<CharacterStats>();
            if (stats2 != null)
                stats2.TakeDamage(damage);
        }
    }

    // Picks the right GameObject to damage for a given collider.
    private static GameObject ResolveDamageTarget(Collider2D col)
    {
        if (col == null) return null;

        // Walk up to find a tagged root or a CharacterStats / IEnergyConsumer holder.
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

    // VISUAL RING
    private void BuildRing()
    {
        ringGO = new GameObject("EyeAOERing");
        ringGO.transform.SetParent(transform, false);
        ringGO.transform.localPosition = Vector3.zero;

        ringLR = ringGO.AddComponent<LineRenderer>();
        ringLR.useWorldSpace = false;
        ringLR.loop = true;
        ringLR.positionCount = 48;
        ringLR.startWidth = 0.08f;
        ringLR.endWidth = 0.08f;
        ringLR.numCornerVertices = 2;
        ringLR.sortingLayerName = "Default";
        // Into the grass<->fog band (like the dust), a little ABOVE the dust so the
        // telegraph reads over it, and below the eye body. At order 110 it sat under
        // the generative grass (~1600) and was invisible.
        var chainsForRing = GetComponent<EyeChains>();
        ringLR.sortingOrder = chainsForRing != null
            ? chainsForRing.EyeBodyOrder - dustOrderBelowEye + 60
            : groundEffectSortingOrder + 60;

        // Use a basic unlit material so vertex colors show through.
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        var mat = new Material(sh);
        mat.mainTexture = Texture2D.whiteTexture;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        ringLR.material = mat;

        // Bake the circle once 
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.001f);
        float localRadius = aoeRadius / s;
        for (int i = 0; i < ringLR.positionCount; i++)
        {
            float a = (i / (float)ringLR.positionCount) * Mathf.PI * 2f;
            ringLR.SetPosition(i, new Vector3(Mathf.Cos(a) * localRadius, Mathf.Sin(a) * localRadius, 0f));
        }

        SetRingVisible(false);
    }

    private void SetRingVisible(bool visible)
    {
        // The telegraph ring is opt-in. It used to sit at sorting order 110, buried
        // under the generative grass (~1600), so it was effectively invisible; once
        // the cluster moved into the grass<->fog band it became a big purple circle
        // on screen. Off by default - flip showAoeTelegraph on if you want a
        // readable tell for the wind-up window.
        if (!showAoeTelegraph) visible = false;
        if (ringLR != null) ringLR.enabled = visible;
    }

    private void SetRingColor(Color c, float intensity)
    {
        if (ringLR == null) return;
        Color a = c;
        Color b = c; b.a *= 0.3f;
        a.a *= intensity;
        b.a *= intensity;
        ringLR.startColor = a;
        ringLR.endColor = b;
    }

    // ATTACK DUST
    private void SpawnAttackDust()
    {
        Sprite puffSprite = GetSoftDiscSprite();
        if (puffSprite == null)
        {
            if (debugLogs) Debug.LogWarning("[Eye] Dust skipped — soft disc sprite was null.");
            return;
        }

        if (debugLogs) Debug.Log($"[Eye] Spawning attack dust at {transform.position} (puffs={dustPuffCount}, radius={dustMaxRadius})");

        // Sorting. The biome's generative grass Y-sorts (order = base + -y*precision),
        // so a FIXED order works at one height and gets buried at another. Match the
        // formula and sit a fixed bias above the local grass instead. Clamped below
        // the fog (5000) so the dust can never punch through it.
        var chainsForSort = GetComponent<EyeChains>();
        string sortLayerName = chainsForSort != null
            ? chainsForSort.ClusterSortingLayer
            : (string.IsNullOrEmpty(groundEffectSortingLayer) ? "Default" : groundEffectSortingLayer);

        int sortOrder;
        if (dustYSortToBiome)
        {
            int grassHere = biomeSortBase + Mathf.RoundToInt(-transform.position.y * biomeSortPrecision);
            sortOrder = Mathf.Clamp(grassHere + dustAboveGrassBias, biomeSortBase, 4900);
        }
        else if (chainsForSort != null)
        {
            sortOrder = chainsForSort.EyeBodyOrder - dustOrderBelowEye;
        }
        else
        {
            sortOrder = groundEffectSortingOrder;
        }

        if (debugLogs)
            Debug.Log($"[Eye] Dust sortLayer={sortLayerName} order={sortOrder} " +
                      $"(eye.y={transform.position.y:F2}, localGrass≈{biomeSortBase + Mathf.RoundToInt(-transform.position.y * biomeSortPrecision)})");

        // World-space root so the dust doesn't follow the eye after launch.
        GameObject root = new GameObject("EyeAttackDust");
        root.transform.position = transform.position; // root is at pivot; child offsets handle layer Y shifts

        // Host the dust animation ON the root itself, not on the Eye. Coroutines
        // started on the Eye are killed the instant the Eye's GameObject is
        // destroyed — so if the Eye died mid-attack, the puff coroutines froze
        // and DestroyAfter never ran, orphaning this root as a permanent smear
        // of dust on the ground. Running everything on the root makes the effect
        // finish and clean itself up regardless of the Eye's lifetime.
        var host = root.AddComponent<EyeAttackDustRunner>();

        // Ellipse footprint. When aligning, pull the long-axis radius and the
        // perspective aspect straight off the chain orbit so the ground dust
        // rings the base of the chains in the same tilted ellipse they orbit in
        // (instead of a wide flat circle). radiusY = radiusX * aspect.
        var chains = GetComponent<EyeChains>();
        bool aligned = alignDustToChains && chains != null;

        float aspect = aligned ? Mathf.Clamp(chains.OrbitTilt, 0.1f, 1f) : dustAspectFallback;

        float primaryRX = aligned ? chains.OrbitRadiusXWorld * dustRadiusScale : dustMaxRadius;
        float primaryRY = primaryRX * aspect;

        // Earth layer hugs a touch tighter than the primary, as before.
        float earthRX = aligned ? primaryRX * 0.7f : earthDustMaxRadius;
        float earthRY = earthRX * aspect;

        // Vertical + horizontal anchor. When aligned, sit the dust on the chain
        // TIP ellipse - the actual ground level where the chains end - rather
        // than at the eye pivot, where it overlapped the sprite. OrbitCenterWorld
        // is already flip-corrected, so this also keeps the dust centred under the
        // eye when the sprite mirrors to face the player.
        float primaryY = dustYOffset;
        float earthY = earthDustYOffset;
        if (aligned)
        {
            Vector3 ringCenter = chains.OrbitCenterWorld;
            root.transform.position = new Vector3(ringCenter.x, transform.position.y, transform.position.z);

            // Layer offsets are relative to the root (which is at the eye's Y).
            float ringY = ringCenter.y - transform.position.y;   // ~ the tip-ellipse centre
            primaryY = ringY + dustGroundDrop;
            earthY = primaryY + earthDustExtraDrop;
        }

        // PRIMARY DUST LAYER 
        SpawnDustLayer(host, root.transform, puffSprite, sortLayerName, sortOrder,
                       dustPuffCount, primaryRX, primaryRY, dustColor, primaryY,
                       baseSortOffset: 24);

        // EARTH DUST LAYER
        if (emitEarthDust)
        {
            SpawnDustLayer(host, root.transform, puffSprite, sortLayerName, sortOrder,
                           earthDustPuffCount, earthRX, earthRY, earthDustColor, earthY,
                           baseSortOffset: 22);
        }

        // DEBRIS — opaque tumbling chunks. These are the part that actually reads
        // against dense grass; soft translucent puffs get lost in it.
        if (emitDebris)
        {
            int chunks = Mathf.Max(1, debrisCount);
            for (int i = 0; i < chunks; i++)
            {
                float ang = (i / (float)chunks) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                host.StartCoroutine(EyeDebrisChunk(root.transform, sortLayerName,
                                                   sortOrder + 30, ang, primaryRX, primaryRY, primaryY));
            }
        }

        // Tear the root down after the longest-lived puff has finished. This is
        // an engine-scheduled destroy (not a coroutine on the Eye), so it fires
        // even if the Eye is destroyed the same frame the dust spawns.
        Object.Destroy(root, 1.6f);   // longest puff is ~1.05s + fade headroom
    }

    // Spawns one dust layer. radiusX/radiusY are the ellipse's half-axes in world
    // units (radiusY < radiusX for the flat perspective footprint).
    private void SpawnDustLayer(
        MonoBehaviour host,
        Transform parent, Sprite puffSprite, string sortLayerName, int sortOrderBase,
        int puffCount, float radiusX, float radiusY, Color color, float yOffset, int baseSortOffset)
    {
        // Layer host so each layer's puffs/disc share their own Y offset
        // without polluting the root transform.
        var layer = new GameObject("DustLayer");
        layer.transform.SetParent(parent, false);
        layer.transform.localPosition = new Vector3(0f, yOffset, 0f);

        // Soft ground-hugging disc. Driven by `host` (the dust root) so it keeps
        // animating after the Eye is gone.
        host.StartCoroutine(EyeDustDisc(layer.transform, puffSprite, sortLayerName,
                                   sortOrderBase + baseSortOffset, radiusX, radiusY, color));

        int puffs = Mathf.Max(1, puffCount);
        for (int i = 0; i < puffs; i++)
        {
            float ang = (i / (float)puffs) * Mathf.PI * 2f + Random.Range(-0.12f, 0.12f);
            host.StartCoroutine(EyeDustPuff(layer.transform, puffSprite, sortLayerName,
                                       sortOrderBase + baseSortOffset + 1, i, ang,
                                       radiusX, radiusY, color));
        }
    }

    // A solid chunk kicked out along the ground ellipse, arcing up and falling
    // back with tumble. Opaque, so it stays readable over busy biome grass.
    private System.Collections.IEnumerator EyeDebrisChunk(
        Transform parent, string sortLayerName, int sortOrder, float angle,
        float radiusX, float radiusY, float baseY)
    {
        if (parent == null) yield break;

        var go = new GameObject("DebrisChunk");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetChunkSprite();
        sr.sortingLayerName = sortLayerName;
        sr.sortingOrder = sortOrder;

        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float endFrac = Random.Range(0.65f, 1.15f);
        float life = Random.Range(0.45f, 0.75f);
        float pop = debrisPopHeight * Random.Range(0.6f, 1.3f);
        float spin = Random.Range(-540f, 540f);
        float size = debrisSize * Random.Range(0.6f, 1.4f);

        // Slight per-chunk colour variation so they don't read as clones.
        float v = Random.Range(0.8f, 1.25f);
        Color c = new Color(debrisColor.r * v, debrisColor.g * v, debrisColor.b * v, 1f);

        float e = 0f;
        while (e < life && go != null)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / life);

            float travel = 1f - (1f - t) * (1f - t);         // ease-out outward
            float frac = endFrac * travel;
            float arc = Mathf.Sin(t * Mathf.PI) * pop;        // up then back down

            go.transform.localPosition = new Vector3(dir.x * radiusX * frac,
                                                     baseY + dir.y * radiusY * frac + arc, 0f);
            go.transform.localScale = Vector3.one * size;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spin * t);

            // Hold full opacity, then fade only at the very end as it settles.
            c.a = t < 0.75f ? 1f : Mathf.InverseLerp(1f, 0.75f, t);
            sr.color = c;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private static Sprite _chunkSprite;

    // Small irregular solid chunk (a blunt shard), 16x16.
    private static Sprite GetChunkSprite()
    {
        if (_chunkSprite != null) return _chunkSprite;

        const int S = 16;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);

        // Rough blob: a filled ellipse with a couple of bitten-off corners so the
        // silhouette reads as a broken chip rather than a dot.
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S * 0.5f) / (S * 0.42f);
                float dy = (y - S * 0.5f) / (S * 0.34f);
                if (dx * dx + dy * dy > 1f) continue;
                if (x + y < 5) continue;              // chip one corner
                if (x - y > 9) continue;              // and another
                // Slight top highlight so the tumble is readable.
                float shade = y > S * 0.55f ? 1f : 0.72f;
                px[y * S + x] = new Color(shade, shade, shade, 1f);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _chunkSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _chunkSprite;
    }

    private System.Collections.IEnumerator EyeDustPuff(
        Transform parent, Sprite puffSprite, string sortLayerName, int sortOrder, int index, float angle,
        float radiusX, float radiusY, Color color)
    {
        if (parent == null) yield break;

        var go = new GameObject("DustPuff");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = puffSprite;
        sr.sortingLayerName = sortLayerName;
        sr.sortingOrder = sortOrder + (index % 5);

        // Travel outward along the ELLIPSE, not a circle: the vertical reach is
        // squashed by radiusY so the whole ring lies flat on the ground in the
        // tilted view and matches the chain orbit's perspective.
        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        // Fraction of the way to the ellipse edge this mote travels.
        float startFrac = Random.Range(0.10f, 0.22f);
        float endFrac = Random.Range(0.80f, 1.05f);
        float life = Random.Range(0.75f, 1.05f);
        // Puff sizes scale with the long axis so the grit stays readable at any
        // footprint size (bigger ring => bigger motes, and vice-versa).
        float startSize = radiusX * Random.Range(0.22f, 0.34f);
        float endSize = radiusX * Random.Range(0.42f, 0.66f);
        float spin = Random.Range(-150f, 150f);
        // Each mote is kicked up and arcs back down a little - reads as dry dust.
        // Tied to the SHORT axis so the lift stays subtle in the flat footprint.
        float arcHeight = radiusY * Random.Range(0.35f, 0.75f);

        float e = 0f;
        while (e < life && go != null)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / life);
            // Ease-out travel: fast launch, decelerating like real dust.
            float travel = 1f - (1f - t) * (1f - t);
            float frac = Mathf.Lerp(startFrac, endFrac, travel);
            // Parabolic lift: up fast, settle back down.
            float lift = Mathf.Sin(t * Mathf.PI) * arcHeight;
            go.transform.localPosition = new Vector3(dir.x * radiusX * frac,
                                                     dir.y * radiusY * frac + lift, 0f);
            // Grow while rising, then shrink a touch as it settles.
            float sizeT = Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI * 0.5f);
            go.transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, sizeT);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spin * t);

            Color c = color;
            // Quick pop-in, then a smooth settle-out so it doesn't linger as a haze.
            float fadeIn = Mathf.Clamp01(t / 0.12f);
            c.a = Mathf.Lerp(0.95f, 0f, t) * fadeIn;
            sr.color = c;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private System.Collections.IEnumerator EyeDustDisc(
        Transform parent, Sprite puffSprite, string sortLayerName, int sortOrder,
        float radiusX, float radiusY, Color color)
    {
        if (parent == null) yield break;

        var go = new GameObject("DustDisc");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = puffSprite;
        sr.sortingLayerName = sortLayerName;
        sr.sortingOrder = sortOrder;

        // Squashed to the SAME aspect as the chain orbit ellipse (radiusY/radiusX),
        // so it reads as a flat patch of dust on the ground under the tilted ring
        // rather than a round ball of gas hanging in the air.
        float aspect = radiusX > 1e-4f ? radiusY / radiusX : 0.5f;
        float life = 0.42f;
        float e = 0f;
        while (e < life && go != null)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float eased = 1f - (1f - p) * (1f - p);
            // Tight footprint that only just reaches the dust radius.
            float sx = Mathf.Lerp(radiusX * 0.5f, radiusX * 1.25f, eased);
            go.transform.localScale = new Vector3(sx, sx * aspect, 1f);
            Color c = color;
            // Low, brief - a subtle ground bloom, not a lingering haze.
            c.a = Mathf.Lerp(0.45f, 0f, eased);
            sr.color = c;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // Procedural soft circle for dust puffs
    private static Sprite _softDiscSprite;
    private static Sprite GetSoftDiscSprite()
    {
        if (_softDiscSprite != null) return _softDiscSprite;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[S * S];
        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);
        float maxD = S * 0.5f;

        // Random offsets so the two Perlin lookups (ragged rim + internal mottle)
        // don't line up into an obvious pattern.
        float no = Random.Range(0f, 100f);
        float mo = Random.Range(0f, 100f);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxD;   // 0 centre .. 1 edge
                float ang = Mathf.Atan2(dy, dx);

                // Ragged rim: perturb the fade radius with low-frequency angular noise,
                // so the puff has a torn, cloudy silhouette instead of a clean circle.
                float rim = 0.70f + 0.26f * Mathf.PerlinNoise(
                    Mathf.Cos(ang) * 1.7f + no, Mathf.Sin(ang) * 1.7f + no);

                // Core falloff up to that ragged rim.
                float a = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, rim));
                a = a * a;   // soft but not billowy

                // Internal mottle so the body looks like clumped grit, not smooth gas.
                float mottle = 0.6f + 0.4f * Mathf.PerlinNoise(x * 0.22f + mo, y * 0.22f + mo);
                a *= mottle;

                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _softDiscSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
                                        new Vector2(0.5f, 0.5f), S);
        return _softDiscSprite;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.2f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, aoeRadius);
    }
}

// Lightweight coroutine host that lives on the world-space attack-dust root.
// The dust animation and cleanup run here — independent of the Eye — so killing
// the Eye mid-attack can no longer freeze puffs or leave dust stuck on the
// ground. Added at runtime via AddComponent; it needs no state of its own.
public sealed class EyeAttackDustRunner : MonoBehaviour { }


