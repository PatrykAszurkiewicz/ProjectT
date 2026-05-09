using UnityEngine;
using System.Collections;


// Attach to any enemy prefab. Call Flash() when they take damage.
// Integration: GetComponent<HitFlash>()?.Flash();
//
// Provides:
//  - Two-stage color flash on melee hits (white → warm ember → restore)
//  - Single white flash on ranged hits
//  - Squash & stretch (existing, optional)
//  - Scale punch (snap big, settle back) — feels like cartoon impact

public class HitFlash : MonoBehaviour
{
    [Header("Flash Settings")]
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.05f;
    [SerializeField] private Color meleeEmberColor = new Color(1f, 0.5f, 0.35f, 1f);

    [Header("Squash on Hit")]
    [SerializeField] private bool enableSquash = true;
    [SerializeField] private float squashAmount = 0.12f;
    [SerializeField] private float squashDuration = 0.08f;

    [Header("Scale Punch on Hit")]
    [SerializeField] private bool enableScalePunch = true;
    [SerializeField] private float meleePunchMultiplier = 1.4f;
    [SerializeField] private float rangedPunchMultiplier = 1.25f;
    [SerializeField] private float punchDuration = 0.12f;

    private SpriteRenderer spriteRenderer;
    private Coroutine flashCoroutine;
    private Coroutine punchCoroutine;
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

    // Default call — keeps backward compatibility with all existing call sites.
    // Defaults to melee feel (two-stage flash + bigger punch).
    public void Flash()
    {
        Flash(true);
    }

    // Explicit version — pass false for ranged hits (single flash, smaller punch).
    public void Flash(bool isMelee)
    {
        if (spriteRenderer == null || !gameObject.activeInHierarchy) return;

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(isMelee ? MeleeFlashRoutine() : RangedFlashRoutine());

        if (enableScalePunch)
        {
            if (punchCoroutine != null)
                StopCoroutine(punchCoroutine);
            float multiplier = isMelee ? meleePunchMultiplier : rangedPunchMultiplier;
            punchCoroutine = StartCoroutine(ScalePunchRoutine(multiplier, punchDuration));
        }
    }

    // ── COLOR FLASH ROUTINES ──

    private IEnumerator MeleeFlashRoutine()
    {
        Color before = spriteRenderer.color;

        // Stage 1: hard white
        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(flashDuration);

        // Stage 2: warm ember tint
        spriteRenderer.color = meleeEmberColor;
        yield return new WaitForSeconds(flashDuration);

        // Restore
        spriteRenderer.color = before;

        // Squash & stretch (only if scale punch is disabled — they fight each other)
        if (enableSquash && !enableScalePunch)
            yield return SquashRoutine();

        flashCoroutine = null;
    }

    private IEnumerator RangedFlashRoutine()
    {
        Color before = spriteRenderer.color;

        // Single white flash
        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        spriteRenderer.color = before;

        if (enableSquash && !enableScalePunch)
            yield return SquashRoutine();

        flashCoroutine = null;
    }

    // ── SQUASH (existing behavior, preserved as fallback) ──

    private IEnumerator SquashRoutine()
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

    // ── SCALE PUNCH (snap big, smooth settle) ──

    private IEnumerator ScalePunchRoutine(float multiplier, float duration)
    {
        // Always reset to base before punching, in case a previous punch was interrupted
        transform.localScale = baseScale;
        Vector3 targetScale = baseScale * multiplier;

        // Quick snap up (30% of duration)
        float snapTime = duration * 0.3f;
        float elapsed = 0f;
        while (elapsed < snapTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / snapTime);
            transform.localScale = Vector3.Lerp(baseScale, targetScale, t);
            yield return null;
        }

        // Smooth settle back (70% of duration)
        float settleTime = duration * 0.7f;
        elapsed = 0f;
        while (elapsed < settleTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settleTime);
            float smooth = t * t * (3f - 2f * t); // smoothstep
            transform.localScale = Vector3.Lerp(targetScale, baseScale, smooth);
            yield return null;
        }

        transform.localScale = baseScale;
        punchCoroutine = null;
    }

    void OnDisable()
    {
        // Restore if killed mid-flash
        if (spriteRenderer != null)
            spriteRenderer.color = storedColor;
        transform.localScale = baseScale;
    }
}
