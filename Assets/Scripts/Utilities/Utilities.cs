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

}
