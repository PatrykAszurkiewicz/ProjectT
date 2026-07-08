using System.Collections.Generic;
using UnityEngine;

// Player-fired mortar shell. Mirrors the Mort enemy's MortarProjectile.
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerMortarProjectile : MonoBehaviour
{
    private float damage;
    private float explosionRadius;
    private bool knockback;
    private float knockbackForce;

    private Vector3 spawnPos;
    private Vector3 landingPos;
    private float travelTime;
    private float arcHeight;
    private float maxLifetime;
    private float age;

    private bool initialized;
    private bool detonated;
    private Vector3 lastPos;

    private Color explosionColor = new Color(1f, 0.55f, 0.10f, 1f);
    private Color telegraphColor = new Color(1f, 0.30f, 0.15f, 1f);
    private bool showTelegraph = true;
    private GameObject telegraphObj;


    private const float TelegraphScale = 0.7f;

    public void Initialize(Vector3 landingPosition, float damage, float explosionRadius,
                           float travelTime, float arcHeight, bool knockback, float knockbackForce,
                           Color explosionColor, Color telegraphColor, bool showTelegraph,
                           float maxLifetime = 6f)
    {
        this.landingPos = landingPosition;
        this.damage = damage;
        this.explosionRadius = Mathf.Max(0.3f, explosionRadius);
        this.travelTime = Mathf.Max(0.1f, travelTime);
        this.arcHeight = Mathf.Max(0f, arcHeight);
        this.knockback = knockback;
        this.knockbackForce = knockbackForce;
        this.explosionColor = explosionColor;
        this.telegraphColor = telegraphColor;
        this.showTelegraph = showTelegraph;

        this.spawnPos = transform.position;
        this.maxLifetime = Mathf.Max(this.travelTime + 0.5f, maxLifetime);
        this.lastPos = transform.position;
        initialized = true;

        // Subtle warm tracer light on the shell while it arcs.
        ProjectileGlow.Attach(transform, new Color(1f, 0.6f, 0.2f), worldRadius: 0.8f,
                              alpha: 0.5f, pulse: true, pulseSpeed: 6f, pulseAmount: 0.22f);

        SpawnTelegraph();

        // Launch SFX (player mortar). The enemy Mort plays its own MortarShot from
        // its projectile's Initialize.
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.mortarShot.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.mortarShot, spawnPos);
        }
    }

    private void Update()
    {
        if (!initialized || detonated) return;

        age += Time.deltaTime;

        // Safety cap so a shell can never leak.
        if (age >= maxLifetime)
        {
            Explode();
            return;
        }

        float t = Mathf.Clamp01(age / travelTime);

        // Linear ground travel + parabolic visual lift (peaks at the midpoint).
        Vector3 groundPos = Vector3.Lerp(spawnPos, landingPos, t);
        float height = arcHeight * 4f * t * (1f - t);
        Vector3 newPos = groundPos + Vector3.up * height;

        Vector3 travelDelta = newPos - lastPos;
        transform.position = newPos;
        lastPos = newPos;

        // Face direction of travel.
        if (travelDelta.sqrMagnitude > 1e-8f)
        {
            float angle = Mathf.Atan2(travelDelta.y, travelDelta.x) * Mathf.Rad2Deg;
            if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        if (t >= 1f)
            Explode();
    }

    private void Explode()
    {
        if (detonated) return;
        detonated = true;

        // Detonation SFX (player mortar). The enemy Mort plays its own from its
        // projectile's explode path.
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.mortarExplosion.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.mortarExplosion, landingPos);
        }

        SpawnExplosionVfx();
        ApplyAreaDamageToEnemies();
        DestroyTelegraph();
        Destroy(gameObject);
    }

    // Damages every enemy within explosionRadius of the landing point
    private void ApplyAreaDamageToEnemies()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(landingPos, explosionRadius);
        if (hits == null || hits.Length == 0) return;

        var damaged = new HashSet<GameObject>();

        foreach (var col in hits)
        {
            if (col == null) continue;
            if (!col.CompareTag("Enemy")) continue;

            // CharacterStats path (EnemyStats, Boss1, all bosses).
            CharacterStats stats = col.GetComponent<CharacterStats>();
            if (stats == null) stats = col.GetComponentInParent<CharacterStats>();
            if (stats != null)
            {
                // Never damage the player even if somehow tagged/overlapping.
                if (stats.GetComponent<PlayerStats>() != null) continue;
                if (!damaged.Add(stats.gameObject)) continue;

                stats.TakeDamage(damage);
                CombatJuice.OnPlayerHitEnemy(stats.gameObject, isMelee: false);

                if (knockback)
                {
                    var enemyController = stats.GetComponent<EnemyController>();
                    if (enemyController != null)
                    {
                        Vector2 dir = (Vector2)(stats.transform.position - landingPos);
                        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Random.insideUnitCircle.normalized;
                        enemyController.ApplyKnockback(dir, knockbackForce);
                    }
                }
                continue;
            }

            // IDamageable fallback (Gremlin and other non-CharacterStats enemies).
            IDamageable damageable = col.GetComponent<IDamageable>();
            if (damageable == null) damageable = col.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                Component comp = damageable as Component;
                GameObject key = comp != null ? comp.gameObject : col.gameObject;
                if (!damaged.Add(key)) continue;

                damageable.TakeDamage(damage, gameObject);
                CombatJuice.OnPlayerHitEnemy(key, isMelee: false);
            }
        }
    }

    private void SpawnExplosionVfx()
    {
        // Reuse the enemy mortar's self-contained explosion VFX (no art needed).
        var root = new GameObject("PlayerMortarExplosionVFX");
        root.transform.position = landingPos;
        root.AddComponent<MortarExplosionVFX>().Play(explosionRadius, explosionColor);
    }

    private void SpawnTelegraph()
    {
        if (!showTelegraph) return;

        // Reuse the enemy mortar's ground telegraph so the landing footprint
        // reads identically.
        telegraphObj = new GameObject("PlayerMortarTelegraph");
        telegraphObj.transform.position = landingPos;
        telegraphObj.AddComponent<MortarTelegraph>()
                    .Initialize(explosionRadius * TelegraphScale, travelTime, telegraphColor);
    }

    private void DestroyTelegraph()
    {
        if (telegraphObj != null) Destroy(telegraphObj);
    }

    private void OnDestroy()
    {
        DestroyTelegraph();
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


// The red aiming circle that follows the cursor while a Mortar weapon is equipped. 
public class MortarAimReticle : MonoBehaviour
{
    private const string SortLayer = "Default";
    private const int FillOrder = 2950;
    private const int RingOrder = 2951;
    private const int DotOrder = 2952;
    private const int GlowRingOrder = 2949;   // just under the crisp ring

    // Ring alpha range — kept low so the border is faint rather than harsh.
    private const float RingAlphaMin = 0.22f;
    private const float RingAlphaMax = 0.42f;
    private const float FillAlpha = 0.12f;

    // Cosmetic scale of the drawn reticle circle relative to the true blast
    // radius (0.7 = 30% smaller). Does NOT affect the AoE or landing telegraph.
    private const float CircleScale = 0.7f;

    // Whitish-red for the dotted trajectory so the arc reads brightly against
    // the ground while still sitting in the "aiming" family (not the deep
    // committed telegraph red). Raise the G/B channels toward 1 for whiter,
    // lower them back toward 0 for a more saturated red. This is the default;
    // call SetDotColor() to retint the arc (e.g. blue for the Smoke Screen).
    private Color _dotColor = new Color(1f, 0.75f, 0.72f, 1f);

    // Dotted ballistic arc.
    private const int DotCount = 16;
    private const float DotSize = 0.13f;
    private const float DotAlphaNear = 0.22f;  // faint at the launch end
    private const float DotAlphaFar = 0.55f;   // firmer near the target

    private SpriteRenderer _fill;
    private SpriteRenderer _ring;
    private SpriteRenderer _glowRing;   // additive halo so the outline reads in dark biomes
    private SpriteRenderer[] _dots;
    private NightLight _nightLight;      // punches through the NightOverlay so the outline is visible at night
    private float _radius = 1.5f;
    private Color _color = new Color(1f, 0.42f, 0.38f, 1f);

    public void Initialize(float radius, Color color)
    {
        _radius = Mathf.Max(0.3f, radius);
        _color = color;
        Build();
        SetRadius(_radius);
    }

    // Retint the dotted ballistic arc. Lets a reused reticle (e.g. the Smoke
    // Screen tool's blue reticle) carry a matching arc colour instead of the
    // default coral-red. Call after Initialize().
    public void SetDotColor(Color c)
    {
        _dotColor = c;
        if (_dots != null)
            for (int i = 0; i < _dots.Length; i++)
                if (_dots[i] != null) { Color cc = _dotColor; cc.a = _dots[i].color.a; _dots[i].color = cc; }
    }

    private void Build()
    {
        var fillGo = new GameObject("ReticleFill");
        fillGo.transform.SetParent(transform, false);
        _fill = fillGo.AddComponent<SpriteRenderer>();
        _fill.sprite = Boss2WarningSprites.GetFilledDisc();
        _fill.sortingLayerName = SortLayer;
        _fill.sortingOrder = FillOrder;

        var ringGo = new GameObject("ReticleRing");
        ringGo.transform.SetParent(transform, false);
        _ring = ringGo.AddComponent<SpriteRenderer>();
        _ring.sprite = Boss2WarningSprites.GetRing(thicknessFraction: 0.07f);
        _ring.sortingLayerName = SortLayer;
        _ring.sortingOrder = RingOrder;

        // Soft additive halo tracing the same ring, so the reticle outline stays
        // visible against a darkened biome. Tinted to the reticle colour, so the
        // Smoke Screen's blue reticle glows blue automatically.
        var glowGo = new GameObject("ReticleGlowRing");
        glowGo.transform.SetParent(transform, false);
        _glowRing = glowGo.AddComponent<SpriteRenderer>();
        _glowRing.sprite = Boss2WarningSprites.GetRing(thicknessFraction: 0.16f);
        _glowRing.sharedMaterial = ProjectileGlow.SharedAdditiveMaterial;
        _glowRing.sortingLayerName = SortLayer;
        _glowRing.sortingOrder = GlowRingOrder;

        // Dotted trajectory preview — a row of small discs laid along the arc.
        _dots = new SpriteRenderer[DotCount];
        for (int i = 0; i < DotCount; i++)
        {
            var go = new GameObject("TrajDot" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * DotSize;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Boss2WarningSprites.GetFilledDisc();
            sr.sortingLayerName = SortLayer;
            sr.sortingOrder = DotOrder;
            Color c = _dotColor; c.a = 0.5f; sr.color = c;
            _dots[i] = sr;
        }

        // Punch a faint hole in the NightOverlay at the cursor so the aiming
        // outline (mortar AND smoke screen) is readable in dark/corruption biomes.
        // The additive glow ring above is drawn UNDER the overlay and would be
        // crushed by it; this NightLight is what actually reveals the circle.
        // Added unconditionally (cheap, single instance) so it auto-registers if
        // night toggles on while the weapon is already equipped — NightLight
        // no-ops while no NightOverlay is active. Honors the global perf switch.
        if (ProjectileGlow.EnableNightLights)
        {
            _nightLight = gameObject.AddComponent<NightLight>();
            _nightLight.radius = _radius * CircleScale * 1.25f;
            _nightLight.intensity = 0.4f;          // subtle — just enough to lift the outline
            _nightLight.lightColor = _color;
            _nightLight.warmTintStrength = 0.25f;
            _nightLight.fadeInDuration = 0f;
        }
    }

    public void SetRadius(float radius)
    {
        _radius = Mathf.Max(0.3f, radius);
        float diameter = _radius * 2f * CircleScale;
        if (_fill != null) _fill.transform.localScale = Vector3.one * diameter;
        if (_ring != null) _ring.transform.localScale = Vector3.one * diameter;
        if (_glowRing != null) _glowRing.transform.localScale = Vector3.one * (diameter * 1.04f);
        if (_nightLight != null) _nightLight.radius = _radius * CircleScale * 1.25f;
    }

    // Lays the dotted arc from `origin` (the launch point, above the player) up
    // and over to the reticle centre. The parabola matches the shell's flight
    // (height = arcHeight * 4t(1-t)), so the preview is exact. Called each frame
    // by Weapon.UpdateMortarSystem while a mortar is equipped.
    public void SetTrajectory(Vector3 origin, float arcHeight)
    {
        if (_dots == null) return;

        Vector3 landing = transform.position; // reticle sits on the target spot
        int n = _dots.Length;

        for (int i = 0; i < n; i++)
        {
            var d = _dots[i];
            if (d == null) continue;

            float t = (i + 1f) / n;              // 1/n .. 1 (last dot at centre)
            Vector3 ground = Vector3.Lerp(origin, landing, t);
            float h = arcHeight * 4f * t * (1f - t);
            Vector3 p = ground + Vector3.up * h;
            p.z = 0f;
            d.transform.position = p;

            Color c = _dotColor;
            c.a = Mathf.Lerp(DotAlphaNear, DotAlphaFar, t);
            d.color = c;
        }
    }

    public void SetVisible(bool visible)
    {
        if (_fill != null) _fill.enabled = visible;
        if (_ring != null) _ring.enabled = visible;
        if (_glowRing != null) _glowRing.enabled = visible;
        if (_nightLight != null) _nightLight.enabled = visible;  // OnEnable/OnDisable (un)registers the light
        if (_dots != null)
            for (int i = 0; i < _dots.Length; i++)
                if (_dots[i] != null) _dots[i].enabled = visible;
    }

    private void Update()
    {
        // Gentle pulse so the reticle reads as live (unscaled so it animates
        // regardless of timeScale — though Weapon hides it while paused). The
        // ring stays faint at both ends of the pulse.
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);

        if (_ring != null)
        {
            Color c = _color;
            c.a = Mathf.Lerp(RingAlphaMin, RingAlphaMax, pulse);
            _ring.color = c;
        }
        if (_fill != null)
        {
            Color c = _color;
            c.a = FillAlpha;
            _fill.color = c;
        }
        if (_glowRing != null)
        {
            // Faint additive halo that breathes with the same pulse.
            Color c = _color;
            c.a = Mathf.Lerp(0.18f, 0.40f, pulse);
            _glowRing.color = c;
        }
    }
}

