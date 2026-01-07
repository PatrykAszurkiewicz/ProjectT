using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class LightningOnDodgeEffect : MonoBehaviour
{
    [Header("CSV-Driven Parameters")]
    [System.NonSerialized] public float damagePercent = 0.2f;

    [Header("Lightning Properties")]
    [SerializeField] private float lightningDuration = 0.6f;
    [SerializeField] private int lightningBoltCount = 7;

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
            Debug.LogError("[LIGHTNING_DODGE] PlayerStats not found");
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
            SpawnLightningTrail(dashStartPosition, dashEndPosition);
        }

        wasDashing = isDashing;
    }

    private bool GetDashingState()
    {
        return playerMovement.GetType()
            .GetField("isDashing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(playerMovement) as bool? ?? false;
    }

    private void SpawnLightningTrail(Vector2 startPos, Vector2 endPos)
    {
        float baseDamage = CalculateBaseDamage();
        float lightningDamage = baseDamage * damagePercent;

        // Calculate dash direction for rotation
        Vector2 dashDirection = (endPos - startPos).normalized;
        float dashAngle = Mathf.Atan2(dashDirection.y, dashDirection.x) * Mathf.Rad2Deg;

        for (int i = 0; i < lightningBoltCount; i++)
        {
            float t = i / (float)(lightningBoltCount - 1);
            Vector2 boltPosition = Vector2.Lerp(startPos, endPos, t);

            SpawnLightningBolt(boltPosition, dashAngle, lightningDamage);
        }

        //Debug.Log($"[LIGHTNING_DODGE] Spawned {lightningBoltCount} lightning bolts at angle {dashAngle}°");
    }

    private void SpawnLightningBolt(Vector2 position, float angle, float damage)
    {
        GameObject boltObj = new GameObject("LightningBolt");
        boltObj.transform.position = new Vector3(position.x, position.y, 0f);

        // Rotate to align with dash direction
        boltObj.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);

        var bolt = boltObj.AddComponent<LightningBolt>();
        bolt.Initialize(damage, lightningDuration);
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

public class LightningBolt : MonoBehaviour
{
    private float damage;
    private float duration;
    private float timer;
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D damageCollider;
    private HashSet<GameObject> hitEnemies = new HashSet<GameObject>();

    public void Initialize(float dmg, float dur)
    {
        damage = dmg;
        duration = dur;
        timer = 0f;

        SetupVisuals();
        SetupCollider();

        StartCoroutine(AnimateAndFade());
    }

    private void SetupVisuals()
    {
        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = CreateLightningSprite();
        spriteRenderer.sortingOrder = 10;
        spriteRenderer.color = new Color(0.8f, 0.95f, 1f, 1f);

        transform.localScale = new Vector3(1.5f, 1.5f, 1f);
    }

    private Sprite CreateLightningSprite()
    {
        int width = 32;
        int height = 64;
        Texture2D texture = new Texture2D(width, height);
        Color[] pixels = new Color[width * height];

        Color coreColor = new Color(1f, 1f, 1f, 1f);
        Color edgeColor = new Color(0.6f, 0.85f, 1f, 0.8f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;

                float centerX = width / 2f;

                // Lightning bolt
                float jaggedOffset = Mathf.PerlinNoise(y * 0.3f, 0f) * 8f - 4f;
                float adjustedCenter = centerX + jaggedOffset;
                float adjustedDist = Mathf.Abs(x - adjustedCenter);
                // Main bolt
                float heightFactor = Mathf.Sin((y / (float)height) * Mathf.PI);
                float maxWidth = 3f + heightFactor * 4f;
                if (adjustedDist < maxWidth)
                {
                    float intensity = 1f - (adjustedDist / maxWidth);
                    Color color = Color.Lerp(edgeColor, coreColor, intensity);
                    pixels[index] = color;
                }
                // Glow
                else if (adjustedDist < maxWidth + 4f)
                {
                    float glowIntensity = 1f - ((adjustedDist - maxWidth) / 4f);
                    Color glowColor = edgeColor;
                    glowColor.a *= glowIntensity * 0.5f;
                    pixels[index] = glowColor;
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

        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 32f);
    }

    private void SetupCollider()
    {
        damageCollider = gameObject.AddComponent<CircleCollider2D>();
        damageCollider.radius = 0.8f;
        damageCollider.isTrigger = true;
    }

    private IEnumerator AnimateAndFade()
    {
        Color originalColor = spriteRenderer.color;

        while (timer < duration)
        {
            timer += Time.deltaTime;

            // Fast flicker
            float flicker = Mathf.Sin(timer * 30f) * 0.3f + 0.7f;
            // Fade out
            float fadeAlpha = 1f - (timer / duration);
            Color currentColor = originalColor * flicker;
            currentColor.a = fadeAlpha;
            spriteRenderer.color = currentColor;

            yield return null;
        }

        Destroy(gameObject);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag("Enemy") && !hitEnemies.Contains(other.gameObject))
        {
            var enemyStats = other.GetComponent<EnemyStats>();
            if (enemyStats != null && !enemyStats.IsDead())
            {
                enemyStats.TakeDamage(damage);
                hitEnemies.Add(other.gameObject);
                //Debug.Log($"[LIGHTNING] Hit {other.name} for {damage} damage");
            }
        }
    }
}
