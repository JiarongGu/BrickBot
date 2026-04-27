namespace BrickBot.Modules.Recording.Entities;

public sealed class RecordingEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? WindowTitle { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameCount { get; set; }
    public int IntervalMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class RecordingFrameEntity
{
    public string Id { get; set; } = "";
    public string RecordingId { get; set; } = "";
    public int FrameIndex { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CapturedAt { get; set; }
}
