using BrickBot.Modules.Core.Exceptions;
using OpenCvSharp;

namespace BrickBot.Modules.Capture.Services;

public sealed class ScreenshotService : IScreenshotService
{
    private readonly ICaptureService _capture;
    private readonly IWindowFinder _windowFinder;

    public ScreenshotService(ICaptureService capture, IWindowFinder windowFinder)
    {
        _capture = capture;
        _windowFinder = windowFinder;
    }

    public ScreenshotResult GrabPng(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new OperationException("CAPTURE_WINDOW_REQUIRED");
        }

        // Validate that the window still exists; the frontend may have a stale handle.
        if (_windowFinder.GetByHandle(windowHandle) is null)
        {
            throw new OperationException("CAPTURE_WINDOW_NOT_FOUND",
                new() { ["handle"] = windowHandle.ToString() });
        }

        using var frame = _capture.Grab(windowHandle);

        // OpenCV captures BGR; PNG encoder handles channel order. Default compression level (3).
        if (!Cv2.ImEncode(".png", frame.Image, out var bytes))
        {
            throw new OperationException("CAPTURE_ENCODE_FAILED");
        }

        return new ScreenshotResult(bytes, frame.Width, frame.Height);
    }
}
