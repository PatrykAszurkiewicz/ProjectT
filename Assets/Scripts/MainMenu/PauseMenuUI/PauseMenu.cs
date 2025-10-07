using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{

    public void OpenOptions()
    {
        SceneManager.LoadScene("Options");
    }
    public void RestartGame()
    {

    }
    public void SaveGame()
    {

    }
    public void QuitToMainMenu()
    {
        SceneManager.LoadScene("MenuScene");
    }
    public void QuitGame()
    {
        Application.Quit();
    }
}
