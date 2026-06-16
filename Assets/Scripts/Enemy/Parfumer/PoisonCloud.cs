using UnityEngine;


// A single green poison patch dropped by a Parfumer enemy.
// Structurally the sibling of BufferFog, but its job is different:
//   * It does NOT buff enemies (the Parfumer is a pure anti-player hazard).
//   * It does NOT damage the player directly while inside. Instead, every
//     frame the player is inside it (re)stamps a PoisonStatusEffect on them,
//     which keeps ticking damage for `poisonDuration` seconds AFTER they leave
//     the cloud.
public class PoisonCloud : MonoBehaviour
{
    private float radius = 2.5f;
    private float duration = 5f;                 // how long the cloud lingers
    private float poisonDuration = 20f;          // poison time granted per exposure
    private float poisonDamagePerSecond = 6f;
    private GameObject attacker;

    private float elapsed = 0f;

    public void Configure(float radius, float duration, float poisonDuration,
                          float poisonDamagePerSecond, GameObject attacker)
    {
        this.radius = radius;
        this.duration = duration;
        this.poisonDuration = poisonDuration;
        this.poisonDamagePerSecond = poisonDamagePerSecond;
        this.attacker = attacker;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= duration)
        {
            // Lifetime ended
            Destroy(gameObject);
            return;
        }

        ApplyPoisonToPlayerIfInside();
    }

    private void ApplyPoisonToPlayerIfInside()
    {
        // Co-op: poison every alive player standing in the cloud (two players in
        // the same cloud should BOTH be poisoned). AllAliveInRadius filters by
        // distance + dead. PoisonStatusEffect is already per-player (it lives on
        // the player object), so each player keeps their own DoT. With one player
        // this is identical to the old single lookup.
        foreach (var stats in PlayerRegistry.Instance.AllAliveInRadius(transform.position, radius))
        {
            if (stats == null) continue;
            var go = stats.gameObject;

            var poison = go.GetComponent<PoisonStatusEffect>();
            if (poison == null) poison = go.AddComponent<PoisonStatusEffect>();
            poison.Refresh(poisonDuration, poisonDamagePerSecond, attacker);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.7f, 0.15f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}

