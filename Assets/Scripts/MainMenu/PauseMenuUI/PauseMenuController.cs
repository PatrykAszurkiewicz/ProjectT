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
            pauseMenu.SetActive(true);
        }
        else
        {
            activated = false;
            pauseMenu.SetActive(false);
        }
    }
}
