using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Recording.Models;
using BrickBot.Modules.Recording.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Recording;

/// <summary>
/// IPC for the per-profile Recording store.
/// Types:
///   LIST              { profileId } → RecordingInfo[]
///   GET               { profileId, id } → RecordingInfo | null
///   CREATE            { profileId, name, description?, windowTitle?, intervalMs?, frames: NewRecordingFrame[] } → RecordingInfo
///   UPDATE_METADATA   { profileId, id, name, description? } → RecordingInfo
///   DELETE            { profileId, id } → { success }
///   LIST_FRAMES       { profileId, recordingId } → RecordingFrameInfo[]  (no images)
///   GET_FRAME         { profileId, recordingId, frameIndex } → RecordingFrameInfo (with image)
/// </summary>
public sealed class RecordingFacade : BaseFacade
{
    private readonly IRecordingService _service;
    private readonly PayloadHelper _payload;

    public RecordingFacade(IRecordingService service, PayloadHelper payload, ILogger<RecordingFacade> logger) : base(logger)
    {
        _service = service;
        _payload = payload;
    }

    protected override async Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "LIST" => await ListAsync(request).ConfigureAwait(false),
            "GET" => await GetAsync(request).ConfigureAwait(false),
            "CREATE" => await CreateAsync(request).ConfigureAwait(false),
            "UPDATE_METADATA" => await UpdateMetadataAsync(request).ConfigureAwait(false),
            "DELETE" => await DeleteAsync(request).ConfigureAwait(false),
            "LIST_FRAMES" => await ListFramesAsync(request).ConfigureAwait(false),
            "GET_FRAME" => await GetFrameAsync(request).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown RECORDING request type: {request.Type}"),
        };
    }

    private async Task<object> ListAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        return new { recordings = await _service.ListAsync(profileId).ConfigureAwait(false) };
    }

    private async Task<object?> GetAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetRequiredValue<string>(request.Payload, "id");
        return await _service.GetAsync(profileId, id).ConfigureAwait(false);
    }

    private async Task<object> CreateAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        var description = _payload.GetOptionalValue<string>(request.Payload, "description");
        var windowTitle = _payload.GetOptionalValue<string>(request.Payload, "windowTitle");
        var intervalMs = _payload.GetOptionalValue<int?>(request.Payload, "intervalMs") ?? 0;
        var frames = _payload.GetRequiredValue<NewRecordingFrame[]>(request.Payload, "frames");
        return await _service.CreateAsync(profileId, name, description, windowTitle, intervalMs, frames).ConfigureAwait(false);
    }

    private async Task<object> UpdateMetadataAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetRequiredValue<string>(request.Payload, "id");
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        var description = _payload.GetOptionalValue<string>(request.Payload, "description");
        return await _service.UpdateMetadataAsync(profileId, id, name, description).ConfigureAwait(false);
    }

    private async Task<object> DeleteAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetRequiredValue<string>(request.Payload, "id");
        await _service.DeleteAsync(profileId, id).ConfigureAwait(false);
        return new { success = true };
    }

    private async Task<object> ListFramesAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var recordingId = _payload.GetRequiredValue<string>(request.Payload, "recordingId");
        return new { frames = await _service.ListFramesAsync(profileId, recordingId).ConfigureAwait(false) };
    }

    private async Task<object?> GetFrameAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var recordingId = _payload.GetRequiredValue<string>(request.Payload, "recordingId");
        var frameIndex = _payload.GetRequiredValue<int>(request.Payload, "frameIndex");
        return await _service.GetFrameAsync(profileId, recordingId, frameIndex).ConfigureAwait(false);
    }
}
