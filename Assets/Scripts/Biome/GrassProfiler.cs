using UnityEngine;
using UnityEngine.Profiling;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Debug = UnityEngine.Debug;


public class GrassProfiler : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Automatically profile on Start (after grass scripts run)")]
    public bool profileOnStart = true;

    [Tooltip("Also log per-frame render cost for N frames (0 = skip)")]
    public int profileFrameCount = 60;

    // Internal
    private int framesProfiled = 0;
    private float frameTotalMs = 0f;
    private float frameMinMs = float.MaxValue;
    private float frameMaxMs = 0f;
    private bool profilingFrames = false;
    private Stopwatch frameSW = new Stopwatch();

    void Start()
    {
        if (profileOnStart)
        {
            // Use LateUpdate-style delay so grass scripts finish their Start() first
            Invoke(nameof(ProfileGrassGeneration), 0.1f);
        }
    }

    [ContextMenu("Profile Grass Generation")]
    public void ProfileGrassGeneration()
    {
        Debug.LogWarning("═══════════════════════════════════════════════════");
        Debug.LogWarning("       [GrassProfiler] GRASS MEMORY REPORT");
        Debug.LogWarning("═══════════════════════════════════════════════════");

        ProfileCPUGrass();
        ProfileGPUGrass();
        ProfileSystemMemory();

        if (profileFrameCount > 0)
        {
            framesProfiled = 0;
            frameTotalMs = 0f;
            frameMinMs = float.MaxValue;
            frameMaxMs = 0f;
            profilingFrames = true;
            Debug.LogWarning($"[GrassProfiler] Starting per-frame profiling for {profileFrameCount} frames...");
        }
    }

    //  CPU GRASS (GrassOverlay — mesh-based)

    void ProfileCPUGrass()
    {
        GrassOverlay cpuGrass = FindAnyObjectByType<GrassOverlay>();
        if (cpuGrass == null)
        {
            Debug.LogWarning("[GrassProfiler] No GrassOverlay found in scene. Skipping CPU grass profiling.");
            return;
        }

        Debug.LogWarning("───────── CPU Grass (GrassOverlay) ─────────");

        // Measure regeneration time + memory delta
        long memBefore = Profiler.GetTotalAllocatedMemoryLong();
        long monoMemBefore = Profiler.GetMonoUsedSizeLong();

        Stopwatch sw = Stopwatch.StartNew();
        cpuGrass.GenerateGrass();
        sw.Stop();

        long memAfter = Profiler.GetTotalAllocatedMemoryLong();
        long monoMemAfter = Profiler.GetMonoUsedSizeLong();

        float genTimeMs = sw.ElapsedTicks / (float)Stopwatch.Frequency * 1000f;
        long allocDelta = memAfter - memBefore;
        long monoDelta = monoMemAfter - monoMemBefore;

        Debug.LogWarning($"  Blade Count:        {cpuGrass.bladeCount:N0}");
        Debug.LogWarning($"  Clump Count:        {cpuGrass.clumpCount:N0}");
        Debug.LogWarning($"  Generation Time:    {genTimeMs:F2} ms");
        Debug.LogWarning($"  Unity Alloc Delta:  {FormatBytes(allocDelta)}");
        Debug.LogWarning($"  Mono Heap Delta:    {FormatBytes(monoDelta)}");

        // Estimate mesh memory from blade count
        // Standard blade: 7 verts × (12+16+8+8) bytes = 308 bytes, + 15 tri indices × 4 = 60 → ~368 bytes
        // Tall blade:     9 verts × 44 + 21×4 = 480 bytes
        int stdBlades = Mathf.RoundToInt(cpuGrass.bladeCount * (1f - cpuGrass.tallBladeRatio));
        int tallBlades = cpuGrass.bladeCount - stdBlades;
        long estMeshRAM = (long)stdBlades * 368 + (long)tallBlades * 480;

        Debug.LogWarning($"  Est. Mesh RAM:      {FormatBytes(estMeshRAM)}");

        // Count child mesh objects
        MeshFilter[] meshFilters = cpuGrass.GetComponentsInChildren<MeshFilter>();
        int totalVerts = 0, totalTris = 0;
        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                totalVerts += mf.sharedMesh.vertexCount;
                totalTris += mf.sharedMesh.triangles.Length / 3;
            }
        }
        Debug.LogWarning($"  Mesh Objects:       {meshFilters.Length}");
        Debug.LogWarning($"  Total Vertices:     {totalVerts:N0}");
        Debug.LogWarning($"  Total Triangles:    {totalTris:N0}");
    }

    // ─────────────────────────────────────────────
    //  GPU GRASS (GrassOverlayGPU — instanced)
    // ─────────────────────────────────────────────

    void ProfileGPUGrass()
    {
        GrassOverlayGPU gpuGrass = FindAnyObjectByType<GrassOverlayGPU>();
        if (gpuGrass == null)
        {
            Debug.LogWarning("[GrassProfiler] No GrassOverlayGPU found in scene. Skipping GPU grass profiling.");
            return;
        }

        Debug.LogWarning("───────── GPU Grass (GrassOverlayGPU) ─────────");

        // Measure regeneration
        long memBefore = Profiler.GetTotalAllocatedMemoryLong();

        Stopwatch sw = Stopwatch.StartNew();
        gpuGrass.GenerateGrass();
        sw.Stop();

        long memAfter = Profiler.GetTotalAllocatedMemoryLong();
        float genTimeMs = sw.ElapsedTicks / (float)Stopwatch.Frequency * 1000f;

        Debug.LogWarning($"  Blade Count:        {gpuGrass.bladeCount:N0}");
        Debug.LogWarning($"  Clump Count:        {gpuGrass.clumpCount:N0}");
        Debug.LogWarning($"  Generation Time:    {genTimeMs:F2} ms  (CPU side — GPU compute is async)");
        Debug.LogWarning($"  Unity Alloc Delta:  {FormatBytes(memAfter - memBefore)}");

        // ComputeBuffer VRAM estimates
        int bladeStride = Marshal.SizeOf(typeof(GrassBladeProxy));
        long bladeBufferBytes = (long)gpuGrass.bladeCount * bladeStride;

        // Args buffer: 5 × 4 bytes
        long argsBytes = 5 * sizeof(uint);

        // Shared blade mesh on GPU: 7 verts × (12 pos + 8 uv) + 15 indices × 2 = ~170 bytes (trivial)
        long meshGPU = 170;

        long totalVRAM = bladeBufferBytes + argsBytes + meshGPU;

        Debug.LogWarning($"  Blade Buffer VRAM:  {FormatBytes(bladeBufferBytes)}  ({bladeStride} bytes/blade × {gpuGrass.bladeCount:N0})");
        Debug.LogWarning($"  Args Buffer VRAM:   {FormatBytes(argsBytes)}");
        Debug.LogWarning($"  Total Est. VRAM:    {FormatBytes(totalVRAM)}");

        // CPU-side RAM for clump generation (temporary, already freed)
        int clumpStride = Marshal.SizeOf(typeof(ClumpProxy));
        long clumpTempRAM = (long)gpuGrass.clumpCount * clumpStride;
        Debug.LogWarning($"  Clump Temp RAM:     {FormatBytes(clumpTempRAM)}  (freed after dispatch)");

        // If CPU fallback was used, the blade array would cost:
        long fallbackRAM = (long)gpuGrass.bladeCount * bladeStride;
        Debug.LogWarning($"  CPU Fallback RAM:   {FormatBytes(fallbackRAM)}  (only if compute shader missing)");
    }

    // ─────────────────────────────────────────────
    //  SYSTEM-WIDE MEMORY
    // ─────────────────────────────────────────────

    void ProfileSystemMemory()
    {
        Debug.LogWarning("───────── System Memory Overview ─────────");

        long totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
        long totalReserved = Profiler.GetTotalReservedMemoryLong();
        long totalUnused = Profiler.GetTotalUnusedReservedMemoryLong();
        long monoUsed = Profiler.GetMonoUsedSizeLong();
        long monoHeap = Profiler.GetMonoHeapSizeLong();
        long gfxDriver = Profiler.GetAllocatedMemoryForGraphicsDriver();

        Debug.LogWarning($"  Total Allocated:    {FormatBytes(totalAllocated)}");
        Debug.LogWarning($"  Total Reserved:     {FormatBytes(totalReserved)}");
        Debug.LogWarning($"  Unused Reserved:    {FormatBytes(totalUnused)}");
        Debug.LogWarning($"  Mono Heap Used:     {FormatBytes(monoUsed)}  /  {FormatBytes(monoHeap)} heap");
        Debug.LogWarning($"  GFX Driver Memory:  {FormatBytes(gfxDriver)}");
        Debug.LogWarning($"  System RAM:         {SystemInfo.systemMemorySize} MB");
        Debug.LogWarning($"  GPU VRAM:           {SystemInfo.graphicsMemorySize} MB");
        Debug.LogWarning($"  GPU:                {SystemInfo.graphicsDeviceName}");

        Debug.LogWarning("═══════════════════════════════════════════════════");
    }

    // ─────────────────────────────────────────────
    //  PER-FRAME RENDER COST
    // ─────────────────────────────────────────────

    void LateUpdate()
    {
        if (!profilingFrames) return;

        // We measure the full frame time as an approximation.
        // For more precise GPU-only timing, use Unity's FrameTimingManager.
        float frameMs = Time.deltaTime * 1000f;

        framesProfiled++;
        frameTotalMs += frameMs;
        if (frameMs < frameMinMs) frameMinMs = frameMs;
        if (frameMs > frameMaxMs) frameMaxMs = frameMs;

        if (framesProfiled >= profileFrameCount)
        {
            profilingFrames = false;
            float avgMs = frameTotalMs / framesProfiled;
            float avgFPS = 1000f / avgMs;

            Debug.LogWarning("───────── Per-Frame Timing ─────────");
            Debug.LogWarning($"  Frames Sampled:     {framesProfiled}");
            Debug.LogWarning($"  Avg Frame Time:     {avgMs:F2} ms  ({avgFPS:F1} FPS)");
            Debug.LogWarning($"  Min Frame Time:     {frameMinMs:F2} ms");
            Debug.LogWarning($"  Max Frame Time:     {frameMaxMs:F2} ms");
            Debug.LogWarning("═══════════════════════════════════════════════════");
        }
    }

    // ─────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────

    static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024f * 1024f):F2} MB";
        return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
    }

    // Mirror structs just for sizeof — must match GrassOverlayGPU's layout
    [StructLayout(LayoutKind.Sequential)]
    private struct GrassBladeProxy
    {
        public Vector3 position;
        public float height;
        public float width;
        public float lean;
        public float curvature;
        public float phase;
        public uint packedType;
        public float padding;
        public Vector4 colorBase;
        public Vector4 colorTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClumpProxy
    {
        public Vector2 position;
    }
}
