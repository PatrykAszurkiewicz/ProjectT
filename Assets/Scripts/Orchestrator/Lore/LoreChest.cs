using UnityEngine;
using System.Collections;

// LORE CHEST 
// A collectible chest the player walks into to open. On open it pulls a random
// UNDISCOVERED lore fragment from the LoreCodex, marks it discovered (which also
// persists it), shows the scroll pop-up, then plays a little open/despawn animation.

[RequireComponent(typeof(SpriteRenderer))]
public class LoreChest : MonoBehaviour
{
    [Header("Open Settings")]
    [Tooltip("If the player gets within this distance, the chest opens (in addition to trigger contact).")]
    public float openProximityRadius = 0.7f;

    [Tooltip("Collider radius used only when this component has to create its own trigger.")]
    public float triggerRadius = 0.45f;

    [Header("Visuals")]
    [Tooltip("Set by the spawner. true = draw a generated chest sprite; false = keep the prefab's own art.")]
    public bool proceduralVisual = true;

    [Header("Energy Reward")]
    [Tooltip("Energy granted when the chest is opened, on top of the lore fragment. 0 = no energy.")]
    public int energyReward = 10;

    [Tooltip("How many orbs the reward is split into. Purely cosmetic — the total always adds up\n" +
             "to energyReward (e.g. 10 over 3 orbs = 4 + 3 + 3).")]
    [Min(1)] public int energyOrbCount = 3;

    [Tooltip("Seconds between each orb popping out of the chest, so they burst rather than stack.")]
    public float energyOrbInterval = 0.09f;

    [Tooltip("How far the orbs scatter from the chest before the magnet pulls them to the player.")]
    public float energyOrbScatter = 0.6f;

    [Tooltip("Route the orbs through EnergyDropManager so global multipliers and bonus-resource\n" +
             "rolls apply, exactly like enemy drops. OFF = the chest always pays exactly energyReward.")]
    public bool useDropManagerScaling = false;

    [Tooltip("Show a '+10 Energy recovered' line on the lore scroll.")]
    public bool showRewardOnScroll = true;

    [Header("Idle Animation")]
    [Tooltip("How much the chest inflates/deflates each beat (0.12 = ±12% size).")]
    public float pulseAmount = 0.12f;
    [Tooltip("Pulse / bump speed (beats per ~6.3s).")]
    public float pulseSpeed = 3.2f;
    [Tooltip("Small vertical bump synced with the pulse (world units, before chest scale). 0 = none.")]
    public float bumpHeight = 0.1f;
    [Tooltip("Slight squash & stretch so the pulse reads as a bounce rather than a balloon.")]
    [Range(0f, 1f)] public float squashStretch = 0.5f;

    private SpriteRenderer sr;
    private Transform playerTransform;
    private bool isOpened;
    private Vector3 baseScale = Vector3.one;
    private float baseY;
    private float phase;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        FindPlayer();

        if (proceduralVisual && (sr.sprite == null))
        {
            sr.sprite = CreateChestSprite();
        }
        sr.sortingLayerName = "Default";

        // Y-sort against grass, exactly like the gremlin does.
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
            ysort.sortYOffset = -0.2f;
        }

        // Ensure a trigger collider exists so walking onto the chest opens it.
        var col = GetComponent<Collider2D>();
        if (col == null)
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = triggerRadius;
            circle.isTrigger = true;
        }
        else
        {
            col.isTrigger = true; // never block movement / enemy navigation
        }

        // Captured AFTER the spawner has applied chestScale, so the pulse oscillates
        // around the chest's real size.
        baseScale = transform.localScale;
        baseY = transform.position.y;
        phase = Random.value * Mathf.PI * 2f;

        EnsureVisible();
    }

    void FindPlayer()
    {
        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) { playerTransform = pm.transform; return; }
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerTransform = p.transform;
    }

    void EnsureVisible()
    {
        // Only generate art for procedural chests; never overwrite a prefab's own art.
        if (proceduralVisual && sr.sprite == null) sr.sprite = CreateChestSprite();
        if (sr.sprite != null) { var c = sr.color; c.a = 1f; sr.color = c; }
    }

    void Update()
    {
        if (isOpened) return;

        // Idle pulse: inflate/deflate (with a touch of squash-and-stretch) plus a
        // small up/down bump, so the chest breathes/bounces instead of hovering.
        phase += Time.deltaTime * pulseSpeed;
        float s = Mathf.Sin(phase);
        float grow = 1f + s * pulseAmount;
        float squash = 1f - s * pulseAmount * squashStretch; // narrows as it grows taller
        transform.localScale = new Vector3(baseScale.x * squash, baseScale.y * grow, baseScale.z);

        if (bumpHeight > 0f)
        {
            var pos = transform.position;
            pos.y = baseY + Mathf.Abs(s) * bumpHeight; // little hop off the ground
            transform.position = pos;
        }

        // Proximity fallback (covers setups where trigger contact doesn't fire).
        if (playerTransform == null) FindPlayer();
        if (playerTransform != null)
        {
            float d = Vector2.Distance(transform.position, playerTransform.position);
            if (d <= openProximityRadius) Open();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isOpened) return;
        if (other.CompareTag("Player") || other.GetComponent<PlayerMovement>() != null)
            Open();
    }

    /// True from the moment the player triggers this chest. The spawner uses it so it
    /// never prunes a chest that's already being read, and the path trail uses it so it
    /// stops pointing at a chest that's spent.
    public bool IsOpened => isOpened;

    public void Open()
    {
        if (isOpened) return;
        isOpened = true;

        // Settle the pulse: reset to base size + base Y so the despawn looks clean.
        transform.localScale = baseScale;
        var pos = transform.position; pos.y = baseY; transform.position = pos;

        PlayOpenSound();
        StartCoroutine(OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        var popup = LoreScrollPopup.Instance;

        // If another chest's scroll is on screen, wait our turn. The scroll is modal and
        // silently ignores a second ShowFragment — without this wait, a chest opened
        // during that window would pick a fragment, mark it discovered, and the player
        // would never see it. It'd surface in the archive already read.
        if (popup != null)
            while (popup.IsOpen) yield return null;

        // Pick AFTER the wait, so we claim the fragment at the moment we can show it.
        var codex = LoreCodex.Instance;
        int id = codex != null ? codex.PickRandomUndiscoveredId() : -1;

        LoreFragment fragment = id >= 0 ? LoreContent.Get(id) : null;
        if (fragment == null)
        {
            // Nothing left (or the id has no content) — show a closing note, claim nothing.
            id = -1;
            fragment = AllFoundFragment();
        }

        bool shown = fragment != null && popup != null && popup.ShowFragment(fragment, RewardNote());

        // Discover only once the scroll is actually up. PickRandomUndiscoveredId never
        // returns a known id and Discover is a set-add, so a fragment can't repeat.
        if (shown && id >= 0) codex.Discover(id);   // persists to prefs + fires events

        yield return StartCoroutine(OpenAndDespawn());
    }

    /// Remove this chest without giving anything — used by the spawner when the codex runs
    /// out and a still-unopened chest has nothing left to hand over.
    public void Vanish()
    {
        if (isOpened) return;   // already claimed by the player; let it finish its scroll
        isOpened = true;        // also stops the idle pulse in Update
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        // Deliberately does NOT snap to baseScale/baseY: Vanish can fire the same frame
        // the chest spawned, before Start has captured them, and that would pop a
        // scaled-up chest back to 1×. Fading from wherever it is looks right either way.
        StartCoroutine(FadeOutAndDestroy(0.5f));
    }

    private static LoreFragment AllFoundFragment()
    {
        return new LoreFragment(-1, "The Archive is Complete",
            "Empty — but for dust and the smell of cold brass. You have recovered every log that survived the spill. Whatever else this place knew, it took down into the dark with it.");
    }

    private void PlayOpenSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.openChest.IsNull)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.openChest, transform.position);
    }

    // "+10 Energy recovered", or null when there's nothing to advertise. With
    // useDropManagerScaling on, the real payout can differ (multiplier / bonus roll),
    // so we stay vague rather than promise a number we might not pay.
    private string RewardNote()
    {
        if (!showRewardOnScroll || energyReward <= 0) return null;
        return useDropManagerScaling ? "Energy recovered" : $"+{energyReward} Energy recovered";
    }

    // Pops the reward out as real EnergyDrop orbs: they scatter, glow, then arc into
    // the player (who is standing right here — that's how the chest opened), which is
    // what actually credits the gauge via EnergyManager.GivePlayerEnergy.
    private IEnumerator SpawnEnergyReward()
    {
        int count = Mathf.Max(1, energyOrbCount);
        int remaining = Mathf.Max(0, energyReward);
        if (remaining == 0) yield break;

        count = Mathf.Min(count, remaining);   // never spawn 0-value orbs

        for (int i = 0; i < count; i++)
        {
            // Even split with the remainder spread over the first orbs (10/3 → 4,3,3).
            int share = Mathf.CeilToInt(remaining / (float)(count - i));
            remaining -= share;

            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(0.15f, energyOrbScatter);
            Vector3 pos = transform.position + (Vector3)offset;
            pos.z = 0f;

            if (useDropManagerScaling)
                EnergyDropManager.TrySpawnEnergyDrop(pos, 1f, share);   // chance 1 = guaranteed
            else
                EnergyDrop.CreateEnergyDrop(pos, share);

            if (energyOrbInterval > 0f && i < count - 1)
                yield return new WaitForSeconds(energyOrbInterval);
        }
    }

    private IEnumerator OpenAndDespawn()
    {
        // The scroll pauses the game (timeScale 0); wait — unscaled — for it to close.
        var popup = LoreScrollPopup.Instance;
        if (popup != null)
        {
            // Let it open first.
            float guard = 0f;
            while (!popup.IsOpen && guard < 2f) { guard += Time.unscaledDeltaTime; yield return null; }
            while (popup.IsOpen) yield return null;
        }

        // Orbs go out AFTER the scroll closes — they move on scaled time, so spawning
        // them during the pause would just freeze them behind the backdrop. Awaited
        // here (not fire-and-forget) because Destroy below would kill the coroutine.
        if (energyReward > 0)
            yield return StartCoroutine(SpawnEnergyReward());

        // Fade + shrink out (now back at normal time scale).
        yield return StartCoroutine(FadeOutAndDestroy(0.45f));
    }

    private IEnumerator FadeOutAndDestroy(float dur)
    {
        float t = 0f;
        Vector3 startScale = transform.localScale;
        Color start = sr != null ? sr.color : Color.white;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            transform.localScale = Vector3.Lerp(startScale, startScale * 0.6f, u);
            if (sr != null) sr.color = new Color(start.r, start.g, start.b, Mathf.Lerp(1f, 0f, u));
            yield return null;
        }
        Destroy(gameObject);
    }

    // Simple generated treasure-chest sprite (brown body, banded lid, gold lock).
    Sprite CreateChestSprite()
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        Color clear = new Color(0, 0, 0, 0);
        Color wood = new Color(0.45f, 0.28f, 0.13f, 1f);
        Color woodDark = new Color(0.32f, 0.19f, 0.09f, 1f);
        Color band = new Color(0.62f, 0.45f, 0.18f, 1f);   // brass banding
        Color bandDark = new Color(0.42f, 0.30f, 0.12f, 1f);
        Color gold = new Color(0.92f, 0.78f, 0.30f, 1f);
        Color outline = new Color(0.12f, 0.07f, 0.03f, 1f);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        // Body box.
        int left = 10, right = 54, bottom = 8, top = 44;
        for (int y = bottom; y <= top; y++)
        {
            for (int x = left; x <= right; x++)
            {
                bool edge = (x == left || x == right || y == bottom || y == top);
                tex.SetPixel(x, y, edge ? outline : wood);
            }
        }

        // Lid arc (top third), slightly darker.
        int lidBase = 32;
        for (int y = lidBase; y <= top; y++)
            for (int x = left + 1; x < right; x++)
                tex.SetPixel(x, y, woodDark);

        // Lid seam.
        for (int x = left; x <= right; x++) tex.SetPixel(x, lidBase, outline);

        // Vertical brass bands.
        foreach (int bx in new int[] { 18, 32, 46 })
        {
            for (int y = bottom + 1; y < top; y++)
            {
                tex.SetPixel(bx, y, band);
                tex.SetPixel(bx + 1, y, bandDark);
            }
        }

        // Lock plate.
        for (int y = 24; y <= 34; y++)
            for (int x = 29; x <= 35; x++)
                tex.SetPixel(x, y, (x == 29 || x == 35 || y == 24 || y == 34) ? bandDark : gold);
        // Keyhole.
        tex.SetPixel(32, 30, outline);
        tex.SetPixel(32, 29, outline);

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }
}
