using UnityEngine;
using System.Collections;

//  WOLF - LIFESTEAL


[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyController))]
public class WolfController : MonoBehaviour
{
    [Header("Lifesteal")]
    [Tooltip("Fraction of the damage actually dealt to a PLAYER that the Wolf " +
             "recovers as health. 0.4 = a bite for 12 heals 4.8.\n\n" +
             "Damage that was parried, blocked, or fully absorbed by armor heals " +
             "nothing, because it never reduced the player's health.")]
    [Range(0f, 1f)] public float lifestealFromPlayers = 0.4f;

    [Tooltip("Same, but for everything that is NOT a player - towers and the core.\n\n" +
             "Left at 0 on purpose. Structures cannot dodge or parry, so any value " +
             "here is flat sustain the defence has to out-damage; set it above your " +
             "early-tower DPS and the Wolf simply cannot be killed while it chews. " +
             "Raise it only after checking that number.")]
    [Range(0f, 1f)] public float lifestealFromStructures = 0f;

    [Tooltip("Hard ceiling on a single heal, as a fraction of the Wolf's maxHealth. " +
             "Expressed as a fraction rather than flat HP so it tracks the augment / " +
             "stage / difficulty health multipliers automatically.")]
    [Range(0f, 1f)] public float maxHealPerHitFraction = 0.12f;

    [Tooltip("Total health one Wolf may ever recover, as a multiple of its maxHealth. " +
             "2 = it can heal itself back up twice over, then lifesteal stops for good. " +
             "Stops a long duel from stalling out. Set to 0 for no lifetime limit.")]
    public float lifetimeHealCapFraction = 2f;

    [Header("Feedback")]
    [Tooltip("Spawn a short green pulse on the Wolf when it drains health, so the " +
             "player can see WHY the health bar went back up.")]
    public bool showHealVfx = true;
    public Color healColor = new Color(0.35f, 1f, 0.45f, 0.75f);
    [Range(0.05f, 1.5f)] public float healVfxDuration = 0.35f;

    [Header("Attack Frames (optional per-instance override)")]
    [Tooltip("Leave every field at -1 (the default) and the Wolf's EnemyData asset " +
             "stays the single source of truth, exactly as for every other enemy.\n\n" +
             "Set them to override the frame numbers for this prefab only. The " +
             "override is written into the per-instance EnemyData COPY that " +
             "EnemyStats.Awake() clones, so the shared asset on disk is never " +
             "touched and other enemies using the same asset are unaffected.\n\n" +
             "Frame where the bite connects (0-based, relative to attack start). " +
             "Must be greater than Parry Frame Start or the window closes before " +
             "the player could ever react to it.")]
    public int hitFrameOverride = -1;

    [Tooltip("First parryable frame (0-based, relative to attack start). This is " +
             "where the '!' appears over the Wolf's head. -1 = leave EnemyData alone.")]
    public int parryFrameStartOverride = -1;

    [Tooltip("Last parryable frame, inclusive. Usually equal to the hit frame so the " +
             "window stays open right up to the bite. -1 = leave EnemyData alone.")]
    public int parryFrameEndOverride = -1;

    // Cached refs
    private EnemyStats stats;
    private EnemyController controller;

    // Running total for the lifetime cap.
    private float totalHealed = 0f;

    // Live pulse, so rapid bites replace it instead of stacking a pile of
    // overlapping sprites on the Wolf.
    private Coroutine healVfxRoutine;
    private GameObject healVfxGO;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        controller = GetComponent<EnemyController>();
    }

    private void Start()
    {
        // Start() and not Awake(): EnemyStats.Awake() replaces enemyData with a
        // per-instance clone, and Unity runs every Awake() on a GameObject before
        // any Start() on it. Waiting until Start therefore guarantees we are
        // writing to this Wolf's own copy and can never dirty the shared asset.
        ApplyFrameOverrides();
        WarnIfParryWindowUnusable();

        if (controller != null)
            controller.OnDamageDealt += HandleDamageDealt;
    }

    private void OnDestroy()
    {
        if (controller != null)
            controller.OnDamageDealt -= HandleDamageDealt;
    }

    //  LIFESTEAL

    // damageDealt is the health the victim ACTUALLY lost (see the hook's comment
    // in EnemyController) - never the nominal swing.
    private void HandleDamageDealt(Transform target, float damageDealt)
    {
        if (stats == null || damageDealt <= 0f) return;

        // A Wolf that died to reflected damage / Ice Armor on this very hit must
        // not resurrect itself off it.
        if (stats.IsDead()) return;

        bool targetIsPlayer = target != null && target.GetComponent<PlayerStats>() != null;
        float rate = targetIsPlayer ? lifestealFromPlayers : lifestealFromStructures;
        if (rate <= 0f) return;

        float heal = damageDealt * rate;

        // Per-hit ceiling.
        float perHitCap = stats.maxHealth * Mathf.Clamp01(maxHealPerHitFraction);
        if (perHitCap > 0f)
            heal = Mathf.Min(heal, perHitCap);

        // Lifetime ceiling.
        if (lifetimeHealCapFraction > 0f)
        {
            float remainingBudget = (stats.maxHealth * lifetimeHealCapFraction) - totalHealed;
            if (remainingBudget <= 0f) return;
            heal = Mathf.Min(heal, remainingBudget);
        }

        // Don't burn budget (or pop a pulse) on healing the Wolf is already too
        // healthy to use.
        float missing = stats.maxHealth - stats.currentHealth;
        if (missing <= 0.01f) return;
        heal = Mathf.Min(heal, missing);
        if (heal <= 0.01f) return;

        stats.Heal(heal);          // clamps at maxHealth and refreshes the health bar
        totalHealed += heal;

        if (showHealVfx)
            PlayHealPulse();
    }

    //  FRAME CONFIG

    private void ApplyFrameOverrides()
    {
        if (hitFrameOverride < 0 && parryFrameStartOverride < 0 && parryFrameEndOverride < 0)
            return; // Nothing to override - EnemyData wins, as for every other enemy.

        EnemyData data = stats != null ? stats.enemyData : null;
        if (data == null) return;

        if (hitFrameOverride >= 0) data.hitFrame = hitFrameOverride;
        if (parryFrameStartOverride >= 0) data.parryFrameStart = parryFrameStartOverride;
        if (parryFrameEndOverride >= 0) data.parryFrameEnd = parryFrameEndOverride;

        // Keep the numbers self-consistent and inside the animation, so a typo
        // degrades to a smaller window rather than to a window that never opens.
        int lastFrame = Mathf.Max(0, data.attack.frameCount - 1);
        if (data.attack.frameCount > 0)
        {
            data.hitFrame = Mathf.Clamp(data.hitFrame, 0, lastFrame);
            data.parryFrameStart = Mathf.Clamp(data.parryFrameStart, 0, lastFrame);
            data.parryFrameEnd = Mathf.Clamp(data.parryFrameEnd, 0, lastFrame);
        }
        if (data.parryFrameEnd < data.parryFrameStart)
            data.parryFrameEnd = data.parryFrameStart;

        // EnemyController may already have cached the old numbers in its own
        // Start() - component order between the two is not guaranteed - so
        // re-sync it. ParryIndicator re-reads its config at the start of every
        // attack cycle, so it picks the new values up on its own.
        if (controller != null)
            controller.RefreshFrameConfig();
    }

    // Editor-time sanity check. Costs nothing in a build and catches the two
    // configurations that silently produce an unparryable Wolf.
    private void WarnIfParryWindowUnusable()
    {
#if UNITY_EDITOR
        EnemyData data = stats != null ? stats.enemyData : null;
        if (data == null) return;

        if (data.attack.frameCount <= 1)
        {
            Debug.LogWarning($"[WolfController] {name}: the attack animation has " +
                $"{data.attack.frameCount} frame(s), so there is no wind-up to react to. " +
                "ParryIndicator disables itself in this case and the Wolf is effectively " +
                "unparryable. Give the attack at least 3-4 frames.", this);
            return;
        }

        if (data.parryFrameStart == 0 && data.parryFrameEnd == 0 && data.hitFrame == 0)
        {
            Debug.LogWarning($"[WolfController] {name}: hitFrame / parryFrameStart / " +
                "parryFrameEnd are all 0 on its EnemyData, so the Wolf falls back to the " +
                "generic 'shield raised within 0.2s' parry and shows no '!' telegraph. " +
                "Set real frame numbers on the EnemyData asset (or via the override " +
                "fields on this component).", this);
            return;
        }

        if (data.hitFrame <= data.parryFrameStart)
        {
            Debug.LogWarning($"[WolfController] {name}: hitFrame ({data.hitFrame}) is not " +
                $"after parryFrameStart ({data.parryFrameStart}), so the bite lands at or " +
                "before the window opens and the player gets no reaction time. Move the hit " +
                "frame later in the attack.", this);
        }
#endif
    }

    //  HEAL PULSE 


    private void PlayHealPulse()
    {
        if (healVfxRoutine != null)
        {
            StopCoroutine(healVfxRoutine);
            healVfxRoutine = null;
        }
        if (healVfxGO != null) Destroy(healVfxGO);

        healVfxGO = new GameObject("WolfLifestealPulse");
        healVfxGO.transform.SetParent(transform, false);

        var sr = healVfxGO.AddComponent<SpriteRenderer>();
        sr.sprite = GetPulseSprite();
        sr.color = healColor;

        healVfxRoutine = StartCoroutine(AnimateHealPulse(healVfxGO, sr));
    }

    private IEnumerator AnimateHealPulse(GameObject go, SpriteRenderer sr)
    {
        var ownSR = GetComponent<SpriteRenderer>();
        float duration = Mathf.Max(0.05f, healVfxDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (go == null || sr == null) { healVfxRoutine = null; yield break; }

            float t = Mathf.Clamp01(elapsed / duration);

            // Cancel the parent scale every frame - the Wolf prefab is scaled to
            // 0.25 and the hit flash can punch it further, exactly as
            // ParryIndicator has to handle.
            float parentScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.01f);
            float grow = Mathf.Lerp(0.9f, 1.7f, t);
            go.transform.localScale = Vector3.one * (grow / parentScale);
            go.transform.rotation = Quaternion.identity;

            // Sit one order above the Wolf. Sampled per-frame because YSortEntity
            // rewrites the Wolf's sorting order as it moves.
            if (ownSR != null)
            {
                sr.sortingLayerID = ownSR.sortingLayerID;
                sr.sortingOrder = ownSR.sortingOrder + 1;
            }

            float alpha = healColor.a * (1f - t) * (1f - t);
            sr.color = new Color(healColor.r, healColor.g, healColor.b, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (go != null) Destroy(go);
        healVfxGO = null;
        healVfxRoutine = null;
    }

    // Soft radial blob, built once and shared by every Wolf.
    private static Sprite _pulseSprite;

    private static Sprite GetPulseSprite()
    {
        if (_pulseSprite != null) return _pulseSprite;

        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[S * S];
        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / (S * 0.5f);
                // Ring: brightest at ~70% of the radius, fading both ways.
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.7f) * 4f);
                px[y * S + x] = new Color(1f, 1f, 1f, ring * ring * Mathf.Clamp01(1f - d));
            }

        tex.SetPixels(px);
        tex.Apply();
        _pulseSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _pulseSprite;
    }
}
