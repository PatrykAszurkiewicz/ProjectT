using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// An arcing mortar shell fired by the Mort enemy. Unlike EnemyProjectile a mortar locks onto a fixed ground position
[RequireComponent(typeof(SpriteRenderer))]
public class MortarProjectile : MonoBehaviour
{
    [Header("Explosion")]
    [Tooltip("World-unit radius of the blast at the landing point. Everything " +
             "(player / towers / core) inside this radius takes the hit. Other " +
             "enemies are never damaged. The ground telegraph matches this radius.")]
    [SerializeField] private float explosionRadius = 1.5f;

    [Tooltip("Layers the explosion overlap check considers. Leave as Everything " +
             "unless you want to restrict it; victims are filtered by component " +
             "(PlayerStats / IEnergyConsumer) regardless, so Everything is safe.")]
    [SerializeField] private LayerMask explosionMask = ~0;

    [Tooltip("Fire tint of the built-in explosion VFX.")]
    [SerializeField] private Color explosionColor = new Color(1f, 0.55f, 0.10f, 1f);

    [Tooltip("If true, spawn the self-contained built-in explosion VFX on impact " +
             "(no art needed). If an explosionVfxPrefab is assigned below it is " +
             "used INSTEAD of the built-in one.")]
    [SerializeField] private bool useBuiltInExplosion = true;

    [Tooltip("Optional custom VFX prefab spawned at the landing point on impact. " +
             "Overrides the built-in explosion when assigned. Leave empty to use " +
             "the built-in one.")]
    [SerializeField] private GameObject explosionVfxPrefab;

    [Tooltip("Seconds the spawned custom explosion VFX lives before being " +
             "destroyed. Ignored for the built-in VFX (it cleans itself up).")]
    [SerializeField] private float explosionVfxLifetime = 2f;

    [Header("Arc / Flight")]
    [Tooltip("Peak visual height of the arc, in world units. Purely cosmetic in " +
             "this top-down game: it lifts the sprite up and back down so the " +
             "shot reads as 'lobbed'. The blast still lands on the ground at the " +
             "captured target position.")]
    [SerializeField] private float arcHeight = 2.5f;

    [Tooltip("If true, the sprite rotates to face its current direction of travel " +
             "as it arcs. Turn off for shells that shouldn't rotate.")]
    [SerializeField] private bool faceTravelDirection = true;

    [Tooltip("If true, the sprite spins continuously while in flight (tumbling " +
             "shell look). Overrides faceTravelDirection when on.")]
    [SerializeField] private bool spinInFlight = false;
    [SerializeField] private float spinDegreesPerSecond = 360f;

    [Tooltip("Force a high sorting order so the shell renders above the grass " +
             "Y-sort range. 0 = leave the prefab's value alone.")]
    [SerializeField] private int forcedSortingOrder = 2000;

    [Header("Ground Telegraph")]
    [Tooltip("If true, a minimalist semi-transparent circle is drawn on the " +
             "ground at the landing point for the whole flight so the player can " +
             "see where the shell will land and dodge it. Self-contained — needs " +
             "no art.")]
    [SerializeField] private bool showGroundTelegraph = true;

    [Tooltip("Tint of the ground telegraph circle.")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.30f, 0.15f, 1f);

    [Tooltip("Optional custom telegraph prefab spawned at the landing point " +
             "instead of the built-in circle. Destroyed on impact. Leave empty " +
             "to use the built-in circle.")]
    [SerializeField] private GameObject groundTelegraphPrefab;

    [Header("Projectile Parry (Augment 325)")]
    [Tooltip("Fraction of the flight (0..1) after which the shell becomes " +
             "parry-able and shows the '!' prompt. e.g. 0.55 = the last 45% of " +
             "the descent is the reaction window. You face the FIRING enemy to " +
             "parry a lobbed shell, and a parry lobs it back onto that enemy.")]
    [Range(0f, 1f)]
    [SerializeField] private float parryWindowStart = 0.55f;

    [Tooltip("Damage multiplier applied to the shell's damage when its blast is " +
             "parried back onto the enemies. 1 = same as it would have hit you " +
             "for; >1 rewards the parry.")]
    [SerializeField] private float parryReflectMultiplier = 2f;

    [Tooltip("Seconds the bounced shell spends arcing back to the firing enemy " +
             "after a successful parry.")]
    [SerializeField] private float parryReturnTime = 0.5f;

    private EnemyController firer;   // may become null if the firer dies mid-flight
    private float damage;            // fallback damage if the firer is gone
    private Vector3 spawnPos;
    private Vector3 landingPos;      // FIXED at launch — the dodge-able landing spot
    private float travelTime;
    private float maxLifetime;
    private float age;
    private bool initialized;
    private bool detonated;

    private Vector3 lastPos;
    private GameObject telegraphObj;

    // Parry state
    private bool parried;                 // true once the blast has been bounced back
    private bool blocked;                 // true once the shield has reduced this shell
    private float playerBlockScale = 1f;  // <1 → reduced blast damage to the player
    private Weapon cachedPlayerWeapon;    // cached player Weapon for shield lookups
    private ProjectileParryIndicator parryPrompt;

    // Called by MortController at the moment the attack animation lands.
    // landingPosition is captured ONCE here - the shell never re-aims at the target
    public void Initialize(EnemyController firer, Vector3 landingPosition,
                           float damage, float travelTime, float maxLifetime = 6f)
    {
        this.firer = firer;
        this.damage = damage;
        this.landingPos = landingPosition;
        this.spawnPos = transform.position;
        this.travelTime = Mathf.Max(0.1f, travelTime);
        this.maxLifetime = Mathf.Max(this.travelTime + 0.5f, maxLifetime);
        this.lastPos = transform.position;
        initialized = true;

        if (forcedSortingOrder != 0)
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = forcedSortingOrder;
        }

        SpawnTelegraph();
    }

    private void Update()
    {
        if (!initialized || detonated) return;

        age += Time.deltaTime;

        // Safety cap so a shell can never leak (e.g. timescale weirdness).
        if (age >= maxLifetime)
        {
            Explode();
            return;
        }

        float t = Mathf.Clamp01(age / travelTime);

        // Shield interaction during the back end of the descent ──
        if (!parried && !blocked && t >= parryWindowStart)
        {
            TryProjectileParry();
            if (detonated) return; // resolved this frame — nothing left to move
            // A successful parry restarts the arc (age / travelTime / spawn /
            // landing all change), so recompute progress before moving.
            t = Mathf.Clamp01(age / travelTime);
        }

        // Linear travel across the ground from launch to the captured landing
        // spot, plus a parabolic visual lift that peaks at the midpoint.
        Vector3 groundPos = Vector3.Lerp(spawnPos, landingPos, t);
        float height = arcHeight * 4f * t * (1f - t); // 0 at t=0 and t=1, peak at t=0.5
        Vector3 newPos = groundPos + Vector3.up * height;

        Vector3 travelDelta = newPos - lastPos;
        transform.position = newPos;
        lastPos = newPos;

        // Orientation
        if (spinInFlight)
        {
            transform.Rotate(Vector3.forward, spinDegreesPerSecond * Time.deltaTime);
        }
        else if (faceTravelDirection && travelDelta.sqrMagnitude > 1e-8f)
        {
            float angle = Mathf.Atan2(travelDelta.y, travelDelta.x) * Mathf.Rad2Deg;
            if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        // Landed.
        if (t >= 1f)
            Explode();
    }

    private void Explode()
    {
        if (detonated) return;
        detonated = true;

        SpawnExplosionVfx();
        if (parried) ApplyAreaDamageToEnemies();
        else ApplyAreaDamage();
        DestroyTelegraph();
        Destroy(gameObject);
    }

    //  Shield interaction path 
    private void TryProjectileParry()
    {
        if (!ProjectileParry.TryResolve(ref cachedPlayerWeapon, out var shield, out var playerT)
            || playerT == null)
        {
            HideParryPrompt();
            return;
        }

        // Face the firing enemy to engage its shell. If the firer is gone there's
        // nothing to bounce toward, but a held shield can still soften the blast.
        Vector3 aimRef = firer != null ? firer.transform.position : transform.position;

        // The "!" prompt only advertises a parry, so only show it once the
        // bounce-back augment is unlocked.
        if (ProjectileParry.Unlocked) ShowParryPrompt();
        else HideParryPrompt();

        var result = shield.TryInterceptProjectile(aimRef);

        bool canBounce = result == ShieldSystem.ProjectileInterception.Parried
                         && ProjectileParry.Unlocked
                         && firer != null;

        if (canBounce)
        {
            shield.PlayProjectileParryFeedback(aimRef);
            // Stun + debuff the firer like a melee parry (ShieldSystem.ApplyParry).
            // This activates the parry-damage bonus read back in
            // ApplyAreaDamageToEnemies(), so Powerful Parry (331), Longer Parry
            // Stun (330) and the base debuff apply to mortar parries too. canBounce
            // already guarantees firer != null, but guard defensively.
            if (firer != null)
                ParryStunEffect.ApplyOrRefresh(firer.gameObject);
            BecomeParried();
        }
        else if (result != ShieldSystem.ProjectileInterception.None)
        {
            // Blocked, or a parry attempt without the augment → reduce the blast
            // damage the player will take. The shell still lands and explodes.
            // Block feedback only (no gold parry phantom).
            shield.PlayProjectileBlockFeedback(aimRef);
            blocked = true;
            playerBlockScale = shield.BlockDamageMultiplier;
            HideParryPrompt();
        }
    }

    // Re-lob the shell back onto the firing enemy; its blast will now hit enemies.
    private void BecomeParried()
    {
        if (parried || detonated) return;
        if (firer == null) { Destroy(gameObject); return; }

        parried = true;
        HideParryPrompt();

        // Start a fresh short arc from where the shell is now to the firer.
        spawnPos = transform.position;
        landingPos = firer.transform.position;
        age = 0f;
        travelTime = Mathf.Max(0.2f, parryReturnTime);
        maxLifetime = travelTime + 0.5f;
        lastPos = transform.position;

        // Re-point the ground telegraph at the new (enemy) landing spot.
        DestroyTelegraph();
        SpawnTelegraph();
    }

    private void ShowParryPrompt()
    {
        if (parryPrompt == null)
            parryPrompt = ProjectileParryIndicator.Attach(transform, yOffset: 0.6f, size: 0.45f);
    }

    private void HideParryPrompt()
    {
        if (parryPrompt != null)
        {
            Destroy(parryPrompt.gameObject);
            parryPrompt = null;
        }
    }

    // Damages every valid victim (player / towers / core) within explosionRadius
    // of the landing point. 
    private void ApplyAreaDamage()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(landingPos, explosionRadius, explosionMask);
        if (hits == null || hits.Length == 0) return;

        // A single object can have several colliders; only damage each once.
        var damaged = new HashSet<GameObject>();

        foreach (var col in hits)
        {
            if (col == null) continue;

            // Player: walk up to the PlayerStats owner (handles colliders that
            // sit on child objects).
            var playerStats = col.GetComponentInParent<PlayerStats>();
            if (playerStats != null)
            {
                if (damaged.Add(playerStats.gameObject))
                    DamageVictim(playerStats.transform, playerStats.GetComponent<CharacterStats>(), null);
                continue;
            }

            // Tower / core: anything exposing IEnergyConsumer.
            var consumer = col.GetComponentInParent<IEnergyConsumer>();
            if (consumer != null)
            {
                var consumerComp = consumer as Component;
                GameObject key = consumerComp != null ? consumerComp.gameObject : col.gameObject;
                if (damaged.Add(key))
                    DamageVictim(consumerComp != null ? consumerComp.transform : col.transform,
                                 null, consumer);
                continue;
            }

            // Anything else (other enemies, scenery, the firer itself) is ignored.
        }
    }

    private void DamageVictim(Transform victim, CharacterStats charStats, IEnergyConsumer consumer)
    {
        if (victim == null) return;

        // Player blocked the shell → apply reduced damage directly (the firer
        // route can't scale). Towers / core are unaffected by the player's shield.
        if (charStats != null && playerBlockScale < 1f)
        {
            charStats.TakeDamage(damage * playerBlockScale);
            return;
        }

        // Preferred path: route through the firing controller so the explosion
        // behaves exactly like that enemy's normal hit (reflection, freeze-on-hit,
        // retarget-on-kill all handled there). The shield interaction was already
        // resolved in flight, so tell the controller not to re-check it.
        if (firer != null)
        {
            firer.ApplyDamageToTarget(victim, viaProjectile: true);
            return;
        }

        // Fallback (firer already destroyed): apply damage directly, mirroring
        // EnemyProjectile's two damage sinks.
        if (charStats != null)
        {
            charStats.TakeDamage(damage);
            return;
        }

        if (consumer != null && EnergyManager.Instance != null)
            EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
    }

    // Parried blast: damage ENEMIES (incl. the original firer) inside the radius
    // instead of the player / towers. Mirrors the melee damage path on enemies.
    private void ApplyAreaDamageToEnemies()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(landingPos, explosionRadius, explosionMask);
        if (hits == null || hits.Length == 0) return;

        var damaged = new HashSet<GameObject>();
        float baseDmg = damage * parryReflectMultiplier;

        foreach (var col in hits)
        {
            if (col == null) continue;

            var enemyStats = col.GetComponentInParent<EnemyStats>();
            if (enemyStats == null) continue;
            if (!damaged.Add(enemyStats.gameObject)) continue;

            var cs = enemyStats.GetComponent<CharacterStats>();
            if (cs == null) continue;

            float dmg = baseDmg;
            var stun = enemyStats.GetComponent<ParryStunEffect>();
            if (stun != null) dmg *= stun.DamageMultiplier;

            cs.TakeDamage(dmg);
            CombatJuice.OnPlayerHitEnemy(enemyStats.gameObject, isMelee: false);
        }
    }

    private void SpawnExplosionVfx()
    {
        // Custom prefab takes priority when assigned.
        if (explosionVfxPrefab != null)
        {
            GameObject vfx = Instantiate(explosionVfxPrefab, landingPos, Quaternion.identity);
            if (explosionVfxLifetime > 0f)
                Destroy(vfx, explosionVfxLifetime);
            return;
        }

        if (!useBuiltInExplosion) return;

        var root = new GameObject("MortarExplosionVFX");
        root.transform.position = landingPos;
        root.AddComponent<MortarExplosionVFX>().Play(explosionRadius, explosionColor);
    }

    // Spawns the minimalist ground telegraph at the landing point.
    private void SpawnTelegraph()
    {
        if (!showGroundTelegraph) return;

        if (groundTelegraphPrefab != null)
        {
            telegraphObj = Instantiate(groundTelegraphPrefab, landingPos, Quaternion.identity);
            return;
        }

        telegraphObj = new GameObject("MortarTelegraph");
        telegraphObj.transform.position = landingPos;
        telegraphObj.AddComponent<MortarTelegraph>()
                    .Initialize(explosionRadius, travelTime, telegraphColor);
    }

    private void DestroyTelegraph()
    {
        if (telegraphObj != null) Destroy(telegraphObj);
    }

    private void OnDestroy()
    {
        // Safety: never strand the telegraph if the shell is destroyed by some
        // other system before it lands.
        DestroyTelegraph();
        HideParryPrompt();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 c = Application.isPlaying ? landingPos : transform.position;
        Gizmos.DrawWireSphere(c, explosionRadius);
    }
#endif
}


// Ground telegraph: a soft semi-transparent circle
public class MortarTelegraph : MonoBehaviour
{
    private const string SortLayer = "Default";
    private const int FillOrder = 3000;
    private const int RingOrder = 3001;

    private float _radius;
    private float _duration;
    private Color _color;

    private SpriteRenderer _fill;
    private SpriteRenderer _ring;

    public void Initialize(float radius, float duration, Color color)
    {
        _radius = Mathf.Max(0.3f, radius);
        _duration = Mathf.Max(0.1f, duration);
        _color = color;
        BuildVisuals();
        StartCoroutine(Run());
    }

    private void BuildVisuals()
    {
        // Soft transparent fill — faintly shades the patch of ground that will explode.
        var fillGo = new GameObject("TelegraphFill");
        fillGo.transform.SetParent(transform, false);
        fillGo.transform.localScale = Vector3.one * (_radius * 2f);
        _fill = fillGo.AddComponent<SpriteRenderer>();
        _fill.sprite = Boss2WarningSprites.GetFilledDisc();
        _fill.sortingLayerName = SortLayer;
        _fill.sortingOrder = FillOrder;

        // Single clean perimeter ring.
        var ringGo = new GameObject("TelegraphRing");
        ringGo.transform.SetParent(transform, false);
        ringGo.transform.localScale = Vector3.one * (_radius * 2f);
        _ring = ringGo.AddComponent<SpriteRenderer>();
        _ring.sprite = Boss2WarningSprites.GetRing(thicknessFraction: 0.06f);
        _ring.sortingLayerName = SortLayer;
        _ring.sortingOrder = RingOrder;
    }

    private IEnumerator Run()
    {
        float elapsed = 0f;
        while (elapsed < _duration)
        {
            float progress = Mathf.Clamp01(elapsed / _duration);

            // One gentle pulse, mildly accelerating toward impact.
            float pulseHz = Mathf.Lerp(1.2f, 3.5f, progress);
            float pulse = 0.5f + 0.5f * Mathf.Sin(elapsed * pulseHz * Mathf.PI * 2f);

            // Ring: subtle scale breathing + alpha that firms up as the timer runs out.
            float ringScale = Mathf.Lerp(0.98f, 1.05f, pulse);
            _ring.transform.localScale = Vector3.one * (_radius * 2f * ringScale);
            Color ringCol = _color;
            ringCol.a = Mathf.Lerp(0.45f, 0.80f, progress) * (0.7f + 0.3f * pulse);
            _ring.color = ringCol;

            // Fill: stays faint, intensifies just a little near impact.
            Color fillCol = _color;
            fillCol.a = Mathf.Lerp(0.12f, 0.24f, progress);
            _fill.color = fillCol;

            elapsed += Time.deltaTime;
            yield return null;
        }
        // Left fully visible at end-of-window; the projectile destroys us on impact.
    }
}


// Explosion VFX:
public class MortarExplosionVFX : MonoBehaviour
{
    private const string SortLayer = "Default";
    private const int ScorchOrder = -100;
    private const int ShockwaveOrder = 5200;
    private const int DebrisOrder = 5400;
    private const int FlashOrder = 5600;

    private static readonly Color Charcoal = new Color(0.16f, 0.10f, 0.07f, 1f);

    private float _radius;
    private Color _fire;

    public void Play(float radius, Color fireColor)
    {
        _radius = Mathf.Max(0.3f, radius);
        _fire = fireColor;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        BuildScorch();
        BuildShockwave();
        BuildCoreFlash();
        BuildDebris(Random.Range(7, 11));

        // Longest-lived element (scorch fade) drives the self-destruct.
        yield return new WaitForSeconds(1.9f);
        Destroy(gameObject);
    }

    // Dark patch stamped on the ground at impact, lingers then fades.
    private void BuildScorch()
    {
        var go = new GameObject("Scorch");
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * (_radius * 1.7f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetSoftDisc();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = ScorchOrder;
        Color c = Charcoal; c.a = 0.6f;
        sr.color = c;
        StartCoroutine(FadeSprite(sr, life: 1.4f, delay: 0.35f));
    }

    // Soft ground-hugging cloud that expands outward and fades.
    private void BuildShockwave()
    {
        var go = new GameObject("Shockwave");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetSoftDisc();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = ShockwaveOrder;
        Color c = _fire; c.a = 0.5f;
        sr.color = c;
        StartCoroutine(ShockwaveRoutine(go.transform, sr));
    }

    private IEnumerator ShockwaveRoutine(Transform t, SpriteRenderer sr)
    {
        float life = 0.5f;
        Color start = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float eased = 1f - (1f - p) * (1f - p);
            t.localScale = Vector3.one * Mathf.Lerp(_radius * 0.4f, _radius * 2.0f, eased);
            Color c = start; c.a = start.a * (1f - p); sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    // Sharp bright pop at the point of impact.
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
        Color hot = Color.Lerp(_fire, Color.white, 0.7f);
        float life = 0.25f;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            t.localScale = Vector3.one * Mathf.Lerp(_radius * 1.2f, _radius * 0.5f, p);
            Color c = hot; c.a = Mathf.Lerp(0.95f, 0f, p); sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    // A modest burst of small rock chunks arcing out and fading.
    private void BuildDebris(int count)
    {
        for (int i = 0; i < count; i++)
            StartCoroutine(DebrisChunk());
    }

    private IEnumerator DebrisChunk()
    {
        var go = new GameObject("Debris");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Boss2VFXSprites.GetRockChunk();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DebrisOrder;
        Color earth = Color.Lerp(Charcoal, _fire, Random.Range(0f, 0.35f));
        sr.color = earth;

        float size = _radius * Random.Range(0.03f, 0.07f);
        Vector3 rest = new Vector3(size * Random.Range(0.8f, 1.2f),
                                   size * Random.Range(0.8f, 1.2f), 1f);
        go.transform.localScale = rest;

        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector2 outward = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        float dist = _radius * Random.Range(0.5f, 1.2f);
        float hgt = _radius * Random.Range(0.5f, 1.0f);
        float flight = Random.Range(0.4f, 0.65f);
        float spin = Random.Range(-540f, 540f);
        float spinAccum = Random.Range(0f, 360f);

        float e = 0f;
        while (e < flight)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / flight);
            float horiz = 1f - (1f - t) * (1f - t);
            Vector3 ground = (Vector3)(outward * dist * horiz);
            float h = hgt * 4f * t * (1f - t);
            go.transform.localPosition = ground + Vector3.up * h;
            spinAccum += spin * Time.deltaTime;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spinAccum);
            yield return null;
        }

        // Brief rest, then fade.
        yield return new WaitForSeconds(Random.Range(0.15f, 0.35f));
        float fade = 0.35f;
        Color baseCol = sr.color;
        e = 0f;
        while (e < fade)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / fade);
            Color c = baseCol; c.a = baseCol.a * (1f - t); sr.color = c;
            yield return null;
        }
        Destroy(go);
    }

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

