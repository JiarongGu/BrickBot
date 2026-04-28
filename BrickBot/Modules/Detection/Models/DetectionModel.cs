namespace BrickBot.Modules.Detection.Models;

/// <summary>
/// Compiled output of training. One file per detection at
/// <c>data/profiles/{profileId}/models/{detectionId}.model.json</c>.
///
/// <para>Separation of concerns:</para>
/// <list type="bullet">
///   <item><see cref="DetectionDefinition"/> = user-editable config (kind, name, search ROI,
///   tunables, output bindings). Lives in the SQLite Detections table.</item>
///   <item><see cref="DetectionModel"/> = trainer output (binary artifacts + training metadata).
///   Lives as a JSON file on disk. Existence of this file is the "trained" indicator.</item>
///   <item><c>TrainingSample</c> = raw labeled inputs with per-sample object box. Used to
///   re-train and to score diagnostics. SQLite + on-disk PNGs.</item>
/// </list>
///
/// Tracker + Pattern require the model at runtime (init frame / descriptors blob); Text + Bar
/// can run from the definition alone but still publish a Model file so the UI shows them as
/// "trained" and can record training stats.
/// </summary>
public sealed class DetectionModel
{
    /// <summary>Stable id (lowercase letters/digits/_/-). Mirrors <see cref="DetectionId"/>
    /// — kept as a separate field so the model file is self-identifying when copied around.</summary>
    public string Id { get; set; } = "";

    /// <summary>FK to <see cref="DetectionDefinition.Id"/>. The runner uses this to load the
    /// model when it sees a definition.</summary>
    public string DetectionId { get; set; } = "";

    /// <summary>Kind discriminator — must match <see cref="DetectionDefinition.Kind"/>.
    /// Validated on load so a swapped-kind definition can't pick up a stale model file.</summary>
    public DetectionKind Kind { get; set; }

    /// <summary>Bumped on every successful training run. Future migrations may use this to
    /// trigger lazy re-training when the trainer logic changes.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset TrainedAt { get; set; }

    /// <summary>Total number of training samples used.</summary>
    public int SampleCount { get; set; }

    /// <summary>Positives (label = "true" / non-empty for tracker init / fill ratio &gt; 0 etc).
    /// Always equals <see cref="SampleCount"/> for one-shot kinds (tracker/text).</summary>
    public int PositiveCount { get; set; }

    /// <summary>Negatives (label = "false"). Pattern only — other kinds report 0.</summary>
    public int NegativeCount { get; set; }

    /// <summary>Mean prediction error across training samples (kind-specific):
    ///   bar → mean |predicted − labeled| fill ratio.
    ///   pattern → fraction misclassified (0..1).
    ///   tracker / text → 0 (one-shot, no error metric).</summary>
    public double MeanError { get; set; }

    /// <summary>Mean intersection-over-union between predicted bbox and labeled object box.
    ///   pattern → IoU on positive samples (0 = no overlap, 1 = perfect).
    ///   tracker → IoU on init frame (almost always 1).
    ///   bar / text → 0 (no bbox prediction).</summary>
    public double MeanIoU { get; set; }

    /// <summary>Free-form one-line summary the UI shows under the model badge.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Per-kind compiled artifacts. Exactly one is non-null, matching <see cref="Kind"/>.</summary>
    public TrackerModelData? Tracker { get; set; }
    public PatternModelData? Pattern { get; set; }
    public TextModelData? Text { get; set; }
    public BarModelData? Bar { get; set; }
    public CompositeModelData? Composite { get; set; }
}

/// <summary>Composite "model" — pure metadata. The runner pulls operand ids straight off the
/// definition; this block exists so composites carry the standard "trained" badge + summary
/// like every other kind.</summary>
public sealed class CompositeModelData
{
    public CompositeOp Op { get; set; }
    public string[] DetectionIds { get; set; } = Array.Empty<string>();
}

/// <summary>Tracker artifacts: init frame + bbox the OpenCV tracker is .Init()'d with on first run.</summary>
public sealed class TrackerModelData
{
    /// <summary>Base64-encoded PNG of the frame the tracker was initialized on.</summary>
    public string InitFramePng { get; set; } = "";

    public int InitX { get; set; }
    public int InitY { get; set; }
    public int InitW { get; set; }
    public int InitH { get; set; }
}

/// <summary>Pattern artifacts: ORB descriptor blob + reference patch.</summary>
public sealed class PatternModelData
{
    /// <summary>Base64-encoded ORB descriptor matrix (CV_8U N×32 rows). Trainer extracts ORB
    /// keypoints from each positive's per-sample object box, unions the descriptors.</summary>
    public string Descriptors { get; set; } = "";

    /// <summary>Number of trained keypoints (rows in <see cref="Descriptors"/>).</summary>
    public int KeypointCount { get; set; }

    /// <summary>Reference patch dimensions (template size). Used to project matched keypoints
    /// back into a bbox via RANSAC homography at runtime.</summary>
    public int TemplateWidth { get; set; }
    public int TemplateHeight { get; set; }

    /// <summary>Base64-encoded reference patch (cropped from the first positive sample's
    /// object box). Used for re-training and for the editor's "trained model" preview.</summary>
    public string EmbeddedPng { get; set; } = "";
}

/// <summary>Text artifacts: marker only — text training derives no binary blob, all parameters
/// live on <see cref="TextOptions"/>. The model file's existence indicates "user has confirmed
/// the OCR config" so the runner won't refuse to run with default-but-unconfigured options.</summary>
public sealed class TextModelData
{
    /// <summary>Object box on the init sample, in window-relative pixel coords. Snapshot for
    /// the editor's preview overlay.</summary>
    public int BoxX { get; set; }
    public int BoxY { get; set; }
    public int BoxW { get; set; }
    public int BoxH { get; set; }

    /// <summary>Base64-encoded PNG of the init sample (cropped to <see cref="BoxX"/>..H).
    /// Used for the editor's "trained model" preview.</summary>
    public string EmbeddedPng { get; set; } = "";
}

/// <summary>Bar artifacts: snapshot of the trainer-derived parameters. Definition's Bar
/// options carry the live runtime values; model's snapshot lets the editor offer a
/// "reset to trained values" action.</summary>
public sealed class BarModelData
{
    /// <summary>Bar bbox averaged across all training samples (median-aligned).</summary>
    public int BoxX { get; set; }
    public int BoxY { get; set; }
    public int BoxW { get; set; }
    public int BoxH { get; set; }

    public RgbColor FillColor { get; set; } = new(0, 0, 0);
    public int Tolerance { get; set; }
    public BrickBot.Modules.Vision.Models.FillDirection Direction { get; set; }
    public double LineThreshold { get; set; }

    /// <summary>Base64-encoded PNG of the highest-fill sample (cropped to the bar bbox).</summary>
    public string EmbeddedPng { get; set; } = "";
}
