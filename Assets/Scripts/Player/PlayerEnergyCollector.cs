using UnityEngine;

public class PlayerEnergyCollector : MonoBehaviour
{
    [Header("Auto-Configuration")]
    public float collectionRadius = 1f;
    public bool enableCollectionEffects = true;

    // Statistics
    private int totalEnergyCollected = 0;
    private int totalDropsCollected = 0;

    // Events
    public System.Action<int> OnEnergyCollected;

    public void UpdateCollectionRadius(float newRadius)
    {
        collectionRadius = newRadius;

        // Update existing collider on player
        var existingCollider = GetComponent<CircleCollider2D>();
        if (existingCollider != null && existingCollider.isTrigger)
        {
            existingCollider.radius = newRadius;
            //Debug.Log($"[PlayerEnergyCollector] Updated player collider radius to {newRadius}");
            return;
        }

        // Update collider on child trigger object
        var triggerObj = transform.Find("EnergyCollectionTrigger");
        if (triggerObj != null)
        {
            var childCollider = triggerObj.GetComponent<CircleCollider2D>();
            if (childCollider != null)
            {
                childCollider.radius = newRadius;
                //Debug.Log($"[PlayerEnergyCollector] Updated child trigger collider radius to {newRadius}");
            }
        }
    }

    void Awake()
    {
        // Auto-setup collection trigger
        SetupCollectionTrigger();

        // Register with EnergyDropManager
        EnergyDropManager.RegisterPlayerCollector(this);
    }

    void SetupCollectionTrigger()
    {
        // Check if we already have a suitable collider
        var existingCollider = GetComponent<CircleCollider2D>();
        if (existingCollider != null && existingCollider.isTrigger)
        {
            existingCollider.radius = Mathf.Max(existingCollider.radius, collectionRadius);
            return;
        }

        // Create collection trigger as child object
        GameObject triggerObj = new GameObject("EnergyCollectionTrigger");
        triggerObj.transform.SetParent(transform);
        triggerObj.transform.localPosition = Vector3.zero;

        var collider = triggerObj.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = collectionRadius;

        // Add trigger handler
        triggerObj.AddComponent<EnergyCollectionTrigger>().collector = this;
    }

    public void OnEnergyDropCollected(int energyValue)
    {
        totalEnergyCollected += energyValue;
        totalDropsCollected++;
        OnEnergyCollected?.Invoke(energyValue);

        //Debug.Log($"Collected {energyValue} energy! Total: {totalEnergyCollected}");
    }

    public int GetTotalEnergyCollected() => totalEnergyCollected;
    public int GetTotalDropsCollected() => totalDropsCollected;

    void OnDestroy()
    {
        EnergyDropManager.UnregisterPlayerCollector(this);
    }
}

public class EnergyCollectionTrigger : MonoBehaviour
{
    [System.NonSerialized]
    public PlayerEnergyCollector collector;

    void OnTriggerEnter2D(Collider2D other)
    {
        var energyDrop = other.GetComponent<EnergyDrop>();
        if (energyDrop != null && collector != null)
        {
            energyDrop.CollectEnergy();
        }
    }
}