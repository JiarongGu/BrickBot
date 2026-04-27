namespace BrickBot.Modules.Profile.Models;

public sealed class CreateProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? GameName { get; set; }
}

public sealed class UpdateProfileRequest
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? GameName { get; set; }
    public string? Thumbnail { get; set; }
}

public sealed class ProfileListResponse
{
    public List<Profile> Profiles { get; set; } = new();
    public string ActiveProfileId { get; set; } = string.Empty;
}

/// <summary>
/// On-disk index file at data/settings/profiles.json. Owns the profile list + active pointer.
/// </summary>
public sealed class ProfileIndex
{
    public List<Profile> Profiles { get; set; } = new();
    public string? ActiveProfileId { get; set; }
}
