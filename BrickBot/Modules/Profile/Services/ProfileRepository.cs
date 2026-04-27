using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Core.Utilities;
using BrickBot.Modules.Profile.Models;

namespace BrickBot.Modules.Profile.Services;

/// <summary>
/// Persists the profile list + active pointer to data/settings/profiles.json.
/// Thread-safe via a single semaphore. Reads use a small in-memory cache invalidated on every write.
/// </summary>
public interface IProfileRepository
{
    Task<List<Models.Profile>> GetAllAsync();
    Task<Models.Profile?> GetByIdAsync(string profileId);
    Task<string?> GetActiveProfileIdAsync();
    Task SetActiveProfileIdAsync(string? profileId);
    Task SaveProfileAsync(Models.Profile profile);
    Task DeleteProfileAsync(string profileId);
}

public sealed class ProfileRepository : IProfileRepository
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private ProfileIndex? _cache;

    public ProfileRepository(IGlobalPathService globalPaths, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public async Task<List<Models.Profile>> GetAllAsync()
    {
        var index = await ReadIndexAsync().ConfigureAwait(false);
        return index.Profiles.ToList();
    }

    public async Task<Models.Profile?> GetByIdAsync(string profileId)
    {
        var index = await ReadIndexAsync().ConfigureAwait(false);
        return index.Profiles.FirstOrDefault(p => p.Id == profileId);
    }

    public async Task<string?> GetActiveProfileIdAsync()
    {
        var index = await ReadIndexAsync().ConfigureAwait(false);
        return index.ActiveProfileId;
    }

    public async Task SetActiveProfileIdAsync(string? profileId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var index = await ReadIndexLockedAsync().ConfigureAwait(false);
            index.ActiveProfileId = profileId;
            await WriteIndexLockedAsync(index).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task SaveProfileAsync(Models.Profile profile)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var index = await ReadIndexLockedAsync().ConfigureAwait(false);
            var existing = index.Profiles.FindIndex(p => p.Id == profile.Id);
            if (existing >= 0) index.Profiles[existing] = profile;
            else index.Profiles.Add(profile);
            await WriteIndexLockedAsync(index).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteProfileAsync(string profileId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var index = await ReadIndexLockedAsync().ConfigureAwait(false);
            index.Profiles.RemoveAll(p => p.Id == profileId);
            if (index.ActiveProfileId == profileId)
            {
                index.ActiveProfileId = index.Profiles.FirstOrDefault()?.Id;
            }
            await WriteIndexLockedAsync(index).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    private async Task<ProfileIndex> ReadIndexAsync()
    {
        if (_cache is not null) return _cache;
        await _lock.WaitAsync().ConfigureAwait(false);
        try { return await ReadIndexLockedAsync().ConfigureAwait(false); }
        finally { _lock.Release(); }
    }

    private async Task<ProfileIndex> ReadIndexLockedAsync()
    {
        if (_cache is not null) return _cache;

        var path = _globalPaths.ProfilesConfigPath;
        if (!File.Exists(path))
        {
            _cache = new ProfileIndex();
            return _cache;
        }

        try
        {
            _cache = await JsonHelper.DeserializeFromFileAsync<ProfileIndex>(path).ConfigureAwait(false)
                ?? new ProfileIndex();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read profile index: {ex.Message}", "ProfileRepository", ex);
            _cache = new ProfileIndex();
        }
        return _cache;
    }

    private async Task WriteIndexLockedAsync(ProfileIndex index)
    {
        await JsonHelper.SerializeToFileAsync(_globalPaths.ProfilesConfigPath, index).ConfigureAwait(false);
        _cache = index;
    }
}
