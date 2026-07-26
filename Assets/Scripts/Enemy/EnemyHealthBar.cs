using UnityEngine;
using UnityEngine.UI;

// World-space follower for an enemy's health bar.
public class EnemyHealthBar : MonoBehaviour
{
    [Header("Bar source (optional - Resources fallback below covers it)")]
    [Tooltip("The EnemyBar prefab. Leave empty to load it from Resources instead.")]
    [SerializeField] private GameObject barPrefab;
    [Tooltip("Resources path used when Bar Prefab is empty. The path is relative to the " +
             "Resources folder and has no extension: " +
             "Assets/Resources/Sprites/HUD/EnemyHP/EnemyBar.prefab -> " +
             "\"Sprites/HUD/EnemyHP/EnemyBar\". If this misses, the prefab is searched for " +
             "by name instead, so a wrong path here is not fatal.")]
    [SerializeField] private string barPrefabResourcePath = "Sprites/HUD/EnemyHP/EnemyBar";

    [Header("Already-wired bar (leave empty - resolved automatically)")]
    [SerializeField] private EnemyBarUI enemyBar;
    [Tooltip("Legacy simple bar. Destroyed at runtime once the new bar is built, " +
             "unless Replace Legacy Bar is off.")]
    [SerializeField] private ResourceBarUI barUI;
    [SerializeField] private bool replaceLegacyBar = true;

    [SerializeField] private Vector3 offset = new Vector3(0, 1.5f, 0);

    [Header("Canvas (only used if this object has no Canvas above it)")]
    [SerializeField] private float fallbackCanvasScale = 0.01f;

    [Header("Death")]
    [Tooltip("Fade the bar out instead of vanishing with the enemy. Note EnemyStats " +
             "calls Destroy(healthBar.gameObject) on death, so this only fires when the " +
             "target disappears some other way.")]
    [SerializeField] private bool fadeOutOnTargetLost = false;
    [SerializeField] private float fadeOutDuration = 0.2f;

    private Transform target;
    private float maxHealth = 1f;
    private bool initialized = false;
    private bool leaving = false;

    // Public read of the transform this bar is tracking. Used by
    // external systems (Scarecrow) to find their bar when EnemyStats.healthBar
    // is null — e.g. on prefabs that spawn the bar via a different path.
    public Transform Target => target;

    // The root GameObject of whichever bar implementation ended up in use.
    private GameObject BarObject
    {
        get
        {
            if (enemyBar != null) return enemyBar.gameObject;
            if (barUI != null) return barUI.gameObject;
            return null;
        }
    }

    private void Awake()
    {
        EnsureCanvas();

        if (enemyBar == null) enemyBar = GetComponentInChildren<EnemyBarUI>(true);
        if (barUI == null) barUI = GetComponentInChildren<ResourceBarUI>(true);

        // Nothing new present: build it, then clear whatever was drawing before.
        if (enemyBar == null)
        {
            enemyBar = BuildBar();
            if (enemyBar != null && replaceLegacyBar)
            {
                PurgeLegacyArt(enemyBar.transform);
                barUI = null;
            }
        }

        // Hide until Initialize() is called with a valid target.
        // Prevents the bar from briefly appearing at world origin (0,0,0).
        var bar = BarObject;
        if (bar != null) bar.SetActive(false);
    }

    // Instantiate the EnemyBar prefab under this object and attach the driver.
    private EnemyBarUI BuildBar()
    {
        GameObject prefab = barPrefab != null ? barPrefab : ResolveBarPrefab(barPrefabResourcePath);

        if (prefab == null)
        {
            // Not an error: a prefab that still uses ResourceBarUI keeps working.
            if (barUI == null)
            {
                Debug.LogWarning("[EnemyHealthBar] No bar found on '" + name + "' and no EnemyBar " +
                                 "prefab to build one from. It must live somewhere under an " +
                                 "Assets/Resources folder, or be assigned to 'Bar Prefab' in the " +
                                 "inspector.", this);
            }
            return null;
        }

        var go = Instantiate(prefab, transform, false);
        go.name = "EnemyBar";

        // Inactive first, so EnemyBarUI.Awake runs when we activate in
        // Initialize() - after the canvas and parenting are settled.
        go.SetActive(false);

        var ui = go.GetComponent<EnemyBarUI>();
        if (ui == null) ui = go.AddComponent<EnemyBarUI>();
        return ui;
    }

    // Resolved once per play session and shared by every enemy - a Resources
    // lookup per spawned enemy would be wasteful, and the fallback scan doubly so.
    private static GameObject _cachedPrefab;
    private static bool _fallbackSearched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _cachedPrefab = null;
        _fallbackSearched = false;
    }

    // Tries the configured path, then the handful of places this prefab has
    // plausibly lived, then gives up and searches Resources by name. The last
    // step is slow, so it runs at most once and says so.
    private static GameObject ResolveBarPrefab(string configuredPath)
    {
        if (_cachedPrefab != null) return _cachedPrefab;

        string[] candidates =
        {
            configuredPath,
            "Sprites/HUD/EnemyHP/EnemyBar",
            "UI/EnemyBar",
            "EnemyBar"
        };

        foreach (var path in candidates)
        {
            if (string.IsNullOrEmpty(path)) continue;
            var found = Resources.Load<GameObject>(path);
            if (found != null)
            {
                _cachedPrefab = found;
                return _cachedPrefab;
            }
        }

        if (_fallbackSearched) return null;
        _fallbackSearched = true;

        // Last resort: the prefab moved and nobody updated the path. Find it by
        // name so the bars still work, and log where it actually is.
        foreach (var go in Resources.LoadAll<GameObject>(""))
        {
            if (go == null || go.name != "EnemyBar") continue;
            _cachedPrefab = go;
            Debug.LogWarning("[EnemyHealthBar] EnemyBar was not at \"" + configuredPath +
                             "\" but was found by name. Update 'Bar Prefab Resource Path' to " +
                             "skip this scan on future runs.");
            break;
        }

        return _cachedPrefab;
    }

    // Kill everything that was drawing the OLD bar, whatever shape it had.
    //
    // The previous version of this only deactivated the object holding the
    // ResourceBarUI component, which left the art behind on any prefab where
    // the fill/frame sat on the root or on sibling objects - the "two bars
    // stacked" symptom. So instead of guessing, this removes every child that
    // existed before we built the new bar, and disables any Graphic sitting
    // directly on this object.
    //
    // Only ever runs when we built the bar ourselves, i.e. exactly once during
    // the migration off ResourceBarUI.
    private void PurgeLegacyArt(Transform newBar)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == newBar) continue;

            // Never take down the canvas the bar is drawn on, and never remove
            // another EnemyBarUI someone deliberately placed.
            if (child.GetComponentInChildren<Canvas>(true) != null) continue;
            if (child.GetComponentInChildren<EnemyBarUI>(true) != null) continue;

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        // Old art drawn straight on the root object (Image/RawImage/Text).
        foreach (var g in GetComponents<Graphic>()) g.enabled = false;

        // The component itself, if it lived on the root and so survived above.
        if (barUI != null) barUI.enabled = false;
    }

    // The bar is Instantiate()d with no parent by EnemyStats, so it must carry
    // its own canvas. The existing prefab has one; this only covers the case
    // where it doesn't.
    private void EnsureCanvas()
    {
        if (GetComponentInParent<Canvas>() != null) return;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        gameObject.AddComponent<CanvasScaler>();

        var rt = transform as RectTransform;
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(100f, 100f);
        rt.localScale = new Vector3(fallbackCanvasScale, fallbackCanvasScale, 1f);
    }


    /// currentHealth < 0 means "assume full", which keeps the existing
    /// two-argument call in EnemyStats.Start working unchanged.
    public void Initialize(Transform targetTransform, float maxHealth, float currentHealth = -1f)
    {
        this.target = targetTransform;
        this.maxHealth = Mathf.Max(0.0001f, maxHealth);
        this.initialized = true;

        float hp = currentHealth < 0f ? this.maxHealth : currentHealth;

        // Snap to the target's position
        if (targetTransform != null)
            transform.position = targetTransform.position + offset;

        // Ensure the Canvas renders above grass Y-sort range (400-1600)
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = 4000;
        }

        var bar = BarObject;
        if (bar != null) bar.SetActive(true);   // EnemyBarUI.Awake runs here

        // SnapTo, not SetValue: no drain animation on spawn.
        if (enemyBar != null)
        {
            enemyBar.RefreshLayout();   // canvas scale is only final now
            enemyBar.SnapTo(hp, this.maxHealth);
        }
        else if (barUI != null)
        {
            barUI.SetValue(hp, this.maxHealth);
        }
    }

    public void SetOffset(Vector3 newOffset)
    {
        offset = newOffset;
    }

    public void UpdateHealth(float currentHealth)
    {
        if (enemyBar != null) enemyBar.SetValue(currentHealth, maxHealth);
        else if (barUI != null) barUI.SetValue(currentHealth, maxHealth);
    }

    // Update the bar's maximum WITHOUT the full-bar flash that Initialize()
    // causes (Initialize calls SetValue(max, max)
    public void SetMaxHealth(float newMax, float currentHealth)
    {
        this.maxHealth = Mathf.Max(0.0001f, newMax);
        if (enemyBar != null) enemyBar.SetValue(currentHealth, this.maxHealth);
        else if (barUI != null) barUI.SetValue(currentHealth, this.maxHealth);
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
            if (leaving) return;

            if (fadeOutOnTargetLost && enemyBar != null)
            {
                leaving = true;
                enemyBar.FadeOutAndDestroy(fadeOutDuration);
                Destroy(gameObject, fadeOutDuration + 0.05f);
                return;
            }

            // Destroy in BOTH cases — initialized or not. 
            Destroy(gameObject);
            return;
        }

        transform.position = target.position + offset;
        transform.rotation = Quaternion.identity;
    }
}



