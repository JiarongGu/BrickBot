using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Runner.Models;
using BrickBot.Modules.Runner.Services;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Per-run binding the Lua engine reads from when scripts call vision/input/etc.
/// Lifetime: created at run start, disposed at run stop.
/// </summary>
public interface IScriptHost
{
    /// <summary>Active profile id — used to resolve per-profile assets (templates, detections).</summary>
    string ProfileId { get; }

    nint TargetWindowHandle { get; }
    int WindowOriginX { get; }
    int WindowOriginY { get; }
    string TemplateRoot { get; }
    CancellationToken Cancellation { get; }

    /// <summary>Auto-stop config the runner / JS-side checks each tick. Null when no conditions set.</summary>
    StopWhenOptions? StopWhen { get; }

    /// <summary>Reason the run is stopping. Set by <see cref="RequestStop"/>; read by the runner
    /// after the engine exits to populate <see cref="RunnerState.StoppedReason"/>.</summary>
    StopReason StoppedReason { get; }

    /// <summary>Free-form detail (e.g. "ctx.fishCount >= 100 (was 102)" / event name / etc.).</summary>
    string? StoppedDetail { get; }

    void EnsureNotCancelled();
    CaptureFrame GrabFrame();

    /// <summary>Trigger graceful shutdown with a reason. Idempotent — first reason wins. Cancels
    /// the run's cancellation token so any waiting <c>wait()</c> / vision call wakes immediately.</summary>
    void RequestStop(StopReason reason, string? detail);
}
