using System.Text.Json;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Detection.Models;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// File-backed store. One JSON file per detection at
/// <c>data/profiles/{profileId}/models/{detectionId}.model.json</c>. Binary trainer artifacts
/// (descriptors blob, init frame PNG, ref patch PNG) ride inside the JSON as base64. Keeping
/// them in one file simplifies copy / backup / portability versus a directory-per-detection
/// layout.
/// </summary>
public sealed class DetectionModelStore : IDetectionModelStore
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;
    private readonly JsonSerializerOptions _json;

    public DetectionModelStore(
        IGlobalPathService globalPaths,
        ILogHelper logger,
        JsonSerializerOptions json)
    {
        _globalPaths = globalPaths;
        _logger = logger;
        _json = new JsonSerializerOptions(json)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };
    }

    public DetectionModel? Load(string profileId, string detectionId)
    {
        ValidateId(detectionId);
        var path = GetModelPath(profileId, detectionId);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DetectionModel>(json, _json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Detection model {detectionId} unreadable, treating as untrained: {ex.Message}", "DetectionModel");
            return null;
        }
    }

    public void Save(string profileId, DetectionModel model)
    {
        if (string.IsNullOrWhiteSpace(model.DetectionId))
            throw new OperationException("DETECTION_MODEL_DETECTION_REQUIRED");
        ValidateId(model.DetectionId);
        if (string.IsNullOrEmpty(model.Id)) model.Id = model.DetectionId;
        if (model.TrainedAt == default) model.TrainedAt = DateTimeOffset.UtcNow;

        var dir = GetModelsDirectory(profileId);
        Directory.CreateDirectory(dir);
        var path = GetModelPath(profileId, model.DetectionId);

        // Atomic write via temp + replace so a crashed save doesn't leave a half-written file.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(model, _json));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);

        _logger.Info($"Saved detection model {model.DetectionId} ({model.Kind})", "DetectionModel");
    }

    public void Delete(string profileId, string detectionId)
    {
        ValidateId(detectionId);
        var path = GetModelPath(profileId, detectionId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.Info($"Deleted detection model {detectionId}", "DetectionModel");
        }
    }

    public bool Exists(string profileId, string detectionId)
    {
        ValidateId(detectionId);
        return File.Exists(GetModelPath(profileId, detectionId));
    }

    private string GetModelsDirectory(string profileId) =>
        Path.Combine(_globalPaths.GetProfileDirectoryPath(profileId), "models");

    private string GetModelPath(string profileId, string detectionId) =>
        Path.Combine(GetModelsDirectory(profileId), $"{detectionId}.model.json");

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new OperationException("DETECTION_ID_REQUIRED");
        foreach (var ch in id)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                throw new OperationException("DETECTION_INVALID_ID", new() { ["id"] = id });
        }
    }
}
