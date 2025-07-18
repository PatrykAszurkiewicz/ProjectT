using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            Debug.Log("Atak wykonany!");
            // Tutaj wywo³ujesz logikê ataku, np. currentWeapon.Attack();
        }
    }
}