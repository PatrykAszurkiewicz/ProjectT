using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private Weapon weapon;
    [SerializeField] private float attackAnimationDuration = 0.3f; // Duration of attack animation

    private PlayerMovement playerMovement;

    void Start()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed && weapon != null)
        {
            StartCoroutine(PerformAttackWithAnimation());
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
        // End appropriate animation
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