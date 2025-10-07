using UnityEngine;
using System.Collections.Generic;

public class GameOrchestrator : MonoBehaviour
{
    public static GameOrchestrator Instance { get; private set; }

    // All game systems
    private EnergyManager energyManager;
    private EnemyStatModifierManager enemyStatManager;
    private AudioManager audioManager;
    private WaveSpawner waveSpawner;
    private AugmentRegistry augmentRegistry;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeSystems();
    }

    private void InitializeSystems()
    {
        // Initialize in correct order
        energyManager = FindOrCreateSystem<EnergyManager>();
        enemyStatManager = FindOrCreateSystem<EnemyStatModifierManager>();
        audioManager = FindOrCreateSystem<AudioManager>();
        augmentRegistry = FindOrCreateSystem<AugmentRegistry>();

        // Initialize each system with orchestrator reference
        if (enemyStatManager is IGameSystem system)
            system.Initialize(this);
    }

    private T FindOrCreateSystem<T>() where T : Component
    {
        T system = FindFirstObjectByType<T>();
        if (system == null)
        {
            GameObject go = new GameObject(typeof(T).Name);
            go.transform.SetParent(transform);
            system = go.AddComponent<T>();
        }
        return system;
    }

    public T GetSystem<T>() where T : Component
    {
        // Service locator pattern
        return GetComponentInChildren<T>();
    }

    void OnDestroy()
    {
        // Cleanup all systems
        if (enemyStatManager is IGameSystem system)
            system.Shutdown();
    }
}