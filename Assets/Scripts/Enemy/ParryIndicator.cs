using UnityEngine;


// PARRY INDICATOR
// Shows a "!" above the enemy's head during parry-able frames when the player has a shield equipped.
// Should be added to enemy prefabs. 
// Reads parryFrameStart / parryFrameEnd / attackHitFrame from EnemyController and animationSpeed from EnemyStats.enemyData.

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
    private float attackStartTime = -999f;
    private bool wasAttacking = false;
    private bool playerHasShield = false;
    private float nextShieldCheck = 0f;

    // Read from EnemyController
    private int parryStart;
    private int parryEnd;
    private float animSpeed;

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

        // Use EnemyController's attack state and timing directly
        // so the indicator is perfectly synchronized with IsInParryWindow().
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

        // Check if we're in parry frames — use the SAME timing as IsInParryWindow()
        if (animSpeed <= 0f || enemyController == null)
        {
            if (isShowingIndicator) SetVisible(false);
            return;
        }

        float cycleStart = enemyController.AttackCycleStartTime;
        float parryWindowStart = cycleStart + parryStart * animSpeed;
        float parryWindowEnd = cycleStart + (parryEnd + 1) * animSpeed;
        bool inParryWindow = Time.time >= parryWindowStart && Time.time <= parryWindowEnd;

        if (inParryWindow && !isShowingIndicator)
            SetVisible(true);
        else if (!inParryWindow && isShowingIndicator)
            SetVisible(false);

        // Animate while visible
        if (isShowingIndicator)
            AnimateIndicator(Time.time - cycleStart);
    }


    private void CheckPlayerShield()
    {
        playerHasShield = false;

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
        animSpeed = 0f;

        if (enemyStats != null && enemyStats.enemyData != null)
            animSpeed = enemyStats.enemyData.animationSpeed;

        if (enemyController == null) return;

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var ps = typeof(EnemyController).GetField("parryFrameStart", flags);
        var pe = typeof(EnemyController).GetField("parryFrameEnd", flags);

        if (ps != null) parryStart = Mathf.Max((int)ps.GetValue(enemyController), 0);
        if (pe != null) parryEnd = Mathf.Max((int)pe.GetValue(enemyController), 0);

        if (parryEnd < parryStart) parryEnd = parryStart;
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
