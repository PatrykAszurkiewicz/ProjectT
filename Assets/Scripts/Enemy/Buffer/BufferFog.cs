using System.Collections.Generic;
using UnityEngine;

// A single fog patch dropped by a Buffer enemy.

public class BufferFog : MonoBehaviour
{
    private float radius = 2.5f;
    private float duration = 5f;
    private float damageBuff = 1.25f;
    private float playerDamagePerSecond = 6f;
    private GameObject attacker;

    private float elapsed = 0f;

    // Co-op: one damage accumulator PER player so each is taxed independently
    // (a single shared accumulator would split the fog DPS between them).
    private readonly Dictionary<CharacterStats, float> playerDamageAccumulators = new Dictionary<CharacterStats, float>();
    // Reused each frame to avoid allocations.
    private readonly HashSet<CharacterStats> _playersInsideThisFrame = new HashSet<CharacterStats>();
    private readonly List<CharacterStats> _accResetScratch = new List<CharacterStats>();

    // Enemies we've tagged. Tracked so we can strip buffs cleanly on expiry.
    private readonly HashSet<ScarecrowBuffTag> taggedEnemies = new HashSet<ScarecrowBuffTag>();

    // Per-frame scan buffer — reused; same pattern as ScarecrowStasisAura.
    private static readonly Collider2D[] _overlapBuffer = new Collider2D[64];
    private static readonly ContactFilter2D _overlapFilter = new ContactFilter2D().NoFilter();

    public void Configure(float radius, float duration, float damageBuff,
                          float playerDamagePerSecond, GameObject attacker)
    {
        this.radius = radius;
        this.duration = duration;
        this.damageBuff = damageBuff;
        this.playerDamagePerSecond = playerDamagePerSecond;
        this.attacker = attacker;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= duration)
        {
            // Lifetime ended — strip buffs and self-destruct.

            ClearAllBuffs();
            Destroy(gameObject);
            return;
        }

        ApplyBuffsToEnemiesInside();
        DamagePlayerIfInside();
    }

    private void ApplyBuffsToEnemiesInside()
    {
        int hitCount = Physics2D.OverlapCircle(
            transform.position, radius, _overlapFilter, _overlapBuffer);

        // Track who's inside this frame so we can strip the buff from anyone
        // who walked out.
        var nowInside = new HashSet<ScarecrowBuffTag>();

        for (int i = 0; i < hitCount; i++)
        {
            var col = _overlapBuffer[i];
            if (col == null) continue;

            var es = col.GetComponentInParent<EnemyStats>();
            if (es == null) continue;
            if (es.IsDead()) continue;
            // Buffers don't buff each other — keeps a wave of Buffers from
            // mutually amping themselves into nonsense damage. Same exclusion
            // pattern as ScarecrowStasisAura applies to Scarecrows.
            if (es.GetComponent<BufferController>() != null) continue;
            // Don't buff Gremlins — they're cowardly fleeing units, not
            // combatants. Same exclusion ScarecrowStasisAura uses.
            if (es.GetComponent<GremlinController>() != null) continue;

            var tag = es.GetComponent<ScarecrowBuffTag>();
            if (tag == null)
            {
                tag = es.gameObject.AddComponent<ScarecrowBuffTag>();
                tag.ApplyBuff(damageBuff);
            }
            nowInside.Add(tag);
        }

        // Strip buffs from anyone we were tagging that's no longer inside.
        if (taggedEnemies.Count > 0)
        {
            var toRemove = new List<ScarecrowBuffTag>();
            foreach (var tag in taggedEnemies)
            {
                if (tag == null || !nowInside.Contains(tag))
                    toRemove.Add(tag);
            }
            foreach (var tag in toRemove)
            {
                if (tag != null) tag.RemoveBuff();
                taggedEnemies.Remove(tag);
            }
        }

        foreach (var tag in nowInside)
            taggedEnemies.Add(tag);
    }

    private void DamagePlayerIfInside()
    {
        // Co-op: damage every alive player inside the fog. AllAliveInRadius
        // filters by distance + dead. Each player keeps their own accumulator so
        // sub-1 DPS doesn't spam TakeDamage and they tick on independent
        // schedules; a player who leaves has their accumulator reset so re-entry
        // doesn't dump a frame-1 spike. With one player this matches the old
        // single-player behavior exactly.
        _playersInsideThisFrame.Clear();

        foreach (var stats in PlayerRegistry.Instance.AllAliveInRadius(transform.position, radius))
        {
            if (stats == null) continue;
            _playersInsideThisFrame.Add(stats);

            float acc = playerDamageAccumulators.TryGetValue(stats, out var existing) ? existing : 0f;
            acc += playerDamagePerSecond * Time.deltaTime;
            if (acc >= 1f)
            {
                int whole = Mathf.FloorToInt(acc);
                acc -= whole;
                stats.TakeDamage(whole);
            }
            playerDamageAccumulators[stats] = acc;
        }

        // Reset the accumulator for anyone who is no longer inside (or who died).
        if (playerDamageAccumulators.Count > 0)
        {
            _accResetScratch.Clear();
            foreach (var kv in playerDamageAccumulators)
                if (!_playersInsideThisFrame.Contains(kv.Key))
                    _accResetScratch.Add(kv.Key);

            for (int i = 0; i < _accResetScratch.Count; i++)
                playerDamageAccumulators.Remove(_accResetScratch[i]);
        }
    }

    private void ClearAllBuffs()
    {
        foreach (var tag in taggedEnemies)
            if (tag != null) tag.RemoveBuff();
        taggedEnemies.Clear();
    }

    private void OnDestroy()
    {
        // Defensive cleanup if the fog is destroyed externally (scene unload,
        // wave reset, etc.) before its timer ends.
        ClearAllBuffs();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.15f, 0.7f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}

