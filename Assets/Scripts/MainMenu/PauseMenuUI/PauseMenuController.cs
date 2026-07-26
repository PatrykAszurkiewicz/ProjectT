using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// PAUSE MENU — now a well-behaved member of UIModalStack.
//
// Fixed here:
//  1) It no longer owns Time.timeScale / Cursor.visible / PlayerAttack directly.
//     UIModalStack does. Two overlapping screens can therefore never restore a
//     stale value over each other (the "frozen game, no menu" freeze).
//  2) Esc / Start is claimed through MenuBackInput. While Options, the Tutorial,
//     the rebind screen, the augment menu or the disconnect guard sits ABOVE the
//     pause menu, Esc goes to that screen only — it can no longer toggle pause
//     underneath an open modal and desync everything.
//  3) `_usingActionPause` used to latch true forever. A downed co-op player's
//     PlayerInput is disabled (its Pause action goes with it); if that was the
//     only hooked player, pause became unreachable AND the raw fallback stayed
//     suppressed. It is now recomputed from the live, ENABLED actions each frame.
//  4) Two players' Pause actions can fire on the same frame (co-op, or the solo
//     player that has keyboard AND pad paired with a widened binding mask). A
//     frame guard stops the double-toggle that opened and instantly closed the menu.
public class PauseMenuController : MonoBehaviour
{
    public GameObject pauseMenu;
    private bool activated;

    // Co-op: one weapon-roll canvas per player, so hide ALL of them.
    private readonly List<GameObject> _weaponRollCanvases = new List<GameObject>();

    private readonly List<InputAction> _pauseActions = new List<InputAction>();
    private int _lastToggleFrame = -1;

    private void Awake()
    {
        if (pauseMenu != null) pauseMenu.SetActive(false);
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

        // Torn down (scene reload) while still on the stack? Don't strand the freeze.
        if (activated) { activated = false; UIModalStack.Pop(this); }
    }

    private void OnPlayerJoined(PlayerRef pr) => HookPause(pr);
    private void OnPlayerLeft(PlayerRef pr) => UnhookPause(pr);

    private void HookPause(PlayerRef pr)
    {
        if (pr == null) return;   // also catches a destroyed PlayerRef (Unity == null)
        var pi = pr.GetComponent<PlayerInput>();
        if (pi == null || pi.actions == null) return;

        var a = pi.actions.FindAction("Pause", false);
        if (a == null || _pauseActions.Contains(a)) return;

        a.performed += OnPausePerformed;
        _pauseActions.Add(a);
    }

    private void UnhookPause(PlayerRef pr)
    {
        // A player that LEFT is usually already destroyed, so GetComponent would throw.
        // AnyLivePauseAction()/OnDisable sweep the dead entries; just bail here.
        if (pr == null) return;
        var pi = pr.GetComponent<PlayerInput>();
        if (pi == null || pi.actions == null) return;

        var a = pi.actions.FindAction("Pause", false);
        if (a == null) return;

        a.performed -= OnPausePerformed;
        _pauseActions.Remove(a);
    }

    // A hooked action only counts while it is actually enabled. A downed player's
    // PlayerInput is disabled, taking its Pause action with it.
    private bool AnyLivePauseAction()
    {
        for (int i = _pauseActions.Count - 1; i >= 0; i--)
            if (_pauseActions[i] == null) _pauseActions.RemoveAt(i);   // destroyed player

        for (int i = 0; i < _pauseActions.Count; i++)
            if (_pauseActions[i].enabled) return true;
        return false;
    }

    private void OnPausePerformed(InputAction.CallbackContext _) => RequestToggle();

    private void Update()
    {
        // Fallback ONLY when no Pause action is live (legacy setup / pre-spawn
        // frames / every player's input temporarily disabled).
        if (AnyLivePauseAction()) return;

        // PausePressedThisFrame is Esc/Start only. The general back press also
        // includes gamepad B, which is a GAMEPLAY control (dodge / cancel) — polling
        // it here would make B open the pause menu.
        if (MenuBackInput.PausePressedThisFrame) RequestToggle();
    }

    /// <summary>
    /// Toggle the pause menu — but only if nothing is layered on top of it.
    /// Whether this arrives from an InputAction callback (which can run before or
    /// after Update) or from the fallback poll, the arbitration is identical, so
    /// script execution order can no longer decide who wins the key press.
    /// </summary>
    private void RequestToggle()
    {
        if (_lastToggleFrame == Time.frameCount) return;   // one toggle per press

        // Some other modal (Options, Tutorial, rebind, augment, disconnect guard)
        // is in front of us. It owns this press; we stay exactly as we are.
        if (!UIModalStack.IsTopOrEmpty(this)) return;

        // Claim the press so nobody else acts on the same frame.
        if (MenuBackInput.PressedThisFrame && !MenuBackInput.ConsumeBack(this, requireTop: false))
            return;

        _lastToggleFrame = Time.frameCount;
        ActivatePauseMenu();
    }

    /// <summary>Public so existing Button OnClick wiring keeps working.</summary>
    public void ActivatePauseMenu()
    {
        if (!activated)
        {
            activated = true;
            if (pauseMenu != null) pauseMenu.SetActive(true);

            // UIModalStack takes timeScale, cursor and attack suppression.
            UIModalStack.Push(this);

            // Collect the canvases WHILE THEY ARE STILL ACTIVE, then hide them.
            // Re-collecting on resume would miss them (they're inactive by then).
            CollectWeaponRollCanvases();
            SetWeaponRollCanvasesActive(false);

            CombatJuice.StopAllShake();   // no wobble under the menu
        }
        else
        {
            activated = false;
            if (pauseMenu != null) pauseMenu.SetActive(false);
            SetWeaponRollCanvasesActive(true);
            UIModalStack.Pop(this);
        }
    }

    /// <summary>Close the menu if it is open. Safe to call from anywhere.</summary>
    public void ClosePauseMenu()
    {
        if (activated) ActivatePauseMenu();
    }

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
