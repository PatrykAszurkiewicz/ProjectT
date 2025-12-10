using UnityEngine;
using UnityEngine.InputSystem;

public class CursorPointer : MonoBehaviour
{
    public Transform player;
    public float radius = 1.5f;

    private SpriteRenderer spriteRenderer;

    private void Start()
    {
        Cursor.visible = false;

        // Get SpriteRenderer with safety check
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
        {
            // Try to add SpriteRenderer if missing
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            Debug.LogWarning("[CURSOR POINTER] SpriteRenderer was missing - added automatically");
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.white;

            // Ensure sprite is assigned
            if (spriteRenderer.sprite == null)
            {
                Debug.LogWarning("[CURSOR POINTER] No sprite assigned to cursor!");
            }
        }
        else
        {
            Debug.LogError("[CURSOR POINTER] Failed to get or create SpriteRenderer!");
        }
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

    void LateUpdate()
    {
        // Ensure cursor stays white every frame
        if (spriteRenderer != null && spriteRenderer.color != Color.white)
        {
            spriteRenderer.color = Color.white;
        }
    }
}