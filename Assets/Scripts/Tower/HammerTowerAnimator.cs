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
    [Tooltip("Path passed to Resources.Load. No leading 'Assets/Resources/' and no extension.")]
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

    [Header("Rendering")]
    [Tooltip("Uniform scale applied to the tower transform. <= 0 = don't touch scale.")]
    public float spriteScale = 0.25f;
    public int sortingOrder = 20;

    [Header("Debug")]
    public bool verboseLogging = false;

    // Shared cache: load the frames once per session, not once per placement 
    private static readonly Dictionary<string, Sprite[]> FrameCache = new Dictionary<string, Sprite[]>();

    private SpriteRenderer sr;
    private Sprite[] frames;
    private bool isAttacking;
    private int currentFrame;
    private float frameTimer;
    private bool impactFired;
    private Action onImpact;
    private Action onComplete;

    public bool IsAttacking => isAttacking;
    public bool IsReady => frames != null && frames.Length > 0;

    private float SecondsPerFrame => 1f / Mathf.Max(1f, framesPerSecond);

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;

        if (spriteScale > 0f)
            transform.localScale = Vector3.one * spriteScale;

        LoadFrames();
        ShowIdle();
    }

    void LoadFrames()
    {
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
            var req = Resources.LoadAsync<Sprite>($"{folder}/{i:00}");
            yield return req;
            if (req.asset == null) break;              // walked past the last frame
            list.Add(req.asset as Sprite);
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

        frameTimer += Time.deltaTime;
        while (frameTimer >= SecondsPerFrame)
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
        ShowIdle();
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
}

