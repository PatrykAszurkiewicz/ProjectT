using UnityEngine;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;

    [Header("Cursor Sprites")]
    public SpriteRenderer cursorSpriteRenderer;

    [Header("Cursor Sprite Paths")]
    public string defaultCursorPath = "";
    public string repairCursorPath = "Sprites/cursor_spritesheet_repair3";
    public string shieldCursorPath = "Sprites/cursor_spritesheet_shield";
    public string meleeCursorPath = "Sprites/cursor_spritesheet_melee";
    public string hookCursorPath = "Sprites/cursor_spritesheet_hook2";
    public string hookHighlightCursorPath = "Sprites/cursor_spritesheet_hook_highlight";

    private Sprite defaultCursorSprite;
    private Sprite repairCursorSprite;
    private Sprite shieldCursorSprite;
    private Sprite meleeCursorSprite;
    private Sprite hookCursorSprite;
    private Sprite hookHighlightCursorSprite;

    private Sprite previousCursorSprite;
    private CursorType currentCursorType = CursorType.Default;

    public enum CursorType
    {
        Default,
        Repair,
        Shield,
        Melee,
        Hook,
        HookHightlight
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

    void LoadCursorSprites()
    {
        if (cursorSpriteRenderer != null)
        {
            defaultCursorSprite = cursorSpriteRenderer.sprite;
            previousCursorSprite = cursorSpriteRenderer.sprite;
        }

        repairCursorSprite = Resources.Load<Sprite>(repairCursorPath);
        shieldCursorSprite = Resources.Load<Sprite>(shieldCursorPath);
        meleeCursorSprite = Resources.Load<Sprite>(meleeCursorPath);
        hookCursorSprite = Resources.Load<Sprite>(hookCursorPath);
        hookHighlightCursorSprite = Resources.Load<Sprite>(hookHighlightCursorPath);
    }

    public void SetCursor(CursorType cursorType)
    {
        if (cursorSpriteRenderer == null)
        {
            Debug.LogWarning("CursorManager: No SpriteRenderer assigned");
            return;
        }

        // FIXED: Don't let grappling hook override repair cursor during placement mode
        bool inPlacementMode = TowerPlacementManager.Instance != null && TowerPlacementManager.Instance.IsInPlacementMode();

        if (inPlacementMode && currentCursorType == CursorType.Repair)
        {
            // During placement mode, don't let hook cursors override the repair cursor
            if (cursorType == CursorType.Hook || cursorType == CursorType.HookHightlight)
            {
                return; // Ignore hook cursor changes during placement mode
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
            CursorType.HookHightlight => hookHighlightCursorSprite,
            _ => defaultCursorSprite
        };

        if (targetSprite != null)
        {
            cursorSpriteRenderer.sprite = targetSprite;
            currentCursorType = cursorType;
            //Debug.Log($"CursorManager: Changed cursor to {cursorType}");
        }
        else
        {
            Debug.LogWarning($"CursorManager: {cursorType} cursor sprite is null");
        }
    }

    public void ReturnToPreviousCursor()
    {
        if (cursorSpriteRenderer == null || previousCursorSprite == null)
        {
            Debug.LogWarning("CursorManager: Cannot return to previous cursor");
            return;
        }

        cursorSpriteRenderer.sprite = previousCursorSprite;
        currentCursorType = CursorType.Default; // Reset to default
        //Debug.Log("CursorManager: Returned to previous cursor");
    }

    public CursorType GetCurrentCursorType()
    {
        return currentCursorType;
    }
}