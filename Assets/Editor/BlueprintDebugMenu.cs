#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Tools > Blueprints menu. Must live in a folder named "Editor" (e.g. Assets/Editor/).
public static class BlueprintDebugMenu
{
    const string PrefsKey = "WeaponBlueprints_v1";   // must match WeaponBlueprintRegistry

    [MenuItem("Tools/Blueprints/Log Discovered Blueprints")]
    static void LogBlueprints()
    {
        Debug.Log($"[Blueprints] Saved slots: {PlayerPrefs.GetString(PrefsKey, "(none)")}");
    }

    [MenuItem("Tools/Blueprints/Reset Discovered Blueprints")]
    static void ResetBlueprints()
    {
        if (!EditorUtility.DisplayDialog("Reset blueprints",
                "Forget every discovered weapon blueprint? Other saved settings are kept.",
                "Reset", "Cancel")) return;

        // In Play mode the registry holds its own copy in memory, so clear that too.
        if (Application.isPlaying && WeaponBlueprintRegistry.Instance != null)
            WeaponBlueprintRegistry.Instance.ClearAll();

        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
        Debug.Log("[Blueprints] Discovered blueprints reset.");
    }
}
#endif
