using UnityEngine;
using System.Collections.Generic;

public class DrawnObstacle : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private EdgeCollider2D edgeCollider;
    private List<Vector2> points = new List<Vector2>();

    [Header("Obstacle Properties")]
    public float maxHealth = 50f;
    private float currentHealth;

    [Header("Decay Settings")]
    public float lifetime = 10f;
    private float creationTime;

    [Header("Visual Settings")]
    public Color solidColor = Color.blue;
    public float lineWidth = 0.3f;

    void Awake()
    {
        // Setup LineRenderer
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.sortingOrder = 5;
        lineRenderer.numCapVertices = 5;
        lineRenderer.numCornerVertices = 5;

        // Setup EdgeCollider2D
        edgeCollider = gameObject.AddComponent<EdgeCollider2D>();
        edgeCollider.edgeRadius = lineWidth * 0.5f;

        currentHealth = maxHealth;

        // Record creation time for decay
        creationTime = Time.time;

        // Set layer to Obstacle if it exists
        int obstacleLayer = LayerMask.NameToLayer("Obstacles");
        if (obstacleLayer != -1)
        {
            gameObject.layer = obstacleLayer;
        }

        // Set tag
        gameObject.tag = "Obstacle";
    }

    public void InitializeObstacle(List<Vector2> pathPoints, Color color, float width, float health)
    {
        points = new List<Vector2>(pathPoints);
        solidColor = color;
        lineWidth = width;
        maxHealth = health;
        currentHealth = maxHealth;

        UpdateVisuals();
        UpdateCollider();
    }

    void Update()
    {
        // Check if lifetime has expired
        float age = Time.time - creationTime;

        if (age >= lifetime)
        {
            DestroyObstacle();
            return;
        }

        // Visual feedback for obstacle decay (fade out in last 2 seconds)
        if (age >= lifetime - 2f)
        {
            float fadeProgress = (lifetime - age) / 2f; // 1.0 to 0.0
            Color fadedColor = solidColor;
            fadedColor.a = fadeProgress;
            lineRenderer.startColor = fadedColor;
            lineRenderer.endColor = fadedColor;
        }
    }

    void UpdateVisuals()
    {
        if (lineRenderer == null || points.Count < 2) return;

        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = solidColor;
        lineRenderer.endColor = solidColor;
        lineRenderer.positionCount = points.Count;

        for (int i = 0; i < points.Count; i++)
        {
            lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, 0));
        }
    }

    void UpdateCollider()
    {
        if (edgeCollider == null || points.Count < 2) return;

        edgeCollider.points = points.ToArray();
        edgeCollider.edgeRadius = lineWidth * 0.5f;
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        // Visual feedback - flash when damaged
        if (currentHealth > 0)
        {
            StartCoroutine(DamageFlash());
        }
        else
        {
            DestroyObstacle();
        }
    }

    System.Collections.IEnumerator DamageFlash()
    {
        Color originalColor = solidColor;
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.red;

        yield return new WaitForSeconds(0.1f);

        lineRenderer.startColor = originalColor;
        lineRenderer.endColor = originalColor;
    }

    public void DestroyObstacle()
    {
        // TODO Add destruction effect here
        Destroy(gameObject);
    }

    public float GetHealthPercentage()
    {
        return currentHealth / maxHealth;
    }

    // For enemies to detect and avoid
    void OnCollisionEnter2D(Collision2D collision)
    {
        // Enemies colliding with obstacle
        if (collision.gameObject.CompareTag("Enemy"))
        {
            // TODO Apply damage to obstacle from enemy contact
            // TakeDamage(1f);
        }
    }
}