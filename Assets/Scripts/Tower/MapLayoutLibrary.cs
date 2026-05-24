using System.Collections.Generic;
using UnityEngine;


// Pool of MapLayoutDefinitions the orchestrator randomly picks from each stage.
// Create via: Assets → Create → Game → Map Layout Library
// Then assign it to RunConfig.mapLayoutLibrary.
// AUTO-POPULATION
// If `useBuiltinLayouts` is true (default) and the `layouts` list is empty,
// the library will automatically populate itself with the built-in
// example layouts on first access.
//
// TESTING / DEBUG: FORCE A SPECIFIC LAYOUT
// Set `forcedLayoutName` to a layout's name (e.g. "Stonehenge") to make
// PickRandom() always return that layout. Useful for testing one layout
// repeatedly. Set it back to empty / "None" to resume random picking.
// You can also right-click the asset and use the context menu shortcuts at
// the bottom to force any of the built-in layouts in one click.
[CreateAssetMenu(fileName = "MapLayoutLibrary", menuName = "Game/Map Layout Library")]
public class MapLayoutLibrary : ScriptableObject
{
    [Tooltip("Layouts available to the orchestrator.\n" +
             "Leave EMPTY and keep 'Use Built-in Layouts' checked to use all built-in example layouts.\n" +
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

    [Header("Debug / Testing")]
    [Tooltip("If non-empty, PickRandom() always returns the layout whose\n" +
             "layoutName matches this string (case-insensitive). Use to test a\n" +
             "specific layout in isolation. Leave empty for normal random pick.\n" +
             "Examples: 'Stonehenge', 'Twin Moons', 'Orbital Dance'.")]
    public string forcedLayoutName = "";

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

    // Returns a random layout from the resolved pool, optionally excluding already-used ones.
    // If `forcedLayoutName` is set and matches a layout, that layout is returned
    // instead — every time, ignoring `alreadyUsed`. Returns null if the library is empty.
    public MapLayoutDefinition PickRandom(List<MapLayoutDefinition> alreadyUsed = null)
    {
        var list = GetLayouts();
        if (list == null || list.Count == 0) return null;

        // Force override path (used for testing one layout in isolation)
        if (!string.IsNullOrWhiteSpace(forcedLayoutName))
        {
            var forced = FindLayoutByName(forcedLayoutName);
            if (forced != null)
            {
                Debug.Log($"[MapLayoutLibrary] FORCED layout: {forced.layoutName} " +
                          $"(forcedLayoutName='{forcedLayoutName}').");
                return forced;
            }
            else
            {
                Debug.LogWarning($"[MapLayoutLibrary] forcedLayoutName='{forcedLayoutName}' " +
                                 $"didn't match any layout — falling back to random pick.");
            }
        }

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

    // Case-insensitive name lookup against the resolved pool.
    public MapLayoutDefinition FindLayoutByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var list = GetLayouts();
        foreach (var l in list)
        {
            if (l == null) continue;
            if (string.Equals(l.layoutName, name, System.StringComparison.OrdinalIgnoreCase))
                return l;
        }
        return null;
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

    // Clear the force-select.
    [ContextMenu("Clear Forced Layout")]
    public void ClearForcedLayout()
    {
        forcedLayoutName = "";
        Debug.Log("[MapLayoutLibrary] Forced layout cleared. Random picking resumed.");
    }

    // Context-menu shortcuts for each of the new built-in layouts.
    // Right-click the asset in the project window and pick one.
    [ContextMenu("Force: Stonehenge")] private void _F1() { forcedLayoutName = "Stonehenge"; LogForced(); }
    [ContextMenu("Force: Crossroads Pillars")] private void _F3() { forcedLayoutName = "Crossroads Pillars"; LogForced(); }
    [ContextMenu("Force: Asteroid Belt")] private void _F4() { forcedLayoutName = "Asteroid Belt"; LogForced(); }
    [ContextMenu("Force: Pinwheel")] private void _F6() { forcedLayoutName = "Pinwheel"; LogForced(); }

    private void LogForced()
    {
        Debug.Log($"[MapLayoutLibrary] forcedLayoutName set to '{forcedLayoutName}'. " +
                  "Next stage will use this layout.");
    }
}
