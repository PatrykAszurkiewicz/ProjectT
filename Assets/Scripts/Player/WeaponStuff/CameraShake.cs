using UnityEngine;
using System.Collections;


public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Default Shake")]
    [SerializeField] private float defaultIntensity = 0.08f;
    [SerializeField] private float defaultDuration = 0.1f;
    [SerializeField] private float frequency = 25f;

    [Header("Limits")]
    [SerializeField] private float maxIntensity = 0.3f;

    private Vector3 originalLocalPosition;
    private Coroutine shakeCoroutine;
    private float currentIntensity = 0f;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(this);
            return;
        }

        originalLocalPosition = transform.localPosition;
    }

    public void Shake(float intensity = -1f, float duration = -1f)
    {
        if (intensity < 0f) intensity = defaultIntensity;
        if (duration < 0f) duration = defaultDuration;
        intensity = Mathf.Min(intensity, maxIntensity);

        if (shakeCoroutine != null)
        {
            currentIntensity = Mathf.Min(currentIntensity + intensity * 0.5f, maxIntensity);
            return;
        }

        currentIntensity = intensity;
        shakeCoroutine = StartCoroutine(ShakeRoutine(duration));
    }

    // Immediately cancels any active shake and snaps the camera back to its
    // resting local position. Call this when opening menus, transitions, etc.
    public void StopShake()
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }
        currentIntensity = 0f;
        transform.localPosition = originalLocalPosition;
    }

    private IEnumerator ShakeRoutine(float duration)
    {
        float elapsed = 0f;
        float seed = Random.value * 100f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float decayedIntensity = currentIntensity * (1f - t);

            float noiseX = (Mathf.PerlinNoise(seed, elapsed * frequency) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(seed + 50f, elapsed * frequency) - 0.5f) * 2f;

            Vector3 offset = new Vector3(noiseX, noiseY, 0f) * decayedIntensity;
            transform.localPosition = originalLocalPosition + offset;

            yield return null;
        }

        transform.localPosition = originalLocalPosition;
        currentIntensity = 0f;
        shakeCoroutine = null;
    }

    void LateUpdate()
    {
        if (shakeCoroutine == null)
            originalLocalPosition = transform.localPosition;
    }

    void OnDisable()
    {
        if (shakeCoroutine != null)
        {
            transform.localPosition = originalLocalPosition;
            shakeCoroutine = null;
        }
    }
}
