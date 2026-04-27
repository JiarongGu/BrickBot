using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;

namespace BrickBot.Modules.Profile.Services;

/// <summary>
/// Lifecycle of per-profile temp folders at data/profiles/{id}/temp/.
/// Used for capture buffers, script-emitted artifacts, screenshot scratch, etc.
/// </summary>
public interface IProfileTempService
{
    /// <summary>Returns the absolute path, creating the folder if missing.</summary>
    string GetOrCreateTempDirectory(string profileId);

    /// <summary>Recursively deletes everything under the profile's temp folder.</summary>
    Task ClearTempAsync(string profileId);

    /// <summary>Reserve a fresh subfolder under temp/ (named with a timestamp + GUID).</summary>
    string CreateScratchFolder(string profileId, string? prefix = null);
}

public sealed class ProfileTempService : IProfileTempService
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;

    public ProfileTempService(IGlobalPathService globalPaths, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public string GetOrCreateTempDirectory(string profileId)
    {
        var path = _globalPaths.GetProfileTempDirectory(profileId);
        Directory.CreateDirectory(path);
        return path;
    }

    public Task ClearTempAsync(string profileId)
    {
        var path = _globalPaths.GetProfileTempDirectory(profileId);
        if (!Directory.Exists(path)) return Task.CompletedTask;

        try
        {
            // Clear contents but keep the folder itself.
            foreach (var sub in Directory.EnumerateDirectories(path))
            {
                Directory.Delete(sub, recursive: true);
            }
            foreach (var file in Directory.EnumerateFiles(path))
            {
                File.Delete(file);
            }
            _logger.Info($"Cleared temp folder for profile {profileId}", "ProfileTemp");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to clear temp for {profileId}: {ex.Message}", "ProfileTemp");
        }
        return Task.CompletedTask;
    }

    public string CreateScratchFolder(string profileId, string? prefix = null)
    {
        var root = GetOrCreateTempDirectory(profileId);
        var name = $"{prefix ?? "scratch"}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
