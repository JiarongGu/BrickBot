using BrickBot.Modules.Capture.Models;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Per-run binding the Lua engine reads from when scripts call vision/input/etc.
/// Lifetime: created at run start, disposed at run stop.
/// </summary>
public interface IScriptHost
{
    nint TargetWindowHandle { get; }
    int WindowOriginX { get; }
    int WindowOriginY { get; }
    string TemplateRoot { get; }
    CancellationToken Cancellation { get; }

    void EnsureNotCancelled();
    CaptureFrame GrabFrame();
}
