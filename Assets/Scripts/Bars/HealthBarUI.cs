using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private PlayerStats pstats;
    [SerializeField] private Image healthFill;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float fill = pstats.currentHealth / pstats.maxHealth;
        healthFill.fillAmount = Mathf.Clamp01(fill);
    }
}
