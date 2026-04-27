using BrickBot.Modules.Runner.Models;

namespace BrickBot.Modules.Runner.Services;

public interface IRunnerService
{
    RunnerState State { get; }
    void Start(RunRequest request);
    void Stop();
}

/// <summary>
/// Start a Run against a target window using a profile-scoped main script.
/// The Runner reads <c>data/profiles/{ProfileId}/scripts/main/{MainName}.js</c> as the entrypoint
/// and pre-loads every <c>library/*.js</c> into the engine first.
/// </summary>
public sealed record RunRequest(
    nint WindowHandle,
    string ProfileId,
    string MainName,
    string TemplateRoot);
