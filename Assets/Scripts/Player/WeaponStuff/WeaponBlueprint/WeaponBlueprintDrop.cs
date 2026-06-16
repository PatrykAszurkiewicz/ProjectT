using UnityEngine;
using System.Collections;

/// A floating pickup that grants the player a permanent blueprint when collected.
/// Visually it's a WeaponRollUI-style circular tile (colored bg + weapon icon) so the
/// player instantly understands what they're picking up.
///
/// Spawned by BossBlueprintDropper after a stage boss dies (with some probability).
/// Mirrors EnergyDrop's magnet / arc / lifetime structure so collection feels familiar.
public class WeaponBlueprintDrop : MonoBehaviour
{
    [Header("Drop Data")]
    public int slotIndex = -1;             // which WeaponRollController slot this blueprint corresponds to
    public WeaponData weaponDataPreview;   // for sprite lookup

    [Header("Lifetime / Magnet")]
    public float lifetime = 60f;           // blueprints linger longer than energy
    public float magnetRange = 2.5f;
    public float arcSpeed = 5f;
    public float arcHeight = 1f;
    public float collectionRadius = 0.8f;

    [Header("Arming")]
    [Tooltip("Seconds after spawn before the drop can be collected. " +
             "Prevents the player from instantly grabbing the drop while " +
             "standing on the boss corpse.")]
    public float armDelay = 1.0f;
    private float spawnTime;

    [Header("On-Collect Behavior")]
    [Tooltip("When picked up, also force-unlock the slot in WeaponUnlockRegistry " +
             "so it appears in the hotbar immediately (and gets auto-equipped by " +
             "WeaponRollController). The blueprint is also permanently recorded.")]
    public bool autoEquipOnCollect = true;

    [Header("Visual")]
    public float bgRadiusWorld = 0.6f;    // circle background radius in world units
    public Color weaponBgColor = new Color(0.10f, 0.45f, 0.35f, 1f);  // deeper green
    public Color toolBgColor = new Color(0.40f, 0.20f, 0.55f, 1f);    // deeper purple
    [Range(0.1f, 1f)] public float bgAlpha = 1f;   // master opacity for the bg circle
    public float iconScale = 0.85f;       // icon size relative to bg (slightly smaller so colored ring shows)

    [Header("Glow Pulse")]
    public bool enableGlow = true;
    public float glowPulseSpeed = 2.5f;
    public float glowScaleMultiplier = 1.6f;
    [Range(0f, 1f)] public float glowAlpha = 0.55f;

    [Header("Float Animation")]
    public float bobAmplitude = 0.12f;
    public float bobSpeed = 1.8f;
    public float spinSpeed = 35f;         // background spins very slowly for shimmer

    // INTERNAL 
    private Transform playerTransform;
    private bool isCollected;
    private bool isMovingToPlayer;
    private Vector3 startPos;
    private Vector3 spawnPos;
    private float arcProgress;
    private float bobTimer;
    private float glowTimer;

    private GameObject bgObject;
    private SpriteRenderer bgRenderer;
    private GameObject iconObject;
    private SpriteRenderer iconRenderer;
    private GameObject glowObject;
    private SpriteRenderer glowRenderer;
    private CircleCollider2D dropCollider;

    private static Sprite _cachedCircleSprite;

    // FACTORY 

    public static GameObject Spawn(Vector3 worldPos, int slot, WeaponData data)
    {
        // Create the GameObject in a deactivated state so AddComponent does NOT
        // fire Awake() yet. We assign the fields the component needs (slotIndex,
        // weaponDataPreview), THEN activate — at which point Awake runs and
        // BuildVisual() sees the correct data.
        //
        // Without this, AddComponent triggers Awake immediately, BuildVisual
        // runs with weaponDataPreview == null, icon resolution fails, and the
        // drop shows the yellow fallback instead of the real weapon icon.
        var go = new GameObject($"BlueprintDrop_Slot{slot}");
        go.SetActive(false);
        go.transform.position = worldPos;
        var drop = go.AddComponent<WeaponBlueprintDrop>();
        drop.slotIndex = slot;
        drop.weaponDataPreview = data;
        go.SetActive(true);
        return go;
    }

    //  LIFECYCLE 

    void Awake()
    {
        SetupCollider();
        FindPlayer();
        BuildVisual();
    }

    void Start()
    {
        spawnPos = transform.position;
        spawnTime = Time.time;
        StartCoroutine(LifetimeTimer());
    }

    void Update()
    {
        if (isCollected) return;
        UpdateBob();
        UpdateGlow();
        UpdateMagnet();
    }

    //  SETUP 

    void SetupCollider()
    {
        dropCollider = gameObject.AddComponent<CircleCollider2D>();
        dropCollider.isTrigger = true;
        dropCollider.radius = collectionRadius;
        gameObject.layer = LayerMask.NameToLayer("Default");
    }

    void FindPlayer()
    {
        // Co-op: anchor to the nearest alive player so either player can magnet
        // the drop. Falls back to the old lookups if the registry is empty.
        var nearest = PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true);
        if (nearest != null) { playerTransform = nearest.transform; return; }

        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) { playerTransform = pm.transform; return; }
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    void BuildVisual()
    {
        Sprite circle = GetCircleSprite();
        bool isTool = weaponDataPreview != null && weaponDataPreview.IsTool;
        Color bgColor = isTool ? toolBgColor : weaponBgColor;

        // Use Default sorting layer with very high orders so the drop renders on top
        // of grass (which Y-sorts in the 400-1600 range) and other world objects.
        const string sortLayer = "Default";
        const int glowOrder = 2598;
        const int bgOrder = 2599;
        const int iconOrder = 2600;

        // Background circle
        bgObject = new GameObject("BlueprintBg");
        bgObject.transform.SetParent(transform, false);
        bgRenderer = bgObject.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = circle;
        Color bgFinal = bgColor; bgFinal.a = bgAlpha;
        bgRenderer.color = bgFinal;
        bgRenderer.sortingLayerName = sortLayer;
        bgRenderer.sortingOrder = bgOrder;

        // Scale to bgRadiusWorld (default circle sprite is unit-radius)
        float circleSize = bgRadiusWorld * 2f;
        bgObject.transform.localScale = Vector3.one * circleSize;

        // Glow halo behind bg
        if (enableGlow)
        {
            glowObject = new GameObject("BlueprintGlow");
            glowObject.transform.SetParent(transform, false);
            glowRenderer = glowObject.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = circle;
            Color glowCol = bgColor;
            glowCol.a = glowAlpha;
            glowRenderer.color = glowCol;
            glowRenderer.sortingLayerName = sortLayer;
            glowRenderer.sortingOrder = glowOrder;
            glowObject.transform.localScale = Vector3.one * circleSize * glowScaleMultiplier;
        }

        // Weapon icon on top
        iconObject = new GameObject("BlueprintIcon");
        iconObject.transform.SetParent(transform, false);
        iconRenderer = iconObject.AddComponent<SpriteRenderer>();
        iconRenderer.sortingLayerName = sortLayer;
        iconRenderer.sortingOrder = iconOrder;
        iconRenderer.color = Color.white;

        // Resolve icon sprite — with fallbacks so we NEVER show an empty tile.
        Sprite iconSprite = ResolveIconSprite();

        if (iconSprite != null)
        {
            iconRenderer.sprite = iconSprite;
            Vector2 sp = iconSprite.bounds.size;
            float maxDim = Mathf.Max(sp.x, sp.y, 0.01f);
            // Slightly smaller than the bg so a colored ring is visible around it
            // (the icon sprites have their own dark backing).
            float targetSize = circleSize * iconScale;
            iconObject.transform.localScale = Vector3.one * (targetSize / maxDim);
        }
        else
        {
            // Last-resort fallback: a smaller, brighter inner circle so the
            // player at least sees SOMETHING distinguishing the drop from empty.
            iconRenderer.sprite = circle;
            iconRenderer.color = new Color(1f, 1f, 0.6f, 1f);
            iconObject.transform.localScale = Vector3.one * (circleSize * 0.45f);

            // Dump everything we know so we can diagnose why no sprite resolved.
            string dataInfo;
            if (weaponDataPreview == null)
            {
                dataInfo = "weaponDataPreview=NULL (BossBlueprintDropper couldn't find a WeaponData for this slot)";
            }
            else
            {
                var d = weaponDataPreview;
                dataInfo = $"weaponName='{d.weaponName}'  " +
                           $"flags: isRanged={d.isRanged} isGrapplingHook={d.isGrapplingHook} " +
                           $"isObstacleDrawer={d.isObstacleDrawer} isFlamethrower={d.isFlamethrower} " +
                           $"isBombLauncher={d.isBombLauncher} isTrap={d.isTrap} isTurret={d.isTurret} " +
                           $"isDecoy={d.isDecoy} isBoomerang={d.isBoomerang} armorBonus={d.armorBonus} " +
                           $"isCloak={d.isCloak} isBook={d.isBook} isHammer={d.isHammer} " +
                           $"isTorch={d.isTorch} isClock={d.isClock} isSmoke={d.isSmoke}";
            }

            // Probe every likely Resources path so we know which exists and which don't.
            string[] probes = new[]
            {
    "Icons/WeaponIconMelee", "Icons/WeaponIconRanged", "Icons/WeaponIconGrapplingHook",
    "Icons/WeaponIconShield", "Icons/WeaponIconObstacleDrawer", "Icons/WeaponIconFlamethrower",
    "Icons/WeaponIconBomb",  "Icons/WeaponIconTrap","Icons/WeaponIconTorch",
    "Icons/WeaponIconTurret", "Icons/WeaponIconDecoy", "Icons/WeaponIconBoomerang","Icons/WeaponIconClock",
    "Icons/WeaponIconCloak", "Icons/WeaponIconBook", "Icons/WeaponIconHammer", "Icons/WeaponIconSmoke"
};
            var found = new System.Text.StringBuilder();
            var missing = new System.Text.StringBuilder();
            foreach (var p in probes)
            {
                if (Resources.Load<Sprite>(p) != null) found.Append(p).Append(", ");
                else missing.Append(p).Append(", ");
            }

            Debug.LogWarning(
                $"[BlueprintDrop] No icon resolved for slot {slotIndex}. Yellow fallback shown.\n" +
                $"  {dataInfo}\n" +
                $"  Resources.Load FOUND:   {(found.Length == 0 ? "(none)" : found.ToString())}\n" +
                $"  Resources.Load MISSING: {(missing.Length == 0 ? "(none)" : missing.ToString())}\n" +
                $"  Fix: ensure icons are inside an 'Assets/Resources/Icons/' folder " +
                $"(NOT 'Assets/Icons/'); Resources.Load only sees files under a Resources/ folder.");
        }
    }

    // Cache resolved sprites — Resources.Load is cheap but not free, and the
    // same sprite may be looked up many times across drops in a session.
    static readonly System.Collections.Generic.Dictionary<int, Sprite> _iconCache
        = new System.Collections.Generic.Dictionary<int, Sprite>();

    // Use the EXACT same icon-resolution path the WeaponRollUI hotbar uses.
    // If you ever change icon naming, change it once in WeaponRollUI.LoadIconForData
    // and this drop picks it up automatically.
    Sprite ResolveIconSprite()
    {
        if (slotIndex >= 0 && _iconCache.TryGetValue(slotIndex, out Sprite cached) && cached != null)
            return cached;

        Sprite found = WeaponRollUI.LoadIconForData(weaponDataPreview);

        if (found != null && slotIndex >= 0) _iconCache[slotIndex] = found;
        return found;
    }

    static Sprite GetCircleSprite()
    {
        if (_cachedCircleSprite != null) return _cachedCircleSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        float r = size * 0.48f;
        float edge = 1.5f; // antialias band in px
        var cols = new Color[size * size];

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float a = Mathf.Clamp01((r - d) / edge);
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(cols);
        tex.Apply();
        // 1 unit = full sprite, so the sprite spans 1 world unit at scale 1.
        _cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                            new Vector2(0.5f, 0.5f), size);
        return _cachedCircleSprite;
    }

    //  UPDATE LOOPS 

    void UpdateBob()
    {
        if (isMovingToPlayer) return;
        bobTimer += Time.deltaTime * bobSpeed;
        float yOff = Mathf.Sin(bobTimer) * bobAmplitude;
        transform.position = new Vector3(spawnPos.x, spawnPos.y + yOff, spawnPos.z);

        if (bgObject != null)
            bgObject.transform.Rotate(0, 0, spinSpeed * Time.deltaTime);
    }

    void UpdateGlow()
    {
        if (glowRenderer == null || isMovingToPlayer) return;
        glowTimer += Time.deltaTime * glowPulseSpeed;
        float t = 0.5f + 0.5f * Mathf.Sin(glowTimer);
        Color c = glowRenderer.color;
        c.a = Mathf.Lerp(0.2f, glowAlpha, t);
        glowRenderer.color = c;
        float s = bgRadiusWorld * 2f * glowScaleMultiplier * (1f + 0.08f * Mathf.Sin(glowTimer * 1.3f));
        glowObject.transform.localScale = Vector3.one * s;
    }

    void UpdateMagnet()
    {
        // Co-op: re-anchor to the nearest alive player each frame so the drop is
        // pulled toward whichever player is closest (and retargets if one goes
        // down). With one player this is a no-op re-resolve of the same player.
        var nearest = PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true);
        if (nearest != null) playerTransform = nearest.transform;

        if (playerTransform == null) return;

        // Don't engage the magnet or auto-collect while still arming
        if (Time.time - spawnTime < armDelay) return;

        float dist = Vector3.Distance(transform.position, playerTransform.position);

        if (!isMovingToPlayer && dist <= magnetRange)
        {
            isMovingToPlayer = true;
            startPos = transform.position;
            arcProgress = 0f;
            if (glowObject != null) glowObject.SetActive(false);
        }

        if (isMovingToPlayer)
        {
            arcProgress += arcSpeed * Time.deltaTime;
            if (arcProgress >= 1f) { Collect(); return; }

            Vector3 target = playerTransform.position;
            float arcMul = Mathf.Sin(arcProgress * Mathf.PI);
            Vector3 linear = Vector3.Lerp(startPos, target, arcProgress);
            transform.position = linear + Vector3.up * arcHeight * arcMul;
        }

        if (dist <= collectionRadius) Collect();
    }

    //  COLLECTION 

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isCollected) return;
        if (Time.time - spawnTime < armDelay) return;
        if (other.CompareTag("Player")
            || other.GetComponent<PlayerMovement>() != null
            || other.GetComponent<PlayerStats>() != null)
        {
            var collector = other.GetComponent<PlayerRef>() ?? other.GetComponentInParent<PlayerRef>();
            Collect(collector);
        }
    }

    public void Collect(PlayerRef collector = null)
    {
        if (isCollected) return;
        isCollected = true;

        // 1) Persistent blueprint registry (cross-run permanence). SHARED: this
        //    makes the matching unlock-augment eligible in the reward pool for
        //    BOTH players (the perma-upgrade the player asked for).
        if (WeaponBlueprintRegistry.Instance != null && slotIndex >= 0)
            WeaponBlueprintRegistry.Instance.UnlockBlueprint(slotIndex);

        // 2) In-run hotbar unlock + auto-equip
        // WeaponUnlockRegistry.ForceUnlock fires OnUnlocksChanged, which
        // WeaponRollController already listens to — it rebuilds its active
        // list, sets the new slot as the current index, and calls
        // EquipWeapon()/EquipTool() automatically. We get the full behavior
        // for free with a single call.
        // Per-run hotbar equip goes to the COLLECTOR only (their hotbar), since
        // the per-run unlock pool is per-player. The other player gains it by
        // picking the now-eligible augment from their own reward menu.
        if (autoEquipOnCollect && slotIndex >= 0 && WeaponUnlockRegistry.Instance != null)
        {
            WeaponUnlockRegistry.Instance.ForceUnlock(slotIndex, ResolveCollectorIndex(collector));
        }

        // NOTE: We intentionally do NOT call AugmentRegistry.ApplyAugment here.
        // AugmentsMenu.GetExcludedIDs() excludes any applied non-repeatable augment
        // from the random pool. If we marked the matching unlock-augment as applied
        // on pickup, it would be permanently locked out of future rolls — exactly
        // the opposite of what blueprints are supposed to do. The minor cosmetic
        // cost is that the popup may still occasionally offer "Unlock Flamethrower"
        // after you've already picked up the blueprint and have it equipped;
        // selecting that card is harmless (just re-runs the same hot-swap).

        PlayCollectSound();
        StartCoroutine(CollectionEffect());
    }

    // The collector's player index: the explicit collider if known, else the
    // player the magnet is anchored to, else player 0 (single player).
    int ResolveCollectorIndex(PlayerRef collector)
    {
        if (collector != null) return collector.PlayerIndex;
        if (playerTransform != null)
        {
            var pref = playerTransform.GetComponent<PlayerRef>()
                       ?? playerTransform.GetComponentInParent<PlayerRef>();
            if (pref != null) return pref.PlayerIndex;
        }
        return 0;
    }

    void PlayCollectSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.resourceDropCollection, transform.position);
    }

    IEnumerator CollectionEffect()
    {
        if (dropCollider != null) dropCollider.enabled = false;
        if (glowObject != null) glowObject.SetActive(false);

        float duration = 0.45f;
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Expand briefly then shrink — give pickup a "claim" feel distinct from energy.
            float pop = t < 0.3f ? 1f + (t / 0.3f) * 0.35f : 1.35f * (1f - (t - 0.3f) / 0.7f);
            transform.localScale = startScale * pop;

            float a = Mathf.Lerp(1f, 0f, t);
            if (bgRenderer != null) { var c = bgRenderer.color; c.a = a; bgRenderer.color = c; }
            if (iconRenderer != null) { var c = iconRenderer.color; c.a = a; iconRenderer.color = c; }

            transform.Rotate(0, 0, 540f * t * Time.deltaTime);
            yield return null;
        }

        Destroy(gameObject);
    }

    IEnumerator LifetimeTimer()
    {
        yield return new WaitForSeconds(lifetime);
        if (!isCollected) StartCoroutine(FadeAndDie());
    }

    IEnumerator FadeAndDie()
    {
        float fade = 1.5f, e = 0f;
        Color cBg = bgRenderer != null ? bgRenderer.color : Color.clear;
        Color cIc = iconRenderer != null ? iconRenderer.color : Color.clear;
        while (e < fade)
        {
            e += Time.deltaTime;
            float t = e / fade;
            if (bgRenderer != null) { var c = cBg; c.a = Mathf.Lerp(cBg.a, 0f, t); bgRenderer.color = c; }
            if (iconRenderer != null) { var c = cIc; c.a = Mathf.Lerp(cIc.a, 0f, t); iconRenderer.color = c; }
            yield return null;
        }
        Destroy(gameObject);
    }
}


