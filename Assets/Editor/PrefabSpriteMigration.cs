#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// =============================================================================
//  PREFAB SPRITE MIGRATION      Tools ▸ Enemies ▸ 2b. Migrate PREFAB sprite paths
// -----------------------------------------------------------------------------
//  Automates what was previously a manual drag-and-drop chore.
//
//  Some enemies keep their sprite folder paths on the COMPONENT rather than on an
//  EnemyData asset — EliteInsectVisuals and InsectAnimator have four burrow folders
//  each, Boss1 has separate laser and head paths. EnemySpriteMigration cannot see
//  those, because it only walks EnemyData assets.
//
//  But the paths are right there in the prefab. This reads them and fills the
//  matching Sprite[] / Sprite fields, so nothing has to be dragged by hand.
//
//  PAIRING RULE: a serialized string ending in "Folder" or "Path" is matched to a
//  sprite field with the same base name:
//      diveFolder       →  diveFrames
//      laserSpritePath  →  laserSpriteFrames
//      headSpritePath   →  headSpriteAsset      (single Sprite, not an array)
//  Anything hardcoded as a const (Gremlin, Scarecrow) is handled by the explicit
//  table below, since a const is not serialized and cannot be discovered.
//
//  DELETE THIS FILE once the migration is done.
// =============================================================================
public static class PrefabSpriteMigration
{
    private const string ResourcesRoot = "Assets/Resources";

    // Hardcoded paths that are NOT serialized, so no rule can find them.
    //   componentTypeName , spriteFieldName , resourcesPath
    private static readonly (string type, string field, string path)[] HardcodedSources =
    {
        ("GremlinController", "gremlinSpriteFrames", "Sprites/EnemySprites/Gremlin"),
        ("Scarecrow",         "fallbackSprite",      "Sprites/EnemySprites/Scarecrow/00"),

        // The Gremlin has NO prefab asset (GremlinSpawner builds it with
        // new GameObject + AddComponent), so the references live on the spawner.
        ("GremlinSpawner",    "gremlinSpriteFrames", "Sprites/EnemySprites/Gremlin"),

        // InsectAnimator is added AT RUNTIME by InsectController, so its four burrow
        // folders are field initializers with no serialized data on Insect.prefab.
        // The arrays therefore live on InsectController, which IS on the prefab.
        ("InsectController",  "diveFrames",          "Sprites/EnemySprites/Insect/FromAboveToUnder"),
        ("InsectController",  "undergroundFrames",   "Sprites/EnemySprites/Insect/MovingUnderground"),
        ("InsectController",  "emergeFrames",        "Sprites/EnemySprites/Insect/FromUnderToAbove"),
        ("InsectController",  "attackFrames",        "Sprites/EnemySprites/Insect/Attacking"),
    };

    private static readonly string[] SuffixesToStrip =
        { "SpriteFolderPath", "SpritePath", "FolderPath", "Folder", "Path" };

    private static readonly string[] TargetSuffixes =
        { "Frames", "SpriteFrames", "Sprites", "SpriteAsset", "Asset", "Sprite" };

    // ───────────────────────────────────────────────────────── DRY RUN ──

    [MenuItem("Tools/Enemies/2a. PREVIEW prefab sprite migration (no changes)")]
    public static void Preview() => Run(dryRun: true);

    [MenuItem("Tools/Enemies/2b. Migrate PREFAB sprite paths → arrays")]
    public static void Migrate()
    {
        if (!EditorUtility.DisplayDialog("Migrate prefab sprite references?",
                "Reads the legacy folder-path fields on every enemy prefab and fills the " +
                "matching Sprite[] fields automatically.\n\n" +
                "The PNGs must STILL be under Resources/ right now.\n\n" +
                "Only EMPTY sprite fields are touched — anything you already assigned by " +
                "hand is left alone. Legacy paths are preserved as fallbacks.\n\n" +
                "Run 2a. PREVIEW first if you want to see what it would do.",
                "Migrate", "Cancel"))
            return;

        Run(dryRun: false);
    }

    private static void Run(bool dryRun)
    {
        var guids = AssetDatabase.FindAssets("t:Prefab");
        int prefabsChanged = 0, fieldsFilled = 0, failures = 0;

        for (int g = 0; g < guids.Length; g++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[g]);
            EditorUtility.DisplayProgressBar(dryRun ? "Previewing" : "Migrating prefabs",
                                             Path.GetFileName(path), (float)g / guids.Length);

            GameObject root = null;
            try
            {
                root = dryRun
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(path)
                    : PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                var report = new List<string>();
                bool dirty = false;

                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    if (ProcessComponent(mb, dryRun, report, ref fieldsFilled)) dirty = true;
                }

                if (report.Count > 0)
                {
                    prefabsChanged++;
                    Debug.Log($"[PrefabMigrate] {(dryRun ? "WOULD FILL" : "FILLED")} — {path}\n" +
                              string.Join("\n", report));
                }

                if (dirty && !dryRun) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            catch (System.Exception e)
            {
                failures++;
                Debug.LogError($"[PrefabMigrate] FAILED on {path}: {e.Message}");
            }
            finally
            {
                if (!dryRun && root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        EditorUtility.ClearProgressBar();
        if (!dryRun) AssetDatabase.SaveAssets();

        Debug.Log($"[PrefabMigrate] ══ {(dryRun ? "PREVIEW" : "DONE")}: " +
                  $"{fieldsFilled} field(s) across {prefabsChanged} prefab(s), {failures} failure(s) ══" +
                  (dryRun ? "\n    Nothing was changed. Run 2b to apply."
                          : "\n    Now run 0b to confirm nothing is left unassigned."));
    }

    // ─────────────────────────────────────────────────── per component ──

    private static bool ProcessComponent(MonoBehaviour mb, bool dryRun,
                                         List<string> report, ref int filled)
    {
        var so = new SerializedObject(mb);
        string typeName = mb.GetType().Name;
        bool dirty = false;

        // 1. Discoverable pairs: a serialized path string + a same-named sprite field.
        var pathFields = new List<(string name, string value)>();
        var it = so.GetIterator();
        while (it.NextVisible(true))
            if (it.propertyType == SerializedPropertyType.String &&
                !string.IsNullOrEmpty(it.stringValue) &&
                it.stringValue.Contains("Sprites/"))
                pathFields.Add((it.name, it.stringValue));

        foreach (var (fieldName, resourcePath) in pathFields)
        {
            var target = FindSpriteTarget(so, fieldName);
            if (target == null) continue;
            if (TryFill(so, target, resourcePath, typeName, report, ref filled)) dirty = true;
        }

        // 2. Hardcoded consts — invisible to the rule above.
        foreach (var (t, field, resourcePath) in HardcodedSources)
        {
            if (typeName != t) continue;
            if (ShouldSkipHardcoded(mb, t, field, report)) continue;

            var target = so.FindProperty(field);
            if (target == null) continue;
            if (TryFill(so, target, resourcePath, typeName, report, ref filled)) dirty = true;
        }

        if (dirty && !dryRun) so.ApplyModifiedPropertiesWithoutUndo();
        return dirty;
    }

    /// Guards for the hardcoded table, which matches on COMPONENT TYPE alone and so can
    /// feed the wrong art to a prefab that happens to share a component.
    ///
    /// This is not hypothetical — it already happened. EliteInsect.prefab carries an
    /// InsectController as well as EliteInsectVisuals, so the unconditional
    /// InsectController → "Sprites/EnemySprites/Insect/..." mapping filled the Elite
    /// Insect with the REGULAR Insect's frames. Both components then injected into the
    /// shared InsectAnimator and whichever ran last won, so the Elite would have
    /// animated as a plain Insect. Worse, 0b reported "clean" throughout, because it
    /// only checks WHETHER an array is filled, never with WHAT.
    private static bool ShouldSkipHardcoded(MonoBehaviour mb, string type, string field,
                                            List<string> report)
    {
        if (type == "InsectController" &&
            mb.GetComponentInParent<EliteInsectVisuals>(true) != null)
        {
            report.Add($"    – skipped InsectController.{field}: EliteInsectVisuals owns this " +
                       "prefab's burrow art and passes it to InsectAnimator itself");
            return true;
        }
        return false;
    }

    /// Given "diveFolder", look for "diveFrames" / "diveSprites" / "diveSprite"…
    private static SerializedProperty FindSpriteTarget(SerializedObject so, string pathFieldName)
    {
        string baseName = pathFieldName;
        foreach (var suffix in SuffixesToStrip)
            if (baseName.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
            { baseName = baseName.Substring(0, baseName.Length - suffix.Length); break; }

        if (string.IsNullOrEmpty(baseName)) return null;

        foreach (var suffix in TargetSuffixes)
        {
            var p = so.FindProperty(baseName + suffix);
            if (p != null && IsSpriteTarget(p)) return p;

            // Also try a capitalised base ("Dive" + "Frames").
            string cap = char.ToUpperInvariant(baseName[0]) + baseName.Substring(1);
            p = so.FindProperty(cap + suffix);
            if (p != null && IsSpriteTarget(p)) return p;
        }
        return null;
    }

    private static bool IsSpriteTarget(SerializedProperty p)
        => (p.isArray && p.arrayElementType == "PPtr<$Sprite>")
        || (p.propertyType == SerializedPropertyType.ObjectReference &&
            (p.objectReferenceValue is Sprite || p.objectReferenceValue == null));

    private static bool TryFill(SerializedObject so, SerializedProperty target, string resourcePath,
                                string typeName, List<string> report, ref int filled)
    {
        // Never overwrite work already done by hand.
        if (target.isArray && target.arraySize > 0) return false;
        if (!target.isArray && target.objectReferenceValue != null) return false;

        if (target.isArray)
        {
            var sprites = LoadFolderSorted(resourcePath);
            if (sprites.Length == 0)
            {
                report.Add($"    ✗ {typeName}.{target.name}: no sprites at Resources/{resourcePath}");
                return false;
            }

            target.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                target.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];

            report.Add($"    ✓ {typeName}.{target.name} ← {sprites.Length} frame(s) from {resourcePath}");
            filled++;
            return true;
        }

        var single = LoadSingle(resourcePath);
        if (single == null)
        {
            report.Add($"    ✗ {typeName}.{target.name}: no sprite at Resources/{resourcePath}");
            return false;
        }

        target.objectReferenceValue = single;
        report.Add($"    ✓ {typeName}.{target.name} ← {single.name} from {resourcePath}");
        filled++;
        return true;
    }

    // ────────────────────────────────────────────────────────── loading ──

    /// Ordinal sort, matching Resources.LoadAll + Array.Sort exactly. A different
    /// comparison would silently reorder animation frames.
    private static Sprite[] LoadFolderSorted(string resourcesRelative)
    {
        string folder = $"{ResourcesRoot}/{resourcesRelative}";
        if (!AssetDatabase.IsValidFolder(folder)) return System.Array.Empty<Sprite>();

        var sprites = new List<Sprite>(64);
        foreach (var file in Directory.GetFiles(folder))
        {
            if (file.EndsWith(".meta")) continue;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(file.Replace('\\', '/')))
                if (o is Sprite s) sprites.Add(s);
        }

        sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return sprites.ToArray();
    }

    private static Sprite LoadSingle(string resourcesRelative)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".psd", ".tga", ".asset" })
        {
            string p = $"{ResourcesRoot}/{resourcesRelative}{ext}";
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            if (s != null) return s;

            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                if (o is Sprite sub) return sub;
        }
        return null;
    }
}
#endif





