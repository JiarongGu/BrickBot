namespace BrickBot.Modules.Detection;

public static class DetectionEvents
{
    /// <summary>Emitted when a detection definition is created or updated.
    /// Payload: <c>DetectionDefinition</c>.</summary>
    public const string SAVED = "SAVED";

    /// <summary>Emitted when a detection is deleted. Payload: <c>{ profileId, id }</c>.</summary>
    public const string DELETED = "DELETED";

    /// <summary>Emitted by the runner each time a detection produces a result whose <c>output</c>
    /// has overlay rendering enabled. Payload: <c>DetectionResult</c>. Frontend overlay subscribes.</summary>
    public const string RESULT = "RESULT";

    /// <summary>Emitted when a DetectionModel is saved (training succeeded). Payload:
    /// <c>{ profileId, detectionId, kind, version }</c>. The Detections list refreshes its
    /// "trained" badge in response.</summary>
    public const string MODEL_TRAINED = "MODEL_TRAINED";
}
