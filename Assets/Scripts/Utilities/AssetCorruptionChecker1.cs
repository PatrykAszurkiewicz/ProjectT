#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

public class AssetCorruptionChecker
{
    [MenuItem("Tools/Check Project Assets")]
    private static void CheckProjectAssets()
    {
        Debug.Log("=== Checking Project Assets ===");

        CheckScriptableObjects();
        CheckPrefabs();
        CheckForMissingReferences();

        Debug.Log("=== Asset Check Complete ===");
    }

    private static void CheckScriptableObjects()
    {
        var guids = AssetDatabase.FindAssets("t:ScriptableObject");
        Debug.Log($"Found {guids.Length} ScriptableObject assets");

        foreach (var guid in guids)
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
                else
                {
                    Debug.Log($"✓ Valid ScriptableObject: {asset.name}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error checking ScriptableObject {asset.name}: {e.Message}");
            }
        }
    }

    private static void CheckPrefabs()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab");
        Debug.Log($"Found {guids.Length} Prefab assets");

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogError($"Failed to load Prefab at: {path}");
                continue;
            }

            // Check for missing components
            var components = prefab.GetComponentsInChildren<Component>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    Debug.LogError($"Missing component in prefab: {prefab.name} at {path}");
                }
            }

            Debug.Log($"✓ Valid Prefab: {prefab.name}");
        }
    }

    private static void CheckForMissingReferences()
    {
        var allAssets = AssetDatabase.FindAssets("")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".asset") || path.EndsWith(".prefab"))
            .ToArray();

        Debug.Log($"Checking {allAssets.Length} assets for missing references...");

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
                        iterator.objectReferenceValue == null)
                    {
                        // Only report if it's supposed to have a reference (not intentionally null)
                        if (!iterator.hasMultipleDifferentValues)
                        {
                            Debug.LogWarning($"Potential missing reference in {asset.name}.{iterator.propertyPath} at {path}");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error checking asset {asset.name}: {e.Message}");
            }
        }
    }
}
#endif