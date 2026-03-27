using UnityEngine;
using System.Collections.Generic;

public class DrawnObstacle : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private EdgeCollider2D edgeCollider;
    private List<Vector2> points = new List<Vector2>();
    private List<Vector2> localPoints = new List<Vector2>();

    // Mesh overlay
    private GameObject meshOverlayGO;
    private MeshRenderer meshRendererOverlay;
    private MeshFilter meshFilter;
    private Mesh overlayMesh;
    private Material overlayMaterial;

    [Header("Obstacle Properties")]
    public float maxHealth = 50f;
    private float currentHealth;

    [Header("Decay Settings")]
    public float lifetime = 10f;
    private float creationTime;

    [Header("Visual Settings")]
    public Color solidColor = Color.blue;
    public float lineWidth = 0.3f;

    [Header("Solidification Settings")]
    [Tooltip("Color during the drawing phase (passed from WeaponData.drawLineColor)")]
    public Color drawColor = new Color(0.5f, 0.8f, 1f, 0.6f);
    [Tooltip("Duration of the solidification transition in seconds")]
    public float solidifyDuration = 0.5f;

    [Header("Mesh Pattern Settings")]
    [Tooltip("Resolution: sample points per world unit along the path")]
    public float meshResolution = 20f;
    [Tooltip("How many diamond cells fit across the width")]
    public int diamondsAcross = 2;
    [Tooltip("Aspect ratio of each diamond cell (length/width). >1 = elongated along path")]
    public float diamondAspect = 1.5f;
    [Tooltip("Thickness of the wire lines as a fraction of cell size (0-0.5)")]
    [Range(0.05f, 0.45f)]
    public float wireThickness = 0.15f;
    [Tooltip("Brightness of wire lines over base color (0=same, 1=white)")]
    [Range(0f, 1f)]
    public float wireBrightness = 0.5f;
    [Tooltip("Number of cross-width vertices (higher = smoother edges)")]
    public int crossResolution = 12;
    [Tooltip("Number of segments in each rounded end cap")]
    public int capSegments = 10;
    [Tooltip("Chaikin smoothing iterations (higher = smoother curves, 2-3 recommended)")]
    public int smoothingIterations = 2;

    // State
    private float currentAlpha = 1f;
    private bool isSolidified = false;
    private float solidifyProgress = 0f; // 0..1

    // Cached vertex colors for efficient alpha updates
    private Color[] baseMeshColors;

    void Awake()
    {
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.numCapVertices = 5;
        lineRenderer.numCornerVertices = 5;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.useWorldSpace = false;
        lineRenderer.sortingOrder = 1500;

        edgeCollider = gameObject.AddComponent<EdgeCollider2D>();
        edgeCollider.edgeRadius = lineWidth * 0.5f;
        // Collider starts disabled — enabled after solidification completes
        edgeCollider.enabled = false;

        currentHealth = maxHealth;
        creationTime = Time.time;

        int obstacleLayer = LayerMask.NameToLayer("Obstacles");
        if (obstacleLayer != -1)
            gameObject.layer = obstacleLayer;

        gameObject.tag = "Obstacle";
    }

    public void InitializeObstacle(List<Vector2> pathPoints, Color color, float width, float health)
    {
        points = new List<Vector2>(pathPoints);
        solidColor = color;
        lineWidth = width;
        maxHealth = health;
        currentHealth = maxHealth;

        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;

        // Start in draw color (transparent/glowing) — will transition to solid
        lineRenderer.startColor = drawColor;
        lineRenderer.endColor = drawColor;

        Vector2 center = Vector2.zero;
        if (points.Count >= 2)
        {
            foreach (var p in points) center += p;
            center /= points.Count;
        }
        transform.position = new Vector3(center.x, center.y, 0f);

        localPoints.Clear();
        for (int i = 0; i < points.Count; i++)
            localPoints.Add(points[i] - center);

        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
        }

        UpdateBaseLinePositions();
        UpdateCollider();
        BuildMeshOverlay();

        // Start with mesh overlay hidden — it fades in during solidification
        if (meshOverlayGO != null)
            meshRendererOverlay.enabled = false;

        // Begin solidification
        isSolidified = false;
        solidifyProgress = 0f;
    }

    public void SetDrawColor(Color color)
    {
        drawColor = color;
    }

    //  Chaikin curve subdivision for smooth paths

    private List<Vector2> ChaikinSmooth(List<Vector2> path, int iterations)
    {
        if (path.Count < 3 || iterations <= 0)
            return new List<Vector2>(path);

        List<Vector2> current = new List<Vector2>(path);

        for (int iter = 0; iter < iterations; iter++)
        {
            List<Vector2> smoothed = new List<Vector2>();

            // Keep the first point
            smoothed.Add(current[0]);

            for (int i = 0; i < current.Count - 1; i++)
            {
                Vector2 p0 = current[i];
                Vector2 p1 = current[i + 1];

                // Chaikin's rule: generate two points at 25% and 75%
                smoothed.Add(Vector2.Lerp(p0, p1, 0.25f));
                smoothed.Add(Vector2.Lerp(p0, p1, 0.75f));
            }

            // Keep the last point
            smoothed.Add(current[current.Count - 1]);

            current = smoothed;
        }

        return current;
    }

    //  Procedural mesh with integrated rounded caps

    private void BuildMeshOverlay()
    {
        if (localPoints.Count < 2) return;

        // Smooth the path first to eliminate sharp corners
        List<Vector2> smoothedPath = ChaikinSmooth(localPoints, smoothingIterations);

        // Then resample at uniform intervals
        List<Vector2> sampled = ResamplePath(smoothedPath, meshResolution);
        if (sampled.Count < 2) return;

        Vector2[] normals = ComputeNormals(sampled);

        float[] arcLen = new float[sampled.Count];
        float[] widths = new float[sampled.Count];
        arcLen[0] = 0f;
        for (int i = 1; i < sampled.Count; i++)
            arcLen[i] = arcLen[i - 1] + Vector2.Distance(sampled[i - 1], sampled[i]);
        for (int i = 0; i < sampled.Count; i++)
            widths[i] = 1f;

        float totalArc = arcLen[sampled.Count - 1];

        // Generate cap arcs
        Vector2 startTangent = (sampled[1] - sampled[0]).normalized;
        List<Vector2> startCapPts, startCapNormals;
        List<float> startCapWidths;
        GenerateCapArc(sampled[0], -startTangent, normals[0], capSegments,
                        out startCapPts, out startCapNormals, out startCapWidths);

        int last = sampled.Count - 1;
        Vector2 endTangent = (sampled[last] - sampled[last - 1]).normalized;
        List<Vector2> endCapPts, endCapNormals;
        List<float> endCapWidths;
        GenerateCapArc(sampled[last], endTangent, normals[last], capSegments,
                        out endCapPts, out endCapNormals, out endCapWidths);

        // Combine startCap (reversed) + mainBody + endCap
        int startCapCount = startCapPts.Count;
        int mainCount = sampled.Count;
        int endCapCount = endCapPts.Count;
        int totalSamples = startCapCount + mainCount + endCapCount;

        Vector2[] allPts = new Vector2[totalSamples];
        Vector2[] allNormals = new Vector2[totalSamples];
        float[] allArc = new float[totalSamples];
        float[] allWidths = new float[totalSamples];

        float capArcOffset = lineWidth * 0.5f;
        for (int i = 0; i < startCapCount; i++)
        {
            int ri = startCapCount - 1 - i;
            allPts[i] = startCapPts[ri];
            allNormals[i] = startCapNormals[ri];
            allWidths[i] = startCapWidths[ri];
            allArc[i] = -capArcOffset + (capArcOffset * (float)i / Mathf.Max(startCapCount - 1, 1));
        }

        for (int i = 0; i < mainCount; i++)
        {
            allPts[startCapCount + i] = sampled[i];
            allNormals[startCapCount + i] = normals[i];
            allWidths[startCapCount + i] = widths[i];
            allArc[startCapCount + i] = arcLen[i];
        }

        for (int i = 0; i < endCapCount; i++)
        {
            allPts[startCapCount + mainCount + i] = endCapPts[i];
            allNormals[startCapCount + mainCount + i] = endCapNormals[i];
            allWidths[startCapCount + mainCount + i] = endCapWidths[i];
            allArc[startCapCount + mainCount + i] = totalArc + capArcOffset * ((float)i / Mathf.Max(endCapCount - 1, 1));
        }

        // Build quad strip mesh
        int rows = Mathf.Max(crossResolution, 5);
        float halfW = lineWidth * 0.5f;

        float cellV = lineWidth / diamondsAcross;
        float cellU = cellV * diamondAspect;

        Color baseCol = solidColor; baseCol.a = 1f;
        Color wireCol = Color.Lerp(solidColor, Color.white, wireBrightness); wireCol.a = 1f;

        int vertCount = totalSamples * rows;
        Vector3[] verts = new Vector3[vertCount];
        Color[] colors = new Color[vertCount];

        for (int i = 0; i < totalSamples; i++)
        {
            Vector2 p = allPts[i];
            Vector2 n = allNormals[i];
            float u = allArc[i];
            float w = allWidths[i];

            for (int r = 0; r < rows; r++)
            {
                float t = (float)r / (rows - 1);
                float offset = Mathf.Lerp(-halfW, halfW, t) * w;

                int vi = i * rows + r;
                verts[vi] = new Vector3(p.x + n.x * offset, p.y + n.y * offset, 0f);

                float scaledT = 0.5f + (t - 0.5f) * w;
                colors[vi] = ComputeWireColor(u, scaledT, cellU, cellV, baseCol, wireCol);
            }
        }

        int quadCount = (totalSamples - 1) * (rows - 1);
        int[] tris = new int[quadCount * 6];
        int ti = 0;

        for (int i = 0; i < totalSamples - 1; i++)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                int bl = i * rows + r;
                int br = i * rows + r + 1;
                int tl = (i + 1) * rows + r;
                int tr = (i + 1) * rows + r + 1;
                tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = tr;
                tris[ti++] = bl; tris[ti++] = tr; tris[ti++] = br;
            }
        }

        overlayMesh = new Mesh();
        overlayMesh.name = "ObstacleMeshOverlay";
        overlayMesh.vertices = verts;
        overlayMesh.colors = colors;
        overlayMesh.triangles = tris;
        overlayMesh.RecalculateBounds();

        // Cache the fully-opaque mesh colors for reuse during alpha fading
        baseMeshColors = (Color[])colors.Clone();

        meshOverlayGO = new GameObject("MeshOverlay");
        meshOverlayGO.transform.SetParent(transform);
        meshOverlayGO.transform.localPosition = Vector3.zero;
        meshOverlayGO.transform.localRotation = Quaternion.identity;
        meshOverlayGO.transform.localScale = Vector3.one;

        meshFilter = meshOverlayGO.AddComponent<MeshFilter>();
        meshFilter.mesh = overlayMesh;

        overlayMaterial = new Material(Shader.Find("Sprites/Default"));
        meshRendererOverlay = meshOverlayGO.AddComponent<MeshRenderer>();
        meshRendererOverlay.sharedMaterial = overlayMaterial;
        meshRendererOverlay.sortingOrder = 1501;
        meshRendererOverlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRendererOverlay.receiveShadows = false;
    }

    private void GenerateCapArc(Vector2 center, Vector2 outDir, Vector2 normal, int segments,
                                 out List<Vector2> capPts, out List<Vector2> capNormals, out List<float> capWidths)
    {
        capPts = new List<Vector2>();
        capNormals = new List<Vector2>();
        capWidths = new List<float>();

        float halfW = lineWidth * 0.5f;

        for (int s = 0; s <= segments; s++)
        {
            float t = (float)s / segments;
            float angle = t * Mathf.PI * 0.5f;

            Vector2 pos = center + outDir * Mathf.Sin(angle) * halfW;
            float w = Mathf.Cos(angle);

            capPts.Add(pos);
            capNormals.Add(normal);
            capWidths.Add(w);
        }
    }

    private Color ComputeWireColor(float u, float t, float cellU, float cellV,
                                    Color baseCol, Color wireCol)
    {
        float v = t * lineWidth;
        float cu = (cellU > 0.001f) ? Mathf.Repeat(u, cellU) / cellU : 0f;
        float cv = (cellV > 0.001f) ? Mathf.Repeat(v, cellV) / cellV : 0f;
        float diamond = Mathf.Abs(cu - 0.5f) + Mathf.Abs(cv - 0.5f);
        float distToWire = Mathf.Abs(diamond - 0.5f);
        float distToEdge = Mathf.Min(t, 1f - t);
        float edgeFactor = 1f - Mathf.Clamp01(distToEdge / (wireThickness * 0.5f));
        float wireFactor = 1f - Mathf.Clamp01(distToWire / wireThickness);
        float blend = Mathf.Max(wireFactor, edgeFactor);
        return Color.Lerp(baseCol, wireCol, blend);
    }

    //  Path utilities

    private List<Vector2> ResamplePath(List<Vector2> path, float samplesPerUnit)
    {
        float totalLen = 0f;
        for (int i = 0; i < path.Count - 1; i++)
            totalLen += Vector2.Distance(path[i], path[i + 1]);

        if (totalLen < 0.001f) return new List<Vector2>(path);

        int sampleCount = Mathf.Max(Mathf.CeilToInt(totalLen * samplesPerUnit), 2);
        float step = totalLen / (sampleCount - 1);

        List<Vector2> result = new List<Vector2>(sampleCount);
        result.Add(path[0]);

        int seg = 0;
        float segStart = 0f;
        float segLen = Vector2.Distance(path[0], path[1]);

        for (int s = 1; s < sampleCount; s++)
        {
            float targetDist = s * step;
            while (seg < path.Count - 2 && targetDist > segStart + segLen)
            {
                segStart += segLen;
                seg++;
                segLen = Vector2.Distance(path[seg], path[seg + 1]);
            }
            float t = (segLen > 0.0001f) ? (targetDist - segStart) / segLen : 0f;
            result.Add(Vector2.Lerp(path[seg], path[seg + 1], Mathf.Clamp01(t)));
        }

        return result;
    }

    private Vector2[] ComputeNormals(List<Vector2> path)
    {
        Vector2[] normals = new Vector2[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            Vector2 dir = Vector2.zero;
            if (i > 0) dir += (path[i] - path[i - 1]).normalized;
            if (i < path.Count - 1) dir += (path[i + 1] - path[i]).normalized;
            dir.Normalize();
            normals[i] = new Vector2(-dir.y, dir.x);
        }
        return normals;
    }



    void Update()
    {
        float age = Time.time - creationTime;

        // Phase 1: Solidification transition
        if (!isSolidified)
        {
            solidifyProgress = Mathf.Clamp01(age / solidifyDuration);
            UpdateSolidification(solidifyProgress);

            if (solidifyProgress >= 1f)
            {
                isSolidified = true;
                edgeCollider.enabled = true;

                // Play creation sound when fully solidified
                if (AudioManager.instance != null && FMODEvents.instance != null)
                {
                    AudioManager.instance.PlayOneShot(FMODEvents.instance.towerCreation, transform.position);
                }
            }
            return; // Don't check lifetime during solidification
        }

        // Phase 2: Active obstacle (lifetime countdown starts after solidification)
        float activeAge = age - solidifyDuration;

        if (activeAge >= lifetime)
        {
            DestroyObstacle();
            return;
        }

        // Phase 3: Decay fade-out in last 2 seconds
        if (activeAge >= lifetime - 2f)
        {
            currentAlpha = (lifetime - activeAge) / 2f;
            ApplyAlpha(currentAlpha);
        }
    }


    /// Animates the transition from draw-line appearance to solid obstacle.

    private void UpdateSolidification(float progress)
    {
        // Smooth the transition curve (ease-in-out)
        float t = progress * progress * (3f - 2f * progress); // smoothstep

        // Base line: lerp from draw color to solid color
        Color currentLineColor = Color.Lerp(drawColor, solidColor, t);
        lineRenderer.startColor = currentLineColor;
        lineRenderer.endColor = currentLineColor;

        // Width: slight expansion from thinner to full width (subtle "hardening" feel)
        float widthScale = Mathf.Lerp(0.85f, 1f, t);
        lineRenderer.startWidth = lineWidth * widthScale;
        lineRenderer.endWidth = lineWidth * widthScale;

        // Mesh overlay: fade in during second half of solidification
        if (meshRendererOverlay != null)
        {
            float meshFadeStart = 0.3f; // mesh starts appearing at 30% progress
            float meshAlpha = Mathf.Clamp01((t - meshFadeStart) / (1f - meshFadeStart));

            if (meshAlpha > 0f && !meshRendererOverlay.enabled)
                meshRendererOverlay.enabled = true;

            if (meshAlpha > 0f && overlayMesh != null && baseMeshColors != null)
            {
                Color[] cols = new Color[baseMeshColors.Length];
                for (int i = 0; i < cols.Length; i++)
                {
                    cols[i] = baseMeshColors[i];
                    cols[i].a = meshAlpha;
                }
                overlayMesh.colors = cols;
            }
        }
    }

    private void ApplyAlpha(float alpha)
    {
        Color fadedBase = solidColor;
        fadedBase.a = alpha;
        lineRenderer.startColor = fadedBase;
        lineRenderer.endColor = fadedBase;

        if (overlayMesh != null && baseMeshColors != null)
        {
            Color[] cols = new Color[baseMeshColors.Length];
            for (int i = 0; i < baseMeshColors.Length; i++)
            {
                cols[i] = baseMeshColors[i];
                cols[i].a = alpha;
            }
            overlayMesh.colors = cols;
        }
    }

    void UpdateBaseLinePositions()
    {
        if (lineRenderer == null || localPoints.Count < 2) return;

        lineRenderer.positionCount = localPoints.Count;
        for (int i = 0; i < localPoints.Count; i++)
            lineRenderer.SetPosition(i, new Vector3(localPoints[i].x, localPoints[i].y, 0));
    }

    void UpdateCollider()
    {
        if (edgeCollider == null || localPoints.Count < 2) return;

        edgeCollider.points = localPoints.ToArray();
        edgeCollider.edgeRadius = lineWidth * 0.5f;
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        if (currentHealth > 0)
            StartCoroutine(DamageFlash());
        else
            DestroyObstacle();
    }

    System.Collections.IEnumerator DamageFlash()
    {
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.red;
        if (overlayMaterial != null) overlayMaterial.color = Color.red;

        yield return new WaitForSeconds(0.1f);

        Color c = solidColor; c.a = currentAlpha;
        lineRenderer.startColor = c;
        lineRenderer.endColor = c;
        if (overlayMaterial != null) overlayMaterial.color = Color.white;
    }

    public void DestroyObstacle()
    {
        if (overlayMesh != null) Destroy(overlayMesh);
        if (overlayMaterial != null) Destroy(overlayMaterial);
        if (lineRenderer != null && lineRenderer.material != null)
            Destroy(lineRenderer.material);

        Destroy(gameObject);
    }

    public float GetHealthPercentage()
    {
        return currentHealth / maxHealth;
    }

    public bool IsSolidified()
    {
        return isSolidified;
    }

    public float GetSolidifyProgress()
    {
        return solidifyProgress;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Enemy"))
        {
            // TODO Apply damage to obstacle from enemy contact
            // TakeDamage(1f);
        }
    }
}
