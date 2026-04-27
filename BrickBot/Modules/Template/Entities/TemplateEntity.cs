namespace BrickBot.Modules.Template.Entities;

/// <summary>Dapper-mapped row for the Templates table.</summary>
public sealed class TemplateEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
