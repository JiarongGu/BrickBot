namespace BrickBot.Modules.Detection.Models;

/// <summary>
/// User-editable configuration for a vision rule. Stored as JSON in the SQLite Detections
/// table. Pairs with a <see cref="DetectionModel"/> file (compiled trainer output) — the
/// definition holds the runtime knobs, the model holds the binary artifacts.
///
/// Locked-in kinds:
///   • <see cref="DetectionKind.Tracker"/>   — track a moving element / character location.
///   • <see cref="DetectionKind.Pattern"/>   — detect appearance via ORB descriptor matching.
///   • <see cref="DetectionKind.Text"/>      — OCR (Tesseract) for buff names / status text.
///   • <see cref="DetectionKind.Bar"/>       — HP / MP / cooldown meters; reads fill ratio.
///   • <see cref="DetectionKind.Composite"/> — boolean AND/OR over other detections (no training).
/// </summary>
public sealed class DetectionDefinition
{
    /// <summary>Stable id (filename, lowercase letters/digits/_/-). Generated from name on first save.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name. Free-form; what the user types in the editor.</summary>
    public string Name { get; set; } = "";

    /// <summary>Detection kind discriminator. Drives which of the *Options sub-objects is read.</summary>
    public DetectionKind Kind { get; set; } = DetectionKind.Pattern;

    /// <summary>Free-form tag the user can use to group detections in the UI.</summary>
    public string? Group { get; set; }

    /// <summary>Whether <c>detect.runAll()</c> includes this detection.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Runtime SEARCH region. Null = whole frame.</summary>
    public DetectionRoi? Roi { get; set; }

    public TrackerOptions? Tracker { get; set; }
    public PatternOptions? Pattern { get; set; }
    public TextOptions? Text { get; set; }
    public BarOptions? Bar { get; set; }
    public CompositeOptions? Composite { get; set; }

    /// <summary>Flip the result's <c>found</c> flag. Equivalent to <c>!detect.run(x).found</c>
    /// without the script-side boilerplate. Lets <c>brickbot.when()</c> predicates stay one-line.</summary>
    public bool Inverse { get; set; }

    /// <summary>Auto-disable after N successful runs in the current Run. Null = unlimited.
    /// Mirrors MaaFramework's <c>max_hit</c>. Useful for one-shot triggers (e.g. "open door" that
    /// should fire exactly once per run).</summary>
    public int? MaxHit { get; set; }

    /// <summary>How the detection result reaches the rest of the system (ctx / event / overlay).</summary>
    public DetectionOutput Output { get; set; } = new();
}

/// <summary>
/// Kind discriminator. Serialized as camelCase string by JsonStringEnumConverter(CamelCase)
/// so the frontend type union is <c>'tracker' | 'pattern' | 'text' | 'bar' | 'composite'</c>.
/// </summary>
public enum DetectionKind
{
    Tracker,
    Pattern,
    Text,
    Bar,
    /// <summary>Boolean AND/OR over other detections. No training — the model file holds metadata only.</summary>
    Composite,
}

public enum AnchorOrigin
{
    TopLeft, TopCenter, TopRight,
    MidLeft, Center, MidRight,
    BottomLeft, BottomCenter, BottomRight,
}

/// <summary>
/// Region of interest. Three resolution modes (priority order at runtime):
///   1. <see cref="FromDetectionId"/> set → use that detection's last match bbox; X/Y/W/H
///      are interpreted per <see cref="OffsetMode"/>.
///   2. <see cref="Anchor"/> set → resolve X/Y as offsets from the anchor point; W/H absolute pixel sizes.
///   3. Neither set → X/Y/W/H is absolute pixels in window-relative coordinates.
/// </summary>
public sealed class DetectionRoi
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    public AnchorOrigin? Anchor { get; set; }
    public string? FromDetectionId { get; set; }

    /// <summary>How X/Y/W/H are interpreted when <see cref="FromDetectionId"/> is set.
    /// Default <see cref="RoiOffsetMode.Inset"/> for back-compat. Use <see cref="RoiOffsetMode.Relative"/>
    /// to anchor a sub-region at an explicit offset within the parent's bbox.</summary>
    public RoiOffsetMode? OffsetMode { get; set; }
}

/// <summary>How <see cref="DetectionRoi"/>'s X/Y/W/H are read when chained off another detection.</summary>
public enum RoiOffsetMode
{
    /// <summary>X = left inset, Y = top inset, W = right inset, H = bottom inset. ROI = parent bbox shrunk inward.</summary>
    Inset,
    /// <summary>X/Y = offsets from parent's top-left; W/H = absolute pixel size. ROI = sub-region anchored to parent.</summary>
    Relative,
}

// ============================================================================
//  Per-kind options — RUNTIME KNOBS only.
// ============================================================================

public enum TrackerAlgorithm
{
    Kcf,
    Csrt,
    Mil,
}

public sealed class TrackerOptions
{
    public TrackerAlgorithm Algorithm { get; set; } = TrackerAlgorithm.Kcf;
    public bool ReacquireOnLost { get; set; } = true;
}

public sealed class PatternOptions
{
    public double LoweRatio { get; set; } = 0.75;
    public double MinConfidence { get; set; } = 0.20;
    public int MaxRuntimeKeypoints { get; set; } = 500;
}

public sealed class TextOptions
{
    public string Language { get; set; } = "eng";
    public int PageSegMode { get; set; } = 7;
    public string? MatchRegex { get; set; }
    public int MinConfidence { get; set; } = 60;
    public bool Binarize { get; set; } = true;
    public double UpscaleFactor { get; set; } = 2.0;
}

public sealed class BarOptions
{
    public string? AnchorPatternId { get; set; }
    public RgbColor FillColor { get; set; } = new(220, 30, 30);
    public int Tolerance { get; set; } = 60;
    public BrickBot.Modules.Vision.Models.ColorSpace ColorSpace { get; set; } = BrickBot.Modules.Vision.Models.ColorSpace.Rgb;
    public BrickBot.Modules.Vision.Models.FillDirection Direction { get; set; } = BrickBot.Modules.Vision.Models.FillDirection.LeftToRight;
    public double LineThreshold { get; set; } = 0.4;
    public double InsetLeftPct { get; set; } = 0.30;
    public double InsetRightPct { get; set; } = 0.18;
}

/// <summary>Boolean composition over other detections. Reads each operand's last cached
/// result within the current Run; <see cref="DetectionKind.Composite"/> detections must run
/// AFTER their operands. <see cref="StdLib"/>'s <c>detect.runAll</c> handles this via a 3-pass
/// schedule (independents → ROI-chained → composites).</summary>
public sealed class CompositeOptions
{
    public CompositeOp Op { get; set; } = CompositeOp.And;

    /// <summary>Ids of operand detections. AND requires every operand to report <c>found = true</c>;
    /// OR requires at least one. Empty = always <c>found = false</c>.</summary>
    public string[] DetectionIds { get; set; } = Array.Empty<string>();
}

public enum CompositeOp
{
    And,
    Or,
}

public sealed record RgbColor(int R, int G, int B);

// ============================================================================
//  Output bindings (unchanged)
// ============================================================================

public sealed class DetectionOutput
{
    public string? CtxKey { get; set; }
    public string? Event { get; set; }
    public bool EventOnChangeOnly { get; set; } = true;
    public DetectionOverlay? Overlay { get; set; }
    public DetectionOutputType? Type { get; set; }
    public DetectionStability? Stability { get; set; }
}

public enum DetectionOutputType
{
    Boolean,
    Number,
    Text,
    Bbox,
    Bboxes,
    Point,
}

public sealed class DetectionStability
{
    public int MinDurationMs { get; set; }
    public double Tolerance { get; set; }
}

public sealed class DetectionOverlay
{
    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = "#52c41a";
    public string? Label { get; set; }
}
