using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GremlinSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    public float spawnInterval = 10f;
    public int maxGremlinsOnMap = 1;
    public float spawnDistance = 8f;

    private List<GameObject> activeGremlins = new List<GameObject>();
    private Transform playerTransform;
    private GameObject gremlinPrefab;

    void Start()
    {
        FindPlayer();
        CreateGremlinPrefab();
        InvokeRepeating(nameof(TrySpawn), 2f, spawnInterval);
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
}