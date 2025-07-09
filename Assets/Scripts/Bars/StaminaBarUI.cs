using UnityEngine;
using UnityEngine.UI;

public class StaminaBarUI : MonoBehaviour
{
    [SerializeField] private PlayerStats pstats;
    [SerializeField] private Image staminaFill;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (pstats != null && staminaFill != null)
        {
            float fill = pstats.currentStamina / pstats.maxStamina;
            staminaFill.fillAmount = Mathf.Clamp01(fill);
        }
    }
}
