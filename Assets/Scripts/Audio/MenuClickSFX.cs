using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// GLOBAL MENU CLICK SFX
// Plays FMODEvents.menuClick whenever the player clicks an interactable UI control
// (button / slider / toggle / etc.) in ANY menu — main menu, options, pause,
// augment reward, control rebind, lore, co-op lobby, and so on. It spawns itself
// once and persists across scene loads, so no menu needs to wire anything.
// Global poller instead of per-button listeners: menus in this project are
// built at runtime from many different scripts (MenuTheme buttons, raw UI Buttons,
// sliders), so hooking each one would be fragile and easy to forget. Detecting the
// click at the EventSystem level catches all of them, including menus added later.
// Gamepad: menus are cursor-driven and GamepadMenuCursor turns the right trigger
// into a real queued mouse click (it warps the cursor onto the control), so this
// mouse-based detector covers gamepad "clicks" too. The single exception is the
// augment menu's DirectionalSwitch confirm, which deliberately suppresses that
// synthetic mouse click (GamepadMenuCursor.ClicksSuppressed) — AugmentsMenu plays
// the click itself on that path.

public class MenuClickSFX : MonoBehaviour
{
    private static MenuClickSFX _instance;
    private static readonly List<RaycastResult> _hits = new List<RaycastResult>(8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("MenuClickSFX");
        _instance = go.AddComponent<MenuClickSFX>();
        DontDestroyOnLoad(go);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (!ClickedThisFrame()) return;
        if (PointerOverInteractable(PointerScreenPos()))
            Play();
    }

    private static bool ClickedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.leftButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    private static Vector2 PointerScreenPos()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }

    // True if `screenPos` is over a UI Selectable that is currently interactable.
    private static bool PointerOverInteractable(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return false;

        var ped = new PointerEventData(es) { position = screenPos };
        _hits.Clear();
        es.RaycastAll(ped, _hits);

        for (int i = 0; i < _hits.Count; i++)
        {
            var go = _hits[i].gameObject;
            if (go == null) continue;

            // A control's raycast target may be a child graphic, so search parents.
            var sel = go.GetComponentInParent<Selectable>();
            if (sel != null && sel.isActiveAndEnabled && sel.interactable)
                return true;
        }
        return false;
    }

    // Fires the menu-click one-shot. Public so menu paths that bypass the cursor
    // (e.g. the augment DirectionalSwitch confirm) can trigger the same sound.
    public static void Play()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.menuClick.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.menuClick, Vector3.zero);
        }
    }
}
