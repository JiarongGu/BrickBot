namespace BrickBot.Modules.Template.Models;

/// <summary>Public DTO returned by template IPC + script-host APIs. The on-disk filename
/// is <c>{Id}.png</c> regardless of <see cref="Name"/> so users can rename freely.</summary>
public sealed record TemplateInfo(
    string Id,
    string Name,
    string? Description,
    int Width,
    int Height,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
