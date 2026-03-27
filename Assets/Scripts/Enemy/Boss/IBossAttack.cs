using UnityEngine;

/// Interface for boss attack patterns. Implement this for different boss attacks.
/// TODO Remove?
public interface IBossAttack
{
    void Initialize(Transform bossTransform);
    void PerformAttack(Transform target);
    bool CanAttack();
    float GetCooldown();
    float GetRange();
}
