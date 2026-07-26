using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One row of the vortex's spawn table. Drag a prefab in, give it a weight
[System.Serializable]
public class VortexSpawnEntry
{
    [Tooltip("Any enemy prefab. Anything with EnemyStats works — Slime, Splitter, " +
             "Pitcher, Insect. The vortex doesn't care what it births.")]
    public GameObject prefab;

    [Tooltip("Relative pick chance. An entry at 3 is picked three times as often " +
             "as one at 1. Set 0 to disable the row without deleting it.")]
    [Min(0)] public int weight = 1;

    [Tooltip("Cap on how many of THIS prefab may be alive from this vortex at once. " +
             "0 = no per-prefab cap. Useful for 'at most one Brute'.")]
    [Min(0)] public int maxSimultaneous = 0;

    [Tooltip("Scale MULTIPLIER for this prefab when the vortex spawns it. Leave at " +
             "1 to use the prefab's own scale. Set to 4 if the prefab is a small " +
             "'child' variant (like SmallSlime at 0.25) that is normally resized by " +
             "whatever spawns it — the vortex spawns it cold, so it needs the boost.")]
    [Min(0.01f)] public float spawnScaleMultiplier = 1f;
}


// VORTEX — a killable rift that emits enemies
// Immobile. No EnemyController, no EnemyAnimationController, no sprites. It
// places itself on a ring at a chosen distance from the core, then births waves
// from its accretion disk on a fixed cadence.
[RequireComponent(typeof(VortexStats))]
[RequireComponent(typeof(VortexVisual))]
public class VortexSpawner : MonoBehaviour
{
    [Header("Spawn Table")]
    [Tooltip("Drag enemy prefabs here. Picked by weight, each spawn independently.")]
    [SerializeField] private List<VortexSpawnEntry> spawnTable = new List<VortexSpawnEntry>();

    [Header("Cadence")]
    [Tooltip("Seconds between waves. THIS is the frequency knob.")]
    [SerializeField] private float spawnInterval = 5f;

    [Tooltip("Seconds before the first wave. Gives the player time to notice the " +
             "vortex and decide whether to deal with it now or later.")]
    [SerializeField] private float initialDelay = 2.5f;

    [Tooltip("Enemies released per wave. THIS is the simultaneous-count knob.")]
    [Min(1)][SerializeField] private int enemiesPerWave = 3;

    [Tooltip("Seconds between each enemy WITHIN a wave. With the spit animation on, " +
             "the effective gap is at least the spit peak (~0.28s at defaults), so " +
             "each enemy gets its own visible heave. 0.35+ keeps them distinct.")]
    [SerializeField] private float intraWaveDelay = 0.35f;

    [Tooltip("Hard cap on enemies alive from THIS vortex. Waves are skipped while " +
             "the cap is met. 0 = unlimited (do not ship this at 0).")]
    [Min(0)][SerializeField] private int maxAliveFromThisVortex = 12;

    [Tooltip("Total enemies this vortex will ever spawn before going dormant " +
             "(it stays killable). 0 = forever.")]
    [Min(0)][SerializeField] private int lifetimeSpawnBudget = 0;

    [Header("Birth")]
    [Tooltip("Where on the disk enemies appear, as a fraction of diskRadius. " +
             "0.85 births them just inside the visible edge.")]
    [Range(0.2f, 1.4f)][SerializeField] private float birthRadiusScale = 0.85f;

    [Tooltip("Enemies spawn on an arc of this width (degrees) centred on the " +
             "TARGET direction, so none is born behind the vortex where it would " +
             "have to path around the collider. 0 = all from one point toward the " +
             "target; 120 = a wide facing fan; 360 = all around (the old behaviour, " +
             "which caused the 'stuck behind' problem).")]
    [Range(0f, 360f)][SerializeField] private float spawnArcDegrees = 120f;

    [Tooltip("Outward shove given to each newborn, so they clear the disk instead " +
             "of piling on the horizon.")]
    [SerializeField] private float birthPushSpeed = 6f;

    [SerializeField] private float birthPushDuration = 0.3f;

    [Tooltip("Seconds for a newborn to scale from nothing to full size. It's " +
             "intangible and unhittable for this long — keep it short.")]
    [SerializeField] private float emergeDuration = 0.35f;

    [Tooltip("Seconds newborns from the same wave phase through each other. Reuses " +
             "the Splitter's SiblingPhase, for the same degenerate-contact reason.")]
    [SerializeField] private float siblingPhaseDuration = 0.5f;

    [Header("Placement")]
    [Tooltip("If true, on Start the vortex teleports itself to a random point on a " +
             "ring around the core. Turn off to place it by hand in the scene.")]
    [SerializeField] private bool placeRelativeToCore = true;

    [SerializeField] private float minDistanceFromCore = 12f;
    [SerializeField] private float maxDistanceFromCore = 18f;

    [Tooltip("OPTIONAL. If set, only colliders on these layers are considered when " +
             "checking a spawn spot. Leave it EMPTY (Nothing) and the vortex scans " +
             "ALL solid colliders instead — biome props, layout obstacles, towers — " +
             "skipping triggers, the player, and other enemies automatically. Empty " +
             "is the recommended setting; the mask is just a fast pre-filter if you " +
             "have a dedicated obstacle layer.")]
    [SerializeField] private LayerMask placementBlockers;

    [Tooltip("Required clearance around a spawn spot. Should be a bit LARGER than " +
             "the disk (diskRadius ~2.2) so obstacles don't clip the visible edge, " +
             "not just the collider.")]
    [SerializeField] private float clearRadius = 2.8f;

    [Tooltip("How many random points to try before giving up and using the last one.")]
    [Min(1)][SerializeField] private int placementAttempts = 24;

    [Header("Feel")]
    [Tooltip("Disk brightness tracks remaining health, so a wounded vortex visibly " +
             "guTters before it dies.")]
    [SerializeField] private bool intensityTracksHealth = true;

    [SerializeField] private float spawnCameraShake = 0.05f;

    [Header("Spit Animation")]
    [Tooltip("Inflate/deflate the disk to 'spit' each enemy out: it winds down " +
             "(anticipation), punches outward as the enemy launches, then settles.")]
    [SerializeField] private bool spitOnSpawn = true;

    [Tooltip("Duration of one spit pulse. The enemy is born ~55% of the way " +
             "through, at the outward punch.")]
    [SerializeField] private float spitDuration = 0.5f;

    [Tooltip("How far the disk swells past its resting radius at the punch, as a " +
             "fraction. 0.35 = +35%. Bigger = a more violent heave.")]
    [SerializeField] private float spitAmount = 0.35f;

    [Header("Path Indicator")]
    [Tooltip("Auto-create a light-red footprint trail from the player to this vortex " +
             "(the same mechanic the Gremlin and chest trails use). ONE shared " +
             "indicator is created for the whole scene no matter how many vortices " +
             "exist — it always trails to the nearest one — so leaving this on for " +
             "every vortex is safe and won't stack duplicate trails.")]
    [SerializeField] private bool showPathToVortex = true;

    [Tooltip("World-unit gap between footprints.")]
    [SerializeField] private float pathFootprintSpacing = 1.3f;

    [Tooltip("Beyond this distance the trail isn't drawn.")]
    [SerializeField] private float pathMaxDistance = 80f;

    [Tooltip("Footprint tint. A bright hot red reads as the vortex's trail.")]
    [SerializeField] private Color pathFootprintTint = new Color(1f, 0.30f, 0.28f, 1f);

    [Tooltip("Footprint size. 0.9 is clearly visible; the gremlin uses ~0.4.")]
    [SerializeField] private float pathFootprintScale = 0.9f;

    // ---- runtime ----
    private VortexStats stats;
    private VortexVisual visual;
    private Transform coreTransform;

    // One indicator for the whole scene, shared across every vortex
    private static VortexPathIndicator _sharedIndicator;

    private readonly List<GameObject> alive = new List<GameObject>();
    private int totalSpawned;
    private float spawnTimer;

    private void Awake()
    {
        stats = GetComponent<VortexStats>();
        visual = GetComponent<VortexVisual>();
        // VortexStats.Awake already calls ConfigureDeathVfx(0) and owns the collapse.
        // NOTE: the disk is pure procedural mesh with NO SpriteRenderer, so a
        // YSortEntity has nothing of its own to drive and can't lift the mesh above
        // the baked grass on its own — that was the old "behind the grass" bug.
        // VortexVisual now Y-sorts its mesh layers directly against the grass (base
        // 1000 + round(-y*10)) with an upward bias, so the disk draws ABOVE the field
        // regardless. YSortEntity is still added so any OTHER system that scans for it
        // (targeting, minimap, etc.) finds the vortex like every other enemy.
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
            ysort.sortYOffset = -0.2f;
        }
    }

    private void Start()
    {
        if (placeRelativeToCore) PlaceOnRing();
        spawnTimer = initialDelay;

        if (showPathToVortex) EnsureSharedPathIndicator();
    }

    // Lazily create ONE indicator for the whole scene. Guarded three ways so
    // multiple vortices, scene reloads, and domain reloads can't leave a stale or
    // duplicate trail: the static ref, a live-null check, and a name lookup.
    private void EnsureSharedPathIndicator()
    {
        if (_sharedIndicator != null) return;

        // Survives a domain reload where the static reset but the object persisted.
        var existing = FindFirstObjectByType<VortexPathIndicator>();
        if (existing != null) { _sharedIndicator = existing; return; }

        var go = new GameObject("VortexPathIndicator");
        var ind = go.AddComponent<VortexPathIndicator>();
        ind.footprintSpacing = pathFootprintSpacing;
        ind.maxPathDistance = pathMaxDistance;
        ind.footprintTint = pathFootprintTint;
        ind.footprintScale = pathFootprintScale;

        _sharedIndicator = ind;
    }

    // PLACEMENT

    private void PlaceOnRing()
    {
        Transform core = ResolveCore();
        Vector3 origin = core != null ? core.position : Vector3.zero;

        Vector3 chosen = transform.position;
        Vector3 firstTry = Vector3.zero;
        bool haveFirst = false;

        for (int i = 0; i < placementAttempts; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(minDistanceFromCore, maxDistanceFromCore);
            Vector3 candidate = origin + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * dist;

            if (!haveFirst) { firstTry = candidate; haveFirst = true; }

            if (IsSpotClear(candidate))
            {
                transform.position = candidate;
                return;
            }
        }

        // Every attempt was blocked. Rather than plonk down on top of an obstacle,
        // fall back to the first candidate but nudge it outward until it's clear, so
        // the vortex never ends up buried. Give up after a bounded search.
        chosen = firstTry;
        Vector2 outDir = ((Vector2)(firstTry - origin)).normalized;
        if (outDir.sqrMagnitude < 0.0001f) outDir = Vector2.right;

        for (int step = 1; step <= 12; step++)
        {
            Vector3 nudged = firstTry + (Vector3)(outDir * (clearRadius * step));
            if (IsSpotClear(nudged)) { chosen = nudged; break; }
        }

        transform.position = chosen;
    }

    // True if nothing SOLID overlaps a disk of clearRadius at 'pos'. Layer-agnostic:
    // it inspects every collider and ignores the things that aren't obstacles
    // (triggers, this vortex, players, other enemies). That way biome props and
    // layout obstacles are rejected without needing a perfectly configured
    // LayerMask — which was why they got spawned on before (the mask was empty).
    private bool IsSpotClear(Vector3 pos)
    {
        // If a specific mask IS configured, honour it as a fast pre-filter; if it's
        // empty (the default), scan everything.
        int mask = placementBlockers.value != 0 ? placementBlockers.value : ~0;

        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, clearRadius, mask);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (c == null) continue;
            if (c.isTrigger) continue;                       // pickups, zones, etc.
            if (c.transform.IsChildOf(transform)) continue;  // our own disk/particles
            if (c.gameObject == gameObject) continue;

            // Don't treat the player or other enemies as terrain.
            if (c.CompareTag("Player")) continue;
            if (c.GetComponentInParent<EnemyStats>() != null) continue;

            // Anything else with a solid collider here is terrain we must avoid.
            return false;
        }
        return true;
    }

    private Transform ResolveCore()
    {
        if (coreTransform != null) return coreTransform;
        GameObject core = GameObject.FindGameObjectWithTag("Core");
        coreTransform = core != null ? core.transform : null;
        return coreTransform;
    }

    // The point the wave should be aimed at: nearest live player if one exists (the
    // enemies' most likely first target), otherwise the core. Falling back to the
    // vortex's own position just yields a random arc, which is still fine.
    private Vector3 ResolveSpawnFacing()
    {
        var pr = PlayerRegistry.Instance;
        if (pr != null)
        {
            var nearest = pr.NearestAlive(transform.position, includeCloaked: true);
            if (nearest != null) return nearest.transform.position;
        }

        Transform core = ResolveCore();
        if (core != null) return core.position;

        return transform.position;
    }

    // CADENCE

    private void Update()
    {
        if (stats == null || stats.IsDead()) return;   // VortexStats.Die() collapsed us

        PruneAlive();

        if (intensityTracksHealth && visual != null && stats.maxHealth > 0f)
        {
            // maxHealth / currentHealth are public fields on CharacterStats.
            // Never fully dark: 0.35 floor keeps a dying vortex legible.
            float hp = Mathf.Clamp01(stats.currentHealth / stats.maxHealth);
            visual.SetIntensity(Mathf.Lerp(0.35f, 1f, hp));
        }

        if (!CanSpawnWave()) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            spawnTimer = spawnInterval;
            StartCoroutine(SpawnWave());
        }
    }

    private bool CanSpawnWave()
    {
        if (spawnTable == null || spawnTable.Count == 0) return false;
        if (lifetimeSpawnBudget > 0 && totalSpawned >= lifetimeSpawnBudget) return false;
        if (maxAliveFromThisVortex > 0 && alive.Count >= maxAliveFromThisVortex) return false;
        return true;
    }

    private void PruneAlive()
    {
        for (int i = alive.Count - 1; i >= 0; i--)
            if (alive[i] == null) alive.RemoveAt(i);
    }

    // SPAWNING

    private IEnumerator SpawnWave()
    {
        if (spawnCameraShake > 0f && CameraShake.Instance != null)
            CameraShake.Instance.Shake(spawnCameraShake, 0.15f);

        var born = new List<Collider2D>(enemiesPerWave);

        // Aim the spawn arc at the target (core / nearest player). Enemies emerge on
        // the target-FACING side of the disk, so none is born behind the vortex where
        // it would have to path around the big collider to reach the core.
        Vector3 aimAt = ResolveSpawnFacing();
        Vector2 toTarget = (Vector2)(aimAt - transform.position);
        float centerAngle = toTarget.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(toTarget.y, toTarget.x)
            : Random.Range(0f, Mathf.PI * 2f);

        for (int i = 0; i < enemiesPerWave; i++)
        {
            if (stats == null || stats.IsDead()) break;
            if (maxAliveFromThisVortex > 0 && alive.Count >= maxAliveFromThisVortex) break;
            if (lifetimeSpawnBudget > 0 && totalSpawned >= lifetimeSpawnBudget) break;

            VortexSpawnEntry entry = PickEntry();
            if (entry == null || entry.prefab == null) break;

            // Fan the wave across an arc centred on the target direction. One enemy
            // sits dead-centre; the rest spread symmetrically to either side, never
            // wrapping behind the disk.
            float spread = 0f;
            if (enemiesPerWave > 1)
            {
                float half = spawnArcDegrees * 0.5f * Mathf.Deg2Rad;
                float frac = i / (float)(enemiesPerWave - 1);   // 0..1
                spread = Mathf.Lerp(-half, half, frac);
            }
            float ang = centerAngle + spread + Random.Range(-0.12f, 0.12f);

            // Wind the disk down and punch it out — anticipation before the enemy
            // appears, so it reads as the vortex heaving to expel it.
            float peak = 0f;
            if (visual != null && spitOnSpawn)
            {
                visual.Spit(spitDuration, spitAmount);
                peak = visual.SpitPeakTime;
            }
            else if (visual != null)
            {
                visual.Flare(1f);
            }

            // Wait for the disk to reach its outward punch, THEN birth the enemy so
            // it rides the expansion outward.
            if (peak > 0f) yield return new WaitForSeconds(peak);

            GameObject go = BirthEnemy(entry, ang);
            if (go != null)
            {
                // Sound the expulsion, synced to the actual emergence (we've already
                // waited out the disk's outward punch above). One shot per enemy.
                PlaySpawnSound();

                var c = go.GetComponent<Collider2D>();
                if (c != null) born.Add(c);
            }

            // The rest of the intra-wave gap (we already spent 'peak' of it waiting).
            float remaining = intraWaveDelay - peak;
            if (remaining > 0f) yield return new WaitForSeconds(remaining);
        }

        // Same degenerate-contact problem the Splitter's children have: two circles
        // spawned close together produce a normal Box2D can't resolve.
        for (int a = 0; a < born.Count; a++)
            for (int b = a + 1; b < born.Count; b++)
                SiblingPhase.Begin(born[a], born[b], siblingPhaseDuration);
    }

    private void PlaySpawnSound()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (FMODEvents.instance.vortexSpawn.IsNull) return;
        AudioManager.instance.PlayOneShot(FMODEvents.instance.vortexSpawn, transform.position);
    }

    // Weighted pick, respecting each row's per-prefab live cap.
    private VortexSpawnEntry PickEntry()
    {
        int total = 0;
        for (int i = 0; i < spawnTable.Count; i++)
        {
            var e = spawnTable[i];
            if (e == null || e.prefab == null || e.weight <= 0) continue;
            if (e.maxSimultaneous > 0 && CountAliveOf(e.prefab) >= e.maxSimultaneous) continue;
            total += e.weight;
        }
        if (total <= 0) return null;

        int roll = Random.Range(0, total);
        for (int i = 0; i < spawnTable.Count; i++)
        {
            var e = spawnTable[i];
            if (e == null || e.prefab == null || e.weight <= 0) continue;
            if (e.maxSimultaneous > 0 && CountAliveOf(e.prefab) >= e.maxSimultaneous) continue;

            roll -= e.weight;
            if (roll < 0) return e;
        }
        return null;
    }

    // Instantiate(prefab) names the clone "Prefab(Clone)", so a prefix match is a
    // reliable identity test without storing a component on every spawn.
    private int CountAliveOf(GameObject prefab)
    {
        int n = 0;
        for (int i = 0; i < alive.Count; i++)
            if (alive[i] != null && alive[i].name.StartsWith(prefab.name))
                n++;
        return n;
    }

    private GameObject BirthEnemy(VortexSpawnEntry entry, float angleRad)
    {
        GameObject prefab = entry.prefab;

        Vector3 pos = visual != null
            ? visual.DiskPoint(angleRad, birthRadiusScale)
            : transform.position;

        // The prefab's own scale is what it looks like when you drop it in the scene
        // by hand. We keep that EXACTLY, times an optional multiplier for 'child'
        // prefabs (SmallSlime ships at 0.25 because a Splitter normally resizes it).
        Vector3 finalScale = prefab.transform.localScale * Mathf.Max(0.01f, entry.spawnScaleMultiplier);

        GameObject go = Instantiate(prefab, pos, Quaternion.identity);

        // Set scale AFTER Instantiate and never touch it again. No coroutine animates
        // it, so nothing can leave the enemy frozen at a fraction of its size — which
        // was the whole "extremely small enemies" bug. If you want a pop-in, we can
        // drive it on a HARMLESS child transform later, never the root the gameplay
        // scripts read.
        go.transform.localScale = finalScale;

        alive.Add(go);
        totalSpawned++;

        Vector2 outward = ((Vector2)(pos - transform.position)).normalized;
        if (outward.sqrMagnitude < 0.0001f) outward = Random.insideUnitCircle.normalized;

        StartCoroutine(EmergePush(go, outward, finalScale));
        return go;
    }

    // Brief scale pop that is GUARANTEED to end at finalScale, plus the outward
    // shove. Kept deliberately simple: it writes finalScale on every exit path,
    // including early-out, so an interrupted pop can never shrink the enemy.
    private IEnumerator EmergePush(GameObject go, Vector2 outward, Vector3 finalScale)
    {
        if (go == null) yield break;

        var controller = go.GetComponent<EnemyController>();

        // Optional pop — purely cosmetic, and self-correcting.
        if (emergeDuration > 0f)
        {
            float e = 0f;
            while (e < emergeDuration)
            {
                if (go == null) yield break;   // destroyed mid-pop: nothing to fix
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / emergeDuration);
                // Grow from 60% → 100% with a tiny overshoot. Never starts at 0, so
                // even one frame of this looks fine, and it can't read as "tiny".
                float s = Mathf.Lerp(0.6f, 1f, Mathf.Sin(k * Mathf.PI * 0.5f)) * (1f + 0.08f * (1f - k));
                go.transform.localScale = finalScale * s;
                yield return null;
            }
        }

        // Authoritative final size. Always runs (unless the object is gone).
        if (go == null) yield break;
        go.transform.localScale = finalScale;

        // One frame so EnemyController.Start has cached its Rigidbody2D —
        // ApplyKnockback silently no-ops on a null rb.
        yield return null;
        if (go == null) yield break;

        if (controller != null && birthPushSpeed > 0f)
            controller.ApplyKnockback(outward, birthPushSpeed, birthPushDuration);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // The placement ring, drawn around the core.
        Transform core = coreTransform;
        if (core == null)
        {
            var found = GameObject.FindGameObjectWithTag("Core");
            if (found != null) core = found.transform;
        }

        if (placeRelativeToCore && core != null)
        {
            Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.35f);
            Gizmos.DrawWireSphere(core.position, minDistanceFromCore);
            Gizmos.DrawWireSphere(core.position, maxDistanceFromCore);
        }

        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, clearRadius);
    }
#endif
}

