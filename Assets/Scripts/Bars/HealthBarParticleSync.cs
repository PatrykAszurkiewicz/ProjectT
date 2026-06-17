using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(ParticleSystem))]
public class HealthBarParticleSync : MonoBehaviour
{
    [SerializeField] private RectTransform barRect;
    [SerializeField] private Image fillImage;
    [SerializeField] private ResourceBarUI bar;

    private ParticleSystem ps;
    private ParticleSystem.Particle[] particleBuffer;
    private float fullWidth;
    private bool ready;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        if (ps != null)
        {
            // Read the initial width ONCE from a valid PS (a float copy is safe to keep).
            fullWidth = ps.shape.scale.x;
            ready = true;
        }

        if (bar == null && fillImage != null)
            bar = fillImage.GetComponentInParent<ResourceBarUI>();
    }

    private void LateUpdate()
    {
        // Guard: PS destroyed, not yet initialized, or this object is inactive.
        if (!ready || ps == null) return;

        float fill = bar != null
            ? bar.CurrentFill
            : (fillImage != null ? fillImage.fillAmount : 1f);

        // Fetch the modules FRESH every frame. Never cache a ShapeModule/
        // EmissionModule in a field — those structs hold a back-pointer to the PS that goes
        // stale when the PS is disabled/re-enabled/recreated, and using a stale one throws
        // "Do not create your own module instances, get them from a ParticleSystem instance".
        var shape = ps.shape;
        var emission = ps.emission;

        float newWidth = fullWidth * fill;
        float offsetX = -(fullWidth - newWidth) / 2f;

        shape.scale = new Vector3(newWidth, shape.scale.y, shape.scale.z);
        shape.position = new Vector3(offsetX, shape.position.y, shape.position.z);

        emission.enabled = fill > 0.01f;

        float rightEdgeX = shape.position.x + shape.scale.x * 0.5f;

        int count = ps.particleCount;
        if (count > 0)
        {
            int max = Mathf.Max(count, ps.main.maxParticles);
            if (particleBuffer == null || particleBuffer.Length < max)
                particleBuffer = new ParticleSystem.Particle[max];

            int alive = ps.GetParticles(particleBuffer);

            bool changed = false;
            for (int i = 0; i < alive; i++)
            {
                if (particleBuffer[i].position.x > rightEdgeX)
                {
                    particleBuffer[i].remainingLifetime = 0f;
                    changed = true;
                }
            }
            if (changed) ps.SetParticles(particleBuffer, alive);
        }
    }
}
