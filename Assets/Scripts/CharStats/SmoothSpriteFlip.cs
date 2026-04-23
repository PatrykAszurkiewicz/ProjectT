using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Smoothly flips a sprite to simulate a volumetric creature turning around.
///
/// Layered illusion tricks (each can be disabled independently):
///   1. PERSPECTIVE HINGE — sprite hinges around its leading edge, not center.
///   2. SQUASH & HOP — vertical squash + small arc hop, sells weight.
///   3. MOTION TRAIL — fading ghost sprites during the flip.
///   4. RIM FLASH — brightness pulse at midpoint.
///   5. MINIMUM WIDTH — sprite never collapses to zero.
///
/// --- Boss-compatible mode ---
/// If useMinimalMode is enabled, ONLY the hinge + squash + hop run. Trail,
/// rim flash, and color writes are disabled. Use this for bosses where:
///   - Other systems read SpriteRenderer.color (damage/armor flashes).
///   - Other systems read SpriteRenderer.flipX to position colliders /
///     health bars / grapple points every frame (they'll jump at the flip's
///     midpoint swap, which is visible on a large sprite).
///   - Ghost sprites at boss-scale create visual noise.
///
/// --- Oscillation debounce ---
/// The boss's laser-attack loop calls SetFacingLeft() many times per second.
/// If the target is near the boss's X-axis, noise can flip the decision back
/// and forth. minTimeBetweenFlips (default 80ms) debounces this — direction
/// changes within that window are ignored, so the flip animation always gets
/// to finish some visible progress before the next one can start.
///
/// --- Smooth reversal ---
/// If SetFacingLeft() is called with the opposite direction WHILE a flip is
/// in progress (and past the debounce window), the animation MIRRORS its
/// progress rather than restarting. Prevents shrink-expand flicker.
///
/// --- Write hygiene ---
/// Driven from LateUpdate, after physics. Peels off last frame's additive
/// position/rotation/color writes before applying this frame's, so we don't
/// drift or compound with physics or other scripts.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SmoothSpriteFlip : MonoBehaviour
{
    [Header("Mode")]
    [Tooltip("ON for bosses or any sprite whose flipX / color / position is read by other scripts every frame. Disables trail, rim flash, color writes, AND the perspective hinge (which writes transform.position.x). In minimal mode the flip uses symmetric squash + hop only — no horizontal translation of the GameObject.")]
    [SerializeField] private bool useMinimalMode = false;

    [Header("Timing")]
    [Tooltip("How long the flip takes, in seconds. 0.22–0.30 reads as volumetric rotation.")]
    [SerializeField] private float flipDuration = 0.26f;

    [Tooltip("Minimum seconds between direction changes. Debounces rapid oscillation when SetFacingLeft is called many times per second with jittering direction.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float minTimeBetweenFlips = 0.08f;

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
    /// idempotent. Rapid oscillations are debounced via minTimeBetweenFlips.
    /// Mid-flip direction reversals are handled smoothly without restart.
    /// </summary>
    public void SetFacingLeft(bool shouldFaceLeft)
    {
        // Same as target → no-op.
        if (shouldFaceLeft == facingLeft) return;

        // Debounce: too soon after last flip start → swallow this change.
        // This matters most on the boss, which calls this dozens of times
        // per second during the laser attack while the target jitters.
        if (Time.time - lastFlipStartTime < minTimeBetweenFlips) return;

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

    /// <summary>
    /// Enable minimal mode at runtime. Boss1 calls this so auto-added
    /// components get the boss-compatible config without needing the
    /// component pre-placed on the prefab with the right toggle.
    /// </summary>
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

        if (!isFlipping)
        {
            if (transform.localScale != baseScale) transform.localScale = baseScale;
            return;
        }

        flipT += Time.deltaTime;
        float k = Mathf.Clamp01(flipT / Mathf.Max(0.0001f, flipDuration));
        float eased = k * k * (3f - 2f * k);

        // --- Motion trail (skipped entirely in minimal mode) ---
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

        // --- X SCALE ---
        float fullAbsX = baseScaleAbsX;
        float minAbsX = baseScaleAbsX * minWidthFraction;
        float absX;
        if (eased < 0.5f)
            absX = Mathf.Lerp(flipStartAbsX, minAbsX, eased * 2f);
        else
            absX = Mathf.Lerp(minAbsX, fullAbsX, (eased - 0.5f) * 2f);

        // --- Y SQUASH ---
        float squeeze = 1f - Mathf.Abs(eased - 0.5f) * 2f;
        float absY = absBaseY * (1f - squashAmount * squeeze);

        transform.localScale = new Vector3(absX, absY * baseYSign, baseScale.z);

        // --- PERSPECTIVE HINGE ---
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

        // --- HOP ---
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

        // --- LEAN ---
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

        // --- RIM FLASH (skipped entirely in minimal mode) ---
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
