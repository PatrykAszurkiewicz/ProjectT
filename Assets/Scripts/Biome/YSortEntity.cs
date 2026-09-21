using UnityEngine;

/// <summary>
/// Attach to any GameObject that should Y-sort against grass.
/// Updates sortingOrder each frame based on Y position.
/// Cost: 1 integer assignment per frame per entity — negligible.
///
/// sortPrecision and sortOrderBase MUST match GrassCartoonOverlay values.
/// Supports both SpriteRenderer and LineRenderer.
///
/// SORTING OVERRIDE
/// Callers (e.g. a burrowing insect that must render UNDER the terrain while
/// tunnelling) can pin the sorting order to a fixed value with
/// SetSortingOverride(order). While an override is active the per-frame Y-sort
/// is bypassed entirely. ClearSortingOverride() resumes normal Y-sorting.
/// CurrentOrder always reflects the value last written, so other renderers
/// (e.g. a mound overlay or aura) can chase it without re-deriving it.
/// </summary>
public class YSortEntity : MonoBehaviour
{
    [Tooltip("Must match GrassCartoonOverlay.sortPrecision")]
    public float sortPrecision = 10f;

    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase")]
    public int sortOrderBase = 1000;

    [Tooltip("Y offset for the sort point. Negative = sort from lower (feet).")]
    public float sortYOffset = 0f;

    /// <summary>The sortingOrder written on the most recent LateUpdate (Y-sorted or overridden).</summary>
    public int CurrentOrder { get; private set; }

    /// <summary>True while a fixed override is pinning the sorting order.</summary>
    public bool HasOverride => _hasOverride;

    private bool _hasOverride;
    private int _overrideOrder;

    private SpriteRenderer sr;
    private LineRenderer lr;

    /// <summary>Pin the sorting order to a fixed value, bypassing Y-sort. Applied immediately.</summary>
    public void SetSortingOverride(int order)
    {
        _hasOverride = true;
        _overrideOrder = order;
        ApplyOrder(order);
    }

    /// <summary>Resume normal Y-sorting on the next LateUpdate.</summary>
    public void ClearSortingOverride()
    {
        _hasOverride = false;
    }

    void Start()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
            lr = GetComponentInChildren<LineRenderer>();
    }

    void LateUpdate()
    {
        int order;
        if (_hasOverride)
        {
            order = _overrideOrder;
        }
        else
        {
            float sortY = transform.position.y + sortYOffset;
            order = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);
        }

        ApplyOrder(order);
    }

    private void ApplyOrder(int order)
    {
        CurrentOrder = order;

        // Resolve renderers lazily so an override applied before Start() still lands.
        if (sr == null && lr == null)
        {
            sr = GetComponentInChildren<SpriteRenderer>();
            if (sr == null) lr = GetComponentInChildren<LineRenderer>();
        }

        if (sr != null)
            sr.sortingOrder = order;
        else if (lr != null)
            lr.sortingOrder = order;
    }
}


