using UnityEngine;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;

    [Header("Cursor Sprites")]
    public SpriteRenderer cursorSpriteRenderer;

    [Header("Cursor Sprite Paths")]
    public string defaultCursorPath = "";
    public string repairCursorPath = "Sprites/Cursors/cursor_spritesheet_repair3";
    public string shieldCursorPath = "Sprites/Cursors/cursor_spritesheet_shield";
    public string meleeCursorPath = "Sprites/Cursors/cursor_spritesheet_melee";
    public string hookCursorPath = "Sprites/Cursors/cursor_spritesheet_hook2";
    public string hookHighlightCursorPath = "Sprites/Cursors/cursor_spritesheet_hook_highlight";
    public string obstacleDrawerCursorPath = "Sprites/Cursors/cursor_spritesheet_obstacle_drawer";
    public string rangedCursorPath = "Sprites/Cursors/cursor_spritesheet_ranged";
    public string flamethrowerCursorPath = "Sprites/Cursors/cursor_spritesheet_flamethrower";
    public string bombLauncherCursorPath = "Sprites/Cursors/cursor_spritesheet_bomb";
    public string trapCursorPath = "Sprites/Cursors/cursor_spritesheet_trap";
    public string turretCursorPath = "Sprites/Cursors/cursor_spritesheet_turret";
    public string decoyCursorPath = "Sprites/Cursors/cursor_spritesheet_decoy";
    public string boomerangCursorPath = "Sprites/Cursors/cursor_spritesheet_boomerang";

    [Header("Cursor Size")]
    [Tooltip("Desired cursor size in world units. All cursor sprites will be normalized to this size.")]
    public float targetCursorWorldSize = 1.6f;

    private Sprite defaultCursorSprite;
    private Sprite repairCursorSprite;
    private Sprite shieldCursorSprite;
    private Sprite meleeCursorSprite;
    private Sprite hookCursorSprite;
    private Sprite rangedCursorSprite;
    private Sprite hookHighlightCursorSprite;
    private Sprite obstacleDrawerCursorSprite;
    private Sprite flamethrowerCursorSprite;
    private Sprite bombLauncherCursorSprite;
    private Sprite trapCursorSprite;
    private Sprite turretCursorSprite;
    private Sprite decoyCursorSprite;
    private Sprite boomerangCursorSprite;

    private Sprite previousCursorSprite;
    private CursorType currentCursorType = CursorType.Default;

    // The scale the cursor object had at startup, before we touch it.
    private Vector3 baseScale = Vector3.one;
    private bool baseScaleCaptured = false;

    public enum CursorType
    {
        Default,
        Repair,
        Shield,
        Melee,
        Hook,
        Ranged,
        HookHightlight,
        ObstacleDrawer,
        Flamethrower,
        BombLauncher,
        Trap,
        Turret,
        Decoy,
        Boomerang
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            LoadCursorSprites();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        CaptureBaseScale();
    }

    private void CaptureBaseScale()
    {
        if (baseScaleCaptured) return;
        if (cursorSpriteRenderer != null)
        {
            baseScale = cursorSpriteRenderer.transform.localScale;
            baseScaleCaptured = true;

            // Auto-detect target size from the default sprite if not manually set.

            if (defaultCursorSprite != null && targetCursorWorldSize <= 0f)
            {
                float defaultSpriteSize = Mathf.Max(defaultCursorSprite.bounds.size.x,
                                                      defaultCursorSprite.bounds.size.y);
                targetCursorWorldSize = defaultSpriteSize * Mathf.Max(baseScale.x, baseScale.y);
            }
        }
    }

    void LoadCursorSprites()
    {
        if (cursorSpriteRenderer != null)
        {
            defaultCursorSprite = cursorSpriteRenderer.sprite;
            previousCursorSprite = cursorSpriteRenderer.sprite;
            cursorSpriteRenderer.color = Color.white;
        }

        repairCursorSprite = Resources.Load<Sprite>(repairCursorPath);
        shieldCursorSprite = Resources.Load<Sprite>(shieldCursorPath);
        meleeCursorSprite = Resources.Load<Sprite>(meleeCursorPath);
        hookCursorSprite = Resources.Load<Sprite>(hookCursorPath);
        hookHighlightCursorSprite = Resources.Load<Sprite>(hookHighlightCursorPath);
        obstacleDrawerCursorSprite = Resources.Load<Sprite>(obstacleDrawerCursorPath);
        rangedCursorSprite = Resources.Load<Sprite>(rangedCursorPath);
        flamethrowerCursorSprite = Resources.Load<Sprite>(flamethrowerCursorPath);
        bombLauncherCursorSprite = Resources.Load<Sprite>(bombLauncherCursorPath);
        trapCursorSprite = Resources.Load<Sprite>(trapCursorPath);
        turretCursorSprite = Resources.Load<Sprite>(turretCursorPath);
        decoyCursorSprite = Resources.Load<Sprite>(decoyCursorPath);
        boomerangCursorSprite = Resources.Load<Sprite>(boomerangCursorPath);
    }

    public void SetCursor(CursorType cursorType)
    {
        if (cursorSpriteRenderer == null)
        {
            Debug.LogWarning("CursorManager: No SpriteRenderer assigned");
            return;
        }

        CaptureBaseScale();

        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        if (inPlacementMode && currentCursorType == CursorType.Repair)
        {
            if (cursorType == CursorType.Hook || cursorType == CursorType.HookHightlight)
            {
                return;
            }
        }

        if (cursorType == CursorType.Repair)
        {
            previousCursorSprite = cursorSpriteRenderer.sprite;
        }

        Sprite targetSprite = cursorType switch
        {
            CursorType.Default => defaultCursorSprite,
            CursorType.Repair => repairCursorSprite,
            CursorType.Shield => shieldCursorSprite,
            CursorType.Melee => meleeCursorSprite,
            CursorType.Hook => hookCursorSprite,
            CursorType.Ranged => rangedCursorSprite,
            CursorType.HookHightlight => hookHighlightCursorSprite,
            CursorType.ObstacleDrawer => obstacleDrawerCursorSprite,
            CursorType.Flamethrower => flamethrowerCursorSprite,
            CursorType.BombLauncher => bombLauncherCursorSprite,
            CursorType.Trap => trapCursorSprite ?? defaultCursorSprite,
            CursorType.Turret => turretCursorSprite ?? defaultCursorSprite,
            CursorType.Decoy => decoyCursorSprite ?? defaultCursorSprite,
            CursorType.Boomerang => boomerangCursorSprite ?? rangedCursorSprite ?? defaultCursorSprite,
            _ => defaultCursorSprite
        };

        if (targetSprite != null)
        {
            cursorSpriteRenderer.sprite = targetSprite;
            cursorSpriteRenderer.color = Color.white;
            currentCursorType = cursorType;

            NormalizeCursorScale(targetSprite);
        }
        else
        {
            Debug.LogError($"[CursorManager] {cursorType} cursor sprite is NULL! Cannot change cursor!");
        }
    }

    /// <summary>
    /// Adjusts the SpriteRenderer's localScale so every cursor sprite renders
    /// at the same world-space size regardless of pixel dimensions or PPU.
    /// </summary>
    private void NormalizeCursorScale(Sprite sprite)
    {
        if (sprite == null || cursorSpriteRenderer == null) return;

        // sprite.bounds.size is the unscaled world size (pixels / pixelsPerUnit)
        float spriteWorldSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        if (spriteWorldSize < 0.001f) return;

        float scaleFactor = targetCursorWorldSize / spriteWorldSize;
        cursorSpriteRenderer.transform.localScale = baseScale * scaleFactor;
    }

    public void ReturnToPreviousCursor()
    {
        if (cursorSpriteRenderer == null || previousCursorSprite == null)
        {
            Debug.LogWarning("CursorManager: Cannot return to previous cursor");
            return;
        }

        cursorSpriteRenderer.sprite = previousCursorSprite;
        cursorSpriteRenderer.color = Color.white;
        currentCursorType = CursorType.Default;

        NormalizeCursorScale(previousCursorSprite);
    }

    public CursorType GetCurrentCursorType()
    {
        return currentCursorType;
    }
}
