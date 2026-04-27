namespace BrickBot.Modules.Runner.Models;

public enum RunnerStatus
{
    Idle,
    Running,
    Stopping,
    Faulted,
}

/// <summary>
/// Why the Runner transitioned out of <see cref="RunnerStatus.Running"/>. Surfaced in
/// <see cref="RunnerState"/> so the UI can show "Stopped: timed out after 30 min" instead
/// of a generic "Idle". Camelcase via JsonStringEnumConverter.
/// </summary>
public enum StopReason
{
    /// <summary>Hasn't stopped or never started.</summary>
    None,
    /// <summary>User clicked Stop in the UI.</summary>
    User,
    /// <summary>StopWhen.TimeoutMs elapsed.</summary>
    Timeout,
    /// <summary>brickbot event matching StopWhen.OnEvent fired.</summary>
    Event,
    /// <summary>ctx predicate StopWhen.CtxKey/CtxOp/CtxValue matched on a tick boundary.</summary>
    Context,
    /// <summary>Script called brickbot.stop().</summary>
    Script,
    /// <summary>Main script returned without runForever (one-shot scripts).</summary>
    Completed,
    /// <summary>Run threw an unhandled exception.</summary>
    Faulted,
}

public sealed record RunnerState(
    RunnerStatus Status,
    string? ErrorMessage = null,
    StopReason StoppedReason = StopReason.None,
    string? StoppedDetail = null);

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message);
