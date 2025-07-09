using UnityEngine;

public class HealthBar : MonoBehaviour
{
    [Header("Health Bar Settings")]
    public float width = 1f;
    public float height = 0.1f;
    public Color backgroundColor = Color.red;
    public Color foregroundColor = Color.green;
    public bool hideWhenFull = true;
    public bool alwaysFaceCamera = true;

    private SpriteRenderer backgroundRenderer;
    private SpriteRenderer foregroundRenderer;
    private Transform cameraTransform;

    void Awake()
    {
        cameraTransform = Camera.main?.transform;
    }

    void Update()
    {
        if (alwaysFaceCamera && cameraTransform != null)
        {
            transform.rotation = cameraTransform.rotation;
        }
    }

    public void Initialize(SpriteRenderer background, SpriteRenderer foreground)
    {
        backgroundRenderer = background;
        foregroundRenderer = foreground;

        SetupRenderers();
    }

    void SetupRenderers()
    {
        if (backgroundRenderer != null)
        {
            backgroundRenderer.color = backgroundColor;
        }

        if (foregroundRenderer != null)
        {
            foregroundRenderer.color = foregroundColor;
        }
    }

    public void UpdateHealthBar(float healthPercentage)
    {
        healthPercentage = Mathf.Clamp01(healthPercentage);

        // Hide/show based on health
        if (hideWhenFull && healthPercentage >= 1f)
        {
            gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(true);
        }

        // Update foreground
        if (foregroundRenderer != null)
        {
            Vector3 scale = foregroundRenderer.transform.localScale;
            scale.x = healthPercentage;
            foregroundRenderer.transform.localScale = scale;

            // Adjust position to keep left-aligned
            Vector3 position = foregroundRenderer.transform.localPosition;
            position.x = (healthPercentage - 1f) * 0.5f;
            foregroundRenderer.transform.localPosition = position;
        }
    }
}