using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Detection.Models;
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
    /// of <paramref name="targetColor"/> inside the ROI; returns the fraction (0..1).
    /// Robust to noise + anti-aliasing because it's pixel-counting, not the leftmost-fill
    /// heuristic. Use for HP/MP/cooldown bars.
    /// <para>When <paramref name="colorSpace"/> is <see cref="ColorSpace.Hsv"/> the tolerance
    /// is interpreted as a hue half-window (degrees); the matcher accepts wide saturation
    /// and brightness ranges so it survives lighting / filter differences between the saved
    /// template color and the live frame.</para>
    /// </summary>
    double PercentBar(CaptureFrame frame, RegionOfInterest roi, ColorSample targetColor, int tolerance,
        ColorSpace colorSpace = ColorSpace.Rgb);

    /// <summary>
    /// Directional fill ratio. Walks the bar from the empty side toward the full side; for each
    /// orthogonal line (column for horizontal directions, row for vertical) computes the fraction
    /// of pixels matching the target color. A line counts as "filled" when its fraction meets
    /// <paramref name="lineThreshold"/>. Returns the count of filled lines / total lines.
    ///
    /// <para>Why this beats <see cref="PercentBar"/>: when the bar has a vertical gradient or
    /// anti-aliased edges, the simple "matched / total" ratio underestimates fill because edge
    /// pixels miss the tolerance window. Per-line thresholding ignores those gaps and tracks
    /// the boundary the user actually sees.</para>
    /// </summary>
    double LinearFillRatio(CaptureFrame frame, RegionOfInterest roi, ColorSample targetColor, int tolerance,
        ColorSpace colorSpace, FillDirection direction, double lineThreshold);

    /// <summary>
    /// Mean absolute difference between two ROIs of the same frame, normalized to 0..1.
    /// 0 = identical (or near-identical) pixels; 1 = totally different. Use for "did the
    /// skill icon change" / "did the visual effect appear" against a stored baseline.
    /// When <paramref name="edge"/> is true, both inputs are Canny-edge-detected before the
    /// diff so color drift / brightness shifts don't trigger; only shape changes do.
    /// </summary>
    double Diff(CaptureFrame frame, Mat baseline, RegionOfInterest currentRoi, bool edge = false);

    /// <summary>
    /// Scale-invariant template matching: iterates a range of scales and returns the best
    /// match across all of them. Solves the "icon scaled slightly because the user changed
    /// resolution" problem without paying ORB's native-feature-extractor cost. Combine with
    /// a tight Roi to keep total runtime reasonable.
    /// </summary>
    VisionMatch? FindFeatures(CaptureFrame frame, Mat template, FeatureMatchOptions options);

    /// <summary>
    /// Find blobs of a color range. Returns up to <c>options.MaxResults</c> bounding boxes
    /// sorted by area descending. Exposed via <c>vision.findColors</c> for scripts.
    /// </summary>
    IReadOnlyList<ColorBlob> FindColors(CaptureFrame frame, ColorRange range, FindColorsOptions options);

    /// <summary>
    /// Extract ORB keypoints + binary descriptors from <paramref name="image"/>. The trainer
    /// uses this on each positive sample to assemble the stored descriptor blob; the runtime
    /// uses it on each frame's ROI to find candidates against the stored blob. Returns the
    /// keypoints (positions + scales + angles) and the N×32 CV_8U descriptor matrix.
    /// </summary>
    (KeyPoint[] Keypoints, Mat Descriptors) ExtractDescriptors(Mat image, int maxKeypoints);

    /// <summary>
    /// Match a trained ORB descriptor blob against the current frame's ROI. Uses BFMatcher
    /// (Hamming) + Lowe ratio test + RANSAC homography. Returns the projected bbox + a
    /// confidence equal to the ratio of inlier matches to total trained keypoints — or null
    /// if the inlier count falls below <see cref="PatternMatchOptions.MinConfidence"/>.
    /// </summary>
    PatternMatch? MatchPattern(CaptureFrame frame, Mat trainDescriptors, PatternMatchOptions options);

    /// <summary>
    /// OCR the given ROI via Tesseract. Returns the recognized text and per-result confidence
    /// (0..100). Honors <see cref="TextOptions.Language"/>, page-segmentation mode, optional
    /// pre-binarization and upscaling. NOT IMPLEMENTED in phase B — returns ("", 0) until
    /// the Tesseract NuGet package is wired in phase C.
    /// </summary>
    (string Text, int Confidence) OcrRoi(CaptureFrame frame, RegionOfInterest roi, TextOptions options);
}
