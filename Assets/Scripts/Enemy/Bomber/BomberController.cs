using UnityEngine;
using System.Collections;
using System.Collections.Generic;


// Bomber — enemy that completely ignores the player and walks straight
// toward the nearest tower or the central core
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(Rigidbody2D))]
public class BomberController : MonoBehaviour
{
    [Header("Procedural Sprite")]
    [Tooltip("Generate the Bomber's look in code (a serious organic spiked monster " +
             "ball) instead of loading a PNG sheet from Resources. Lets you delete " +
             "the old placeholder 00.png. Turn OFF to fall back to the old PNG pipeline.")]
    [SerializeField] private bool useProceduralSprite = true;

    [Tooltip("World-space height (units) of the generated sprite. Purely cosmetic — " +
             "the collider is a separate component, so fuse range / explosion radius " +
             "are unchanged. Default is large (a hulking ball); lower it to taste.")]
    [SerializeField] private float proceduralSpriteWorldSize = 4.8f;

    [Tooltip("Disintegration VFX duration when the Bomber is KILLED (by towers/player) " +
             "before it can detonate. 1.0+ uses the full sprite-shatter shown for bosses. " +
             "Set 0 to disable. Does NOT affect the detonation path, which keeps its own " +
             "explosion VFX.")]
    [SerializeField] private float deathDisintegrationDuration = 1.2f;

    [Tooltip("Spin the ball about its centre as it travels, like a rolling boulder. " +
             "Only applies to the procedural sprite; it stops while the fuse is armed. " +
             "Purely visual — never touches physics, the collider, or the health bar.")]
    [SerializeField] private bool rollWhenMoving = true;

    [Tooltip("Roll speed multiplier. 1 = physically-correct rolling for the sprite's " +
             "size (fairly slow for a big ball); raise it for a livelier tumble.")]
    [SerializeField] private float rollSpeedScale = 1f;

    [Header("Pulsate")]
    [Tooltip("Subtle 'breathing' scale pulse so the ball looks alive. Visual only — " +
             "the collider radius used for arming is cached at Start, so fuse range " +
             "stays put. Only applies to the procedural sprite.")]
    [SerializeField] private bool pulsate = true;

    [Tooltip("Pulse depth as a fraction of size (0.06 = ±6%).")]
    [SerializeField] private float pulseAmplitude = 0.06f;

    [Tooltip("Pulse speed in cycles per second.")]
    [SerializeField] private float pulseSpeed = 1.05f;

    [Header("Targeting")]
    [Tooltip("How often (seconds) the Bomber re-evaluates its target.")]
    [SerializeField] private float targetUpdateInterval = 0.5f;

    [Header("Explosion")]
    [Tooltip("Edge-to-edge GAP (world units) between the Bomber's COLLIDER and the " +
             "target's collider at which the fuse starts — this is literally how far " +
             "apart the two bodies are when it plants, so smaller = it hugs the target " +
             "before arming. Size-independent (works the same vs a small tower and the " +
             "large core). Lower it toward 0 for near-contact; raise it to arm early.")]
    [SerializeField] private float fuseStartRange = 0.15f;

    [Tooltip("Seconds from fuse start to detonation.")]
    [SerializeField] private float fuseTime = 3f;

    [Tooltip("World-unit radius of the explosion hit.")]
    [SerializeField] private float explosionRadius = 2.5f;

    [Tooltip("Layer mask for explosion targets. Default: everything except the Enemy layer.")]
    [SerializeField] private LayerMask explosionLayers;

    [Header("Blink")]
    [Tooltip("Color to flash between the normal color and during countdown.")]
    [SerializeField] private Color blinkColor = new Color(2f, 0.2f, 0.2f, 1f);

    [Tooltip("Blink interval at the start of the fuse (seconds between flashes). Speeds up over time.")]
    [SerializeField] private float blinkIntervalStart = 0.5f;

    [Tooltip("Blink interval when about to explode.")]
    [SerializeField] private float blinkIntervalEnd = 0.07f;

    [Header("Fuse Warning Sound")]
    [Tooltip("Play FMODEvents.bombWarning (the 'BombWarning' event) for the whole fuse " +
             "window — it starts when the Bomber plants itself and arms, and is cut the " +
             "instant it detonates so the explosion one-shot lands on silence rather than " +
             "on top of a still-ticking warning. The audio counterpart of the blink, so " +
             "the threat is readable off-screen too. Needs a loop region in FMOD Studio: " +
             "the fuse can also be CANCELLED (target died / walked away), in which case " +
             "the sound fades out instead.")]
    [SerializeField] private bool playFuseWarningSound = true;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float avoidDistance = 1f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Stuck Prevention")]
    [SerializeField] private float stuckCheckTime = 0.5f;
    [SerializeField] private float minMovementThreshold = 0.05f;

    private EnemyStats stats;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private EnemyAnimationController animController;
    private SmoothSpriteFlip smoothFlip;

    private Transform coreTarget;
    private Transform currentTarget;

    private bool isFuseActive = false;
    private bool hasExploded = false;

    // Held instance for the fuse warning. Its lifetime is exactly the fuse's:
    // started in FuseRoutine, stopped in Explode (hard cut) or RestoreFromFuse
    // (fade, the fuse was cancelled), plus the disable/destroy safety nets.
    private readonly SpatialLoopSfx fuseWarningSfx = new SpatialLoopSfx("Bomber fuse warning");

    // Fuse blink state — driven from LateUpdate so the blink color is the
    // LAST writer each frame and can't be overwritten by SmoothSpriteFlip's
    // rim flash or EnemyStats' damage flash.
    private float fuseStartTime = 0f;
    private Color fuseBaseColor = Color.white;

    // The sprite's resting tint, captured once in Start() before anything can
    // recolour it. Unfreeze() restores THIS rather than a hardcoded white, so a
    // tinted Bomber prefab isn't bleached the first time it is frozen.
    private Color restingColor = Color.white;
    private bool fuseCancelled = false;
    private float bomberBodyRadius = 0f;

    // Rolling-ball spin. Accumulated Z angle (degrees) driven by horizontal travel.
    // Applied to transform.rotation, which the animation controller has been told
    // to leave alone (SetOrientationDrivingEnabled(false)), so nothing fights it.
    private float rollAngle = 0f;
    private float lastRollSign = 1f; // remembered horizontal heading for spin direction
    private Vector3 baseScale = Vector3.one; // captured at Start; the pulse multiplies this

    // Combined avoidance mask: walls/buildings (obstacleLayer) PLUS towers.

    private LayerMask avoidMask;

    // Remembers which side we last steered around a ROUND blocker (+1 left /
    // -1 right / 0 none) so the choice doesn't flip frame-to-frame — that flip
    // is the visible "jiggle" when sliding past a tower.
    private int lastAvoidSign = 0;


    private Vector2 lastStuckDir = Vector2.zero;

    // Stuck detection (mirrors EnemyController logic)
    private Vector2 lastKnownPosition;
    private float timeSinceLastMovement = 0f;
    private bool isInStuckMode = false;
    private float stuckModeTimer = 0f;
    private Vector2 stuckAvoidanceDirection;
    private float smokeShufflePhase;

    // Freeze / knockback (thin re-implementation — Bomber still respects crowd-control)
    private bool isFrozen = false;
    private float freezeTimeRemaining = 0f;
    private bool isKnockedBack = false;
    private float knockbackTimer = 0f;
    private Vector2 knockbackVelocity;

    // Procedural-sprite + death-VFX wiring. Runs after EnemyStats.Awake (see the
    // DefaultExecutionOrder above) but before every component's Start(), so the
    // sprite is in place and the animation controller has nothing to load.
    private void Awake()
    {
        if (!useProceduralSprite) return;

        var st = GetComponent<EnemyStats>();
        var sr = GetComponent<SpriteRenderer>();
        var anim = GetComponent<EnemyAnimationController>();

        // Build (or reuse) the shared procedural monster-ball sprite. It carries a
        // READABLE texture, which is exactly what EnemyDeathVFX needs to shatter
        // it into chunks — no PNG, no Read/Write import flag, no source path.
        float ppu = BomberSprite.SIZE / Mathf.Max(0.1f, proceduralSpriteWorldSize);
        Sprite bomberSprite = BomberSprite.Get(ppu);

        if (sr != null && bomberSprite != null)
            sr.sprite = bomberSprite;

        // Point the (already-cloned) EnemyData away from the deleted PNG folder and
        // collapse every animation range onto the single procedural frame. This
        // stops the animation controller from logging "0 sprites" errors and makes
        // it impossible to index past our one sprite. Scoped to this clone only.
        if (st != null && st.enemyData != null)
        {
            st.enemyData.spriteFolderPath = string.Empty;
            st.enemyData.idle = new AnimationFrameRange(0, 1);
            st.enemyData.attack = new AnimationFrameRange(0, 1);
            st.enemyData.death = new AnimationFrameRange(0, 1);
        }

        // Hand the sprite to the animation controller so its state machine still
        // runs off code, not a PNG sheet. (No-op-safe if there's no controller.)
        if (anim != null && bomberSprite != null)
        {
            anim.SetSpritesDirectly(new[] { bomberSprite });

            // The monster ball ROLLS instead of leaning — take rotation away from
            // the controller so its walk-lean can't fight our spin below.
            anim.SetOrientationDrivingEnabled(false);
        }

        // Disintegrate when killed by towers/player before detonating. The
        // detonation path (PerformExplosionDeath) is untouched and still plays
        // its own meteor explosion VFX.
        if (st != null && deathDisintegrationDuration > 0f)
            st.ConfigureDeathVfx(deathDisintegrationDuration, destroyHealthBarBeforeVfx: true);
    }

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animController = GetComponent<EnemyAnimationController>();
        smoothFlip = GetComponent<SmoothSpriteFlip>();

        // Cache the resting scale — the pulse multiplies it — and disable the
        // left/right flip for the procedural ball: it's radially symmetric, so a
        // flip is invisible anyway and would only fight the pulse's localScale.
        baseScale = transform.localScale;
        if (spriteRenderer != null) restingColor = spriteRenderer.color;
        if (useProceduralSprite && smoothFlip != null)
            smoothFlip.enabled = false;

        // Cache the Bomber's own collider radius so arming uses edge-to-edge
        // gap (body-to-body), which is independent of target size.
        var ownCol = GetComponent<Collider2D>();
        if (ownCol != null)
        {
            Vector3 ext = ownCol.bounds.extents; // world-space, already scaled
            bomberBodyRadius = Mathf.Min(ext.x, ext.y);
        }

        avoidMask = obstacleLayer;
        int towerLayer = LayerMask.NameToLayer("Tower");
        if (towerLayer >= 0) avoidMask |= (1 << towerLayer);


        if (animController != null)
            animController.SetAutoAttackDetectionEnabled(false);

        // Y-sort so the Bomber layers correctly with the rest of the map.
        if (GetComponent<YSortEntity>() == null)
        {
            var ys = gameObject.AddComponent<YSortEntity>();
            ys.sortPrecision = 10f;
            ys.sortOrderBase = 1000;
            ys.sortYOffset = -0.2f;
        }

        // Default explosion mask: everything except the Enemy layer.
        if (explosionLayers.value == 0)
            explosionLayers = ~LayerMask.GetMask("Enemy");

        // Find the core once and cache it.
        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null) coreTarget = core.transform;

        currentTarget = coreTarget;
        lastKnownPosition = transform.position;
        smokeShufflePhase = SmokeBlind.NewPhase();

        InvokeRepeating(nameof(RefreshTarget), 0f, targetUpdateInterval);
    }

    private void Update()
    {
        // Respect freeze debuff timer.
        if (isFrozen)
        {
            freezeTimeRemaining -= Time.deltaTime;
            if (freezeTimeRemaining <= 0f) Unfreeze();
        }

        // BUGFIX: the knockback timer used to be decremented HERE as well as in
        // FixedUpdate, so it drained at ~2x real time and a 0.25s knockback lasted
        // ~0.125s (drifting with framerate vs fixed timestep). FixedUpdate owns the
        // knockback - it is the only place that also decays knockbackVelocity and
        // writes it to the Rigidbody - so the decrement belongs there and only there.
    }

    // Drive the arm-blink here so it is the LAST writer to spriteRenderer.color each frame. 
    private void LateUpdate()
    {
        // Roll the ball while it travels. Runs in LateUpdate so it has the final
        // say on rotation each frame (the animation controller has been told not
        // to touch it). Naturally stops once the fuse arms and velocity is zeroed.
        UpdateRoll();
        UpdatePulse();

        if (!isFuseActive || hasExploded || spriteRenderer == null) return;

        float elapsed = Time.time - fuseStartTime;
        float t = Mathf.Clamp01(elapsed / fuseTime);

        // Blink period ramps from slow to frantic as detonation approaches.
        float period = Mathf.Lerp(blinkIntervalStart, blinkIntervalEnd, t * t);
        if (period < 0.0001f) period = 0.0001f;

        bool on = (Mathf.FloorToInt(elapsed / period) & 1) == 0;
        spriteRenderer.color = on ? blinkColor : fuseBaseColor;
    }

    // Spin the sprite about its centre like a rolling boulder. Uses the rolling
    // constraint angle = distance / radius, driven by the FULL travel distance so
    // it tumbles no matter which way it heads (not just left/right). The spin
    // direction follows the last horizontal heading, so it stays consistent even
    // while moving straight up or down. Visual only: writes transform.rotation
    // (which nothing else drives for the Bomber) and never touches the Rigidbody,
    // collider, or the unparented health bar. Frozen while armed/dead so the ball
    // sits still through its warning blink and detonation.
    private void UpdateRoll()
    {
        if (!useProceduralSprite || !rollWhenMoving) return;
        if (rb == null || isFuseActive || hasExploded) return;

        float speed = rb.linearVelocity.magnitude;
        if (speed < 0.01f) return;

        if (Mathf.Abs(rb.linearVelocity.x) > 0.05f)
            lastRollSign = Mathf.Sign(rb.linearVelocity.x);

        float radius = Mathf.Max(0.1f, proceduralSpriteWorldSize * 0.5f);
        float distance = speed * Time.deltaTime;                 // travel this frame, any direction
        rollAngle -= (distance / radius) * Mathf.Rad2Deg * rollSpeedScale * lastRollSign;
        transform.rotation = Quaternion.Euler(0f, 0f, rollAngle);
    }

    // Subtle 'breathing' pulse so the ball reads as a living thing. Scales the
    // sprite about its centre; the collider radius used for arming was cached once
    // at Start, so the fuse range is unaffected by the pulse. Rotation (roll) and
    // scale (pulse) are independent, so they coexist on the same transform.
    private void UpdatePulse()
    {
        if (!useProceduralSprite || !pulsate || hasExploded) return;
        float s = 1f + pulseAmplitude * Mathf.Sin(Time.time * pulseSpeed * (Mathf.PI * 2f));
        transform.localScale = baseScale * s;
    }

    private void FixedUpdate()
    {
        if (hasExploded) return;

        // Frozen: stop completely.
        if (isFrozen)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Parry stun.
        // BUGFIX: this used to test whether the ParryStunEffect COMPONENT EXISTS
        // rather than whether the stun is still active. Powerful Parry (331) leaves
        // the component alive after the freeze window as a lingering damage debuff,
        // so a single parried shot pinned the Bomber in place permanently - it never
        // moved or detonated again. Test IsStunActive, matching EnemyController and
        // BruteController (whose comment warns about exactly this mistake).
        var parryStun = GetComponent<ParryStunEffect>();
        if (parryStun != null && parryStun.IsStunActive)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Game over.
        if (EnergyManager.Instance != null && EnergyManager.Instance.IsGameOver())
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Knockback phase: let physics drive, just decay.
        if (isKnockedBack)
        {
            knockbackTimer -= Time.fixedDeltaTime;
            knockbackVelocity *= 0.82f;
            rb.linearVelocity = knockbackVelocity;
            if (knockbackTimer <= 0f)
            {
                isKnockedBack = false;
                rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        // While armed, the Bomber plants itself and blinks (handled in LateUpdate).

        if (isFuseActive)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Normal movement / retargeting.
        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            RefreshTarget();
            // If still no valid target, idle this frame.
            if (currentTarget == null || !IsValidTarget(currentTarget))
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }
        }

        // Smoke Screen: if a smoke cloud blocks our sightline to the target, we
        // lose sight of it and mill in place until it clears — don't advance,
        // and don't arm the fuse. Same confusion every other enemy gets.
        if (SmokeBlind.Blocks(transform.position, currentTarget.position))
        {
            rb.linearVelocity = SmokeBlind.ShuffleVelocity(smokeShufflePhase, stats.MoveSpeed);
            return;
        }

        float gap = GapToTarget(currentTarget);

        // Enter fuse zone when the two bodies are nearly touching.
        if (gap <= fuseStartRange)
        {
            StartCoroutine(FuseRoutine());
            return;
        }

        MoveTowardTarget();
    }

    private void MoveTowardTarget()
    {
        if (currentTarget == null) return;

        HandleStuckDetection();
        Vector2 dir = ((Vector2)currentTarget.position - (Vector2)transform.position).normalized;
        dir = GetMovementDirection(dir);
        rb.linearVelocity = dir * stats.MoveSpeed;
    }

    private void RefreshTarget()
    {
        // Bomber never targets the player. It picks the genuinely CLOSEST structure 
        float bestDist = Mathf.Infinity;
        Transform best = null;

        GameObject[] towers = GameObject.FindGameObjectsWithTag("Tower");
        foreach (var t in towers)
        {
            if (t == null || !t.activeInHierarchy) continue;
            var tc = t.GetComponent<Tower>();
            if (tc != null && tc.IsDestroyed()) continue;

            float d = DistanceToTarget(t.transform);
            if (d < bestDist)
            {
                bestDist = d;
                best = t.transform;
            }
        }

        // Core is a candidate too — NOT just a fallback.
        if (coreTarget != null)
        {
            float dc = DistanceToTarget(coreTarget);
            if (dc < bestDist)
            {
                bestDist = dc;
                best = coreTarget;
            }
        }

        // Final fallback: if somehow nothing was picked, aim at the core.
        currentTarget = best != null ? best : coreTarget;
    }

    // Distance from the Bomber to the target's COLLIDER EDGE 
    private float DistanceToTarget(Transform t)
    {
        if (t == null) return Mathf.Infinity;
        Vector2 self = transform.position;

        Collider2D chosen = null;
        var cols = t.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            if (!cols[i].isTrigger) { chosen = cols[i]; break; } // prefer solid body
            if (chosen == null) chosen = cols[i];                // remember a trigger as fallback
        }

        if (chosen != null)
        {
            Vector2 edge = chosen.ClosestPoint(self); // returns self if inside → distance 0
            return Vector2.Distance(self, edge);
        }

        return Vector2.Distance(self, t.position);
    }

    // Edge-to-edge gap between the Bomber's body and the target's body.

    private float GapToTarget(Transform t)
    {
        return Mathf.Max(0f, DistanceToTarget(t) - bomberBodyRadius);
    }

    private bool IsValidTarget(Transform t)
    {
        if (t == null || t.gameObject == null || !t.gameObject.activeInHierarchy) return false;
        var tower = t.GetComponent<Tower>();
        if (tower != null && tower.IsDestroyed()) return false;
        return true;
    }

    private IEnumerator FuseRoutine()
    {
        isFuseActive = true;
        fuseCancelled = false;
        fuseStartTime = Time.time;
        rb.linearVelocity = Vector2.zero;

        // Capture the resting color BEFORE we start blinking.
        fuseBaseColor = spriteRenderer != null ? spriteRenderer.color : Color.white;

        // Arm the audio warning alongside the visual one.
        if (playFuseWarningSound && FMODEvents.instance != null)
            fuseWarningSfx.Play(FMODEvents.instance.bombWarning, transform.position);

        // Silence the systems that fight us for spriteRenderer.color while armed:
        //   - SmoothSpriteFlip rim flash (minimal mode disables its color writes)
        //   - EnemyAnimationController sprite/rotation churn that triggers flips
        if (smoothFlip != null) smoothFlip.SetMinimalMode(true);
        if (animController != null) animController.enabled = false;

        float elapsed = 0f;
        while (elapsed < fuseTime)
        {
            elapsed = Time.time - fuseStartTime;

            // The Bomber plants itself while armed, but knockback and shoving from
            // other enemies still move it, so keep the warning on the body.
            fuseWarningSfx.SetPosition(transform.position);

            // Killed by player/tower before detonation — abort, die normally (no boom).
            if (stats == null || stats.IsDead())
            {
                RestoreFromFuse();
                yield break;
            }

            // Target destroyed while we were armed → try to re-acquire.
            if (!IsValidTarget(currentTarget))
                RefreshTarget();

            // If nothing valid is within fuse range any more, DISARM and
            // resume chasing the next closest target instead of wasting the
            // explosion on empty ground.
            if (!IsValidTarget(currentTarget) ||
                GapToTarget(currentTarget) > fuseStartRange + 0.75f)
            {
                fuseCancelled = true;
                RestoreFromFuse();
                yield break;
            }

            yield return null;
        }

        RestoreColorOnly();
        Explode();
    }

    // Re-enables anim/flip and restores resting color after a CANCELLED fuse.
    private void RestoreFromFuse()
    {
        isFuseActive = false;

        // Disarmed, not detonated — let the warning fade out rather than snapping
        // off, which would read as a bug ("did it explode?").
        fuseWarningSfx.Stop(immediate: false);

        RestoreColorOnly();
        if (animController != null) animController.enabled = true;
        // Leave SmoothSpriteFlip in minimal mode off again so normal flips resume.
        if (smoothFlip != null) smoothFlip.SetMinimalMode(false);
    }

    private void RestoreColorOnly()
    {
        if (spriteRenderer != null) spriteRenderer.color = fuseBaseColor;
    }

    private void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        Vector3 pos = transform.position;

        // Cut the warning FIRST, hard, and before the explosion one-shot goes out —
        // the whole point of the warning is that it resolves into the bang.
        fuseWarningSfx.Stop(immediate: true);

        var vfxRoot = new GameObject("Bomber_ExplosionVFX");
        vfxRoot.transform.position = pos;
        var fx = vfxRoot.AddComponent<Boss2MeteorVFX>();
        fx.Play(explosionRadius);

        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.bomberExplosion.IsNull)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.bomberExplosion, pos);

        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(0.3f, 0.15f);

        float explosionDamage = stats != null ? stats.Damage : 40f;
        ApplyExplosionDamage(pos, explosionDamage);

        //  Self-destruct without triggering the regular Die() path 

        PerformExplosionDeath();
    }

    private void ApplyExplosionDamage(Vector3 pos, float damage)
    {
        HashSet<CharacterStats> damagedChars = new HashSet<CharacterStats>();
        HashSet<IEnergyConsumer> damagedConsumers = new HashSet<IEnergyConsumer>();

        // Use ContactFilter2D with useTriggers=true — OverlapCircleAll with
        // a plain layermask silently skips trigger colliders, which is why
        // towers and the core (which use triggers) were never taking damage.
        var filter = new ContactFilter2D();
        filter.SetLayerMask(explosionLayers);
        filter.useTriggers = true;
        filter.useLayerMask = true;

        var hitList = new List<Collider2D>();
        Physics2D.OverlapCircle(pos, explosionRadius, filter, hitList);

        foreach (var hit in hitList)
        {
            if (hit == null || hit.gameObject == gameObject) continue;

            // Don't damage other enemies (the Bomber itself is also caught here).
            if (hit.GetComponentInParent<EnemyStats>() != null) continue;

            // Player / other CharacterStats targets.
            var cs = hit.GetComponentInParent<CharacterStats>();
            if (cs != null && damagedChars.Add(cs))
            {
                cs.TakeDamage(damage);
                // Player-side on-hit augments (Damage Reflection / Ice Armor). The
                // explosion never routes through EnemyController, so these used to be
                // skipped entirely. damagedChars already dedups; no-op for non-players.
                EnemyController.NotifyCharacterDamaged(cs, damage, gameObject);
                continue;
            }

            // Towers / Core: call EnergyManager DIRECTLY (same path the normal
            // enemy melee attack uses in EnemyController.ApplyDamageToTarget).
            // Routing through EnemyDamageSystem worked too, but going straight
            // to the manager removes one indirection and matches the proven path.
            var consumer = hit.GetComponentInParent<IEnergyConsumer>();
            if (consumer != null && damagedConsumers.Add(consumer))
            {
                if (EnergyManager.Instance != null)
                    EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
            }
        }

        // Safety net: explicit player check (same pattern as Boss2).
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            float dist = Vector2.Distance(player.transform.position, pos);
            if (dist <= explosionRadius)
            {
                var ps = player.GetComponentInChildren<CharacterStats>()
                      ?? player.GetComponentInParent<CharacterStats>();
                if (ps != null && !damagedChars.Contains(ps))
                {
                    ps.TakeDamage(damage);
                    EnemyController.NotifyCharacterDamaged(ps, damage, gameObject);
                }
            }
        }
    }

    // Safety nets for the fuse warning. The Bomber can leave the fuse window without
    // going through either Explode or RestoreFromFuse — killed by a tower, pooled, or
    // the scene unloaded mid-countdown — and a held instance would otherwise keep
    // ticking with nothing on screen. Stop() is idempotent, so overlapping with the
    // normal paths is harmless.
    private void OnDisable()
    {
        fuseWarningSfx.Stop(immediate: true);
    }

    private void OnDestroy()
    {
        fuseWarningSfx.Stop(immediate: true);
    }

    // Minimal death wrap: notifies wave spawner, drops energy, destroys the GameObject.
    private void PerformExplosionDeath()
    {
        if (stats != null && stats.canDropEnergy)
        {
            // Routed through EnemyDropAugments, exactly as EnemyStats.PerformDeath does.
            // The block here used to hand-roll the per-enemy-override branch against the
            // raw EnergyDropManager — the pre-augment API — which produced the same
            // drops but silently skipped Lucky Strikes (337), Marksman's Bounty (341)
            // and Plunder (342). SpawnEnemyDrop applies that same override internally,
            // so the drop behaviour is unchanged; only the augments are now honoured.
            EnemyDropAugments.SpawnEnemyDrop(
                transform.position,
                GameOrchestrator.Instance?.CurrentStageIndex ?? 0,
                gameObject,
                stats.energyDropChance,
                stats.energyDropValue);
        }

        // Shared death book-keeping, identical to EnemyStats.PerformDeath: wave
        // counter -> augment 335 tithe -> EnergyManager kill event -> attribution
        // cleanup. This path previously did only the wave counter and the EnergyManager
        // call, so a Bomber detonated by a tower paid NO tithe and leaked a
        // TowerKillAttribution entry.
        EnemyStats.FireCommonDeathHooks(gameObject);

        // Destroy the health bar manually since we're not going through EnemyStats.Die().
        var hb = stats?.GetHealthBar();
        if (hb != null) Destroy(hb.gameObject);

        Destroy(gameObject);
    }

    //  Crowd-control public API (mirrors EnemyController surface) 
    public void ApplyFreeze(float duration)
    {
        isFrozen = true;
        freezeTimeRemaining = duration;
        if (spriteRenderer != null) spriteRenderer.color = Color.cyan;
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    private void Unfreeze()
    {
        isFrozen = false;
        freezeTimeRemaining = 0f;
        // Color will be overwritten by the fuse blink if the fuse is active.
        // BUGFIX: this used to restore a hardcoded Color.white, which permanently
        // bleached any tinted prefab after its first freeze. Restore the tint
        // captured at Start instead - same thing EnemyController.UnfreezeEnemy does.
        if (spriteRenderer != null && !isFuseActive)
            spriteRenderer.color = restingColor;
    }

    public void ApplyKnockback(Vector2 direction, float force, float duration = 0.25f)
    {
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;
        isKnockedBack = true;
        knockbackTimer = duration;
        knockbackVelocity = direction.normalized * force;
        rb.linearVelocity = knockbackVelocity;
    }

    //  Stuck detection (verbatim from EnemyController) 
    private void HandleStuckDetection()
    {
        if (currentTarget == null) return;

        Vector2 displacement = (Vector2)transform.position - lastKnownPosition;
        Vector2 toTarget = (Vector2)currentTarget.position - lastKnownPosition;
        float progress = toTarget.sqrMagnitude > 0.0001f
            ? Vector2.Dot(displacement, toTarget.normalized)
            : displacement.magnitude;

        if (progress > minMovementThreshold)
        {
            timeSinceLastMovement = 0f;
            lastKnownPosition = transform.position;
            isInStuckMode = false;
        }
        else
        {
            timeSinceLastMovement += Time.fixedDeltaTime;
            if (timeSinceLastMovement > stuckCheckTime && !isInStuckMode)
                EnterStuckMode();
        }

        if (isInStuckMode)
        {
            stuckModeTimer -= Time.fixedDeltaTime;
            if (stuckModeTimer <= 0f)
            {
                bool stillBlocked = FindBlocker() != null;
                if (stillBlocked)
                {
                    EnterStuckMode();
                    timeSinceLastMovement = 0f;
                    lastKnownPosition = transform.position;
                }
                else
                {
                    // Genuinely clear of the obstacle — forget the committed
                    // wall-follow direction so the next wall starts fresh.
                    isInStuckMode = false;
                    lastStuckDir = Vector2.zero;
                }
            }
        }
    }

    private void EnterStuckMode()
    {
        isInStuckMode = true;
        stuckModeTimer = 2f;

        Collider2D wall = FindBlocker();
        Vector2 wallNormal;
        if (wall != null)
        {
            Vector2 selfPos = transform.position;
            Vector2 closest = wall.ClosestPoint(selfPos);
            wallNormal = selfPos - closest;
            if (wallNormal.sqrMagnitude < 0.0001f)
                wallNormal = selfPos - (Vector2)wall.transform.position;
            if (wallNormal.sqrMagnitude < 0.0001f)
            {
                Vector2 toT = ((Vector2)currentTarget.position - selfPos).normalized;
                wallNormal = new Vector2(-toT.y, toT.x);
            }
            else
                wallNormal = wallNormal.normalized;
        }
        else
        {
            Vector2 toTarget = ((Vector2)currentTarget.position - (Vector2)transform.position).normalized;
            wallNormal = new Vector2(-toTarget.y, toTarget.x);
        }

        Vector2 toT2 = ((Vector2)currentTarget.position - (Vector2)transform.position).normalized;
        Vector2 perpA = new Vector2(-wallNormal.y, wallNormal.x);
        Vector2 perpB = -perpA;

        // If we were already following this wall, KEEP going the same way so we
        // consistently round a wide obstacle (a wall-follower reliably escapes
        // convex shapes). Only on the FIRST contact do we pick the side that
        // points more toward the target.
        if (lastStuckDir != Vector2.zero)
            stuckAvoidanceDirection = Vector2.Dot(perpA, lastStuckDir) >= Vector2.Dot(perpB, lastStuckDir) ? perpA : perpB;
        else
            stuckAvoidanceDirection = Vector2.Dot(perpA, toT2) >= Vector2.Dot(perpB, toT2) ? perpA : perpB;

        lastStuckDir = stuckAvoidanceDirection;
    }

    // Finds the nearest collider on the avoid mask (walls + towers) within
    // avoidDistance, EXCLUDING the current target (we don't dodge the thing we
    // are trying to reach). Returns null if the only thing nearby is our target
    // or nothing at all.
    private Collider2D FindBlocker()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, avoidDistance, avoidMask);
        Collider2D nearest = null;
        float best = Mathf.Infinity;
        Vector2 self = transform.position;
        foreach (var h in hits)
        {
            if (h == null) continue;
            // Don't avoid our own target (live target tower, or core).
            if (currentTarget != null &&
                (h.transform == currentTarget || h.transform.IsChildOf(currentTarget)))
                continue;
            float d = Vector2.Distance(self, h.ClosestPoint(self));
            if (d < best) { best = d; nearest = h; }
        }
        return nearest;
    }

    private Vector2 GetMovementDirection(Vector2 desired)
    {
        if (isInStuckMode) return stuckAvoidanceDirection;

        Collider2D obstacle = FindBlocker();
        if (obstacle != null)
        {
            Vector2 selfPos = transform.position;
            Vector2 closest = obstacle.ClosestPoint(selfPos);
            Vector2 toObstacle = closest - selfPos;
            if (toObstacle.sqrMagnitude < 0.0001f)
                toObstacle = (Vector2)obstacle.transform.position - selfPos;
            toObstacle = toObstacle.normalized;

            Vector2 perpL = new Vector2(-toObstacle.y, toObstacle.x);
            Vector2 perpR = new Vector2(toObstacle.y, -toObstacle.x);
            float dotL = Vector2.Dot(perpL, desired);
            float dotR = Vector2.Dot(perpR, desired);

            bool roundBlocker = obstacle.GetComponentInParent<Tower>() != null;

            if (roundBlocker)
            {
                // TOWERS
                bool pickLeft;
                if (lastAvoidSign > 0) pickLeft = dotL >= dotR - 0.25f;
                else if (lastAvoidSign < 0) pickLeft = dotL > dotR + 0.25f;
                else pickLeft = dotL > dotR;
                lastAvoidSign = pickLeft ? 1 : -1;

                Vector2 chosen = pickLeft ? perpL : perpR;
                return Vector2.Lerp(chosen, desired, 0.45f).normalized;
            }

            // WALLS / obstacles
            lastAvoidSign = 0;
            Vector2 chosenWall = dotL > dotR ? perpL : perpR;
            return Vector2.Lerp(chosenWall, desired, 0.1f);
        }

        // No blocker nearby — clear all avoidance memory so the next obstacle
        // starts fresh.
        lastAvoidSign = 0;
        lastStuckDir = Vector2.zero;
        return desired;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, fuseStartRange);
        Gizmos.color = new Color(1f, 0.4f, 0f);
        Gizmos.DrawWireSphere(transform.position, explosionRadius);

        if (currentTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }
    }
}



