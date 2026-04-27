using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Profile.Models;
using BrickBot.Modules.Profile.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Profile;

/// <summary>
/// IPC entrypoint for the PROFILE module.
/// Handles: GET_ALL, GET_ACTIVE, GET_BY_ID, CREATE, UPDATE, DELETE, DUPLICATE, SWITCH,
/// GET_CONFIG, UPDATE_CONFIG, CLEAR_TEMP, CREATE_SCRATCH_FOLDER.
/// </summary>
public sealed class ProfileFacade : BaseFacade
{
    private readonly IProfileService _profileService;
    private readonly IProfileTempService _tempService;
    private readonly PayloadHelper _payloadHelper;

    public ProfileFacade(
        IProfileService profileService,
        IProfileTempService tempService,
        PayloadHelper payloadHelper,
        ILogger<ProfileFacade> logger) : base(logger)
    {
        _profileService = profileService;
        _tempService = tempService;
        _payloadHelper = payloadHelper;
    }

    protected override async Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "GET_ALL" => await GetAllAsync().ConfigureAwait(false),
            "GET_ACTIVE" => await _profileService.GetActiveProfileAsync().ConfigureAwait(false),
            "GET_BY_ID" => await _profileService.GetProfileByIdAsync(_payloadHelper.GetRequiredValue<string>(request.Payload, "id")).ConfigureAwait(false),

            "CREATE" => await _profileService.CreateProfileAsync(_payloadHelper.GetRequiredValue<CreateProfileRequest>(request.Payload, "request")).ConfigureAwait(false),
            "UPDATE" => new { success = await _profileService.UpdateProfileAsync(_payloadHelper.GetRequiredValue<UpdateProfileRequest>(request.Payload, "request")).ConfigureAwait(false) },
            "DELETE" => new { success = await _profileService.DeleteProfileAsync(_payloadHelper.GetRequiredValue<string>(request.Payload, "id")).ConfigureAwait(false) },
            "SWITCH" => new { success = await _profileService.SwitchProfileAsync(_payloadHelper.GetRequiredValue<string>(request.Payload, "id")).ConfigureAwait(false) },
            "DUPLICATE" => await _profileService.DuplicateProfileAsync(
                _payloadHelper.GetRequiredValue<string>(request.Payload, "sourceId"),
                _payloadHelper.GetRequiredValue<string>(request.Payload, "newName")).ConfigureAwait(false),

            "GET_CONFIG" => await _profileService.GetProfileConfigurationAsync(_payloadHelper.GetRequiredValue<string>(request.Payload, "id")).ConfigureAwait(false),
            "UPDATE_CONFIG" => new { success = await _profileService.UpdateProfileConfigurationAsync(_payloadHelper.GetRequiredValue<ProfileConfiguration>(request.Payload, "config")).ConfigureAwait(false) },

            "CLEAR_TEMP" => await ClearTempAsync(request).ConfigureAwait(false),
            "CREATE_SCRATCH_FOLDER" => new { path = _tempService.CreateScratchFolder(
                _payloadHelper.GetRequiredValue<string>(request.Payload, "id"),
                _payloadHelper.GetOptionalValue<string>(request.Payload, "prefix")) },

            _ => throw new InvalidOperationException($"Unknown PROFILE message type: {request.Type}"),
        };
    }

    private async Task<ProfileListResponse> GetAllAsync()
    {
        var profiles = await _profileService.GetAllProfilesAsync().ConfigureAwait(false);
        var active = await _profileService.GetActiveProfileAsync().ConfigureAwait(false);
        return new ProfileListResponse
        {
            Profiles = profiles,
            ActiveProfileId = active?.Id ?? string.Empty,
        };
    }

    private async Task<object> ClearTempAsync(IpcRequest request)
    {
        var id = _payloadHelper.GetRequiredValue<string>(request.Payload, "id");
        await _tempService.ClearTempAsync(id).ConfigureAwait(false);
        return new { success = true, id };
    }
}
