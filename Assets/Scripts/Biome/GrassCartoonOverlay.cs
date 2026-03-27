using UnityEngine;
using System.Collections.Generic;


public class GrassCartoonOverlay : MonoBehaviour
{
    [Header("Distribution")]
    public int instanceCount = 40000;
    public float spawnRadius = 60f;
    public float coreExclusionRadius = 1.5f;

    [Header("Prefabs (set by BiomeManager — null slots are skipped)")]
    public GameObject[] prefabs;

    [Header("Scale")]
    [Tooltip("Base scale for prefab instances. Prefabs are natively 0.5, so 0.5 = original size.")]
    public float baseScale = 0.5f;

    [Tooltip("Random variation around baseScale (0.15 = ±15%)")]
    [Range(0f, 0.5f)]
    public float scaleVariation = 0.15f;

    [Header("Y-Sort Settings")]
    [Tooltip("Multiplier for converting Y position to sortingOrder. " +
             "Higher = finer sort granularity. 10 = 0.1 world-unit precision.")]
    public float sortPrecision = 10f;

    [Tooltip("Base offset added to all sortingOrders so they stay above the background " +
             "(which uses sortingOrder around -1). Must be larger than spawnRadius * sortPrecision. " +
             "Default 1000 handles spawnRadius up to 100 at precision 10.")]
    public int sortOrderBase = 1000;

    [Header("Material Override")]
    public bool forceDefaultSpriteMaterial = false;

    [Header("Performance Tuning")]
    [Tooltip("How many sortingOrder values to group into one mesh. " +
             "1 = pixel-perfect sort (more meshes), 5 = fewer meshes but coarser sort. " +
             "2 is a good balance: 0.2 world-unit bands at precision 10.")]
    public int sortBandSize = 2;

    private GameObject containerGO;

    public void GenerateCartoonGrass()
    {
        // Clean up previous
        if (containerGO != null)
        {
            CleanupContainer();
            containerGO = null;
        }

        // Collect valid (non-null) prefabs
        List<GameObject> validPrefabs = new List<GameObject>();
        if (prefabs != null)
        {
            foreach (var p in prefabs)
                if (p != null) validPrefabs.Add(p);
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogError("[GrassCartoonOverlay] No prefabs assigned — nothing to spawn.");
            return;
        }

        // Root container at world origin
        containerGO = new GameObject("GrassCartoon_Container");
        containerGO.transform.SetParent(null);
        containerGO.transform.position = Vector3.zero;
        containerGO.transform.rotation = Quaternion.identity;
        containerGO.transform.localScale = Vector3.one;

        //  Extract sprite info from prefabs 
        var spriteMeta = new Dictionary<Sprite, SpriteMeta>();

        foreach (var prefab in validPrefabs)
        {
            SpriteRenderer sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null || sr.sprite == null) continue;

            if (!spriteMeta.ContainsKey(sr.sprite))
            {
                spriteMeta[sr.sprite] = new SpriteMeta
                {
                    sprite = sr.sprite,
                    material = sr.sharedMaterial
                };
            }
        }

        if (spriteMeta.Count == 0)
        {
            Debug.LogError("[GrassCartoonOverlay] No valid SpriteRenderers found on prefabs.");
            return;
        }

        // Build lookup prefab index → sprite
        Sprite[] prefabSprite = new Sprite[validPrefabs.Count];
        for (int i = 0; i < validPrefabs.Count; i++)
        {
            SpriteRenderer sr = validPrefabs[i].GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null)
                prefabSprite[i] = sr.sprite;
        }

        //  Jittered-grid placement 


        var bands = new SortedDictionary<int, Dictionary<Sprite, List<QuadInstance>>>();

        int prefabCount = validPrefabs.Count;
        float coreExclSq = coreExclusionRadius * coreExclusionRadius;
        float radiusSq = spawnRadius * spawnRadius;

        float area = Mathf.PI * spawnRadius * spawnRadius;
        float cellSize = Mathf.Sqrt(area / (float)instanceCount);
        int gridHalf = Mathf.CeilToInt(spawnRadius / cellSize);

        int spawned = 0;
        int bandSize = Mathf.Max(1, sortBandSize);

        for (int gx = -gridHalf; gx <= gridHalf; gx++)
        {
            for (int gy = -gridHalf; gy <= gridHalf; gy++)
            {
                float cx = gx * cellSize;
                float cy = gy * cellSize;
                float distSq = cx * cx + cy * cy;

                if (distSq > radiusSq) continue;
                if (distSq < coreExclSq) continue;

                float jx = cx + Random.Range(-0.45f, 0.45f) * cellSize;
                float jy = cy + Random.Range(-0.45f, 0.45f) * cellSize;

                float variation = Random.Range(1f - scaleVariation, 1f + scaleVariation);
                float s = baseScale * variation;

                int prefabIdx = Random.Range(0, prefabCount);
                Sprite spr = prefabSprite[prefabIdx];
                if (spr == null) continue;

                int sortOrder = sortOrderBase + Mathf.RoundToInt(-jy * sortPrecision);
                int bandKey = sortOrder / bandSize;

                if (!bands.TryGetValue(bandKey, out var spriteMap))
                {
                    spriteMap = new Dictionary<Sprite, List<QuadInstance>>();
                    bands[bandKey] = spriteMap;
                }

                if (!spriteMap.TryGetValue(spr, out var quadList))
                {
                    quadList = new List<QuadInstance>();
                    spriteMap[spr] = quadList;
                }

                quadList.Add(new QuadInstance
                {
                    x = jx,
                    y = jy,
                    scale = s,
                    sortOrder = sortOrder
                });

                spawned++;
            }
        }

        //  Build one combined mesh per band per sprite 

        var sharedMaterials = new Dictionary<Sprite, Material>();
        foreach (var kvp in spriteMeta)
        {
            Sprite spr = kvp.Key;
            SpriteMeta meta = kvp.Value;

            Material mat;
            if (forceDefaultSpriteMaterial || meta.material == null)
                mat = new Material(Shader.Find("Sprites/Default"));
            else
                mat = new Material(meta.material);

            mat.mainTexture = spr.texture;

            float tileW = BackgroundTiler.TileWorldSize;
            //Debug.Log($"[GrassCartoon] TileWorldSize = {BackgroundTiler.TileWorldSize}");
            if (tileW > 0f)
                mat.SetFloat("_BackgroundScale", tileW);

            sharedMaterials[spr] = mat;
        }

        int meshCount = 0;

        foreach (var bandKvp in bands)
        {
            int bandKey = bandKvp.Key;
            int bandSortOrder = bandKey * bandSize;

            foreach (var spriteKvp in bandKvp.Value)
            {
                Sprite spr = spriteKvp.Key;
                List<QuadInstance> quads = spriteKvp.Value;
                if (quads.Count == 0) continue;

                BuildBandMesh(spriteMeta[spr], sharedMaterials[spr], quads, bandSortOrder, meshCount);
                meshCount++;
            }
        }

        //  Camera culling
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.ResetCullingMatrix();

            float oversizeFactor = 10f;
            if (cam.orthographic)
            {
                float h = cam.orthographicSize * oversizeFactor;
                float w = h * cam.aspect;
                Matrix4x4 proj = Matrix4x4.Ortho(-w, w, -h, h, cam.nearClipPlane, cam.farClipPlane);
                cam.cullingMatrix = proj * cam.worldToCameraMatrix;
            }
        }

        //Debug.Log($"[GrassCartoonOverlay] Baked {spawned} grass quads into {meshCount} band meshes " +
        //          $"(bandSize={bandSize}, {bands.Count} bands, {spriteMeta.Count} sprite(s)).");
    }

    //  Mesh building 

    private void BuildBandMesh(SpriteMeta meta, Material sharedMat, List<QuadInstance> quads, int bandSortOrder, int batchId)
    {
        Sprite spr = meta.sprite;

        // Sprite UV rect
        Rect texRect = spr.textureRect;
        float texW = spr.texture.width;
        float texH = spr.texture.height;
        float uvLeft = texRect.x / texW;
        float uvBottom = texRect.y / texH;
        float uvRight = (texRect.x + texRect.width) / texW;
        float uvTop = (texRect.y + texRect.height) / texH;

        // Sprite extents in local space
        float ppu = spr.pixelsPerUnit;
        Vector2 pivot = spr.pivot;
        float halfW = texRect.width / ppu * 0.5f;
        float halfH = texRect.height / ppu * 0.5f;
        float pivotOffX = (texRect.width * 0.5f - pivot.x) / ppu;
        float pivotOffY = (texRect.height * 0.5f - pivot.y) / ppu;

        int count = quads.Count;
        Vector3[] vertices = new Vector3[count * 4];
        Vector2[] uvs = new Vector2[count * 4];
        Color32[] colors = new Color32[count * 4];
        int[] triangles = new int[count * 6];

        Color32 white = new Color32(255, 255, 255, 255);

        for (int i = 0; i < count; i++)
        {
            QuadInstance q = quads[i];
            int vi = i * 4;
            int ti = i * 6;

            float s = q.scale;
            float left = q.x + (pivotOffX - halfW) * s;
            float right = q.x + (pivotOffX + halfW) * s;
            float bottom = q.y + (pivotOffY - halfH) * s;
            float top = q.y + (pivotOffY + halfH) * s;

            vertices[vi + 0] = new Vector3(left, bottom, 0f);
            vertices[vi + 1] = new Vector3(left, top, 0f);
            vertices[vi + 2] = new Vector3(right, top, 0f);
            vertices[vi + 3] = new Vector3(right, bottom, 0f);

            uvs[vi + 0] = new Vector2(uvLeft, uvBottom);
            uvs[vi + 1] = new Vector2(uvLeft, uvTop);
            uvs[vi + 2] = new Vector2(uvRight, uvTop);
            uvs[vi + 3] = new Vector2(uvRight, uvBottom);

            colors[vi + 0] = white;
            colors[vi + 1] = white;
            colors[vi + 2] = white;
            colors[vi + 3] = white;

            triangles[ti + 0] = vi + 0;
            triangles[ti + 1] = vi + 1;
            triangles[ti + 2] = vi + 2;
            triangles[ti + 3] = vi + 0;
            triangles[ti + 4] = vi + 2;
            triangles[ti + 5] = vi + 3;
        }

        Mesh mesh = new Mesh();
        mesh.name = $"GrassBand_{batchId}";
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        //  Create GameObject for this band 
        GameObject bandGO = new GameObject($"GrassBand_{batchId}");
        bandGO.transform.SetParent(containerGO.transform, false);
        bandGO.transform.localPosition = Vector3.zero;
        bandGO.transform.localRotation = Quaternion.identity;
        bandGO.transform.localScale = Vector3.one;

        MeshFilter mf = bandGO.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = bandGO.AddComponent<MeshRenderer>();

        // Share one material per sprite type 
        mr.sharedMaterial = sharedMat;

        // Each band gets its own sortingOrder
        mr.sortingOrder = bandSortOrder;

        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // LateUpdate: keep culling matrix in sync 

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic && containerGO != null)
        {
            float oversizeFactor = 10f;
            float h = cam.orthographicSize * oversizeFactor;
            float w = h * cam.aspect;
            Matrix4x4 proj = Matrix4x4.Ortho(-w, w, -h, h, cam.nearClipPlane, cam.farClipPlane);
            cam.cullingMatrix = proj * cam.worldToCameraMatrix;
        }
    }

    //  Cleanup 

    private void CleanupContainer()
    {
        if (containerGO == null) return;

        // Collect unique materials to destroy (shared across bands)
        HashSet<Material> uniqueMats = new HashSet<Material>();

        foreach (Transform child in containerGO.transform)
        {
            MeshFilter mf = child.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                if (Application.isPlaying) Destroy(mf.sharedMesh);
                else DestroyImmediate(mf.sharedMesh);
            }

            MeshRenderer mr = child.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null)
                uniqueMats.Add(mr.sharedMaterial);
        }

        foreach (var mat in uniqueMats)
        {
            if (Application.isPlaying) Destroy(mat);
            else DestroyImmediate(mat);
        }

        if (Application.isPlaying)
            Destroy(containerGO);
        else
            DestroyImmediate(containerGO);
    }

    void OnDestroy()
    {
        Camera cam = Camera.main;
        if (cam != null)
            cam.ResetCullingMatrix();

        CleanupContainer();
    }

    //  Data structures 

    private struct QuadInstance
    {
        public float x, y, scale;
        public int sortOrder;
    }

    private class SpriteMeta
    {
        public Sprite sprite;
        public Material material;
    }
}
