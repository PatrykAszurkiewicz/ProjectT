using UnityEngine;
using System.Collections;

public class EnemyAnimationController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private EnemyStats enemyStats;
    private EnemyData enemyData;
    private Sprite[] sprites;
    private Coroutine currentAnimationCoroutine;
    private bool isAttacking = false;
    private bool isDying = false;

    private enum AnimationState { Idle, Attack, LaserAttack, Death }
    private AnimationState currentState = AnimationState.Idle;
    private bool isLaserAttacking = false;

    // Explicit melee attack flag
    // Set by EnemyController when it starts/ends an attack cycle.

    private bool isMeleeAttacking = false;

    // When true, velocity-based attack auto-detection is permanently disabled. 
    private bool disableAutoAttackDetection = false;

    // Sprite orientation settings
    [Header("Sprite Orientation")]
    [SerializeField] private float maxRotationAngle = 20f;
    [SerializeField] private float orientationSmoothSpeed = 10f;

    private Quaternion targetRotation = Quaternion.identity;


    // MELEE ATTACK (called by EnemyController)
    /// Called by EnemyController.AttackCycle() to start the melee attack animation
    public void PlayMeleeAttackAnimation()
    {
        if (isDying || isLaserAttacking || isAnimationFrozen) return;
        isMeleeAttacking = true;

        if (currentState != AnimationState.Attack)
        {
            currentState = AnimationState.Attack;
            if (currentAnimationCoroutine != null)
                StopCoroutine(currentAnimationCoroutine);

            currentAnimationCoroutine = StartCoroutine(PlayMeleeAttackOnce());
        }
    }

    // Called by EnemyController when the attack cycle ends.
    public void StopMeleeAttackAnimation()
    {
        isMeleeAttacking = false;

    }

    private IEnumerator PlayMeleeAttackOnce()
    {
        // Play the full attack animation once
        for (int i = 0; i < enemyData.attack.frameCount; i++)
        {
            int frameIndex = enemyData.attack.startFrame + i;
            if (frameIndex < sprites.Length)
                spriteRenderer.sprite = sprites[frameIndex];
            yield return new WaitForSeconds(enemyData.animationSpeed);
        }

        // Hold on last frame until EnemyController calls StopMeleeAttackAnimation().
        while (isMeleeAttacking)
            yield return null;

        // Return to idle only after the flag is cleared
        currentState = AnimationState.Idle; // force reset so PlayIdleAnimation works
        PlayIdleAnimation();
    }


    // LASER ATTACK


    public void PlayLaserAttackAnimation()
    {
        if (currentState == AnimationState.LaserAttack || isDying) return;
        currentState = AnimationState.LaserAttack;
        isLaserAttacking = true;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(PlayLaserAttackCoroutine());
    }

    private IEnumerator PlayLaserAttackCoroutine()
    {
        for (int i = 0; i < enemyData.laserAttack.frameCount; i++)
        {
            int frameIndex = enemyData.laserAttack.startFrame + i;
            if (frameIndex < sprites.Length)
                spriteRenderer.sprite = sprites[frameIndex];
            yield return new WaitForSeconds(enemyData.animationSpeed);
        }

        while (isLaserAttacking)
            yield return null;

        PlayIdleAnimation();
    }

    public void StopLaserAttackAnimation()
    {
        isLaserAttacking = false;
    }

    public bool IsPlayingLaserAttack() => isLaserAttacking;
    public bool IsPlayingMeleeAttack() => isMeleeAttacking;

    //  Animation Freeze (used by ParryStunEffect) 
    private bool isAnimationFrozen = false;

    // Freezes the animation on the current frame. Stops all animation coroutines. The sprite stays on whatever frame it was showing when frozen.
    public void FreezeAnimation()
    {
        isAnimationFrozen = true;
        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
            currentAnimationCoroutine = null;
        }
    }

    // Unfreezes the animation, allowing it to resume. Returns to idle if not in an active attack state.

    public void UnfreezeAnimation()
    {
        if (!isAnimationFrozen) return;
        isAnimationFrozen = false;

        // Don't try to start coroutines on inactive/destroyed objects
        if (this == null || !gameObject.activeInHierarchy) return;

        // If no attack is active, return to idle
        if (!isMeleeAttacking && !isLaserAttacking)
        {
            currentState = AnimationState.Attack; // force reset so PlayIdleAnimation works
            PlayIdleAnimation();
        }
    }


    // LIFECYCLE


    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        enemyStats = GetComponent<EnemyStats>();

        if (enemyStats == null || enemyStats.enemyData == null)
        {
            Debug.LogError($"No EnemyStats or EnemyData on {gameObject.name}");
            enabled = false;
            return;
        }

        enemyData = enemyStats.enemyData;

        // Bosses must use explicit PlayMeleeAttackAnimation() 
        if (GetComponent<Boss1>() != null)
            disableAutoAttackDetection = true;

        if (string.IsNullOrEmpty(enemyData.spriteFolderPath))
        {
            enabled = false;
            return;
        }

        LoadSprites();

        if (sprites != null && sprites.Length > 0)
        {
            spriteRenderer.sprite = sprites[0];
            StartCoroutine(DelayedStartAnimation());
        }
        else
        {
            enabled = false;
        }
    }

    private void LoadSprites()
    {
        Sprite[] loadedSprites = Resources.LoadAll<Sprite>(enemyData.spriteFolderPath);

        if (loadedSprites == null || loadedSprites.Length == 0)
        {
            Debug.LogWarning($"[{gameObject.name}] LoadAll found 0 sprites. Trying individual file loading...");

            System.Collections.Generic.List<Sprite> spriteList = new System.Collections.Generic.List<Sprite>();
            for (int i = 0; i < 100; i++)
            {
                string spritePath = $"{enemyData.spriteFolderPath}/{i:D2}";
                Sprite sprite = Resources.Load<Sprite>(spritePath);

                if (sprite != null)
                {
                    spriteList.Add(sprite);
                    Debug.Log($"[{gameObject.name}] Loaded sprite: {spritePath}");
                }
                else
                {
                    if (i == 0)
                        Debug.LogError($"[{gameObject.name}] Could not load any sprites from {enemyData.spriteFolderPath}");
                    break;
                }
            }

            loadedSprites = spriteList.ToArray();
        }

        if (loadedSprites == null || loadedSprites.Length == 0)
        {
            Debug.LogError($"[{gameObject.name}] FAILED to load sprites from {enemyData.spriteFolderPath}");
            return;
        }

        System.Array.Sort(loadedSprites, (a, b) => a.name.CompareTo(b.name));
        sprites = loadedSprites;
    }

    private IEnumerator DelayedStartAnimation()
    {
        yield return null;
        PlayIdleAnimation();
    }



    void Update()
    {
        if (sprites == null || isDying) return;

        // When animation is frozen (parry stun), skip all updates
        if (isAnimationFrozen) return;

        // Update sprite orientation (skip during laser attack)
        if (!isLaserAttacking)
        {
            if (isMeleeAttacking)
                FaceAttackTarget();  // Face the target during melee attacks
            else
                UpdateSpriteOrientation();
        }
        else
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.identity,
                Time.deltaTime * orientationSmoothSpeed);

        // Don't auto-switch animation if laser or explicit melee is active
        if (isLaserAttacking || isMeleeAttacking) return;

        // Velocity-based fallback for non-boss enemies that don't use
        // explicit melee attack calls. Disabled for bosses — they use PlayMeleeAttackAnimation().
        if (disableAutoAttackDetection) return;

        bool shouldBeAttacking = IsEnemyAttacking();
        if (shouldBeAttacking != isAttacking)
        {
            isAttacking = shouldBeAttacking;
            if (isAttacking) PlayAttackAnimation();
            else PlayIdleAnimation();
        }
    }

    // During melee attacks, face the nearest valid target (player, tower, or core). This ensures the sprite doesn't face backwards while attacking.
    private void FaceAttackTarget()
    {
        if (spriteRenderer == null) return;

        // Find the most likely attack target
        Transform target = null;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            float dist = Vector2.Distance(transform.position, player.transform.position);
            if (dist < 5f) // reasonable melee range check
                target = player.transform;
        }

        if (target == null)
        {
            GameObject core = GameObject.FindGameObjectWithTag("Core");
            if (core != null)
                target = core.transform;
        }

        if (target != null)
        {
            float dx = target.position.x - transform.position.x;
            if (dx < -0.1f)
                spriteRenderer.flipX = true;
            else if (dx > 0.1f)
                spriteRenderer.flipX = false;
        }

        // Reset rotation during attack
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.identity,
            Time.deltaTime * orientationSmoothSpeed);
    }

    private void UpdateSpriteOrientation()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;

        Vector2 velocity = rb.linearVelocity;

        if (velocity.magnitude < 0.1f)
        {
            targetRotation = Quaternion.identity;
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,
                Time.deltaTime * orientationSmoothSpeed);
            return;
        }

        if (velocity.x < -0.1f)
            spriteRenderer.flipX = true;
        else if (velocity.x > 0.1f)
            spriteRenderer.flipX = false;

        float angle = 0f;
        if (Mathf.Abs(velocity.x) > 0.1f || Mathf.Abs(velocity.y) > 0.1f)
        {
            angle = Mathf.Atan2(velocity.y, Mathf.Abs(velocity.x)) * Mathf.Rad2Deg;
            angle = Mathf.Clamp(angle, -maxRotationAngle, maxRotationAngle);
            if (spriteRenderer.flipX)
                angle = -angle;
        }

        targetRotation = Quaternion.Euler(0, 0, angle);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,
            Time.deltaTime * orientationSmoothSpeed);
    }

    private bool IsEnemyAttacking()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        return rb != null && rb.linearVelocity.magnitude < 0.1f;
    }

    private void PlayIdleAnimation()
    {
        if (currentState == AnimationState.Idle || isDying || isAnimationFrozen) return;
        currentState = AnimationState.Idle;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer, sprites, true,
            enemyData.idle.frameCount,
            enemyData.idle.startFrame,
            enemyData.animationSpeed));
    }

    private void PlayAttackAnimation()
    {
        if (currentState == AnimationState.Attack || isDying || isAnimationFrozen) return;
        currentState = AnimationState.Attack;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer, sprites, true,
            enemyData.attack.frameCount,
            enemyData.attack.startFrame,
            enemyData.animationSpeed));
    }

    public void PlayDeathAnimation()
    {
        if (isDying) return;
        isDying = true;
        currentState = AnimationState.Death;
        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);
        transform.rotation = Quaternion.identity;
        StartCoroutine(PlayDeathAnimationCoroutine());
    }

    private IEnumerator PlayDeathAnimationCoroutine()
    {
        for (int i = 0; i < enemyData.death.frameCount; i++)
        {
            int frameIndex = enemyData.death.startFrame + i;
            if (frameIndex < sprites.Length)
                spriteRenderer.sprite = sprites[frameIndex];
            yield return new WaitForSeconds(enemyData.animationSpeed);
        }
    }
}
