using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// SHIELD SYSTEM
// Directional blocking + timed parry for the Shield tool if right-click is pressed within PARRY_WINDOW seconds of an incoming
// Integration points:
//   - Weapon.cs creates / destroys this system when shield tool is equipped.
//   - PlayerAttack.cs calls RaiseShield() / LowerShield().
//   - EnemyController.ApplyDamageToTarget() calls TryBlockOrParry().
public class ShieldSystem
{
    //  Tuning 
    private const float BLOCK_ARC_DEGREES = 120f;   // total arc width (mouse)
    // On a gamepad the aim direction is coarser and holds its last value when the
    // right stick is centered, so a tight directional arc makes blocking feel
    // unreliable — attacks from off-facing slip through after the player rotates.
    // 360 = omnidirectional while raised. Lower it (e.g. 240) to keep some
    // directionality on the pad.
    private const float BLOCK_ARC_DEGREES_GAMEPAD = 360f;
    private const float PARRY_WINDOW = 0.2f;        // seconds after raise
    // Base stun durations and the parry damage bonus now live in ParryUpgrades
    // (ParryUpgrades.BaseStunNormal / BaseStunBoss / BaseDamageBonus) so the
    // upgrade augments (330 Longer Parry Stun, 331 Powerful Parry) can layer onto
    // a single source of truth shared by melee and projectile parry.

    // Fraction of damage that still gets through when the shield BLOCKS a hit
    // (as opposed to fully negating it). 0 = block negates entirely; 0.5 = a
    // blocked hit deals half damage. Used by the projectile-block path so a
    // raised shield reduces incoming shots even without the parry augment.
    private const float BLOCK_DAMAGE_MULTIPLIER = 0.5f;

    // Visual arc
    private const float ARC_RADIUS = 1.0f;
    private const int ARC_SEGMENTS = 16;
    private const float ARC_WIDTH = 0.08f;
    private const float VISUAL_ARC_DEGREES = BLOCK_ARC_DEGREES * 0.70f; // visual is shorter than hitbox

    //  State 
    private readonly Weapon weapon;
    private readonly WeaponData shieldData;

    private bool isRaised = false;
    private float raiseTime = -999f;

    // Track whether the shield is actively blocking (held down).

    private float lastLowerTime = -999f;  // when shield was last lowered

    // Grace period after releasing right-click during which a parry can still trigger

    private const float QUICK_PRESS_GRACE = 0.6f;

    // Block-feedback throttle: continuous damage sources (laser, flames) call TryBlockOrParry many times per second. 
    private const float BLOCK_FEEDBACK_COOLDOWN = 0.12f;
    private float lastBlockFeedbackTime = -999f;

    // Stamina exhaustion gating:

    private const float STAMINA_REENGAGE_THRESHOLD = 0.5f;
    private bool shieldBroken = false;

    // Sustained-source drain rate (stamina per second).
    private const float BLOCK_DRAIN_PER_SECOND = 2.4f;
    private const float SUSTAINED_SOURCE_WINDOW = 0.1f; // same attacker within this = continuous
    private GameObject lastBlockedAttacker = null;
    private float lastBlockTime = -999f;

    // Visual objects
    private GameObject arcObject;
    private LineRenderer arcLine;
    private Material arcMaterial;

    // Parry VFX pool (reuse across parries)
    private static Sprite _shieldIconSprite;

    // Reference to player transform (cached)
    private Transform playerTransform;
    private PlayerStats playerStats;

    // Phase 8: which player owns this shield (for per-player parry upgrades).
    private int ownerIndex = 0;

    //  Construction / Cleanup 

    public ShieldSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.shieldData = data;

        // Cache player transform + stats. Resolve the OWNER from this weapon
        // (Phase 8) so in co-op each shield heals/credits its own player instead
        // of whichever object happens to carry the "Player" tag. Falls back to the
        // old tag lookup only if the weapon isn't parented under a player.
        var ownerStats = weapon != null ? weapon.GetComponentInParent<PlayerStats>() : null;
        if (ownerStats != null)
        {
            playerStats = ownerStats;
            playerTransform = ownerStats.transform;
            var ownerRef = weapon.GetComponentInParent<PlayerRef>();
            ownerIndex = ownerRef != null ? ownerRef.PlayerIndex : 0;
        }
        else
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
                playerStats = player.GetComponent<PlayerStats>();
            }
        }

        CreateArcVisual();
        SetArcVisible(false);
    }

    public void Cleanup()
    {
        if (arcObject != null) Object.Destroy(arcObject);
        arcObject = null;
        arcLine = null;
    }

    //  Public API 

    public bool IsRaised => isRaised;

    // Fraction of damage a blocked hit still deals (see BLOCK_DAMAGE_MULTIPLIER).
    public float BlockDamageMultiplier => BLOCK_DAMAGE_MULTIPLIER;

    /// Expose the arc LineRenderer for visual feedback (ShieldFeedback uses this).
    public LineRenderer ArcLineRenderer => arcLine;

    public void RaiseShield()
    {
        if (isRaised) return;
        isRaised = true;
        raiseTime = Time.time;
        SetArcVisible(true);
    }

    public void LowerShield()
    {
        if (!isRaised) return;
        isRaised = false;
        lastLowerTime = Time.time;
        SetArcVisible(false);
    }


    // Called every frame from Weapon.Update().
    // Updates arc visual position and rotation.

    public void Update()
    {
        if (!isRaised || playerTransform == null) return;

        if (arcLine != null)
            UpdateArcTransform();
    }


    // Called by EnemyController (or any damage source) BEFORE applying damage.

    public bool TryBlockOrParry(GameObject attackerGO)
    {
        if (playerTransform == null || attackerGO == null)
            return false;

        // Shield must be either currently raised OR recently released (quick-press grace)
        bool currentlyRaised = isRaised;
        bool recentlyReleased = !isRaised && (Time.time - lastLowerTime) <= QUICK_PRESS_GRACE;

        if (!currentlyRaised && !recentlyReleased)
        {
            return false;
        }

        // Check if attack comes from within the shield arc.

        Vector2 cursorDir = GetCursorDirection();
        Vector2 attackDir = ((Vector2)attackerGO.transform.position - (Vector2)playerTransform.position).normalized;

        float angle = Vector2.Angle(cursorDir, attackDir);
        if (angle > BlockHalfAngle())
        {
            //Debug.Log($"[SHIELD] MISS — outside arc. angle={angle:F1}° (need <{BLOCK_ARC_DEGREES * 0.5f}°) " +
            //          $"cursor toward {cursorDir}, enemy at {attackDir}");
            return false;
        }

        // Parry check: only a fresh right-click PRESS during the parry window counts.
        // Holding the shield from before the window is just a block, not a parry.
        bool isParry = false;
        var ec = attackerGO.GetComponent<EnemyController>();
        if (ec != null)
        {
            isParry = ec.IsInParryWindow(raiseTime, ownerIndex);

            //Debug.Log($"[PARRY EVAL] {attackerGO.name}: raiseInWindow={isParry} " +
            //          $"=> {(isParry ? "PARRY!" : "BLOCK")}");
        }
        else
        {
            // No EnemyController (boss projectile, etc.) — fallback
            isParry = (Time.time - raiseTime) <= PARRY_WINDOW;
        }

        // If the shield was already lowered (quick press), only a parry counts — not a block
        if (recentlyReleased && !isParry)
        {
            //Debug.Log($"[SHIELD] Quick-press but not in parry window — no block (shield is down)");
            return false;
        }

        //  Stamina gate 
        // Parries bypass this — they're one-frame skill, not sustained defense.
        // Blocks require either non-broken state OR enough stamina to re-engage.
        if (!isParry && playerStats != null)
        {
            // Already broken? Stay broken until stamina recovers above the threshold.
            if (shieldBroken && playerStats.currentStamina < STAMINA_REENGAGE_THRESHOLD)
                return false;

            // Fully drained? Block fails as well (no stamina to absorb the hit).
            if (playerStats.currentStamina <= 0f)
            {
                shieldBroken = true;
                return false;
            }

            // Stamina has recovered enough — shield is operational again.
            shieldBroken = false;
        }

        //Debug.Log($"[SHIELD] {(isParry ? "PARRY!" : "BLOCK")} from {attackerGO.name} " +
        //          $"angle={angle:F1}° raised={currentlyRaised} recentRelease={recentlyReleased} " +
        //          $"raiseAge={Time.time - raiseTime:F3}s");

        if (isParry)
        {
            ApplyParry(attackerGO);
            SpawnParryVFX();

            //  Visual + audio feedback (parry) 
            ShieldFeedback.OnParry(playerTransform, attackerGO.transform.position);
        }
        else
        {
            //  Audio every tick (constant clang reads as sustained pressure) 
            ShieldFeedback.OnBlockAudio(playerTransform);

            //  Visual feedback throttled (sparks/arc flash/shake would otherwise stack
            //    every laser/flame tick and saturate the screen to white) ──
            if (Time.time - lastBlockFeedbackTime >= BLOCK_FEEDBACK_COOLDOWN)
            {
                lastBlockFeedbackTime = Time.time;
                ShieldFeedback.OnBlockVisuals(playerTransform, attackerGO.transform.position, arcLine);
            }
        }

        //  Stamina drain (only on successful block / parry) 
        // Detect continuous source: same attacker calling within the sustained window.
        // - Discrete hit / new attacker:  pay the full per-call cost.
        // - Continuous source:            pay per-second rate × elapsed time.
        // Parries always drain the full discrete cost.
        if (playerStats != null)
        {
            float drainAmount;
            if (isParry)
            {
                drainAmount = playerStats.shieldBlockStaminaCost;
            }
            else
            {
                bool sameSource = attackerGO == lastBlockedAttacker
                                  && (Time.time - lastBlockTime) <= SUSTAINED_SOURCE_WINDOW;
                if (sameSource)
                {
                    // Per-second rate: drain proportional to elapsed time since last block
                    float dt = Time.time - lastBlockTime;
                    drainAmount = BLOCK_DRAIN_PER_SECOND * dt;
                }
                else
                {
                    // Discrete hit (or first hit from this attacker) — full per-call cost
                    drainAmount = playerStats.shieldBlockStaminaCost;
                }
            }

            playerStats.DrainStamina(drainAmount);

            if (!isParry && playerStats.currentStamina <= 0f)
                shieldBroken = true;

            lastBlockedAttacker = attackerGO;
            lastBlockTime = Time.time;
        }

        return true;
    }

    //  Projectile interception (Augment 325 — Projectile Parry) 

    public enum ProjectileInterception { None, Blocked, Parried }

    // Evaluates whether the raised / just-pressed shield intercepts an in-flight
    // enemy projectile, and returns whether it was a parry (bounce back) or a
    // plain block (absorb). Unlike TryBlockOrParry this is keyed to the SHOT, not
    // to an enemy's melee animation:
    //   - Direction: the angle is measured to 'aimReference' (pass the projectile
    //     for a direct shot, or the firing enemy for a lobbed shell), NOT the
    //     enemy's body at impact time.
    //   - Timing:    a parry is a fresh right-click PRESS within PARRY_WINDOW. A
    //     shield merely held open absorbs the shot as a block instead.
    // The projectile is responsible for calling this each frame while it is inside
    // its own reaction radius, and for acting on the result (bounce / destroy).
    public ProjectileInterception TryInterceptProjectile(Vector3 aimReference)
    {
        if (playerTransform == null) return ProjectileInterception.None;

        bool currentlyRaised = isRaised;
        bool recentlyReleased = !isRaised && (Time.time - lastLowerTime) <= QUICK_PRESS_GRACE;
        if (!currentlyRaised && !recentlyReleased)
            return ProjectileInterception.None;

        // Directional gate — the shot must approach from within the shield arc.
        Vector2 cursorDir = GetCursorDirection();
        Vector2 toShot = (Vector2)aimReference - (Vector2)playerTransform.position;
        if (toShot.sqrMagnitude < 1e-6f) return ProjectileInterception.None;
        float angle = Vector2.Angle(cursorDir, toShot.normalized);
        if (angle > BlockHalfAngle())
            return ProjectileInterception.None;

        // Parry = fresh press within the parry window; otherwise it's a block.
        bool isParry = (Time.time - raiseTime) <= PARRY_WINDOW;

        // Quick-press grace: if the shield is already down, only a parry counts.
        if (recentlyReleased && !isParry)
            return ProjectileInterception.None;

        // Stamina gate for blocks (parries bypass — pure timing skill).
        if (!isParry && playerStats != null)
        {
            if (shieldBroken && playerStats.currentStamina < STAMINA_REENGAGE_THRESHOLD)
                return ProjectileInterception.None;
            if (playerStats.currentStamina <= 0f)
            {
                shieldBroken = true;
                return ProjectileInterception.None;
            }
            shieldBroken = false;
        }

        // Feedback (parry phantom vs block sparks) is deliberately NOT
        // played here. Whether a parry-timed hit actually PARRIES depends on
        // state the projectile owns (augment unlocked + a live firer to bounce
        // to). The caller decides the final outcome and then calls
        // PlayProjectileParryFeedback / PlayProjectileBlockFeedback, so the gold
        // shield phantom only ever shows on a real bounce-back parry.

        // Stamina drain — a single discrete cost per intercepted shot.
        if (playerStats != null)
        {
            playerStats.DrainStamina(playerStats.shieldBlockStaminaCost);
            if (!isParry && playerStats.currentStamina <= 0f)
                shieldBroken = true;
        }

        return isParry ? ProjectileInterception.Parried : ProjectileInterception.Blocked;
    }

    // Feedback for a projectile interception, fired by the projectile AFTER it
    // resolves the outcome (so the parry phantom is exclusive to a real bounce).
    public void PlayProjectileParryFeedback(Vector3 contactPoint)
    {
        if (playerTransform == null) return;
        SpawnParryVFX();
        ShieldFeedback.OnParry(playerTransform, contactPoint);

        // 333 Heal on Parry — projectile parry counts as a successful parry.
        HealOnParry();
    }

    public void PlayProjectileBlockFeedback(Vector3 contactPoint)
    {
        if (playerTransform == null) return;
        ShieldFeedback.OnBlock(playerTransform, contactPoint, arcLine);
    }

    //  Parry Logic 

    private void ApplyParry(GameObject attackerGO)
    {
        if (attackerGO == null) return;

        // Stun + damage-debuff (with augment upgrades layered in by the helper:
        // 330 Longer Parry Stun, 331 Powerful Parry). Base stun + base bonus come
        // from ParryUpgrades so melee and projectile parry stay in lockstep.
        ParryStunEffect.ApplyOrRefresh(attackerGO, ownerIndex);

        // 333 Heal on Parry
        HealOnParry();
    }

    // Augment 333 — Heal on Parry. Heals a fraction of the player's max health on
    // a successful parry. Shared by melee (ApplyParry) and projectile parry
    // (PlayProjectileParryFeedback) so the augment covers both routes.
    private void HealOnParry()
    {
        if (!ParryUpgrades.HealOnParryEnabledFor(ownerIndex)) return;
        if (playerStats == null) return;

        float healAmount = playerStats.maxHealth * ParryUpgrades.HealOnParryPercentFor(ownerIndex);
        if (healAmount <= 0f) return;

        // CharacterStats.Heal() clamps to maxHealth AND fires OnHealthChanged,
        // which is the event the player HUD subscribes to (the same path
        // PlayerStats.Update() uses for passive health regen). So this both
        // applies the heal and refreshes the bar — no manual GetHealthBar()
        // call needed (that method only exists on EnemyStats, not PlayerStats).
        playerStats.Heal(healAmount);
    }

    //  Cursor Direction 

    // Half-arc used by the block/parry angle test. Widened on gamepad.
    private float BlockHalfAngle()
    {
        bool pad = PlayerAim.Instance != null && PlayerAim.Instance.UsingGamepad;
        return (pad ? BLOCK_ARC_DEGREES_GAMEPAD : BLOCK_ARC_DEGREES) * 0.5f;
    }

    private Vector2 GetCursorDirection()
    {
        if (playerTransform == null) return Vector2.right;

        if (PlayerAim.Instance != null)
            return PlayerAim.Instance.Direction;

        Vector2 mouseScreen = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;

        Vector2 dir = ((Vector2)mouseWorld - (Vector2)playerTransform.position).normalized;
        if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
        return dir;
    }

    //  Visual Arc 

    private void CreateArcVisual()
    {
        arcObject = new GameObject("ShieldArc");

        arcLine = arcObject.AddComponent<LineRenderer>();
        arcMaterial = new Material(Shader.Find("Sprites/Default"));
        arcLine.material = arcMaterial;
        arcLine.startWidth = ARC_WIDTH;
        arcLine.endWidth = ARC_WIDTH;
        arcLine.positionCount = ARC_SEGMENTS + 1;
        arcLine.useWorldSpace = true;
        arcLine.loop = false;
        arcLine.sortingOrder = 9000; // Above most things, below cursor

        // Semi-transparent blue-white
        Color arcColor = new Color(0.6f, 0.8f, 1f, 0.45f);
        arcLine.startColor = arcColor;
        arcLine.endColor = arcColor;
    }

    private void SetArcVisible(bool visible)
    {
        if (arcLine != null)
            arcLine.enabled = visible;
    }

    private void UpdateArcTransform()
    {
        if (arcLine == null || playerTransform == null) return;

        Vector2 cursorDir = GetCursorDirection();
        float centerAngle = Mathf.Atan2(cursorDir.y, cursorDir.x) * Mathf.Rad2Deg;
        float halfArc = VISUAL_ARC_DEGREES * 0.5f;

        Vector3 center = playerTransform.position;

        for (int i = 0; i <= ARC_SEGMENTS; i++)
        {
            float t = (float)i / ARC_SEGMENTS;
            float angle = (centerAngle - halfArc + t * VISUAL_ARC_DEGREES) * Mathf.Deg2Rad;
            Vector3 pos = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ARC_RADIUS;
            arcLine.SetPosition(i, pos);
        }

        // Pulse alpha slightly for visual interest
        float pulse = 0.35f + Mathf.PingPong(Time.time * 2f, 0.2f);
        Color c = arcLine.startColor;
        c.a = pulse;
        arcLine.startColor = c;
        arcLine.endColor = c;
    }

    //  Parry VFX 

    private void SpawnParryVFX()
    {
        if (playerTransform == null) return;

        Vector2 cursorDir = GetCursorDirection();
        Vector3 vfxPos = playerTransform.position + (Vector3)(cursorDir * 1.2f);

        // Create a host object that self-destructs
        GameObject host = new GameObject("ParryVFX");
        host.transform.position = vfxPos;
        var vfx = host.AddComponent<ParryVFXHost>();
        vfx.Play();
    }

    //  Static: Procedural Shield Icon Sprite 

    public static Sprite GetShieldIconSprite()
    {
        if (_shieldIconSprite != null) return _shieldIconSprite;

        // 32x32 procedural shield shape
        const int S = 32;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] px = new Color[S * S];

        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // Shield shape: rounded top, pointed bottom
                float nx = (x - center.x) / (S * 0.5f);  // -1 to 1
                float ny = (y - center.y) / (S * 0.5f);

                // Top half: ellipse; bottom half: triangle taper
                float halfWidth;
                if (ny >= 0f)
                    halfWidth = Mathf.Sqrt(Mathf.Max(0f, 1f - ny * ny)) * 0.85f;
                else
                    halfWidth = 0.85f * (1f + ny * 0.9f); // tapers to point

                bool inside = Mathf.Abs(nx) < halfWidth && ny > -0.95f;

                // Border: slightly smaller inner region
                float borderWidth = 0.15f;
                float innerHalfWidth = halfWidth - borderWidth;
                float innerBottom = -0.95f + borderWidth;
                bool innerInside = Mathf.Abs(nx) < innerHalfWidth && ny > innerBottom && ny < (1f - borderWidth);

                // Cross/emblem in center
                bool crossH = Mathf.Abs(ny - 0.05f) < 0.08f && Mathf.Abs(nx) < 0.25f;
                bool crossV = Mathf.Abs(nx) < 0.08f && ny > -0.25f && ny < 0.35f;
                bool emblem = crossH || crossV;

                if (inside)
                {
                    if (!innerInside || emblem)
                        px[y * S + x] = Color.white;          // border or emblem
                    else
                        px[y * S + x] = new Color(1f, 1f, 1f, 0.5f); // fill
                }
                else
                {
                    px[y * S + x] = Color.clear;
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _shieldIconSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, 32f);
        return _shieldIconSprite;
    }
}

// PARRY VFX HOST
public class ParryVFXHost : MonoBehaviour
{
    private float elapsed = 0f;
    private const float DURATION = 0.45f;

    // Color palette — warm white-gold
    private static readonly Color SHIELD_TINT = new Color(1.00f, 0.95f, 0.72f, 1f);
    private static readonly Color GLOW_TINT = new Color(1.00f, 0.85f, 0.40f, 1f);
    private static readonly Color SHARD_TINT = new Color(1.00f, 0.96f, 0.72f, 1f);

    private SpriteRenderer shieldRenderer;
    private SpriteRenderer glowRenderer;

    private const int SHARD_COUNT = 8;
    private SpriteRenderer[] shardRenderers = new SpriteRenderer[SHARD_COUNT];

    private float baseAngleDeg;

    public void Play()
    {
        baseAngleDeg = Random.Range(0f, 360f);

        // Layer 0: Glow halo (behind everything)
        glowRenderer = MakeChild("Glow", GetGlowSprite(), GLOW_TINT, 9498);
        glowRenderer.transform.localScale = Vector3.zero;

        // Layer 1: Shards (sunburst behind the shield)
        Sprite shardSprite = GetShardSprite();
        for (int i = 0; i < SHARD_COUNT; i++)
        {
            float angle = baseAngleDeg + (360f / SHARD_COUNT) * i;
            SpriteRenderer sr = MakeChild($"Shard_{i}", shardSprite, SHARD_TINT, 9499);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            sr.transform.localScale = Vector3.zero;
            shardRenderers[i] = sr;
        }

        // Layer 2: Shield (hero element, rendered on top)
        shieldRenderer = MakeChild("Shield", GetShieldSprite(), SHIELD_TINT, 9501);
        shieldRenderer.transform.localScale = Vector3.zero;
    }

    private SpriteRenderer MakeChild(string name, Sprite sprite, Color color, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / DURATION);

        // Phase split: slam-in (0–0.10s), hold (0.10–0.25s), fade-out (0.25–0.45s)
        const float slamEnd = 0.10f / 0.45f;
        const float holdEnd = 0.25f / 0.45f;

        // Shield animation: ease-out-back slam, hold, smooth bloom-fade
        float shieldScale, shieldAlpha;
        if (t < slamEnd)
        {
            float k = t / slamEnd;
            shieldScale = EaseOutBack(k);
            shieldAlpha = Mathf.Clamp01(k * 1.5f);
        }
        else if (t < holdEnd)
        {
            shieldScale = 1f;
            shieldAlpha = 1f;
        }
        else
        {
            float k = (t - holdEnd) / (1f - holdEnd);
            shieldScale = Mathf.Lerp(1f, 1.18f, k);
            shieldAlpha = (1f - k) * (1f - k);
        }

        if (shieldRenderer != null)
        {
            shieldRenderer.transform.localScale = Vector3.one * shieldScale;
            Color c = SHIELD_TINT;
            c.a = shieldAlpha;
            shieldRenderer.color = c;
        }

        // Glow halo: ramps in, peaks, fades faster than shield
        if (glowRenderer != null)
        {
            float glowScale, glowAlpha;
            if (t < slamEnd)
            {
                float k = t / slamEnd;
                glowScale = Mathf.Lerp(0.6f, 1.7f, EaseOut(k));
                glowAlpha = k;
            }
            else
            {
                float k = (t - slamEnd) / (1f - slamEnd);
                glowScale = Mathf.Lerp(1.7f, 2.1f, k);
                glowAlpha = Mathf.Lerp(1f, 0f, Mathf.Pow(k, 0.7f));
            }
            glowRenderer.transform.localScale = Vector3.one * glowScale;
            Color c = GLOW_TINT;
            c.a = glowAlpha * 0.55f;
            glowRenderer.color = c;
        }

        // Shards: sunburst — extend, hold briefly, retract
        for (int i = 0; i < SHARD_COUNT; i++)
        {
            SpriteRenderer sr = shardRenderers[i];
            if (sr == null) continue;

            const float shardPeakT = 0.30f;
            float lengthFactor;
            if (t < shardPeakT)
                lengthFactor = EaseOutBack(t / shardPeakT);
            else
                lengthFactor = 1f - Mathf.Pow((t - shardPeakT) / (1f - shardPeakT), 1.3f);

            float lengthScale = Mathf.Max(0f, lengthFactor) * 1.4f;
            float widthScale = Mathf.Lerp(0.12f, 0.03f, t);
            sr.transform.localScale = new Vector3(lengthScale, widthScale, 1f);

            float a = (1f - Mathf.Pow(t, 1.4f)) * 0.7f;
            Color c = SHARD_TINT;
            c.a = a;
            sr.color = c;
        }

        if (elapsed >= DURATION)
            Destroy(gameObject);
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    private static float EaseOut(float x)
    {
        return 1f - (1f - x) * (1f - x);
    }

    //  PROCEDURAL SPRITES — every "outside" pixel is hard alpha=0 

    // Helper: is the point (nx, ny) inside the shield silhouette
    // Coordinates are in normalized texture space, [-1, 1].
    // Shield occupies roughly nx ∈ [-0.78, 0.78], ny ∈ [-0.92, 0.85].
    private static bool IsInsideShield(float nx, float ny)
    {
        // Hard clip outside the bounding region — guarantees full transparency
        // far from the shape, so the texture never renders as a rectangle.
        if (ny > 0.85f || ny < -0.92f) return false;
        if (nx < -0.78f || nx > 0.78f) return false;

        float halfWidth;
        if (ny >= 0f)
        {
            // Upper half: rounded shoulders (ellipse)
            float u = ny / 0.85f;          // 0 at middle, 1 at top
            halfWidth = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u * 0.55f)) * 0.78f;
        }
        else
        {
            // Lower half: heater taper to a soft point
            float u = -ny / 0.92f;         // 0 at middle, 1 at bottom
            halfWidth = 0.78f * (1f - Mathf.Pow(u, 2.2f));
        }

        return Mathf.Abs(nx) <= halfWidth;
    }

    // Returns 0 (outside) to 1 (deeply inside) using 4x4 supersampling
    // for clean anti-aliased edges that don't depend on bilinear filtering.
    private static float ShieldCoverage(float nx, float ny, float pixelSize)
    {
        const int SS = 4; // 4x4 = 16 sub-samples per pixel
        int hits = 0;
        float subStep = pixelSize / SS;
        float startOffset = -pixelSize * 0.5f + subStep * 0.5f;

        for (int sy = 0; sy < SS; sy++)
        {
            for (int sx = 0; sx < SS; sx++)
            {
                float sNx = nx + startOffset + sx * subStep;
                float sNy = ny + startOffset + sy * subStep;
                if (IsInsideShield(sNx, sNy))
                    hits++;
            }
        }
        return hits / (float)(SS * SS);
    }

    // Distance from point to nearest shield edge, sampled coarsely
    // (used to know how far inside we are, for the rim band).
    // Returns 0 at the edge, increasing toward the interior.
    private static float DistanceInsideShield(float nx, float ny)
    {
        if (!IsInsideShield(nx, ny)) return -1f;

        float halfWidth;
        if (ny >= 0f)
        {
            float u = ny / 0.85f;
            halfWidth = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u * 0.55f)) * 0.78f;
        }
        else
        {
            float u = -ny / 0.92f;
            halfWidth = 0.78f * (1f - Mathf.Pow(u, 2.2f));
        }

        // Distance to the side edges (left/right)
        float distHoriz = halfWidth - Mathf.Abs(nx);
        // Distance to top/bottom (approximate, since top is rounded)
        float distTop = 0.85f - ny;
        float distBottom = ny - (-0.92f);
        return Mathf.Min(distHoriz, Mathf.Min(distTop, distBottom));
    }

    private static Sprite _shieldSprite;
    private static Sprite GetShieldSprite()
    {
        if (_shieldSprite != null) return _shieldSprite;
        const int S = 256;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        // alphaIsTransparency is editor-only; removed so Player builds compile (no behavior change — RGBA32 already preserves alpha).
        Color[] px = new Color[S * S];

        // Pixel size in normalized [-1,1] space
        float pixelSize = 2f / (S - 1);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float nx = (x / (float)(S - 1)) * 2f - 1f;
                float ny = (y / (float)(S - 1)) * 2f - 1f;

                // Coverage: 0..1 from supersampling. 0 means fully outside.
                float coverage = ShieldCoverage(nx, ny, pixelSize);

                if (coverage <= 0f)
                {
                    // Hard transparent — no shape contribution, no color leak
                    px[y * S + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                // We're inside or on the edge. Compute rim, fill, emblem.
                float distIn = DistanceInsideShield(nx, ny); // 0 at edge, larger inside

                // Rim: brighter band along the edge (within ~0.06 of the edge)
                float rim = 0f;
                if (distIn >= 0f && distIn < 0.06f)
                {
                    // Smooth peak around distIn ~ 0.03
                    float rimT = distIn / 0.06f;
                    rim = Mathf.Sin(rimT * Mathf.PI); // 0 at edge, 1 at distIn=0.03, 0 at distIn=0.06
                }

                // Fill: warm interior, slightly brighter near top
                float fill = 0.55f;
                fill += Mathf.Clamp01((ny + 0.5f) * 0.4f) * 0.15f; // top brighter
                // Soft vertical highlight stripe down the centerline
                float highlight = Mathf.Exp(-nx * nx * 12f) * 0.20f;
                fill += highlight;

                // Emblem: simple upright cross in the upper-middle of the shield
                float emblem = 0f;
                {
                    float ey = ny - 0.05f; // emblem center
                    bool inVert = Mathf.Abs(nx) < 0.07f && ey > -0.30f && ey < 0.30f;
                    bool inHorz = Mathf.Abs(ey) < 0.07f && Mathf.Abs(nx) < 0.22f;
                    if (inVert || inHorz) emblem = 0.55f;
                }

                // Combine: rim adds bright outline, fill is base, emblem is brighter
                // brightness in [0,1] (multiplied by SpriteRenderer.color tint at runtime)
                float brightness = Mathf.Clamp01(fill + rim * 0.5f + emblem);

                // Alpha: coverage handles edge AA naturally.
                // Fill is semi-opaque; rim/emblem boost alpha for a more solid look on top.
                float alpha = coverage * Mathf.Clamp01(0.85f + rim * 0.3f + emblem * 0.4f);

                px[y * S + x] = new Color(brightness, brightness, brightness, alpha);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _shieldSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _shieldSprite;
    }

    // Soft circular halo glow
    private static Sprite _glowSprite;
    private static Sprite GetGlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;
        const int S = 128;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        // alphaIsTransparency is editor-only; removed so Player builds compile (no behavior change — RGBA32 already preserves alpha).
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float maxR = S * 0.5f;

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x - c.x;
                float dy = y - c.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxR; // 0..>1
                if (d >= 1f)
                {
                    px[y * S + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }
                // Smooth gaussian-ish falloff, fully transparent at the edge
                float a = Mathf.Exp(-d * d * 3.5f) * (1f - d); // hard zero at d=1
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Max(0f, a));
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _glowSprite;
    }

    // Tapered lens shape for sunburst shards (pivot at left edge so it points outward)
    private static Sprite _shardSprite;
    private static Sprite GetShardSprite()
    {
        if (_shardSprite != null) return _shardSprite;
        const int W = 128;
        const int H = 32;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        // alphaIsTransparency is editor-only; removed so Player builds compile (no behavior change — RGBA32 already preserves alpha).
        Color[] px = new Color[W * H];

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = (x / (float)(W - 1)) * 2f - 1f;
                float v = (y / (float)(H - 1)) * 2f - 1f;

                float halfWidth = Mathf.Cos(u * Mathf.PI * 0.5f);
                halfWidth = Mathf.Pow(Mathf.Max(0f, halfWidth), 0.6f);

                if (halfWidth <= 0.001f)
                {
                    px[y * W + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                float across = Mathf.Abs(v) / halfWidth;
                if (across >= 1f)
                {
                    px[y * W + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                float edge = 1f - Mathf.SmoothStep(0.85f, 1.0f, across);
                float core = Mathf.Exp(-across * across * 6f);
                float a = Mathf.Clamp01(edge * 0.7f + core * 0.6f);

                float endFade = Mathf.SmoothStep(1f, 0.92f, Mathf.Abs(u));
                a *= endFade;

                px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _shardSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), W);
        return _shardSprite;
    }
}

