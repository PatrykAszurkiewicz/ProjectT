using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Persistence + propagation for control rebinding.
// PlayerInputManager gives EACH player its own clone of
// the InputActionAsset, so a binding override applied to one player's instance is
// invisible to the other and is lost on scene reload. This service treats the
// override set as shared data living OUTSIDE the asset instances:
//   persisted as JSON in PlayerPrefs (survives reloads and app restarts), and
//   applied to every live PlayerInput, plus every player that spawns later.

public static class ControlRebindService
{
    private const string PrefKey = "controls.bindingOverrides.v1";

    /// <summary>Raised whenever the saved overrides change (the UI listens to refresh).</summary>
    public static event Action OnRebindsChanged;

    //  persistence 

    /// <summary>The saved override JSON, or "" if the player is on defaults.</summary>
    public static string LoadJson() => PlayerPrefs.GetString(PrefKey, string.Empty);

    public static bool HasOverrides => !string.IsNullOrEmpty(LoadJson());

    /// <summary>Persist the override JSON taken from an edited asset, then push it live.</summary>
    public static void SaveJson(string json)
    {
        if (string.IsNullOrEmpty(json)) PlayerPrefs.DeleteKey(PrefKey);
        else PlayerPrefs.SetString(PrefKey, json);
        PlayerPrefs.Save();

        ApplyToAllLive();
        OnRebindsChanged?.Invoke();
    }

    ///Read the current overrides off an edited asset and persist+broadcast them.
    public static void CaptureFrom(InputActionAsset asset)
    {
        if (asset == null) return;
        SaveJson(asset.SaveBindingOverridesAsJson());
    }

    /// Wipe all rebinds (clears the save and restores defaults everywhere).
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        PlayerPrefs.Save();

        foreach (var pi in PlayerInput.all)
            if (pi != null && pi.actions != null)
                pi.actions.RemoveAllBindingOverrides();

        OnRebindsChanged?.Invoke();
    }


    /// Apply (or clear) the saved overrides on a single asset instance.
    public static void ApplyTo(InputActionAsset asset)
    {
        if (asset == null) return;
        string json = LoadJson();
        if (string.IsNullOrEmpty(json)) asset.RemoveAllBindingOverrides();
        else asset.LoadBindingOverridesFromJson(json);
    }

    /// Re-apply the saved overrides to every player currently in the scene.
    public static void ApplyToAllLive()
    {
        foreach (var pi in PlayerInput.all)
            if (pi != null)
                ApplyTo(pi.actions);
    }


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        // Catch every future join (co-op drop-in, respawn, next-scene spawn)…
        PlayerRegistry.OnPlayerJoined -= OnPlayerJoined; // guard against double-subscribe
        PlayerRegistry.OnPlayerJoined += OnPlayerJoined;

        // …and players that already registered before this hook ran (e.g. players
        // placed directly in the scene, which register during scene load).
        ApplyToAllLive();
    }

    private static void OnPlayerJoined(PlayerRef pr)
    {
        if (pr == null) return;
        var pi = pr.GetComponent<PlayerInput>();
        if (pi != null) ApplyTo(pi.actions);
    }
}
