using UnityEngine;

public class WeaponSelectionManager : MonoBehaviour
{
    public static WeaponSelectionManager Instance;

    public WeaponData SelectedWeapon; // Save selected weapon
    public WeaponData DefaultWeapon;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Work between scenes
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public WeaponData GetChosenWeapon()
    {
        return SelectedWeapon != null ? SelectedWeapon : DefaultWeapon;
    }
}
