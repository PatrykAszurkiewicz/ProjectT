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
        if (activated == false)
        {
            activated = true;
            Time.timeScale = 0f;
            Cursor.visible = true;
            pauseMenu.SetActive(true);
            PlayerAttack.InputSuppressed = true;

            // Kill any in-flight shake so the menu doesn't wobble.
            CombatJuice.StopAllShake();
        }
        else
        {
            activated = false;
            Time.timeScale = 1f;
            Cursor.visible = false;
            pauseMenu.SetActive(false);
            PlayerAttack.InputSuppressed = false;
        }
    }
}
