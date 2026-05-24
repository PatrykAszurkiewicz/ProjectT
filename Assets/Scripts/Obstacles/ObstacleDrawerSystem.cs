using UnityEngine;
using System.Collections.Generic;

public class ObstacleDrawerSystem
{
    // A draw must trace at least this much total length (in world units) to
    // produce an obstacle. Replaces the previous "needs >= 2 path points"
    // check, which falsely treated a stationary press (path seeded with one
    // visual-only duplicate point) as a valid draw.
    private const float MIN_VALID_PATH_LENGTH = 0.25f;

    // Tiny offset used to seed a visible second point at draw start so the
    // LineRenderer renders something the instant the player presses, even
    // before they've moved. Without this, a press-without-movement showed
    // nothing on screen and the player thought the tool was unresponsive.
    private const float SEED_POINT_OFFSET = 0.001f;

    private Weapon weapon;
    private WeaponData weaponData;
    private Transform playerTransform;

    // Drawing state
    private bool isDrawing = false;
    private float drawStartTime = 0f;
    private List<Vector2> currentPath = new List<Vector2>();
    private Vector2 lastRecordedPosition;

    // Visual feedback
    private GameObject drawingVisualObject;
    private LineRenderer drawingLineRenderer;

    // Obstacle management
    private Queue<GameObject> activeObstacles = new Queue<GameObject>();

    public ObstacleDrawerSystem(Weapon weaponInstance, WeaponData data)
    {
        weapon = weaponInstance;
        weaponData = data;
        playerTransform = weaponInstance.transform.parent;

        CreateDrawingVisual();
    }

    void CreateDrawingVisual()
    {
        drawingVisualObject = new GameObject("DrawingVisual");
        drawingVisualObject.transform.SetParent(weapon.transform);
        drawingVisualObject.transform.localPosition = Vector3.zero;

        drawingLineRenderer = drawingVisualObject.AddComponent<LineRenderer>();
        drawingLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        drawingLineRenderer.startWidth = weaponData.obstacleWidth;
        drawingLineRenderer.endWidth = weaponData.obstacleWidth;
        drawingLineRenderer.startColor = weaponData.drawLineColor;
        drawingLineRenderer.endColor = weaponData.drawLineColor;
        drawingLineRenderer.sortingOrder = 2500; // Above grass Y-sort range while drawing
        drawingLineRenderer.numCapVertices = 5;
        drawingLineRenderer.numCornerVertices = 5;

        drawingVisualObject.SetActive(false);
    }

    public void StartDrawing()
    {
        if (isDrawing) return;

        isDrawing = true;
        drawStartTime = Time.time;
        currentPath.Clear();

        // Seed the path with TWO near-identical points so the LineRenderer
        // has visible geometry from frame 1 of the draw. A single-point line
        // renders nothing — which used to make press-without-movement look
        // like the tool was unresponsive. The second point is offset by a
        // negligible amount that gets dwarfed by any real movement, and is
        // filtered out by the path-length validity check in FinishDrawing
        // so a stationary draw still correctly cancels rather than placing
        // an invisible dot-obstacle.
        Vector2 startPos = playerTransform.position;
        currentPath.Add(startPos);
        currentPath.Add(startPos + new Vector2(SEED_POINT_OFFSET, 0f));
        lastRecordedPosition = startPos;

        // Show drawing visual
        drawingVisualObject.SetActive(true);
        UpdateDrawingVisual();
    }

    public void Update()
    {
        if (!isDrawing) return;

        float elapsedTime = Time.time - drawStartTime;

        // Record new position if player moved enough
        Vector2 currentPos = playerTransform.position;
        float distanceFromLast = Vector2.Distance(currentPos, lastRecordedPosition);

        if (distanceFromLast >= weaponData.minDrawDistance)
        {
            currentPath.Add(currentPos);
            lastRecordedPosition = currentPos;
            UpdateDrawingVisual();
        }

        // Auto-finish when the gameplay cap is reached. The cap is
        // weaponData.drawDuration — set per-asset in the Inspector (e.g. 1s)
        // to limit how long a single drawn obstacle can be.
        if (elapsedTime >= weaponData.drawDuration)
        {
            bool created = FinishDrawing();
            if (created) autoFinishCreatedObstacle = true;
        }
    }

    // One-shot signal consumed by Weapon to charge stamina + start a cooldown
    // when the auto-finish (above) created an obstacle without the player
    // having released the right mouse button. This is the normal path when
    // the player holds for the full gameplay cap (drawDuration). Without
    // this signal, hitting the cap would be a free obstacle — no stamina,
    // no cooldown.
    private bool autoFinishCreatedObstacle = false;
    public bool ConsumeAutoFinishSignal()
    {
        if (!autoFinishCreatedObstacle) return false;
        autoFinishCreatedObstacle = false;
        return true;
    }

    // Returns true if an obstacle was actually created (path was long enough),
    // false if the draw was cancelled (path too short — e.g. stationary press,
    // very brief tap). Callers use this to decide whether to charge stamina
    // and start a cooldown — failed draws cost nothing, same "no whiff tax"
    // rule the grappling hook already follows.
    public bool StopDrawing()
    {
        if (!isDrawing) return false;

        return FinishDrawing();
    }

    bool FinishDrawing()
    {
        if (!isDrawing)
        {
            return false;
        }

        // Validity check: total traced path length must exceed the minimum.
        // We measure length, not point count, because StartDrawing now seeds
        // the path with two near-identical points for instant visual feedback,
        // so a count-based check would falsely accept a stationary press as
        // a valid draw.
        float pathLength = 0f;
        for (int i = 1; i < currentPath.Count; i++)
        {
            pathLength += Vector2.Distance(currentPath[i - 1], currentPath[i]);
        }

        if (pathLength < MIN_VALID_PATH_LENGTH)
        {
            CancelDrawing();
            return false;
        }

        // Create solidified obstacle
        CreateObstacle(currentPath);

        // Reset drawing state
        isDrawing = false;
        currentPath.Clear();
        drawingVisualObject.SetActive(false);

        return true;
    }

    void CancelDrawing()
    {
        isDrawing = false;
        currentPath.Clear();
        drawingVisualObject.SetActive(false);

        //Debug.Log("[ObstacleDrawer] Drawing cancelled - path too short");
    }

    void UpdateDrawingVisual()
    {
        if (drawingLineRenderer == null || currentPath.Count < 1) return;

        drawingLineRenderer.positionCount = currentPath.Count;

        for (int i = 0; i < currentPath.Count; i++)
        {
            drawingLineRenderer.SetPosition(i, new Vector3(currentPath[i].x, currentPath[i].y, 0));
        }
    }

    void CreateObstacle(List<Vector2> path)
    {
        // Create new obstacle GameObject
        GameObject obstacleObj = new GameObject("DrawnObstacle");
        obstacleObj.transform.position = Vector3.zero;

        // Add DrawnObstacle component
        DrawnObstacle obstacle = obstacleObj.AddComponent<DrawnObstacle>();
        obstacle.SetDrawColor(weaponData.drawLineColor);
        obstacle.InitializeObstacle(
            path,
            weaponData.solidifiedColor,
            weaponData.obstacleWidth,
            weaponData.obstacleHealth
        );

        // Add to active obstacles queue
        activeObstacles.Enqueue(obstacleObj);

        // Remove oldest obstacle if we exceed max count
        while (activeObstacles.Count > weaponData.maxObstacles)
        {
            GameObject oldestObstacle = activeObstacles.Dequeue();
            if (oldestObstacle != null)
            {
                Object.Destroy(oldestObstacle);
            }
        }

        // Sound now plays inside DrawnObstacle after solidification completes
    }

    public bool IsDrawing()
    {
        return isDrawing;
    }

    public float GetDrawProgress()
    {
        if (!isDrawing) return 0f;

        float elapsed = Time.time - drawStartTime;
        return Mathf.Clamp01(elapsed / weaponData.drawDuration);
    }

    public int GetActiveObstacleCount()
    {
        return activeObstacles.Count;
    }

    public void Cleanup()
    {
        // Drop any uncollected auto-finish signal — caller will be torn down too.
        autoFinishCreatedObstacle = false;

        // Destroy all active obstacles
        while (activeObstacles.Count > 0)
        {
            GameObject obstacle = activeObstacles.Dequeue();
            if (obstacle != null)
            {
                Object.Destroy(obstacle);
            }
        }

        // Destroy drawing visual
        if (drawingVisualObject != null)
        {
            Object.Destroy(drawingVisualObject);
        }
    }
}
