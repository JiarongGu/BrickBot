using BrickBot.Modules.Runner.Models;

namespace BrickBot.Modules.Runner.Services;

public interface IRunnerService
{
    RunnerState State { get; }
    void Start(RunRequest request);
    void Stop();

    /// <summary>Currently registered <c>brickbot.action</c> names for the active run.
    /// Empty when no run is active.</summary>
    IReadOnlyList<string> ListActions();

    /// <summary>Schedule a registered action to fire on the next engine tick.
    /// Throws RUNNER_ACTION_NOT_FOUND if no run is active or the name is unknown.</summary>
    void InvokeAction(string actionName);
}

/// <summary>
/// Start a Run against a target window using a profile-scoped main script.
/// The Runner reads <c>data/profiles/{ProfileId}/scripts/main/{MainName}.js</c> as the entrypoint
/// and pre-loads every <c>library/*.js</c> into the engine first.
///
/// Optional <see cref="StopWhen"/> wires automatic shutdown — useful for fixed-duration runs,
/// stop-on-event, or condition-based stop (e.g. "stop when ctx.fishCount >= 100"). Manual Stop
/// always works regardless.
/// </summary>
public sealed record RunRequest(
    nint WindowHandle,
    string ProfileId,
    string MainName,
    string TemplateRoot,
    StopWhenOptions? StopWhen = null);

/// <summary>
/// Auto-stop conditions evaluated during a run. All fields optional and combined with OR —
/// the run stops when ANY condition triggers. Manual Stop overrides everything.
/// </summary>
public sealed record StopWhenOptions(
    /// <summary>Stop after this many ms have elapsed since Start.</summary>
    int? TimeoutMs = null,
    /// <summary>Stop when a brickbot event with this name fires (any payload).</summary>
    string? OnEvent = null,
    /// <summary>ctx-based stop: stop when ctx[<see cref="CtxKey"/>] <see cref="CtxOp"/> <see cref="CtxValue"/>.</summary>
    string? CtxKey = null,
    /// <summary>"eq" / "neq" / "gte" / "lte" / "gt" / "lt". Numeric comparisons coerce strings via TryParse.</summary>
    string? CtxOp = null,
    /// <summary>Right-hand side of the ctx comparison. Strings round-trip; numeric ops parse via double.TryParse.</summary>
    string? CtxValue = null);
