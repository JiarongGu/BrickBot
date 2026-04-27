namespace BrickBot.Modules.Profile.Models;

/// <summary>
/// A saved game-automation target. Each profile owns:
/// - a folder at data/profiles/{id}/ with config.json, scripts/, templates/, temp/
/// - its own ProfileConfiguration (window match, capture settings, script + template paths)
/// </summary>
public sealed class Profile
{
    /// <summary>Stable unique id (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name shown in the profile picker.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional human description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional UI color tag (hex, e.g. "#1890ff").</summary>
    public string? Color { get; set; }

    /// <summary>Optional game/window name this profile targets (free text, used in UI).</summary>
    public string? GameName { get; set; }

    /// <summary>Optional thumbnail path (relative to profile dir).</summary>
    public string? Thumbnail { get; set; }
}
