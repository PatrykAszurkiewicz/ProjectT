using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Shared guard used when a paused, cursor-driven menu (stage reward screen, augment
// menus, etc.) hands control back to gameplay.
//
// The problem it solves: gamepad players confirm menu choices with the same physical
// control that gameplay uses to attack (right trigger by default — see
// GamepadMenuCursor). While a menu is open the attack handler is suppressed
// (PlayerAttack.SetAllSuppressed(true)), so the pull that confirms a choice is
// swallowed. If gameplay then resumes while the control is STILL HELD, the weapon
// action cannot produce a new press: Unity Button actions skip the "initial state
// check", so a control already held when input resumes must be released and pressed
// again before it fires. That is why the gamepad weapon trigger looked dead after a
// stage, and why "doing something" (which happened to release the trigger) appeared
// to fix it.
//
// This used to test <Gamepad>/rightTrigger and <Gamepad>/leftTrigger literally, with a
// comment asserting "rightTrigger = AttackWeapon/Build, leftTrigger = AttackTool" —
// true only on defaults. Rebind AttackWeapon onto buttonSouth (a binding the asset
// already ships as an alternate) and the guard stopped waiting for the control that
// was actually held, resurrecting the exact bug it was written to prevent.
//
// Now it asks each live player's AttackWeapon/AttackTool actions which controls they
// resolve to (via PlayerAttack.ActionPhysicallyHeld — the same check PlayerAttack's own
// held-fire safety nets use, so the two can't drift apart), so it follows rebinds and is
// correct per-player in co-op.
public static class MenuInputGuard
{
    /// <summary>
    /// Wait (unscaled — works while Time.timeScale == 0) until no player is holding a
    /// control bound to an attack action, or until <paramref name="timeout"/> seconds
    /// pass so a stuck/held control can never hang the game. Returns immediately if
    /// nothing is held.
    /// </summary>
    public static IEnumerator WaitForAttackControlsReleased(float timeout = 2f)
    {
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (!AnyAttackControlHeld()) yield break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>Old name — kept so existing call sites keep compiling.</summary>
    public static IEnumerator WaitForGamepadTriggersReleased(float timeout = 2f)
        => WaitForAttackControlsReleased(timeout);

    /// <summary>
    /// True while ANY registered player is physically holding a control bound to
    /// AttackWeapon or AttackTool. Falls back to raw trigger polling only when there
    /// is no PlayerInput at all (a legacy scene, or a menu scene with no players).
    /// </summary>
    public static bool AnyAttackControlHeld()
    {
        bool sawPlayer = false;

        var players = PlayerInput.all;
        for (int i = 0; i < players.Count; i++)
        {
            var pi = players[i];
            if (pi == null || pi.actions == null) continue;
            sawPlayer = true;

            if (PlayerAttack.ActionPhysicallyHeld(PlayerAttack.FindAction(pi, "AttackWeapon"))) return true;
            if (PlayerAttack.ActionPhysicallyHeld(PlayerAttack.FindAction(pi, "AttackTool"))) return true;
            // Build shares AttackWeapon's controls (ControlRebindService mirrors the
            // overrides), so it needs no separate check — but cover it in case the
            // alias is ever unlinked.
            if (PlayerAttack.ActionPhysicallyHeld(PlayerAttack.FindAction(pi, "Build"))) return true;
        }

        return sawPlayer ? false : AnyGamepadTriggerHeld();
    }

    // Last-resort fallback: no PlayerInput exists to ask, so assume defaults.
    private static bool AnyGamepadTriggerHeld()
    {
        var pads = Gamepad.all;
        for (int i = 0; i < pads.Count; i++)
        {
            var pad = pads[i];
            if (pad == null) continue;
            if (pad.rightTrigger.ReadValue() > PlayerAttack.TRIGGER_HELD_THRESHOLD ||
                pad.leftTrigger.ReadValue() > PlayerAttack.TRIGGER_HELD_THRESHOLD) return true;
        }
        return false;
    }
}
