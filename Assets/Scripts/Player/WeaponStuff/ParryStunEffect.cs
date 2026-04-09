using UnityEngine;

// PARRY STUN EFFECT
// Added to an enemy when the player successfully parries their attack.
// - Stuns the enemy for a configurable duration (3s normal, 2s boss).
// - Increases damage taken by 30% for the stun duration.
// - Shows a visual indicator (yellow tint + stars VFX).
// - Self-destructs when the effect expires.
// Stun works by:
//   1. Freezing the EnemyController (sets velocity to zero, blocks movement).
//   2. Stopping the EnemyAnimationController.
//   3. Applying a damage multiplier via a simple component check in TakeDamage.

public class ParryStunEffect : MonoBehaviour
{
    private float stunDuration;
    private float damageBonus;
    private float elapsed = 0f;
    private bool initialized = false;

    // Cached references
    private EnemyController enemyController;
    private EnemyAnimationController animController;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Color originalColor;

    // Visual: rotating stars around the stunned enemy
    private GameObject starsHost;
    private SpriteRenderer[] starRenderers;
    private const int STAR_COUNT = 5;
    private const float STAR_ORBIT_RADIUS = 0.55f;
    private const float STAR_ROTATE_SPEED = 200f; // degrees per second
    private const float STAR_Y_OFFSET = 0.7f; // above enemy center (near head)


    /// Public accessor: the bonus damage multiplier while stunned.
    /// Other systems can check: enemy.GetComponent<ParryStunEffect>()?.DamageMultiplier ?? 1f

    public float DamageMultiplier => 1f + damageBonus;

    public void Initialize(float duration, float dmgBonus)
    {
        stunDuration = duration;
        damageBonus = dmgBonus;
        elapsed = 0f;
        initialized = true;

        CacheReferences();
        ApplyStun();
        CreateStarsVFX();
    }

    public void Refresh(float duration, float dmgBonus)
    {
        stunDuration = duration;
        damageBonus = dmgBonus;
        elapsed = 0f;
        // Re-apply stun in case it partially wore off
        ApplyStun();
    }

    private void CacheReferences()
    {
        enemyController = GetComponent<EnemyController>();
        animController = GetComponent<EnemyAnimationController>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();

        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;
    }

    private void ApplyStun()
    {
        // Stop movement
        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        // Freeze the attack animation — stop the melee attack coroutine
        // so the enemy holds on its current frame (stunned pose)
        if (animController != null)
        {
            animController.StopMeleeAttackAnimation();
            animController.FreezeAnimation();
        }

        // Visual: yellow-ish tint to indicate stun
        if (spriteRenderer != null)
            spriteRenderer.color = new Color(1f, 1f, 0.5f, originalColor.a);
    }

    private void Update()
    {
        if (!initialized) return;

        elapsed += Time.deltaTime;

        // Keep the enemy frozen during stun
        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        // Animate stars — 3D-perspective elliptical orbit around the enemy's head.
        // Stars orbit in an ellipse (wide horizontally, compressed vertically)
        // to simulate a tilted circular path viewed from the side.
        // Stars in "front" (bottom of ellipse) appear larger, stars in "back" smaller.
        if (starsHost != null)
        {
            // Follow the enemy position (offset upward near head)
            starsHost.transform.position = transform.position + Vector3.up * STAR_Y_OFFSET;
            // Don't rotate the host — we position each star manually for the 3D effect.
            starsHost.transform.rotation = Quaternion.identity;

            float orbitAngle = elapsed * STAR_ROTATE_SPEED * Mathf.Deg2Rad;
            float fadeT = Mathf.Clamp01((elapsed - (stunDuration - 0.5f)) / 0.5f);

            if (starRenderers != null)
            {
                for (int i = 0; i < starRenderers.Length; i++)
                {
                    if (starRenderers[i] == null) continue;

                    // Each star is evenly spaced around the orbit
                    float starAngle = orbitAngle + (Mathf.PI * 2f / STAR_COUNT) * i;

                    // Elliptical orbit: wide X, compressed Y (perspective tilt)
                    float x = Mathf.Cos(starAngle) * STAR_ORBIT_RADIUS;
                    float y = Mathf.Sin(starAngle) * STAR_ORBIT_RADIUS * 0.35f; // squash Y for perspective

                    starRenderers[i].transform.localPosition = new Vector3(x, y, 0f);

                    // Depth simulation: Sin > 0 means "behind" the head, Sin < 0 means "in front"
                    // Stars in front are larger and brighter, stars behind are smaller and dimmer
                    float depth = Mathf.Sin(starAngle); // -1 (front) to +1 (back)
                    float depthScale = Mathf.Lerp(0.45f, 0.22f, (depth + 1f) * 0.5f);
                    starRenderers[i].transform.localScale = Vector3.one * depthScale;

                    // Sorting: stars in front render above, stars behind render below
                    starRenderers[i].sortingOrder = depth < 0f ? 9201 : 9199;

                    // Alpha: dimmer when behind, brighter when in front; fade out at end of stun
                    float depthAlpha = Mathf.Lerp(1f, 0.45f, (depth + 1f) * 0.5f);
                    Color c = starRenderers[i].color;
                    c.a = depthAlpha * (1f - fadeT);
                    starRenderers[i].color = c;
                }
            }
        }

        // Blink sprite near end of stun to warn
        if (elapsed > stunDuration - 0.8f && spriteRenderer != null)
        {
            float blink = Mathf.PingPong(elapsed * 8f, 1f);
            spriteRenderer.color = Color.Lerp(
                new Color(1f, 1f, 0.5f, originalColor.a),
                originalColor,
                blink);
        }

        if (elapsed >= stunDuration)
            RemoveEffect();
    }

    private void RemoveEffect()
    {
        // Restore original color
        if (spriteRenderer != null)
            spriteRenderer.color = originalColor;

        // Unfreeze animation so it can resume
        if (animController != null)
            animController.UnfreezeAnimation();

        // Clean up VFX
        if (starsHost != null)
            Destroy(starsHost);

        Destroy(this);
    }

    private void OnDestroy()
    {
        // Safety cleanup
        if (starsHost != null)
            Destroy(starsHost);

        // Restore color if we still have a reference
        if (spriteRenderer != null && initialized)
            spriteRenderer.color = originalColor;

        // Unfreeze animation on destroy (safety)
        if (animController != null && initialized)
            animController.UnfreezeAnimation();
    }

    //  Stars VFX 

    private void CreateStarsVFX()
    {
        starsHost = new GameObject("ParryStunStars");
        starsHost.transform.position = transform.position + Vector3.up * STAR_Y_OFFSET;

        starRenderers = new SpriteRenderer[STAR_COUNT];
        Sprite starSprite = GetStarSprite();

        for (int i = 0; i < STAR_COUNT; i++)
        {
            float angle = (360f / STAR_COUNT) * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * STAR_ORBIT_RADIUS,
                Mathf.Sin(angle) * STAR_ORBIT_RADIUS,
                0f);

            GameObject starGO = new GameObject($"Star_{i}");
            starGO.transform.SetParent(starsHost.transform, false);
            starGO.transform.localPosition = offset;
            starGO.transform.localScale = Vector3.one * 0.35f;

            SpriteRenderer sr = starGO.AddComponent<SpriteRenderer>();
            sr.sprite = starSprite;
            sr.sortingOrder = 9200;
            // Alternate between yellow and white for variety
            sr.color = (i % 2 == 0)
                ? new Color(1f, 1f, 0.3f, 1f)   // bright yellow
                : new Color(1f, 0.95f, 0.6f, 1f); // warm white

            starRenderers[i] = sr;
        }
    }

    // Procedural 4-pointed star sprite
    private static Sprite _starSprite;
    private static Sprite GetStarSprite()
    {
        if (_starSprite != null) return _starSprite;

        const int S = 16;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] px = new Color[S * S];

        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float nx = (x - center.x) / (S * 0.5f); // -1 to 1
                float ny = (y - center.y) / (S * 0.5f);

                // 4-pointed star: union of two rotated diamonds
                float d1 = Mathf.Abs(nx) + Mathf.Abs(ny); // diamond
                float rx = nx * 0.707f - ny * 0.707f;     // rotated 45°
                float ry = nx * 0.707f + ny * 0.707f;
                float d2 = Mathf.Abs(rx) + Mathf.Abs(ry);

                // Thin in one axis, wider in the other → star spikes
                float star = Mathf.Min(
                    Mathf.Abs(nx) * 3f + Mathf.Abs(ny),
                    Mathf.Abs(nx) + Mathf.Abs(ny) * 3f);

                bool inside = star < 1.2f;

                if (inside)
                {
                    float brightness = 1f - star / 1.2f;
                    px[y * S + x] = new Color(1f, 1f, 1f, brightness);
                }
                else
                {
                    px[y * S + x] = Color.clear;
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _starSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _starSprite;
    }
}
