using UnityEngine;
using System.Collections.Generic;

public class MarshFootstepRipples : MonoBehaviour
{
    [Header("Ripple Settings")]
    public int maxRipples = 80;
    public float rippleLifetime = 1.2f;
    public float rippleMaxRadius = 0.35f;
    public float rippleWidth = 0.025f;
    public float stepDistance = 0.18f;
    public Color rippleColor = new Color(0.50f, 0.62f, 0.70f, 0.55f);
    public float waterCheckPadding = 0.3f;

    [Header("Foot Position")]
    [Tooltip("Manual X offset from transform.position to the foot point. " +
             "Only used when autoDetectFeet is OFF.")]
    public float footOffsetX = 0f;
    [Tooltip("Manual Y offset from transform.position to the foot point. " +
             "Negative = below center. Only used when autoDetectFeet is OFF.")]
    public float footOffsetY = -0.25f;
    [Tooltip("When true, uses SpriteRenderer.bounds to auto-detect foot " +
             "position (bottom-center of sprite). When false, uses manual offsets.")]
    public bool autoDetectFeet = true;

    [Header("Debug")]
    [Tooltip("When true, spawns ripples regardless of water check")]
    public bool debugAlwaysSpawn = false;
    [Tooltip("Draw a green gizmo sphere at each entity's foot position in Scene view")]
    public bool debugDrawFootGizmos = true;

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 12;

    private MarshWaterOverlay waterOverlay;
    private Mesh rippleMesh;
    private GameObject meshGO;
    private Vector3[] verts;
    private Color[] cols;
    private int segs = 16;

    struct RippleSlot
    {
        public Vector2 center;
        public float birthTime;
        public float lifetime;
        public float maxR;
        public bool active;
    }
    private RippleSlot[] slots;
    private int nextSlot = 0;
    private int totalSpawned = 0;

    struct TrackedEntity
    {
        public Transform transform;
        public SpriteRenderer spriteRenderer;
        public Vector2 lastPos;
        public float distAccum;
        public bool initialized;
        public string label;
    }
    private List<TrackedEntity> tracked = new List<TrackedEntity>();
    private float lastScanTime = -999f;
    private bool hasLoggedInit = false;
    private List<Vector2> debugFootPositions = new List<Vector2>();

    Vector2 GetFootPos(TrackedEntity te)
    {
        if (autoDetectFeet && te.spriteRenderer != null && te.spriteRenderer.sprite != null
            && te.spriteRenderer.enabled && te.spriteRenderer.gameObject.activeInHierarchy)
        {
            Bounds b = te.spriteRenderer.bounds;
            return new Vector2(te.transform.position.x, b.min.y);
        }
        return (Vector2)te.transform.position + new Vector2(footOffsetX, footOffsetY);
    }

    public void Init(MarshWaterOverlay overlay)
    {
        waterOverlay = overlay;
        //Debug.Log($"[MarshFootstepRipples] Init called. Overlay null? {overlay == null}, " +
        //          $"parent.position={transform.position}");
        BuildMesh();
    }

    void BuildMesh()
    {
        slots = new RippleSlot[maxRipples];
        int vertsPerRipple = (segs + 1) * 2;
        int totalVerts = maxRipples * vertsPerRipple;

        var V = new List<Vector3>(totalVerts);
        var T = new List<int>(maxRipples * segs * 6);
        var C = new List<Color>(totalVerts);

        for (int i = 0; i < maxRipples; i++)
        {
            slots[i].active = false;
            slots[i].birthTime = -999f;
            int vBase = V.Count;
            for (int s = 0; s <= segs; s++)
            {
                V.Add(Vector3.zero); V.Add(Vector3.zero);
                C.Add(Color.clear); C.Add(Color.clear);
            }
            for (int s = 0; s < segs; s++)
            {
                int b = vBase + s * 2;
                T.Add(b); T.Add(b + 2); T.Add(b + 1);
                T.Add(b + 1); T.Add(b + 2); T.Add(b + 3);
            }
        }

        rippleMesh = new Mesh { name = "FootstepRipples" };
        if (V.Count > 65000) rippleMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        rippleMesh.SetVertices(V);
        rippleMesh.SetTriangles(T, 0);
        rippleMesh.SetColors(C);
        rippleMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 500f);

        verts = (Vector3[])rippleMesh.vertices.Clone();
        cols = (Color[])rippleMesh.colors.Clone();
        meshGO = new GameObject("Marsh_FootstepRipples");
        meshGO.transform.position = new Vector3(0f, 0f, -0.01f);
        meshGO.transform.localScale = Vector3.one;
        meshGO.AddComponent<MeshFilter>().sharedMesh = rippleMesh;
        MeshRenderer mr = meshGO.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder;

        //Debug.Log($"[MarshFootstepRipples] Mesh built: {totalVerts} verts, sortingOrder={sortingOrder}, " +
        //          $"meshGO.position={meshGO.transform.position} (NOT parented to BiomeManager)");
    }

    void ScanEntities()
    {
        HashSet<Transform> existing = new HashSet<Transform>();
        for (int i = 0; i < tracked.Count; i++)
            if (tracked[i].transform != null) existing.Add(tracked[i].transform);

        // Player
        var player = FindFirstObjectByType<PlayerMovement>();
        if (player != null && !existing.Contains(player.transform))
        {
            var sr = player.GetComponent<SpriteRenderer>();
            tracked.Add(new TrackedEntity
            {
                transform = player.transform,
                spriteRenderer = sr,
                lastPos = (Vector2)player.transform.position,
                distAccum = 0f,
                initialized = false,
                label = "Player"
            });
            //Debug.Log($"[MarshFootstepRipples] Tracking Player at {player.transform.position}");
        }

        // Enemies
        var enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        foreach (var e in enemies)
        {
            if (e != null && !existing.Contains(e.transform))
            {
                // Try GetComponent first, fall back to children
                var sr = e.GetComponent<SpriteRenderer>();
                if (sr == null) sr = e.GetComponentInChildren<SpriteRenderer>();

                tracked.Add(new TrackedEntity
                {
                    transform = e.transform,
                    spriteRenderer = sr,
                    lastPos = (Vector2)e.transform.position,
                    distAccum = 0f,
                    initialized = false,
                    label = e.gameObject.name
                });

                if (sr != null && sr.sprite != null)
                {
                    Bounds b = sr.bounds;
                    //Debug.Log($"[MarshFootstepRipples] Tracking Enemy '{e.gameObject.name}': " +
                    //          $"transform.pos={e.transform.position}, scale={e.transform.localScale}, " +
                    //          $"sr.gameObject='{sr.gameObject.name}' (same={sr.gameObject == e.gameObject}), " +
                    //          $"sprite='{sr.sprite.name}', pivot={sr.sprite.pivot}, " +
                    //          $"bounds.center={b.center}, bounds.min={b.min}, bounds.max={b.max}, " +
                    //          $"flipX={sr.flipX}");
                }
            }
        }

        // Bosses (inherit from BaseBossStats, not EnemyController)
        var bosses = FindObjectsByType<BaseBossStats>(FindObjectsSortMode.None);
        foreach (var b in bosses)
        {
            if (b != null && !existing.Contains(b.transform))
            {
                var sr = b.GetComponent<SpriteRenderer>();
                if (sr == null) sr = b.GetComponentInChildren<SpriteRenderer>();

                tracked.Add(new TrackedEntity
                {
                    transform = b.transform,
                    spriteRenderer = sr,
                    lastPos = (Vector2)b.transform.position,
                    distAccum = 0f,
                    initialized = false,
                    label = b.gameObject.name
                });

                if (sr != null && sr.sprite != null)
                {
                    Bounds b2 = sr.bounds;
                    //Debug.Log($"[MarshFootstepRipples] Tracking Boss '{b.gameObject.name}': " +
                    //          $"transform.pos={b.transform.position}, scale={b.transform.localScale}, " +
                    //          $"sr.gameObject='{sr.gameObject.name}' (same={sr.gameObject == b.gameObject}), " +
                    //          $"sprite='{sr.sprite.name}', pivot={sr.sprite.pivot}, " +
                    //          $"bounds.center={b2.center}, bounds.min={b2.min}, bounds.max={b2.max}, " +
                    //          $"flipX={sr.flipX}");
                }
            }
        }

        // Remove dead
        for (int i = tracked.Count - 1; i >= 0; i--)
            if (tracked[i].transform == null) tracked.RemoveAt(i);
    }

    void Update()
    {
        if (rippleMesh == null) return;
        float t = Time.time;

        if (!hasLoggedInit && Time.frameCount > 10)
        {
            hasLoggedInit = true;
            //Debug.Log($"[MarshFootstepRipples] Status: overlay={(waterOverlay != null ? "OK" : "NULL")}, " +
            //          $"tracked={tracked.Count}, meshGO.pos={meshGO?.transform.position}, " +
            //          $"parent.pos={transform.position}");
        }

        if (t - lastScanTime > 1.5f)
        {
            ScanEntities();
            lastScanTime = t;
        }

        debugFootPositions.Clear();

        for (int i = 0; i < tracked.Count; i++)
        {
            var te = tracked[i];
            if (te.transform == null) continue;

            Vector2 pos = (Vector2)te.transform.position;
            Vector2 footPos = GetFootPos(te);
            debugFootPositions.Add(footPos);

            if (!te.initialized)
            {
                te.lastPos = pos;
                te.initialized = true;
                tracked[i] = te;
                continue;
            }

            float dist = (pos - te.lastPos).magnitude;
            te.lastPos = pos;
            te.distAccum += dist;

            if (te.distAccum >= stepDistance)
            {
                bool onWater = false;

                if (debugAlwaysSpawn)
                {
                    onWater = true;
                }
                else if (waterOverlay != null)
                {
                    onWater = IsNearWater(footPos);
                }

                if (onWater)
                {
                    SpawnRipple(footPos, t);
                    if (totalSpawned <= 15)
                    {
                        //Debug.Log($"[MarshFootstepRipples] Ripple #{totalSpawned} for {te.label}: " +
                        //          $"footPos={footPos}, transformPos={pos}, " +
                        //          $"delta=({footPos.x - pos.x:F3}, {footPos.y - pos.y:F3}), " +
                        //          $"sr={(te.spriteRenderer != null ? "OK" : "NULL")}, " +
                        //          $"autoDetect={autoDetectFeet}");
                    }
                }

                te.distAccum = 0f;
                tracked[i] = te;
            }
            else
            {
                tracked[i] = te;
            }
        }

        AnimateRipples(t);
    }

    bool IsNearWater(Vector2 pos)
    {
        if (waterOverlay.IsOverWater(pos)) return true;
        float p = waterCheckPadding;
        if (waterOverlay.IsOverWater(pos + new Vector2(p, 0f))) return true;
        if (waterOverlay.IsOverWater(pos + new Vector2(-p, 0f))) return true;
        if (waterOverlay.IsOverWater(pos + new Vector2(0f, p))) return true;
        if (waterOverlay.IsOverWater(pos + new Vector2(0f, -p))) return true;
        return false;
    }

    void SpawnRipple(Vector2 pos, float t)
    {
        slots[nextSlot] = new RippleSlot
        {
            center = pos,
            birthTime = t,
            lifetime = rippleLifetime * (0.8f + Random.value * 0.4f),
            maxR = rippleMaxRadius * (0.7f + Random.value * 0.6f),
            active = true
        };
        nextSlot = (nextSlot + 1) % maxRipples;
        totalSpawned++;
    }

    void AnimateRipples(float t)
    {
        bool anyActive = false;
        int vpr = (segs + 1) * 2;

        for (int i = 0; i < maxRipples; i++)
        {
            var sl = slots[i];
            if (!sl.active) continue;

            float age = t - sl.birthTime;
            float life01 = age / sl.lifetime;

            if (life01 >= 1f)
            {
                sl.active = false; slots[i] = sl;
                int vBase = i * vpr;
                for (int j = 0; j < vpr; j++) { verts[vBase + j] = Vector3.zero; cols[vBase + j] = Color.clear; }
                anyActive = true;
                continue;
            }

            anyActive = true;
            float expand = life01 * sl.maxR;
            float alpha = (1f - life01); alpha *= alpha;

            int vb = i * vpr;
            for (int s = 0; s <= segs; s++)
            {
                float ang = (s / (float)segs) * Mathf.PI * 2f;
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                float ri = Mathf.Max(0f, expand - rippleWidth), ro = expand + rippleWidth;

                float outerX = sl.center.x + cs * ro;
                float outerY = sl.center.y + sn * ro;

                int inner = vb + s * 2, outer = inner + 1;

                // Clip, collapse segment if outer point is outside water
                bool segOverWater = true;
                if (waterOverlay != null && !debugAlwaysSpawn)
                {
                    segOverWater = waterOverlay.IsOverWater(new Vector2(outerX, outerY));
                }

                if (segOverWater)
                {
                    verts[inner] = new Vector3(sl.center.x + cs * ri, sl.center.y + sn * ri, 0f);
                    verts[outer] = new Vector3(outerX, outerY, 0f);
                    Color rc = rippleColor; rc.a *= alpha;
                    cols[inner] = rc; cols[outer] = new Color(rc.r, rc.g, rc.b, 0f);
                }
                else
                {
                    verts[inner] = Vector3.zero;
                    verts[outer] = Vector3.zero;
                    cols[inner] = Color.clear;
                    cols[outer] = Color.clear;
                }
            }
        }

        if (anyActive)
        {
            rippleMesh.vertices = verts;
            rippleMesh.colors = cols;
        }
    }

    void OnDrawGizmos()
    {
        if (!debugDrawFootGizmos || debugFootPositions == null) return;
        Gizmos.color = Color.green;
        for (int i = 0; i < debugFootPositions.Count; i++)
            Gizmos.DrawWireSphere((Vector3)debugFootPositions[i], 0.08f);
    }

    void OnDisable()
    {
        if (meshGO != null) DestroyImmediate(meshGO);
        meshGO = null;
        if (rippleMesh != null) DestroyImmediate(rippleMesh);
        rippleMesh = null; verts = null; cols = null;
    }

    void OnDestroy() => OnDisable();
}


