using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            Debug.Log("Atak wykonany!");
            // Tutaj wywo�ujesz logik� ataku, np. currentWeapon.Attack();
        }
    }
}