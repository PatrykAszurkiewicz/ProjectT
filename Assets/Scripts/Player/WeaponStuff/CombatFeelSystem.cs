using UnityEngine;
using System.Collections;

// =============================================================================
// COMBAT FEEL SYSTEM v9
//
// Removed the _CombatShaker node approach — it created a visible artifact.
// Camera shake now uses a component on Camera.main that runs in LateUpdate
// with [DefaultExecutionOrder(1000)] to guarantee it runs AFTER Cinemachine.
// Cinemachine Brain runs at default order. We run at 1000 = always after.
// =============================================================================

public static class CombatFeel
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        var go = new GameObject("_CombatFeel");
        go.AddComponent<CombatFeelManager>();
        Object.DontDestroyOnLoad(go);
    }

    public static void OnHitEnemy(GameObject enemy, bool isMelee = true)
    {
        if (enemy == null) return;

        var flash = enemy.GetComponent<CombatHitFlash>();
        if (flash == null)
            flash = enemy.AddComponent<CombatHitFlash>();
        flash.Flash(isMelee);

        if (CombatFeelManager.Instance != null)
            CombatFeelManager.Instance.DoShake(isMelee ? 0.3f : 0.12f, isMelee ? 0.12f : 0.06f);
    }

    public static void OnPlayerHurt()
    {
        if (CombatFeelManager.Instance != null)
            CombatFeelManager.Instance.DoShake(0.25f, 0.10f);
    }

    public static void OnHeavyHit(GameObject enemy)
    {
        if (enemy == null) return;

        var flash = enemy.GetComponent<CombatHitFlash>();
        if (flash == null)
            flash = enemy.AddComponent<CombatHitFlash>();
        flash.FlashHeavy();

        if (CombatFeelManager.Instance != null)
            CombatFeelManager.Instance.DoShake(0.6f, 0.18f);
    }
}


public class CombatFeelManager : MonoBehaviour
{
    public static CombatFeelManager Instance { get; private set; }

    private CameraShaker cameraShaker;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Attach the shaker component directly to Camera.main
        StartCoroutine(AttachCameraShaker());
    }

    private IEnumerator AttachCameraShaker()
    {
        // Wait for camera to exist
        yield return null;
        yield return null;

        Camera cam = Camera.main;
        if (cam != null)
        {
            cameraShaker = cam.GetComponent<CameraShaker>();
            if (cameraShaker == null)
                cameraShaker = cam.gameObject.AddComponent<CameraShaker>();
            Debug.Log("[CombatFeel] CameraShaker attached to Main Camera");
        }
        else
        {
            Debug.LogWarning("[CombatFeel] No Main Camera found — no camera shake");
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void DoShake(float intensity, float duration)
    {
        if (cameraShaker != null)
            cameraShaker.Shake(intensity, duration);
    }

    // Immediately cancels any active shake.
    public void StopShake()
    {
        if (cameraShaker != null)
            cameraShaker.StopShake();
    }
}


// =============================================================================
// CAMERA SHAKER — sits on Camera.main, runs AFTER Cinemachine
//
// [DefaultExecutionOrder(1000)] ensures LateUpdate runs after Cinemachine Brain
// (which uses default order). We add an offset AFTER Cinemachine has set the
// camera position, so it can't be overwritten.
// =============================================================================

[DefaultExecutionOrder(1000)]
public class CameraShaker : MonoBehaviour
{
    private float shakeIntensity;
    private float shakeDuration;
    private float shakeElapsed;
    private bool isShaking = false;

    public void Shake(float intensity, float duration)
    {
        // Stack: if already shaking, boost intensity
        if (isShaking)
        {
            shakeIntensity = Mathf.Max(shakeIntensity, intensity);
            shakeElapsed = 0f; // Reset timer
            shakeDuration = Mathf.Max(shakeDuration, duration);
            return;
        }

        shakeIntensity = intensity;
        shakeDuration = duration;
        shakeElapsed = 0f;
        isShaking = true;
    }

    // Immediately cancels any active shake. Safe to call even if not shaking.
    // We don't reset transform.position because Cinemachine (or whatever drives
    // the camera) will overwrite it on the next frame anyway.
    public void StopShake()
    {
        isShaking = false;
        shakeIntensity = 0f;
        shakeElapsed = 0f;
    }

    void LateUpdate()
    {
        if (!isShaking) return;

        shakeElapsed += Time.deltaTime;
        if (shakeElapsed >= shakeDuration)
        {
            isShaking = false;
            return;
        }

        float t = shakeElapsed / shakeDuration;
        float decay = (1f - t) * (1f - t); // Quadratic falloff

        // Random offset each frame
        Vector2 offset = Random.insideUnitCircle * shakeIntensity * decay;

        // Apply AFTER Cinemachine (execution order 1000)
        transform.position += new Vector3(offset.x, offset.y, 0f);
    }
}


// =============================================================================
// HIT FLASH + SCALE PUNCH
// =============================================================================

public class CombatHitFlash : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock mpb;
    private Coroutine flashCoroutine;
    private Coroutine punchCoroutine;
    private static readonly int ColorProp = Shader.PropertyToID("_Color");

    private Vector3 baseScale;
    private bool baseScaleSet = false;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
    }

    private void CaptureBaseScale()
    {
        if (!baseScaleSet)
        {
            baseScale = transform.localScale;
            baseScaleSet = true;
        }
    }

    public void Flash(bool isMelee = true)
    {
        if (spriteRenderer == null) return;
        CaptureBaseScale();

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(isMelee ? MeleeFlashRoutine() : RangedFlashRoutine());

        if (punchCoroutine != null) StopCoroutine(punchCoroutine);
        punchCoroutine = StartCoroutine(ScalePunch(isMelee ? 1.4f : 1.25f, 0.12f));
    }

    public void FlashHeavy()
    {
        if (spriteRenderer == null) return;
        CaptureBaseScale();

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(HeavyFlashRoutine());

        if (punchCoroutine != null) StopCoroutine(punchCoroutine);
        punchCoroutine = StartCoroutine(ScalePunch(1.8f, 0.16f));
    }

    private IEnumerator MeleeFlashRoutine()
    {
        Color original = spriteRenderer.color;
        SetColor(Color.white);
        yield return new WaitForSeconds(0.05f);
        SetColor(new Color(1f, 0.5f, 0.35f, 1f));
        yield return new WaitForSeconds(0.05f);
        SetColor(original);
        flashCoroutine = null;
    }

    private IEnumerator RangedFlashRoutine()
    {
        Color original = spriteRenderer.color;
        SetColor(Color.white);
        yield return new WaitForSeconds(0.05f);
        SetColor(original);
        flashCoroutine = null;
    }

    private IEnumerator HeavyFlashRoutine()
    {
        Color original = spriteRenderer.color;
        SetColor(Color.white);
        yield return new WaitForSeconds(0.06f);
        SetColor(new Color(1f, 0.3f, 0.2f, 1f));
        yield return new WaitForSeconds(0.06f);
        SetColor(Color.white);
        yield return new WaitForSeconds(0.04f);
        SetColor(original);
        flashCoroutine = null;
    }

    private void SetColor(Color c)
    {
        spriteRenderer.color = c;
        spriteRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(ColorProp, c);
        spriteRenderer.SetPropertyBlock(mpb);
    }

    private IEnumerator ScalePunch(float multiplier, float duration)
    {
        transform.localScale = baseScale;
        Vector3 targetScale = baseScale * multiplier;

        float snapTime = duration * 0.3f;
        float elapsed = 0f;
        while (elapsed < snapTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / snapTime);
            transform.localScale = Vector3.Lerp(baseScale, targetScale, t);
            yield return null;
        }

        float settleTime = duration * 0.7f;
        elapsed = 0f;
        while (elapsed < settleTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settleTime);
            float smooth = t * t * (3f - 2f * t);
            transform.localScale = Vector3.Lerp(targetScale, baseScale, smooth);
            yield return null;
        }

        transform.localScale = baseScale;
        punchCoroutine = null;
    }

    void OnDisable()
    {
        if (spriteRenderer != null && mpb != null)
        {
            spriteRenderer.GetPropertyBlock(mpb);
            mpb.Clear();
            spriteRenderer.SetPropertyBlock(mpb);
        }
        if (baseScaleSet) transform.localScale = baseScale;
    }
}
