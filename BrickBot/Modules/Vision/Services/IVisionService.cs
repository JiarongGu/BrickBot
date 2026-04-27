using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Vision.Models;
using OpenCvSharp;

namespace BrickBot.Modules.Vision.Services;

public interface IVisionService
{
    /// <summary>Find the best match of `template` inside `frame`. Returns null if confidence below threshold.</summary>
    VisionMatch? Find(CaptureFrame frame, Mat template, FindOptions options);

    /// <summary>Sample the BGR color at the given coordinate inside the frame.</summary>
    ColorSample ColorAt(CaptureFrame frame, int x, int y);

    /// <summary>
    /// Estimate how full a colored bar is. Counts pixels within <paramref name="tolerance"/>
    /// of <paramref name="targetColor"/> (per-channel max abs diff) inside the ROI; returns
    /// the fraction (0..1). Robust to noise + anti-aliasing because it's pixel-counting,
    /// not the leftmost-fill heuristic. Use for HP/MP/cooldown bars.
    /// </summary>
    double PercentBar(CaptureFrame frame, RegionOfInterest roi, ColorSample targetColor, int tolerance);

    /// <summary>
    /// Mean absolute difference between two ROIs of the same frame, normalized to 0..1.
    /// 0 = identical (or near-identical) pixels; 1 = totally different. Use for "did the
    /// skill icon change" / "did the visual effect appear" against a stored baseline.
    /// </summary>
    double Diff(CaptureFrame frame, Mat baseline, RegionOfInterest currentRoi);

    /// <summary>
    /// Scale-invariant template matching: iterates a range of scales and returns the best
    /// match across all of them. Solves the "icon scaled slightly because the user changed
    /// resolution" problem without paying ORB's native-feature-extractor cost. Combine with
    /// a tight Roi to keep total runtime reasonable.
    /// </summary>
    VisionMatch? FindFeatures(CaptureFrame frame, Mat template, FeatureMatchOptions options);

    /// <summary>
    /// Find blobs of a color range. Returns up to <c>options.MaxResults</c> bounding boxes
    /// sorted by area descending. Order of magnitude faster than template matching for
    /// distinctly-colored elements (HP bar fills, skill cooldown overlays, debuff icons).
    /// </summary>
    IReadOnlyList<ColorBlob> FindColors(CaptureFrame frame, ColorRange range, FindColorsOptions options);
}
