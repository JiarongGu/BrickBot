using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Runner.Models;
using BrickBot.Modules.Runner.Services;

namespace BrickBot.Modules.Script.Services;

public sealed class ScriptHost : IScriptHost
{
    private readonly ICaptureService _capture;
    private readonly CancellationTokenSource _cts;
    private int _stopRequested;  // Interlocked flag — first RequestStop call wins

    public string ProfileId { get; }
    public nint TargetWindowHandle { get; }
    public int WindowOriginX { get; }
    public int WindowOriginY { get; }
    public string TemplateRoot { get; }
    public CancellationToken Cancellation => _cts.Token;
    public StopWhenOptions? StopWhen { get; }
    public StopReason StoppedReason { get; private set; } = StopReason.None;
    public string? StoppedDetail { get; private set; }

    public ScriptHost(
        ICaptureService capture,
        string profileId,
        nint targetWindowHandle,
        int windowOriginX,
        int windowOriginY,
        string templateRoot,
        CancellationTokenSource cts,
        StopWhenOptions? stopWhen)
    {
        _capture = capture;
        _cts = cts;
        ProfileId = profileId;
        TargetWindowHandle = targetWindowHandle;
        WindowOriginX = windowOriginX;
        WindowOriginY = windowOriginY;
        TemplateRoot = templateRoot;
        StopWhen = stopWhen;
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

    public void RequestStop(StopReason reason, string? detail)
    {
        // First call wins — second / racing calls leave the original reason intact so the UI
        // doesn't show "stopped due to user click" overwriting "stopped due to timeout".
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0) return;
        StoppedReason = reason;
        StoppedDetail = detail;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { /* run already torn down */ }
    }
}
