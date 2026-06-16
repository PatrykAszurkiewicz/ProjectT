using System.Collections.Generic;
using UnityEngine;

// Stasis aura logic for the Scarecrow. Runs on a child GameObject
//   1. Finds all EnemyStats inside <see cref="radius"/> and tags them with
//      <see cref="ScarecrowBuffTag"/> (handles damage-multiplier + heal-tick).
//   2. Untags any enemy that has wandered out of range.
//   3. Damages the player while they're inside the radius.

public class ScarecrowStasisAura : MonoBehaviour
{
    private Scarecrow owner;
    private float radius = 4f;
    private float damageBuff = 1.2f;
    private float healPerSecond = 5f;
    private float playerDamagePerSecond = 8f;

    private bool active = false;

    // Tracks which enemies currently have our buff so we can remove it when
    // they leave the radius (or when the aura turns off).
    private readonly HashSet<ScarecrowBuffTag> taggedEnemies = new HashSet<ScarecrowBuffTag>();

    // Co-op: one damage accumulator PER player so each is taxed independently
    // (a single shared accumulator would split the aura DPS between them).
    private readonly Dictionary<CharacterStats, float> playerDamageAccumulators = new Dictionary<CharacterStats, float>();
    // Reused each Update to avoid per-frame allocations.
    private readonly HashSet<CharacterStats> _playersInsideThisFrame = new HashSet<CharacterStats>();
    private readonly List<CharacterStats> _accResetScratch = new List<CharacterStats>();

    private float coreDamageAccumulator = 0f;

    // Per-frame allocation churn would be nasty here; reuse a buffer.
    private static readonly Collider2D[] _overlapBuffer = new Collider2D[64];

    // Cached "match everything" contact filter 
    private static readonly ContactFilter2D _overlapFilter = new ContactFilter2D().NoFilter();

    public void Configure(Scarecrow owner, float radius, float damageBuff, float healPerSecond, float playerDamagePerSecond)
    {
        this.owner = owner;
        this.radius = radius;
        this.damageBuff = damageBuff;
        this.healPerSecond = healPerSecond;
        this.playerDamagePerSecond = playerDamagePerSecond;
    }

    public void SetActive(bool on)
    {
        if (active == on) return;
        active = on;

        var visual = GetComponent<ScarecrowAuraVisual>();
        if (visual != null) visual.SetActive(on);

        if (!on)
        {
            // Strip buffs from everyone we were affecting.
            foreach (var tag in taggedEnemies)
            {
                if (tag != null) tag.RemoveBuff();
            }
            taggedEnemies.Clear();
            playerDamageAccumulators.Clear();
            coreDamageAccumulator = 0f;
        }
    }

    private void Update()
    {
        if (!active) return;

        // Enemies in range: tag/heal 

        int hitCount = Physics2D.OverlapCircle(transform.position, radius, _overlapFilter, _overlapBuffer);
        var nowInside = new HashSet<ScarecrowBuffTag>();

        for (int i = 0; i < hitCount; i++)
        {
            var col = _overlapBuffer[i];
            if (col == null) continue;

            // Skip self & other scarecrows — they don't buff each other.
            var es = col.GetComponentInParent<EnemyStats>();
            if (es == null) continue;
            if (es.IsDead()) continue;
            if (es.GetComponent<Scarecrow>() != null) continue;
            // Skip Gremlins
            if (es.GetComponent<GremlinController>() != null) continue;

            var tag = es.GetComponent<ScarecrowBuffTag>();
            if (tag == null)
            {
                tag = es.gameObject.AddComponent<ScarecrowBuffTag>();
                tag.ApplyBuff(damageBuff);
            }

            // Heal this enemy by healPerSecond * dt. Heal() clamps to maxHealth.
            es.Heal(healPerSecond * Time.deltaTime);

            nowInside.Add(tag);
        }

        // Anyone we were tagging that's no longer in range — remove buff.
        // Iterate over a snapshot to avoid modifying-while-enumerating.
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

        // Union — add the new ones we found this frame.
        foreach (var tag in nowInside)
            taggedEnemies.Add(tag);

        //  Player in range: damage 
        DamagePlayerIfInside();

        //  Core in range: damage 
        DamageCoreIfInside();
    }

    private void DamageCoreIfInside()
    {
        GameObject coreGO = GameObject.FindGameObjectWithTag("Core");
        if (coreGO == null) return;

        Vector2 toCore = (Vector2)coreGO.transform.position - (Vector2)transform.position;
        if (toCore.sqrMagnitude > radius * radius)
        {
            coreDamageAccumulator = 0f;
            return;
        }

        // Use the same DPS rate as the player. Could split into its own field
        // if you want a different core-DPS later.
        coreDamageAccumulator += playerDamagePerSecond * Time.deltaTime;
        if (coreDamageAccumulator >= 1f)
        {
            int whole = Mathf.FloorToInt(coreDamageAccumulator);
            coreDamageAccumulator -= whole;

            var consumer = coreGO.GetComponent<IEnergyConsumer>();
            if (consumer != null && EnergyManager.Instance != null)
            {
                // Pass the scarecrow as the attacker (owner's gameObject) so
                // any "killed by" tracking attributes the core damage to us.
                GameObject attacker = (owner != null) ? owner.gameObject : this.gameObject;
                EnergyManager.Instance.DamageEnergyConsumer(consumer, whole, attacker);
            }
        }
    }

    private void DamagePlayerIfInside()
    {
        // Co-op: tax every alive player inside the radius. AllAliveInRadius
        // filters by distance + dead. Each player gets their own accumulator so
        // sub-1 DPS doesn't spam TakeDamage and they tick on independent
        // schedules. A player who leaves has their accumulator reset so re-entry
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

    private void OnDestroy()
    {
        // Defensive cleanup if the aura is destroyed mid-pulse.
        foreach (var tag in taggedEnemies)
            if (tag != null) tag.RemoveBuff();
        taggedEnemies.Clear();
    }
}

// Attached at runtime to any EnemyStats currently being buffed by a scarecrow
// aura. Multiplies the host enemy's damage by <see cref="multiplier"/> while
// alive; removed when the enemy leaves the aura or the aura turns off.
public class ScarecrowBuffTag : MonoBehaviour
{
    private EnemyStats stats;
    private float multiplier = 1f;
    private float appliedDamageMultiplier = 1f;
    private bool buffApplied = false;

    private float originalDamage;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
    }

    public void ApplyBuff(float multiplier)
    {
        if (buffApplied) return;
        if (stats == null || stats.enemyData == null) return;

        this.multiplier = multiplier;
        this.appliedDamageMultiplier = multiplier;
        originalDamage = stats.enemyData.damage;
        stats.enemyData.damage = originalDamage * multiplier;
        buffApplied = true;
    }

    public void RemoveBuff()
    {
        if (!buffApplied) return;
        if (stats == null || stats.enemyData == null)
        {
            // Enemy already destroyed — nothing to undo on a missing asset.
            buffApplied = false;
            Destroy(this);
            return;
        }

        // Undo
        if (appliedDamageMultiplier > 0f)
            stats.enemyData.damage = stats.enemyData.damage / appliedDamageMultiplier;

        buffApplied = false;
        Destroy(this);
    }

    private void OnDestroy()
    {

        if (buffApplied && stats != null && stats.enemyData != null && appliedDamageMultiplier > 0f)
        {
            stats.enemyData.damage = stats.enemyData.damage / appliedDamageMultiplier;
            buffApplied = false;
        }
    }
}

