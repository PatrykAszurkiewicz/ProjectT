using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;

[InitializeOnLoad]
public class SerializationDebugger
{
    static SerializationDebugger()
    {
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        Selection.selectionChanged += OnSelectionChanged;
    }

    private static void OnHierarchyChanged()
    {
        CheckForCorruptedObjects();
    }

    private static void OnSelectionChanged()
    {
        if (Selection.activeGameObject != null)
        {
            CheckGameObjectSerialization(Selection.activeGameObject);
        }
    }

    private static void CheckForCorruptedObjects()
    {
        var allObjects = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

        foreach (var obj in allObjects)
        {
            if (obj == null) continue;

            try
            {
                var serializedObject = new SerializedObject(obj);
                if (serializedObject == null)
                {
                    Debug.LogError($"Corrupted SerializedObject found on: {obj.name}", obj.gameObject);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Serialization error on {obj.name}: {e.Message}", obj.gameObject);
            }
        }
    }

    private static void CheckGameObjectSerialization(GameObject go)
    {
        var components = go.GetComponents<MonoBehaviour>();

        foreach (var component in components)
        {
            if (component == null)
            {
                Debug.LogError($"Missing component reference on {go.name}!", go);
                continue;
            }

            try
            {
                var serializedObject = new SerializedObject(component);
                var iterator = serializedObject.GetIterator();

                while (iterator.NextVisible(true))
                {
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                        iterator.objectReferenceValue == null)
                    {
                        Debug.LogWarning($"Missing reference in {component.GetType().Name}.{iterator.propertyPath} on {go.name}", go);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error checking serialization for {component.GetType().Name} on {go.name}: {e.Message}", go);
            }
        }
    }

    [MenuItem("Tools/Debug Serialization Issues")]
    private static void DebugSerializationIssues()
    {
        Debug.Log("=== Starting Serialization Debug ===");
        CheckForCorruptedObjects();
        Debug.Log("=== Serialization Debug Complete ===");
    }

    [MenuItem("Tools/Find Objects with Missing Scripts")]
    private static void FindMissingScripts()
    {
        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        int missingCount = 0;

        foreach (var go in allObjects)
        {
            var components = go.GetComponents<Component>();
            foreach (var c in components)
            {
                if (c == null)
                {
                    missingCount++;
                    Debug.LogError($"Missing script on: {go.name}", go);
                }
            }
        }

        Debug.Log($"Found {missingCount} missing script references");
    }
}
#endif