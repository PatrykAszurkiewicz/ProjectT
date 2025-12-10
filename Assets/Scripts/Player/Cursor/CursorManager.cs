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

    private Sprite defaultCursorSprite;
    private Sprite repairCursorSprite;
    private Sprite shieldCursorSprite;
    private Sprite meleeCursorSprite;
    private Sprite hookCursorSprite;
    private Sprite rangedCursorSprite;
    private Sprite hookHighlightCursorSprite;
    private Sprite obstacleDrawerCursorSprite;

    private Sprite previousCursorSprite;
    private CursorType currentCursorType = CursorType.Default;

    public enum CursorType
    {
        Default,
        Repair,
        Shield,
        Melee,
        Hook,
        Ranged,
        HookHightlight,
        ObstacleDrawer
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
            cursorSpriteRenderer.color = Color.white;
        }

        repairCursorSprite = Resources.Load<Sprite>(repairCursorPath);
        shieldCursorSprite = Resources.Load<Sprite>(shieldCursorPath);
        meleeCursorSprite = Resources.Load<Sprite>(meleeCursorPath);
        hookCursorSprite = Resources.Load<Sprite>(hookCursorPath);
        hookHighlightCursorSprite = Resources.Load<Sprite>(hookHighlightCursorPath);
        obstacleDrawerCursorSprite = Resources.Load<Sprite>(obstacleDrawerCursorPath);
        rangedCursorSprite = Resources.Load<Sprite>(rangedCursorPath);

        // DEBUG LOGGING
        /*
        Debug.Log("========== CURSOR SPRITES LOADED ==========");
        Debug.Log($"Repair: {(repairCursorSprite != null ? "✓ LOADED" : "✗ NULL")}");
        Debug.Log($"Melee: {(meleeCursorSprite != null ? "✓ LOADED" : "✗ NULL")}");
        Debug.Log($"Hook: {(hookCursorSprite != null ? "✓ LOADED" : "✗ NULL")}");
        Debug.Log($"ObstacleDrawer: {(obstacleDrawerCursorSprite != null ? "✓ LOADED" : "✗ NULL")}");
        Debug.Log($"Ranged: {(rangedCursorSprite != null ? "✓ LOADED" : "✗ NULL")} <- Path: '{rangedCursorPath}'");
        Debug.Log("==========================================");

        if (rangedCursorSprite == null)
        {
            Debug.LogError($"[CursorManager] RANGED CURSOR FAILED TO LOAD!");
            Debug.LogError($"[CursorManager] Tried path: '{rangedCursorPath}'");
            Debug.LogError($"[CursorManager] Full path should be: Assets/Resources/{rangedCursorPath}.png (or in spritesheet)");
        }
        */
    }

    public void SetCursor(CursorType cursorType)
    {
        Debug.Log($"[CursorManager] SetCursor called with: {cursorType}");

        if (cursorSpriteRenderer == null)
        {
            Debug.LogWarning("CursorManager: No SpriteRenderer assigned");
            return;
        }

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
            _ => defaultCursorSprite
        };

        //Debug.Log($"[CursorManager] Target sprite for {cursorType}: {(targetSprite != null ? targetSprite.name : "NULL")}");

        if (targetSprite != null)
        {
            cursorSpriteRenderer.sprite = targetSprite;
            cursorSpriteRenderer.color = Color.white;
            currentCursorType = cursorType;
            //Debug.Log($"[CursorManager] ✓ Successfully set cursor to {cursorType} (sprite: {targetSprite.name})");
        }
        else
        {
            Debug.LogError($"[CursorManager] ✗ {cursorType} cursor sprite is NULL! Cannot change cursor!");
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
        cursorSpriteRenderer.color = Color.white;
        currentCursorType = CursorType.Default;
    }

    public CursorType GetCurrentCursorType()
    {
        return currentCursorType;
    }
}