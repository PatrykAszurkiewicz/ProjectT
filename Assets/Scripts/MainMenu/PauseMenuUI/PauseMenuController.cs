using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class PauseMenuController : MonoBehaviour
{
    public GameObject pauseMenu;
    private bool activated;
    private GameObject _weaponRollCanvas;

    private void Awake()
    {
        pauseMenu.SetActive(false);
        activated = false;
    }

    private void Update()
    {
        // Start (Menu) button opens AND closes the pause menu. Input polling
        // runs regardless of Time.timeScale, so this still un-pauses at 0.
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
            ActivatePauseMenu();
    }

    public void ActivatePauseMenu()
    {
        // The WeaponRollUI builds its own canvas at runtime, so we look it up
        // lazily by name the first time we pause.
        if (_weaponRollCanvas == null)
            _weaponRollCanvas = GameObject.Find("WeaponRoll_Canvas");

        if (activated == false)
        {
            activated = true;
            Time.timeScale = 0f;
            Cursor.visible = true;
            pauseMenu.SetActive(true);
            if (_weaponRollCanvas != null) _weaponRollCanvas.SetActive(false);
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
            if (_weaponRollCanvas != null) _weaponRollCanvas.SetActive(true);
            PlayerAttack.InputSuppressed = false;
        }
    }
}

