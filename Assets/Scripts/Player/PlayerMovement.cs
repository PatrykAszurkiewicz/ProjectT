using System;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class PlayerMovement : MonoBehaviour
{
    PlayerStats pstats;
    Vector2 move;
    Rigidbody2D rb;

    bool isSprinting = false;

    private bool isDashing = false;
    private Vector2 dashDirection;
    private float dashTimer;
    private float lastDashTime = -Mathf.Infinity;
    private float dashRegenTimer = 0f;
    public float dashStaminaCost = 1f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        pstats = GetComponent<PlayerStats>();
        rb = GetComponent<Rigidbody2D>();

        pstats.dashesLeft = pstats.maxDashes;
        dashStaminaCost = pstats.maxStamina / pstats.maxDashes;
    }

    // Update is called once per frame
    void Update()
    {
        RegenerateDash();
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
        if (!isDashing && pstats.dashesLeft > 0 && Time.time - lastDashTime >= pstats.dashCooldown)
        {
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
}
