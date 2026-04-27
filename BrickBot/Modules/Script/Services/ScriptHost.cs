using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core.Exceptions;

namespace BrickBot.Modules.Script.Services;

public sealed class ScriptHost : IScriptHost
{
    private readonly ICaptureService _capture;

    public nint TargetWindowHandle { get; }
    public int WindowOriginX { get; }
    public int WindowOriginY { get; }
    public string TemplateRoot { get; }
    public CancellationToken Cancellation { get; }

    public ScriptHost(
        ICaptureService capture,
        nint targetWindowHandle,
        int windowOriginX,
        int windowOriginY,
        string templateRoot,
        CancellationToken cancellation)
    {
        _capture = capture;
        TargetWindowHandle = targetWindowHandle;
        WindowOriginX = windowOriginX;
        WindowOriginY = windowOriginY;
        TemplateRoot = templateRoot;
        Cancellation = cancellation;
    }

    public void EnsureNotCancelled()
    {
        if (Cancellation.IsCancellationRequested) throw new OperationCanceledException(Cancellation);
    }

    public CaptureFrame GrabFrame()
    {
        EnsureNotCancelled();
        return _capture.Grab(TargetWindowHandle);
    }
}
