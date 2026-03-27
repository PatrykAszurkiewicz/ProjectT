using UnityEngine;

/// <summary>
/// Attach to any GameObject that should Y-sort against grass.
/// Updates sortingOrder each frame based on Y position.
/// Cost: 1 integer assignment per frame per entity — negligible.
///
/// sortPrecision and sortOrderBase MUST match GrassCartoonOverlay values.
/// Supports both SpriteRenderer and LineRenderer.
/// </summary>
public class YSortEntity : MonoBehaviour
{
    [Tooltip("Must match GrassCartoonOverlay.sortPrecision")]
    public float sortPrecision = 10f;

    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase")]
    public int sortOrderBase = 1000;

    [Tooltip("Y offset for the sort point. Negative = sort from lower (feet).")]
    public float sortYOffset = 0f;

    private SpriteRenderer sr;
    private LineRenderer lr;

    void Start()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
            lr = GetComponentInChildren<LineRenderer>();
    }

    void LateUpdate()
    {
        float sortY = transform.position.y + sortYOffset;
        int order = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);

        if (sr != null)
            sr.sortingOrder = order;
        else if (lr != null)
            lr.sortingOrder = order;
    }
}
