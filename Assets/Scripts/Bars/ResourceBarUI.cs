using UnityEngine;
using UnityEngine.UI;

public class ResourceBarUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Image shadowImage;

    private float targetFill = 1f;
    private float lerpSpeed = 10f;

    private float shadowLerpSpeed = 2f;
    private float shadowDelay = 0.5f;
    private float shadowTimer = 0f;

    private float previousValue = 1f;
    private bool hasShadow => shadowImage != null;

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

        fillImage.fillAmount = Mathf.Lerp(fillImage.fillAmount, targetFill, Time.deltaTime * lerpSpeed);

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
