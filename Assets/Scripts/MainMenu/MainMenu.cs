using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void PlayGame()
    {
        SceneManager.LoadScene("StartingWeapon");
    }
    public void OpenOptions()
    {
        SceneManager.LoadScene("Options");
    }

    public void OpenCredits()
    {
        SceneManager.LoadScene("Credits");
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}
