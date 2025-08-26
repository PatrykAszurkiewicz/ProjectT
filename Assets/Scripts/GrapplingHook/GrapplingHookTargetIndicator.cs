using UnityEngine;

public class GrapplingHookTargetIndicator : MonoBehaviour
{
    private Transform target;
    private bool isDestroyed = false;
    private const float FOLLOW_SPEED = 8f;
    private const float ROTATION_SPEED = 45f;
    private const float TARGET_OFFSET_Y = 1.5f;
    private const float FALLBACK_OFFSET = 2f;

    public static GrapplingHookTargetIndicator CreateIndicator(Transform target, Sprite hookSprite)
    {
        GameObject indicatorObj = new GameObject("HookIndicator");

        // Position indicator
        Vector3 indicatorPosition = GetIndicatorPosition(target);
        indicatorObj.transform.position = indicatorPosition;

        // Create hook sprite
        CreateHookSprite(indicatorObj, hookSprite);

        // Add and setup component
        var indicator = indicatorObj.AddComponent<GrapplingHookTargetIndicator>();
        indicator.target = target;

        return indicator;
    }

    private static Vector3 GetIndicatorPosition(Transform target)
    {
        if (target != null)
            return target.position + Vector3.up * TARGET_OFFSET_Y;

        // Fallback position
        Vector3 cameraPos = Camera.main?.transform.position ?? Vector3.zero;
        return cameraPos + Vector3.up * FALLBACK_OFFSET + Vector3.right;
    }

    private static void CreateHookSprite(GameObject parent, Sprite hookSprite)
    {
        GameObject spriteObj = new GameObject("HookSprite");
        spriteObj.transform.SetParent(parent.transform);
        spriteObj.transform.localPosition = Vector3.zero;

        var spriteRenderer = spriteObj.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = hookSprite ?? CreateFallbackSprite();
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = 1000;
        spriteRenderer.transform.localScale = Vector3.one * 1.1f;
    }

    private static Sprite CreateFallbackSprite()
    {
        const int size = 16;
        var texture = new Texture2D(size, size);
        var pixels = new Color[size * size];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.yellow;

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }

    private void Update()
    {
        if (isDestroyed) return;

        RotateIndicator();
        FollowTarget();
        KeepInCameraView();
    }

    private void RotateIndicator()
    {
        transform.Rotate(0, 0, ROTATION_SPEED * Time.deltaTime);
    }

    private void FollowTarget()
    {
        if (target == null) return;

        Vector3 targetPosition = target.position + Vector3.up * TARGET_OFFSET_Y;
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * FOLLOW_SPEED);
    }

    private void KeepInCameraView()
    {
        if (Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position);

        if (IsOutOfBounds(screenPos))
        {
            Vector3 cameraPos = Camera.main.transform.position;
            transform.position = cameraPos + Vector3.up * FALLBACK_OFFSET + Vector3.right;
        }
    }

    private bool IsOutOfBounds(Vector3 screenPos)
    {
        const float margin = 50f;
        return screenPos.x < margin || screenPos.x > Screen.width - margin ||
               screenPos.y < margin || screenPos.y > Screen.height - margin;
    }

    public void Hide()
    {
        if (isDestroyed) return;

        isDestroyed = true;
        if (gameObject != null)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        isDestroyed = true;
    }
}