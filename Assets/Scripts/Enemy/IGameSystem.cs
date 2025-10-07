public interface IGameSystem
{
    void Initialize(GameOrchestrator orchestrator);
    void Shutdown();
}

public interface IEnemyStatProvider
{
    float GetMoveSpeedMultiplier();
    float GetDamageMultiplier();
    float GetHealthMultiplier();
    void ApplyMoveSpeedMultiplier(float multiplier);
    void ApplyDamageMultiplier(float multiplier);
    void ApplyHealthMultiplier(float multiplier);
}
