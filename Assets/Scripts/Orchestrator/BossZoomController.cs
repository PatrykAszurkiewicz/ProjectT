using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Boss-intro cinematic. When a stage boss or the final boss appears, this pans a
// dedicated full-screen camera onto the boss and zooms in, holds for a beat, then
// pulls back out and hands control back to the normal gameplay cameras.
//   SINGLE PLAYER : the cinematic camera flies from the player's current framing
//     onto the boss and zooms in, then flies back — a classic "focus on the boss".
//   CO-OP         : the same cinematic camera is FULL-SCREEN and renders on top of
//     the split view, so for the duration of the intro the split momentarily
//     collapses into one full-screen shot of the boss, then the split returns.

[DisallowMultipleComponent]
public class BossZoomController : MonoBehaviour
{
    public static BossZoomController Instance { get; private set; }

    /// <summary>True while a boss intro is currently playing. Other systems (e.g. the
    /// split-screen divider) consult this to get out of the way.</summary>
    public static bool CinematicActive { get; private set; }

    [Header("Enable")]
    [Tooltip("Master switch. Off = boss intros never play (identical to not having this component).")]
    [SerializeField] private bool enableBossIntro = true;

    [Header("Camera fidelity (URP)")]
    [Tooltip("OPTIONAL but recommended for an exact look: a duplicate of your GameScene Main Camera " +
             "(same URP post-processing / volume settings) used as the cinematic camera. " +
             "Strip any follow / co-op / Cinemachine / AudioListener from it — this controller disables " +
             "those automatically anyway. If left null, a camera is built at runtime by copying an " +
             "active gameplay camera's settings (base settings + best-effort post-processing).")]
    [SerializeField] private GameObject cinematicCameraPrefab;

    [Tooltip("How far ABOVE the gameplay cameras' depth the cinematic camera renders. " +
             "Must be high enough that it paints over every split half.")]
    [SerializeField] private int depthAboveGameplay = 100;

    [Header("Framing")]
    [Tooltip("Orthographic size at the tightest point of the zoom. Smaller = closer to the boss. " +
             "Your gameplay size is ~5.75, so 3 is roughly a 'push to half the framing'.")]
    [SerializeField] private float zoomedOrthographicSize = 3.0f;

    [Tooltip("Nudge the framing off the boss's pivot (e.g. up a little so a tall boss sits nicely).")]
    [SerializeField] private Vector2 bossFramingOffset = Vector2.zero;

    [Tooltip("Keep the boss framed if it moves during the intro (only matters when the game isn't frozen).")]
    [SerializeField] private bool followBossDuringHold = true;

    [Header("Timing (unscaled seconds)")]
    [Tooltip("Fly-in / push-in onto the boss.")]
    [SerializeField] private float approachDuration = 0.8f;
    [Tooltip("How long to linger on the boss at full zoom.")]
    [SerializeField] private float holdDuration = 1.2f;
    [Tooltip("Pull back out and hand control back to the gameplay cameras.")]
    [SerializeField] private float returnDuration = 0.65f;
    [Tooltip("Extra subtle continued push-in across the hold, for drama. 0 = perfectly still hold.")]
    [SerializeField] private float holdExtraPushIn = 0.25f;

    [Header("Gameplay")]
    [Tooltip("Freeze the game (Time.timeScale = 0) for the duration of the intro so the boss doesn't " +
             "act while the camera focuses on it. The previous timescale is restored afterwards. " +
             "Turn OFF if you'd rather see the boss animate in while the camera moves.")]
    [SerializeField] private bool freezeGameplayDuringIntro = true;

    [Tooltip("Block player attack + aim input during the intro (uses the existing global suppression gates).")]
    [SerializeField] private bool suppressPlayerInputDuringIntro = true;

    [Header("UI")]
    [Tooltip("OPTIONAL screen-space HUD roots (health bars, energy, minimap, custom cursors …) to hide " +
             "for the duration of the intro. Screen-Space-Overlay UI draws on TOP of the cinematic camera, " +
             "so anything you want out of the shot must be listed here. The split-screen divider is hidden " +
             "automatically.")]
    [SerializeField] private GameObject[] hideDuringIntro;

    [Tooltip("Hide every player's weapon-roll hotbar for the duration of the intro. In split-screen the " +
             "second player's hotbar sits near the centre of the screen, so it would otherwise overlap the " +
             "full-screen boss shot. Uses WeaponRollUI.SetHudVisible and restores each hotbar's prior state " +
             "afterwards (so a downed player's already-hidden hotbar isn't revealed).")]
    [SerializeField] private bool hideWeaponRollUiDuringIntro = true;

    [Header("Intro sound")]
    [Tooltip("Play FMODEvents.bossZoomSound for the duration of the intro, positioned on the " +
             "boss. Fires for EVERY boss that goes through PlayIntro — stage bosses and the " +
             "final boss alike — so new bosses get it with no extra wiring.\n\n" +
             "The intro freezes gameplay (timeScale 0), which does NOT affect FMOD: it runs on " +
             "its own clock, so the sound plays normally through the freeze.")]
    [SerializeField] private bool playZoomSound = true;

    [Tooltip("Stop the zoom sound when the camera hands control back to gameplay (with " +
             "ALLOWFADEOUT, so the event's own release tail still plays out). Turn OFF if " +
             "you authored it as a riser that should ring on into the fight — then the " +
             "event's own length decides when it ends.")]
    [SerializeField] private bool stopZoomSoundWhenIntroEnds = true;

    [Header("Boss illumination (dark biomes)")]
    [Tooltip("In night-darkened biomes (Night, Corruption, Pitch Black — any biome that uses NightOverlay) " +
             "attach a light to the boss so it's actually visible during the zoom AND the ensuing fight. " +
             "Does nothing in lit biomes. The light lives on the boss and is cleaned up automatically when " +
             "the boss dies (NightLight unregisters itself on destroy/disable).")]
    [SerializeField] private bool illuminateBossInDarkBiomes = true;

    [Tooltip("Light radius around the boss (world units). Bosses are big, so this is larger than a torch; " +
             "~8 comfortably fills the zoomed-in frame.")]
    [SerializeField] private float bossLightRadius = 8f;

    [Tooltip("Brightness. 1 = fully revealed, like standing in the torch beam.")]
    [SerializeField] private float bossLightIntensity = 1f;

    [Tooltip("Local offset of the light from the boss's pivot — raise it toward a tall boss's torso.")]
    [SerializeField] private Vector2 bossLightOffset = Vector2.zero;

    [Tooltip("Warm tint blended into the darkness around the boss.")]
    [SerializeField] private Color bossLightColor = new Color(1f, 0.9f, 0.7f);

    [Range(0f, 1f)]
    [Tooltip("How strongly the boss light tints the darkness (0 = pure white light).")]
    [SerializeField] private float bossLightWarmTint = 0.25f;

    [Tooltip("Subtle flicker so the boss light feels alive (0 = perfectly steady).")]
    [SerializeField] private float bossLightFlickerSpeed = 2.5f;
    [Range(0f, 0.5f)]
    [SerializeField] private float bossLightFlickerAmount = 0.06f;

    [Tooltip("Seconds to fade the light in as the boss appears (pairs nicely with the zoom-in).")]
    [SerializeField] private float bossLightFadeIn = 0.4f;

    [Header("Events")]
    [Tooltip("Fires the moment the intro begins (hook a boss roar / music sting here).")]
    public UnityEvent onIntroStarted;
    [Tooltip("Fires once the intro is fully done and normal cameras have resumed.")]
    public UnityEvent onIntroFinished;

    public bool IsPlaying => _running != null;

    private Camera _cineCam;
    private Coroutine _running;

    // Held instance for the intro sting. One per controller, and PlayIntro already
    // refuses to start a second intro while one is running, so this can never be
    // asked to hold two sounds at once.
    private readonly SpatialLoopSfx _zoomSfx = new SpatialLoopSfx("Boss zoom");

    // Weapon-roll hotbars we hid for the current intro, so we restore exactly these
    // (and don't reveal one that was already hidden, e.g. a downed player's).
    private readonly List<WeaponRollUI> _rollsHiddenByIntro = new List<WeaponRollUI>();

    // Reset static state between Play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        CinematicActive = false;
    }

    private void Awake()
    {
        // First one wins; keep behaviour predictable if two get dropped in by mistake.
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        // Belt-and-suspenders: if we're torn down mid-intro (scene change), make sure we
        // don't strand a frozen timescale / suppressed input / hidden divider — or a
        // sting still playing into the next scene. The coroutine's finally block does
        // NOT run when a MonoBehaviour is destroyed mid-yield, so this is the only
        // thing that stops the sound on that path.
        _zoomSfx.Stop(immediate: true);

        if (CinematicActive)
        {
            if (freezeGameplayDuringIntro) Time.timeScale = 1f;
            PlayerAttack.SetAllSuppressed(false);
            PlayerAim.SetAllSuppressed(false);
            SplitScreenDivider.SetHiddenOverride(false);
            SetWeaponRollsHidden(false);
            SetHudHidden(false);
            CinematicActive = false;
        }
        if (_cineCam != null) Destroy(_cineCam.gameObject);
        if (Instance == this) Instance = null;
    }

    /// <summary>Handle a boss appearing: light it up in dark biomes (independent of the
    /// zoom) and play the focus cinematic. No-op parts: illumination only in night biomes;
    /// the zoom only if enabled and not already running.</summary>
    public void PlayIntro(GameObject boss)
    {
        if (boss == null) return;

        // Boss illumination is independent of the cinematic: even if the zoom is disabled
        // or already running, a boss in a night-darkened biome should still be made visible.
        IlluminateBoss(boss);

        if (!enableBossIntro) return;
        if (_running != null) return;
        _running = StartCoroutine(IntroRoutine(boss.transform));
    }

    // Attach a NightLight to the boss so it's visible in night-darkened biomes. NightLight
    // registers a point light with NightOverlay's shader, which reveals the area by WORLD
    // position — so it lights the boss identically for the cinematic camera and the split
    // cameras. It follows the boss if it moves and unregisters itself automatically when the
    // boss is destroyed or disabled, so there's nothing to tear down here.
    private void IlluminateBoss(GameObject boss)
    {
        if (!illuminateBossInDarkBiomes || boss == null) return;

        // Only meaningful when a night overlay is actually active (Night / Corruption /
        // Pitch Black). In lit biomes NightOverlay.Instance is null and we add nothing.
        if (NightOverlay.Instance == null) return;

        // Don't stack a second light if this boss already carries one (e.g. re-entry).
        if (boss.GetComponentInChildren<NightLight>() != null) return;

        var go = new GameObject("BossNightLight");
        go.transform.SetParent(boss.transform, false);
        go.transform.localPosition = new Vector3(bossLightOffset.x, bossLightOffset.y, 0f);

        var nl = go.AddComponent<NightLight>();
        nl.radius = bossLightRadius;
        nl.lightColor = bossLightColor;
        nl.warmTintStrength = bossLightWarmTint;
        nl.flickerSpeed = bossLightFlickerSpeed;
        nl.flickerAmount = bossLightFlickerAmount;

        // CRITICAL: the intro freezes the game (Time.timeScale = 0) and NightLight's built-in
        // fade-in is driven by SCALED Time.deltaTime — which is 0 while frozen, so the light
        // would sit at intensity 0 for the entire zoom (pitch black). Disable NightLight's own
        // fade and drive the reveal ourselves on UNSCALED time so it works during the freeze.
        nl.fadeInDuration = 0f;

        if (bossLightFadeIn > 0f)
        {
            nl.intensity = 0f;
            StartCoroutine(FadeInBossLight(nl, bossLightIntensity, bossLightFadeIn));
        }
        else
        {
            nl.intensity = bossLightIntensity;   // instant — guaranteed visible immediately
        }
    }

    // Ramp the boss light from 0 to full on unscaled time (works while the game is frozen).
    // NightLight.fadeInDuration is 0 here, so it copies nl.intensity straight to its handle.
    private IEnumerator FadeInBossLight(NightLight nl, float target, float dur)
    {
        float t = 0f;
        while (t < 1f)
        {
            if (nl == null) yield break;
            t += Time.unscaledDeltaTime / dur;
            nl.intensity = Mathf.Lerp(0f, target, Mathf.Clamp01(t));
            yield return null;
        }
        if (nl != null) nl.intensity = target;
    }

    private IEnumerator IntroRoutine(Transform boss)
    {
        CinematicActive = true;

        bool didFreeze = freezeGameplayDuringIntro;
        bool didSuppress = suppressPlayerInputDuringIntro;
        float prevTimeScale = Time.timeScale;

        try
        {
            // Get the split seam, the per-player hotbars, and any HUD out of the shot.
            SplitScreenDivider.SetHiddenOverride(true);
            SetWeaponRollsHidden(true);
            SetHudHidden(true);

            if (didSuppress)
            {
                PlayerAttack.SetAllSuppressed(true);
                PlayerAim.SetAllSuppressed(true);
            }

            Camera src = ResolveSourceCamera();
            EnsureCineCam(src);

            // Couldn't build a camera (no cameras in scene at all) — bail cleanly.
            if (_cineCam == null) yield break;

            // Start framing: mirror the current gameplay view so the fly-in reads as a
            // continuation of it. Fall back to sitting on the boss at gameplay size.
            float startSize;
            Vector3 startPos;
            if (src != null)
            {
                startPos = src.transform.position;
                startSize = src.orthographic ? src.orthographicSize : 5.75f;
            }
            else
            {
                startSize = 5.75f;
                startPos = TargetPos(boss, -10f);
            }
            float camZ = startPos.z;   // preserve the 2D camera depth throughout

            _cineCam.orthographicSize = startSize;
            _cineCam.transform.position = startPos;
            _cineCam.gameObject.SetActive(true);

            onIntroStarted?.Invoke();

            // Start on the boss, on the frame the cinematic camera goes live — the
            // sound and the cut to the boss are the same beat.
            if (playZoomSound && FMODEvents.instance != null)
                _zoomSfx.Play(FMODEvents.instance.bossZoomSound, BossPos(boss));

            if (didFreeze) Time.timeScale = 0f;

            // 1) Approach — fly onto the boss and zoom in.
            yield return Animate(approachDuration, t =>
            {
                float e = EaseOutCubic(t);
                _cineCam.transform.position = Vector3.LerpUnclamped(startPos, TargetPos(boss, camZ), e);
                _cineCam.orthographicSize = Mathf.LerpUnclamped(startSize, zoomedOrthographicSize, e);
                _zoomSfx.SetPosition(BossPos(boss));
            });

            // 2) Hold — linger, with an optional subtle continued push-in.
            float holdEndSize = Mathf.Max(0.1f, zoomedOrthographicSize - holdExtraPushIn);
            yield return Animate(holdDuration, t =>
            {
                float e = Mathf.SmoothStep(0f, 1f, t);
                if (followBossDuringHold && boss != null)
                {
                    Vector3 want = TargetPos(boss, camZ);
                    _cineCam.transform.position = Vector3.Lerp(_cineCam.transform.position, want, 0.2f);
                }
                _cineCam.orthographicSize = Mathf.Lerp(zoomedOrthographicSize, holdEndSize, e);
                _zoomSfx.SetPosition(BossPos(boss));
            });

            // 3) Return — pull back out to gameplay size near the current view, then cut.
            Vector3 fromPos = _cineCam.transform.position;
            float fromSize = _cineCam.orthographicSize;
            yield return Animate(returnDuration, t =>
            {
                float e = EaseInOutCubic(t);
                Vector3 backPos = src != null
                    ? new Vector3(src.transform.position.x, src.transform.position.y, camZ)
                    : fromPos;
                _cineCam.transform.position = Vector3.LerpUnclamped(fromPos, backPos, e);
                _cineCam.orthographicSize = Mathf.LerpUnclamped(fromSize, startSize, e);
            });
        }
        finally
        {
            // In the finally block deliberately: this runs on the bail-out paths too
            // (no camera could be built, the coroutine was stopped), and a sting left
            // playing after a failed intro would be the worst possible symptom.
            if (stopZoomSoundWhenIntroEnds) _zoomSfx.Stop(immediate: false);

            if (_cineCam != null) _cineCam.gameObject.SetActive(false);
            if (didFreeze) Time.timeScale = prevTimeScale;
            if (didSuppress)
            {
                PlayerAttack.SetAllSuppressed(false);
                PlayerAim.SetAllSuppressed(false);
            }
            SetHudHidden(false);
            SetWeaponRollsHidden(false);
            SplitScreenDivider.SetHiddenOverride(false);
            CinematicActive = false;
            _running = null;
            onIntroFinished?.Invoke();
        }
    }

    // Where the intro sound sits. The boss's own position, NOT TargetPos(): the
    // framing offset exists to make a tall boss sit nicely in shot, which is a
    // composition concern with no business moving the sound source. Falls back to
    // the origin if the boss was destroyed mid-intro.
    private Vector3 BossPos(Transform boss) => boss != null ? boss.position : Vector3.zero;

    // Frame center on the boss (+ configured offset), at the given camera Z.
    private Vector3 TargetPos(Transform boss, float z)
    {
        Vector3 c = boss != null ? boss.position : Vector3.zero;
        return new Vector3(c.x + bossFramingOffset.x, c.y + bossFramingOffset.y, z);
    }

    // Prefer a live player's camera (co-op may have no MainCamera tag), then Camera.main,
    // then any enabled camera that isn't ours.
    private Camera ResolveSourceCamera()
    {
        var reg = PlayerRegistry.Instance;
        if (reg != null)
        {
            var all = reg.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p != null && p.Camera != null && p.Camera.isActiveAndEnabled && p.Camera != _cineCam)
                    return p.Camera;
            }
        }
        if (Camera.main != null && Camera.main != _cineCam) return Camera.main;

        var cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
            if (cams[i] != null && cams[i] != _cineCam && cams[i].isActiveAndEnabled) return cams[i];
        return null;
    }

    // Build the cinematic camera once and reuse it. Kept inactive until an intro plays.
    private void EnsureCineCam(Camera src)
    {
        if (_cineCam != null) return;

        // Preferred: a user-supplied duplicate of the game camera (exact URP look).
        if (cinematicCameraPrefab != null)
        {
            var go = Instantiate(cinematicCameraPrefab);
            go.name = "BossCinematicCamera";
            _cineCam = go.GetComponent<Camera>();
            if (_cineCam == null) _cineCam = go.GetComponentInChildren<Camera>();
            if (_cineCam != null) StripCameraExtras(_cineCam);
        }

        // Fallback: build one from an active gameplay camera's settings.
        if (_cineCam == null)
        {
            var go = new GameObject("BossCinematicCamera");
            _cineCam = go.AddComponent<Camera>();
            if (src != null)
            {
                _cineCam.CopyFrom(src);           // clearFlags, background, ortho, size, culling, clip planes …
                TryCopyUrpCameraData(src, _cineCam);
            }
            else
            {
                _cineCam.orthographic = true;
                _cineCam.clearFlags = CameraClearFlags.SolidColor;
            }
        }

        if (_cineCam == null) return;

        // Full-screen, on top, no second audio listener, not the "main" camera.
        _cineCam.rect = new Rect(0f, 0f, 1f, 1f);
        int baseDepth = src != null ? Mathf.RoundToInt(src.depth) : 0;
        _cineCam.depth = baseDepth + depthAboveGameplay;
        _cineCam.tag = "Untagged";

        var al = _cineCam.GetComponent<AudioListener>();
        if (al != null) al.enabled = false;

        _cineCam.gameObject.SetActive(false);
    }

    // Disable anything on a cloned prefab that would fight our manual transform / add a
    // second audio listener: co-op camera drivers, CameraShake, a CinemachineBrain, or a
    // stray PlayerInput. Matched by type NAME so we need no Cinemachine assembly reference.
    private static void StripCameraExtras(Camera cam)
    {
        if (cam == null) return;

        var listeners = cam.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++) listeners[i].enabled = false;

        var behaviours = cam.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var mb = behaviours[i];
            if (mb == null) continue;
            if (mb is ICoopCamera) { mb.enabled = false; continue; }

            string n = mb.GetType().Name;
            if (n.Contains("Cinemachine") || n == "CameraShake" || n.Contains("PlayerInput"))
                mb.enabled = false;
        }
    }

    // Best-effort copy of URP per-camera data (post-processing etc.) with NO hard
    // dependency on the URP assembly, so this compiles on non-URP projects too.
    private static void TryCopyUrpCameraData(Camera src, Camera dst)
    {
        if (src == null || dst == null) return;
        try
        {
            var t = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (t == null) return;

            var srcData = src.GetComponent(t);
            if (srcData == null) return;

            var dstData = dst.GetComponent(t);
            if (dstData == null) dstData = dst.gameObject.AddComponent(t);

            string[] props = { "renderPostProcessing", "antialiasing", "antialiasingQuality",
                               "dithering", "stopNaN", "volumeLayerMask" };
            for (int i = 0; i < props.Length; i++)
            {
                var pi = t.GetProperty(props[i]);
                if (pi != null && pi.CanRead && pi.CanWrite)
                    pi.SetValue(dstData, pi.GetValue(srcData));
            }
        }
        catch
        {
            // Non-URP, or the URP API changed — the base camera still renders fine.
        }
    }

    private void SetHudHidden(bool hidden)
    {
        if (hideDuringIntro == null) return;
        for (int i = 0; i < hideDuringIntro.Length; i++)
            if (hideDuringIntro[i] != null) hideDuringIntro[i].SetActive(!hidden);
    }

    // Hide/restore every player's weapon-roll hotbar. The hotbar canvas is a runtime
    // ScreenSpaceOverlay object (built by WeaponRollUI), so it can't be wired into
    // hideDuringIntro in the inspector — we resolve it live here. On hide we only touch
    // hotbars that are currently shown and remember them, so restore puts back exactly
    // those (a downed player's already-hidden hotbar stays hidden).
    private void SetWeaponRollsHidden(bool hidden)
    {
        if (!hideWeaponRollUiDuringIntro) return;

        if (hidden)
        {
            _rollsHiddenByIntro.Clear();
            var uis = FindObjectsByType<WeaponRollUI>(FindObjectsSortMode.None);
            for (int i = 0; i < uis.Length; i++)
            {
                var ui = uis[i];
                if (ui != null && ui.HudVisible)
                {
                    ui.SetHudVisible(false);
                    _rollsHiddenByIntro.Add(ui);
                }
            }
        }
        else
        {
            for (int i = 0; i < _rollsHiddenByIntro.Count; i++)
                if (_rollsHiddenByIntro[i] != null) _rollsHiddenByIntro[i].SetHudVisible(true);
            _rollsHiddenByIntro.Clear();
        }
    }

    // Drive `step(0..1)` over `dur` unscaled seconds so the intro plays even while frozen.
    private static IEnumerator Animate(float dur, System.Action<float> step)
    {
        if (dur <= 0f) { step(1f); yield break; }
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            step(Mathf.Clamp01(t));
            yield return null;
        }
    }

    private static float EaseOutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }
    private static float EaseInOutCubic(float t) => t < 0.5f ? 4f * t * t * t
                                                             : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
}


