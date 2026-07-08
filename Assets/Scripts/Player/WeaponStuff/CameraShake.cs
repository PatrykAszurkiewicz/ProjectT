using System.Collections.Generic;
using UnityEngine;

// Per-camera shake that plays nicely with Cinemachine.
// Cinemachine Brain (and PlayerCameraController's follow) writes the camera
// transform in LateUpdate at default execution order; this runs at order 1000 so
// the shake offset is added on TOP of whatever the follow/Brain just set.
//
// CO-OP: this used to be a hard singleton that Destroy()'d every duplicate, which
// meant only ONE of the two split-screen cameras could ever shake, and every hit
// shook that one camera regardless of which player caused it. It is now a
// self-registering, MULTI-instance component: one lives on each player camera.
// Route a shake to a specific player's half with ShakeFor()/ShakeForCamera();
// shake every half at once (boss death, global events) with ShakeAll().
[DefaultExecutionOrder(1000)]
public class CameraShake : MonoBehaviour
{
    // Every currently-enabled shake (one per active player camera).
    private static readonly List<CameraShake> _all = new List<CameraShake>();
    public static IReadOnlyList<CameraShake> All => _all;

    // Backward-compatible "primary" accessor for any legacy/global call site that
    // still does CameraShake.Instance.Shake(...). Returns the first registered
    // instance (full-screen camera in single player) or null if none exist.
    // NOTE: prefer the static ShakeAll / ShakeFor helpers in new code.
    public static CameraShake Instance => _all.Count > 0 ? _all[0] : null;

    [Header("Default Shake")]
    [SerializeField] private float defaultIntensity = 0.08f;
    [SerializeField] private float defaultDuration = 0.1f;
    [SerializeField] private float frequency = 25f;

    [Header("Limits")]
    [SerializeField] private float maxIntensity = 0.3f;

    // ── Global user intensity setting (Options → Camera Shake) ───────────────
    // A multiplier applied to EVERY shake: 0 = off, 1 = normal (as authored),
    // MaxIntensityScale = the strongest the slider allows. Persisted to PlayerPrefs
    // so it survives sessions and is shared by every camera. The hard per-camera
    // maxIntensity above still acts as a safety ceiling AFTER this scale.
    public const float MaxIntensityScale = 2f;
    private const string IntensityScaleKey = "opt.cameraShake"; // stored value is the 0..2 multiplier
    private static float _intensityScale = 1f;
    private static bool _scaleLoaded = false;

    /// <summary>Current global shake multiplier (0 = off … 1 = normal … 2 = double).</summary>
    public static float IntensityScale { get { EnsureScaleLoaded(); return _intensityScale; } }

    /// <summary>Set + persist the global shake multiplier. Clamped to [0, MaxIntensityScale].</summary>
    public static void SetIntensityScale(float multiplier)
    {
        _intensityScale = Mathf.Clamp(multiplier, 0f, MaxIntensityScale);
        _scaleLoaded = true;
        PlayerPrefs.SetFloat(IntensityScaleKey, _intensityScale);
        PlayerPrefs.Save();
    }

    private static void EnsureScaleLoaded()
    {
        if (_scaleLoaded) return;
        _intensityScale = Mathf.Clamp(PlayerPrefs.GetFloat(IntensityScaleKey, 1f), 0f, MaxIntensityScale);
        _scaleLoaded = true;
    }

    private float currentIntensity = 0f;
    private float shakeDuration = 0f;
    private float shakeElapsed = 0f;
    private bool isShaking = false;
    private float seed;

    private Camera _cam;

    // Clear static registry between Play sessions (Enter Play Mode w/o domain reload).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _all.Clear();
        _scaleLoaded = false;   // force a fresh PlayerPrefs read next time it's needed
    }

    void Awake()
    {
        _cam = GetComponent<Camera>();
        seed = Random.value * 100f;
    }

    void OnEnable()
    {
        if (!_all.Contains(this)) _all.Add(this);
        if (_cam == null) _cam = GetComponent<Camera>();
    }

    void OnDisable()
    {
        _all.Remove(this);
        isShaking = false;
        currentIntensity = 0f;
        shakeElapsed = 0f;
    }

    // ── Static routing helpers ──────────────────────────────────────────────

    /// <summary>Shake EVERY registered camera (shared/global events: boss death, big impacts).</summary>
    public static void ShakeAll(float intensity = -1f, float duration = -1f)
    {
        EnsurePresent();
        for (int i = 0; i < _all.Count; i++)
            if (_all[i] != null) _all[i].Shake(intensity, duration);
    }

    // If a shake is requested but NO camera has registered one, lazily attach a
    // CameraShake to the main/active camera. This is the single-player safety net:
    // a scene whose Main Camera has neither PlayerCameraController nor a CameraShake
    // still shakes, with no editor wiring. In co-op the player cameras register in
    // Awake, so _all is already non-empty and this is a no-op.
    private static void EnsurePresent()
    {
        if (_all.Count > 0) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            var cams = Camera.allCameras;   // enabled cameras only
            if (cams != null && cams.Length > 0) cam = cams[0];
        }
        if (cam != null && cam.GetComponent<CameraShake>() == null)
            cam.gameObject.AddComponent<CameraShake>(); // Awake+OnEnable register it now
    }

    /// <summary>
    /// Shake only the camera that renders <paramref name="cam"/>. Returns false if
    /// that camera has no CameraShake registered (caller may fall back to ShakeAll).
    /// </summary>
    public static bool ShakeForCamera(Camera cam, float intensity = -1f, float duration = -1f)
    {
        if (cam == null) return false;
        for (int i = 0; i < _all.Count; i++)
        {
            if (_all[i] != null && _all[i]._cam == cam)
            {
                _all[i].Shake(intensity, duration);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Shake the given player's own screen half. Falls back to ShakeAll when the
    /// player/camera can't be resolved (single player, or an un-routed call site),
    /// so a shake is never silently dropped.
    /// </summary>
    public static void ShakeFor(PlayerRef player, float intensity = -1f, float duration = -1f)
    {
        if (player != null && ShakeForCamera(player.Camera, intensity, duration))
            return;
        ShakeAll(intensity, duration);
    }

    /// <summary>Immediately cancel shake on every camera.</summary>
    public static void StopAllShakes()
    {
        for (int i = 0; i < _all.Count; i++)
            if (_all[i] != null) _all[i].StopShake();
    }

    // ── Instance API (unchanged behaviour) ──────────────────────────────────

    public void Shake(float intensity = -1f, float duration = -1f)
    {
        if (intensity < 0f) intensity = defaultIntensity;
        if (duration < 0f) duration = defaultDuration;

        // Global user setting (Options → Camera Shake): 0 = off, 1 = normal, 2 = 2x.
        intensity *= IntensityScale;

        // Hard safety ceiling still applies after scaling.
        intensity = Mathf.Min(intensity, maxIntensity);

        // Shake disabled (or scaled to nothing) — nothing to do.
        if (intensity <= 0f) return;

        // If already shaking, stack — but only let the dominant shake control timing.
        if (isShaking)
        {
            if (intensity >= currentIntensity)
            {
                currentIntensity = Mathf.Min(currentIntensity + intensity * 0.5f, maxIntensity);
                shakeDuration = Mathf.Max(shakeDuration, duration);
                shakeElapsed = 0f;
            }
            // Otherwise: ignore. The bigger shake is already covering this hit.
            return;
        }

        currentIntensity = intensity;
        shakeDuration = duration;
        shakeElapsed = 0f;
        isShaking = true;
    }

    // Immediately cancels any active shake on THIS camera.
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

        // Add on TOP of whatever the follow/Cinemachine Brain just set. Because this
        // runs at execution order 1000, it happens after their LateUpdate.
        transform.position += offset;
    }
}
