using UnityEngine;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    public GameObject pauseMenu;
    private bool activated;
    private void Awake()
    {
        pauseMenu.SetActive(false);
        activated = false;
    }
    public void ActivatePauseMenu()
    {
        if(activated == false)
        {
            activated = true;
            Time.timeScale = 0f;
            Cursor.visible = true;
            pauseMenu.SetActive(true);
        }
        else
        {
            activated = false;
            Time.timeScale = 1f;
            Cursor.visible = false;
            pauseMenu.SetActive(false);
        }
    }
}
