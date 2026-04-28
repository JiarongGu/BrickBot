namespace BrickBot.Modules.Detection.Models;

/// <summary>
/// Typed result of running one <see cref="DetectionDefinition"/> against a frame.
/// All fields are nullable; only the ones matching <see cref="Kind"/> are populated.
/// Returned to JS scripts (Jint surfaces the public properties as JS object fields)
/// AND to the frontend over IPC. Uses lowercase property names so the JS / TypeScript
/// shape is identical without an extra mapper step — Jint reads CLR property names
/// verbatim, so a C# <c>Found</c> would be <c>r.Found</c> in JS, awkward.
/// </summary>
public sealed class DetectionResult
{
    public string id { get; init; } = "";
    public string name { get; init; } = "";
    public DetectionKind kind { get; init; }
    public bool found { get; set; }
    public double durationMs { get; init; }

    /// <summary>0..1 fill ratio for <c>bar</c>.</summary>
    public double? value { get; init; }

    /// <summary>Bbox: tracker = current position; pattern = matched element bbox via RANSAC homography;
    /// bar = the bar's bounding box.</summary>
    public ResultBox? match { get; init; }

    /// <summary>Confidence (0..1). Pattern: matched-keypoint ratio post Lowe + RANSAC.
    /// Tracker: not populated (tracker doesn't expose a confidence value). Bar: not populated.</summary>
    public double? confidence { get; init; }

    /// <summary>Strip used by <c>bar</c> for the per-line fill scan — useful for UI overlay.</summary>
    public ResultBox? strip { get; init; }

    /// <summary>OCR result text (Text kind only). Empty/null when the kind isn't Text or no
    /// confident match was found.</summary>
    public string? text { get; init; }
}

/// <summary>Lower-cased so JS receives <c>{x,y,w,h,cx,cy}</c> with no mapping.</summary>
public sealed class ResultBox
{
    public int x { get; init; }
    public int y { get; init; }
    public int w { get; init; }
    public int h { get; init; }
    public int cx { get; init; }
    public int cy { get; init; }
}
