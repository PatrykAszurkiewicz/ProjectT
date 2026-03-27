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

    void Update()
    {
        // Safety net for the flamethrower and obstacle drawer
        if (isAttackButtonHeld && weapon != null)
        {
            bool mouseStillDown = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (!mouseStillDown)
            {
                isAttackButtonHeld = false;
                weapon.StopAttack();
            }
        }
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
                isAttackButtonHeld = true;
                weapon.PerformAttack();
            }
            else if (context.canceled)
            {
                isAttackButtonHeld = false;
                weapon.StopAttack();
            }
        }
        // Handle flamethrower - hold to fire, release to stop
        else if (weaponData != null && weaponData.isFlamethrower)
        {
            if (context.started)
            {
                isAttackButtonHeld = true;
                weapon.PerformAttack();
            }
            else if (context.canceled)
            {
                isAttackButtonHeld = false;
                weapon.StopAttack();
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
        bool isRangedAttack = weapon.GetWeaponData().isRanged;

        if (playerMovement != null)
        {
            if (isRangedAttack)
                playerMovement.StartRangedAttack();
            else
                playerMovement.StartMeleeAttack();
        }

        weapon.PerformAttack();

        yield return new WaitForSeconds(attackAnimationDuration);

        if (playerMovement != null)
        {
            if (isRangedAttack)
                playerMovement.EndRangedAttack();
            else
                playerMovement.EndMeleeAttack();
        }
    }
}
