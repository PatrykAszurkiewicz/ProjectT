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

    [Header("Layout")]
    [Tooltip("Sorting order for the reward canvas (above gameplay HUD, below hard overlays).")]
    public int sortingOrder = 9997;

    [Tooltip("Scale applied to each co-op panel, relative to fully filling that player's screen " +
             "half. 1.0 = fills the half edge-to-edge (largest without overlapping the other " +
             "player); lower leaves a margin. Raise above 1.0 only if your prefab content is " +
             "smaller than full-screen.")]
    public float coopScaleMultiplier = 1.0f;

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


    /// Shows the reward screen and waits until every player has chosen. The callback receives one
    /// Choice per player (length 1 in single player). Owns the pause/input-suppress/entrance, then
    /// restores them before invoking onChosen.

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
            WireButton(inst, restoreButtonName, () => Pick(picks, playerIndex, Choice.Heal));
            WireButton(inst, empowerButtonName, () => Pick(picks, playerIndex, Choice.Augment));

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

    private void WireButton(GameObject instance, string childName, Action onClick)
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
        ConfigureButtonFeedback(btn);
    }

    // Adds hover/select/press feedback to a button, fully in code:
    //  - cursor hover  -> subtle brighten (ColorTint highlightedColor)
    //  - gamepad focus -> subtle brighten (ColorTint selectedColor)
    //  - click / submit -> clearly darker + a small scale "pop" so the press is visible
    // ColorTint animates with ignoreTimeScale internally, so it still works while this menu
    // pauses the game (Time.timeScale = 0). The scale pop uses unscaled time for the same reason.
    private void ConfigureButtonFeedback(Button btn)
    {
        RectTransform brt = btn.GetComponent<RectTransform>();

        // The prefab's buttons are invisible click hotspots sitting OVER painted art that lives in
        // the shared MainImage — so tinting/scaling the button's own (transparent) graphic shows
        // nothing. Instead add a visible highlight overlay as the LAST child of the button; it
        // draws on top of the painted button beneath it, so hover/press read clearly regardless of
        // what the art looks like.
        var overlayGO = new GameObject("HoverHighlight");
        var ort = overlayGO.AddComponent<RectTransform>();
        overlayGO.transform.SetParent(btn.transform, false);
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = Vector2.zero;
        ort.offsetMax = Vector2.zero;
        ort.SetAsLastSibling();

        var overlay = overlayGO.AddComponent<Image>();
        overlay.sprite = GetSoftRectSprite();   // soft-feathered so the highlight reads as a glow
        overlay.type = Image.Type.Simple;
        overlay.color = new Color(1f, 1f, 1f, 0f); // start invisible
        overlay.raycastTarget = false;             // must never block the button beneath

        // Keep ColorTint as a bonus (harmless if the button graphic is invisible; adds shading if
        // it happens to be visible).
        if (btn.targetGraphic == null)
        {
            Graphic g = btn.GetComponent<Graphic>();
            if (g == null) g = btn.GetComponentInChildren<Graphic>(true);
            btn.targetGraphic = g;
        }
        btn.transition = Selectable.Transition.ColorTint;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        cb.selectedColor = new Color(1.06f, 1.06f, 1.06f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.fadeDuration = 0.10f;
        btn.colors = cb;

        // Drive the overlay (and a small scale pop) on hover/select/press for mouse AND gamepad.
        ButtonFeedbackFx fx = btn.GetComponent<ButtonFeedbackFx>();
        if (fx == null) fx = btn.gameObject.AddComponent<ButtonFeedbackFx>();
        fx.Init(brt, overlay);
    }

    // Soft-feathered rounded-rect sprite used for the hover/press highlight (cached).
    private static Sprite _softRect;
    private static Sprite GetSoftRectSprite()
    {
        if (_softRect != null) return _softRect;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        float feather = 10f;   // edge softness in px
        float radius = 14f;    // corner radius in px
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // Distance from the rounded-rect edge (positive = inside).
                float dx = Mathf.Max(radius - x, x - (S - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (S - 1 - radius), 0f);
                float corner = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = Mathf.Min(
                    Mathf.Min(x, S - 1 - x),
                    Mathf.Min(y, S - 1 - y));
                float inset = Mathf.Min(edge + radius, radius + (radius - corner));
                float a = Mathf.Clamp01(inset / feather);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        _softRect = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _softRect;
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
        es.AddComponent<StandaloneInputModule>();
    }

    // Hover/select/press feedback for an invisible-hotspot button. Drives a visible overlay Image
    // (color/alpha) plus a small scale pop on the button rect. Works for mouse (enter/exit/down/up)
    // and gamepad (select/deselect/submit), and uses unscaled time so it animates while the menu
    // pauses the game (Time.timeScale = 0).
    private class ButtonFeedbackFx : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        private RectTransform _rt;
        private Image _overlay;
        private Vector3 _baseScale;
        private bool _hovered, _pressed, _selected;
        private Coroutine _co;

        private static readonly Color ColNone = new Color(1f, 0.95f, 0.80f, 0f);
        private static readonly Color ColHover = new Color(1f, 0.95f, 0.80f, 0.22f); // subtle warm glow
        private static readonly Color ColPress = new Color(1f, 0.85f, 0.55f, 0.45f); // clearly visible
        private const float HoverScale = 1.03f, PressScale = 0.95f, AnimDur = 0.10f;

        public void Init(RectTransform target, Image overlay)
        {
            _rt = target;
            _overlay = overlay;
            _baseScale = _rt != null ? _rt.localScale : Vector3.one;
            if (_overlay != null) _overlay.color = ColNone;
        }

        private void OnDisable()
        {
            _hovered = _pressed = _selected = false;
            if (_co != null) { StopCoroutine(_co); _co = null; }
            if (_overlay != null) _overlay.color = ColNone;
            if (_rt != null) _rt.localScale = _baseScale;
        }

        public void OnPointerEnter(PointerEventData e) { _hovered = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { _hovered = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { _pressed = true; Apply(); }
        public void OnPointerUp(PointerEventData e) { _pressed = false; Apply(); }
        public void OnSelect(BaseEventData e) { _selected = true; Apply(); }
        public void OnDeselect(BaseEventData e) { _selected = false; Apply(); }
        public void OnSubmit(BaseEventData e) { if (isActiveAndEnabled) StartCoroutine(SubmitFlash()); }

        private IEnumerator SubmitFlash()
        {
            _pressed = true; Apply();
            yield return new WaitForSecondsRealtime(0.12f);
            _pressed = false; Apply();
        }

        private void Apply()
        {
            Color targetColor;
            float scaleMul;
            if (_pressed) { targetColor = ColPress; scaleMul = PressScale; }
            else if (_hovered || _selected) { targetColor = ColHover; scaleMul = HoverScale; }
            else { targetColor = ColNone; scaleMul = 1f; }

            Vector3 targetScale = _baseScale * scaleMul;
            if (_co != null) StopCoroutine(_co);
            if (isActiveAndEnabled) _co = StartCoroutine(AnimateTo(targetColor, targetScale));
            else { if (_overlay != null) _overlay.color = targetColor; if (_rt != null) _rt.localScale = targetScale; }
        }

        private IEnumerator AnimateTo(Color targetColor, Vector3 targetScale)
        {
            Color c0 = _overlay != null ? _overlay.color : targetColor;
            Vector3 s0 = _rt != null ? _rt.localScale : targetScale;
            float t = 0f;
            while (t < AnimDur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / AnimDur);
                float e = 1f - Mathf.Pow(1f - u, 3f);
                if (_overlay != null) _overlay.color = Color.Lerp(c0, targetColor, e);
                if (_rt != null) _rt.localScale = Vector3.Lerp(s0, targetScale, e);
                yield return null;
            }
            if (_overlay != null) _overlay.color = targetColor;
            if (_rt != null) _rt.localScale = targetScale;
            _co = null;
        }
    }
}
