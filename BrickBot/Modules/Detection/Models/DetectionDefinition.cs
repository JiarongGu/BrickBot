namespace BrickBot.Modules.Detection.Models;

/// <summary>
/// A named, typed, persisted vision detection. One JSON file per detection at
/// <c>data/profiles/{id}/detections/{id}.json</c>. The runner picks these up at run time
/// (via <c>detect.run(name)</c> / <c>detect.runAll()</c>) and the UI's Detections panel
/// edits them. Each definition declares its kind, ROI, type-specific options, and the
/// output bindings (ctx key + event name + overlay) that downstream systems consume.
/// </summary>
public sealed class DetectionDefinition
{
    /// <summary>Stable id (filename, lowercase letters/digits/_/-). Generated from name on first save.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name. Free-form; what the user types in the editor.</summary>
    public string Name { get; set; } = "";

    /// <summary>Detection kind discriminator. Drives which of the *Config sub-objects is read.</summary>
    public DetectionKind Kind { get; set; } = DetectionKind.Template;

    /// <summary>Free-form tag the user can use to group detections in the UI.</summary>
    public string? Group { get; set; }

    /// <summary>Whether <c>detect.runAll()</c> includes this detection.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Window-relative search region. null = whole frame. Required for progressBar/effect/text.</summary>
    public DetectionRoi? Roi { get; set; }

    public TemplateOptions? Template { get; set; }
    public ProgressBarOptions? ProgressBar { get; set; }
    public ColorPresenceOptions? ColorPresence { get; set; }
    public EffectOptions? Effect { get; set; }
    public FeatureMatchOptions? FeatureMatch { get; set; }

    /// <summary>Region kind has no kind-specific config — its ROI (with anchor support) IS the
    /// definition. Useful for declaring named, anchored screen regions that other detections
    /// reference via <see cref="DetectionRoi.FromDetectionId"/>.</summary>
    public RegionOptions? Region { get; set; }

    /// <summary>How the detection result reaches the rest of the system (ctx / event / overlay).</summary>
    public DetectionOutput Output { get; set; } = new();
}

/// <summary>
/// Kind discriminator. Serialized as camelCase string by JsonStringEnumConverter(CamelCase)
/// so the frontend type union is <c>'template' | 'progressBar' | 'colorPresence' | 'effect' | 'featureMatch'</c>.
/// </summary>
public enum DetectionKind
{
    /// <summary>Template match. Output: bbox + confidence (or not-found).</summary>
    Template,

    /// <summary>Two-stage progress bar: template locates the bar, brightest fill row is auto-discovered,
    /// percent-bar samples a strip across it. Output: 0..1 fill ratio.</summary>
    ProgressBar,

    /// <summary>Color blobs inside an ROI. Output: count + bboxes.</summary>
    ColorPresence,

    /// <summary>ROI vs baseline diff. Output: bool (true when diff > threshold) — use for visual-effect detection.</summary>
    Effect,

    /// <summary>Scale-invariant template match. Output: bbox + confidence (or not-found).</summary>
    FeatureMatch,

    /// <summary>Anchored screen region — no detection logic, just outputs the resolved ROI.
    /// Use for declaring "the top-right HUD area" once and referencing it from many detections
    /// via <see cref="DetectionRoi.FromDetectionId"/>.</summary>
    Region,
}

/// <summary>
/// 9-point anchor origin for window-relative ROIs. Frontend type is camelCase string union:
/// <c>'topLeft' | 'topCenter' | 'topRight' | 'midLeft' | 'center' | 'midRight' | 'bottomLeft' | 'bottomCenter' | 'bottomRight'</c>.
/// </summary>
public enum AnchorOrigin
{
    TopLeft, TopCenter, TopRight,
    MidLeft, Center, MidRight,
    BottomLeft, BottomCenter, BottomRight,
}


/// <summary>
/// Region of interest. Three resolution modes (priority order at runtime):
///   1. <see cref="FromDetectionId"/> set → use that detection's last match bbox; X/Y/W/H
///      become inset offsets inside it (X = left inset, Y = top inset, W = right inset,
///      H = bottom inset; all clamped to >= 0). Lets one detection compose on top of another.
///   2. <see cref="Anchor"/> set → resolve X/Y as offsets from the anchor point against the
///      current frame. W/H are absolute pixel sizes. Use this to track HUD areas that move
///      with the window edges.
///   3. Neither set → X/Y/W/H is absolute pixels in window-relative coordinates.
/// </summary>
public sealed class DetectionRoi
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    /// <summary>9-point anchor for window-relative positioning. null = absolute coords.</summary>
    public AnchorOrigin? Anchor { get; set; }

    /// <summary>When set, this ROI is resolved from the referenced detection's match bbox at
    /// run time. X/Y/W/H are interpreted as inset margins inside the referenced match. Combine
    /// with cross-detection composition: detection A finds the HP bar bbox, detection B uses
    /// A's bbox (with insets) as its ROI to compute fill %.</summary>
    public string? FromDetectionId { get; set; }
}

public sealed class TemplateOptions
{
    public string TemplateName { get; set; } = "";
    public double MinConfidence { get; set; } = 0.85;
    public double Scale { get; set; } = 1.0;
    public bool Grayscale { get; set; } = true;
    public bool Pyramid { get; set; }

    /// <summary>Match in edge-detected (Canny) space instead of pixel space. Robust against
    /// color drift, lighting changes, and variable fill (e.g. HP bar saved at 80% but runtime
    /// at 30% — only outline survives). Implies grayscale.</summary>
    public bool Edge { get; set; }
}

public sealed class ProgressBarOptions
{
    /// <summary>Template that frames the bar's bbox. Strip auto-discovered inside it at run time.
    /// Optional when <see cref="DetectionDefinition.Roi"/> uses <see cref="DetectionRoi.FromDetectionId"/>
    /// or has an explicit anchored ROI — in those cases the bbox is supplied directly.</summary>
    public string TemplateName { get; set; } = "";
    public double MinConfidence { get; set; } = 0.80;

    /// <summary>Match the bar's bbox in edge space — survives the case where the saved
    /// template captured the bar at a different fill level than the live frame.</summary>
    public bool TemplateEdge { get; set; } = true;

    /// <summary>Uniform downsample for the bbox match (1.0 = full res; lower = faster, less accurate).</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Convert the bbox match to grayscale before comparing — ~3× faster than color match.
    /// Already implied by edge mode.</summary>
    public bool Grayscale { get; set; } = true;

    /// <summary>Coarse-to-fine pyramid bbox match — ~5× faster than flat full-res with same accuracy.</summary>
    public bool Pyramid { get; set; }

    /// <summary>BGR fill color (despite the field names — matches the rest of the codebase's RGB-flavored API).</summary>
    public RgbColor FillColor { get; set; } = new(220, 220, 220);

    public int Tolerance { get; set; } = 60;

    /// <summary>Color space used to count fill pixels. HSV is the right choice when the game
    /// applies bloom / gamma / color filters that drift the saved RGB but keep the hue intact.</summary>
    public BrickBot.Modules.Vision.Models.ColorSpace ColorSpace { get; set; } = BrickBot.Modules.Vision.Models.ColorSpace.Rgb;

    /// <summary>Direction the bar fills. The runner's per-line scan starts from the empty side
    /// and walks toward the full side, so the boundary detection (where line-fill drops below
    /// <see cref="LineThreshold"/>) maps cleanly to the user-perceived fill ratio.</summary>
    public BrickBot.Modules.Vision.Models.FillDirection Direction { get; set; } = BrickBot.Modules.Vision.Models.FillDirection.LeftToRight;

    /// <summary>Per-line fill threshold (0..1). A line (column for horizontal direction, row for
    /// vertical) is considered "filled" if at least this fraction of its pixels match the fill
    /// color. 0.4 = robust to anti-aliasing/gradients; raise toward 0.7 if the bar has noisy
    /// or partial sub-fills you want to ignore.</summary>
    public double LineThreshold { get; set; } = 0.4;

    /// <summary>Skip this fraction of the bbox width on the left when sampling — drops side icons / endcaps.</summary>
    public double InsetLeftPct { get; set; } = 0.30;

    public double InsetRightPct { get; set; } = 0.18;
}

public sealed class ColorPresenceOptions
{
    public RgbColor Color { get; set; } = new(220, 30, 30);
    public int Tolerance { get; set; } = 30;
    public int MinArea { get; set; } = 100;
    public int MaxResults { get; set; } = 8;

    /// <summary>HSV mode replaces RGB-channel thresholds with hue-window matching — much more
    /// stable across lighting / shading variation in the live frame.</summary>
    public BrickBot.Modules.Vision.Models.ColorSpace ColorSpace { get; set; } = BrickBot.Modules.Vision.Models.ColorSpace.Rgb;
}

public sealed class EffectOptions
{
    /// <summary>How much the ROI must change (0..1) vs. baseline to count as triggered.</summary>
    public double Threshold { get; set; } = 0.15;

    /// <summary>If true, the runner snapshots the baseline on first invocation (or after Reset) — handy
    /// for "is the screen flashing right now" without authoring a separate baseline asset.</summary>
    public bool AutoBaseline { get; set; } = true;

    /// <summary>Compare in Canny edge space instead of raw pixels. Catches shape changes
    /// (icon swaps, buff appears) without false-positives from lighting / color shifts.</summary>
    public bool Edge { get; set; }
}

public sealed class FeatureMatchOptions
{
    public string TemplateName { get; set; } = "";
    public double MinConfidence { get; set; } = 0.80;
    public double ScaleMin { get; set; } = 0.9;
    public double ScaleMax { get; set; } = 1.1;
    public int ScaleSteps { get; set; } = 3;

    /// <summary>Convert haystack + template to grayscale before each scale step. Default true —
    /// feature matching is naturally cross-channel-robust so the color signal rarely helps.</summary>
    public bool Grayscale { get; set; } = true;

    /// <summary>Match in edge space — combine with multi-scale to handle scale + color drift.</summary>
    public bool Edge { get; set; }
}

/// <summary>Region kind has no body — the definition's <see cref="DetectionDefinition.Roi"/>
/// (with anchor support) IS the configuration. Kept as a dedicated record so the JSON file
/// shape is self-describing and the editor can show kind-specific helper text.</summary>
public sealed class RegionOptions
{
    /// <summary>Free-form description shown alongside the region in the editor's pickers.</summary>
    public string? Note { get; set; }
}

public sealed record RgbColor(int R, int G, int B);

public sealed class DetectionOutput
{
    /// <summary>Write the result value into <c>ctx[CtxKey]</c> on every run (when set).</summary>
    public string? CtxKey { get; set; }

    /// <summary>Emit a brickbot event with this name on every run, payload = the result (when set).</summary>
    public string? Event { get; set; }

    /// <summary>Only emit the event when the result changes (debounce static-state churn). Default true.</summary>
    public bool EventOnChangeOnly { get; set; } = true;

    /// <summary>Overlay rendering hints. Read by future GameOverlay; runner publishes them anyway.</summary>
    public DetectionOverlay? Overlay { get; set; }
}

public sealed class DetectionOverlay
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hex string (#RRGGBB). Frontend converts.</summary>
    public string Color { get; set; } = "#52c41a";

    /// <summary>Optional label template — supports {value} / {confidence} / {count}. Empty = no label.</summary>
    public string? Label { get; set; }
}
