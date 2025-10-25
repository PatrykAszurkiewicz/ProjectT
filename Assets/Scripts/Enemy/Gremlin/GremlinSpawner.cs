using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GremlinSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    public float spawnInterval = 10f;
    public int maxGremlinsOnMap = 1;
    public float spawnDistance = 8f;

    [Header("Path Indicator")]
    public bool showPathToGremlin = true;
    public float pathFootprintSpacing = 1.5f;
    public float pathMaxDistance = 20f;

    private List<GameObject> activeGremlins = new List<GameObject>();
    private Transform playerTransform;
    private GameObject gremlinPrefab;
    private GremlinPathIndicator pathIndicator;

    void Start()
    {
        FindPlayer();
        CreateGremlinPrefab();

        // Create path indicator if enabled
        if (showPathToGremlin)
        {
            SetupPathIndicator();
        }

        InvokeRepeating(nameof(TrySpawn), 2f, spawnInterval);
    }

    void SetupPathIndicator()
    {
        GameObject indicatorObj = new GameObject("GremlinPathIndicator");
        indicatorObj.transform.SetParent(transform);
        pathIndicator = indicatorObj.AddComponent<GremlinPathIndicator>();
        pathIndicator.footprintSpacing = pathFootprintSpacing;
        pathIndicator.maxPathDistance = pathMaxDistance;
        pathIndicator.footprintScale = 0.4f; // Slightly larger
        pathIndicator.footprintAlpha = 0.85f; // More visible
        pathIndicator.updateInterval = 2.0f; // Update less frequently
        pathIndicator.alternateFootOrientation = true;
        pathIndicator.fadeInFootprints = false; // No fade-in to prevent blinking
        pathIndicator.fadeOutOldFootprints = true; // Keep slow fade-out
        pathIndicator.enableDebugLogs = false; // Disable logs by default
    }

    void FindPlayer()
    {
        var playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null)
        {
            playerTransform = playerMovement.transform;
        }
        else
        {
            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) playerTransform = playerObject.transform;
        }
    }

    void CreateGremlinPrefab()
    {
        gremlinPrefab = new GameObject("Gremlin");

        var rb = gremlinPrefab.AddComponent<Rigidbody2D>();
        var sprite = gremlinPrefab.AddComponent<SpriteRenderer>();
        var collider = gremlinPrefab.AddComponent<CircleCollider2D>();
        gremlinPrefab.AddComponent<GremlinController>();

        rb.gravityScale = 0f;
        rb.linearDamping = 5f;
        rb.freezeRotation = true;
        collider.radius = 0.3f;

        gremlinPrefab.layer = 0;
        gremlinPrefab.tag = "Enemy";
        sprite.sprite = CreateTestSprite();
        sprite.color = Color.red;
        sprite.sortingOrder = 100;

        gremlinPrefab.SetActive(false);
    }

    Sprite CreateTestSprite()
    {
        var texture = new Texture2D(64, 64);
        var colors = new Color[64 * 64];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.red;
        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f, 100f);
    }

    void TrySpawn()
    {
        activeGremlins.RemoveAll(g => g == null);
        if (activeGremlins.Count >= maxGremlinsOnMap) return;

        if (playerTransform != null)
        {
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            Vector3 spawnPos = playerTransform.position + (Vector3)(randomDirection * spawnDistance);
            SpawnGremlinAt(spawnPos);
        }
    }

    void SpawnGremlinAt(Vector3 position)
    {
        if (gremlinPrefab == null) return;

        GameObject newGremlin = Instantiate(gremlinPrefab, position, Quaternion.identity);
        newGremlin.SetActive(true);

        // Play gremlin appearance sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.gremlinAppearance, position);
        }

        activeGremlins.Add(newGremlin);
        StartCoroutine(MonitorGremlin(newGremlin));
    }

    IEnumerator MonitorGremlin(GameObject gremlin)
    {
        while (gremlin != null) yield return new WaitForSeconds(1f);
        activeGremlins.Remove(gremlin);
    }

    [ContextMenu("Spawn Gremlin")]
    void SpawnNow() => TrySpawn();

    [ContextMenu("Toggle Path Indicator")]
    void TogglePathIndicator()
    {
        if (pathIndicator != null)
        {
            pathIndicator.gameObject.SetActive(!pathIndicator.gameObject.activeSelf);
        }
        else if (showPathToGremlin)
        {
            SetupPathIndicator();
        }
    }
}
