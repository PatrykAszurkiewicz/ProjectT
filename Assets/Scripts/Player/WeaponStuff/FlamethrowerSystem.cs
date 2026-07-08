using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;

// Flamethrower logic + visuals.
// The fire is built from layered Unity ParticleSystems (Shuriken) rather than
// hand-spawned SpriteRenderers
public class FlamethrowerSystem
{
    //  References 
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;

    // Co-op: this flamethrower's OWNING player's aim, resolved from the weapon's
    // parent hierarchy so the flame follows THIS player's cursor/stick rather than
    // whichever player last won the global PlayerAim.Instance.
    private PlayerAim ownerAim;

    //  State 
    private bool isFiring;
    private float currentFuel;
    private float damageTickTimer;
    private float lastFireTime;

    //  Flame visuals (ParticleSystems) 
    private GameObject flameRoot;
    private ParticleSystem psCore;
    private ParticleSystem psOuter;
    private ParticleSystem psSmoke;
    private ParticleSystem psGlow;
    private ParticleSystem psSparks;
    private ParticleSystem[] allSystems;

    //  AoE tracking 
    private readonly HashSet<int> damagedThisTickIds = new HashSet<int>();

    //  Cached values 
    private Vector2 aimDirection = Vector2.right;
    private Camera mainCam;

    //  Audio (looping FMOD instance) 
    private EventInstance flamethrowerSfx;
    private bool flamethrowerSfxValid;

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

        BuildFlameVisuals();

        // Looping flamethrower SFX. Created here, started/stopped in StartFiring/StopFiring,
        // and released in Cleanup. We don't start it now — it only plays while firing.
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.flamethrower.IsNull)
        {
            flamethrowerSfx = AudioManager.instance.CreateInstance(FMODEvents.instance.flamethrower);
            flamethrowerSfx.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(playerTransform));
            flamethrowerSfxValid = true;
        }
    }

    public void Cleanup()
    {
        StopFiring();

        if (flamethrowerSfxValid && flamethrowerSfx.isValid())
        {
            flamethrowerSfx.stop(STOP_MODE.IMMEDIATE);
            flamethrowerSfx.release();
        }
        flamethrowerSfxValid = false;

        if (flameRoot != null)
            Object.Destroy(flameRoot);
    }



    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;
        UpdateAimDirection();
        UpdateEmitterTransform();

        // Keep the looping SFX positioned so the (now 3D) event isn't muted by FMOD.
        // playerTransform is the flame's owner and is always valid. Runs every frame
        // the weapon is equipped, so the instance has a position before it even starts.
        if (flamethrowerSfxValid && flamethrowerSfx.isValid())
            flamethrowerSfx.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(playerTransform));


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
    }

    // FIRING CONTROL

    public void StartFiring()
    {
        if (currentFuel <= 0f) return;
        isFiring = true;

        if (allSystems != null)
            foreach (var ps in allSystems)
                if (ps != null) ps.Play();

        // Start looping SFX if not already playing (idempotent — guard against rapid re-clicks
        // and the case where StartFiring is called while StopFiring's fadeout is still running)
        if (flamethrowerSfxValid && flamethrowerSfx.isValid())
        {
            flamethrowerSfx.getPlaybackState(out PLAYBACK_STATE state);
            if (state == PLAYBACK_STATE.STOPPED || state == PLAYBACK_STATE.STOPPING)
                flamethrowerSfx.start();
        }
    }

    public void StopFiring()
    {
        isFiring = false;
        lastFireTime = Time.time;

        // Stop emitting but let live particles finish their lifetime for a clean tail.
        if (allSystems != null)
            foreach (var ps in allSystems)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // Stop looping SFX (allow fade-out for a smoother tail)
        if (flamethrowerSfxValid && flamethrowerSfx.isValid())
        {
            flamethrowerSfx.getPlaybackState(out PLAYBACK_STATE state);
            if (state == PLAYBACK_STATE.PLAYING || state == PLAYBACK_STATE.STARTING)
                flamethrowerSfx.stop(STOP_MODE.ALLOWFADEOUT);
        }
    }

    public bool CanFire() => currentFuel > 0.05f;

    // AIM

    // This flamethrower's OWN player's aim (resolved from the weapon hierarchy,
    // then cached). Retries until found so a transient early-null can't stick.
    private PlayerAim ResolveOwnerAim()
    {
        if (ownerAim == null && weapon != null)
            ownerAim = weapon.GetComponentInParent<PlayerAim>();
        return ownerAim;
    }

    private void UpdateAimDirection()
    {
        // Use THIS flamethrower's own player's unified aim (mouse OR gamepad).
        // Reading the global PlayerAim.Instance pointed the flame along whichever
        // player spawned last, so in co-op a gamepad player's fire ignored their
        // own turn and streamed off in the other player's aim direction.
        PlayerAim a = ResolveOwnerAim();
        if (a == null) a = PlayerAim.Instance;   // legacy single-player fallback
        if (a != null)
        {
            aimDirection = a.Direction;
            return;
        }

        if (mainCam == null) return;

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
    }

    // Point the emitter at the nozzle and orient it along the aim direction.

    private void UpdateEmitterTransform()
    {
        if (flameRoot == null) return;

        Vector3 nozzle = playerTransform.position + (Vector3)(aimDirection * 0.4f);
        flameRoot.transform.position = nozzle;
        flameRoot.transform.rotation = Quaternion.LookRotation(
            new Vector3(aimDirection.x, aimDirection.y, 0f), Vector3.forward);
    }

    //  VISUAL CONSTRUCTION

    private void BuildFlameVisuals()
    {
        flameRoot = new GameObject("_FlamethrowerFX");
        flameRoot.transform.SetParent(playerTransform, false);
        UpdateEmitterTransform();

        Material fireMat = GetFireMaterial();   // soft round texture, alpha-blended
        Material smokeMat = GetSmokeMaterial();  // puffy texture, alpha-blended

        float lifeMin = data.flameParticleLifetimeMin;
        float lifeMax = data.flameParticleLifetimeMax;
        float speed = Mathf.Max(0.5f, data.flameSpeed);
        // Baseline density so the stream always reads as a solid flame even if the
        // data's particles-per-second is tuned low. Raise flameParticlesPerSecond
        // (or these multipliers) for a thicker jet.
        float rate = Mathf.Max(data.flameParticlesPerSecond, 45f);
        float coneAngle = Mathf.Clamp(data.flameConeAngle * 0.6f, 8f, 35f);

        //  SMOKE (back) 
        psSmoke = CreateSystem("Smoke", 1985, smokeMat);
        ConfigureCommon(psSmoke, coneAngle * 1.15f, 0.18f,
            speedMin: speed * 0.28f, speedMax: speed * 0.5f,
            lifeMin: lifeMax * 0.9f, lifeMax: lifeMax * 1.7f,
            sizeMin: 0.45f, sizeMax: 0.75f, rate: rate * 0.22f, maxParticles: 80);
        SetGradient(psSmoke, BuildSmokeGradient());
        SetSizeOverLife(psSmoke, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.9f));
        SetNoise(psSmoke, strength: 0.35f, frequency: 0.4f, scroll: 0.6f);
        SetSpin(psSmoke, 90f);

        //  OUTER FLAME 
        psOuter = CreateSystem("OuterFlame", 2000, fireMat);
        ConfigureCommon(psOuter, coneAngle, 0.13f,
            speedMin: speed * 0.8f, speedMax: speed * 1.15f,
            lifeMin: lifeMin, lifeMax: lifeMax,
            sizeMin: 0.32f, sizeMax: 0.58f, rate: rate * 0.85f, maxParticles: 250);
        SetGradient(psOuter, BuildOuterGradient());
        SetSizeOverLife(psOuter, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.35f));
        SetNoise(psOuter, strength: 0.55f, frequency: 0.55f, scroll: 1.1f);
        SetDrag(psOuter, dampen: 0.22f, limit: speed * 0.25f);
        SetSpin(psOuter, 160f);

        //  HOT CORE 
        psCore = CreateSystem("FlameCore", 2010, fireMat);
        ConfigureCommon(psCore, coneAngle * 0.8f, 0.10f,
            speedMin: speed * 0.9f, speedMax: speed * 1.25f,
            lifeMin: lifeMin * 0.7f, lifeMax: lifeMax * 0.85f,
            sizeMin: 0.18f, sizeMax: 0.34f, rate: rate, maxParticles: 250);
        SetGradient(psCore, BuildCoreGradient());
        SetSizeOverLife(psCore, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 1.15f));
        SetNoise(psCore, strength: 0.45f, frequency: 0.6f, scroll: 1.3f);
        SetDrag(psCore, dampen: 0.25f, limit: speed * 0.3f);
        SetSpin(psCore, 200f);

        //  NOZZLE GLOW 
        psGlow = CreateSystem("MuzzleGlow", 2005, fireMat);
        ConfigureCommon(psGlow, 18f, 0.05f,
            speedMin: speed * 0.05f, speedMax: speed * 0.12f,
            lifeMin: 0.12f, lifeMax: 0.22f,
            sizeMin: 0.55f, sizeMax: 0.85f, rate: 22f, maxParticles: 30);
        SetGradient(psGlow, BuildGlowGradient());
        SetSizeOverLife(psGlow, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f));

        //  SPARKS / EMBERS (front) 
        psSparks = CreateSystem("Sparks", 2020, fireMat);
        ConfigureCommon(psSparks, coneAngle * 1.3f, 0.10f,
            speedMin: speed * 1.1f, speedMax: speed * 1.8f,
            lifeMin: lifeMin * 0.6f, lifeMax: lifeMax * 0.9f,
            sizeMin: 0.05f, sizeMax: 0.12f, rate: rate * 0.5f, maxParticles: 100);
        SetGradient(psSparks, BuildSparkGradient());
        SetSizeOverLife(psSparks, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
        SetNoise(psSparks, strength: 0.7f, frequency: 0.9f, scroll: 1.6f);

        allSystems = new[] { psSmoke, psOuter, psCore, psGlow, psSparks };

        // Built in the "stopped/cleared" state — nothing emits until StartFiring().
        foreach (var ps in allSystems)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private ParticleSystem CreateSystem(string name, int sortingOrder, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(flameRoot.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f; // top-down: no falling
        main.startColor = Color.white;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortingOrder = sortingOrder;
        r.alignment = ParticleSystemRenderSpace.View;

        return ps;
    }

    private void ConfigureCommon(ParticleSystem ps, float coneAngle, float coneRadius,
        float speedMin, float speedMax, float lifeMin, float lifeMax,
        float sizeMin, float sizeMax, float rate, int maxParticles)
    {
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); // random spin start
        main.maxParticles = maxParticles;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = coneRadius;
    }

    private void SetGradient(ParticleSystem ps, Gradient g)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    private void SetSizeOverLife(ParticleSystem ps, AnimationCurve curve)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private void SetNoise(ParticleSystem ps, float strength, float frequency, float scroll)
    {
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
        noise.scrollSpeed = scroll;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        noise.damping = true;
    }

    private void SetDrag(ParticleSystem ps, float dampen, float limit)
    {
        var lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = dampen;
        lim.limit = limit;
    }

    private void SetSpin(ParticleSystem ps, float degPerSec)
    {
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        float r = degPerSec * Mathf.Deg2Rad;
        rot.z = new ParticleSystem.MinMaxCurve(-r, r);
    }

    //  Gradients 

    private static Gradient BuildCoreGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 1f, 0.88f), 0.0f),  // white-hot
                new GradientColorKey(new Color(1f, 0.93f, 0.5f), 0.2f), // pale yellow
                new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.55f), // orange
                new GradientColorKey(new Color(0.95f, 0.35f, 0.12f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(0.95f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    private static Gradient BuildOuterGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.72f, 0.28f), 0.0f),
                new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.3f),
                new GradientColorKey(new Color(0.82f, 0.2f, 0.06f), 0.7f),
                new GradientColorKey(new Color(0.45f, 0.1f, 0.04f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.85f, 0.12f),
                new GradientAlphaKey(0.6f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    private static Gradient BuildSmokeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.26f, 0.23f, 0.21f), 0f),
                new GradientColorKey(new Color(0.12f, 0.11f, 0.10f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.28f, 0.25f),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    private static Gradient BuildGlowGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.2f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.55f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    private static Gradient BuildSparkGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                new GradientColorKey(new Color(1f, 0.7f, 0.3f), 0.5f),
                new GradientColorKey(new Color(1f, 0.4f, 0.15f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    // Materials & textures 


    private static Material _fireMat;
    private static Material _smokeMat;
    private static Texture2D _softTex;
    private static Texture2D _puffTex;

    private static Material GetFireMaterial()
    {
        if (_fireMat != null) return _fireMat;
        _fireMat = new Material(Shader.Find("Sprites/Default"));
        _fireMat.mainTexture = GetSoftTexture();
        return _fireMat;
    }

    private static Material GetSmokeMaterial()
    {
        if (_smokeMat != null) return _smokeMat;
        _smokeMat = new Material(Shader.Find("Sprites/Default"));
        _smokeMat.mainTexture = GetPuffTexture();
        return _smokeMat;
    }

    // Soft round particle with a bright, tight hot core.
    private static Texture2D GetSoftTexture()
    {
        if (_softTex != null) return _softTex;

        const int SIZE = 64;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[SIZE * SIZE];
        var center = new Vector2(SIZE * 0.5f, SIZE * 0.5f);
        float radius = SIZE * 0.5f - 1f;

        for (int y = 0; y < SIZE; y++)
            for (int x = 0; x < SIZE; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                float t = Mathf.Clamp01(d / radius);
                float a = Mathf.Pow(1f - t, 1.5f);  // bright core, soft edge
                pixels[y * SIZE + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(pixels);
        tex.Apply();
        _softTex = tex;
        return _softTex;
    }

    // Puffier, noisier blob for smoke so it doesn't read as a clean disc.
    private static Texture2D GetPuffTexture()
    {
        if (_puffTex != null) return _puffTex;

        const int SIZE = 64;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[SIZE * SIZE];
        var center = new Vector2(SIZE * 0.5f, SIZE * 0.5f);
        float radius = SIZE * 0.5f - 1f;

        for (int y = 0; y < SIZE; y++)
            for (int x = 0; x < SIZE; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                float falloff = Mathf.Clamp01(1f - d / radius);
                float n = Mathf.PerlinNoise(x * 0.16f, y * 0.16f);
                float a = Mathf.Clamp01(falloff * (0.55f + 0.45f * n));
                pixels[y * SIZE + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(pixels);
        tex.Apply();
        _puffTex = tex;
        return _puffTex;
    }

    //  AoE DAMAGE — cone-shaped area in front of player (point-blank safe)

    private void TickAoEDamage()
    {
        damageTickTimer -= Time.deltaTime;
        if (damageTickTimer > 0f) return;

        damageTickTimer = data.flameDamageInterval;
        damagedThisTickIds.Clear();

        Collider2D[] hits = Physics2D.OverlapCircleAll(playerTransform.position, data.flameRange);
        float halfAngle = data.flameConeAngle * 0.5f;
        Vector2 playerPos = (Vector2)playerTransform.position;

        // Inside this radius the cone/angle test is skipped. At point-blank the
        // direction to an enemy is a tiny, jittery vector that can point anywhere
        // (even backward) — which is exactly why very-close enemies used to land
        // in a "grey zone" and take no damage. Anything this close is in the fire.
        float pointBlankRadius = Mathf.Max(1.0f, data.flameRange * 0.3f);

        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Enemy")) continue;

            // Measure to the CLOSEST point on the collider, not its centre, so
            // large / overlapping enemies register (ClosestPoint returns the
            // player's own position when it's inside the collider, i.e. dist 0).
            Vector2 closest = hit.ClosestPoint(playerPos);
            Vector2 toEnemy = closest - playerPos;
            float dist = toEnemy.magnitude;

            if (dist > data.flameRange) continue;

            if (dist > pointBlankRadius)
            {
                Vector2 dir = toEnemy.sqrMagnitude > 1e-4f ? toEnemy.normalized : aimDirection;
                float angle = Vector2.Angle(aimDirection, dir);
                if (angle > halfAngle) continue;
            }

            int id = hit.GetInstanceID();
            if (damagedThisTickIds.Contains(id)) continue;
            damagedThisTickIds.Add(id);

            float distanceFalloff = 1f - (dist / data.flameRange) * 0.4f;
            float dmg = data.damage * distanceFalloff;

            CharacterStats stats = hit.GetComponent<CharacterStats>();
            if (stats != null)
            {
                stats.TakeDamage(dmg);
                CombatJuice.OnPlayerHitEnemy(hit.gameObject, isMelee: false);
                continue;
            }

            IDamageable damageable = hit.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(dmg, weapon.gameObject);
                CombatJuice.OnPlayerHitEnemy(hit.gameObject, isMelee: false);
            }
        }
    }
}

