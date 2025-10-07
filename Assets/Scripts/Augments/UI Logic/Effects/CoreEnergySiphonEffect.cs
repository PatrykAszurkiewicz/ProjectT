using UnityEngine;

public class CoreEnergySiphonEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float siphonPercentage = 0f; // Set by augment system from CSV

    public float minimumEnergyThreshold = 0.15f; // Don't siphon below 15% energy

    private CentralCore core;

    void Awake()
    {
        core = GetComponent<CentralCore>();
        if (core == null)
        {
            Debug.LogError("[CORE_SIPHON] CoreEnergySiphonEffect requires CentralCore component");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        core.OnDamageTaken += OnCoreDamaged;
        //Debug.Log($"[CORE_SIPHON] Effect started with {siphonPercentage*100}% siphon rate");
    }

    void OnDestroy()
    {
        if (core != null)
        {
            core.OnDamageTaken -= OnCoreDamaged;
        }
    }

    private void OnCoreDamaged(float damage, GameObject source)
    {
        if (core == null || core.IsDestroyed()) return;

        // Don't siphon if core energy is too low 
        float energyPercentage = core.GetEnergyPercentage();
        if (energyPercentage <= minimumEnergyThreshold)
        {
            //Debug.Log($"[CORE_SIPHON] Core energy too low ({energyPercentage*100:F1}%), siphon disabled");
            return;
        }

        // Convert damage to energy (percentage comes from CSV via augment system)
        float energyToRestore = damage * siphonPercentage;

        // Don't restore more than what would bring energy above max energy
        float currentEnergy = core.GetEnergy();
        float maxEnergy = core.GetMaxEnergy();
        energyToRestore = Mathf.Min(energyToRestore, maxEnergy - currentEnergy);

        if (energyToRestore > 0f)
        {
            // Restore energy to core
            core.SupplyEnergy(energyToRestore);
            //Debug.Log($"[CORE_SIPHON] Core took {damage} damage, siphoned {energyToRestore} energy back");
        }
    }
}