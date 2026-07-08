// Global selector for how the game paces the gap BETWEEN waves during a run.
// Lives next to RunConfig.timeBetweenWaves and is read by GameOrchestrator.
// Countdown is value 0 and the default everywhere, so a RunConfig authored before
// this field existed deserializes to Countdown == the original behaviour. That keeps
// the change a pure no-op for any existing run until a designer opts into a new mode.
public enum WavePacingMode
{
    // Wait a fixed number of seconds between waves (RunConfig.timeBetweenWaves).
    // This is the original behaviour.
    Countdown = 0,

    // Wait indefinitely. A "READY" button appears bottom-right of the screen; the
    // next wave only starts once EVERY player has readied up (both players in local
    // co-op). Players keep full control meanwhile (place towers, reposition, etc.).
    ReadyUp = 1,

    // No gap: the next wave spawns the instant the previous one is cleared.
    Immediate = 2,
}
