using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


// Lists ONE player's currently-applied augments as a single vertical column
// (scrollable when wrapped in a ScrollRect). Built for the co-op P1/P2
// RightPanels, which are too narrow for the single-player square grid.
//    Put on the RightPanel's content object (or set `content` to it).
//    Set Player Index: 0 = P1, 1 = P2, -1 = single player / first player.
//    Optional: assign `rowPrefab` (any TMP text) for styling, and point
//     `content` at a ScrollRect's "Content" so long lists scroll.
// It adds a VerticalLayoutGroup + ContentSizeFitter to the content at runtime,
// so you don't have to wire layout by hand. Does NOT touch the single-player
// grid display — leave that as-is to avoid regressions.

public class ActiveAugmentsList : MonoBehaviour
{
    [Header("Binding")]
    [Tooltip("0 = Player 1, 1 = Player 2, -1 = single player / first player.")]
    [SerializeField] private int playerIndex = -1;

    [Tooltip("Where rows are added. Leave empty to use THIS object. For scrolling, " +
             "point at a ScrollRect's Content.")]
    [SerializeField] private RectTransform content;

    [Header("Appearance")]
    [Tooltip("Optional TMP row to clone per augment. If empty, plain text rows are created.")]
    [SerializeField] private TextMeshProUGUI rowPrefab;
    [SerializeField] private float fontSize = 22f;
    [Tooltip("Optional header row at the top (e.g. \"Active\"). Leave blank for none.")]
    [SerializeField] private string headerText = "";
    [Tooltip("Shown when the player has no augments yet.")]
    [SerializeField] private string emptyText = "—";
    [SerializeField] private float refreshInterval = 0.5f;

    private readonly List<GameObject> _rows = new List<GameObject>();
    private float _timer;
    private bool _layoutReady;
    private bool _subscribed;

    private RectTransform _autoContent;
    // Rows go in a dedicated child so we never put a ContentSizeFitter on the
    // panel itself (that would stretch its background). Assign `content` to a
    // ScrollRect's Content for scrolling; otherwise this child is created.
    private RectTransform Target
    {
        get
        {
            if (content != null) return content;
            if (_autoContent == null)
            {
                var go = new GameObject("AugList_Content", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
                _autoContent = rt;
            }
            return _autoContent;
        }
    }
    private int ResolvedIndex => playerIndex >= 0 ? playerIndex : 0;

    private void OnEnable()
    {
        EnsureLayout();
        TrySubscribe();
        Rebuild();
    }

    private void OnDisable()
    {
        if (_subscribed && AugmentRegistry.Instance != null)
            AugmentRegistry.Instance.OnAugmentAppliedByPlayer -= OnAugmentApplied;
        _subscribed = false;
    }

    private void Update()
    {
        // Best-effort event hookup (registry may not exist yet at OnEnable), plus a
        // cheap periodic refresh (unscaled — the pause screen runs at timeScale 0)
        // so rerolls/removals show even without an event.
        TrySubscribe();
        _timer += Time.unscaledDeltaTime;
        if (_timer >= refreshInterval) { _timer = 0f; Rebuild(); }
    }

    private void TrySubscribe()
    {
        if (_subscribed || AugmentRegistry.Instance == null) return;
        AugmentRegistry.Instance.OnAugmentAppliedByPlayer += OnAugmentApplied;
        _subscribed = true;
    }

    private void OnAugmentApplied(AugmentData data, int idx) => Rebuild();

    private void EnsureLayout()
    {
        if (_layoutReady) return;
        var t = Target;

        var vlg = t.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = t.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = t.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = t.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _layoutReady = true;
    }

    private List<int> AppliedIds()
    {
        if (AugmentRegistry.Instance == null) return new List<int>();
        return AugmentRegistry.Instance.GetAppliedAugments(ResolvedIndex) ?? new List<int>();
    }

    private void Rebuild()
    {
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i] != null) Destroy(_rows[i]);
        _rows.Clear();

        if (!string.IsNullOrEmpty(headerText)) AddRow(headerText, header: true);

        var ids = AppliedIds();
        if (ids.Count == 0) { AddRow(emptyText, header: false); return; }

        foreach (int id in ids)
        {
            var data = AugmentRegistry.Instance != null
                ? AugmentRegistry.Instance.GetAugmentData(id) : null;
            AddRow(data != null && !string.IsNullOrEmpty(data.Name)
                       ? data.Name : $"Augment {id}",
                   header: false);
        }
    }

    private void AddRow(string text, bool header)
    {
        TextMeshProUGUI tmp;
        if (rowPrefab != null)
        {
            tmp = Instantiate(rowPrefab, Target);
            tmp.gameObject.SetActive(true);
        }
        else
        {
            var go = new GameObject("AugRow", typeof(RectTransform));
            go.transform.SetParent(Target, false);
            tmp = go.AddComponent<TextMeshProUGUI>();
        }

        tmp.text = text;
        tmp.fontSize = header ? fontSize + 4f : fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = header ? FontStyles.Bold : FontStyles.Normal;
        //tmp.enableWordWrapping = true;
        _rows.Add(tmp.gameObject);
    }
}
