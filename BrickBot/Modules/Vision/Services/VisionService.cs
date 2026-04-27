using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Vision.Models;
using OpenCvSharp;

namespace BrickBot.Modules.Vision.Services;

public sealed class VisionService : IVisionService
{
    public VisionMatch? Find(CaptureFrame frame, Mat template, FindOptions options)
    {
        if (template.Empty()) throw new OperationException("VISION_TEMPLATE_EMPTY");
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");

        // Pyramid path: when explicitly enabled and no other size hints are set, do a
        // coarse-to-fine search. Match at quarter res to find a candidate, then refine at
        // full res in a tight ROI around the candidate. Same accuracy as a flat full-res
        // match, ~5× faster.
        if (options.Pyramid && options.Roi is null && options.Scale >= 0.999)
        {
            var coarseOpts = options with { Pyramid = false, Scale = 0.25, MinConfidence = options.MinConfidence * 0.9 };
            var coarse = Find(frame, template, coarseOpts);
            if (coarse is null) return null;

            var pad = Math.Max(template.Width, template.Height) / 2;
            var refineX = Math.Max(0, coarse.X - pad);
            var refineY = Math.Max(0, coarse.Y - pad);
            var refineW = Math.Min(frame.Image.Width - refineX, template.Width + 2 * pad);
            var refineH = Math.Min(frame.Image.Height - refineY, template.Height + 2 * pad);
            var refineRoi = new RegionOfInterest(refineX, refineY, refineW, refineH);
            return Find(frame, template, options with { Pyramid = false, Scale = 1.0, Roi = refineRoi });
        }

        Mat haystack = frame.Image;
        var roiOffsetX = 0;
        var roiOffsetY = 0;
        Mat? cropped = null;
        Mat? scaledHaystack = null;
        Mat? scaledTemplate = null;
        Mat? grayHaystack = null;
        Mat? grayTemplate = null;
        Mat? edgeHaystack = null;
        Mat? edgeTemplate = null;

        try
        {
            if (options.Roi is { } roi)
            {
                var rect = ClampRect(haystack, roi);
                if (rect.Width <= 0 || rect.Height <= 0) return null;
                cropped = new Mat(haystack, rect);
                haystack = cropped;
                roiOffsetX = rect.X;
                roiOffsetY = rect.Y;
            }

            // Optional uniform downsample for speed. INTER_AREA is the right kernel for
            // shrinking — keeps template-matching confidence reasonable at 0.5–0.25 scale.
            var matchTemplate = template;
            var scale = options.Scale;
            if (scale > 0 && scale < 0.999)
            {
                scaledHaystack = new Mat();
                scaledTemplate = new Mat();
                Cv2.Resize(haystack, scaledHaystack, new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
                Cv2.Resize(template, scaledTemplate, new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
                haystack = scaledHaystack;
                matchTemplate = scaledTemplate;
            }
            else
            {
                scale = 1.0;
            }

            // Grayscale conversion drops MatchTemplate cost ~3× by collapsing 3 channels → 1.
            // Edge mode forces grayscale first (Canny needs single channel) — `Grayscale` is
            // implied. Edge mode is the right choice when template & runtime have different
            // colors / fills (e.g. HP bar saved at 80%, runtime at 30%): only outline survives.
            if (options.Edge || options.Grayscale)
            {
                grayHaystack = new Mat();
                grayTemplate = new Mat();
                Cv2.CvtColor(haystack, grayHaystack, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(matchTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);
                haystack = grayHaystack;
                matchTemplate = grayTemplate;
            }

            if (options.Edge)
            {
                edgeHaystack = new Mat();
                edgeTemplate = new Mat();
                Cv2.Canny(haystack, edgeHaystack, options.EdgeLow, options.EdgeHigh);
                Cv2.Canny(matchTemplate, edgeTemplate, options.EdgeLow, options.EdgeHigh);
                haystack = edgeHaystack;
                matchTemplate = edgeTemplate;
            }

            if (matchTemplate.Width > haystack.Width || matchTemplate.Height > haystack.Height) return null;

            using var result = new Mat();
            Cv2.MatchTemplate(haystack, matchTemplate, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal < options.MinConfidence) return null;

            var inverse = 1.0 / scale;
            return new VisionMatch(
                X: (int)Math.Round(maxLoc.X * inverse) + roiOffsetX,
                Y: (int)Math.Round(maxLoc.Y * inverse) + roiOffsetY,
                Width: template.Width,
                Height: template.Height,
                Confidence: maxVal);
        }
        finally
        {
            cropped?.Dispose();
            scaledHaystack?.Dispose();
            scaledTemplate?.Dispose();
            grayHaystack?.Dispose();
            grayTemplate?.Dispose();
            edgeHaystack?.Dispose();
            edgeTemplate?.Dispose();
        }
    }

    public double PercentBar(CaptureFrame frame, RegionOfInterest roi, ColorSample targetColor, int tolerance,
        ColorSpace colorSpace = ColorSpace.Rgb)
    {
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");
        var rect = ClampRect(frame.Image, roi);
        if (rect.Width <= 0 || rect.Height <= 0) return 0.0;

        using var crop = new Mat(frame.Image, rect);
        using var mask = new Mat();

        if (colorSpace == ColorSpace.Hsv)
        {
            BuildHsvMask(crop, targetColor, tolerance, mask);
        }
        else
        {
            // BGR target so we don't have to swap on every pixel read.
            var lower = new Scalar(
                Math.Max(0, targetColor.B - tolerance),
                Math.Max(0, targetColor.G - tolerance),
                Math.Max(0, targetColor.R - tolerance));
            var upper = new Scalar(
                Math.Min(255, targetColor.B + tolerance),
                Math.Min(255, targetColor.G + tolerance),
                Math.Min(255, targetColor.R + tolerance));
            Cv2.InRange(crop, lower, upper, mask);
        }

        // Count matching pixels via native CountNonZero — vectorized; far faster than
        // walking pixels in C#. Total pixel count is rect area.
        var matched = Cv2.CountNonZero(mask);
        return matched / (double)(rect.Width * rect.Height);
    }

    public double LinearFillRatio(CaptureFrame frame, RegionOfInterest roi, ColorSample targetColor, int tolerance,
        ColorSpace colorSpace, FillDirection direction, double lineThreshold)
    {
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");
        var rect = ClampRect(frame.Image, roi);
        if (rect.Width <= 0 || rect.Height <= 0) return 0.0;

        using var crop = new Mat(frame.Image, rect);
        using var mask = new Mat();
        if (colorSpace == ColorSpace.Hsv)
        {
            BuildHsvMask(crop, targetColor, tolerance, mask);
        }
        else
        {
            var lower = new Scalar(
                Math.Max(0, targetColor.B - tolerance),
                Math.Max(0, targetColor.G - tolerance),
                Math.Max(0, targetColor.R - tolerance));
            var upper = new Scalar(
                Math.Min(255, targetColor.B + tolerance),
                Math.Min(255, targetColor.G + tolerance),
                Math.Min(255, targetColor.R + tolerance));
            Cv2.InRange(crop, lower, upper, mask);
        }

        var threshold = Math.Clamp(lineThreshold, 0.01, 1.0);
        var horizontal = direction == FillDirection.LeftToRight || direction == FillDirection.RightToLeft;
        var lineCount = horizontal ? rect.Width : rect.Height;
        var lineLength = horizontal ? rect.Height : rect.Width;
        if (lineCount <= 0 || lineLength <= 0) return 0.0;

        // Reduce the mask to a 1D fill-fraction array along the orthogonal axis.
        // For a horizontal direction, each column's "fill" = sum of mask pixels in that column / column height.
        // OpenCV's Reduce sums per-row or per-column; we then normalize to 0..1.
        using var reduced = new Mat();
        if (horizontal)
        {
            // Sum down each column → 1×W row vector. Each value = sum of column's 0/255 pixels.
            Cv2.Reduce(mask, reduced, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);
        }
        else
        {
            // Sum across each row → H×1 col vector.
            Cv2.Reduce(mask, reduced, ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_32S);
        }

        // Convert sum-of-(0|255) to fill-fraction per line, count lines that meet threshold.
        // Then translate count → ratio honoring direction (count from the empty side toward full).
        var per255 = lineLength * 255;
        var lineFractions = new double[lineCount];
        for (var i = 0; i < lineCount; i++)
        {
            int sum = horizontal ? reduced.Get<int>(0, i) : reduced.Get<int>(i, 0);
            lineFractions[i] = (double)sum / per255;
        }

        // For a "leftToRight" bar (grows from left), partially-full → left lines are filled,
        // right lines are empty. We walk from the GROW-START side toward the END side and find
        // the last filled line (with one-line gap tolerance for AA/noise). Fill ratio is
        // (lastFilledIndex + 1) / lineCount measured from the grow-start side.
        bool growsFromStart =
            direction == FillDirection.LeftToRight || direction == FillDirection.TopToBottom;

        int lastFilledFromStart = -1;
        int consecutiveGap = 0;
        if (growsFromStart)
        {
            for (var i = 0; i < lineCount; i++)
            {
                if (lineFractions[i] >= threshold) { lastFilledFromStart = i; consecutiveGap = 0; }
                else { consecutiveGap++; if (consecutiveGap > 1) break; }
            }
            return Math.Clamp((double)(lastFilledFromStart + 1) / lineCount, 0.0, 1.0);
        }
        else
        {
            int firstFilledFromEnd = lineCount;
            for (var i = lineCount - 1; i >= 0; i--)
            {
                if (lineFractions[i] >= threshold) { firstFilledFromEnd = i; consecutiveGap = 0; }
                else { consecutiveGap++; if (consecutiveGap > 1) break; }
            }
            return Math.Clamp((double)(lineCount - firstFilledFromEnd) / lineCount, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Build an InRange mask in HSV space. <paramref name="targetColor"/> is RGB; we convert
    /// to a 1×1 HSV pixel for the hue, then threshold around it. Hue is wrapped at 180 so a
    /// red target near 0/180 still matches both ends of the wheel.
    ///
    /// The <paramref name="tolerance"/> parameter is reused as a hue half-window in degrees
    /// (OpenCV hue: 0..179). Saturation + value are clamped wide so dim or pale variants of
    /// the same hue still match — this is the property that makes HSV mode robust to
    /// lighting / color-grading drift between the saved template and the live frame.
    /// </summary>
    private static void BuildHsvMask(Mat bgrCrop, ColorSample target, int tolerance, Mat outMask)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgrCrop, hsv, ColorConversionCodes.BGR2HSV);

        // Convert the target RGB to HSV via a 1×1 BGR pixel so the conversion matches OpenCV's
        // exact Hue scaling (0..179) — getting this right by hand is error-prone.
        using var pixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar(target.B, target.G, target.R));
        using var pixelHsv = new Mat();
        Cv2.CvtColor(pixel, pixelHsv, ColorConversionCodes.BGR2HSV);
        var hsvVec = pixelHsv.Get<Vec3b>(0, 0);
        int hue = hsvVec.Item0;

        // Half-window in hue degrees (OpenCV 0..179). Sat/Val window is intentionally wide
        // (allow >= 30 sat to filter near-grays) so colored UI elements survive lighting drift.
        var hueTol = Math.Clamp(tolerance, 1, 90);
        var lowH = (hue - hueTol + 180) % 180;
        var highH = (hue + hueTol) % 180;

        if (lowH <= highH)
        {
            Cv2.InRange(hsv,
                new Scalar(lowH, 30, 30),
                new Scalar(highH, 255, 255),
                outMask);
        }
        else
        {
            // Hue wraps at 180; build two ranges and OR them.
            using var maskA = new Mat();
            using var maskB = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 30, 30), new Scalar(highH, 255, 255), maskA);
            Cv2.InRange(hsv, new Scalar(lowH, 30, 30), new Scalar(179, 255, 255), maskB);
            Cv2.BitwiseOr(maskA, maskB, outMask);
        }
    }

    public VisionMatch? FindFeatures(CaptureFrame frame, Mat template, FeatureMatchOptions options)
    {
        if (template.Empty()) throw new OperationException("VISION_TEMPLATE_EMPTY");
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");

        // Iterate uniformly-spaced scales between ScaleMin and ScaleMax; pick the highest-
        // confidence match. Each iteration delegates to Find which is already optimized
        // (grayscale-capable, Roi-clamped). One iteration ≈ one regular vision.find call.
        var steps = Math.Max(1, options.ScaleSteps);
        var scaleMin = Math.Max(0.05, options.ScaleMin);
        var scaleMax = Math.Max(scaleMin, options.ScaleMax);
        var stride = steps == 1 ? 0 : (scaleMax - scaleMin) / (steps - 1);

        VisionMatch? best = null;
        for (var i = 0; i < steps; i++)
        {
            var scale = steps == 1 ? scaleMin : scaleMin + i * stride;
            // Scale the TEMPLATE, not the haystack — that way coords stay in frame space.
            using var scaledTemplate = new Mat();
            Cv2.Resize(template, scaledTemplate, new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
            if (scaledTemplate.Width < 4 || scaledTemplate.Height < 4) continue;

            var findOpts = new FindOptions(
                MinConfidence: options.MinConfidence,
                Roi: options.Roi,
                Scale: 1.0,
                Grayscale: true,
                Edge: options.Edge);

            var match = Find(frame, scaledTemplate, findOpts);
            if (match is not null && (best is null || match.Confidence > best.Confidence))
            {
                // Scale match dims back to original-template coords so callers don't have
                // to know the picker's chosen scale.
                best = match with
                {
                    Width = template.Width,
                    Height = template.Height,
                };
            }
        }
        return best;
    }

    public IReadOnlyList<ColorBlob> FindColors(CaptureFrame frame, ColorRange range, FindColorsOptions options)
    {
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");

        Mat haystack = frame.Image;
        var roiOffsetX = 0;
        var roiOffsetY = 0;
        Mat? cropped = null;

        try
        {
            if (options.Roi is { } roi)
            {
                var rect = ClampRect(haystack, roi);
                if (rect.Width <= 0 || rect.Height <= 0) return Array.Empty<ColorBlob>();
                cropped = new Mat(haystack, rect);
                haystack = cropped;
                roiOffsetX = rect.X;
                roiOffsetY = rect.Y;
            }

            using var mask = new Mat();
            if (options.ColorSpace == ColorSpace.Hsv)
            {
                // Use the range midpoint as the target color and the half-spread as tolerance.
                // For HSV mode the user usually picks via PercentBar/Color picker which both
                // surface a single color + tolerance, so collapsing the range here mirrors that.
                var midR = (range.RMin + range.RMax) / 2;
                var midG = (range.GMin + range.GMax) / 2;
                var midB = (range.BMin + range.BMax) / 2;
                var spread = Math.Max(
                    Math.Max((range.RMax - range.RMin) / 2, (range.GMax - range.GMin) / 2),
                    (range.BMax - range.BMin) / 2);
                BuildHsvMask(haystack, new ColorSample(midR, midG, midB), Math.Max(5, spread), mask);
            }
            else
            {
                // OpenCV is BGR-ordered.
                var lower = new Scalar(range.BMin, range.GMin, range.RMin);
                var upper = new Scalar(range.BMax, range.GMax, range.RMax);
                Cv2.InRange(haystack, lower, upper, mask);
            }

            Cv2.FindContours(mask,
                out var contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            var blobs = new List<ColorBlob>(contours.Length);
            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                var area = rect.Width * rect.Height;
                if (area < options.MinArea) continue;

                blobs.Add(new ColorBlob(
                    X: rect.X + roiOffsetX,
                    Y: rect.Y + roiOffsetY,
                    Width: rect.Width,
                    Height: rect.Height,
                    Area: area,
                    CenterX: rect.X + rect.Width / 2 + roiOffsetX,
                    CenterY: rect.Y + rect.Height / 2 + roiOffsetY));
            }

            blobs.Sort((a, b) => b.Area.CompareTo(a.Area));
            if (blobs.Count > options.MaxResults) blobs.RemoveRange(options.MaxResults, blobs.Count - options.MaxResults);
            return blobs;
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    public double Diff(CaptureFrame frame, Mat baseline, RegionOfInterest currentRoi, bool edge = false)
    {
        if (baseline.Empty()) throw new OperationException("VISION_TEMPLATE_EMPTY");
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");

        var rect = ClampRect(frame.Image, currentRoi);
        if (rect.Width <= 0 || rect.Height <= 0) return 1.0;

        using var crop = new Mat(frame.Image, rect);

        // If sizes differ (window resized between baseline and now), normalize the current
        // crop to baseline dimensions so AbsDiff has matching shapes.
        Mat aligned = crop;
        Mat? resized = null;
        Mat? edgeA = null;
        Mat? edgeB = null;
        Mat? grayA = null;
        Mat? grayB = null;
        try
        {
            if (crop.Size() != baseline.Size())
            {
                resized = new Mat();
                Cv2.Resize(crop, resized, baseline.Size(), 0, 0, InterpolationFlags.Area);
                aligned = resized;
            }

            // Edge-diff path: Canny both ROIs and compare. Only shape changes register —
            // useful when the watched area undergoes color drift / lighting changes that
            // would falsely trip a raw pixel diff.
            if (edge)
            {
                grayA = new Mat();
                grayB = new Mat();
                edgeA = new Mat();
                edgeB = new Mat();
                Cv2.CvtColor(aligned, grayA, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(baseline, grayB, ColorConversionCodes.BGR2GRAY);
                Cv2.Canny(grayA, edgeA, 50, 150);
                Cv2.Canny(grayB, edgeB, 50, 150);

                using var edgeDiff = new Mat();
                Cv2.Absdiff(edgeA, edgeB, edgeDiff);
                var edgeMean = Cv2.Mean(edgeDiff).Val0;
                return Math.Clamp(edgeMean / 255.0, 0.0, 1.0);
            }

            using var diff = new Mat();
            Cv2.Absdiff(aligned, baseline, diff);
            // Mean returns per-channel mean; average across channels then normalize to 0..1.
            var mean = Cv2.Mean(diff);
            var avg = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            return Math.Clamp(avg / 255.0, 0.0, 1.0);
        }
        finally
        {
            resized?.Dispose();
            edgeA?.Dispose();
            edgeB?.Dispose();
            grayA?.Dispose();
            grayB?.Dispose();
        }
    }

    public ColorSample ColorAt(CaptureFrame frame, int x, int y)
    {
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
        {
            throw new OperationException("VISION_COORD_OUT_OF_BOUNDS",
                new() { ["x"] = x.ToString(), ["y"] = y.ToString() });
        }

        var pixel = frame.Image.Get<Vec3b>(y, x);
        return new ColorSample(pixel.Item2, pixel.Item1, pixel.Item0); // BGR → RGB
    }

    private static Rect ClampRect(Mat haystack, RegionOfInterest roi)
    {
        var x = Math.Clamp(roi.X, 0, haystack.Width);
        var y = Math.Clamp(roi.Y, 0, haystack.Height);
        var w = Math.Clamp(roi.Width, 0, haystack.Width - x);
        var h = Math.Clamp(roi.Height, 0, haystack.Height - y);
        return new Rect(x, y, w, h);
    }
}
