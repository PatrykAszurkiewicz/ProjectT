using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class PauseMenuController : MonoBehaviour
{
    public GameObject pauseMenu;
    private bool activated;

    // Co-op: there can be one weapon-roll canvas per player, so we hide ALL of
    // them rather than a single GameObject.Find("WeaponRoll_Canvas").
    private readonly List<GameObject> _weaponRollCanvases = new List<GameObject>();

    // Phase 1: Pause is now a rebindable action ("Pause" in the Player map,
    // Esc / gamepad Start by default). We subscribe to it on EVERY player so
    // either co-op player can pause. The raw keyboard/gamepad poll below is kept
    // ONLY as a fallback for a setup with no Pause action / no PlayerInput, and is
    // skipped the moment at least one action subscription is live (so pause can
    // never double-toggle).
    private readonly List<InputAction> _pauseActions = new List<InputAction>();
    private bool _usingActionPause;

    private void Awake()
    {
        pauseMenu.SetActive(false);
        activated = false;
    }

    private void OnEnable()
    {
        PlayerRegistry.OnPlayerJoined += OnPlayerJoined;
        PlayerRegistry.OnPlayerLeft += OnPlayerLeft;
        foreach (var pr in PlayerRegistry.Instance.All) HookPause(pr);
    }

    private void OnDisable()
    {
        PlayerRegistry.OnPlayerJoined -= OnPlayerJoined;
        PlayerRegistry.OnPlayerLeft -= OnPlayerLeft;
        for (int i = 0; i < _pauseActions.Count; i++)
            if (_pauseActions[i] != null) _pauseActions[i].performed -= OnPausePerformed;
        _pauseActions.Clear();
        _usingActionPause = false;
    }

    private void OnPlayerJoined(PlayerRef pr) => HookPause(pr);
    private void OnPlayerLeft(PlayerRef pr) => UnhookPause(pr);

    private void HookPause(PlayerRef pr)
    {
        if (pr == null) return;
        var pi = pr.GetComponent<PlayerInput>();
        if (pi == null || pi.actions == null) return;

        var a = pi.actions.FindAction("Pause", false);
        if (a == null || _pauseActions.Contains(a)) return;

        a.performed += OnPausePerformed;
        _pauseActions.Add(a);
        _usingActionPause = true;
    }

    private void UnhookPause(PlayerRef pr)
    {
        if (pr == null) return;
        var pi = pr.GetComponent<PlayerInput>();
        if (pi == null || pi.actions == null) return;

        var a = pi.actions.FindAction("Pause", false);
        if (a == null) return;

        a.performed -= OnPausePerformed;
        _pauseActions.Remove(a);
        if (_pauseActions.Count == 0) _usingActionPause = false;
    }

    private void OnPausePerformed(InputAction.CallbackContext _) => ActivatePauseMenu();

    private void Update()
    {
        // Fallback ONLY when no Pause action is hooked (legacy / pre-spawn frames).
        if (_usingActionPause) return;

        // Start (Menu) button opens AND closes the pause menu. Input polling
        // runs regardless of Time.timeScale, so this still un-pauses at 0.
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
            ActivatePauseMenu();

        // Esc on the keyboard toggles the pause menu too.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            ActivatePauseMenu();
    }

    public void ActivatePauseMenu()
    {
        if (activated == false)
        {
            activated = true;
            Time.timeScale = 0f;
            Cursor.visible = true;
            pauseMenu.SetActive(true);

            // Collect the weapon-roll canvases WHILE THEY ARE STILL ACTIVE, then
            // hide them. We reuse this same list on resume — re-collecting on
            // resume would miss them (they're inactive) and they'd never come
            // back. The WeaponRollUI builds one canvas per player at runtime.
            CollectWeaponRollCanvases();
            SetWeaponRollCanvasesActive(false);
            PlayerAttack.SetAllSuppressed(true);

            // Kill any in-flight shake so the menu doesn't wobble.
            CombatJuice.StopAllShake();
        }
        else
        {
            activated = false;
            Time.timeScale = 1f;
            Cursor.visible = false;
            pauseMenu.SetActive(false);
            SetWeaponRollCanvasesActive(true);
            PlayerAttack.SetAllSuppressed(false);
        }
    }

    // Find every weapon-roll canvas in the scene. Phase 3 gives each player's
    // canvas a unique name (e.g. "WeaponRoll_Canvas_P0"), so we match by prefix.
    private void CollectWeaponRollCanvases()
    {
        _weaponRollCanvases.Clear();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.parent != null) continue; // canvas roots only
            if (t.name.StartsWith("WeaponRoll_Canvas"))
                _weaponRollCanvases.Add(t.gameObject);
        }
    }

    private void SetWeaponRollCanvasesActive(bool active)
    {
        for (int i = 0; i < _weaponRollCanvases.Count; i++)
            if (_weaponRollCanvases[i] != null)
                _weaponRollCanvases[i].SetActive(active);
    }
}
