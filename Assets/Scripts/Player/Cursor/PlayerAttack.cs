using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private Weapon weapon;
    [SerializeField] private float attackAnimationDuration = 0.3f;

    // Co-op: input suppression is now per-player. A shared global gate
    // (_globalSuppressed) still exists for things that must freeze EVERY player
    // at once — the pause menu — and new players inherit it on enable. Effective
    // suppression = global OR this instance's own flag.
    private static bool _globalSuppressed = false;
    private bool _instanceSuppressed = false;
    private bool Suppressed => _globalSuppressed || _instanceSuppressed;

    // Reset static gate between Play sessions (domain reload off).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _globalSuppressed = false; }

    /// <summary>Suppress/unsuppress THIS player's attack input.</summary>
    public void SetSuppressed(bool suppressed) { _instanceSuppressed = suppressed; }

    /// <summary>Suppress/unsuppress ALL players at once (used by the shared pause menu).</summary>
    public static void SetAllSuppressed(bool suppressed) { _globalSuppressed = suppressed; }

    /// <summary>
    /// Backward-compatible shim for old call sites that used the static property.
    /// Setting it routes to the global gate; getting reports the global gate.
    /// Prefer <see cref="SetAllSuppressed"/> in new code.
    /// </summary>
    public static bool InputSuppressed
    {
        get => _globalSuppressed;
        set => _globalSuppressed = value;
    }

    private PlayerMovement playerMovement;
    private PlayerInput playerInput;
    private bool isWeaponButtonHeld = false;
    private bool isToolButtonHeld = false;

    private int activeAttackCount = 0;
    private const int MAX_BUFFERED_ATTACKS = 2;

    // Analog triggers fire the action's "started" phase as soon as they leave rest
    // (~0.13), but ButtonControl.isPressed only reports true past the 0.5 press point.
    // The held-button safety nets below must use this lower analog threshold to match
    // where "started" raised the shield / began firing — otherwise a normal-speed
    // trigger pull gets lowered the same frame it was raised.
    private const float TRIGGER_HELD_THRESHOLD = 0.1f;

    void Start()
    {
        playerMovement = GetComponent<PlayerMovement>();
        // Co-op: read this player's own paired devices for the held-fire safety
        // nets, instead of polling the global Mouse.current / Gamepad.current.
        playerInput = GetComponent<PlayerInput>();
    }

    // Is this player's WEAPON button (LMB / right trigger) still physically down?
    // Reads THIS player's paired devices; falls back to the global devices if
    // there's no PlayerInput (legacy single-player object).
    private bool WeaponButtonStillDown()
    {
        if (playerInput != null && playerInput.devices.Count > 0)
        {
            foreach (var d in playerInput.devices)
            {
                if (d is Mouse m && m.leftButton.isPressed) return true;
                if (d is Gamepad g && g.rightTrigger.ReadValue() > TRIGGER_HELD_THRESHOLD) return true;
            }
            return false;
        }
        return (Mouse.current != null && Mouse.current.leftButton.isPressed)
            || (Gamepad.current != null && Gamepad.current.rightTrigger.ReadValue() > TRIGGER_HELD_THRESHOLD);
    }

    // Is this player's TOOL button (RMB / left trigger) still physically down?
    private bool ToolButtonStillDown()
    {
        if (playerInput != null && playerInput.devices.Count > 0)
        {
            foreach (var d in playerInput.devices)
            {
                if (d is Mouse m && m.rightButton.isPressed) return true;
                if (d is Gamepad g && g.leftTrigger.ReadValue() > TRIGGER_HELD_THRESHOLD) return true;
            }
            return false;
        }
        return (Mouse.current != null && Mouse.current.rightButton.isPressed)
            || (Gamepad.current != null && Gamepad.current.leftTrigger.ReadValue() > TRIGGER_HELD_THRESHOLD);
    }

    void Update()
    {
        if (weapon == null) return;

        // Safety net for held-down weapon attacks (flamethrower / hammer-charge
        // on left click) — if the button comes up without a 'canceled' event.
        if (isWeaponButtonHeld)
        {
            if (!WeaponButtonStillDown())
            {
                isWeaponButtonHeld = false;
                WeaponData heldWeaponData = weapon.GetWeaponData();
                if (heldWeaponData != null && heldWeaponData.isHammer)
                    weapon.ReleaseHammerCharge(); // release a charged slam
                else
                    weapon.StopAttack();          // stop the flamethrower
            }
        }

        // Safety net for held-down tool attacks (obstacle drawer / shield on right click)
        if (isToolButtonHeld)
        {
            if (!ToolButtonStillDown())
            {
                isToolButtonHeld = false;
                weapon.LowerShield(); // no-op if shield isn't active
                weapon.StopToolAttack();
                weapon.OnToolButtonReleased();
            }
        }
    }


    // Wire these to TWO separate input actions in your .inputactions asset:
    //   "AttackWeapon" -> Left Mouse Button  + Gamepad Right Trigger
    //   "AttackTool"   -> Right Mouse Button + Gamepad Left Trigger
    // (No more parsing control paths to tell left/right apart.)
    public void OnAttackWeapon(InputAction.CallbackContext context)
    {
        if (weapon == null || Suppressed) return;
        HandleWeaponInput(context);
    }

    public void OnAttackTool(InputAction.CallbackContext context)
    {
        if (weapon == null || Suppressed) return;
        HandleToolInput(context);
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
        // Battle Hammer — hold to charge, release to slam.
        else if (weaponData != null && weaponData.isHammer)
        {
            if (context.started)
            {
                isWeaponButtonHeld = true;
                // Begins the charge (or, if charging is disabled on the data,
                // fires an immediate slam — see Weapon.PerformAttack).
                weapon.PerformAttack();
            }
            else if (context.canceled)
            {
                isWeaponButtonHeld = false;
                // Releases the charged slam. No-op if no charge is in progress.
                weapon.ReleaseHammerCharge();
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

