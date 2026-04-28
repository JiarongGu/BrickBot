using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Trains a detection from labeled samples. Outputs a paired (<see cref="DetectionDefinition"/>,
/// <see cref="DetectionModel"/>) — the definition holds runtime knobs, the model holds compiled
/// artifacts. Both must be saved together via <see cref="IDetectionFileService"/> +
/// <see cref="IDetectionModelStore"/> for the runner to use the trained detection.
/// </summary>
public interface IDetectionTrainerService
{
    /// <summary>
    /// Train a detection of the given <paramref name="kind"/> from labeled <paramref name="samples"/>.
    /// Each sample carries its own <see cref="TrainingSample.ObjectBox"/> — the trainer reads
    /// per-sample boxes (not a single global ROI) so positives can sit at different positions
    /// across frames.
    /// </summary>
    /// <param name="seed">Existing definition the trainer should refine — pulls user-edited
    /// runtime knobs (algorithm, lowe ratio, etc) so they aren't lost on retrain.</param>
    TrainingResult Train(string profileId, DetectionKind kind, TrainingSample[] samples, DetectionDefinition? seed);

    /// <summary>
    /// Analyze a multi-frame recording — returns one or more ROI candidates ranked by variance
    /// (areas that change between frames). Used by the training wizard's "auto-detect dynamic
    /// region" hint after the user records a play session.
    /// </summary>
    RoiSuggestion[] SuggestRois(string[] framesBase64, int maxResults);
}

public sealed class RoiSuggestion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public double Score { get; set; }
    public string Reason { get; set; } = "";
}
