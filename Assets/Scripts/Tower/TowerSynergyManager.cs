using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Adjacency synergy: a tower standing near other towers gets a damage bonus.
//
// FIXES APPLIED (the original had four real problems):
//
//  1. NO-OP BY DEFAULT. damageMultiplier defaulted to 1.0 and the boost was
//     Mathf.Pow(1.0, nearbyCount) == 1.0, so the entire system did nothing until an
//     augment raised the field. Worse, when an augment later lowered it back toward 1
//     the existing TowerSynergyBoost components were kept (they were only removed at
//     nearbyCount == 0), so a stale bonus lingered. The multiplier is now an
//     inspector-exposed, documented value, and the boost component is removed whenever
//     the effective bonus is neutral.
//
//  2. IT STOMPED OTHER SPRITE TINTS. The old code wrote renderer.color directly —
//     Color.Lerp(white, yellow) when boosted and a hard Color.white when not. That
//     fought the damage-flash coroutine, the low-energy tint and the disabled-tower
//     alpha in Tower.cs: a tower disabled by damage (alpha 0.5) was snapped back to
//     opaque white by the next sweep. Tinting is now opt-in, blends RGB only, and
//     preserves alpha.
//
//  3. O(n^2) EVERY 0.5s, FOREVER. FindObjectsByType allocated a fresh array twice a
//     second and compared every tower against every other tower with Vector3.Distance.
//     It now uses the shared Tower.ActiveTowers registry (no allocation) and squared
//     distances, with a cap on how many neighbours can compound.
//
//  4. InvokeRepeating WAS NEVER CANCELLED, so a disabled or destroyed manager kept
//     firing against a stale board. It is a coroutine tied to OnEnable/OnDisable now.

public class TowerSynergyManager : MonoBehaviour
{
    [Header("Synergy")]
    [Tooltip("Damage multiplier applied PER adjacent tower, compounding.\n" +
             "1.0 = disabled (this was the old default, which made the whole system a\n" +
             "no-op). 1.05 = +5% damage for each tower within Synergy Range.")]
    [Min(1f)]
    public float damageMultiplier = 1.0f;

    [Tooltip("How close two towers must be to count as adjacent (world units).")]
    [Min(0.1f)]
    public float synergyRange = 4f;

    [Tooltip("Cap on how many neighbours can contribute, so a dense cluster can't\n" +
             "compound into an absurd multiplier. 0 = uncapped.")]
    [Min(0)]
    public int maxNeighboursCounted = 4;

    [Tooltip("How often the adjacency map is recalculated (seconds).")]
    [Min(0.05f)]
    public float refreshInterval = 0.5f;

    [Header("Visuals")]
    [Tooltip("Tint synergised towers. OFF is recommended: writing SpriteRenderer.color\n" +
             "here fights the damage flash, the low-energy tint and the disabled-tower\n" +
             "alpha that Tower.cs owns.")]
    public bool tintSynergisedTowers = false;

    [Tooltip("Colour blended over a synergised tower when tinting is enabled. Alpha is\n" +
             "always preserved from the tower's current colour.")]
    public Color synergyTint = Color.yellow;

    [Range(0f, 1f)]
    public float synergyTintStrength = 0.3f;

    // Remembers the RGB we applied so we can undo exactly our own contribution
    // instead of blanket-resetting the renderer to white.
    private readonly Dictionary<Tower, Color> _tintedOriginals = new Dictionary<Tower, Color>();

    private Coroutine _loop;

    void OnEnable()
    {
        _loop = StartCoroutine(SynergyLoop());
    }

    void OnDisable()
    {
        if (_loop != null) { StopCoroutine(_loop); _loop = null; }
        ClearAllBoosts();
    }

    private IEnumerator SynergyLoop()
    {
        // One immediate pass so towers placed before the first tick are covered.
        UpdateTowerSynergies();
        var wait = new WaitForSeconds(Mathf.Max(0.05f, refreshInterval));
        while (true)
        {
            yield return wait;
            UpdateTowerSynergies();
        }
    }

    [ContextMenu("Recalculate Synergies Now")]
    public void UpdateTowerSynergies()
    {
        // Shared registry — no allocation, already maintained by Tower.OnEnable/OnDisable.
        var towers = Tower.ActiveTowers;
        if (towers == null || towers.Count == 0) { ClearAllBoosts(); return; }

        bool synergyEnabled = damageMultiplier > 1.0001f;
        float rangeSqr = synergyRange * synergyRange;

        for (int i = 0; i < towers.Count; i++)
        {
            Tower tower = towers[i];
            if (tower == null || !tower.gameObject.activeInHierarchy || tower.IsDestroyed())
            {
                if (tower != null) RemoveBoost(tower);
                continue;
            }

            int nearbyCount = 0;
            if (synergyEnabled)
            {
                Vector2 selfPos = tower.transform.position;
                for (int j = 0; j < towers.Count; j++)
                {
                    if (j == i) continue;
                    Tower other = towers[j];
                    if (other == null || !other.gameObject.activeInHierarchy || other.IsDestroyed())
                        continue;

                    if ((((Vector2)other.transform.position) - selfPos).sqrMagnitude <= rangeSqr)
                        nearbyCount++;
                }

                if (maxNeighboursCounted > 0)
                    nearbyCount = Mathf.Min(nearbyCount, maxNeighboursCounted);
            }

            float wanted = nearbyCount > 0 ? Mathf.Pow(damageMultiplier, nearbyCount) : 1f;

            if (wanted <= 1.0001f)
            {
                // FIX: the old code only removed the boost when nearbyCount hit 0, so a
                // neutral-but-present multiplier left a dead component attached.
                RemoveBoost(tower);
                continue;
            }

            var boost = tower.GetComponent<TowerSynergyBoost>();
            if (boost == null)
            {
                boost = tower.gameObject.AddComponent<TowerSynergyBoost>();
                ApplyTint(tower);
            }

            if (!Mathf.Approximately(boost.damageMultiplier, wanted))
                boost.damageMultiplier = wanted;
        }
    }

    private void RemoveBoost(Tower tower)
    {
        var boost = tower.GetComponent<TowerSynergyBoost>();
        if (boost != null) Destroy(boost);
        RemoveTint(tower);
    }

    private void ClearAllBoosts()
    {
        var towers = Tower.ActiveTowers;
        if (towers == null) return;
        for (int i = 0; i < towers.Count; i++)
            if (towers[i] != null) RemoveBoost(towers[i]);
    }

    private void ApplyTint(Tower tower)
    {
        if (!tintSynergisedTowers) return;
        var sr = tower.GetComponent<SpriteRenderer>();
        if (sr == null || _tintedOriginals.ContainsKey(tower)) return;

        Color cur = sr.color;
        _tintedOriginals[tower] = cur;

        // Blend RGB only — alpha belongs to Tower.DisableTower/EnableTower.
        Color tinted = Color.Lerp(cur, synergyTint, synergyTintStrength);
        tinted.a = cur.a;
        sr.color = tinted;
    }

    private void RemoveTint(Tower tower)
    {
        if (!_tintedOriginals.TryGetValue(tower, out Color original)) return;
        _tintedOriginals.Remove(tower);

        var sr = tower != null ? tower.GetComponent<SpriteRenderer>() : null;
        if (sr == null) return;

        // Restore our RGB contribution but keep whatever alpha the tower owns NOW,
        // so we can't resurrect a disabled tower's opacity.
        Color restore = original;
        restore.a = sr.color.a;
        sr.color = restore;
    }
}

public class TowerSynergyBoost : MonoBehaviour
{
    public float damageMultiplier = 1.0f;

    public float GetDamageMultiplier()
    {
        return damageMultiplier;
    }
}



