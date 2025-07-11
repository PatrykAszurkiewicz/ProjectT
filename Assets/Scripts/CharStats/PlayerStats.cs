using UnityEngine;

public class PlayerStats : CharacterStats
{
    [Header("Mana")]

    public float maxMana = 50f;
    public float currentMana = 50f;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.5f;

    [Header("Dashing")]
    public float dashForce = 5f;
    public int maxDashes = 3;
    public float dashTime = 0.2f;
    public float dashSpeed = 20f;
    public float dashCooldown = 1f;
    public float dashRegenRate = 2f;
    public int dashesLeft = 2;

    [Header("Stamina")]
    public float maxStamina = 5f;
    public float staminaRegenRate = 1f;
    public float staminaDrainRate = 1.5f;
    public float currentStamina = 5f;

    public void UseMana(float amount)
    {
        currentMana -= amount;
        if (currentMana < 0) currentMana = 0;
    }

    public void RegenerateMana(float amount)
    {
        currentMana += amount;
        if (currentMana > maxMana) currentMana = maxMana;
    }
}
