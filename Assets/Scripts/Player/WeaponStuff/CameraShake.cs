using UnityEngine;

// Camera shake that plays nicely with Cinemachine.
//
// Cinemachine Brain writes the camera transform in LateUpdate at default
// execution order. By running at order 1000 we guarantee our LateUpdate
// runs AFTER it, and we add our offset on top of whatever Cinemachine set.
//
// If you ever remove Cinemachine, this still works — adding a per-frame
// offset to a static camera is harmless.

[DefaultExecutionOrder(1000)]
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Default Shake")]
    [SerializeField] private float defaultIntensity = 0.08f;
    [SerializeField] private float defaultDuration = 0.1f;
    [SerializeField] private float frequency = 25f;

    [Header("Limits")]
    [SerializeField] private float maxIntensity = 0.3f;

    private float currentIntensity = 0f;
    private float shakeDuration = 0f;
    private float shakeElapsed = 0f;
    private bool isShaking = false;
    private float seed;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
            return;
        }

        seed = Random.value * 100f;
    }

    public void Shake(float intensity = -1f, float duration = -1f)
    {
        if (intensity < 0f) intensity = defaultIntensity;
        if (duration < 0f) duration = defaultDuration;
        intensity = Mathf.Min(intensity, maxIntensity);

        // If already shaking, stack: boost intensity slightly and reset timer.
        if (isShaking)
        {
            currentIntensity = Mathf.Min(currentIntensity + intensity * 0.5f, maxIntensity);
            shakeDuration = Mathf.Max(shakeDuration, duration);
            shakeElapsed = 0f;
            return;
        }

        currentIntensity = intensity;
        shakeDuration = duration;
        shakeElapsed = 0f;
        isShaking = true;
    }

    // Immediately cancels any active shake. Cinemachine (or whatever drives
    // the camera) will reposition cleanly on the next frame.
    public void StopShake()
    {
        isShaking = false;
        currentIntensity = 0f;
        shakeElapsed = 0f;
    }

    void LateUpdate()
    {
        if (!isShaking) return;

        shakeElapsed += Time.unscaledDeltaTime;
        if (shakeElapsed >= shakeDuration)
        {
            isShaking = false;
            return;
        }

        float t = shakeElapsed / shakeDuration;
        float decayedIntensity = currentIntensity * (1f - t);

        float noiseX = (Mathf.PerlinNoise(seed, shakeElapsed * frequency) - 0.5f) * 2f;
        float noiseY = (Mathf.PerlinNoise(seed + 50f, shakeElapsed * frequency) - 0.5f) * 2f;

        Vector3 offset = new Vector3(noiseX, noiseY, 0f) * decayedIntensity;

        // Add on TOP of whatever Cinemachine just set. Because we run at
        // execution order 1000, this happens after the Brain's LateUpdate.
        transform.position += offset;
    }

    void OnDisable()
    {
        isShaking = false;
        currentIntensity = 0f;
    }
}
