using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Detection.Models;
using BrickBot.Modules.Template.Services;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using OpenCvSharp;
using VisionFillDirection = BrickBot.Modules.Vision.Models.FillDirection;

namespace BrickBot.Modules.Detection.Services;

public sealed class DetectionTrainerService : IDetectionTrainerService
{
    private readonly IDetectionRunner _runner;
    private readonly IVisionService _vision;
    private readonly ITemplateFileService _templateFiles;

    public DetectionTrainerService(IDetectionRunner runner, IVisionService vision, ITemplateFileService templateFiles)
    {
        _runner = runner;
        _vision = vision;
        _templateFiles = templateFiles;
    }

    public TrainingResult Train(string profileId, DetectionKind kind, TrainingSample[] samples, DetectionDefinition? seed)
    {
        if (samples is null || samples.Length < 2)
        {
            throw new OperationException("DETECTION_TRAIN_NEEDS_SAMPLES",
                new() { ["min"] = "2" });
        }

        return kind switch
        {
            DetectionKind.ProgressBar => TrainProgressBar(profileId, samples, seed),
            DetectionKind.Template => TrainElement(profileId, samples, seed),
            DetectionKind.ColorPresence => TrainColorPresence(profileId, samples, seed),
            DetectionKind.Effect => TrainEffect(profileId, samples, seed),
            DetectionKind.FeatureMatch => TrainFeatureMatch(profileId, samples, seed),
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
            // Compute per-pixel std-dev across frames → highlights regions with motion / animation.
            var w = frames[0].Width;
            var h = frames[0].Height;
            using var meanMat = new Mat(h, w, MatType.CV_32FC3, Scalar.All(0));
            foreach (var f in frames)
            {
                using var f32 = new Mat();
                f.Image.ConvertTo(f32, MatType.CV_32FC3);
                Cv2.Add(meanMat, f32, meanMat);
            }
            using var dummy = new Mat();
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
            // Reduce to single-channel magnitude.
            using var varGray = new Mat();
            Cv2.CvtColor(varMat, varGray, ColorConversionCodes.BGR2GRAY);

            // Threshold at 95th percentile → keep high-variance pixels only.
            using var var8u = new Mat();
            varGray.ConvertTo(var8u, MatType.CV_8U, 0.005);
            using var thresh = new Mat();
            Cv2.Threshold(var8u, thresh, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
            using var dilated = new Mat();
            Cv2.MorphologyEx(thresh, dilated, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7)));

            Cv2.FindContours(dilated, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            var suggestions = contours
                .Select(c => Cv2.BoundingRect(c))
                .Where(r => r.Width >= 10 && r.Height >= 4 && r.Width * r.Height >= 200)
                .OrderByDescending(r => r.Width * (long)r.Height)
                .Take(maxResults)
                .Select((r, i) => new RoiSuggestion
                {
                    X = r.X, Y = r.Y, W = r.Width, H = r.Height,
                    Score = 1.0 - (i * 0.1),
                    Reason = $"high motion · {r.Width}×{r.Height}",
                })
                .ToArray();
            return suggestions;
        }
        finally
        {
            foreach (var f in frames) f.Dispose();
        }
    }

    // ---------------- ProgressBar trainer ----------------

    /// <summary>
    /// Bar trainer (unchanged from prior — see git history for full algorithm doc).
    /// Pipeline: median fill color from highest-fill sample → tolerance from cross-sample
    /// channel spread → direction inferred via hi/lo fill comparison → lineThreshold sweep.
    /// </summary>
    private TrainingResult TrainProgressBar(string profileId, TrainingSample[] samples, DetectionDefinition? seed)
    {
        var frames = samples.Select(s => DecodeFrame(s.ImageBase64)).ToList();
        try
        {
            var labels = ParseNumericLabels(samples);

            var maxIdx = Array.IndexOf(labels, labels.Max());
            var refFrame = frames[maxIdx];
            var refSample = samples[maxIdx];

            var roi = ResolveRoi(refSample.Roi ?? seed?.Roi, refFrame.Width, refFrame.Height)
                      ?? new RegionOfInterest(0, refFrame.Height * 6 / 10, refFrame.Width, Math.Max(8, refFrame.Height / 30));

            var fillColor = MedianColor(refFrame, roi);
            var tolerance = EstimateTolerance(frames, roi);
            var direction = InferDirection(frames, labels, roi, fillColor, tolerance);
            var (bestThreshold, bestErr) = SweepLineThreshold(frames, labels, roi, fillColor, tolerance, direction);

            var pb = seed?.ProgressBar ?? new ProgressBarOptions();
            pb.FillColor = new RgbColor(fillColor.R, fillColor.G, fillColor.B);
            pb.Tolerance = tolerance;
            pb.Direction = direction;
            pb.LineThreshold = bestThreshold;
            pb.ColorSpace = ColorSpace.Rgb;
            pb.InsetLeftPct = 0.0;
            pb.InsetRightPct = 0.0;

            var suggested = ApplySeed(seed, DetectionKind.ProgressBar, "Trained Progress Bar", roi,
                d => d.ProgressBar = pb);

            var diagnostics = new TrainingDiagnostic[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var predicted = _vision.LinearFillRatio(frames[i], roi,
                    new ColorSample(fillColor.R, fillColor.G, fillColor.B), tolerance,
                    ColorSpace.Rgb, direction, bestThreshold);
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labels[i].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                    Predicted = predicted.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                    Error = Math.Abs(predicted - labels[i]),
                };
            }

            return new TrainingResult
            {
                Suggested = suggested,
                Diagnostics = diagnostics,
                Summary = $"color rgb({fillColor.R},{fillColor.G},{fillColor.B}) · tolerance ±{tolerance} · " +
                          $"dir {direction} · lineThreshold {bestThreshold:0.00} · mean abs err {bestErr:0.000}",
            };
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // ---------------- Element / Template trainer ----------------

    /// <summary>
    /// Element trainer: positives + negatives. Builds a robust template from positives via
    /// per-pixel median across aligned crops, saves it to the template store, then tunes
    /// minConfidence to maximally separate positives from negatives.
    ///
    /// Sample label semantics: <c>"true"</c> / <c>"yes"</c> / <c>"1"</c> = positive (element present);
    /// anything else = negative.
    /// </summary>
    private TrainingResult TrainElement(string profileId, TrainingSample[] samples, DetectionDefinition? seed)
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

            // Use the first positive's ROI (or seed ROI) as the template footprint.
            var anchorIdx = positives[0];
            var anchorFrame = frames[anchorIdx];
            var roi = ResolveRoi(samples[anchorIdx].Roi ?? seed?.Roi, anchorFrame.Width, anchorFrame.Height)
                ?? throw new OperationException("DETECTION_TRAIN_ELEMENT_NEEDS_ROI");

            // Crop each positive at the same ROI; median across aligned pixels = robust template.
            using var medianTemplate = MedianTemplate(frames, positives, roi);

            // Embed PNG bytes directly in the definition. No external Templates-table row →
            // detection is fully self-contained, can be exported / shared without dragging the
            // template store along with it.
            var pngBytes = medianTemplate.ImEncode(".png");
            var embeddedPng = Convert.ToBase64String(pngBytes);

            // Probe positives + negatives with a sweep of confidence thresholds, pick the value
            // that maximizes (true positives - false positives).
            using var templateMat = Cv2.ImDecode(pngBytes, ImreadModes.Color);
            var posScores = positives.Select(i => MaxConfidence(frames[i], templateMat, roi)).ToArray();
            var negScores = negatives.Select(i => MaxConfidence(frames[i], templateMat, roi)).ToArray();
            var bestConfidence = TuneConfidence(posScores, negScores);

            var tpl = seed?.Template ?? new TemplateOptions();
            tpl.TemplateName = "";        // embedded path supersedes external lookup
            tpl.EmbeddedPng = embeddedPng;
            tpl.MinConfidence = bestConfidence;
            tpl.Grayscale = true;
            tpl.Edge = false;
            tpl.Pyramid = false;

            var suggested = ApplySeed(seed, DetectionKind.Template, "Trained Element", roi, d => d.Template = tpl);

            var diagnostics = new TrainingDiagnostic[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var conf = MaxConfidence(frames[i], templateMat, roi);
                var predicted = conf >= bestConfidence;
                var labelPos = IsPositive(samples[i].Label);
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labelPos ? "true" : "false",
                    Predicted = $"{(predicted ? "true" : "false")} ({conf:0.00})",
                    Error = labelPos == predicted ? 0.0 : 1.0,
                };
            }

            var posMin = posScores.Length > 0 ? posScores.Min() : 0;
            var negMax = negScores.Length > 0 ? negScores.Max() : 0;
            return new TrainingResult
            {
                Suggested = suggested,
                Diagnostics = diagnostics,
                Summary = $"embedded template ({medianTemplate.Width}×{medianTemplate.Height}) · " +
                          $"minConf {bestConfidence:0.00} · pos≥{posMin:0.00} neg≤{negMax:0.00}",
            };
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // ---------------- ColorPresence trainer ----------------

    /// <summary>
    /// ColorPresence trainer: each sample labeled with expected blob count. Picks the dominant
    /// non-background color from the highest-count sample, then sweeps tolerance + minArea to
    /// minimize prediction error.
    /// </summary>
    private TrainingResult TrainColorPresence(string profileId, TrainingSample[] samples, DetectionDefinition? seed)
    {
        var frames = samples.Select(s => DecodeFrame(s.ImageBase64)).ToList();
        try
        {
            var labels = samples.Select(s => int.TryParse(s.Label, out var n) ? n : 0).ToArray();
            var maxIdx = Array.IndexOf(labels, labels.Max());
            var refFrame = frames[maxIdx];

            var roi = ResolveRoi(samples[maxIdx].Roi ?? seed?.Roi, refFrame.Width, refFrame.Height)
                ?? new RegionOfInterest(0, 0, refFrame.Width, refFrame.Height);

            // Dominant saturated color in ROI = mode of hue from saturated pixels.
            var color = DominantColor(refFrame, roi);

            // Sweep tolerance × minArea, pick combination that minimizes label error.
            int bestTol = 30, bestArea = 100;
            var bestErr = double.PositiveInfinity;
            for (var tol = 15; tol <= 75; tol += 10)
            {
                for (var area = 25; area <= 500; area += 50)
                {
                    var totalErr = 0.0;
                    for (var i = 0; i < frames.Count; i++)
                    {
                        var range = new ColorRange(
                            Math.Max(0, color.R - tol), Math.Min(255, color.R + tol),
                            Math.Max(0, color.G - tol), Math.Min(255, color.G + tol),
                            Math.Max(0, color.B - tol), Math.Min(255, color.B + tol));
                        var blobs = _vision.FindColors(frames[i], range,
                            new FindColorsOptions(roi, area, 64, ColorSpace.Rgb));
                        totalErr += Math.Abs(blobs.Count - labels[i]);
                    }
                    var meanErr = totalErr / frames.Count;
                    if (meanErr < bestErr) { bestErr = meanErr; bestTol = tol; bestArea = area; }
                }
            }

            var cp = seed?.ColorPresence ?? new ColorPresenceOptions();
            cp.Color = color;
            cp.Tolerance = bestTol;
            cp.MinArea = bestArea;
            cp.MaxResults = Math.Max(8, labels.Max() * 2);
            cp.ColorSpace = ColorSpace.Rgb;

            var suggested = ApplySeed(seed, DetectionKind.ColorPresence, "Trained Color Presence", roi,
                d => d.ColorPresence = cp);

            var diagnostics = new TrainingDiagnostic[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var range = new ColorRange(
                    Math.Max(0, color.R - bestTol), Math.Min(255, color.R + bestTol),
                    Math.Max(0, color.G - bestTol), Math.Min(255, color.G + bestTol),
                    Math.Max(0, color.B - bestTol), Math.Min(255, color.B + bestTol));
                var n = _vision.FindColors(frames[i], range, new FindColorsOptions(roi, bestArea, 64, ColorSpace.Rgb)).Count;
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labels[i].ToString(),
                    Predicted = n.ToString(),
                    Error = Math.Abs(n - labels[i]),
                };
            }

            return new TrainingResult
            {
                Suggested = suggested,
                Diagnostics = diagnostics,
                Summary = $"color rgb({color.R},{color.G},{color.B}) · tol ±{bestTol} · minArea {bestArea} · " +
                          $"mean abs err {bestErr:0.0}",
            };
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // ---------------- Effect trainer ----------------

    /// <summary>
    /// Effect trainer: samples labeled "quiet" (effect absent) vs "trigger" (effect active).
    /// Threshold = midpoint between max quiet/quiet diff and min quiet/trigger diff. At
    /// runtime <c>autoBaseline=true</c> snapshots the first frame; the threshold trips
    /// when subsequent frames differ enough.
    /// </summary>
    private TrainingResult TrainEffect(string profileId, TrainingSample[] samples, DetectionDefinition? seed)
    {
        var frames = samples.Select(s => DecodeFrame(s.ImageBase64)).ToList();
        try
        {
            var quiet = new List<int>();
            var trigger = new List<int>();
            for (var i = 0; i < samples.Length; i++)
            {
                var label = (samples[i].Label ?? "").Trim().ToLowerInvariant();
                if (label == "trigger" || label == "true" || label == "1" || label == "yes" || label == "+")
                    trigger.Add(i);
                else
                    quiet.Add(i);
            }
            if (quiet.Count == 0 || trigger.Count == 0)
            {
                throw new OperationException("DETECTION_TRAIN_EFFECT_NEEDS_BOTH");
            }

            var anchorFrame = frames[quiet[0]];
            var roi = ResolveRoi(samples[quiet[0]].Roi ?? seed?.Roi, anchorFrame.Width, anchorFrame.Height)
                ?? new RegionOfInterest(0, 0, anchorFrame.Width, anchorFrame.Height);

            using var baseline = SnapshotBaseline(frames[quiet[0]], roi);

            var quietDiffs = quiet.Select(qi => _vision.Diff(frames[qi], baseline, roi, edge: false)).ToList();
            var triggerDiffs = trigger.Select(ti => _vision.Diff(frames[ti], baseline, roi, edge: false)).ToList();

            var noiseMax = quietDiffs.Max();
            var signalMin = triggerDiffs.Min();
            var threshold = noiseMax < signalMin
                ? Math.Round((noiseMax + signalMin) / 2.0, 3)
                : Math.Round(Math.Max(0.05, signalMin - 0.02), 3);

            // Embed the chosen quiet sample as the baseline so the runtime doesn't have to
            // gamble on whatever frame happens to land first. AutoBaseline=false because we
            // pinned an explicit one.
            var baselinePngBytes = baseline.ImEncode(".png");
            var ef = seed?.Effect ?? new EffectOptions();
            ef.Threshold = threshold;
            ef.AutoBaseline = false;
            ef.Edge = false;
            ef.EmbeddedBaselinePng = Convert.ToBase64String(baselinePngBytes);

            var suggested = ApplySeed(seed, DetectionKind.Effect, "Trained Effect", roi, d => d.Effect = ef);

            var diagnostics = new TrainingDiagnostic[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var diff = _vision.Diff(frames[i], baseline, roi, edge: false);
                var triggered = diff >= threshold;
                var labelTrigger = trigger.Contains(i);
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labelTrigger ? "trigger" : "quiet",
                    Predicted = $"{(triggered ? "trigger" : "quiet")} ({diff:0.000})",
                    Error = triggered == labelTrigger ? 0.0 : 1.0,
                };
            }

            return new TrainingResult
            {
                Suggested = suggested,
                Diagnostics = diagnostics,
                Summary = $"threshold {threshold:0.000} · noise≤{noiseMax:0.000} signal≥{signalMin:0.000}",
            };
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    private static Mat SnapshotBaseline(CaptureFrame frame, RegionOfInterest roi)
    {
        var rect = ClampRect(frame.Image, roi);
        return new Mat(frame.Image, rect).Clone();
    }

    // ---------------- FeatureMatch trainer ----------------

    /// <summary>
    /// FeatureMatch trainer: builds a robust template via the Element pipeline (median across
    /// positives), then wraps it in a multi-scale FeatureMatchOptions. Same labeling convention
    /// as Element (true / false). Multi-scale handles UI scaling differences across resolutions.
    /// </summary>
    private TrainingResult TrainFeatureMatch(string profileId, TrainingSample[] samples, DetectionDefinition? seed)
    {
        var elementResult = TrainElement(profileId, samples, seed);
        var elementSuggested = elementResult.Suggested
            ?? throw new OperationException("DETECTION_TRAIN_KIND_UNSUPPORTED",
                new() { ["kind"] = "featureMatch" });

        var fm = seed?.FeatureMatch ?? new BrickBot.Modules.Detection.Models.FeatureMatchOptions();
        if (elementSuggested.Template is not null)
        {
            fm.TemplateName = elementSuggested.Template.TemplateName;
            fm.EmbeddedPng = elementSuggested.Template.EmbeddedPng;
            fm.MinConfidence = Math.Max(0.7, elementSuggested.Template.MinConfidence - 0.05);
            fm.Grayscale = elementSuggested.Template.Grayscale;
            fm.Edge = elementSuggested.Template.Edge;
        }
        if (fm.ScaleSteps <= 1) fm.ScaleSteps = 3;
        if (fm.ScaleMin >= fm.ScaleMax) { fm.ScaleMin = 0.9; fm.ScaleMax = 1.1; }

        var converted = new DetectionDefinition
        {
            Id = elementSuggested.Id,
            Name = string.IsNullOrEmpty(elementSuggested.Name) ? "Trained Sprite" : elementSuggested.Name,
            Kind = DetectionKind.FeatureMatch,
            Group = elementSuggested.Group,
            Enabled = elementSuggested.Enabled,
            Roi = elementSuggested.Roi,
            FeatureMatch = fm,
            Output = elementSuggested.Output,
        };

        return new TrainingResult
        {
            Suggested = converted,
            Diagnostics = elementResult.Diagnostics,
            Summary = elementResult.Summary + $" · scales {fm.ScaleMin:0.00}..{fm.ScaleMax:0.00} × {fm.ScaleSteps}",
        };
    }

    // ---------------- Helpers ----------------

    private static bool IsPositive(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        var l = label.Trim().ToLowerInvariant();
        return l == "true" || l == "yes" || l == "1" || l == "+" || l == "positive";
    }

    private static Mat MedianTemplate(List<CaptureFrame> frames, List<int> indices, RegionOfInterest roi)
    {
        var crops = indices.Select(i =>
        {
            var rect = ClampRect(frames[i].Image, roi);
            return new Mat(frames[i].Image, rect).Clone();
        }).ToList();
        try
        {
            // All crops share the same dimensions (roi is fixed). Per-pixel median across the stack.
            var w = crops[0].Width;
            var h = crops[0].Height;
            var result = new Mat(h, w, MatType.CV_8UC3);
            var rs = new byte[crops.Count];
            var gs = new byte[crops.Count];
            var bs = new byte[crops.Count];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    for (var k = 0; k < crops.Count; k++)
                    {
                        var px = crops[k].Get<Vec3b>(y, x);
                        bs[k] = px.Item0; gs[k] = px.Item1; rs[k] = px.Item2;
                    }
                    Array.Sort(rs); Array.Sort(gs); Array.Sort(bs);
                    var mid = crops.Count / 2;
                    result.Set(y, x, new Vec3b(bs[mid], gs[mid], rs[mid]));
                }
            }
            return result;
        }
        finally { foreach (var c in crops) c.Dispose(); }
    }

    private double MaxConfidence(CaptureFrame frame, Mat template, RegionOfInterest roi)
    {
        var match = _vision.Find(frame, template, new FindOptions(0.0, roi, 1.0, Grayscale: true));
        return match?.Confidence ?? 0.0;
    }

    private static double TuneConfidence(double[] positives, double[] negatives)
    {
        // Best threshold = midpoint between worst positive and best negative if separable.
        // Fall back to (worst positive - epsilon) when the sets overlap.
        if (positives.Length == 0) return 0.85;
        var posMin = positives.Min();
        if (negatives.Length == 0) return Math.Max(0.5, posMin - 0.05);
        var negMax = negatives.Max();
        if (posMin > negMax) return Math.Round((posMin + negMax) / 2.0, 2);
        return Math.Max(0.5, Math.Round(posMin - 0.05, 2));
    }

    private static RgbColor DominantColor(CaptureFrame frame, RegionOfInterest roi)
    {
        var rect = ClampRect(frame.Image, roi);
        using var crop = new Mat(frame.Image, rect);
        using var hsv = new Mat();
        Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);

        // Saturated pixels carry the meaningful UI color; gray/black/white pixels are background.
        var hueBuckets = new int[180];
        var pixels = 0;
        for (var y = 0; y < hsv.Rows; y++)
        {
            for (var x = 0; x < hsv.Cols; x++)
            {
                var p = hsv.Get<Vec3b>(y, x);
                if (p.Item1 < 80 || p.Item2 < 60) continue;  // skip low-saturation / low-value pixels
                hueBuckets[p.Item0]++;
                pixels++;
            }
        }
        if (pixels == 0)
        {
            // No saturated pixels → fall back to the mean color of the ROI.
            var mean = Cv2.Mean(crop);
            return new RgbColor((int)mean.Val2, (int)mean.Val1, (int)mean.Val0);
        }

        // Find the modal hue bin, then convert that hue + a fully-saturated S/V back to BGR.
        var modeHue = 0;
        var modeCount = 0;
        for (var i = 0; i < 180; i++) if (hueBuckets[i] > modeCount) { modeCount = hueBuckets[i]; modeHue = i; }
        using var seed = new Mat(1, 1, MatType.CV_8UC3, new Scalar(modeHue, 220, 220));
        using var bgr = new Mat();
        Cv2.CvtColor(seed, bgr, ColorConversionCodes.HSV2BGR);
        var c = bgr.Get<Vec3b>(0, 0);
        return new RgbColor(c.Item2, c.Item1, c.Item0);
    }

    private DetectionDefinition ApplySeed(DetectionDefinition? seed, DetectionKind kind, string defaultName,
        RegionOfInterest roi, Action<DetectionDefinition> apply)
    {
        var def = seed is null
            ? new DetectionDefinition
            {
                Id = "",
                Name = defaultName,
                Kind = kind,
                Enabled = true,
                Roi = ToDetectionRoi(roi),
                Output = new DetectionOutput { EventOnChangeOnly = true },
            }
            : new DetectionDefinition
            {
                Id = seed.Id,
                Name = string.IsNullOrEmpty(seed.Name) ? defaultName : seed.Name,
                Kind = kind,
                Group = seed.Group,
                Enabled = seed.Enabled,
                Roi = seed.Roi ?? ToDetectionRoi(roi),
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
