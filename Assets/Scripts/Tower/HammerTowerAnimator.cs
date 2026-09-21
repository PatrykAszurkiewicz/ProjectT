using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// Play-once attack animator for the Hammer Tower.
/// Frames live as individual PNGs (00.png .. 68.png) under a Resources folder.
///   frame 0             = idle (shown whenever not attacking)
///   frames 1 .. 68      = the attack swing
///   frame <impactFrame> = the moment the hammer hits the ground
///                           (damage + sound fire here, via the onImpact callback)
/// Frames are loaded ONCE per session into a shared static cache, and warmed
/// asynchronously at startup so placing the tower never blocks the main thread.
/// Set the Tower's usePrefabVisuals = true so the base class leaves the
/// SpriteRenderer alone and lets this component drive it.

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class HammerTowerAnimator : MonoBehaviour
{
    public const string DefaultFramesFolder = "Sprites/Buildings/Towers/HammerTower";

    [Header("Frames (Resources folder holding 00.png .. 68.png)")]
    //[Tooltip("Path passed to Resources.Load. No leading 'Assets/Resources/' and no extension.")]
    // ── Direct sprite references (preferred) ─────────────────────────────────
    // Assign on the Hammer Tower prefab; framesResourceFolder is then ignored.
    [Tooltip("Hammer animation frames in order. When set, Frames Resource Folder is ignored.")]
    public Sprite[] frameSprites;

    public bool HasDirectFrames => frameSprites != null && frameSprites.Length > 0;

    [Tooltip("DEPRECATED fallback — used only when Frame Sprites above is empty.")]
    public string framesResourceFolder = DefaultFramesFolder;

    [Header("Frame layout")]
    public int idleFrame = 0;
    public int attackStartFrame = 1;
    [Tooltip("Frame on which the hammer strikes the ground. Damage + sound fire here.")]
    public int impactFrame = 40;
    public int attackEndFrame = 68;

    [Header("Playback")]
    [Tooltip("Frames per second for the swing. Your art is authored for 12.")]
    public float framesPerSecond = 12f;

    [Header("Interrupt (tower dies mid-swing)")]
    [Tooltip("Seconds to crossfade from the interrupted swing frame back to the idle/dead " +
             "frame when the tower dies mid-attack. 0 = snap instantly.")]
    public float interruptFadeDuration = 0.2f;

    [Header("Rendering")]
    [Tooltip("Uniform scale applied to the tower transform. <= 0 = don't touch scale.")]
    public float spriteScale = 0.25f;
    public int sortingOrder = 20;

    [Header("Debug")]
    public bool verboseLogging = false;

    // Shared cache: load the frames once per session, not once per placement 
    private static readonly Dictionary<string, Sprite[]> FrameCache = new Dictionary<string, Sprite[]>();

    // FIX: the cache is static and held Sprites loaded from Resources. With "Enter Play
    // Mode without domain reload" the dictionary survived into the next session while
    // the Sprites it held did not, so LoadFrames' fast path handed out destroyed sprites
    // and the tower rendered blank. (Tower.cs already resets its own SpriteFrameCache
    // this way; this one was missed.)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => FrameCache.Clear();

    private SpriteRenderer sr;
    private Sprite[] frames;
    private bool isAttacking;
    private int currentFrame;
    private float frameTimer;
    private bool impactFired;
    private Action onImpact;
    private Action onComplete;

    // The owning tower (same GameObject). Polled while swinging so the attack is cut
    // short the moment the tower stops being operational — killed by enemy damage,
    // drained below the dead threshold, or flagged destroyed. Tower.Update bails out
    // early once the tower is dead, but this component keeps its own Update, which is
    // why the swing used to play to the end on a dead tower.
    private Tower tower;

    // Interrupt crossfade: a temporary child renderer holding the frame the swing was
    // cut on, fading out over the idle frame on the main renderer. Using a separate
    // renderer means we never fight Tower over sr.color (death dimming, damage flash).
    private SpriteRenderer fadeOverlay;
    private float fadeTimer;
    private bool isFading;

    public bool IsAttacking => isAttacking;
    public bool IsReady => frames != null && frames.Length > 0;

    private float SecondsPerFrame => 1f / Mathf.Max(1f, framesPerSecond);

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;

        tower = GetComponent<Tower>();

        if (spriteScale > 0f)
            transform.localScale = Vector3.one * spriteScale;

        LoadFrames();
        ShowIdle();
    }

    void LoadFrames()
    {
        // PREFERRED: direct references. No Resources, no cache, no async warm needed —
        // the prefab reference chain already brought these in with the scene.
        if (HasDirectFrames)
        {
            frames = frameSprites;
            ClampFrameRange();
            return;
        }

        Debug.LogWarning($"[HammerTowerAnimator] '{name}' still loads frames from " +
                         $"Resources/{framesResourceFolder}. Assign Frame Sprites on the prefab.");

        // Fast path: frames already in the shared cache (warmed at startup or by a
        // previous placement). Validate the first entry hasn't been unloaded.
        if (FrameCache.TryGetValue(framesResourceFolder, out var cached) &&
            cached != null && cached.Length > 0 && cached[0] != null)
        {
            frames = cached;
            ClampFrameRange();
            if (verboseLogging) Debug.Log($"HammerTowerAnimator: used cached frames for {framesResourceFolder}.");
            return;
        }

        // Cold path: synchronous load (only happens if the async warm-up hasn't
        // finished yet). Still cached afterwards so it only ever costs once.
        var loaded = Resources.LoadAll<Sprite>(framesResourceFolder);
        if (loaded == null || loaded.Length == 0)
        {
            Debug.LogError($"HammerTowerAnimator: no sprites found at Resources/{framesResourceFolder}.");
            frames = Array.Empty<Sprite>();
            return;
        }

        frames = loaded.OrderBy(s => ParseLeadingInt(s.name, int.MaxValue)).ToArray();
        FrameCache[framesResourceFolder] = frames;
        ClampFrameRange();

        if (verboseLogging)
            Debug.Log($"HammerTowerAnimator: cold-loaded {frames.Length} frames from Resources/{framesResourceFolder}.");
    }

    void ClampFrameRange()
    {
        if (frames == null || frames.Length == 0) return;   // nothing to clamp against
        int last = frames.Length - 1;
        if (attackEndFrame > last)
        {
            Debug.LogWarning($"HammerTowerAnimator: attackEndFrame ({attackEndFrame}) > frame count ({frames.Length}). Clamping.");
            attackEndFrame = last;
        }
        impactFrame = Mathf.Clamp(impactFrame, attackStartFrame, attackEndFrame);
        idleFrame = Mathf.Clamp(idleFrame, 0, last);
    }

    static int ParseLeadingInt(string name, int fallback)
    {
        int i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        return i > 0 && int.TryParse(name.Substring(0, i), out int v) ? v : fallback;
    }

    //  startup async preload (keeps the first placement smooth) 

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoWarmCache()
    {
        // Skip the whole startup warm when the prefab uses direct references — there is
        // nothing to warm and the runner object would be pure overhead.
        // (Delete AutoWarmCache, Warmer, FrameCache and PreloadAsync entirely once every
        // Hammer Tower prefab has Frame Sprites assigned.)
        // Already warmed (e.g. a second scene load in the same session) — don't spawn
        // another runner. AfterSceneLoad fires per scene load, so this used to create a
        // fresh DontDestroyOnLoad object every time.
        if (FrameCache.ContainsKey(DefaultFramesFolder)) return;

        // Statics can't run coroutines, so spawn a throwaway hidden runner.
        var go = new GameObject("~HammerFrameWarmup") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<Warmer>().Begin(DefaultFramesFolder);
    }

    // Load a folder's frames into the shared cache without blocking. Safe to call repeatedly
    public static IEnumerator PreloadAsync(string folder)
    {
        if (string.IsNullOrEmpty(folder) || FrameCache.ContainsKey(folder)) yield break;

        // Resources.LoadAll has no async form, so pull frames one at a time (00, 01, ...)
        // to spread the cost across frames instead of hitching the main thread.
        var list = new List<Sprite>(80);
        for (int i = 0; ; i++)
        {
            // FIX: "{i:00}" produces "100" for frame 100 but the importer names files
            // "00".."99" then "100" — which happens to agree — however it produced "0"
            // for a single-digit request only if the format were dropped. The real
            // hazard is a folder whose frames are named without zero padding, where the
            // first LoadAsync misses and the whole warm silently yields nothing. Try the
            // padded name first, then the bare one, before deciding we're past the end.
            var req = Resources.LoadAsync<Sprite>($"{folder}/{i:00}");
            yield return req;

            Sprite s = req.asset as Sprite;
            if (s == null)
            {
                var alt = Resources.LoadAsync<Sprite>($"{folder}/{i}");
                yield return alt;
                s = alt.asset as Sprite;
            }

            if (s == null) break;                      // walked past the last frame
            list.Add(s);
        }
        if (list.Count > 0 && !FrameCache.ContainsKey(folder))
            FrameCache[folder] = list.ToArray();        // already in numeric order
    }

    private class Warmer : MonoBehaviour
    {
        public void Begin(string folder) => StartCoroutine(Run(folder));
        IEnumerator Run(string folder)
        {
            yield return PreloadAsync(folder);
            Destroy(gameObject);
        }
    }


    public bool PlayAttack(Action onImpact, Action onComplete = null)
    {
        if (!IsReady)
        {
            onImpact?.Invoke();
            onComplete?.Invoke();
            return false;
        }
        if (isAttacking) return false;

        EndFade();   // tower revived and swinging again before a death fade finished

        this.onImpact = onImpact;
        this.onComplete = onComplete;
        isAttacking = true;
        impactFired = false;
        frameTimer = 0f;
        currentFrame = attackStartFrame;
        ShowFrame(currentFrame);

        if (verboseLogging) Debug.Log("HammerTowerAnimator: swing started.");
        if (currentFrame == impactFrame) FireImpact();
        return true;
    }

    void Update()
    {
        if (!isAttacking) return;

        // Tower died mid-swing: stop here, before advancing another frame or reaching
        // the impact frame, and ease back to the idle frame (Tower applies the dead
        // tint/alpha to that same renderer). Runs before Tower.Update thanks to
        // DefaultExecutionOrder(-50), so energy-drain deaths are caught the same frame.
        if (tower != null && !tower.IsOperational())
        {
            InterruptAttack();
            return;
        }

        frameTimer += Time.deltaTime;

        // Guard against a pathological SecondsPerFrame / huge dt spinning forever.
        int guard = 0;
        while (frameTimer >= SecondsPerFrame && guard++ < 256)
        {
            frameTimer -= SecondsPerFrame;
            currentFrame++;

            if (currentFrame > attackEndFrame) { EndAttack(); return; }

            ShowFrame(currentFrame);
            if (currentFrame == impactFrame && !impactFired) FireImpact();
        }
    }

    void FireImpact()
    {
        impactFired = true;
        if (verboseLogging) Debug.Log($"HammerTowerAnimator: IMPACT on frame {impactFrame}.");
        var cb = onImpact;
        cb?.Invoke();
    }

    void EndAttack()
    {
        isAttacking = false;
        ShowIdle();
        var cb = onComplete;
        onComplete = null;
        onImpact = null;
        cb?.Invoke();
    }

    void ShowIdle() => ShowFrame(idleFrame);

    void ShowFrame(int index)
    {
        if (frames == null || frames.Length == 0) return;
        index = Mathf.Clamp(index, 0, frames.Length - 1);
        if (sr != null) sr.sprite = frames[index];
    }

    public void ResetToIdle()
    {
        isAttacking = false;
        onImpact = null;
        onComplete = null;
        EndFade();
        ShowIdle();
    }

    //  interrupt (tower died mid-swing) 

    /// Cut the current swing short and ease back to the idle frame.
    /// The impact callback will NOT fire (a dead tower deals no damage) and
    /// onComplete is NOT invoked, since the swing never completed.
    /// Called automatically when the owning Tower stops being operational; public
    /// so other systems (stun, sell, rewind) can use it too. No-op if not swinging.
    public void InterruptAttack()
    {
        if (!isAttacking) return;

        Sprite interruptedFrame = sr != null ? sr.sprite : null;

        isAttacking = false;
        impactFired = true;     // belt and braces: nothing can fire the impact now
        onImpact = null;
        onComplete = null;
        frameTimer = 0f;

        ShowIdle();

        if (verboseLogging)
            Debug.Log($"HammerTowerAnimator: swing interrupted on frame {currentFrame}.");

        if (interruptFadeDuration > 0f && isActiveAndEnabled &&
            sr != null && interruptedFrame != null && interruptedFrame != sr.sprite)
        {
            BeginFade(interruptedFrame);
        }
    }

    void BeginFade(Sprite fromFrame)
    {
        if (fadeOverlay == null)
        {
            // Created on demand and destroyed when the fade ends, so no extra child
            // lingers for code that looks up SpriteRenderers in children.
            var go = new GameObject("~HammerInterruptFade") { hideFlags = HideFlags.DontSave };
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);   // inherits position + scale
            fadeOverlay = go.AddComponent<SpriteRenderer>();
        }

        fadeOverlay.sprite = fromFrame;
        fadeTimer = 0f;
        isFading = true;
        SyncOverlay(1f);
    }

    // LateUpdate so the overlay picks up whatever Tower did to the main renderer this
    // frame (death dimming, damage flash, energy tint, Y-sort order) before it renders.
    void LateUpdate()
    {
        if (!isFading) return;

        if (sr == null || fadeOverlay == null) { EndFade(); return; }

        fadeTimer += Time.deltaTime;
        float t = Mathf.Clamp01(fadeTimer / interruptFadeDuration);
        if (t >= 1f) { EndFade(); return; }

        // Ease-out: the swing frame drops away quickly, then settles.
        float remaining = 1f - Mathf.SmoothStep(0f, 1f, t);
        SyncOverlay(remaining);
    }

    void SyncOverlay(float alphaFactor)
    {
        fadeOverlay.sortingLayerID = sr.sortingLayerID;
        fadeOverlay.sortingOrder = sr.sortingOrder + 1;   // draw over the idle frame
        fadeOverlay.sharedMaterial = sr.sharedMaterial;
        fadeOverlay.flipX = sr.flipX;
        fadeOverlay.flipY = sr.flipY;
        fadeOverlay.maskInteraction = sr.maskInteraction;

        Color c = sr.color;          // follow the tower's tint (incl. dead alpha)
        c.a *= alphaFactor;
        fadeOverlay.color = c;
    }

    void EndFade()
    {
        isFading = false;
        if (fadeOverlay != null)
        {
            Destroy(fadeOverlay.gameObject);
            fadeOverlay = null;
        }
    }

    void OnDisable()
    {
        // A disabled/pooled tower shouldn't come back with a half-faded ghost frame.
        EndFade();
    }

    [ContextMenu("TEST: Play Attack")]
    void TestPlayAttack()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("HammerTowerAnimator: enter Play mode first.");
            return;
        }
        PlayAttack(
            () => Debug.Log($"HammerTowerAnimator TEST: IMPACT (frame {impactFrame})"),
            () => Debug.Log("HammerTowerAnimator TEST: swing complete"));
    }

    [ContextMenu("TEST: Interrupt Attack")]
    void TestInterruptAttack()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("HammerTowerAnimator: enter Play mode first.");
            return;
        }
        InterruptAttack();
    }
}


