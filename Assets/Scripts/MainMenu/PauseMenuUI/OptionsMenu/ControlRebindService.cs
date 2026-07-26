using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Persistence + propagation for control rebinding.
// PlayerInputManager gives EACH player its own clone of the InputActionAsset, so a
// binding override applied to one player's instance is invisible to the other and is
// lost on scene reload. This service treats the override set as shared data living
// OUTSIDE the asset instances:
//   persisted as JSON in PlayerPrefs (survives reloads and app restarts), and
//   applied to every live PlayerInput, plus every player that spawns later.
//
// It also owns ACTION ALIASES — see MirrorAliases below.

public static class ControlRebindService
{
    // v2: the defaults changed (Sprint moved off leftShift/leftShoulder, which it
    // shared with Dash; Previous/Next became PreviousWeapon/NextWeapon and lost their
    // keyboard 1/2 bindings to the new Hotbar actions). A v1 save could re-apply an
    // override that reintroduces one of those clashes, so old saves are dropped.
    private const string PrefKey = "controls.bindingOverrides.v2";
    private const string LegacyPrefKey = "controls.bindingOverrides.v1";

    private const string MapName = "Player";

    // Schemes the rebind UI exposes. Alias mirroring is done per-scheme because a
    // binding's `groups` string is not written consistently in the asset (";Gamepad"
    // vs "Gamepad"), so the scheme NAME is the only reliable key.
    public static readonly string[] Schemes = { "Keyboard&Mouse", "Gamepad" };

    // ALIASES: actions that must always resolve to the same controls as another action.
    //
    // `Build` duplicates `AttackWeapon` exactly (both on rightTrigger + leftButton).
    // That's deliberate — building uses the attack control — but as two independent
    // actions they silently desynced the moment a player rebound one of them: the
    // controls screen offered both, and rebinding "Attack Weapon" left "Build" on the
    // old key. Rather than making the player set the same control twice, Build is
    // hidden from the UI and mirrors AttackWeapon's effective paths here.
    //
    // key = alias action, value = the action it follows.
    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
    {
        { "Build", "AttackWeapon" },
    };

    /// <summary>True if this action is a mirror of another and should not be shown or
    /// edited directly.</summary>
    public static bool IsAlias(string actionName)
        => !string.IsNullOrEmpty(actionName) && Aliases.ContainsKey(actionName);

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

    /// <summary>Read the current overrides off an edited asset and persist+broadcast them.
    /// Aliases are re-mirrored first so the saved set is self-consistent.</summary>
    public static void CaptureFrom(InputActionAsset asset)
    {
        if (asset == null) return;
        MirrorAliases(asset);
        SaveJson(asset.SaveBindingOverridesAsJson());
    }

    /// <summary>Wipe all rebinds (clears the save and restores defaults everywhere).</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        PlayerPrefs.Save();

        foreach (var pi in PlayerInput.all)
        {
            if (pi == null || pi.actions == null) continue;
            pi.actions.RemoveAllBindingOverrides();
            MirrorAliases(pi.actions);   // defaults already agree; keeps it true by construction
        }

        OnRebindsChanged?.Invoke();
    }

    /// <summary>Apply (or clear) the saved overrides on a single asset instance.</summary>
    public static void ApplyTo(InputActionAsset asset)
    {
        if (asset == null) return;
        string json = LoadJson();
        if (string.IsNullOrEmpty(json)) asset.RemoveAllBindingOverrides();
        else asset.LoadBindingOverridesFromJson(json);
        MirrorAliases(asset);
    }

    /// <summary>Re-apply the saved overrides to every player currently in the scene.</summary>
    public static void ApplyToAllLive()
    {
        foreach (var pi in PlayerInput.all)
            if (pi != null)
                ApplyTo(pi.actions);
    }

    //  aliases 

    /// <summary>Force every alias action onto the same effective controls as the action
    /// it follows, matched per scheme and per position within that scheme.</summary>
    public static void MirrorAliases(InputActionAsset asset)
    {
        if (asset == null) return;
        var map = asset.FindActionMap(MapName, false);
        if (map == null) return;

        foreach (var pair in Aliases)
        {
            var alias = map.FindAction(pair.Key, false);
            var source = map.FindAction(pair.Value, false);
            if (alias == null || source == null) continue;

            var ordinal = new Dictionary<string, int>();

            for (int i = 0; i < alias.bindings.Count; i++)
            {
                var b = alias.bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;

                string scheme = SchemeOf(b.groups);
                if (scheme == null) continue;   // XR/Touch/Joystick — not editable, leave alone

                int n = ordinal.TryGetValue(scheme, out var c) ? c : 0;
                ordinal[scheme] = n + 1;

                string path = NthEffectivePathInScheme(source, scheme, n);
                if (path == null) continue;     // source has fewer bindings in this scheme

                if (b.effectivePath != path)
                    alias.ApplyBindingOverride(i, path);
            }
        }
    }

    /// <summary>The effective path of the n-th (0-based) simple binding of `action` that
    /// belongs to `scheme`, or null if there aren't that many.</summary>
    private static string NthEffectivePathInScheme(InputAction action, string scheme, int n)
    {
        int seen = 0;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var b = action.bindings[i];
            if (b.isComposite || b.isPartOfComposite) continue;
            if (SchemeOf(b.groups) != scheme) continue;
            if (seen++ == n) return b.effectivePath;
        }
        return null;
    }

    /// <summary>First known scheme named in a binding's groups string, or null.
    /// Handles the asset's inconsistent ";Gamepad" / "Gamepad" formatting.</summary>
    public static string SchemeOf(string groups)
    {
        if (string.IsNullOrEmpty(groups)) return null;
        // Longest first, so "Keyboard&Mouse" is never shadowed by a shorter match.
        for (int i = 0; i < Schemes.Length; i++)
            if (groups.Contains(Schemes[i])) return Schemes[i];
        return null;
    }

    //  bootstrap 

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DropLegacySave()
    {
        // v1 overrides referenced the old default layout (Sprint on leftShift, etc.).
        // Re-applying them on top of the new asset can only recreate the clashes the
        // new defaults were written to remove, so they're discarded once.
        if (PlayerPrefs.HasKey(LegacyPrefKey))
        {
            PlayerPrefs.DeleteKey(LegacyPrefKey);
            PlayerPrefs.Save();
            Debug.Log("[ControlRebindService] Discarded v1 control overrides — the default " +
                      "bindings changed. Controls are back on defaults and can be reset in Options.");
        }
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

