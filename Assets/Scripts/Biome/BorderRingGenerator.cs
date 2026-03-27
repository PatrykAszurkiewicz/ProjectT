using UnityEngine;
using System.Collections.Generic;


// Spawns densely packed prefab sprites in a ring outside the biome radius to create a natural, impassable border (e.g. a thick tree line).

public class BorderRingGenerator : MonoBehaviour
{
    //  Prefabs 
    [Header("Prefabs (set by BiomeManager per biome — null slots skipped)")]
    [Tooltip("Do NOT set these manually. BiomeManager assigns per-biome prefabs at runtime.")]
    public GameObject[] prefabs;

    //  Ring Geometry 
    [Header("Ring Geometry")]
    [Tooltip("Inner edge of the border ring. Set to -1 to auto-read from BiomeManager.")]
    public float innerRadius = -1f;

    [Tooltip("Width (thickness) of the border ring in world units.")]
    [Range(3f, 40f)]
    public float ringWidth = 12f;

    [Tooltip("Inward overlap so there's no visible gap between biome edge and border.")]
    public float overlapInward = 1.5f;

    //  Density 
    [Header("Density")]
    [Tooltip("Approximate spacing between prefab centers (smaller = denser).")]
    public float spacing = 0.8f;

    [Tooltip("Random jitter as fraction of spacing (0.3 = ±30%)")]
    [Range(0f, 0.45f)]
    public float jitter = 0.3f;

    //  Overlap Control 
    [Header("Overlap Control (set by BiomeManager per biome)")]
    [Tooltip("When true, sprites won't visually overlap. Uses sprite bounds to compute separation. " +
             "Great for rocks/boulders. Turn off for trees where overlap looks natural.")]
    public bool preventOverlap = false;

    [Tooltip("Extra padding between sprites when preventOverlap is on (world units). " +
             "0 = sprites touch edge-to-edge. 0.1 = small gap. Negative = allow slight overlap.")]
    public float overlapPadding = 0.05f;

    //  Scale 
    [Header("Scale")]
    public float baseScale = 0.5f;

    [Range(0f, 0.5f)]
    public float scaleVariation = 0.2f;

    //  Y-Sort 
    [Header("Y-Sort (must match GrassCartoonOverlay / PlayerMovement)")]
    public float sortPrecision = 10f;
    public int sortOrderBase = 1000;
    public float sortYOffset = -0.5f;

    [Header("Performance Tuning")]
    public int sortBandSize = 2;

    [Header("Material Override")]
    public bool forceDefaultSpriteMaterial = false;

    //  Collider 
    [Header("Collider")]
    public bool generateCollider = true;

    [Range(16, 128)]
    public int colliderSegments = 64;


    private GameObject containerGO;

    [ContextMenu("Generate Border")]
    public void GenerateBorder()
    {
        Cleanup();

        // ── Resolve inner radius ──
        float effectiveInner = innerRadius;
        if (effectiveInner < 0f)
        {
            BiomeManager bm = GetComponent<BiomeManager>();
            if (bm != null)
                effectiveInner = bm.grassCartoonSpawnRadius;
            else
                effectiveInner = 50f;
        }

        float ringInner = effectiveInner - overlapInward;
        float ringOuter = effectiveInner + ringWidth;

        //  Collect valid prefabs 
        List<GameObject> validPrefabs = new List<GameObject>();
        if (prefabs != null)
        {
            foreach (var p in prefabs)
                if (p != null) validPrefabs.Add(p);
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("[BorderRingGenerator] No prefabs assigned — skipping border generation.");
            return;
        }

        //  Container 
        containerGO = new GameObject("BorderRing_Container");
        containerGO.transform.SetParent(null);
        containerGO.transform.position = Vector3.zero;
        containerGO.transform.rotation = Quaternion.identity;
        containerGO.transform.localScale = Vector3.one;

        //  Extract sprite metadata from prefabs 
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
            Debug.LogError("[BorderRingGenerator] No valid SpriteRenderers found on prefabs.");
            return;
        }

        // Build prefab → sprite lookup + world-space half-widths for overlap checking
        Sprite[] prefabSprite = new Sprite[validPrefabs.Count];
        float[] prefabHalfW = new float[validPrefabs.Count];
        float[] prefabHalfH = new float[validPrefabs.Count];

        for (int i = 0; i < validPrefabs.Count; i++)
        {
            SpriteRenderer sr = validPrefabs[i].GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null)
            {
                prefabSprite[i] = sr.sprite;
                // Sprite bounds in local units (before our baseScale)
                prefabHalfW[i] = sr.sprite.bounds.extents.x;
                prefabHalfH[i] = sr.sprite.bounds.extents.y;
            }
        }

        //  Placement 
        var bands = new SortedDictionary<int, Dictionary<Sprite, List<QuadInstance>>>();

        float cellSize = Mathf.Max(0.1f, spacing);
        int gridHalf = Mathf.CeilToInt(ringOuter / cellSize) + 1;
        float innerSq = ringInner * ringInner;
        float outerSq = ringOuter * ringOuter;
        int prefabCount = validPrefabs.Count;
        int bandSize = Mathf.Max(1, sortBandSize);

        // Hash grid for overlap prevention
        // Cell size = largest possible sprite diameter at max scale, so we only check neighbors
        float maxSpriteRadius = 0f;
        if (preventOverlap)
        {
            for (int i = 0; i < prefabCount; i++)
            {
                float maxScale = baseScale * (1f + scaleVariation);
                float r = Mathf.Max(prefabHalfW[i], prefabHalfH[i]) * maxScale + overlapPadding;
                if (r > maxSpriteRadius) maxSpriteRadius = r;
            }
        }

        float hashCellSize = preventOverlap ? Mathf.Max(0.5f, maxSpriteRadius * 2f) : 1f;
        var spatialHash = preventOverlap ? new Dictionary<long, List<PlacedInstance>>() : null;

        int spawned = 0;

        for (int gx = -gridHalf; gx <= gridHalf; gx++)
        {
            for (int gy = -gridHalf; gy <= gridHalf; gy++)
            {
                float cx = gx * cellSize;
                float cy = gy * cellSize;

                float jx = cx + Random.Range(-jitter, jitter) * cellSize;
                float jy = cy + Random.Range(-jitter, jitter) * cellSize;

                float distSq = jx * jx + jy * jy;
                if (distSq < innerSq || distSq > outerSq)
                    continue;

                int prefabIdx = Random.Range(0, prefabCount);
                Sprite spr = prefabSprite[prefabIdx];
                if (spr == null) continue;

                float variation = Random.Range(1f - scaleVariation, 1f + scaleVariation);
                float s = baseScale * variation;

                //  Overlap check 
                if (preventOverlap)
                {
                    float myHW = prefabHalfW[prefabIdx] * s + overlapPadding;
                    float myHH = prefabHalfH[prefabIdx] * s + overlapPadding;
                    float myRadius = Mathf.Max(myHW, myHH);

                    if (OverlapsExisting(spatialHash, hashCellSize, jx, jy, myRadius))
                        continue;

                    // Register in spatial hash
                    int hx = Mathf.FloorToInt(jx / hashCellSize);
                    int hy = Mathf.FloorToInt(jy / hashCellSize);
                    long key = HashKey(hx, hy);

                    if (!spatialHash.TryGetValue(key, out var bucket))
                    {
                        bucket = new List<PlacedInstance>();
                        spatialHash[key] = bucket;
                    }
                    bucket.Add(new PlacedInstance { x = jx, y = jy, radius = myRadius });
                }

                float sortY = jy + sortYOffset;
                int sortOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);
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

        //  Build baked meshes 
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

        //  Collider 
        if (generateCollider)
            BuildEdgeCollider(ringInner);

        //Debug.Log($"[BorderRingGenerator] Baked {spawned} border quads into {meshCount} band meshes " +
        //          $"(inner={ringInner:F1}, outer={ringOuter:F1}, overlap={!preventOverlap}, " +
        //          $"prefabs={validPrefabs.Count}).");
    }

    //  Spatial Hash Overlap Check 

    private bool OverlapsExisting(Dictionary<long, List<PlacedInstance>> hash, float cellSize,
                                   float x, float y, float radius)
    {
        int cx = Mathf.FloorToInt(x / cellSize);
        int cy = Mathf.FloorToInt(y / cellSize);

        // Check 3×3 neighborhood
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                long key = HashKey(cx + dx, cy + dy);
                if (!hash.TryGetValue(key, out var bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    PlacedInstance p = bucket[i];
                    float minDist = radius + p.radius;
                    float ddx = x - p.x;
                    float ddy = y - p.y;
                    if (ddx * ddx + ddy * ddy < minDist * minDist)
                        return true;
                }
            }
        }

        return false;
    }

    private static long HashKey(int x, int y)
    {
        return ((long)x << 32) | (uint)y;
    }

    //  Mesh Building 

    private void BuildBandMesh(SpriteMeta meta, Material sharedMat, List<QuadInstance> quads, int bandSortOrder, int batchId)
    {
        Sprite spr = meta.sprite;

        Rect texRect = spr.textureRect;
        float texW = spr.texture.width;
        float texH = spr.texture.height;
        float uvLeft = texRect.x / texW;
        float uvBottom = texRect.y / texH;
        float uvRight = (texRect.x + texRect.width) / texW;
        float uvTop = (texRect.y + texRect.height) / texH;

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
        mesh.name = $"BorderBand_{batchId}";

        if (vertices.Length > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        GameObject bandGO = new GameObject($"BorderBand_{batchId}");
        bandGO.transform.SetParent(containerGO.transform, false);
        bandGO.transform.localPosition = Vector3.zero;
        bandGO.transform.localRotation = Quaternion.identity;
        bandGO.transform.localScale = Vector3.one;

        MeshFilter mf = bandGO.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = bandGO.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sharedMat;
        mr.sortingOrder = bandSortOrder;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    //  Collider 

    private void BuildEdgeCollider(float radius)
    {
        GameObject colliderGO = new GameObject("BorderRing_Collider");
        colliderGO.transform.SetParent(containerGO.transform, false);
        colliderGO.transform.position = Vector3.zero;

        Rigidbody2D rb = colliderGO.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;

        EdgeCollider2D edge = colliderGO.AddComponent<EdgeCollider2D>();

        int segments = Mathf.Max(16, colliderSegments);
        Vector2[] points = new Vector2[segments + 1];

        for (int i = 0; i < segments; i++)
        {
            float angle = i * (2f * Mathf.PI / segments);
            points[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
        points[segments] = points[0];

        edge.points = points;
        edge.edgeRadius = 0.1f;
    }

    //  Cleanup 

    [ContextMenu("Remove Border")]
    public void Cleanup()
    {
        if (containerGO == null) return;

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

        if (Application.isPlaying) Destroy(containerGO);
        else DestroyImmediate(containerGO);

        containerGO = null;
    }

    void OnDestroy()
    {
        Cleanup();
    }

    //  Data Structures 

    private struct QuadInstance
    {
        public float x, y, scale;
        public int sortOrder;
    }

    private struct PlacedInstance
    {
        public float x, y, radius;
    }

    private class SpriteMeta
    {
        public Sprite sprite;
        public Material material;
    }
}





