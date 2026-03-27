using UnityEngine;
using System.Collections;
using System.Collections.Generic;



public class FlamethrowerSystem
{
    //  References 
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;

    //  State 
    private bool isFiring;
    private float currentFuel;
    private float damageTickTimer;
    private float lastFireTime;

    // Flame visuals 
    private GameObject flameRoot;
    private readonly List<FlameParticle> particles = new List<FlameParticle>();
    private const int MAX_PARTICLES = 40;
    private float spawnAccumulator;

    //  AoE tracking 
    private readonly HashSet<int> damagedThisTickIds = new HashSet<int>();

    //  Cached values 
    private Vector2 aimDirection = Vector2.right;
    private Camera mainCam;

    public bool IsFiring => isFiring;
    public float FuelNormalized => data != null ? currentFuel / data.flameFuelMax : 0f;
    public float CurrentFuel => currentFuel;


    /// Restore fuel level when re-creating the system (weapon swap back).
    public void SetFuel(float fuel)
    {
        currentFuel = Mathf.Clamp(fuel, 0f, data != null ? data.flameFuelMax : 100f);
    }


    public FlamethrowerSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.currentFuel = data.flameFuelMax;
        this.mainCam = Camera.main;

        // Create a root object for all flame visuals
        flameRoot = new GameObject("_FlamethrowerFX");
        flameRoot.transform.SetParent(playerTransform, false);
        flameRoot.transform.localPosition = Vector3.zero;
    }

    public void Cleanup()
    {
        StopFiring();

        if (flameRoot != null)
            Object.Destroy(flameRoot);
        particles.Clear();
    }



    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;
        UpdateAimDirection();

        if (isFiring)
        {
            // Drain fuel
            currentFuel -= data.flameFuelDrain * Time.deltaTime;
            if (currentFuel <= 0f)
            {
                currentFuel = 0f;
                StopFiring();
            }
            else
            {
                SpawnFlameParticles();
                TickAoEDamage();
            }
        }
        else
        {
            // Regenerate fuel when not firing (after a short delay)
            if (Time.time - lastFireTime > data.flameFuelRegenDelay)
            {
                currentFuel = Mathf.Min(currentFuel + data.flameFuelRegen * Time.deltaTime, data.flameFuelMax);
            }
        }

        UpdateParticles();
    }

    // FIRING CONTROL

    public void StartFiring()
    {
        if (currentFuel <= 0f) return;
        isFiring = true;
    }

    public void StopFiring()
    {
        isFiring = false;
        lastFireTime = Time.time;
    }

    public bool CanFire() => currentFuel > 0.05f;

    // AIM

    private void UpdateAimDirection()
    {
        if (mainCam == null) return;

#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            Vector2 mouseScreen = mouse.position.ReadValue();
            Vector3 mouseWorld = mainCam.ScreenToWorldPoint(mouseScreen);
            mouseWorld.z = 0f;
            aimDirection = ((Vector2)(mouseWorld - playerTransform.position)).normalized;
            if (aimDirection.sqrMagnitude < 0.001f)
                aimDirection = Vector2.right;
        }
#else
        Vector3 mp = mainCam.ScreenToWorldPoint(Input.mousePosition);
        mp.z = 0f;
        aimDirection = ((Vector2)(mp - playerTransform.position)).normalized;
        if (aimDirection.sqrMagnitude < 0.001f)
            aimDirection = Vector2.right;
#endif
    }

    // FLAME PARTICLE SPAWNING — procedural sprites

    private void SpawnFlameParticles()
    {
        float spawnRate = data.flameParticlesPerSecond;
        spawnAccumulator += spawnRate * Time.deltaTime;

        while (spawnAccumulator >= 1f && particles.Count < MAX_PARTICLES)
        {
            spawnAccumulator -= 1f;
            SpawnOneParticle();
        }
    }

    private void SpawnOneParticle()
    {
        GameObject go = new GameObject("FlamePart");
        go.transform.SetParent(flameRoot.transform, true);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GenerateFlameSprite();
        sr.sortingOrder = 2000 + Random.Range(0, 100);
        sr.material = new Material(Shader.Find("Sprites/Default"));

        float angleOffset = Random.Range(-data.flameConeAngle * 0.5f, data.flameConeAngle * 0.5f);
        float baseAngle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
        float particleAngle = (baseAngle + angleOffset) * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(particleAngle), Mathf.Sin(particleAngle));

        Vector2 perp = new Vector2(-dir.y, dir.x);
        float lateralJitter = Random.Range(-0.15f, 0.15f);

        Vector3 spawnPos = playerTransform.position + (Vector3)(aimDirection * 0.4f) + (Vector3)(perp * lateralJitter);
        go.transform.position = spawnPos;

        float lifetime = Random.Range(data.flameParticleLifetimeMin, data.flameParticleLifetimeMax);
        float speed = data.flameSpeed * Random.Range(0.8f, 1.2f);
        float startSize = Random.Range(0.18f, 0.35f);
        float endSize = Random.Range(0.5f, 0.9f);

        var p = new FlameParticle
        {
            go = go,
            sr = sr,
            velocity = dir * speed + perp * Random.Range(-0.5f, 0.5f),
            lifetime = lifetime,
            maxLifetime = lifetime,
            startSize = startSize,
            endSize = endSize,
            rotationSpeed = Random.Range(-180f, 180f),
            turbulenceOffset = Random.Range(0f, 100f)
        };

        go.transform.localScale = Vector3.one * startSize;
        go.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

        particles.Add(p);
    }

    // PARTICLE UPDATE

    private void UpdateParticles()
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];

            if (p.go == null)
            {
                particles.RemoveAt(i);
                continue;
            }

            p.lifetime -= Time.deltaTime;
            if (p.lifetime <= 0f)
            {
                Object.Destroy(p.go);
                particles.RemoveAt(i);
                continue;
            }

            float t = 1f - (p.lifetime / p.maxLifetime); // 0 → 1

            float turbX = (Mathf.PerlinNoise(p.turbulenceOffset, Time.time * 3f) - 0.5f) * 2f;
            float turbY = (Mathf.PerlinNoise(p.turbulenceOffset + 50f, Time.time * 3f) - 0.5f) * 2f;
            Vector2 turbulence = new Vector2(turbX, turbY) * 1.2f;

            float speedDecay = 1f - t * 0.4f;
            p.go.transform.position += (Vector3)(p.velocity * speedDecay + turbulence) * Time.deltaTime;

            float size = Mathf.Lerp(p.startSize, p.endSize, t);
            p.go.transform.localScale = Vector3.one * size;

            p.go.transform.Rotate(0, 0, p.rotationSpeed * Time.deltaTime);

            Color c = GetFlameColor(t);
            p.sr.color = c;
        }
    }

    private Color GetFlameColor(float t)
    {
        Color c;
        if (t < 0.1f)
        {
            c = Color.Lerp(new Color(1f, 1f, 0.9f, 0.9f), new Color(1f, 0.95f, 0.3f, 0.85f), t / 0.1f);
        }
        else if (t < 0.3f)
        {
            float sub = (t - 0.1f) / 0.2f;
            c = Color.Lerp(new Color(1f, 0.85f, 0.2f, 0.8f), new Color(1f, 0.5f, 0.1f, 0.7f), sub);
        }
        else if (t < 0.6f)
        {
            float sub = (t - 0.3f) / 0.3f;
            c = Color.Lerp(new Color(1f, 0.45f, 0.05f, 0.65f), new Color(0.8f, 0.15f, 0.05f, 0.45f), sub);
        }
        else
        {
            float sub = (t - 0.6f) / 0.4f;
            c = Color.Lerp(new Color(0.7f, 0.1f, 0.02f, 0.4f), new Color(0.2f, 0.15f, 0.1f, 0f), sub);
        }
        return c;
    }

    // AoE DAMAGE — cone-shaped area in front of player

    private void TickAoEDamage()
    {
        damageTickTimer -= Time.deltaTime;
        if (damageTickTimer > 0f) return;

        damageTickTimer = data.flameDamageInterval;
        damagedThisTickIds.Clear();

        Collider2D[] hits = Physics2D.OverlapCircleAll(playerTransform.position, data.flameRange);
        float halfAngle = data.flameConeAngle * 0.5f;
        Vector2 playerPos = (Vector2)playerTransform.position;

        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Enemy")) continue;

            Vector2 hitCenter = (Vector2)hit.bounds.center;
            Vector2 toEnemy = hitCenter - playerPos;
            float dist = toEnemy.magnitude;

            // No minimum distance, flamethrower should damage at point-blank range
            if (dist > data.flameRange) continue;

            float angle = Vector2.Angle(aimDirection, toEnemy);
            if (angle > halfAngle) continue;

            int id = hit.GetInstanceID();
            if (damagedThisTickIds.Contains(id)) continue;
            damagedThisTickIds.Add(id);

            float distanceFalloff = 1f - (dist / data.flameRange) * 0.4f;
            float dmg = data.damage * distanceFalloff;

            CharacterStats stats = hit.GetComponent<CharacterStats>();
            if (stats != null)
            {
                stats.TakeDamage(dmg);
                CombatFeel.OnHitEnemy(hit.gameObject, isMelee: false);
                continue;
            }

            IDamageable damageable = hit.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(dmg, weapon.gameObject);
                CombatFeel.OnHitEnemy(hit.gameObject, isMelee: false);
            }
        }
    }

    // PROCEDURAL FLAME SPRITE

    private static Sprite _cachedFlameSprite;

    private static Sprite GenerateFlameSprite()
    {
        if (_cachedFlameSprite != null) return _cachedFlameSprite;

        const int SIZE = 32;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[SIZE * SIZE];
        Vector2 center = new Vector2(SIZE * 0.5f, SIZE * 0.5f);
        float radius = SIZE * 0.5f - 1f;

        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float t = Mathf.Clamp01(dist / radius);
                float alpha = 1f - t * t * t;
                alpha = Mathf.Clamp01(alpha);
                pixels[y * SIZE + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        _cachedFlameSprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), Vector2.one * 0.5f, SIZE);
        return _cachedFlameSprite;
    }

    private class FlameParticle
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float lifetime;
        public float maxLifetime;
        public float startSize;
        public float endSize;
        public float rotationSpeed;
        public float turbulenceOffset;
    }
}
