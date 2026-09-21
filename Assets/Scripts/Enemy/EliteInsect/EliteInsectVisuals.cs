using UnityEngine;

// EliteInsectVisuals
[DisallowMultipleComponent]
public class EliteInsectVisuals : MonoBehaviour, ISpritePrewarm
{
    // Auto-prewarm hook (GameOrchestrator finds this on the prefab). Warms the Elite
    // burrow folders through the shared cache so the first Elite doesn't stall.
    public void PrewarmSpriteFolders()
    {
        // Nothing to warm. The burrow frames are DIRECT references (diveFrames et al.)
        // assigned on this prefab, so Unity loaded them with the scene. The Resources
        // warm that used to live here relied on
        // EnemyAnimationController.LoadFolderCached, which has been deleted along with
        // the rest of the folder-loading system.
        if (diveFrames == null || diveFrames.Length == 0)
            Debug.LogWarning($"[EliteInsectVisuals] '{name}' has no Dive Frames assigned. " +
                             "Assign the burrow clips on this component — there is no longer " +
                             "a Resources fallback.");
    }

    [Header("Elite burrow sprite folders (relative to a Resources/ root)")]
    // Direct references (preferred). EliteInsect's burrow art lives on this component,
    // not on EnemyData, so the EnemyData migration tool cannot reach it — assign these
    // by hand on the EliteInsect prefab before moving EliteInsect/ out of Resources.
    [Header("Sprite frames (direct references — preferred)")]
    [SerializeField] private Sprite[] diveFrames;
    [SerializeField] private Sprite[] undergroundFrames;
    [SerializeField] private Sprite[] emergeFrames;
    [SerializeField] private Sprite[] attackFrames;

    [Header("LEGACY Resource folders (fallback only)")]
    [SerializeField] private string diveFolder = "Sprites/EnemySprites/EliteInsect/FromAboveToUnder";
    [SerializeField] private string undergroundFolder = "Sprites/EnemySprites/EliteInsect/MovingUnderground";
    [SerializeField] private string emergeFolder = "Sprites/EnemySprites/EliteInsect/FromUnderToAbove";
    [SerializeField] private string attackFolder = "Sprites/EnemySprites/EliteInsect/Attacking";

    [Header("Pulsating red aura")]
    [SerializeField] private Color auraColor = new Color(1f, 0.16f, 0.12f, 1f);

    [Tooltip("Aura diameter as a multiple of the insect sprite's on-screen height.")]
    [SerializeField] private float auraSizeFactor = 1.8f;

    [Tooltip("Pulses per second.")]
    [SerializeField] private float pulseSpeed = 2.2f;

    [Tooltip("Aura alpha at the dimmest / brightest point of the pulse.")]
    [Range(0f, 1f)][SerializeField] private float minAlpha = 0.22f;
    [Range(0f, 1f)][SerializeField] private float maxAlpha = 0.6f;

    [Tooltip("How much the aura grows/shrinks over a pulse, as a fraction of its base size.")]
    [Range(0f, 0.5f)][SerializeField] private float scalePulse = 0.12f;

    [Tooltip("Sorting-order offset relative to the insect sprite. Negative = behind it.")]
    [SerializeField] private int auraSortingOffset = -1;

    private SpriteRenderer insectRenderer;
    private EnemyAnimationController enemyAnim;

    private SpriteRenderer auraRenderer;
    private Transform auraTransform;
    private float baseAuraScale = 1f;
    private float phase;

    private void Awake()
    {
        // Repoint (or create + point) the burrow animator at the Elite folders
        // BEFORE InsectController.Start() reads it. If the animator isn't present
        // yet we add it here; otherwise InsectController would add one in Start()
        // with the default Insect folders.
        var anim = GetComponent<InsectAnimator>();
        if (anim == null) anim = gameObject.AddComponent<InsectAnimator>();
        anim.Configure(diveFolder, undergroundFolder, emergeFolder, attackFolder,
                       diveFrames, undergroundFrames, emergeFrames, attackFrames);

        insectRenderer = GetComponent<SpriteRenderer>();
        enemyAnim = GetComponent<EnemyAnimationController>();
    }

    private void Start()
    {
        BuildAura();
    }

    private void BuildAura()
    {
        float spriteHeight = (insectRenderer != null && insectRenderer.sprite != null)
            ? Mathf.Max(0.25f, insectRenderer.bounds.size.y)   // world-space height
            : 1f;

        var go = new GameObject("EliteAura");
        auraTransform = go.transform;
        auraTransform.SetParent(transform, false);
        auraTransform.localPosition = Vector3.zero;

        auraRenderer = go.AddComponent<SpriteRenderer>();
        auraRenderer.sprite = GetGlowSprite();
        auraRenderer.color = auraColor;

        if (insectRenderer != null)
        {
            auraRenderer.sortingLayerID = insectRenderer.sortingLayerID;
            auraRenderer.sortingOrder = insectRenderer.sortingOrder + auraSortingOffset;
        }

        // The generated glow sprite is exactly 1 world unit across at localScale 1.
        // We want a WORLD size of (spriteHeight * auraSizeFactor), independent of the
        // insect's own transform scale (the prefab is scaled to 0.25), so divide the
        // parent's lossy scale back out. localScale then carries the pulse.
        float desiredWorld = spriteHeight * Mathf.Max(0.1f, auraSizeFactor);
        float parentScale = Mathf.Abs(transform.lossyScale.y);
        if (parentScale < 0.0001f) parentScale = 1f;

        baseAuraScale = desiredWorld / parentScale;
        auraTransform.localScale = Vector3.one * baseAuraScale;
    }

    private void LateUpdate()
    {
        if (auraRenderer == null) return;

        // While the enemy is dying, fade the aura out cleanly so it doesn't hang
        // over the death animation / disintegration VFX.
        if (enemyAnim != null && enemyAnim.IsDying)
        {
            Color dead = auraRenderer.color;
            dead.a = Mathf.MoveTowards(dead.a, 0f, Time.deltaTime * 3f);
            auraRenderer.color = dead;
            return;
        }

        // Keep the aura glued just behind the body's CURRENT sorting order. This
        // matters while burrowing: the body sprite is pinned below the terrain, so
        // the aura must follow it under (rather than hovering over obstacles at the
        // order it happened to have when it was built).
        if (insectRenderer != null)
        {
            auraRenderer.sortingLayerID = insectRenderer.sortingLayerID;
            auraRenderer.sortingOrder = insectRenderer.sortingOrder + auraSortingOffset;
        }

        phase += Time.deltaTime * pulseSpeed * Mathf.PI * 2f;
        float t = (Mathf.Sin(phase) + 1f) * 0.5f;               // 0..1

        Color c = auraColor;
        c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
        auraRenderer.color = c;

        float s = baseAuraScale * (1f + Mathf.Lerp(-scalePulse, scalePulse, t));
        auraTransform.localScale = Vector3.one * s;

        // Keep the glow upright and centred on the insect even though the body
        // sprite rotates (aim / lean) and mirrors — a spinning radial glow would
        // look wrong, and a body-centred glow reads as an aura rather than art.
        auraTransform.rotation = Quaternion.identity;
        auraTransform.position = transform.position;
    }

    // Builds (once, shared across all Elites) a soft radial-gradient sprite used as
    // the glow. Drawn white so SpriteRenderer.color controls the hue; alpha falls
    // off smoothly to the edge.
    private static Sprite _glowSprite;
    private static Sprite GetGlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        float r = size * 0.5f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - r;
                float dy = (y + 0.5f) - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;    // 0 centre .. 1 edge
                float a = Mathf.Clamp01(1f - d);
                a *= a;                                          // soft falloff
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        // pixelsPerUnit = size => the sprite is exactly 1 world unit across at scale 1.
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                    new Vector2(0.5f, 0.5f), size);
        _glowSprite.name = "EliteAuraGlow";
        return _glowSprite;
    }
}


