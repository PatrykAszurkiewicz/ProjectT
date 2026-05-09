using UnityEngine;


public class BackgroundTiler : MonoBehaviour
{
    [Header("Tiling Settings")]
    [Tooltip("How many tiles in each direction from center (e.g. 3 = 7x7 grid)")]
    public int tilesPerDirection = 3;

    [Tooltip("Slight overlap in world units to prevent seam lines")]
    public float overlapPixels = 0.01f;

    [Header("Auto-Configuration")]
    [Tooltip("If true, automatically calculates grid size from camera/map radius")]
    public bool autoCalculateGrid = true;

    [Tooltip("Used when autoCalculateGrid is true — covers this radius from origin")]
    public float coverageRadius = 25f;

    private SpriteRenderer sourceSR;
    //Patryk
    public static float TileWorldSize { get; private set; }
    //P
    void Awake()
    {
        sourceSR = GetComponent<SpriteRenderer>();
        if (sourceSR == null || sourceSR.sprite == null)
        {
            Debug.LogError("[BackgroundTiler] No SpriteRenderer or Sprite found on this GameObject.");
            return;
        }
        //Patryk
        float tileWorldWidth = sourceSR.sprite.bounds.size.x * transform.lossyScale.x;
        TileWorldSize = tileWorldWidth;
        SetBackgroundScale(tileWorldWidth);
        //P
        GenerateTiles();
    }
    //Patryk
    void SetBackgroundScale(float scale)
    {
        if (sourceSR.material != null)
            sourceSR.material.SetFloat("_BackgroundScale", scale);
    }
    //P
    [ContextMenu("Generate Tiles")]
    public void GenerateTiles()
    {
        // Clean up any previously generated tiles
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        if (sourceSR == null) sourceSR = GetComponent<SpriteRenderer>();
        if (sourceSR == null || sourceSR.sprite == null) return;

        // Calculate tile size in world units (accounting for scale)
        Vector2 tileWorldSize = new Vector2(
            sourceSR.sprite.bounds.size.x * transform.lossyScale.x,
            sourceSR.sprite.bounds.size.y * transform.lossyScale.y
        );

        TileWorldSize = tileWorldSize.x;
        SetBackgroundScale(TileWorldSize);

        // Auto-calculate how many tiles we need
        if (autoCalculateGrid)
        {
            // Also consider camera size if available
            float neededRadius = coverageRadius;
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                float camHeight = cam.orthographicSize * 2f;
                float camWidth = camHeight * cam.aspect;
                float camRadius = Mathf.Max(camWidth, camHeight) * 0.5f;
                neededRadius = Mathf.Max(neededRadius, camRadius + 5f);
            }

            tilesPerDirection = Mathf.CeilToInt(neededRadius / Mathf.Min(tileWorldSize.x, tileWorldSize.y)) + 1;
        }

        // Create grid of tile copies
        int totalTiles = 0;
        for (int x = -tilesPerDirection; x <= tilesPerDirection; x++)
        {
            for (int y = -tilesPerDirection; y <= tilesPerDirection; y++)
            {
                if (x == 0 && y == 0) continue; // Skip center

                Vector3 offset = new Vector3(
                    x * (tileWorldSize.x - overlapPixels),
                    y * (tileWorldSize.y - overlapPixels),
                    0f
                );

                GameObject tile = new GameObject($"BG_Tile_{x}_{y}");
                tile.transform.SetParent(transform);
                tile.transform.localPosition = offset / transform.lossyScale.x; // Compensate for parent scale
                tile.transform.localRotation = Quaternion.identity;
                tile.transform.localScale = Vector3.one;

                SpriteRenderer tileSR = tile.AddComponent<SpriteRenderer>();
                tileSR.sprite = sourceSR.sprite;
                tileSR.color = sourceSR.color;
                tileSR.sortingLayerName = sourceSR.sortingLayerName;
                tileSR.sortingOrder = sourceSR.sortingOrder;
                tileSR.drawMode = sourceSR.drawMode;
                tileSR.sharedMaterial = sourceSR.sharedMaterial;

                totalTiles++;
            }
        }

        //Debug.Log($"[BackgroundTiler] Generated {totalTiles} tiles ({2 * tilesPerDirection + 1}x{2 * tilesPerDirection + 1} grid). " +
        //          $"Tile size: {tileWorldSize.x:F1}x{tileWorldSize.y:F1} world units.");
        //Patryk
        float tileW = sourceSR.sprite.bounds.size.x * transform.lossyScale.x;
        foreach (Transform child in transform)
        {
            var sr = child.GetComponent<SpriteRenderer>();
            if (sr != null && sr.material != null)
                sr.material.SetFloat("_BackgroundScale", tileW);
        }
        //P
    }
}
