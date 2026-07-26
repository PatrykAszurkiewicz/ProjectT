using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.SceneManagement;
using TMPro;

// Mid-run controller-loss gate. A sibling of ContinueRunMenu: it uses the SAME
// MenuTheme look and the SAME "live-poll controllers, gate the primary button until
// they're seated" pattern — but where ContinueRunMenu gates BEFORE a run and acts by
// reloading the scene, this one runs DURING a run and resumes IN PLACE (so it owns the
// pause + re-pairs the returning device). That different lifecycle is why it's its own
// class rather than literally the same menu.
// FLOW
//   A seated pad player loses their gamepad → pause (Time.timeScale = 0 +
//     PlayerAttack.SetAllSuppressed) and show the overlay. The normal pause menu is
//     NOT used, so a Start press from the missing pad can't dismiss it.
//   The overlay live-polls, exactly like ContinueRunMenu's gate. "Resume Run" stays
//     disabled ("Waiting for controller…") until every stranded player has a pad again,
//     then the player presses it to un-pause when ready.
//   returning OR replacement pad is re-paired to the stranded player automatically.
//   Exit & Save Run leaves the wave-start autosave on disk and quits to the menu
//     (resume later via ContinueRunMenu). "Abandon Run" deletes the save first.
public class ControllerDisconnectGuard : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("Also guard single-player runs (pause if the one pad is lost).")]
    public bool enableInSinglePlayer = true;

    [Tooltip("Solo only: if true, losing the pad does NOT pause while keyboard+mouse " +
             "are still connected (silent fallback to KB+M). If false, losing the pad " +
             "always pauses until a controller returns.")]
    public bool allowKeyboardFallbackSolo = false;

    [Tooltip("Only engage while a run is in progress (a GameOrchestrator exists). Keeps " +
             "the guard inert in menu scenes if it happens to be present there.")]
    public bool requireActiveRun = true;

    [Tooltip("Scene loaded by 'Exit & Save Run' and 'Abandon Run'. Leave blank to use " +
             "the default below (matches PauseMenu.QuitToMainMenu).")]
    public string mainMenuScene = "";

    [Tooltip("Fallback menu scene used when 'Main Menu Scene' is left blank.")]
    public string defaultMenuScene = "MenuScene";

    public bool debugLog = true;

    // Players that have held a connected gamepad this session → "pad players".
    private readonly HashSet<PlayerInput> _everHadPad = new HashSet<PlayerInput>();

    private bool _engaged;
    private bool _committing;

    // UI (built once, mirrors ContinueRunMenu)
    private GameObject _root;
    private TextMeshProUGUI _summary, _status, _resumeLabel;
    private Button _resumeBtn;
    private Image _resumeImg;

    private void OnEnable() { InputSystem.onDeviceChange += OnDeviceChange; }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        // Never leave the game frozen if torn down mid-overlay (e.g. scene reload).
        if (_engaged)
        {
            _engaged = false;
            UIModalStack.Pop(this);
        }
        _everHadPad.Clear();
    }

    private void Update()
    {
        // Remember who currently holds a connected gamepad. Destroyed PlayerInputs
        // must be dropped, or a respawn/scene teardown leaves a ghost "pad player"
        // that can never have a pad again — permanently stranding the guard open.
        _everHadPad.RemoveWhere(pi => pi == null);

        var all = PlayerInput.all;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && HasConnectedGamepad(all[i])) _everHadPad.Add(all[i]);

        if (!_engaged)
        {
            if (AnyStranded()) Engage();
        }
        else
        {
            // Live gate, exactly like ContinueRunMenu.Update → RefreshGate.
            RefreshGate();
        }
    }

    //  Detection 

    private bool AnyStranded()
    {
        if (requireActiveRun && GameOrchestrator.Instance == null) return false;

        bool solo = PlayerInput.all.Count <= 1;
        if (solo && !enableInSinglePlayer) return false;

        var all = PlayerInput.all;
        for (int i = 0; i < all.Count; i++)
        {
            var pi = all[i];
            if (pi == null || !_everHadPad.Contains(pi)) continue;  // not a pad player → ignore
            if (HasConnectedGamepad(pi)) continue;                  // still has a pad → fine
            if (solo && allowKeyboardFallbackSolo && HasKeyboardMouse(pi)) continue;
            return true;
        }
        return false;
    }

    private int PadPlayerCount()
    {
        int n = 0;
        var all = PlayerInput.all;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && _everHadPad.Contains(all[i])) n++;
        return Mathf.Max(1, n);
    }

    private string WaitingSummary()
    {
        var sb = new StringBuilder();
        var all = PlayerInput.all;
        for (int i = 0; i < all.Count; i++)
        {
            var pi = all[i];
            if (pi == null || !_everHadPad.Contains(pi) || HasConnectedGamepad(pi)) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"Player {pi.playerIndex + 1}");
        }
        if (sb.Length == 0) return "Controller disconnected.";
        return PlayerInput.all.Count > 1
            ? $"{sb}'s controller disconnected."
            : "Controller disconnected.";
    }

    private static bool HasConnectedGamepad(PlayerInput pi)
    {
        var ds = pi.devices;
        for (int i = 0; i < ds.Count; i++)
            if (ds[i] is Gamepad g && g.added) return true;
        return false;
    }

    private static bool HasKeyboardMouse(PlayerInput pi)
    {
        bool kb = false, ms = false;
        var ds = pi.devices;
        for (int i = 0; i < ds.Count; i++)
        {
            if (ds[i] is Keyboard k && k.added) kb = true;
            else if (ds[i] is Mouse m && m.added) ms = true;
        }
        return kb && ms;
    }

    //  Re-pair a returning / replacement pad to the stranded player 

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!_engaged || !(device is Gamepad pad)) return;
        if (change != InputDeviceChange.Added && change != InputDeviceChange.Reconnected) return;
        TryAssignPadToStranded(pad);
    }

    private void TryAssignPadToStranded(Gamepad pad)
    {
        if (DeviceInUse(pad)) return;

        var all = PlayerInput.all;
        for (int i = 0; i < all.Count; i++)
        {
            var pi = all[i];
            if (pi == null || !_everHadPad.Contains(pi) || HasConnectedGamepad(pi)) continue;
            try
            {
                // BUG FIX. This used to be an unconditional
                //     pi.SwitchCurrentControlScheme("Gamepad", pad);
                // SwitchCurrentControlScheme UNPAIRS every device currently paired to
                // the player and pairs only the one passed in. In single player,
                // CoopManager deliberately pairs keyboard + mouse + every pad to the
                // ONE PlayerInput and widens its binding mask to "Keyboard&Mouse;Gamepad"
                // so both are live at once. Unplug the pad, plug it back in, and this
                // line silently threw the keyboard and mouse away: the pad worked, the
                // keyboard and mouse were dead until the scene reloaded. Exactly the
                // "lost mouse and keyboard, controller still fine" symptom.
                //
                // So: only take the destructive path for a player that has nothing but
                // gamepads. Anyone holding keyboard+mouse gets the pad paired ADDITIVELY,
                // leaving their existing devices (and widened mask) untouched.
                if (HasKeyboardMouse(pi))
                {
                    var user = pi.user;
                    if (user.valid)
                    {
                        InputUser.PerformPairingWithDevice(pad, user);
                        if (debugLog) Debug.Log($"[DisconnectGuard] Paired '{pad.displayName}' to player {pi.playerIndex + 1} " +
                                                "additively (keyboard+mouse preserved).");
                    }
                }
                else
                {
                    pi.SwitchCurrentControlScheme("Gamepad", pad);  // pad-only player: pair + activate
                    if (debugLog) Debug.Log($"[DisconnectGuard] Re-paired '{pad.displayName}' to player {pi.playerIndex + 1}.");
                }
            }
            catch (System.Exception e)
            {
                if (debugLog) Debug.LogWarning($"[DisconnectGuard] Re-pair failed: {e.Message}");
            }
            return;
        }
    }

    private static bool DeviceInUse(InputDevice device)
    {
        var all = PlayerInput.all;
        for (int i = 0; i < all.Count; i++)
            foreach (var d in all[i].devices)
                if (d == device) return true;
        return false;
    }

    //  Pause / resume 

    // This overlay sits at sortingOrder 6000 — ABOVE the pause menu and the augment
    // menus. It used to snapshot Time.timeScale on engage and write it back on
    // resume, which stomped whatever the screen underneath had set. It now pushes
    // itself onto UIModalStack: resuming pops one layer and the stack recomputes
    // the correct global state, leaving any menu below still correctly paused.
    private void Engage()
    {
        _engaged = true;

        if (_root == null) BuildUI();
        if (_root != null) _root.SetActive(true);

        UIModalStack.Push(this);
        RefreshGate();

        if (debugLog) Debug.Log("[DisconnectGuard] Controller lost — run paused.");
    }

    private void Resume()
    {
        _engaged = false;
        if (_root != null) _root.SetActive(false);
        UIModalStack.Pop(this);

        if (debugLog) Debug.Log("[DisconnectGuard] Resumed.");
    }

    //  Gate (mirrors ContinueRunMenu.RefreshGate) 

    private void RefreshGate()
    {
        if (_resumeBtn == null) return;

        bool ready = !AnyStranded();
        _resumeBtn.interactable = ready;
        if (_resumeImg != null)
            _resumeImg.color = ready ? MenuTheme.BtnActive : new Color(0.18f, 0.20f, 0.26f, 1f);

        if (_resumeLabel != null)
        {
            _resumeLabel.text = ready ? "Resume Run" : "Waiting for controller…";
            _resumeLabel.color = ready ? Color.white : MenuTheme.ValueCol;
        }

        if (_status != null)
            _status.text = $"Controllers connected: {Gamepad.all.Count} / {PadPlayerCount()}";

        if (_summary != null)
            _summary.text = ready ? "All controllers connected — ready to resume." : WaitingSummary();
    }

    //  Actions 

    private void OnResume()
    {
        if (!AnyStranded()) Resume();
    }

    private void OnExitAndSave()
    {
        if (_committing) return;
        _committing = true;

        // Force a fresh save NOW. We can't rely on the last wave-start autosave: resuming
        // a run CONSUMES (deletes) its save, so after a Continue there may be no file on
        // disk yet — which is why a second "Exit & Save → Continue" failed. Writing here
        // guarantees a resumable save every time, even mid-wave and right after a resume.
        var orch = GameOrchestrator.Instance;
        var persist = RunPersistence.Instance;
        if (orch != null && persist != null)
        {
            orch.ForceAutoSave();   // clamps to a real, resumable wave (handles the final boss)
            if (debugLog) Debug.Log($"[DisconnectGuard] Forced save @ stage {orch.CurrentStageIndex} wave {orch.CurrentWaveInStage} (clamped on write).");
        }
        else if (debugLog)
        {
            Debug.LogWarning("[DisconnectGuard] Exit & Save: no GameOrchestrator/RunPersistence — could not write a save.");
        }

        QuitToMenu();
    }

    private void OnAbandon()
    {
        if (_committing) return;
        _committing = true;
        RunPersistence.Instance?.DeleteSave();
        QuitToMenu();
    }

    private void QuitToMenu()
    {
        _engaged = false;
        if (_root != null) _root.SetActive(false);

        // Drop EVERY open modal, not just ours: the pause menu / augment menu may
        // still be beneath us, and their freeze would ride into the menu scene.
        UIModalStack.ForceClear();

        // Leave no stale resume intent behind: GameOrchestrator/CoopManager read this
        // static on the next boot, and a leftover value could mis-seat or mis-route the
        // next launch. Exit & Save still leaves the run SAVE on disk (resume via
        // ContinueRunMenu); we only clear the one-shot intent handoff.
        RunResumeIntent.Clear();

        // Prefer the explicit field, else the default (matches PauseMenu's "MenuScene").
        string scene = !string.IsNullOrEmpty(mainMenuScene) ? mainMenuScene : defaultMenuScene;
        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogWarning("[DisconnectGuard] No menu scene set (Main Menu Scene and Default Menu Scene both blank).");
            _committing = false;
            return;
        }
        SceneManager.LoadScene(scene);
    }

    //  UI — built like ContinueRunMenu so the two screens are visually identical 

    private void BuildUI()
    {
        MenuTheme.EnsureEventSystem();
        var font = MenuTheme.ResolveFont(null, null);

        _root = new GameObject("ControllerDisconnectCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Must be the topmost thing on screen. 6000 sat UNDER LoreArchiveMenu (9996)
        // and RunProgressBar (9995): lose a pad with the archive open and the "plug it
        // back in" overlay rendered behind it, invisible.
        canvas.sortingOrder = 10000;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var dim = MenuTheme.NewUI("Dim", _root.transform);
        MenuTheme.Stretch(dim.GetComponent<RectTransform>());
        dim.AddComponent<Image>().sprite = MenuTheme.VerticalGradient(MenuTheme.GradTop, MenuTheme.GradBottom);

        var panel = MenuTheme.NewUI("Panel", dim.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(900, 620);
        MenuTheme.ApplySprite(panel.AddComponent<Image>(), MenuTheme.PanelSprite, MenuTheme.PanelSolid);

        var inner = MenuTheme.NewUI("Inner", panel.transform);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(64, 72); irt.offsetMax = new Vector2(-64, -78);
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.spacing = 14; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childAlignment = TextAnchor.UpperCenter;

        var title = MenuTheme.NewText("CONTROLLER DISCONNECTED", inner.transform, 46, TextAlignmentOptions.Center, font);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 6f;
        title.enableVertexGradient = true;
        var top = new Color(0.97f, 0.88f, 1f, 1f);
        title.colorGradient = new VertexGradient(top, top, MenuTheme.Magenta, MenuTheme.Magenta);
        SetH(title, 58);

        AddDivider(inner.transform);

        _summary = MenuTheme.NewText("", inner.transform, 24, TextAlignmentOptions.Center, font);
        _summary.color = MenuTheme.ValueCol;
        SetH(_summary, 80);

        _status = MenuTheme.NewText("", inner.transform, 22, TextAlignmentOptions.Center, font);
        _status.color = MenuTheme.Magenta;
        SetH(_status, 32);

        // Primary, gated like ContinueRunMenu's "Continue".
        _resumeBtn = MenuTheme.NewButton("Resume Run", inner.transform, 24, font);
        SetH(_resumeBtn, 64);
        _resumeImg = _resumeBtn.targetGraphic as Image;
        _resumeLabel = _resumeBtn.GetComponentInChildren<TextMeshProUGUI>();
        _resumeBtn.onClick.AddListener(OnResume);

        // Always-available secondary actions.
        var save = MenuTheme.NewButton("Exit & Save Run", inner.transform, 22, font);
        SetH(save, 54);
        save.onClick.AddListener(OnExitAndSave);

        var abandon = MenuTheme.NewButton("Abandon Run", inner.transform, 22, font);
        SetH(abandon, 52);
        abandon.onClick.AddListener(OnAbandon);

        var spacer = MenuTheme.NewUI("Spacer", inner.transform);
        var sle = spacer.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 0f;
    }

    private void AddDivider(Transform parent)
    {
        var holder = MenuTheme.NewUI("RuleHolder", parent);
        SetH(holder, 14);
        var rule = MenuTheme.NewUI("Rule", holder.transform);
        var rr = rule.GetComponent<RectTransform>();
        rr.anchorMin = new Vector2(0.20f, 0.5f); rr.anchorMax = new Vector2(0.80f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(0f, 3f);
        var img = rule.AddComponent<Image>();
        img.sprite = MenuTheme.HorizontalFade();
        img.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.8f);
    }

    private static void SetH(Component c, float h) => SetH(c.gameObject, h);

    private static void SetH(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0f;
    }
}
