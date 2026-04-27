namespace BrickBot.Modules.Detection.Models;

/// <summary>Public DTO for a persisted training sample, returned by the LIST_SAMPLES IPC.
/// The image is fetched separately as base64 only when the editor needs it, so list ops
/// stay cheap (just metadata).</summary>
public sealed record TrainingSampleInfo(
    string Id,
    string DetectionId,
    string? Label,
    string? Note,
    int Width,
    int Height,
    DateTimeOffset CapturedAt,
    string? ImageBase64);
