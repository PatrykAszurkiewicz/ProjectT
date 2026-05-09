using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// DEBUG HELPER FOR BONUS SLOTS
//   1. Logs the slot's exact world position to the console
//   2. Adds a big blinking colored marker on top of the slot


public class BonusSlotDebugHighlighter : MonoBehaviour
{
    [Header("Marker Settings")]
    [SerializeField] private float markerScale = 4f;        // size relative to slot
    [SerializeField] private float pulseSpeed = 4f;         // how fast it blinks
    [SerializeField] private float minAlpha = 0.3f;
    [SerializeField] private float maxAlpha = 1.0f;
    [SerializeField] private float scalePulseAmount = 0.15f; // 15% size pulse

    [Header("Logging")]
    [SerializeField] private bool drawGizmoLine = true;

    // Color palette — one unique color per bonus slot
    private static readonly Color[] palette = new Color[]
    {
        new Color(1.0f, 0.0f, 0.0f, 1f),  // red
        new Color(1.0f, 0.5f, 0.0f, 1f),  // orange
        new Color(1.0f, 1.0f, 0.0f, 1f),  // yellow
        new Color(0.0f, 1.0f, 0.0f, 1f),  // green
        new Color(0.0f, 1.0f, 1.0f, 1f),  // cyan
        new Color(0.0f, 0.4f, 1.0f, 1f),  // blue
        new Color(0.6f, 0.0f, 1.0f, 1f),  // purple
        new Color(1.0f, 0.0f, 1.0f, 1f),  // magenta
        new Color(1.0f, 0.4f, 0.7f, 1f),  // pink
        new Color(0.5f, 1.0f, 0.5f, 1f),  // light green
        new Color(1.0f, 0.8f, 0.0f, 1f),  // gold
        new Color(0.3f, 0.7f, 1.0f, 1f),  // sky blue
    };

    private static readonly string[] paletteNames = new string[]
    {
        "RED", "ORANGE", "YELLOW", "GREEN", "CYAN", "BLUE",
        "PURPLE", "MAGENTA", "PINK", "LIGHT-GREEN", "GOLD", "SKY-BLUE"
    };

    private HashSet<TowerSlot> seen = new HashSet<TowerSlot>();
    private int colorCounter = 0;

    void Start()
    {
        // Poll for new bonus slots every 0.2s. Cheap and reliable.
        InvokeRepeating(nameof(ScanForNewBonusSlots), 0.1f, 0.2f);
    }

    void OnDestroy()
    {
        CancelInvoke(nameof(ScanForNewBonusSlots));
    }

    private void ScanForNewBonusSlots()
    {
        var allSlots = FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);
        foreach (var slot in allSlots)
        {
            if (slot == null || seen.Contains(slot)) continue;
            seen.Add(slot);

            // Bonus slots are named "BonusSlot_N" by TowerDefenseMap.AddBonusSlots()
            if (slot.gameObject.name.StartsWith("BonusSlot_"))
            {
                AttachMarker(slot);
            }
        }

        // Clean up dead refs (slots destroyed when a stage regenerates)
        seen.RemoveWhere(s => s == null);
    }

    private void AttachMarker(TowerSlot slot)
    {
        // Skip if a marker already exists (e.g. from a previous attach)
        if (slot.transform.Find("BONUS_DEBUG_MARKER") != null) return;

        int colorIdx = colorCounter % palette.Length;
        colorCounter++;

        Color baseColor = palette[colorIdx];
        string colorName = paletteNames[colorIdx];

        // Log
        Vector3 p = slot.transform.position;
        Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(baseColor)}>" +
                  $"[BONUS_SLOT_DEBUG] '{slot.gameObject.name}' marker = {colorName} " +
                  $"at world pos ({p.x:F2}, {p.y:F2}). " +
                  $"If you don't see a giant blinking {colorName} circle, position is OFF-SCREEN.</color>");

        // Build the marker
        GameObject marker = new GameObject("BONUS_DEBUG_MARKER");
        marker.transform.SetParent(slot.transform, worldPositionStays: true);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localScale = Vector3.one * markerScale;

        var sr = marker.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite();
        sr.color = baseColor;
        sr.sortingOrder = 32000; // on top of everything

        // Attach the pulser
        var pulser = marker.AddComponent<MarkerPulser>();
        pulser.Configure(baseColor, pulseSpeed, minAlpha, maxAlpha, scalePulseAmount);
    }

    private Sprite cachedCircleSprite;
    private Sprite CreateCircleSprite()
    {
        if (cachedCircleSprite != null) return cachedCircleSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                colors[y * size + x] = Vector2.Distance(new Vector2(x, y), center) <= radius ? Color.white : Color.clear;

        tex.SetPixels(colors);
        tex.Apply();
        cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
        return cachedCircleSprite;
    }

    void OnDrawGizmos()
    {
        if (!drawGizmoLine) return;

        var allSlots = FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);
        foreach (var slot in allSlots)
        {
            if (slot == null) continue;
            if (!slot.gameObject.name.StartsWith("BonusSlot_")) continue;

            Vector3 p = slot.transform.position;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(p, p + Vector3.up * 5f);
            Gizmos.DrawWireSphere(p, 1f);
        }
    }
}

// Pulses a SpriteRenderer's alpha + scale forever. Lives on the marker
// child GameObject created by BonusSlotDebugHighlighter.
public class MarkerPulser : MonoBehaviour
{
    private Color baseColor = Color.white;
    private float pulseSpeed = 4f;
    private float minAlpha = 0.3f;
    private float maxAlpha = 1.0f;
    private float scalePulseAmount = 0.15f;

    private SpriteRenderer sr;
    private Vector3 baseScale;

    public void Configure(Color color, float speed, float aMin, float aMax, float scalePulse)
    {
        baseColor = color;
        pulseSpeed = speed;
        minAlpha = aMin;
        maxAlpha = aMax;
        scalePulseAmount = scalePulse;
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
    }

    void Update()
    {
        if (sr == null) return;

        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;

        Color c = baseColor;
        c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
        sr.color = c;

        float s = 1f + (t - 0.5f) * 2f * scalePulseAmount;
        transform.localScale = baseScale * s;
    }
}
