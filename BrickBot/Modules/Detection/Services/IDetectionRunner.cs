using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Executes a <see cref="DetectionDefinition"/> against a frame and returns a typed result.
/// Pure C# — no JS boundary, no IPC. Both the script-side <c>HostApi</c> and the IPC
/// <c>DETECTION.TEST</c> endpoint go through this so live preview matches run-time behavior.
/// </summary>
public interface IDetectionRunner
{
    DetectionResult Run(string profileId, DetectionDefinition definition, CaptureFrame frame);

    /// <summary>Drop any per-detection state (effect baselines etc.) — call between runs.</summary>
    void Reset();
}
