#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// =============================================================================
//  THROWAWAY MIGRATION TOOL       Tools ▸ Enemies ▸ Migrate Sprite Folders → Arrays
// -----------------------------------------------------------------------------
//  Populates EnemyData.frames / idleFrames / attackFrames / deathFrames from the
//  legacy Resources folder paths, so the PNGs can leave Resources/.
//
//  DELETE THIS FILE once every asset reports migrated. It is a one-time tool, not
//  part of the pipeline.
//
//  ORDER OF OPERATIONS — GET THIS WRONG AND YOU LOSE THE REFERENCES:
//    1. Run AUDIT. Confirm every EnemyData resolves its folders.
//    2. Run MIGRATE. Arrays are filled while the PNGs are STILL in Resources/.
//    3. Run AUDIT again. Every asset must say MIGRATED with a sane frame count.
//    4. ONLY THEN move the PNGs to Assets/Art/EnemySprites/ (drag in the Project
//       window — never in Explorer/Finder, or the .meta GUIDs break and every
//       reference you just created goes null).
//    5. Play. Watch for "still loads sprites from Resources" warnings.
//    6. Clear the legacy paths (CLEAR LEGACY PATHS), delete this file.
// =============================================================================
public static class EnemySpriteMigration
{
    // Where the PNGs will live AFTER the move. Used only by the audit, to report
    // whether a folder has already been relocated.
    private const string NewArtRoot = "Assets/Art/EnemySprites";

    // Resources roots to resolve legacy paths against.
    private static readonly string[] ResourcesRoots =
    {
        "Assets/Resources",
    };

    // ───────────────────────────────────────────────────────────────── AUDIT ──

    [MenuItem("Tools/Enemies/1. Audit EnemyData sprite wiring")]
    public static void Audit()
    {
        var assets = LoadAllEnemyData();
        if (assets.Count == 0) { Debug.LogWarning("[Migrate] No EnemyData assets found."); return; }

        int migrated = 0, pending = 0, broken = 0;

        foreach (var d in assets)
        {
            string path = AssetDatabase.GetAssetPath(d);

            if (d.HasDirectFrames)
            {
                migrated++;
                int n = (d.frames?.Length ?? 0);
                int i = (d.idleFrames?.Length ?? 0), a = (d.attackFrames?.Length ?? 0), x = (d.deathFrames?.Length ?? 0);

                var nulls = CountNulls(d);
                if (nulls > 0)
                {
                    broken++;
                    Debug.LogError($"[Migrate] ✗ {d.name}: MIGRATED but has {nulls} NULL frame(s). " +
                                   "A hole shifts every later index and desyncs hitFrame. Re-run migrate.\n    {path}");
                }
                else
                {
                    Debug.Log($"[Migrate] ✓ {d.name}: MIGRATED " +
                              (d.useAnimationFolders ? $"(idle {i}, attack {a}, death {x})" : $"({n} frames)"));
                }
                continue;
            }

            // Not migrated — can we still resolve the legacy folders?
            var missing = new List<string>();
            if (d.useAnimationFolders)
            {
                if (!ResolveFolder(d.idleFolderPath, out _)) missing.Add($"idle '{d.idleFolderPath}'");
                if (!ResolveFolder(d.attackFolderPath, out _)) missing.Add($"attack '{d.attackFolderPath}'");
                if (!string.IsNullOrEmpty(d.deathFolderPath) && !ResolveFolder(d.deathFolderPath, out _))
                    missing.Add($"death '{d.deathFolderPath}'");
            }
            else
            {
                if (!ResolveFolder(d.spriteFolderPath, out _)) missing.Add($"sprite '{d.spriteFolderPath}'");
            }

            if (missing.Count > 0)
            {
                broken++;
                Debug.LogError($"[Migrate] ✗ {d.name}: NOT migrated and these folders do not resolve: " +
                               $"{string.Join(", ", missing)}\n    {path}\n" +
                               "    If the PNGs were already moved out of Resources, move them BACK, " +
                               "migrate, then move them again.");
            }
            else
            {
                pending++;
                Debug.LogWarning($"[Migrate] … {d.name}: not migrated yet (folders resolve OK)");
            }
        }

        Debug.Log($"[Migrate] ══ {assets.Count} EnemyData: {migrated} migrated, {pending} pending, {broken} BROKEN ══" +
                  (broken == 0 && pending == 0
                     ? "\n    All clear. Safe to move the PNGs to " + NewArtRoot
                     : "\n    Do NOT move the PNGs yet."));
    }

    // ─────────────────────────────────────────────────────────────── MIGRATE ──

    [MenuItem("Tools/Enemies/2. Migrate Sprite Folders → Arrays")]
    public static void Migrate()
    {
        var assets = LoadAllEnemyData();
        if (assets.Count == 0) { Debug.LogWarning("[Migrate] No EnemyData assets found."); return; }

        if (!EditorUtility.DisplayDialog("Migrate EnemyData sprite arrays?",
                $"{assets.Count} EnemyData asset(s) will have their frame arrays filled from the " +
                "legacy Resources folder paths.\n\n" +
                "The PNGs must STILL be under Resources/ right now. Nothing is moved or deleted; " +
                "only the ScriptableObjects are edited.\n\n" +
                "Existing non-empty arrays are overwritten.",
                "Migrate", "Cancel"))
            return;

        int ok = 0, failed = 0, totalFrames = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < assets.Count; i++)
            {
                var d = assets[i];
                EditorUtility.DisplayProgressBar("Migrating EnemyData", d.name, (float)i / assets.Count);

                bool good = true;
                int n = 0;

                if (d.useAnimationFolders)
                {
                    d.idleFrames = LoadFolderSorted(d.idleFolderPath, out bool i1);
                    d.attackFrames = LoadFolderSorted(d.attackFolderPath, out bool i2);
                    d.deathFrames = LoadFolderSorted(d.deathFolderPath, out _);   // optional
                    good = i1 && i2;
                    n = d.idleFrames.Length + d.attackFrames.Length + d.deathFrames.Length;
                }
                else
                {
                    d.frames = LoadFolderSorted(d.spriteFolderPath, out good);
                    n = d.frames.Length;
                }

                if (good && n > 0)
                {
                    EditorUtility.SetDirty(d);
                    ok++; totalFrames += n;
                    Debug.Log($"[Migrate] ✓ {d.name}: {n} frame(s)");
                }
                else
                {
                    failed++;
                    Debug.LogError($"[Migrate] ✗ {d.name}: resolved 0 frames — left unmigrated. " +
                                   $"Check its folder paths.\n    {AssetDatabase.GetAssetPath(d)}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[Migrate] ══ Done: {ok} migrated ({totalFrames} frames total), {failed} failed ══\n" +
                  "    Run the AUDIT again before moving any PNG.");
    }

    // ────────────────────────────────────────────────── CLEAR LEGACY PATHS ──

    [MenuItem("Tools/Enemies/3. Clear legacy Resources paths (after verifying)")]
    public static void ClearLegacyPaths()
    {
        var assets = LoadAllEnemyData();

        // The old guard refused whenever ANY asset was unmigrated. That is too blunt:
        // "unmigrated" is not the same as "at risk". An asset only loses something if
        // it is unmigrated AND its path still resolves to real sprites — i.e. it is
        // genuinely still relying on Resources.
        //
        // In this project five assets fail migration harmlessly:
        //   GoblinsData, OrcsData, SplitterData, VortexData — spriteFolderPath is EMPTY,
        //       so there is nothing to clear and nothing to lose. They get their visuals
        //       from somewhere other than EnemyData.
        //   BerserkData — its path points at Sprites/EnemySprites/Berserk, which does not
        //       exist. BerserkVisual COMPUTES its frames on a background thread and
        //       injects them; the path has been dead for a while. Clearing it is a
        //       cleanup, not a loss.
        var atRisk = new List<EnemyData>();
        var harmless = new List<string>();

        foreach (var d in assets)
        {
            if (d.HasDirectFrames) continue;

            bool anyResolves =
                (!string.IsNullOrEmpty(d.spriteFolderPath) && ResolveFolder(d.spriteFolderPath, out _)) ||
                (!string.IsNullOrEmpty(d.idleFolderPath) && ResolveFolder(d.idleFolderPath, out _)) ||
                (!string.IsNullOrEmpty(d.attackFolderPath) && ResolveFolder(d.attackFolderPath, out _));

            if (anyResolves) atRisk.Add(d);
            else harmless.Add($"{d.name} (path is empty or points nowhere)");
        }

        if (atRisk.Count > 0)
        {
            Debug.LogError($"[Migrate] REFUSING: {atRisk.Count} asset(s) are unmigrated AND their folder " +
                           $"paths still resolve to real sprites — clearing would leave them with no art " +
                           $"at all: {string.Join(", ", atRisk.Select(d => d.name))}\n" +
                           "    Migrate these first (Tools ▸ Enemies ▸ 2), or give them direct references.");
            return;
        }

        if (harmless.Count > 0)
            Debug.LogWarning($"[Migrate] {harmless.Count} unmigrated asset(s) are safe to clear because " +
                             $"they resolve nothing:\n    {string.Join("\n    ", harmless)}");

        int migrated = assets.Count(d => d.HasDirectFrames);
        if (!EditorUtility.DisplayDialog("Clear legacy paths?",
                $"{migrated} of {assets.Count} EnemyData assets are migrated; the remaining " +
                $"{assets.Count - migrated} resolve nothing, so they have nothing to lose.\n\n" +
                "This blanks spriteFolderPath / idle / attack / deathFolderPath on all of them.\n\n" +
                "Only do this AFTER moving the art out of Resources and playing the game with " +
                "no 'still loads sprites from Resources' warnings.",
                "Clear", "Cancel"))
            return;

        foreach (var d in assets)
        {
            d.spriteFolderPath = d.idleFolderPath = d.attackFolderPath = d.deathFolderPath = "";
            EditorUtility.SetDirty(d);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Migrate] Cleared legacy paths on {assets.Count} asset(s). " +
                  "You can now delete EnemySpriteMigration.cs and the fallback branches " +
                  "in EnemyAnimationController.");
    }

    // ───────────────────────────────────────────────────────────── helpers ──

    private static List<EnemyData> LoadAllEnemyData()
        => AssetDatabase.FindAssets("t:EnemyData")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<EnemyData>)
            .Where(d => d != null)
            .OrderBy(d => d.name)
            .ToList();

    /// Resolve a Resources-relative path ("Sprites/EnemySprites/Slime") to a project
    /// folder ("Assets/Resources/Sprites/EnemySprites/Slime").
    private static bool ResolveFolder(string resourcesRelative, out string projectPath)
    {
        projectPath = null;
        if (string.IsNullOrEmpty(resourcesRelative)) return false;

        foreach (var root in ResourcesRoots)
        {
            string candidate = $"{root}/{resourcesRelative}";
            if (AssetDatabase.IsValidFolder(candidate)) { projectPath = candidate; return true; }
        }
        return false;
    }

    /// Load every Sprite in the folder, sorted by name with the SAME comparison the
    /// runtime used (string.CompareOrdinal) so frame order is byte-identical to the
    /// old Resources.LoadAll + Array.Sort behaviour. Getting this wrong would silently
    /// reorder animations.
    private static Sprite[] LoadFolderSorted(string resourcesRelative, out bool resolved)
    {
        resolved = false;
        if (string.IsNullOrEmpty(resourcesRelative)) return System.Array.Empty<Sprite>();
        if (!ResolveFolder(resourcesRelative, out string folder)) return System.Array.Empty<Sprite>();

        var sprites = new List<Sprite>(64);

        // Only this folder, not subfolders — matches Resources.LoadAll's behaviour for
        // a folder path, and keeps multi-folder enemies (Move/, Shoot/) separate.
        foreach (var file in Directory.GetFiles(folder))
        {
            if (file.EndsWith(".meta")) continue;
            string assetPath = file.Replace('\\', '/');

            // A texture can contain MULTIPLE sprites (sliced sheets), so take them all.
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (o is Sprite s) sprites.Add(s);
        }

        sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        resolved = sprites.Count > 0;
        return sprites.ToArray();
    }

    private static int CountNulls(EnemyData d)
    {
        int n = 0;
        n += d.frames?.Count(s => s == null) ?? 0;
        n += d.idleFrames?.Count(s => s == null) ?? 0;
        n += d.attackFrames?.Count(s => s == null) ?? 0;
        n += d.deathFrames?.Count(s => s == null) ?? 0;
        return n;
    }
}
#endif


