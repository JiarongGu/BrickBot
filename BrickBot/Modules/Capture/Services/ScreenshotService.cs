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

    public ScreenshotResult GrabPng(nint windowHandle, int maxDimension = 0)
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
        var image = frame.Image;
        Mat? scaled = null;

        try
        {
            // Optional uniform downscale when the longest side exceeds the cap. Keeps the
            // PNG payload + frontend canvas paint cheap on 4K monitors. INTER_AREA is the
            // right kernel for shrinking.
            if (maxDimension > 0)
            {
                var longest = Math.Max(frame.Width, frame.Height);
                if (longest > maxDimension)
                {
                    var scale = (double)maxDimension / longest;
                    scaled = new Mat();
                    Cv2.Resize(image, scaled, new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
                    image = scaled;
                }
            }

            if (!Cv2.ImEncode(".png", image, out var bytes))
            {
                throw new OperationException("CAPTURE_ENCODE_FAILED");
            }

            return new ScreenshotResult(bytes, image.Width, image.Height);
        }
        finally
        {
            scaled?.Dispose();
        }
    }
}
