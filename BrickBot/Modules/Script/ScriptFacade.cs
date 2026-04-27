using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Script.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Script;

/// <summary>
/// IPC for managing script files (Main + Library) inside the active profile's scripts/ folder.
/// Types: LIST, GET, SAVE, DELETE.
/// </summary>
public sealed class ScriptFacade : BaseFacade
{
    private readonly IScriptFileService _files;
    private readonly PayloadHelper _payload;

    public ScriptFacade(
        IScriptFileService files,
        PayloadHelper payload,
        ILogger<ScriptFacade> logger) : base(logger)
    {
        _files = files;
        _payload = payload;
    }

    protected override Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "LIST" => Task.FromResult<object?>(List(request)),
            "GET" => Task.FromResult<object?>(Get(request)),
            "SAVE" => Task.FromResult<object?>(Save(request)),
            "DELETE" => Task.FromResult<object?>(Delete(request)),
            _ => throw new InvalidOperationException($"Unknown SCRIPT message type: {request.Type}"),
        };
    }

    private object List(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var files = _files.List(profileId)
            .Select(f => new { kind = f.Kind.ToString().ToLowerInvariant(), name = f.Name })
            .ToList();
        return new { files };
    }

    private object Get(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var kind = ParseKind(_payload.GetRequiredValue<string>(request.Payload, "kind"));
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        return new { source = _files.Read(profileId, kind, name) };
    }

    private object Save(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var kind = ParseKind(_payload.GetRequiredValue<string>(request.Payload, "kind"));
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        var source = _payload.GetRequiredValue<string>(request.Payload, "source");
        var path = _files.Save(profileId, kind, name, source);
        return new { success = true, path };
    }

    private object Delete(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var kind = ParseKind(_payload.GetRequiredValue<string>(request.Payload, "kind"));
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        _files.Delete(profileId, kind, name);
        return new { success = true };
    }

    private static ScriptKind ParseKind(string raw) => raw.ToLowerInvariant() switch
    {
        "main" => ScriptKind.Main,
        "library" => ScriptKind.Library,
        _ => throw new ArgumentException($"Unknown script kind: {raw}"),
    };
}
