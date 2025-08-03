using System.Collections;
using UnityEngine;

public static class Utilities
{
    /// <summary>
    /// Animate through a sprite array on the given SpriteRenderer.
    /// </summary>
    public static IEnumerator AnimateSprite(
        SpriteRenderer spriteRenderer,
        Sprite[] sprites,
        bool enableAnimation,
        int animationFrameCount,
        int spriteIndex,
        float animationSpeed
    )
    {
        if (!enableAnimation || sprites == null || sprites.Length == 0)
            yield break;

        int frameCount = Mathf.Min(animationFrameCount, sprites.Length - spriteIndex);
        int currentFrame = 0;
        float timer = 0f;

        while (true)
        {
            timer += Time.deltaTime;
            if (timer > animationSpeed * 2f)
                timer = animationSpeed * 2f;

            if (timer >= animationSpeed)
            {
                //Debug.Log("Advancing frame at time: " + Time.time);
                timer -= animationSpeed;
                currentFrame = (currentFrame + 1) % frameCount;
                int idx = spriteIndex + currentFrame;
                if (idx < sprites.Length)
                    spriteRenderer.sprite = sprites[idx];
            }
            yield return null;
        }
    }

    public static IEnumerator AnimateSpritePingPong(
        SpriteRenderer spriteRenderer,
        Sprite[] sprites,
        bool enableAnimation,
        int animationFrameCount,
        int spriteIndex,
        float animationSpeed
    )
    {
        if (!enableAnimation || sprites == null || sprites.Length == 0)
            yield break;

        int start = Mathf.Clamp(spriteIndex, 0, sprites.Length - 1);
        int end = Mathf.Clamp(start + animationFrameCount - 1, 0, sprites.Length - 1);

        int currentFrame = start;
        int direction = 1; // 1 = forward, -1 = backward

        while (true)
        {
            spriteRenderer.sprite = sprites[currentFrame];
            yield return new WaitForSeconds(animationSpeed);

            currentFrame += direction;

            if (currentFrame >= end)
            {
                currentFrame = end;
                direction = -1;
            }
            else if (currentFrame <= start)
            {
                currentFrame = start;
                direction = 1;
            }
        }
    }

    /// <summary>
    /// Get all attackable targets for enemies (Central Core, Player, Towers)
    /// </summary>
    public static GameObject[] GetAttackableTargets()
    {
        var targets = new System.Collections.Generic.List<GameObject>();

        // Find Central Core
        var core = Object.FindFirstObjectByType<CentralCore>();
        if (core != null)
            targets.Add(core.gameObject);

        // Find Player
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            targets.Add(player);

        // Find all Towers
        var towers = Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);
        foreach (var tower in towers)
        {
            if (tower != null && tower.IsOperational())
                targets.Add(tower.gameObject);
        }
        return targets.ToArray();
    }

    /// <summary>
    /// Get the closest attackable target to a position
    /// </summary>
    public static GameObject GetClosestAttackableTarget(Vector3 position)
    {
        var targets = GetAttackableTargets();
        GameObject closest = null;
        float closestDistance = float.MaxValue;

        foreach (var target in targets)
        {
            float distance = Vector3.Distance(position, target.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = target;
            }
        }
        return closest;
    }

    /// <summary>
    /// Get collision bounds for pathfinding obstacle avoidance
    /// </summary>
    public static Bounds GetTargetCollisionBounds(GameObject target)
    {
        // Try to get the non-trigger collider first
        var colliders = target.GetComponents<Collider2D>();
        foreach (var collider in colliders)
        {
            if (!collider.isTrigger)
                return collider.bounds;
        }
        // Fallback to sprite bounds if no physical collider found
        return SpriteCollisionManager.GetSpriteBounds(target);
    }

    /// <summary>
    /// Check if a target is still valid for attacking
    /// </summary>
    public static bool IsValidAttackTarget(GameObject target)
    {
        if (target == null) return false;
        // Check if it's a Tower and if it's operational
        var tower = target.GetComponent<Tower>();
        if (tower != null)
            return tower.IsOperational();
        // Check if it's a Central Core and not destroyed
        var core = target.GetComponent<CentralCore>();
        if (core != null)
            return !core.IsDestroyed();
        // Check if it's the Player
        if (target.CompareTag("Player"))
            return true;

        return false;
    }
}

[System.Serializable]
public class SpriteCollisionConfig
{
    [Header("Collision Settings")]
    public bool enableCollision = true;
    public bool isTrigger = false;
    public ColliderType colliderType = ColliderType.Box;

    [Header("Size Settings")]
    [Range(0f, 0.5f)]
    public float paddingPercent = 0.1f;

    public enum ColliderType
    {
        Box,
        Circle,
        PixelPerfect
    }
}

// Shared collision manager that both Tower and Core can use
public static class SpriteCollisionManager
{
    /// <summary>
    /// Setup collision for any GameObject with a SpriteRenderer
    /// </summary>
    public static Collider2D SetupCollision(GameObject gameObject, SpriteCollisionConfig config)
    {
        if (!config.enableCollision) return null;

        SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null || spriteRenderer.sprite == null)
        {
            Debug.LogWarning($"No SpriteRenderer or sprite found on {gameObject.name}");
            return null;
        }

        // Remove existing sprite colliders
        RemoveExistingSpriteColliders(gameObject, config.isTrigger);
        Collider2D newCollider = null;
        switch (config.colliderType)
        {
            case SpriteCollisionConfig.ColliderType.Box:
                newCollider = SetupBoxCollider(gameObject, spriteRenderer, config);
                break;
            case SpriteCollisionConfig.ColliderType.Circle:
                newCollider = SetupCircleCollider(gameObject, spriteRenderer, config);
                break;
            case SpriteCollisionConfig.ColliderType.PixelPerfect:
                newCollider = SetupPixelPerfectCollider(gameObject, spriteRenderer, config);
                break;
        }

        if (newCollider != null)
        {
            Debug.Log($"Added {config.colliderType} collider to {gameObject.name}");
        }

        return newCollider;
    }

    /// <summary>
    /// Setup collision with a delay (useful when sprite loads asynchronously)
    /// </summary>
    public static Coroutine SetupCollisionDelayed(MonoBehaviour owner, SpriteCollisionConfig config)
    {
        return owner.StartCoroutine(SetupCollisionCoroutine(owner.gameObject, config));
    }
    private static IEnumerator SetupCollisionCoroutine(GameObject gameObject, SpriteCollisionConfig config)
    {
        yield return null; // Wait one frame
        SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            SetupCollision(gameObject, config);
        }
    }

    private static void RemoveExistingSpriteColliders(GameObject gameObject, bool isTrigger)
    {
        // Remove existing sprite colliders of the same trigger type
        var boxColliders = gameObject.GetComponents<BoxCollider2D>();
        foreach (var collider in boxColliders)
        {
            if (collider.isTrigger == isTrigger)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }
        }

        var circleColliders = gameObject.GetComponents<CircleCollider2D>();
        foreach (var collider in circleColliders)
        {
            if (collider.isTrigger == isTrigger)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }
        }

        var polygonColliders = gameObject.GetComponents<PolygonCollider2D>();
        foreach (var collider in polygonColliders)
        {
            if (collider.isTrigger == isTrigger)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }
        }
    }

    private static BoxCollider2D SetupBoxCollider(GameObject gameObject, SpriteRenderer spriteRenderer, SpriteCollisionConfig config)
    {
        BoxCollider2D collider = gameObject.AddComponent<BoxCollider2D>();

        Bounds spriteBounds = spriteRenderer.sprite.bounds;
        Vector2 colliderSize = spriteBounds.size;

        if (config.paddingPercent > 0f)
        {
            colliderSize *= (1f + config.paddingPercent);
        }

        collider.size = colliderSize;
        collider.offset = spriteBounds.center;
        collider.isTrigger = config.isTrigger;

        return collider;
    }

    private static CircleCollider2D SetupCircleCollider(GameObject gameObject, SpriteRenderer spriteRenderer, SpriteCollisionConfig config)
    {
        CircleCollider2D collider = gameObject.AddComponent<CircleCollider2D>();

        Bounds spriteBounds = spriteRenderer.sprite.bounds;
        float radius = Mathf.Max(spriteBounds.size.x, spriteBounds.size.y) * 0.5f;

        if (config.paddingPercent > 0f)
        {
            radius *= (1f + config.paddingPercent);
        }

        collider.radius = radius;
        collider.offset = spriteBounds.center;
        collider.isTrigger = config.isTrigger;

        return collider;
    }

    private static PolygonCollider2D SetupPixelPerfectCollider(GameObject gameObject, SpriteRenderer spriteRenderer, SpriteCollisionConfig config)
    {
        PolygonCollider2D collider = gameObject.AddComponent<PolygonCollider2D>();
        collider.isTrigger = config.isTrigger;

        // Unity automatically generates points based on sprite's alpha
        return collider;
    }

    /// <summary>
    /// Get sprite bounds for any GameObject with SpriteRenderer
    /// </summary>
    public static Bounds GetSpriteBounds(GameObject gameObject)
    {
        SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return spriteRenderer.bounds; // World space bounds
        }
        return new Bounds(gameObject.transform.position, Vector3.one);
    }

    /// <summary>
    /// Check if a point is within the sprite collision bounds
    /// </summary>
    public static bool IsPointWithinSprite(GameObject gameObject, Vector3 worldPoint)
    {
        // Try to find a non-trigger collider first
        var colliders = gameObject.GetComponents<Collider2D>();
        foreach (var collider in colliders)
        {
            if (!collider.isTrigger)
            {
                return collider.bounds.Contains(worldPoint);
            }
        }

        // Fallback to sprite bounds
        return GetSpriteBounds(gameObject).Contains(worldPoint);
    }

    /// <summary>
    /// Get collision radius for circular objects
    /// </summary>
    public static float GetCollisionRadius(GameObject gameObject)
    {
        CircleCollider2D circleCollider = gameObject.GetComponent<CircleCollider2D>();
        if (circleCollider != null && !circleCollider.isTrigger)
        {
            Transform transform = gameObject.transform;
            return circleCollider.radius * Mathf.Max(transform.localScale.x, transform.localScale.y);
        }

        var bounds = GetSpriteBounds(gameObject);
        return Mathf.Max(bounds.size.x, bounds.size.y) * 0.5f;
    }

    /// <summary>
    /// Update collision settings at runtime
    /// </summary>
    public static Collider2D UpdateCollisionSettings(GameObject gameObject, SpriteCollisionConfig config)
    {
        return SetupCollision(gameObject, config);
    }
}