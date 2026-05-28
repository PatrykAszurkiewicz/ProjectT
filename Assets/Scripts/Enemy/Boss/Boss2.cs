using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Boss2 — The Lich
// Two attack patterns, chosen alternately:
//   Meteor (rod-cast AoE):
//   Summon (rod-cast slimes):

public class Boss2 : BaseBossStats
{
    //  Boss Config 
    [Header("Boss2 Configuration")]
    [SerializeField] private float bossMaxArmor = 800f;
    [SerializeField] private float bossMaxHealth = 800f;

    [Header("Boss Collider")]
    [SerializeField] private float bossColliderRadius = 2f;
    [SerializeField] private float bossColliderOffsetY = 0f;

    [Header("Boss Physics")]
    [SerializeField] private float bossRigidbodyMass = 100f;
    [SerializeField] private float bossLinearDrag = 5f;

    [Header("Health Bar")]
    [Tooltip("Horizontal offset in world units. Flips with the boss sprite's facing direction.")]
    [SerializeField] private float healthBarXOffset = 0f;

    [Tooltip("How much to pull the health bar DOWN from its natural position (sprite top + extraPadding). " +
             "Leave at 0 for most bosses. Boss1 used 1.5 because its sprite is much larger; that value " +
             "is wrong for Boss2 and pushes the bar into the body. The script clamps the final offset to " +
             "always be above the sprite top regardless of what you set here.")]
    [SerializeField] private float healthBarYReduction = 0f;

    [Tooltip("Extra spacing in world units between the boss sprite top and the bottom of the health bar.")]
    [SerializeField] private float healthBarExtraYPadding = 0.5f;
    private float healthBarYOffset = 0f;

    [Header("Disintegration Effect")]
    [SerializeField] private float disintegrationDuration = 1.5f;

    //  Attack Targeting / Cadence 
    [Header("Attack Behavior")]
    [Tooltip("Distance at which the Lich starts casting attacks against the player.")]
    [SerializeField] private float attackRange = 10f;

    [Tooltip("Below this distance, the Lich prefers melee/movement (handled by EnemyController) and won't cast.")]
    [SerializeField] private float meleeOnlyRange = 2.5f;

    [Tooltip("Cooldown between spell casts (Meteor or Summon). The two attacks alternate.")]
    [SerializeField] private float spellCooldown = 5f;

    //  Rod (child object) 
    [Header("Lich Rod")]
    [Tooltip("Name of the rod child object under this boss. Default is 'LichRod' (matches your prefab).")]
    [SerializeField] private string rodChildName = "LichRod";

    [Tooltip("How long the rod stays visible after a cast finishes, before fading away.")]
    [SerializeField] private float rodLingerAfterCast = 0.2f;

    [Tooltip("Sorting order applied to every SpriteRenderer on the rod so it draws above the grass overlay. " +
             "Grass tops out around order ~1600 in this project; 3000 puts the rod safely above it while " +
             "still letting damage flashes and UI render on top.")]
    [SerializeField] private int rodSortingOrder = 3000;

    private GameObject lichRod;
    private SpriteRenderer[] lichRodRenderers;
    private ParticleSystem[] lichRodParticles;

    //  Meteor Attack 
    [Header("Meteor Attack")]
    [Tooltip("Seconds between the target circle appearing and the explosion firing. Gives the player time to dodge.")]
    [SerializeField] private float meteorTelegraphDuration = 1.5f;

    [Tooltip("Radius of the explosion in world units.")]
    [SerializeField] private float meteorExplosionRadius = 2.5f;

    [Tooltip("Damage dealt to anything inside the explosion radius.")]
    [SerializeField] private float meteorDamage = 40f;

    [Tooltip("Color of the warning ring drawn on the ground during the telegraph.")]
    [SerializeField] private Color meteorWarningColor = new Color(1f, 0.15f, 0.15f, 0.85f);

    [Tooltip("Color the warning ring pulses to as the explosion approaches.")]
    [SerializeField] private Color meteorWarningPulseColor = new Color(1f, 0.6f, 0f, 1f);

    [Tooltip("Layers that take damage from the explosion. Leave 0 to hit everything except enemies.")]
    [SerializeField] private LayerMask meteorDamageLayers;

    //  Summon Attack 
    [Header("Summon Attack")]
    [Tooltip("Prefab to spawn (SmallSlime).")]
    [SerializeField] private GameObject smallSlimePrefab;

    [Tooltip("How many slimes are summoned per cast.")]
    [SerializeField] private int summonCount = 6;

    [Tooltip("Radius of the ring around the boss where slimes appear.")]
    [SerializeField] private float summonRingRadius = 2f;

    [Tooltip("Seconds between the puff of smoke and the slimes actually appearing.")]
    [SerializeField] private float summonSpawnDelay = 1f;

    [Tooltip("Color of the smoke puff.")]
    [SerializeField] private Color summonSmokeColor = new Color(0.7f, 0.7f, 0.8f, 0.7f);

    //  Runtime State 
    private SpriteRenderer bossSprite;
    private SmoothSpriteFlip bossSmoothFlip;
    private EnemyAnimationController animController;

    private Transform currentTarget;
    private float lastSpellTime = -999f;
    private bool isCasting = false;
    private bool isDying = false;

    // Alternates between Meteor (false) and Summon (true) on each cast.
    private bool nextCastIsSummon = false;

    // Procedurally-built circle sprite (filled, opaque white — color is applied
    // via SpriteRenderer.color so we can tint and alpha-fade it cheaply).
    private static Sprite _filledCircleSprite;


    //  INITIALIZATION 
    protected override void Awake()
    {
        //Debug.Log($"[Boss2] Awake on {gameObject.name}. enemyData={(enemyData != null ? enemyData.name : "NULL")}, healthBarPrefab={(healthBarPrefab != null ? healthBarPrefab.name : "NULL")}, layer={LayerMask.LayerToName(gameObject.layer)}, tag={gameObject.tag}");

        // Pull boss stats from the EnemyData asset when one is assigned 
        if (enemyData != null)
        {
            maxHealth = enemyData.maxHealth;
            maxArmor = enemyData.maxArmor;
        }
        else
        {
            maxHealth = bossMaxHealth;
            maxArmor = bossMaxArmor;
        }

        base.Awake();

        // Default damage layers: hit everything except the Enemy layer (so the
        // boss doesn't damage itself or its own slimes).
        if (meteorDamageLayers == 0)
            meteorDamageLayers = ~LayerMask.GetMask("Enemy");

        //Debug.Log($"[Boss2] After Awake: maxHealth={maxHealth}, maxArmor={maxArmor}, bossArmor={bossArmor}, currentHealth={currentHealth}");
    }

    protected override void Start()
    {
        // If healthBarPrefab is null on Boss2, borrow one from any other EnemyStats
        if (healthBarPrefab == null)
            healthBarPrefab = FindAnyHealthBarPrefab();

        base.Start();

        bossSprite = GetComponent<SpriteRenderer>();
        bossSmoothFlip = GetComponent<SmoothSpriteFlip>();
        if (bossSmoothFlip == null)
            bossSmoothFlip = gameObject.AddComponent<SmoothSpriteFlip>();
        // Same reasoning as Boss1: avoid stepping on damage flash / color writes.
        bossSmoothFlip.SetMinimalMode(true);

        animController = GetComponent<EnemyAnimationController>();

        ConfigureRigidbody();
        ConfigureBossCollider();
        InitializeBossHealthBar();
        FindAndHideRod();

        // Prewarm the slime's sprite cache so the first summon doesn't pay a cold-load cost.
        PrewarmSlimeSprites();

        // Diagnostic: log final state so any setup issue shows up clearly in the console.
        var col = GetComponent<Collider2D>();
        //Debug.Log($"[Boss2] Start complete. HealthBar={(HealthBar != null ? "OK" : "NULL")}, " +
        //          $"lichRod={(lichRod != null ? "OK" : "NOT FOUND")}, " +
        //          $"animController={(animController != null ? "OK" : "MISSING")}, " +
        //          $"collider={(col != null ? $"{col.GetType().Name} enabled={col.enabled} isTrigger={col.isTrigger}" : "MISSING")}, " +
        //          $"smallSlimePrefab={(smallSlimePrefab != null ? "OK" : "NULL")}");
    }

    private GameObject FindAnyHealthBarPrefab()
    {
        EnemyStats[] otherStats = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);
        foreach (var s in otherStats)
        {
            if (s == this) continue;
            if (s.healthBarPrefab != null)
            {
                Debug.LogWarning($"Boss2: healthBarPrefab not assigned in inspector. " +
                                 $"Borrowed one from {s.gameObject.name}. Please assign it explicitly on the Boss2 prefab.");
                return s.healthBarPrefab;
            }
        }
        Debug.LogWarning("Boss2: healthBarPrefab not assigned and no other EnemyStats in scene to borrow from. " +
                         "Drag the boss health bar prefab into the Boss2 prefab's 'healthBarPrefab' slot.");
        return null;
    }

    private void ConfigureRigidbody()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;
        float mass = (enemyData != null) ? enemyData.mass : bossRigidbodyMass;
        rb.mass = Mathf.Max(mass, 50f);
        rb.linearDamping = bossLinearDrag;
    }

    private void ConfigureBossCollider()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = false;
        col.radius = bossColliderRadius;
        col.offset = new Vector2(0f, bossColliderOffsetY);
    }

    // Pre-loads the slime's sprite folder so EnemyAnimationController.Start() on each
    // spawned slime hits a warm cache. Without this, every slime in a summon burst calls
    // Resources.LoadAll<Sprite>(spriteFolderPath) in its own first-frame Start, all on the
    // main thread — that's a major part of the visible freeze when the boss summons.
    // Costs one synchronous load at scene-start, when nothing else is happening.
    private void PrewarmSlimeSprites()
    {
        if (smallSlimePrefab == null) return;
        var slimeStats = smallSlimePrefab.GetComponent<EnemyStats>();
        if (slimeStats == null || slimeStats.enemyData == null) return;
        string folder = slimeStats.enemyData.spriteFolderPath;
        if (string.IsNullOrEmpty(folder)) return;
        // The return value is discarded — Unity keeps the sprites alive in its internal
        // resource cache, so the slime's Start() will get them back from cache instantly.
        Resources.LoadAll<Sprite>(folder);
    }

    private void InitializeBossHealthBar()
    {
        if (HealthBar == null)
        {
            Debug.LogWarning("Boss2: HealthBar is null! Make sure healthBarPrefab is assigned.");
            return;
        }

        float totalMaxHealth = maxHealth + maxArmor;
        HealthBar.Initialize(transform, totalMaxHealth);

        // Place the health bar above the boss sprite.
        const float MIN_HEADROOM_ABOVE_SPRITE = 0.1f;
        healthBarYOffset = healthBarExtraYPadding;
        if (bossSprite != null && bossSprite.sprite != null)
        {
            Bounds worldBounds = bossSprite.bounds;
            float spriteTopWorld = worldBounds.max.y;
            float bossY = transform.position.y;
            float topAboveBoss = spriteTopWorld - bossY;
            float computed = topAboveBoss + healthBarExtraYPadding - healthBarYReduction;
            // Floor at the sprite top + headroom so the bar is always visibly above the body.
            healthBarYOffset = Mathf.Max(computed, topAboveBoss + MIN_HEADROOM_ABOVE_SPRITE);
        }

        UpdateHealthBarOffset();

        Canvas canvas = HealthBar.GetComponentInChildren<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
        }
    }

    private void UpdateHealthBarOffset()
    {
        if (HealthBar == null) return;
        float xOff = (bossSprite != null && bossSprite.flipX) ? -healthBarXOffset : healthBarXOffset;
        HealthBar.SetOffset(new Vector3(xOff, healthBarYOffset, 0f));
    }

    private void FindAndHideRod()
    {
        // The LichRod is set up in the prefab as a child of Boss2
        Transform rodT = transform.Find(rodChildName);
        if (rodT == null)
        {
            // Fall back to a recursive search in case it's nested deeper.
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == rodChildName) { rodT = t; break; }
            }
        }

        if (rodT == null)
        {
            Debug.LogWarning($"Boss2: Could not find child '{rodChildName}'. The rod attacks will still run logically, but no rod will appear visually.");
            return;
        }

        lichRod = rodT.gameObject;
        lichRodRenderers = lichRod.GetComponentsInChildren<SpriteRenderer>(true);

        bool hasUsableRenderer = false;
        if (lichRodRenderers != null)
        {
            foreach (var sr in lichRodRenderers)
                if (sr != null && sr.sprite != null) { hasUsableRenderer = true; break; }
        }

        if (!hasUsableRenderer)
        {
            Debug.LogWarning($"Boss2: '{rodChildName}' has no SpriteRenderer with a sprite assigned. " +
                             $"Adding a placeholder rod sprite so it's visible during casts. " +
                             $"To replace it, edit the LichRod prefab and add a real sprite.");

            SpriteRenderer placeholder = lichRod.GetComponent<SpriteRenderer>();
            if (placeholder == null)
                placeholder = lichRod.AddComponent<SpriteRenderer>();
            placeholder.sprite = BuildPlaceholderRodSprite();
            placeholder.color = new Color(0.85f, 0.2f, 0.95f, 1f); // purple, lich-y

            // Re-query the renderer list now that we've added one.
            lichRodRenderers = lichRod.GetComponentsInChildren<SpriteRenderer>(true);
        }


        string targetLayerName = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        if (lichRodRenderers != null)
        {
            for (int i = 0; i < lichRodRenderers.Length; i++)
            {
                var sr = lichRodRenderers[i];
                if (sr == null) continue;
                sr.sortingLayerName = targetLayerName;
                // +i so the orb child renders just above the shaft child if both exist —
                // keeps any layered art readable instead of fighting the same order.
                sr.sortingOrder = rodSortingOrder + i;
            }
        }

        // Particle systems on the rod (e.g. the orb's glow VFX) must appear and disappear
        // with the rod, otherwise they keep emitting in mid-air after the cast ends.
        lichRodParticles = lichRod.GetComponentsInChildren<ParticleSystem>(true);

        SetRodVisible(false);
    }

    // Generates a stylized purple rod sprite
    private static Sprite _placeholderRodSprite;
    private static Sprite BuildPlaceholderRodSprite()
    {
        if (_placeholderRodSprite != null) return _placeholderRodSprite;

        const int W = 32;
        const int H = 96;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color[W * H];
        // Default fully transparent.
        for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

        // SHAFT — a thin dark vertical bar running from the bottom up to ~75% height.
        float shaftCenterX = W * 0.5f;
        float shaftHalfWidth = 2.5f;
        Color shaftColor = new Color(0.25f, 0.15f, 0.30f, 1f); // dark wood/violet
        Color shaftHighlight = new Color(0.5f, 0.35f, 0.6f, 1f);
        for (int y = 0; y < H * 0.78f; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float dx = x - shaftCenterX;
                float dist = Mathf.Abs(dx);
                if (dist <= shaftHalfWidth)
                {
                    float t = (dx + shaftHalfWidth) / (shaftHalfWidth * 2f); // 0..1 across width
                    Color c = Color.Lerp(shaftColor, shaftHighlight, 1f - Mathf.Abs(t - 0.3f) * 1.5f);
                    c.a = 1f;
                    px[y * W + x] = c;
                }
                else if (dist <= shaftHalfWidth + 0.8f)
                {
                    // soft AA edge
                    float feather = 1f - (dist - shaftHalfWidth) / 0.8f;
                    px[y * W + x] = new Color(shaftColor.r, shaftColor.g, shaftColor.b, feather);
                }
            }
        }

        // ORB — a glowing circle at the top of the staff.
        float orbCenterX = W * 0.5f;
        float orbCenterY = H * 0.82f;
        float orbRadius = 9f;
        Color orbInner = new Color(1f, 0.85f, 1f, 1f);          // bright pink-white core
        Color orbOuter = new Color(0.8f, 0.2f, 1f, 1f);          // purple glow
        Color orbGlow = new Color(0.6f, 0.1f, 0.9f, 0.35f);      // soft outer glow
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float dx = x - orbCenterX;
                float dy = y - orbCenterY;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= orbRadius)
                {
                    float t = d / orbRadius;
                    Color c = Color.Lerp(orbInner, orbOuter, t);
                    // Slight off-center highlight for a 3D look.
                    float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(dx, dy), new Vector2(-2.5f, 2.5f)) / 3.5f);
                    c = Color.Lerp(c, Color.white, hl * 0.55f);
                    c.a = 1f;
                    px[y * W + x] = c;
                }
                else if (d <= orbRadius + 4f)
                {
                    // outer glow halo
                    float falloff = 1f - (d - orbRadius) / 4f;
                    Color g = orbGlow; g.a = orbGlow.a * falloff;
                    // Overlay onto whatever was already there.
                    Color existing = px[y * W + x];
                    if (existing.a < g.a) px[y * W + x] = g;
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        // PPU 32 → sprite is 1 unit wide, 3 units tall.
        _placeholderRodSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.1f), 32f);
        return _placeholderRodSprite;
    }

    private void SetRodVisible(bool visible)
    {
        // Toggle every SpriteRenderer that belongs to the rod (the shaft, the orb, etc.).
        if (lichRodRenderers != null)
        {
            foreach (var sr in lichRodRenderers)
                if (sr != null) sr.enabled = visible;
        }

        // Toggle particle systems on the rod the same way
        if (lichRodParticles != null)
        {
            foreach (var ps in lichRodParticles)
            {
                if (ps == null) continue;
                if (visible)
                {
                    if (!ps.isPlaying) ps.Play(true);
                }
                else
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }
    }


    //  MAIN LOOP 
    private void Update()
    {
        if (isDying) return;

        UpdateHealthBarOffset();
        FindTarget();

        if (currentTarget != null && !isCasting && Time.time >= lastSpellTime + spellCooldown)
        {
            float distance = Vector2.Distance(transform.position, currentTarget.position);
            if (distance > meleeOnlyRange && distance <= attackRange)
            {
                StartCoroutine(CastNextSpell());
            }
        }
    }

    private void FindTarget()
    {
        // Same priority as Boss1: prefer the player, fall back to the core.
        if (!PlayerCloakEffect.IsActive)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) { currentTarget = player.transform; return; }
        }

        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null) { currentTarget = core.transform; return; }

        currentTarget = null;
    }


    //  SPELL DISPATCH 
    private IEnumerator CastNextSpell()
    {
        isCasting = true;
        lastSpellTime = Time.time;

        // Stop the boss so it doesn't slide while casting.
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // Pause movement on the controller so it doesn't fight the cast.
        var ec = GetComponent<EnemyController>();
        bool ecWasEnabled = ec != null && ec.enabled;
        if (ec != null) ec.enabled = false;

        // Show the rod for the duration of the cast.
        SetRodVisible(true);

        if (nextCastIsSummon)
            yield return StartCoroutine(PerformSummonAttack());
        else
            yield return StartCoroutine(PerformMeteorAttack());

        // Toggle for next time so the two attacks alternate.
        nextCastIsSummon = !nextCastIsSummon;

        // Linger so the rod doesn't snap away the instant the spell resolves.
        yield return new WaitForSeconds(rodLingerAfterCast);
        SetRodVisible(false);

        if (ec != null) ec.enabled = ecWasEnabled;
        isCasting = false;
    }


    //  METEOR ATTACK 
    private IEnumerator PerformMeteorAttack()
    {
        if (currentTarget == null) yield break;

        // Lock the explosion location to where the player is RIGHT NOW. This is what
        // makes the attack dodgeable — the warning ring is a fixed marker, not a tracker.
        Vector3 explosionPos = currentTarget.position;

        // Spawn the pulsating warning ring on top of the grass.
        GameObject warning = SpawnWarningRing(explosionPos, meteorExplosionRadius, meteorTelegraphDuration);

        // Wait out the telegraph. The ring animates itself — Boss2 only needs to
        // observe the timer and abort if dying.
        float elapsed = 0f;
        while (elapsed < meteorTelegraphDuration)
        {
            if (isDying) { if (warning != null) Destroy(warning); _activeWarningRing = null; yield break; }
            elapsed += Time.deltaTime;
            yield return null;
        }

        // BOOM.
        if (warning != null) Destroy(warning);
        _activeWarningRing = null;
        SpawnExplosion(explosionPos, meteorExplosionRadius);
        ApplyExplosionDamage(explosionPos, meteorExplosionRadius);
    }

    // Self-contained warning ring marker
    private GameObject SpawnWarningRing(Vector3 worldPos, float radius, float duration)
    {
        var go = new GameObject("Boss2_MeteorWarningRing");
        go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

        // Parent to the boss with worldPositionStays = true so the ring's world position
        // stays locked to the cast site (boss doesn't move during a cast, but anchoring it
        // to the boss means it dies when the boss dies — without this parenting an
        // unparented ring leaks as a permanent decal if the boss dies mid-telegraph,
        // because ExecuteBossDeath calls StopAllCoroutines before the meteor coroutine
        // can clean up its own ring.
        go.transform.SetParent(transform, worldPositionStays: true);

        var anim = go.AddComponent<Boss2MeteorWarningRing>();
        anim.Initialize(radius, duration, meteorWarningColor, meteorWarningPulseColor);
        _activeWarningRing = go;
        return go;
    }

    // Tracked so ExecuteBossDeath can destroy it immediately, even if StopAllCoroutines
    // has already aborted the meteor coroutine mid-flight.
    private GameObject _activeWarningRing;

    private void SpawnExplosion(Vector3 pos, float radius)
    {
        // VFX
        var root = new GameObject("Boss2_MeteorVFX");
        root.transform.position = pos;
        var fx = root.AddComponent<Boss2MeteorVFX>();
        fx.Play(radius);

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.bossGroundHit, pos);

        // A bit of camera shake on impact so the player feels the hit even at the edge of frame.
        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(0.35f, 0.18f);
    }

    private void ApplyExplosionDamage(Vector3 pos, float radius)
    {
        //Debug.Log($"[Boss2] ApplyExplosionDamage at {pos} radius={radius} damage={meteorDamage}");

        // We deduplicate by the RESOLVED top-level damage target (the CharacterStats /
        // IEnergyConsumer that actually receives damage), not by the raw collider's
        // GameObject. 
        HashSet<CharacterStats> damagedCharacters = new HashSet<CharacterStats>();
        HashSet<IEnergyConsumer> damagedConsumers = new HashSet<IEnergyConsumer>();

        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, radius, meteorDamageLayers);
        //Debug.Log($"[Boss2] OverlapCircleAll found {(hits != null ? hits.Length : 0)} colliders (mask={meteorDamageLayers.value})");

        if (hits != null)
        {
            foreach (var hit in hits)
            {
                if (hit == null || hit.gameObject == gameObject) continue;
                // Skip other enemies & enemy-children so the boss doesn't kill its own slimes.
                if (hit.GetComponentInParent<EnemyStats>() != null) continue;

                // Resolve CharacterStats via PARENT lookup
                var cs = hit.GetComponentInParent<CharacterStats>();
                if (cs != null && damagedCharacters.Add(cs))
                {
                    //Debug.Log($"[Boss2] Hitting CharacterStats on {cs.gameObject.name} (via collider {hit.name})");
                    cs.TakeDamage(meteorDamage);
                    continue;
                }

                // Towers / Core: route via the energy damage system.
                var consumer = hit.GetComponentInParent<IEnergyConsumer>();
                if (consumer != null && damagedConsumers.Add(consumer))
                {
                    //Debug.Log($"[Boss2] Hitting IEnergyConsumer on {hit.gameObject.name}");
                    if (EnemyDamageSystem.Instance != null)
                        EnemyDamageSystem.Instance.DamageEnergyConsumer(consumer, meteorDamage, gameObject);
                    continue;
                }

                // Unknown collider — ignore. Logging here would spam (Background, slot triggers, etc.).
            }
        }

        // Final safety net: explicit player lookup. Catches the case where the player's
        // collider somehow wasn't in OverlapCircleAll's results (wrong layer mask, etc.).
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var playerStats = player.GetComponentInChildren<CharacterStats>();
            if (playerStats == null) playerStats = player.GetComponentInParent<CharacterStats>();

            float dist = Vector2.Distance(player.transform.position, pos);
            bool alreadyHit = playerStats != null && damagedCharacters.Contains(playerStats);
            //Debug.Log($"[Boss2] Player safety check: distance={dist} radius={radius} alreadyHit={alreadyHit} playerStats={(playerStats != null ? playerStats.GetType().Name : "NULL")}");

            if (!alreadyHit && dist <= radius && playerStats != null)
            {
                //Debug.Log($"[Boss2] DIRECT player hit via safety net");
                playerStats.TakeDamage(meteorDamage);
            }
        }
        else
        {
            Debug.LogWarning("[Boss2] No GameObject with tag 'Player' found — explosion cannot damage player.");
        }
    }


    //  SUMMON ATTACK 
    private IEnumerator PerformSummonAttack()
    {
        // Rich magical flash at the boss body — replaces the previous ugly inflating
        // white disc with a multi-layer effect that reads as "lich is conjuring something":
        // bright core flash, expanding magic ring, swirling sparkles, runic sigil ground mark.
        SpawnSummonFlash(transform.position);

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.bossGroundHit, transform.position);

        yield return new WaitForSeconds(summonSpawnDelay);

        if (isDying) yield break;

        // Spawn slimes in an evenly-spaced ring around the boss. A tiny per-slot
        // smoke puff sells the "they appear out of magic" idea.
        if (smallSlimePrefab == null)
        {
            Debug.LogWarning("Boss2: smallSlimePrefab is not assigned — summon attack cannot spawn anything.");
            yield break;
        }

        // Compute a spawn radius that's guaranteed to be OUTSIDE the boss's collider.
        float bossColliderR = 0f;
        var bossCol = GetComponent<CircleCollider2D>();
        if (bossCol != null) bossColliderR = bossCol.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
        float safeSpawnRadius = Mathf.Max(summonRingRadius, bossColliderR + 0.5f);

        // Spread the spawns across frames so the engine doesn't have to instantiate
        // every slime, run their Awake/Start chains, and load their sprites in a single
        // frame. 
        for (int i = 0; i < summonCount; i++)
        {
            float angleDeg = (360f / summonCount) * i + Random.Range(-10f, 10f);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0f) * safeSpawnRadius;
            Vector3 spawnPos = transform.position + offset;

            SpawnSmokePuff(spawnPos, 0.6f);
            GameObject slime = Instantiate(smallSlimePrefab, spawnPos, Quaternion.identity);

            var slimeAnim = slime.GetComponent<EnemyAnimationController>();
            if (slimeAnim != null) slimeAnim.enabled = true;
            var slimeCtl = slime.GetComponent<EnemyController>();
            if (slimeCtl != null) slimeCtl.enabled = true;

            StartCoroutine(SuperviseSpawnedSlime(slime));

            // Yield one frame between spawns. The smoke puffs cover the staggered timing
            // so it still reads as "they all appeared at once". Skip the wait after the
            // final spawn so the attack ends without an unnecessary frame of lag.
            if (i < summonCount - 1)
                yield return null;
        }
    }


    private IEnumerator SuperviseSpawnedSlime(GameObject slime)
    {
        if (slime == null) yield break;

        // Wait one frame so Start() runs on the slime's components.
        yield return null;
        if (slime == null) yield break;

        Vector3 startPos = slime.transform.position;
        const float watchDuration = 1.2f;
        const float minMoveThreshold = 0.15f;
        float elapsed = 0f;

        while (elapsed < watchDuration)
        {
            elapsed += Time.deltaTime;
            yield return null;

            if (slime == null) yield break;

            float moved = Vector3.Distance(slime.transform.position, startPos);
            if (moved >= minMoveThreshold)
                yield break; // healthy slime, supervisor done
        }

        // Slime never moved in the watch window — treat it as stuck and respawn.
        // Use the same position so the player still sees the slime materialize there.
        if (slime == null || smallSlimePrefab == null) yield break;
        Vector3 stuckPos = slime.transform.position;
        Destroy(slime);

        GameObject fresh = Instantiate(smallSlimePrefab, stuckPos, Quaternion.identity);
        var freshAnim = fresh.GetComponent<EnemyAnimationController>();
        if (freshAnim != null) freshAnim.enabled = true;
        var freshCtl = fresh.GetComponent<EnemyController>();
        if (freshCtl != null) freshCtl.enabled = true;
    }

    private void SpawnSmokePuff(Vector3 pos, float scale)
    {
        EnsureCircleSprite();

        GameObject puff = new GameObject("Boss2_SmokePuff");
        puff.transform.position = new Vector3(pos.x, pos.y, 0f);
        puff.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = puff.AddComponent<SpriteRenderer>();
        sr.sprite = _filledCircleSprite;
        sr.color = summonSmokeColor;
        sr.sortingLayerName = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        sr.sortingOrder = 2400;

        var fx = puff.AddComponent<Boss2SmokeFX>();
        fx.Initialize(scale, 0.7f);
    }

    // A magical summon flash for the boss body — layered effect that reads as
    // "conjuration in progress" rather than the previous inflating-white-disc look.
    // Composed of: bright pinkish-purple core flash, expanding magical ring (hollow),
    // 8 swirling sparkle motes, and a faint runic disc on the ground.
    private void SpawnSummonFlash(Vector3 pos)
    {
        var root = new GameObject("Boss2_SummonFlash");
        root.transform.position = new Vector3(pos.x, pos.y, 0f);
        var fx = root.AddComponent<Boss2SummonFlashFX>();
        string layer = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        fx.Play(layer);
    }


    //  CIRCLE SPRITE FACTORY 
    private static void EnsureCircleSprite()
    {
        if (_filledCircleSprite != null) return;

        // 128px texture, antialiased edge, white so it can be tinted via SpriteRenderer.color.
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.5f - 1f;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                // 1.5px feathered edge so the circle doesn't look jaggy at small scales.
                float a = Mathf.Clamp01((radius - dist) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        // pixelsPerUnit = size → the resulting sprite is exactly 1 world unit across,
        // which makes scaling by (radius*2) line up with real damage geometry.
        _filledCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }


    //  PROJECTILE DAMAGE INTAKE 

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isDying) return;

        var wp = other.GetComponent<WeaponProjectile>();
        if (wp != null)
        {
            //Debug.Log($"[Boss2] OnTriggerEnter2D: WeaponProjectile {other.name} dmg={wp.GetDamage()}");
            TakeDamage(wp.GetDamage());
            Destroy(other.gameObject);
            return;
        }

        var proj = other.GetComponent<Projectile>();
        if (proj != null)
        {
            //Debug.Log($"[Boss2] OnTriggerEnter2D: Projectile {other.name} dmg={proj.damage}");
            TakeDamage(proj.damage);
            Destroy(other.gameObject);
            return;
        }
    }


    //  DAMAGE / DEATH 

    public override void TakeDamage(float amount)
    {
        //Debug.Log($"[Boss2] TakeDamage({amount}) called. isDying={isDying}, armorDestroyed={armorDestroyed}, bossArmor={bossArmor}, currentHealth={currentHealth}");

        if (isDying) return;

        if (!armorDestroyed && bossArmor > 0)
        {
            bossArmor -= amount;
            if (bossArmor <= 0)
            {
                float overflow = -bossArmor;
                bossArmor = 0;
                OnArmorDestroyed();
                currentHealth -= overflow;
            }
        }
        else
        {
            currentHealth -= amount;
        }

        CallStartDamageFlash();
        UpdateBossHealthBar();

        //Debug.Log($"[Boss2] After damage: bossArmor={bossArmor}, currentHealth={currentHealth}, HealthBar={(HealthBar != null ? "OK" : "NULL")}");

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            ExecuteBossDeath();
        }
    }

    public override void Die()
    {
        if (isDying) return;
        currentHealth = 0f;
        ExecuteBossDeath();
    }

    private void ExecuteBossDeath()
    {
        isDying = true;
        if (!gameObject.scene.isLoaded) return;

        transform.rotation = Quaternion.identity;
        CombatJuice.OnBossKilled(gameObject);

        // Reset to idle frame 0 BEFORE disabling animController, mirroring Boss1.
        if (bossSprite != null && enemyData != null)
        {
            var allSprites = Resources.LoadAll<Sprite>(enemyData.spriteFolderPath);
            if (allSprites != null && allSprites.Length > 0)
            {
                System.Array.Sort(allSprites, (a, b) => a.name.CompareTo(b.name));
                if (enemyData.idle.startFrame < allSprites.Length)
                    bossSprite.sprite = allSprites[enemyData.idle.startFrame];
            }
        }

        // Hide the rod if it was visible mid-cast.
        SetRodVisible(false);

        // Destroy any active warning ring BEFORE StopAllCoroutines kills its watcher.
        if (_activeWarningRing != null)
        {
            Destroy(_activeWarningRing);
            _activeWarningRing = null;
        }

        if (HealthBar != null)
            Destroy(HealthBar.gameObject);

        StopAllCoroutines();

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;

        foreach (var col in GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        var ec = GetComponent<EnemyController>();
        if (ec != null) ec.enabled = false;
        if (animController != null) animController.enabled = false;

        bossArmor = 0f;
        armorDestroyed = true;

        Vector3 deathPos = transform.position;

        // Drop boss energy rewards — same pattern as Boss1 (10 orbs in a ring).
        for (int i = 0; i < 10; i++)
        {
            float angle = (360f / 10) * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 1.5f;
            Vector3 spawnPos = deathPos + offset;
            int energyValue = (EnergyDropManager.Instance != null)
                ? EnergyDropManager.Instance.defaultEnergyValue
                : 10;
            EnergyDrop.CreateEnergyDrop(spawnPos, energyValue);
        }

        // Roll for a permanent weapon/tool blueprint drop, same as every boss.
        RollBlueprintDrop(deathPos);

        if (EnergyManager.Instance != null)
            EnergyManager.Instance.OnEnemyKilled(gameObject);

        // Run the standard disintegration VFX, then play the boss death sound on completion.
        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: disintegrationDuration,
            onComplete: () =>
            {
                if (AudioManager.instance != null && FMODEvents.instance != null)
                    AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, deathPos);
            });
    }


    //  CLEANUP 
    private void OnDestroy()
    {
        if (HealthBar != null)
            Destroy(HealthBar.gameObject);
    }


    //  DEBUG GIZMOS 
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Attack range / melee deadzone — easier to tune in the editor than guessing numbers.
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, meleeOnlyRange);

        // Summon ring preview.
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, summonRingRadius);

        // Meteor explosion radius preview at player position (if running).
        if (Application.isPlaying && currentTarget != null)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(currentTarget.position, meteorExplosionRadius);
        }
    }
#endif
}


/// A pulsating warning ring drawn above the grass background
public class Boss2MeteorWarningRing : MonoBehaviour
{
    // Sort above the grass overlay 
    private const string SortLayer = "Default";
    private const int RingFillOrder = 3000;
    private const int RingStrokeOrder = 3001;
    private const int RingInnerOrder = 3002;

    private float _radius;
    private float _duration;
    private Color _baseColor;
    private Color _pulseColor;

    private SpriteRenderer _fill;        // semi-transparent danger zone shading
    private SpriteRenderer _outerRing;   // bright pulsing stroke around the perimeter
    private SpriteRenderer _innerRing;   // a smaller inner ring that pulses out of phase

    public void Initialize(float radius, float duration, Color baseColor, Color pulseColor)
    {
        _radius = Mathf.Max(0.4f, radius);
        _duration = Mathf.Max(0.1f, duration);
        _baseColor = baseColor;
        _pulseColor = pulseColor;
        BuildVisuals();
        StartCoroutine(Run());
    }

    private void BuildVisuals()
    {
        // Soft transparent fill — paints the danger zone faintly so the player
        // can tell at a glance which patch of ground will explode.
        var fillGo = new GameObject("WarningFill");
        fillGo.transform.SetParent(transform, false);
        fillGo.transform.localScale = Vector3.one * (_radius * 2f);
        _fill = fillGo.AddComponent<SpriteRenderer>();
        _fill.sprite = Boss2WarningSprites.GetFilledDisc();
        _fill.sortingLayerName = SortLayer;
        _fill.sortingOrder = RingFillOrder;
        Color fillCol = _baseColor;
        fillCol.a = 0.22f;
        _fill.color = fillCol;

        // The bright stroke around the perimeter
        var outerGo = new GameObject("WarningRingOuter");
        outerGo.transform.SetParent(transform, false);
        outerGo.transform.localScale = Vector3.one * (_radius * 2f);
        _outerRing = outerGo.AddComponent<SpriteRenderer>();
        _outerRing.sprite = Boss2WarningSprites.GetRing(thicknessFraction: 0.07f);
        _outerRing.sortingLayerName = SortLayer;
        _outerRing.sortingOrder = RingStrokeOrder;
        _outerRing.color = _baseColor;

        // A second smaller ring pulses INWARD out-of-phase with the outer one,
        // creating a "closing in" feel as the explosion approaches.
        var innerGo = new GameObject("WarningRingInner");
        innerGo.transform.SetParent(transform, false);
        _innerRing = innerGo.AddComponent<SpriteRenderer>();
        _innerRing.sprite = Boss2WarningSprites.GetRing(thicknessFraction: 0.04f);
        _innerRing.sortingLayerName = SortLayer;
        _innerRing.sortingOrder = RingInnerOrder;
        Color innerCol = _baseColor;
        innerCol.a = 0.85f;
        _innerRing.color = innerCol;
    }

    private IEnumerator Run()
    {
        float elapsed = 0f;
        while (elapsed < _duration)
        {
            float progress = Mathf.Clamp01(elapsed / _duration);

            // Pulse rate accelerates as the explosion nears — early seconds are
            // 1.5 Hz, the last quarter ramps to ~6 Hz so the warning feels urgent.
            float pulseHz = Mathf.Lerp(1.5f, 6f, progress * progress);
            float pulsePhase = Mathf.Sin(elapsed * pulseHz * Mathf.PI * 2f);
            // Map sin [-1..1] to a punchier [0..1] curve so the dim/bright contrast reads as a beat.
            float pulse01 = Mathf.Pow(0.5f + 0.5f * pulsePhase, 1.5f);

            // Outer ring
            float outerScaleFactor = Mathf.Lerp(0.97f, 1.05f, pulse01);
            _outerRing.transform.localScale = Vector3.one * (_radius * 2f * outerScaleFactor);
            Color outerCol = Color.Lerp(_baseColor, _pulseColor, pulse01);
            outerCol.a = Mathf.Lerp(0.25f, 0.55f, progress); // was 0.8→1.0, now much softer
            _outerRing.color = outerCol;

            // Inner ring: contracts and expands inversely, so the two rings "breathe against each other".
            // It also tightens its average radius as the timer runs down (closing in on impact).
            float innerBase = Mathf.Lerp(0.75f, 0.55f, progress);
            float innerScale = innerBase + (1f - pulse01) * 0.18f;
            _innerRing.transform.localScale = Vector3.one * (_radius * 2f * innerScale);
            Color innerCol = Color.Lerp(_pulseColor, _baseColor, pulse01);
            innerCol.a = Mathf.Lerp(0.18f, 0.45f, progress); // was 0.55→0.9, now much softer
            _innerRing.color = innerCol;

            // Fill: intensifies slightly as the explosion nears. Slightly stronger than before
            // since it's now carrying more of the visual weight after toning down the rings.
            Color fillCol = Color.Lerp(_baseColor, _pulseColor, pulse01 * 0.6f);
            fillCol.a = Mathf.Lerp(0.2f, 0.4f, progress);
            _fill.color = fillCol;

            elapsed += Time.deltaTime;
            yield return null;
        }
        // Boss2 destroys this object right after the timer; we leave the
        // visuals fully visible at end-of-window so they don't dim before the boom.
    }
}


// Procedural sprite cache for the warning ring. Built once on first use, shared
// across every meteor cast for the rest of the session.
public static class Boss2WarningSprites
{
    private static Sprite _filledDisc;
    // Two ring stroke variants cached by thickness fraction (relative to radius).
    private static readonly Dictionary<float, Sprite> _rings = new Dictionary<float, Sprite>();

    public static Sprite GetFilledDisc()
    {
        if (_filledDisc != null) return _filledDisc;

        // 128px circle with feathered edge for clean upscaling.
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float c = (S - 1) * 0.5f;
        float r = c - 1f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                // 1.5px feathered edge so the disc doesn't look jagged at small scales.
                float a = Mathf.Clamp01((r - d) / 1.5f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _filledDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _filledDisc;
    }

    // A hollow ring sprite. thicknessFraction = ring stroke width as a fraction of radius
    // (e.g. 0.07 = a stroke 7% of the radius wide). Cached per thickness so the same value
    // is built only once.
    public static Sprite GetRing(float thicknessFraction)
    {
        if (_rings.TryGetValue(thicknessFraction, out var cached) && cached != null)
            return cached;

        const int S = 256; // higher res than the disc — ring strokes need crispness
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float c = (S - 1) * 0.5f;
        float outerR = c - 1f;
        float halfStroke = outerR * thicknessFraction * 0.5f;
        // The ring is centered on the radius at (outerR - halfStroke) so the OUTER edge of
        // the stroke is exactly at the sprite's edge. That makes the sprite's bounds
        // line up with the radius the caller asked for.
        float ringCenterR = outerR - halfStroke;

        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                // Distance from the ideal ring centerline.
                float distFromRing = Mathf.Abs(d - ringCenterR);
                // Inside the stroke: full alpha. At the edges: 1.5px feather.
                float a;
                if (distFromRing <= halfStroke - 1.5f)
                    a = 1f;
                else if (distFromRing >= halfStroke + 1.5f)
                    a = 0f;
                else
                    a = Mathf.Clamp01((halfStroke + 1.5f - distFromRing) / 3f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        // PPU = S so the sprite is exactly 1 world unit across at scale 1 — scaling by
        // (radius * 2) gives a ring matching the caller's radius. Same convention as the
        // other procedural sprite caches in this file.
        var sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        _rings[thicknessFraction] = sprite;
        return sprite;
    }
}


/// Explosion VFX.
public class Boss2MeteorVFX : MonoBehaviour
{
    // Sorting strategy — mirrors HammerSlamVFX so the meteor blast slots into the
    // same visual layering used by the hammer's slam.
    private const string SortLayer = "Default";
    private const int ScorchOrder = -110;
    private const int CrackOrder = -100;
    private const int DustOrder = 5200;
    private const int DebrisOrder = 5400;
    private const int FlashOrder = 5600;

    // Meteor color palette — hotter and more orange than the hammer's dusty palette
    // so the AoE reads as fire, not stone-on-stone.
    private static readonly Color FireOrange = new Color(1f, 0.55f, 0.10f, 1f);
    private static readonly Color EmberRed = new Color(0.9f, 0.18f, 0.08f, 1f);
    private static readonly Color CharcoalBrown = new Color(0.18f, 0.10f, 0.07f, 1f);
    private static readonly Color DustTan = new Color(0.78f, 0.66f, 0.5f, 1f);

    private float _radius;

    public void Play(float radius)
    {
        _radius = Mathf.Max(0.4f, radius);
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        // Instant ground marks first so they appear behind everything else.
        BuildScorch();
        BuildCracks();

        // Front-of-player volume + burst.
        BuildDustShockwave();
        BuildDustDisc();
        BuildDebris();
        BuildCoreFlash();

        // The longest-lived child (debris flight + bounce + rest + fade) drives the
        // self-destruct delay. After 2.6s every spawned sub-coroutine is finished.
        yield return new WaitForSeconds(2.6f);
        Destroy(gameObject);
    }

    //  SCORCH: a dark patch stamped on the ground at impact 
    private void BuildScorch()
    {
        var go = new GameObject("Scorch");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetSoftDisc();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = ScorchOrder;
        Color c = CharcoalBrown; c.a = 0.7f;
        sr.color = c;
        go.transform.localScale = Vector3.one * (_radius * 1.8f);
        // Scorch sticks around briefly then fades out.
        StartCoroutine(FadeSprite(sr, life: 1.6f, delay: 0.35f));
    }

    //  CRACKS: thin radial slash marks that look painted onto the ground 
    private void BuildCracks()
    {
        int crackCount = Random.Range(6, 10);
        for (int i = 0; i < crackCount; i++)
        {
            float angle = (360f / crackCount) * i + Random.Range(-12f, 12f);
            float len = _radius * Random.Range(0.55f, 1.0f);

            // Each crack is a thin rectangle aligned to the angle. We segment
            // it into 2–4 sub-rects offset slightly to look jagged rather than
            // perfectly straight.
            int segments = Random.Range(2, 5);
            for (int s = 0; s < segments; s++)
            {
                float segStart = (s / (float)segments) * len;
                float segEnd = ((s + 1) / (float)segments) * len;
                float segLen = segEnd - segStart;
                float segMid = (segStart + segEnd) * 0.5f;

                var go = new GameObject($"Crack_{i}_{s}");
                go.transform.SetParent(transform, false);

                // Offset midpoint slightly perpendicular for a jagged break.
                float jitter = Random.Range(-0.08f, 0.08f) * _radius;
                Vector2 perp = new Vector2(-Mathf.Sin(angle * Mathf.Deg2Rad),
                                            Mathf.Cos(angle * Mathf.Deg2Rad));
                Vector2 forward = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad),
                                              Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector2 pos = forward * segMid + perp * jitter;
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Boss2VFXSprites.GetPixel();
                sr.sortingLayerName = SortLayer;
                sr.sortingOrder = CrackOrder;
                sr.color = CharcoalBrown;
                go.transform.localScale = new Vector3(segLen, Random.Range(0.04f, 0.08f) * _radius, 1f);

                StartCoroutine(FadeSprite(sr, life: 1.4f, delay: 0.5f));
            }
        }
    }

    //  DUST SHOCKWAVE: rolling ring of dust expanding outward 
    private void BuildDustShockwave()
    {
        // Multiple small dust puffs in a ring, each expanding outward.
        int puffCount = 14;
        for (int i = 0; i < puffCount; i++)
        {
            float angle = (360f / puffCount) * i * Mathf.Deg2Rad + Random.Range(-0.15f, 0.15f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            var go = new GameObject($"DustPuff_{i}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Boss2VFXSprites.GetSoftDisc();
            sr.sortingLayerName = SortLayer;
            sr.sortingOrder = DustOrder;
            // Warm dust — sand-tan tinged with the fire orange so the cloud reads as
            // "kicked up by something hot" rather than a generic gray puff.
            Color dust = Color.Lerp(DustTan, FireOrange, 0.25f);
            dust.a = 0.78f;
            sr.color = dust;

            float startR = _radius * Random.Range(0.08f, 0.18f);
            float endR = _radius * Random.Range(0.95f, 1.25f);
            float startSize = _radius * Random.Range(0.16f, 0.26f);
            float endSize = _radius * Random.Range(0.45f, 0.72f);
            StartCoroutine(DustPuffRoutine(go.transform, sr, dir, startR, endR, startSize, endSize));
        }
    }

    private IEnumerator DustPuffRoutine(Transform t, SpriteRenderer sr, Vector2 dir,
                                        float startR, float endR, float startSize, float endSize)
    {
        float life = Random.Range(0.6f, 0.85f);
        Color startCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float eased = 1f - (1f - p) * (1f - p); // ease-out
            t.localPosition = dir * Mathf.Lerp(startR, endR, eased);
            float s = Mathf.Lerp(startSize, endSize, eased);
            t.localScale = new Vector3(s, s, 1f);
            Color c = startCol; c.a = startCol.a * (1f - p); sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  DUST DISC: soft ground-hugging cloud at the impact center 
    private void BuildDustDisc()
    {
        var go = new GameObject("DustDisc");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetSoftDisc();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DustOrder - 1;
        // Slightly darker, slightly more orange than the ring puffs.
        Color dust = Color.Lerp(DustTan, FireOrange, 0.4f);
        dust.a = 0.55f;
        sr.color = dust;

        StartCoroutine(DustDiscRoutine(go.transform, sr));
    }

    private IEnumerator DustDiscRoutine(Transform t, SpriteRenderer sr)
    {
        float life = 0.9f;
        Color startCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float eased = 1f - (1f - p) * (1f - p);
            t.localScale = Vector3.one * Mathf.Lerp(_radius * 0.4f, _radius * 2.1f, eased);
            Color c = startCol; c.a = startCol.a * (1f - p); sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  DEBRIS: arcing rock chunks scattered outward by the blast 
    private void BuildDebris()
    {
        int debrisCount = Random.Range(12, 18);
        for (int i = 0; i < debrisCount; i++)
        {
            bool bigChunk = Random.value < 0.3f;
            StartCoroutine(DebrisChunk(bigChunk));
        }
    }

    private IEnumerator DebrisChunk(bool bigChunk)
    {
        var go = new GameObject("Debris");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetRockChunk();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DebrisOrder;

        // Dark earth tone, some chunks lean warmer (catching the fire's light).
        Color earth = Color.Lerp(CharcoalBrown, EmberRed, Random.Range(0f, 0.35f));
        earth = Color.Lerp(earth, earth * 1.3f, Random.Range(0f, 0.5f));
        sr.color = earth;

        float baseSize = bigChunk
            ? _radius * Random.Range(0.07f, 0.11f)
            : _radius * Random.Range(0.025f, 0.055f);
        float aspectX = Random.Range(0.75f, 1.25f);
        float aspectY = Random.Range(0.75f, 1.25f);
        Vector3 restScale = new Vector3(baseSize * aspectX, baseSize * aspectY, 1f);
        go.transform.localScale = restScale;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector2 outward = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        float launchDist = _radius * (bigChunk
            ? Random.Range(0.3f, 0.7f)
            : Random.Range(0.5f, 1.2f));
        float launchHeight = _radius * (bigChunk
            ? Random.Range(0.35f, 0.7f)
            : Random.Range(0.5f, 1.05f));
        float flightTime = Random.Range(0.42f, 0.7f) * (bigChunk ? 1.15f : 1f);
        float spin = Random.Range(-720f, 720f) * (bigChunk ? 0.5f : 1f);

        Vector3 start = Vector3.zero;
        float spinAccum = Random.Range(0f, 360f);

        // FLIGHT: parabolic arc to the landing point.
        float e = 0f;
        while (e < flightTime)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / flightTime);
            float horiz = 1f - (1f - t) * (1f - t);
            Vector3 ground = start + (Vector3)(outward * launchDist * horiz);
            float height = launchHeight * 4f * t * (1f - t);
            go.transform.localPosition = ground + Vector3.up * height;
            spinAccum += spin * Time.deltaTime;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spinAccum);
            yield return null;
        }

        Vector3 landPos = start + (Vector3)(outward * launchDist);
        go.transform.localPosition = landPos;

        // LANDING: squash + bounce + small dust puff.
        SpawnLandingDust(landPos, baseSize);

        // Squash on impact.
        float squashTime = 0.06f;
        e = 0f;
        while (e < squashTime)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / squashTime);
            go.transform.localScale = new Vector3(
                restScale.x * Mathf.Lerp(1f, 1.35f, t),
                restScale.y * Mathf.Lerp(1f, 0.6f, t),
                1f);
            yield return null;
        }
        // Bounce + un-squash.
        float bounceHeight = launchHeight * (bigChunk ? 0.1f : 0.16f);
        float bounceTime = 0.14f;
        e = 0f;
        while (e < bounceTime)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / bounceTime);
            float hop = bounceHeight * 4f * t * (1f - t);
            go.transform.localPosition = landPos + Vector3.up * hop;
            float s = Mathf.Lerp(0f, 1f, t);
            go.transform.localScale = new Vector3(
                restScale.x * Mathf.Lerp(1.35f, 1f, s),
                restScale.y * Mathf.Lerp(0.6f, 1f, s),
                1f);
            spinAccum += spin * 0.25f * Time.deltaTime;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spinAccum);
            yield return null;
        }
        go.transform.localPosition = landPos;
        go.transform.localScale = restScale;

        yield return new WaitForSeconds(Random.Range(0.25f, 0.55f));

        // FADE.
        float fade = 0.4f;
        e = 0f;
        Color baseCol = sr.color;
        while (e < fade)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / fade);
            Color c = baseCol; c.a = baseCol.a * (1f - t); sr.color = c;
            yield return null;
        }
        Destroy(go);
    }

    private void SpawnLandingDust(Vector3 localPos, float chunkSize)
    {
        var go = new GameObject("DebrisDust");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetSoftDisc();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DebrisOrder - 1;
        Color dust = Color.Lerp(DustTan, FireOrange, 0.3f);
        dust.a = 0.45f;
        sr.color = dust;
        StartCoroutine(LandingDustRoutine(go.transform, sr, chunkSize));
    }

    private IEnumerator LandingDustRoutine(Transform t, SpriteRenderer sr, float chunkSize)
    {
        float life = 0.32f;
        float startSize = chunkSize * 1.2f;
        float endSize = chunkSize * 3.2f;
        Color baseCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            t.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, p);
            Color c = baseCol; c.a = baseCol.a * (1f - p); sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  CORE FLASH: sharp bright pop at the point of impact 
    private void BuildCoreFlash()
    {
        var go = new GameObject("CoreFlash");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetSoftDisc();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = FlashOrder;
        StartCoroutine(CoreFlashRoutine(go.transform, sr));
    }

    private IEnumerator CoreFlashRoutine(Transform t, SpriteRenderer sr)
    {
        // Bright hot core that snaps to size then shrinks and fades.
        Color hot = Color.Lerp(FireOrange, Color.white, 0.7f);
        float life = 0.28f;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            t.localScale = Vector3.one * Mathf.Lerp(_radius * 1.25f, _radius * 0.55f, p);
            Color c = hot; c.a = Mathf.Lerp(0.95f, 0f, p);
            sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  SHARED FADE HELPER 
    private IEnumerator FadeSprite(SpriteRenderer sr, float life, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (sr == null) yield break;
        Color baseCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / life);
            Color c = baseCol; c.a = baseCol.a * (1f - t); sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }
}


// Procedural sprite cache for Boss2's VFX. All sprites are baked once on first
// use and shared by every meteor explosion thereafter.
public static class Boss2VFXSprites
{
    private static Sprite _pixel;
    private static Sprite _softDisc;
    private static Sprite[] _rockChunks;

    // A 1×1 white pixel — used for crack rectangles. Scale + color via SpriteRenderer.
    public static Sprite GetPixel()
    {
        if (_pixel != null) return _pixel;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        _pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _pixel;
    }

    // A soft radial-gradient disc — used for dust puffs and flash glows.
    public static Sprite GetSoftDisc()
    {
        if (_softDisc != null) return _softDisc;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float c = (S - 1) * 0.5f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                // smoothstep — opaque core, feathered edge.
                a = a * a * (3f - 2f * a);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _softDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _softDisc;
    }

    // A small set of irregular rock chunk sprites for debris variety.
    public static Sprite GetRockChunk()
    {
        if (_rockChunks == null) BakeRockChunks();
        return _rockChunks[Random.Range(0, _rockChunks.Length)];
    }

    private static void BakeRockChunks()
    {
        const int variants = 6;
        const int S = 32;
        _rockChunks = new Sprite[variants];
        float c = (S - 1) * 0.5f;

        for (int v = 0; v < variants; v++)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Jagged silhouette: per-angle radius wobble so the outline is angular and
            // asymmetric, like a real fractured stone shard.
            const int spokes = 11;
            float[] radii = new float[spokes];
            for (int s = 0; s < spokes; s++)
                radii[s] = Random.Range(0.52f, 0.96f);

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float ang = Mathf.Atan2(dy, dx);
                    if (ang < 0f) ang += Mathf.PI * 2f;
                    float fs = ang / (Mathf.PI * 2f) * spokes;
                    int s0 = Mathf.FloorToInt(fs) % spokes;
                    int s1 = (s0 + 1) % spokes;
                    float edge = Mathf.Lerp(radii[s0], radii[s1], fs - Mathf.Floor(fs));

                    if (dist > edge)
                    {
                        px[y * S + x] = Color.clear;
                        continue;
                    }

                    // Baked shading: brighter toward the top, darker at the base,
                    // soft 1px feather at the silhouette edge for anti-alias.
                    float topLight = Mathf.Lerp(0.55f, 1.15f, (dy + 1f) * 0.5f);
                    float coreShade = Mathf.Lerp(0.82f, 1f, dist / Mathf.Max(edge, 0.001f));
                    float lum = Mathf.Clamp01(topLight * coreShade);
                    float aa = Mathf.Clamp01((edge - dist) * c);
                    px[y * S + x] = new Color(lum, lum, lum, aa);
                }

            tex.SetPixels(px);
            tex.Apply();
            _rockChunks[v] = Sprite.Create(tex, new Rect(0, 0, S, S),
                                           new Vector2(0.5f, 0.5f), S);
        }
    }
}


// Tiny self-destructing animator for the summon-attack smoke puff.
public class Boss2SmokeFX : MonoBehaviour
{
    private float _baseScale;
    private float _duration;
    private float _elapsed;
    private SpriteRenderer _sr;
    private Color _startColor;

    public void Initialize(float baseScale, float duration)
    {
        _baseScale = baseScale;
        _duration = Mathf.Max(0.05f, duration);
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) _startColor = _sr.color;
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);

        // Expand to 1.8× and drift upward slightly to look like a real smoke puff.
        float scale = Mathf.Lerp(0.4f, 1.8f, t) * _baseScale;
        transform.localScale = new Vector3(scale, scale, 1f);
        transform.position += Vector3.up * (0.3f * Time.deltaTime);

        if (_sr != null)
        {
            Color c = _startColor;
            // Ease-out alpha so the puff is solid at first and fades gently.
            c.a = Mathf.Lerp(_startColor.a, 0f, t * t);
            _sr.color = c;
        }

        if (_elapsed >= _duration)
            Destroy(gameObject);
    }
}


// Magical summon flash
public class Boss2SummonFlashFX : MonoBehaviour
{
    // Lich-y purple/pink palette. The "magic" reads cleanly against any biome.
    private static readonly Color CoreHot = new Color(1f, 0.85f, 1f, 1f);   // near-white pink core
    private static readonly Color CoreOuter = new Color(0.85f, 0.25f, 1f, 1f);   // saturated purple
    private static readonly Color RingColor = new Color(0.95f, 0.5f, 1f, 1f);   // bright magenta ring
    private static readonly Color SparkleColor = new Color(1f, 0.75f, 1f, 1f);   // hot pink sparkle
    private static readonly Color SigilColor = new Color(0.6f, 0.15f, 0.95f, 1f);   // deep violet ground sigil

    private const int SigilOrder = 2300;   // BELOW boss (ground decal)
    private const int CoreOrder = 2500;   // ABOVE boss so the flash is the focal point
    private const int RingOrder = 2510;
    private const int SparkleOrder = 2520;

    private string _sortLayer = "Default";

    public void Play(string sortingLayerName)
    {
        if (!string.IsNullOrEmpty(sortingLayerName)) _sortLayer = sortingLayerName;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        BuildSigil();
        BuildCore();
        BuildRing();
        BuildSparkles();
        // The longest sub-coroutine is the sigil at ~1.2s. Self-destruct safely after that.
        yield return new WaitForSeconds(1.4f);
        Destroy(gameObject);
    }

    //  GROUND SIGIL 
    private void BuildSigil()
    {
        var go = new GameObject("SummonSigil");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2SummonFlashSprites.GetSoftDisc();
        sr.sortingLayerName = _sortLayer;
        sr.sortingOrder = SigilOrder;
        Color c = SigilColor; c.a = 0.5f;
        sr.color = c;
        StartCoroutine(SigilRoutine(go.transform, sr));
    }

    private IEnumerator SigilRoutine(Transform t, SpriteRenderer sr)
    {
        // Snap to size instantly, hold briefly, then fade out.
        const float life = 1.2f;
        const float targetScale = 2.2f;
        Color startCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            // First 15% of life: snap in (0 → full)
            // Last 60% of life: fade out
            float alphaMul = p < 0.15f ? p / 0.15f : Mathf.Lerp(1f, 0f, (p - 0.4f) / 0.6f);
            alphaMul = Mathf.Clamp01(alphaMul);
            float scale = Mathf.Lerp(0.3f, targetScale, Mathf.Clamp01(p / 0.15f));
            t.localScale = new Vector3(scale, scale, 1f);
            Color c = startCol; c.a = startCol.a * alphaMul; sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  CORE FLASH 
    private void BuildCore()
    {
        var go = new GameObject("SummonCore");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2SummonFlashSprites.GetSoftDisc();
        sr.sortingLayerName = _sortLayer;
        sr.sortingOrder = CoreOrder;
        StartCoroutine(CoreRoutine(go.transform, sr));
    }

    private IEnumerator CoreRoutine(Transform t, SpriteRenderer sr)
    {
        const float life = 0.35f;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            // Snap in fast (first 12% of life), then shrink and fade.
            float openP = Mathf.Clamp01(p / 0.12f);
            float settleP = Mathf.Clamp01((p - 0.12f) / 0.88f);
            float scale = Mathf.Lerp(0.2f, 1.6f, openP) * Mathf.Lerp(1f, 0.55f, settleP);
            t.localScale = new Vector3(scale, scale, 1f);
            // Color lerps from hot near-white to saturated purple as it fades.
            Color col = Color.Lerp(CoreHot, CoreOuter, settleP);
            col.a = Mathf.Lerp(1f, 0f, p * p); // ease-out alpha
            sr.color = col;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  MAGIC RING
    private void BuildRing()
    {
        var go = new GameObject("SummonRing");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2SummonFlashSprites.GetThinRing();
        sr.sortingLayerName = _sortLayer;
        sr.sortingOrder = RingOrder;
        sr.color = RingColor;
        StartCoroutine(RingRoutine(go.transform, sr));
    }

    private IEnumerator RingRoutine(Transform t, SpriteRenderer sr)
    {
        const float life = 0.5f;
        float e = 0f;
        Color baseCol = sr.color;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            // Ease-out expansion so it bursts out fast then settles.
            float expansion = 1f - (1f - p) * (1f - p);
            float scale = Mathf.Lerp(0.3f, 2.6f, expansion);
            t.localScale = new Vector3(scale, scale, 1f);
            Color c = baseCol; c.a = baseCol.a * (1f - p); sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  SPARKLES
    private void BuildSparkles()
    {
        const int count = 8;
        for (int i = 0; i < count; i++)
        {
            float baseAngle = (360f / count) * i;
            StartCoroutine(SparkleRoutine(baseAngle));
        }
    }

    private IEnumerator SparkleRoutine(float baseAngleDeg)
    {
        var go = new GameObject("SummonSparkle");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2SummonFlashSprites.GetSoftDisc();
        sr.sortingLayerName = _sortLayer;
        sr.sortingOrder = SparkleOrder;
        sr.color = SparkleColor;

        float life = Random.Range(0.55f, 0.8f);
        float maxRadius = Random.Range(1.3f, 2.1f);
        float startSize = Random.Range(0.18f, 0.28f);
        // The spiral
        float spiralRotation = Random.Range(-90f, 90f);

        Color baseCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float dist = Mathf.Lerp(0f, maxRadius, 1f - (1f - p) * (1f - p)); // ease-out
            float ang = (baseAngleDeg + spiralRotation * p) * Mathf.Deg2Rad;
            go.transform.localPosition = new Vector3(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist, 0f);
            // Sparkle pulsates and shrinks as it flies.
            float twinkle = 1f + 0.3f * Mathf.Sin(e * 25f);
            float sizeNow = startSize * twinkle * Mathf.Lerp(1f, 0.4f, p);
            go.transform.localScale = new Vector3(sizeNow, sizeNow, 1f);
            Color c = baseCol;
            // Hold full alpha for first half, then fade.
            c.a = baseCol.a * (p < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (p - 0.5f) / 0.5f));
            sr.color = c;
            yield return null;
        }
        Destroy(go);
    }
}


// Procedural sprites for the summon flash. Baked once, shared forever.
public static class Boss2SummonFlashSprites
{
    private static Sprite _softDisc;
    private static Sprite _thinRing;

    public static Sprite GetSoftDisc()
    {
        if (_softDisc != null) return _softDisc;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float c = (S - 1) * 0.5f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                // Smoothstep for a soft glow falloff.
                a = a * a * (3f - 2f * a);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _softDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _softDisc;
    }

    public static Sprite GetThinRing()
    {
        if (_thinRing != null) return _thinRing;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float c = (S - 1) * 0.5f;
        float outerR = c - 1f;
        float strokeHalf = outerR * 0.05f; // 5% of radius = thin ring
        float centerR = outerR - strokeHalf;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c;
                float dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float distFromRing = Mathf.Abs(d - centerR);
                float a;
                if (distFromRing <= strokeHalf - 1.2f) a = 1f;
                else if (distFromRing >= strokeHalf + 1.2f) a = 0f;
                else a = Mathf.Clamp01((strokeHalf + 1.2f - distFromRing) / 2.4f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _thinRing = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _thinRing;
    }
}
