using System.Collections.Concurrent;
using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Exceptions;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Default implementation. Holds the registered-actions snapshot in a volatile reference
/// (always replaced as a whole, never mutated in place) and a lock-free invocation queue.
/// Emits <c>SCRIPT.ACTIONS_CHANGED</c> on the profile event bus whenever the registered
/// list changes so the UI's Tools tab can update without polling.
/// </summary>
public sealed class ScriptDispatcher : IScriptDispatcher
{
    private readonly IProfileEventBus _eventBus;
    private readonly ConcurrentQueue<string> _pending = new();
    private volatile IReadOnlyList<string> _registered = Array.Empty<string>();

    public ScriptDispatcher(IProfileEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void SetRegisteredActions(IReadOnlyList<string> actionNames)
    {
        // Defensive copy — caller may keep mutating its own list.
        var snapshot = actionNames.ToArray();
        _registered = snapshot;
        _ = _eventBus.EmitAsync(ModuleNames.SCRIPT, ScriptEvents.ACTIONS_CHANGED,
            new { actions = snapshot });
    }

    public IReadOnlyList<string> GetRegisteredActions() => _registered;

    public void EnqueueInvocation(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new OperationException("RUNNER_ACTION_NOT_FOUND",
                new() { ["name"] = actionName ?? "(empty)" });
        }
        if (!_registered.Contains(actionName, StringComparer.Ordinal))
        {
            throw new OperationException("RUNNER_ACTION_NOT_FOUND",
                new() { ["name"] = actionName });
        }
        _pending.Enqueue(actionName);
    }

    public string? TryDequeueInvocation() =>
        _pending.TryDequeue(out var name) ? name : null;

    public void Reset()
    {
        while (_pending.TryDequeue(out _)) { }
        var hadActions = _registered.Count > 0;
        _registered = Array.Empty<string>();
        if (hadActions)
        {
            _ = _eventBus.EmitAsync(ModuleNames.SCRIPT, ScriptEvents.ACTIONS_CHANGED,
                new { actions = Array.Empty<string>() });
        }
    }
}
