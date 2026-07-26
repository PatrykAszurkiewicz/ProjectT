using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Left-stick scrolling for the scroll area under the pad cursor.
//
// This now DEFERS to MenuNavigator. Both used to read the left stick and the dpad,
// so as soon as focus navigation existed, one press both stepped the selection and
// scrolled the panel. Rule: while the player is navigating by focus, MenuNavigator
// owns the left stick and scrolls the selection into view itself. This script only
// runs in pure cursor mode (right stick / mouse), where nothing is focused.
//
// Performance: FindObjectsByType<ScrollRect> ran EVERY FRAME the stick was off
// centre — an allocating full-scene scan, on a stick that rests slightly off zero
// on a worn pad. Now cached and refreshed at most a few times a second.
public class GamepadScrollRouter : MonoBehaviour
{
    [Tooltip("Scroll speed in CONTENT pixels per second at full stick deflection.")]
    public float scrollSpeed = 1500f;

    [Tooltip("Left-stick Y magnitude before scrolling starts.")]
    [Range(0f, 0.9f)] public float stickDeadzone = 0.2f;

    [Tooltip("Also scroll with the dpad up/down. OFF by default: the dpad now steps " +
             "between controls (MenuNavigator).")]
    public bool useDpad = false;

    [Tooltip("Only scroll while a menu is open (or the game is frozen).")]
    public bool onlyWhilePaused = true;

    [Tooltip("Seconds between rescans for ScrollRects. 0 = every frame (slow).")]
    public float rescanInterval = 0.3f;

    private ScrollRect[] _cache = new ScrollRect[0];
    private float _nextScan;

    private void Update()
    {
        // Was `Time.timeScale != 0f`, which left the stick dead for non-freezing
        // overlays AND for the whole main-menu scene. Ask the modal stack instead.
        if (onlyWhilePaused && !UIModalStack.MenuInputActive) return;

        // Focus navigation owns the left stick and the dpad while it's active.
        if (MenuNavigator.NavigationActive) return;

        var pad = Gamepad.current;
        var mouse = Mouse.current;
        if (pad == null || mouse == null) return;

        float input = 0f;
        Vector2 stick = pad.leftStick.ReadValue();
        if (Mathf.Abs(stick.y) > stickDeadzone) input += stick.y;
        if (useDpad)
        {
            if (pad.dpad.up.isPressed) input += 1f;
            if (pad.dpad.down.isPressed) input -= 1f;
        }
        if (Mathf.Approximately(input, 0f)) return;

        var sr = FindScrollRectUnder(mouse.position.ReadValue());
        if (sr == null || sr.content == null || sr.viewport == null) return;

        float hidden = sr.content.rect.height - sr.viewport.rect.height;
        if (hidden <= 1f) return; // content fits

        // verticalNormalizedPosition: 1 = top, 0 = bottom. Stick up reveals earlier
        // items (toward the top), so add the delta.
        float deltaNorm = (input * scrollSpeed * Time.unscaledDeltaTime) / hidden;
        sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition + deltaNorm);
    }

    /// Topmost active ScrollRect whose viewport contains the cursor.
    private ScrollRect FindScrollRectUnder(Vector2 screenPos)
    {
        if (Time.unscaledTime >= _nextScan)
        {
            _cache = FindObjectsByType<ScrollRect>(FindObjectsSortMode.None);
            _nextScan = Time.unscaledTime + Mathf.Max(0f, rescanInterval);
        }

        ScrollRect best = null;
        foreach (var sr in _cache)
        {
            if (sr == null || !sr.isActiveAndEnabled) continue;

            var vp = sr.viewport != null ? sr.viewport : sr.transform as RectTransform;
            if (vp == null || !vp.gameObject.activeInHierarchy) continue;

            Camera cam = GetCanvasCamera(sr);
            if (RectTransformUtility.RectangleContainsScreenPoint(vp, screenPos, cam))
                best = sr; // later match wins; panels don't overlap
        }
        return best;
    }

    private Camera GetCanvasCamera(Component c)
    {
        var canvas = c.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }
}

