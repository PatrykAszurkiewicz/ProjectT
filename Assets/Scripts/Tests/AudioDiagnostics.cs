using System.Collections;
using UnityEngine;
using FMODUnity;


/// Drop on any active GameObject in your gameplay scene and press Play. Reports the live state
/// of the audio chain (F8), polls music init over several seconds (because AudioManager inits
/// music in a coroutine that WAITS — a first-frame 'music not initialized' is usually just
/// timing), and gives you F10 to force music + play a test SFX. Filter the Console by [AudioDiag].
/// FMOD audio does NOT use Unity's AudioListener — it uses FMOD's Studio Listener. If MUSIC is
/// silent, the cause is init/volume (music is a 2D event, plays regardless of listener position);
/// if only 3D SFX are silent, it's the Studio Listener count/placement.

public class AudioDiagnostics : MonoBehaviour
{
    private const string TAG = "[AudioDiag] ";

    private void Start()
    {
        Report();
        StartCoroutine(PollMusicInit());
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (kb.f8Key.wasPressedThisFrame) Report();
        if (kb.f10Key.wasPressedThisFrame) ForceMusicAndTestSFX();
#else
        if (Input.GetKeyDown(KeyCode.F8)) Report();
        if (Input.GetKeyDown(KeyCode.F10)) ForceMusicAndTestSFX();
#endif
    }

    [ContextMenu("Run Audio Diagnostics")]
    private void Report()
    {
        L("================ AUDIO DIAGNOSTICS ================");

        var am = AudioManager.instance;
        if (am == null)
        {
            LErr("AudioManager.instance is NULL (none in scene, or it destroyed itself as a duplicate — " +
                 "search the Console for 'Found more than one Audio Manager'). Nothing will play.");
        }
        else
        {
            L($"AudioManager: present.  FMOD initialized = {am.IsFMODInitialized}.  Music initialized = {am.IsMusicInitialized}.");
            if (!am.IsFMODInitialized)
                LErr("FMOD is NOT initialized — buses failed. Check banks are built/assigned.");

            // Volumes + toggle — a single 0 here mutes a whole category.
            L($"musicEnabled={am.musicEnabled}  master={am.masterVolume:F2}  music={am.musicVolume:F2}  " +
              $"sfx={am.SFXVolume:F2}  ambience={am.ambienceVolume:F2}");
            if (am.masterVolume <= 0.001f) LWarn("masterVolume ~0 — EVERYTHING is muted. Raise it on the AudioManager.");
            if (am.musicVolume <= 0.001f) LWarn("musicVolume ~0 — music muted.");
            if (am.SFXVolume <= 0.001f) LWarn("SFXVolume ~0 — SFX muted.");
            if (!am.musicEnabled) LWarn("musicEnabled is FALSE — music won't start until toggled on.");
        }

        var fe = FMODEvents.instance;
        if (fe == null) LErr("FMODEvents.instance is NULL — music + named SFX references unavailable.");
        else
        {
            L("FMODEvents: present.");
            try { L(fe.musicAmbient.IsNull ? "FMODEvents.musicAmbient NOT assigned." : "FMODEvents.musicAmbient: assigned."); }
            catch (System.Exception e) { LWarn($"musicAmbient read failed: {e.Message}"); }
        }

        int unity = ActiveCount<AudioListener>(out int unityTotal);
        int fmod = ActiveCount<StudioListener>(out int fmodTotal);
        L($"Unity AudioListener: {unity} active ({unityTotal} total). FMOD ignores this one.");
        if (unity != 1) LWarn($"{unity} active Unity AudioListeners — want exactly 1 for split-screen.");
        L($"FMOD Studio Listener: {fmod} active ({fmodTotal} total). FMOD uses THIS for 3D.");
        if (fmod == 0) LErr("Zero active FMOD Studio Listeners — 3D SFX silent. Put ONE on a persistent active object.");
        else if (fmod > 1) LWarn($"{fmod} active FMOD Studio Listeners — keep exactly ONE unless FMOD is configured for more.");
        else L("FMOD Studio Listener count looks right (exactly one active).");

        L("If music shows False here, that may be timing — see the [AudioDiag] poll lines over the next few seconds.");
        L("==================================================");
    }

    /// AudioManager inits music in a coroutine that waits for buses + FMODEvents + a frame, so a
    /// first-frame 'False' is normal. Poll for a while and report when it actually settles.
    private IEnumerator PollMusicInit()
    {
        var am = AudioManager.instance;
        if (am == null) yield break;
        if (am.IsMusicInitialized) yield break;

        float t = 0f; const float timeout = 8f;
        while (t < timeout)
        {
            yield return new WaitForSeconds(0.5f);
            t += 0.5f;
            am = AudioManager.instance;
            if (am == null) { LErr("AudioManager disappeared while waiting for music."); yield break; }
            if (am.IsMusicInitialized)
            {
                L($"Music initialized after ~{t:F1}s (the earlier 'False' was just timing).");
                try { am.SetMusicSection(AudioManager.MusicSection.Calm); } catch { }
                L("Set music section -> Calm. You should hear music now (if not, check the volumes above / mixer).");
                yield break;
            }
        }
        LErr($"Music STILL not initialized after {timeout:F0}s. FMOD is up but the music event never started.");
        L("ROOT CAUSE is in AudioManager's OWN logs — clear the [AudioDiag] filter and look for: " +
          "'Attempting to initialize music', 'Failed to start music instance: <RESULT>', or " +
          "'Exception initializing music'. That RESULT/exception is the answer — most often the music " +
          "event's BANK isn't loaded, or the EventReference points to an event not in the loaded banks.");
        L("Press F10 to force music init + play a test SFX.");
    }

    [ContextMenu("Force music + test SFX (F10)")]
    private void ForceMusicAndTestSFX()
    {
        L("----- F10: force music + test SFX -----");
        var am = AudioManager.instance;
        if (am == null) { LErr("No AudioManager."); return; }

        try { am.EnsureMusicReady(); } catch (System.Exception e) { LWarn($"EnsureMusicReady threw: {e.Message}"); }
        try { am.SetMusicSection(AudioManager.MusicSection.Calm); } catch (System.Exception e) { LWarn($"SetMusicSection threw: {e.Message}"); }
        L($"After force: Music initialized = {am.IsMusicInitialized}, musicEnabled = {am.musicEnabled}, music volume = {am.musicVolume:F2}.");

        var fe = FMODEvents.instance;
        if (fe != null)
        {
            try
            {
                if (!fe.click.IsNull) { am.PlaySFX(fe.click, Vector3.zero); L("Played test SFX 'click' at origin — did you hear it?"); }
                else LWarn("No 'click' SFX assigned to test with.");
            }
            catch (System.Exception e) { LWarn($"PlaySFX threw: {e.Message}"); }
        }
        L("If the test SFX is audible but music isn't: it's a MUSIC event/bank issue. If NOTHING is audible: " +
          "it's master volume / mixer / output device. If only world SFX are quiet: it's the Studio Listener position.");
    }

    private static int ActiveCount<T>(out int total) where T : Behaviour
    {
        var all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        total = all.Length;
        int active = 0;
        foreach (var b in all) if (b != null && b.isActiveAndEnabled) active++;
        return active;
    }

    private static void L(string m) => Debug.Log(TAG + m);
    private static void LWarn(string m) => Debug.LogWarning(TAG + m);
    private static void LErr(string m) => Debug.LogError(TAG + m);
}

