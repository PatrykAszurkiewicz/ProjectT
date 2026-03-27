using UnityEngine;
using System.Collections;


// Attach to any enemy prefab. Call Flash() when they take damage.
// Integration: GetComponent<HitFlash>()?.Flash();

public class HitFlash : MonoBehaviour
{
    [Header("Flash Settings")]
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.05f;

    [Header("Squash on Hit")]
    [SerializeField] private bool enableSquash = true;
    [SerializeField] private float squashAmount = 0.12f;
    [SerializeField] private float squashDuration = 0.08f;

    private SpriteRenderer spriteRenderer;
    private Coroutine flashCoroutine;
    private Color storedColor;
    private Vector3 baseScale;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            storedColor = spriteRenderer.color;

        baseScale = transform.localScale;
    }

    public void Flash()
    {
        if (spriteRenderer == null || !gameObject.activeInHierarchy) return;

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);

        flashCoroutine = StartCoroutine(DoFlash());
    }

    private IEnumerator DoFlash()
    {
        // Snapshot current color 
        Color before = spriteRenderer.color;

        // White flash
        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        spriteRenderer.color = before;

        // Squash & stretch
        if (enableSquash)
        {
            float half = squashDuration * 0.5f;
            float elapsed = 0f;

            Vector3 squashed = new Vector3(
                baseScale.x * (1f + squashAmount),
                baseScale.y * (1f - squashAmount),
                baseScale.z
            );

            // Squash
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(baseScale, squashed, elapsed / half);
                yield return null;
            }

            // Stretch back
            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(squashed, baseScale, elapsed / half);
                yield return null;
            }

            transform.localScale = baseScale;
        }

        flashCoroutine = null;
    }

    void OnDisable()
    {
        // Restore if killed mid-flash
        if (spriteRenderer != null)
            spriteRenderer.color = storedColor;
        transform.localScale = baseScale;
    }
}

