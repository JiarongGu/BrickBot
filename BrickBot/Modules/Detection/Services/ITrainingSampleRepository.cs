using BrickBot.Modules.Detection.Entities;

namespace BrickBot.Modules.Detection.Services;

public interface ITrainingSampleRepository
{
    Task<List<TrainingSampleEntity>> ListByDetectionAsync(string profileId, string detectionId);
    Task UpsertAsync(string profileId, TrainingSampleEntity entity);
    Task<bool> DeleteAsync(string profileId, string id);
    Task<int> DeleteByDetectionAsync(string profileId, string detectionId);
}
