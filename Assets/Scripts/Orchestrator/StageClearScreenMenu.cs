using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;


// Prefab-driven post-stage reward menu. Replaces the procedural PostStageChoiceMenu UI with
// the authored StageClearScreen prefab (two buttons: RestoreButton = heal reward,
// EmpowerButton = augment reward).
// SINGLE PLAYER: one full-screen instance, shown "as is". Clicking either button resolves the
// single choice — identical flow to the old menu (timeScale 0, input suppressed, entrance fade).
// CO-OP: one HALF-SIZE instance per player, placed inside that player's split-screen camera
// viewport. Each player picks independently; the menu waits until BOTH have chosen, then returns
// one Choice per player so the orchestrator can apply each reward to the right player.
public class StageClearScreenMenu : MonoBehaviour
{
    // Reuse the existing enum so orchestrator code/logging is unchanged.
    public enum Choice { None, Heal, Augment }

    [Header("Prefab")]
    [Tooltip("Resources path to the StageClearScreen prefab (no extension). " +
             "File lives at Assets/Resources/Sprites/WINSCREEN/StageClearScreen.prefab.")]
    public string prefabResourcePath = "Sprites/WINSCREEN/StageClearScreen";

    [Tooltip("Child GameObject name of the HEAL / restore button in the prefab.")]
    public string restoreButtonName = "RestoreButton";

    [Tooltip("Child GameObject name of the AUGMENT / empower button in the prefab.")]
    public string empowerButtonName = "EmpowerButton";

    [Tooltip("Name of the reward ICON shown for the restore/heal choice. The hover/press highlight " +
             "is fitted to THIS graphic's shape rather than the (rectangular) button hotspot.")]
    public string restoreGraphicName = "Regen";

    [Tooltip("Name of the reward ICON shown for the empower/augment choice. The hover/press " +
             "highlight is fitted to THIS graphic's shape.")]
    public string empowerGraphicName = "Power";

    [Header("Layout")]
    [Tooltip("Sorting order for the reward canvas (above gameplay HUD, below hard overlays).")]
    public int sortingOrder = 9997;

    [Tooltip("Scale applied to each co-op panel, relative to fully filling that player's screen " +
             "half. 1.0 = fills the half edge-to-edge (largest without overlapping the other " +
             "player); lower leaves a margin. Raise above 1.0 only if your prefab content is " +
             "smaller than full-screen.")]
    public float coopScaleMultiplier = 1.15f;

    [Header("Animation")]
    [Tooltip("Entrance fade duration (seconds, unscaled).")]
    public float entranceDuration = 0.35f;

    [Tooltip("Fade-out duration when a panel is dismissed after a choice (seconds, unscaled).")]
    public float exitDuration = 0.3f;

    // Optional: inject the scaled energy bonus into a child label. Leave name blank to show the
    // prefab's authored text unchanged. {0} in the format is replaced with the energy amount.
    [Header("Optional reward text injection (blank = leave prefab text as-is)")]
    public string restoreValueChildName = "";
    public string restoreValueFormat = "+{0}";
    public string empowerValueChildName = "";
    public string empowerValueFormat = "+{0}";

    // ---- runtime ----
    private GameObject _prefab;
    private bool _prefabResolved;

    /// True when the prefab is loadable; the orchestrator checks this to decide whether to use
    /// the prefab menu or fall back to the legacy PostStageChoiceMenu.
    public bool IsAvailable
    {
        get
        {
            ResolvePrefab();
            return _prefab != null;
        }
    }

    private void ResolvePrefab()
    {
        if (_prefabResolved) return;
        _prefabResolved = true;
        _prefab = Resources.Load<GameObject>(prefabResourcePath);
        if (_prefab == null)
            Debug.LogWarning($"[StageClearScreenMenu] Prefab not found at Resources/{prefabResourcePath}. " +
                             "Falling back to the legacy reward menu.");
    }


    // Shows the reward screen and waits until every player has chosen. The callback receives one
    // Choice per player (length 1 in single player). Owns the pause/input-suppress/entrance, then
    // restores them before invoking onChosen.
    public IEnumerator ShowChoices(int healEnergyBonus, int augmentEnergyBonus, Action<Choice[]> onChosen)
    {
        ResolvePrefab();
        if (_prefab == null)
        {
            // Safety: nothing to show. Default everyone to Heal so the run can't soft-lock.
            int fallbackCount = Mathf.Max(1, PlayerRegistry.Count);
            var fb = new Choice[fallbackCount];
            for (int i = 0; i < fb.Length; i++) fb[i] = Choice.Heal;
            onChosen?.Invoke(fb);
            yield break;
        }

        bool coop = PlayerRegistry.Count > 1;
        int playerCount = coop ? PlayerRegistry.Count : 1;

        // Build the overlay canvas.
        var canvasObj = new GameObject("StageClearCanvas");
        canvasObj.transform.SetParent(null, false);
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        var picks = new Choice[playerCount];
        var instances = new GameObject[playerCount];
        var groups = new CanvasGroup[playerCount];

        for (int i = 0; i < playerCount; i++)
        {
            int playerIndex = i; // capture for closures
            Rect viewport = ViewportFor(playerIndex, coop);

            // Holder fills exactly this player's viewport region of the overlay.
            var holder = new GameObject($"RewardHolder_P{playerIndex}");
            var hrt = holder.AddComponent<RectTransform>();
            holder.transform.SetParent(canvasObj.transform, false);
            hrt.anchorMin = new Vector2(viewport.xMin, viewport.yMin);
            hrt.anchorMax = new Vector2(viewport.xMax, viewport.yMax);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;

            var inst = Instantiate(_prefab, holder.transform, false);
            instances[i] = inst;
            var irt = inst.GetComponent<RectTransform>();
            if (irt != null)
            {
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = Vector2.zero;
                // Single player: show full-size ("as is"). Co-op: fit each half, then margin.
                float fit = coop ? Mathf.Min(viewport.width, viewport.height) * coopScaleMultiplier : 1f;
                irt.localScale = new Vector3(fit, fit, 1f);
            }

            // Fade group for the entrance.
            var cg = inst.GetComponent<CanvasGroup>();
            if (cg == null) cg = inst.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            groups[i] = cg;

            // Wire the two buttons for THIS instance.
            WireButton(inst, restoreButtonName, restoreGraphicName, () => Pick(picks, playerIndex, Choice.Heal));
            WireButton(inst, empowerButtonName, empowerGraphicName, () => Pick(picks, playerIndex, Choice.Augment));

            // Optional reward-value text.
            TrySetChildText(inst, restoreValueChildName, restoreValueFormat, healEnergyBonus);
            TrySetChildText(inst, empowerValueChildName, empowerValueFormat, augmentEnergyBonus);
        }

        // Freeze gameplay and suppress input while the menu is up.
        CombatJuice.StopAllShake();
        float prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        bool prevCursor = Cursor.visible;
        Cursor.visible = true;
        PlayerAttack.SetAllSuppressed(true);

        // Entrance fade (unscaled so it runs while paused).
        float t = 0f;
        while (t < entranceDuration)
        {
            t += Time.unscaledDeltaTime;
            float a = entranceDuration > 0f ? Mathf.Clamp01(t / entranceDuration) : 1f;
            float e = 1f - Mathf.Pow(1f - a, 3f);
            for (int i = 0; i < groups.Length; i++) if (groups[i] != null) groups[i].alpha = e;
            yield return null;
        }
        for (int i = 0; i < groups.Length; i++) if (groups[i] != null) groups[i].alpha = 1f;

        // Wait until every player has chosen. As soon as a player picks, fade THAT player's panel
        // away (so the click clearly registers); the run continues once everyone has chosen.
        var fading = new bool[playerCount];
        bool allChosen = false;
        while (!allChosen)
        {
            for (int i = 0; i < picks.Length; i++)
            {
                if (picks[i] != Choice.None && !fading[i])
                {
                    fading[i] = true;
                    if (groups[i] != null)
                    {
                        groups[i].interactable = false;
                        groups[i].blocksRaycasts = false;
                    }
                    StartCoroutine(FadeOutGroup(groups[i], exitDuration));
                }
            }

            allChosen = true;
            for (int i = 0; i < picks.Length; i++)
                if (picks[i] == Choice.None) { allChosen = false; break; }
            if (!allChosen) yield return null;
        }

        // Let the last panel's fade-out finish before tearing down.
        yield return new WaitForSecondsRealtime(exitDuration);

        // A gamepad player confirms with the right trigger — the same control as the
        // in-game weapon. Wait until it is released before resuming gameplay attacks,
        // otherwise a still-held trigger leaves the weapon action unable to fire until
        // the player lets go (see MenuInputGuard). Still paused here, so no gameplay runs.
        yield return MenuInputGuard.WaitForGamepadTriggersReleased();

        // Restore gameplay state.
        Time.timeScale = prevTimeScale;
        Cursor.visible = prevCursor;
        PlayerAttack.SetAllSuppressed(false);

        // Tear down.
        if (canvasObj != null) Destroy(canvasObj);

        onChosen?.Invoke(picks);
    }

    // Fades a CanvasGroup to fully transparent over the given unscaled duration.
    private IEnumerator FadeOutGroup(CanvasGroup cg, float duration)
    {
        if (cg == null) yield break;
        float start = cg.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            cg.alpha = Mathf.Lerp(start, 0f, u);
            yield return null;
        }
        cg.alpha = 0f;
    }

    private static void Pick(Choice[] picks, int playerIndex, Choice choice)
    {
        if (playerIndex < 0 || playerIndex >= picks.Length) return;
        if (picks[playerIndex] != Choice.None) return; // already locked in
        picks[playerIndex] = choice;
    }

    // Normalized viewport (0..1) for a player. Co-op uses the player's camera rect when present,
    // else falls back to a left/right split. Single player is full screen.
    private Rect ViewportFor(int playerIndex, bool coop)
    {
        if (!coop) return new Rect(0f, 0f, 1f, 1f);

        Camera cam = CameraFor(playerIndex);
        if (cam != null)
        {
            Rect r = cam.rect;
            // Guard against degenerate rects.
            if (r.width > 0.01f && r.height > 0.01f) return r;
        }

        // Fallback: even vertical split (left = P0, right = P1, ...).
        int count = Mathf.Max(1, PlayerRegistry.Count);
        float w = 1f / count;
        return new Rect(playerIndex * w, 0f, w, 1f);
    }

    private static Camera CameraFor(int playerIndex)
    {
        if (PlayerRegistry.Count == 0) return null;
        var pref = PlayerRegistry.Instance.Get(playerIndex);
        if (pref == null) return null;
        // PlayerRef.Camera is the per-player split-screen camera. Resolved via reflection so this
        // file compiles regardless of whether Camera is exposed as a property or field.
        var ty = pref.GetType();
        var pi = ty.GetProperty("Camera");
        if (pi != null) { try { return pi.GetValue(pref) as Camera; } catch { } }
        var fi = ty.GetField("Camera");
        if (fi != null) { try { return fi.GetValue(pref) as Camera; } catch { } }
        return null;
    }

    private void WireButton(GameObject instance, string childName, string choiceGraphicName, Action onClick)
    {
        Transform tr = FindDeep(instance.transform, childName);
        if (tr == null)
        {
            Debug.LogWarning($"[StageClearScreenMenu] Button '{childName}' not found in prefab '{instance.name}'.");
            return;
        }
        Button btn = tr.GetComponent<Button>();
        if (btn == null) btn = tr.GetComponentInChildren<Button>(true);
        if (btn == null)
        {
            Debug.LogWarning($"[StageClearScreenMenu] No Button component on '{childName}'.");
            return;
        }
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => onClick?.Invoke());

        // The choice ICON whose shape the highlight is fitted to (e.g. "Regen"/"Power").
        Graphic choiceGraphic = null;
        Transform gtr = FindDeep(instance.transform, choiceGraphicName);
        if (gtr != null) choiceGraphic = gtr.GetComponent<Graphic>();

        ConfigureButtonFeedback(btn, choiceGraphic);
    }

    // Hover / press / gamepad-focus feedback using the Button's OWN built-in ColorTint transition.
    // This is the exact mechanism every Unity button uses, so it always fires when the button gets
    // events (which it does — clicks work). We point the transition's targetGraphic at the visible
    // reward ICON (Regen/Power), so the icon itself tints — shape-aligned and guaranteed visible,
    // with no custom component, no overlay, and no AddComponent.
    private void ConfigureButtonFeedback(Button btn, Graphic choiceGraphic)
    {
        // Combine the two effects that were each confirmed visible:
        //   (1) the choice ICON inflates (scales up) on hover, dips on press, and
        //   (2) a soft warm GLOW fades in behind/around the icon — a nicer, rounded, design-aligned
        //       version of the old full-rectangle highlight.
        // Plus the Button's built-in ColorTint as a subtle, can't-fail safety layer.
        Graphic target = choiceGraphic;
        if (target == null) target = btn.targetGraphic;
        if (target == null) target = btn.GetComponentInChildren<Graphic>(true);

        // (Safety) built-in ColorTint on the icon — very subtle warm so it just complements the halo.
        btn.targetGraphic = target;
        btn.transition = Selectable.Transition.ColorTint;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 0.97f, 0.90f, 1f);
        cb.selectedColor = new Color(1f, 0.97f, 0.90f, 1f);
        cb.pressedColor = new Color(0.88f, 0.84f, 0.78f, 1f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        // The icon we (very gently) inflate, and where the glow sits.
        RectTransform inflateRT = target != null ? target.rectTransform : btn.GetComponent<RectTransform>();

        // Soft CIRCULAR glow, child of the icon so it's centred automatically, drawn ON TOP (this is
        // the layer that was actually visible), very transparent and pulsing so it reads as a gentle
        // "this is selectable" highlight rather than a hard box.
        Image glow = null;
        if (inflateRT != null)
        {
            var glowGO = new GameObject("ChoiceGlow");
            var grt = glowGO.AddComponent<RectTransform>();
            glowGO.transform.SetParent(inflateRT, false);
            // Fill ~125% of the icon, centred (anchors handle centring regardless of icon pivot).
            grt.anchorMin = new Vector2(-0.2f, -0.2f);
            grt.anchorMax = new Vector2(1.2f, 1.2f);
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;
            grt.SetAsLastSibling(); // ON TOP of the icon

            glow = glowGO.AddComponent<Image>();
            glow.sprite = GetSoftCircleSprite();
            glow.type = Image.Type.Simple;
            glow.raycastTarget = false;
            glow.color = new Color(1f, 0.92f, 0.7f, 0f); // soft warm, starts invisible
        }

        StageClearButtonFeedbackFx fx = btn.GetComponent<StageClearButtonFeedbackFx>();
        if (fx == null) fx = btn.gameObject.AddComponent<StageClearButtonFeedbackFx>();
        fx.Init(inflateRT, glow);
    }

    // Soft radial circle sprite (alpha 1 at centre, fading to 0 at the edge) used for the glow.
    private static Sprite _softCircle;
    private static Sprite GetSoftCircleSprite()
    {
        if (_softCircle != null) return _softCircle;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Vector2 c = Vector2.one * (S * 0.5f);
        float maxR = S * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / maxR;
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // soft edge but a strong, visible core
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        _softCircle = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _softCircle;
    }

    private void TrySetChildText(GameObject instance, string childName, string format, int value)
    {
        if (string.IsNullOrEmpty(childName)) return;
        Transform tr = FindDeep(instance.transform, childName);
        if (tr == null) return;
        try
        {
            var tmp = tr.GetComponent<TMPro.TMP_Text>() ?? tr.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null) { tmp.text = string.Format(format, value); return; }
        }
        catch { /* TMP not present — fall through to legacy Text */ }
        var legacy = tr.GetComponent<Text>() ?? tr.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = string.Format(format, value);
    }

    // Depth-first search for a descendant by exact name (the root itself is checked too).
    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        // With the New Input System active, the legacy StandaloneInputModule does NOT deliver
        // pointer events — use the Input System UI module so hover/click work.
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }
}

// Hover/select/press feedback for a StageClearScreen choice. Brightens + scales the actual reward
// icon graphics (inherently shape-aligned and guaranteed visible). Works for mouse
// (enter/exit/down/up) and gamepad (select/deselect/submit), in unscaled time so it animates while
// the menu pauses the game (Time.timeScale = 0).
// IMPORTANT: this is a TOP-LEVEL class on purpose — Unity's AddComponent<T>() cannot attach a
// nested MonoBehaviour, so a nested version silently fails to attach (no feedback at all).
public class StageClearButtonFeedbackFx : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler, ISubmitHandler
{
    private RectTransform _rt;        // the icon (gentle inflate)
    private Vector3 _baseScale;
    private Image _glow;              // soft circular glow
    private bool _hovered, _pressed, _selected;
    private float _curAlpha;
    private float _blink;             // remaining blink time

    private static readonly Color GlowColor = new Color(1f, 0.93f, 0.72f, 1f);
    private const float HoverAlpha = 0.45f;   // steady glow strength on hover (clearly visible)
    private const float BlinkPeak = 0.90f;   // bright flash on click
    private const float BlinkDur = 0.45f;   // length of the two-flash blink
    private const float PulseSpeed = 2.5f;    // gentle breathing while hovered
    private const float PulseFloor = 0.78f;   // glow only dips to 78% of peak (subtle pulse)
    private const float FadeRate = 12f;
    private const float HoverScale = 1.04f, PressScale = 0.97f;

    public void Init(RectTransform inflateTarget, Image glow)
    {
        _rt = inflateTarget;
        _baseScale = _rt != null ? _rt.localScale : Vector3.one;
        _glow = glow;
        _curAlpha = 0f; _blink = 0f;
        if (_glow != null) { var c = GlowColor; c.a = 0f; _glow.color = c; }
    }

    private void OnDisable()
    {
        _hovered = _pressed = _selected = false;
        _curAlpha = 0f; _blink = 0f;
        if (_glow != null) { var c = GlowColor; c.a = 0f; _glow.color = c; }
        if (_rt != null) _rt.localScale = _baseScale;
    }

    public void OnPointerEnter(PointerEventData e) { _hovered = true; }
    public void OnPointerExit(PointerEventData e) { _hovered = false; }
    public void OnPointerDown(PointerEventData e) { _pressed = true; _blink = BlinkDur; }   // blink on click
    public void OnPointerUp(PointerEventData e) { _pressed = false; }
    public void OnSelect(BaseEventData e) { _selected = true; }
    public void OnDeselect(BaseEventData e) { _selected = false; }
    public void OnSubmit(BaseEventData e) { _blink = BlinkDur; }                            // blink on gamepad submit

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool hoverOn = _hovered || _selected;

        // Steady, gently-pulsing glow while hovered/selected (eased in and out).
        float hoverTarget = 0f;
        if (hoverOn)
        {
            float pulse = PulseFloor + (1f - PulseFloor) * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseSpeed));
            hoverTarget = HoverAlpha * pulse;
        }
        _curAlpha = Mathf.Lerp(_curAlpha, hoverTarget, 1f - Mathf.Exp(-FadeRate * dt));

        // Crisp double-blink flash layered on top when clicked.
        float shown = _curAlpha;
        if (_blink > 0f)
        {
            _blink -= dt;
            float t = 1f - Mathf.Clamp01(_blink / BlinkDur);          // 0 -> 1 over the blink
            float env = Mathf.Max(0f, Mathf.Cos(t * Mathf.PI * 4f)) * (1f - t); // two flashes, fading
            shown = Mathf.Max(shown, BlinkPeak * env);
        }
        if (_glow != null) { var c = GlowColor; c.a = shown; _glow.color = c; }

        // Very gentle inflate.
        float s = _pressed ? PressScale : (hoverOn ? HoverScale : 1f);
        if (_rt != null)
            _rt.localScale = Vector3.Lerp(_rt.localScale, _baseScale * s, 1f - Mathf.Exp(-FadeRate * dt));
    }
}
