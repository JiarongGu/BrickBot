using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Loads and persists <see cref="DetectionModel"/> files at
/// <c>data/profiles/{profileId}/models/{detectionId}.model.json</c>.
///
/// Distinct from <see cref="IDetectionFileService"/>: that one owns runtime config (Definitions
/// table). This one owns trainer output (compiled artifacts on disk). Existence of a model
/// file is the "this detection is trained" signal — the editor uses it to render the
/// trained-vs-untrained badge.
/// </summary>
public interface IDetectionModelStore
{
    /// <summary>Load the model for a detection. Returns null if no model file exists yet
    /// (untrained detection).</summary>
    DetectionModel? Load(string profileId, string detectionId);

    /// <summary>Persist the model. Overwrites any prior model for the same detection.
    /// Stamps <see cref="DetectionModel.TrainedAt"/> on save.</summary>
    void Save(string profileId, DetectionModel model);

    /// <summary>Delete the model file. Called when the detection itself is deleted, or when
    /// the user wants to "untrain" without losing the definition.</summary>
    void Delete(string profileId, string detectionId);

    /// <summary>True when a model file exists. Cheaper than <see cref="Load"/> for badge rendering.</summary>
    bool Exists(string profileId, string detectionId);
}
