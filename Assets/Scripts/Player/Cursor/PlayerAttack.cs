using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private Weapon weapon;
    [SerializeField] private float attackAnimationDuration = 0.3f;
    public static bool InputSuppressed { get; set; } = false;

    private PlayerMovement playerMovement;
    private bool isWeaponButtonHeld = false;
    private bool isToolButtonHeld = false;

    private int activeAttackCount = 0;
    private const int MAX_BUFFERED_ATTACKS = 2;

    void Start()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    void Update()
    {
        if (weapon == null) return;

        // Safety net for held-down weapon attacks (flamethrower on left click)
        if (isWeaponButtonHeld)
        {
            bool leftStillDown = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (!leftStillDown)
            {
                isWeaponButtonHeld = false;
                weapon.StopAttack();
            }
        }

        // Safety net for held-down tool attacks (obstacle drawer / shield on right click)
        if (isToolButtonHeld)
        {
            bool rightStillDown = Mouse.current != null && Mouse.current.rightButton.isPressed;
            if (!rightStillDown)
            {
                isToolButtonHeld = false;
                weapon.LowerShield(); // no-op if shield isn't active
                weapon.StopToolAttack();
                weapon.OnToolButtonReleased();
            }
        }
    }


    // Called by the existing Attack action which has both Left Button and Right Button bindings.
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (weapon == null || InputSuppressed) return;

        // Determine which mouse button triggered this callback
        bool isRightClick = false;
        if (context.control != null)
        {
            string controlPath = context.control.path;
            // path looks like "/Mouse/rightButton"
            isRightClick = controlPath.Contains("rightButton");
        }

        if (isRightClick)
            HandleToolInput(context);
        else
            HandleWeaponInput(context);
    }


    // Left-click: attack with currently equipped weapon.
    private void HandleWeaponInput(InputAction.CallbackContext context)
    {
        WeaponData weaponData = weapon.GetWeaponData();

        // Flamethrower — hold to fire, release to stop
        if (weaponData != null && weaponData.isFlamethrower)
        {
            if (context.started)
            {
                isWeaponButtonHeld = true;
                weapon.PerformAttack();
            }
            else if (context.canceled)
            {
                isWeaponButtonHeld = false;
                weapon.StopAttack();
            }
        }
        else
        {
            // Regular weapons (melee, ranged) — single press
            if (context.performed)
            {
                if (activeAttackCount < MAX_BUFFERED_ATTACKS)
                    StartCoroutine(PerformWeaponAttackWithAnimation());
            }
        }
    }


    /// Right-click: use currently equipped tool.
    private void HandleToolInput(InputAction.CallbackContext context)
    {
        if (!weapon.HasTool) return;

        WeaponData toolData = weapon.GetToolData();

        // Shield — hold to block, release to lower
        if (toolData != null && toolData.armorBonus > 0f)
        {
            if (context.started)
            {
                isToolButtonHeld = true;
                weapon.OnToolButtonPressed();
                weapon.RaiseShield();
            }
            else if (context.canceled)
            {
                isToolButtonHeld = false;
                weapon.LowerShield();
                weapon.OnToolButtonReleased();
            }
            return;
        }

        // Obstacle drawer — hold and release mechanics
        if (toolData != null && toolData.isObstacleDrawer)
        {
            if (context.started)
            {
                isToolButtonHeld = true;
                weapon.OnToolButtonPressed();
                weapon.PerformToolAttack();
            }
            else if (context.canceled)
            {
                isToolButtonHeld = false;
                weapon.StopToolAttack();
                weapon.OnToolButtonReleased();
            }
        }
        else
        {
            // Single-click tools (bomb launcher, trap, turret, decoy, grappling hook, shield)
            if (context.started)
            {
                weapon.OnToolButtonPressed();
            }

            if (context.performed)
            {
                weapon.PerformToolAttack();
            }

            if (context.canceled)
            {
                weapon.OnToolButtonReleased();
            }
        }
    }

    private IEnumerator PerformWeaponAttackWithAnimation()
    {
        WeaponData wd = weapon.GetWeaponData();
        bool isRangedAttack = wd != null && wd.isRanged;

        activeAttackCount++;

        if (playerMovement != null)
        {
            if (isRangedAttack)
                playerMovement.StartRangedAttack();
            else
                playerMovement.StartMeleeAttack();
        }

        weapon.PerformAttack();

        yield return new WaitForSeconds(attackAnimationDuration);

        activeAttackCount--;

        if (activeAttackCount <= 0)
        {
            activeAttackCount = 0;
            if (playerMovement != null)
            {
                if (isRangedAttack)
                    playerMovement.EndRangedAttack();
                else
                    playerMovement.EndMeleeAttack();
            }
        }
    }
}
