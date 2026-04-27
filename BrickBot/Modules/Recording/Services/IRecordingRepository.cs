using BrickBot.Modules.Recording.Entities;

namespace BrickBot.Modules.Recording.Services;

public interface IRecordingRepository
{
    Task<List<RecordingEntity>> ListAsync(string profileId);
    Task<RecordingEntity?> GetByIdAsync(string profileId, string id);
    Task UpsertAsync(string profileId, RecordingEntity entity);
    Task<bool> DeleteAsync(string profileId, string id);
    Task<List<RecordingFrameEntity>> ListFramesAsync(string profileId, string recordingId);
    Task UpsertFrameAsync(string profileId, RecordingFrameEntity entity);
    Task DeleteFramesForRecordingAsync(string profileId, string recordingId);
}
