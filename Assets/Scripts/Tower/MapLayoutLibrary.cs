using System.Collections.Generic;
using UnityEngine;


// Pool of MapLayoutDefinitions the orchestrator randomly picks from each stage.
// Create via: Assets → Create → Game → Map Layout Library
// Then assign it to RunConfig.mapLayoutLibrary.
// AUTO-POPULATION
// If `useBuiltinLayouts` is true (default) and the `layouts` list is empty,
// the library will automatically populate itself with the 11 built-in
// example layouts on first access. This means the simplest setup is:
//   1. Create one MapLayoutLibrary asset.
//   2. Drag it into RunConfig.mapLayoutLibrary.
[CreateAssetMenu(fileName = "MapLayoutLibrary", menuName = "Game/Map Layout Library")]
public class MapLayoutLibrary : ScriptableObject
{
    [Tooltip("Layouts available to the orchestrator.\n" +
             "Leave EMPTY and keep 'Use Built-in Layouts' checked to use all 11 example layouts.\n" +
             "Add entries here for custom layouts.")]
    public List<MapLayoutDefinition> layouts = new List<MapLayoutDefinition>();

    [Tooltip("If true and the 'layouts' list is empty, auto-fill it with the\n" +
             "built-in example layouts from MapLayoutExamples.CreateAll().")]
    public bool useBuiltinLayouts = true;

    [Tooltip("If true, the same layout cannot appear twice in the same run.\n" +
             "Falls back to repeating if there are fewer layouts than stages.")]
    public bool avoidRepeatLayouts = true;

    [Tooltip("Pick a new layout every stage (true) or once at run start (false).\n" +
             "DEFAULT IS FALSE — players retain towers between stages, so the layout\n" +
             "must stay consistent for the whole run.")]
    public bool changeLayoutPerStage = false;

    // Cached resolved list (built-ins + user layouts), built lazily.
    [System.NonSerialized]
    private List<MapLayoutDefinition> resolvedLayouts;

    // Returns the active layout list, generating built-ins on first call if the user list is empty and useBuiltinLayouts is true.
    public List<MapLayoutDefinition> GetLayouts()
    {
        if (resolvedLayouts != null && resolvedLayouts.Count > 0)
            return resolvedLayouts;

        resolvedLayouts = new List<MapLayoutDefinition>();

        // User-defined layouts always come first
        if (layouts != null)
        {
            foreach (var l in layouts)
                if (l != null) resolvedLayouts.Add(l);
        }

        // Auto-add built-ins only if the user list is empty
        if (resolvedLayouts.Count == 0 && useBuiltinLayouts)
        {
            resolvedLayouts.AddRange(MapLayoutExamples.CreateAll());
            Debug.Log($"[MapLayoutLibrary] Auto-populated with {resolvedLayouts.Count} built-in layouts.");
        }

        return resolvedLayouts;
    }

    // Returns a random layout from the resolved pool, optionally excluding  already-used ones. Returns null if the library is empty.
    public MapLayoutDefinition PickRandom(List<MapLayoutDefinition> alreadyUsed = null)
    {
        var list = GetLayouts();
        if (list == null || list.Count == 0) return null;

        var pool = new List<MapLayoutDefinition>(list);

        if (avoidRepeatLayouts && alreadyUsed != null && alreadyUsed.Count > 0)
        {
            pool.RemoveAll(l => alreadyUsed.Contains(l));

            // If we've exhausted the pool, refill (mirrors RunConfig biome logic)
            if (pool.Count == 0)
                pool = new List<MapLayoutDefinition>(list);
        }

        return pool[Random.Range(0, pool.Count)];
    }

    // Force a re-resolve next time GetLayouts() is called. Useful after editing user layouts in the inspector mid-play.
    [ContextMenu("Refresh Layout List")]
    public void Refresh()
    {
        resolvedLayouts = null;
        Debug.Log("[MapLayoutLibrary] Layout list will refresh on next access.");
    }

    // Print the resolved layout list to the console (debug helper).
    [ContextMenu("Log Resolved Layouts")]
    public void LogResolved()
    {
        var list = GetLayouts();
        Debug.Log($"[MapLayoutLibrary] Resolved {list.Count} layouts:");
        for (int i = 0; i < list.Count; i++)
            Debug.Log($"  {i + 1}. {list[i].layoutName} ({list[i].layoutType})");
    }
}
