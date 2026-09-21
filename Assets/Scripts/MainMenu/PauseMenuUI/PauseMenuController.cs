using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using FMODUnity;
using FMOD.Studio;

// PAUSE MENU
public class PauseMenuController : MonoBehaviour
{
    public GameObject pauseMenu;
    private bool activated;

    //  Presentation options 

    [Header("Audio while paused")]
    [Tooltip("Pause gameplay audio while the menu is open. Music keeps playing.")]
    [SerializeField] private bool silenceGameplayAudio = true;

    [Tooltip("FMOD buses to pause. Leave the music bus OUT so the track keeps playing. " +
             "Paths must match FMOD Studio (right-click a bus → Copy Path). If you add " +
             "click sounds to menus later, give them their own bus and leave it out of " +
             "this list so they stay audible.")]
    [SerializeField] private string[] pausedAudioBuses = { "bus:/SFX", "bus:/Ambience" };

    [Header("Dim behind the menu")]
    [SerializeField] private bool dimBackground = true;

    [Tooltip("Alpha is what you want to tune: ~0.45 = the game stays clearly readable, " +
             "~0.75 = heavily blacked out.")]
    [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.55f);

    [Tooltip("Fade-in seconds (unscaled — the game is frozen). 0 = instant.")]
    [SerializeField] private float dimFadeSeconds = 0.12f;

    [Tooltip("Sorting order relative to the pause menu's canvas. -1 keeps the sheet " +
             "behind the menu but in front of the game. Lower it further to dim the HUD too.")]
    [SerializeField] private int dimSortingOffset = -1;

    [Tooltip("Swallow clicks aimed at whatever is behind the sheet. OFF by default so " +
             "nothing that used to be clickable under the menu changes behaviour.")]
    [SerializeField] private bool dimBlocksClicks = false;

    [Tooltip("OPTIONAL: your existing panel background Image(s), made more transparent " +
             "while the menu is open so the dim does the work instead of the panel. " +
             "Original alpha is restored on close. Leave empty to change nothing.")]
    [SerializeField] private Graphic[] softenPanels;
    [SerializeField, Range(0f, 1f)] private float softenedPanelAlpha = 0.80f;

    [Header("Button hover")]
    [SerializeField] private bool buttonHoverEffects = true;

    [Tooltip("Overlay colour. White lightens the button; a dark colour deepens it.")]
    [SerializeField] private Color hoverShadeColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float hoverAlpha = 0.11f;
    [SerializeField, Range(0f, 1f)] private float pressAlpha = 0.22f;
    [SerializeField] private bool hoverScaleEnabled = true;
    [SerializeField, Range(1f, 1.2f)] private float hoverScale = 1.035f;
    [SerializeField, Range(0.8f, 1f)] private float pressScale = 0.985f;
    [SerializeField] private float hoverFadeSeconds = 0.09f;

    [Tooltip("Off = Buttons only (recommended). On = sliders, toggles and dropdowns too.")]
    [SerializeField] private bool hoverAllSelectables = false;

    //  State 

    // Co-op: one weapon-roll canvas per player, so hide ALL of them.
    private readonly List<GameObject> _weaponRollCanvases = new List<GameObject>();

    private readonly List<InputAction> _pauseActions = new List<InputAction>();
    private int _lastToggleFrame = -1;

    private GameObject _dimRoot;
    private Image _dimImage;
    private float _dimT;

    private float[] _panelAlphas;

    private readonly List<Selectable> _hoverScratch = new List<Selectable>();
    private float _nextHoverScan;

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

        // …or the silence: FMOD bus state is global and survives a scene load.
        GameAudioPause.Pop(this);

        // …or the dim: it lives outside this hierarchy so it will not go away on its own.
        DestroyDim();
        SoftenPanels(false);
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
        // Presentation upkeep runs BEFORE the input early-out below, which returns
        // whenever a live Pause action exists.
        TickDim();
        TickHoverScan();

        // Fallback ONLY when no Pause action is live (legacy setup / pre-spawn
        // frames / every player's input temporarily disabled).
        if (AnyLivePauseAction()) return;

        // PausePressedThisFrame is Esc/Start only. The general back press also
        // includes gamepad B, which is a GAMEPLAY control (dodge / cancel) — polling
        // it here would make B open the pause menu.
        if (MenuBackInput.PausePressedThisFrame) RequestToggle();
    }

    /// Toggle the pause menu — but only if nothing is layered on top of it.
    /// Whether this arrives from an InputAction callback (which can run before or
    /// after Update) or from the fallback poll, the arbitration is identical, so
    /// script execution order can no longer decide who wins the key press.
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

            // …and GameAudioPause takes the mixer, so the frozen world is silent too.
            if (silenceGameplayAudio)
            {
                GameAudioPause.SetBuses(pausedAudioBuses);
                GameAudioPause.Push(this);
            }

            BuildDim();
            SoftenPanels(true);
            InstallHover();

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

            // Unconditional: a hold taken before the toggle was switched off in the
            // Inspector must still be released.
            GameAudioPause.Pop(this);
            DestroyDim();
            SoftenPanels(false);
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

    //  Dim sheet 
    // The sheet must sit BETWEEN the game and the menu. Parenting it inside the
    // pause menu's own canvas would make it fight the panel's sibling order and any
    // layout group in the way, so it gets a canvas of its own that COPIES the pause
    // canvas's render mode, camera and sorting layer and sits one order below it.
    // Copying the render mode matters: a Screen Space - Overlay canvas draws above
    // every Screen Space - Camera canvas whatever the sorting orders say, so a
    // hard-coded Overlay sheet would cover a camera-space menu.

    private void BuildDim()
    {
        if (!dimBackground || _dimRoot != null) return;

        var host = pauseMenu != null ? pauseMenu.GetComponentInParent<Canvas>() : null;
        var root = host != null ? host.rootCanvas : null;

        _dimRoot = new GameObject("PauseDim (auto)", typeof(RectTransform), typeof(Canvas));

        var canvas = _dimRoot.GetComponent<Canvas>();
        if (root != null)
        {
            canvas.renderMode = root.renderMode;
            canvas.worldCamera = root.worldCamera;
            canvas.planeDistance = root.planeDistance;
            canvas.sortingLayerID = root.sortingLayerID;
            canvas.sortingOrder = root.sortingOrder + dimSortingOffset;
        }
        else
        {
            // No canvas above the menu (shouldn't happen for a UI object, but never
            // leave the sheet un-rendered because of it).
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = dimSortingOffset;
        }

        var sheet = new GameObject("Dim", typeof(RectTransform));
        sheet.transform.SetParent(_dimRoot.transform, false);
        var rt = (RectTransform)sheet.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        _dimImage = sheet.AddComponent<Image>();
        _dimImage.raycastTarget = dimBlocksClicks;
        // A raycast target is useless without a raycaster on its canvas.
        if (dimBlocksClicks) _dimRoot.AddComponent<GraphicRaycaster>();

        _dimT = dimFadeSeconds <= 0f ? 1f : 0f;
        PushDimAlpha();
    }

    private void TickDim()
    {
        if (_dimImage == null || _dimT >= 1f) return;
        _dimT = dimFadeSeconds <= 0f ? 1f
                                     : Mathf.MoveTowards(_dimT, 1f, Time.unscaledDeltaTime / dimFadeSeconds);
        PushDimAlpha();
    }

    private void PushDimAlpha()
    {
        if (_dimImage == null) return;
        var c = dimColor;
        c.a = dimColor.a * _dimT;
        _dimImage.color = c;
    }

    private void DestroyDim()
    {
        if (_dimRoot == null) return;
        if (Application.isPlaying) Destroy(_dimRoot); else DestroyImmediate(_dimRoot);
        _dimRoot = null;
        _dimImage = null;
        _dimT = 0f;
    }

    private void SoftenPanels(bool soften)
    {
        if (softenPanels == null || softenPanels.Length == 0) return;

        if (soften)
        {
            // Capture once. Re-capturing on the second open would record the ALREADY
            // softened value as the original and fade the panel away over time.
            if (_panelAlphas == null || _panelAlphas.Length != softenPanels.Length)
            {
                _panelAlphas = new float[softenPanels.Length];
                for (int i = 0; i < softenPanels.Length; i++)
                    _panelAlphas[i] = softenPanels[i] != null ? softenPanels[i].color.a : 1f;
            }

            for (int i = 0; i < softenPanels.Length; i++)
            {
                if (softenPanels[i] == null) continue;
                var c = softenPanels[i].color;
                c.a = softenedPanelAlpha;
                softenPanels[i].color = c;
            }
            return;
        }

        if (_panelAlphas == null) return;
        for (int i = 0; i < softenPanels.Length && i < _panelAlphas.Length; i++)
        {
            if (softenPanels[i] == null) continue;
            var c = softenPanels[i].color;
            c.a = _panelAlphas[i];
            softenPanels[i].color = c;
        }
    }

    //  Hover 

    private void TickHoverScan()
    {
        if (!activated || !buttonHoverEffects) return;
        if (Time.unscaledTime < _nextHoverScan) return;   // unscaled: the menu is frozen
        InstallHover();
    }

    // Scope is deliberately "children of the pause menu", so the Options overlay, the
    // rebind screen and the augment menu — which build their own themed buttons via
    // MenuTheme.NewButton — are untouched.
    //
    // Rescanned periodically because runtime-built buttons appear late:
    // PauseControlsButton creates its "Controls" button in Start(), which on the very
    // first open runs AFTER this call.
    private void InstallHover()
    {
        _nextHoverScan = Time.unscaledTime + 0.5f;
        if (!buttonHoverEffects || pauseMenu == null) return;

        _hoverScratch.Clear();
        pauseMenu.GetComponentsInChildren(true, _hoverScratch);

        for (int i = 0; i < _hoverScratch.Count; i++)
        {
            var s = _hoverScratch[i];
            if (s == null) continue;
            if (!hoverAllSelectables && !(s is Button)) continue;
            if (s.GetComponent<MenuButtonHover>() != null) continue;   // already set up

            var h = s.gameObject.AddComponent<MenuButtonHover>();
            h.shadeColor = hoverShadeColor;
            h.hoverAlpha = hoverAlpha;
            h.pressAlpha = pressAlpha;
            h.useScale = hoverScaleEnabled;
            h.hoverScale = hoverScale;
            h.pressScale = pressScale;
            h.fadeSeconds = hoverFadeSeconds;
        }

        _hoverScratch.Clear();
    }
}


//  GameAudioPause — "the world is frozen, so the world should be silent."
//  Time.timeScale = 0 stops Unity, not FMOD. FMOD's mixer runs on its own audio
//  thread, so every looping event keeps playing while the pause menu is up: the
//  eye enemy's chain attack, the laser tower's beam, boss loops, tower ambience.
//  Pausing them one instance at a time is not possible from here:
//    AudioManager.eventInstances only holds what went through CreateInstance()
//      — basically the music and ambience beds.
//    Every held gameplay loop uses SpatialLoopSfx, which creates its instance
//      with RuntimeManager.CreateInstance directly and deliberately does NOT
//      register it (registering would grow that list per enemy and risk a double
//      release — see the comment above SpatialLoopSfx).
//    One-shots (AudioManager.PlayOneShot) are fire-and-forget; nobody holds a
//      handle to them at all.
//  A BUS catches all three, whoever created the instance, because pausing a bus
//  pauses everything routed through it and through its children.

public static class GameAudioPause
{
    /// <summary>Buses silenced while a menu holds the game. Music is intentionally absent.</summary>
    public static readonly string[] DefaultBusPaths = { "bus:/SFX", "bus:/Ambience" };

    private static readonly List<object> _owners = new List<object>();
    private static readonly List<string> _busPaths = new List<string>(DefaultBusPaths);

    // Exactly what we actually managed to pause, so resuming can never miss a bus —
    // and can never un-pause one we never touched — even if the path list changed
    // while the menu was open.
    private static readonly List<string> _pausedPaths = new List<string>();

    private static readonly HashSet<string> _warned = new HashSet<string>();

    /// <summary>True while at least one owner is holding gameplay audio silent.</summary>
    public static bool IsPaused => _owners.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // "No domain reload" keeps statics between play sessions: a stale owner or a
        // stale _pausedPaths entry would make the next session start silent.
        _owners.Clear();
        _pausedPaths.Clear();
        _warned.Clear();
        _busPaths.Clear();
        _busPaths.AddRange(DefaultBusPaths);

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                                      UnityEngine.SceneManagement.LoadSceneMode m) => ForceResume();

    /// Replace the list of buses to silence. Applied immediately if a menu is already
    /// holding the pause. An empty list disables the feature (nothing is ever paused).
    public static void SetBuses(IList<string> paths)
    {
        _busPaths.Clear();
        if (paths != null)
        {
            for (int i = 0; i < paths.Count; i++)
                if (!string.IsNullOrEmpty(paths[i]) && !_busPaths.Contains(paths[i]))
                    _busPaths.Add(paths[i]);
        }

        if (IsPaused) Apply();   // pick up added buses; release removed ones
    }

    /// <summary>Ask for gameplay silence on behalf of <paramref name="owner"/>.</summary>
    public static void Push(object owner)
    {
        if (owner == null) return;
        Prune();
        if (!Contains(owner)) _owners.Add(owner);
        Apply();
    }

    /// <summary>Release <paramref name="owner"/>'s hold. Safe to call when not held.</summary>
    public static void Pop(object owner)
    {
        if (owner == null) return;
        for (int i = _owners.Count - 1; i >= 0; i--)
            if (ReferenceEquals(_owners[i], owner)) { _owners.RemoveAt(i); break; }
        Prune();
        Apply();
    }

    /// <summary>Drop every hold and bring the audio back. Used when leaving for another scene.</summary>
    public static void ForceResume()
    {
        _owners.Clear();
        Apply();
    }

    private static bool Contains(object owner)
    {
        for (int i = 0; i < _owners.Count; i++)
            if (ReferenceEquals(_owners[i], owner)) return true;
        return false;
    }

    private static void Prune()
    {
        for (int i = _owners.Count - 1; i >= 0; i--)
        {
            var o = _owners[i];
            if (o == null || (o is UnityEngine.Object uo && uo == null)) _owners.RemoveAt(i);
        }
    }

    private static void Apply()
    {
        if (_owners.Count > 0)
        {
            for (int i = 0; i < _busPaths.Count; i++)
            {
                string path = _busPaths[i];
                if (_pausedPaths.Contains(path)) continue;
                if (SetBusPaused(path, true)) _pausedPaths.Add(path);
            }

            // A bus removed from the list mid-pause must not stay stuck paused.
            for (int i = _pausedPaths.Count - 1; i >= 0; i--)
            {
                if (_busPaths.Contains(_pausedPaths[i])) continue;
                SetBusPaused(_pausedPaths[i], false);
                _pausedPaths.RemoveAt(i);
            }
            return;
        }

        // Resume exactly what we paused, then forget it.
        for (int i = 0; i < _pausedPaths.Count; i++) SetBusPaused(_pausedPaths[i], false);
        _pausedPaths.Clear();
    }

    private static bool SetBusPaused(string path, bool paused)
    {
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            // Throws for a path that isn't in the loaded banks, and can throw while
            // FMOD is still starting up or already shutting down.
            Bus bus = RuntimeManager.GetBus(path);
            if (!bus.isValid()) return false;

            FMOD.RESULT r = bus.setPaused(paused);
            if (r != FMOD.RESULT.OK) { WarnOnce(path, $"setPaused({paused}) returned {r}."); return false; }
            return true;
        }
        catch (System.Exception e)
        {
            WarnOnce(path, e.Message);
            return false;
        }
    }

    // One warning per bus per session — a missing bus would otherwise log every time
    // any menu opens.
    private static void WarnOnce(string path, string message)
    {
        if (!_warned.Add(path)) return;
        Debug.LogWarning($"[GameAudioPause] Could not control '{path}': {message} " +
                         "Check the bus path in FMOD Studio (right-click the bus → Copy Path) " +
                         "and update 'Paused Audio Buses' on PauseMenuController. Gameplay audio " +
                         "will keep playing under the menu until this is fixed.");
    }
}


//  MenuButtonHover — hover / focus / press feedback for ONE Selectable.

[DisallowMultipleComponent]
public class MenuButtonHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler
{
    public Color shadeColor = Color.white;
    [Range(0f, 1f)] public float hoverAlpha = 0.11f;
    [Range(0f, 1f)] public float pressAlpha = 0.22f;

    public bool useScale = true;
    [Range(1f, 1.2f)] public float hoverScale = 1.035f;
    [Range(0.8f, 1f)] public float pressScale = 0.985f;

    public float fadeSeconds = 0.09f;

    private Selectable _selectable;
    private Image _overlay;
    private Vector3 _baseScale = Vector3.one;

    private bool _hovered, _focused, _pressed;
    private float _t;            // 0 = rest, 1 = highlighted
    private float _applied = -1f;

    private bool Interactable => _selectable == null || _selectable.IsInteractable();

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();
        _baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        // A menu is hidden with SetActive(false), which never sends OnPointerExit — so
        // a button hovered when the menu closed would come back scaled up and lit.
        _hovered = _focused = _pressed = false;
        _t = 0f;
        _applied = -1f;
        ApplyVisual(0f);
    }

    private void OnDisable()
    {
        if (_overlay != null) SetOverlayAlpha(0f);
        if (useScale) transform.localScale = _baseScale;
    }

    public void OnPointerEnter(PointerEventData e) => _hovered = true;
    public void OnPointerExit(PointerEventData e) { _hovered = false; _pressed = false; }
    public void OnPointerDown(PointerEventData e) => _pressed = true;
    public void OnPointerUp(PointerEventData e) => _pressed = false;
    public void OnSelect(BaseEventData e) => _focused = true;
    public void OnDeselect(BaseEventData e) { _focused = false; _pressed = false; }

    private void Update()
    {
        // A button greyed out mid-hover (a "Continue" that disables itself) must drop
        // the highlight rather than sit lit and un-clickable.
        float target = (Interactable && (_hovered || _focused)) ? 1f : 0f;

        _t = fadeSeconds <= 0f ? target
                               : Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime / fadeSeconds);
        ApplyVisual(_t);
    }

    private void ApplyVisual(float t)
    {
        bool pressed = _pressed && Interactable;
        float alpha = pressed ? pressAlpha : Mathf.Lerp(0f, hoverAlpha, t);
        float scale = pressed ? pressScale : Mathf.Lerp(1f, hoverScale, t);

        // Cheap early-out: menus are full of buttons and only one is ever hovered.
        float signature = alpha + scale * 10f;
        if (Mathf.Approximately(signature, _applied)) return;
        _applied = signature;

        SetOverlayAlpha(alpha);
        if (useScale) transform.localScale = _baseScale * scale;
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (alpha <= 0.001f && _overlay == null) return;   // don't build until needed
        EnsureOverlay();
        if (_overlay == null) return;

        var c = shadeColor;
        c.a = alpha;
        _overlay.color = c;
        _overlay.enabled = alpha > 0.001f;
    }

    private void EnsureOverlay()
    {
        if (_overlay != null) return;

        var go = new GameObject("HoverShade", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.SetAsFirstSibling();   // above the background, below the label

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        _overlay = go.AddComponent<Image>();
        _overlay.raycastTarget = false;     // must never eat a click

        // Take the button's own shape, so a rounded / 9-sliced button doesn't get a
        // rectangular flash around it.
        var src = _selectable != null ? _selectable.targetGraphic as Image : null;
        if (src == null) src = GetComponent<Image>();
        if (src != null && src.sprite != null)
        {
            _overlay.sprite = src.sprite;
            _overlay.type = src.type;
            _overlay.preserveAspect = src.preserveAspect;
            if (src.type == Image.Type.Sliced || src.type == Image.Type.Tiled)
                _overlay.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier;
        }

        // If the button itself carries a layout group or fitter, an extra child would
        // otherwise be laid out alongside the label.
        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        _overlay.enabled = false;
    }
}

