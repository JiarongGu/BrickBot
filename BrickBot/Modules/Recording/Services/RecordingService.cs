using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Recording.Entities;
using BrickBot.Modules.Recording.Models;
using OpenCvSharp;

namespace BrickBot.Modules.Recording.Services;

public sealed class RecordingService : IRecordingService
{
    private readonly IRecordingRepository _repository;
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;

    public RecordingService(IRecordingRepository repository, IGlobalPathService globalPaths, ILogHelper logger)
    {
        _repository = repository;
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RecordingInfo>> ListAsync(string profileId)
    {
        var rows = await _repository.ListAsync(profileId).ConfigureAwait(false);
        return rows.Select(ToInfo).ToList();
    }

    public async Task<RecordingInfo?> GetAsync(string profileId, string id)
    {
        var row = await _repository.GetByIdAsync(profileId, id).ConfigureAwait(false);
        return row is null ? null : ToInfo(row);
    }

    public async Task<RecordingInfo> CreateAsync(
        string profileId, string name, string? description, string? windowTitle, int intervalMs,
        IEnumerable<NewRecordingFrame> frames)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new OperationException("RECORDING_NAME_REQUIRED");
        var frameList = frames.ToList();
        if (frameList.Count == 0) throw new OperationException("RECORDING_NEEDS_FRAMES");

        var recordingId = Guid.NewGuid().ToString("N");
        var dir = GetRecordingDir(profileId, recordingId);
        Directory.CreateDirectory(dir);

        // Decode all frames upfront to capture dimensions; use the first frame's size as the
        // recording's canonical Width/Height (they should all match in practice).
        var first = true;
        var width = 0;
        var height = 0;
        for (var i = 0; i < frameList.Count; i++)
        {
            var f = frameList[i];
            if (string.IsNullOrEmpty(f.ImageBase64)) continue;
            var bytes = Convert.FromBase64String(f.ImageBase64);
            using var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (mat.Empty())
            {
                _logger.Warn($"Recording {recordingId}: skipping undecodable frame #{i}", "Recording");
                continue;
            }
            if (first) { width = mat.Width; height = mat.Height; first = false; }

            var framePath = Path.Combine(dir, $"{i}.png");
            await File.WriteAllBytesAsync(framePath, bytes).ConfigureAwait(false);

            await _repository.UpsertFrameAsync(profileId, new RecordingFrameEntity
            {
                Id = $"{recordingId}-{i}",
                RecordingId = recordingId,
                FrameIndex = i,
                Width = mat.Width,
                Height = mat.Height,
                CapturedAt = (f.CapturedAt ?? DateTimeOffset.UtcNow).UtcDateTime,
            }).ConfigureAwait(false);
        }

        var entity = new RecordingEntity
        {
            Id = recordingId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle.Trim(),
            Width = width,
            Height = height,
            FrameCount = frameList.Count,
            IntervalMs = intervalMs,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await _repository.UpsertAsync(profileId, entity).ConfigureAwait(false);
        _logger.Info($"Created recording {recordingId} ({name}) with {frameList.Count} frames", "Recording");
        return ToInfo(entity);
    }

    public async Task<RecordingInfo> UpdateMetadataAsync(string profileId, string id, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new OperationException("RECORDING_NAME_REQUIRED");
        var entity = await _repository.GetByIdAsync(profileId, id).ConfigureAwait(false)
            ?? throw new OperationException("RECORDING_NOT_FOUND", new() { ["id"] = id });
        entity.Name = name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await _repository.UpsertAsync(profileId, entity).ConfigureAwait(false);
        return ToInfo(entity);
    }

    public async Task DeleteAsync(string profileId, string id)
    {
        await _repository.DeleteFramesForRecordingAsync(profileId, id).ConfigureAwait(false);
        await _repository.DeleteAsync(profileId, id).ConfigureAwait(false);
        var dir = GetRecordingDir(profileId, id);
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, true); }
            catch (Exception ex) { _logger.Warn($"Failed to delete recording dir {dir}: {ex.Message}", "Recording"); }
        }
    }

    public async Task<IReadOnlyList<RecordingFrameInfo>> ListFramesAsync(string profileId, string recordingId)
    {
        var rows = await _repository.ListFramesAsync(profileId, recordingId).ConfigureAwait(false);
        return rows.Select(r => ToFrameInfo(r, includeImage: false, profileId, recordingId)).ToList();
    }

    public async Task<RecordingFrameInfo?> GetFrameAsync(string profileId, string recordingId, int frameIndex)
    {
        var rows = await _repository.ListFramesAsync(profileId, recordingId).ConfigureAwait(false);
        var row = rows.FirstOrDefault(r => r.FrameIndex == frameIndex);
        if (row is null) return null;
        return ToFrameInfo(row, includeImage: true, profileId, recordingId);
    }

    private RecordingFrameInfo ToFrameInfo(RecordingFrameEntity e, bool includeImage, string profileId, string recordingId)
    {
        string? base64 = null;
        if (includeImage)
        {
            var path = Path.Combine(GetRecordingDir(profileId, recordingId), $"{e.FrameIndex}.png");
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                base64 = Convert.ToBase64String(bytes);
            }
        }
        return new RecordingFrameInfo(
            Id: e.Id,
            FrameIndex: e.FrameIndex,
            Width: e.Width,
            Height: e.Height,
            CapturedAt: new DateTimeOffset(DateTime.SpecifyKind(e.CapturedAt, DateTimeKind.Utc)),
            ImageBase64: base64);
    }

    private static RecordingInfo ToInfo(RecordingEntity e) => new(
        Id: e.Id,
        Name: e.Name,
        Description: e.Description,
        WindowTitle: e.WindowTitle,
        Width: e.Width,
        Height: e.Height,
        FrameCount: e.FrameCount,
        IntervalMs: e.IntervalMs,
        CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc)));

    private string GetRecordingDir(string profileId, string recordingId) =>
        Path.Combine(_globalPaths.GetProfileRecordingsDirectory(profileId), recordingId);
}
