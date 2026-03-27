using UnityEngine;
using System.Collections.Generic;

public class ObstacleDrawerSystem
{
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

        // Record initial position
        Vector2 startPos = playerTransform.position;
        currentPath.Add(startPos);
        lastRecordedPosition = startPos;

        // Show drawing visual
        drawingVisualObject.SetActive(true);
        UpdateDrawingVisual();

        Debug.Log("[ObstacleDrawer] Started drawing obstacle");
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

        // Auto-finish drawing after max duration
        if (elapsedTime >= weaponData.drawDuration)
        {
            FinishDrawing();
        }
    }

    public void StopDrawing()
    {
        if (!isDrawing) return;

        FinishDrawing();
    }

    void FinishDrawing()
    {
        if (!isDrawing || currentPath.Count < 2)
        {
            CancelDrawing();
            return;
        }

        // Create solidified obstacle
        CreateObstacle(currentPath);

        // Reset drawing state
        isDrawing = false;
        currentPath.Clear();
        drawingVisualObject.SetActive(false);

        //Debug.Log($"[ObstacleDrawer] Finished drawing. Active obstacles: {activeObstacles.Count}");
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
