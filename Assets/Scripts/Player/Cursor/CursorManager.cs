using System.Collections.Generic;
using UnityEngine;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;

    [Header("Co-op")]
    [Tooltip("Optional. Leave null for the legacy single shared cursor (single player). " +
             "Assign the owning PlayerRef on a per-player cursor so two coexist; resolve with CursorManager.For(playerRef).")]
    public PlayerRef owner;

    // All live cursor managers, so per-player consumers (Phase 3) can resolve
    // the right one. In single player there's exactly one and it's Instance.
    private static readonly List<CursorManager> _all = new List<CursorManager>();

    // Co-op cursor sizing: every player's cursor is forced to this same on-screen
    // world size so they're always equal. It tracks PLAYER 1's "natural" size (the
    // lowest PlayerIndex cursor), so the shared size matches how player 1 looks and
    // co-op never inflates it. Cached only — recomputed live in
    // GetSharedCursorWorldSize. -1 = not computed yet.
    private static float _sharedCursorWorldSize = -1f;

    // Global size multiplier applied to every player's cursor equally. 1.0 = the
    // natural (player 1) size; 0.8 = 20% smaller. Kept as one knob so both players
    // always stay in lockstep.
    private const float CURSOR_SIZE_MULTIPLIER = 0.8f;

    // Reset the shared size between Play sessions (domain reload off), mirroring
    // PlayerAttack.ResetStatics so a stale value can't leak across sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _sharedCursorWorldSize = -1f;
        _all.Clear();
    }

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
    public string bookCursorPath = "Sprites/Cursors/cursor_spritesheet_book";
    public string hammerCursorPath = "Sprites/Cursors/cursor_spritesheet_hammer";
    public string cloakCursorPath = "Sprites/Cursors/cursor_spritesheet_cloak";
    public string mortarCursorPath = "Sprites/Cursors/cursor_spritesheet_mortar";
    public string smokeCursorPath = "Sprites/Cursors/cursor_spritesheet_smoke";
    // No dedicated smoke cursor art was supplied — leave blank to fall back to
    // the mortar/ranged cursor (see SetCursor). Point this at a sprite if you
    // add one later.
    //public string smokeCursorPath = "";

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
    private Sprite bookCursorSprite;
    private Sprite hammerCursorSprite;
    private Sprite cloakCursorSprite;
    private Sprite mortarCursorSprite;
    private Sprite smokeCursorSprite;

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
        Boomerang,
        Book,
        Hammer,
        Cloak,
        Mortar,
        Smoke
    }

    void Awake()
    {
        _all.Add(this);

        if (owner == null)
        {
            // Legacy global-singleton path (single player / shared cursor) —
            // identical behavior to before: first one wins, duplicates destroyed.
            if (Instance == null)
            {
                Instance = this;
                LoadCursorSprites();
            }
            else
            {
                _all.Remove(this);
                Destroy(gameObject);
            }
        }
        else
        {
            // Per-player cursor (co-op): coexists with other players' cursors.
            if (Instance == null) Instance = this;
            LoadCursorSprites();
        }
    }

    void OnDestroy()
    {
        _all.Remove(this);
        if (Instance == this)
            Instance = _all.Count > 0 ? _all[0] : null;
    }

    /// <summary>
    /// Resolve the cursor manager for a given player. Returns that player's
    /// owned manager if one exists, otherwise the primary Instance (single-player).
    /// </summary>
    public static CursorManager For(PlayerRef player)
    {
        if (player != null)
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null && _all[i].owner == player) return _all[i];
        return Instance;
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
        bookCursorSprite = Resources.Load<Sprite>(bookCursorPath);
        hammerCursorSprite = Resources.Load<Sprite>(hammerCursorPath);
        cloakCursorSprite = Resources.Load<Sprite>(cloakCursorPath);
        mortarCursorSprite = Resources.Load<Sprite>(mortarCursorPath);
        smokeCursorSprite = Resources.Load<Sprite>(smokeCursorPath);
        //smokeCursorSprite = string.IsNullOrEmpty(smokeCursorPath) ? null : Resources.Load<Sprite>(smokeCursorPath);
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
            CursorType.Book => bookCursorSprite ?? defaultCursorSprite,
            CursorType.Hammer => hammerCursorSprite ?? meleeCursorSprite ?? defaultCursorSprite,
            CursorType.Cloak => cloakCursorSprite ?? defaultCursorSprite,
            CursorType.Mortar => mortarCursorSprite ?? rangedCursorSprite ?? defaultCursorSprite,
            CursorType.Smoke => smokeCursorSprite ?? defaultCursorSprite,
            //CursorType.Smoke => smokeCursorSprite ?? mortarCursorSprite ?? rangedCursorSprite ?? defaultCursorSprite,
            _ => defaultCursorSprite
        };

        if (targetSprite != null)
        {
            cursorSpriteRenderer.sprite = targetSprite;
            cursorSpriteRenderer.color = Color.white;
            currentCursorType = cursorType;

            NormalizeCursorScale(targetSprite);
            //Debug.Log($"SetCursor({cursorType}) -> {targetSprite?.name}");
        }
        else
        {
            Debug.LogError($"[CursorManager] {cursorType} cursor sprite is NULL! Cannot change cursor!");
        }
    }

    /// Sizes the cursor so EVERY player's cursor renders at the same on-screen
    /// size — specifically the size player 1 produced before the co-op fixes.
    /// The rendered size of a sprite is spriteWorldSize * localScale * parentLossyScale.
    /// In co-op the two players' cursor visuals had different base scales AND sat
    /// under parents with different world scales, so the same target produced two
    /// different sizes. Rather than force an absolute size (which changed how
    /// player 1 looked), we measure the largest per-player "natural" size once and
    /// drive every cursor to exactly that, compensating for each cursor's own
    /// sprite size and parent scale. Player 1 is unchanged; player 2 matches it.
    private void NormalizeCursorScale(Sprite sprite)
    {
        ApplySharedCursorScale(sprite);
    }

    // Max abs world scale of a transform's PARENT (the part we don't control).
    private static float ParentLossyScale(Transform t)
    {
        if (t == null || t.parent == null) return 1f;
        Vector3 pls = t.parent.lossyScale;
        float v = Mathf.Max(Mathf.Abs(pls.x), Mathf.Abs(pls.y));
        return v < 1e-6f ? 1f : v;
    }

    // The on-screen size a manager would render at under the ORIGINAL formula
    // (baseScale * target), with its parent scale folded in. This is exactly how
    // each player looked before the co-op size fixes.
    private float NaturalCursorWorldSize()
    {
        if (cursorSpriteRenderer == null) return -1f;
        CaptureBaseScale();
        float bsMag = Mathf.Max(Mathf.Abs(baseScale.x), Mathf.Abs(baseScale.y));
        if (bsMag < 1e-6f) bsMag = 1f;
        return targetCursorWorldSize * bsMag * ParentLossyScale(cursorSpriteRenderer.transform);
    }

    // Shared reference size = the natural size of PLAYER 1's cursor (lowest
    // PlayerIndex, or the single shared cursor in single player). Every cursor is
    // driven to THIS size, so co-op cursors match player 1 exactly.
    private static float GetSharedCursorWorldSize()
    {
        CursorManager reference = null;
        int bestIndex = int.MaxValue;
        for (int i = 0; i < _all.Count; i++)
        {
            var m = _all[i];
            if (m == null || m.cursorSpriteRenderer == null) continue;
            int idx = (m.owner != null) ? m.owner.PlayerIndex : 0;
            if (idx < bestIndex)
            {
                bestIndex = idx;
                reference = m;
            }
        }

        float size = reference != null ? reference.NaturalCursorWorldSize() : -1f;
        if (size > 0f) _sharedCursorWorldSize = size;   // cache; NOT forced to only grow
        return size > 0f ? size : _sharedCursorWorldSize;
    }

    private void ApplySharedCursorScale(Sprite sprite)
    {
        if (sprite == null || cursorSpriteRenderer == null) return;

        float spriteWorldSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        if (spriteWorldSize < 0.001f) return;

        Transform t = cursorSpriteRenderer.transform;

        float refSize = GetSharedCursorWorldSize();

        float scaleFactor;
        if (refSize > 0f)
        {
            // Drive this cursor to the shared world size, compensating for its own
            // sprite size and parent scale so the final on-screen size is identical
            // for every player regardless of hierarchy.
            scaleFactor = refSize / (spriteWorldSize * ParentLossyScale(t));
        }
        else
        {
            // Reference not ready (shouldn't normally happen) — original formula.
            scaleFactor = targetCursorWorldSize / spriteWorldSize;
        }

        // Preserve any sprite-flip sign baked into the prefab; use computed magnitude.
        // CURSOR_SIZE_MULTIPLIER shrinks/grows every player's cursor by the same factor.
        float mag = scaleFactor * CURSOR_SIZE_MULTIPLIER;
        float sx = baseScale.x < 0f ? -mag : mag;
        float sy = baseScale.y < 0f ? -mag : mag;
        Vector3 desired = new Vector3(sx, sy, 1f);

        // Avoid redundant transform writes (skip if effectively unchanged).
        if ((t.localScale - desired).sqrMagnitude > 1e-8f)
            t.localScale = desired;
    }

    // Keep this cursor matched to the shared size every frame. This self-heals
    // any spawn-order edge case (e.g. player 1 spawning after player 2) without
    // needing the cursor to be re-set, and is a no-op once sizes are stable.
    void Update()
    {
        if (cursorSpriteRenderer == null || cursorSpriteRenderer.sprite == null) return;
        ApplySharedCursorScale(cursorSpriteRenderer.sprite);
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
