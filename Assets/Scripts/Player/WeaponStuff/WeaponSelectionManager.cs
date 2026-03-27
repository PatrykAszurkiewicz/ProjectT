using UnityEngine;

public class WeaponSelectionManager : MonoBehaviour
{
    public static WeaponSelectionManager Instance;

    [Header("Fallback when nothing is selected")]
    public WeaponData DefaultWeapon;

    public WeaponData SelectedWeapon;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    public WeaponData GetChosenWeapon() => SelectedWeapon != null ? SelectedWeapon : DefaultWeapon;
}