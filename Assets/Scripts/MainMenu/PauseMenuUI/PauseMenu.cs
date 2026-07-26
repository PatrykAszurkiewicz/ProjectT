using UnityEngine;
using UnityEngine.SceneManagement;

// Pause-menu button targets.
//
// Fixed: every button here used to call SceneManager.LoadScene() straight from a
// paused state. Time.timeScale survives a scene load, so the destination scene
// booted at 0 — frozen intro, dead input, "black screen". PlayerAttack's
// suppression flag survived too. UIModalStack.ForceClear() restores both.
//
// OpenOptions also loaded a whole "Options" scene, which discards the run.
// The in-game OptionsMenu is an overlay; open that instead.
public class PauseMenu : MonoBehaviour
{
    [Tooltip("Load a separate Options SCENE instead of the in-run overlay. " +
             "Leave off in the gameplay scene — loading a scene abandons the run.")]
    [SerializeField] private bool useOptionsScene = false;

    public void OpenOptions()
    {
        if (useOptionsScene) { LeaveToScene("Options"); return; }
        OptionsMenu.Open();          // overlay: stacks above the pause menu
    }

    public void RestartGame() { }
    public void SaveGame() { }

    public void QuitToMainMenu() => LeaveToScene("MenuScene");

    public void QuitGame()
    {
        UIModalStack.ForceClear();
        Application.Quit();
    }

    private static void LeaveToScene(string scene)
    {
        // Drop every open modal first: restores Time.timeScale, un-suppresses
        // attacks, and clears the stack so the next scene starts unpaused.
        UIModalStack.ForceClear();
        RunResumeIntent.Clear();     // no stale resume/seating intent into the menu
        SceneManager.LoadScene(scene);
    }
}
