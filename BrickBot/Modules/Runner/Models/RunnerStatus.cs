namespace BrickBot.Modules.Runner.Models;

public enum RunnerStatus
{
    Idle,
    Running,
    Stopping,
    Faulted,
}

public sealed record RunnerState(RunnerStatus Status, string? ErrorMessage = null);

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message);
