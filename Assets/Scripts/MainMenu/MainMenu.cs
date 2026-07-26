using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void PlayGame()
    {
        //SceneManager.LoadScene("StartingWeapon");
        SceneManager.LoadScene("MenuContinue");
    }
    public void OpenOptions()
    {
        SceneManager.LoadScene("Options");
    }

    public void OpenCredits()
    {
        SceneManager.LoadScene("Credits");
    }
    public void BackToMainMenu()
    {
        SceneManager.LoadScene("MenuScene");
    }
    public void ExitGame()
    {
        Application.Quit();
    }
}
