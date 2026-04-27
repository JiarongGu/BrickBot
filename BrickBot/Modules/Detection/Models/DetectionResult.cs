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
    public bool found { get; init; }
    public double durationMs { get; init; }

    /// <summary>0..1 fill ratio for progressBar; 0..1 diff for effect.</summary>
    public double? value { get; init; }

    /// <summary>true when an effect threshold tripped; null for non-effect kinds.</summary>
    public bool? triggered { get; init; }

    public ResultBox? match { get; init; }
    public ResultBox[]? blobs { get; init; }

    /// <summary>Confidence reported by template / featureMatch matchers.</summary>
    public double? confidence { get; init; }

    /// <summary>Strip auto-discovered for progressBar — useful for the UI to render the sample band.</summary>
    public ResultBox? strip { get; init; }
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
