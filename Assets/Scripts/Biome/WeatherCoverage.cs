using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class WeatherCoverage : MonoBehaviour
{
    [Tooltip("Camera to cover. Leave empty to use Camera.main.")]
    public Camera targetCamera;

    [Tooltip("Coverage beyond the exact view. 1.3 = 30% margin.")]
    public float margin = 1.3f;

    [Tooltip("Grow emission rate + max particles with the box area so density stays constant.")]
    public bool keepDensity = true;

    private ParticleSystem ps;
    private float baseRate;
    private int baseMax;
    private float baseArea;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        baseRate = ps.emission.rateOverTime.constant;
        baseMax = ps.main.maxParticles;

        var sh = ps.shape;
        // Calculate initial area based on X and Y scale
        baseArea = Mathf.Max(0.01f, Mathf.Abs(sh.scale.x * sh.scale.y));
    }

    void LateUpdate()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        // Force orthographic calculation regardless of camera mode for 2D games
        float h = cam.orthographicSize * 2f * margin;
        float w = h * cam.aspect;

        // If the camera is Perspective, hardcode a fallback size based on your map height
        if (!cam.orthographic)
        {
            h = 30f * margin; // Adjust 30f to roughly match your screen height in world units
            w = h * cam.aspect;
        }

        var sh = ps.shape;
        sh.scale = new Vector3(w, h, sh.scale.z);

        if (keepDensity)
        {
            float areaScale = (w * h) / baseArea;
            var em = ps.emission;
            em.rateOverTime = baseRate * areaScale;
            var main = ps.main;
            main.maxParticles = Mathf.CeilToInt(baseMax * areaScale);
        }
    }
}
