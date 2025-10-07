using UnityEngine;
using System.Collections.Generic;

public class EnemyStatModifierManager : MonoBehaviour, IGameSystem, IEnemyStatProvider
{
    public static EnemyStatModifierManager Instance { get; private set; }

    [Header("Global Enemy Modifiers")]
    [SerializeField] private float moveSpeedMultiplier = 1f;
    [SerializeField] private float damageMultiplier = 1f;
    [SerializeField] private float healthMultiplier = 1f;

    // Track all living enemies for retroactive health changes
    private HashSet<EnemyStats> trackedEnemies = new HashSet<EnemyStats>();

    private GameOrchestrator orchestrator;

    #region Singleton (can be replaced by orchestrator injection)
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Don't use DontDestroyOnLoad if using orchestrator
            // DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    #endregion

    #region IGameSystem
    public void Initialize(GameOrchestrator orchestrator)
    {
        this.orchestrator = orchestrator;
        //Debug.Log("[ENEMY_MODIFIER] Initialized by GameOrchestrator");
    }

    public void Shutdown()
    {
        trackedEnemies.Clear();
        Instance = null;
    }
    #endregion

    #region Enemy Tracking (for retroactive health changes)
    public void RegisterEnemy(EnemyStats enemy)
    {
        if (enemy != null)
        {
            trackedEnemies.Add(enemy);
        }
    }

    public void UnregisterEnemy(EnemyStats enemy)
    {
        trackedEnemies.Remove(enemy);
    }

    private void ApplyHealthChangeToExistingEnemies(float oldMultiplier, float newMultiplier)
    {
        if (Mathf.Approximately(oldMultiplier, newMultiplier)) return;

        float ratio = newMultiplier / oldMultiplier;
        int affectedCount = 0;

        foreach (var enemy in trackedEnemies)
        {
            if (enemy == null || enemy.IsDead()) continue;

            // Scale both max and current health proportionally
            float oldMaxHealth = enemy.maxHealth;
            float oldCurrentHealth = enemy.currentHealth;
            float healthPercentage = oldCurrentHealth / oldMaxHealth;

            enemy.maxHealth *= ratio;
            enemy.currentHealth = enemy.maxHealth * healthPercentage;

            affectedCount++;
        }

        //Debug.Log($"[ENEMY_MODIFIER] Health changed: {oldMultiplier:F3}x -> {newMultiplier:F3}x. Affected {affectedCount} living enemies.");
    }
    #endregion

    #region IEnemyStatProvider
    public void ApplyMoveSpeedMultiplier(float multiplier)
    {
        float oldValue = moveSpeedMultiplier;
        moveSpeedMultiplier *= multiplier;
        //Debug.Log($"[ENEMY_MODIFIER] Move speed: {oldValue:F3}x -> {moveSpeedMultiplier:F3}x (applied {multiplier:F2}x)");
    }

    public void ApplyDamageMultiplier(float multiplier)
    {
        float oldValue = damageMultiplier;
        damageMultiplier *= multiplier;
        Debug.Log($"[ENEMY_MODIFIER] Damage: {oldValue:F3}x -> {damageMultiplier:F3}x (applied {multiplier:F2}x)");
    }

    public void ApplyHealthMultiplier(float multiplier)
    {
        float oldValue = healthMultiplier;
        healthMultiplier *= multiplier;
        // Apply to existing enemies retroactively
        Debug.Log($"[ENEMY_MODIFIER] Health: {oldValue:F3}x -> {healthMultiplier:F3}x (applied {multiplier:F2}x)");
        ApplyHealthChangeToExistingEnemies(oldValue, healthMultiplier);
    }

    public float GetMoveSpeedMultiplier() => moveSpeedMultiplier;
    public float GetDamageMultiplier() => damageMultiplier;
    public float GetHealthMultiplier() => healthMultiplier;
    #endregion

    public void ResetModifiers()
    {
        float oldHealthMultiplier = healthMultiplier;
        moveSpeedMultiplier = 1f;
        damageMultiplier = 1f;
        healthMultiplier = 1f;
        ApplyHealthChangeToExistingEnemies(oldHealthMultiplier, 1f);
        //Debug.Log("[ENEMY_MODIFIER] All modifiers reset to 1.0x");
    }

#if UNITY_EDITOR
    [ContextMenu("Log Current State")]
    void LogState()
    {
        //Debug.Log($"=== Enemy Modifier State ===");
        //Debug.Log($"Move Speed: {moveSpeedMultiplier:F3}x");
        //Debug.Log($"Damage: {damageMultiplier:F3}x");
        //Debug.Log($"Health: {healthMultiplier:F3}x");
        //Debug.Log($"Tracked Enemies: {trackedEnemies.Count}");
    }
#endif
}
