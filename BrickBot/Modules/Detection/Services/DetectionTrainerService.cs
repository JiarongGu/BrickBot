using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Detection.Models;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using OpenCvSharp;
using VisionFillDirection = BrickBot.Modules.Vision.Models.FillDirection;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// v3 trainer. Reads PER-SAMPLE object boxes (not a single global ROI). Outputs a paired
/// (<see cref="DetectionDefinition"/>, <see cref="DetectionModel"/>):
///
/// <list type="bullet">
///   <item><b>Definition</b> = runtime knobs (algorithm choice, lowe ratio, fill direction, …).</item>
///   <item><b>Model</b> = compiled artifacts (descriptors blob, init frame, ref patch) +
///         training metadata (sample counts, mean error, mean IoU).</item>
/// </list>
///
/// Per-kind contract:
/// <list type="bullet">
///   <item><b>Tracker</b> — exactly one sample with <see cref="TrainingSample.IsInit"/>=true
///         and a non-null <see cref="TrainingSample.ObjectBox"/>. Other samples ignored.</item>
///   <item><b>Pattern</b> — positives (label="true") need <see cref="TrainingSample.ObjectBox"/>
///         each. Negatives (label="false") are scored against the trained blob to tune
///         <see cref="PatternOptions.MinConfidence"/>.</item>
///   <item><b>Text</b> — first sample with a box defines the OCR region.</item>
///   <item><b>Bar</b> — each sample needs a box (location of the bar at that fill level) and
///         a numeric label 0..1. Trainer median-aligns the boxes and derives fill color +
///         direction + line threshold.</item>
/// </list>
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
        // Composite is the one kind that doesn't need samples — it's just operand-id metadata.
        if (kind == DetectionKind.Composite) return TrainComposite(seed);

        if (samples is null || samples.Length == 0)
        {
            throw new OperationException("DETECTION_TRAIN_NEEDS_SAMPLES", new() { ["min"] = "1" });
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

    // ============================================================
    //  Composite — boolean AND/OR over other detections (no samples)
    // ============================================================

    private TrainingResult TrainComposite(DetectionDefinition? seed)
    {
        var comp = seed?.Composite ?? new CompositeOptions();
        if (comp.DetectionIds is null || comp.DetectionIds.Length == 0)
        {
            throw new OperationException("DETECTION_TRAIN_COMPOSITE_NEEDS_OPERANDS");
        }

        var definition = NewDefinition(seed, DetectionKind.Composite, "Composite", searchRoi: null,
            d => d.Composite = comp);

        var model = new DetectionModel
        {
            Id = definition.Id,
            DetectionId = definition.Id,
            Kind = DetectionKind.Composite,
            Version = 1,
            TrainedAt = DateTimeOffset.UtcNow,
            SampleCount = 0,
            Summary = $"composite {comp.Op.ToString().ToLowerInvariant()} over {comp.DetectionIds.Length} detections",
            Composite = new CompositeModelData
            {
                Op = comp.Op,
                DetectionIds = comp.DetectionIds,
            },
        };

        return new TrainingResult
        {
            Definition = definition,
            Model = model,
            Diagnostics = Array.Empty<TrainingDiagnostic>(),
            Summary = model.Summary,
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
    //  Tracker — one init sample + bbox + algorithm
    // ============================================================

    private TrainingResult TrainTracker(TrainingSample[] samples, DetectionDefinition? seed)
    {
        // Pick the user-flagged init sample, or fall back to the first sample with a box.
        var initIdx = Array.FindIndex(samples, s => s.IsInit && s.ObjectBox is not null);
        if (initIdx < 0) initIdx = Array.FindIndex(samples, s => s.ObjectBox is not null);
        if (initIdx < 0)
        {
            throw new OperationException("DETECTION_TRAIN_TRACKER_NEEDS_INIT");
        }

        var init = samples[initIdx];
        var box = init.ObjectBox!;
        if (box.W <= 0 || box.H <= 0)
        {
            throw new OperationException("DETECTION_TRAIN_TRACKER_NEEDS_BBOX");
        }

        using var initFrame = DecodeFrame(init.ImageBase64);
        var pngBytes = initFrame.Image.ImEncode(".png");
        var initFramePng = Convert.ToBase64String(pngBytes);

        var definition = NewDefinition(seed, DetectionKind.Tracker, "Trained Tracker", searchRoi: null,
            d => d.Tracker = seed?.Tracker ?? new TrackerOptions());

        var model = new DetectionModel
        {
            Id = definition.Id,
            DetectionId = definition.Id,
            Kind = DetectionKind.Tracker,
            Version = 1,
            TrainedAt = DateTimeOffset.UtcNow,
            SampleCount = samples.Length,
            PositiveCount = 1,
            Summary = $"tracker {definition.Tracker!.Algorithm.ToString().ToLowerInvariant()} · " +
                      $"init bbox ({box.X},{box.Y}) {box.W}×{box.H}",
            Tracker = new TrackerModelData
            {
                InitFramePng = initFramePng,
                InitX = box.X,
                InitY = box.Y,
                InitW = box.W,
                InitH = box.H,
            },
        };

        return new TrainingResult
        {
            Definition = definition,
            Model = model,
            Diagnostics = Array.Empty<TrainingDiagnostic>(),
            Summary = model.Summary,
        };
    }

    // ============================================================
    //  Pattern — ORB descriptor union from per-sample positive crops
    // ============================================================

    private TrainingResult TrainPattern(TrainingSample[] samples, DetectionDefinition? seed)
    {
        var frames = samples.Select(s => DecodeFrame(s.ImageBase64)).ToList();
        try
        {
            var positives = new List<int>();
            var negatives = new List<int>();
            for (var i = 0; i < samples.Length; i++)
            {
                if (IsPositive(samples[i].Label))
                {
                    if (samples[i].ObjectBox is null)
                        throw new OperationException("DETECTION_TRAIN_PATTERN_POSITIVE_NEEDS_BOX",
                            new() { ["index"] = i.ToString() });
                    positives.Add(i);
                }
                else negatives.Add(i);
            }
            if (positives.Count == 0)
            {
                throw new OperationException("DETECTION_TRAIN_NEEDS_POSITIVES");
            }

            // Extract descriptors from EACH positive's own object box. This is the key
            // difference from v2: positives are no longer cropped at one shared ROI.
            const int maxKeypointsPerSample = 150;
            var allDescriptorRows = new List<Mat>();
            int? refTemplateW = null, refTemplateH = null;
            string? refPatchPng = null;

            foreach (var i in positives)
            {
                var rect = ClampRect(frames[i].Image, ToRoi(samples[i].ObjectBox!, frames[i].Width, frames[i].Height));
                if (rect.Width < 16 || rect.Height < 16) continue;
                using var crop = new Mat(frames[i].Image, rect);
                var (_, descriptors) = _vision.ExtractDescriptors(crop, maxKeypointsPerSample);
                if (!descriptors.Empty())
                {
                    allDescriptorRows.Add(descriptors);
                    if (refTemplateW is null)
                    {
                        refTemplateW = rect.Width;
                        refTemplateH = rect.Height;
                        using var refClone = crop.Clone();
                        refPatchPng = Convert.ToBase64String(refClone.ImEncode(".png"));
                    }
                }
                else descriptors.Dispose();
            }

            if (allDescriptorRows.Count == 0)
            {
                throw new OperationException("DETECTION_TRAIN_PATTERN_NO_FEATURES");
            }

            using var trainedDescriptors = new Mat();
            Cv2.VConcat(allDescriptorRows.ToArray(), trainedDescriptors);
            foreach (var m in allDescriptorRows) m.Dispose();

            var descBytes = new byte[trainedDescriptors.Rows * trainedDescriptors.Cols];
            System.Runtime.InteropServices.Marshal.Copy(trainedDescriptors.Data, descBytes, 0, descBytes.Length);
            var descBase64 = Convert.ToBase64String(descBytes);

            var pat = seed?.Pattern ?? new PatternOptions();

            // Score positives + negatives at the trained MinConfidence to tune the threshold
            // halfway between worst-positive and best-negative when separable.
            // Use the search ROI from seed.Roi (whole-frame default).
            var searchRoi = seed?.Roi is { } sr ? new RegionOfInterest(sr.X, sr.Y, sr.W, sr.H) : (RegionOfInterest?)null;
            var posScores = new List<double>();
            var posBoxes = new List<PatternMatch?>();
            foreach (var pi in positives)
            {
                var match = _vision.MatchPattern(frames[pi], trainedDescriptors,
                    new PatternMatchOptions(searchRoi, pat.LoweRatio, 0.0,
                        pat.MaxRuntimeKeypoints, refTemplateW!.Value, refTemplateH!.Value));
                posScores.Add(match?.Confidence ?? 0.0);
                posBoxes.Add(match);
            }
            var negScores = new List<double>();
            foreach (var ni in negatives)
            {
                var match = _vision.MatchPattern(frames[ni], trainedDescriptors,
                    new PatternMatchOptions(searchRoi, pat.LoweRatio, 0.0,
                        pat.MaxRuntimeKeypoints, refTemplateW!.Value, refTemplateH!.Value));
                negScores.Add(match?.Confidence ?? 0.0);
            }

            var posMin = posScores.Count > 0 ? posScores.Min() : 0;
            var negMax = negScores.Count > 0 ? negScores.Max() : 0;
            pat.MinConfidence = posMin > negMax
                ? Math.Round((posMin + negMax) / 2.0, 2)
                : Math.Max(0.10, Math.Round(posMin * 0.80, 2));

            var definition = NewDefinition(seed, DetectionKind.Pattern, "Trained Pattern", seed?.Roi,
                d => d.Pattern = pat);

            var diagnostics = new TrainingDiagnostic[samples.Length];
            var iouSum = 0.0;
            var iouCount = 0;
            var errorCount = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                var match = _vision.MatchPattern(frames[i], trainedDescriptors,
                    new PatternMatchOptions(searchRoi, pat.LoweRatio, 0.0,
                        pat.MaxRuntimeKeypoints, refTemplateW!.Value, refTemplateH!.Value));
                var conf = match?.Confidence ?? 0.0;
                var predicted = conf >= pat.MinConfidence;
                var labelPos = IsPositive(samples[i].Label);

                PredictedBox? predBox = match is null ? null
                    : new PredictedBox { X = match.X, Y = match.Y, W = match.Width, H = match.Height };

                double iou = 0;
                if (labelPos && samples[i].ObjectBox is { } gt && match is not null)
                {
                    iou = ComputeIoU(gt, match);
                    iouSum += iou; iouCount++;
                }

                var matched = labelPos == predicted;
                if (!matched) errorCount++;
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labelPos ? "true" : "false",
                    Predicted = $"{(predicted ? "true" : "false")} ({conf:0.00})",
                    Error = matched ? 0.0 : 1.0,
                    PredictedBox = predBox,
                    IoU = iou,
                };
            }

            var meanIoU = iouCount > 0 ? iouSum / iouCount : 0.0;
            var meanError = samples.Length > 0 ? (double)errorCount / samples.Length : 0.0;

            var model = new DetectionModel
            {
                Id = definition.Id,
                DetectionId = definition.Id,
                Kind = DetectionKind.Pattern,
                Version = 1,
                TrainedAt = DateTimeOffset.UtcNow,
                SampleCount = samples.Length,
                PositiveCount = positives.Count,
                NegativeCount = negatives.Count,
                MeanError = meanError,
                MeanIoU = meanIoU,
                Summary = $"pattern {refTemplateW}×{refTemplateH} · {trainedDescriptors.Rows} keypoints · " +
                          $"minConf {pat.MinConfidence:0.00} · pos≥{posMin:0.00} neg≤{negMax:0.00} · " +
                          $"meanIoU {meanIoU:0.00}",
                Pattern = new PatternModelData
                {
                    Descriptors = descBase64,
                    KeypointCount = trainedDescriptors.Rows,
                    TemplateWidth = refTemplateW!.Value,
                    TemplateHeight = refTemplateH!.Value,
                    EmbeddedPng = refPatchPng ?? "",
                },
            };

            return new TrainingResult
            {
                Definition = definition,
                Model = model,
                Diagnostics = diagnostics,
                Summary = model.Summary,
            };
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // ============================================================
    //  Text — first sample with a box defines OCR region
    // ============================================================

    private TrainingResult TrainText(TrainingSample[] samples, DetectionDefinition? seed)
    {
        var initIdx = Array.FindIndex(samples, s => s.ObjectBox is not null);
        if (initIdx < 0)
        {
            throw new OperationException("DETECTION_TRAIN_TEXT_NEEDS_BOX");
        }
        var init = samples[initIdx];
        var box = init.ObjectBox!;
        if (box.W <= 0 || box.H <= 0)
        {
            throw new OperationException("DETECTION_TRAIN_TEXT_NEEDS_BOX");
        }

        using var initFrame = DecodeFrame(init.ImageBase64);
        var rect = ClampRect(initFrame.Image, ToRoi(box, initFrame.Width, initFrame.Height));
        using var crop = new Mat(initFrame.Image, rect);
        var embedded = Convert.ToBase64String(crop.ImEncode(".png"));

        var txt = seed?.Text ?? new TextOptions();
        // Use the box as the runtime ROI (the area to OCR). Text has no notion of search-vs-object
        // so the box IS the ROI.
        var searchRoi = new DetectionRoi { X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height };
        var definition = NewDefinition(seed, DetectionKind.Text, "Trained Text", searchRoi,
            d => d.Text = txt);

        var model = new DetectionModel
        {
            Id = definition.Id,
            DetectionId = definition.Id,
            Kind = DetectionKind.Text,
            Version = 1,
            TrainedAt = DateTimeOffset.UtcNow,
            SampleCount = samples.Length,
            PositiveCount = samples.Length,
            Summary = $"text {rect.Width}×{rect.Height} · lang {txt.Language} · psm {txt.PageSegMode} · " +
                      $"binarize {(txt.Binarize ? "on" : "off")} · upscale {txt.UpscaleFactor:0.0}× · minConf {txt.MinConfidence}",
            Text = new TextModelData
            {
                BoxX = rect.X, BoxY = rect.Y, BoxW = rect.Width, BoxH = rect.Height,
                EmbeddedPng = embedded,
            },
        };

        return new TrainingResult
        {
            Definition = definition,
            Model = model,
            Diagnostics = Array.Empty<TrainingDiagnostic>(),
            Summary = model.Summary,
        };
    }

    // ============================================================
    //  Bar — per-sample bar bbox + numeric fill labels
    // ============================================================

    private TrainingResult TrainBar(TrainingSample[] samples, DetectionDefinition? seed)
    {
        var frames = samples.Select(s => DecodeFrame(s.ImageBase64)).ToList();
        try
        {
            // Need numeric labels and a per-sample box on every sample.
            for (var i = 0; i < samples.Length; i++)
            {
                if (samples[i].ObjectBox is null)
                    throw new OperationException("DETECTION_TRAIN_BAR_NEEDS_BOX",
                        new() { ["index"] = i.ToString() });
            }
            var labels = ParseNumericLabels(samples);

            // Median-aligned bar bbox across samples — robust to a single sample with a wonky box.
            var boxes = samples.Select((s, i) => ToRoi(s.ObjectBox!, frames[i].Width, frames[i].Height)).ToArray();
            var medianBox = MedianBox(boxes);

            var hiIdx = Array.IndexOf(labels, labels.Max());
            var hiBoxRect = ClampRect(frames[hiIdx].Image, ToRoi(samples[hiIdx].ObjectBox!, frames[hiIdx].Width, frames[hiIdx].Height));

            var color = MedianColor(frames[hiIdx], hiBoxRect);
            var tolerance = EstimateTolerance(frames, boxes);
            var direction = InferDirection(frames, labels, boxes, color, tolerance);
            var (lineThreshold, meanError) = SweepLineThreshold(frames, labels, boxes, color, tolerance, direction);

            var bar = seed?.Bar ?? new BarOptions();
            bar.FillColor = color;
            bar.Tolerance = tolerance;
            bar.Direction = direction;
            bar.LineThreshold = lineThreshold;

            var searchRoi = new DetectionRoi { X = medianBox.X, Y = medianBox.Y, W = medianBox.Width, H = medianBox.Height };
            var definition = NewDefinition(seed, DetectionKind.Bar, "Trained Bar", searchRoi,
                d => d.Bar = bar);

            // Embed cropped highest-fill sample for the editor preview.
            using var hiCrop = new Mat(frames[hiIdx].Image, hiBoxRect);
            var embedded = Convert.ToBase64String(hiCrop.ImEncode(".png"));

            var diagnostics = new TrainingDiagnostic[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = new ColorSample(color.R, color.G, color.B);
                var rect = ClampRect(frames[i].Image, boxes[i]);
                var fill = _vision.LinearFillRatio(frames[i],
                    new RegionOfInterest(rect.X, rect.Y, rect.Width, rect.Height),
                    sample, tolerance, ColorSpace.Rgb, direction, lineThreshold);
                diagnostics[i] = new TrainingDiagnostic
                {
                    Label = labels[i].ToString("0.00"),
                    Predicted = fill.ToString("0.00"),
                    Error = Math.Abs(fill - labels[i]),
                    PredictedBox = new PredictedBox { X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height },
                };
            }

            var model = new DetectionModel
            {
                Id = definition.Id,
                DetectionId = definition.Id,
                Kind = DetectionKind.Bar,
                Version = 1,
                TrainedAt = DateTimeOffset.UtcNow,
                SampleCount = samples.Length,
                PositiveCount = samples.Length,
                MeanError = meanError,
                Summary = $"bar fill rgb({color.R},{color.G},{color.B}) ±{tolerance} · " +
                          $"dir {direction} · lineThreshold {lineThreshold:0.00} · meanErr {meanError:0.000}",
                Bar = new BarModelData
                {
                    BoxX = medianBox.X, BoxY = medianBox.Y, BoxW = medianBox.Width, BoxH = medianBox.Height,
                    FillColor = color, Tolerance = tolerance, Direction = direction, LineThreshold = lineThreshold,
                    EmbeddedPng = embedded,
                },
            };

            return new TrainingResult
            {
                Definition = definition,
                Model = model,
                Diagnostics = diagnostics,
                Summary = model.Summary,
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

    private DetectionDefinition NewDefinition(DetectionDefinition? seed, DetectionKind kind, string defaultName,
        DetectionRoi? searchRoi, Action<DetectionDefinition> apply)
    {
        var def = seed is null
            ? new DetectionDefinition
            {
                Id = "",
                Name = defaultName,
                Kind = kind,
                Enabled = true,
                Roi = searchRoi,
                Output = new DetectionOutput { EventOnChangeOnly = true },
            }
            : new DetectionDefinition
            {
                Id = seed.Id,
                Name = string.IsNullOrEmpty(seed.Name) ? defaultName : seed.Name,
                Kind = kind,
                Group = seed.Group,
                Enabled = seed.Enabled,
                Roi = seed.Roi ?? searchRoi,
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

    private static RegionOfInterest ToRoi(DetectionRoi r, int frameW, int frameH)
    {
        var w = r.W <= 0 ? frameW : r.W;
        var h = r.H <= 0 ? frameH : r.H;
        return new RegionOfInterest(r.X, r.Y, w, h);
    }

    private static Rect ClampRect(Mat haystack, RegionOfInterest roi)
    {
        var x = Math.Clamp(roi.X, 0, haystack.Width);
        var y = Math.Clamp(roi.Y, 0, haystack.Height);
        var w = Math.Clamp(roi.Width, 0, haystack.Width - x);
        var h = Math.Clamp(roi.Height, 0, haystack.Height - y);
        return new Rect(x, y, w, h);
    }

    private static double ComputeIoU(DetectionRoi gt, PatternMatch match)
    {
        var gx1 = gt.X; var gy1 = gt.Y; var gx2 = gt.X + gt.W; var gy2 = gt.Y + gt.H;
        var mx1 = match.X; var my1 = match.Y; var mx2 = match.X + match.Width; var my2 = match.Y + match.Height;
        var ix1 = Math.Max(gx1, mx1); var iy1 = Math.Max(gy1, my1);
        var ix2 = Math.Min(gx2, mx2); var iy2 = Math.Min(gy2, my2);
        var inter = Math.Max(0, ix2 - ix1) * Math.Max(0, iy2 - iy1);
        var union = (gt.W * gt.H) + (match.Width * match.Height) - inter;
        return union <= 0 ? 0 : (double)inter / union;
    }

    private static Rect MedianBox(RegionOfInterest[] boxes)
    {
        if (boxes.Length == 0) return new Rect(0, 0, 0, 0);
        var xs = boxes.Select(b => b.X).OrderBy(v => v).ToArray();
        var ys = boxes.Select(b => b.Y).OrderBy(v => v).ToArray();
        var ws = boxes.Select(b => b.Width).OrderBy(v => v).ToArray();
        var hs = boxes.Select(b => b.Height).OrderBy(v => v).ToArray();
        var m = boxes.Length / 2;
        return new Rect(xs[m], ys[m], ws[m], hs[m]);
    }

    private static RgbColor MedianColor(CaptureFrame frame, Rect rect)
    {
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

    private static int EstimateTolerance(List<CaptureFrame> frames, RegionOfInterest[] boxes)
    {
        var means = frames.Select((f, i) =>
        {
            var rect = ClampRect(f.Image, boxes[i]);
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
        List<CaptureFrame> frames, double[] labels, RegionOfInterest[] boxes, RgbColor color, int tolerance)
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
            var hiFill = _vision.LinearFillRatio(frames[hi], boxes[hi], sample, tolerance, ColorSpace.Rgb, dir, 0.4);
            var loFill = _vision.LinearFillRatio(frames[lo], boxes[lo], sample, tolerance, ColorSpace.Rgb, dir, 0.4);
            var score = (hiFill - loFill) * Math.Sign(labelDelta);
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }

    private (double threshold, double meanError) SweepLineThreshold(
        List<CaptureFrame> frames, double[] labels, RegionOfInterest[] boxes, RgbColor color, int tolerance,
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
                var fill = _vision.LinearFillRatio(frames[i], boxes[i], sample, tolerance, ColorSpace.Rgb, direction, t);
                totalErr += Math.Abs(fill - labels[i]);
            }
            var meanErr = totalErr / frames.Count;
            if (meanErr < bestErr) { bestErr = meanErr; bestThreshold = t; }
        }
        return (bestThreshold, bestErr);
    }
}
