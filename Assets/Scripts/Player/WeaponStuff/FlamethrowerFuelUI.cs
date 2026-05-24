using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Legacy standalone flamethrower fuel bar.
///
/// SUPERSEDED: the flamethrower's fuel level is now shown directly on its
/// WeaponRollUI hotbar icon (the icon depletes from the top down). This
/// separate on-screen bar is therefore redundant.
///
/// `useLegacyBar` is false by default, so this component does nothing — you
/// can safely delete the GameObject it sits on. Set it true only if you want
/// the old separate bar back instead of the icon gauge.
/// </summary>
public class FlamethrowerFuelUI : MonoBehaviour
{
    [Header("Legacy")]
    [Tooltip("OFF: the fuel level is shown on the WeaponRollUI icon instead " +
             "(recommended). ON: restores this old standalone bar.")]
    public bool useLegacyBar = false;

    [Header("Layout")]
    public Vector2 position = new Vector2(66f, 40f);
    public float barWidth = 110f;
    public float barHeight = 20f;

    [Header("Colours")]
    public Color fuelFull = new Color(1f, 0.6f, 0.1f, 0.9f);
    public Color fuelEmpty = new Color(0.4f, 0.1f, 0.05f, 0.9f);
    public Color barBackground = new Color(0.1f, 0.1f, 0.1f, 0.7f);

    private Canvas canvas;
    private Image bgImage;
    private Image fillImage;
    private RectTransform fillRect;

    private Weapon weapon;
    private bool uiBuilt;

    void Start()
    {
        weapon = FindFirstObjectByType<Weapon>();
    }

    void LateUpdate()
    {
        // Superseded by the WeaponRollUI icon gauge — do nothing (and make sure
        // any previously-built bar is hidden) unless explicitly re-enabled.
        if (!useLegacyBar)
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
            return;
        }

        if (weapon == null)
        {
            weapon = FindFirstObjectByType<Weapon>();
            if (weapon == null) return;
        }

        var data = weapon.GetWeaponData();
        bool isFlamethrower = data != null && data.isFlamethrower;

        if (isFlamethrower && !uiBuilt)
            BuildUI();

        if (canvas != null)
        {
            canvas.gameObject.SetActive(isFlamethrower);

            // Re-assert sorting order every frame the gauge is visible.
            // The weapon roll canvas (sortingOrder=100) can be rebuilt when
            // augments unlock new weapons, which may cause it to render on
            // top of us. Forcing 200 here guarantees we stay above it.
            if (isFlamethrower)
                canvas.sortingOrder = 200;
        }

        if (!isFlamethrower || fillImage == null) return;

        float fuel = GetFuelNormalized();
        fillRect.anchorMax = new Vector2(fuel, 1f);
        fillImage.color = Color.Lerp(fuelEmpty, fuelFull, fuel);
    }

    private float GetFuelNormalized()
    {
        if (weapon == null) return 0f;
        return weapon.GetFlamethrowerFuelNormalized();
    }

    private void BuildUI()
    {
        var go = new GameObject("FlamethrowerFuel_Canvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        go.AddComponent<CanvasScaler>();

        // Background
        var bgGo = new GameObject("FuelBG", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(canvas.transform, false);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = bgRect.anchorMax = bgRect.pivot = Vector2.zero;
        bgRect.anchoredPosition = position;
        bgRect.sizeDelta = new Vector2(barWidth, barHeight);
        bgImage = bgGo.GetComponent<Image>();
        bgImage.color = barBackground;

        // Fill
        var fillGo = new GameObject("FuelFill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(bgGo.transform, false);
        fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.one;
        fillRect.offsetMax = -Vector2.one;
        fillImage = fillGo.GetComponent<Image>();
        fillImage.color = fuelFull;

        uiBuilt = true;
    }

    void OnDestroy()
    {
        if (canvas != null)
            Destroy(canvas.gameObject);
    }
}
