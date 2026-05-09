using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ResourceBarUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Image shadowImage;
    [SerializeField] private float fadeWidth = 0.15f;

    [Header("Tuning")]
    [SerializeField] private float fillLerpSpeed = 10f;
    [SerializeField] private float shadowLerpSpeed = 2f;
    [SerializeField] private float shadowDelay = 0.5f;

    private Image blurImage;
    private Material fillMaterial;
    private Material shadowMaterial;
    private Material blurMaterial;
    private bool fillUsesShader = false;
    private bool shadowUsesShader = false;
    private bool blurUsesShader = false;

    private static readonly int FillProp = Shader.PropertyToID("_Fill");
    private static readonly int FadeWidthProp = Shader.PropertyToID("_FadeWidth");

    private float currentFill = 1f;
    private float targetFill = 1f;
    private float shadowFill = 1f;
    private float shadowTimer = 0f;
    private float previousValue = 1f;

    public float CurrentFill => currentFill;

    private void Awake()
    {
        blurImage = GetComponentsInChildren<Image>()
            .FirstOrDefault(img => img != fillImage && img != shadowImage && img.name.Contains("Blur"));

        fillMaterial = TrySetupShaderMaterial(fillImage, out fillUsesShader);
        shadowMaterial = TrySetupShaderMaterial(shadowImage, out shadowUsesShader);
        blurMaterial = TrySetupShaderMaterial(blurImage, out blurUsesShader);
    }

    private Material TrySetupShaderMaterial(Image img, out bool usesShader)
    {
        usesShader = false;
        if (img == null || img.material == null) return null;
        if (!img.material.HasProperty(FillProp)) return null;

        Material mat = new Material(img.material);
        img.material = mat;
        mat.SetFloat(FadeWidthProp, fadeWidth);
        mat.SetFloat(FillProp, 1f);
        usesShader = true;
        return mat;
    }

    public void SetValue(float current, float max)
    {
        if (max <= 0f) return;
        float newTarget = Mathf.Clamp01(current / max);
        if (Mathf.Approximately(newTarget, previousValue)) return;

        bool damaged = newTarget < previousValue;
        previousValue = newTarget;
        targetFill = newTarget;

        if (shadowImage != null)
        {
            if (damaged)
            {
                shadowFill = Mathf.Max(shadowFill, currentFill);
                ApplyShadow();
                shadowTimer = shadowDelay;
            }
            else
            {
                shadowFill = Mathf.Max(shadowFill, newTarget);
                ApplyShadow();
            }
        }
    }

    private void Update()
    {
        currentFill = Mathf.Lerp(currentFill, targetFill, Time.deltaTime * fillLerpSpeed);
        if (Mathf.Abs(currentFill - targetFill) < 0.001f) currentFill = targetFill;

        ApplyFillToImage(fillImage, fillMaterial, fillUsesShader, currentFill);
        ApplyFillToImage(blurImage, blurMaterial, blurUsesShader, currentFill);

        if (shadowImage != null)
        {
            if (shadowTimer > 0f)
            {
                shadowTimer -= Time.deltaTime;
            }
            else
            {
                shadowFill = Mathf.Lerp(shadowFill, targetFill, Time.deltaTime * shadowLerpSpeed);
                if (Mathf.Abs(shadowFill - targetFill) < 0.001f) shadowFill = targetFill;
                ApplyShadow();
            }
        }
    }

    private void ApplyFillToImage(Image img, Material mat, bool usesShader, float value)
    {
        if (img == null) return;
        if (usesShader && mat != null)
        {
            if (img.fillAmount != 1f) img.fillAmount = 1f;
            mat.SetFloat(FillProp, value);
        }
        else
        {
            img.fillAmount = value;
        }
    }

    private void ApplyShadow()
    {
        ApplyFillToImage(shadowImage, shadowMaterial, shadowUsesShader, shadowFill);
    }
}
