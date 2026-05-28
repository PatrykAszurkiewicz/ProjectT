using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private ResourceBarUI barUI;
    [SerializeField] private Vector3 offset = new Vector3(0, 1.5f, 0);

    private Transform target;
    private float maxHealth;
    private bool initialized = false;

    // Public read of the transform this bar is tracking. Used by
    // external systems (Scarecrow) to find their bar when EnemyStats.healthBar
    // is null — e.g. on prefabs that spawn the bar via a different path.
    public Transform Target => target;

    private void Awake()
    {
        // Hide until Initialize() is called with a valid target.
        // Prevents the bar from briefly appearing at world origin (0,0,0).
        if (barUI != null) barUI.gameObject.SetActive(false);
    }

    public void Initialize(Transform targetTransform, float maxHealth)
    {
        this.target = targetTransform;
        this.maxHealth = maxHealth;
        this.initialized = true;

        // Snap to the target's position
        if (targetTransform != null)
            transform.position = targetTransform.position + offset;

        if (barUI != null)
        {
            barUI.gameObject.SetActive(true);
            barUI.SetValue(maxHealth, maxHealth);
        }

        // Ensure the Canvas renders above grass Y-sort range (400-1600)
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = 4000;
        }
    }

    public void SetOffset(Vector3 newOffset)
    {
        offset = newOffset;
    }

    public void UpdateHealth(float currentHealth)
    {
        if (barUI != null)
            barUI.SetValue(currentHealth, maxHealth);
    }

    // Update the bar's maximum WITHOUT the full-bar flash that Initialize()
    // causes (Initialize calls SetValue(max, max)
    public void SetMaxHealth(float newMax, float currentHealth)
    {
        this.maxHealth = newMax;
        if (barUI != null)
            barUI.SetValue(currentHealth, newMax);
    }


    // Cleanly hide/show the bar. Used by support enemies (e.g. Scarecrow)
    // that have an invisible phase.
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    // Fade-friendly alpha. 
    public CanvasGroup EnsureCanvasGroup()
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        return cg;
    }

    private void LateUpdate()
    {
        // If the target was destroyed (or never assigned), clean ourselves up
        // instead of stranding the bar at world origin.
        if (target == null)
        {
            // Destroy in BOTH cases — initialized or not. 
            Destroy(gameObject);
            return;
        }

        transform.position = target.position + offset;
        transform.rotation = Quaternion.identity;
    }
}

