using UnityEngine;
using UnityEngine.InputSystem;

public class CursorPointer : MonoBehaviour
{
    public Transform player;
    public float radius = 1.5f;

    [Tooltip("Optional explicit aim binding. If left null, the PlayerAim on the " +
             "pointed-at player is used (falling back to PlayerAim.Instance).")]
    public PlayerAim aim;

    private SpriteRenderer spriteRenderer;
    private PlayerRef playerRef;
    private Camera cam;

    private void Start()
    {
        Cursor.visible = false;

        // Resolve this cursor's player bindings. In single player `player` is the
        // one player; in co-op each player has its own CursorPointer pointing at
        // itself, so these resolve per-player.
        if (player != null)
        {
            if (aim == null) aim = player.GetComponent<PlayerAim>();
            playerRef = player.GetComponent<PlayerRef>();
        }
        cam = ResolveCamera();

        // Get SpriteRenderer — it's on the child CursorVisual, not on this GameObject
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            Debug.LogWarning("[CURSOR POINTER] SpriteRenderer was missing - added automatically");
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.white;
            // Must be above fog (5000) and grass Y-sort range (400–1600)
            spriteRenderer.sortingOrder = 10000;

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

    private Camera ResolveCamera()
    {
        if (playerRef != null && playerRef.Camera != null) return playerRef.Camera;
        return Camera.main;
    }

    void Update()
    {
        // Re-bind to this player's camera once it becomes available.
        if (playerRef != null && playerRef.Camera != null) cam = playerRef.Camera;
        else if (cam == null) cam = ResolveCamera();

        PlayerAim activeAim = aim != null ? aim : PlayerAim.Instance;

        Vector3 direction;
        if (activeAim != null)
        {
            direction = activeAim.Direction;
        }
        else
        {
            if (Mouse.current == null || cam == null) return;
            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Vector3 mouseWorldPos = cam.ScreenToWorldPoint(mouseScreenPos);
            mouseWorldPos.z = 0;
            direction = (mouseWorldPos - player.position).normalized;
        }

        transform.position = player.position + direction * radius;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void LateUpdate()
    {
        // Ensure cursor stays white and always renders above everything
        if (spriteRenderer != null)
        {
            if (spriteRenderer.color != Color.white)
            {
                spriteRenderer.color = Color.white;
            }
            spriteRenderer.sortingOrder = 10000;
        }
    }
}

