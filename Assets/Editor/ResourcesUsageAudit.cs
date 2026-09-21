#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// =============================================================================
//  RESOURCES USAGE AUDIT           Tools ▸ Enemies ▸ 0. Audit Resources.Load calls
// -----------------------------------------------------------------------------
//  Run this FIRST, before migrating anything.
//
//  The EnemyData migration only reaches sprite paths stored on EnemyData assets.
//  Six enemies turned out to load art from somewhere else entirely:
//
//    Boss1 / Boss2   read enemyData.spriteFolderPath DIRECTLY in their death handler
//    GremlinController   a hardcoded const, no EnemyData asset at all
//    InsectAnimator      4 burrow folders serialized on the COMPONENT
//    EliteInsectVisuals  4 more of the same
//    Boss1               separate laser + head sprite paths
//    Scarecrow           a hardcoded fallback frame path
//
//  Every one of those fails SILENTLY when the art leaves Resources/ — no exception,
//  just a missing animation. This tool finds the rest of them before you move a file.
//
//  DELETE THIS FILE when the migration is finished.
// =============================================================================
public static class ResourcesUsageAudit
{
    [MenuItem("Tools/Enemies/0. Audit Resources.Load calls in scripts")]
    public static void AuditScripts()
    {
        string[] files = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);

        var callRx = new Regex(@"Resources\.(Load|LoadAll|LoadAsync)\s*<", RegexOptions.Compiled);
        var litRx = new Regex("\"([^\"]*(?:Sprite|Enemy|Boss|Art)[^\"]*)\"", RegexOptions.Compiled);

        int hits = 0;
        var byFile = new SortedDictionary<string, List<string>>();

        foreach (var file in files)
        {
            // Don't report the audit tools themselves.
            string fname = Path.GetFileName(file);
            if (fname == "ResourcesUsageAudit.cs" || fname == "EnemySpriteMigration.cs") continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!callRx.IsMatch(line)) continue;

                string trimmed = line.Trim();
                if (trimmed.StartsWith("//")) continue;   // commented-out call

                string rel = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                if (!byFile.TryGetValue(rel, out var list)) byFile[rel] = list = new List<string>();

                var lit = litRx.Match(line);
                string note = lit.Success ? $"  ← literal \"{lit.Groups[1].Value}\"" : "";
                list.Add($"    line {i + 1}: {Truncate(trimmed, 100)}{note}");
                hits++;
            }
        }

        if (hits == 0)
        {
            Debug.Log("[ResAudit] No Resources.Load calls found. The migration is complete. ✓");
            return;
        }

        Debug.LogWarning($"[ResAudit] ══ {hits} Resources.Load call(s) across {byFile.Count} file(s) ══\n" +
                         "Every one of these keeps its asset pinned in Resources/. Any that loads enemy\n" +
                         "art must be given a direct reference BEFORE that art is moved, or it breaks\n" +
                         "silently at runtime.");

        foreach (var kv in byFile)
            Debug.LogWarning($"[ResAudit] {kv.Key}\n{string.Join("\n", kv.Value)}");
    }

    [MenuItem("Tools/Enemies/0b. Audit prefabs for unassigned direct sprite refs")]
    public static void AuditPrefabs()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab");
        int flagged = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            var problems = new List<string>();

            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();

                bool hasEmptyArray = false, hasLegacyPath = false;
                string emptyName = null, pathValue = null;

                while (it.NextVisible(true))
                {
                    // An empty Sprite[] whose sibling path string is still populated is
                    // exactly the "not migrated yet" state we care about.
                    if (it.isArray && it.arrayElementType == "PPtr<$Sprite>" && it.arraySize == 0)
                    { hasEmptyArray = true; emptyName = it.displayName; }

                    if (it.propertyType == SerializedPropertyType.String &&
                        !string.IsNullOrEmpty(it.stringValue) &&
                        it.stringValue.Contains("Sprites/"))
                    { hasLegacyPath = true; pathValue = it.stringValue; }
                }

                if (hasEmptyArray && hasLegacyPath)
                    problems.Add($"    {mb.GetType().Name}: '{emptyName}' is empty but a legacy " +
                                 $"path is set (\"{pathValue}\")");
            }

            if (problems.Count > 0)
            {
                flagged++;
                Debug.LogWarning($"[ResAudit] {path}\n{string.Join("\n", problems)}");
            }
        }

        Debug.Log(flagged == 0
            ? "[ResAudit] No prefabs left with an empty sprite array beside a legacy path. ✓"
            : $"[ResAudit] ══ {flagged} prefab(s) still need direct sprite references assigned ══");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
}
#endif


