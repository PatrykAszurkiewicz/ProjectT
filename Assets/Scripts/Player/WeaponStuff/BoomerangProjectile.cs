using System.Collections.Generic;
using UnityEngine;


// Boomerang projectile — flies toward cursor with a curved arc, damages enemies

public class BoomerangProjectile : MonoBehaviour
{

    private Transform playerTransform;
    private Vector2 launchDir;
    private Vector2 curvePerp;
    private float damage;
    private float speed;
    private float maxRange;
    private float knockBackForce;
    private float curveStrength;

    //  Internal state 
    private bool initialized;
    private bool isReturning;
    private float distanceTravelled;
    private float spawnTime;
    private readonly HashSet<int> hitOutgoing = new HashSet<int>();
    private readonly HashSet<int> hitReturning = new HashSet<int>();

    //  VFX handles 
    private SpriteRenderer sr;
    private TrailRenderer trail;
    private ProjectileGlow glow;

    private const float ROTATION_SPEED = 720f;   // deg/sec
    private const float MAX_LIFETIME = 6f;
    private const float CATCH_RADIUS = 0.7f;

    // Hit SFX throttle: once the boomerang hits something we don't replay the
    // hit sound for this many seconds, even if it clips several enemies in a
    // cluster. It replays only if a further target is hit after the window.
    private const float HIT_SFX_COOLDOWN = 1.6f;
    private float lastHitSoundTime = -999f;

    //  PUBLIC API 

    public void Initialize(Transform player, Vector2 direction, float dmg,
                           float spd, float range, float knockback, float curve)
    {
        playerTransform = player;
        launchDir = direction.normalized;
        damage = dmg;
        speed = spd;
        maxRange = range;
        knockBackForce = knockback;
        curveStrength = curve;
        spawnTime = Time.time;

        // perpendicular for lateral curve
        curvePerp = new Vector2(-launchDir.y, launchDir.x);

        BuildVisuals();
        initialized = true;

        // Throw SFX
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.boomerangShot.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.boomerangShot, transform.position);
        }

        //Debug.Log($"[Boomerang] Initialized — dir={launchDir} spd={speed} range={maxRange} dmg={damage}");
    }

    //  MOVEMENT  (position-based, rotation-independent)

    private void Update()
    {
        if (!initialized) return;

        // safety despawn
        if (Time.time - spawnTime > MAX_LIFETIME)
        {
            Destroy(gameObject);
            return;
        }

        // visual spin (purely cosmetic)
        transform.Rotate(0f, 0f, ROTATION_SPEED * Time.deltaTime);

        float dt = Time.deltaTime;

        if (!isReturning)
        {
            //  OUTGOING 
            float step = speed * dt;
            distanceTravelled += step;

            // sine curve peaks at the midpoint
            float t = distanceTravelled / maxRange;          // 0→1
            float lateral = Mathf.Sin(t * Mathf.PI) * curveStrength * dt;

            Vector3 move = (Vector3)(launchDir * step + curvePerp * lateral);
            transform.position += move;

            if (distanceTravelled >= maxRange)
            {
                isReturning = true;
                // colour shift on return trip
                if (sr != null) sr.color = new Color(0.55f, 0.85f, 1f);
                if (glow != null) glow.SetColor(new Color(0.55f, 0.85f, 1f));
                if (trail != null)
                {
                    trail.startColor = new Color(0.5f, 0.85f, 1f, 0.7f);
                    trail.endColor = new Color(0.3f, 0.6f, 1f, 0f);
                }
            }
        }
        else
        {
            //  RETURNING 
            if (playerTransform == null) { Destroy(gameObject); return; }

            Vector2 toPlayer = (Vector2)playerTransform.position - (Vector2)transform.position;
            if (toPlayer.magnitude < CATCH_RADIUS) { Destroy(gameObject); return; }

            Vector2 dir = toPlayer.normalized;
            float rSpd = speed * 1.15f;
            // small wobble
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float wobble = Mathf.Sin(Time.time * 6f) * curveStrength * 0.15f * dt;

            transform.position += (Vector3)(dir * rSpd * dt + perp * wobble);
        }
    }

    //  COLLISION

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!initialized) return;

        // skip non-enemy objects
        if (other.CompareTag("Player")) return;
        if (other.GetComponent<PlayerMovement>() != null) return;
        if (other.name.Contains("Energy")) return;

        if (!other.CompareTag("Enemy")) return;

        int id = other.GetInstanceID();
        var set = isReturning ? hitReturning : hitOutgoing;
        if (set.Contains(id)) return;          // one hit per trip per enemy
        set.Add(id);

        //  deal damage 
        CharacterStats stats = other.GetComponent<CharacterStats>();
        if (stats != null)
        {
            stats.TakeDamage(damage);
            CombatJuice.OnPlayerHitEnemy(other.gameObject, isMelee: false);
            PlayHitSfx();

            EnemyController ec = other.GetComponent<EnemyController>();
            if (ec != null)
            {
                Vector2 kb = ((Vector2)other.transform.position - (Vector2)transform.position).normalized;
                ec.ApplyKnockback(kb, knockBackForce);
            }
            return;
        }

        IDamageable dmg = other.GetComponent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(damage, gameObject);
            CombatJuice.OnPlayerHitEnemy(other.gameObject, isMelee: false);
            PlayHitSfx();
        }
        // boomerang passes through — never destroyed on hit
    }

    // Plays the boomerang hit sound, throttled so a single sweep through a
    // cluster of enemies doesn't spam it. Fires once, then stays quiet for
    // HIT_SFX_COOLDOWN seconds before it can play again on a later target.
    private void PlayHitSfx()
    {
        if (Time.time - lastHitSoundTime < HIT_SFX_COOLDOWN) return;
        lastHitSoundTime = Time.time;

        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.boomerangHit.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.boomerangHit, transform.position);
        }
    }

    //  PROGRAMMATIC VISUALS

    private void BuildVisuals()
    {
        //  sprite 
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = BoomerangSprite();
        sr.color = new Color(0.85f, 0.65f, 0.30f);
        sr.sortingOrder = 2500;

        //  trail 
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.20f;
        trail.startWidth = 0.38f;
        trail.endWidth = 0.03f;
        trail.numCornerVertices = 4;
        trail.numCapVertices = 4;
        trail.minVertexDistance = 0.05f;
        trail.sortingOrder = 2499;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = new Color(1f, 0.85f, 0.4f, 0.7f);
        trail.endColor = new Color(1f, 0.6f, 0.2f, 0f);
        trail.Clear();

        //  particles 
        BuildParticles();

        //  glow / tracer light (warm gold on the way out) 
        glow = ProjectileGlow.Attach(transform, new Color(1f, 0.85f, 0.4f), worldRadius: 0.7f,
                                     alpha: 0.5f, pulse: true, pulseSpeed: 7f, pulseAmount: 0.2f);
    }

    private void BuildParticles()
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        // Disable unused velocity module that spams console
        var vel = ps.velocityOverLifetime;
        vel.enabled = false;


        var main = ps.main;
        main.loop = true;
        main.startLifetime = 0.25f;
        main.startSpeed = 1.2f;
        main.startSize = 0.07f;
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(1f, 0.9f, 0.5f, 0.85f);

        var em = ps.emission;
        em.rateOverTime = 25f;

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.12f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[]{ new GradientColorKey(new Color(1f,.95f,.6f), 0f),
                   new GradientColorKey(new Color(1f,.5f,.2f),  1f) },
            new[]{ new GradientAlphaKey(0.8f, 0f),
                   new GradientAlphaKey(0f,  1f) });
        col.color = g;

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.material = new Material(Shader.Find("Sprites/Default"));
        psr.sortingOrder = 2498;
    }

    //  cached sprite 

    private static Sprite _cached;
    private static Sprite BoomerangSprite()
    {
        if (_cached != null) return _cached;

        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

        // V-shape
        Line(px, S, 16, 8, 16, 20, 2.8f, new Color(.9f, .7f, .35f));
        Line(px, S, 16, 20, 4, 28, 2.4f, new Color(.85f, .6f, .25f));
        Line(px, S, 16, 20, 28, 28, 2.4f, new Color(.85f, .6f, .25f));
        // tip highlights
        Line(px, S, 4, 28, 6, 26, 1.4f, new Color(1f, .92f, .6f));
        Line(px, S, 28, 28, 26, 26, 1.4f, new Color(1f, .92f, .6f));

        tex.SetPixels(px);
        tex.Apply();
        _cached = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), S);
        return _cached;
    }

    private static void Line(Color[] px, int s, float ax, float ay,
                              float bx, float by, float r, Color c)
    {
        Vector2 a = new Vector2(ax, ay), b = new Vector2(bx, by);
        int n = Mathf.CeilToInt(Vector2.Distance(a, b) * 3);
        for (int i = 0; i <= n; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / n);
            int x0 = Mathf.Max(0, (int)(p.x - r)), x1 = Mathf.Min(s - 1, (int)(p.x + r));
            int y0 = Mathf.Max(0, (int)(p.y - r)), y1 = Mathf.Min(s - 1, (int)(p.y + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), p);
                    if (d <= r)
                        px[y * s + x] = Color.Lerp(px[y * s + x], c, 1f - d / r * .5f);
                }
        }
    }
}

