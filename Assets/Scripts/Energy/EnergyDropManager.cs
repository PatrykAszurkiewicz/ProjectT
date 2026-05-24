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

    /// <summary>
    /// Spawn a drop from a regular enemy using stage-scaled values from RunConfig.
    /// Call this from your enemy death handler instead of TrySpawnEnergyDrop.
    /// </summary>
    public static void TrySpawnEnemyDrop(Vector3 position, int stageIndex)
    {
        if (Instance == null)
        {
            var go = new GameObject("EnergyDropManager");
            go.AddComponent<EnergyDropManager>();
        }

        RunConfig cfg = GameOrchestrator.Instance?.runConfig;
        int value = StageEnergyScaling.EnemyDropValue(cfg, stageIndex);
        float chance = cfg != null ? Instance.globalDropChance : Instance.globalDropChance;
        Instance.SpawnEnergyDropInternal(position, chance, value);
    }

    /// <summary>
    /// Spawn a burst of drops from a boss using stage-scaled values from RunConfig.
    /// Call this from BaseBossStats / boss death handler.
    /// </summary>
    public static void SpawnBossDrop(Vector3 position, int stageIndex)
    {
        if (Instance == null)
        {
            var go = new GameObject("EnergyDropManager");
            go.AddComponent<EnergyDropManager>();
        }

        RunConfig cfg = GameOrchestrator.Instance?.runConfig;
        int totalValue = StageEnergyScaling.BossDropValue(cfg, stageIndex);
        int count = cfg != null ? cfg.bossDropCount : 5;
        int perDrop = Mathf.Max(1, Mathf.RoundToInt((float)totalValue / count));

        for (int i = 0; i < count; i++)
        {
            // Always spawn boss drops (chance = 1)
            Instance.SpawnEnergyDropInternal(position, 1f, perDrop);
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

        // Null-guard: EnergyManager may not exist (e.g. during scene transitions
        // or in test scenes). Fall back to a 1.0 multiplier if missing.
        var em = EnergyManager.Instance;
        float multiplier = em != null ? em.globalResourceMultiplier : 1f;
        int adjustedValue = Mathf.RoundToInt(energyValue * multiplier);

        GameObject drop = EnergyDrop.CreateEnergyDrop(spawnPos, adjustedValue);
        return drop;
    }

    GameObject CreateEnergyDrop(Vector3 position, int energyValue)
    {
        // Add random spread
        Vector3 spawnPos = position + (Vector3)Random.insideUnitCircle * spawnRadius;
        spawnPos.z = 0;

        // Null-guard: EnergyManager may not exist yet (e.g. during scene
        // transitions, or if the enemy died on the same frame the manager
        // was destroyed). Fall back to no scaling rather than throwing.
        var em = EnergyManager.Instance;
        if (em == null)
        {
            // Plain drop with the unscaled value — better than crashing.
            return EnergyDrop.CreateEnergyDrop(spawnPos, energyValue);
        }

        int adjustedValue = Mathf.RoundToInt(energyValue * em.globalResourceMultiplier);

        // Roll for bonus resources (also guarded by the null check above)
        if (em.bonusResourceDropChance > 0f &&
            Random.Range(0f, 1f) <= em.bonusResourceDropChance)
        {
            adjustedValue = Mathf.RoundToInt(adjustedValue * em.bonusResourceMultiplier);
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
        /*
        GUILayout.BeginArea(new Rect(10, 10, 300, 100));
        GUILayout.Label($"Energy Drops Spawned: {totalDropsSpawned}");
        GUILayout.Label($"Total Energy Spawned: {totalEnergySpawned}");
        GUILayout.Label($"Active Drops: {activeDrops.Count}");
        GUILayout.Label($"Player Found: {(playerTransform != null ? "Yes" : "No")}");
        GUILayout.EndArea();
        */
    }
#endif
}
