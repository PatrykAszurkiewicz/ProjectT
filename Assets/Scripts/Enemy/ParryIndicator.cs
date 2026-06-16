using UnityEngine;


// PARRY INDICATOR
// Shows a "!" above the enemy's head during parry-able frames when the player has a shield equipped.
// Should be added to enemy prefabs. 
// Now reads frame config from EnemyData (with fallback to EnemyController fields via reflection).

public class ParryIndicator : MonoBehaviour
{
    [Header("Indicator Settings")]
    [Tooltip("Y offset above enemy pivot in world units")]
    [SerializeField] private float yOffset = 1.8f;
    [Tooltip("Size of the indicator in world units")]
    [SerializeField] private float indicatorSize = 0.5f;

    // Cached refs
    private EnemyController enemyController;
    private EnemyAnimationController animController;
    private EnemyStats enemyStats;
    private Transform playerTransform;
    private Weapon playerWeapon;

    // Indicator objects
    private GameObject indicatorGO;
    private SpriteRenderer indicatorSR;
    private SpriteRenderer glowSR;

    // State
    private bool isShowingIndicator = false;
    private bool wasAttacking = false;
    private bool playerHasShield = false;
    private float nextShieldCheck = 0f;

    // Read from EnemyData or EnemyController
    private int parryStart;
    private int parryEnd;

    // Procedural sprite
    private static Sprite _exclamSprite;
    private static Sprite _glowSprite;

    void Start()
    {
        enemyController = GetComponent<EnemyController>();
        animController = GetComponent<EnemyAnimationController>();
        enemyStats = GetComponent<EnemyStats>();

        if (enemyController == null || enemyStats == null)
        {
            enabled = false;
            return;
        }

        // Ranged enemies (Mort, Pitcher, …) deliver damage through a projectile.
        // Their shots are parried in flight (see ProjectileParry), NOT by reacting
        // to the throw animation — so the melee-style head "!" is misleading here
        // (it implied you could melee-parry a Mort). Disable it for them.
        if (enemyController.HasAttackOverride)
        {
            enabled = false;
            return;
        }

        ReadParryConfig();
        BuildIndicator();
        SetVisible(false);
    }

    void Update()
    {
        // Periodically check if player has shield (don't do every frame)
        if (Time.time > nextShieldCheck)
        {
            nextShieldCheck = Time.time + 0.5f;
            CheckPlayerShield();
        }

        if (!playerHasShield)
        {
            if (isShowingIndicator) SetVisible(false);
            return;
        }

        bool attacking = enemyController != null && enemyController.IsAttacking;

        if (attacking && !wasAttacking)
        {
            ReadParryConfig();
        }
        wasAttacking = attacking;

        if (!attacking)
        {
            if (isShowingIndicator) SetVisible(false);
            return;
        }

        // Once this enemy has been successfully parried it gets a ParryStunEffect,
        // which FREEZES its attack animation on a frame that's still inside the
        // parry window — so the frame-based check below would keep the "!" up for
        // the whole stun. The parry is already over, so hide the mark immediately
        // the moment the enemy is parry-stunned. (A block doesn't stun, so its
        // animation advances past the window and clears the mark on its own.)
        var parryStun = GetComponent<ParryStunEffect>();
        if (parryStun != null && parryStun.IsStunActive)
        {
            if (isShowingIndicator) SetVisible(false);
            return;
        }

        // If ReadParryConfig disabled the indicator (e.g. single-frame attack),
        // never show. parryStart < 0 is the disable sentinel.
        if (parryStart < 0)
        {
            if (isShowingIndicator) SetVisible(false);
            return;
        }

        // Augment 332 "Longer Parry Window" opens the parry window earlier by
        // ExtraParryFrames (see EnemyController.IsInParryWindow). Mirror that here
        // so the "!" appears as early as the augment allows and the telegraph
        // stays in lockstep with the actual parry window. Clamp at frame 0 — the
        // attack animation has no earlier frame to show the "!" on, so on an enemy
        // whose parry frames start at N, the visible warning can move forward by at
        // most N frames even if ExtraParryFrames is larger.
        int effParryStart = Mathf.Max(0, parryStart - ParryUpgrades.MaxExtraParryFrames());

        // Use CurrentAttackFrame from the animation controller for pixel-perfect sync
        bool inParryWindow = false;

        if (animController != null && animController.CurrentAttackFrame >= 0)
        {
            int currentFrame = animController.CurrentAttackFrame;
            inParryWindow = currentFrame >= effParryStart && currentFrame <= parryEnd;
        }
        else
        {
            // Fallback to time-based check (same as IsInParryWindow logic)
            float animSpeed = (enemyStats != null && enemyStats.enemyData != null) ? enemyStats.enemyData.AttackAnimSpeed : 0f;
            if (animSpeed > 0f && enemyController != null)
            {
                float cycleStart = enemyController.AttackCycleStartTime;
                float parryWindowStart = cycleStart + effParryStart * animSpeed;
                float parryWindowEnd = cycleStart + (parryEnd + 1) * animSpeed;
                inParryWindow = Time.time >= parryWindowStart && Time.time <= parryWindowEnd;
            }
        }

        if (inParryWindow && !isShowingIndicator)
            SetVisible(true);
        else if (!inParryWindow && isShowingIndicator)
            SetVisible(false);

        // Animate while visible
        if (isShowingIndicator)
        {
            float elapsed = Time.time - enemyController.AttackCycleStartTime;
            AnimateIndicator(elapsed);
        }
    }


    private void CheckPlayerShield()
    {
        playerHasShield = false;

        // Co-op: the "!" should show if ANY alive player has a shield equipped,
        // not just whichever object carries the "Player" tag. Falls back to the
        // old tag lookup if the registry isn't available.
        var reg = PlayerRegistry.Instance;
        if (reg != null)
        {
            var all = reg.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Stats == null || p.Stats.IsDead()) continue;
                var w = p.Stats.GetComponentInChildren<Weapon>();
                if (w != null && w.GetShieldSystem() != null)
                {
                    playerHasShield = true;
                    break;
                }
            }
            return;
        }

        if (playerTransform == null || playerWeapon == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
                playerWeapon = player.GetComponentInChildren<Weapon>();
            }
        }

        if (playerWeapon != null)
            playerHasShield = playerWeapon.GetShieldSystem() != null;
    }

    private void ReadParryConfig()
    {
        parryStart = 0;
        parryEnd = 0;

        // Read from EnemyData (single source of truth)
        if (enemyStats != null && enemyStats.enemyData != null)
        {
            var data = enemyStats.enemyData;
            parryStart = Mathf.Max(data.parryFrameStart, 0);
            parryEnd = Mathf.Max(data.parryFrameEnd, 0);
            if (parryEnd < parryStart) parryEnd = parryStart;

            // Degenerate case: a single-frame attack collapses the parry window
            // onto the hit frame. There's no meaningful "wind-up" the player can
            // react to, and the indicator would show continuously every cycle.
            // Disable the indicator until the enemy has at least 2 attack frames.
            if (data.attack.frameCount <= 1)
            {
                parryStart = -1;
                parryEnd = -1;
            }
        }
    }

    private void BuildIndicator()
    {
        // Compute inverse scale
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.01f);
        float inv = 1f / s;

        indicatorGO = new GameObject("ParryIndicator");
        indicatorGO.transform.SetParent(transform, false);
        indicatorGO.transform.localScale = Vector3.one * inv;

        // Glow behind the exclamation mark
        GameObject glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(indicatorGO.transform, false);
        glowGO.transform.localPosition = new Vector3(0f, yOffset, 0f);
        glowGO.transform.localScale = Vector3.one * (indicatorSize * 2.2f);

        glowSR = glowGO.AddComponent<SpriteRenderer>();
        glowSR.sprite = GetGlowSprite();
        glowSR.color = new Color(1f, 0.85f, 0.1f, 0.35f);
        glowSR.sortingOrder = 9600;

        // Exclamation mark
        GameObject exclGO = new GameObject("Excl");
        exclGO.transform.SetParent(indicatorGO.transform, false);
        exclGO.transform.localPosition = new Vector3(0f, yOffset, 0f);
        exclGO.transform.localScale = Vector3.one * indicatorSize;

        indicatorSR = exclGO.AddComponent<SpriteRenderer>();
        indicatorSR.sprite = GetExclamationSprite();
        indicatorSR.color = new Color(1f, 0.9f, 0.1f, 1f);
        indicatorSR.sortingOrder = 9601;
    }

    private void SetVisible(bool visible)
    {
        isShowingIndicator = visible;
        if (indicatorGO != null)
            indicatorGO.SetActive(visible);
    }

    private void AnimateIndicator(float elapsed)
    {
        if (indicatorGO == null) return;

        // Keep world rotation (cancel parent rotation)
        indicatorGO.transform.rotation = Quaternion.identity;

        // Recompute inverse scale in case of hit-flash scale punches
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.01f);
        indicatorGO.transform.localScale = Vector3.one / s;

        // Gentle bob up and down
        float bob = Mathf.Sin(elapsed * 12f) * 0.04f;
        // Pulse the glow
        if (glowSR != null)
        {
            float pulse = 0.25f + Mathf.PingPong(elapsed * 3f, 0.2f);
            glowSR.color = new Color(1f, 0.85f, 0.1f, pulse);
            glowSR.transform.localPosition = new Vector3(0f, yOffset + bob, 0f);
        }
        if (indicatorSR != null)
        {
            indicatorSR.transform.localPosition = new Vector3(0f, yOffset + bob, 0f);
        }
    }

    void OnDestroy()
    {
        if (indicatorGO != null)
            Destroy(indicatorGO);
    }

    //  PROCEDURAL SPRITES

    private static Sprite GetExclamationSprite()
    {
        if (_exclamSprite != null) return _exclamSprite;

        const int S = 32;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] px = new Color[S * S];

        // Clear
        for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

        // Draw exclamation mark: vertical bar + dot
        // Bar: x=13-18, y=10-28
        for (int y = 10; y <= 28; y++)
            for (int x = 13; x <= 18; x++)
                px[y * S + x] = Color.white;

        // Slight taper at top
        for (int y = 24; y <= 28; y++)
        {
            int inset = (y - 24) / 2;
            for (int x = 13; x <= 18; x++)
                if (x < 13 + inset || x > 18 - inset)
                    px[y * S + x] = Color.clear;
        }

        // Dot: x=13-18, y=4-8
        for (int y = 4; y <= 8; y++)
            for (int x = 13; x <= 18; x++)
                px[y * S + x] = Color.white;

        tex.SetPixels(px);
        tex.Apply();
        _exclamSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _exclamSprite;
    }

    private static Sprite GetGlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;

        const int S = 32;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a * a);
            }

        tex.SetPixels(px);
        tex.Apply();
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _glowSprite;
    }
}

