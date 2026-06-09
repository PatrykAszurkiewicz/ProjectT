using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

// LORE CHEST SPAWNER 
// Periodically spawns lore chests on the map and lays a light-grey footprint trail to the nearest one. 

public class LoreChestSpawner : MonoBehaviour
{
    public static LoreChestSpawner Instance { get; private set; }

    [Header("Spawn Settings")]
    [Tooltip("Seconds between spawn attempts.")]
    public float spawnInterval = 25f;
    [Tooltip("Delay before the first spawn attempt.")]
    public float initialDelay = 8f;
    [Tooltip("Maximum chests allowed on the map at once.")]
    public int maxChestsOnMap = 1;

    [Header("Chest Visual (optional)")]
    [Tooltip("Leave EMPTY for a generated chest sprite. Assign a prefab to use your own art.\n" +
             "A LoreChest component is added automatically if the prefab lacks one.")]
    public GameObject chestPrefabOverride;

    [Tooltip("Uniform scale multiplier applied to every spawned chest (2.5 = 2.5× size).\n" +
             "Scales the trigger collider too. Set to 1 if your prefab is already sized.")]
    public float chestScale = 2.5f;

    [Header("Custom Lore (optional)")]
    [Tooltip("Optional database of extra fragments authored in the inspector. Use ids >= 1000.\n" +
             "Leave empty to use only the built-in lore.")]
    public LoreFragmentDatabase extraFragmentSet;

    [Header("Placement Bounds")]
    [Tooltip("Fraction of the map radius chests may spawn within (keeps them off the border).")]
    [Range(0.2f, 1f)] public float mapRadiusFraction = 0.85f;
    [Tooltip("Keep chests at least this far from the central core.")]
    public float coreClearance = 2.5f;
    [Tooltip("Spawn chests at least this far from the map centre, so they appear out in the\n" +
             "field (like the gremlin) rather than hugging the core. Sampled as a ring between\n" +
             "this and the outer radius.\n" +
             "NOTE: changing the number here in code does NOT update a LoreChestSpawner already\n" +
             "placed in your scene — edit this value on that component in the Inspector.")]
    public float minDistanceFromCenter = 10f;
    [Tooltip("Force the OUTER spawn radius in world units. 0 = auto (detected map radius × fraction).\n" +
             "If auto-detection underestimates your map and chests cluster near the centre, set this\n" +
             "to a large value (e.g. 30) to push the spawn ring outward.")]
    public float spawnRadiusOverride = 0f;
    [Tooltip("Minimum thickness of the spawn ring. Guarantees a valid band even when\n" +
             "'Min Distance From Center' is larger than the detected/override outer radius —\n" +
             "so the inner distance is always honoured.")]
    public float minRingWidth = 6f;
    [Tooltip("Don't drop a chest right on top of the player.")]
    public float minDistanceFromPlayer = 3f;
    [Tooltip("Minimum spacing between two chests.")]
    public float minDistanceBetweenChests = 4f;
    [Tooltip("Fallback play radius used only if no TowerDefenseMap is found in the scene.")]
    public float fallbackMapRadius = 9f;

    [Header("Obstacle Avoidance (mirrors GremlinSpawner)")]
    [Tooltip("Radius around a candidate point that must be clear of obstacles.")]
    public float spawnClearanceRadius = 0.55f;
    [Tooltip("How many random positions to try before giving up this tick.")]
    public int maxSpawnPlacementAttempts = 24;
    public LayerMask obstacleBlockingMask = ~0;

    [Header("Path Indicator")]
    public bool showPathToChest = true;
    public float pathFootprintSpacing = 1.5f;
    public float pathMaxDistance = 30f;
    [Tooltip("Footprint size (world units ≈ this value). Raise to match the gremlin trail's size.")]
    public float pathFootprintScale = 1.0f;
    [Tooltip("Footprint colour — papyrus (yellowish white-grey) by default.")]
    public Color pathTint = new Color(0.90f, 0.86f, 0.74f, 1f);
    [Tooltip("Footprint opacity (0-1).")]
    [Range(0f, 1f)] public float pathAlpha = 0.8f;
    [Tooltip("Sort offset for the trail. Positive draws prints in FRONT of nearby grass " +
             "(they're still clamped below the chest itself).")]
    public int pathSortOffset = 8;
    [Tooltip("End the trail this far before the chest, so the prints lead up to it without overlapping.")]
    public float pathStopBeforeChest = 1.6f;
    [Tooltip("Log trail diagnostics to the Console (is a chest found? how far? prints laid?).")]
    public bool pathDebugLogs = false;

    [Header("Archive (recovered-logs browser)")]
    [Tooltip("Create the archive so the player can browse already-unlocked papers.")]
    public bool enableArchiveBrowser = true;
    [Tooltip("Show a small on-screen button that opens the archive. " +
             "(The archive can also be opened from code or the hotkey below.)")]
    public bool showArchiveButton = true;
    [Tooltip("Keyboard key that toggles the archive. Set to None to disable the hotkey.")]
    public KeyCode archiveHotkey = KeyCode.J;
    [Tooltip("Show a 'Reset Lore' button inside the archive (handy for testing). Off for players.")]
    public bool archiveResetButton = false;

    [Header("Archive Theming (optional — assign your pause-menu assets)")]
    [Tooltip("Drag your big menu box here (e.g. MenuPanel_1). Used as the archive backdrop.\n" +
             "If left empty, it's loaded from Resources, then a generated panel as a last resort.")]
    public Sprite archivePanelSprite;
    [Tooltip("Optional darker box for the list column (e.g. MenuPanel). Leave empty for a clean inset.")]
    public Sprite archiveListPanelSprite;
    [Tooltip("Drag your button sprite here (e.g. Button). Used for the Archive/close/reset buttons.")]
    public Sprite archiveButtonSprite;
    [Tooltip("Optional button highlight sprite (e.g. Button_1) shown on hover/press.")]
    public Sprite archiveButtonHighlightSprite;
    [Tooltip("Drag your menu font here — a TextMeshPro font asset (e.g. Cinzel-Black SDF). " +
             "Used on the archive AND the scroll papers.")]
    public TMP_FontAsset loreFont;

    [Header("Testing")]
    [Tooltip("ON: wipe ALL historically-collected papers when the game starts (fresh every play).\n" +
             "OFF: accumulate them across sessions (normal behaviour).\n" +
             "Handy for repeatedly testing the lore-gathering loop.")]
    public bool resetLoreOnPlay = false;

    private readonly List<GameObject> activeChests = new List<GameObject>();
    private Transform playerTransform;
    private Transform coreTransform;
    private float playRadius;
    private ChestPathIndicator pathIndicator;

    void Awake()
    {
        Instance = this;
        if (extraFragmentSet != null) LoreContent.Register(extraFragmentSet);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        FindPlayer();
        ResolveMapBounds();

        // Pre-bake the cached paper/panel art now (at load) so the first chest scroll
        // and first archive open don't hitch generating textures mid-game.
        LorePaperArt.Warm();

        // Testing reset: wipe all historically collected papers if requested.
        if (resetLoreOnPlay)
        {
            LoreCodex.Instance.ClearAll();
            Debug.Log("[LoreChestSpawner] resetLoreOnPlay = ON → cleared all collected papers for this test.");
        }
        else
        {
            _ = LoreCodex.Instance; // ensure it exists (loads accumulated papers from prefs)
        }

        // Spin up the archive browser (+ optional on-screen button / hotkey).
        // Push the optional font to the chest scroll popup.
        if (loreFont != null)
            LoreScrollPopup.Instance.overrideFont = loreFont;

        if (enableArchiveBrowser)
        {
            // Assign your sprites/font BEFORE the panel is first built (guaranteed theming).
            LoreArchiveMenu.Instance.ApplyTheme(
                archivePanelSprite, archiveListPanelSprite,
                archiveButtonSprite, archiveButtonHighlightSprite, loreFont);
            LoreArchiveMenu.Ensure(showArchiveButton, archiveHotkey, archiveResetButton);
        }

        if (showPathToChest) SetupPathIndicator();

        InvokeRepeating(nameof(TrySpawn), initialDelay, spawnInterval);
    }

    void SetupPathIndicator()
    {
        var go = new GameObject("ChestPathIndicator");
        go.transform.SetParent(transform);
        pathIndicator = go.AddComponent<ChestPathIndicator>();
        pathIndicator.footprintSpacing = pathFootprintSpacing;
        pathIndicator.maxPathDistance = pathMaxDistance;
        pathIndicator.footprintScale = pathFootprintScale;
        pathIndicator.footprintAlpha = pathAlpha;
        pathIndicator.updateInterval = 1.0f;
        pathIndicator.alternateFootOrientation = true;
        pathIndicator.fadeInFootprints = false;
        pathIndicator.fadeOutOldFootprints = true;
        pathIndicator.useGeneratedFootprint = true;        // light print so the grey tint shows
        pathIndicator.footprintTint = pathTint;
        pathIndicator.footprintSortOffset = pathSortOffset; // in front of grass, below the chest
        pathIndicator.stopBeforeChest = pathStopBeforeChest;
        pathIndicator.enableDebugLogs = pathDebugLogs;
    }

    void FindPlayer()
    {
        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) { playerTransform = pm.transform; return; }
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerTransform = p.transform;
    }

    void ResolveMapBounds()
    {
        var map = FindFirstObjectByType<TowerDefenseMap>();
        if (map != null)
        {
            playRadius = map.mapRadius * mapRadiusFraction;
            var core = map.GetCentralCore();
            if (core != null) coreTransform = core.transform;
        }
        else
        {
            playRadius = fallbackMapRadius * mapRadiusFraction;
        }
        if (playRadius <= 0.1f) playRadius = fallbackMapRadius * mapRadiusFraction;
    }

    void TrySpawn()
    {
        activeChests.RemoveAll(c => c == null);
        if (activeChests.Count >= maxChestsOnMap) return;

        // Nothing left to give? Don't litter the map with empty chests.
        var codex = LoreCodex.Instance;
        if (codex != null && !codex.HasUndiscovered) return;

        if (playerTransform == null) FindPlayer();

        if (TryFindClearSpawnPosition(out Vector3 pos))
            SpawnChestAt(pos);
        // else: silently retry next interval (matches GremlinSpawner)
    }

    Vector3 CenterPosition => coreTransform != null ? coreTransform.position : Vector3.zero;

    // Samples random points inside the play disc, rejecting the core area, the
    // player's immediate vicinity, other chests, and anything overlapping an
    // obstacle collider (the "don't spawn on obstacles" requirement).
    bool TryFindClearSpawnPosition(out Vector3 result)
    {
        result = default;

        int ignoreMask = LayerToBit(LayerMask.NameToLayer("Player")) |
                         LayerToBit(LayerMask.NameToLayer("Enemy"));
        int testMask = obstacleBlockingMask.value & ~ignoreMask;

        Vector2 center = CenterPosition;
        Vector2 playerPos = playerTransform != null ? (Vector2)playerTransform.position : center;

        // Ring between the inner clearance and the outer radius. The inner edge is honoured
        // DIRECTLY (it is no longer capped by the detected radius); the outer edge is the
        // override (if set) or the detected play radius, but is always pushed out to at least
        // inner + minRingWidth so the band is valid even on small/mis-detected maps.
        float baseOuter = (spawnRadiusOverride > 0.5f) ? spawnRadiusOverride : playRadius;
        float inner = Mathf.Max(coreClearance, minDistanceFromCenter);
        float outer = Mathf.Max(baseOuter, inner + Mathf.Max(1f, minRingWidth));

        for (int attempt = 0; attempt < maxSpawnPlacementAttempts; attempt++)
        {
            // Uniform-in-annulus: sqrt-lerp the squared radii so points don't cluster inward.
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, Random.value));
            Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            // Off the core (redundant with the ring, kept as a guard).
            if (Vector2.Distance(candidate, center) < coreClearance) continue;

            // Not on top of the player.
            if (playerTransform != null && Vector2.Distance(candidate, playerPos) < minDistanceFromPlayer)
                continue;

            // Spaced from other chests.
            bool tooClose = false;
            foreach (var c in activeChests)
            {
                if (c == null) continue;
                if (Vector2.Distance(candidate, c.transform.position) < minDistanceBetweenChests)
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            // Clear of obstacle colliders.
            if (!IsPositionClear(candidate, testMask)) continue;

            result = new Vector3(candidate.x, candidate.y, 0f);
            return true;
        }
        return false;
    }

    // Same logic as GremlinSpawner.IsPositionClear: inspect hits, skip triggers and
    // anything tagged Player/Enemy, and skip other lore chests.
    bool IsPositionClear(Vector2 pos, int testMask)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, spawnClearanceRadius, testMask);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (c == null) continue;
            if (c.isTrigger) continue; // energy drops, pickups, our own chest triggers
            if (c.CompareTag("Player") || c.CompareTag("Enemy")) continue;
            if (c.GetComponent<LoreChest>() != null || c.GetComponentInParent<LoreChest>() != null) continue;
            return false; // a solid obstacle is here → reject
        }
        return true;
    }

    static int LayerToBit(int layer) => (layer < 0 || layer > 31) ? 0 : (1 << layer);

    void SpawnChestAt(Vector3 position)
    {
        GameObject chest;

        if (chestPrefabOverride != null)
        {
            chest = Instantiate(chestPrefabOverride, position, Quaternion.identity);
            var lc = chest.GetComponent<LoreChest>();
            if (lc == null) lc = chest.AddComponent<LoreChest>();
            lc.proceduralVisual = false; // keep the prefab's own art
        }
        else
        {
            chest = new GameObject("LoreChest");
            chest.transform.position = position;
            chest.AddComponent<SpriteRenderer>();        // LoreChest fills in the generated sprite
            var lc = chest.AddComponent<LoreChest>();
            lc.proceduralVisual = true;
        }

        // Make the chest bigger (scales its trigger collider too).
        if (chestScale > 0f && !Mathf.Approximately(chestScale, 1f))
            chest.transform.localScale = chest.transform.localScale * chestScale;

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.gremlinAppearance, position);

        activeChests.Add(chest);
    }

    // Used by the checkpoint system on a wave-rewind so chests opened/created during
    // the rewound wave don't linger after the rollback.
    public void ClearAllChests()
    {
        foreach (var c in activeChests) if (c != null) Destroy(c);
        activeChests.Clear();
    }

    [ContextMenu("Spawn Chest Now")]
    void SpawnNow() => TrySpawn();

    // Lore is permanent (PlayerPrefs), so after enough testing every fragment is
    // discovered and chests stop spawning. This wipes it so chests return.
    [ContextMenu("Reset Lore Codex (debug)")]
    void DebugResetCodex()
    {
        LoreCodex.Instance.ClearAll();
        Debug.Log("[LoreChestSpawner] Lore codex cleared — all fragments are undiscovered again, chests will spawn.");
    }

    // Right-click → this to see WHY no chest/trail is showing.
    [ContextMenu("Log Codex / Spawn Status")]
    void DebugLogStatus()
    {
        var codex = LoreCodex.Instance;
        int total = LoreContent.TotalCount;
        int found = codex != null ? codex.DiscoveredCount : 0;
        activeChests.RemoveAll(c => c == null);
        bool gated = codex != null && !codex.HasUndiscovered;
        float dbgBaseOuter = (spawnRadiusOverride > 0.5f) ? spawnRadiusOverride : playRadius;
        float dbgInner = Mathf.Max(coreClearance, minDistanceFromCenter);
        float dbgOuter = Mathf.Max(dbgBaseOuter, dbgInner + Mathf.Max(1f, minRingWidth));
        Debug.Log($"[LoreChestSpawner] Lore {found}/{total} discovered, {total - found} left. " +
                  $"Active chests {activeChests.Count}/{maxChestsOnMap}. " +
                  $"Spawn ring ≈ {dbgInner:F1}..{dbgOuter:F1} (playRadius={playRadius:F1}, " +
                  $"override={(spawnRadiusOverride > 0.5f ? spawnRadiusOverride.ToString("F1") : "off")}). " +
                  (gated ? "→ SPAWNING DISABLED: nothing left to give (Reset Lore Codex to test)."
                         : "→ Spawning enabled."));
    }
}

