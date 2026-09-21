using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Profiler = UnityEngine.Profiling.Profiler;


/// Boot / load profiler.
/// USAGE
///   BootProfiler.StartClock("Play pressed");            // optional
///   BootProfiler.Mark("phase boundary");                // between phases
///   using (BootProfiler.Scope("Slime LoadAll")) { … }   // real durations
///   BootProfiler.Accumulate("LoadFolderCached", ms, "Slime");   // hot repeats
///   BootProfiler.SnapshotTextures("after prewarm");     // memory truth
///   BootProfiler.Summary();                             // sorted report
///
/// Set Enabled = false (or strip the calls) before shipping.

public static class BootProfiler
{
    /// Master switch. When false every call becomes a couple of predictable
    /// branches and nothing is logged or allocated.
    public static bool Enabled = false;

    /// Per-mark lines. Turn off to keep only the end-of-boot Summary(), which is
    /// usually all you actually need and is far easier to read.
    public static bool VerboseMarks = true;

    /// Gaps at or above this many ms are flagged in the log and the summary.
    public static float SlowGapMs = 250f;

    /// A gap this long spanning ONE frame is a visible hitch, not background work.
    public static float HitchMs = 100f;

    private static readonly Stopwatch _sw = new Stopwatch();
    private static long _lastMs;
    private static int _lastFrame;
    private static string _lastLabel = "clock start";

    private sealed class Gap
    {
        public string From, To;
        public long Ms;
        public int Frames;
        public long MemDeltaBytes;
    }

    private sealed class Bucket
    {
        public int Count;
        public double TotalMs, MaxMs;
        public string MaxTag;
    }

    private static readonly List<Gap> _gaps = new List<Gap>();
    private static readonly Dictionary<string, Bucket> _scopes = new Dictionary<string, Bucket>();
    private static readonly Dictionary<string, Bucket> _accum = new Dictionary<string, Bucket>();
    private static long _lastMemBytes;


    // ───────────────────────────────────── automatic lifecycle brackets ──
    // These fire without any component, and they bracket the phase that the
    // orchestrator's own marks cannot see: everything Unity does between the
    // scene deserialising and the first Start().

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void _BootHook_Subsystem()
    {
        if (!Enabled) return;
        StartClock("engine subsystems registered");   // earliest reachable point
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void _BootHook_BeforeScene() => Mark("[PERF] ── before scene load");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void _BootHook_AfterScene() => Mark("[PERF] ── scene loaded, ALL Awake/OnEnable done");


    // ─────────────────────────────────────────────────────────────── clock ──

    /// Call the instant the player commits (button press, just before LoadScene).
    public static void StartClock(string label)
    {
        if (!Enabled) return;

        _gaps.Clear();
        _scopes.Clear();
        _accum.Clear();

        _lastMs = 0;
        _lastFrame = Time.frameCount;
        _lastLabel = label;
        _lastMemBytes = Profiler.GetTotalAllocatedMemoryLong();

        _sw.Restart();
        Debug.Log($"[PERF] ═════ CLOCK START: {label} ═════");
    }

    private static void EnsureRunning()
    {
        if (_sw.IsRunning) return;
        _sw.Restart();
        _lastMs = 0;
        _lastFrame = Time.frameCount;
        _lastMemBytes = Profiler.GetTotalAllocatedMemoryLong();
        Debug.Log("[PERF] ═════ CLOCK AUTO-START (no StartClock; played the scene directly) ═════");
    }

    // ──────────────────────────────────────────────────────────────── mark ──

    /// Record a phase boundary. The number printed is the gap SINCE THE PREVIOUS
    /// MARK — it is shown as a transition "previous → this" precisely so it can
    /// never be mistaken for the cost of this label. To measure the cost of a
    /// specific piece of work, use Scope() instead.
    public static void Mark(string label)
    {
        if (!Enabled) return;
        EnsureRunning();

        long now = _sw.ElapsedMilliseconds;
        long ms = now - _lastMs;
        int frames = Time.frameCount - _lastFrame;

        long mem = Profiler.GetTotalAllocatedMemoryLong();
        long memDelta = mem - _lastMemBytes;

        _gaps.Add(new Gap { From = _lastLabel, To = label, Ms = ms, Frames = frames, MemDeltaBytes = memDelta });

        if (VerboseMarks)
        {
            bool slow = ms >= SlowGapMs;
            bool hitch = ms >= HitchMs && frames <= 1;

            string line =
                $"[PERF] {(hitch ? "HITCH " : slow ? "SLOW  " : "      ")}" +
                $"+{ms,6} ms  {frames,4} frame(s)  {Mb(memDelta),9}  " +
                $"total {now,6} ms   │ {_lastLabel}  →  {label}";

            if (hitch) Debug.LogWarning(line + "\n         ↑ one frame — this is a visible freeze, not background work.");
            else if (slow) Debug.LogWarning(line);
            else Debug.Log(line);
        }

        _lastMs = now;
        _lastFrame = Time.frameCount;
        _lastLabel = label;
        _lastMemBytes = mem;
    }

    // ─────────────────────────────────────────────────────────────── scope ──

    /// Measure the ACTUAL duration of a block:
    ///     using (BootProfiler.Scope("Slime LoadAll")) { … }
    /// Unlike Mark, this times the work itself, so the number belongs to the label.
    public static ScopeHandle Scope(string label)
    {
        if (!Enabled) return default;
        EnsureRunning();
        return new ScopeHandle(label, _sw.ElapsedMilliseconds, Time.frameCount,
                               Profiler.GetTotalAllocatedMemoryLong());
    }

    public readonly struct ScopeHandle : IDisposable
    {
        private readonly string _label;
        private readonly long _startMs;
        private readonly int _startFrame;
        private readonly long _startMem;

        internal ScopeHandle(string label, long startMs, int startFrame, long startMem)
        { _label = label; _startMs = startMs; _startFrame = startFrame; _startMem = startMem; }

        public void Dispose()
        {
            if (!Enabled || _label == null) return;

            long ms = _sw.ElapsedMilliseconds - _startMs;
            int frames = Time.frameCount - _startFrame;
            long mem = Profiler.GetTotalAllocatedMemoryLong() - _startMem;

            if (!_scopes.TryGetValue(_label, out var b)) _scopes[_label] = b = new Bucket();
            b.Count++;
            b.TotalMs += ms;
            if (ms > b.MaxMs) { b.MaxMs = ms; b.MaxTag = null; }

            if (VerboseMarks && ms >= 1)
                Debug.Log($"[PERF] SCOPE  {ms,6} ms  {frames,4} frame(s)  {Mb(mem),9}   │ {_label}");
        }
    }

    // ───────────────────────────────────────────────────────── accumulate ──

    /// Sum many small repeated operations into one bucket, so a thousand 2 ms
    /// loads show up as a 2,000 ms line instead of vanishing into the noise.
    /// `tag` records which call was the worst (e.g. the folder name).
    public static void Accumulate(string bucket, double ms, string tag = null)
    {
        if (!Enabled) return;
        if (!_accum.TryGetValue(bucket, out var b)) _accum[bucket] = b = new Bucket();
        b.Count++;
        b.TotalMs += ms;
        if (ms > b.MaxMs) { b.MaxMs = ms; b.MaxTag = tag; }
    }

    // ────────────────────────────────────────────────────────────── memory ──

    /// Walk every loaded Texture and report count + VRAM. This is how you PROVE
    /// the Resources double-load: if a sprite atlas page and its source frames
    /// are both resident, both appear here and the total is roughly doubled.
    /// Not cheap (it enumerates all loaded objects) — call at phase boundaries only.
    public static void SnapshotTextures(string label, int listTopN = 8)
    {
        if (!Enabled) return;

        var textures = Resources.FindObjectsOfTypeAll<Texture>();
        long total = 0;
        var rows = new List<(string name, long bytes, string dims)>(textures.Length);

        foreach (var t in textures)
        {
            if (t == null) continue;
            long bytes = Profiler.GetRuntimeMemorySizeLong(t);
            total += bytes;
            rows.Add((t.name, bytes, $"{t.width}x{t.height}"));
        }

        Debug.Log($"[PERF] ── TEXTURE SNAPSHOT '{label}': {textures.Length} textures, {Mb(total)} total ──");

        foreach (var r in rows.OrderByDescending(r => r.bytes).Take(listTopN))
            Debug.Log($"[PERF]      {Mb(r.bytes),9}  {r.dims,11}  {r.name}");
    }

    // ───────────────────────────────────────────────────────────── summary ──

    /// Sorted end-of-boot report. This is the output worth reading: it ranks gaps
    /// by cost, separates real hitches from background work, and surfaces buckets
    /// that individually looked trivial.
    public static void Summary(string label = "BOOT")
    {
        if (!Enabled || _gaps.Count == 0) return;

        long total = _sw.ElapsedMilliseconds;
        var sb = new System.Text.StringBuilder(2048);

        sb.AppendLine($"[PERF] ╔══════════ {label} PROFILE SUMMARY — {total} ms total ══════════");

        sb.AppendLine("[PERF] ║ TOP GAPS  (time BETWEEN marks — attributed to the transition, not one label)");
        foreach (var g in _gaps.OrderByDescending(g => g.Ms).Take(10))
        {
            string flag = g.Ms >= HitchMs && g.Frames <= 1 ? "HITCH" : g.Ms >= SlowGapMs ? "slow " : "     ";
            sb.AppendLine($"[PERF] ║  {flag} {g.Ms,6} ms  {g.Frames,4}f  {Pct(g.Ms, total),5}   {g.From} → {g.To}");
        }

        var hitches = _gaps.Where(g => g.Ms >= HitchMs && g.Frames <= 1).ToList();
        if (hitches.Count > 0)
        {
            sb.AppendLine($"[PERF] ║");
            sb.AppendLine($"[PERF] ║ SINGLE-FRAME HITCHES ({hitches.Count}) — these are visible freezes:");
            foreach (var g in hitches.OrderByDescending(g => g.Ms))
                sb.AppendLine($"[PERF] ║    {g.Ms,6} ms   {g.From} → {g.To}");
        }

        if (_scopes.Count > 0)
        {
            sb.AppendLine("[PERF] ║");
            sb.AppendLine("[PERF] ║ MEASURED SCOPES  (real durations — trust these over gaps)");
            foreach (var kv in _scopes.OrderByDescending(k => k.Value.TotalMs))
                sb.AppendLine($"[PERF] ║    {kv.Value.TotalMs,8:F0} ms  x{kv.Value.Count,-4} " +
                              $"max {kv.Value.MaxMs,6:F0} ms   {kv.Key}");
        }

        if (_accum.Count > 0)
        {
            sb.AppendLine("[PERF] ║");
            sb.AppendLine("[PERF] ║ ACCUMULATORS  (many small calls summed)");
            foreach (var kv in _accum.OrderByDescending(k => k.Value.TotalMs))
                sb.AppendLine($"[PERF] ║    {kv.Value.TotalMs,8:F0} ms  x{kv.Value.Count,-4} " +
                              $"max {kv.Value.MaxMs,6:F0} ms ({kv.Value.MaxTag ?? "?"})   {kv.Key}");
        }

        double covered = _scopes.Sum(k => k.Value.TotalMs) + _accum.Sum(k => k.Value.TotalMs);
        sb.AppendLine("[PERF] ║");
        sb.AppendLine($"[PERF] ║ Measured work accounts for {covered:F0} ms of {total} ms " +
                      $"({Pct((long)covered, total)}). The remainder sits in gaps that have no");
        sb.AppendLine("[PERF] ║ Scope() around them — if a big gap is unexplained, that is where to instrument next.");
        sb.AppendLine("[PERF] ╚═══════════════════════════════════════════════════════════");

        Debug.Log(sb.ToString());
    }

    // ───────────────────────────────────────────────────────────── helpers ──

    private static string Mb(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        if (Math.Abs(mb) < 0.05) return "";
        return $"{(mb >= 0 ? "+" : "")}{mb:F1} MB";
    }

    private static string Pct(long part, long whole)
        => whole <= 0 ? "  -  " : $"{100.0 * part / whole,4:F1}%";
}


