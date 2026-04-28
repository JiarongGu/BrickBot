namespace BrickBot.Modules.Detection.Entities;

/// <summary>Dapper-mapped row for the TrainingSamples table. Image bytes live on disk at
/// <c>data/profiles/{profileId}/training/{Id}.png</c>; this row carries the label, note,
/// dimensions, captured-at timestamp, per-sample object box (JSON), and the init-frame
/// flag for tracker training.</summary>
public sealed class TrainingSampleEntity
{
    public string Id { get; set; } = "";
    public string DetectionId { get; set; } = "";
    public string? Label { get; set; }
    public string? Note { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CapturedAt { get; set; }

    /// <summary>JSON-encoded <c>{ x, y, w, h }</c>. Null = sample has no object annotation
    /// (e.g. pattern negatives).</summary>
    public string? ObjectBoxJson { get; set; }

    /// <summary>Tracker only: flags the sample as the init frame.</summary>
    public int IsInit { get; set; }
}
