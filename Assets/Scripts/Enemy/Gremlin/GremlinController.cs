using UnityEngine;
using System.Collections;

public class GremlinController : MonoBehaviour, IDamageable
{
    [Header("Gremlin Settings")]
    public float fleeRange = 4f;
    public float playerSpeedPercent = 0.7f;
    public int energyDropCount = 3;
    public int energyDropValue = 10;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Transform playerTransform;
    private bool isFleeingFromPlayer = false;
    private bool isDead = false;
    private float moveSpeed;

    private Sprite[] gremlinSprites;
    private Coroutine currentAnimationCoroutine;
    private bool isRunning = false;

    void Awake()
    {
        SetupComponents();
        FindPlayer();
        CalculateMoveSpeed();
    }

    void Start()
    {
        LoadSprites();
        SetupGremlinProperties();
        StartIdleAnimation();
        EnsureVisibility();
    }


    void SetupComponents()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.gravityScale = 0f;
        rb.linearDamping = 5f;
        rb.freezeRotation = true;

        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = 15;

        gameObject.tag = "Enemy";
        gameObject.layer = LayerMask.NameToLayer("Enemy");
    }

    void FindPlayer()
    {
        var playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null)
        {
            playerTransform = playerMovement.transform;
        }
        else
        {
            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                playerTransform = playerObject.transform;
            }
        }
    }

    void CalculateMoveSpeed()
    {
        if (playerTransform != null)
        {
            var playerStats = playerTransform.GetComponent<PlayerStats>();
            if (playerStats != null)
            {
                moveSpeed = playerStats.moveSpeed * playerSpeedPercent;
                return;
            }
        }
        moveSpeed = 3.5f;
    }

    void LoadSprites()
    {
        // TODO move hard coded path to some orchestrator or Prefab
        gremlinSprites = Resources.LoadAll<Sprite>("Sprites/EnemySprites/gremlin");
        if (gremlinSprites != null && gremlinSprites.Length >= 9)
        {
            spriteRenderer.sprite = gremlinSprites[0];
        }
        else
        {
            spriteRenderer.sprite = CreateFallbackSprite();
            spriteRenderer.color = Color.green;
        }
    }
    void SetupGremlinProperties()
    {
        var grapplingTarget = GetComponent<GremlinGrapplingTarget>();
        if (grapplingTarget == null)
        {
            grapplingTarget = gameObject.AddComponent<GremlinGrapplingTarget>();
        }
        grapplingTarget.gremlinController = this;

        var enemyStats = GetComponent<EnemyStats>();
        if (enemyStats == null)
        {
            enemyStats = gameObject.AddComponent<EnemyStats>();
        }

        var enemyData = ScriptableObject.CreateInstance<EnemyData>();
        enemyData.enemyName = "Gremlin";
        enemyData.maxHealth = 1f;
        enemyData.moveSpeed = moveSpeed;
        enemyData.mass = 5f;

        enemyStats.enemyData = enemyData;
        enemyStats.maxHealth = 1f;
        enemyStats.currentHealth = 1f;
        enemyStats.canDropEnergy = true;

        enemyStats.energyDropChance = 1f; // 100% drop chance
        enemyStats.energyDropValue = energyDropValue;
    }

    void EnsureVisibility()
    {
        if (spriteRenderer.sprite == null)
        {
            spriteRenderer.sprite = CreateFallbackSprite();
            spriteRenderer.color = Color.red;
        }
        Color currentColor = spriteRenderer.color;
        currentColor.a = 1f;
        spriteRenderer.color = currentColor;
        spriteRenderer.sortingOrder = 100;
    }

    void Update()
    {
        if (isDead) return;

        if (playerTransform == null)
        {
            FindPlayer();
            if (playerTransform == null)
            {
                if (isFleeingFromPlayer)
                {
                    isFleeingFromPlayer = false;
                    UpdateAnimation();
                }
                return;
            }
        }

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        bool shouldFlee = distanceToPlayer <= fleeRange;

        if (shouldFlee != isFleeingFromPlayer)
        {
            isFleeingFromPlayer = shouldFlee;
            UpdateAnimation();
        }
    }

    void FixedUpdate()
    {
        if (isDead) return;

        if (isFleeingFromPlayer && playerTransform != null)
        {
            Vector3 fleeDirection = (transform.position - playerTransform.position).normalized;
            rb.linearVelocity = fleeDirection * moveSpeed;
        }
        else
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 3f);
        }
    }

    void UpdateAnimation()
    {
        if (gremlinSprites == null || gremlinSprites.Length < 9) return;

        if (isFleeingFromPlayer && !isRunning)
        {
            isRunning = true;
            StartRunningAnimation();
        }
        else if (!isFleeingFromPlayer && isRunning)
        {
            isRunning = false;
            StartIdleAnimation();
        }
    }

    void StartIdleAnimation()
    {
        if (currentAnimationCoroutine != null) StopCoroutine(currentAnimationCoroutine);
        if (gremlinSprites != null && gremlinSprites.Length >= 3)
        {
            currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(spriteRenderer, gremlinSprites, true, 3, 0, 0.2f));
        }
    }

    void StartRunningAnimation()
    {
        if (currentAnimationCoroutine != null) StopCoroutine(currentAnimationCoroutine);
        if (gremlinSprites != null && gremlinSprites.Length >= 6)
        {
            currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(spriteRenderer, gremlinSprites, true, 3, 3, 0.15f));
        }
    }

    void StartDisintegratingAnimation()
    {
        if (currentAnimationCoroutine != null) StopCoroutine(currentAnimationCoroutine);
        if (gremlinSprites != null && gremlinSprites.Length >= 9)
        {
            currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(spriteRenderer, gremlinSprites, false, 3, 6, 0.1f));
        }
    }

    Sprite CreateFallbackSprite()
    {
        int size = 32;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                colors[y * size + x] = distance <= radius ? new Color(0.2f, 0.8f, 0.2f, 1f) : Color.clear;
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (isDead) return false;
        Die(damageSource);
        return true;
    }

    public bool CanTakeDamage() => !isDead;
    public float GetCurrentHealth() => isDead ? 0f : 1f;
    public float GetMaxHealth() => 1f;
    public float GetHealthPercentage() => isDead ? 0f : 1f;
    public bool IsDestroyed() => isDead;

    public void Die(GameObject killer = null)
    {
        if (isDead) return;
        isDead = true;
        PlayDeathSound();

        rb.linearVelocity = Vector2.zero;
        rb.simulated = false;
        GetComponent<Collider2D>().enabled = false;

        StartCoroutine(DeathSequence());

        if (EnergyManager.Instance != null)
            EnergyManager.Instance.OnEnemyKilled(gameObject);

        //var waveSpawner = FindFirstObjectByType<WaveSpawner>();
        //if (waveSpawner != null)
        //    waveSpawner.OnEnemyDeath();
    }

    private void PlayDeathSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.gremlinDeath, transform.position);
        }
    }

    IEnumerator DeathSequence()
    {
        StartDisintegratingAnimation();
        yield return new WaitForSeconds(0.3f);

        for (int i = 0; i < energyDropCount; i++)
        {
            Vector3 spawnPos = transform.position + (Vector3)Random.insideUnitCircle * 0.5f;
            EnergyDropManager.TrySpawnEnergyDrop(spawnPos, 1f, energyDropValue);
        }

        float fadeTime = 0.5f;
        float elapsed = 0f;
        Color startColor = spriteRenderer.color;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            spriteRenderer.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }

        Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isDead) return;
        if (IsPlayerAttack(other)) Die(other.gameObject);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;
        if (IsPlayerAttack(collision.collider)) Die(collision.gameObject);
    }

    bool IsPlayerAttack(Collider2D other)
    {
        return other.CompareTag("Player") || other.GetComponent<PlayerMovement>() ||
               other.GetComponent<Weapon>() || other.GetComponent<Projectile>() ||
               other.GetComponent<WeaponProjectile>();
    }
}

public class GremlinGrapplingTarget : MonoBehaviour, IGrapplingTarget
{
    [System.NonSerialized] public GremlinController gremlinController;
    private bool isDestroyed = false;

    void Awake()
    {
        gremlinController = GetComponent<GremlinController>();
    }

    void Update()
    {
        if (!isDestroyed && gremlinController != null && !gremlinController.IsDestroyed())
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude > 15f)
            {
                var enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
                bool otherEnemiesMovingFast = false;
                foreach (var enemy in enemies)
                {
                    if (enemy != null && enemy.gameObject != gameObject)
                    {
                        var enemyRb = enemy.GetComponent<Rigidbody2D>();
                        if (enemyRb != null && enemyRb.linearVelocity.magnitude > 3f)
                        {
                            otherEnemiesMovingFast = true;
                            break;
                        }
                    }
                }

                if (!otherEnemiesMovingFast)
                {
                    ForceImmediateDeath();
                }
            }
        }
    }

    public bool CanBeGrappled() => !isDestroyed && gremlinController != null && !gremlinController.IsDestroyed();
    public Vector3 GetGrapplePoint() => isDestroyed ? Vector3.zero : transform.position;
    public bool IsSolidTarget() => false;
    public Transform GetTransform() => isDestroyed ? null : transform;

    public void OnGrappleHit(object hook)
    {
        if (!isDestroyed) ForceImmediateDeath();
    }

    public void OnGrappleRelease() { }

    public void ApplyGrapplePull(Vector3 direction, float force)
    {
        if (!isDestroyed) ForceImmediateDeath();
    }

    void ForceImmediateDeath()
    {
        if (isDestroyed) return;
        isDestroyed = true;

        gremlinController?.Die(gameObject);

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }

        var colliders = GetComponents<Collider2D>();
        foreach (var col in colliders) col.enabled = false;

        enabled = false;
    }
}