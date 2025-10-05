using UnityEngine;

public class EnergyVampireTouchEffect : MonoBehaviour
{
    [System.NonSerialized]
    public int drainAmount = 0;

    public void DrainEnergy()
    {
        if (drainAmount > 0 && EnergyManager.Instance != null)
        {
            EnergyManager.Instance.GivePlayerEnergy(drainAmount);
        }
    }
}