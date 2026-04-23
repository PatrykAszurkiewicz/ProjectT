using System;
using System.Collections;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using FMOD.Studio;

public class PlayerMovement : MonoBehaviour
{
    PlayerStats pstats;
    Vector2 move;
    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;

    bool isSprinting = false;

    private bool isDashing = false;
    private Vector2 dashDirection;
    private float dashTimer;
    private float lastDashTime = -Mathf.Infinity;
    private float dashRegenTimer = 0f;
    public float dashStaminaCost = 1f;

    private EventInstance footsteps;

    // Sprite Animation Variables
    [Header("Sprite Animation")]
    [SerializeField] private Sprite[] playerSprites; // Fallback Sprite
    public float animationSpeed = 0.1f;
    [SerializeField] private float playerScale = 0.5f; // Configurable scale

    [Header("Y-Sort")]
    [Tooltip("Must match GrassCartoonOverlay.sortPrecision")]
    public float sortPrecision = 10f;

    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase")]
    public int sortOrderBase = 1000;

    [Tooltip("Y offset for the sort point relative to transform center. " +
             "Negative = sort from lower on the sprite (feet). " +
             "Adjust so the front/behind transition happens at the player's feet.")]
    public float sortYOffset = -0.3f;

    // Animation state tracking
    private enum AnimationState
    {
        Idle,
        MeleeAttack,
        RangedAttack,
        Dying
    }

    //private AnimationState currentAnimationState = AnimationState.Idle;
    private AnimationState currentAnimationState = (AnimationState)(-1); // force first animation to play

    private Coroutine currentAnimationCoroutine;
    private bool isMeleeAttacking = false;
    private bool isRangedAttacking = false;
    private bool isDying = false;

    // Animation frame indices (3 frames per each state)
    private const int IDLE_START_FRAME = 0;
    private const int MELEE_ATTACK_START_FRAME = 3;
    private const int DYING_START_FRAME = 6;
    private const int RANGED_ATTACK_START_FRAME = 9;
    private const int FRAMES_PER_ANIMATION = 3;

    void Start()
    {
        footsteps = AudioManager.instance.CreateInstance(FMODEvents.instance.footstepsSound);

        pstats = GetComponent<PlayerStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        pstats.dashesLeft = pstats.maxDashes;
        dashStaminaCost = pstats.maxStamina / pstats.maxDashes;

        LoadPlayerSprites();

        if (playerSprites != null && playerSprites.Length > 0)
        {
            spriteRenderer.sprite = playerSprites[0];
        }

        // Scale the player
        transform.localScale = new Vector3(playerScale, playerScale, 1f);

        // Start the Idle animation
        //StartCoroutine(DelayedStartAnimation());
        PlayIdleAnimation();
    }

    private IEnumerator DelayedStartAnimation()
    {
        yield return null; // Wait one frame
        PlayIdleAnimation();
    }

    private void LoadPlayerSprites()
    {
        if (playerSprites != null && playerSprites.Length >= 12)
        {
            Debug.Log($"Using Inspector-assigned sprites: {playerSprites.Length} sprites");
            return;
        }

        //TODO move the sprite paths to the Battle Orchestrator when ready
        Sprite[] loadedSprites = Resources.LoadAll<Sprite>("Sprites/player/player_character_spritesheet4");

        if (loadedSprites != null && loadedSprites.Length >= 12)
        {
            playerSprites = loadedSprites;

            for (int i = 0; i < Mathf.Min(3, playerSprites.Length); i++)
            {
            }
        }
        else
        {
            Debug.LogError($"Failed to load player sprites. Found {(loadedSprites?.Length ?? 0)} sprites, expected at least 12.");
            Texture2D spritesheet = Resources.Load<Texture2D>("Sprites/player/player_character_spritesheet4");
            if (spritesheet != null)
            {
                Debug.LogWarning("Found texture but not sprites. Make sure the texture is set to 'Sprite (2D and UI)' and sliced properly.");
            }
        }
    }

    void Update()
    {
        RegenerateDash();
        UpdateAnimationState();
    }

    /// <summary>
    /// Update the player's sortingOrder based on Y position every frame.
    /// Same formula as GrassCartoonOverlay baked values:
    ///   sortingOrder = sortOrderBase + round(-(y + sortYOffset) * sortPrecision)
    /// This is 1 integer assignment per frame on 1 object — zero performance cost.
    /// </summary>
    void LateUpdate()
    {
        if (spriteRenderer != null)
        {
            float sortY = transform.position.y + sortYOffset;
            spriteRenderer.sortingOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);
        }
    }

    private void UpdateAnimationState()
    {
        if (isDying)
            return;

        // Priority: Ranged > Melee > Idle
        if (isRangedAttacking && currentAnimationState != AnimationState.RangedAttack)
        {
            PlayRangedAttackAnimation();
        }
        else if (isMeleeAttacking && currentAnimationState != AnimationState.MeleeAttack)
        {
            PlayMeleeAttackAnimation();
        }
        else if (!isMeleeAttacking && !isRangedAttacking && currentAnimationState != AnimationState.Idle)
        {
            PlayIdleAnimation();
        }
    }

    private void PlayIdleAnimation()
    {
        if (playerSprites == null || currentAnimationState == AnimationState.Idle)
            return;

        currentAnimationState = AnimationState.Idle;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            IDLE_START_FRAME,
            animationSpeed
        ));
    }

    private void PlayMeleeAttackAnimation()
    {
        if (playerSprites == null || currentAnimationState == AnimationState.MeleeAttack)
            return;

        currentAnimationState = AnimationState.MeleeAttack;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            MELEE_ATTACK_START_FRAME,
            animationSpeed
        ));
    }

    private void PlayRangedAttackAnimation()
    {
        if (playerSprites == null || currentAnimationState == AnimationState.RangedAttack)
            return;

        currentAnimationState = AnimationState.RangedAttack;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            RANGED_ATTACK_START_FRAME,
            animationSpeed
        ));
    }

    public void PlayDeathAnimation()
    {
        if (playerSprites == null || currentAnimationState == AnimationState.Dying)
            return;

        isDying = true;
        currentAnimationState = AnimationState.Dying;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            DYING_START_FRAME,
            animationSpeed
        ));
    }

    public void StartMeleeAttack()
    {
        isMeleeAttacking = true;
    }

    public void EndMeleeAttack()
    {
        isMeleeAttacking = false;
    }

    public void StartRangedAttack()
    {
        isRangedAttacking = true;
    }

    public void EndRangedAttack()
    {
        isRangedAttacking = false;
    }

    // TODO Review and remove legacy methods for backward compatibility
    public void StartAttack()
    {
        // Handled in PlayerAttack script
    }

    public void EndAttack()
    {
        // Handled PlayerAttack script
    }

    private void FixedUpdate()
    {
        if (isDashing)
        {
            rb.MovePosition(rb.position + dashDirection * pstats.dashSpeed * Time.fixedDeltaTime);
            dashTimer -= Time.fixedDeltaTime;
            if (dashTimer <= 0f)
            {
                isDashing = false;
            }
            return;
        }

        float currentSpeed = pstats.moveSpeed;

        bool isTryingToSprint = isSprinting && move.magnitude > 0.01f && pstats.currentStamina > 0;

        if (isTryingToSprint && pstats.currentStamina > 0)
        {
            currentSpeed *= pstats.sprintMultiplier;
            pstats.currentStamina -= pstats.staminaDrainRate * Time.fixedDeltaTime;
            pstats.currentStamina = Mathf.Max(pstats.currentStamina, 0f);
        }
        else
        {
            pstats.currentStamina += pstats.staminaRegenRate * Time.fixedDeltaTime;
            pstats.currentStamina = Mathf.Min(pstats.currentStamina, pstats.maxStamina);
        }

        Vector2 movement = move.normalized * currentSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);

        UpdateSound();
    }

    public void Move(InputAction.CallbackContext context)
    {
        move = context.ReadValue<Vector2>();
    }

    public void Sprint(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            if (context.control is KeyControl key && key.wasPressedThisFrame)
            {
                TryDash();
            }
        }
        isSprinting = context.ReadValueAsButton();
    }

    private void TryDash()
    {
        // Guard against calls before everything is initialized
        if (rb == null || pstats == null)
            return;

        if (!isDashing && pstats.dashesLeft > 0 && Time.time - lastDashTime >= pstats.dashCooldown)
        {
            if (AudioManager.instance != null && FMODEvents.instance != null)
                AudioManager.instance.PlayOneShot(FMODEvents.instance.dashSound, rb.position);

            dashDirection = move.normalized;
            if (dashDirection == Vector2.zero)
                dashDirection = Vector2.up;

            isDashing = true;
            dashTimer = pstats.dashTime;
            lastDashTime = Time.time;
            pstats.dashesLeft--;

            pstats.currentStamina -= dashStaminaCost;
            pstats.currentStamina = Mathf.Max(pstats.currentStamina, 0f);
        }
    }

    private void RegenerateDash()
    {
        if (pstats.dashesLeft >= pstats.maxDashes)
            return;

        dashRegenTimer += Time.deltaTime;

        if (dashRegenTimer >= pstats.dashRegenRate && pstats.currentStamina > dashStaminaCost)
        {
            pstats.dashesLeft++;
            dashRegenTimer = 0f;
        }
    }

    private void UpdateSound()
    {
        if (move.magnitude > 0.01f)
        {
            // Fetch the playback state
            PLAYBACK_STATE playbackState;
            footsteps.getPlaybackState(out playbackState);
            if (playbackState.Equals(PLAYBACK_STATE.STOPPED))
            {
                footsteps.start();
            }
        }
        else
        {
            footsteps.stop(STOP_MODE.ALLOWFADEOUT);
        }
    }
}
