using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Core.Utilities;
using BrickBot.Modules.Profile.Models;

namespace BrickBot.Modules.Profile.Services;

/// <summary>
/// Profile lifecycle: list, create, update, delete, switch active.
/// On first run a default profile is auto-created so the UI always has something to show.
/// </summary>
public interface IProfileService
{
    Task<List<Models.Profile>> GetAllProfilesAsync();
    Task<Models.Profile?> GetActiveProfileAsync();
    Task<Models.Profile?> GetProfileByIdAsync(string profileId);
    Task<Models.Profile> CreateProfileAsync(CreateProfileRequest request);
    Task<bool> UpdateProfileAsync(UpdateProfileRequest request);
    Task<bool> DeleteProfileAsync(string profileId);
    Task<bool> SwitchProfileAsync(string profileId);
    Task<Models.Profile> DuplicateProfileAsync(string sourceProfileId, string newName);

    Task<ProfileConfiguration?> GetProfileConfigurationAsync(string profileId);
    Task<bool> UpdateProfileConfigurationAsync(ProfileConfiguration config);
}

public sealed class ProfileService : IProfileService
{
    private readonly IProfileRepository _repository;
    private readonly IGlobalPathService _globalPaths;
    private readonly IProfileEventBus _eventBus;
    private readonly ILogHelper _logger;
    private readonly Lazy<Task> _init;

    public ProfileService(
        IProfileRepository repository,
        IGlobalPathService globalPaths,
        IProfileEventBus eventBus,
        ILogHelper logger)
    {
        _repository = repository;
        _globalPaths = globalPaths;
        _eventBus = eventBus;
        _logger = logger;
        _init = new Lazy<Task>(EnsureInitialProfileExistsAsync, isThreadSafe: true);
    }

    private Task EnsureInitializedAsync() => _init.Value;

    private async Task EnsureInitialProfileExistsAsync()
    {
        var profiles = await _repository.GetAllAsync().ConfigureAwait(false);
        if (profiles.Count == 0)
        {
            _logger.Info("No profiles found — creating default profile", "ProfileService");
            await CreateProfileInternalAsync(new CreateProfileRequest
            {
                Name = "My Profile",
                Description = "Default profile",
                Color = "#1890ff",
            }, emitEvent: false).ConfigureAwait(false);
        }

        var active = await _repository.GetActiveProfileIdAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(active))
        {
            var first = (await _repository.GetAllAsync().ConfigureAwait(false)).FirstOrDefault();
            if (first is not null)
            {
                await _repository.SetActiveProfileIdAsync(first.Id).ConfigureAwait(false);
            }
        }
    }

    public async Task<List<Models.Profile>> GetAllProfilesAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await _repository.GetAllAsync().ConfigureAwait(false);
    }

    public async Task<Models.Profile?> GetActiveProfileAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var id = await _repository.GetActiveProfileIdAsync().ConfigureAwait(false);
        return id is null ? null : await _repository.GetByIdAsync(id).ConfigureAwait(false);
    }

    public async Task<Models.Profile?> GetProfileByIdAsync(string profileId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await _repository.GetByIdAsync(profileId).ConfigureAwait(false);
    }

    public async Task<Models.Profile> CreateProfileAsync(CreateProfileRequest request)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await CreateProfileInternalAsync(request, emitEvent: true).ConfigureAwait(false);
    }

    private async Task<Models.Profile> CreateProfileInternalAsync(CreateProfileRequest request, bool emitEvent)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new OperationException("PROFILE_NAME_REQUIRED");

        var profile = new Models.Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name.Trim(),
            Description = request.Description,
            Color = request.Color,
            GameName = request.GameName,
        };

        EnsureProfileFolders(profile.Id);

        var config = new ProfileConfiguration { ProfileId = profile.Id };
        await JsonHelper.SerializeToFileAsync(_globalPaths.GetProfileConfigPath(profile.Id), config).ConfigureAwait(false);

        await _repository.SaveProfileAsync(profile).ConfigureAwait(false);

        // First profile becomes active automatically.
        var existingActive = await _repository.GetActiveProfileIdAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(existingActive))
        {
            await _repository.SetActiveProfileIdAsync(profile.Id).ConfigureAwait(false);
        }

        if (emitEvent)
        {
            await _eventBus.EmitAsync(ModuleNames.PROFILE, ProfileEvents.CREATED, profile).ConfigureAwait(false);
        }

        _logger.Info($"Profile created: {profile.Id} ({profile.Name})", "ProfileService");
        return profile;
    }

    public async Task<bool> UpdateProfileAsync(UpdateProfileRequest request)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var profile = await _repository.GetByIdAsync(request.Id).ConfigureAwait(false);
        if (profile is null) return false;

        if (request.Name is not null) profile.Name = request.Name.Trim();
        if (request.Description is not null) profile.Description = request.Description;
        if (request.Color is not null) profile.Color = request.Color;
        if (request.GameName is not null) profile.GameName = request.GameName;
        if (request.Thumbnail is not null) profile.Thumbnail = request.Thumbnail;

        await _repository.SaveProfileAsync(profile).ConfigureAwait(false);
        await _eventBus.EmitAsync(ModuleNames.PROFILE, ProfileEvents.UPDATED, profile).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteProfileAsync(string profileId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var profile = await _repository.GetByIdAsync(profileId).ConfigureAwait(false);
        if (profile is null) return false;

        await _repository.DeleteProfileAsync(profileId).ConfigureAwait(false);

        try
        {
            var dir = _globalPaths.GetProfileDirectoryPath(profileId);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to delete profile folder {profileId}: {ex.Message}", "ProfileService");
        }

        await _eventBus.EmitAsync(ModuleNames.PROFILE, ProfileEvents.DELETED, new { id = profileId }).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SwitchProfileAsync(string profileId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var profile = await _repository.GetByIdAsync(profileId).ConfigureAwait(false);
        if (profile is null) return false;

        await _repository.SetActiveProfileIdAsync(profileId).ConfigureAwait(false);
        await _eventBus.EmitAsync(ModuleNames.PROFILE, ProfileEvents.SWITCHED, profile).ConfigureAwait(false);
        return true;
    }

    public async Task<Models.Profile> DuplicateProfileAsync(string sourceProfileId, string newName)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var source = await _repository.GetByIdAsync(sourceProfileId).ConfigureAwait(false)
            ?? throw new OperationException("PROFILE_NOT_FOUND", new() { ["id"] = sourceProfileId });

        var copy = new Models.Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} (Copy)" : newName.Trim(),
            Description = source.Description,
            Color = source.Color,
            GameName = source.GameName,
        };

        EnsureProfileFolders(copy.Id);
        await _repository.SaveProfileAsync(copy).ConfigureAwait(false);

        // Clone config + scripts + templates dirs
        var srcConfig = await GetProfileConfigurationAsync(source.Id).ConfigureAwait(false) ?? new ProfileConfiguration();
        srcConfig.ProfileId = copy.Id;
        await JsonHelper.SerializeToFileAsync(_globalPaths.GetProfileConfigPath(copy.Id), srcConfig).ConfigureAwait(false);

        TryCopyDirectory(_globalPaths.GetProfileScriptsDirectory(source.Id), _globalPaths.GetProfileScriptsDirectory(copy.Id));
        TryCopyDirectory(_globalPaths.GetProfileTemplatesDirectory(source.Id), _globalPaths.GetProfileTemplatesDirectory(copy.Id));

        await _eventBus.EmitAsync(ModuleNames.PROFILE, ProfileEvents.DUPLICATED, copy).ConfigureAwait(false);
        return copy;
    }

    public async Task<ProfileConfiguration?> GetProfileConfigurationAsync(string profileId)
    {
        var path = _globalPaths.GetProfileConfigPath(profileId);
        if (!File.Exists(path)) return null;
        return await JsonHelper.DeserializeFromFileAsync<ProfileConfiguration>(path).ConfigureAwait(false);
    }

    public async Task<bool> UpdateProfileConfigurationAsync(ProfileConfiguration config)
    {
        if (string.IsNullOrEmpty(config.ProfileId)) return false;

        var profile = await _repository.GetByIdAsync(config.ProfileId).ConfigureAwait(false);
        if (profile is null) return false;

        EnsureProfileFolders(config.ProfileId);
        await JsonHelper.SerializeToFileAsync(_globalPaths.GetProfileConfigPath(config.ProfileId), config).ConfigureAwait(false);
        await _eventBus.EmitAsync(ModuleNames.PROFILE, ProfileEvents.CONFIG_UPDATED, config).ConfigureAwait(false);
        return true;
    }

    private void EnsureProfileFolders(string profileId)
    {
        Directory.CreateDirectory(_globalPaths.GetProfileDirectoryPath(profileId));
        Directory.CreateDirectory(_globalPaths.GetProfileTempDirectory(profileId));
        Directory.CreateDirectory(_globalPaths.GetProfileTemplatesDirectory(profileId));
        Directory.CreateDirectory(_globalPaths.GetProfileScriptsDirectory(profileId));
    }

    private static void TryCopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: true);
        }
    }
}
