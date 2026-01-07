using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class FireOnDodgeEffect : MonoBehaviour
{
    [Header("CSV-Driven Parameters")]
    [System.NonSerialized] public float damagePercent = 0.2f;
    [System.NonSerialized] public float dotPercent = 0.05f;
    [System.NonSerialized] public float dotDuration = 10f;

    [Header("Fire Properties")]
    [SerializeField] private float fireDuration = 8f;
    [SerializeField] private int fireCount = 10;

    private PlayerStats playerStats;
    private PlayerMovement playerMovement;
    private Weapon weapon;
    private bool wasDashing = false;
    private Vector2 dashStartPosition;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        playerMovement = GetComponent<PlayerMovement>();

        if (playerStats == null)
        {
            Debug.LogError("[FIRE_DODGE] PlayerStats not found");
            enabled = false;
        }
    }

    void Start()
    {
        weapon = GetComponentInChildren<Weapon>();
        if (weapon == null)
        {
            weapon = FindFirstObjectByType<Weapon>();
        }
    }

    void Update()
    {
        bool isDashing = GetDashingState();

        if (isDashing && !wasDashing)
        {
            dashStartPosition = transform.position;
        }

        if (!isDashing && wasDashing)
        {
            Vector2 dashEndPosition = transform.position;
            SpawnFireTrail(dashStartPosition, dashEndPosition);
        }

        wasDashing = isDashing;
    }

    private bool GetDashingState()
    {
        return playerMovement.GetType()
            .GetField("isDashing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(playerMovement) as bool? ?? false;
    }

    private void SpawnFireTrail(Vector2 startPos, Vector2 endPos)
    {
        float baseDamage = CalculateBaseDamage();
        float initialDamage = baseDamage * damagePercent;
        float dotDamagePerSecond = baseDamage * dotPercent;

        for (int i = 0; i < fireCount; i++)
        {
            float t = i / (float)(fireCount - 1);
            Vector2 firePosition = Vector2.Lerp(startPos, endPos, t);
            // Add slight random offset
            Vector2 randomOffset = Random.insideUnitCircle * 0.1f;
            firePosition += randomOffset;

            SpawnFire(firePosition, initialDamage, dotDamagePerSecond, i);
        }
    }

    private void SpawnFire(Vector2 position, float initialDamage, float dotDamage, int index)
    {
        GameObject fireObj = new GameObject("FirePatch");
        fireObj.transform.position = position;

        var fire = fireObj.AddComponent<FirePatch>();
        fire.Initialize(initialDamage, dotDamage, dotDuration, fireDuration, index);
    }

    private float CalculateBaseDamage()
    {
        if (weapon != null)
        {
            var weaponData = weapon.GetWeaponData();
            if (weaponData != null)
            {
                return weaponData.damage;
            }
        }
        return 10f;
    }
}

public class FirePatch : MonoBehaviour
{
    private float initialDamage;
    private float dotDamagePerSecond;
    private float dotDuration;
    private float patchDuration;
    private float timer;
    private int seed;

    private SpriteRenderer coreSprite;
    private SpriteRenderer midSprite;
    private SpriteRenderer glowSprite;
    private List<GameObject> embers = new List<GameObject>();
    private CircleCollider2D damageCollider;
    private HashSet<GameObject> burningEnemies = new HashSet<GameObject>();

    public void Initialize(float initDmg, float dotDmg, float dotDur, float patchDur, int index)
    {
        initialDamage = initDmg;
        dotDamagePerSecond = dotDmg;
        dotDuration = dotDur;
        patchDuration = patchDur;
        timer = 0f;
        seed = index;

        SetupVisuals();
        SetupCollider();
        CreateEmbers();

        StartCoroutine(BurnCycle());
    }

    private void SetupVisuals()
    {
        // Bottom glow layer - red/orange 
        GameObject glowObj = new GameObject("FireGlow");
        glowObj.transform.SetParent(transform);
        glowObj.transform.localPosition = Vector3.zero;

        glowSprite = glowObj.AddComponent<SpriteRenderer>();
        glowSprite.sprite = CreateFireSprite(2.0f, seed);
        glowSprite.sortingOrder = 3;
        glowSprite.color = new Color(1f, 0.2f, 0f, 0.35f);
        glowObj.transform.localScale = new Vector3(1.0f, 1.0f, 1f);

        // Middle layer - orange 
        GameObject midObj = new GameObject("FireMid");
        midObj.transform.SetParent(transform);
        midObj.transform.localPosition = new Vector3(0, 0.1f, 0);

        midSprite = midObj.AddComponent<SpriteRenderer>();
        midSprite.sprite = CreateFireSprite(1.5f, seed + 1);
        midSprite.sortingOrder = 4;
        midSprite.color = new Color(1f, 0.5f, 0f, 0.55f); // Steering transparency 0.55
        midObj.transform.localScale = new Vector3(0.8f, 0.8f, 1f); // Steering width

        // Top core layer - yellow/white
        GameObject coreObj = new GameObject("FireCore");
        coreObj.transform.SetParent(transform);
        coreObj.transform.localPosition = new Vector3(0, 0.15f, 0);

        coreSprite = coreObj.AddComponent<SpriteRenderer>();
        coreSprite.sprite = CreateFireSprite(1.2f, seed + 2);
        coreSprite.sortingOrder = 5;
        coreSprite.color = new Color(1f, 0.9f, 0.3f, 0.7f); // Steering transparency
        coreObj.transform.localScale = new Vector3(0.7f, 0.7f, 1f); // Steering width
    }

    private Sprite CreateFireSprite(float shapeVariation, int noiseSeed)
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];

        Vector2 center = new Vector2(size / 2f, size / 2.5f);
        float maxRadius = size / 2.8f; // Steering width

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = y * size + x;

                Vector2 pos = new Vector2(x, y);
                float dist = Vector2.Distance(pos, center);

                if (dist < maxRadius * shapeVariation)
                {
                    // Multiple octaves of noise
                    float noise1 = Mathf.PerlinNoise((x + noiseSeed * 100) * 0.08f, y * 0.08f);
                    float noise2 = Mathf.PerlinNoise((x + noiseSeed * 100) * 0.15f, y * 0.15f);
                    float noise3 = Mathf.PerlinNoise((x + noiseSeed * 100) * 0.25f, y * 0.25f);
                    float combinedNoise = (noise1 * 0.5f + noise2 * 0.3f + noise3 * 0.2f);

                    // Vertical gradient
                    float heightFactor = 1f - (y / (float)size);
                    float intensity = 1f - (dist / (maxRadius * shapeVariation));
                    intensity = Mathf.Pow(intensity, 1.3f);
                    intensity *= (0.5f + combinedNoise * 0.5f);
                    intensity *= (0.3f + heightFactor * 0.7f);

                    // Create flickering
                    if (dist > maxRadius * shapeVariation * 0.6f)
                    {
                        float angle = Mathf.Atan2(y - center.y, x - center.x);
                        float tendrilNoise = Mathf.PerlinNoise(angle * 3f + noiseSeed, 0f);
                        intensity *= (0.6f + tendrilNoise * 0.4f);
                    }
                    intensity = Mathf.Clamp01(intensity);
                    pixels[index] = new Color(1f, 1f, 1f, intensity);
                }
                else
                {
                    pixels[index] = Color.clear;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;

        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.4f), 32f);
    }

    private void CreateEmbers()
    {
        // Ember particles
        for (int i = 0; i < 3; i++)
        {
            GameObject emberObj = new GameObject($"Ember_{i}");
            emberObj.transform.SetParent(transform);

            Vector3 randomPos = new Vector3(
                Random.Range(-0.15f, 0.15f), // Steering width
                Random.Range(0f, 0.3f),
                0f
            );
            emberObj.transform.localPosition = randomPos;
            SpriteRenderer emberSprite = emberObj.AddComponent<SpriteRenderer>();
            emberSprite.sprite = CreateEmberSprite();
            emberSprite.sortingOrder = 6;
            emberSprite.color = new Color(1f, 0.7f, 0.2f, 0.6f); // Steering transparency
            emberObj.transform.localScale = Vector3.one * Random.Range(0.15f, 0.3f);

            embers.Add(emberObj);
        }
    }

    private Sprite CreateEmberSprite()
    {
        int size = 8;
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];

        Vector2 center = new Vector2(size / 2f, size / 2f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = y * size + x;
                float dist = Vector2.Distance(new Vector2(x, y), center);

                if (dist < size / 2f)
                {
                    float intensity = 1f - (dist / (size / 2f));
                    pixels[index] = new Color(1f, 1f, 1f, intensity);
                }
                else
                {
                    pixels[index] = Color.clear;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;

        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
    }

    private void SetupCollider()
    {
        damageCollider = gameObject.AddComponent<CircleCollider2D>();
        damageCollider.radius = 0.5f; // Steering how narrow the flames are
        damageCollider.isTrigger = true;
    }

    private IEnumerator BurnCycle()
    {
        Color coreColor = coreSprite.color;
        Color midColor = midSprite.color;
        Color glowColor = glowSprite.color;

        while (timer < patchDuration)
        {
            timer += Time.deltaTime;
            float time = Time.time + seed;

            // Core flame 
            float coreFlicker = 0.85f + Mathf.PerlinNoise(time * 5f, seed) * 0.15f;
            float coreScale = 1f + Mathf.Sin(time * 4f) * 0.12f;
            float coreRise = Mathf.Sin(time * 3f) * 0.08f;

            coreSprite.transform.localScale = Vector3.one * coreScale;
            coreSprite.transform.localPosition = new Vector3(0, 0.15f + coreRise, 0);
            Color currentCore = coreColor * coreFlicker;

            // Mid flame 
            float midFlicker = 0.9f + Mathf.PerlinNoise(time * 3.5f, seed + 1) * 0.1f;
            float midScale = 1.1f + Mathf.Sin(time * 3f) * 0.1f;
            float midRise = Mathf.Sin(time * 2.5f) * 0.06f;

            midSprite.transform.localScale = Vector3.one * midScale;
            midSprite.transform.localPosition = new Vector3(0, 0.1f + midRise, 0);
            Color currentMid = midColor * midFlicker;

            // Glow 
            float glowPulse = 0.92f + Mathf.Sin(time * 2f) * 0.08f;
            float glowScale = 1.4f + Mathf.Sin(time * 1.8f) * 0.15f;

            glowSprite.transform.localScale = Vector3.one * glowScale;
            Color currentGlow = glowColor * glowPulse;

            // Animate embers
            for (int i = 0; i < embers.Count; i++)
            {
                if (embers[i] == null) continue;

                float emberSpeed = 0.3f + i * 0.1f;
                float emberRise = (time * emberSpeed) % 0.5f;
                float emberFlicker = 0.7f + Mathf.PerlinNoise(time * 8f, i + seed) * 0.3f;

                Vector3 pos = embers[i].transform.localPosition;
                pos.y = emberRise;
                pos.x = Mathf.Sin(time * 2f + i) * 0.15f;
                embers[i].transform.localPosition = pos;

                SpriteRenderer emberSprite = embers[i].GetComponent<SpriteRenderer>();
                if (emberSprite != null)
                {
                    Color emberColor = emberSprite.color;
                    emberColor.a = (1f - emberRise / 0.5f) * emberFlicker * 0.8f;
                    emberSprite.color = emberColor;
                }
            }

            // Fade out in last 2 seconds
            if (timer >= patchDuration - 2f)
            {
                float fadeProgress = (patchDuration - timer) / 2f;
                currentCore.a *= fadeProgress;
                currentMid.a *= fadeProgress;
                currentGlow.a *= fadeProgress;
            }

            coreSprite.color = currentCore;
            midSprite.color = currentMid;
            glowSprite.color = currentGlow;

            yield return null;
        }

        Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Enemy"))
        {
            var enemyStats = other.GetComponent<EnemyStats>();
            if (enemyStats != null && !enemyStats.IsDead())
            {
                enemyStats.TakeDamage(initialDamage);

                if (!burningEnemies.Contains(other.gameObject))
                {
                    burningEnemies.Add(other.gameObject);
                    ApplyBurningEffect(other.gameObject, enemyStats);
                }
            }
        }
    }

    private void ApplyBurningEffect(GameObject enemy, EnemyStats enemyStats)
    {
        var burning = enemy.GetComponent<BurningEffect>();
        if (burning == null)
        {
            burning = enemy.AddComponent<BurningEffect>();
        }

        burning.ApplyBurn(dotDamagePerSecond, dotDuration);
    }
}

public class BurningEffect : MonoBehaviour
{
    private float damagePerSecond;
    private float duration;
    private float timer;
    private EnemyStats enemyStats;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private bool isBurning = false;

    void Awake()
    {
        enemyStats = GetComponent<EnemyStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }

    public void ApplyBurn(float dps, float dur)
    {
        if (isBurning)
        {
            duration = Mathf.Max(duration - timer, dur);
            timer = 0f;
        }
        else
        {
            damagePerSecond = dps;
            duration = dur;
            timer = 0f;
            isBurning = true;

            StartCoroutine(BurnCoroutine());
        }
    }

    private IEnumerator BurnCoroutine()
    {
        while (timer < duration && enemyStats != null && !enemyStats.IsDead())
        {
            timer += Time.deltaTime;

            if (Mathf.FloorToInt(timer * 2f) > Mathf.FloorToInt((timer - Time.deltaTime) * 2f))
            {
                float damage = damagePerSecond * 0.5f;
                enemyStats.TakeDamage(damage);
            }

            if (spriteRenderer != null)
            {
                float intensity = 0.4f + Mathf.PerlinNoise(timer * 5f, 0f) * 0.3f;
                spriteRenderer.color = Color.Lerp(originalColor, new Color(1f, 0.5f, 0f), intensity);
            }

            yield return null;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        isBurning = false;

        if (enemyStats != null && !enemyStats.IsDead())
        {
            Destroy(this);
        }
    }

    void OnDestroy()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
}
