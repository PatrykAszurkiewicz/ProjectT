using System.Collections.Generic;
using UnityEngine;

/// Smoothly flips a sprite to simulate a volumetric creature turning around.
/// Layered illusion tricks (each can be disabled independently):
/// If useMinimalMode is enabled, ONLY the hinge + squash + hop run. Trail,
/// rim flash, and color writes are disabled. Use this for bosses where:
///   - Other systems read SpriteRenderer.color (damage/armor flashes).
///   - Other systems read SpriteRenderer.flipX to position colliders /
///     health bars / grapple points every frame (they'll jump at the flip's
///     midpoint swap, which is visible on a large sprite).
///   - Ghost sprites at boss-scale create visual noise.

[RequireComponent(typeof(SpriteRenderer))]
public class SmoothSpriteFlip : MonoBehaviour
{
    // GLOBAL MASTER SWITCH
    // false = every enemy and boss flips instantly (plain flipX — no squash,
    //         hop, hinge, trail or rim flash).
    // true  = the animated flip.

    private const bool DEFAULT_SMOOTH_FLIP = false;

    public static bool SmoothFlipEnabled = DEFAULT_SMOOTH_FLIP;

    // Re-seeds the switch every play session. Also guards against statics
    // surviving Play-Mode restarts when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitSwitch()
    {
#if UNITY_EDITOR
        SmoothFlipEnabled = UnityEditor.EditorPrefs.GetBool(MenuPrefKey, DEFAULT_SMOOTH_FLIP);
#else
        SmoothFlipEnabled = DEFAULT_SMOOTH_FLIP;
#endif
    }

#if UNITY_EDITOR
    // Menu-bar checkbox. Lives here rather than in an Editor script so no
    // extra file is needed — MenuItem works fine from a runtime class as
    // long as it's static and editor-only.
    private const string MenuPrefKey = "SmoothSpriteFlip.Enabled";
    private const string MenuPath = "Tools/Smooth Sprite Flip";

    [UnityEditor.MenuItem(MenuPath)]
    private static void ToggleFromMenu()
    {
        bool v = !UnityEditor.EditorPrefs.GetBool(MenuPrefKey, DEFAULT_SMOOTH_FLIP);
        UnityEditor.EditorPrefs.SetBool(MenuPrefKey, v);
        SmoothFlipEnabled = v; // applies immediately, mid-play included
    }

    [UnityEditor.MenuItem(MenuPath, true)]
    private static bool ToggleFromMenuValidate()
    {
        UnityEditor.Menu.SetChecked(
            MenuPath, UnityEditor.EditorPrefs.GetBool(MenuPrefKey, DEFAULT_SMOOTH_FLIP));
        return true;
    }
#endif

    [Header("Mode")]
    [Tooltip("Per-instance opt-out. Even with the global switch ON, this " +
             "sprite flips instantly when unchecked. Has no effect while " +
             "SmoothSpriteFlip.SmoothFlipEnabled is false.")]
    [SerializeField] private bool allowSmoothFlip = true;

    // Single source of truth used by SetFacingLeft and LateUpdate.
    private bool UseSmoothFlip => SmoothFlipEnabled && allowSmoothFlip;

    [Tooltip("ON for bosses or any sprite whose flipX / color / position is read by other scripts every frame. Disables trail, rim flash, color writes, AND the perspective hinge (which writes transform.position.x). In minimal mode the flip uses symmetric squash + hop only — no horizontal translation of the GameObject.")]
    [SerializeField] private bool useMinimalMode = false;

    [Header("Timing")]
    [Tooltip("How long the flip takes, in seconds. 0.22–0.30 reads as volumetric rotation.")]
    [SerializeField] private float flipDuration = 0.26f;

    [Tooltip("Minimum seconds between direction changes. Debounces rapid oscillation when SetFacingLeft is called many times per second with jittering direction.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float minTimeBetweenFlips = 0.08f;

    [Header("Flip Tolerance (applies with the smooth effect ON or OFF)")]
    [Tooltip("A new direction must be requested CONTINUOUSLY for this long " +
             "before the sprite actually turns. This is what stops enemies " +
             "strobing left/right while they scrape along obstacles: the " +
             "jitter never sustains, so it never gets honoured. A real turn " +
             "just costs this much delay. 0 = turn the instant it's asked " +
             "(old behaviour). 0.15–0.25 feels tolerant without looking slow.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float flipConfirmDelay = 0.18f;

    [Tooltip("Hold required to reverse a flip that JUST happened. Turning back " +
             "is treated as more suspicious than turning for the first time, " +
             "which is what kills the left-right-left weave between obstacles. " +
             "Decays back down to flipConfirmDelay over flipBackWindow. Set " +
             "equal to flipConfirmDelay to disable this.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float flipBackDelay = 0.45f;

    [Tooltip("How long a flip counts as 'just happened'. Within this window a " +
             "reversal needs the longer flipBackDelay hold; after it, facing is " +
             "considered settled and normal rules resume. Also the calm period " +
             "that clears the escalation below.")]
    [Range(0f, 3f)]
    [SerializeField] private float flipBackWindow = 1.0f;

    [Tooltip("Escalation: each flip that lands while still inside flipBackWindow " +
             "multiplies the next required hold, so an enemy that keeps changing " +
             "its mind progressively commits to a side instead of strobing. " +
             "This is the ceiling on that — the longest a turn can ever be made " +
             "to wait. Lower = more responsive, more strobe-prone.")]
    [Range(0f, 2f)]
    [SerializeField] private float maxFlipHold = 1.0f;

    // How many stacked rapid flips the escalation counts before it saturates.
    private const int MaxEscalationSteps = 3;
    private int rapidFlipCount;

    // If nobody calls SetFacingLeft for this long, a half-confirmed turn is
    // forgotten. Without it, an enemy that requested "left" for 0.1s, stopped,
    // then asked again minutes later would turn instantly — the pending timer
    // would look like it had been satisfied all along. A few frames is plenty.
    private const float RequestStaleTime = 0.12f;

    // Pending (requested but not yet confirmed) direction.
    private bool pendingFacingLeft;
    private bool hasPending;
    private float pendingSince;
    private float lastRequestTime = -999f;

    [Header("Perspective Hinge")]
    [Tooltip("Minimum X-scale fraction at the flip midpoint. Higher = more volumetric, less dramatic rotation.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float minWidthFraction = 0.38f;

    [Tooltip("How strongly the sprite hinges around its leading edge (1 = full hinge, 0 = symmetric scale from center).")]
    [Range(0f, 1f)]
    [SerializeField] private float hingeStrength = 0.85f;

    [Header("Mass & Weight")]
    [Tooltip("Vertical squash at the midpoint.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float squashAmount = 0.14f;

    [Tooltip("Upward hop at the midpoint (world units, scales with object size).")]
    [Range(0f, 0.5f)]
    [SerializeField] private float hopHeight = 0.08f;

    [Header("Motion Trail (disabled in minimal mode)")]
    [SerializeField] private bool enableTrail = true;

    [Tooltip("If the sprite's world-space width exceeds this, trail is auto-disabled.")]
    [SerializeField] private float trailMaxSpriteWidth = 3f;

    [Range(0, 8)][SerializeField] private int trailGhostCount = 4;
    [Range(0f, 1f)][SerializeField] private float trailStartAlpha = 0.45f;
    [Range(0.05f, 1f)][SerializeField] private float trailFadeDuration = 0.25f;

    [Header("Rim Flash (disabled in minimal mode)")]
    [Range(0f, 1f)][SerializeField] private float rimFlashStrength = 0.35f;

    [Header("Advanced")]
    [Range(0f, 25f)][SerializeField] private float leanAngle = 6f;
    [SerializeField] private bool applyLean = false;

    // Cached resting state.
    private SpriteRenderer spriteRenderer;
    private Vector3 baseScale;
    private float baseScaleAbsX;
    private float absBaseY;
    private float baseYSign;

    // Logical facing (source of truth during animation).
    private bool facingLeft = false;

    // Flip state.
    private bool isFlipping = false;
    private float flipT = 0f;
    private float flipStartAbsX = 1f;
    private bool flipSwappedMirror = false;
    private float lastFlipStartTime = -999f;

    // Residuals for clean undo next frame.
    private float lastHopOffset = 0f;
    private float lastHingeOffsetX = 0f;
    private float lastLeanAngle = 0f;
    private float lastRimFlashAdded = 0f;

    // Trail.
    private float trailSpawnTimer = 0f;
    private readonly List<GhostSprite> activeGhosts = new List<GhostSprite>();

    private class GhostSprite
    {
        public GameObject go;
        public SpriteRenderer sr;
        public float life;
        public float maxLife;
        public float startAlpha;
    }

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        CaptureBase();
        facingLeft = spriteRenderer.flipX;
    }

    private void CaptureBase()
    {
        baseScale = transform.localScale;
        baseScaleAbsX = Mathf.Abs(baseScale.x);
        if (baseScaleAbsX < 0.0001f) baseScaleAbsX = 1f;
        absBaseY = Mathf.Abs(baseScale.y);
        baseYSign = baseScale.y < 0 ? -1f : 1f;
    }

    /// <summary>
    /// Request the sprite to face left (true) or right (false). Safe to spam;
    /// idempotent. The request must persist for flipConfirmDelay before it's
    /// honoured, so jittery direction noise (obstacle scraping, a target
    /// hovering near our X axis) is filtered out instead of strobing the
    /// sprite. Mid-flip direction reversals are handled smoothly.
    /// </summary>
    public void SetFacingLeft(bool shouldFaceLeft)
    {
        float now = Time.time;

        // Did we skip enough frames that the pending request went cold?
        bool stale = now - lastRequestTime > RequestStaleTime;
        lastRequestTime = now;

        // Already facing that way → nothing to do, and any opposite request
        // still in flight is abandoned. THIS is the hysteresis: a jittering
        // caller alternates directions, so each request cancels the previous
        // one's progress and neither ever reaches flipConfirmDelay.
        if (shouldFaceLeft == facingLeft)
        {
            hasPending = false;
            return;
        }

        // Start (or restart) the confirmation window.
        if (!hasPending || pendingFacingLeft != shouldFaceLeft || stale)
        {
            hasPending = true;
            pendingFacingLeft = shouldFaceLeft;
            pendingSince = now;
        }

        // How long must this request be held to be honoured? Reversing a flip
        // we just made is treated as suspicious: the requirement starts at
        // flipBackDelay right after a flip and decays to the normal
        // flipConfirmDelay once flipBackWindow has passed without one.
        float sinceFlip = now - lastFlipStartTime;
        if (sinceFlip > flipBackWindow) rapidFlipCount = 0; // settled → forgiven

        float settled = flipBackWindow <= 0f ? 1f : Mathf.Clamp01(sinceFlip / flipBackWindow);
        float required = Mathf.Lerp(
            Mathf.Max(flipBackDelay, flipConfirmDelay), flipConfirmDelay, settled);

        // Escalation: each flip that lands while still inside the window makes
        // the next one wait longer, so an enemy weaving between two obstacles
        // stops arguing with itself and commits to a side.
        required *= 1f + rapidFlipCount;
        required = Mathf.Min(required, Mathf.Max(maxFlipHold, flipConfirmDelay));

        // Not held long enough yet — keep waiting.
        if (now - pendingSince < required) return;

        // Confirmed, but too soon after the last flip. Keep the request
        // pending so it lands the moment the cooldown expires.
        if (now - lastFlipStartTime < minTimeBetweenFlips) return;

        hasPending = false;
        CommitFlip(shouldFaceLeft);
    }

    // The actual turn, once the request has earned it.
    private void CommitFlip(bool shouldFaceLeft)
    {
        // Count this flip against the streak BEFORE lastFlipStartTime moves.
        // Landing inside flipBackWindow of the previous flip = indecisive;
        // outside it = a clean, settled turn that resets the escalation.
        rapidFlipCount = (Time.time - lastFlipStartTime <= flipBackWindow)
            ? Mathf.Min(rapidFlipCount + 1, MaxEscalationSteps)
            : 0;

        // ---- INSTANT MODE ----
        // No animation: just mirror the renderer. LateUpdate cleans up any
        // in-progress flip on the next frame if we were toggled off mid-flip.
        if (!UseSmoothFlip)
        {
            facingLeft = shouldFaceLeft;
            if (spriteRenderer != null) spriteRenderer.flipX = facingLeft;
            lastFlipStartTime = Time.time;
            return;
        }

        // Direction change.
        if (isFlipping)
        {
            // Mid-flip reversal: mirror the current progress rather than
            // restarting. Sprite continues from its current visual state
            // back toward full-width facing the new direction.
            flipT = Mathf.Max(0f, flipDuration - flipT);
            flipStartAbsX = Mathf.Abs(transform.localScale.x);
            flipSwappedMirror = false;
            facingLeft = shouldFaceLeft;
        }
        else
        {
            // Fresh flip from rest.
            facingLeft = shouldFaceLeft;
            flipStartAbsX = Mathf.Abs(transform.localScale.x);
            flipT = 0f;
            flipSwappedMirror = false;
            isFlipping = true;
            trailSpawnTimer = 0f;
        }

        lastFlipStartTime = Time.time;
    }

    public bool IsFacingLeft => facingLeft;
    public bool IsFlipping => isFlipping;
    public void RecaptureBaseScale() => CaptureBase();

    /// Enable minimal mode at runtime. Boss1 calls this so auto-added
    /// components get the boss-compatible config without needing the
    /// component pre-placed on the prefab with the right toggle.
    public void SetMinimalMode(bool minimal) => useMinimalMode = minimal;

    private void LateUpdate()
    {
        // Peel off last frame's contributions before anything reads the transform.
        if (lastHopOffset != 0f || lastHingeOffsetX != 0f)
        {
            transform.position -= new Vector3(lastHingeOffsetX, lastHopOffset, 0f);
            lastHopOffset = 0f;
            lastHingeOffsetX = 0f;
        }
        if (applyLean && lastLeanAngle != 0f)
        {
            transform.rotation *= Quaternion.Euler(0f, 0f, -lastLeanAngle);
            lastLeanAngle = 0f;
        }
        // Peel off color contribution — subtract what we added, preserving any
        // change another script made (damage flash, etc.). If we didn't add
        // anything, leave color alone entirely.
        if (!useMinimalMode && lastRimFlashAdded != 0f && spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.r = Mathf.Clamp01(c.r - lastRimFlashAdded);
            c.g = Mathf.Clamp01(c.g - lastRimFlashAdded);
            c.b = Mathf.Clamp01(c.b - lastRimFlashAdded);
            spriteRenderer.color = c;
            lastRimFlashAdded = 0f;
        }

        UpdateGhosts();

        // Instant mode: nothing to animate. The residual peel above already
        // ran, so any leftover hop / hinge / rim-flash from the frame we were
        // switched off is undone here.
        if (!UseSmoothFlip)
        {
            if (isFlipping) // toggled off mid-flip → snap to rest
            {
                transform.localScale = baseScale;
                if (spriteRenderer != null) spriteRenderer.flipX = facingLeft;
                isFlipping = false;
            }
            return; // deliberately does NOT re-write localScale every frame
        }

        if (!isFlipping)
        {
            if (transform.localScale != baseScale) transform.localScale = baseScale;
            return;
        }

        flipT += Time.deltaTime;
        float k = Mathf.Clamp01(flipT / Mathf.Max(0.0001f, flipDuration));
        float eased = k * k * (3f - 2f * k);

        //  Motion trail (skipped entirely in minimal mode) 
        if (!useMinimalMode && enableTrail && trailGhostCount > 0 && spriteRenderer.sprite != null)
        {
            float worldWidth = spriteRenderer.sprite.bounds.size.x * baseScaleAbsX;
            if (worldWidth <= trailMaxSpriteWidth)
            {
                float spawnInterval = flipDuration / (trailGhostCount + 1);
                trailSpawnTimer += Time.deltaTime;
                if (trailSpawnTimer >= spawnInterval && k > 0.1f && k < 0.85f)
                {
                    trailSpawnTimer = 0f;
                    SpawnGhost();
                }
            }
        }

        //  X SCALE 
        float fullAbsX = baseScaleAbsX;
        float minAbsX = baseScaleAbsX * minWidthFraction;
        float absX;
        if (eased < 0.5f)
            absX = Mathf.Lerp(flipStartAbsX, minAbsX, eased * 2f);
        else
            absX = Mathf.Lerp(minAbsX, fullAbsX, (eased - 0.5f) * 2f);

        //  Y SQUASH 
        float squeeze = 1f - Mathf.Abs(eased - 0.5f) * 2f;
        float absY = absBaseY * (1f - squashAmount * squeeze);

        transform.localScale = new Vector3(absX, absY * baseYSign, baseScale.z);

        //  PERSPECTIVE HINGE 
        // Skipped in minimal mode: the hinge writes transform.position.x,
        // which is read each frame by the boss's health bar, collider,
        // grapple point, and laser origin code. Even though we peel the
        // offset off at the next LateUpdate, within a single frame those
        // systems sample the shifted position and render in the wrong
        // place — producing a visible horizontal shift on the boss.
        // In minimal mode we fall back to symmetric scale around center.
        float hingeOffset = 0f;
        if (!useMinimalMode)
        {
            float widthPerScale = 0f;
            if (spriteRenderer.sprite != null)
                widthPerScale = spriteRenderer.sprite.bounds.size.x;

            float shrinkAmount = fullAbsX - absX;
            float halfShrinkWorld = shrinkAmount * widthPerScale * 0.5f;
            float hingeSign = facingLeft ? -1f : 1f;
            hingeOffset = halfShrinkWorld * hingeSign * hingeStrength;
        }

        //  HOP 
        // Kept in minimal mode: writes transform.position.y. Y-axis movement
        // is benign for the systems reading position (health bar tracks the
        // boss vertically anyway, collider offset doesn't care about Y jitter).
        float hopOffset = Mathf.Sin(eased * Mathf.PI) * (hopHeight * baseScaleAbsX);

        if (hopOffset != 0f || hingeOffset != 0f)
        {
            transform.position += new Vector3(hingeOffset, hopOffset, 0f);
            lastHopOffset = hopOffset;
            lastHingeOffsetX = hingeOffset;
        }

        //  LEAN 
        if (applyLean)
        {
            float leanDir = facingLeft ? 1f : -1f;
            float leanCurve = Mathf.Sin(eased * Mathf.PI) *
                              Mathf.Cos(eased * Mathf.PI * 0.5f);
            float angle = leanCurve * leanAngle * leanDir;
            if (angle != 0f)
            {
                transform.rotation *= Quaternion.Euler(0f, 0f, angle);
                lastLeanAngle = angle;
            }
        }

        //  RIM FLASH (skipped entirely in minimal mode) 
        if (!useMinimalMode && rimFlashStrength > 0f && squeeze > 0.01f)
        {
            float flash = squeeze * rimFlashStrength;
            Color c = spriteRenderer.color;
            c.r = Mathf.Clamp01(c.r + flash);
            c.g = Mathf.Clamp01(c.g + flash);
            c.b = Mathf.Clamp01(c.b + flash);
            spriteRenderer.color = c;
            lastRimFlashAdded = flash;
        }

        // Mirror swap at midpoint.
        if (!flipSwappedMirror && eased >= 0.5f)
        {
            spriteRenderer.flipX = facingLeft;
            flipSwappedMirror = true;
        }

        // End of flip.
        if (k >= 1f)
        {
            transform.localScale = baseScale;
            spriteRenderer.flipX = facingLeft;
            isFlipping = false;
        }
    }

    private void SpawnGhost()
    {
        if (spriteRenderer.sprite == null) return;

        GameObject g = new GameObject("FlipGhost");
        g.transform.position = transform.position;
        g.transform.rotation = transform.rotation;
        g.transform.localScale = transform.localScale;

        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = spriteRenderer.sprite;
        sr.flipX = spriteRenderer.flipX;
        sr.sortingLayerID = spriteRenderer.sortingLayerID;
        sr.sortingOrder = spriteRenderer.sortingOrder - 1;
        Color c = spriteRenderer.color;
        c.a = trailStartAlpha;
        sr.color = c;

        activeGhosts.Add(new GhostSprite
        {
            go = g,
            sr = sr,
            life = 0f,
            maxLife = trailFadeDuration,
            startAlpha = trailStartAlpha
        });
    }

    private void UpdateGhosts()
    {
        if (activeGhosts.Count == 0) return;

        for (int i = activeGhosts.Count - 1; i >= 0; i--)
        {
            GhostSprite g = activeGhosts[i];
            g.life += Time.deltaTime;
            float t = g.life / g.maxLife;

            if (t >= 1f || g.go == null)
            {
                if (g.go != null) Destroy(g.go);
                activeGhosts.RemoveAt(i);
                continue;
            }

            if (g.sr != null)
            {
                Color c = g.sr.color;
                c.a = Mathf.Lerp(g.startAlpha, 0f, t);
                g.sr.color = c;
            }
            g.go.transform.localScale *= 0.995f;
        }
    }

    void OnDisable()
    {
        for (int i = activeGhosts.Count - 1; i >= 0; i--)
            if (activeGhosts[i].go != null) Destroy(activeGhosts[i].go);
        activeGhosts.Clear();

        if (spriteRenderer != null) spriteRenderer.flipX = facingLeft;
        transform.localScale = baseScale;
        isFlipping = false;
    }
}

