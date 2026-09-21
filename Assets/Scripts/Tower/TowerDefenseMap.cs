using System.Collections.Generic;
using UnityEngine;

public class TowerDefenseMap : MonoBehaviour
{
    [Header("Central Core Sprites")]
    [Tooltip("Central Core animation frames IN ORDER (00 … 23). Handed to the Core, " +
             "which is created at runtime and therefore has no prefab of its own.\n\n" +
             "Do NOT include the old 'central_core_sprite' sheet — that is the 2048x2048 " +
             "/ 32 MB asset the Core's loader has to filter out at runtime.")]
    public Sprite[] coreSpriteFrames;

    [Header("Map Configuration")]
    public float mapRadius = 10f;

    //  Enemy clearance contract 
    [Header("Enemy Clearance (must match the layout spacing contract)")]
    [Tooltip("Radius of the largest enemy that has to reach the core — usually a boss, " +
             "not a basic enemy. Read it off the prefab's non-trigger collider.")]
    public float enemyClearanceRadius = 0.75f;

    [Tooltip("How much space a built tower occupies. Runtime placement keeps a full " +
             "lane clear of this, so a tower next to an obstacle never pinches.")]
    public float towerFootprintRadius = 0.75f;

    [Tooltip("Minimum clear width an enemy needs to walk through. Should be at least " +
             "2 x enemyClearanceRadius, with a little margin so they don't scrape both sides.")]
    public float laneWidth = 2.0f;

    /// Clearance a runtime obstacle must keep from a TOWER SLOT: a tower may be
    /// built there later, and an enemy still has to get past it.
    public float SlotClearance => towerFootprintRadius + laneWidth;
    public GameObject backgroundGameObject; // Manual background GameObject reference

    [Tooltip("World-radius the VISIBLE ground texture must cover. Decoupled from mapRadius " +
             "(which is the small playable/collider radius). Keep this >= BiomeManager's " +
             "groundCoverageRadius so the ground fills the whole view instead of a small " +
             "central square. Auto-raised to match BiomeManager at runtime.")]
    public float backgroundCoverageRadius = 80f;
    //public string backgroundImagePath = "Backgrounds/Background3"; // Fallback for generated terrain
    public string backgroundImagePath = "Backgrounds/Background8"; // Fallback for generated terrain    
    public bool useBackgroundImage = true;
    public Material terrainMaterial;
    public Color terrainColor = Color.green;

    [Header("Tower Slot Configuration")]
    public GameObject towerSlotPrefab;
    public List<RingConfiguration> rings = new List<RingConfiguration>();
    public int maxTotalRings = 8; // Limits total rings including augment-added ones

    [Header("Central Core Configuration")]
    public bool enableCentralCore = true;
    public float coreSize = 2f;
    [Tooltip("OVERRIDE ONLY. 0 = use EnergyManager.coreMaxEnergy, which is the single " +
             "source of truth for the core's pool. Set above 0 only if THIS map needs a " +
             "core different from the global one. (Was a duplicate that fought with " +
             "EnergyManager and always lost, because registration runs last.)")]
    public float coreMaxEnergy = 0f;

    [Tooltip("OVERRIDE ONLY. 0 = the core starts at full. Set above 0 to have a fresh " +
             "run's core come online below its max.")]
    public float coreStartingEnergy = 0f;

    /// The core pool this map should use: its own override if one is set, else the
    /// global value on EnergyManager. Single place to ask, so the two declarations
    /// can never silently disagree again.
    public float ResolvedCoreMaxEnergy =>
        coreMaxEnergy > 0f ? coreMaxEnergy
        : (EnergyManager.Instance != null ? EnergyManager.Instance.coreMaxEnergy : 100f);

    // -- Core augment carry-over ---------------------------------------------
    // Shield Matrix (73), Repair Systems (74) and Energy Siphon are components that
    // AugmentRegistry attaches to the CORE GameObject -- which ClearExistingMap
    // destroys at every stage transition. Towers survive this because Tower.Start
    // calls ApplyGlobalAugments(); the core had no equivalent, so from stage 2
    // onward those three augments quietly did nothing for the rest of the run.
    //
    // Re-attaching components with their captured values (rather than replaying the
    // augments through AugmentRegistry) is deliberate: the registry's core branches
    // STACK onto an existing component, so a replay would double them.
    private struct CoreAugmentCarry
    {
        public bool hasSiphon; public float siphonPercentage;
        public bool hasRepair; public float regenerationRate; public float activationDelay;
        public bool hasShield; public float maxShieldStrength;
    }
    [System.NonSerialized] private CoreAugmentCarry carriedCoreAugments;
    [System.NonSerialized] private bool carryCoreAugments = false;

    private void CaptureCoreAugments(CentralCore core)
    {
        carriedCoreAugments = default;
        if (core == null) return;

        var siphon = core.GetComponent<CoreEnergySiphonEffect>();
        if (siphon != null)
        {
            carriedCoreAugments.hasSiphon = true;
            carriedCoreAugments.siphonPercentage = siphon.siphonPercentage;
        }

        var repair = core.GetComponent<CoreRepairSystems>();
        if (repair != null)
        {
            carriedCoreAugments.hasRepair = true;
            carriedCoreAugments.regenerationRate = repair.regenerationRate;
            carriedCoreAugments.activationDelay = repair.activationDelay;
        }

        var shield = core.GetComponent<CoreShieldMatrix>();
        if (shield != null)
        {
            carriedCoreAugments.hasShield = true;
            carriedCoreAugments.maxShieldStrength = shield.maxShieldStrength;
        }

        carryCoreAugments = carriedCoreAugments.hasSiphon
                         || carriedCoreAugments.hasRepair
                         || carriedCoreAugments.hasShield;
    }

    private void RestoreCoreAugments(CentralCore core)
    {
        if (!carryCoreAugments || core == null) return;
        carryCoreAugments = false;

        if (carriedCoreAugments.hasSiphon && core.GetComponent<CoreEnergySiphonEffect>() == null)
        {
            var c = core.gameObject.AddComponent<CoreEnergySiphonEffect>();
            c.siphonPercentage = carriedCoreAugments.siphonPercentage;
        }

        if (carriedCoreAugments.hasRepair && core.GetComponent<CoreRepairSystems>() == null)
        {
            var c = core.gameObject.AddComponent<CoreRepairSystems>();
            c.regenerationRate = carriedCoreAugments.regenerationRate;
            c.activationDelay = carriedCoreAugments.activationDelay;
        }

        if (carriedCoreAugments.hasShield && core.GetComponent<CoreShieldMatrix>() == null)
        {
            var c = core.gameObject.AddComponent<CoreShieldMatrix>();
            // Design choice: the shield comes back FULL each stage. To carry the
            // depleted value instead, store currentShieldStrength in
            // CaptureCoreAugments and assign it here.
            c.maxShieldStrength = carriedCoreAugments.maxShieldStrength;
            c.currentShieldStrength = carriedCoreAugments.maxShieldStrength;
        }
    }

    [Header("Layout Override")]
    [Tooltip("Set by the orchestrator each stage. When non-null, this layout's slot\n" +
             "positions override the rings list above. Leave null to use the rings.")]
    public MapLayoutDefinition activeLayout;

    [Header("Test Layout (Editor Only)")]
    [Tooltip("Quick way to test a built-in layout WITHOUT creating any assets.\n" +
             "Type a name like 'Stonehenge', 'Twin Moons', 'Mushroom Grove', etc.\n" +
             "Then right-click this component header → 'Generate Map'.\n" +
             "Leave empty to use 'Active Layout' (set by the orchestrator) instead.\n" +
             "\n" +
             "Available built-ins:\n" +
             "  Concentric Classic, Chokepoint Corridor, Spiral Siege,\n" +
             "  Breached Fortress, Crossroads, The Gauntlet, The Arena,\n" +
             "  Ghost Town, Maze Hallways, Diamond Formation, Pincer Grip,\n" +
             "  Stonehenge, Crossroads Pillars,\n" +
             "  Asteroid Belt, Pinwheel,\n" +
             "  Broken Crown, The Ford, Crescent Bastion.")]
    public string testLayoutName = "";

    [Tooltip("If true and Test Layout Name is set, the named built-in layout\n" +
             "is applied automatically on Start, overriding Active Layout.\n" +
             "Turn this OFF in production builds.")]
    public bool useTestLayoutOnStart = true;

    [Tooltip("Runtime multiplier applied to all layout positions, ring radii, " +
             "obstacle positions/sizes, and connection-line points when a layout " +
             "is applied.\n" +
             "1.0 = layouts are used as-authored (recommended — built-in layouts " +
             "are already pre-spaced).\n" +
             "Values >1 push slots further apart and scale obstacles proportionally.\n" +
             "Slot SIZES are NOT scaled — towers stay the same size.\n" +
             "mapRadius is auto-scaled to match so outer slots don't hit the border.")]
    [Min(0.1f)]
    public float layoutSpreadScale = 1.0f;

    [Tooltip("Physics layer name for layout obstacles (walls, buildings).\n" +
             "Must match the LayerMask 'obstacleLayer' on enemy prefabs so they avoid them.")]
    public string obstacleLayerName = "Obstacle";

    [Tooltip("Maximum size of any single collider segment. Long obstacles are\n" +
             "broken into multiple colliders of this size so enemies can avoid them.\n" +
             "Smaller = better navigation, more colliders. 1.2 is a good default.")]
    public float maxColliderSegmentSize = 1.2f;

    [Header("Visual Settings")]
    public bool showDebugCircles = true;
    public Color debugCircleColor = Color.white;
    public float debugCircleWidth = 0.02f;

    [System.NonSerialized]
    private List<TowerSlot> allTowerSlots = new List<TowerSlot>();
    private int bonusSlotsAdded = 0; // tracks how many bonus slots have been revealed
    private GameObject terrainObject;
    private GameObject slotsContainer;
    private GameObject obstaclesContainer; // layout-specific obstacles (walls, buildings)
    private GameObject augmentArchContainer; // arches added by the "obstacle generation" augment (ID 3)
    private int augmentArchWaves = 0; // how many times the arch augment has run on the current map
    private CentralCore centralCore;

    // Captured on first GenerateMap so we can rescale mapRadius from the
    // original (un-scaled) value whenever layoutSpreadScale changes.
    [System.NonSerialized]
    private float baseMapRadius = -1f;
    [System.NonSerialized]
    private bool baseMapRadiusCaptured = false;

    // Track the SOURCE layout asset (not the scaled clone) so we can detect
    // "same layout asked for again" and skip the rebuild — preserving towers
    // and slots between stages. Without this, ApplyLayout's reference check
    // against `activeLayout` always misses when scale != 1.0 because we
    // create a fresh clone each call.
    [System.NonSerialized]
    private MapLayoutDefinition sourceLayout;
    [System.NonSerialized]
    private float lastAppliedSpreadScale = 1f;
    [System.NonSerialized]
    private bool sourceLayoutCaptured = false;

    // ── FIX: core energy must survive a map rebuild ───────────────────────────
    // ClearExistingMap() destroys the CentralCore and CreateCentralCore() makes a
    // fresh one at coreStartingEnergy. Every stage that changes layout therefore
    // silently full-healed the core, and on RESUME the saved core energy restored by
    // RunPersistence was wiped by the very next ApplyLayout.
    // We now carry the live values across the rebuild, and expose SeedCoreEnergy()
    // so a resume can pre-seed them BEFORE the map is ever built.
    [System.NonSerialized] private bool carryCoreEnergy = false;
    [System.NonSerialized] private float carriedCoreEnergy;
    [System.NonSerialized] private float carriedCoreMaxEnergy;

    [Tooltip("Keep the Central Core's CURRENT energy when the map is rebuilt for a new " +
             "stage/layout. OFF reproduces the old behaviour (core resets to " +
             "'Core Starting Energy' on every layout change), which made difficulty depend " +
             "on whether Change Layout Per Stage happened to be on.")]
    public bool preserveCoreEnergyAcrossRebuild = true;

    // ── FIX: bonus slots must survive a map rebuild ───────────────────────────
    // CreateTowerSlots() reset bonusSlotsAdded to 0 and the slots themselves were
    // destroyed with slotsContainer, so the "additional_tower_slots" augment was a
    // one-stage effect, and towers saved into a bonus slot (ringIndex 99) could never
    // be restored because FindSlot(99, n) returned null.
    [System.NonSerialized] private int carriedBonusSlots = 0;

    [Tooltip("Re-create augment-revealed bonus slots after a map rebuild. OFF reproduces " +
             "the old behaviour, where the 'additional_tower_slots' augment was lost at the " +
             "next stage and saved towers in those slots could not be restored.")]
    public bool preserveBonusSlotsAcrossRebuild = true;

    /// Pre-seed the Central Core's energy for the NEXT build of the map. Called by
    /// GameOrchestrator/RunPersistence on resume, before the stage layout is applied,
    /// so the core comes out of CreateCentralCore() already holding the saved values.
    public void SeedCoreEnergy(float current, float max)
    {
        carryCoreEnergy = true;
        carriedCoreMaxEnergy = max > 0f ? max : ResolvedCoreMaxEnergy;
        carriedCoreEnergy = Mathf.Clamp(current, 0f, carriedCoreMaxEnergy);

        // If the core already exists (no rebuild pending) apply it immediately too.
        // SeedEnergyState rather than SetMaxEnergy/SetEnergy: a core that has not yet
        // registered with EnergyManager would otherwise have these values stamped
        // over with a full pool the moment registration lands.
        if (centralCore != null)
        {
            centralCore.SeedEnergyState(carriedCoreEnergy, carriedCoreMaxEnergy);
        }
    }

    /// Runtime-safe destroy. DestroyImmediate is an EDITOR call: using it in play mode
    /// (this map rebuilds mid-run) can tear objects down inside a physics callback and
    /// corrupt collider state. Route every teardown through here instead.
    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    /// Destroy a GameObject that something in the SAME FRAME might otherwise find again.
    ///
    /// THIS EXISTS BECAUSE OF A REAL BUG. ClearExistingMap used to call
    /// DestroyImmediate, so a torn-down object was gone before CreateTerrain ran.
    /// Switching to the runtime-correct Destroy() defers teardown to END OF FRAME, and
    /// CreateTerrain re-adopts the shared background BY NAME:
    ///     var shared = GameObject.Find("Background");
    /// so it could hand back the very object that was already doomed. backgroundGameObject
    /// then held a destroyed reference forever, every later rebuild took the
    /// "background already assigned" branch on a dead object, and the map rendered as a
    /// flat uniform fill with no background for the rest of the session.
    ///
    /// Deactivating and renaming first makes the corpse un-findable: GameObject.Find only
    /// returns ACTIVE objects, and the name no longer matches either.
    private static void RetireAndDestroy(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying)
        {
            go.name = "~doomed_" + go.name;
            go.SetActive(false);
            go.transform.SetParent(null, true);
            Destroy(go);
        }
        else DestroyImmediate(go);
    }

    [System.Serializable]
    public class RingConfiguration
    {
        public float radius = 5f;
        public int slotCount = 8;
        public float slotSize = 1f;
        public float rotationOffset = 0f; // Degrees
        public bool enabled = true;
    }

    void Start()
    {
        // Add default rings if none are configured
        if (rings.Count == 0)
        {
            rings.Add(new RingConfiguration { radius = 2.3f, slotCount = 6, slotSize = 1.9f });
            rings.Add(new RingConfiguration { radius = 3.8f, slotCount = 6, slotSize = 1.9f });
        }

        // A GameOrchestrator drives the layout per stage via ApplyLayout(). Building
        // the map here too causes a redundant, race-prone double generation at launch,
        // so stand down (the orchestrator builds it during the stage intro). The editor
        // test-layout path below is exempt so standalone testing still works.
        if (GameOrchestrator.Instance != null && !useTestLayoutOnStart)
            return;

        // Editor-only quick override: if testLayoutName is set, build that
        // layout from MapLayoutExamples directly. No assets needed.
        if (useTestLayoutOnStart && !string.IsNullOrWhiteSpace(testLayoutName))
        {
            var testLayout = MapLayoutExamplesLookup.FindByName(testLayoutName);
            if (testLayout != null)
            {
                Debug.Log($"[TowerDefenseMap] Using TEST layout '{testLayout.layoutName}'.");
                ApplyLayout(testLayout);
                return; // ApplyLayout already calls GenerateMap
            }
            else
            {
                Debug.LogWarning($"[TowerDefenseMap] testLayoutName='{testLayoutName}' " +
                                 "didn't match any built-in layout. Falling back to default.");
            }
        }

        GenerateMap();
    }

    // Editor helper — right-click the component header in the Inspector,
    // pick "Generate Test Layout", and the map rebuilds using whatever name
    // is currently typed into 'Test Layout Name'.
    [ContextMenu("Generate Test Layout")]
    public void GenerateTestLayout()
    {
        if (string.IsNullOrWhiteSpace(testLayoutName))
        {
            Debug.LogWarning("[TowerDefenseMap] Test Layout Name is empty. " +
                             "Type a layout name (e.g. 'Stonehenge') first.");
            return;
        }
        var layout = MapLayoutExamplesLookup.FindByName(testLayoutName);
        if (layout == null)
        {
            Debug.LogWarning($"[TowerDefenseMap] No built-in layout named '{testLayoutName}'. " +
                             "Check the spelling against the list in the tooltip.");
            return;
        }
        Debug.Log($"[TowerDefenseMap] Applying test layout '{layout.layoutName}'.");
        ApplyLayout(layout);
    }

    // One-click shortcuts for the new curvy layouts. Right-click the component
    // header in the inspector and pick one — the map rebuilds immediately.
    // These bypass testLayoutName entirely (avoids inspector-serialization
    // assertions that fire when mutating serialized fields mid-draw).
    [ContextMenu("Test: Stonehenge")] void _TestStonehenge() { ApplyTestLayoutByName("Stonehenge"); }
    [ContextMenu("Test: Crossroads Pillars")] void _TestCrossroadsPillars() { ApplyTestLayoutByName("Crossroads Pillars"); }
    [ContextMenu("Test: Asteroid Belt")] void _TestAsteroidBelt() { ApplyTestLayoutByName("Asteroid Belt"); }
    [ContextMenu("Test: Pinwheel")] void _TestPinwheel() { ApplyTestLayoutByName("Pinwheel"); }

    // Asymmetric layouts — the core is exposed from one flank.
    [ContextMenu("Test: Broken Crown")] void _TestBrokenCrown() { ApplyTestLayoutByName("Broken Crown"); }
    [ContextMenu("Test: The Ford")] void _TestTheFord() { ApplyTestLayoutByName("The Ford"); }
    [ContextMenu("Test: Crescent Bastion")] void _TestCrescentBastion() { ApplyTestLayoutByName("Crescent Bastion"); }

    // Helper used by the [ContextMenu("Test: …")] shortcuts.
    void ApplyTestLayoutByName(string name)
    {
        var layout = MapLayoutExamplesLookup.FindByName(name);
        if (layout == null)
        {
            Debug.LogWarning($"[TowerDefenseMap] No built-in layout named '{name}'.");
            return;
        }
        Debug.Log($"[TowerDefenseMap] Applying test layout '{layout.layoutName}'.");
        ApplyLayout(layout);
    }

    [ContextMenu("Generate Map")]
    public void GenerateMap()
    {
        ClearExistingMap();
        FitMapRadiusToLayout();
        CreateTerrain();
        CreateCentralCore();
        CreateLayoutObstacles();
        CreateTowerSlots();
        if (showDebugCircles)
        {
            DrawDebugCircles();
        }

        // Biome decorations (trees/rocks) are placed relative to this layout's
        // footprint, so they must be rebuilt whenever the layout changes —
        // otherwise props spawned for the previous layout sit on top of the new
        // walls. No-op until ObstacleGenerator has generated at least once, so
        // this can't double-spawn during first-frame startup ordering.
        var obstacleGen = FindFirstObjectByType<ObstacleGenerator>();
        if (obstacleGen != null) obstacleGen.NotifyLayoutChanged();

        // The player prefab is seated at scene start, but the layout is chosen and
        // built HERE, during the stage intro — so a blocking obstacle can land
        // directly on top of a player who is already standing there, and a dynamic
        // body that starts fully inside a static collider wedges. Same story for a
        // mid-run rebuild (per-stage layout change, extra-ring augment).
        //
        // Only players actually INSIDE solid geometry are moved; standing next to a
        // wall is untouched, so this is a no-op in every normal case.
        if (Application.isPlaying) PlayerSpawnSafety.EvacuateAllPlayers();
    }

    /// Every non-trigger collider that belongs to the CURRENT layout obstacles or
    /// the augment arches — i.e. the solid geometry a player can get wedged in.
    ///
    /// Read from the container fields rather than by physics layer, deliberately:
    ///   * CreateLayoutObstacles falls back to layer 0 (Default) when the project
    ///     has no "Obstacle" layer, and a mask query on Default would sweep up half
    ///     the scene.
    ///   * ClearExistingMap destroys the previous container with Destroy(), which
    ///     Unity defers to end of frame. A layer query run during a rebuild would
    ///     still see the OLD walls; these fields already point at the new ones.
    public void CollectBlockingObstacleColliders(List<Collider2D> results)
    {
        if (results == null) return;
        results.Clear();

        AppendColliders(obstaclesContainer, results);
        AppendColliders(augmentArchContainer, results);
    }

    private static void AppendColliders(GameObject container, List<Collider2D> results)
    {
        if (container == null) return;

        var found = container.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < found.Length; i++)
        {
            var c = found[i];
            if (c == null || c.isTrigger) continue;   // decorative-only obstacles have no collider at all
            results.Add(c);
        }
    }

    /// Grows mapRadius so it actually covers the layout that's about to be built.
    ///
    /// The layouts reach further out than the authored default (outer bonus rings sit
    /// around 14.6 while mapRadius ships at 10). Everything that clamps against
    /// mapRadius — the terrain disc, the boundary collider, the debug circles, and the
    /// arch augment's placement ring — was therefore working from a radius smaller than
    /// the map it described. The arch augment is the visible casualty: it clamps its
    /// ring to mapRadius - 1.4, which drops arches INSIDE the layout's obstacle band
    /// instead of outside the outer slots, where it intends to put them.
    ///
    /// Only ever grows, never shrinks below the authored value, and recomputes from
    /// scratch each build so repeated calls can't compound.
    void FitMapRadiusToLayout()
    {
        if (!baseMapRadiusCaptured)
        {
            baseMapRadius = mapRadius;
            baseMapRadiusCaptured = true;
        }

        float scale = Mathf.Approximately(layoutSpreadScale, 0f) ? 1f : layoutSpreadScale;
        float required = baseMapRadius * scale;

        void Cover(Vector2 p, float extent)
        {
            float r = p.magnitude + extent;
            if (r > required) required = r;
        }

        if (activeLayout != null)
        {
            if (activeLayout.layoutType == MapLayoutDefinition.LayoutType.Custom &&
                activeLayout.customSlotPositions != null)
            {
                foreach (var p in activeLayout.customSlotPositions)
                    Cover(p, activeLayout.customSlotSize * 0.5f);
            }

            if (activeLayout.rings != null)
                foreach (var ring in activeLayout.rings)
                    if (ring != null && ring.enabled)
                        Cover(new Vector2(ring.radius, 0f), ring.slotSize * 0.5f);

            if (activeLayout.bonusSlotPositions != null)
                foreach (var p in activeLayout.bonusSlotPositions)
                    Cover(p, activeLayout.bonusSlotSize * 0.5f);

            if (activeLayout.obstacles != null)
                foreach (var o in activeLayout.obstacles)
                    Cover(o.position, Mathf.Max(o.size.x, o.size.y) * 0.5f);
        }
        else if (rings != null)
        {
            foreach (var ring in rings)
                if (ring != null && ring.enabled)
                    Cover(new Vector2(ring.radius, 0f), ring.slotSize * 0.5f);
        }

        // Leave a lane of walkable ground outside the outermost thing, so enemies
        // can still round the outside of the map instead of clipping the border.
        required += laneWidth;

        if (required > mapRadius + 0.01f)
        {
            Debug.Log($"[TowerDefenseMap] mapRadius {mapRadius:F1} → {required:F1} " +
                      $"to fit layout '{(activeLayout != null ? activeLayout.layoutName : "default rings")}'.");
            mapRadius = required;
        }
    }

    /// Rebuild the map (e.g. after the "additional_tower_rings" augment adds a ring)
    /// WITHOUT losing the towers the player has already built.
    ///
    /// GenerateMap() destroys slotsContainer, and every tower is parented to its slot,
    /// so a bare GenerateMap() call silently deleted the whole board. This snapshots
    /// each live tower by its stable (ringIndex, slotIndex) identity, rebuilds, and
    /// then re-places them cost-free through TowerPlacementManager. Existing ring
    /// coordinates are stable across an ADDED ring (new rings append at the end), so
    /// every tower lands back where it was.
    public void RebuildPreservingTowers()
    {
        var saved = new List<(int ring, int slot, Tower.TowerType type, int level, float energy, float maxEnergy)>();

        foreach (var s in allTowerSlots)
        {
            if (s == null || !s.IsOccupied || s.currentTower == null) continue;
            var t = s.currentTower.GetComponent<Tower>();
            if (t == null) continue;
            saved.Add((s.ringIndex, s.slotIndex, t.towerType, t.upgradeLevel, t.currentEnergy, t.maxEnergy));
        }

        GenerateMap();   // core energy + bonus slots are carried over by ClearExistingMap

        if (saved.Count == 0) return;

        var placement = TowerPlacementManager.Instance;
        if (placement == null)
        {
            Debug.LogError($"[TowerDefenseMap] Rebuilt the map but no TowerPlacementManager exists — " +
                           $"{saved.Count} tower(s) could not be restored.");
            return;
        }

        int restored = 0;
        foreach (var e in saved)
        {
            var slot = placement.FindSlot(e.ring, e.slot);
            if (slot == null || slot.IsOccupied) continue;
            if (placement.RestoreTowerInto(slot, e.type, e.level, e.energy, e.maxEnergy) != null) restored++;
        }

        if (restored != saved.Count)
            Debug.LogWarning($"[TowerDefenseMap] Rebuild preserved {restored}/{saved.Count} towers " +
                             "(a slot coordinate no longer exists in the new layout).");
    }

    void ClearExistingMap()
    {
        // FIX: remember how many bonus slots the augment had revealed so
        // CreateTowerSlots() can put them back after the rebuild.
        carriedBonusSlots = preserveBonusSlotsAcrossRebuild ? bonusSlotsAdded : 0;

        // Retire the old slots BEFORE destroying them.
        //
        // CRITICAL ORDERING NOTE. This teardown used DestroyImmediate, so every slot's
        // OnDestroy — and therefore TowerPlacementManager.UnregisterSlot — ran
        // synchronously. Now that we correctly use Destroy() at runtime, OnDestroy is
        // deferred to end of frame, which means the hub's slot list would still contain
        // these dying slots for the rest of THIS frame. Anything calling FindSlot()
        // straight after a rebuild (RebuildPreservingTowers does exactly that) would
        // match an old slot, place a tower into it, and watch that tower disappear when
        // the slot was destroyed moments later.
        //
        // So: unregister explicitly, and deactivate the container so the towers parented
        // under it fire OnDisable now and drop out of Tower.ActiveTowers immediately
        // instead of lingering as targetable ghosts for a frame.
        var hub = TowerPlacementManager.Instance;
        if (hub != null)
        {
            for (int i = 0; i < allTowerSlots.Count; i++)
                if (allTowerSlots[i] != null) hub.UnregisterSlot(allTowerSlots[i]);
        }
        if (slotsContainer != null) slotsContainer.SetActive(false);

        // Clear existing slots
        allTowerSlots.Clear();

        // Destroy existing terrain, but preserve manually assigned background
        if (terrainObject != null && terrainObject != backgroundGameObject)
        {
            // RetireAndDestroy, not SafeDestroy: CreateTerrain runs later THIS SAME FRAME
            // and looks the background up by name. See RetireAndDestroy's comment.
            RetireAndDestroy(terrainObject);
        }
        terrainObject = null;

        // If the shared background was itself destroyed by something else, drop the stale
        // reference so CreateTerrain rebuilds instead of binding to a dead object.
        if (backgroundGameObject == null) backgroundGameObject = null;

        // Destroy existing slots container
        if (slotsContainer != null)
        {
            SafeDestroy(slotsContainer);
            slotsContainer = null;
        }

        // Destroy existing obstacles container
        if (obstaclesContainer != null)
        {
            SafeDestroy(obstaclesContainer);
            obstaclesContainer = null;
        }

        // Destroy augment-added arches and reset the augment wave counter.
        // A fresh map (new stage / layout) starts with no augment arches.
        if (augmentArchContainer != null)
        {
            SafeDestroy(augmentArchContainer);
            augmentArchContainer = null;
        }
        augmentArchWaves = 0;

        // Destroy old debug rings and layout connection lines
        var toDelete = new List<GameObject>();
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Debug_Ring_") || child.name.StartsWith("LayoutLine_"))
                toDelete.Add(child.gameObject);
        }
        foreach (var go in toDelete) SafeDestroy(go);

        // Destroy existing central core.
        // FIX: capture its live energy first so the replacement core built by
        // CreateCentralCore() resumes from the same value instead of snapping back to
        // coreStartingEnergy. A core that was actually DESTROYED (game over) is not
        // carried over — that state belongs to a run that already ended.
        if (centralCore != null)
        {
            if (preserveCoreEnergyAcrossRebuild && !centralCore.IsDestroyed())
            {
                carryCoreEnergy = true;
                carriedCoreEnergy = centralCore.currentEnergy;
                carriedCoreMaxEnergy = centralCore.maxEnergy;
            }

            // Outside the preserve-energy branch on purpose: a core that was
            // DESTROYED does not carry its energy, but the run's augments are still
            // the player's and belong on whatever core comes next.
            CaptureCoreAugments(centralCore);

            centralCore.OnEnergyChanged -= OnCoreEnergyChanged;
            centralCore.OnEnergyDepleted -= OnCoreEnergyDepleted;
            SafeDestroy(centralCore.gameObject);
            centralCore = null;
        }
    }

    // The radius the ground texture should actually fill. The playable mapRadius is
    // tiny (~10); the visible ground must reach the view edges, so we use the larger
    // of our backgroundCoverageRadius and BiomeManager.groundCoverageRadius. This is
    // what stops the ground from being a small central square.
    float EffectiveBackgroundCoverage()
    {
        float cover = Mathf.Max(mapRadius + 5f, backgroundCoverageRadius);
        var bm = FindFirstObjectByType<BiomeManager>();
        if (bm != null) cover = Mathf.Max(cover, bm.groundCoverageRadius);
        return cover;
    }

    void CreateTerrain()
    {
        // If no background was assigned, REUSE the shared scene background (the one
        // BiomeManager tiles to groundCoverageRadius) instead of spawning our own
        // "Terrain" tiled only to mapRadius+5 (~15). That duplicate overlapped the
        // biome background and left a small textured square in the center of the map
        // — re-created every stage by ApplyLayout, which is why it was so persistent.
        if (backgroundGameObject == null)
        {
            var shared = GameObject.Find("Background");
            if (shared != null && shared.GetComponent<SpriteRenderer>() != null)
                backgroundGameObject = shared;
        }

        if (backgroundGameObject != null)
        {
            // Use manually assigned background GameObject
            terrainObject = backgroundGameObject;

            // Ensure proper parenting
            if (terrainObject.transform.parent != transform)
            {
                terrainObject.transform.SetParent(transform);
            }

            // Ensure SpriteRenderer exists
            var renderer = terrainObject.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = terrainObject.AddComponent<SpriteRenderer>();
            }
            renderer.sortingOrder = -1;
        }
        else
        {
            // Generate terrain procedurally
            terrainObject = new GameObject("Terrain");
            terrainObject.transform.parent = transform;
            terrainObject.transform.localPosition = Vector3.zero;

            var renderer = terrainObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = -1;

            if (useBackgroundImage && !string.IsNullOrEmpty(backgroundImagePath))
            {
                // Load background image from Resources
                Texture2D backgroundTexture = Resources.Load<Texture2D>(backgroundImagePath);
                if (backgroundTexture != null)
                {
                    Sprite backgroundSprite = Sprite.Create(
                        backgroundTexture,
                        new Rect(0, 0, backgroundTexture.width, backgroundTexture.height),
                        Vector2.one * 0.5f,
                        100f
                    );

                    renderer.sprite = backgroundSprite;
                    renderer.color = Color.white;

                    // Keep native pixel size (no scaling) — use tiling for full coverage
                    terrainObject.transform.localScale = Vector3.one;

                    // Add BackgroundTiler to cover the map area via repeating tiles
                    BackgroundTiler tiler = terrainObject.GetComponent<BackgroundTiler>();
                    if (tiler == null)
                        tiler = terrainObject.AddComponent<BackgroundTiler>();
                    tiler.autoCalculateGrid = true;
                    tiler.coverageRadius = EffectiveBackgroundCoverage(); // cover the whole view, not just mapRadius+5
                    tiler.GenerateTiles();
                }
                else
                {
                    Debug.LogWarning($"Background image not found: {backgroundImagePath}. Using fallback.");
                    CreateFallbackTerrain(renderer);
                }
            }
            else
            {
                CreateFallbackTerrain(renderer);
            }
        }

        // Add boundary collider
        var collider = terrainObject.GetComponent<CircleCollider2D>();
        if (collider == null)
        {
            collider = terrainObject.AddComponent<CircleCollider2D>();
        }
        collider.radius = mapRadius;
        collider.isTrigger = true;
    }

    void CreateFallbackTerrain(SpriteRenderer renderer)
    {
        renderer.sprite = CreateSimpleCircleSprite();
        renderer.color = terrainColor;

        // Scale to desired map size
        float desiredDiameter = mapRadius * 2f;
        float currentSize = 0.64f; // Default sprite size
        float scale = desiredDiameter / currentSize;
        terrainObject.transform.localScale = Vector3.one * scale;
    }

    void CreateCentralCore()
    {
        if (!enableCentralCore) return;

        GameObject coreObject = new GameObject("CentralCore");
        coreObject.transform.SetParent(transform, false);
        coreObject.transform.position = Vector3.zero;

        // DEACTIVATE BEFORE AddComponent.
        //
        // Awake does NOT run on an inactive GameObject. This matters because
        // CentralCore.Awake() → InitializeComponents() → LoadCoreSprites() runs
        // IMMEDIATELY inside AddComponent<CentralCore>() on an active object — so any
        // configuration written on the following lines arrives too late. An earlier
        // version handed over the sprite frames after AddComponent and the Core loaded
        // ZERO frames every time, falling back to a Resources folder that no longer
        // exists: invisible Core, and an emergency collider because setup had failed.
        //
        // Configuring while inactive and activating at the end means Awake sees the
        // finished object. It also fixes the energy assignment below, which had the same
        // ordering problem in a less visible form.
        coreObject.SetActive(false);

        centralCore = coreObject.AddComponent<CentralCore>();
        centralCore.coreSize = coreSize;

        // The Core is built in code and has no prefab, so its sprite references cannot
        // live on it — they live on this component (a scene object) and are handed over
        // here, before the object is activated and LoadCoreSprites() resolves.
        if (coreSpriteFrames != null && coreSpriteFrames.Length > 0)
            centralCore.SetCoreFrames(coreSpriteFrames);
        else
            Debug.LogWarning("[TowerDefenseMap] No Core Sprite Frames assigned — the Central Core " +
                             "will fall back to Resources/Sprites/Buildings/Towers/Core, which breaks " +
                             "once that art leaves the Resources folder. Assign frames 00–23 on this " +
                             "component (and leave out the old 'central_core_sprite' sheet).");

        // FIX: a rebuild (new stage layout) or a resume must NOT reset the core to
        // full. carryCoreEnergy is set either by ClearExistingMap (mid-run rebuild) or
        // by SeedCoreEnergy (save resume, called before the map is built).
        // Raw field writes here were pointless: CentralCore.Start registers with
        // EnergyManager moments later, and registration stamped a full pool over the
        // top. Everything now goes through SeedEnergyState, which survives
        // registration (see EnergyManager.InitializeConsumerEnergy).
        float resolvedMax = ResolvedCoreMaxEnergy;

        if (carryCoreEnergy)
        {
            float carriedMax = carriedCoreMaxEnergy > 0f ? carriedCoreMaxEnergy : resolvedMax;
            centralCore.SeedEnergyState(Mathf.Clamp(carriedCoreEnergy, 0f, carriedMax), carriedMax);
            carryCoreEnergy = false;   // one-shot; a genuinely fresh run seeds nothing
        }
        else if (coreMaxEnergy > 0f || coreStartingEnergy > 0f)
        {
            // A per-map override is in play. Seeding (rather than writing the fields)
            // is what stops registration resetting a deliberate sub-full start.
            float start = coreStartingEnergy > 0f ? coreStartingEnergy : resolvedMax;
            centralCore.SeedEnergyState(Mathf.Clamp(start, 0f, resolvedMax), resolvedMax);
        }
        // else: no override and nothing carried -- let EnergyManager fill it.

        // Put back the augment components that lived on the core we just destroyed.
        RestoreCoreAugments(centralCore);

        // Subscribe to core events BEFORE activation, so nothing raised during Awake or
        // Start is missed.
        centralCore.OnEnergyChanged += OnCoreEnergyChanged;
        centralCore.OnEnergyDepleted += OnCoreEnergyDepleted;

        // Fully configured — now let Awake/Start run.
        coreObject.SetActive(true);
    }

    void CreateTowerSlots()
    {
        slotsContainer = new GameObject("Tower Slots");
        slotsContainer.transform.parent = transform;
        slotsContainer.transform.localPosition = Vector3.zero;

        bonusSlotsAdded = 0;

        // If a layout is active, use it; otherwise fall back to rings (original behaviour).
        if (activeLayout != null && activeLayout.layoutType == MapLayoutDefinition.LayoutType.Custom)
        {
            CreateCustomSlots(activeLayout.customSlotPositions, activeLayout.customSlotSize);
        }
        else
        {
            // Concentric rings — original logic, untouched.
            List<TowerDefenseMap.RingConfiguration> sourceRings =
                (activeLayout != null && activeLayout.rings != null && activeLayout.rings.Count > 0)
                ? activeLayout.rings
                : rings;

            for (int ringIndex = 0; ringIndex < sourceRings.Count; ringIndex++)
            {
                var ring = sourceRings[ringIndex];
                if (!ring.enabled) continue;

                if (ringIndex % 2 == 1)
                {
                    float angleStep = 360f / ring.slotCount;
                    ring.rotationOffset = angleStep / 2f;
                }
                else
                {
                    ring.rotationOffset = 0f;
                }

                CreateRingSlots(ring);
            }
        }

        // FIX: put back the bonus slots the "additional_tower_slots" augment had
        // revealed before this rebuild. Without this the augment silently expired at
        // every stage transition and towers saved into a bonus slot (ringIndex 99)
        // could never be found by TowerPlacementManager.FindSlot on resume.
        if (carriedBonusSlots > 0)
        {
            int want = carriedBonusSlots;
            carriedBonusSlots = 0;               // consume before AddBonusSlots re-enters
            int restored = AddBonusSlots(want);
            if (restored < want)
                Debug.LogWarning($"[TowerDefenseMap] Only {restored}/{want} augment bonus slot(s) could be " +
                                 "re-created on this layout — the new layout defines fewer bonusSlotPositions. " +
                                 "Any tower saved in a missing slot will be skipped on restore.");
        }
    }

    // Spawns free-form slots from a list of world-space positions.
    void CreateCustomSlots(List<Vector2> positions, float slotSize)
    {
        if (positions == null) return;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 pos = new Vector3(positions[i].x, positions[i].y, 0f);
            GameObject slotObj = CreateTowerSlot(pos, slotSize, i);
            slotObj.transform.parent = slotsContainer.transform;
            slotObj.name = $"Slot_{i}";

            TowerSlot slot = slotObj.GetComponent<TowerSlot>();
            slot.ringIndex = 0;
            slot.slotIndex = i;
            allTowerSlots.Add(slot);
        }
    }

    // Spawns the rectangular obstacles defined by the active layout.
    // Skipped silently when there's no active layout or no obstacles.
    void CreateLayoutObstacles()
    {
        if (activeLayout == null || activeLayout.obstacles == null || activeLayout.obstacles.Count == 0)
            return;

        obstaclesContainer = new GameObject("Layout Obstacles");
        obstaclesContainer.transform.parent = transform;
        obstaclesContainer.transform.localPosition = Vector3.zero;

        Sprite squareSprite = CreateSimpleSquareSprite();
        int obstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
        if (obstacleLayer < 0) obstacleLayer = 0; // fallback to default if layer doesn't exist

        // Core safety radius: any obstacle that intersects this disc around (0,0)
        // is skipped so a misplaced wall can never block the central core.
        float coreSafeRadius = (enableCentralCore ? coreSize : 0f) + 1.0f;

        for (int i = 0; i < activeLayout.obstacles.Count; i++)
        {
            var obs = activeLayout.obstacles[i];

            if (obs.blocksMovement && IntersectsCore(obs, coreSafeRadius))
            {
                Debug.LogWarning($"[TowerDefenseMap] Skipping obstacle '{obs.label}' " +
                                 $"at {obs.position} size {obs.size} — would overlap central core.");
                continue;
            }

            CreateOneObstacle(obs, i, squareSprite, obstacleLayer);
        }
    }

    // Returns true if the obstacle overlaps a circle of `safeRadius` around
    // the world origin (where the core lives). Shape-aware:
    //   Rectangle → tested as AABB (rotation ignored, conservative).
    //   Circle    → centre-distance < (safe + radius).
    //   Ellipse   → conservative bounding circle of max axis.
    bool IntersectsCore(MapLayoutDefinition.LayoutObstacle obs, float safeRadius)
    {
        switch (obs.shape)
        {
            case MapLayoutDefinition.ObstacleShape.Circle:
                {
                    float r = obs.size.x * 0.5f;
                    float dx = obs.position.x;
                    float dy = obs.position.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    return dist < (safeRadius + r);
                }
            case MapLayoutDefinition.ObstacleShape.Ellipse:
                {
                    float r = Mathf.Max(obs.size.x, obs.size.y) * 0.5f;
                    float dx = obs.position.x;
                    float dy = obs.position.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    return dist < (safeRadius + r);
                }
            case MapLayoutDefinition.ObstacleShape.Crescent:
                {
                    // Conservative: treat the crescent as its bounding circle.
                    // The convex outer arc fits inside this radius.
                    float r = Mathf.Max(obs.size.x, obs.size.y) * 0.5f;
                    float dx = obs.position.x;
                    float dy = obs.position.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    return dist < (safeRadius + r);
                }
            case MapLayoutDefinition.ObstacleShape.Rectangle:
            default:
                {
                    float halfW = obs.size.x * 0.5f;
                    float halfH = obs.size.y * 0.5f;
                    float closestX = Mathf.Clamp(0f, obs.position.x - halfW, obs.position.x + halfW);
                    float closestY = Mathf.Clamp(0f, obs.position.y - halfH, obs.position.y + halfH);
                    float dx = closestX;
                    float dy = closestY;
                    return (dx * dx + dy * dy) < (safeRadius * safeRadius);
                }
        }
    }

    // Creates one obstacle. Dispatches by shape:
    //   Rectangle → segmented box colliders (good for walls/buildings).
    //   Circle    → single CircleCollider2D    (smooth, no segment edges).
    //   Ellipse   → single CapsuleCollider2D   (still smooth, just stretched).
    // Circle and Ellipse are STRONGLY preferred for blockMovement obstacles
    // because enemy local-avoidance glides around them with no snag points.
    void CreateOneObstacle(MapLayoutDefinition.LayoutObstacle obs, int i, Sprite squareSprite, int obstacleLayer)
    {
        switch (obs.shape)
        {
            case MapLayoutDefinition.ObstacleShape.Circle:
                CreateCircleObstacle(obs, i, obstacleLayer);
                break;
            case MapLayoutDefinition.ObstacleShape.Ellipse:
                CreateEllipseObstacle(obs, i, obstacleLayer);
                break;
            case MapLayoutDefinition.ObstacleShape.Crescent:
                CreateCrescentObstacle(obs, i, obstacleLayer);
                break;
            case MapLayoutDefinition.ObstacleShape.Rectangle:
            default:
                CreateRectObstacle(obs, i, squareSprite, obstacleLayer);
                break;
        }
    }

    // Rectangle obstacle — the original behaviour, kept as-is for compatibility
    // with all existing layouts (Chokepoint Corridor, Breached Fortress, etc.).
    void CreateRectObstacle(MapLayoutDefinition.LayoutObstacle obs, int i, Sprite squareSprite, int obstacleLayer)
    {
        string name = string.IsNullOrEmpty(obs.label) ? $"Obstacle_{i}" : obs.label;
        GameObject root = new GameObject(name);
        root.transform.parent = obstaclesContainer.transform;
        root.transform.position = new Vector3(obs.position.x, obs.position.y, 0f);
        root.transform.rotation = Quaternion.Euler(0f, 0f, obs.rotationDegrees);

        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.parent = root.transform;
        visualGO.transform.localPosition = Vector3.zero;
        visualGO.transform.localRotation = Quaternion.identity;
        visualGO.transform.localScale = new Vector3(obs.size.x, obs.size.y, 1f);

        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";

        if (obs.blocksMovement)
        {
            sr.sprite = CreateRoundedSquareSprite();
            sr.color = Color.white;
            visualGO.transform.localScale = new Vector3(obs.size.x, obs.size.y, 1f);

            const int sortOrderBase = 1000;
            const float sortPrecision = 10f;
            float sortY = obs.position.y - obs.size.y * 0.5f;
            sr.sortingOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);

            CreateSegmentedColliders(root, obs.size, obstacleLayer);
        }
        else
        {
            sr.sprite = squareSprite;
            sr.color = obs.color.a > 0f ? obs.color : new Color(0.35f, 0.40f, 0.50f, 0.85f);
            sr.sortingOrder = 600;
        }
    }

    // Circle obstacle — single round sprite + single CircleCollider2D.
    // Enemies' local-avoidance treats this as a smooth round obstacle with
    // no edges to snag on.
    void CreateCircleObstacle(MapLayoutDefinition.LayoutObstacle obs, int i, int obstacleLayer)
    {
        string name = string.IsNullOrEmpty(obs.label) ? $"Circle_{i}" : obs.label;
        GameObject root = new GameObject(name);
        root.transform.parent = obstaclesContainer.transform;
        root.transform.position = new Vector3(obs.position.x, obs.position.y, 0f);
        root.transform.rotation = Quaternion.identity; // rotation irrelevant for a circle

        float diameter = obs.size.x; // size.y is ignored for a true circle

        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.parent = root.transform;
        visualGO.transform.localPosition = Vector3.zero;
        visualGO.transform.localRotation = Quaternion.identity;
        visualGO.transform.localScale = new Vector3(diameter, diameter, 1f);

        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";

        if (obs.blocksMovement)
        {
            // Use the rich stone-textured sprite (same palette as Rectangle
            // obstacles) so blocking circles read as solid stone walls.
            sr.sprite = CreateStoneCircleSprite();
            sr.color = Color.white; // texture provides the color
        }
        else
        {
            // Decorative circle: flat-tinted simple disc.
            sr.sprite = CreateRoundObstacleSprite();
            sr.color = obs.color.a > 0f ? obs.color : new Color(0.45f, 0.46f, 0.50f, 1f);
        }

        if (obs.blocksMovement)
        {
            // Y-sort from bottom edge so the obstacle behaves like a "standing" sprite.
            const int sortOrderBase = 1000;
            const float sortPrecision = 10f;
            float sortY = obs.position.y - diameter * 0.5f;
            sr.sortingOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);

            root.layer = obstacleLayer;
            var col = root.AddComponent<CircleCollider2D>();
            col.radius = diameter * 0.5f;
            col.isTrigger = false;
        }
        else
        {
            sr.sortingOrder = 600;
        }
    }

    // Ellipse obstacle — same round sprite stretched non-uniformly, plus a
    // CapsuleCollider2D for smooth oblong collision.
    void CreateEllipseObstacle(MapLayoutDefinition.LayoutObstacle obs, int i, int obstacleLayer)
    {
        string name = string.IsNullOrEmpty(obs.label) ? $"Ellipse_{i}" : obs.label;
        GameObject root = new GameObject(name);
        root.transform.parent = obstaclesContainer.transform;
        root.transform.position = new Vector3(obs.position.x, obs.position.y, 0f);
        root.transform.rotation = Quaternion.Euler(0f, 0f, obs.rotationDegrees);

        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.parent = root.transform;
        visualGO.transform.localPosition = Vector3.zero;
        visualGO.transform.localRotation = Quaternion.identity;
        visualGO.transform.localScale = new Vector3(obs.size.x, obs.size.y, 1f);

        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";

        if (obs.blocksMovement)
        {
            // Stone-textured sprite — same palette as Rectangle obstacles.
            // When stretched non-uniformly (size.x != size.y) the texture
            // stretches with it, which reads naturally for oblong rocks.
            sr.sprite = CreateStoneCircleSprite();
            sr.color = Color.white;
        }
        else
        {
            sr.sprite = CreateRoundObstacleSprite();
            sr.color = obs.color.a > 0f ? obs.color : new Color(0.45f, 0.46f, 0.50f, 1f);
        }

        if (obs.blocksMovement)
        {
            const int sortOrderBase = 1000;
            const float sortPrecision = 10f;
            float sortY = obs.position.y - obs.size.y * 0.5f;
            sr.sortingOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);

            root.layer = obstacleLayer;
            var col = root.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(obs.size.x, obs.size.y);
            col.direction = (obs.size.x > obs.size.y)
                ? CapsuleDirection2D.Horizontal
                : CapsuleDirection2D.Vertical;
            col.isTrigger = false;
        }
        else
        {
            sr.sortingOrder = 600;
        }
    }

    // Cached round sprite for Circle and Ellipse obstacles.
    // 128×128 antialiased disc with a subtle radial darkening for depth.
    private static Sprite cachedRoundObstacleSprite;
    Sprite CreateRoundObstacleSprite()
    {
        if (cachedRoundObstacleSprite != null) return cachedRoundObstacleSprite;

        const int size = 128;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float r = size * 0.48f;
        float rInner = r - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center.x;
                float dy = y + 0.5f - center.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha;
                if (d <= rInner) alpha = 1f;
                else if (d <= r) alpha = 1f - (d - rInner);
                else alpha = 0f;

                // Subtle radial darken so it doesn't look like a flat disc.
                float shade = Mathf.Lerp(1f, 0.78f, d / r);

                pixels[y * size + x] = new Color(shade, shade, shade, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        cachedRoundObstacleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        return cachedRoundObstacleSprite;
    }

    // Stone-textured CIRCLE sprite. Uses the same multi-octave Perlin palette
    // as CreateRoundedSquareSprite (stoneLight/Mid/Dark + cracks + edge
    // darkening) but masked into a circle with a slightly noisy outline so
    // it doesn't look like a perfect geometric disc.
    private static Sprite cachedStoneCircleSprite;
    Sprite CreateStoneCircleSprite()
    {
        if (cachedStoneCircleSprite != null) return cachedStoneCircleSprite;

        const int size = 128;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;
        float baseR = halfSize - size * 0.05f; // 5% inset baseline
        float edgeNoiseAmp = size * 0.04f;     // up to ~4% rim jitter
        // (No edgeNoiseScale needed here — the rim noise is sampled in polar
        // coordinates below using Cos(ang)/Sin(ang), not x/y screen coords.)

        const float surfaceScale1 = 0.06f;
        const float surfaceScale2 = 0.18f;
        const float surfaceScale3 = 0.45f;

        Color stoneLight = new Color(0.62f, 0.63f, 0.65f, 1f);
        Color stoneMid = new Color(0.48f, 0.49f, 0.52f, 1f);
        Color stoneDark = new Color(0.30f, 0.31f, 0.34f, 1f);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float dx = x - halfSize;
                float dy = y - halfSize;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // Direction-dependent edge noise — perturbs the circle's rim
                // by a few percent so it reads as a worn stone, not a perfect disc.
                float ang = Mathf.Atan2(dy, dx);
                float rimNoise = (Mathf.PerlinNoise(Mathf.Cos(ang) * 4f + 31.7f,
                                                    Mathf.Sin(ang) * 4f + 13.2f) - 0.5f) * 2f * edgeNoiseAmp;
                float r = baseR + rimNoise;

                float inside;
                if (dist <= r - 1f) inside = 1f;
                else if (dist <= r) inside = 1f - (dist - (r - 1f));
                else inside = 0f;

                if (inside <= 0f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                //  Stone surface with multi-octave noise 
                float n1 = Mathf.PerlinNoise(x * surfaceScale1, y * surfaceScale1);
                float n2 = Mathf.PerlinNoise(x * surfaceScale2 + 7.3f, y * surfaceScale2 + 4.1f) * 0.5f;
                float n3 = Mathf.PerlinNoise(x * surfaceScale3 + 13.7f, y * surfaceScale3 + 9.2f) * 0.25f;
                float surface = (n1 + n2 + n3) / 1.75f;

                float crack = surface < 0.32f ? Mathf.InverseLerp(0.32f, 0.18f, surface) : 0f;

                Color stone;
                if (surface < 0.5f)
                    stone = Color.Lerp(stoneMid, stoneDark, (0.5f - surface) * 1.4f);
                else
                    stone = Color.Lerp(stoneMid, stoneLight, (surface - 0.5f) * 1.4f);

                stone = Color.Lerp(stone, stoneDark * 0.7f, crack);

                // Edge darkening — radial falloff toward the outer rim.
                float edgeDist = r - dist;
                float edgeDarkening = Mathf.InverseLerp(0f, size * 0.05f, edgeDist);
                stone = Color.Lerp(stoneDark, stone, edgeDarkening);

                stone.a = inside;
                pixels[y * size + x] = stone;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        cachedStoneCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        return cachedStoneCircleSprite;
    }

    // Stone-textured CRESCENT sprite
    private static Sprite cachedCrescentSprite;
    Sprite CreateCrescentSprite()
    {
        if (cachedCrescentSprite != null) return cachedCrescentSprite;

        const int size = 256;  // bigger texture — crescents are wider on screen
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;

        // Outer disc parameters
        float outerR = size * 0.46f;
        // Inner "bite" disc — pushed UP so the concave side faces +Y (up).
        // Game code rotates the GameObject so the bite points wherever we want.
        Vector2 innerCenter = new Vector2(halfSize, halfSize + size * 0.18f);
        float innerR = size * 0.36f;

        const float rimNoiseScale = 4f;
        float rimNoiseAmp = size * 0.025f;

        const float surfaceScale1 = 0.04f;
        const float surfaceScale2 = 0.12f;
        const float surfaceScale3 = 0.32f;

        Color stoneLight = new Color(0.62f, 0.63f, 0.65f, 1f);
        Color stoneMid = new Color(0.48f, 0.49f, 0.52f, 1f);
        Color stoneDark = new Color(0.30f, 0.31f, 0.34f, 1f);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float dx = x - halfSize;
                float dy = y - halfSize;
                float distOuter = Mathf.Sqrt(dx * dx + dy * dy);

                float idx = x - innerCenter.x;
                float idy = y - innerCenter.y;
                float distInner = Mathf.Sqrt(idx * idx + idy * idy);

                // Noisy outer rim
                float angOuter = Mathf.Atan2(dy, dx);
                float outerNoise = (Mathf.PerlinNoise(Mathf.Cos(angOuter) * rimNoiseScale + 31.7f,
                                                      Mathf.Sin(angOuter) * rimNoiseScale + 13.2f) - 0.5f) * 2f * rimNoiseAmp;
                float effOuterR = outerR + outerNoise;

                // Noisy inner (concave) rim
                float angInner = Mathf.Atan2(idy, idx);
                float innerNoise = (Mathf.PerlinNoise(Mathf.Cos(angInner) * rimNoiseScale + 47.1f,
                                                      Mathf.Sin(angInner) * rimNoiseScale + 22.5f) - 0.5f) * 2f * rimNoiseAmp;
                float effInnerR = innerR + innerNoise;

                // Inside crescent = inside outer disc AND outside inner disc
                float insideOuter;
                if (distOuter <= effOuterR - 1f) insideOuter = 1f;
                else if (distOuter <= effOuterR) insideOuter = 1f - (distOuter - (effOuterR - 1f));
                else insideOuter = 0f;

                float outsideInner;
                if (distInner >= effInnerR + 1f) outsideInner = 1f;
                else if (distInner >= effInnerR) outsideInner = (distInner - effInnerR);
                else outsideInner = 0f;

                float inside = Mathf.Min(insideOuter, outsideInner);

                if (inside <= 0f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                //  Stone surface with multi-octave noise 
                float n1 = Mathf.PerlinNoise(x * surfaceScale1, y * surfaceScale1);
                float n2 = Mathf.PerlinNoise(x * surfaceScale2 + 7.3f, y * surfaceScale2 + 4.1f) * 0.5f;
                float n3 = Mathf.PerlinNoise(x * surfaceScale3 + 13.7f, y * surfaceScale3 + 9.2f) * 0.25f;
                float surface = (n1 + n2 + n3) / 1.75f;

                float crack = surface < 0.32f ? Mathf.InverseLerp(0.32f, 0.18f, surface) : 0f;

                Color stone;
                if (surface < 0.5f)
                    stone = Color.Lerp(stoneMid, stoneDark, (0.5f - surface) * 1.4f);
                else
                    stone = Color.Lerp(stoneMid, stoneLight, (surface - 0.5f) * 1.4f);
                stone = Color.Lerp(stone, stoneDark * 0.7f, crack);

                // Edge darkening near both rims
                float edgeDistOuter = effOuterR - distOuter;
                float edgeDistInner = distInner - effInnerR;
                float edgeDist = Mathf.Min(edgeDistOuter, edgeDistInner);
                float edgeDarkening = Mathf.InverseLerp(0f, size * 0.04f, edgeDist);
                stone = Color.Lerp(stoneDark, stone, edgeDarkening);

                stone.a = inside;
                pixels[y * size + x] = stone;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        cachedCrescentSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        return cachedCrescentSprite;
    }

    // Crescent obstacle. Renders as a single textured crescent sprite.
    //   obs.position        = centre of the bounding box
    //   obs.size.x          = overall width of the crescent
    //   obs.size.y          = overall height of the crescent
    //   obs.rotationDegrees = rotation of the whole sprite. 0° = bite facing UP.
    //                         90° = bite facing LEFT. 180° = bite DOWN. 270° = RIGHT.
    void CreateCrescentObstacle(MapLayoutDefinition.LayoutObstacle obs, int i, int obstacleLayer)
    {
        string name = string.IsNullOrEmpty(obs.label) ? $"Crescent_{i}" : obs.label;
        GameObject root = new GameObject(name);
        root.transform.parent = obstaclesContainer.transform;
        root.transform.position = new Vector3(obs.position.x, obs.position.y, 0f);
        root.transform.rotation = Quaternion.Euler(0f, 0f, obs.rotationDegrees);

        // Visual
        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.parent = root.transform;
        visualGO.transform.localPosition = Vector3.zero;
        visualGO.transform.localRotation = Quaternion.identity;
        visualGO.transform.localScale = new Vector3(obs.size.x, obs.size.y, 1f);

        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";
        sr.sprite = CreateCrescentSprite();
        sr.color = Color.white; // texture provides color

        if (obs.blocksMovement)
        {
            const int sortOrderBase = 1000;
            const float sortPrecision = 10f;
            float sortY = obs.position.y - obs.size.y * 0.5f;
            sr.sortingOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);

            root.layer = obstacleLayer;

            // Approximate the convex outer rim
            const float hornAngleDeg = 43.7f;        // horn position (from +X)
            const float arcStartDeg = hornAngleDeg;  // right horn
            const float arcEndDeg = hornAngleDeg - 360f + (180f - 2f * hornAngleDeg);
            // ^ left horn (180-43.7 = 136.3°) reached by sweeping CLOCKWISE
            //   around the bottom: 136.3 - 360 = -223.7°. Span ≈ 267.4°.

            const float rimFactor = 0.44f;           // just inside the 0.46 rim
            float rx = obs.size.x * rimFactor;
            float ry = obs.size.y * rimFactor;

            // Collider count scales with the arc length so the chain stays
            // continuous (no gaps to snag on) regardless of crescent size.
            float avgRimR = (rx + ry) * 0.5f;
            float arcRad = Mathf.Abs(arcStartDeg - arcEndDeg) * Mathf.Deg2Rad;
            float arcLen = avgRimR * arcRad;
            int colliderCount = Mathf.Clamp(Mathf.CeilToInt(arcLen / 0.22f), 10, 64);

            // Pre-compute centres so we can size the collider radius from the
            // largest gap between neighbours — guaranteeing overlap (a solid,
            // smooth convex wall) even where the ellipse is most stretched.
            var centers = new System.Collections.Generic.List<Vector2>(colliderCount);
            for (int c = 0; c < colliderCount; c++)
            {
                float t = colliderCount == 1 ? 0.5f : c / (float)(colliderCount - 1);
                float phi = Mathf.Lerp(arcStartDeg, arcEndDeg, t) * Mathf.Deg2Rad;
                centers.Add(new Vector2(rx * Mathf.Cos(phi), ry * Mathf.Sin(phi)));
            }

            float maxGap = 0f;
            for (int c = 1; c < centers.Count; c++)
                maxGap = Mathf.Max(maxGap, Vector2.Distance(centers[c], centers[c - 1]));
            float colliderRadius = Mathf.Max(maxGap * 0.62f, 0.06f);

            for (int c = 0; c < centers.Count; c++)
            {
                var colGO = new GameObject($"Col_{c}");
                colGO.transform.parent = root.transform;
                colGO.transform.localPosition = new Vector3(centers[c].x, centers[c].y, 0f);
                colGO.layer = obstacleLayer;

                var col = colGO.AddComponent<CircleCollider2D>();
                col.radius = colliderRadius;
                col.isTrigger = false;
            }
        }
        else
        {
            sr.sortingOrder = 600;
        }
    }

    // Cache the stone texture so all obstacles share one (saves memory).
    private static Sprite cachedStoneSprite;

    // Creates a stone-textured sprite with rough irregular edges and
    // darker noise patterns suggesting cracks and mineral variation.
    Sprite CreateRoundedSquareSprite()
    {
        if (cachedStoneSprite != null) return cachedStoneSprite;

        const int size = 128;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;

        // Edge irregularity — perturb the rectangle's outline using low-freq noise.
        // The result reads as rough stone instead of a clean rectangle.
        const float edgeNoiseScale = 0.08f;
        const float edgeNoiseAmp = size * 0.06f;     // up to ~6% jitter on edges
        const float baseInset = size * 0.04f;        // 4% inset baseline

        // Surface noise — multiple octaves of value noise.
        const float surfaceScale1 = 0.06f;
        const float surfaceScale2 = 0.18f;
        const float surfaceScale3 = 0.45f;

        // Stone palette: mid-grey with slight cool tint, darker cracks.
        Color stoneLight = new Color(0.62f, 0.63f, 0.65f, 1f);
        Color stoneMid = new Color(0.48f, 0.49f, 0.52f, 1f);
        Color stoneDark = new Color(0.30f, 0.31f, 0.34f, 1f);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                //  Edge with noise-perturbed outline 
                float dx = x - halfSize;
                float dy = y - halfSize;

                // Direction-dependent edge noise — different perturbation per side
                float edgeNoise = (Mathf.PerlinNoise(x * edgeNoiseScale, y * edgeNoiseScale) - 0.5f) * 2f * edgeNoiseAmp;

                float halfX = halfSize - baseInset + edgeNoise;
                float halfY = halfSize - baseInset + edgeNoise;

                // Corner roundness: combine x/y distances with a soft corner radius
                float cornerRadius = size * 0.08f;
                float adx = Mathf.Abs(dx);
                float ady = Mathf.Abs(dy);
                float cornerDx = Mathf.Max(0f, adx - (halfX - cornerRadius));
                float cornerDy = Mathf.Max(0f, ady - (halfY - cornerRadius));
                float cornerDist = Mathf.Sqrt(cornerDx * cornerDx + cornerDy * cornerDy);

                float inside;
                if (adx <= halfX - cornerRadius && ady <= halfY - cornerRadius)
                    inside = 1f;
                else if (cornerDist <= cornerRadius - 1f)
                    inside = 1f;
                else if (cornerDist <= cornerRadius)
                    inside = 1f - (cornerDist - (cornerRadius - 1f));
                else
                    inside = 0f;

                if (inside <= 0f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                //  Stone surface with multi-octave noise 
                float n1 = Mathf.PerlinNoise(x * surfaceScale1, y * surfaceScale1);
                float n2 = Mathf.PerlinNoise(x * surfaceScale2 + 7.3f, y * surfaceScale2 + 4.1f) * 0.5f;
                float n3 = Mathf.PerlinNoise(x * surfaceScale3 + 13.7f, y * surfaceScale3 + 9.2f) * 0.25f;
                float surface = (n1 + n2 + n3) / 1.75f; // normalise to ~0..1

                // Add subtle "cracks" — darken where noise is very low
                float crack = surface < 0.32f ? Mathf.InverseLerp(0.32f, 0.18f, surface) : 0f;

                // Blend stone palette: dark→mid→light by surface value
                Color stone;
                if (surface < 0.5f)
                    stone = Color.Lerp(stoneMid, stoneDark, (0.5f - surface) * 1.4f);
                else
                    stone = Color.Lerp(stoneMid, stoneLight, (surface - 0.5f) * 1.4f);

                // Apply crack darkening
                stone = Color.Lerp(stone, stoneDark * 0.7f, crack);

                // Slight darkening near edges for depth
                float edgeDist = Mathf.Min(halfX - adx, halfY - ady);
                float edgeDarkening = Mathf.InverseLerp(0f, size * 0.04f, edgeDist);
                stone = Color.Lerp(stoneDark, stone, edgeDarkening);

                stone.a = inside;
                pixels[y * size + x] = stone;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        cachedStoneSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        return cachedStoneSprite;
    }

    // Plain 64×64 white square (used by decorative obstacles).
    Sprite CreateSimpleSquareSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 64f);
    }

    // Creates a grid of small BoxCollider2Ds covering the obstacle area, so
    // enemies treat each one as an individual obstacle and can navigate around
    // segment edges instead of getting stuck on a giant wall's center.
    void CreateSegmentedColliders(GameObject root, Vector2 size, int obstacleLayer)
    {
        float seg = Mathf.Max(0.5f, maxColliderSegmentSize);

        int cols = Mathf.Max(1, Mathf.CeilToInt(size.x / seg));
        int rows = Mathf.Max(1, Mathf.CeilToInt(size.y / seg));

        float cellW = size.x / cols;
        float cellH = size.y / rows;
        float startX = -size.x * 0.5f + cellW * 0.5f;
        float startY = -size.y * 0.5f + cellH * 0.5f;

        for (int cx = 0; cx < cols; cx++)
        {
            for (int cy = 0; cy < rows; cy++)
            {
                GameObject seg2 = new GameObject($"Col_{cx}_{cy}");
                seg2.transform.parent = root.transform;
                seg2.transform.localRotation = Quaternion.identity;
                seg2.transform.localScale = Vector3.one;
                seg2.transform.localPosition = new Vector3(
                    startX + cx * cellW,
                    startY + cy * cellH,
                    0f);
                seg2.layer = obstacleLayer;

                var col = seg2.AddComponent<BoxCollider2D>();
                col.size = new Vector2(cellW * 0.95f, cellH * 0.95f); // small gap so segments are individually findable
                col.isTrigger = false;
            }
        }
    }

    void CreateRingSlots(RingConfiguration ring)
    {
        GameObject ringContainer = new GameObject($"Ring_R{ring.radius}_S{ring.slotCount}");
        ringContainer.transform.parent = slotsContainer.transform;
        ringContainer.transform.localPosition = Vector3.zero;

        float angleStep = 360f / ring.slotCount;

        for (int i = 0; i < ring.slotCount; i++)
        {
            float angle = (i * angleStep + ring.rotationOffset) * Mathf.Deg2Rad;
            Vector3 position = new Vector3(
                Mathf.Cos(angle) * ring.radius,
                Mathf.Sin(angle) * ring.radius,
                0f
            );

            GameObject slotObj = CreateTowerSlot(position, ring.slotSize, i);
            slotObj.transform.parent = ringContainer.transform;
            slotObj.name = $"Slot_{i}";

            TowerSlot slot = slotObj.GetComponent<TowerSlot>();
            slot.ringIndex = rings.IndexOf(ring);
            slot.slotIndex = i;

            allTowerSlots.Add(slot);
        }
    }

    GameObject CreateTowerSlot(Vector3 position, float size, int index)
    {
        GameObject slot;

        if (towerSlotPrefab != null)
        {
            slot = Instantiate(towerSlotPrefab, position, Quaternion.identity);

            // Scale prefab to match desired size
            var sr = slot.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                float currentDiameter = sr.bounds.size.x;
                float desiredDiameter = size * 0.3f;
                float scaleFactor = desiredDiameter / currentDiameter;
                slot.transform.localScale = Vector3.one * scaleFactor;
            }

            var col = slot.GetComponent<CircleCollider2D>();
            if (col != null)
            {
                col.radius = size * 0.5f;
            }
        }
        else
        {
            // Create default slot
            slot = new GameObject("TowerSlot");
            slot.transform.position = position;

            var renderer = slot.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSimpleCircleSprite();
            renderer.color = new Color(1f, 1f, 1f, 0.5f);
            renderer.sortingOrder = 1;

            // Scale to desired size
            float desiredDiameter = size;
            float currentSize = 0.64f;
            float scale = desiredDiameter / currentSize;
            slot.transform.localScale = Vector3.one * scale;

            var collider = slot.AddComponent<CircleCollider2D>();
            collider.radius = size * 0.5f;
            collider.isTrigger = true;
        }

        // Ensure TowerSlot component exists
        if (slot.GetComponent<TowerSlot>() == null)
        {
            slot.AddComponent<TowerSlot>();
        }

        // Force the slot to be Untagged so EnemyController.UpdateTarget() never
        // mistakes an empty slot for a real tower. Only actual placed towers
        // (instantiated by TowerSlot.PlaceTower) carry the "Tower" tag.
        if (slot.CompareTag("Tower"))
            slot.tag = "Untagged";

        return slot;
    }

    Sprite CreateSimpleCircleSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                colors[y * size + x] = distance <= radius ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    void DrawDebugCircles()
    {
        // ── Custom layouts: draw their own connection lines, skip rings ─────
        if (activeLayout != null && activeLayout.layoutType == MapLayoutDefinition.LayoutType.Custom)
        {
            DrawLayoutConnectionLines();
            return;
        }

        // ── Concentric layouts (or no layout): draw the rings as before ─────
        List<RingConfiguration> sourceRings =
            (activeLayout != null && activeLayout.rings != null && activeLayout.rings.Count > 0)
            ? activeLayout.rings
            : rings;

        foreach (var ring in sourceRings)
        {
            if (!ring.enabled) continue;

            GameObject debugCircle = new GameObject($"Debug_Ring_{ring.radius}");
            debugCircle.transform.parent = transform;
            debugCircle.transform.localPosition = Vector3.zero;

            LineRenderer lr = debugCircle.AddComponent<LineRenderer>();
            Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.color = debugCircleColor;
            lr.material = lineMaterial;
            lr.startColor = debugCircleColor;
            lr.endColor = debugCircleColor;
            lr.startWidth = debugCircleWidth;
            lr.endWidth = debugCircleWidth;
            lr.useWorldSpace = false;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 500;  // above terrain (-1), below all gameplay sprites

            int segments = 64;
            lr.positionCount = segments + 1;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * 2f * Mathf.PI;
                Vector3 pos = new Vector3(
                    Mathf.Cos(angle) * ring.radius,
                    Mathf.Sin(angle) * ring.radius,
                    0f
                );
                lr.SetPosition(i, pos);
            }
        }
    }

    // Draws the polylines defined by the active custom layout. Each line
    // becomes its own LineRenderer GameObject under the map.
    void DrawLayoutConnectionLines()
    {
        if (activeLayout == null || activeLayout.connectionLines == null) return;

        for (int idx = 0; idx < activeLayout.connectionLines.Count; idx++)
        {
            var line = activeLayout.connectionLines[idx];
            if (line == null || line.points == null || line.points.Count < 2) continue;

            GameObject lineObj = new GameObject($"LayoutLine_{idx}");
            lineObj.transform.parent = transform;
            lineObj.transform.localPosition = Vector3.zero;

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = line.color;
            lr.material = mat;
            lr.startColor = line.color;
            lr.endColor = line.color;
            lr.startWidth = line.width;
            lr.endWidth = line.width;
            lr.useWorldSpace = false;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 500;  // above terrain (-1), below all gameplay sprites
            lr.loop = line.closed;

            lr.positionCount = line.points.Count;
            for (int i = 0; i < line.points.Count; i++)
                lr.SetPosition(i, new Vector3(line.points[i].x, line.points[i].y, 0f));
        }
    }

    // Central Core event handlers
    private void OnCoreEnergyChanged(float newEnergy)
    {
        // Handle core energy changes if needed
    }

    private void OnCoreEnergyDepleted()
    {
        Debug.Log("Core energy depleted! Game Over");
        if (GameOrchestrator.Instance != null)
            GameOrchestrator.Instance.TriggerGameOver();
    }

    //  Layout API (called by orchestrator) 
    public void ApplyLayout(MapLayoutDefinition layout)
    {
        // FIX: Compare against the SOURCE layout (the asset reference passed in
        // by the orchestrator), not the scaled-clone activeLayout. When scale
        // != 1.0 we replace activeLayout with a fresh clone every call, so the
        // old `layout == activeLayout` check always missed and rebuilt the map
        // every stage — destroying all placed towers in the process.
        if (sourceLayoutCaptured &&
            layout == sourceLayout &&
            Mathf.Approximately(layoutSpreadScale, lastAppliedSpreadScale))
        {
            // Same source layout AND same scale — preserve everything.
            Debug.Log($"[TowerDefenseMap] ApplyLayout: same layout '{(layout != null ? layout.layoutName : "null")}'" +
                      $" — preserving towers/slots, no rebuild.");
            return;
        }

        // Capture the authored mapRadius once so we can rescale from the
        // original value every time layoutSpreadScale changes.
        if (!baseMapRadiusCaptured)
        {
            baseMapRadius = mapRadius;
            baseMapRadiusCaptured = true;
        }

        // Build a scaled working copy so we never mutate the source asset.
        // When scale == 1.0 we skip the clone for zero overhead (and keep the
        // exact reference so other systems comparing to the asset still match).
        if (layout != null && !Mathf.Approximately(layoutSpreadScale, 1f))
        {
            activeLayout = CreateScaledLayout(layout, layoutSpreadScale);
            // Auto-scale mapRadius proportionally so outer slots / obstacles
            // don't run past the border ring.
            mapRadius = baseMapRadius * layoutSpreadScale;
        }
        else
        {
            activeLayout = layout;
            mapRadius = baseMapRadius;
        }

        // Remember source asset + scale we just applied so the next call can
        // short-circuit if neither has changed.
        sourceLayout = layout;
        lastAppliedSpreadScale = layoutSpreadScale;
        sourceLayoutCaptured = true;

        GenerateMap();

        if (layout != null)
        {
            string scaleNote = Mathf.Approximately(layoutSpreadScale, 1f)
                ? ""
                : $" (spread ×{layoutSpreadScale:F2})";
            Debug.Log($"[TowerDefenseMap] Layout applied: {layout.layoutName}{scaleNote}");
        }
        else
        {
            Debug.Log("[TowerDefenseMap] Reverted to default rings.");
        }
    }

    // Creates a scaled clone of the supplied layout. All position-like fields
    // are multiplied by `scale`; slot SIZES are intentionally left untouched
    // so towers stay the same physical size while spreading further apart.
    private MapLayoutDefinition CreateScaledLayout(MapLayoutDefinition src, float scale)
    {
        var copy = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        copy.layoutName = src.layoutName;
        copy.description = src.description;
        copy.layoutType = src.layoutType;

        // Concentric rings — scale radius only, keep slotCount/slotSize/rotation/enabled.
        copy.rings = new List<RingConfiguration>();
        if (src.rings != null)
        {
            foreach (var r in src.rings)
            {
                if (r == null) continue;
                copy.rings.Add(new RingConfiguration
                {
                    radius = r.radius * scale,
                    slotCount = r.slotCount,
                    slotSize = r.slotSize,        // unchanged
                    rotationOffset = r.rotationOffset,
                    enabled = r.enabled,
                });
            }
        }

        // Custom slot positions.
        copy.customSlotPositions = new List<Vector2>();
        if (src.customSlotPositions != null)
        {
            foreach (var p in src.customSlotPositions)
                copy.customSlotPositions.Add(p * scale);
        }
        copy.customSlotSize = src.customSlotSize;       // unchanged

        // Bonus slot positions.
        copy.bonusSlotPositions = new List<Vector2>();
        if (src.bonusSlotPositions != null)
        {
            foreach (var p in src.bonusSlotPositions)
                copy.bonusSlotPositions.Add(p * scale);
        }
        copy.bonusSlotSize = src.bonusSlotSize;         // unchanged

        // Obstacles — scale position AND size so walls/buildings keep
        // proportional length relative to the layout. Rotation/color/flags stay.
        copy.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        if (src.obstacles != null)
        {
            foreach (var o in src.obstacles)
            {
                copy.obstacles.Add(new MapLayoutDefinition.LayoutObstacle
                {
                    shape = o.shape,
                    position = o.position * scale,
                    size = o.size * scale,
                    rotationDegrees = o.rotationDegrees,
                    color = o.color,
                    blocksMovement = o.blocksMovement,
                    label = o.label,
                });
            }
        }

        // Connection lines — scale every point, keep width/colour/closed flag.
        copy.connectionLines = new List<MapLayoutDefinition.ConnectionLine>();
        if (src.connectionLines != null)
        {
            foreach (var line in src.connectionLines)
            {
                if (line == null) continue;
                var newLine = new MapLayoutDefinition.ConnectionLine
                {
                    closed = line.closed,
                    color = line.color,
                    width = line.width,
                    points = new List<Vector2>(),
                };
                if (line.points != null)
                {
                    foreach (var pt in line.points)
                        newLine.points.Add(pt * scale);
                }
                copy.connectionLines.Add(newLine);
            }
        }

        // Mark as DontSave so the clone is never accidentally serialised
        // and gets garbage-collected naturally between layout swaps.
        copy.hideFlags = HideFlags.DontSave;
        return copy;
    }

    // Reveal one or more bonus slots from the active layout's bonusSlotPositions.
    // Called by the "additional_tower_slots" augment handler.

    public int AddBonusSlots(int count)
    {
        if (activeLayout == null ||
            activeLayout.bonusSlotPositions == null ||
            activeLayout.bonusSlotPositions.Count == 0)
        {
            Debug.LogWarning("[TowerDefenseMap] No bonus slot positions defined in the active layout.");
            return 0;
        }

        int available = activeLayout.bonusSlotPositions.Count - bonusSlotsAdded;
        if (available <= 0)
        {
            Debug.LogWarning("[TowerDefenseMap] All bonus slots already revealed.");
            return 0;
        }

        int toAdd = Mathf.Min(count, available);
        float size = activeLayout.bonusSlotSize;

        for (int i = 0; i < toAdd; i++)
        {
            Vector2 pos2d = activeLayout.bonusSlotPositions[bonusSlotsAdded + i];
            Vector3 pos = new Vector3(pos2d.x, pos2d.y, 0f);

            // Use the existing slotsContainer so everything stays tidy.
            if (slotsContainer == null)
            {
                slotsContainer = new GameObject("Tower Slots");
                slotsContainer.transform.parent = transform;
                slotsContainer.transform.localPosition = Vector3.zero;
            }

            int globalIndex = allTowerSlots.Count;
            GameObject slotObj = CreateTowerSlot(pos, size, globalIndex);
            slotObj.transform.parent = slotsContainer.transform;
            slotObj.name = $"BonusSlot_{bonusSlotsAdded + i}";

            TowerSlot slot = slotObj.GetComponent<TowerSlot>();
            slot.ringIndex = 99; // sentinel: bonus slot
            slot.slotIndex = bonusSlotsAdded + i;
            allTowerSlots.Add(slot);
        }

        bonusSlotsAdded += toAdd;
        Debug.Log($"[TowerDefenseMap] Added {toAdd} bonus slot(s). Total bonus slots: {bonusSlotsAdded}/{activeLayout.bonusSlotPositions.Count}");
        return toAdd;
    }

    //  Original ring API 

    public void AddRing(float radius, int slotCount, float slotSize = 1f, float rotationOffset = 0f)
    {
        RingConfiguration newRing = new RingConfiguration
        {
            radius = radius,
            slotCount = slotCount,
            slotSize = slotSize,
            rotationOffset = rotationOffset,
            enabled = true
        };
        rings.Add(newRing);
        // TODO: Handle regeneration after augmentation
    }

    /// Handles the "additional_tower_rings" augment.
    ///
    /// The old implementation (in AugmentRegistry) appended to `rings`, this component's
    /// own list. But CreateTowerSlots reads activeLayout.rings whenever the layout
    /// defines any, and takes the CreateCustomSlots branch entirely for Custom layouts.
    /// Every built-in layout falls into one of those two branches, so the augment
    /// silently did nothing at all — the player paid for a ring that was never built.
    ///
    /// This writes to the list that actually gets read, sizes the ring so its towers
    /// keep a full lane from everything already on the map, and falls back to revealing
    /// bonus slots on Custom layouts, where "another ring" has no meaning.
    ///
    /// Returns the number of new slots the player actually gained.
    public int AddAugmentRings(int ringsToAdd)
    {
        if (ringsToAdd <= 0) return 0;

        // ---- Custom layouts: no rings to add to, so grant slots instead ----
        if (activeLayout != null &&
            activeLayout.layoutType == MapLayoutDefinition.LayoutType.Custom)
        {
            const int slotsPerRing = 4;
            int granted = AddBonusSlots(ringsToAdd * slotsPerRing);
            if (granted == 0)
            {
                Debug.LogWarning("[TowerDefenseMap] additional_tower_rings on Custom layout " +
                                 $"'{activeLayout.layoutName}': no bonus slots left to reveal.");
                return 0;
            }
            Debug.Log($"[TowerDefenseMap] additional_tower_rings on Custom layout " +
                      $"'{activeLayout.layoutName}' → revealed {granted} bonus slot(s) instead.");
            return granted;
        }

        // ---- Concentric layouts: append to the list CreateTowerSlots reads ----
        // Never mutate the source asset. When layoutSpreadScale is 1 the active layout
        // IS the project asset, so clone it first or the extra ring would be baked into
        // the .asset file and persist into every future run.
        if (activeLayout != null && ReferenceEquals(activeLayout, sourceLayout))
            activeLayout = CreateScaledLayout(activeLayout, 1f);

        List<RingConfiguration> target =
            (activeLayout != null && activeLayout.rings != null && activeLayout.rings.Count > 0)
            ? activeLayout.rings
            : rings;

        int slotsGained = 0;

        for (int i = 0; i < ringsToAdd; i++)
        {
            if (target.Count >= maxTotalRings)
            {
                Debug.LogWarning($"[TowerDefenseMap] Cannot add more rings: at maximum ({maxTotalRings}).");
                break;
            }

            // Copy the outermost ring's shape, as the original did.
            float outerR = 2.3f;
            int slotCount = 8;
            float slotSize = 1.9f;
            foreach (var ring in target)
            {
                if (ring == null || !ring.enabled || ring.radius <= outerR) continue;
                outerR = ring.radius;
                slotCount = ring.slotCount;
                slotSize = ring.slotSize;
            }

            // Radial spacing between two rings of towers: a tower on each plus a lane
            // between them. The old +1.8 left 0.3 — a wedge, not a lane.
            float minStep = towerFootprintRadius * 2f + laneWidth;

            // CreateTowerSlots half-steps the rotation of odd-indexed rings, so the new
            // ring's offset depends on where it lands in the list.
            int newIndex = target.Count;
            float offsetDeg = (newIndex % 2 == 1) ? (180f / Mathf.Max(1, slotCount)) : 0f;

            // Walk outward until the ring clears the bonus slots and any obstacles.
            // Bonus positions are checked whether or not they're revealed yet — the
            // slots augment can turn them on at any point afterwards.
            float radius = outerR + minStep;
            float limit = outerR + minStep + 12f;
            while (radius < limit && !RingPositionIsClear(radius, slotCount, offsetDeg))
                radius += 0.25f;

            if (radius >= limit)
            {
                Debug.LogWarning("[TowerDefenseMap] additional_tower_rings: couldn't find a radius " +
                                 "with enough clearance from existing slots/obstacles. Ring not added.");
                break;
            }

            target.Add(new RingConfiguration
            {
                radius = radius,
                slotCount = slotCount,
                slotSize = slotSize,
                rotationOffset = offsetDeg,
                enabled = true,
            });

            slotsGained += slotCount;
            Debug.Log($"[TowerDefenseMap] additional_tower_rings: added ring at r={radius:F2} " +
                      $"with {slotCount} slot(s).");
        }

        if (slotsGained == 0) return 0;

        // GenerateMap() destroys slotsContainer and every tower parented to it, so the
        // rebuild has to be the tower-preserving one.
        RebuildPreservingTowers();
        return slotsGained;
    }

    /// True when every slot on a candidate ring keeps a full lane from the layout's
    /// bonus slot positions and from its obstacles.
    bool RingPositionIsClear(float radius, int slotCount, float offsetDeg)
    {
        if (activeLayout == null || slotCount <= 0) return true;

        for (int i = 0; i < slotCount; i++)
        {
            float a = Mathf.Deg2Rad * (i * 360f / slotCount + offsetDeg);
            Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;

            if (activeLayout.bonusSlotPositions != null)
            {
                foreach (var b in activeLayout.bonusSlotPositions)
                    if (Vector2.Distance(b, p) < towerFootprintRadius * 2f + laneWidth)
                        return false;
            }

            if (activeLayout.obstacles != null)
            {
                foreach (var o in activeLayout.obstacles)
                {
                    if (!o.blocksMovement) continue;
                    float bound = Mathf.Max(o.size.x, o.size.y) * 0.5f;
                    if (Vector2.Distance(o.position, p) < bound + towerFootprintRadius + laneWidth)
                        return false;
                }
            }
        }
        return true;
    }

    public void RemoveRing(int ringIndex)
    {
        if (ringIndex >= 0 && ringIndex < rings.Count)
        {
            rings.RemoveAt(ringIndex);
        }
    }

    // AUGMENT: Obstacle Generation (augment ID 3)

    public int GenerateAugmentArches()
    {
        Debug.Log("[AUGMENT/Arches] GenerateAugmentArches() called.");

        if (augmentArchContainer == null)
        {
            augmentArchContainer = new GameObject("Augment Arches");
            augmentArchContainer.transform.parent = transform;
            augmentArchContainer.transform.localPosition = Vector3.zero;
        }

        int obstacleLayerIndex = LayerMask.NameToLayer(obstacleLayerName);
        if (obstacleLayerIndex < 0) obstacleLayerIndex = 0;
        int obstacleMask = 1 << obstacleLayerIndex;

        // Each application places a fresh ring. Push successive rings outward and
        // rotate them so they interleave with earlier arches.
        int wave = augmentArchWaves;

        // Anything spawned from here on can be rolled back if the finished ring
        // turns out to choke the map (see the reachability check at the end).
        int childCountBefore = augmentArchContainer.transform.childCount;

        // --- Choose a placement radius OUTSIDE the outer slots ---
        float outerSlotR = 0f;
        foreach (var s in allTowerSlots)
        {
            if (s == null) continue;
            float r = ((Vector2)s.transform.position).magnitude;
            if (r > outerSlotR) outerSlotR = r;
        }
        float coreSafe = (enableCentralCore ? coreSize : 0f) + 1.0f;

        // Ring radius. Arches belong OUTSIDE the outermost tower slot, in the corridor
        // enemies walk in through — which is where this augment always meant to put
        // them ("between the outer slots and the edge").
        //
        // The old bound was mapRadius - 1.4, and mapRadius (10) was smaller than the
        // layouts themselves (outer slots reach 11–16). That forced the entire ring
        // INWARD, on top of the layout's own obstacles, which is the opposite of the
        // intent. The outer bound is now the visible ground rather than the playable
        // radius, so there's somewhere legal to stand.
        const float archWidthMax = 3.2f;                  // matches the clamp below
        float archRadiusMax = archWidthMax * 0.5f;
        float archStep = laneWidth + 1.0f;                // waves step out by a full lane

        // Clear the outermost slot by an arch's own footprint PLUS a lane, so a tower
        // built on that slot still has room to be walked past.
        float baseR = outerSlotR + archRadiusMax + SlotClearance;
        float ringR = baseR + wave * archStep;
        float outerLimit = EffectiveBackgroundCoverage() - 1.0f;

        if (ringR > outerLimit)
        {
            Debug.LogWarning($"[AUGMENT/Arches] No room outside the outer slots for wave {wave} " +
                             $"(ring would sit at {ringR:F1}, ground ends at {outerLimit:F1}). " +
                             $"Nothing placed.");
            return 0;
        }
        if (ringR <= coreSafe + 1.0f)
        {
            Debug.LogWarning("[AUGMENT/Arches] No room between core and map edge for arches.");
            return 0;
        }

        // Arch sizing scales gently with the ring. The HEIGHT cap is the important
        // one: a crescent is a C, and once its bowl is deeper than an enemy is
        // wide it becomes an alcove the enemy noses into and then has to reverse
        // out of. Measured threshold is about 1.2 — the old formula reached 1.44
        // at the width clamp, just over the line.
        float archWidth = Mathf.Clamp(ringR * 0.42f, 2.0f, archWidthMax);
        float archHeight = Mathf.Min(archWidth * 0.45f, 1.2f);

        Debug.Log($"[AUGMENT/Arches] wave={wave} outerSlotR={outerSlotR:F1} " +
                  $"mapRadius={mapRadius:F1} ringR={ringR:F1} outerLimit={outerLimit:F1} " +
                  $"archSize=({archWidth:F1}x{archHeight:F1}) coreSafe={coreSafe:F1}");

        // Per-wave angular phase so successive augments interleave their arches
        // and don't stack on the same angles. 18° keeps arches off the cardinal
        // axes (common enemy approach lanes).
        float baseOffsetDeg = wave * 31f + 18f;

        //  Candidate placement: scan many angles across a few radii 

        const int maxArches = 5;
        const int angleSamples = 24;              // every 15° around the ring
        float minSeparationDeg = 360f / (maxArches + 1); // ~60° apart minimum

        // Fallback radii step OUTWARD only, a full lane at a time. Nudging inward (as
        // this used to) now just walks back into the slot rings, and each step has to
        // be a lane so a fallback ring can't land a wedge-width from the first.
        float[] radiusAttempts =
        {
            ringR,
            ringR + laneWidth,
            ringR + laneWidth * 2f,
            ringR + laneWidth * 3f,
        };

        int placed = 0;
        var placedPositions = new List<Vector2>();
        var placedAngles = new List<float>();

        foreach (float tryR in radiusAttempts)
        {
            if (placed >= maxArches) break;
            if (tryR > outerLimit) continue;   // off the visible ground
            // Rotate the sample start per wave so successive augments interleave.
            float startDeg = baseOffsetDeg + (tryR * 7.13f) % 15f; // small per-radius phase

            for (int s = 0; s < angleSamples && placed < maxArches; s++)
            {
                float angDeg = startDeg + s * (360f / angleSamples);
                float angNorm = Mathf.Repeat(angDeg, 360f);

                // Enforce minimum angular separation from already-placed arches.
                bool tooClose = false;
                foreach (float pa in placedAngles)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(angNorm, pa));
                    if (d < minSeparationDeg) { tooClose = true; break; }
                }
                if (tooClose) continue;

                float angRad = angDeg * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Cos(angRad) * tryR, Mathf.Sin(angRad) * tryR);

                // Convex side faces away from core, concave mouth cups the core.
                float rotDeg = angDeg + 90f;

                if (!IsArchPlacementClear(pos, archWidth, archHeight, coreSafe, placedPositions))
                    continue;

                var arch = new MapLayoutDefinition.LayoutObstacle
                {
                    shape = MapLayoutDefinition.ObstacleShape.Crescent,
                    position = pos,
                    size = new Vector2(archWidth, archHeight),
                    rotationDegrees = rotDeg,
                    color = Color.white,        // crescent texture supplies its own colour
                    blocksMovement = true,
                    label = $"AugmentArch_w{wave}_{placed}",
                };

                SpawnCrescentInto(arch, augmentArchContainer, obstacleLayerIndex, wave * 100 + placed);

                placedPositions.Add(pos);
                placedAngles.Add(angNorm);
                placed++;
            }
        }

        if (placed == 0)
        {
            Debug.LogWarning("[AUGMENT/Arches] Map too crowded — no arches could be placed " +
                             "without overlapping existing obstacles or slots.");
            return 0;
        }

        // Sanity check: confirm an ENEMY-SIZED body still has open lanes to the core.
        // This used to be advisory — it logged a warning and kept the arches anyway,
        // which meant a bad roll could leave the map unwinnable. Now it's binding: if
        // the ring closed the map down, the arches from this wave are removed and the
        // augment reports that it placed nothing.

        if (!CoreHasOpenApproach(coreSafe, obstacleMask))
        {
            int removed = 0;
            for (int i = augmentArchContainer.transform.childCount - 1; i >= childCountBefore; i--)
            {
                var child = augmentArchContainer.transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
                removed++;
            }
            Debug.LogWarning($"[AUGMENT/Arches] Placement left too few open lanes to the core — " +
                             $"rolled back {removed} arch(es) from wave {wave}. " +
                             $"The map is unchanged.");
            return 0;
        }

        augmentArchWaves++;
        Debug.Log($"[AUGMENT/Arches] Added {placed} arches (wave {wave}) at radius {ringR:F1}.");

        // Arch placement only avoids obstacles and slots, not people — a crescent
        // can land on a player standing on the ring. Same rule as a map rebuild:
        // only someone genuinely inside the new geometry is moved.
        if (Application.isPlaying) PlayerSpawnSafety.EvacuateAllPlayers();

        return placed;
    }


    bool IsArchPlacementClear(Vector2 pos, float width, float height, float coreSafe,
                              List<Vector2> placedThisWave)
    {
        // Bounding radius of the crescent footprint (convex outer rim). The real
        // collider chain reaches 0.44*width + ~0.2, so max(w,h)*0.5 is a close and
        // slightly conservative stand-in.
        float archRadius = Mathf.Max(width, height) * 0.5f;

        // 1) Core clearance — enemies must still be able to stand next to the core
        //    and swing at it.
        if (pos.magnitude < coreSafe + archRadius + laneWidth)
        {
            Debug.Log($"[AUGMENT/Arches]   reject {pos} — too close to core.");
            return false;
        }

        // 2) Existing obstacles — biome decorations (trees/rocks) AND layout obstacles
        //    (walls/stones/crescents).
        //
        //    The pad used to be 0.4, which let an arch land within half an enemy's
        //    width of a wall: exactly the wedge geometry the layouts were rewritten to
        //    eliminate. It's now a full lane, so an arch either leaves room to walk
        //    past or isn't placed at all.
        float pad = laneWidth;
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, archRadius + pad, Physics2D.AllLayers);
        foreach (var hit in hits)
        {
            if (hit == null) continue;

            // Trigger colliders (terrain boundary, tower slots, pickups) are not
            // solid terrain — never block on them.
            if (hit.isTrigger) continue;

            // Skip the central core itself (core clearance handled in step 1).
            if (centralCore != null && hit.transform.IsChildOf(centralCore.transform))
                continue;

            // Skip the player — transient, not terrain. ("Player" is a tag the
            // project defines and uses elsewhere, so CompareTag is safe here.)
            if (hit.CompareTag("Player"))
                continue;

            // Skip enemies — they may be passing through the ring as the augment
            // fires; they're transient and must not veto a placement.
            if (hit.GetComponent<EnemyController>() != null ||
                hit.GetComponentInParent<EnemyController>() != null)
                continue;

            // NOTE: arches from EARLIER waves are deliberately NOT skipped any more.
            // They used to be, on the theory that per-wave spacing kept things tidy —
            // but per-wave spacing is angular, and successive waves only stepped out
            // 1.4 units, so wave 2 could land a fraction of a lane from wave 1. They
            // are solid obstacles like any other and get the same lane of clearance.

            // Anything else with a SOLID collider here is real terrain → block.
            Debug.Log($"[AUGMENT/Arches]   reject {pos} — too close to solid collider " +
                      $"'{hit.name}' (layer {LayerMask.LayerToName(hit.gameObject.layer)}).");
            return false;
        }

        // 3) Tower slots — an arch must leave room for a tower to be built here AND
        //    for an enemy to get past that tower. The old 1.1 covered the slot
        //    footprint but nothing else, so an arch could sit a wedge-width from a
        //    future tower.
        foreach (var s in allTowerSlots)
        {
            if (s == null) continue;
            Vector2 sp = s.transform.position;
            if (Vector2.Distance(sp, pos) < archRadius + SlotClearance)
            {
                Debug.Log($"[AUGMENT/Arches]   reject {pos} — too close to a tower slot.");
                return false;
            }
        }

        // 4) Other arches placed this wave.
        foreach (var p in placedThisWave)
        {
            if (Vector2.Distance(p, pos) < archRadius * 2f + laneWidth)
                return false;
        }

        return true;
    }

    // Sweeps an ENEMY-SIZED circle from the map edge straight toward the core. If at
    // least two sweeps get through without hitting an obstacle, the core is considered
    // reachable.
    //
    // This used to cast zero-width rays, which answer the wrong question entirely: a
    // ray slips through a 1-unit gap that a 1.5-wide enemy cannot. Sweeping a circle of
    // enemyClearanceRadius asks whether an actual enemy fits.
    //
    // Still a conservative proxy — it only tests straight radial lines, so a map with
    // only curved routes reads as blocked and the augment declines to place. Failing
    // that direction is the right one: worst case the player doesn't get their arches.
    bool CoreHasOpenApproach(float coreSafe, int obstacleMask)
    {
        const int probeCount = 24;            // every 15°
        float startR = mapRadius - 0.5f;
        int clearLanes = 0;

        for (int i = 0; i < probeCount; i++)
        {
            float ang = (360f / probeCount) * i * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 from = dir * startR;
            // Sweep inward toward the core; stop just outside the core safe disc.
            float rayLen = startR - coreSafe;
            if (rayLen <= 0f) continue;

            RaycastHit2D rh = Physics2D.CircleCast(from, enemyClearanceRadius, -dir, rayLen, obstacleMask);
            if (rh.collider == null)
            {
                clearLanes++;
                if (clearLanes >= 2) return true; // enough open corridors
            }
        }
        return clearLanes >= 2;
    }

    // Spawns a single Crescent obstacle into an arbitrary container, reusing the
    // exact collider/visual construction of CreateCrescentObstacle. Kept as a
    // thin wrapper so the augment shares one source of truth with layout arches.
    void SpawnCrescentInto(MapLayoutDefinition.LayoutObstacle obs, GameObject container,
                           int obstacleLayer, int index)
    {
        GameObject prevContainer = obstaclesContainer;
        obstaclesContainer = container;      // CreateCrescentObstacle parents to obstaclesContainer
        try
        {
            CreateCrescentObstacle(obs, index, obstacleLayer);
        }
        finally
        {
            obstaclesContainer = prevContainer; // restore — never leave it pointing at the arch container
        }
    }

    public TowerSlot GetSlot(int ringIndex, int slotIndex)
    {
        foreach (var slot in allTowerSlots)
        {
            if (slot.ringIndex == ringIndex && slot.slotIndex == slotIndex)
            {
                return slot;
            }
        }
        return null;
    }

    public List<TowerSlot> GetAllSlots()
    {
        return new List<TowerSlot>(allTowerSlots);
    }

    public List<TowerSlot> GetAvailableSlots()
    {
        return allTowerSlots.FindAll(slot => !slot.IsOccupied);
    }

    public CentralCore GetCentralCore()
    {
        return centralCore;
    }

    public bool HasCentralCore()
    {
        return centralCore != null;
    }

    // Runtime background switching methods
    public void SetBackgroundImage(string imagePath)
    {
        backgroundImagePath = imagePath;
        useBackgroundImage = true;
        if (terrainObject != null)
        {
            if (terrainObject != backgroundGameObject) RetireAndDestroy(terrainObject);
            terrainObject = null;
        }
        CreateTerrain();
    }

    public void UseGeneratedTerrain()
    {
        useBackgroundImage = false;
        if (terrainObject != null)
        {
            if (terrainObject != backgroundGameObject) RetireAndDestroy(terrainObject);
            terrainObject = null;
        }
        CreateTerrain();
    }

    // Utility methods
    [ContextMenu("Tile Background to Map Radius")]
    public void ScaleBackgroundToMapRadius()
    {
        if (backgroundGameObject != null)
        {
            // Keep native size — use tiling for coverage
            backgroundGameObject.transform.localScale = Vector3.one;



            // PUSH THE BACKGROUND BACK IN THE Z-AXIS
            Vector3 pos = backgroundGameObject.transform.position;
            backgroundGameObject.transform.position = new Vector3(pos.x, pos.y, 2f);

            BackgroundTiler tiler = backgroundGameObject.GetComponent<BackgroundTiler>();
            if (tiler == null)
                tiler = backgroundGameObject.AddComponent<BackgroundTiler>();
            tiler.autoCalculateGrid = true;
            tiler.coverageRadius = EffectiveBackgroundCoverage(); // cover the whole view, not just mapRadius+5
            tiler.GenerateTiles();

            Debug.Log($"Tiled background at native size to cover map radius {mapRadius}");
        }
    }

    [ContextMenu("Fix Central Core Position")]
    public void FixCentralCorePosition()
    {
        if (centralCore != null)
        {
            centralCore.transform.position = Vector3.zero;
            centralCore.transform.localPosition = Vector3.zero;
            Debug.Log("Central Core position fixed to (0,0,0)");
        }
        else
        {
            Debug.LogError("Central Core not found!");
        }
    }

    void OnDestroy()
    {
        // Clean up event subscriptions
        if (centralCore != null)
        {
            centralCore.OnEnergyChanged -= OnCoreEnergyChanged;
            centralCore.OnEnergyDepleted -= OnCoreEnergyDepleted;
        }
    }
}




