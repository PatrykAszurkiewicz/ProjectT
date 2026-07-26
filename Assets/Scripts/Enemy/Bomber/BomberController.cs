using UnityEngine;
using System.Collections;
using System.Collections.Generic;


// Bomber — a special enemy that completely ignores the player and walks straight
// toward the nearest tower or the central core.  Once it enters explosion range,
// it blinks red for 3 seconds and detonates, damaging everything nearby (towers,
// core, and the player — but not other enemies).


[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(Rigidbody2D))]
public class BomberController : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("How often (seconds) the Bomber re-evaluates its target.")]
    [SerializeField] private float targetUpdateInterval = 0.5f;

    [Header("Explosion")]
    [Tooltip("Edge-to-edge GAP (world units) between the Bomber's body and the " +
             "target's body at which the fuse starts. ~0 means it must be nearly " +
             "touching. This is size-independent, so it works the same against a " +
             "small tower and the large central core. Keep it small (0.3–0.8).")]
    [SerializeField] private float fuseStartRange = 0.5f;

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
    private bool fuseCancelled = false;
    private float bomberBodyRadius = 0f;

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

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animController = GetComponent<EnemyAnimationController>();
        smoothFlip = GetComponent<SmoothSpriteFlip>();

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

        if (isKnockedBack)
        {
            knockbackTimer -= Time.deltaTime;
            if (knockbackTimer <= 0f) isKnockedBack = false;
        }
    }

    // Drive the arm-blink here so it is the LAST writer to spriteRenderer.color each frame. 
    private void LateUpdate()
    {
        if (!isFuseActive || hasExploded || spriteRenderer == null) return;

        float elapsed = Time.time - fuseStartTime;
        float t = Mathf.Clamp01(elapsed / fuseTime);

        // Blink period ramps from slow to frantic as detonation approaches.
        float period = Mathf.Lerp(blinkIntervalStart, blinkIntervalEnd, t * t);
        if (period < 0.0001f) period = 0.0001f;

        bool on = (Mathf.FloorToInt(elapsed / period) & 1) == 0;
        spriteRenderer.color = on ? blinkColor : fuseBaseColor;
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
        if (GetComponent<ParryStunEffect>() != null)
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
                    ps.TakeDamage(damage);
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
            if (stats.energyDropValue > 0 && stats.energyDropChance >= 0f)
                EnergyDropManager.TrySpawnEnergyDrop(transform.position, stats.energyDropChance, stats.energyDropValue);
            else
                EnergyDropManager.TrySpawnEnemyDrop(transform.position, GameOrchestrator.Instance?.CurrentStageIndex ?? 0);
        }

        EnergyManager.Instance?.OnEnemyKilled(gameObject);

        WaveSpawner waveSpawner = FindAnyObjectByType<WaveSpawner>();
        waveSpawner?.OnEnemyDeath();

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
        // Color will be overwritten by blink coroutine if fuse is active.
        if (spriteRenderer != null && !isFuseActive)
            spriteRenderer.color = Color.white;
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



