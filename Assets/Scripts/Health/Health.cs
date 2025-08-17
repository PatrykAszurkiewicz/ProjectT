using UnityEngine;
using UnityEngine.Events;

public class Health : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public bool destroyOnDeath = true;
    public bool showHealthBar = true;

    [Header("Health Bar Settings")]
    public GameObject healthBarPrefab;
    public Vector3 healthBarOffset = new Vector3(0, 0.5f, 0);

    [Header("Events")]
    public UnityEvent OnHealthChanged;
    public UnityEvent OnDeath;

    private float currentHealth;
    private GameObject healthBarInstance;
    private HealthBar healthBarScript;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthPercentage => maxHealth > 0 ? currentHealth / maxHealth : 0f;
    public bool IsAlive => currentHealth > 0f;
    public bool IsDead => currentHealth <= 0f;

    void Awake()
    {
        currentHealth = maxHealth;

        if (showHealthBar)
        {
            CreateHealthBar();
        }
    }

    void Start()
    {
        OnHealthChanged?.Invoke();
    }

    void CreateHealthBar()
    {
        if (healthBarPrefab != null)
        {
            healthBarInstance = Instantiate(healthBarPrefab, transform);
            healthBarInstance.transform.localPosition = healthBarOffset;
            healthBarScript = healthBarInstance.GetComponent<HealthBar>();
        }
        else
        {
            // TODO create prefabs for Health
            CreateSimpleHealthBar();
        }
    }

    void CreateSimpleHealthBar()
    {
        GameObject healthBarObj = new GameObject("HealthBar");
        healthBarObj.transform.parent = transform;
        healthBarObj.transform.localPosition = healthBarOffset;
        healthBarObj.transform.localScale = Vector3.one;
        // Background
        GameObject background = new GameObject("Background");
        background.transform.parent = healthBarObj.transform;
        background.transform.localPosition = Vector3.zero;
        SpriteRenderer bgRenderer = background.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = CreateSimpleSprite(Color.red);
        bgRenderer.sortingOrder = 1;
        // Foreground
        GameObject foreground = new GameObject("Foreground");
        foreground.transform.parent = healthBarObj.transform;
        foreground.transform.localPosition = Vector3.zero;
        SpriteRenderer fgRenderer = foreground.AddComponent<SpriteRenderer>();
        fgRenderer.sprite = CreateSimpleSprite(Color.green);
        fgRenderer.sortingOrder = 2;
        // Add HealthBar script
        healthBarScript = healthBarObj.AddComponent<HealthBar>();
        healthBarScript.Initialize(bgRenderer, fgRenderer);
    }

    Sprite CreateSimpleSprite(Color color)
    {
        Texture2D texture = new Texture2D(32, 4);
        Color[] pixels = new Color[32 * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, 32, 4), new Vector2(0.5f, 0.5f), 100f);
    }

    public void  TakeDamage(float damage)
    {
        if (IsDead) return;

        currentHealth -= damage;
        currentHealth = Mathf.Max(0f, currentHealth);

        OnHealthChanged?.Invoke();
        
        if (IsDead)
        {
            UpdateHealthBar();
            Die();
        }
        else
        {
            UpdateHealthBar();
        }
    }

    public void Heal(float amount)
    {
        if (IsDead) return;

        currentHealth += amount;
        currentHealth = Mathf.Min(maxHealth, currentHealth);

        OnHealthChanged?.Invoke();
        UpdateHealthBar();
    }

    public void SetHealth(float newHealth)
    {
        currentHealth = Mathf.Clamp(newHealth, 0f, maxHealth);
        OnHealthChanged?.Invoke();
        UpdateHealthBar();
        
        if (IsDead)
        {
            Die();
        }
    }

    public void SetMaxHealth(float newMaxHealth)
    {
        float healthPercentage = HealthPercentage;
        maxHealth = newMaxHealth;
        currentHealth = maxHealth * healthPercentage;

        OnHealthChanged?.Invoke();
        UpdateHealthBar();
    }

    void UpdateHealthBar()
    {
        if (healthBarScript != null)
        {
            healthBarScript.UpdateHealthBar(HealthPercentage);
        }
    }

    void Die()
    {
        OnDeath?.Invoke();

        if (destroyOnDeath)
        {
            Destroy(gameObject, 0.05f);
        }
    }

    void OnDestroy()
    {
        if (healthBarInstance != null)
        {
            Destroy(healthBarInstance);
        }
    }
}