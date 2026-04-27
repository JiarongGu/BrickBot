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
            // Default cap of 1280px (long edge) when caller passes 0. Pre-fix, a 1920×1080
            // game window encoded at full size produced ~2 MB PNGs; base64 + WebView2
            // PostMessage transfer of that size compounded across multi-frame recording into
            // visible "frames are seconds behind real time" lag. 1280 keeps training-sample
            // fidelity while shrinking the payload ~2-3×. Caller can request 0-bypass if it
            // genuinely needs pixel-perfect full-res (e.g. one-shot template authoring).
            var effectiveCap = maxDimension > 0 ? maxDimension : 1280;
            var longest = Math.Max(frame.Width, frame.Height);
            if (longest > effectiveCap)
            {
                var scale = (double)effectiveCap / longest;
                scaled = new Mat();
                Cv2.Resize(image, scaled, new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
                image = scaled;
            }

            // Fast PNG compression (level 1: LZ77 only, no Huffman tuning). ~5× faster
            // encode vs OpenCV's default for a ~10-15% larger output — net win for live UI
            // capture since transfer cost is dominated by base64 + IPC, not the few extra KB.
            var encodeParams = new[] { (int)ImwriteFlags.PngCompression, 1 };
            if (!Cv2.ImEncode(".png", image, out var bytes, encodeParams))
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
