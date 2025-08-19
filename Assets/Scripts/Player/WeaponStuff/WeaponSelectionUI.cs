using UnityEngine;

public class WeaponSelectionUI : MonoBehaviour
{
    [SerializeField] private WeaponData swordData;
    [SerializeField] private WeaponData shieldData;
    [SerializeField] private WeaponData gunData;

    public void ChooseSword()
    {
        WeaponSelectionManager.Instance.SelectedWeapon = swordData;
    }

    public void ChooseShield()
    {
        WeaponSelectionManager.Instance.SelectedWeapon = shieldData;
    }

    public void ChooseGun()
    {
        WeaponSelectionManager.Instance.SelectedWeapon = gunData;
    }
}
