using UnityEngine;
using System.Collections.Generic;

// Controller for the Parfumer enemy.


[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(Rigidbody2D))]
public class ParfumerController : MonoBehaviour, ISpritePrewarm
{
    // Auto-prewarm hook (GameOrchestrator finds this on the prefab). Warms the body
    // frames through the Parfumer's own cache so the first spawn doesn't stall.
    // Skipped entirely once the frames are direct references: those load with the
    // scene, off the main thread, so there is nothing left to warm and probing
    // Resources for a path that no longer exists would just be wasted work.
    public void PrewarmSpriteFolders()
    {
        // Bake the snow-contrast smoke textures during loading, not on the first drop.
        PoisonCloudVisual.WarmUp();

        if (HasDirectFrames()) return;
        Prewarm(spriteFolderPath);
    }

    // True when frames are available without touching Resources — either assigned
    // straight on this component, or present on the EnemyData asset.
    private bool HasDirectFrames() => ResolveDirectFrames() != null;

    [Header("Targeting")]
    [Tooltip("How close (world units) the Parfumer wants to be to the player " +
             "before stopping. Keeps it from grinding into the player's " +
             "collider while still parking inside its own cloud radius.")]
    [SerializeField] private float stoppingDistance = 1.2f;

    [Tooltip("If true, when no player exists the Parfumer falls back to walking " +
             "toward the core. Prevents it from idling in place — which would " +
             "soft-lock the wave, since it deals no direct damage. " +
             "Strongly recommended ON.")]
    [SerializeField] private bool fallbackToCoreIfNoPlayer = true;

    [Header("Poison Cloud")]
    [Tooltip("Prefab spawned as the lingering poison patch. If left null, a " +
             "GameObject is created procedurally at runtime (with " +
             "BufferFogVisual + PoisonCloud attached).")]
    [SerializeField] private GameObject cloudPrefab;

    [Tooltip("Seconds between cloud drops.")]
    [SerializeField] private float cloudDropInterval = 2.0f;

    [Tooltip("How long each spawned cloud patch lingers.")]
    [SerializeField] private float cloudDuration = 5f;

    [Tooltip("Radius of each spawned cloud patch.")]
    [SerializeField] private float cloudRadius = 2.5f;

    [Tooltip("Seconds the poison keeps ticking on the player AFTER exposure. " +
             "The defining Parfumer trait — default 20.")]
    [SerializeField] private float poisonDuration = 20f;

    [Tooltip("Damage per second dealt to the player while the poison is active.")]
    [SerializeField] private float poisonDamagePerSecond = 6f;

    [Header("Cloud Visual")]
    [Tooltip("Toggle the soft green mist body.")]
    [SerializeField] private bool cloudEnableMist = true;

    [Tooltip("Toggle the curling pale tendrils sprouting from the cloud.")]
    [SerializeField] private bool cloudEnableWisps = true;

    [Tooltip("Mist body color. Alpha drives additive intensity, not transparency.")]
    [SerializeField] private Color cloudMistColor = new Color(0.25f, 0.65f, 0.15f, 0.30f);

    [Tooltip("Wisp / tendril color. A pale toxic green reads against the mist.")]
    [SerializeField] private Color cloudWispColor = new Color(0.55f, 0.95f, 0.40f, 0.55f);

    [Tooltip("Transparent edge color for mist falloff. Keep alpha 0.")]
    [SerializeField] private Color cloudOuterColor = new Color(0.10f, 0.30f, 0.05f, 0f);

    [Header("Cloud Snow Contrast")]
    [Tooltip("Adds soft, alpha-blended smoke UNDER the mist above. The mist is additive, and " +
             "additive light can't brighten white snow, so on its own it vanishes there. This " +
             "layer gives it body on bright ground and is barely noticeable on dark ground.")]
    [SerializeField] private bool cloudSnowContrast = true;

    [Tooltip("Look of the contrast smoke. Read live, so tweaks during Play affect clouds on screen.")]
    [SerializeField] private PoisonCloudVisual.Settings cloudContrast = new PoisonCloudVisual.Settings();

    [Header("Death VFX")]
    [Tooltip("Duration of the disintegration VFX played when the Parfumer dies. " +
             "Set at runtime via EnemyStats.ConfigureDeathVfx(). Below 1.0 = " +
             "'classic chunks'; 1.0+ = boss-style sprite-shatter. 0 disables.")]
    [SerializeField] private float deathVfxDuration = 0.7f;

    [Header("Body Animation")]
    [Tooltip("PREFERRED. The Parfumer's animation frames, in order, as direct " +
             "references. Leave empty to use EnemyData.frames instead (which is " +
             "where the Parfumer's frames already live), and only then falls back " +
             "to the legacy Resources path below.\n\n" +
             "Direct references are the point: a Resources path is a string, so " +
             "Unity cannot statically analyse it, must ship everything under " +
             "Resources/, and can never strip it. A direct reference puts the art " +
             "back in the dependency graph and lets the atlas packer do its job.")]
    [SerializeField] private Sprite[] bodyFrameSprites;

    [Tooltip("DEPRECATED fallback. Resources-relative folder holding the frames " +
             "(00.png .. NN.png). Used only when neither Body Frame Sprites above " +
             "nor EnemyData.frames has any entries. Safe to leave empty.")]
    [SerializeField] private string spriteFolderPath = "";

    [Tooltip("Cap on how many loaded frames to cycle. The Parfumer ships with 16 " +
             "frames (00–15). Set 0 to cycle every frame found in the folder.")]
    [SerializeField] private int animationFrameCount = 16;

    [Tooltip("Seconds each body frame is shown. 0.09 ≈ 11 fps — a gentle drift " +
             "that suits a drifting mist creature.")]
    [SerializeField] private float animationFrameDuration = 0.09f;

    [Header("Aura (green pulsation)")]
    [Tooltip("Spawn a soft green glow that breathes behind the Parfumer's body.")]
    [SerializeField] private bool enableAura = true;

    [Tooltip("Aura tint. A toxic green matching the poison cloud reads best. " +
             "Alpha here is ignored — it's driven by the pulse min/max below.")]
    [SerializeField] private Color auraColor = new Color(0.45f, 0.95f, 0.35f, 1f);

    [Tooltip("Aura diameter as a multiple of the body's on-screen size, so it " +
             "scales correctly regardless of the sprite's pixels-per-unit.")]
    [SerializeField] private float auraSizeMultiplier = 1.55f;

    [Tooltip("Vertical offset of the glow as a fraction of the body height. " +
             "A small negative value nudges it toward the sting.")]
    [SerializeField] private float auraYOffsetFraction = -0.08f;

    [Tooltip("How fast the glow breathes (higher = quicker pulse).")]
    [SerializeField] private float auraPulseSpeed = 2.0f;

    [Tooltip("Alpha at the dim end of the pulse.")]
    [SerializeField, Range(0f, 1f)] private float auraMinAlpha = 0.12f;

    [Tooltip("Alpha at the bright end of the pulse.")]
    [SerializeField, Range(0f, 1f)] private float auraMaxAlpha = 0.34f;

    [Tooltip("How much the glow grows/shrinks over the pulse, as a fraction of " +
             "its diameter (0.12 = ±12%).")]
    [SerializeField] private float auraScalePulse = 0.14f;

    [Header("Hover (flying bob)")]
    [Tooltip("Gently bob the body up and down so it reads as airborne. The bob is " +
             "reconciled so it never drifts the physics body.")]
    [SerializeField] private bool hoverEnabled = true;

    [Tooltip("Bob height as a fraction of the body height. Keep it subtle.")]
    [SerializeField] private float hoverAmplitudeFraction = 0.05f;

    [Tooltip("Bobs per second.")]
    [SerializeField] private float hoverSpeed = 1.1f;

    [Header("Emit Squash (inflate/deflate on cloud drop)")]
    [Tooltip("Briefly inflate then settle when a poison cloud is released, like " +
             "an exhale.")]
    [SerializeField] private bool emitSquashEnabled = true;

    [Tooltip("Peak inflation on a drop (0.12 = +12%).")]
    [SerializeField] private float emitSquashAmount = 0.12f;

    [Tooltip("Seconds for the inflate/deflate to settle back to normal.")]
    [SerializeField] private float emitSquashDuration = 0.45f;

    [Header("Sting Poison Emission")]
    [Tooltip("Drip little green poison puffs from the lower sting.")]
    [SerializeField] private bool stingEmissionEnabled = true;

    [Tooltip("Sting-tip offset FROM THE SPRITE PIVOT, in source pixels (x right, " +
             "y up). Default is measured from the provided 480×702 frame. Nudge " +
             "if your pivot or art differs.")]
    [SerializeField] private Vector2 stingPixelOffset = new Vector2(19f, -142f);

    [Tooltip("Poison puff tint.")]
    [SerializeField] private Color stingColor = new Color(0.50f, 0.95f, 0.35f, 1f);

    [Tooltip("Puffs per second dripping from the sting between clouds.")]
    [SerializeField] private float stingTrickleRate = 5f;

    [Tooltip("Extra puffs released the instant a cloud is dropped.")]
    [SerializeField] private int stingBurstCount = 10;

    // Cached references
    private EnemyStats stats;
    private Rigidbody2D rb;
    private Transform playerTransform;
    private Transform coreTransform;

    private float cloudDropTimer;
    private Transform currentTarget;
    private float smokeShufflePhase;

    // Body animation state (driven directly by this controller — see class note).
    private SpriteRenderer bodyRenderer;
    private Sprite[] bodyFrames;
    private int bodyFrameIndex;
    private float bodyFrameTimer;

    // Hover / squash / sting state.
    private float bodyWorldSize = 1.5f;   // on-screen body size, used to scale effects
    private float hoverPhase;
    private float lastBobApplied;         // reconciles the bob so it never drifts physics
    private float emitSquashTimer = -1f;  // <0 = inactive
    private Vector3 emitBaseScale;        // localScale captured when a squash begins
    private float stingTrickleTimer;

    // Aura: a plain child SpriteRenderer, driven directly by this controller
    // (no separate component/script).
    private SpriteRenderer auraRenderer;
    private float auraPhase;

    // Sting poison puffs: plain world-space SpriteRenderers this controller spawns,
    // advances, and cleans up itself (again, no separate script).
    private readonly List<Puff> puffs = new List<Puff>();

    private struct Puff
    {
        public Transform t;
        public SpriteRenderer sr;
        public Vector3 vel;
        public float age, life, startScale, endScale, startAlpha;
        public Color tint;
    }

    // One soft radial sprite shared by the aura and every puff (built once).
    private static Sprite _softGlow;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();

        // Auto-add Y-sort entity (EnemyController normally does this; we replace
        // it, so we take over the job — same values the Buffer uses).
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
            ysort.sortYOffset = -0.2f;
        }

        // The Parfumer never melees — its damage is purely the cloud
        var animController = GetComponent<EnemyAnimationController>();
        if (animController != null)
            animController.SetAutoAttackDetectionEnabled(false);

        // Load the Parfumer's frames and show the first one. The per-frame cycling
        // happens in Update() (replaces the old static sprite).
        LoadBodyFrames();

        // Measure the body's on-screen size so all the effects below scale with it
        // regardless of the sprite's pixels-per-unit.
        bodyWorldSize = ComputeBodyWorldSize();

        // Spawn the subtle green pulsating aura behind the body.
        SetupAura();

        // No-op after the first call; covers scenes that skip the loading-screen prewarm.
        PoisonCloudVisual.WarmUp();

        // Configure the death VFX on the EnemyStats so we don't depend on the
        // prefab inspector having the right value.
        if (stats != null && deathVfxDuration > 0f)
        {
            stats.ConfigureDeathVfx(deathVfxDuration, destroyHealthBarBeforeVfx: true);
        }

        // Stagger the first drop so a group of Parfumers doesn't fire their
        // initial clouds all on the same frame.
        cloudDropTimer = Random.Range(0f, cloudDropInterval * 0.5f);
        smokeShufflePhase = SmokeBlind.NewPhase();
    }

    // Loads the frames from Resources (same folder + ordering the rest of the game
    // uses) and paints the first one so the enemy isn't blank for a frame.
    // PERF: run-wide cache of the Parfumer body frames. LoadAll + the individual-
    // file fallback + the ordinal sort used to run on EVERY Parfumer spawn; now they
    // run at most once per folder for the whole run. Cleared on play-mode exit so
    // "fast enter play mode" (domain reload off) doesn't hold last session's sprites.
    private static readonly System.Collections.Generic.Dictionary<string, Sprite[]> _frameCache
        = new System.Collections.Generic.Dictionary<string, Sprite[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFrameCache() => _frameCache.Clear();

    /// <summary>
    /// Loads (once) and caches a Parfumer sprite folder: LoadAll, then an
    /// individual-file fallback for folders LoadAll doesn't return in one shot,
    /// then an ordinal sort. Returns an empty (never null) array on a miss. The
    /// returned array is shared and must be treated as read-only.
    /// </summary>
    public static Sprite[] LoadFramesCached(string spriteFolderPath)
    {
        if (string.IsNullOrEmpty(spriteFolderPath)) return System.Array.Empty<Sprite>();
        if (_frameCache.TryGetValue(spriteFolderPath, out var cached)) return cached;

        Sprite[] loaded = Resources.LoadAll<Sprite>(spriteFolderPath);

        // Fallback: load 00, 01, 02 ... individually (handles folders that LoadAll
        // doesn't return in one shot).
        if (loaded == null || loaded.Length == 0)
        {
            var list = new System.Collections.Generic.List<Sprite>();
            for (int i = 0; i < 512; i++)
            {
                Sprite s = Resources.Load<Sprite>($"{spriteFolderPath}/{i:D2}");
                if (s == null) break;
                list.Add(s);
            }
            loaded = list.ToArray();
        }

        if (loaded == null) loaded = System.Array.Empty<Sprite>();

        // Sort by name so 00..15 play in order regardless of load order.
        if (loaded.Length > 0)
            System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));

        // Cache even an empty result so a genuinely broken folder isn't re-probed
        // (up to 512 Resources.Load calls) on every subsequent Parfumer spawn.
        _frameCache[spriteFolderPath] = loaded;
        return loaded;
    }

    /// <summary>
    /// Loading-screen hook: warm a Parfumer folder ahead of the first spawn so the
    /// LoadAll/decode cost doesn't land on a gameplay frame. No-op if already cached.
    /// Called by SpritePrewarmer.
    /// </summary>
    public static void Prewarm(string spriteFolderPath) => LoadFramesCached(spriteFolderPath);

    // Frames that need no Resources lookup: the per-prefab override first, then the
    // EnemyData asset. Returns null when neither has anything.
    //
    // EnemyStats clones enemyData in its Awake, but the clone shares the same Sprite
    // references, so this is correct whichever Awake runs first.
    private Sprite[] ResolveDirectFrames()
    {
        if (bodyFrameSprites != null && bodyFrameSprites.Length > 0)
            return bodyFrameSprites;

        var stats = GetComponent<EnemyStats>();
        var data = stats != null ? stats.enemyData : null;
        if (data != null && data.frames != null && data.frames.Length > 0)
            return data.frames;

        return null;
    }

    private void LoadBodyFrames()
    {
        if (bodyRenderer == null)
            bodyRenderer = GetComponent<SpriteRenderer>();

        // Direct references first.
        //
        // This is the whole fix for "Parfumer found no sprites at Resources/". The
        // art moved out of Resources/ (deliberately — see the long note in
        // EnemyData.cs about why path strings prevent stripping and force everything
        // under Resources/ into the build), and ParfumerData.frames was populated
        // with the sprites. But this loader only ever consulted the Resources path,
        // so it looked in a folder that no longer exists and errored on every spawn.
        //
        // NOT sorted by name: a direct array is already in the order the asset
        // author arranged it, and re-sorting would silently reorder a hand-tuned
        // sequence. The ordinal sort below stays on the Resources path, where the
        // load order genuinely is arbitrary.
        Sprite[] loaded = ResolveDirectFrames();
        bool usingDirectFrames = loaded != null;

        if (loaded == null)
        {
            // PERF: pull the frames from the run-wide cache instead of re-running
            // LoadAll + the individual-file fallback + the sort on EVERY Parfumer
            // spawn. LoadFramesCached does that at most once per folder per run.
            loaded = LoadFramesCached(spriteFolderPath);
        }

        if (loaded == null || loaded.Length == 0)
        {
            if (string.IsNullOrEmpty(spriteFolderPath))
            {
                Debug.LogError(
                    $"[{name}] Parfumer has no body frames. Assign them either on this " +
                    $"component's 'Body Frame Sprites', or on the 'Frames' array of its " +
                    $"EnemyData asset (ParfumerData). Both are empty.");
            }
            else
            {
                Debug.LogError(
                    $"[{name}] Parfumer found no sprites at Resources/{spriteFolderPath}, and " +
                    $"no direct frames are assigned. Either populate EnemyData.frames " +
                    $"(preferred) or make sure that folder exists under a Resources/ " +
                    $"directory with the PNGs imported as 'Sprite (2D and UI)'.");
            }
            return;
        }

        // Optional cap (animationFrameCount > 0) so stray sprites in the folder
        // don't extend the loop past the intended frames.
        //
        // NOTE for the direct-reference path: ParfumerData.frames currently holds 17
        // sprites against a cap of 16, so the LAST one is dropped. That is almost
        // certainly right — it matches the stray OLD00.png sitting in the art folder
        // — but it is silent, so if a frame ever goes missing from the loop this cap
        // is the first thing to check. Set it to 0 to play every assigned frame.
        if (animationFrameCount > 0 && loaded.Length > animationFrameCount)
        {
            var trimmed = new Sprite[animationFrameCount];
            System.Array.Copy(loaded, trimmed, animationFrameCount);

            if (usingDirectFrames && Application.isEditor)
            {
                Debug.LogWarning(
                    $"[{name}] Parfumer has {loaded.Length} assigned frames but " +
                    $"Animation Frame Count is {animationFrameCount}, so the last " +
                    $"{loaded.Length - animationFrameCount} are not played. Set it to 0 " +
                    $"to use all of them.");
            }

            loaded = trimmed;
        }

        bodyFrames = loaded;
        bodyFrameIndex = 0;
        bodyFrameTimer = 0f;

        if (bodyRenderer != null)
            bodyRenderer.sprite = bodyFrames[0];
    }

    // Advances the looping body animation. Called every frame while alive.
    private void AdvanceBodyAnimation()
    {
        if (bodyFrames == null || bodyFrames.Length < 2 || bodyRenderer == null) return;

        float spf = Mathf.Max(0.0001f, animationFrameDuration);
        bodyFrameTimer += Time.deltaTime;

        // while-loop so a hitch that spans several frames doesn't stall the anim.
        while (bodyFrameTimer >= spf)
        {
            bodyFrameTimer -= spf;
            bodyFrameIndex = (bodyFrameIndex + 1) % bodyFrames.Length;
        }

        bodyRenderer.sprite = bodyFrames[bodyFrameIndex];
    }

    // Builds a child glow object with a plain SpriteRenderer. It's driven each
    // frame by UpdateAura() below — no extra component. Being a child, it moves
    // and bobs with the body, and the death VFX (which disables every child
    // renderer) hides it automatically; the body SpriteRenderer on the root is
    // still the one the disintegration snapshots.
    private void SetupAura()
    {
        if (!enableAura) return;

        var auraGO = new GameObject("ParfumerAura");
        auraGO.transform.SetParent(transform, worldPositionStays: false);

        auraRenderer = auraGO.AddComponent<SpriteRenderer>();
        auraRenderer.sprite = GetSoftGlowSprite();
        auraPhase = Random.Range(0f, Mathf.PI * 2f);
        UpdateAura(); // paint an initial frame so it isn't a hard pop-in
    }

    // Pulses the aura's alpha and size, and keeps it one sort step behind the body.
    // World size/offset are divided by the parent's scale so the prefab's 0.25 root
    // scale doesn't shrink the glow.
    private void UpdateAura()
    {
        if (auraRenderer == null) return;

        auraPhase += Time.deltaTime * auraPulseSpeed;
        float k = Mathf.Sin(auraPhase) * 0.5f + 0.5f; // 0..1

        Color c = auraColor;
        c.a = Mathf.Lerp(auraMinAlpha, auraMaxAlpha, k);
        auraRenderer.color = c;

        float parentScale = Mathf.Abs(transform.lossyScale.x) < 1e-4f ? 1f : transform.lossyScale.x;
        float worldD = (bodyWorldSize * auraSizeMultiplier) *
                       (1f + Mathf.Lerp(-auraScalePulse, auraScalePulse, k));
        float localD = worldD / parentScale;
        auraRenderer.transform.localScale = new Vector3(localD, localD, 1f);
        auraRenderer.transform.localPosition =
            new Vector3(0f, (bodyWorldSize * auraYOffsetFraction) / parentScale, 0f);

        if (bodyRenderer != null)
        {
            auraRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
            auraRenderer.sortingOrder = bodyRenderer.sortingOrder - 1;
        }
    }

    private void Update()
    {
        if (stats == null || stats.IsDead()) return;

        AdvanceBodyAnimation();
        UpdateStingTrickle();
        UpdatePuffs();

        UpdateTarget();

        cloudDropTimer += Time.deltaTime;
        if (cloudDropTimer >= cloudDropInterval)
        {
            cloudDropTimer = 0f;
            SpawnCloud();
        }
    }

    // Hover bob and the emit squash are applied in LateUpdate so they sit on top
    // of the frame's physics/animation without being overwritten.
    private void LateUpdate()
    {
        if (stats == null || stats.IsDead()) return;

        ApplyHoverBob();
        ApplyEmitSquash();
        UpdateAura();
    }

    // Gentle vertical bob. We only ever add the FRAME-TO-FRAME DELTA of the bob to
    // the position and the bob is periodic, so over a full cycle the net offset is
    // zero — the dynamic rigidbody never drifts, and horizontal movement is
    // untouched. Two summed sines give it a slightly organic, non-mechanical feel.
    private void ApplyHoverBob()
    {
        if (!hoverEnabled) return;

        hoverPhase += Time.deltaTime * hoverSpeed * Mathf.PI * 2f;
        float amp = hoverAmplitudeFraction * bodyWorldSize;
        float bob = amp * (Mathf.Sin(hoverPhase) * 0.85f + Mathf.Sin(hoverPhase * 2.3f) * 0.15f);

        float delta = bob - lastBobApplied;
        lastBobApplied = bob;
        transform.position += new Vector3(0f, delta, 0f);
    }

    // Brief inflate-then-settle after a cloud drop. localScale is only touched
    // while a squash is active (captured fresh each time), so between drops we
    // don't fight anything else that might scale the enemy.
    private void ApplyEmitSquash()
    {
        if (!emitSquashEnabled || emitSquashTimer < 0f) return;

        emitSquashTimer += Time.deltaTime;
        float t = emitSquashTimer / Mathf.Max(0.05f, emitSquashDuration);

        if (t >= 1f)
        {
            transform.localScale = emitBaseScale;
            emitSquashTimer = -1f;
            return;
        }

        // Fast rise, gentle fall — a puff/exhale shape (peak a little before mid).
        float bump = emitSquashAmount * Mathf.Sin(Mathf.Pow(Mathf.Clamp01(t), 0.7f) * Mathf.PI);
        transform.localScale = emitBaseScale * (1f + bump);
    }

    private void FixedUpdate()
    {
        if (stats == null || stats.IsDead() || rb == null) return;

        if (currentTarget == null)
        {
            // No target — hold position. Killing residual velocity keeps the
            // Parfumer from drifting after a nudge.
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Smoke Screen: if a smoke cloud blocks our sightline to the target, we
        // lose sight of it and mill in place until the smoke clears — same as
        // every other enemy.
        if (SmokeBlind.Blocks(transform.position, currentTarget.position))
        {
            rb.linearVelocity = SmokeBlind.ShuffleVelocity(smokeShufflePhase, stats.MoveSpeed);
            return;
        }

        Vector2 toTarget = (Vector2)currentTarget.position - rb.position;
        float dist = toTarget.magnitude;

        if (dist <= stoppingDistance)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 dir = toTarget / dist; // normalized without re-sqrt
        rb.linearVelocity = dir * stats.MoveSpeed;
    }

    private void UpdateTarget()
    {
        // Co-op: chase the nearest alive player. Resolving every frame means the
        // Parfumer retargets automatically when a player goes down. includeCloaked
        // is true to preserve the Parfumer's original cloak-agnostic chasing.
        // With one player this is identical to the old single lookup.
        var nearest = PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true);
        if (nearest != null)
        {
            playerTransform = nearest.transform;
            currentTarget = playerTransform;
            return;
        }
        playerTransform = null;

        // Fallback to the core so we never idle and soft-lock the wave.
        if (fallbackToCoreIfNoPlayer)
        {
            if (coreTransform == null)
            {
                GameObject coreGO = GameObject.FindGameObjectWithTag("Core");
                coreTransform = coreGO != null ? coreGO.transform : null;
            }
            currentTarget = coreTransform;
        }
        else
        {
            currentTarget = null;
        }
    }

    private void PlaySmokeSound()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (FMODEvents.instance.parfumerSmoke.IsNull) return;
        AudioManager.instance.PlayOneShot(FMODEvents.instance.parfumerSmoke, transform.position);
    }

    private void SpawnCloud()
    {
        PlaySmokeSound();

        GameObject cloudGO;
        if (cloudPrefab != null)
        {
            cloudGO = Instantiate(cloudPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            // Procedural fallback: build the cloud from scratch so the designer
            // doesn't need to wire a prefab to get the enemy working.
            cloudGO = new GameObject("PoisonCloud");
            cloudGO.transform.position = transform.position;
            cloudGO.AddComponent<BufferFogVisual>();
            cloudGO.AddComponent<PoisonCloud>();
        }

        var cloud = cloudGO.GetComponent<PoisonCloud>();
        if (cloud == null) cloud = cloudGO.AddComponent<PoisonCloud>();

        var visual = cloudGO.GetComponent<BufferFogVisual>();
        if (visual == null) visual = cloudGO.AddComponent<BufferFogVisual>();

        // Pass our own gameObject as attacker so any "killed by" tracking
        // attributes poison damage to the Parfumer.
        cloud.Configure(
            radius: cloudRadius,
            duration: cloudDuration,
            poisonDuration: poisonDuration,
            // Nightmare's +30% reaches the poison's direct player damage too, matching melee.
            // ScaleDamage gives the poison the same augment x per-stage x difficulty
            // multiplier melee gets. It previously applied difficulty ONLY, so the
            // enemy-damage augment and per-stage scaling silently skipped it.
            poisonDamagePerSecond: stats != null
                ? stats.ScaleDamage(poisonDamagePerSecond)
                : poisonDamagePerSecond,
            attacker: this.gameObject);

        // Reuse the Buffer's procedural fog visual, recolored green. Lightning
        // and the stasis storm are Buffer-flavored, so they're left off.
        visual.Configure(
            radius: cloudRadius,
            duration: cloudDuration,
            enableMist: cloudEnableMist,
            enableWisps: cloudEnableWisps,
            enableLightning: false,
            enableStasisStorm: false,
            mistColorOverride: cloudMistColor,
            wispColorOverride: cloudWispColor,
            outerColorOverride: cloudOuterColor);

        // Soft smoke under the additive mist so the cloud stays visible on snow.
        // It sorts itself just below the fog's renderers.
        if (cloudSnowContrast)
        {
            var contrast = cloudGO.GetComponent<PoisonCloudVisual>();
            if (contrast == null) contrast = cloudGO.AddComponent<PoisonCloudVisual>();
            contrast.Configure(cloudRadius, cloudDuration, cloudContrast,
                               fallbackSortingLayerID: bodyRenderer != null ? bodyRenderer.sortingLayerID : 0,
                               fallbackSortingOrder: 0);
        }

        // Body reacts to the release: a quick inflate/deflate and a burst of
        // poison from the sting.
        if (emitSquashEnabled)
        {
            emitBaseScale = transform.localScale;
            emitSquashTimer = 0f;
        }
        if (stingEmissionEnabled && stingBurstCount > 0)
            SpawnStingPuffs(stingBurstCount);
    }

    // ----- effect helpers -------------------------------------------------

    // On-screen size of the body in world units, used to scale every effect so
    // they don't depend on the sprite's pixels-per-unit or the prefab's scale.
    private float ComputeBodyWorldSize()
    {
        if (bodyRenderer != null && bodyRenderer.sprite != null)
        {
            Vector3 s = bodyRenderer.bounds.size;
            float m = Mathf.Max(s.x, s.y);
            if (m > 0.0001f) return m;
        }
        return 1.5f;
    }

    // Steady drip of poison from the sting between cloud drops.
    private void UpdateStingTrickle()
    {
        if (!stingEmissionEnabled || stingTrickleRate <= 0f) return;

        stingTrickleTimer += Time.deltaTime;
        float interval = 1f / stingTrickleRate;
        // Cap iterations so a huge hitch can't spawn a flood.
        int guard = 0;
        while (stingTrickleTimer >= interval && guard++ < 8)
        {
            stingTrickleTimer -= interval;
            SpawnStingPuffs(1);
        }
        if (guard >= 8) stingTrickleTimer = 0f;
    }

    // World position of the sting tip. Derived from the sprite pivot + a pixel
    // offset, converted through the transform so it inherits position, the
    // walk-lean rotation, scale, and (via the flip check) facing.
    private Vector3 GetStingWorldPosition()
    {
        float ppu = 100f;
        bool flip = false;
        if (bodyRenderer != null)
        {
            flip = bodyRenderer.flipX;
            if (bodyRenderer.sprite != null) ppu = bodyRenderer.sprite.pixelsPerUnit;
        }
        if (ppu <= 0f) ppu = 100f;

        float x = (flip ? -1f : 1f) * stingPixelOffset.x;
        Vector3 local = new Vector3(x / ppu, stingPixelOffset.y / ppu, 0f);
        return transform.TransformPoint(local);
    }

    private void SpawnStingPuffs(int count)
    {
        Vector3 pos = GetStingWorldPosition();
        int order = bodyRenderer != null ? bodyRenderer.sortingOrder + 1 : 1;
        int layer = bodyRenderer != null ? bodyRenderer.sortingLayerID : 0;
        float size = bodyWorldSize * 0.12f;

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("PoisonPuff");   // world-space (not parented)
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetSoftGlowSprite();
            sr.sortingLayerID = layer;
            sr.sortingOrder = order;

            // Poison sinks: mostly downward with a little sideways spread.
            float spread = Random.Range(-0.55f, 0.55f);
            float speed = size * Random.Range(1.4f, 2.6f);

            var p = new Puff
            {
                t = go.transform,
                sr = sr,
                vel = new Vector3(Mathf.Sin(spread) * speed * 0.6f,
                                  -Mathf.Abs(Mathf.Cos(spread)) * speed - size * 0.4f, 0f),
                age = 0f,
                life = Random.Range(0.5f, 1.0f),
                startScale = size * Random.Range(0.35f, 0.6f),
                endScale = size * Random.Range(0.95f, 1.5f),
                startAlpha = Random.Range(0.35f, 0.6f),
                tint = stingColor,
            };
            go.transform.localScale = Vector3.one * p.startScale;

            Color c0 = stingColor; c0.a = p.startAlpha;
            sr.color = c0;

            puffs.Add(p);
        }
    }

    // Advances every live puff (drift + slow-down + grow + fade) and destroys the
    // expired ones. Puffs are world-space so they drift where emitted rather than
    // following the enemy.
    private void UpdatePuffs()
    {
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            Puff p = puffs[i];
            if (p.t == null) { puffs.RemoveAt(i); continue; }

            p.age += Time.deltaTime;
            float t = p.age / p.life;
            if (t >= 1f)
            {
                Destroy(p.t.gameObject);
                puffs.RemoveAt(i);
                continue;
            }

            p.vel *= 1f - Mathf.Clamp01(Time.deltaTime * 1.6f); // air drag
            p.t.position += p.vel * Time.deltaTime;

            float s = Mathf.Lerp(p.startScale, p.endScale, t);
            p.t.localScale = new Vector3(s, s, 1f);

            float k = 1f - t;
            Color c = p.tint; c.a = p.startAlpha * k * k; // ease-out fade
            p.sr.color = c;

            puffs[i] = p; // write back (Puff is a struct)
        }
    }

    // Puffs aren't parented to the enemy, so if it's destroyed mid-drift we clean
    // up any survivors here rather than leaking them.
    private void OnDestroy()
    {
        for (int i = 0; i < puffs.Count; i++)
            if (puffs[i].t != null) Destroy(puffs[i].t.gameObject);
        puffs.Clear();
    }

    // Soft radial disc (opaque core → transparent rim), 1 world unit at scale 1.
    // Shared by the aura and all puffs; built once.
    private static Sprite GetSoftGlowSprite()
    {
        if (_softGlow != null) return _softGlow;

        const int SIZE = 64;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var px = new Color[SIZE * SIZE];
        Vector2 ctr = new Vector2((SIZE - 1) * 0.5f, (SIZE - 1) * 0.5f);
        float maxR = SIZE * 0.5f;
        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), ctr) / maxR;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smoothstep falloff
                px[y * SIZE + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        _softGlow = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f), SIZE);
        _softGlow.name = "ParfumerSoftGlow";
        return _softGlow;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, cloudRadius);
        Gizmos.color = new Color(0.5f, 1f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, stoppingDistance);
    }
#endif
}



