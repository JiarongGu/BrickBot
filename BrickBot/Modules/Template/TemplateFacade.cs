using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Template.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Template;

/// <summary>
/// IPC for managing template metadata + PNGs in the active profile's database.
/// Types:
///   LIST            { profileId } → TemplateInfo[]
///   SAVE            { profileId, id?, name, description?, pngBase64 } → TemplateInfo
///   UPDATE_METADATA { profileId, id, name, description? } → TemplateInfo
///   DELETE          { profileId, id } → { success }
/// SAVE accepts an empty / missing id to create a new row; passing an existing id
/// overwrites the image and metadata in place.
/// </summary>
public sealed class TemplateFacade : BaseFacade
{
    private readonly ITemplateFileService _files;
    private readonly PayloadHelper _payload;

    public TemplateFacade(
        ITemplateFileService files,
        PayloadHelper payload,
        ILogger<TemplateFacade> logger) : base(logger)
    {
        _files = files;
        _payload = payload;
    }

    protected override async Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "LIST" => await ListAsync(request).ConfigureAwait(false),
            "SAVE" => await SaveAsync(request).ConfigureAwait(false),
            "UPDATE_METADATA" => await UpdateMetadataAsync(request).ConfigureAwait(false),
            "DELETE" => await DeleteAsync(request).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown TEMPLATE request type: {request.Type}"),
        };
    }

    private async Task<object> ListAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var templates = await _files.ListAsync(profileId).ConfigureAwait(false);
        return new { templates };
    }

    private async Task<object> SaveAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetOptionalValue<string>(request.Payload, "id") ?? "";
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        var description = _payload.GetOptionalValue<string>(request.Payload, "description");
        var pngBase64 = _payload.GetRequiredValue<string>(request.Payload, "pngBase64");
        var bytes = Convert.FromBase64String(pngBase64);
        var info = await _files.SavePngAsync(profileId, id, name, description, bytes).ConfigureAwait(false);
        return info;
    }

    private async Task<object> UpdateMetadataAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetRequiredValue<string>(request.Payload, "id");
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        var description = _payload.GetOptionalValue<string>(request.Payload, "description");
        return await _files.UpdateMetadataAsync(profileId, id, name, description).ConfigureAwait(false);
    }

    private async Task<object> DeleteAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetRequiredValue<string>(request.Payload, "id");
        await _files.DeleteAsync(profileId, id).ConfigureAwait(false);
        return new { success = true };
    }
}
