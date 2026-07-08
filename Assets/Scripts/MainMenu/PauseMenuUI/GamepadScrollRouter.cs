using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;


// Lets a gamepad's LEFT stick (and optionally the
// dpad) scroll the scroll area the on-screen cursor is
// hovering. Pairs with GamepadMenuCursor, which drives that cursor with the
// RIGHT stick — so right stick moves/clicks, left stick scrolls.
// One component for the whole pause screen. No per-panel or per-player wiring:
// it finds the scroll rect under the cursor and nudges it. Works for the
// single-player grid AND the co-op P1/P2 augment columns, because each player
// drives the shared cursor over their own panel (same single-cursor model the
// pause menu already uses).


public class GamepadScrollRouter : MonoBehaviour
{
    [Tooltip("Scroll speed in CONTENT pixels per second at full stick deflection.")]
    public float scrollSpeed = 1500f;

    [Tooltip("Left-stick Y magnitude before scrolling starts.")]
    [Range(0f, 0.9f)] public float stickDeadzone = 0.2f;

    [Tooltip("Also scroll with the dpad up/down. Turn off if it clashes with " +
             "dpad menu navigation on a screen that's open at the same time.")]
    public bool useDpad = true;

    [Tooltip("Only scroll while the game is paused (Time.timeScale == 0).")]
    public bool onlyWhilePaused = true;

    private static readonly List<ScrollRect> _scratch = new List<ScrollRect>();

    private void Update()
    {
        if (onlyWhilePaused && Time.timeScale != 0f) return;

        var pad = Gamepad.current;          // same "active pad" model as the cursor
        var mouse = Mouse.current;
        if (pad == null || mouse == null) return;

        // Combine stick + dpad into a single -1..1 vertical input.
        float input = 0f;
        Vector2 stick = pad.leftStick.ReadValue();
        if (Mathf.Abs(stick.y) > stickDeadzone) input += stick.y;
        if (useDpad)
        {
            if (pad.dpad.up.isPressed) input += 1f;
            if (pad.dpad.down.isPressed) input -= 1f;
        }
        if (Mathf.Approximately(input, 0f)) return;

        Vector2 cursor = mouse.position.ReadValue();
        var sr = FindScrollRectUnder(cursor);
        if (sr == null || sr.content == null || sr.viewport == null) return;

        // Pixels of content hidden outside the viewport (how far we can scroll).
        float hidden = sr.content.rect.height - sr.viewport.rect.height;
        if (hidden <= 1f) return; // content fits — nothing to scroll

        // verticalNormalizedPosition: 1 = top, 0 = bottom. Stick up (positive y)
        // reveals earlier items (toward the top), so add the delta.
        float deltaNorm = (input * scrollSpeed * Time.unscaledDeltaTime) / hidden;
        sr.verticalNormalizedPosition =
            Mathf.Clamp01(sr.verticalNormalizedPosition + deltaNorm);
    }


    /// Topmost active ScrollRect whose viewport contains the cursor. Uses a
    /// rect test rather than the EventSystem so it has no extra dependencies.

    private ScrollRect FindScrollRectUnder(Vector2 screenPos)
    {
        var all = FindObjectsByType<ScrollRect>(FindObjectsSortMode.None);
        ScrollRect best = null;
        foreach (var sr in all)
        {
            if (sr == null || !sr.isActiveAndEnabled) continue;

            var vp = sr.viewport != null ? sr.viewport : sr.transform as RectTransform;
            if (vp == null || !vp.gameObject.activeInHierarchy) continue;

            Camera cam = GetCanvasCamera(sr);
            if (RectTransformUtility.RectangleContainsScreenPoint(vp, screenPos, cam))
                best = sr; // later match wins; fine since panels don't overlap
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
