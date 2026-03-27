#if UNITY_EDITOR
using UnityEditor;

// Automatically ensures the NightOverlay shader file exists in Assets/Shaders/
[InitializeOnLoad]
public static class NightOverlayShaderBootstrap
{
    static NightOverlayShaderBootstrap()
    {
        NightOverlayShaderSource.EnsureShaderAsset();
    }
}
#endif
