using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// CRUD over the per-profile detections directory at <c>data/profiles/{id}/detections/</c>,
/// one JSON file per definition. Used by <see cref="DetectionFacade"/> for the editor and by
/// <see cref="IDetectionRunner"/> at run time.
/// </summary>
public interface IDetectionFileService
{
    IReadOnlyList<DetectionDefinition> List(string profileId);

    DetectionDefinition? Get(string profileId, string id);

    /// <summary>Save a definition. Generates a safe id from <see cref="DetectionDefinition.Name"/>
    /// when <see cref="DetectionDefinition.Id"/> is empty.</summary>
    DetectionDefinition Save(string profileId, DetectionDefinition definition);

    void Delete(string profileId, string id);
}
