using BrickBot.Modules.Capture.Models;

namespace BrickBot.Modules.Capture.Services;

public interface ICaptureService
{
    /// <summary>Grab a single frame from the given window. Caller owns the returned frame.</summary>
    CaptureFrame Grab(nint windowHandle);
}
