namespace BrickBot.Modules.Detection.Models;

/// <summary>
/// One labeled image used to train a detection. The label's interpretation is kind-dependent:
///   - ProgressBar: <see cref="Label"/> = expected fill ratio (0.0..1.0).
///   - Element / FeatureMatch: <see cref="Label"/> = "true" / "false" (positive vs negative sample).
///   - ColorPresence: <see cref="Label"/> = blob count (integer string).
///   - Effect: <see cref="Label"/> = "trigger" / "quiet".
/// Optional <see cref="Roi"/> narrows the relevant area inside the frame.
/// </summary>
public sealed class TrainingSample
{
    public string ImageBase64 { get; set; } = "";
    public string Label { get; set; } = "";
    public DetectionRoi? Roi { get; set; }
    public string? Note { get; set; }
}

/// <summary>Result of a training run — the suggested definition + per-sample diagnostics.</summary>
public sealed class TrainingResult
{
    public DetectionDefinition? Suggested { get; set; }

    /// <summary>Per-sample evaluation against the suggested config (predicted vs. label).</summary>
    public TrainingDiagnostic[] Diagnostics { get; set; } = Array.Empty<TrainingDiagnostic>();

    /// <summary>Free-form summary the UI shows under the suggestion (what the trainer figured out).</summary>
    public string Summary { get; set; } = "";
}

public sealed class TrainingDiagnostic
{
    public string Label { get; set; } = "";
    public string Predicted { get; set; } = "";
    public double Error { get; set; }
}
