using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ResourceBarUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Image shadowImage;
    [SerializeField] private float fadeWidth = 0.05f;

    private Image blurImage;
    private Material fillMaterial;
    private bool useShader = false;
    private static readonly int FillProp = Shader.PropertyToID("_Fill");
    private static readonly int FadeWidthProp = Shader.PropertyToID("_FadeWidth");

    private float currentFill = 1f;
    private float targetFill = 1f;
    private float lerpSpeed = 10f;
    private float shadowLerpSpeed = 2f;
    private float shadowDelay = 0.5f;
    private float shadowTimer = 0f;
    private float previousValue = 1f;
    private bool hasShadow => shadowImage != null;

    private void Awake()
    {
        blurImage = GetComponentsInChildren<Image>()
            .FirstOrDefault(img => img.name.Contains("Blur"));

        // Shader działa tylko jeśli materiał ma właściwość _Fill
        if (fillImage.material != null && fillImage.material.HasProperty(FillProp))
        {
            fillMaterial = fillImage.material = new Material(fillImage.material);
            fillMaterial.SetFloat(FadeWidthProp, fadeWidth);
            fillMaterial.SetFloat(FillProp, 1f);
            useShader = true;
        }
    }

    public void SetValue(float current, float max)
    {
        float newTarget = Mathf.Clamp01(current / max);
        if (!Mathf.Approximately(newTarget, previousValue))
        {
            previousValue = newTarget;
            targetFill = newTarget;
            if (hasShadow)
            {
                shadowTimer = shadowDelay;
            }
        }
    }

    private void Update()
    {
        currentFill = Mathf.Lerp(currentFill, targetFill, Time.deltaTime * lerpSpeed);

        if (useShader)
        {
            fillImage.fillAmount = 1f;
            fillMaterial.SetFloat(FillProp, currentFill);
        }
        else
        {
            fillImage.fillAmount = currentFill;
        }

        if (blurImage != null)
        {
            blurImage.fillAmount = currentFill;
        }

        if (hasShadow)
        {
            if (shadowTimer > 0)
            {
                shadowTimer -= Time.deltaTime;
            }
            else
            {
                shadowImage.fillAmount = Mathf.Lerp(shadowImage.fillAmount, targetFill, Time.deltaTime * shadowLerpSpeed);
            }
        }
    }
}