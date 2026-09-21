using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

// TEMPORARY DIAGNOSTIC — delete once the "No cameras rendering" bug is found.
//
// Drop this file anywhere under Assets/. It needs NO scene setup: it installs
// itself after the first scene load and prints a report 2 seconds after EVERY
// scene load, once things have had time to spawn.
//
// What it answers, in one block per scene load:
//   * Did a CoopManager exist, and is the live Instance in this scene or in
//     DontDestroyOnLoad (i.e. carried over from a previous run)?
//   * Is there a PlayerInputManager, is it ACTIVE, and how many players has it seated?
//   * Do any Cameras exist at all — enabled, disabled, or on inactive objects?
//   * Did a player register in PlayerRegistry?
//
// Compare the block from the FIRST load (works) against the SECOND (broken).
// The line that differs is the bug.
public static class PlayerSpawnDiagnostic
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Runner.Exists) return;

        var go = new GameObject("~PlayerSpawnDiagnostic");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<Runner>();
    }

    private class Runner : MonoBehaviour
    {
        public static bool Exists;

        private void Awake()
        {
            Exists = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // The scene we were installed into never raises sceneLoaded for us.
            StartCoroutine(ReportAfterDelay(SceneManager.GetActiveScene().name, "initial load"));
        }

        private void OnDestroy()
        {
            Exists = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            => StartCoroutine(ReportAfterDelay(scene.name, mode.ToString()));

        private IEnumerator ReportAfterDelay(string sceneName, string how)
        {
            // Real time, not scaled — a paused/frozen load must still report.
            yield return new WaitForSecondsRealtime(2f);
            Report(sceneName, how);
        }

        private static void Report(string sceneName, string how)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"═══ [SpawnDiag] scene '{sceneName}' ({how}) — 2s after load ═══");

            // ── CoopManager ──────────────────────────────────────────────────
            var coops = FindObjectsByType<CoopManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"CoopManager objects found: {coops.Length}");
            foreach (var c in coops)
                sb.AppendLine($"   • '{Path(c.transform)}' scene='{c.gameObject.scene.name}' " +
                              $"activeInHierarchy={c.gameObject.activeInHierarchy} enabled={c.enabled} " +
                              $"isInstance={(CoopManager.Instance == c)}");

            if (CoopManager.Instance == null)
                sb.AppendLine("   ⚠ CoopManager.Instance is NULL — nothing will seat a player.");
            else
                sb.AppendLine($"   Instance scene='{CoopManager.Instance.gameObject.scene.name}' " +
                              $"ManagedMode={CoopManager.Instance.ManagedMode} " +
                              $"TargetPlayerCount={CoopManager.Instance.TargetPlayerCount}" +
                              (CoopManager.Instance.gameObject.scene.name == "DontDestroyOnLoad"
                                  ? "   ⚠ PERSISTED from an earlier load — its Start() will NOT run again."
                                  : ""));

            // ── PlayerInputManager ───────────────────────────────────────────
            var pims = FindObjectsByType<PlayerInputManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"PlayerInputManager objects found: {pims.Length}" +
                          (pims.Length == 0 ? "   ⚠ NONE — CoopManager falls into LEGACY mode and spawns nobody." : ""));
            foreach (var m in pims)
                sb.AppendLine($"   • '{Path(m.transform)}' scene='{m.gameObject.scene.name}' " +
                              $"activeInHierarchy={m.gameObject.activeInHierarchy} enabled={m.enabled} " +
                              $"playerCount={m.playerCount} maxPlayerCount={m.maxPlayerCount} " +
                              $"joiningEnabled={m.joiningEnabled} playerPrefab=" +
                              $"{(m.playerPrefab != null ? m.playerPrefab.name : "NULL")}");

            // ── Players ──────────────────────────────────────────────────────
            sb.AppendLine($"PlayerInput.all: {PlayerInput.all.Count}");
            foreach (var pi in PlayerInput.all)
                sb.AppendLine($"   • '{pi.gameObject.name}' idx={pi.playerIndex} " +
                              $"scene='{pi.gameObject.scene.name}' inputIsActive={pi.inputIsActive}");
            sb.AppendLine($"PlayerRegistry.Count: {PlayerRegistry.Count}");

            // ── Cameras — the actual symptom ─────────────────────────────────
            var cams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"Cameras in memory: {cams.Length}   (Camera.main={(Camera.main != null ? Camera.main.name : "NULL")})");
            foreach (var cam in cams)
                sb.AppendLine($"   • '{Path(cam.transform)}' scene='{cam.gameObject.scene.name}' " +
                              $"activeInHierarchy={cam.gameObject.activeInHierarchy} enabled={cam.enabled} " +
                              $"rect={cam.rect} depth={cam.depth} tag='{cam.tag}'");
            if (cams.Length == 0)
                sb.AppendLine("   ⚠ ZERO Camera components exist. Overlay canvases still draw; nothing else does.");

            // ── Persistence suspects ─────────────────────────────────────────
            sb.AppendLine($"SessionConfig.Instance: " +
                          (SessionConfig.Instance != null
                              ? $"'{Path(SessionConfig.Instance.transform)}' scene='{SessionConfig.Instance.gameObject.scene.name}' " +
                                $"TargetPlayerCount={SessionConfig.Instance.TargetPlayerCount}"
                              : "NULL"));
            sb.AppendLine("══════════════════════════════════════════════════════");

            Debug.LogWarning(sb.ToString());
        }

        // Full hierarchy path, so you can see WHAT a manager is parented under —
        // that is the detail that tells you whether it got dragged into
        // DontDestroyOnLoad by a persisting parent.
        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}



