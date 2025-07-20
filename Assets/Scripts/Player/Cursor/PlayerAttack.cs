using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private Weapon weapon;

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed && weapon != null)
        {
            Debug.Log("ATAK");
            weapon.PerformAttack();
        }
    }
}
