using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class HealthBarParticleSync : MonoBehaviour
{
    [SerializeField] private RectTransform barRect;      // RectTransform ca³ego paska (FillHP lub jego rodzic)
    [SerializeField] private UnityEngine.UI.Image fillImage; // ten sam fillImage co w ResourceBarUI

    private ParticleSystem ps;
    private ParticleSystem.ShapeModule shapeModule;
    private ParticleSystem.EmissionModule emissionModule;

    private float fullWidth;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        shapeModule = ps.shape;
        emissionModule = ps.emission;

        // Szerokoœæ paska w lokalnych jednostkach (Scale jest 63.99 wg screena)
        fullWidth = shapeModule.scale.x;
    }

    private void LateUpdate()
    {
        float fill = fillImage.fillAmount;

        // Nowa szerokoœæ emitera proporcjonalna do fill
        float newWidth = fullWidth * fill;

        // Przesuniêcie w lewo o po³owê brakuj¹cego miejsca (¿eby by³ wyrównany do lewej)
        float offsetX = -(fullWidth - newWidth) / 2f;

        shapeModule.scale = new Vector3(newWidth, shapeModule.scale.y, shapeModule.scale.z);
        shapeModule.position = new Vector3(offsetX, shapeModule.position.y, shapeModule.position.z);

        // Opcjonalnie: wy³¹cz emisjê gdy pasek pusty
        emissionModule.enabled = fill > 0.01f;
    }
} 