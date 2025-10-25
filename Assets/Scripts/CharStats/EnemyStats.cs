using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using System.Collections;

public class EnemyStats : CharacterStats
{
    [Header("Health Bar")]
    public GameObject healthBarPrefab;
    private EnemyHealthBar healthBar;

    [Header("Enemy Data")]
    public EnemyData enemyData;

    [Header("Energy Drop Settings")]
    [Range(0f, 1f)] public float energyDropChance = -1f;
    public int energyDropValue = -1;
    public bool canDropEnergy = true;

    [Header("Visual Feedback")]
    public float damageFlashDuration = 0.1f;
    public Color damageFlashColor = new Color(2f, 2f, 2f, 1f); // Additive bright white
    private SpriteRenderer spriteRenderer;
    private Coroutine damageFlashCoroutine;
    private Material originalMaterial;
    private Material flashMaterial;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            // Store original material
            originalMaterial = spriteRenderer.material;

            // Create flash material with additive blending
            flashMaterial = new Material(originalMaterial);
        }

        if (enemyData != null)
        {
            enemyData = ScriptableObjectUtility.Clone(enemyData);

            maxHealth = enemyData.maxHealth;

            if (EnemyStatModifierManager.Instance != null)
            {
                maxHealth *= EnemyStatModifierManager.Instance.GetHealthMultiplier();
            }

            currentHealth = maxHealth;
        }

        EnemyStatModifierManager.Instance?.RegisterEnemy(this);
    }

    private void OnDestroy()
    {
        EnemyStatModifierManager.Instance?.UnregisterEnemy(this);

        // Clean up materials
        if (flashMaterial != null)
        {
            Destroy(flashMaterial);
        }
    }

    private void Start()
    {
        if (healthBarPrefab != null)
        {
            GameObject bar = Instantiate(healthBarPrefab);
            healthBar = bar.GetComponent<EnemyHealthBar>();

            if (healthBar != null)
            {
                healthBar.Initialize(transform, maxHealth);
            }
        }
    }

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField, ReadOnly] private float currentMoveSpeedDebug;
#endif

#if UNITY_EDITOR
    private void Update()
    {
        currentMoveSpeedDebug = MoveSpeed;
    }
#endif

    public override void TakeDamage(float amount)
    {
        base.TakeDamage(amount);

        StartDamageFlash();

        if (healthBar != null)
            healthBar.UpdateHealth(currentHealth);
    }

    private void StartDamageFlash()
    {
        if (spriteRenderer == null) return;

        if (damageFlashCoroutine != null)
            StopCoroutine(damageFlashCoroutine);
        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;

        // Double blink 
        for (int i = 0; i < 2; i++)
        {
            spriteRenderer.color = damageFlashColor;
            yield return new WaitForSeconds(damageFlashDuration);
            spriteRenderer.color = originalColor;
            yield return new WaitForSeconds(0.05f);
        }

        damageFlashCoroutine = null;
    }

    public override void Die()
    {
        var gremlinController = GetComponent<GremlinController>();
        if (gremlinController != null)
        {
            if (healthBar != null)
                Destroy(healthBar.gameObject);

            gremlinController.Die();
            return;
        }

        if (canDropEnergy)
        {
            EnergyDropManager.TrySpawnEnergyDrop(transform.position, energyDropChance, energyDropValue);
        }

        if (healthBar != null)
            Destroy(healthBar.gameObject);

        WaveSpawner waveSpawner = FindAnyObjectByType<WaveSpawner>();
        if (waveSpawner != null)
            waveSpawner.OnEnemyDeath();

        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilled(gameObject);
        }

        base.Die();
    }

    public float Damage
    {
        get
        {
            float baseDamage = enemyData?.damage ?? 0f;
            if (EnemyStatModifierManager.Instance != null)
            {
                float multiplier = EnemyStatModifierManager.Instance.GetDamageMultiplier();
                return baseDamage * multiplier;
            }
            return baseDamage;
        }
    }

    public float MoveSpeed
    {
        get
        {
            float baseSpeed = enemyData?.moveSpeed ?? 1f;
            if (EnemyStatModifierManager.Instance != null)
            {
                float multiplier = EnemyStatModifierManager.Instance.GetMoveSpeedMultiplier();
                float finalSpeed = baseSpeed * multiplier;
                return finalSpeed;
            }
            return baseSpeed;
        }
    }

    public float Mass => enemyData?.mass ?? 50f;

    public void ConfigureEnergyDrop(float dropChance, int dropValue)
    {
        energyDropChance = Mathf.Clamp01(dropChance);
        energyDropValue = Mathf.Max(1, dropValue);
    }

    public void DisableEnergyDrops()
    {
        canDropEnergy = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!canDropEnergy) return;

        float chance = energyDropChance >= 0 ? energyDropChance : (EnergyDropManager.Instance?.globalDropChance ?? 0.5f);
        int value = energyDropValue > 0 ? energyDropValue : (EnergyDropManager.Instance?.defaultEnergyValue ?? 10);

        UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, $"Drop: {(chance * 100f):F0}%");
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.3f, $"Energy: {value}");
    }
#endif
}