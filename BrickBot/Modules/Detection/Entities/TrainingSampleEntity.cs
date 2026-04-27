namespace BrickBot.Modules.Detection.Entities;

/// <summary>Dapper-mapped row for the TrainingSamples table. Image bytes live on disk at
/// <c>data/profiles/{profileId}/training/{Id}.png</c>; this row carries the label, note,
/// dimensions, and captured-at timestamp.</summary>
public sealed class TrainingSampleEntity
{
    public string Id { get; set; } = "";
    public string DetectionId { get; set; } = "";
    public string? Label { get; set; }
    public string? Note { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CapturedAt { get; set; }
}
