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
            // Apply AFTER scale so we Resize on the larger BGR image (more accurate kernel)
            // but match on the cheaper single-channel buffer.
            if (options.Grayscale)
            {
                grayHaystack = new Mat();
                grayTemplate = new Mat();
                Cv2.CvtColor(haystack, grayHaystack, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(matchTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);
                haystack = grayHaystack;
                matchTemplate = grayTemplate;
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
        }
    }

    public double PercentBar(CaptureFrame frame, RegionOfInterest roi, ColorSample targetColor, int tolerance)
    {
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");
        var rect = ClampRect(frame.Image, roi);
        if (rect.Width <= 0 || rect.Height <= 0) return 0.0;

        using var crop = new Mat(frame.Image, rect);

        // BGR target so we don't have to swap on every pixel read.
        var lower = new Scalar(
            Math.Max(0, targetColor.B - tolerance),
            Math.Max(0, targetColor.G - tolerance),
            Math.Max(0, targetColor.R - tolerance));
        var upper = new Scalar(
            Math.Min(255, targetColor.B + tolerance),
            Math.Min(255, targetColor.G + tolerance),
            Math.Min(255, targetColor.R + tolerance));

        using var mask = new Mat();
        Cv2.InRange(crop, lower, upper, mask);

        // Count matching pixels via native CountNonZero — vectorized; far faster than
        // walking pixels in C#. Total pixel count is rect area.
        var matched = Cv2.CountNonZero(mask);
        return matched / (double)(rect.Width * rect.Height);
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
                Grayscale: true);

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

            // OpenCV is BGR-ordered.
            var lower = new Scalar(range.BMin, range.GMin, range.RMin);
            var upper = new Scalar(range.BMax, range.GMax, range.RMax);

            using var mask = new Mat();
            Cv2.InRange(haystack, lower, upper, mask);

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

    public double Diff(CaptureFrame frame, Mat baseline, RegionOfInterest currentRoi)
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
        try
        {
            if (crop.Size() != baseline.Size())
            {
                resized = new Mat();
                Cv2.Resize(crop, resized, baseline.Size(), 0, 0, InterpolationFlags.Area);
                aligned = resized;
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
