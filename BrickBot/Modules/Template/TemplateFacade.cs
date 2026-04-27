using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Template.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Template;

/// <summary>
/// IPC for managing template PNGs in the active profile's <c>templates/</c> dir.
/// Types: LIST, SAVE (PNG base64 in payload), DELETE.
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

    protected override Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "LIST" => Task.FromResult<object?>(List(request)),
            "SAVE" => Task.FromResult<object?>(Save(request)),
            "DELETE" => Task.FromResult<object?>(Delete(request)),
            _ => throw new InvalidOperationException($"Unknown TEMPLATE request type: {request.Type}"),
        };
    }

    private object List(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        return new { templates = _files.List(profileId) };
    }

    private object Save(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        var pngBase64 = _payload.GetRequiredValue<string>(request.Payload, "pngBase64");
        var bytes = Convert.FromBase64String(pngBase64);
        var path = _files.SavePng(profileId, name, bytes);
        return new { success = true, path };
    }

    private object Delete(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        _files.Delete(profileId, name);
        return new { success = true };
    }
}
