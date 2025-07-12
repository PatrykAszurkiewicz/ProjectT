using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private Vector3 offset;

    private Transform target;

    public void Initialize(Transform targetTransform, float maxHealth)
    {
        target = targetTransform;
        slider.maxValue = maxHealth;
        slider.value = maxHealth;
    }

    public void UpdateHealth(float currentHealth)
    {
        slider.value = currentHealth;
    }

    private void LateUpdate()
    {
        if (target != null)
        {
            transform.position = target.position + offset;
            transform.rotation = Quaternion.identity; // Zapobiega obracaniu
        }
    }
}
