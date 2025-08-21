using UnityEngine;

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private ResourceBarUI healthBarUI;

    private PlayerStats stats;

    private void Start()
    {
        stats = FindAnyObjectByType<PlayerStats>();
        if (stats != null && healthBarUI != null)
        {
            healthBarUI.SetValue(stats.currentHealth, stats.maxHealth);

            stats.OnHealthChanged += healthBarUI.SetValue;
        }
    }
    private void OnDestroy()
    {
        if (stats != null)
        {
            stats.OnHealthChanged -= healthBarUI.SetValue;
        }
    }
}
