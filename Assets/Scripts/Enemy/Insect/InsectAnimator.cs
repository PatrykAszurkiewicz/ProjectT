using UnityEngine;

// InsectAnimator
//   Dive        (FromAboveToUnder)  — the insect sinks from the surface into the
//                                     ground; played once as it digs in.
//   Underground (MovingUnderground) — the travelling mound + fins while it
//                                     tunnels; played on loop.
//   Emerge      (FromUnderToAbove)  — it erupts back out to strike; played once
//                                     during the leap arc.
//   Attack      (Attacking)         — the surfaced strike; one clip per real
//                                     attack swing (or held on frame 0 between).

[DisallowMultipleComponent]
public class InsectAnimator : MonoBehaviour, ISpritePrewarm
{
    // Auto-prewarm hook (GameOrchestrator finds this on the prefab). Warms the four
    // burrow folders through the shared cache so the first insect doesn't stall.
    public void PrewarmSpriteFolders()
    {
        // Direct references need no warm — they came in with the scene.
        if (HasDirectFrames) return;

        Load(diveFolder, null);
        Load(undergroundFolder, null);
        Load(emergeFolder, null);
        Load(attackFolder, null);
    }

    /// True once the four clips are wired as direct references instead of Resources paths.
    private bool HasDirectFrames =>
        (diveFrames != null && diveFrames.Length > 0) ||
        (undergroundFrames != null && undergroundFrames.Length > 0) ||
        (emergeFrames != null && emergeFrames.Length > 0);

    public enum Clip { None, Dive, Underground, Emerge, Attack }

    // ── Direct sprite references (preferred) ──────────────────────────────────
    // These four burrow clips live on the COMPONENT, not on EnemyData, so the
    // EnemyData migration tool cannot see them. Without direct references here,
    // moving Insect/ and EliteInsect/ out of Resources silently breaks the
    // dive / underground / emerge / attack animations on both insect variants —
    // eight folders in total.
    [Header("Sprite frames (direct references — preferred)")]
    [Tooltip("When any of these is populated the Resources folder paths below are ignored.")]
    [SerializeField] private Sprite[] diveFrames;
    [SerializeField] private Sprite[] undergroundFrames;
    [SerializeField] private Sprite[] emergeFrames;
    [SerializeField] private Sprite[] attackFrames;

    [Header("LEGACY Resource folders (fallback only)")]
    [SerializeField] private string diveFolder = "Sprites/EnemySprites/Insect/FromAboveToUnder";
    [SerializeField] private string undergroundFolder = "Sprites/EnemySprites/Insect/MovingUnderground";
    [SerializeField] private string emergeFolder = "Sprites/EnemySprites/Insect/FromUnderToAbove";
    [SerializeField] private string attackFolder = "Sprites/EnemySprites/Insect/Attacking";

    [Header("Attack clip window")]
    [Tooltip("First attack frame to play (0-based index into the Attacking folder, sorted by name).")]
    [SerializeField] private int attackStartFrame = 0;
    [Tooltip("How many attack frames to play from attackStartFrame. 0 (or less) = play through to the end. " +
             "Use this to trim duplicate/held tail frames without re-exporting — e.g. 12 plays the first 12 only.")]
    [SerializeField] private int attackFrameCount = 0;

    [Header("Attack two-speed profile (optional)")]
    [Tooltip("Play the first N attack frames at the base speed, then the remaining frames sped up. " +
             "0 = uniform speed across the whole clip.")]
    [SerializeField] private int attackNormalFrames = 0;
    [Tooltip("Base seconds-per-frame for the leading (normal-speed) attack frames.")]
    [SerializeField] private float attackBaseSecPerFrame = 0.08f;
    [Tooltip("How many times faster the frames AFTER attackNormalFrames play. 1 = same speed. " +
             "e.g. 6 = the biting tail plays 6x faster than the wind-up.")]
    [SerializeField] private float attackTailSpeedup = 1f;

    [Header("Loop speeds (seconds per frame)")]
    [Tooltip("Playback speed of the underground tunnelling loop.")]
    [SerializeField] private float undergroundSecPerFrame = 0.06f;

    [Tooltip("Fallback playback speed of the attack clip, used only when the attack " +
             "isn't being time-fitted to a real swing (e.g. a single dummy frame).")]
    [SerializeField] private float attackSecPerFrame = 0.08f;

    [Tooltip("Keep the sprite upright (cancel the velocity-based tilt that " +
             "EnemyAnimationController applies) while this component owns the visual. " +
             "The burrow / emerge / attack art is drawn flat, so a tilt looks wrong.")]
    [SerializeField] private bool forceUpright = true;

    [Header("Buried-tail ground overlay")]
    [Tooltip("Draw a patch of dug-up ground in front of the insect's base during the " +
             "dive, emerge and attack so its lower body / tail reads as still buried. " +
             "Uses a frame of the MovingUnderground mound art.")]
    [SerializeField] private bool showMoundOverlay = true;

    [Tooltip("Which MovingUnderground frame to use as the static ground patch.")]
    [SerializeField] private int moundFrameIndex = 0;

    [Tooltip("Sorting order offset of the ground patch relative to the insect sprite. " +
             "Positive (in front) is what buries the tail; the patch is only opaque over " +
             "its lower mound area, so the reared body still shows above it.")]
    [SerializeField] private int moundSortingOffset = 1;

    [Tooltip("Position nudge (in the insect's scale) for the ground patch, if it doesn't " +
             "sit exactly over the base for your art (e.g. a small negative Y to drop it down).")]
    [SerializeField] private Vector2 moundLocalOffset = Vector2.zero;

    private SpriteRenderer moundOverlay;   // ground patch shown during dive / emerge / attack
    private bool moundVisible;
    private bool moundFollow;              // true = track the insect; false = stay pinned to the ground
    private Vector3 moundPinPos;           // world spot the patch stays on (dive hole / emergence hole)
    private Vector2 moundAimDir = Vector2.right;  // direction the soil points when pinned (dive/emerge)

    // Terrain Y-sort constants, resolved from GrassCartoonOverlay so the mound
    // patch competes with map obstacles on the SAME footing they use. The soil
    // patch is a flat ground decal, so it must sort by where it sits on the
    // ground (bottom-edge convention, like obstacles) rather than by the body's
    // forward-biased order — otherwise it rides over obstacles it overlaps.
    private int _terrainBase = 1000;
    private float _terrainPrec = 10f;
    // Ground reference offset matching the obstacle convention (ObstacleGenerator
    // uses sortYOffset -0.5; blocking layout obstacles sort from their bottom edge).
    private const float MoundGroundYOffset = -0.5f;

    private SpriteRenderer sr;
    private EnemyAnimationController enemyAnim;

    private Sprite[] dive, underground, emerge, attack;
    private Sprite[] attackView;   // attack windowed to the configured start/count; everything reads this

    private Clip clip = Clip.None;
    private Sprite[] active;      // frames of the current clip
    private float clipStartTime;  // Time.time the current clip began
    private float secPerFrame;    // playback speed of the current clip
    private bool looping;         // loop, vs play-once-and-hold-last-frame
    private int heldFrame = -1;   // >=0 = hold this exact frame (externally driven, e.g. attack synced to the real swing)
    private float[] attackFrameEnds;   // cumulative end-time of each attack frame when a two-speed profile is active
    private bool usingAttackProfile;   // true while a profiled attack is playing (variable per-frame timing)
    private bool attackProfileLoop;    // true = wrap the profile continuously (never hold the last frame)
    private float attackProfileTotal;  // total duration of one profiled pass

    // Orientation. This component owns BOTH rotation and horizontal mirroring for
    // every insect state so it never fights EnemyAnimationController / SmoothSpriteFlip
    // (writing flipX directly alongside SmoothSpriteFlip desyncs its internal facing
    // state, which previously left the attack facing the wrong way). The art is drawn
    // facing +X (right).
    //   Aim     — rotate the sprite so its right points along orientDir, mirroring
    //             when heading left so it never goes upside-down (tunnelling, attack).
    //   Upright — no rotation; just mirror to face orientDir left/right (dive, emerge,
    //             idle — the reared / sinking art reads best vertical).
    //   Keep    — don't manage orientation (default before first set; also on death,
    //             so the death animation is handled by the shared controller).
    private enum OrientMode { Keep, Upright, Aim }
    private OrientMode orientMode = OrientMode.Keep;
    private Vector2 orientDir = Vector2.right;
    private bool faceLeft;            // last committed facing (hysteresis vs. near-vertical jitter)
    private bool handedBackOnDeath;  // guard so the death reset runs once
    private float baseScaleX = 1f;   // captured at Awake; neutralizes any scale-based external flip

    /// True when at least one burrow clip loaded, i.e. the sprite-driven path is
    /// usable. When false, InsectController keeps its legacy fade/mound visuals.
    public bool Ready { get; private set; }

    /// True when the Attack folder has at least one frame.
    public bool HasAttack => attackView != null && attackView.Length > 0;

    /// Number of frames loaded for the attack clip (0 if none). Used to align the
    /// combat timeline length with the authored animation.
    public int AttackFrameCount => attackView != null ? attackView.Length : 0;

    public Clip Current => clip;

    /// True when currently looping the given clip (used to avoid restarting a loop
    /// that's already running, while still self-healing if the clip drifted).
    public bool IsLooping(Clip c) => looping && clip == c;

    /// True when ANY clip is currently looping. Used so the attack phase can keep a
    /// loop alive (the real attack clip, or a stand-in when the attack folder is
    /// missing) without knowing which clip it settled on.
    public bool IsLoopingAnything => looping && clip != Clip.None && active != null && active.Length > 0;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        enemyAnim = GetComponent<EnemyAnimationController>();
        baseScaleX = Mathf.Abs(transform.localScale.x);
        if (baseScaleX < 0.0001f) baseScaleX = 1f;

        dive = Load(diveFolder, diveFrames);
        underground = Load(undergroundFolder, undergroundFrames);
        emerge = Load(emergeFolder, emergeFrames);
        attack = Load(attackFolder, attackFrames);
        RebuildAttackWindow();

        Ready = dive.Length > 0 || underground.Length > 0 || emerge.Length > 0;

        ResolveTerrainSort();
        CreateMoundOverlay();
    }

    // Pull the terrain Y-sort base/precision from the live grass overlay so the
    // mound patch lands in the exact same numeric band as grass and obstacles.
    // Falls back to the enemy's own YSortEntity, then to the shared defaults.
    private void ResolveTerrainSort()
    {
        var grass = FindFirstObjectByType<GrassCartoonOverlay>();
        if (grass != null)
        {
            _terrainBase = grass.sortOrderBase;
            _terrainPrec = grass.sortPrecision;
            return;
        }
        var ys = GetComponent<YSortEntity>();
        if (ys != null)
        {
            _terrainBase = ys.sortOrderBase;
            _terrainPrec = ys.sortPrecision;
        }
    }

    /// Re-point this animator at a different set of Resource folders at runtime and
    /// reload every clip. Any null/empty argument keeps the current folder for that
    /// slot. Safe to call before or after Awake().
    ///
    /// This exists so a variant (e.g. the EliteInsect) can reuse ALL of the Insect's
    /// burrow logic with different art, without a second animator class. The normal
    /// Insect never calls this, so its behaviour is completely unchanged.
    public void Configure(string divePath, string undergroundPath, string emergePath, string attackPath)
        => Configure(divePath, undergroundPath, emergePath, attackPath, null, null, null, null);

    /// Overload taking DIRECT sprite arrays. EliteInsectVisuals uses this to hand over
    /// its own art without any Resources path, which is what lets EliteInsect/ leave
    /// the Resources folder. Passing null for an array falls back to its path.
    public void Configure(string divePath, string undergroundPath, string emergePath, string attackPath,
                          Sprite[] diveArt, Sprite[] undergroundArt, Sprite[] emergeArt, Sprite[] attackArt)
    {
        if (!string.IsNullOrEmpty(divePath)) diveFolder = divePath;
        if (!string.IsNullOrEmpty(undergroundPath)) undergroundFolder = undergroundPath;
        if (!string.IsNullOrEmpty(emergePath)) emergeFolder = emergePath;
        if (!string.IsNullOrEmpty(attackPath)) attackFolder = attackPath;

        if (diveArt != null && diveArt.Length > 0) diveFrames = diveArt;
        if (undergroundArt != null && undergroundArt.Length > 0) undergroundFrames = undergroundArt;
        if (emergeArt != null && emergeArt.Length > 0) emergeFrames = emergeArt;
        if (attackArt != null && attackArt.Length > 0) attackFrames = attackArt;

        dive = Load(diveFolder, diveFrames);
        underground = Load(undergroundFolder, undergroundFrames);
        emerge = Load(emergeFolder, emergeFrames);
        attack = Load(attackFolder, attackFrames);
        RebuildAttackWindow();
        Ready = dive.Length > 0 || underground.Length > 0 || emerge.Length > 0;

        // Rebuild the ground-patch overlay from the (possibly new) underground art.
        // Both the old and new overlays start disabled, so the deferred Destroy of
        // the old one can never cause a visible double-mound.
        if (moundOverlay != null)
        {
            Destroy(moundOverlay.gameObject);
            moundOverlay = null;
        }
        CreateMoundOverlay();

        // If a clip is already mid-play, refresh its frame array so it doesn't keep
        // pointing at the previous folder's sprites.
        if (clip != Clip.None) active = Frames(clip);
    }

    // Builds the "dug-up ground" patch as a child sprite drawn from the
    // MovingUnderground mound art. Shown during dive / emerge / attack, in front of
    // the insect so its lower body / tail reads as still buried. Positioned and kept
    // flat every frame in LateUpdate (either following the insect or pinned to a
    // fixed ground spot).
    private void CreateMoundOverlay()
    {
        if (underground == null || underground.Length == 0) return;

        int idx = Mathf.Clamp(moundFrameIndex, 0, underground.Length - 1);

        var go = new GameObject("BurrowMoundOverlay");
        go.transform.SetParent(transform, false);

        moundOverlay = go.AddComponent<SpriteRenderer>();
        moundOverlay.sprite = underground[idx];
        SyncMoundSorting();
        moundOverlay.enabled = false;
    }

    private void SyncMoundSorting()
    {
        if (moundOverlay == null || sr == null) return;
        moundOverlay.sortingLayerID = sr.sortingLayerID;

        // Sort the soil patch as a GROUND DECAL on the obstacle convention
        // (bottom-edge, y - 0.5), NOT relative to the body's forward-biased
        // order. This is what lets a map obstacle draw on top of the patch when
        // they overlap. moundSortingOffset is kept as the burial bias so the
        // patch still reads over the insect's lower body / tail where no
        // obstacle intervenes.
        float groundY = moundOverlay.transform.position.y + MoundGroundYOffset;
        int order = _terrainBase + Mathf.RoundToInt(-groundY * _terrainPrec) + moundSortingOffset;
        moundOverlay.sortingOrder = order;
    }

    /// Show the ground patch tracking the insect (used while attacking, where the
    /// insect is planted on the surface).
    public void ShowMoundFollowing()
    {
        moundVisible = true;
        moundFollow = true;
        SyncMoundSorting();
        if (moundOverlay != null) moundOverlay.enabled = showMoundOverlay;
    }

    /// Show the ground patch pinned to a fixed world spot (used for the dive hole and
    /// the emergence hole, where the insect sinks / leaps and the ground stays put).
    /// `aimDir` angles the (directional) soil along the attack / travel vector.
    public void ShowMoundPinned(Vector3 worldGroundPos, Vector2 aimDir)
    {
        moundVisible = true;
        moundFollow = false;
        moundPinPos = worldGroundPos;
        if (aimDir.sqrMagnitude > 0.0001f) moundAimDir = aimDir.normalized;
        SyncMoundSorting();
        if (moundOverlay != null) moundOverlay.enabled = showMoundOverlay;
    }

    /// Hide the ground patch (idle, and while tunnelling — the MovingUnderground clip
    /// already draws its own mound).
    public void HideMound()
    {
        moundVisible = false;
        if (moundOverlay != null) moundOverlay.enabled = false;
    }

    // Rotation that points the right-facing art along `dir`, mirroring when heading
    // left (same convention as the body's aim), so the soil lines up with the body.
    private static Quaternion AimRotation(Vector2 dir, out bool faceLeft)
    {
        if (dir.sqrMagnitude < 0.0001f) { faceLeft = false; return Quaternion.identity; }
        float theta = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        faceLeft = dir.x < 0f;
        float rotZ = faceLeft ? theta + 180f : theta;
        return Quaternion.Euler(0f, 0f, rotZ);
    }

    // Places the soil patch and — crucially — angles it along the attack/travel
    // vector so it's never left flat/"sideways" under an angled body. While the
    // insect is planted (attack) it copies the body's exact orientation; while pinned
    // to a hole (dive/emerge, where the body may be upright or leaping) it aims itself
    // toward the captured target direction.
    private void PositionMound()
    {
        if (moundOverlay == null || !moundOverlay.enabled) return;

        Vector3 anchor;
        Quaternion rot;
        if (moundFollow)
        {
            anchor = transform.position;
            rot = transform.rotation;                         // copy the aimed body exactly
            if (sr != null) moundOverlay.flipX = sr.flipX;
        }
        else
        {
            anchor = moundPinPos;
            rot = AimRotation(moundAimDir, out bool left);    // aim the soil at the target
            moundOverlay.flipX = left;
        }

        Vector3 scale = transform.lossyScale;
        Vector3 localOff = new Vector3(moundLocalOffset.x * scale.x, moundLocalOffset.y * scale.y, 0f);

        // Snap directly to the target pose. The patch is kept hidden through the
        // leap and only reappears here at the attack position (masked by SoilBurst),
        // so there is no stale position to ease from — easing would just reintroduce
        // a visible slide toward the attack spot.
        moundOverlay.transform.position = anchor + rot * localOff;
        moundOverlay.transform.rotation = rot;

        // Sort from the patch's FINAL ground position, every frame, so it tracks
        // the terrain band correctly (and recovers after any underground pinning).
        SyncMoundSorting();
    }

    /// Direct references win; the Resources path is the fallback for unmigrated prefabs.
    private static Sprite[] Load(string folder, Sprite[] direct)
    {
        if (direct != null && direct.Length > 0) return direct;
        return LoadFromResources(folder);
    }

    /// Legacy Resources fallback, kept only for a prefab that has not been given direct
    /// burrow arrays yet.
    ///
    /// This used to route through EnemyAnimationController.LoadFolderCached for run-wide
    /// caching, but that whole folder-loading system has been deleted — enemy art is
    /// referenced directly now, so nothing needs caching. The uncached load below is
    /// acceptable precisely because it should never run: every Insect and EliteInsect
    /// prefab has its arrays assigned, and Load() returns those before reaching here.
    private static Sprite[] LoadFromResources(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return System.Array.Empty<Sprite>();

        Sprite[] s = Resources.LoadAll<Sprite>(folder);
        if (s == null || s.Length == 0)
        {
            Debug.LogWarning($"[InsectAnimator] No sprites found in Resources/{folder}, and no " +
                             "direct frame array was assigned. Assign the burrow clips on the " +
                             "InsectController / EliteInsectVisuals component.");
            return System.Array.Empty<Sprite>();
        }

        System.Array.Sort(s, (a, b) => string.CompareOrdinal(a.name, b.name));
        return s;
    }

    // Slices `attack` down to the configured window starting at attackStartFrame.
    // attackFrameCount <= 0 means "through to the end". This is what lets you trim the
    // duplicate/held tail frames (e.g. play the first 12 of 25) without re-exporting.
    private void RebuildAttackWindow()
    {
        if (attack == null || attack.Length == 0) { attackView = attack; return; }

        int start = Mathf.Clamp(attackStartFrame, 0, attack.Length - 1);
        int max = attack.Length - start;
        int count = (attackFrameCount <= 0) ? max : Mathf.Clamp(attackFrameCount, 1, max);

        if (start == 0 && count == attack.Length) { attackView = attack; return; }  // whole clip, no copy

        var w = new Sprite[count];
        System.Array.Copy(attack, start, w, 0, count);
        attackView = w;
    }

    /// Trim the attack clip to a frame range at runtime (0-based, count<=0 = to the end).
    /// e.g. SetAttackWindow(0, 12) plays only the first 12 attack frames. Handy for
    /// dropping duplicate tail frames without touching the exported sprites.
    public void SetAttackWindow(int start, int count)
    {
        attackStartFrame = start;
        attackFrameCount = count;
        RebuildAttackWindow();
    }

    /// True when a two-speed attack profile is configured and there are tail frames
    /// to speed up. When false, the attack just time-fits to the swing uniformly.
    public bool HasAttackProfile =>
        attackNormalFrames > 0 && attackTailSpeedup > 1f &&
        attackView != null && attackView.Length > attackNormalFrames;

    /// Configure the two-speed profile at runtime: the first `normalFrames` play at
    /// `baseSecPerFrame`, the rest play `tailSpeedup`x faster.
    public void SetAttackSpeedProfile(int normalFrames, float baseSecPerFrame, float tailSpeedup)
    {
        attackNormalFrames = normalFrames;
        if (baseSecPerFrame > 0f) attackBaseSecPerFrame = baseSecPerFrame;
        attackTailSpeedup = tailSpeedup;
    }

    /// True while the profiled attack is running as a continuous loop.
    public bool IsLoopingAttackProfile => usingAttackProfile && attackProfileLoop && clip == Clip.Attack;

    /// Play the attack clip ONCE using the two-speed profile (leading frames at base
    /// speed, tail frames sped up), then hold the last frame. Returns the total clip
    /// duration so the caller can time when to return to the ready pose.
    public float PlayAttackProfiled() => StartAttackProfile(false);

    /// Play the two-speed attack profile as a CONTINUOUS LOOP (wind-up → fast bite →
    /// wind-up → …). Used while the insect is up and attacking so it never rests on a
    /// frozen frame between bites. Returns the duration of one pass.
    public float PlayAttackProfiledLoop() => StartAttackProfile(true);

    private float StartAttackProfile(bool loop)
    {
        Sprite[] frames = Frames(Clip.Attack);
        if (frames.Length == 0) return 0f;

        clip = Clip.Attack;
        active = frames;
        looping = false;      // timing is driven by the profile, not the uniform stepper
        heldFrame = -1;

        int n = frames.Length;
        int slow = Mathf.Clamp(attackNormalFrames, 0, n);
        float baseSPF = Mathf.Max(0.0001f, attackBaseSecPerFrame);
        float fastSPF = baseSPF / Mathf.Max(1f, attackTailSpeedup);

        attackFrameEnds = new float[n];
        float t = 0f;
        for (int i = 0; i < n; i++)
        {
            t += (i < slow) ? baseSPF : fastSPF;   // leading frames slow, tail frames fast
            attackFrameEnds[i] = t;
        }

        attackProfileTotal = Mathf.Max(0.0001f, t);
        attackProfileLoop = loop;
        usingAttackProfile = true;
        clipStartTime = Time.time;
        Apply(0);
        return attackProfileTotal;
    }

    private Sprite[] Frames(Clip c)
    {
        switch (c)
        {
            case Clip.Dive: return dive;
            case Clip.Underground: return underground;
            case Clip.Emerge: return emerge;
            case Clip.Attack: return attackView;
            default: return System.Array.Empty<Sprite>();
        }
    }

    public bool Has(Clip c) => Frames(c).Length > 0;

    /// Play a clip exactly ONCE, stretched to fill `duration` seconds, then hold
    /// its last frame. Used to lock Dive to diveDuration and Emerge to jumpDuration
    /// so the visuals always match the movement timing, whatever the frame count.
    public void PlayOnce(Clip c, float duration)
    {
        Sprite[] frames = Frames(c);
        if (frames.Length == 0) return;

        clip = c;
        active = frames;
        looping = false;
        heldFrame = -1;
        usingAttackProfile = false;
        secPerFrame = Mathf.Max(0.0001f, duration) / frames.Length;
        clipStartTime = Time.time;
        Apply(0);
    }

    /// Play a clip on LOOP. Pass a positive `secPerFrame` to override the clip's
    /// configured speed (e.g. loop the attack at the authored AttackAnimSpeed so one
    /// loop equals one swing); otherwise the per-clip default is used.
    public void PlayLoop(Clip c, float secPerFrame = -1f)
    {
        Sprite[] frames = Frames(c);
        if (frames.Length == 0) return;

        clip = c;
        active = frames;
        looping = true;
        heldFrame = -1;
        usingAttackProfile = false;
        this.secPerFrame = secPerFrame > 0f
            ? secPerFrame
            : (c == Clip.Underground) ? undergroundSecPerFrame
            : (c == Clip.Attack) ? attackSecPerFrame
            : 0.08f;
        clipStartTime = Time.time;
        Apply(0);
    }

    /// Hold a SPECIFIC frame of a clip (a static pose driven externally). Used to
    /// slave the attack clip to the real combat swing frame-by-frame, so the strike
    /// plays exactly once per swing and rests on frame 0 between swings — instead of
    /// a free-running loop that dwells on the clip's tail (which reads as a freeze).
    public void ShowFrame(Clip c, int index)
    {
        Sprite[] frames = Frames(c);
        if (frames.Length == 0) return;

        clip = c;
        active = frames;
        looping = false;
        usingAttackProfile = false;
        heldFrame = Mathf.Clamp(index, 0, frames.Length - 1);
        Apply(heldFrame);
    }

    /// Freeze on frame 0 of a clip (a static pose — e.g. surfaced-and-ready).
    public void ShowFirstFrame(Clip c)
    {
        Sprite[] frames = Frames(c);
        if (frames.Length == 0) return;

        clip = c;
        active = frames;
        looping = false;
        usingAttackProfile = false;
        heldFrame = 0;
        clipStartTime = Time.time;
        Apply(0);
    }

    /// Relinquish the SpriteRenderer (lets EnemyAnimationController drive again).
    public void Stop()
    {
        clip = Clip.None;
        active = null;
        heldFrame = -1;
        usingAttackProfile = false;
    }

    /// Rotate the (right-facing) art to point along `dir`, mirroring when heading
    /// left — used while tunnelling and while attacking so the insect points at
    /// what it's moving toward / biting. Ignored for a zero-length direction.
    public void SetAim(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        orientDir = dir.normalized;
        orientMode = OrientMode.Aim;
    }

    /// Keep the art upright but mirror it to face `dir` left/right — used for the
    /// dive, emerge and idle poses, which read best vertical. Ignored for a
    /// zero-length direction.
    public void SetFacing(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) { orientMode = OrientMode.Upright; return; }
        orientDir = dir.normalized;
        orientMode = OrientMode.Upright;
    }

    private void LateUpdate()
    {
        // Once the enemy is dying, hand orientation back to a neutral pose (once)
        // and let the death animation (main sheet, via the shared controller) own
        // the sprite — never fight it.
        if (enemyAnim != null && enemyAnim.IsDying)
        {
            if (!handedBackOnDeath)
            {
                handedBackOnDeath = true;
                orientMode = OrientMode.Keep;
                transform.rotation = Quaternion.identity;
                if (sr != null) sr.flipX = false;
                Vector3 sc = transform.localScale; sc.x = baseScaleX; transform.localScale = sc;
                moundVisible = false;
                if (moundOverlay != null) moundOverlay.enabled = false;
            }
            return;
        }

        if (clip == Clip.None || active == null || active.Length == 0 || sr == null)
            return;

        // Externally-driven hold (attack synced to the real swing, or a static pose):
        // show exactly the requested frame and skip time-based advancement.
        if (heldFrame >= 0)
        {
            Apply(Mathf.Clamp(heldFrame, 0, active.Length - 1));
            ApplyOrientation();
            PositionMound();
            return;
        }

        // Two-speed attack profile: leading frames slow, tail frames fast. Frame
        // boundaries are precomputed in attackFrameEnds; hold the last frame after.
        if (usingAttackProfile && clip == Clip.Attack && attackFrameEnds != null && attackFrameEnds.Length == active.Length)
        {
            float e = Time.time - clipStartTime;
            if (attackProfileLoop) e %= attackProfileTotal;   // continuous wind-up → bite → wind-up …
            int idx = active.Length - 1;
            for (int k = 0; k < attackFrameEnds.Length; k++)
            {
                if (e < attackFrameEnds[k]) { idx = k; break; }
            }
            Apply(idx);
            ApplyOrientation();
            PositionMound();
            return;
        }

        float elapsed = Time.time - clipStartTime;
        int i;
        if (looping)
            i = Mathf.FloorToInt(elapsed / secPerFrame) % active.Length;
        else
            i = Mathf.Min(active.Length - 1, Mathf.FloorToInt(elapsed / secPerFrame));

        Apply(i);
        ApplyOrientation();
        PositionMound();
    }

    // Owns rotation + horizontal mirror for the current insect state. Mirroring is
    // done via flipX with localScale.x pinned to its positive base, so a scale-based
    // external flip (SmoothSpriteFlip) can neither double-mirror nor desync us. A
    // small deadzone on orientDir.x keeps the facing from flickering when aiming
    // near-vertically.
    private void ApplyOrientation()
    {
        if (orientMode == OrientMode.Keep)
        {
            if (forceUpright) transform.rotation = Quaternion.identity;
            return;
        }

        if (Mathf.Abs(orientDir.x) > 0.05f)
            faceLeft = orientDir.x < 0f;

        Vector3 s = transform.localScale;
        if (!Mathf.Approximately(s.x, baseScaleX))
        {
            s.x = baseScaleX;
            transform.localScale = s;
        }
        if (sr != null) sr.flipX = faceLeft;

        if (orientMode == OrientMode.Upright)
        {
            transform.rotation = Quaternion.identity;
        }
        else // Aim: point +X (mirrored to -X when facing left) along orientDir
        {
            float theta = Mathf.Atan2(orientDir.y, orientDir.x) * Mathf.Rad2Deg;
            float rotZ = faceLeft ? theta + 180f : theta;
            transform.rotation = Quaternion.Euler(0f, 0f, rotZ);
        }
    }

    private void Apply(int i)
    {
        if (active == null || active.Length == 0 || sr == null) return;
        i = Mathf.Clamp(i, 0, active.Length - 1);
        sr.sprite = active[i];
    }
}


