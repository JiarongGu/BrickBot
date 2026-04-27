using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Detection.Models;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using OpenCvSharp;
using VisionFillDirection = BrickBot.Modules.Vision.Models.FillDirection;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Train a <see cref="DetectionDefinition"/> from labeled <see cref="TrainingSample"/>s.
///
/// Locked-in kinds (everything else has been deleted in the rewrite):
///   • <see cref="DetectionKind.Tracker"/> — one frame + drag-bbox + algorithm.
///   • <see cref="DetectionKind.Pattern"/> — multi-positive samples → ORB descriptor blob.
///   • <see cref="DetectionKind.Text"/>    — one frame + drag-bbox + Tesseract config.
///   • <see cref="DetectionKind.Bar"/>     — multi-fill samples → fill-color + direction.
/// </summary>
public sealed class DetectionTrainerService : IDetectionTrainerService
{
    private readonly IVisionService _vision;

    public DetectionTrainerService(IVisionService vision)
    {
        _vision = vision;
    }

    public TrainingResult Train(string profileId, DetectionKind kind, TrainingSample[] samples, DetectionDefinition? seed)
    {
        if (samples is null || samples.Length == 0)
        {
            throw new OperationException("DETECTION_TRAIN_NEEDS_SAMPLES", new() { ["min"] = "1" });
        }

        // Tracker + Text need only ONE init frame; Pattern + Bar need ≥2 labeled samples.
        if ((kind == DetectionKind.Pattern || kind == DetectionKind.Bar) && samples.Length < 2)
        {
            throw new OperationException("DETECTION_TRAIN_NEEDS_SAMPLES", new() { ["min"] = "2" });
        }

        return kind switch
        {
            DetectionKind.Tracker => TrainTracker(samples, seed),
            DetectionKind.Pattern => TrainPattern(samples, seed),
            DetectionKind.Text    => TrainText(samples, seed),
            DetectionKind.Bar     => TrainBar(samples, seed),
            _ => throw new OperationException("DETECTION_TRAIN_KIND_UNSUPPORTED",
                new() { ["kind"] = kind.ToString() }),
        };
    }

    public RoiSuggestion[] SuggestRois(string[] framesBase64, int maxResults)
    {
        if (framesBase64 is null || framesBase64.Length < 2) return Array.Empty<RoiSuggestion>();

        var frames = framesBase64.Select(DecodeFrame).ToList();
        try
        {
            var w = frames[0].Width;
            var h = frames[0].Height;
            using var meanMat = new Mat(h, w, MatType.CV_32FC3, Scalar.All(0));
            foreach (var f in frames)
            {
                using var f32 = new Mat();
                f.Image.ConvertTo(f32, MatType.CV_32FC3);
                Cv2.Add(meanMat, f32, meanMat);
            }
            Cv2.Multiply(meanMat, new Scalar(1.0 / frames.Count, 1.0 / frames.Count, 1.0 / frames.Count), meanMat);

            using var varMat = new Mat(h, w, MatType.CV_32FC3, Scalar.All(0));
            foreach (var f in frames)
            {
                using var f32 = new Mat();
                f.Image.ConvertTo(f32, MatType.CV_32FC3);
                using var diff = new Mat();
                Cv2.Absdiff(f32, meanMat, diff);
                Cv2.Multiply(diff, diff, diff);
                Cv2.Add(varMat, diff, varMat);
            }

            using var grayVar = new Mat();
            Cv2.CvtColor(varMat, grayVar, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(grayVar, blurred, new OpenCvSharp.Size(31, 31), 0);

            var globalMean = Cv2.Mean(blurred).Val0;
            var threshold = Math.Max(globalMean * 1.5, 50);

            using var binMask = new Mat();
            Cv2.Threshold(blurred, binMask, threshold, 255, ThresholdTypes.Binary);
            using var bin8 = new Mat();
            binMask.ConvertTo(bin8, MatType.CV_8UC1);

            Cv2.FindContours(bin8, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            var suggestions = contours
                .Select(c => Cv2.BoundingRect(c))
                .Where(r => r.Width > 8 && r.Height > 8)
                .OrderByDescending(r => r.Width * r.Height)
                .Take(Math.Max(1, maxResults))
                .Select(r => new RoiSuggestion {
                    X = r.X, Y = r.Y, W = r.Width, H = r.Height,
                    Score = r.Width * r.Height, Reason = "high-variance region"
                })
                .ToArray();
            return suggestions;
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // ============================================================
    //  Tracker — one frame + bbox + algo
    // ============================================================

    private TrainingResult TrainTracker(TrainingSample[] samples, DetectionDefinition? seed)
    {
        var trk = seed?.Tracker ?? new TrackerOptions();
        if (trk.InitW <= 0 || trk.InitH <= 0)
        {
            throw new OperationException("DETECTION_TRAIN_TRACKER_NEEDS_BBOX");
        }

        // Re-encode the first sample as PNG so the runtime decoder always sees a clean blob.
        using var firstFrame = DecodeFrame(samples[0].ImageBase64);
        var pngBytes = firstFrame.Image.ImEncode(".png");
        trk.InitFramePng = Convert.ToBase64String(pngBytes);

        var suggested = ApplySeed(seed, DetectionKind.Tracker, "Trained Tracker", roi: null,
            d => d.Tracker = trk);

        return new TrainingResult
        {
            Suggested = suggested,
            Diagnostics = Array.Empty<TrainingDiagnostic>(),
            Summary = $"tracker {trk.Algorithm.ToString().ToLowerInvariant()} · " +
                      $"init bbox ({trk.InitX},{trk.InitY}) {trk.InitW}×{trk.InitH} · " +
                      $"reacquire-on-lost {(trk.ReacquireOnLost ? "on" : "off")}",
        };
    }

    // ============================================================
    //  Pattern — ORB descriptor extraction from positives
    // ============================================================

    /// <summary>
    /// Pattern trainer: extract ORB keypoints + descriptors from each positive sample's ROI
    /// crop, take the union of descriptors as the model. Negatives are scored against the
    /// trained blob and inform the confidence threshold (we drop the worst-performing
    /// descriptors when negatives match too many — but for v1 we just use all positives'
    /// descriptors and tune the min-confidence cutoff).
    /// </summary>
    private TrainingResult TrainPattern(TrainingSample[] samples, DetectionDefinition? seed)
    {
        var frames = samples.Select(s => DecodeFrame(s.ImageBase64)).ToList();
        try
        {
            var positives = new List<int>();
            var negatives = new List<int>();
            for (var i = 0; i < samples.Length; i++)
            {
                if (IsPositive(samples[i].Label)) positives.Add(i);
                else negatives.Add(i);
            }
            if (positives.Count == 0)
            {
                throw new OperationException("DETECTION_TRAIN_NEEDS_POSITIVES");
            }

            var anchorIdx = positives[0];
            var roi = ResolveRoi(samples[anchorIdx].Roi ?? seed?.Roi, frames[anchorIdx].Width, frames[anchorIdx].Height)
                ?? throw new OperationException("DETECTION_TRAIN_PATTERN_NEEDS_ROI");

            // Crop each positive at the trained ROI, extract ORB keypoints + descriptors,
            // and concatenate into the trained model. Limit to ~150 descriptors per sample
            // to keep the stored blob size reasonable.
            const int maxKeypointsPerSample = 150;
            var allDescriptorRows = new List<Mat>();
            foreach (var i in positives)
            {
                var rect = ClampRect(frames[i].Image, roi);
                if (rect.Width < 16 || rect.Height < 16) continue;
                using var crop = new Mat(frames[i].Image, rect);
                var (_, descriptors) = _vision.ExtractDescriptors(crop, maxKeypointsPerSample);
                if (!descriptors.Empty()) allDescriptorRows.Add(descriptors);
                else descriptors.Dispose();
            }

            if (allDescriptorRows.Count == 0)
            {
                throw new OperationException("DETECTION_TRAIN_PATTERN_NO_FEATURES");
            }

            using var trainedDescriptors = new Mat();
            Cv2.VConcat(allDescriptorRows.ToArray(), trainedDescriptors);
            foreach (var m in allDescriptorRows) m.Dispose();

            // Crop the anchor positive as the reference patch for re-training / overlay.
            var anchorRect = ClampRect(frames[anchorIdx].Image, roi);
            using var refPatch = new Mat(frames[anchorIdx].Image, anchorRect).Clone();
            var refPng = Convert.ToBase64String(refPatch.ImEncode(".png"));

            // Serialize descriptors as raw bytes (rows × 32). Binary blob in base64 keeps the
            // detection JSON-portable.
            // Descriptor width is 32 (ORB) or 64 (BRISK). Read it from the Mat instead of
            // hard-coding so the trainer survives detector-algorithm swaps in VisionService.
            var descBytes = new byte[trainedDescriptors.Rows * trainedDescriptors.Cols];
            System.Runtime.InteropServices.Marshal.Copy(trainedDescriptors.Data, descBytes, 0, descBytes.Length);
            var descBase64 = Convert.ToBase64String(descBytes);

            var pat = seed?.Pattern ?? new PatternOptions();
            pat.EmbeddedPng = refPng;
            pat.Descriptors = descBase64;
            pat.KeypointCount = trainedDescriptors.Rows;
            pat.TemplateWidth = anchorRect.Width;
            pat.TemplateHeight = anchorRect.Height;

            // Quick negative scoring — count how many trained-keypoint matches each negative
            // produces; tune min-confidence to be just above the worst negative.
            var negMatchCounts = new List<double>();
            foreach (var ni in negatives)
            {
                var match = _vision.MatchPattern(frames[ni], trainedDescriptors,
                    new PatternMatchOptions(roi, pat.LoweRatio, 0.0,
                        pat.MaxRuntimeKeypoints, pat.TemplateWidth, pat.TemplateHeight));
                negMatchCounts.Add(match?.Confidence ?? 0.0);
            }
            var posMatchCounts = new List<double>();
            foreach (var pi in positives)
            {
                var match = _vision.MatchPattern(frames[pi], trainedDescriptors,
                    new PatternMatchOptions(roi, pat.LoweRatio, 0.0,
                        pat.MaxRuntimeKeypoints, pat.TemplateWidth, pat.TemplateHeight));
                posMatchCounts.Add(match?.Confidence ?? 0.0);
            }

            var posMin = posMatchCounts.Count > 0 ? posMatchCounts.Min() : 0;
            var negMax = negMatchCounts.Count > 0 ? negMatchCounts.Max() : 0;
            // Set MinConfidence between the worst positive and best negative when separable;
            // fall back to 80 % of the worst positive when they overlap.
            pat.MinConfidence = posMin > negMax
                ? Math.Round((posMin + negMax) / 2.0, 2)
                : Math.Max(0.10, Math.Round(posMin * 0.80, 2));

            var suggested = ApplySeed(seed, DetectionKind.Pattern, "Trained Pattern", roi,
                d => d.Pattern = pat);

            var diagnostics = new TrainingDiagnostic[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var match = _vision.MatchPattern(frames[i], trainedDescriptors,
                    new PatternMatchOptions(roi, pat.LoweRatio, 0.0,
                        pat.MaxRuntimeKeypoints, pat.TemplateWidth, pat.TemplateHeight));
                var conf = match?.Confidence ?? 0.0;
                var predicted = conf >= pat.MinConfidence;
                var labelPos = IsPositive(samples[i].Label);
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labelPos ? "true" : "false",
                    Predicted = $"{(predicted ? "true" : "false")} ({conf:0.00})",
                    Error = labelPos == predicted ? 0.0 : 1.0,
                };
            }

            return new TrainingResult
            {
                Suggested = suggested,
                Diagnostics = diagnostics,
                Summary = $"pattern {pat.TemplateWidth}×{pat.TemplateHeight} · " +
                          $"{pat.KeypointCount} keypoints · minConf {pat.MinConfidence:0.00} · " +
                          $"pos≥{posMin:0.00} neg≤{negMax:0.00}",
            };
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // ============================================================
    //  Text — Tesseract (one frame + bbox + language)
    // ============================================================

    private TrainingResult TrainText(TrainingSample[] samples, DetectionDefinition? seed)
    {
        var txt = seed?.Text ?? new TextOptions();

        // The runner does the actual OCR — training just packages the user's chosen ROI +
        // language + page-segmentation mode. The first sample's ROI is what was selected.
        var roi = samples[0].Roi is { } r
            ? new RegionOfInterest(r.X, r.Y, r.W, r.H)
            : seed?.Roi is { } sr
                ? new RegionOfInterest(sr.X, sr.Y, sr.W, sr.H)
                : throw new OperationException("DETECTION_TRAIN_TEXT_NEEDS_ROI");

        var suggested = ApplySeed(seed, DetectionKind.Text, "Trained Text", roi,
            d => d.Text = txt);

        return new TrainingResult
        {
            Suggested = suggested,
            Diagnostics = Array.Empty<TrainingDiagnostic>(),
            Summary = $"text {roi.Width}×{roi.Height} · lang {txt.Language} · " +
                      $"psm {txt.PageSegMode} · binarize {(txt.Binarize ? "on" : "off")} · " +
                      $"upscale {txt.UpscaleFactor:0.0}× · minConf {txt.MinConfidence}",
        };
    }

    // ============================================================
    //  Bar — fill-color + direction inference from labeled samples
    // ============================================================

    /// <summary>
    /// Bar trainer: each sample is labeled with its expected fill ratio (0..1). Picks the
    /// dominant fill color from the highest-fill sample, infers fill direction by comparing
    /// fill measurements at hi/lo labels in each direction, and sweeps the line-fill threshold
    /// to minimize prediction error.
    /// </summary>
    private TrainingResult TrainBar(TrainingSample[] samples, DetectionDefinition? seed)
    {
        var frames = samples.Select(s => DecodeFrame(s.ImageBase64)).ToList();
        try
        {
            var labels = ParseNumericLabels(samples);
            var hiIdx = Array.IndexOf(labels, labels.Max());
            var roi = ResolveRoi(samples[hiIdx].Roi ?? seed?.Roi, frames[hiIdx].Width, frames[hiIdx].Height)
                ?? throw new OperationException("DETECTION_TRAIN_BAR_NEEDS_ROI");

            var color = MedianColor(frames[hiIdx], roi);
            var tolerance = EstimateTolerance(frames, roi);
            var direction = InferDirection(frames, labels, roi, color, tolerance);
            var (lineThreshold, meanError) = SweepLineThreshold(frames, labels, roi, color, tolerance, direction);

            var bar = seed?.Bar ?? new BarOptions();
            bar.FillColor = color;
            bar.Tolerance = tolerance;
            bar.Direction = direction;
            bar.LineThreshold = lineThreshold;

            var suggested = ApplySeed(seed, DetectionKind.Bar, "Trained Bar", roi,
                d => d.Bar = bar);

            var diagnostics = new TrainingDiagnostic[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = new ColorSample(color.R, color.G, color.B);
                var fill = _vision.LinearFillRatio(frames[i], roi, sample, tolerance,
                    ColorSpace.Rgb, direction, lineThreshold);
                var error = Math.Abs(fill - labels[i]);
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labels[i].ToString("0.00"),
                    Predicted = fill.ToString("0.00"),
                    Error = error,
                };
            }

            return new TrainingResult
            {
                Suggested = suggested,
                Diagnostics = diagnostics,
                Summary = $"bar fill rgb({color.R},{color.G},{color.B}) ±{tolerance} · " +
                          $"dir {direction.ToString()} · lineThreshold {lineThreshold:0.00} · " +
                          $"mean error {meanError:0.000}",
            };
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private static bool IsPositive(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        var l = label.Trim().ToLowerInvariant();
        return l == "true" || l == "yes" || l == "1" || l == "+" || l == "positive";
    }

    private DetectionDefinition ApplySeed(DetectionDefinition? seed, DetectionKind kind, string defaultName,
        RegionOfInterest? roi, Action<DetectionDefinition> apply)
    {
        var def = seed is null
            ? new DetectionDefinition
            {
                Id = "",
                Name = defaultName,
                Kind = kind,
                Enabled = true,
                Roi = roi is not null ? ToDetectionRoi(roi) : null,
                Output = new DetectionOutput { EventOnChangeOnly = true },
            }
            : new DetectionDefinition
            {
                Id = seed.Id,
                Name = string.IsNullOrEmpty(seed.Name) ? defaultName : seed.Name,
                Kind = kind,
                Group = seed.Group,
                Enabled = seed.Enabled,
                Roi = seed.Roi ?? (roi is not null ? ToDetectionRoi(roi) : null),
                Output = seed.Output,
            };
        apply(def);
        return def;
    }

    private static double[] ParseNumericLabels(TrainingSample[] samples) =>
        samples.Select((s, i) =>
        {
            if (!double.TryParse(s.Label, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                throw new OperationException("DETECTION_TRAIN_INVALID_LABEL",
                    new() { ["index"] = i.ToString(), ["label"] = s.Label });
            }
            return Math.Clamp(v, 0.0, 1.0);
        }).ToArray();

    private static CaptureFrame DecodeFrame(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty()) throw new OperationException("DETECTION_TRAIN_DECODE_FAILED");
        return new CaptureFrame(mat, 0, DateTimeOffset.UtcNow);
    }

    private static RgbColor MedianColor(CaptureFrame frame, RegionOfInterest roi)
    {
        var rect = ClampRect(frame.Image, roi);
        using var crop = new Mat(frame.Image, rect);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        var meanBrightness = (int)Cv2.Mean(gray).Val0;
        var brightSet = new List<Vec3b>(rect.Width * rect.Height / 2);
        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                if (gray.Get<byte>(y, x) >= meanBrightness) brightSet.Add(crop.Get<Vec3b>(y, x));
            }
        }
        if (brightSet.Count == 0) return new RgbColor(255, 255, 255);
        var bs = brightSet.Select(p => (int)p.Item0).OrderBy(v => v).ToArray();
        var gs = brightSet.Select(p => (int)p.Item1).OrderBy(v => v).ToArray();
        var rs = brightSet.Select(p => (int)p.Item2).OrderBy(v => v).ToArray();
        return new RgbColor(rs[rs.Length / 2], gs[gs.Length / 2], bs[bs.Length / 2]);
    }

    private static int EstimateTolerance(List<CaptureFrame> frames, RegionOfInterest roi)
    {
        var means = frames.Select(f =>
        {
            var rect = ClampRect(f.Image, roi);
            using var crop = new Mat(f.Image, rect);
            var m = Cv2.Mean(crop);
            return (R: (int)m.Val2, G: (int)m.Val1, B: (int)m.Val0);
        }).ToArray();
        if (means.Length == 0) return 30;
        var spreadR = means.Max(m => m.R) - means.Min(m => m.R);
        var spreadG = means.Max(m => m.G) - means.Min(m => m.G);
        var spreadB = means.Max(m => m.B) - means.Min(m => m.B);
        var spread = Math.Max(spreadR, Math.Max(spreadG, spreadB));
        return Math.Clamp(spread + 25, 25, 90);
    }

    private VisionFillDirection InferDirection(
        List<CaptureFrame> frames, double[] labels, RegionOfInterest roi, RgbColor color, int tolerance)
    {
        if (frames.Count < 2) return VisionFillDirection.LeftToRight;
        var hi = Array.IndexOf(labels, labels.Max());
        var lo = Array.IndexOf(labels, labels.Min());
        if (hi == lo) return VisionFillDirection.LeftToRight;
        var sample = new ColorSample(color.R, color.G, color.B);
        var directions = new[]
        {
            VisionFillDirection.LeftToRight, VisionFillDirection.RightToLeft,
            VisionFillDirection.TopToBottom, VisionFillDirection.BottomToTop,
        };
        var labelDelta = labels[hi] - labels[lo];
        var best = VisionFillDirection.LeftToRight;
        var bestScore = double.NegativeInfinity;
        foreach (var dir in directions)
        {
            var hiFill = _vision.LinearFillRatio(frames[hi], roi, sample, tolerance, ColorSpace.Rgb, dir, 0.4);
            var loFill = _vision.LinearFillRatio(frames[lo], roi, sample, tolerance, ColorSpace.Rgb, dir, 0.4);
            var score = (hiFill - loFill) * Math.Sign(labelDelta);
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }

    private (double threshold, double meanError) SweepLineThreshold(
        List<CaptureFrame> frames, double[] labels, RegionOfInterest roi, RgbColor color, int tolerance,
        VisionFillDirection direction)
    {
        var sample = new ColorSample(color.R, color.G, color.B);
        var bestThreshold = 0.4;
        var bestErr = double.PositiveInfinity;
        for (var t = 0.20; t <= 0.81; t += 0.05)
        {
            var totalErr = 0.0;
            for (var i = 0; i < frames.Count; i++)
            {
                var fill = _vision.LinearFillRatio(frames[i], roi, sample, tolerance, ColorSpace.Rgb, direction, t);
                totalErr += Math.Abs(fill - labels[i]);
            }
            var meanErr = totalErr / frames.Count;
            if (meanErr < bestErr) { bestErr = meanErr; bestThreshold = t; }
        }
        return (bestThreshold, bestErr);
    }

    private static RegionOfInterest? ResolveRoi(DetectionRoi? r, int frameW, int frameH)
    {
        if (r is null) return null;
        var w = r.W <= 0 ? frameW : r.W;
        var h = r.H <= 0 ? frameH : r.H;
        return new RegionOfInterest(r.X, r.Y, w, h);
    }

    private static DetectionRoi ToDetectionRoi(RegionOfInterest r) =>
        new() { X = r.X, Y = r.Y, W = r.Width, H = r.Height };

    private static Rect ClampRect(Mat haystack, RegionOfInterest roi)
    {
        var x = Math.Clamp(roi.X, 0, haystack.Width);
        var y = Math.Clamp(roi.Y, 0, haystack.Height);
        var w = Math.Clamp(roi.Width, 0, haystack.Width - x);
        var h = Math.Clamp(roi.Height, 0, haystack.Height - y);
        return new Rect(x, y, w, h);
    }
}
