namespace BrickBot.Modules.Vision.Models;

public sealed record VisionMatch(int X, int Y, int Width, int Height, double Confidence)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

/// <summary>
/// <paramref name="Scale"/> downsamples both the search frame and the template by the same
/// factor before <c>MatchTemplate</c>. 1.0 = full resolution (best accuracy, slowest);
/// 0.5 ≈ 4× faster with marginal confidence loss; 0.25 ≈ 16× faster, OK for large distinctive
/// templates. Match coordinates are scaled back up before returning.
///
/// <paramref name="Grayscale"/> converts haystack + template to single-channel before matching
/// (~3× faster, slight color-discrimination loss). Combine with Scale + Roi for compounding
/// speedups.
///
/// <paramref name="Pyramid"/> enables a 2-level coarse-to-fine search: match at quarter res
/// to find a candidate, then refine at full res in a small ROI around it. ~5× faster than a
/// flat full-res match while keeping full-res accuracy. Ignored when an explicit Roi or
/// Scale is set.
/// </summary>
public sealed record FindOptions(
    double MinConfidence = 0.85,
    RegionOfInterest? Roi = null,
    double Scale = 1.0,
    bool Grayscale = false,
    bool Pyramid = false);

public sealed record RegionOfInterest(int X, int Y, int Width, int Height);

public sealed record ColorSample(int R, int G, int B);

/// <summary>
/// Per-channel inclusive RGB range for color-based detection. Build with helpers like
/// <c>ColorRange.AroundRgb(r, g, b, tolerance)</c> to avoid manually computing min/max.
/// </summary>
public sealed record ColorRange(
    int RMin, int RMax,
    int GMin, int GMax,
    int BMin, int BMax)
{
    public static ColorRange AroundRgb(int r, int g, int b, int tolerance) => new(
        Math.Max(0, r - tolerance), Math.Min(255, r + tolerance),
        Math.Max(0, g - tolerance), Math.Min(255, g + tolerance),
        Math.Max(0, b - tolerance), Math.Min(255, b + tolerance));
}

public sealed record ColorBlob(int X, int Y, int Width, int Height, int Area, int CenterX, int CenterY);

/// <summary>
/// Scale-invariant template matching options. The matcher tries <paramref name="ScaleSteps"/>
/// uniformly-spaced scales from <paramref name="ScaleMin"/> to <paramref name="ScaleMax"/>
/// and returns the best match. Defaults span ±20% which catches resolution-driven UI scaling
/// without making the search prohibitively slow. Combine with <paramref name="Roi"/> to keep
/// per-iteration cost low.
/// </summary>
public sealed record FeatureMatchOptions(
    double MinConfidence = 0.80,
    RegionOfInterest? Roi = null,
    double ScaleMin = 0.9,
    double ScaleMax = 1.1,
    int ScaleSteps = 3);

public sealed record FindColorsOptions(
    RegionOfInterest? Roi = null,
    int MinArea = 25,
    int MaxResults = 32);
