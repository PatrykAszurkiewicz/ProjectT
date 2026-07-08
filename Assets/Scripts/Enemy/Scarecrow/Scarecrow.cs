using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMOD.Studio;
using FMODUnity;


// Scarecrow enemy
// Appears for <see cref="visibleDuration"/> seconds near a random
// nearby enemy (or boss), buffs all enemies in <see cref="auraRadius"/> with
// a 1.2x damage multiplier and a 5 HP/s regen, and damages the player on
// contact with the aura. Then disappears for <see cref="hiddenDuration"/>
// seconds and repeats. Can be killed while visible.
// EnemyController — disabled if present; scarecrow doesn't chase/melee.
[RequireComponent(typeof(EnemyStats))]
public class Scarecrow : MonoBehaviour
{
    [Header("Cycle Timing")]
    [Tooltip("Seconds the scarecrow stays visible & active (aura on, hittable).")]
    [SerializeField] private float visibleDuration = 6f;

    [Tooltip("Seconds the scarecrow stays hidden (no aura, not hittable).")]
    [SerializeField] private float hiddenDuration = 6f;

    [Tooltip("Wait this long before the FIRST appearance so the scarecrow " +
             "doesn't pop in on top of the player at wave start.")]
    [SerializeField] private float initialHiddenDelay = 2f;

    [Header("Appearance")]
    [Tooltip("Visual scale MULTIPLIER applied on top of the prefab's existing " +
             "transform.localScale. Set to 1.0 to use the prefab's scale " +
             "as-is. Set to 0.5 to render at half the prefab's authored size " +
             "without touching the prefab.\n\n" +
             "Math: finalScale = prefab.transform.localScale × visualScale. " +
             "If your sprite is too big or too small, prefer editing the " +
             "prefab's Transform.Scale (one place) over this field.")]
    [SerializeField] private float visualScale = 1.62f;

    [Header("Sorting")]
    [Tooltip("Sorting order applied to the scarecrow's SpriteRenderer. " +
             "Must be high enough that the aura disc (sortingOrder ~100) " +
             "renders behind it but low enough that UI/health-bars (typically " +
             "4000+) still render on top. 1500 is a safe middle ground.")]
    [SerializeField] private int scarecrowSortingOrder = 1500;

    [Header("Teleport / Placement")]
    [Tooltip("When picking a re-appearance spot, look for other living enemies " +
             "within this radius and teleport to the centre of the densest " +
             "cluster (so multiple enemies fall inside the aura at once).")]
    [SerializeField] private float searchForAlliesRadius = 25f;

    [Tooltip("Two enemies are considered 'in the same cluster' if they're " +
             "within this distance of each other. Larger = more enemies " +
             "group together = scarecrow buffs bigger groups but spreads " +
             "the aura's centre further from any one enemy.")]
    [SerializeField] private float clusterRadius = 3.5f;

    [Tooltip("If no enemies are found within searchForAlliesRadius, fall back " +
             "to threatening the Central Core: teleport adjacent to it and " +
             "the aura DPS will tick the Core directly.")]
    [SerializeField] private float coreFallbackOffset = 1.5f;

    [Tooltip("Layer mask used when scanning for nearby enemies to teleport to. " +
             "If left at 0 (Nothing), the scarecrow will fall back to finding " +
             "all EnemyStats components in the scene.")]
    [SerializeField] private LayerMask allyScanLayers;

    [Tooltip("Physics layers considered 'blocked' for spawn positioning — " +
             "layout obstacles (walls, buildings), props, etc. The scarecrow " +
             "will not teleport into anything on these layers. Defaults to " +
             "the 'Obstacle' layer at runtime (matches TowerDefenseMap." +
             "obstacleLayerName). Leave at 0 (Nothing) to auto-resolve.")]
    [SerializeField] private LayerMask spawnBlockedLayers;

    [Tooltip("How close to other things the scarecrow can spawn. Acts as the " +
             "clearance radius for the OverlapCircle test against obstacles, " +
             "towers, and the Core. Tune up if the scarecrow keeps clipping " +
             "into things; tune down if he refuses to fit between enemies.")]
    [SerializeField] private float spawnClearanceRadius = 0.6f;

    [Tooltip("Minimum distance from the Central Core for non-Core-fallback " +
             "spawns. Even if a cluster centroid lands near the Core, we " +
             "won't spawn there in the normal cluster path — that's reserved " +
             "for the 'no enemies, threaten the Core' fallback case, which " +
             "intentionally spawns next to the Core.")]
    [SerializeField] private float minDistanceFromCore = 2.5f;

    [Tooltip("How many nudge directions to try around a blocked candidate " +
             "before giving up and reverting to the previous safe position. " +
             "8 = cardinal + diagonal, fast and usually enough.")]
    [SerializeField] private int spawnNudgeAttempts = 8;

    [Header("Aura")]
    [Tooltip("Radius of the stasis aura — anything inside is buffed/healed/" +
             "damaged depending on whether it's an enemy or the player.")]
    [SerializeField] private float auraRadius = 4f;

    [Tooltip("Damage multiplier applied to affected enemies' outgoing damage.")]
    [SerializeField] private float damageBuff = 1.2f;

    [Tooltip("HP/sec healed to enemies inside the aura.")]
    [SerializeField] private float healPerSecond = 5f;

    [Tooltip("DPS dealt to the player while standing in the aura.")]
    [SerializeField] private float playerDamagePerSecond = 8f;

    [Header("Fade")]
    [Tooltip("How long the appear/disappear fade takes.")]
    [SerializeField] private float fadeDuration = 0.4f;

    [Header("Death VFX")]
    [Tooltip("Duration passed to EnemyDeathVFX.Trigger on death. <1.0 uses the " +
             "subtle 'classic chunks' path; ≥1.0 uses the boss-style sprite-" +
             "shatter. 0.6 is a good 'minor enemy' feel.")]
    [SerializeField] private float deathVfxDuration = 0.6f;

    [Header("Debug")]
    [Tooltip("If true, the scarecrow prints diagnostic state to the Console " +
             "on Awake and on each Appear. Useful when something doesn't " +
             "render and you need to know whether the sprite/sortingOrder/" +
             "color/scale are sane.")]
    [SerializeField] private bool debugLogs = false;

    private EnemyStats stats;
    private SpriteRenderer spriteRenderer;
    private Collider2D bodyCollider;
    private Rigidbody2D rb;
    private EnemyAnimationController animController; // optional — drives idle/death frames from EnemyData
    private ScarecrowStasisAura aura;       // child VFX/logic object
    private Coroutine cycleCoroutine;
    private bool isVisible = false;
    private bool isDead = false;

    // Cached full-visibility scale, set in Awake after visualScale is applied.
    private Vector3 baseScale = Vector3.one;

    // Cached references resolved on first need. The bar itself is owned by
    // EnemyStats (typically), but we cache here so we don't repeatedly hit
    // FindObjectsByType during transitions if EnemyStats.healthBar is null.
    private EnemyHealthBar resolvedHealthBar;
    private CanvasGroup healthBarCanvasGroup;
    private bool warnedAboutMissingBar = false;

    // Set true once death VFX has been triggered. Prevents firing twice if
    // OnHealthChanged is invoked multiple times in a single death frame.
    private bool deathVfxFired = false;

    // ScarecrowScream FMOD instance
    private EventInstance screamInstance;
    private bool screamInstanceCreated = false;

    // Public read for outside systems (grappling hook) so they can skip the
    // scarecrow during its hidden-cycle phase. Counts the brief fade
    // transitions as "not visible" — isVisible is flipped to true only at the
    // start of an Appear cycle and to false at the start of a Disappear cycle,
    // which is the behavior we want here.
    public bool IsCurrentlyVisible() => isVisible && !isDead;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        animController = GetComponent<EnemyAnimationController>();

        // Disable any EnemyController on this object — scarecrow doesn't chase or melee. 
        var controller = GetComponent<EnemyController>();
        if (controller != null) controller.enabled = false;

        // The scarecrow is a support enemy — it never plays an attack animation.
        if (animController != null)
            animController.SetAutoAttackDetectionEnabled(false);

        // Auto-resolve the obstacle layer mask if not set in the inspector.
        // Same pattern WaveSpawner uses: read the layer name from
        // TowerDefenseMap if present, fall back to literal "Obstacle".
        if (spawnBlockedLayers.value == 0)
        {
            string layerName = "Obstacle";
            var mapInstance = FindFirstObjectByType<TowerDefenseMap>();
            if (mapInstance != null && !string.IsNullOrEmpty(mapInstance.obstacleLayerName))
                layerName = mapInstance.obstacleLayerName;

            int layerIndex = LayerMask.NameToLayer(layerName);
            if (layerIndex >= 0)
                spawnBlockedLayers = 1 << layerIndex;
        }

        // Set a high sortingOrder so the aura disc (sortingOrder ~100)
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = scarecrowSortingOrder;
        }

        // Start fully transparent so the brief window between Awake and the
        // first SetVisible(false) in DeferredInitialHide doesn't show the
        // scarecrow at world origin (or wherever the spawner dropped it).
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;

            c.r = 1f; c.g = 1f; c.b = 1f;
            c.a = 0f;
            spriteRenderer.color = c;
        }

        // Force the Rigidbody2D to Kinematic
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // Apply visualScale as a MULTIPLIER on the prefab's existing scale
        transform.localScale = transform.localScale * visualScale;
        // Cache for the teleport-style appear/disappear effect. All scale
        // animations lerp against this baseline.
        baseScale = transform.localScale;

        // Disable SmoothSpriteFlip 
        var spriteFlip = GetComponent<SmoothSpriteFlip>();
        if (spriteFlip != null) spriteFlip.enabled = false;


        if (spriteRenderer == null)
        {
            Debug.LogError("[Scarecrow] No SpriteRenderer on the prefab! Add one.");
        }
        else if (debugLogs)
        {
            Debug.Log($"[Scarecrow] Awake @ {transform.position}: " +
                      $"sprite={(spriteRenderer.sprite != null ? spriteRenderer.sprite.name : "<NONE>")}, " +
                      $"sortingOrder={spriteRenderer.sortingOrder}, " +
                      $"sortingLayer='{spriteRenderer.sortingLayerName}', " +
                      $"color={spriteRenderer.color}, " +
                      $"enabled={spriteRenderer.enabled}, " +
                      $"scale={transform.localScale.x}");
        }
    }

    private void Start()
    {
        // Configure how the grappling hook treats the scarecrow.
        var grappleTarget = GetComponent<GrapplingTarget>() ?? gameObject.AddComponent<GrapplingTarget>();
        grappleTarget.canBeGrappled = true;     // YES, can be hooked
        grappleTarget.isSolidTarget = true;     // Treat like a tower: pull player to it, don't yank it

        // Build the aura as a child object so we control its lifetime independently
        // of the scarecrow's sprite/collider state.
        CreateAura();

        // Subscribe to health changes BEFORE the scarecrow can take damage.
        if (stats != null)
        {
            stats.OnHealthChanged += HandleHealthChanged;
        }

        // Defer initial hide by one frame
        StartCoroutine(DeferredInitialHide());
    }

    // CharacterStats.OnHealthChanged listener. Fires the EnemyDeathVFX exactly
    // once when health hits 0, in the same call stack as the killing
    // TakeDamage() — BEFORE EnemyStats.Die() → PerformDeath() → Destroy(go).
    private void HandleHealthChanged(float current, float max)
    {

        var bar = ResolveHealthBar();
        if (bar != null)
        {
            bar.UpdateHealth(current);
        }

        // Death path: fire death VFX exactly once when HP hits 0.
        if (deathVfxFired) return;
        if (current > 0f) return;
        deathVfxFired = true;

        // Also stop the scream right here so it doesn't ring out during the
        // disintegration. Death-by-kill, not natural disappear → sharp cut.
        StopScream(allowFadeOut: false);

        // Hide the bar before VFX so it doesn't hover over the disintegration.
        if (bar != null) bar.SetVisible(false);

        // Aura off — buffs shouldn't persist past death.
        if (aura != null) aura.SetActive(false);

        // Fire the death visual. Duration < 1.0 picks the small "classic
        // chunks" path inside EnemyDeathVFX (vs the boss sprite-shatter).
        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: deathVfxDuration,
            onComplete: null);
    }

    private IEnumerator DeferredInitialHide()
    {
        yield return null; // let sibling Start()s finish

        // Fallback if EnemyAnimationController didn't end up setting a sprite
        // (e.g. enemyData.spriteFolderPath was misconfigured, the asset isn't
        // under Resources/, or the controller got disabled for some other
        // reason)
        if (spriteRenderer != null && spriteRenderer.sprite == null)
        {
            Sprite fallback = Resources.Load<Sprite>("Sprites/EnemySprites/Scarecrow/00");
            if (fallback != null)
            {
                spriteRenderer.sprite = fallback;
                Debug.Log($"[Scarecrow] Fallback sprite loaded: {fallback.name}. " +
                          "EnemyAnimationController either didn't run or couldn't find sprites — " +
                          "check that EnemyData.spriteFolderPath = 'Sprites/EnemySprites/Scarecrow' " +
                          "and that the sprite asset is under a Resources/ folder.");
            }
            else
            {
                Debug.LogError("[Scarecrow] No sprite assigned and fallback load from " +
                               "'Sprites/EnemySprites/Scarecrow/00' also failed. The PNG must " +
                               "live under a folder literally named 'Resources' for Resources.Load " +
                               "to find it — e.g. 'Assets/Resources/Sprites/EnemySprites/Scarecrow/00.png'.");
            }
        }

        SetVisible(false, instant: true);
        cycleCoroutine = StartCoroutine(CycleLoop());
    }

    private void OnDisable()
    {
        // Disabling the aura when scarecrow disables ensures it doesn't keep
        // buffing/damaging after death or scene unload.
        if (aura != null) aura.SetActive(false);
        // Also stop the scream — covers cases like EnemyDeathVFX disabling
        // all MonoBehaviours on the GameObject, or scene unload.
        StopScream(allowFadeOut: false);
    }

    private void OnDestroy()
    {
        if (stats != null)
            stats.OnHealthChanged -= HandleHealthChanged;

        if (aura != null && aura.gameObject != null)
            Destroy(aura.gameObject);

        // Stop & release the FMOD instance
        if (screamInstanceCreated)
        {
            if (screamInstance.isValid())
            {
                screamInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                screamInstance.release();
            }
            screamInstanceCreated = false;
        }
    }

    // Hook EnemyStats death into our state machine. EnemyStats already calls
    // our gameObject's death path via PerformDeath(); this just ensures the
    // aura turns off the moment we die so it doesn't linger during the death
    // animation / VFX.
    private void Update()
    {
        if (!isDead && stats != null && stats.IsDead())
        {
            isDead = true;
            if (aura != null) aura.SetActive(false);
            if (cycleCoroutine != null) { StopCoroutine(cycleCoroutine); cycleCoroutine = null; }
            // Cut the scream immediately on death
            StopScream(allowFadeOut: false);
        }
    }

    //  Cycle 

    private IEnumerator CycleLoop()
    {
        if (initialHiddenDelay > 0f)
            yield return new WaitForSeconds(initialHiddenDelay);

        while (!isDead)
        {
            // Pick a spot near an ally (or stay put if none found).
            TeleportNearAlly();

            yield return Appear();
            yield return new WaitForSeconds(visibleDuration);
            yield return Disappear();
            yield return new WaitForSeconds(hiddenDuration);
        }
    }

    private IEnumerator Appear()
    {
        // Re-enable the animation controller before fading in so the silhouette
        if (animController != null) animController.enabled = true;

        // Start the scream BEFORE the fade so the sound leads the visual
        // appearance — gives the player a sub-second warning to look around
        // before the scarecrow's silhouette resolves.
        StartScream();

        SetVisible(true, instant: false);
        yield return TeleportFade(appearing: true);
        if (aura != null) aura.SetActive(true);

        // Diagnostic at fully-appeared state. Gated behind debugLogs.
        if (debugLogs && spriteRenderer != null)
        {
            Debug.Log($"[Scarecrow] Appeared @ {transform.position}: " +
                      $"sprite={(spriteRenderer.sprite != null ? spriteRenderer.sprite.name : "<NONE>")}, " +
                      $"color={spriteRenderer.color}, " +
                      $"enabled={spriteRenderer.enabled}, " +
                      $"scale={transform.localScale.x}, " +
                      $"animController.enabled={(animController != null ? animController.enabled.ToString() : "N/A")}");
        }
    }

    private IEnumerator Disappear()
    {
        if (aura != null) aura.SetActive(false);
        // Stop the scream as the scarecrow starts fading out.
        StopScream(allowFadeOut: true);
        yield return TeleportFade(appearing: false);
        SetVisible(false, instant: true);
        // Restore scale to baseline so the next appear starts from the right
        // size — TeleportFade leaves the transform at the squashed final state
        // when going invisible.
        transform.localScale = baseScale;
    }

    /// Teleport-style transition
    private IEnumerator TeleportFade(bool appearing)
    {
        if (spriteRenderer == null) yield break;

        float duration = Mathf.Max(0.05f, fadeDuration);

        EnsureHealthBarCanvasGroup();

        //  Slash geometry 
        float slashMaxWidth = baseScale.x * 1.2f;
        // Hair-thin in world units. Doesn't scale with the sprite — a
        // thicker scarecrow shouldn't have a thicker line.
        float slashThickness = 0.035f;
        // Slash vertical position: middle of the sprite's bounds. This
        // handles centre/bottom/top pivots uniformly. Computed once at the
        // start of the transition so it doesn't jitter if the sprite resizes.
        float slashWorldY = spriteRenderer.bounds.center.y;

        // Slash core colour. RGB > 1 + additive blending gives the glow feel.
        Color slashColor = new Color(1.6f, 1.1f, 1.9f, 1f);

        // Timeline keyframes (fractions of total duration) 
        //   k=0     sprite at full alpha, no slash
        //   k=t1    sprite starts fading + slash starts growing
        //   k=t2    sprite fully gone, slash at full width
        //   k=t3    slash starts retracting + fading
        //   k=1     nothing
        const float t1 = 0.30f; // start of cross-fade
        const float t2 = 0.55f; // slash at peak, sprite gone
        const float t3 = 0.75f; // slash starts retracting

        // Bar fade fraction of the total transition.
        const float barFraction = 0.25f;

        Vector3 startPosition = transform.position;
        // IMPORTANT: do NOT inherit whatever color is currently on the sprite —
        // external systems can tint it (e.g. the grappling hook adds a green
        // highlight when the player is locked on). If we capture that tinted
        // color as `baseColor`, the fade will look "off" and any later
        // RemoveHighlight call will restore the sprite to a wrong state at
        // the OLD position, leaving a visible ghost on the map after the
        // scarecrow has teleported away.
        Color baseColor = Color.white;
        spriteRenderer.color = baseColor;

        //  Build the slash GameObject (LineRenderer with tapered ends) 
        GameObject slashGO = new GameObject("ScarecrowTeleportSlash");
        slashGO.transform.position = new Vector3(startPosition.x, slashWorldY, startPosition.z);

        var lr = slashGO.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 3;
        lr.startWidth = slashThickness;
        lr.endWidth = slashThickness;
        // Width curve: thickness tapers to 0 at the ends, full in the middle.
        lr.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f));
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.alignment = LineAlignment.TransformZ;
        lr.sortingLayerName = spriteRenderer.sortingLayerName;
        lr.sortingOrder = spriteRenderer.sortingOrder + 10;

        // Additive material so the slash glows over the grass instead of
        // alpha-blending into a flat smudge.
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        var slashMat = new Material(sh);
        slashMat.mainTexture = Texture2D.whiteTexture;
        slashMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        slashMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        slashMat.SetInt("_ZWrite", 0);
        lr.material = slashMat;

        // Start collapsed (zero-width, zero-alpha) — the loop below sets
        // proper values on the first iteration.
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, Vector3.zero);
        lr.SetPosition(2, Vector3.zero);

        float t = 0f;
        while (t < duration && !isDead)
        {
            t += Time.deltaTime;
            float kRaw = Mathf.Clamp01(t / duration);
            // Disappear plays forward (k = kRaw), appear plays backward
            // (k = 1 - kRaw). All timeline math below is in disappear-time.
            float k = appearing ? (1f - kRaw) : kRaw;

            //  Sprite alpha 
            // Stays at 1 until k = t1, fades linearly to 0 by k = t2, stays
            // at 0 afterwards. So when the slash is alone on screen, the
            // sprite is fully gone — no awkward overlap.
            float spriteAlpha;
            if (k <= t1) spriteAlpha = 1f;
            else if (k >= t2) spriteAlpha = 0f;
            else spriteAlpha = 1f - (k - t1) / (t2 - t1);

            Color sc = baseColor;
            sc.a = spriteAlpha;
            spriteRenderer.color = sc;

            //  Slash width + intensity 
            // 0       at k <= t1  (no slash yet)
            // grows   on k in [t1, t2]   (cross-fading WITH sprite)
            // full    on k in [t2, t3]   (slash alone)
            // retracts on k in [t3, 1]   (collapses to a point + fades)
            float slashWidth01;     // 0..1 of slashMaxWidth
            float slashIntensity;   // 0..1 alpha multiplier
            if (k <= t1)
            {
                slashWidth01 = 0f;
                slashIntensity = 0f;
            }
            else if (k <= t2)
            {
                // Grow phase — ease-out so the line snaps open and then
                // settles, matching the sprite's fade-out.
                float p = (k - t1) / (t2 - t1);
                slashWidth01 = EaseOut(p);
                slashIntensity = p;          // alpha mirrors growth
            }
            else if (k <= t3)
            {
                // Hold phase — slash sits at full.
                slashWidth01 = 1f;
                slashIntensity = 1f;
            }
            else
            {
                // Retract phase — width AND alpha drop together so the
                // slash visually pulls in toward a point as it disappears.
                float p = (k - t3) / (1f - t3);
                slashWidth01 = 1f - EaseIn(p);
                slashIntensity = 1f - p;
            }

            float halfW = slashMaxWidth * slashWidth01 * 0.5f;
            lr.SetPosition(0, new Vector3(-halfW, 0f, 0f));
            lr.SetPosition(1, new Vector3(0f, 0f, 0f));
            lr.SetPosition(2, new Vector3(halfW, 0f, 0f));

            // Soft alpha gradient along the line — fades to nothing at the
            // tips. Combined with the width taper, this makes the slash
            // look like a soft streak of light, not a hard bar.
            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(slashColor, 0f),
                    new GradientColorKey(slashColor, 0.5f),
                    new GradientColorKey(slashColor, 1f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f,               0f),
                    new GradientAlphaKey(slashIntensity,   0.5f),
                    new GradientAlphaKey(0f,               1f),
                });
            lr.colorGradient = g;

            //  Health bar fade on its shorter timeline 
            if (healthBarCanvasGroup != null)
            {
                float barAlpha;
                if (appearing)
                {
                    float barK = Mathf.InverseLerp(1f - barFraction, 1f, kRaw);
                    barAlpha = barK;
                }
                else
                {
                    float barK = Mathf.InverseLerp(0f, barFraction, kRaw);
                    barAlpha = 1f - barK;
                }
                healthBarCanvasGroup.alpha = barAlpha;
            }

            yield return null;
        }

        //  Snap to final state 
        Color finalColor = baseColor;
        finalColor.a = appearing ? 1f : 0f;
        spriteRenderer.color = finalColor;

        if (healthBarCanvasGroup != null)
            healthBarCanvasGroup.alpha = appearing ? 1f : 0f;

        if (slashMat != null) Destroy(slashMat);
        if (slashGO != null) Destroy(slashGO);
    }

    /// <summary>Quadratic ease-out: fast start, slow end.</summary>
    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    //Quadratic ease-in: slow start, fast end
    private static float EaseIn(float t) => t * t;

    // Find and cache the EnemyHealthBar 
    private EnemyHealthBar ResolveHealthBar()
    {
        if (resolvedHealthBar != null) return resolvedHealthBar;

        // Primary path
        if (stats != null)
        {
            var fromStats = stats.GetHealthBar();
            if (fromStats != null)
            {
                resolvedHealthBar = fromStats;
                return resolvedHealthBar;
            }
        }

        // Fallback: scan the scene for the bar tracking us.
        var all = Object.FindObjectsByType<EnemyHealthBar>(FindObjectsSortMode.None);
        foreach (var bar in all)
        {
            if (bar == null) continue;
            if (bar.Target == transform)
            {
                resolvedHealthBar = bar;
                return resolvedHealthBar;
            }
        }

        return null;
    }

    // Lazily resolves the CanvasGroup we use for sprite-synced alpha fading.
    private void EnsureHealthBarCanvasGroup()
    {
        if (healthBarCanvasGroup != null) return;
        var bar = ResolveHealthBar();
        if (bar == null) return;
        healthBarCanvasGroup = bar.EnsureCanvasGroup();
    }

    //  FMOD scream 
    // Start the ScarecrowScream FMOD instance. 
    private void StartScream()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;

        var screamRef = FMODEvents.instance.scarecrowScream;
        if (screamRef.IsNull)
        {
            if (debugLogs) Debug.LogWarning("[Scarecrow] scarecrowScream EventReference is not assigned in FMODEvents.");
            return;
        }

        // First-time creation
        if (!screamInstanceCreated)
        {
            screamInstance = AudioManager.instance.CreateInstance(screamRef);
            if (!screamInstance.isValid())
            {
                if (debugLogs) Debug.LogWarning("[Scarecrow] Failed to create scarecrowScream EventInstance — FMOD may not be ready yet.");
                return;
            }
            screamInstanceCreated = true;

            // Attach to this GameObject so FMOD's spatial mixer pans/attenuates
            // the scream by the scarecrow's world position. 
            RuntimeManager.AttachInstanceToGameObject(screamInstance, gameObject);
        }

        if (!screamInstance.isValid()) return;

        // Only start if not already playing 
        FMOD.Studio.PLAYBACK_STATE state;
        screamInstance.getPlaybackState(out state);
        if (state == FMOD.Studio.PLAYBACK_STATE.STOPPED || state == FMOD.Studio.PLAYBACK_STATE.STOPPING)
        {
            screamInstance.start();
        }
    }

    // Stop the ScarecrowScream FMOD instance. 
    private void StopScream(bool allowFadeOut)
    {
        if (!screamInstanceCreated) return;
        if (!screamInstance.isValid()) return;

        screamInstance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
    }

    private void SetVisible(bool visible, bool instant)
    {
        isVisible = visible;

        if (spriteRenderer != null)
        {
            // Keep the SpriteRenderer enabled so fading works; we manipulate alpha.
            if (instant)
            {
                Color c = spriteRenderer.color;
                c.a = visible ? 1f : 0f;
                spriteRenderer.color = c;
            }
            spriteRenderer.enabled = true;
        }

        // While hidden the scarecrow is intangible: can't be hit by player or
        // collide with anything. Re-enable on appear.
        if (bodyCollider != null) bodyCollider.enabled = visible;

        // Freeze in place while hidden so physics never nudges it; also stop
        // any residual velocity from a previous appearance.
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = visible;
        }

        // The animation controller writes to spriteRenderer.sprite every frame.
        if (animController != null && instant)
            animController.enabled = visible;

        // When going invisible, normalise rotation so the next appear isn't
        // stuck mid-tilt from a previous velocity-driven sprite rotation.
        if (!visible)
            transform.rotation = Quaternion.identity;

        // Hide / show the health bar.
        EnemyHealthBar bar = ResolveHealthBar();
        if (bar != null)
        {
            bar.SetVisible(visible);

            // When transitioning into a fade-in, pre-set the bar's CanvasGroup
            // alpha to 0 so it doesn't flash at full opacity for one frame
            // before TeleportFade ramps it up. (No-op if no CanvasGroup; the
            // SetActive(false) already hides the bar fully when invisible.)
            if (visible && !instant)
            {
                EnsureHealthBarCanvasGroup();
                if (healthBarCanvasGroup != null) healthBarCanvasGroup.alpha = 0f;
            }
        }
        else if (!visible)
        {
            // Couldn't find a bar to hide — log once so the player knows
            // why their bar might be hanging in space.
            if (!warnedAboutMissingBar)
            {
                Debug.LogWarning("[Scarecrow] Could not resolve a health bar to hide. " +
                                 "Either EnemyStats.healthBar is null, OR no EnemyHealthBar " +
                                 "in the scene is tracking this transform. The bar (if any) " +
                                 "will linger until the scarecrow is destroyed.");
                warnedAboutMissingBar = true;
            }
        }
    }

    // Placement 
    // Picks the best spot for the scarecrow to appear and validates it isn't
    // inside the Core, a Tower, or a layout obstacle:
    //   1. If 2+ living enemies form a cluster (each within clusterRadius of
    //      at least one other enemy), teleport to the centroid of the
    //      LARGEST cluster — buffing the biggest pack.
    //   2. Else if at least one living enemy exists in range, teleport
    //      adjacent to it.
    //   3. Else (no enemies anywhere), teleport adjacent to the Core so
    //      the aura DPS chips at it. Adds genuine threat to a quiet
    //      moment between waves. (Core-proximity check is intentionally
    //      skipped for this path — that's the whole point.)

    private void TeleportNearAlly()
    {
        List<Transform> nearbyEnemies = FindNearbyLivingEnemies();

        if (nearbyEnemies.Count > 0)
        {
            // Try to find the largest cluster among them.
            Vector2 clusterCentre;
            int clusterSize;
            FindLargestCluster(nearbyEnemies, out clusterCentre, out clusterSize);

            if (clusterSize >= 2)
            {
                Vector2 validated;
                if (TryFindValidPosition(clusterCentre, requireCoreDistance: true, out validated))
                {
                    transform.position = new Vector3(validated.x, validated.y, transform.position.z);
                    return;
                }
                // Cluster blocked — fall through to the loner path.
            }

            // Loner path — pick nearest enemy and stand next to them.
            Transform nearest = nearbyEnemies[0];
            float bestDist = float.MaxValue;
            foreach (var t in nearbyEnemies)
            {
                if (t == null) continue;
                float d = ((Vector2)t.position - (Vector2)transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; nearest = t; }
            }
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (auraRadius * 0.4f);
            Vector2 candidate = (Vector2)nearest.position + offset;

            Vector2 lonerValidated;
            if (TryFindValidPosition(candidate, requireCoreDistance: true, out lonerValidated))
            {
                transform.position = new Vector3(lonerValidated.x, lonerValidated.y, transform.position.z);
                return;
            }
            // All loner candidates blocked — fall through to the Core-fallback
            // path, which has more permissive validation (allowed near Core).
        }

        // No enemies (or all candidates blocked) — threaten the Core.
        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
        {
            Vector2 corePos = (Vector2)core.transform.position;
            Vector2 toFromScarecrow = (Vector2)transform.position - corePos;
            Vector2 dir = toFromScarecrow.sqrMagnitude > 0.0001f ? toFromScarecrow.normalized : Vector2.up;
            Vector2 coreCandidate = corePos + dir * coreFallbackOffset;

            Vector2 coreValidated;
            // requireCoreDistance=false here: this path is specifically about
            // spawning NEAR the Core, so we don't reject for Core-proximity.
            // We DO still reject for Tower / Obstacle overlap.
            if (TryFindValidPosition(coreCandidate, requireCoreDistance: false, out coreValidated))
            {
                transform.position = new Vector3(coreValidated.x, coreValidated.y, transform.position.z);
                return;
            }

            // Even the Core-fallback failed — pick a ring of 8 positions around
            // the Core at coreFallbackOffset and try each in turn.
            for (int i = 0; i < 8; i++)
            {
                float a = (i / 8f) * Mathf.PI * 2f;
                Vector2 ringPos = corePos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * coreFallbackOffset;
                if (IsPositionValid(ringPos, requireCoreDistance: false))
                {
                    transform.position = new Vector3(ringPos.x, ringPos.y, transform.position.z);
                    return;
                }
            }
        }
        // else: no enemies AND no core — stay put. Shouldn't happen in practice.
    }

    // Returns true if a position is safe to spawn at
    private bool IsPositionValid(Vector2 pos, bool requireCoreDistance)
    {
        // 1. Obstacle layer (walls, layout obstacles). Cheap circle test.
        if (spawnBlockedLayers.value != 0 &&
            Physics2D.OverlapCircle(pos, spawnClearanceRadius, spawnBlockedLayers) != null)
        {
            return false;
        }

        // 2. Tower overlap
        Collider2D[] anyHits = Physics2D.OverlapCircleAll(pos, spawnClearanceRadius);
        foreach (var c in anyHits)
        {
            if (c == null) continue;
            // Skip our own colliders so we don't reject our current position.
            if (c.attachedRigidbody == rb) continue;
            if (c.GetComponentInParent<Tower>() != null) return false;
        }

        // 3. Core proximity
        if (requireCoreDistance)
        {
            GameObject coreGO = GameObject.FindGameObjectWithTag("Core");
            if (coreGO != null)
            {
                float d2 = ((Vector2)coreGO.transform.position - pos).sqrMagnitude;
                if (d2 < minDistanceFromCore * minDistanceFromCore) return false;
            }
        }

        return true;
    }

    // Tries the requested position first; if blocked, walks around it in
    // spawnNudgeAttempts evenly-spaced directions at progressively larger
    // radii. Returns false if nothing within ~2 aura radii is clear.
    private bool TryFindValidPosition(Vector2 desired, bool requireCoreDistance, out Vector2 result)
    {
        if (IsPositionValid(desired, requireCoreDistance))
        {
            result = desired;
            return true;
        }

        int attempts = Mathf.Max(1, spawnNudgeAttempts);
        // Two rings of nudges so a tight pack of obstacles can still be
        // escaped if the first ring is fully blocked.
        for (int ring = 1; ring <= 2; ring++)
        {
            float radius = spawnClearanceRadius * 2.5f * ring;
            for (int i = 0; i < attempts; i++)
            {
                float a = (i / (float)attempts) * Mathf.PI * 2f;
                Vector2 candidate = desired + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (IsPositionValid(candidate, requireCoreDistance))
                {
                    result = candidate;
                    return true;
                }
            }
        }

        result = desired;
        return false;
    }

    private List<Transform> FindNearbyLivingEnemies()
    {
        var result = new List<Transform>();
        float r2 = searchForAlliesRadius * searchForAlliesRadius;

        // Prefer a physics overlap if a layer mask is configured (fast & local).
        if (allyScanLayers.value != 0)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, searchForAlliesRadius, allyScanLayers);
            foreach (var h in hits)
            {
                if (h == null || h.gameObject == this.gameObject) continue;
                var es = h.GetComponentInParent<EnemyStats>();
                if (es == null || es == this.stats || es.IsDead()) continue;
                if (es.GetComponent<Scarecrow>() != null) continue;        // skip other scarecrows
                if (es.GetComponent<GremlinController>() != null) continue; // skip gremlins (loot piñatas, not buff targets)
                if (!result.Contains(es.transform)) result.Add(es.transform);
            }
            return result;
        }

        // Fallback: scan all EnemyStats in the scene.
        var all = Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);
        foreach (var es in all)
        {
            if (es == null || es == this.stats || es.IsDead()) continue;
            if (es.GetComponent<Scarecrow>() != null) continue;
            if (es.GetComponent<GremlinController>() != null) continue;
            float d2 = ((Vector2)es.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d2 <= r2) result.Add(es.transform);
        }
        return result;
    }

    // Greedy clustering: starting from each enemy, grow a cluster by
    // including any other enemy within clusterRadius (and recursively their
    // neighbours, classic flood-fill). Returns the centroid and size of the
    // largest cluster found. O(n²) — fine for n ≤ ~30 enemies on screen.
    private void FindLargestCluster(List<Transform> enemies, out Vector2 centroid, out int size)
    {
        centroid = transform.position;
        size = 0;

        if (enemies == null || enemies.Count == 0) return;

        float cr2 = clusterRadius * clusterRadius;
        bool[] visited = new bool[enemies.Count];
        var queue = new Queue<int>();
        var cluster = new List<int>();

        for (int seed = 0; seed < enemies.Count; seed++)
        {
            if (visited[seed]) continue;

            cluster.Clear();
            queue.Clear();
            queue.Enqueue(seed);
            visited[seed] = true;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                cluster.Add(cur);
                Vector2 curPos = enemies[cur].position;

                for (int j = 0; j < enemies.Count; j++)
                {
                    if (visited[j]) continue;
                    Vector2 jp = enemies[j].position;
                    if ((jp - curPos).sqrMagnitude <= cr2)
                    {
                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            if (cluster.Count > size)
            {
                size = cluster.Count;
                Vector2 sum = Vector2.zero;
                foreach (int idx in cluster) sum += (Vector2)enemies[idx].position;
                centroid = sum / cluster.Count;
            }
        }
    }

    //  Aura 

    private void CreateAura()
    {
        GameObject auraGO = new GameObject("ScarecrowStasisAura");
        auraGO.transform.SetParent(transform, false);
        auraGO.transform.localPosition = Vector3.zero;

        aura = auraGO.AddComponent<ScarecrowStasisAura>();
        aura.Configure(
            owner: this,
            radius: auraRadius,
            damageBuff: damageBuff,   // a multiplier on allies' already-scaled damage — not scaled here
            healPerSecond: healPerSecond,
            // Nightmare's +30% reaches the aura's direct player damage too, matching melee.
            playerDamagePerSecond: playerDamagePerSecond * EnemyStatModifierManager.DifficultyDamageMultiplier
        );

        // Visual layer
        auraGO.AddComponent<ScarecrowAuraVisual>().Configure(auraRadius);

        aura.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, auraRadius);
        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, searchForAlliesRadius);
        Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, clusterRadius);

        // Spawn validation gizmos
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f); // red — clearance bubble
        Gizmos.DrawWireSphere(transform.position, spawnClearanceRadius);

        // Min-distance-from-core ring (drawn around the Core, not us)
        if (Application.isPlaying)
        {
            var core = GameObject.FindGameObjectWithTag("Core");
            if (core != null)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.3f); // green
                Gizmos.DrawWireSphere(core.transform.position, minDistanceFromCore);
            }
        }
    }
}
