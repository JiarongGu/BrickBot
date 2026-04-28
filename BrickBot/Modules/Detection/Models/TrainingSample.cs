namespace BrickBot.Modules.Detection.Models;

/// <summary>
/// One labeled image used to train a detection. Per-kind label semantics:
///   • <see cref="DetectionKind.Tracker"/> — exactly one sample is the init frame; <see cref="ObjectBox"/>
///     is the bbox the tracker is initialized with. <see cref="Label"/> ignored.
///   • <see cref="DetectionKind.Pattern"/> — <see cref="Label"/> = "true" / "false". For positives,
///     <see cref="ObjectBox"/> marks where the object lives in THIS sample (different position per
///     sample is fine — that's the whole point). Negatives have no box.
///   • <see cref="DetectionKind.Text"/> — one sample with <see cref="ObjectBox"/> defines the OCR
///     region. <see cref="Label"/> ignored.
///   • <see cref="DetectionKind.Bar"/> — <see cref="Label"/> = expected fill ratio (0..1).
///     <see cref="ObjectBox"/> marks the bar bbox in THIS sample. Trainer median-aligns across
///     samples to derive the runtime bar bbox.
/// </summary>
public sealed class TrainingSample
{
    public string ImageBase64 { get; set; } = "";

    /// <summary>Kind-dependent label. See class summary for semantics.</summary>
    public string Label { get; set; } = "";

    /// <summary>Per-sample object box — where the object IS in this specific frame. Distinct
    /// from <see cref="DetectionDefinition.Roi"/>, which is the runtime SEARCH area.</summary>
    public DetectionRoi? ObjectBox { get; set; }

    /// <summary>Tracker only: marks this sample as the init frame. Trainer requires exactly
    /// one sample with this flag.</summary>
    public bool IsInit { get; set; }

    public string? Note { get; set; }
}

/// <summary>Result of a training run — the compiled model + per-sample diagnostics.</summary>
public sealed class TrainingResult
{
    /// <summary>Suggested definition (runtime config) — written to the Detections table on save.</summary>
    public DetectionDefinition? Definition { get; set; }

    /// <summary>Compiled model (binary artifacts + training metadata) — written to disk on save.</summary>
    public DetectionModel? Model { get; set; }

    /// <summary>Per-sample evaluation against the trained model (predicted vs. label + IoU).</summary>
    public TrainingDiagnostic[] Diagnostics { get; set; } = Array.Empty<TrainingDiagnostic>();

    /// <summary>Free-form summary the UI shows under the suggestion.</summary>
    public string Summary { get; set; } = "";
}

/// <summary>Per-sample training-time diagnostic. Includes the predicted bbox so the UI can
/// overlay it on the sample image for the user to compare against the labeled object box.</summary>
public sealed class TrainingDiagnostic
{
    public string Label { get; set; } = "";
    public string Predicted { get; set; } = "";

    /// <summary>Kind-specific error metric (0 = perfect, 1 = worst):
    ///   bar → |predicted − labeled| fill ratio.
    ///   pattern → 0 if classification correct, 1 if wrong.
    ///   tracker / text → 0 (one-shot, no error).</summary>
    public double Error { get; set; }

    /// <summary>Predicted bbox (post-training inference) for the diagnostic overlay.
    /// Null when the kind doesn't predict a bbox (text without spatial output).</summary>
    public PredictedBox? PredictedBox { get; set; }

    /// <summary>Intersection-over-union vs the labeled object box (0..1). 0 when no labeled box
    /// or no prediction. Pattern positives only — other kinds ignore this.</summary>
    public double IoU { get; set; }
}

public sealed class PredictedBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
