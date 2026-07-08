using UnityEngine;
using UnityEngine.SceneManagement;

// Entry point for the three menu buttons
//   Play Game / Start Co-op  = START A FRESH RUN. They DELETE any saved run.
//   Continue Previous Game   = RESUME the saved run. It's the ONLY button that
//                                resumes — that's why "Play Game" after Exit & Save
//                                started fresh instead of continuing.
// Controller waiting reuses the themed gates you already have:
//   StartCoop → CoopStartLobby (co-op only): waits until BOTH pads are connected, then starts a fresh 2-player run.
//   ContinuePreviousGame → ContinueRunMenu: reads the save, waits for exactly the controllers that run needs (e.g. 2/2 for a co-op save), or Abandon → solo.
// Both gates are passed gameScene so they load the GAMEPLAY scene, not the menu.
public class GameStarter : MonoBehaviour
{
    [Tooltip("Gameplay scene the run actually begins in.")]
    [SerializeField] private string gameScene = "GameScene";

    [Tooltip("Menu scene for ReturnToMenu().")]
    [SerializeField] private string menuScene = "MainMenu";

    //  Fresh SOLO run, existing Play / ChooseWeapon OnClick. Unchanged call.
    public void StartGame() => BeginFreshSolo();
    public void StartSolo() => BeginFreshSolo();

    private void BeginFreshSolo()
    {
        // Fresh solo: record 1 player, wipe any saved run so it can't auto-resume,
        // and hand the orchestrator a clean resume:false intent.
        if (SessionConfig.Instance != null) SessionConfig.Instance.SetPlayerCount(1);
        RunPersistence.DeleteSaveFile();
        RunResumeIntent.Set(resume: false, count: 1);
        Time.timeScale = 1f;   // a menu may have left it at 0; a frozen intro hangs on black.

        //Debug.Log($"[GameStarter] Fresh SOLO run → loading '{gameScene}'.");
        ScreenFade.LoadScene(gameScene);
    }

    // Fresh CO-OP run - Start Co-op button.
    // Opens the co-op wait screen; it forces co-op and won't start until both pads
    // are connected, then loads gameScene fresh (deletes the save + sets the intent itself on commit).
    public void StartCoop()
    {
        //Debug.Log("[GameStarter] Start Co-op → opening controller wait screen.");
        CoopStartLobby.OpenCoop(gameScene);
    }

    // RESUME - Continue Previous Game button.
    // Reads the saved run and reacts to its state: a co-op save waits for the needed
    // controllers before Continue enables; "Abandon Run & Start Solo" is always there.
    // If there's no save, the screen says so and only Abandon→solo is offered.
    public void ContinuePreviousGame()
    {
        //Debug.Log("[GameStarter] Continue Previous Game → opening continue gate.");
        ContinueRunMenu.Open(gameScene);
    }

    public void ReturnToMenu()
    {
        RunResumeIntent.Clear();   // leaving to the menu, don't carry a stale intent.
        Time.timeScale = 1f;
        SceneManager.LoadScene(menuScene);
    }
}

