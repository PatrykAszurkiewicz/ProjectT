#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

public class ImprovedAssetChecker
{
    [MenuItem("Tools/Check Assets (Smart Filter)")]
    private static void CheckAssetsWithFiltering()
    {
        Debug.Log("=== Smart Asset Validation (Filtering False Positives) ===");

        CheckScriptableObjectsSmart();
        CheckPrefabsSmart();
        CheckMissingReferencesFiltered();

        Debug.Log("=== Smart Asset Validation Complete ===");
    }

    private static void CheckScriptableObjectsSmart()
    {
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        var projectAssets = guids.Where(guid =>
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return path.StartsWith("Assets/"); // Only check project assets, not packages
        }).ToArray();

        Debug.Log($"Checking {projectAssets.Length} project ScriptableObject assets");

        foreach (var guid in projectAssets)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

            if (asset == null)
            {
                Debug.LogError($"Failed to load ScriptableObject at: {path}");
                continue;
            }

            try
            {
                var serializedObject = new SerializedObject(asset);
                if (serializedObject == null)
                {
                    Debug.LogError($"Corrupted ScriptableObject: {asset.name} at {path}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error checking ScriptableObject {asset.name}: {e.Message}");
            }
        }
    }

    private static void CheckPrefabsSmart()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab");
        var projectPrefabs = guids.Where(guid =>
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return path.StartsWith("Assets/"); // Only check project prefabs
        }).ToArray();

        Debug.Log($"Checking {projectPrefabs.Length} project Prefab assets");

        foreach (var guid in projectPrefabs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogError($"Failed to load Prefab at: {path}");
                continue;
            }

            // Check for missing components (actual corruption)
            var components = prefab.GetComponentsInChildren<Component>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    Debug.LogError($"Missing component in prefab: {prefab.name} at {path}");
                }
            }
        }
    }

    private static void CheckMissingReferencesFiltered()
    {
        var allAssets = AssetDatabase.FindAssets("")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.StartsWith("Assets/") && (path.EndsWith(".asset") || path.EndsWith(".prefab")))
            .ToArray();

        Debug.Log($"Checking {allAssets.Length} project assets for critical missing references...");

        var criticalIssues = new List<string>();

        foreach (var path in allAssets)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) continue;

            try
            {
                var serializedObject = new SerializedObject(asset);
                var iterator = serializedObject.GetIterator();

                while (iterator.NextVisible(true))
                {
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                        iterator.objectReferenceValue == null &&
                        IsCriticalMissingReference(iterator.propertyPath, asset.name))
                    {
                        string issue = $"CRITICAL: Missing {iterator.propertyPath} in {asset.name} at {path}";
                        criticalIssues.Add(issue);
                        Debug.LogError(issue, asset);
                    }
                }
                serializedObject.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error checking asset {asset.name}: {e.Message}");
            }
        }

        if (criticalIssues.Count == 0)
        {
            Debug.Log("✅ No critical missing references found!");
        }
        else
        {
            Debug.LogWarning($"Found {criticalIssues.Count} critical issues that need attention.");
        }
    }

    private static bool IsCriticalMissingReference(string propertyPath, string assetName)
    {
        // Filter out known false positives
        var ignoredPaths = new HashSet<string>
        {
            "m_Icon",                          // Prefab icons (optional)
            "atlas",                          // TextMesh Pro atlas (auto-generated)
            "m_SourceFontFile",               // TextMesh Pro source (optional)
            "regularTypeface",                // Font variants (often null)
            "italicTypeface",                 // Font variants (often null)
            "sprite",                         // Sprite references in emoji assets (optional)
            "probeVolumeDebugShader",         // Render pipeline debug shaders (optional)
            "probeVolumeFragmentationDebugShader",
            "probeVolumeOffsetDebugShader",
            "probeVolumeSamplingDebugShader",
            "probeSamplingDebugMesh",
            "probeSamplingDebugTexture",
            "probeVolumeBlendStatesCS",
            "xrSystemData",                   // XR data (not needed for 2D)
            "m_VolumeProfile",               // Volume profiles (optional)
            "m_ObsoleteDefaultVolumeProfile" // Obsolete fields
        };

        // Check if this path should be ignored
        foreach (var ignoredPath in ignoredPaths)
        {
            if (propertyPath.Contains(ignoredPath))
            {
                return false;
            }
        }

        // Special case: Font weight table entries are often intentionally null
        if (propertyPath.Contains("FontWeightTable") || propertyPath.Contains("fontWeights"))
        {
            return false;
        }

        // Special case: Emoji glyph sprites are often null
        if (propertyPath.Contains("GlyphTable") || propertyPath.Contains("spriteInfoList"))
        {
            return false;
        }

        // Special weapon logic: only flag projectilePrefab as critical for ranged non-grappling weapons
        if (propertyPath.Contains("projectilePrefab"))
        {
            return IsProjectilePrefabRequired(assetName);
        }

        // Focus on other critical references
        var criticalPaths = new HashSet<string>
        {
            "sprite",                        // Component sprites (not emoji)
            "material",                      // Materials
            "mesh",                         // Meshes
            "prefab",                       // Prefab references
            "weaponData",                   // Weapon data
            "enemyData"                     // Enemy data
        };

        // Only flag if it's a critical reference
        foreach (var criticalPath in criticalPaths)
        {
            if (propertyPath.Contains(criticalPath))
            {
                return true;
            }
        }

        return false; // Default to not critical
    }

    private static bool IsProjectilePrefabRequired(string assetName)
    {
        // Try to load the weapon asset to check its properties
        var weaponGuids = AssetDatabase.FindAssets($"t:WeaponData {assetName}");

        foreach (var guid in weaponGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

            if (weapon != null && weapon.name == assetName)
            {
                // Only require projectile prefab for ranged weapons that aren't grappling hooks
                return weapon.isRanged && !weapon.isGrapplingHook;
            }
        }

        // If we can't find the weapon, assume projectile is required
        return true;
    }

    [MenuItem("Tools/Check Only Weapon Assets")]
    private static void CheckWeaponAssetsOnly()
    {
        Debug.Log("=== Checking Weapon Assets Only ===");

        var weaponGuids = AssetDatabase.FindAssets("t:WeaponData");

        foreach (var guid in weaponGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

            if (weapon == null) continue;

            // Check weapon-specific issues
            if (weapon.isRanged && !weapon.isGrapplingHook && weapon.projectilePrefab == null)
            {
                Debug.LogError($"Ranged weapon '{weapon.name}' missing projectile prefab!", weapon);
            }

            if (weapon.isGrapplingHook && weapon.projectilePrefab != null)
            {
                Debug.LogWarning($"Grappling hook '{weapon.name}' has projectile prefab but shouldn't need one", weapon);
            }

            if (weapon.sprite == null)
            {
                Debug.LogWarning($"Weapon '{weapon.name}' missing sprite", weapon);
            }

            Debug.Log($"✓ Checked weapon: {weapon.name}");
        }

        Debug.Log("=== Weapon Asset Check Complete ===");
    }
}
#endif