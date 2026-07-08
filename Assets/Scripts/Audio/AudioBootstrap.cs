using UnityEngine;

// AUDIO BOOTSTRAP
// Guarantees the audio singletons (AudioManager + FMODEvents) exist and persist
// in EVERY scene, so scene-local audio (music, SFX, and the menu-click sound)
// works whether you boot from the main menu, the weapon-select screen, gameplay,
// or hit Play on an arbitrary scene in the editor.
// It runs once after the first scene loads. If the audio singletons already exist
// (persisted from a prior scene, or placed directly in the current one), it does
// nothing. Otherwise it instantiates a prefab from Resources and keeps it alive.
//   1) Prefab containing BOTH AudioManager and FMODEvents with all their
//      EventReferences / volumes assigned.
//   2) Put the prefab at: Assets/Resources/Audio/AudioSystem.prefab
//      (path below, relative to any Resources folder, is "Audio/AudioSystem").
//   3) You can now remove the audio object(s) from individual scenes — the
//      bootstrap provides them everywhere. Any leftover per-scene copies are
//      harmless: the singletons destroy duplicates on Awake.
public static class AudioBootstrap
{
    // Path (inside a Resources folder, no extension) to the persistent audio prefab.
    private const string PrefabResourcePath = "Audio/AudioSystem";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAudioSystem()
    {
        // Already present (persisted from a previous scene, or placed in this one)?
        if (AudioManager.instance != null && FMODEvents.instance != null) return;

        var prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning(
                $"[AudioBootstrap] No audio prefab found at Resources/{PrefabResourcePath}. " +
                "Menu and SFX audio will only work in scenes that already contain " +
                "AudioManager + FMODEvents. See the setup notes at the top of AudioBootstrap.cs.");
            return;
        }

        var go = Object.Instantiate(prefab);
        go.name = prefab.name;            // drop the "(Clone)" suffix
        Object.DontDestroyOnLoad(go);     // mark the ROOT so children persist too
    }
}
