using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;


// Shared guard used when a paused, mouse/cursor-driven menu (stage reward screen,
// augment menus, etc.) hands control back to gameplay.
// Gamepad players confirm menu choices with the RIGHT TRIGGER (see
// GamepadMenuCursor). That is the same control bound to the in-game
// AttackWeapon/Build action. While a menu is open the attack handler is suppressed
// (PlayerAttack.SetAllSuppressed(true)), so the trigger pull that confirms a choice
// is swallowed. If gameplay then resumes while the trigger is STILL HELD, the
// weapon action cannot produce a new press: Unity Button actions skip the "initial
// state check", so a control that is already held at the moment input resumes must
// be released and pressed again before it fires. That is exactly why the gamepad
// weapon trigger looked dead after a stage — and why "doing something" (which
// happened to release the trigger) appeared to restore it.

public static class MenuInputGuard
{
    // Wait (unscaled — works while Time.timeScale == 0) until no gamepad is holding
    // a trigger, or until `timeout` seconds pass so a stuck/held control can never
    // hang the game. Returns immediately if there is no gamepad or none is held.
    public static IEnumerator WaitForGamepadTriggersReleased(float timeout = 2f)
    {
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (!AnyGamepadTriggerHeld()) yield break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static bool AnyGamepadTriggerHeld()
    {
        var pads = Gamepad.all;
        for (int i = 0; i < pads.Count; i++)
        {
            var pad = pads[i];
            if (pad == null) continue;
            // Both triggers: rightTrigger = AttackWeapon/Build, leftTrigger = AttackTool.
            if (pad.rightTrigger.isPressed || pad.leftTrigger.isPressed) return true;
        }
        return false;
    }
}
