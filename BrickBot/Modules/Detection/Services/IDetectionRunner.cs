using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Executes a <see cref="DetectionDefinition"/> against a frame and returns a typed result.
/// Pure C# — no JS boundary, no IPC. Both the script-side <c>HostApi</c> and the IPC
/// <c>DETECTION.TEST</c> endpoint go through this so live preview matches run-time behavior.
///
/// The runner needs a <see cref="DetectionModel"/> for tracker / pattern (init frame + descriptors).
/// Text + bar can run from the definition alone, but a model is still required as the "trained"
/// signal — untrained detections refuse to run.
/// </summary>
public interface IDetectionRunner
{
    /// <summary>Run a saved detection. The model is loaded from <see cref="IDetectionModelStore"/>;
    /// throws DETECTION_MODEL_MISSING if the detection has not been trained.</summary>
    DetectionResult Run(string profileId, DetectionDefinition definition, CaptureFrame frame);

    /// <summary>Run with a caller-supplied model — used by the editor's live preview to test
    /// a trained-but-not-yet-saved candidate, and by the trainer's diagnostic loop. The model
    /// kind must match the definition kind.</summary>
    DetectionResult RunWithModel(string profileId, DetectionDefinition definition, DetectionModel model, CaptureFrame frame);

    /// <summary>Drop any per-detection state (trackers, last-results) — call between runs.</summary>
    void Reset();
}
