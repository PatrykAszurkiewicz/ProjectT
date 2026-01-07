using UnityEngine;
using System.Collections;

public class EnemyAnimationController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private EnemyStats enemyStats;
    private EnemyData enemyData;
    private Sprite[] sprites;
    private Coroutine currentAnimationCoroutine;
    private bool isAttacking = false;

    private enum AnimationState { Idle, Attack }
    private AnimationState currentState = AnimationState.Idle;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        enemyStats = GetComponent<EnemyStats>();

        if (enemyStats == null || enemyStats.enemyData == null)
        {
            Debug.LogError($"No EnemyStats or EnemyData on {gameObject.name}!");
            enabled = false;
            return;
        }

        enemyData = enemyStats.enemyData;

        // Skip animation if no sprite folder specified
        if (string.IsNullOrEmpty(enemyData.spriteFolderPath))
        {
            enabled = false;
            return;
        }

        LoadSprites();

        if (sprites != null && sprites.Length > 0)
        {
            spriteRenderer.sprite = sprites[0];
            StartCoroutine(DelayedStartAnimation());
        }
        else
        {
            enabled = false;
        }
    }

    private void LoadSprites()
    {
        //Debug.Log($"[{gameObject.name}] Attempting to load sprites from: {enemyData.spriteFolderPath}");

        // Try loading all sprites from folder
        Sprite[] loadedSprites = Resources.LoadAll<Sprite>(enemyData.spriteFolderPath);

        if (loadedSprites == null || loadedSprites.Length == 0)
        {
            Debug.LogWarning($"[{gameObject.name}] LoadAll found 0 sprites. Trying individual file loading...");

            // Try loading individual numbered files
            System.Collections.Generic.List<Sprite> spriteList = new System.Collections.Generic.List<Sprite>();
            for (int i = 0; i < 100; i++) // Try up to 100 frames
            {
                string spritePath = $"{enemyData.spriteFolderPath}/{i:D2}";
                Sprite sprite = Resources.Load<Sprite>(spritePath);

                if (sprite != null)
                {
                    spriteList.Add(sprite);
                    Debug.Log($"[{gameObject.name}] Loaded sprite: {spritePath}");
                }
                else
                {
                    // No more sprites found
                    if (i == 0)
                    {
                        Debug.LogError($"[{gameObject.name}] Could not load any sprites from {enemyData.spriteFolderPath}");
                    }
                    break;
                }
            }

            loadedSprites = spriteList.ToArray();
        }

        if (loadedSprites == null || loadedSprites.Length == 0)
        {
            Debug.LogError($"[{gameObject.name}] FAILED to load sprites from {enemyData.spriteFolderPath}");
            return;
        }

        System.Array.Sort(loadedSprites, (a, b) => a.name.CompareTo(b.name));
        sprites = loadedSprites;

        //Debug.Log($"[{gameObject.name}] Successfully loaded {sprites.Length} sprites");
        //Debug.Log($"[{gameObject.name}] First sprite: {sprites[0].name}, Last sprite: {sprites[sprites.Length - 1].name}");
    }

    private IEnumerator DelayedStartAnimation()
    {
        yield return null;
        PlayIdleAnimation();
    }

    void Update()
    {
        if (sprites == null) return;

        bool shouldBeAttacking = IsEnemyAttacking();

        if (shouldBeAttacking != isAttacking)
        {
            isAttacking = shouldBeAttacking;
            if (isAttacking) PlayAttackAnimation();
            else PlayIdleAnimation();
        }
    }

    private bool IsEnemyAttacking()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        return rb != null && rb.linearVelocity.magnitude < 0.1f;
    }

    private void PlayIdleAnimation()
    {
        if (currentState == AnimationState.Idle) return;
        currentState = AnimationState.Idle;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            sprites,
            true,
            enemyData.idle.frameCount,
            enemyData.idle.startFrame,
            enemyData.animationSpeed
        ));
    }

    private void PlayAttackAnimation()
    {
        if (currentState == AnimationState.Attack) return;
        currentState = AnimationState.Attack;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            sprites,
            true,
            enemyData.attack.frameCount,
            enemyData.attack.startFrame,
            enemyData.animationSpeed
        ));
    }
}
