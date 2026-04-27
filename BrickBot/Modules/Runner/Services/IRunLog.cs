using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core;
using BrickBot.Modules.Runner.Models;

namespace BrickBot.Modules.Runner.Services;

/// <summary>Log surface that the script host writes to. Lines fan out to the frontend via the event bus.</summary>
public interface IRunLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class RunLog : IRunLog
{
    private readonly IProfileEventBus _eventBus;

    public RunLog(IProfileEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void Info(string message) => Emit("info", message);
    public void Warn(string message) => Emit("warn", message);
    public void Error(string message) => Emit("error", message);

    private void Emit(string level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, level, message);
        _ = _eventBus.EmitAsync(ModuleNames.RUNNER, RunnerEvents.LOG, entry);
    }
}
