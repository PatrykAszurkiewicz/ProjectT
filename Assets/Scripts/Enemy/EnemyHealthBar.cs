using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Vector3 offset = new Vector3(0, 1f, 0);

    private Transform target;
    private float maxHealth = 100f;

    public void Initialize(Transform targetTransform, float maxHealth)
    {
        this.target = targetTransform;
        this.maxHealth = maxHealth;
        UpdateHealth(maxHealth);
    }
    public void UpdateHealth(float currentHealth)
    {
        if (fillImage != null && maxHealth > 0)
        {
            fillImage.fillAmount = currentHealth / maxHealth;
        }
    }

    private void LateUpdate()
    {
        if (target != null)
        {
            transform.position = target.position + offset;
            transform.rotation = Quaternion.identity;
        }
    }
}
