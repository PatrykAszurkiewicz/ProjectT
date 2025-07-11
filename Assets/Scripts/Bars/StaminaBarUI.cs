using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;
using UnityEngine.UI;

public class StaminaBarUI : MonoBehaviour
{
    public ResourceBarUI staminaBarUI;
    private PlayerStats pstats;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        pstats = FindAnyObjectByType<PlayerStats>();
    }

    // Update is called once per frame
    void Update()
    {
        if (pstats != null && staminaBarUI != null)
        {
            staminaBarUI.SetValue(pstats.currentStamina, pstats.maxStamina);
        }
    }
}
