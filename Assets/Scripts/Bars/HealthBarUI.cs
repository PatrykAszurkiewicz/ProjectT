using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    PlayerStats stats;
    public ResourceBarUI healthBarUI;
    private void Start()
    {
        stats = FindAnyObjectByType<PlayerStats>();
    }

    // Update is called once per frame
    void Update()
    {
        if (stats != null && healthBarUI != null)
        {
            healthBarUI.SetValue(stats.currentHealth, stats.maxHealth);
        }
    }
}
