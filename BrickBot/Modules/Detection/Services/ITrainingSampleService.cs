using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Combines the database repository with the on-disk image storage at
/// <c>data/profiles/{profileId}/training/{sampleId}.png</c>. This is the service
/// the IPC facade calls — repository alone doesn't know about the image bytes.
/// </summary>
public interface ITrainingSampleService
{
    /// <summary>Persist a batch of samples for a detection. Pass <paramref name="replaceExisting"/>
    /// = true to delete all prior samples for the detection first (the typical "wizard finished"
    /// case — start fresh). Each sample's <c>Id</c> is generated if blank.</summary>
    Task<IReadOnlyList<TrainingSampleInfo>> SaveBatchAsync(
        string profileId, string detectionId, IEnumerable<NewTrainingSample> samples, bool replaceExisting);

    Task<IReadOnlyList<TrainingSampleInfo>> ListAsync(string profileId, string detectionId, bool includeImages);

    Task DeleteAsync(string profileId, string sampleId);

    Task DeleteAllForDetectionAsync(string profileId, string detectionId);
}

/// <summary>Input shape for SaveBatchAsync — image bytes are required for new samples.</summary>
public sealed class NewTrainingSample
{
    public string? Id { get; set; }
    public string ImageBase64 { get; set; } = "";
    public string? Label { get; set; }
    public string? Note { get; set; }

    /// <summary>Per-sample object box — see <see cref="BrickBot.Modules.Detection.Models.TrainingSample.ObjectBox"/>.</summary>
    public BrickBot.Modules.Detection.Models.DetectionRoi? ObjectBox { get; set; }

    /// <summary>Tracker init-frame flag — see <see cref="BrickBot.Modules.Detection.Models.TrainingSample.IsInit"/>.</summary>
    public bool IsInit { get; set; }
}
