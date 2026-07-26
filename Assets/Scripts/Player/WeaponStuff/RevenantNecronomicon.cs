using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//  REVENANT NECRONOMICON  —  Tool (right-click slot, slot index 11)
//  Player presses Right-Click once to activate the Book. For the next
//  WeaponData.bookAuraDuration seconds (default 8s) a purple aura surrounds
//  the player. Any enemy that dies inside the aura instantly respawns as a
//  friendly ghost that fights on the player's side, attacking nearby
//  enemies until it expires.
//  INTEGRATION POINTS  (see RevenantNecronomicon_INTEGRATION.md for exact diffs)
//    1. WeaponData.cs           — add `isBook` flag + book settings
//    2. Weapon.cs               — add bookSystem field + 6 hooks
//    3. WeaponUnlockRegistry.cs — add { 321, 11 } to AugmentToSlot
//    4. AugmentBlueprintGate.cs — add { 321, 11 } to UnlockAugmentToSlot
//    5. WeaponBlueprintRegistry — add 11 to droppableSlotsWhitelist
//    6. WeaponRollController.cs — grow allWeaponSlots to 12
//    7. WeaponRollUI.cs         — add the `isBook` icon branch
//    8. CursorManager.cs        — add a Book cursor (optional)
//    9. augments.csv            — add the ID 321 line

//  RevenantNecronomiconSystem  —  the tool subsystem owned by Weapon
public class RevenantNecronomiconSystem
{
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;
    private readonly int playerIndex; // Phase 8: per-player cooldown reduction

    // The currently-active aura (null when the book is idle). This is a
    // transient scene object; only the TIMERS need to persist across swaps.
    private RevenantAura activeAura;

    //  Two-phase timing — PERSISTENT 
    // Phase 1 (AURA ACTIVE): the purple aura is up; the book cannot be recast.
    // Phase 2 (COOLDOWN): a recharge must elapse before the next cast.
    // The timers live on PlayerToolCooldownStore (a component on the player)
    private PlayerToolCooldownStore store;

    public enum BookPhase { Ready, AuraActive, CoolingDown }

    public RevenantNecronomiconSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;

        // Resolve the player transform the same way DecoyLauncherSystem does.
        var playerStats = weapon.GetComponentInParent<PlayerStats>();
        this.playerTransform = playerStats != null
            ? playerStats.transform
            : (weapon.transform.parent ?? weapon.transform);

        // Phase 8: which player owns this book, for per-player cooldown reduction.
        var ownerRef = weapon.GetComponentInParent<PlayerRef>();
        this.playerIndex = ownerRef != null ? ownerRef.PlayerIndex : 0;

        // Resolve (or create) the persistent cooldown store on the player.
        store = PlayerToolCooldownStore.GetOrCreate(weapon);

        // Re-attach to an aura that may still be running from before a tool
        // swap, so its reference is restored for IsAuraActive / Deactivate.
        if (store != null && store.book.IsActivePhase)
        {
            var existing = Object.FindFirstObjectByType<RevenantAura>();
            if (existing != null && !existing.IsExpired)
                activeAura = existing;
        }
    }

    // True while a purple aura is currently active around the player.
    public bool IsAuraActive => store != null && store.book.IsActivePhase;

    /// True while the book cannot be re-cast (aura active OR cooling down).
    public bool IsOnCooldown => CurrentPhase != BookPhase.Ready;

    /// Which phase the book is in right now — drives the WeaponRollUI gauge.
    public BookPhase CurrentPhase
    {
        get
        {
            if (store == null) return BookPhase.Ready;
            if (store.book.IsActivePhase) return BookPhase.AuraActive;
            if (store.book.IsCooldownPhase) return BookPhase.CoolingDown;
            return BookPhase.Ready;
        }
    }

    /// 0..1 progress of the AURA-ACTIVE phase. 1 = just cast, 0 = aura about
    /// to end. Rendered as a depleting countdown clock.
    public float AuraNormalized => store != null ? store.book.ActiveNormalized : 0f;

    /// 0..1 readiness of the COOLDOWN phase. 0 = aura just ended, 1 = ready.
    public float CooldownNormalized => store != null ? store.book.CooldownNormalized : 1f;

    // Called from Weapon.UpdateBookSystem() every frame.
    public void Update()
    {
        // NOTE: the timers themselves are advanced by PlayerToolCooldownStore's
        // own Update so they keep running while this tool is unequipped. Here
        // we only maintain the transient aura-GameObject reference.
        if (activeAura != null && (activeAura.gameObject == null || activeAura.IsExpired))
            activeAura = null;

        // Safety: if the persistent timer says the aura phase is over but a
        // stray aura object somehow lingers, expire it.
        if (store != null && !store.book.IsActivePhase
            && activeAura != null && activeAura.gameObject != null
            && !activeAura.IsExpired)
        {
            activeAura.ForceExpire();
            activeAura = null;
        }
    }

    public bool CanFire() => CurrentPhase == BookPhase.Ready;

    // Post-aura recharge length. 
    private float BookCooldownDuration()
    {
        if (data.bookCooldown > 0f) return CooldownModifier.Apply(data.bookCooldown, playerIndex);
        if (data.attackCooldown > 0f) return CooldownModifier.Apply(data.attackCooldown, playerIndex);
        return CooldownModifier.Apply(5f, playerIndex); // default recharge
    }

    // Called from Weapon.ExecuteToolAttack() when the player right-clicks.
    //    Ready           casts the aura, returns true.
    //    Aura active     ends the aura early and enters cooldown, returns false.
    //    Cooling down    does nothing, returns false.

    private float lastActivateTime = -999f;
    private const float ACTIVATE_DEBOUNCE = 0.12f;

    public bool Activate()
    {
        // Debounce: ignore a second call landing within ACTIVATE_DEBOUNCE of
        // the previous one (same click processed twice → no accidental
        // cast-then-cancel).
        if (Time.unscaledTime - lastActivateTime < ACTIVATE_DEBOUNCE)
            return false;
        lastActivateTime = Time.unscaledTime;

        // Second right-click while the aura is up → end it early.
        if (CurrentPhase == BookPhase.AuraActive)
        {
            Deactivate();
            return false;
        }

        // Right-click while cooling down → ignored.
        if (CurrentPhase != BookPhase.Ready) return false;
        if (playerTransform == null) return false;

        StartAura();
        return true;
    }

    /// Casts the aura and begins phase 1 (aura active).
    private void StartAura()
    {
        var go = new GameObject("RevenantAura");
        go.transform.position = playerTransform.position;
        go.layer = LayerMask.NameToLayer("Default");

        activeAura = go.AddComponent<RevenantAura>();
        activeAura.Initialize(
            player: playerTransform,
            radius: data.bookAuraRadius,
            duration: data.bookAuraDuration,
            armDelay: data.bookArmDelay,
            shadowLifetime: data.bookShadowLifetime,
            shadowDamage: data.bookShadowDamage,
            shadowAttackRange: data.bookShadowAttackRange,
            shadowAttackInterval: data.bookShadowAttackInterval,
            shadowMoveSpeed: data.bookShadowMoveSpeed,
            maxShadows: data.bookMaxShadows);

        // Start phase 1 on the PERSISTENT store. The recharge cooldown
        // (phase 2) is armed automatically by the store the moment the active
        // timer reaches zero — and the cooldown length is stashed now so that
        // handoff works even if the player has scrolled to another tool.
        if (store != null)
            store.book.StartActive(data.bookAuraDuration, BookCooldownDuration());

        // Activation SFX for the Revenant Necronomicon (the moment the aura is cast).
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.revenantActivation.IsNull)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.revenantActivation,
                                              playerTransform.position);
    }

    // Manual early end (second right-click while the aura is up)
    public void Deactivate()
    {
        if (CurrentPhase != BookPhase.AuraActive) return;

        if (activeAura != null && activeAura.gameObject != null)
            activeAura.ForceExpire();
        activeAura = null;

        // End phase 1 immediately and arm phase 2 on the persistent store.
        if (store != null)
            store.book.EndActiveStartCooldown(BookCooldownDuration());
    }

    /// Called from Weapon.CleanupToolSubsystems() on tool swap / Weapon destroy.
    public void Cleanup()
    {
        activeAura = null;
    }
}


//  RevenantConvertible  —  death hook stamped onto enemies inside the aura//
//  RevenantAura adds this to every enemy currently inside its radius and
//  removes it when the enemy leaves. 
//  Death detection (any one of these triggers the conversion, latched once):
//    EnemyController gets disabled  (EnemyStats.DelayedDeath does this first)
//    EnemyStats reports IsDead()    (covers no-animation / instant deaths)
//    OnDestroy()                    (final backstop)
public class RevenantConvertible : MonoBehaviour
{
    private RevenantAura owner;
    private EnemyStats enemyStats;
    private EnemyController enemyController;
    private bool enemyControllerSeenEnabled;

    private bool converted = false;   // latch — convert at most once
    private bool armed = false;       // becomes true once Setup ran

    // Called by RevenantAura right after AddComponent.
    public void Setup(RevenantAura aura)
    {
        owner = aura;
        enemyStats = GetComponent<EnemyStats>();
        enemyController = GetComponent<EnemyController>();
        // Remember whether the controller started out enabled, so a later
        // "became disabled" transition reliably signals death.
        enemyControllerSeenEnabled = enemyController != null && enemyController.enabled;
        armed = true;
    }

    // Called by RevenantAura when the enemy walks OUT of the aura without
    // dying — the marker is removed and no conversion happens.
    public void Detach()
    {
        owner = null;
        // If we never converted, remove ourselves cleanly.
        if (!converted)
            Destroy(this);
    }

    // Optional explicit hook. If you add a real death event to EnemyStats,
    // call this from it for frame-perfect conversion.
    public void NotifyDied()
    {
        TryConvert();
    }

    private void Update()
    {
        if (!armed || converted) return;

        // (1) EnemyStats.Die() / DelayedDeath() disables the EnemyController
        //     immediately at the start of the death sequence.
        if (enemyController != null
            && enemyControllerSeenEnabled
            && !enemyController.enabled)
        {
            TryConvert();
            return;
        }

        // (2) Instant-death path (no death animation): EnemyStats has already
        //     run base.Die(). CharacterStats.IsDead() is the cleanest signal.
        if (enemyStats != null && enemyStats.IsDead())
        {
            TryConvert();
            return;
        }
    }

    private void OnDestroy()
    {
        // (3) Final backstop — the corpse is being torn down. If it died while
        //     still inside the aura and nothing else caught it, convert now.
        //     The latch in TryConvert guards against double conversion.
        TryConvert();
    }

    private void TryConvert()
    {
        if (converted) return;
        converted = true;

        if (owner != null)
            owner.RaiseShadowAt(transform.position);
    }
}


//  RevenantAura  
public class RevenantAura : MonoBehaviour
{
    // Config (set by Initialize).
    private Transform player;
    private float radius = 5f;
    private float duration = 8f;
    private float armDelay = 0.3f;

    // Shadow config — forwarded to every ShadowAlly we spawn.
    private float shadowLifetime = 12f;
    private float shadowDamage = 12f;
    private float shadowAttackRange = 1.2f;
    private float shadowAttackInterval = 0.8f;
    private float shadowMoveSpeed = 3.5f;
    private int maxShadows = 6;

    // State.
    private bool isArmed = false;
    private float armTimer;
    private float lifeTimer;
    private bool isExpired = false;
    private int spawnedShadowCount = 0;

    // Enemies currently marked with a RevenantConvertible by this aura.
    private readonly Dictionary<int, RevenantConvertible> _marked
        = new Dictionary<int, RevenantConvertible>();

    // Visuals.
    private SpriteRenderer auraFillRenderer;
    private SpriteRenderer auraRingRenderer;
    private SpriteRenderer auraRing2Renderer;
    private float pulseTimer;
    private float spawnScale = 0f;

    private static readonly Color AURA_PURPLE = new Color(0.55f, 0.20f, 0.85f, 0.32f);
    private static readonly Color AURA_PURPLE_BRIGHT = new Color(0.70f, 0.35f, 1.00f, 0.55f);
    private static readonly Color AURA_RING = new Color(0.75f, 0.45f, 1.00f, 0.45f);

    private const int SORT_ORDER_BASE = 900; // above ground, below cursor (10000)

    public bool IsExpired => isExpired;
    public bool IsArmed => isArmed;
    public float RemainingTime => Mathf.Max(0f, lifeTimer);

    public void Initialize(Transform player, float radius, float duration, float armDelay,
                           float shadowLifetime, float shadowDamage, float shadowAttackRange,
                           float shadowAttackInterval, float shadowMoveSpeed, int maxShadows)
    {
        this.player = player;
        this.radius = Mathf.Max(0.5f, radius);
        this.duration = Mathf.Max(0.1f, duration);
        this.armDelay = Mathf.Max(0f, armDelay);

        this.shadowLifetime = shadowLifetime;
        this.shadowDamage = shadowDamage;
        this.shadowAttackRange = shadowAttackRange;
        this.shadowAttackInterval = shadowAttackInterval;
        this.shadowMoveSpeed = shadowMoveSpeed;
        this.maxShadows = Mathf.Max(1, maxShadows);

        this.armTimer = this.armDelay;
        this.lifeTimer = this.duration;
    }

    private void Start()
    {
        BuildVisual();
        spawnScale = 0f;
    }

    private void Update()
    {
        if (isExpired) return;

        // Follow the player.
        if (player != null)
            transform.position = player.position;

        // Pop-in (ease-out-back), scaled to the aura diameter.
        float targetScale = radius * 2f;
        if (spawnScale < 1f)
        {
            spawnScale = Mathf.Min(spawnScale + Time.deltaTime / 0.25f, 1f);
            float ease = 1f + 2.7f * Mathf.Pow(spawnScale - 1f, 3f)
                            + 1.7f * Mathf.Pow(spawnScale - 1f, 2f);
            transform.localScale = Vector3.one * ease * targetScale;
        }
        else
        {
            transform.localScale = Vector3.one * targetScale;
        }

        // Arm delay.
        if (!isArmed)
        {
            armTimer -= Time.deltaTime;
            if (armTimer <= 0f) isArmed = true;
        }

        // Life timer.
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Expire();
            return;
        }

        UpdateVisual();

        if (isArmed)
            UpdateEnemyMarkers();
    }

    //  ENEMY MARKING  —  stamp / unstamp the RevenantConvertible death hook
    private void UpdateEnemyMarkers()
    {
        Vector2 center = transform.position;
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");

        var insideThisFrame = new HashSet<int>();

        foreach (var go in enemies)
        {
            if (go == null || !go.activeInHierarchy) continue;
            if (go.GetComponent<ShadowAlly>() != null) continue;   // never mark shadows

            // Bosses are deliberately NOT convertible — a friendly boss-shadow
            // would be wildly overpowered. Skip marking them entirely.
            if (IsBoss(go)) continue;

            // Only mark things that can actually report death.
            if (go.GetComponent<EnemyStats>() == null) continue;

            float dist = Vector2.Distance(center, (Vector2)go.transform.position);
            if (dist > radius) continue;

            int id = go.GetInstanceID();
            insideThisFrame.Add(id);

            if (!_marked.ContainsKey(id))
            {
                var conv = go.AddComponent<RevenantConvertible>();
                conv.Setup(this);
                _marked[id] = conv;
            }
        }

        // Any previously-marked enemy not inside this frame either left the
        // aura or died. RevenantConvertible handles the "died" case itself
        // (its latch + OnDestroy). For the "left the aura" case we detach the
        // marker so a death OUTSIDE the aura later does not convert it.
        if (_marked.Count > 0)
        {
            var toRemove = new List<int>();
            foreach (var kv in _marked)
            {
                if (insideThisFrame.Contains(kv.Key)) continue;
                toRemove.Add(kv.Key);

                // kv.Value may be null already if the enemy was destroyed —
                // that's fine, the conversion (if any) already happened.
                if (kv.Value != null)
                    kv.Value.Detach();
            }
            foreach (int id in toRemove)
                _marked.Remove(id);
        }
    }

    /// Called by RevenantConvertible the instant a marked enemy dies.
    public void RaiseShadowAt(Vector3 position)
    {
        // The aura may have expired between the enemy's death frame and this
        // callback — honour the lifetime and don't raise late shadows.
        if (isExpired) return;
        if (spawnedShadowCount >= maxShadows) return;

        spawnedShadowCount++;

        ShadowAlly.Spawn(
            position,
            shadowLifetime,
            shadowDamage,
            shadowAttackRange,
            shadowAttackInterval,
            shadowMoveSpeed);

        SpawnConversionBurst(position);
    }

    private bool IsBoss(GameObject go)
    {
        // Mirrors EnemyController / DecoyDevice boss detection.
        return go.GetComponent<Boss1>() != null
            || go.GetComponent<BaseBossStats>() != null;
    }

    //  EXPIRE
    private void Expire()
    {
        if (isExpired) return;
        isExpired = true;

        // Detach every remaining marker so enemies that survived the aura
        // don't get converted by a death that happens after it ended.
        foreach (var kv in _marked)
            if (kv.Value != null) kv.Value.Detach();
        _marked.Clear();

        StartCoroutine(FadeOutAndDestroy());
    }

    /// Public so the owning system can cancel the aura early if desired.
    public void ForceExpire() => Expire();

    private IEnumerator FadeOutAndDestroy()
    {
        float fade = 0.4f, e = 0f;
        Vector3 startScale = transform.localScale;
        while (e < fade)
        {
            e += Time.deltaTime;
            float t = e / fade;

            transform.localScale = startScale * (1f - 0.15f * t);

            if (auraFillRenderer != null)
            { var c = auraFillRenderer.color; c.a *= (1f - t); auraFillRenderer.color = c; }
            if (auraRingRenderer != null)
            { var c = auraRingRenderer.color; c.a *= (1f - t); auraRingRenderer.color = c; }
            if (auraRing2Renderer != null)
            { var c = auraRing2Renderer.color; c.a *= (1f - t); auraRing2Renderer.color = c; }

            yield return null;
        }
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Safety net: if the aura GameObject is destroyed by something other
        // than Expire() (scene unload, etc.), still detach markers.
        foreach (var kv in _marked)
            if (kv.Value != null) kv.Value.Detach();
        _marked.Clear();
    }

    //  VISUALS  —  procedural, in the DecoyDevice.cs style
    private void BuildVisual()
    {
        // Soft filled disc.
        var fillObj = new GameObject("AuraFill");
        fillObj.transform.SetParent(transform, false);
        fillObj.transform.localPosition = Vector3.zero;
        auraFillRenderer = fillObj.AddComponent<SpriteRenderer>();
        auraFillRenderer.sprite = GenerateGlowSprite();
        auraFillRenderer.color = AURA_PURPLE;
        auraFillRenderer.sortingOrder = SORT_ORDER_BASE;
        fillObj.transform.localScale = Vector3.one;

        // Single rotating rim ring — kept small and semi-transparent so it
        // reads as an "occult circle" without obscuring the view. The second
        // counter-rotating ring stays removed; auraRing2Renderer remains null
        // (all its uses below are null-guarded).
        var ringObj = new GameObject("AuraRing");
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localPosition = Vector3.zero;
        auraRingRenderer = ringObj.AddComponent<SpriteRenderer>();
        auraRingRenderer.sprite = GenerateRingSprite();
        auraRingRenderer.color = AURA_RING;
        auraRingRenderer.sortingOrder = SORT_ORDER_BASE + 1;
        ringObj.transform.localScale = Vector3.one * 0.78f;
    }

    private void UpdateVisual()
    {
        // Breathing pulse on the fill.
        pulseTimer += Time.deltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(pulseTimer * 2.2f);

        if (auraFillRenderer != null)
        {
            float armingDim = isArmed ? 1f : 0.4f;
            auraFillRenderer.color = Color.Lerp(AURA_PURPLE, AURA_PURPLE_BRIGHT, pulse) * armingDim;
        }

        // Counter-rotating rims.
        if (auraRingRenderer != null)
            auraRingRenderer.transform.Rotate(0, 0, 22f * Time.deltaTime);
        if (auraRing2Renderer != null)
            auraRing2Renderer.transform.Rotate(0, 0, -30f * Time.deltaTime);

        // Fade the rim out over the final second so the player sees it ending.
        if (auraRingRenderer != null && lifeTimer < 1f)
        {
            float a = Mathf.Clamp01(lifeTimer);
            var c = AURA_RING; c.a *= a; auraRingRenderer.color = c;
        }
    }

    private void SpawnConversionBurst(Vector3 pos)
    {
        var burstObj = new GameObject("SoulBurst");
        burstObj.transform.position = pos;
        burstObj.AddComponent<SoulBurstVFX>().Play();
    }

    //  PROCEDURAL SPRITES  (cached, shared across all auras)
    private static Sprite _cachedGlow;
    private static Sprite GenerateGlowSprite()
    {
        if (_cachedGlow != null) return _cachedGlow;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float r = S * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 1f - Mathf.Clamp01(d / r);
                a = a * a; // quadratic falloff for a soft edge
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _cachedGlow = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedGlow;
    }

    private static Sprite _cachedRing;
    private static Sprite GenerateRingSprite()
    {
        if (_cachedRing != null) return _cachedRing;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float outerR = S * 0.5f;
        float innerR = S * 0.40f;
        float midR = (innerR + outerR) * 0.5f;
        float halfW = (outerR - innerR) * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;
                if (d >= innerR && d <= outerR)
                {
                    float ringDist = Mathf.Abs(d - midR) / halfW;
                    a = Mathf.Clamp01(1f - ringDist * ringDist);

                    // Subtle 8-segment dashing for an "occult circle" feel.
                    float angle = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;
                    float seg = Mathf.Repeat(angle, 45f);
                    if (seg > 36f)
                        a *= Mathf.Clamp01(1f - (seg - 36f) / 4f);
                }
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _cachedRing = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedRing;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.7f, 0.35f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}


//  ShadowAlly
public class ShadowAlly : MonoBehaviour
{
    // Config (set by Spawn).
    private float lifetime = 12f;
    private float damage = 12f;
    private float attackRange = 1.2f;
    private float attackInterval = 0.8f;
    private float moveSpeed = 3.5f;

    // State.
    private float lifeTimer;
    private float attackCooldown;
    private Transform currentTarget;
    private bool isDying = false;

    // Visuals.
    private SpriteRenderer bodyRenderer;
    private SpriteRenderer glowRenderer;
    private float bobTimer;
    private float spawnScale = 0f;

    private static readonly Color SHADOW_BODY = new Color(0.35f, 0.12f, 0.55f, 0.92f);
    private static readonly Color SHADOW_GLOW = new Color(0.70f, 0.40f, 1.00f, 0.55f);

    private const float SORT_PRECISION = 10f;
    private const int SORT_ORDER_BASE = 1000;
    private const float ENEMY_SEARCH_RANGE = 14f; // how far a shadow looks for foes

    // FACTORY 

    public static GameObject Spawn(Vector3 worldPos, float lifetime, float damage,
                                   float attackRange, float attackInterval, float moveSpeed)
    {
        var go = new GameObject("ShadowAlly");
        go.SetActive(false);
        go.transform.position = worldPos;

        var shadow = go.AddComponent<ShadowAlly>();
        shadow.lifetime = lifetime;
        shadow.damage = damage;
        shadow.attackRange = attackRange;
        shadow.attackInterval = attackInterval;
        shadow.moveSpeed = moveSpeed;

        go.SetActive(true);
        return go;
    }

    private void Awake()
    {
        // Trigger collider so the shadow has a physical presence.
        var col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.35f;
    }

    private void Start()
    {
        lifeTimer = lifetime;
        attackCooldown = 0f;
        BuildVisual();
    }

    private void Update()
    {
        if (isDying) return;

        // Pop-in.
        if (spawnScale < 1f)
        {
            spawnScale = Mathf.Min(spawnScale + Time.deltaTime / 0.25f, 1f);
            float ease = 1f + 2.7f * Mathf.Pow(spawnScale - 1f, 3f)
                            + 1.7f * Mathf.Pow(spawnScale - 1f, 2f);
            transform.localScale = Vector3.one * ease;
        }

        // Lifetime.
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Die();
            return;
        }

        if (attackCooldown > 0f)
            attackCooldown -= Time.deltaTime;

        AcquireTarget();
        MoveAndAttack();
        UpdateVisual();
    }

    //  TARGETING 
    private void AcquireTarget()
    {
        // Keep the current target if still valid and in range.
        if (currentTarget != null
            && currentTarget.gameObject != null
            && currentTarget.gameObject.activeInHierarchy
            && !IsEnemyDeadOrDying(currentTarget)
            && Vector2.Distance(transform.position, currentTarget.position) <= ENEMY_SEARCH_RANGE)
            return;

        currentTarget = null;
        float closest = ENEMY_SEARCH_RANGE;

        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        foreach (var go in enemies)
        {
            if (go == null || !go.activeInHierarchy) continue;
            if (go.GetComponent<ShadowAlly>() != null) continue; // never target other shadows
            if (IsEnemyDeadOrDying(go.transform)) continue;       // ignore dying corpses

            float d = Vector2.Distance(transform.position, go.transform.position);
            if (d < closest)
            {
                closest = d;
                currentTarget = go.transform;
            }
        }
    }

    // An enemy mid-death-animation reports IsDead(). Don't waste attacks on a
    // corpse that's already on its way out.
    private bool IsEnemyDeadOrDying(Transform t)
    {
        if (t == null) return true;
        var es = t.GetComponent<EnemyStats>();
        return es != null && es.IsDead();
    }

    private void MoveAndAttack()
    {
        if (currentTarget == null) return;

        float dist = Vector2.Distance(transform.position, currentTarget.position);

        if (dist > attackRange)
        {
            // Move toward the target.
            Vector3 dir = (currentTarget.position - transform.position).normalized;
            transform.position += dir * moveSpeed * Time.deltaTime;

            if (bodyRenderer != null)
                bodyRenderer.flipX = dir.x < 0f;
        }
        else
        {
            // In range → attack on the interval.
            if (attackCooldown <= 0f)
            {
                AttackTarget(currentTarget);
                attackCooldown = attackInterval;
            }
        }
    }

    private void AttackTarget(Transform target)
    {
        if (target == null) return;

        // Damage via the same CharacterStats.TakeDamage path the player's
        // Weapon uses (Weapon.OnTriggerStay2D) and the enemy uses
        // (EnemyController.ApplyDamageToTarget).
        var stats = target.GetComponent<CharacterStats>();
        if (stats != null)
            stats.TakeDamage(damage);

        // Combat feel — reuse the player's juice hook so shadows feel punchy.
        CombatJuice.OnPlayerHitEnemy(target.gameObject, isMelee: true);

        // Quick lunge toward the target for visual punch.
        StartCoroutine(LungeTo(target.position));
    }

    private IEnumerator LungeTo(Vector3 targetPos)
    {
        Vector3 start = transform.position;
        Vector3 lunge = Vector3.Lerp(start, targetPos, 0.35f);
        float t = 0f;
        while (t < 1f && !isDying)
        {
            t += Time.deltaTime / 0.12f;
            float p = t < 0.5f ? (t * 2f) : (1f - (t - 0.5f) * 2f); // out then back
            transform.position = Vector3.Lerp(start, lunge, p);
            yield return null;
        }
        if (!isDying) transform.position = start;
    }

    //  DEATH
    private void Die()
    {
        if (isDying) return;
        isDying = true;
        StartCoroutine(DissolveAndDestroy());
    }

    private IEnumerator DissolveAndDestroy()
    {
        float dur = 0.4f, e = 0f;
        Vector3 startScale = transform.localScale;
        while (e < dur)
        {
            e += Time.deltaTime;
            float t = e / dur;
            transform.localScale = startScale * (1f - t);

            if (bodyRenderer != null)
            { var c = bodyRenderer.color; c.a = (1f - t) * SHADOW_BODY.a; bodyRenderer.color = c; }
            if (glowRenderer != null)
            { var c = glowRenderer.color; c.a = (1f - t) * SHADOW_GLOW.a; glowRenderer.color = c; }

            yield return null;
        }
        Destroy(gameObject);
    }

    //  VISUALS
    private void BuildVisual()
    {
        // Soft purple glow behind the body.
        var glowObj = new GameObject("ShadowGlow");
        glowObj.transform.SetParent(transform, false);
        glowRenderer = glowObj.AddComponent<SpriteRenderer>();
        glowRenderer.sprite = GenerateBlobSprite();
        glowRenderer.color = SHADOW_GLOW;
        glowRenderer.sortingOrder = SORT_ORDER_BASE;
        glowObj.transform.localScale = Vector3.one * 1.5f;

        // The shadow body — a small dark wisp.
        var bodyObj = new GameObject("ShadowBody");
        bodyObj.transform.SetParent(transform, false);
        bodyRenderer = bodyObj.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = GenerateBlobSprite();
        bodyRenderer.color = SHADOW_BODY;
        bodyRenderer.sortingOrder = SORT_ORDER_BASE + 1;
        bodyObj.transform.localScale = Vector3.one * 0.9f;
    }

    private void UpdateVisual()
    {
        // Gentle bob.
        bobTimer += Time.deltaTime * 3f;
        float bob = Mathf.Sin(bobTimer) * 0.06f;
        if (bodyRenderer != null)
            bodyRenderer.transform.localPosition = new Vector3(0f, bob, 0f);

        // Glow flicker.
        if (glowRenderer != null)
        {
            float flick = 0.4f + 0.15f * Mathf.Sin(Time.time * 6f);
            var c = SHADOW_GLOW; c.a = flick; glowRenderer.color = c;
        }

        // Y-sort against the world (matches the base/precision YSortEntity
        // uses for enemies: sortOrderBase 1000, sortPrecision 10).
        if (bodyRenderer != null)
        {
            int order = SORT_ORDER_BASE + Mathf.RoundToInt(-transform.position.y * SORT_PRECISION);
            bodyRenderer.sortingOrder = order + 1;
            if (glowRenderer != null) glowRenderer.sortingOrder = order;
        }
    }

    private static Sprite _cachedBlob;
    private static Sprite GenerateBlobSprite()
    {
        if (_cachedBlob != null) return _cachedBlob;
        const int S = 48;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.46f);
        float rx = S * 0.32f, ry = S * 0.40f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float ex = (x - c.x) / rx;
                float ey = (y - c.y) / ry;
                float e = ex * ex + ey * ey;
                float a = e <= 1f ? (1f - e * e) : 0f;

                // Wispy tail at the bottom.
                if (y < c.y && Mathf.Abs(x - c.x) < rx * 0.4f)
                    a = Mathf.Max(a, Mathf.Clamp01(1f - (c.y - y) / (ry * 1.1f)) * 0.6f);

                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px); tex.Apply();
        _cachedBlob = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedBlob;
    }
}


//  SoulBurstVFX  —  a brief purple particle pop where an enemy is converted
public class SoulBurstVFX : MonoBehaviour
{
    private SpriteRenderer[] motes;
    private Vector3[] velocities;
    private float timer;
    private const float DURATION = 0.45f;
    private const int MOTE_COUNT = 6;

    public void Play()
    {
        motes = new SpriteRenderer[MOTE_COUNT];
        velocities = new Vector3[MOTE_COUNT];

        Sprite blob = GenerateMoteSprite();

        for (int i = 0; i < MOTE_COUNT; i++)
        {
            var moteObj = new GameObject($"Mote{i}");
            moteObj.transform.SetParent(transform, false);
            var sr = moteObj.AddComponent<SpriteRenderer>();
            sr.sprite = blob;
            sr.color = new Color(0.72f, 0.42f, 1f, 1f);
            sr.sortingOrder = 1500;
            moteObj.transform.localScale = Vector3.one * 0.25f;
            motes[i] = sr;

            float ang = (360f / MOTE_COUNT) * i * Mathf.Deg2Rad;
            velocities[i] = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f)
                            * Random.Range(1.2f, 2.2f);
        }
    }

    private void Update()
    {
        if (motes == null) { Destroy(gameObject); return; }

        timer += Time.deltaTime;
        float t = timer / DURATION;
        if (t >= 1f) { Destroy(gameObject); return; }

        for (int i = 0; i < motes.Length; i++)
        {
            if (motes[i] == null) continue;
            motes[i].transform.position += velocities[i] * Time.deltaTime;
            motes[i].transform.position += Vector3.up * 0.6f * Time.deltaTime; // rising soul

            var c = motes[i].color;
            c.a = 1f - t * t;
            motes[i].color = c;
            motes[i].transform.localScale = Vector3.one * 0.25f * (1f - t * 0.5f);
        }
    }

    private static Sprite _cachedMote;
    private static Sprite GenerateMoteSprite()
    {
        if (_cachedMote != null) return _cachedMote;
        const int S = 16;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float r = S * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 1f - Mathf.Clamp01(d / r);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels(px); tex.Apply();
        _cachedMote = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedMote;
    }
}
