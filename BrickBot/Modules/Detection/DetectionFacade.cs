using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Detection.Models;
using BrickBot.Modules.Detection.Services;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BrickBot.Modules.Detection;

/// <summary>
/// IPC for the per-profile Detection store. Five message types:
///   LIST   { profileId } → DetectionDefinition[]
///   GET    { profileId, id } → DetectionDefinition | null
///   SAVE   { profileId, definition } → DetectionDefinition (with id assigned if blank)
///   DELETE { profileId, id } → { success }
///   TEST   { profileId, definition, frameBase64 } → DetectionResult
/// TEST runs the in-memory definition against a passed PNG so the editor can preview
/// without persisting first.
/// </summary>
public sealed class DetectionFacade : BaseFacade
{
    private readonly IDetectionFileService _files;
    private readonly IDetectionRunner _runner;
    private readonly IDetectionTrainerService _trainer;
    private readonly ITrainingSampleService _samples;
    private readonly IProfileEventBus _eventBus;
    private readonly PayloadHelper _payload;

    public DetectionFacade(
        IDetectionFileService files,
        IDetectionRunner runner,
        IDetectionTrainerService trainer,
        ITrainingSampleService samples,
        IProfileEventBus eventBus,
        PayloadHelper payload,
        ILogger<DetectionFacade> logger) : base(logger)
    {
        _files = files;
        _runner = runner;
        _trainer = trainer;
        _samples = samples;
        _eventBus = eventBus;
        _payload = payload;
    }

    protected override async Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "LIST" => List(request),
            "GET" => Get(request),
            "SAVE" => await SaveAsync(request).ConfigureAwait(false),
            "DELETE" => await DeleteAsync(request).ConfigureAwait(false),
            "TEST" => Test(request),
            "TRAIN" => Train(request),
            "SUGGEST_ROIS" => SuggestRois(request),
            "SAVE_SAMPLES" => await SaveSamplesAsync(request).ConfigureAwait(false),
            "LIST_SAMPLES" => await ListSamplesAsync(request).ConfigureAwait(false),
            "DELETE_SAMPLE" => await DeleteSampleAsync(request).ConfigureAwait(false),
            "DELETE_SAMPLES_FOR_DETECTION" => await DeleteSamplesForDetectionAsync(request).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown DETECTION request type: {request.Type}"),
        };
    }

    private object List(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        return new { detections = _files.List(profileId) };
    }

    private object? Get(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetRequiredValue<string>(request.Payload, "id");
        return _files.Get(profileId, id);
    }

    private async Task<object> SaveAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var def = _payload.GetRequiredValue<DetectionDefinition>(request.Payload, "definition");

        var saved = _files.Save(profileId, def);
        await _eventBus.EmitAsync(ModuleNames.DETECTION, DetectionEvents.SAVED,
            new { profileId, definition = saved }).ConfigureAwait(false);
        return saved;
    }

    private async Task<object> DeleteAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var id = _payload.GetRequiredValue<string>(request.Payload, "id");
        _files.Delete(profileId, id);
        // Cascade: drop training samples + their image files so deleting a detection doesn't
        // leave orphan rows / disk garbage.
        await _samples.DeleteAllForDetectionAsync(profileId, id).ConfigureAwait(false);
        await _eventBus.EmitAsync(ModuleNames.DETECTION, DetectionEvents.DELETED,
            new { profileId, id }).ConfigureAwait(false);
        return new { success = true };
    }

    private object Test(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var def = _payload.GetRequiredValue<DetectionDefinition>(request.Payload, "definition");
        var frameBase64 = _payload.GetRequiredValue<string>(request.Payload, "frameBase64");

        using var frame = DecodeFrame(frameBase64);
        return _runner.Run(profileId, def, frame);
    }

    private object Train(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var kind = _payload.GetRequiredValue<DetectionKind>(request.Payload, "kind");
        var samples = _payload.GetRequiredValue<TrainingSample[]>(request.Payload, "samples");
        var seed = _payload.GetOptionalValue<DetectionDefinition>(request.Payload, "seed");
        return _trainer.Train(profileId, kind, samples, seed);
    }

    private object SuggestRois(IpcRequest request)
    {
        var frames = _payload.GetRequiredValue<string[]>(request.Payload, "frames");
        var maxResults = _payload.GetOptionalValue<int?>(request.Payload, "maxResults") ?? 5;
        var suggestions = _trainer.SuggestRois(frames, maxResults);
        return new { suggestions };
    }

    private async Task<object> SaveSamplesAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var detectionId = _payload.GetRequiredValue<string>(request.Payload, "detectionId");
        var samples = _payload.GetRequiredValue<NewTrainingSample[]>(request.Payload, "samples");
        var replace = _payload.GetOptionalValue<bool?>(request.Payload, "replaceExisting") ?? true;
        var saved = await _samples.SaveBatchAsync(profileId, detectionId, samples, replace).ConfigureAwait(false);
        return new { samples = saved };
    }

    private async Task<object> ListSamplesAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var detectionId = _payload.GetRequiredValue<string>(request.Payload, "detectionId");
        var includeImages = _payload.GetOptionalValue<bool?>(request.Payload, "includeImages") ?? false;
        var samples = await _samples.ListAsync(profileId, detectionId, includeImages).ConfigureAwait(false);
        return new { samples };
    }

    private async Task<object> DeleteSampleAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var sampleId = _payload.GetRequiredValue<string>(request.Payload, "sampleId");
        await _samples.DeleteAsync(profileId, sampleId).ConfigureAwait(false);
        return new { success = true };
    }

    private async Task<object> DeleteSamplesForDetectionAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var detectionId = _payload.GetRequiredValue<string>(request.Payload, "detectionId");
        await _samples.DeleteAllForDetectionAsync(profileId, detectionId).ConfigureAwait(false);
        return new { success = true };
    }

    private static CaptureFrame DecodeFrame(string frameBase64)
    {
        var bytes = Convert.FromBase64String(frameBase64);
        var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty())
        {
            throw new OperationException("VISION_FRAME_DECODE_FAILED",
                new() { ["bytes"] = bytes.Length.ToString() });
        }
        return new CaptureFrame(mat, 0, DateTimeOffset.UtcNow);
    }
}
