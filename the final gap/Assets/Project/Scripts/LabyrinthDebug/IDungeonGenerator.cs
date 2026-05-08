/// <summary>
/// Shared contract for all dungeon / labyrinth generator variants.
///
/// Implement this interface on any MonoBehaviour to make it work automatically
/// with DungeonDebugController and GeneratorSwitcher — no controller changes needed.
///
/// Convention
///   CurrentStep == 0   → pre-generation (nothing carved yet)
///   CurrentStep == k   → step k has just finished
///   CurrentStep == TotalSteps → full layout complete
/// </summary>
public interface IDungeonGenerator
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>Human-readable name shown in the debug UI.</summary>
    string GeneratorName { get; }

    // ── Step Tracking ─────────────────────────────────────────────────────────

    /// <summary>Current step index. 0 = nothing done yet.</summary>
    int CurrentStep { get; }

    /// <summary>Total number of sequential steps this generator exposes.</summary>
    int TotalSteps { get; }

    /// <summary>True while a coroutine animation is actively running.</summary>
    bool IsAnimating { get; }

    // ── Actions ───────────────────────────────────────────────────────────────

    /// <summary>Advance exactly one step forward (no-op if animating or at max).</summary>
    void NextStep();

    /// <summary>
    /// Clear everything and run all steps automatically for a fresh generation.
    /// Equivalent to pressing "New Chunk" in the original generator.
    /// </summary>
    void GenerateNewChunk();

    // ── Feedback ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Short human-readable description of what the current step is / was doing.
    /// Displayed in the debug UI's description text field.
    /// </summary>
    string GetStepDescription();
}
