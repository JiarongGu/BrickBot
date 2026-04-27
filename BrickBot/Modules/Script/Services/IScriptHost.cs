using BrickBot.Modules.Capture.Models;

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

    void EnsureNotCancelled();
    CaptureFrame GrabFrame();
}
