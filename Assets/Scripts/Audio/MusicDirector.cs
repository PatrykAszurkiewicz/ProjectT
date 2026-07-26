using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Section = AudioManager.MusicSection;

// SINGLE OWNER OF MUSIC STATE
// Every music decision in the game funnels through here. Nothing else should call
// AudioManager.SetMusicSection() — before this existed, WaveSpawner and
// GameOrchestrator both drove the same parameter and fought each other (see the
// WaveSpawner.OnEnemyDeath note in the migration list).
//
// It maps GameOrchestrator's RunState machine onto FMOD's "MusicSection" parameter
// and fires one-shot stingers on the punctuation moments (wave cleared, boss down,
// victory, defeat).
//
// Lifetime: this object persists across scene loads (like AudioManager), while
// GameOrchestrator is scene-scoped and dies on every scene change. So we POLL for
// the orchestrator rather than subscribing once, and re-sync to its current state
// the moment a new one appears — that also closes the subscription-timing gap where
// the orchestrator's Start() runs StartRun() and fires StageIntro on the same frame
// a Start()-based subscriber would have hooked up.

[DefaultExecutionOrder(-50)] // after AudioManager (-100) / FMODEvents (-200)
public class MusicDirector : MonoBehaviour
{
    public static MusicDirector Instance { get; private set; }

    [Header("Debug")]
    public bool debugLog = false;

    [Header("Timing")]
    [Tooltip("Seconds to hold the 'wave cleared' breath before dropping back to Calm. " +
             "Keep <= GameOrchestrator.pauseAfterLastKill so it never overruns the next wave.")]
    public float waveClearedBreath = 0.6f;

    [Tooltip("Seconds of Victory/GameOver music before letting the win/lose screen's own " +
             "music (if any) take over. 0 = hold forever.")]
    public float outroHold = 0f;

    private GameOrchestrator _orch;          // the instance we're currently subscribed to
    private Section _desired = Section.Menu; // what the game wants
    private Section _applied = (Section)(-1);// what FMOD was last told (avoids redundant sets)
    private bool _paused;                    // pause menu override
    private Coroutine _breath;

    //  BOOTSTRAP  (mirrors MenuClickSFX / AudioBootstrap — no scene wiring needed)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("MusicDirector");
        Instance = go.AddComponent<MusicDirector>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (transform.parent != null) transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable() { SceneManager.activeSceneChanged += HandleSceneChanged; }
    private void OnDisable() { SceneManager.activeSceneChanged -= HandleSceneChanged; Unsubscribe(); }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        // Unity's overloaded == treats a destroyed orchestrator as null, so this
        // compares cleanly across scene loads.
        //
        // NOTE: menu-vs-gameplay bed selection and per-run random-track choice are now
        // owned by AudioManager.UpdateMusicContext() (which just polls whether a
        // GameOrchestrator exists), so this can't get stuck even if this director were
        // slow to boot. Here we only follow the orchestrator to push the adaptive
        // MusicSection while a run is running.
        var orch = GameOrchestrator.Instance;
        if (orch != _orch)
        {
            Unsubscribe();
            _orch = orch;
            Subscribe();
            ResyncFromOrchestrator();
        }
    }

    //  SCENE / MENU CONTEXT

    private void HandleSceneChanged(Scene from, Scene to)
    {
        StopAllCoroutines();
        _breath = null;
        StartCoroutine(DetectContextNextFrame());
    }

    private IEnumerator DetectContextNextFrame()
    {
        // Give the new scene a frame so GameOrchestrator.Awake can register itself.
        // Without this, every load would blip through Menu on its way to StageIntro.
        yield return null;
        if (GameOrchestrator.Instance == null)
            Set(Section.Menu, "scene has no orchestrator");
    }

    //  ORCHESTRATOR HOOKUP

    private void Subscribe()
    {
        if (_orch == null) return;
        _orch.OnStateChanged += HandleStateChanged;
        _orch.OnWaveCleared += HandleWaveCleared;
        _orch.OnBossKilled += HandleBossKilled;
        _orch.OnVictory += HandleVictory;
        _orch.OnGameOver += HandleGameOver;
        if (debugLog) Debug.Log("[MusicDirector] Subscribed to orchestrator.");
    }

    private void Unsubscribe()
    {
        if (_orch == null) return; // also covers "destroyed on scene unload"
        _orch.OnStateChanged -= HandleStateChanged;
        _orch.OnWaveCleared -= HandleWaveCleared;
        _orch.OnBossKilled -= HandleBossKilled;
        _orch.OnVictory -= HandleVictory;
        _orch.OnGameOver -= HandleGameOver;
    }

    // Adopt whatever state the orchestrator is already in. Covers the case where the
    // run started before we managed to subscribe.
    private void ResyncFromOrchestrator()
    {
        if (_orch == null) { Set(Section.Menu, "no orchestrator"); return; }
        Set(SectionFor(_orch.CurrentState), $"resync ({_orch.CurrentState})");
    }

    //  STATE → SECTION MAP  (the whole design lives in this one function)

    private Section SectionFor(GameOrchestrator.RunState s)
    {
        switch (s)
        {
            case GameOrchestrator.RunState.Idle: return Section.Menu;
            case GameOrchestrator.RunState.StageIntro: return Section.StageIntro;
            case GameOrchestrator.RunState.WaveCountdown: return Section.Calm;
            case GameOrchestrator.RunState.WaveActive: return Section.Intense;
            case GameOrchestrator.RunState.StageBoss: return Section.Boss;
            case GameOrchestrator.RunState.AugmentSelect: return Section.Reward;
            case GameOrchestrator.RunState.StageComplete: return Section.Calm;
            case GameOrchestrator.RunState.FinalBoss: return Section.FinalBoss;
            case GameOrchestrator.RunState.Victory: return Section.Victory;
            case GameOrchestrator.RunState.GameOver: return Section.GameOver;
            default: return Section.Calm;
        }
    }

    private void HandleStateChanged(GameOrchestrator.RunState oldState, GameOrchestrator.RunState newState)
    {
        // A wave-cleared breath must not be stomped by the very next countdown, but
        // anything more important than a countdown wins immediately.
        if (_breath != null && newState == GameOrchestrator.RunState.WaveCountdown) return;

        Set(SectionFor(newState), $"state {oldState}→{newState}");
    }

    //  PUNCTUATION (stingers)

    private void HandleWaveCleared(int waveIndex)
    {
        Sting(f => f.stingerWaveCleared);

        // Short "you did it" beat before the next countdown's Calm.
        if (waveClearedBreath > 0f)
        {
            if (_breath != null) StopCoroutine(_breath);
            _breath = StartCoroutine(Breath(Section.Calm, waveClearedBreath));
        }
    }

    // stageIndex >= 0 → a stage boss. stageIndex < 0 → the FINAL boss (see the
    // orchestrator patch: RunFinalBoss invokes OnBossKilled(-1)). Mirrors the existing
    // OnBossSpawned(null) == final-boss convention.
    private void HandleBossKilled(int stageIndex)
    {
        bool isFinal = stageIndex < 0;
        Sting(f => isFinal ? f.stingerFinalBossDefeated : f.stingerBossDefeated);

        // Drop the fight bed straight away — Victory/StageComplete arrives seconds
        // later (2.75s for the final boss) and holding Boss music through a corpse is
        // the single most noticeable thing about the current setup.
        Set(isFinal ? Section.Victory : Section.Calm, isFinal ? "final boss down" : "stage boss down");
    }

    private void HandleVictory()
    {
        Sting(f => f.stingerVictory);
        Set(Section.Victory, "victory");
        if (outroHold > 0f) StartCoroutine(Breath(Section.Menu, outroHold));
    }

    private void HandleGameOver()
    {
        Sting(f => f.stingerGameOver);
        Set(Section.GameOver, "game over");
    }

    private IEnumerator Breath(Section then, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds); // realtime: menus set timeScale 0
        _breath = null;
        Set(then, "breath elapsed");
    }

    //  PUBLIC API for things outside the run loop

    // Call from the pause menu (or any UIModalStack push that freezes gameplay).
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        Apply(paused ? Section.Pause : _desired, paused ? "paused" : "unpaused");
    }

    // Call from the main menu / weapon-select / co-op lobby if you want to force the
    // menu bed without relying on scene detection.
    public void EnterMenu() => Set(Section.Menu, "menu requested");

    // Re-push the current section. AudioManager calls this after the music toggle is
    // switched back on, so re-enabling music doesn't snap you to Calm mid-boss.
    public void Reapply() { _applied = (Section)(-1); Apply(_paused ? Section.Pause : _desired, "reapply"); }

    public Section Current => _desired;

    //  PLUMBING

    private void Set(Section s, string reason)
    {
        _desired = s;
        if (_paused) return;      // pause bed holds until unpaused
        Apply(s, reason);
    }

    private void Apply(Section s, string reason)
    {
        var am = AudioManager.instance;
        if (am == null || !am.musicEnabled || !am.IsFMODInitialized) return;

        if (_applied == s) return;
        _applied = s;

        // Bed selection (menu track vs. gameplay track) is owned by AudioManager's
        // context poll. Here we ONLY push the adaptive MusicSection onto the gameplay
        // bed. Menu is a no-op — AudioManager ignores section changes while the menu
        // bed is active, and drives the menu track itself.
        if (s != Section.Menu)
            am.SetMusicSection(s);

        if (debugLog) Debug.Log($"[MusicDirector] → {s}   ({reason})");
    }

    private void Sting(System.Func<FMODEvents, FMODUnity.EventReference> pick)
    {
        var am = AudioManager.instance;
        var fe = FMODEvents.instance;
        if (am == null || fe == null || !am.musicEnabled) return;

        var eventRef = pick(fe);
        if (eventRef.IsNull) return;

        // 2D one-shot: stingers are non-diegetic, and in co-op there are two listeners.
        am.PlayOneShot(eventRef, Vector3.zero);
    }
}
