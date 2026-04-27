namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Cross-thread bridge between the engine (single-threaded JS tick loop) and the rest
/// of the host. The engine reads queued action invocations on each tick and reports
/// which named actions the running script has registered. The IPC layer (and any other
/// non-engine thread) writes to the queue.
///
/// One instance per Run. Created by <c>RunnerService</c>, disposed when the run ends.
/// All methods are safe to call from any thread.
/// </summary>
public interface IScriptDispatcher
{
    /// <summary>Replace the set of registered action names. Called from the engine thread
    /// when a script calls <c>brickbot.action()</c>; pushes the new list out so the UI
    /// can show "Run action" buttons.</summary>
    void SetRegisteredActions(IReadOnlyList<string> actionNames);

    /// <summary>Snapshot of currently registered action names. Cheap; safe from any thread.</summary>
    IReadOnlyList<string> GetRegisteredActions();

    /// <summary>Schedule a named action to run on the next engine tick.
    /// Throws RUNNER_ACTION_NOT_FOUND if the action is not currently registered.</summary>
    void EnqueueInvocation(string actionName);

    /// <summary>Pull the next pending invocation. Returns null when the queue is empty.</summary>
    string? TryDequeueInvocation();

    /// <summary>Clear registered actions and the invocation queue. Called by the Runner
    /// at Run.Start / Run.End so state from a previous run never leaks into the next.</summary>
    void Reset();
}
