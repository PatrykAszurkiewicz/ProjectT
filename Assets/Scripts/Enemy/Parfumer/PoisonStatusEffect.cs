using UnityEngine;

// Lingering poison damage-over-time, applied to whatever GameObject carries a
// CharacterStats (the player). Dropped on by a PoisonCloud
[RequireComponent(typeof(CharacterStats))]
public class PoisonStatusEffect : MonoBehaviour
{
    private float remaining;             // seconds of poison left
    private float damagePerSecond;
    private GameObject source;           // for "killed by" attribution

    // Accumulate fractional damage and apply in whole-number ticks, so we don't
    // spam TakeDamage with sub-1 values every frame. Same pattern the Buffer
    // fog / scarecrow aura use against the player.
    private float damageAccumulator;

    private CharacterStats stats;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
    }

    /// Apply or refresh the poison. Call every frame the target is exposed.
    /// duration   — how long the poison should last from this exposure on.
    /// dps        — damage per second while poisoned.
    /// source     — the attacker (Parfumer) responsible, for attribution.
    public void Refresh(float duration, float dps, GameObject source)
    {
        // Refresh to full: re-exposure always restores the timer to (at least)
        // a fresh duration, so standing in the cloud keeps it pinned at full
        // and re-entering resets the countdown.
        remaining = Mathf.Max(remaining, duration);
        damagePerSecond = dps;
        this.source = source;
    }

    private void Update()
    {
        if (stats == null || remaining <= 0f)
        {
            Destroy(this);
            return;
        }

        remaining -= Time.deltaTime;

        // Don't tick a dead target.
        if (!stats.IsDead())
        {
            damageAccumulator += damagePerSecond * Time.deltaTime;
            if (damageAccumulator >= 1f)
            {
                int whole = Mathf.FloorToInt(damageAccumulator);
                damageAccumulator -= whole;
                stats.TakeDamage(whole);
            }
        }

        if (remaining <= 0f)
            Destroy(this);
    }
}
