namespace BrickBot.Modules.Capture.Services;

/// <summary>
/// On-demand screenshot helper used by the Capture &amp; Templates UI tooling.
/// Distinct from the streaming capture pipeline — this grabs one frame and PNG-encodes
/// it, so the frontend can preview the live game state, hover for pixel coordinates,
/// and crop a region into a template file.
/// </summary>
public interface IScreenshotService
{
    /// <summary>
    /// Capture one frame of the target window, return PNG bytes + dimensions.
    /// Window-relative — same coordinate space scripts use for vision/input.
    ///
    /// <paramref name="maxDimension"/> caps the longest side of the returned PNG; the image
    /// is uniformly downscaled when either width or height exceeds it. 0 disables the cap.
    /// Use to keep IPC payloads + canvas paint cheap on 4K monitors. Templates saved from
    /// the panel inherit this resolution, so set 0 when pixel-perfect crops matter.
    /// </summary>
    ScreenshotResult GrabPng(nint windowHandle, int maxDimension = 0);
}

public sealed record ScreenshotResult(byte[] PngBytes, int Width, int Height);
