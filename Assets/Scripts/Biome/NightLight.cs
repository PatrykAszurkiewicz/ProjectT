using UnityEngine;


// Attach to any GameObject to make it a point light source in the NightOverlay.
// The area around this object will be illuminated through the darkness exactly
// Does nothing when night mode is not active (NightOverlay.Instance == null).

public class NightLight : MonoBehaviour
{
    [Header("Light Properties")]
    [Tooltip("How far this light illuminates (world units).")]
    public float radius = 3f;

    [Tooltip("Brightness of the illumination. 1 = fully reveals area like standing in the torch beam. Values above 1 are allowed for extra-bright effects.")]
    public float intensity = 1f;

    [Tooltip("Color tint of the light — affects the warm tint blended into the night color near this light.")]
    public Color lightColor = new Color(1f, 0.85f, 0.55f);

    [Tooltip("How strongly this light's color tints the darkness (0 = pure white light, 1 = strongly tinted).")]
    [Range(0f, 1f)]
    public float warmTintStrength = 0.4f;

    [Header("Animation (optional)")]
    [Tooltip("Flicker speed in Perlin-noise units (0 = steady light).")]
    public float flickerSpeed = 0f;

    [Tooltip("How much the radius varies due to flicker.")]
    [Range(0f, 0.5f)]
    public float flickerAmount = 0f;

    [Tooltip("Time in seconds to fade in from zero to full intensity (0 = instant).")]
    public float fadeInDuration = 0f;

    private NightOverlay.NightLightHandle handle;
    private float flickerPhase;
    private float fadeInElapsed;
    private bool registeredOnce;

    void OnEnable()
    {
        flickerPhase = Random.Range(0f, 100f);
        fadeInElapsed = 0f;
        registeredOnce = false;
    }

    // Immediately register with NightOverlay if night is active.

    private void TryRegister()
    {
        if (handle != null && handle.alive) return;   // already registered
        if (NightOverlay.Instance == null) return;     // night not active

        handle = NightOverlay.RegisterLight(
            transform.position,
            radius,
            fadeInDuration > 0f ? 0f : intensity,
            lightColor,
            warmTintStrength);
    }

    void Update()
    {
        bool nightActive = NightOverlay.Instance != null;

        if (nightActive && (handle == null || !handle.alive))
        {
            // First frame after AddComponent, or night just turned on / recreated
            TryRegister();
            registeredOnce = true;
        }
        else if (!nightActive && handle != null)
        {
            // Night turned off — handle already killed by NightOverlay.OnDestroy
            handle = null;
            return;
        }

        if (handle == null) return;

        // Fade-in ramp
        float fadeMultiplier = 1f;
        if (fadeInDuration > 0f)
        {
            fadeInElapsed += Time.deltaTime;
            fadeMultiplier = Mathf.Clamp01(fadeInElapsed / fadeInDuration);
        }

        // Sync position/params every frame (for moving objects)
        handle.position = transform.position;
        handle.color = lightColor;
        handle.warmTintStrength = warmTintStrength;
        handle.intensity = intensity * fadeMultiplier;

        // Flicker
        if (flickerSpeed > 0f && flickerAmount > 0f)
        {
            float flicker = (Mathf.PerlinNoise(Time.time * flickerSpeed + flickerPhase, 0.5f) * 2f - 1f)
                            * flickerAmount;
            handle.radius = radius * (1f + flicker);
        }
        else
        {
            handle.radius = radius;
        }
    }

    void OnDisable()
    {
        if (handle != null)
        {
            NightOverlay.UnregisterLight(handle);
            handle = null;
        }
    }

    void OnDestroy()
    {
        if (handle != null)
        {
            NightOverlay.UnregisterLight(handle);
            handle = null;
        }
    }
}