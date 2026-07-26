using UnityEngine;
using System.Collections.Generic;

// VORTEX PATH INDICATOR
// A standalone twin of ChestPathIndicator / GremlinPathIndicator that lays
// footprints from the player to the nearest Vortex. Same footprint mechanics and
// grass Y-sort; the prints are tinted LIGHT RED so the vortex trail reads apart
// from the gremlin (dark) and chest (papyrus) trails. Finds vortices by the
// VortexSpawner component — no tag dependency.
// Add this once, anywhere in the scene (or let VortexSpawner spawn it — see the
// autoCreateIndicator hook on that script).
public class VortexPathIndicator : MonoBehaviour
{
    [Header("Path Settings")]
    public float footprintSpacing = 1.3f;
    public float maxPathDistance = 100f;
    public float footprintScale = 0.9f;
    [Range(0f, 1f)] public float footprintAlpha = 1f;
    public float updateInterval = 1.0f;
    public bool alternateFootOrientation = true;

    [Tooltip("Stop the trail this far (world units) before the vortex, so it ends " +
             "in front of it rather than under the disk.")]
    public float stopBeforeVortex = 2.2f;

    [Header("Tint")]
    [Tooltip("Light red so the trail is unmistakably the vortex's. A bright, hot " +
             "red reads against grass without looking like blood.")]
    public Color footprintTint = new Color(1f, 0.30f, 0.28f, 1f);

    [Tooltip("Dark halo drawn behind each footprint so it stays legible over busy " +
             "grass. Alpha 0 disables it.")]
    public Color footprintOutline = new Color(0.25f, 0.02f, 0.05f, 0.8f);

    [Tooltip("Halo size relative to the footprint. 1.3 = a thin dark rim.")]
    public float outlineScale = 1.35f;

    [Header("Sorting")]
    [Tooltip("Added to the grass-matched sort order (base 1000 + round(-y*10)). " +
             "Positive draws prints in FRONT of nearby grass.")]
    public int footprintSortOffset = 8;

    [Header("Animation")]
    public bool fadeOutOldFootprints = true;
    public float footprintLifetime = 2.0f;
    public float fadeOutDuration = 0.6f;

    [Header("Debug")]
    public bool enableDebugLogs = false;

    private Transform playerTransform;
    private readonly List<GameObject> footprintObjects = new List<GameObject>();
    private Sprite footprintSprite;
    private float updateTimer;
    private bool isLeftFoot = true;

    void Start()
    {
        FindPlayer();
        footprintSprite = CreateFallbackFootprint();
        updateTimer = updateInterval;
    }

    void FindPlayer()
    {
        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) { playerTransform = pm.transform; return; }
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerTransform = p.transform;
    }

    // A soft light foot shape (white with soft alpha) so footprintTint colours it.
    // A dark silhouette tinted red would just read as dark red — same trap the chest
    // trail documents.
    Sprite CreateFallbackFootprint()
    {
        int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var colors = new Color[size * size];

        Vector2 ball = new Vector2(size * 0.5f, size * 0.60f);
        Vector2 heel = new Vector2(size * 0.5f, size * 0.24f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float bx = (x - ball.x) / (size * 0.27f);
                float by = (y - ball.y) / (size * 0.36f);
                float db = bx * bx + by * by;

                float hx = (x - heel.x) / (size * 0.18f);
                float hy = (y - heel.y) / (size * 0.22f);
                float dh = hx * hx + hy * hy;

                float d = Mathf.Min(db, dh);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * 0.95f;
                colors[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    void Update()
    {
        updateTimer -= Time.deltaTime;
        if (updateTimer <= 0f)
        {
            updateTimer = updateInterval;
            UpdatePath();
        }

        // Trail vanishes the moment the last vortex is gone.
        if (footprintObjects.Count > 0 && FindClosestVortex() == null)
            ClearPath();
    }

    void UpdatePath()
    {
        if (playerTransform == null) { FindPlayer(); return; }
        if (footprintSprite == null) return;

        GameObject target = FindClosestVortex();
        if (target == null) { ClearPath(); return; }

        Vector3 playerPos = playerTransform.position;
        Vector3 targetPos = target.transform.position;
        float distance = Vector3.Distance(playerPos, targetPos);

        if (distance > maxPathDistance) { ClearPath(); return; }

        ClearPath();

        // The vortex's own order — prints clamp just below so the disk covers the end.
        var vortexSR = target.GetComponentInChildren<SpriteRenderer>();
        int vortexOrder = vortexSR != null ? vortexSR.sortingOrder : int.MaxValue;

        float usable = Mathf.Max(0f, distance - stopBeforeVortex);
        int footprintCount = Mathf.FloorToInt(usable / footprintSpacing);
        if (footprintCount <= 0) return;

        Vector3 direction = (targetPos - playerPos).normalized;
        for (int i = 1; i <= footprintCount; i++)
        {
            float t = (float)i / footprintCount;
            Vector3 position = playerPos + direction * (usable * t);
            CreateFootprint(position, direction, i, vortexOrder);
        }

        if (enableDebugLogs)
            Debug.Log($"[VortexPath] Vortex {distance:F1}u away — laid {footprintCount} footprints.");
    }

    void CreateFootprint(Vector3 position, Vector3 direction, int index, int vortexOrder)
    {
        var footprint = new GameObject($"VortexFootprint_{index}");
        footprint.transform.position = position;
        footprint.transform.SetParent(transform);

        var renderer = footprint.AddComponent<SpriteRenderer>();
        renderer.sprite = footprintSprite;
        renderer.sortingLayerName = "Default";

        const int kSortBase = 1000;
        const float kSortPrecision = 10f;
        int order = kSortBase + Mathf.RoundToInt(-position.y * kSortPrecision) + footprintSortOffset;
        if (vortexOrder != int.MaxValue) order = Mathf.Min(order, vortexOrder - 1);
        renderer.sortingOrder = order;

        Color color = footprintTint;
        color.a = footprintAlpha;
        renderer.color = color;

        // Dark halo behind the print so it reads over any background. Same sprite,
        // scaled up, dark, one order lower.
        if (footprintOutline.a > 0f)
        {
            var halo = new GameObject("Halo");
            halo.transform.SetParent(footprint.transform, false);
            halo.transform.localScale = Vector3.one * outlineScale;
            var hr = halo.AddComponent<SpriteRenderer>();
            hr.sprite = footprintSprite;
            hr.sortingLayerName = "Default";
            hr.sortingOrder = renderer.sortingOrder - 1;
            hr.color = footprintOutline;
        }

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (alternateFootOrientation)
        {
            angle += isLeftFoot ? -15f : 15f;
            isLeftFoot = !isLeftFoot;
        }
        footprint.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        footprint.transform.localScale = Vector3.one * footprintScale;

        footprintObjects.Add(footprint);

        if (fadeOutOldFootprints) StartCoroutine(FadeOutFootprint(footprint, renderer));
        else Destroy(footprint, footprintLifetime);
    }

    System.Collections.IEnumerator FadeOutFootprint(GameObject footprint, SpriteRenderer renderer)
    {
        yield return new WaitForSeconds(footprintLifetime);
        if (footprint == null)
        {
            footprintObjects.Remove(footprint);
            yield break;
        }

        // Fade every renderer on the print (main + halo) together.
        var renderers = footprint.GetComponentsInChildren<SpriteRenderer>();
        var startAlphas = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            startAlphas[i] = renderers[i] != null ? renderers[i].color.a : 0f;

        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            if (footprint == null) { footprintObjects.Remove(footprint); yield break; }
            elapsed += Time.deltaTime;
            float t = elapsed / fadeOutDuration;
            float s = t * t * (3f - 2f * t);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                var c = renderers[i].color;
                c.a = Mathf.Lerp(startAlphas[i], 0f, s);
                renderers[i].color = c;
            }
            yield return null;
        }
        if (footprint != null) { footprintObjects.Remove(footprint); Destroy(footprint); }
    }

    // Nearest LIVE vortex. VortexStats.IsDead() guards against trailing to one that's
    // mid-collapse.
    GameObject FindClosestVortex()
    {
        if (playerTransform == null) return null;

        var vortices = FindObjectsByType<VortexSpawner>(FindObjectsSortMode.None);
        GameObject closest = null;
        float closestDistance = float.MaxValue;

        foreach (var v in vortices)
        {
            if (v == null) continue;
            var vs = v.GetComponent<VortexStats>();
            if (vs != null && vs.IsDead()) continue;

            float d = Vector3.Distance(playerTransform.position, v.transform.position);
            if (d < closestDistance) { closestDistance = d; closest = v.gameObject; }
        }
        return closest;
    }

    void ClearPath()
    {
        foreach (var f in footprintObjects) if (f != null) Destroy(f);
        footprintObjects.Clear();
    }

    void OnDestroy() => ClearPath();
}


