using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private ResourceBarUI barUI;
    [SerializeField] private Vector3 offset = new Vector3(0, 1.5f, 0);

    private Transform target;
    private float maxHealth;

    public void Initialize(Transform targetTransform, float maxHealth)
    {
        this.target = targetTransform;
        this.maxHealth = maxHealth;

        barUI.SetValue(maxHealth, maxHealth);
    }
    public void UpdateHealth(float currentHealth)
    {
        barUI.SetValue(currentHealth, maxHealth);
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
