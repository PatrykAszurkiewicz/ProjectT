using UnityEngine;


// Hold-to-revive progress bar. Created at runtime by
// PlayerTowerPlacer and floated above the downed teammate while a revive is in
// progress. Procedural (no prefab) to match the rest of the co-op UI. Built
// from a left-pivot 1x1 quad so the fill grows cleanly from the left edge.

public class ReviveProgressBar : MonoBehaviour
{
    public Vector3 worldOffset = new Vector3(0f, 1.1f, 0f);
    public float width = 1.4f;
    public float height = 0.18f;
    public Color backColor = new Color(0f, 0f, 0f, 0.65f);
    public Color fillColor = new Color(0.30f, 1f, 0.55f, 1f);
    public int sortingOrder = 3200; // above grass Y-sort, below fog (5000)

    private Transform _fill;
    private bool _built;

    private static Sprite _quad;
    private static Sprite LeftPivotQuad()
    {
        if (_quad == null)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            // Pivot at the left-middle so X-scale fills from the left.
            _quad = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
        }
        return _quad;
    }

    private void Build()
    {
        if (_built) return;
        _built = true;

        var back = new GameObject("Back");
        back.transform.SetParent(transform, false);
        back.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
        back.transform.localScale = new Vector3(width, height, 1f);
        var backSr = back.AddComponent<SpriteRenderer>();
        backSr.sprite = LeftPivotQuad();
        backSr.color = backColor;
        backSr.sortingOrder = sortingOrder;

        var fill = new GameObject("Fill");
        fill.transform.SetParent(transform, false);
        fill.transform.localPosition = new Vector3(-width * 0.5f, 0f, -0.01f);
        fill.transform.localScale = new Vector3(0f, height * 0.8f, 1f);
        var fillSr = fill.AddComponent<SpriteRenderer>();
        fillSr.sprite = LeftPivotQuad();
        fillSr.color = fillColor;
        fillSr.sortingOrder = sortingOrder + 1;
        _fill = fill.transform;
    }

    /// <summary>Position the bar above <paramref name="targetWorldPos"/> and set fill 0..1.</summary>
    public void Show(Vector3 targetWorldPos, float progress01)
    {
        if (!_built) Build();
        transform.position = targetWorldPos + worldOffset;
        float p = Mathf.Clamp01(progress01);
        var s = _fill.localScale;
        s.x = width * p;
        _fill.localScale = s;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }
}
