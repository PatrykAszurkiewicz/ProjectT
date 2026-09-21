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

    // PERF: ONE runtime material instance shared by the centre sprite and every tile.
    // Previously each tile called `sr.material.SetFloat(...)`, and reading
    // `.material` silently clones the material per renderer: ~100+ identical
    // material copies per biome, one draw call each, and none of them were ever
    // destroyed, so every biome switch leaked another full set. All tiles need the
    // same _BackgroundScale value, so one shared instance renders identically.
    private Material runtimeMaterial;
    private Material baseMaterial; // what the renderer had before we took over
    private static readonly int BackgroundScaleId = Shader.PropertyToID("_BackgroundScale");

    void Awake()
    {
        sourceSR = GetComponent<SpriteRenderer>();
        if (sourceSR == null || sourceSR.sprite == null)
        {
            Debug.LogError("[BackgroundTiler] No SpriteRenderer or Sprite found on this GameObject.");
            return;
        }

        sourceSR.sortingOrder = -100;
        //Patryk
        float tileWorldWidth = sourceSR.sprite.bounds.size.x * transform.lossyScale.x;
        TileWorldSize = tileWorldWidth;
        SetBackgroundScale(tileWorldWidth);
        //P
        GenerateTiles();
    }

    // Returns the shared material every background renderer should use. At runtime
    // this is a single instance owned by this component; in edit mode (ContextMenu)
    // the renderer's own shared material is used so no instances leak into the scene.
    private Material GetSharedBackgroundMaterial()
    {
        if (sourceSR == null) return null;
        Material baseMat = sourceSR.sharedMaterial;
        if (baseMat == null) return null;
        if (!Application.isPlaying) return baseMat;

        if (runtimeMaterial == null)
        {
            baseMaterial = baseMat;
            runtimeMaterial = new Material(baseMat) { name = baseMat.name + " (BackgroundTiler)" };
            sourceSR.sharedMaterial = runtimeMaterial;
        }
        else if (sourceSR.sharedMaterial != runtimeMaterial)
        {
            // Something assigned a different material to the source since (e.g. a
            // biome swap): rebuild the shared instance from it.
            Destroy(runtimeMaterial);
            baseMaterial = baseMat;
            runtimeMaterial = new Material(baseMat) { name = baseMat.name + " (BackgroundTiler)" };
            sourceSR.sharedMaterial = runtimeMaterial;
        }
        return runtimeMaterial;
    }

    //Patryk
    void SetBackgroundScale(float scale)
    {
        // Edit mode: leave the material asset untouched (the old code would have
        // cloned it via .material, which Unity warns about outside Play Mode).
        if (!Application.isPlaying) return;

        Material mat = GetSharedBackgroundMaterial();
        if (mat != null && mat.HasProperty(BackgroundScaleId))
            mat.SetFloat(BackgroundScaleId, scale);
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
        Material tileMaterial = Application.isPlaying ? GetSharedBackgroundMaterial() : sourceSR.sharedMaterial;

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
                //tileSR.sortingOrder = sourceSR.sortingOrder;
                tileSR.sortingOrder = -100;
                tileSR.drawMode = sourceSR.drawMode;
                tileSR.sharedMaterial = tileMaterial; // shared - never .material

                totalTiles++;
            }
        }

        //Debug.Log($"[BackgroundTiler] Generated {totalTiles} tiles ({2 * tilesPerDirection + 1}x{2 * tilesPerDirection + 1} grid). " +
        //          $"Tile size: {tileWorldSize.x:F1}x{tileWorldSize.y:F1} world units.");
        //Patryk
        // (_BackgroundScale is already set on the shared material above; every tile
        //  uses that same material, so no per-tile write is needed.)
        //P
    }

    void OnDestroy()
    {
        if (runtimeMaterial == null) return;

        // If only this component is removed (the sprite stays), hand the renderers
        // back their original material so nothing is left pointing at a destroyed
        // one. When the whole GameObject is going away this is harmless.
        if (sourceSR != null && sourceSR.sharedMaterial == runtimeMaterial)
            sourceSR.sharedMaterial = baseMaterial;
        for (int i = 0; i < transform.childCount; i++)
        {
            var sr = transform.GetChild(i).GetComponent<SpriteRenderer>();
            if (sr != null && sr.sharedMaterial == runtimeMaterial)
                sr.sharedMaterial = baseMaterial;
        }

        Destroy(runtimeMaterial);
        runtimeMaterial = null;
    }
}


