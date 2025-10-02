using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private Weapon weapon;
    [SerializeField] private float attackAnimationDuration = 0.3f; // Duration of attack animation

    private PlayerMovement playerMovement;
    private bool isAttackButtonHeld = false;

    void Start()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (weapon == null) return;

        WeaponData weaponData = weapon.GetWeaponData();

        // Handle obstacle drawer - hold and release mechanics
        if (weaponData != null && weaponData.isObstacleDrawer)
        {
            if (context.started)
            {
                // Button pressed - start drawing
                isAttackButtonHeld = true;
                weapon.PerformAttack(); // This starts the drawing
            }
            else if (context.canceled)
            {
                // Button released - stop drawing
                isAttackButtonHeld = false;
                weapon.StopAttack(); // This finalizes the obstacle
            }
        }
        else
        {
            // Regular weapons - single press
            if (context.performed)
            {
                StartCoroutine(PerformAttackWithAnimation());
            }
        }
    }

    private IEnumerator PerformAttackWithAnimation()
    {
        // Determine attack type based on weapon data
        bool isRangedAttack = weapon.GetWeaponData().isRanged;

        // Start appropriate animation
        if (playerMovement != null)
        {
            if (isRangedAttack)
            {
                playerMovement.StartRangedAttack();
            }
            else
            {
                playerMovement.StartMeleeAttack();
            }
        }

        weapon.PerformAttack();

        // Wait for animation duration
        yield return new WaitForSeconds(attackAnimationDuration);

        if (playerMovement != null)
        {
            if (isRangedAttack)
            {
                playerMovement.EndRangedAttack();
            }
            else
            {
                playerMovement.EndMeleeAttack();
            }
        }
    }
}