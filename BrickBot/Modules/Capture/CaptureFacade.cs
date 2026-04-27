using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Capture;

public sealed class CaptureFacade : BaseFacade
{
    private readonly IWindowFinder _windowFinder;
    private readonly IScreenshotService _screenshots;
    private readonly PayloadHelper _payload;

    public CaptureFacade(
        IWindowFinder windowFinder,
        IScreenshotService screenshots,
        PayloadHelper payload,
        ILogger<CaptureFacade> logger) : base(logger)
    {
        _windowFinder = windowFinder;
        _screenshots = screenshots;
        _payload = payload;
    }

    protected override Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "LIST_WINDOWS" => Task.FromResult<object?>(ListWindows()),
            "GRAB_PNG" => Task.FromResult<object?>(GrabPng(request)),
            _ => throw new InvalidOperationException($"Unknown CAPTURE request type: {request.Type}"),
        };
    }

    private IReadOnlyList<WindowInfo> ListWindows() => _windowFinder.ListVisibleWindows();

    private object GrabPng(IpcRequest request)
    {
        var handle = (nint)_payload.GetRequiredValue<long>(request.Payload, "windowHandle");
        var result = _screenshots.GrabPng(handle);
        return new
        {
            pngBase64 = Convert.ToBase64String(result.PngBytes),
            width = result.Width,
            height = result.Height,
        };
    }
}
