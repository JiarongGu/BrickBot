namespace BrickBot.Modules.Detection.Models;

/// <summary>
/// A named, typed, persisted vision detection. One JSON file per detection at
/// <c>data/profiles/{id}/detections/{id}.json</c>. The runner picks these up at run time
/// (via <c>detect.run(name)</c> / <c>detect.runAll()</c>) and the UI's Detections panel
/// edits them. Each definition declares its kind, ROI, type-specific options, and the
/// output bindings (ctx key + event name + overlay) that downstream systems consume.
///
/// Locked-in kinds (everything else has been deleted):
///   • <see cref="DetectionKind.Tracker"/> — track a moving element / character location.
///   • <see cref="DetectionKind.Pattern"/> — detect appearance via ORB descriptor matching
///     (background- / color-invariant; replaces the old template path).
///   • <see cref="DetectionKind.Text"/>    — OCR (Tesseract) for buff names / status text.
///   • <see cref="DetectionKind.Bar"/>     — HP / MP / cooldown meters; reads fill ratio.
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

    /// <summary>Window-relative search region. null = whole frame. Required for bar/text/effect.</summary>
    public DetectionRoi? Roi { get; set; }

    public TrackerOptions? Tracker { get; set; }
    public PatternOptions? Pattern { get; set; }
    public TextOptions? Text { get; set; }
    public BarOptions? Bar { get; set; }

    /// <summary>How the detection result reaches the rest of the system (ctx / event / overlay).</summary>
    public DetectionOutput Output { get; set; } = new();
}

/// <summary>
/// Kind discriminator. Serialized as camelCase string by JsonStringEnumConverter(CamelCase)
/// so the frontend type union is <c>'tracker' | 'pattern' | 'text' | 'bar'</c>.
/// </summary>
public enum DetectionKind
{
    /// <summary>Visual tracker (OpenCV KCF / CSRT / MIL). Initialized once with a chosen frame
    /// + bbox; subsequent runs call <c>tracker.Update(frame)</c> to follow the element as it
    /// moves. Output: bbox + cx/cy. Use for moving sprites / characters where position matters.</summary>
    Tracker,

    /// <summary>ORB-descriptor pattern match. Trainer extracts ORB keypoints + descriptors from
    /// positive samples; runtime extracts descriptors from the current ROI and matches via
    /// <c>BFMatcher</c> (Hamming + Lowe ratio test) with optional RANSAC homography to localize.
    /// Background- and color-invariant — survives shader/filter changes that kill plain template
    /// matching. Output: bbox + confidence (matched-keypoint ratio) or not-found.</summary>
    Pattern,

    /// <summary>OCR text recognition via Tesseract. ROI is binarized + scaled, fed to
    /// <c>TesseractEngine.Process</c>; result is the extracted text plus confidence. Optional
    /// regex filter narrows results to specific patterns. Use for buff names, status banners,
    /// quest text — anything readable that's not amenable to fixed-pattern match.</summary>
    Text,

    /// <summary>Bar / meter (HP, MP, stamina, cooldown). Two paths to locate the bar bbox:
    /// (a) ROI given directly, (b) Pattern template anchors the bar. Then the runner samples a
    /// strip across the bar in the configured fill direction and computes fill ratio via
    /// <c>LinearFillRatio</c>. Output: 0..1 fill ratio.</summary>
    Bar,
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
    /// run time. X/Y/W/H are interpreted as inset margins inside the referenced match.</summary>
    public string? FromDetectionId { get; set; }
}

// ============================================================================
//  Per-kind option blocks
// ============================================================================

/// <summary>
/// Tracker algorithm — wraps OpenCV's <c>cv::Tracker</c> implementations. Picked at training
/// time per detection. Trade-off ≈ FPS vs robustness:
///   • <see cref="Kcf"/>  — fast (~150 fps single tracker), good for stable sprites.
///   • <see cref="Csrt"/> — slow (~25 fps), most accurate, robust to scale + rotation drift.
///   • <see cref="Mil"/>  — moderate, robust to brief occlusions, slower than KCF.
/// Serialized as camelCase string (kcf / csrt / mil).
/// </summary>
public enum TrackerAlgorithm
{
    Kcf,
    Csrt,
    Mil,
}

public sealed class TrackerOptions
{
    /// <summary>Base64-encoded PNG of the frame the tracker was initialized on. Required —
    /// the runtime decodes this once on first run to seed <c>tracker.Init(frame, bbox)</c>.</summary>
    public string? InitFramePng { get; set; }

    /// <summary>Initial bbox in window-relative pixel coords (the user's drag-rectangle at
    /// training time).</summary>
    public int InitX { get; set; }
    public int InitY { get; set; }
    public int InitW { get; set; }
    public int InitH { get; set; }

    /// <summary>Tracker algorithm. Default <see cref="TrackerAlgorithm.Kcf"/>.</summary>
    public TrackerAlgorithm Algorithm { get; set; } = TrackerAlgorithm.Kcf;

    /// <summary>When the tracker reports lost (Update returns false), automatically re-init
    /// from the saved init frame on the next call. Default true.</summary>
    public bool ReacquireOnLost { get; set; } = true;
}

public sealed class PatternOptions
{
    /// <summary>Base64-encoded reference patch (cropped to the trained element's tight bbox).
    /// Used for re-training (descriptor re-extraction) and overlay visualization. NOT used
    /// directly for runtime correlation — that's done by the descriptor blob.</summary>
    public string? EmbeddedPng { get; set; }

    /// <summary>Base64-encoded ORB descriptor matrix (CV_8U N×32 rows × 32 cols, serialized
    /// row-major). Trainer extracts ORB keypoints from positive samples, keeps the
    /// most-stable cluster, stores their descriptors here. Runtime matches against this blob.</summary>
    public string? Descriptors { get; set; }

    /// <summary>Number of keypoints encoded in <see cref="Descriptors"/> (rows). Used to
    /// validate the blob on load and to compute confidence as <c>matched / Keypoints</c>.</summary>
    public int KeypointCount { get; set; }

    /// <summary>Reference template width × height in pixels. Used to project the matched
    /// keypoints back into a bbox via RANSAC homography.</summary>
    public int TemplateWidth { get; set; }
    public int TemplateHeight { get; set; }

    /// <summary>Lowe ratio test threshold for descriptor matching. Lower = stricter (fewer
    /// false positives), higher = more lenient (more matches). 0.75 is the OpenCV default.</summary>
    public double LoweRatio { get; set; } = 0.75;

    /// <summary>Minimum fraction of trained keypoints that must match to count as found.
    /// 0.20 = at least 20 % of stored keypoints have a matching descriptor in the current
    /// ROI (post Lowe ratio + RANSAC inlier filter).</summary>
    public double MinConfidence { get; set; } = 0.20;

    /// <summary>Maximum ORB keypoints extracted per frame at runtime. Higher = better recall
    /// for cluttered frames, slower. 500 is a reasonable cap for 1080p game scenes.</summary>
    public int MaxRuntimeKeypoints { get; set; } = 500;
}

public sealed class TextOptions
{
    /// <summary>Tesseract language tag (e.g. <c>eng</c>, <c>chi_sim</c>, <c>jpn</c>). Maps to
    /// the corresponding <c>{lang}.traineddata</c> file under <c>tessdata/</c>. Default
    /// <c>eng</c> ships with the app; other languages must be downloaded separately.</summary>
    public string Language { get; set; } = "eng";

    /// <summary>Page-segmentation mode hint to Tesseract. 7 = "single line", which is what
    /// most game-UI text is. 8 = "single word"; 6 = "uniform block" for paragraphs.</summary>
    public int PageSegMode { get; set; } = 7;

    /// <summary>Optional regex filter — only count the OCR result as "found" when the
    /// extracted text matches this regex. Lets you target specific labels (e.g. <c>^HP:</c>)
    /// without script-side post-processing. Empty = accept any non-empty result.</summary>
    public string? MatchRegex { get; set; }

    /// <summary>Minimum Tesseract confidence (0..100) to accept the result. Below this the
    /// detection reports not-found regardless of the regex.</summary>
    public int MinConfidence { get; set; } = 60;

    /// <summary>Pre-binarize the ROI before OCR. Most game text has solid stroke colors on
    /// a busy background — binarization (Otsu / adaptive) dramatically improves OCR accuracy.
    /// Default true; turn off for already-clean text on flat backgrounds.</summary>
    public bool Binarize { get; set; } = true;

    /// <summary>Upscale factor applied to the ROI before OCR. Tesseract is most accurate at
    /// 30–60 pt glyphs; small game UI text often needs 2–3× upscale. Default 2.0.</summary>
    public double UpscaleFactor { get; set; } = 2.0;
}

public sealed class BarOptions
{
    /// <summary>Optional Pattern detection id whose match bbox locates the bar. Leave empty
    /// to use the bar's <see cref="DetectionDefinition.Roi"/> directly (which itself supports
    /// anchored / from-detection composition). Pattern path is the way to go for HP bars
    /// that move with the player frame; ROI path for fixed HUDs.</summary>
    public string? AnchorPatternId { get; set; }

    /// <summary>BGR fill color. The runner counts pixels matching this color (within
    /// <see cref="Tolerance"/>) inside a strip across the bar to compute fill ratio.</summary>
    public RgbColor FillColor { get; set; } = new(220, 30, 30);

    /// <summary>±tolerance per channel for fill-color match. 60 ≈ catches dimmed / boosted
    /// versions of the same color. Raise for HDR / bloomy games; lower for pixel-art games.</summary>
    public int Tolerance { get; set; } = 60;

    /// <summary>HSV mode replaces the per-channel RGB tolerance with a hue-window match.
    /// Robust to lighting drift; pick when the game applies post-processing color filters.</summary>
    public BrickBot.Modules.Vision.Models.ColorSpace ColorSpace { get; set; } = BrickBot.Modules.Vision.Models.ColorSpace.Rgb;

    /// <summary>Direction the bar fills (the empty side and full side it walks between).</summary>
    public BrickBot.Modules.Vision.Models.FillDirection Direction { get; set; } = BrickBot.Modules.Vision.Models.FillDirection.LeftToRight;

    /// <summary>Per-line fill threshold (0..1). A line counts as "filled" when at least this
    /// fraction of its pixels match. 0.4 ≈ robust to anti-aliasing; raise toward 0.7 if the
    /// bar has noisy partial sub-fills you want to ignore.</summary>
    public double LineThreshold { get; set; } = 0.4;

    /// <summary>Inset fractions cropped off the empty / full sides of the bar bbox before
    /// sampling — drops side icons / endcaps. 0.30 / 0.18 are sane defaults for most games.</summary>
    public double InsetLeftPct { get; set; } = 0.30;
    public double InsetRightPct { get; set; } = 0.18;
}

public sealed record RgbColor(int R, int G, int B);

// ============================================================================
//  Output bindings (unchanged)
// ============================================================================

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

    /// <summary>Primary output shape — drives <see cref="DetectionResult.typedValue"/>. Each kind
    /// has a sensible default (Bar → number, Tracker/Pattern → bbox, Text → text) but the user
    /// can override.</summary>
    public DetectionOutputType? Type { get; set; }

    /// <summary>Optional debounce — only emit the result when the value has been stable for at
    /// least <see cref="DetectionStability.MinDurationMs"/>. Filters out single-frame flicker.</summary>
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
