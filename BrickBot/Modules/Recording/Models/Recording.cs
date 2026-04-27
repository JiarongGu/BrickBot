namespace BrickBot.Modules.Recording.Models;

/// <summary>
/// A reusable multi-frame capture. Created once via the Recording UI; consumed by many
/// detection trainings. Frames live at <c>data/profiles/{id}/recordings/{Id}/frame-{n}.png</c>;
/// metadata (name, description, dimensions) lives in the Recordings table.
/// </summary>
public sealed record RecordingInfo(
    string Id,
    string Name,
    string? Description,
    string? WindowTitle,
    int Width,
    int Height,
    int FrameCount,
    int IntervalMs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RecordingFrameInfo(
    string Id,
    int FrameIndex,
    int Width,
    int Height,
    DateTimeOffset CapturedAt,
    string? ImageBase64);

/// <summary>Inputs for SaveAsync. Frame bytes required; per-frame timestamps optional.</summary>
public sealed class NewRecordingFrame
{
    public string ImageBase64 { get; set; } = "";
    public DateTimeOffset? CapturedAt { get; set; }
}
