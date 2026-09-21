#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// =============================================================================
//  TOWER + BIOME SPRITE MIGRATION      Tools ▸ Art ▸ …
// -----------------------------------------------------------------------------
//  Same job as the enemy migration, for the two remaining subsystems worth doing:
//
//    Tower.spriteResourcePath            → Tower.spriteFrames
//    HammerTowerAnimator.framesResource… → HammerTowerAnimator.frameSprites
//    BiomeManager.GetBackgroundPath()    → the ten Texture2D slots
//
//  Augment icons are deliberately NOT covered. That system is CSV-driven and looks
//  assets up BY NAME at runtime, which is the one case where Resources is genuinely
//  the right tool — converting it would just be Resources with extra steps.
//
//  ORDER MATTERS: run this while the art is STILL under Resources/. Move the folders
//  only after the audit reports everything assigned.
//
//  DELETE THIS FILE when the migration is done.
// =============================================================================
public static class TowerBiomeSpriteMigration
{
    private const string ResourcesRoot = "Assets/Resources";

    // ─────────────────────────────────────────────────────────────── TOWERS ──

    [MenuItem("Tools/Art/1. PREVIEW tower sprite migration")]
    public static void PreviewTowers() => RunTowers(dryRun: true);

    [MenuItem("Tools/Art/2. Migrate tower sprite paths → arrays")]
    public static void MigrateTowers()
    {
        if (!EditorUtility.DisplayDialog("Migrate tower sprites?",
                "Fills Tower.spriteFrames and HammerTowerAnimator.frameSprites from their " +
                "legacy Resources folder paths.\n\n" +
                "The PNGs must STILL be under Resources/.\n\n" +
                "Only EMPTY arrays are touched; legacy paths are preserved as fallbacks.",
                "Migrate", "Cancel"))
            return;
        RunTowers(dryRun: false);
    }

    private static void RunTowers(bool dryRun)
    {
        var guids = AssetDatabase.FindAssets("t:Prefab");
        int prefabs = 0, fields = 0;

        for (int g = 0; g < guids.Length; g++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[g]);
            EditorUtility.DisplayProgressBar(dryRun ? "Previewing towers" : "Migrating towers",
                                             Path.GetFileName(path), (float)g / guids.Length);

            GameObject root = null;
            try
            {
                root = dryRun ? AssetDatabase.LoadAssetAtPath<GameObject>(path)
                              : PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                var report = new List<string>();
                bool dirty = false;

                foreach (var t in root.GetComponentsInChildren<Tower>(true))
                {
                    if (t == null || t.HasDirectFrames) continue;
                    var frames = LoadFolderOrdered(t.spriteResourcePath);
                    if (frames.Length == 0)
                    {
                        report.Add($"    ✗ Tower.spriteFrames: nothing at Resources/{t.spriteResourcePath}");
                        continue;
                    }
                    if (!dryRun) { t.spriteFrames = frames; EditorUtility.SetDirty(t); }
                    report.Add($"    ✓ Tower.spriteFrames ← {frames.Length} frame(s) from {t.spriteResourcePath}");
                    fields++; dirty = true;
                }

                foreach (var h in root.GetComponentsInChildren<HammerTowerAnimator>(true))
                {
                    if (h == null || h.HasDirectFrames) continue;
                    var frames = LoadFolderOrdered(h.framesResourceFolder);
                    if (frames.Length == 0)
                    {
                        report.Add($"    ✗ HammerTowerAnimator.frameSprites: nothing at Resources/{h.framesResourceFolder}");
                        continue;
                    }
                    if (!dryRun) { h.frameSprites = frames; EditorUtility.SetDirty(h); }
                    report.Add($"    ✓ HammerTowerAnimator.frameSprites ← {frames.Length} frame(s) from {h.framesResourceFolder}");
                    fields++; dirty = true;
                }

                if (report.Count > 0)
                {
                    prefabs++;
                    Debug.Log($"[TowerMigrate] {(dryRun ? "WOULD FILL" : "FILLED")} — {path}\n" +
                              string.Join("\n", report));
                }

                if (dirty && !dryRun) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            catch (System.Exception e) { Debug.LogError($"[TowerMigrate] FAILED on {path}: {e.Message}"); }
            finally { if (!dryRun && root != null) PrefabUtility.UnloadPrefabContents(root); }
        }

        EditorUtility.ClearProgressBar();
        if (!dryRun) AssetDatabase.SaveAssets();
        Debug.Log($"[TowerMigrate] ══ {(dryRun ? "PREVIEW" : "DONE")}: {fields} field(s) across {prefabs} prefab(s) ══");
    }

    // ─────────────────────────────────────────────────────────────── BIOMES ──

    // Mirrors BiomeManager.GetBackgroundPath(). Kept here rather than read via
    // reflection so a mismatch is visible in one place.
    private static readonly (string field, string path)[] BiomeBackgrounds =
    {
        ("grassBackground",        "Backgrounds/Background8"),
        ("snowBackground",         "Backgrounds/BackgroundSnow"),
        ("desertBackground",       "Backgrounds/BackgroundDesert"),
        ("wastelandBackground",    "Backgrounds/Background11"),
        ("stonesBackground",       "Backgrounds/Background12"),
        ("grassCartoonBackground", "Backgrounds/Background13"),
        ("marshBackground",        "Backgrounds/Background8"),
        ("nightBackground",        "Backgrounds/Background13"),
        ("corruptionBackground",   "Backgrounds/Background13"),
        ("pitchBlackBackground",   "Backgrounds/Background13"),
    };

    [MenuItem("Tools/Art/3. Migrate biome backgrounds (scene BiomeManager)")]
    public static void MigrateBiomes()
    {
        var bm = Object.FindFirstObjectByType<BiomeManager>(FindObjectsInactive.Include);
        if (bm == null)
        {
            Debug.LogError("[BiomeMigrate] No BiomeManager in the open scene. Open your gameplay " +
                           "scene first — BiomeManager is a scene object, not a prefab.");
            return;
        }

        var so = new SerializedObject(bm);
        int filled = 0, missing = 0;

        foreach (var (field, path) in BiomeBackgrounds)
        {
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[BiomeMigrate] No field '{field}' on BiomeManager."); continue; }
            if (prop.objectReferenceValue != null) continue;   // already assigned by hand

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ResourcesRoot}/{path}.png")
                   ?? AssetDatabase.LoadAssetAtPath<Texture2D>($"{ResourcesRoot}/{path}.jpg");

            if (tex == null)
            {
                Debug.LogWarning($"[BiomeMigrate] ✗ {field}: nothing at Resources/{path}");
                missing++;
                continue;
            }

            prop.objectReferenceValue = tex;
            Debug.Log($"[BiomeMigrate] ✓ {field} ← {tex.name} ({tex.width}x{tex.height})");
            filled++;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(bm);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);

        Debug.Log($"[BiomeMigrate] ══ {filled} assigned, {missing} missing ══\n" +
                  "    SAVE THE SCENE (Ctrl+S) — scene edits are not saved by the asset database.");
    }

    // ──────────────────────────────────────────────────────────────── AUDIT ──

    // ──────────────────────────────────────────────────────────── CENTRAL CORE ──

    [MenuItem("Tools/Art/4. Migrate Central Core frames (scene TowerDefenseMap)")]
    public static void MigrateCore()
    {
        var map = Object.FindFirstObjectByType<TowerDefenseMap>(FindObjectsInactive.Include);
        if (map == null)
        {
            Debug.LogError("[CoreMigrate] No TowerDefenseMap in the open scene. Open your gameplay " +
                           "scene — the Core is built at runtime, so its frames live on the map.");
            return;
        }

        if (map.coreSpriteFrames != null && map.coreSpriteFrames.Length > 0)
        {
            Debug.Log($"[CoreMigrate] Already assigned ({map.coreSpriteFrames.Length} frames) — skipping.");
            return;
        }

        // Numbered frames ONLY. The old 'central_core_sprite' sheet lives in the same
        // folder and is exactly what CentralCore.LoadCoreSprites has to filter out at
        // runtime; excluding it here means nothing references it and it stops shipping.
        // (It is the 2048x2048 / 32 MB asset from the memory snapshot.)
        var all = LoadFolderOrdered("Sprites/Buildings/Towers/Core");
        var numbered = all.Where(s => char.IsDigit(s.name.TrimStart()[0])).ToArray();
        int skipped = all.Length - numbered.Length;

        if (numbered.Length == 0)
        {
            Debug.LogError("[CoreMigrate] No numbered frames found in " +
                           "Resources/Sprites/Buildings/Towers/Core.");
            return;
        }

        var so = new SerializedObject(map);
        var prop = so.FindProperty("coreSpriteFrames");
        prop.arraySize = numbered.Length;
        for (int i = 0; i < numbered.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = numbered[i];
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(map);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(map.gameObject.scene);

        Debug.Log($"[CoreMigrate] ✓ coreSpriteFrames ← {numbered.Length} frame(s) " +
                  $"({numbered[0].name} … {numbered[numbered.Length - 1].name})" +
                  (skipped > 0 ? $", skipped {skipped} non-numbered sprite(s) — that is the old sheet, " +
                                  "and excluding it is intentional." : "") +
                  "\n    SAVE THE SCENE (Ctrl+S).");
    }

    [MenuItem("Tools/Art/0. Audit tower + biome sprite wiring")]
    public static void Audit()
    {
        int towersOk = 0, towersPending = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go == null) continue;
            foreach (var t in go.GetComponentsInChildren<Tower>(true))
            {
                if (t.HasDirectFrames) towersOk++;
                else { towersPending++; Debug.LogWarning($"[Audit] Tower '{t.name}' in {go.name}: no direct frames (path '{t.spriteResourcePath}')"); }
            }
            foreach (var h in go.GetComponentsInChildren<HammerTowerAnimator>(true))
            {
                if (h.HasDirectFrames) towersOk++;
                else { towersPending++; Debug.LogWarning($"[Audit] HammerTowerAnimator in {go.name}: no direct frames"); }
            }
        }

        var bm = Object.FindFirstObjectByType<BiomeManager>(FindObjectsInactive.Include);
        int bgOk = 0, bgPending = 0;
        if (bm != null)
        {
            var so = new SerializedObject(bm);
            foreach (var (field, path) in BiomeBackgrounds)
            {
                var p = so.FindProperty(field);
                if (p != null && p.objectReferenceValue != null) bgOk++;
                else { bgPending++; Debug.LogWarning($"[Audit] BiomeManager.{field} unassigned (would fall back to Resources/{path})"); }
            }
        }
        else Debug.LogWarning("[Audit] No BiomeManager in the open scene — open your gameplay scene to audit backgrounds.");

        var tdm = Object.FindFirstObjectByType<TowerDefenseMap>(FindObjectsInactive.Include);
        int coreFrames = tdm != null && tdm.coreSpriteFrames != null ? tdm.coreSpriteFrames.Length : 0;
        if (coreFrames == 0)
            Debug.LogWarning("[Audit] TowerDefenseMap.coreSpriteFrames unassigned — the Central Core " +
                             "still falls back to Resources/Sprites/Buildings/Towers/Core.");

        Debug.Log($"[Audit] ══ Towers: {towersOk} migrated / {towersPending} pending · " +
                  $"Backgrounds: {bgOk} assigned / {bgPending} pending · " +
                  $"Core frames: {coreFrames} ══" +
                  (towersPending == 0 && bgPending == 0
                     ? "\n    All clear — safe to move the art out of Resources."
                     : "\n    Do NOT move the art yet."));
    }

    // ─────────────────────────────────────────────────────────────── helpers ──

    /// Ordered by the LEADING INTEGER in each sprite name, matching
    /// Tower.SpriteFrameCache's OrderBy. Plain ordinal sort would put "10" before "2".
    private static Sprite[] LoadFolderOrdered(string resourcesRelative)
    {
        if (string.IsNullOrEmpty(resourcesRelative)) return System.Array.Empty<Sprite>();

        string folder = $"{ResourcesRoot}/{resourcesRelative}";
        var sprites = new List<Sprite>(64);

        if (AssetDatabase.IsValidFolder(folder))
        {
            foreach (var file in Directory.GetFiles(folder))
            {
                if (file.EndsWith(".meta")) continue;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(file.Replace('\\', '/')))
                    if (o is Sprite s) sprites.Add(s);
            }
        }
        else
        {
            // The path may point at a single sliced spritesheet rather than a folder.
            foreach (var ext in new[] { ".png", ".jpg", ".psd" })
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(folder + ext))
                    if (o is Sprite s) sprites.Add(s);
        }

        return sprites.OrderBy(LeadingInt).ThenBy(s => s.name, System.StringComparer.Ordinal).ToArray();
    }

    private static int LeadingInt(Sprite s)
    {
        int i = 0, n = 0; bool any = false;
        while (i < s.name.Length && !char.IsDigit(s.name[i])) i++;
        while (i < s.name.Length && char.IsDigit(s.name[i])) { n = n * 10 + (s.name[i] - '0'); i++; any = true; }
        return any ? n : int.MaxValue;
    }
}
#endif


