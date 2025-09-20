#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class FMODWizardDisabler
{
    [MenuItem("Tools/FMOD/Disable Setup Wizard")]
    private static void DisableFMODSetupWizard()
    {
        try
        {
            // Access FMOD Settings
            var settingsType = System.Type.GetType("FMODUnity.Settings, FMODUnity");
            if (settingsType != null)
            {
                var instanceProperty = settingsType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var settings = instanceProperty.GetValue(null);
                    if (settings != null)
                    {
                        var hideWizardField = settingsType.GetField("HideSetupWizard", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (hideWizardField != null)
                        {
                            hideWizardField.SetValue(settings, true);
                            EditorUtility.SetDirty((UnityEngine.Object)settings);
                            Debug.Log("FMOD Setup Wizard has been disabled!");
                        }
                        else
                        {
                            Debug.LogError("Could not find HideSetupWizard field");
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("Could not find FMOD Settings class");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to disable FMOD Setup Wizard: {e.Message}");
        }
    }

    [MenuItem("Tools/FMOD/Enable Setup Wizard")]
    private static void EnableFMODSetupWizard()
    {
        try
        {
            // Access FMOD Settings
            var settingsType = System.Type.GetType("FMODUnity.Settings, FMODUnity");
            if (settingsType != null)
            {
                var instanceProperty = settingsType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var settings = instanceProperty.GetValue(null);
                    if (settings != null)
                    {
                        var hideWizardField = settingsType.GetField("HideSetupWizard", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (hideWizardField != null)
                        {
                            hideWizardField.SetValue(settings, false);
                            EditorUtility.SetDirty((UnityEngine.Object)settings);
                            Debug.Log("FMOD Setup Wizard has been enabled!");
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to enable FMOD Setup Wizard: {e.Message}");
        }
    }
}
#endif