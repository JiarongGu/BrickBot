using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Generates a <see cref="DetectionDefinition"/> from a set of labeled samples. The first
/// supported kind is <c>progressBar</c> — the trainer aligns the bar across samples, picks
/// the dominant fill color from the highest-fill sample, derives a tolerance from the
/// per-sample variance, infers the fill direction by comparing where fill ends across labels,
/// and tunes <c>lineThreshold</c> by minimizing prediction error vs. labels.
/// </summary>
public interface IDetectionTrainerService
{
    /// <summary>
    /// Train a detection of the given <paramref name="kind"/> from labeled <paramref name="samples"/>.
    /// Returns the suggested definition + per-sample diagnostics. <paramref name="seed"/> is an
    /// existing definition the trainer should refine (e.g. user already picked the bar template).
    /// </summary>
    TrainingResult Train(string profileId, DetectionKind kind, TrainingSample[] samples, DetectionDefinition? seed);

    /// <summary>
    /// Analyze a multi-frame recording — returns one or more ROI candidates ranked by variance
    /// (areas that change between frames, suggesting an animated / dynamic UI element). Used by
    /// the training wizard's "auto-detect ROI" hint after the user records a play session.
    /// </summary>
    RoiSuggestion[] SuggestRois(string[] framesBase64, int maxResults);
}

/// <summary>One candidate ROI from a multi-frame analysis. Includes the bounding box,
/// a 0..1 score (higher = more dynamic), and a short reason string for UI display.</summary>
public sealed class RoiSuggestion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public double Score { get; set; }
    public string Reason { get; set; } = "";
}
