using BrickBot.Modules.Recording.Models;

namespace BrickBot.Modules.Recording.Services;

/// <summary>
/// High-level recording API: combines DB metadata with on-disk frame storage. Recordings
/// are an addressable asset — create once, reference many times from training.
/// </summary>
public interface IRecordingService
{
    Task<IReadOnlyList<RecordingInfo>> ListAsync(string profileId);

    Task<RecordingInfo?> GetAsync(string profileId, string id);

    Task<RecordingInfo> CreateAsync(
        string profileId,
        string name,
        string? description,
        string? windowTitle,
        int intervalMs,
        IEnumerable<NewRecordingFrame> frames);

    Task<RecordingInfo> UpdateMetadataAsync(string profileId, string id, string name, string? description);

    Task DeleteAsync(string profileId, string id);

    /// <summary>List frame metadata (no images). Cheap; used by the Recordings tab list.</summary>
    Task<IReadOnlyList<RecordingFrameInfo>> ListFramesAsync(string profileId, string recordingId);

    /// <summary>Load a single frame's image as base64. Used by the player + by training when
    /// pulling frames into the wizard.</summary>
    Task<RecordingFrameInfo?> GetFrameAsync(string profileId, string recordingId, int frameIndex);
}
