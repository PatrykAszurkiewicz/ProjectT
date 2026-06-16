using UnityEngine;
using System.Collections.Generic;

//  CHEST PATH INDICATOR 
// A standalone twin of GremlinPathIndicator that lays footprints from the player to
// the nearest LoreChest. Same footprint mechanics and grass Y-sort, but the prints
// are TINTED (light grey by default) so the chest trail reads differently from the
// gremlin's. Finds chests by the LoreChest component (no tag dependency).
public class ChestPathIndicator : MonoBehaviour
{
    [Header("Path Settings")]
    public float footprintSpacing = 1.5f;
    public float maxPathDistance = 100f;
    public float footprintScale = 0.45f;
    [Range(0f, 1f)] public float footprintAlpha = 0.75f;
    public float updateInterval = 1.0f;
    public float minimumUpdateDistance = 1.5f;
    public bool alternateFootOrientation = true;

    [Tooltip("Stop the trail this far (world units) before the chest, so it ends in front of it.")]
    public float stopBeforeChest = 1.4f;

    [Header("Tint")]
    [Tooltip("Use a generated LIGHT footprint so the grey tint actually shows. The shared\n" +
             "Resources 'Sprites/Feet' the gremlin uses is a dark silhouette — tinting that\n" +
             "grey still reads as black, which is why the chest prints looked black.")]
    public bool useGeneratedFootprint = true;
    [Tooltip("Footprint colour. Papyrus-toned (yellowish white-grey) by default.")]
    public Color footprintTint = new Color(0.90f, 0.86f, 0.74f, 1f);

    [Header("Sorting")]
    [Tooltip("Added to the grass-matched sort order (base 1000 + round(-y*10)).\n" +
             "Positive draws prints in FRONT of nearby grass. The prints are ALSO clamped\n" +
             "to sit just below the chest's own order, so the chest always covers the end.")]
    public int footprintSortOffset = 8;

    [Header("Animation")]
    public bool fadeInFootprints = false;
    public float fadeInDuration = 0.3f;
    public bool fadeOutOldFootprints = true;
    public float footprintLifetime = 0.8f;
    public float fadeOutDuration = 0.4f;

    [Header("Debug")]
    public bool enableDebugLogs = false;

    private Transform playerTransform;
    private List<GameObject> footprintObjects = new List<GameObject>();
    private Sprite footprintSprite;
    private float updateTimer;
    private bool isLeftFoot = true;
    private Vector3 lastTargetPosition = Vector3.zero;
    private bool hasTargetPosition;

    void Start()
    {
        FindPlayer();
        LoadFootprintSprite();
        updateTimer = updateInterval;
    }

    void FindPlayer()
    {
        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) { playerTransform = pm.transform; return; }
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerTransform = p.transform;
    }

    void LoadFootprintSprite()
    {
        if (useGeneratedFootprint)
        {
            footprintSprite = CreateFallbackFootprint(); // light foot → grey tint shows correctly
            return;
        }

        // Optional: reuse the gremlin's feet art (note: usually a dark silhouette).
        footprintSprite = Resources.Load<Sprite>("Sprites/Feet");
        if (footprintSprite == null)
        {
            Texture2D texture = Resources.Load<Texture2D>("Sprites/Feet");
            if (texture != null)
                footprintSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                                                Vector2.one * 0.5f, 100f);
            else
                footprintSprite = CreateFallbackFootprint();
        }
    }

    // A soft, light foot shape (white with soft alpha) so footprintTint can colour it.
    // Generated at 64px so it has presence at footprintScale ~1 (≈0.64 world units).
    Sprite CreateFallbackFootprint()
    {
        int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        var colors = new Color[size * size];

        // Ball of the foot (large oval) + heel (smaller oval below it).
        Vector2 ball = new Vector2(size * 0.5f, size * 0.60f);
        Vector2 heel = new Vector2(size * 0.5f, size * 0.24f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float bx = (x - ball.x) / (size * 0.27f);
                float by = (y - ball.y) / (size * 0.36f);
                float db = bx * bx + by * by;

                float hx = (x - heel.x) / (size * 0.18f);
                float hy = (y - heel.y) / (size * 0.22f);
                float dh = hx * hx + hy * hy;

                float d = Mathf.Min(db, dh);
                float a = Mathf.Clamp01(1f - d);     // soft edge
                a = a * a * 0.95f;
                colors[y * size + x] = new Color(1f, 1f, 1f, a);
            }
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

        if (footprintObjects.Count > 0 && FindClosestChest() == null)
        {
            ClearPath();
            hasTargetPosition = false;
        }
    }

    void UpdatePath()
    {
        if (playerTransform == null) { FindPlayer(); return; }
        if (footprintSprite == null) return;

        GameObject target = FindClosestChest();
        if (target == null)
        {
            if (enableDebugLogs && footprintObjects.Count == 0)
                Debug.Log("[ChestPath] No LoreChest in the scene right now — no trail to draw.");
            ClearPath();
            hasTargetPosition = false;
            return;
        }

        Vector3 playerPos = playerTransform.position;
        Vector3 targetPos = target.transform.position;
        float distance = Vector3.Distance(playerPos, targetPos);

        if (distance > maxPathDistance)
        {
            if (enableDebugLogs)
                Debug.Log($"[ChestPath] Nearest chest is {distance:F1}u away — beyond maxPathDistance " +
                          $"({maxPathDistance}). No trail. Raise Path Max Distance if this is too tight.");
            ClearPath();
            hasTargetPosition = false;
            return;
        }

        lastTargetPosition = targetPos;
        hasTargetPosition = true;
        ClearPath();

        // The chest's current sort order — footprints are clamped just below it so the
        // chest always draws over the end of the trail.
        var chestSR = target.GetComponentInChildren<SpriteRenderer>();
        int chestOrder = chestSR != null ? chestSR.sortingOrder : int.MaxValue;

        // End the trail a little before the chest so it reads as leading TO it.
        float usable = Mathf.Max(0f, distance - stopBeforeChest);
        int footprintCount = Mathf.FloorToInt(usable / footprintSpacing);
        if (footprintCount <= 0) return;

        Vector3 direction = (targetPos - playerPos).normalized;
        for (int i = 1; i <= footprintCount; i++)
        {
            float t = (float)i / footprintCount;
            Vector3 position = playerPos + direction * (usable * t);
            CreateFootprint(position, direction, i, chestOrder);
        }

        if (enableDebugLogs)
            Debug.Log($"[ChestPath] Chest {distance:F1}u away — laid {footprintCount} footprints, " +
                      $"stopping {stopBeforeChest}u short (tint {footprintTint}, alpha {footprintAlpha}).");
    }

    void CreateFootprint(Vector3 position, Vector3 direction, int index, int chestOrder)
    {
        var footprint = new GameObject($"ChestFootprint_{index}");
        footprint.transform.position = position;
        footprint.transform.SetParent(transform);

        var renderer = footprint.AddComponent<SpriteRenderer>();
        renderer.sprite = footprintSprite;
        renderer.sortingLayerName = "Default";

        // Grass-matched base; positive offset draws over nearby grass, but never above
        // the chest itself (clamped to chestOrder-1 so the chest covers the trail's end).
        const int kSortBase = 1000;
        const float kSortPrecision = 10f;
        int order = kSortBase + Mathf.RoundToInt(-position.y * kSortPrecision) + footprintSortOffset;
        if (chestOrder != int.MaxValue) order = Mathf.Min(order, chestOrder - 1);
        renderer.sortingOrder = order;

        // Apply the grey tint (this is the visual difference from the gremlin trail).
        Color color = footprintTint;
        color.a = fadeInFootprints ? 0f : footprintAlpha;
        renderer.color = color;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (alternateFootOrientation)
        {
            angle += isLeftFoot ? -15f : 15f;
            isLeftFoot = !isLeftFoot;
        }
        footprint.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        footprint.transform.localScale = Vector3.one * footprintScale;

        footprintObjects.Add(footprint);

        if (fadeInFootprints) StartCoroutine(FadeInFootprint(renderer));
        if (fadeOutOldFootprints) StartCoroutine(FadeOutFootprint(footprint, renderer));
        else Destroy(footprint, footprintLifetime);
    }

    System.Collections.IEnumerator FadeInFootprint(SpriteRenderer renderer)
    {
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            if (renderer == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / fadeInDuration;
            float s = t * t * (3f - 2f * t);
            var c = renderer.color; c.a = Mathf.Lerp(0f, footprintAlpha, s); renderer.color = c;
            yield return null;
        }
        if (renderer != null) { var c = renderer.color; c.a = footprintAlpha; renderer.color = c; }
    }

    System.Collections.IEnumerator FadeOutFootprint(GameObject footprint, SpriteRenderer renderer)
    {
        yield return new WaitForSeconds(footprintLifetime);
        if (footprint == null || renderer == null)
        {
            if (footprint != null) footprintObjects.Remove(footprint);
            yield break;
        }

        float elapsed = 0f;
        float startAlpha = renderer.color.a;
        while (elapsed < fadeOutDuration)
        {
            if (footprint == null || renderer == null)
            {
                if (footprint != null) footprintObjects.Remove(footprint);
                yield break;
            }
            elapsed += Time.deltaTime;
            float t = elapsed / fadeOutDuration;
            float s = t * t * (3f - 2f * t);
            var c = renderer.color; c.a = Mathf.Lerp(startAlpha, 0f, s); renderer.color = c;
            yield return null;
        }
        if (footprint != null) { footprintObjects.Remove(footprint); Destroy(footprint); }
    }

    GameObject FindClosestChest()
    {
        if (playerTransform == null) return null;

        var chests = FindObjectsByType<LoreChest>(FindObjectsSortMode.None);
        GameObject closest = null;
        float closestDistance = float.MaxValue;

        foreach (var chest in chests)
        {
            if (chest == null) continue;
            float d = Vector3.Distance(playerTransform.position, chest.transform.position);
            if (d < closestDistance) { closestDistance = d; closest = chest.gameObject; }
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


