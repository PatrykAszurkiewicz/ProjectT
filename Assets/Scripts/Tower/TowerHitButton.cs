using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TowerHitTester : MonoBehaviour
{
    [Header("Test Settings")]
    public float testDamage = 15f;
    public bool hitRandomTower = true;
    public bool hitNearestTower = false;
    public bool hitSpecificTower = false;
    public int specificRingIndex = 0;
    public int specificSlotIndex = 0;

    [Header("Test Options")]
    public bool useEnergyManagerDamage = true;
    public bool useDirectDamage = false;
    public bool hitCore = false;

    private Button testButton;
    private TowerDefenseMap mapGenerator;
    private TowerPlacementManager placementManager;

    void Start()
    {
        // Get the button component
        testButton = GetComponent<Button>();
        if (testButton != null)
        {
            testButton.onClick.AddListener(OnTestButtonClicked);
        }
        else
        {
            Debug.LogError("TowerHitTester: No Button component found!");
        }
        mapGenerator = FindFirstObjectByType<TowerDefenseMap>();
        placementManager = FindFirstObjectByType<TowerPlacementManager>();
    }

    public void OnTestButtonClicked()
    {
        Debug.Log("=== TOWER HIT TEST BUTTON CLICKED ===");

        if (hitCore)
        {
            HitCentralCore();
            return;
        }

        if (hitRandomTower)
        {
            HitRandomTower();
        }
        else if (hitNearestTower)
        {
            HitNearestTower();
        }
        else if (hitSpecificTower)
        {
            HitSpecificTower(specificRingIndex, specificSlotIndex);
        }
        else
        {
            HitFirstAvailableTower();
        }
    }

    void HitRandomTower()
    {
        var towers = FindAllTowersInScene();
        if (towers.Count == 0)
        {
            Debug.LogWarning("No towers found in scene to hit!");
            return;
        }

        // Pick a random tower
        int randomIndex = Random.Range(0, towers.Count);
        Tower targetTower = towers[randomIndex];

        Debug.Log($"Hitting RANDOM tower: {targetTower.towerName} at position {targetTower.transform.position}");
        HitTower(targetTower);
    }

    void HitNearestTower()
    {
        Vector3 centerPosition = Vector3.zero; // Use world center as reference
        if (mapGenerator != null && mapGenerator.HasCentralCore())
        {
            centerPosition = mapGenerator.GetCentralCore().transform.position;
        }

        var nearestConsumer = EnergyManager.Instance?.GetNearestEnergyConsumer(centerPosition);
        if (nearestConsumer is Tower tower)
        {
            Debug.Log($"Hitting NEAREST tower: {tower.towerName} at position {tower.transform.position}");
            HitTower(tower);
        }
        else
        {
            Debug.LogWarning("No nearest tower found!");
        }
    }

    void HitSpecificTower(int ringIndex, int slotIndex)
    {
        if (mapGenerator == null)
        {
            Debug.LogError("No TowerDefenseMap found!");
            return;
        }

        var slot = mapGenerator.GetSlot(ringIndex, slotIndex);
        if (slot == null)
        {
            Debug.LogWarning($"No slot found at Ring {ringIndex}, Slot {slotIndex}");
            return;
        }

        if (!slot.IsOccupied)
        {
            Debug.LogWarning($"Slot at Ring {ringIndex}, Slot {slotIndex} is not occupied!");
            return;
        }

        var tower = slot.currentTower?.GetComponent<Tower>();
        if (tower == null)
        {
            Debug.LogWarning($"No Tower component found in slot at Ring {ringIndex}, Slot {slotIndex}");
            return;
        }

        Debug.Log($"Hitting SPECIFIC tower: {tower.towerName} at Ring {ringIndex}, Slot {slotIndex}");
        HitTower(tower);
    }

    void HitFirstAvailableTower()
    {
        var towers = FindAllTowersInScene();
        if (towers.Count == 0)
        {
            Debug.LogWarning("No towers found in scene to hit!");
            return;
        }

        Tower targetTower = towers[0];
        Debug.Log($"Hitting FIRST tower: {targetTower.towerName} at position {targetTower.transform.position}");
        HitTower(targetTower);
    }

    void HitTower(Tower tower)
    {
        if (tower == null)
        {
            Debug.LogError("Tower is null!");
            return;
        }

        Debug.Log($"--- BEFORE HIT ---");
        Debug.Log($"Tower: {tower.towerName}");
        Debug.Log($"Energy: {tower.GetEnergy():F1}/{tower.GetMaxEnergy():F1}");
        Debug.Log($"Energy %: {tower.GetEnergyPercentage() * 100f:F1}%");
        Debug.Log($"Is Depleted: {tower.IsEnergyDepleted()}");
        Debug.Log($"Is Low: {tower.IsEnergyLow()}");

        if (useEnergyManagerDamage)
        {
            // Method 1: Use EnergyManager damage system
            bool success = EnergyManager.Instance?.DamageTower(tower, gameObject) ?? false;
            Debug.Log($"EnergyManager.DamageTower result: {success}");
        }
        else if (useDirectDamage)
        {
            // Method 2: Use IDamageable interface directly
            if (tower is IDamageable damageable)
            {
                bool wasDestroyed = damageable.TakeDamage(testDamage, gameObject);
                Debug.Log($"Direct damage result - Was destroyed: {wasDestroyed}");
            }
            else
            {
                Debug.LogError("Tower does not implement IDamageable!");
            }
        }
        else
        {
            // Method 3: Use EnergyManager's generic damage method
            bool success = EnergyManager.Instance?.DamageEnergyConsumer(tower, testDamage, gameObject) ?? false;
            Debug.Log($"EnergyManager.DamageEnergyConsumer result: {success}");
        }

        Debug.Log($"--- AFTER HIT ---");
        Debug.Log($"Energy: {tower.GetEnergy():F1}/{tower.GetMaxEnergy():F1}");
        Debug.Log($"Energy %: {tower.GetEnergyPercentage() * 100f:F1}%");
        Debug.Log($"Is Depleted: {tower.IsEnergyDepleted()}");
        Debug.Log($"Is Low: {tower.IsEnergyLow()}");
        Debug.Log($"Is Operational: {tower.IsOperational()}");
        Debug.Log("=== END TOWER HIT TEST ===");
    }

    void HitCentralCore()
    {
        if (mapGenerator == null || !mapGenerator.HasCentralCore())
        {
            Debug.LogWarning("No Central Core found!");
            return;
        }

        var core = mapGenerator.GetCentralCore();
        Debug.Log($"--- BEFORE HITTING CORE ---");
        Debug.Log($"Core Energy: {core.GetEnergy():F1}/{core.GetMaxEnergy():F1}");
        Debug.Log($"Core Energy %: {core.GetEnergyPercentage() * 100f:F1}%");
        Debug.Log($"Is Depleted: {core.IsEnergyDepleted()}");
        Debug.Log($"Is Low: {core.IsEnergyLow()}");

        if (useEnergyManagerDamage)
        {
            bool success = EnergyManager.Instance?.DamageCore(core, gameObject) ?? false;
            Debug.Log($"EnergyManager.DamageCore result: {success}");
        }
        else if (useDirectDamage)
        {
            if (core is IDamageable damageable)
            {
                bool wasDestroyed = damageable.TakeDamage(testDamage, gameObject);
                Debug.Log($"Direct core damage result - Was destroyed: {wasDestroyed}");
            }
        }
        else
        {
            bool success = EnergyManager.Instance?.DamageEnergyConsumer(core, testDamage, gameObject) ?? false;
            Debug.Log($"EnergyManager.DamageEnergyConsumer result: {success}");
        }

        Debug.Log($"--- AFTER HITTING CORE ---");
        Debug.Log($"Core Energy: {core.GetEnergy():F1}/{core.GetMaxEnergy():F1}");
        Debug.Log($"Core Energy %: {core.GetEnergyPercentage() * 100f:F1}%");
        Debug.Log($"Is Depleted: {core.IsEnergyDepleted()}");
        Debug.Log($"Is Low: {core.IsEnergyLow()}");
        Debug.Log("=== END CORE HIT TEST ===");
    }

    List<Tower> FindAllTowersInScene()
    {
        var towers = new List<Tower>();

        // TODO - check if finding towers via FindObject
        Tower[] allTowers = Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);
        towers.AddRange(allTowers);

        // TODO verify alternative search through slots 
        if (towers.Count == 0 && placementManager != null)
        {
            var allSlots = placementManager.GetAllSlots();
            foreach (var slot in allSlots)
            {
                if (slot.IsOccupied && slot.currentTower != null)
                {
                    var tower = slot.currentTower.GetComponent<Tower>();
                    if (tower != null)
                    {
                        towers.Add(tower);
                    }
                }
            }
        }

        Debug.Log($"Found {towers.Count} towers in scene");
        return towers;
    }

    // Public methods for UI buttons
    public void HitRandomTowerButton()
    {
        hitRandomTower = true;
        hitNearestTower = false;
        hitSpecificTower = false;
        hitCore = false;
        OnTestButtonClicked();
    }

    public void HitNearestTowerButton()
    {
        hitRandomTower = false;
        hitNearestTower = true;
        hitSpecificTower = false;
        hitCore = false;
        OnTestButtonClicked();
    }

    public void HitCoreButton()
    {
        hitRandomTower = false;
        hitNearestTower = false;
        hitSpecificTower = false;
        hitCore = true;
        OnTestButtonClicked();
    }

    public void HitSpecificTowerButton(int ringIndex, int slotIndex)
    {
        hitRandomTower = false;
        hitNearestTower = false;
        hitSpecificTower = true;
        hitCore = false;
        specificRingIndex = ringIndex;
        specificSlotIndex = slotIndex;
        OnTestButtonClicked();
    }
}