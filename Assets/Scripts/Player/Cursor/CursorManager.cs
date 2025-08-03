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

    private Sprite defaultCursorSprite;
    private Sprite repairCursorSprite;
    private Sprite shieldCursorSprite;
    private Sprite meleeCursorSprite;
    private Sprite previousCursorSprite;

    public enum CursorType
    {
        Default,
        Repair,
        Shield,
        Melee
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

        //Debug.Log($"CursorManager: Loaded {(repairCursorSprite != null ? "✓" : "✗")} Repair cursor");
        //Debug.Log($"CursorManager: Loaded {(shieldCursorSprite != null ? "✓" : "✗")} Shield cursor");
        //Debug.Log($"CursorManager: Loaded {(meleeCursorSprite != null ? "✓" : "✗")} Melee cursor");
    }

    public void SetCursor(CursorType cursorType)
    {
        if (cursorSpriteRenderer == null)
        {
            Debug.LogWarning("CursorManager: No SpriteRenderer assigned!");
            return;
        }

        if (cursorType == CursorType.Repair)
        {
            previousCursorSprite = cursorSpriteRenderer.sprite;
            //Debug.Log($"CursorManager: Stored previous cursor: {previousCursorSprite?.name}");
        }

        Sprite targetSprite = cursorType switch
        {
            CursorType.Default => defaultCursorSprite,
            CursorType.Repair => repairCursorSprite,
            CursorType.Shield => shieldCursorSprite,
            CursorType.Melee => meleeCursorSprite,
            _ => defaultCursorSprite
        };

        if (targetSprite != null)
        {
            cursorSpriteRenderer.sprite = targetSprite;
            //Debug.Log($"CursorManager: Changed cursor to {cursorType}");
        }
        else
        {
            Debug.LogWarning($"CursorManager: {cursorType} cursor sprite is null!");
        }
    }

    public void ReturnToPreviousCursor()
    {
        if (cursorSpriteRenderer == null || previousCursorSprite == null)
        {
            Debug.LogWarning("CursorManager: Cannot return to previous cursor!");
            return;
        }

        cursorSpriteRenderer.sprite = previousCursorSprite;
        Debug.Log("CursorManager: Returned to previous cursor");
    }
}