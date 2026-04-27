using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Detection.Entities;
using BrickBot.Modules.Detection.Models;
using OpenCvSharp;

namespace BrickBot.Modules.Detection.Services;

public sealed class TrainingSampleService : ITrainingSampleService
{
    private readonly ITrainingSampleRepository _repository;
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;

    public TrainingSampleService(
        ITrainingSampleRepository repository,
        IGlobalPathService globalPaths,
        ILogHelper logger)
    {
        _repository = repository;
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TrainingSampleInfo>> SaveBatchAsync(
        string profileId, string detectionId, IEnumerable<NewTrainingSample> samples, bool replaceExisting)
    {
        if (string.IsNullOrWhiteSpace(detectionId))
        {
            throw new OperationException("DETECTION_TRAIN_DETECTION_REQUIRED");
        }

        if (replaceExisting)
        {
            // Delete prior samples + their image files. Wizard finished → start fresh.
            var prior = await _repository.ListByDetectionAsync(profileId, detectionId).ConfigureAwait(false);
            foreach (var p in prior)
            {
                var path = GetImagePath(profileId, p.Id);
                if (File.Exists(path)) File.Delete(path);
            }
            await _repository.DeleteByDetectionAsync(profileId, detectionId).ConfigureAwait(false);
        }

        var dir = GetTrainingDirectory(profileId);
        Directory.CreateDirectory(dir);

        var saved = new List<TrainingSampleInfo>();
        foreach (var s in samples)
        {
            if (string.IsNullOrEmpty(s.ImageBase64)) continue;
            var bytes = Convert.FromBase64String(s.ImageBase64);
            using var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (mat.Empty())
            {
                _logger.Warn($"Skipping training sample with undecodable image ({bytes.Length} bytes)", "TrainingSample");
                continue;
            }

            var entity = new TrainingSampleEntity
            {
                Id = string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
                DetectionId = detectionId,
                Label = s.Label,
                Note = s.Note,
                Width = mat.Width,
                Height = mat.Height,
                CapturedAt = DateTime.UtcNow,
            };

            await File.WriteAllBytesAsync(GetImagePath(profileId, entity.Id), bytes).ConfigureAwait(false);
            await _repository.UpsertAsync(profileId, entity).ConfigureAwait(false);

            saved.Add(ToInfo(entity, includeImage: false, profileId));
        }
        _logger.Info($"Saved {saved.Count} training samples for detection {detectionId}", "TrainingSample");
        return saved;
    }

    public async Task<IReadOnlyList<TrainingSampleInfo>> ListAsync(string profileId, string detectionId, bool includeImages)
    {
        var rows = await _repository.ListByDetectionAsync(profileId, detectionId).ConfigureAwait(false);
        var results = new List<TrainingSampleInfo>(rows.Count);
        foreach (var row in rows)
        {
            results.Add(ToInfo(row, includeImages, profileId));
        }
        return results;
    }

    public async Task DeleteAsync(string profileId, string sampleId)
    {
        var path = GetImagePath(profileId, sampleId);
        if (File.Exists(path)) File.Delete(path);
        await _repository.DeleteAsync(profileId, sampleId).ConfigureAwait(false);
    }

    public async Task DeleteAllForDetectionAsync(string profileId, string detectionId)
    {
        var prior = await _repository.ListByDetectionAsync(profileId, detectionId).ConfigureAwait(false);
        foreach (var p in prior)
        {
            var path = GetImagePath(profileId, p.Id);
            if (File.Exists(path)) File.Delete(path);
        }
        await _repository.DeleteByDetectionAsync(profileId, detectionId).ConfigureAwait(false);
    }

    private TrainingSampleInfo ToInfo(TrainingSampleEntity e, bool includeImage, string profileId)
    {
        string? base64 = null;
        if (includeImage)
        {
            var path = GetImagePath(profileId, e.Id);
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                base64 = Convert.ToBase64String(bytes);
            }
        }
        return new TrainingSampleInfo(
            Id: e.Id,
            DetectionId: e.DetectionId,
            Label: e.Label,
            Note: e.Note,
            Width: e.Width,
            Height: e.Height,
            CapturedAt: new DateTimeOffset(DateTime.SpecifyKind(e.CapturedAt, DateTimeKind.Utc)),
            ImageBase64: base64);
    }

    private string GetTrainingDirectory(string profileId) =>
        Path.Combine(_globalPaths.GetProfileDirectoryPath(profileId), "training");

    private string GetImagePath(string profileId, string sampleId) =>
        Path.Combine(GetTrainingDirectory(profileId), $"{sampleId}.png");
}
