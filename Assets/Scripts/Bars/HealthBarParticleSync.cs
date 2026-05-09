using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(ParticleSystem))]
public class HealthBarParticleSync : MonoBehaviour
{
    [SerializeField] private RectTransform barRect;
    [SerializeField] private Image fillImage;
    [SerializeField] private ResourceBarUI bar;

    private ParticleSystem ps;
    private ParticleSystem.ShapeModule shapeModule;
    private ParticleSystem.EmissionModule emissionModule;
    private ParticleSystem.Particle[] particleBuffer;

    private float fullWidth;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        shapeModule = ps.shape;
        emissionModule = ps.emission;
        fullWidth = shapeModule.scale.x;

        if (bar == null && fillImage != null)
            bar = fillImage.GetComponentInParent<ResourceBarUI>();
    }

    private void LateUpdate()
    {
        float fill = bar != null
            ? bar.CurrentFill
            : (fillImage != null ? fillImage.fillAmount : 1f);

        float newWidth = fullWidth * fill;
        float offsetX = -(fullWidth - newWidth) / 2f;

        shapeModule.scale = new Vector3(newWidth, shapeModule.scale.y, shapeModule.scale.z);
        shapeModule.position = new Vector3(offsetX, shapeModule.position.y, shapeModule.position.z);

        emissionModule.enabled = fill > 0.01f;

        float rightEdgeX = shapeModule.position.x + shapeModule.scale.x * 0.5f;

        int count = ps.particleCount;
        if (count > 0)
        {
            int max = Mathf.Max(count, ps.main.maxParticles);
            if (particleBuffer == null || particleBuffer.Length < max)
                particleBuffer = new ParticleSystem.Particle[max];

            int alive = ps.GetParticles(particleBuffer);

            // DEBUG: print every ~30 frames
            if (Time.frameCount % 30 == 0 && alive > 0)
            {
                /*
                Debug.Log($"[ParticleSync] fill={fill:F2}, fullWidth={fullWidth:F2}, " +
                          $"shape.scale.x={shapeModule.scale.x:F2}, shape.pos.x={shapeModule.position.x:F2}, " +
                          $"rightEdgeX={rightEdgeX:F2}, " +
                          $"first particle.position={particleBuffer[0].position}, " +
                          $"simSpace={ps.main.simulationSpace}, scalingMode={ps.main.scalingMode}");
                          */
            }

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
