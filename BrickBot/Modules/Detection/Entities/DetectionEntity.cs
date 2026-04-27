namespace BrickBot.Modules.Detection.Entities;

/// <summary>
/// Dapper-mapped row for the Detections table. The full <see cref="Models.DetectionDefinition"/>
/// is serialized to <see cref="DefinitionJson"/> so adding new options doesn't require a schema
/// migration; the indexed columns (<c>Name</c>, <c>Kind</c>, <c>Group</c>, <c>Enabled</c>) drive
/// list/filter queries without parsing JSON.
/// </summary>
public sealed class DetectionEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Group { get; set; }
    public int Enabled { get; set; } = 1;
    public string DefinitionJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
