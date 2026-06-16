using UnityEngine;

// PARRY STUN EFFECT
// Added to an enemy when the player successfully parries their attack (melee OR
// projectile parry — both routes call ParryStunEffect.ApplyOrRefresh).
// Two DECOUPLED phases (so the upgrade augments can tune them independently):
//   STUN FREEZE  : enemy is frozen (no movement, attack animation halted) for
//                    `freezeDuration` seconds = base stun + ParryUpgrades.ExtraStunSeconds
//                    (Augment 330 "Longer Parry Stun").
//   DAMAGE DEBUFF: enemy takes extra damage for `debuffDuration` seconds. By
//                    default this matches the stun; with Augment 331 "Powerful
//                    Parry" it uses ParryUpgrades.PowerfulParry* (e.g. +30% for 5s)
//                    and can OUTLAST the freeze.
public class ParryStunEffect : MonoBehaviour
{
    // Phase durations / amounts (resolved from ParryUpgrades at apply time)
    private float freezeDuration;   // stun freeze length
    private float debuffDuration;   // how long the damage bonus lasts
    private float damageBonus;      // e.g. 0.30 = +30%

    private float elapsed = 0f;
    private bool initialized = false;
    private bool stunReleased = false;

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


    /// Public accessor: the bonus damage multiplier while the debuff is active.
    /// Other systems can check: enemy.GetComponent<ParryStunEffect>()?.DamageMultiplier ?? 1f
    /// Returns 1f once the damage debuff has expired (even if the component is
    /// still lingering for some reason).
    public float DamageMultiplier =>
        (initialized && elapsed < debuffDuration) ? 1f + damageBonus : 1f;

    /// True while the enemy should be FROZEN by the stun. Once the freeze phase
    /// ends the enemy may move/attack again even if a longer damage debuff is
    /// still ticking. EnemyController uses this to gate movement/attacks.
    public bool IsStunActive => initialized && elapsed < freezeDuration;


    // Convenience entry point used by both the melee parry (ShieldSystem) and any
    // projectile-parry code
    // Phase 8: the parry upgrades applied come from the PARRYING player. Pass
    // their index so Longer Parry Stun (330) / Powerful Parry (331) read that
    // player's values. The old 1-arg form below routes to player 0 (back-compat).
    public static ParryStunEffect ApplyOrRefresh(GameObject enemyGO, int parryingIndex)
    {
        if (enemyGO == null) return null;

        bool isBoss = enemyGO.GetComponent<BaseBossStats>() != null;
        float baseStun = isBoss ? ParryUpgrades.BaseStunBoss : ParryUpgrades.BaseStunNormal;

        var existing = enemyGO.GetComponent<ParryStunEffect>();
        if (existing != null)
        {
            existing.Refresh(baseStun, ParryUpgrades.BaseDamageBonus, parryingIndex);
            return existing;
        }

        var effect = enemyGO.AddComponent<ParryStunEffect>();
        effect.Initialize(baseStun, ParryUpgrades.BaseDamageBonus, parryingIndex);
        return effect;
    }

    // Back-compat (player 0) — used by call sites not yet converted.
    public static ParryStunEffect ApplyOrRefresh(GameObject enemyGO)
        => ApplyOrRefresh(enemyGO, 0);


    // Backwards-compatible 2-arg entry point. `baseStunDuration` and
    // `baseDamageBonus` are the un-upgraded values; ParryUpgrades is layered on
    // here so EVERY caller (melee or projectile) gets the augments for free.
    public void Initialize(float baseStunDuration, float baseDamageBonus, int parryingIndex = 0)
    {
        ResolveDurations(baseStunDuration, baseDamageBonus, parryingIndex);
        elapsed = 0f;
        initialized = true;
        stunReleased = false;

        CacheReferences();
        ApplyStun();
        CreateStarsVFX();
    }

    public void Refresh(float baseStunDuration, float baseDamageBonus, int parryingIndex = 0)
    {
        ResolveDurations(baseStunDuration, baseDamageBonus, parryingIndex);
        elapsed = 0f;

        // If the freeze had already been released (long debuff lingering), we are
        // re-stunning — rebuild the freeze + stars.
        if (stunReleased || starsHost == null)
        {
            stunReleased = false;
            ApplyStun();
            if (starsHost == null) CreateStarsVFX();
        }
        else
        {
            // Re-apply stun in case it partially wore off
            ApplyStun();
        }
    }

    // Layer the upgrade augments onto the passed-in base values, using the
    // PARRYING player's per-player upgrades (Phase 8).
    private void ResolveDurations(float baseStunDuration, float baseDamageBonus, int parryingIndex = 0)
    {
        // 330 — Longer Parry Stun
        freezeDuration = Mathf.Max(0.01f, baseStunDuration + ParryUpgrades.ExtraStunSecondsFor(parryingIndex));

        // 331 — Powerful Parry (or default: debuff lasts as long as the freeze)
        ParryUpgrades.ResolveDamageDebuff(parryingIndex, baseDamageBonus, freezeDuration,
                                          out damageBonus, out debuffDuration);
        debuffDuration = Mathf.Max(0.01f, debuffDuration);
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

        bool stunActive = elapsed < freezeDuration;
        float lifetime = Mathf.Max(freezeDuration, debuffDuration);

        if (stunActive)
        {
            // Keep the enemy frozen during stun
            if (rb != null)
                rb.linearVelocity = Vector2.zero;

            AnimateStars();

            // Blink sprite near end of the FREEZE to warn it's ending
            if (elapsed > freezeDuration - 0.8f && spriteRenderer != null)
            {
                float blink = Mathf.PingPong(elapsed * 8f, 1f);
                spriteRenderer.color = Color.Lerp(
                    new Color(1f, 1f, 0.5f, originalColor.a),
                    originalColor,
                    blink);
            }
        }
        else if (!stunReleased)
        {
            // Freeze phase is over — let the enemy act again, restore visuals.
            // The damage debuff (if longer, e.g. Powerful Parry) keeps ticking.
            ReleaseStun();
        }

        if (elapsed >= lifetime)
            RemoveEffect();
    }

    // Animate stars — 3D-perspective elliptical orbit around the enemy's head.
    private void AnimateStars()
    {
        if (starsHost == null) return;

        // Follow the enemy position (offset upward near head)
        starsHost.transform.position = transform.position + Vector3.up * STAR_Y_OFFSET;
        starsHost.transform.rotation = Quaternion.identity;

        float orbitAngle = elapsed * STAR_ROTATE_SPEED * Mathf.Deg2Rad;
        float fadeT = Mathf.Clamp01((elapsed - (freezeDuration - 0.5f)) / 0.5f);

        if (starRenderers == null) return;

        for (int i = 0; i < starRenderers.Length; i++)
        {
            if (starRenderers[i] == null) continue;

            float starAngle = orbitAngle + (Mathf.PI * 2f / STAR_COUNT) * i;

            // Elliptical orbit: wide X, compressed Y (perspective tilt)
            float x = Mathf.Cos(starAngle) * STAR_ORBIT_RADIUS;
            float y = Mathf.Sin(starAngle) * STAR_ORBIT_RADIUS * 0.35f;

            starRenderers[i].transform.localPosition = new Vector3(x, y, 0f);

            float depth = Mathf.Sin(starAngle); // -1 (front) to +1 (back)
            float depthScale = Mathf.Lerp(0.45f, 0.22f, (depth + 1f) * 0.5f);
            starRenderers[i].transform.localScale = Vector3.one * depthScale;

            starRenderers[i].sortingOrder = depth < 0f ? 9201 : 9199;

            float depthAlpha = Mathf.Lerp(1f, 0.45f, (depth + 1f) * 0.5f);
            Color c = starRenderers[i].color;
            c.a = depthAlpha * (1f - fadeT);
            starRenderers[i].color = c;
        }
    }

    // End the FREEZE phase: restore visuals + let the enemy resume. Does NOT end
    // the damage debuff (which may run longer with Powerful Parry).
    private void ReleaseStun()
    {
        stunReleased = true;

        if (spriteRenderer != null)
            spriteRenderer.color = originalColor;

        if (animController != null)
            animController.UnfreezeAnimation();

        if (starsHost != null)
            Destroy(starsHost);
    }

    private void RemoveEffect()
    {
        // Make sure the freeze visuals/anim are restored (in case debuff outlasted
        // the freeze, ReleaseStun already ran — these are idempotent safety calls).
        if (spriteRenderer != null)
            spriteRenderer.color = originalColor;

        if (animController != null)
            animController.UnfreezeAnimation();

        if (starsHost != null)
            Destroy(starsHost);

        Destroy(this);
    }

    private void OnDestroy()
    {
        // Safety cleanup
        if (starsHost != null)
            Destroy(starsHost);

        if (spriteRenderer != null && initialized)
            spriteRenderer.color = originalColor;

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

                float d1 = Mathf.Abs(nx) + Mathf.Abs(ny); // diamond
                float rx = nx * 0.707f - ny * 0.707f;     // rotated 45°
                float ry = nx * 0.707f + ny * 0.707f;
                float d2 = Mathf.Abs(rx) + Mathf.Abs(ry);

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

