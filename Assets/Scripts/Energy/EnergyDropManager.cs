using UnityEngine;
using System.Collections.Generic;

public class EnergyDropManager : MonoBehaviour
{
    public static EnergyDropManager Instance { get; private set; }

    [Header("Global Drop Settings")]
    [Range(0f, 1f)] public float globalDropChance = 0.5f;
    public int defaultEnergyValue = 10;
    public float dropLifetime = 30f;

    [Header("Drop Physics")]
    public float spawnForce = 1.5f;
    public float spawnRadius = 0.3f;

    // Player reference (auto-found)
    private static Transform playerTransform;
    private static PlayerEnergyCollector playerCollector;

    // Statistics
    private int totalDropsSpawned = 0;
    private int totalEnergySpawned = 0;
    private List<GameObject> activeDrops = new List<GameObject>();

    void Awake()
    {
        // Singleton
        if (Instance == null)
        {
            Instance = this;
            if (transform.parent != null)
            {
                transform.SetParent(null);
            }

            DontDestroyOnLoad(gameObject);
            InitializeSystem();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Auto-find and setup player
        FindAndSetupPlayer();
    }

    void InitializeSystem()
    {
        // Create the manager if it doesn't exist
        if (GameObject.Find("EnergyDropManager") == null)
        {
            gameObject.name = "EnergyDropManager";
        }
    }

    void FindAndSetupPlayer()
    {
        if (playerTransform != null) return;

        // Find player using multiple methods
        var playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null)
        {
            SetupPlayer(playerMovement.transform);
            return;
        }

        var playerStats = FindFirstObjectByType<PlayerStats>();
        if (playerStats != null)
        {
            SetupPlayer(playerStats.transform);
            return;
        }

        var playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            SetupPlayer(playerObject.transform);
        }
    }

    void SetupPlayer(Transform player)
    {
        playerTransform = player;

        // Auto-add PlayerEnergyCollector if not present
        playerCollector = player.GetComponent<PlayerEnergyCollector>();
        if (playerCollector == null)
        {
            playerCollector = player.gameObject.AddComponent<PlayerEnergyCollector>();
            //Debug.Log("EnergyDropManager: Auto-added PlayerEnergyCollector to player");
        }
    }

    public static void TrySpawnEnergyDrop(Vector3 position, float customDropChance = -1f, int customEnergyValue = -1)
    {
        if (Instance == null)
        {
            // Create manager if it doesn't exist
            var managerObj = new GameObject("EnergyDropManager");
            managerObj.AddComponent<EnergyDropManager>();
        }

        Instance.SpawnEnergyDropInternal(position, customDropChance, customEnergyValue);
    }

    void SpawnEnergyDropInternal(Vector3 position, float customDropChance, int customEnergyValue)
    {
        float dropChance = customDropChance >= 0 ? customDropChance : globalDropChance;
        if (Random.Range(0f, 1f) > dropChance) return;
        int energyValue = customEnergyValue > 0 ? customEnergyValue : defaultEnergyValue;
        GameObject drop = CreateEnergyDrop(position, energyValue);
        // Track statistics
        totalDropsSpawned++;
        totalEnergySpawned += energyValue;
        activeDrops.Add(drop);
        //Debug.Log($"EnergyDropManager: Spawned energy drop worth {energyValue} at {position}");
    }

    GameObject CreateEnergyDropOld(Vector3 position, int energyValue)
    {
        // Add random spread
        Vector3 spawnPos = position + (Vector3)Random.insideUnitCircle * spawnRadius;
        spawnPos.z = 0;
        int adjustedValue = Mathf.RoundToInt(energyValue * EnergyManager.Instance.globalResourceMultiplier);
        GameObject drop = EnergyDrop.CreateEnergyDrop(spawnPos, adjustedValue);
        //GameObject drop = EnergyDrop.CreateEnergyDrop(spawnPos, energyValue);
        return drop;
    }

    GameObject CreateEnergyDrop(Vector3 position, int energyValue)
    {
        // Add random spread
        Vector3 spawnPos = position + (Vector3)Random.insideUnitCircle * spawnRadius;
        spawnPos.z = 0;

        int adjustedValue = Mathf.RoundToInt(energyValue * EnergyManager.Instance.globalResourceMultiplier);

        // Roll for bonus resources
        if (EnergyManager.Instance.bonusResourceDropChance > 0f &&
            Random.Range(0f, 1f) <= EnergyManager.Instance.bonusResourceDropChance)
        {
            adjustedValue = Mathf.RoundToInt(adjustedValue * EnergyManager.Instance.bonusResourceMultiplier);
            //Debug.Log($"Bonus resources! {adjustedValue} energy");
        }

        GameObject drop = EnergyDrop.CreateEnergyDrop(spawnPos, adjustedValue);
        return drop;
    }


    // Registration methods for player collector
    public static void RegisterPlayerCollector(PlayerEnergyCollector collector)
    {
        playerCollector = collector;
        if (collector != null)
        {
            playerTransform = collector.transform;
        }
    }

    public static void UnregisterPlayerCollector(PlayerEnergyCollector collector)
    {
        if (playerCollector == collector)
        {
            playerCollector = null;
        }
    }

    // Public getters
    public static Transform GetPlayerTransform()
    {
        if (Instance != null && playerTransform == null)
        {
            Instance.FindAndSetupPlayer();
        }
        return playerTransform;
    }

    // Cleanup method
    void Update()
    {
        CleanupDestroyedDrops();
    }

    void CleanupDestroyedDrops()
    {
        activeDrops.RemoveAll(drop => drop == null);
    }

    // Configuration methods
    public static void SetGlobalDropChance(float chance)
    {
        if (Instance != null)
        {
            Instance.globalDropChance = Mathf.Clamp01(chance);
        }
    }

    public static void SetDefaultEnergyValue(int value)
    {
        if (Instance != null)
        {
            Instance.defaultEnergyValue = Mathf.Max(1, value);
        }
    }

    // Statistics
    public static int GetTotalDropsSpawned() => Instance?.totalDropsSpawned ?? 0;
    public static int GetTotalEnergySpawned() => Instance?.totalEnergySpawned ?? 0;
    public static int GetActiveDropCount() => Instance?.activeDrops.Count ?? 0;

#if UNITY_EDITOR
    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 100));
        GUILayout.Label($"Energy Drops Spawned: {totalDropsSpawned}");
        GUILayout.Label($"Total Energy Spawned: {totalEnergySpawned}");
        GUILayout.Label($"Active Drops: {activeDrops.Count}");
        GUILayout.Label($"Player Found: {(playerTransform != null ? "Yes" : "No")}");
        GUILayout.EndArea();
    }
#endif
}