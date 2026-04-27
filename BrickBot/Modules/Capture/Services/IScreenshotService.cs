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
    /// </summary>
    ScreenshotResult GrabPng(nint windowHandle);
}

public sealed record ScreenshotResult(byte[] PngBytes, int Width, int Height);
