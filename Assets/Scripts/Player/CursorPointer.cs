using UnityEngine;
using UnityEngine.InputSystem; 

public class CursorPointer : MonoBehaviour
{
    public Transform player;     
    public float radius = 1.5f;   // promieñ orbity wskaŸnika
    private void Start()
    {
        Cursor.visible = false;
    }
    void Update()
    {
        if (Mouse.current == null) return;

        Vector2 mouseScreenPos = Mouse.current.position.ReadValue(); 
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0;

        Vector3 direction = (mouseWorldPos - player.position).normalized;

        transform.position = player.position + direction * radius;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }
}
