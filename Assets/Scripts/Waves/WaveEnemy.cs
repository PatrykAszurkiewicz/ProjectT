using UnityEngine;

// Marker component added to enemies spawned by the wave system. Detects when the enemy dies and notifies the orchestrator.

public class WaveEnemy : MonoBehaviour
{
    private bool notified = false;
    // TODO verify if another death trigger should apply here
    // Fires when EnemyDeathVFX.Trigger() disables all MonoBehaviours, which is the moment the enemy is logically dead.
    void OnDisable()
    {
        if (notified) return;
        if (!gameObject.scene.isLoaded) return;

        notified = true;

        if (GameOrchestrator.Instance != null &&
            GameOrchestrator.Instance.CurrentState != GameOrchestrator.RunState.Idle)
        {
            GameOrchestrator.Instance.OnEnemyDeath();
        }
    }
}
