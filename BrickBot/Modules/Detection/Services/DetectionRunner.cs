using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Detection.Models;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using OpenCvSharp;
using OpenCvSharp.Tracking;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// Runs a <see cref="DetectionDefinition"/> against a frame and returns the typed result.
/// One per host (singleton); per-Run state (trackers, last-results) is held internally
/// and torn down by <see cref="Reset"/>.
///
/// Dispatch table — only four kinds:
///   • <see cref="DetectionKind.Tracker"/> — stateful OpenCV visual tracker.
///   • <see cref="DetectionKind.Pattern"/> — ORB descriptor match (background-invariant).
///   • <see cref="DetectionKind.Text"/>    — Tesseract OCR (NOT IMPLEMENTED YET — phase C).
///   • <see cref="DetectionKind.Bar"/>     — fill-ratio measurement on a known bbox.
/// </summary>
public sealed class DetectionRunner : IDetectionRunner, IDisposable
{
    private readonly IVisionService _vision;

    /// <summary>Last result per detection id within the current Run — drives cross-detection
    /// ROI references (<see cref="DetectionRoi.FromDetectionId"/>) and bar's
    /// <see cref="BarOptions.AnchorPatternId"/>. Cleared on Reset().</summary>
    private readonly ConcurrentDictionary<string, DetectionResult> _lastResults = new(StringComparer.Ordinal);

    /// <summary>Per-detection live tracker state. Trackers are stateful — initialized once
    /// with the saved init frame + bbox, then updated each tick. Reset() disposes them all
    /// so each new Run starts fresh.</summary>
    private readonly ConcurrentDictionary<string, Tracker> _trackers = new(StringComparer.Ordinal);

    /// <summary>Tracks whether each cached tracker has been .Init()'d yet — OpenCvSharp's
    /// Tracker has no <c>IsInited</c> getter, so we shadow it.</summary>
    private readonly ConcurrentDictionary<string, bool> _trackerInited = new(StringComparer.Ordinal);

    public DetectionRunner(IVisionService vision)
    {
        _vision = vision;
    }

    public DetectionResult Run(string profileId, DetectionDefinition def, CaptureFrame frame)
    {
        var sw = Stopwatch.StartNew();
        var roi = ResolveRoi(def.Roi, frame);
        var result = def.Kind switch
        {
            DetectionKind.Tracker => RunTracker(profileId, def, frame, sw),
            DetectionKind.Pattern => RunPattern(def, frame, roi, sw),
            DetectionKind.Text    => RunText(def, frame, roi, sw),
            DetectionKind.Bar     => RunBar(def, frame, roi, sw),
            _ => throw new OperationException("DETECTION_KIND_UNSUPPORTED",
                new() { ["kind"] = def.Kind.ToString() }),
        };
        if (!string.IsNullOrEmpty(def.Id)) _lastResults[def.Id] = result;
        return result;
    }

    public void Reset()
    {
        foreach (var (_, tr) in _trackers) tr.Dispose();
        _trackers.Clear();
        _trackerInited.Clear();
        _lastResults.Clear();
    }

    public void Dispose() => Reset();

    // ============================================================
    //  Tracker — stateful KCF / CSRT / MIL
    // ============================================================

    private DetectionResult RunTracker(string profileId, DetectionDefinition def, CaptureFrame frame, Stopwatch sw)
    {
        var opt = def.Tracker ?? throw MissingOpts("tracker");
        if (string.IsNullOrEmpty(opt.InitFramePng) || opt.InitW <= 0 || opt.InitH <= 0)
        {
            throw new OperationException("DETECTION_TRACKER_NOT_TRAINED", new() { ["id"] = def.Id });
        }

        var key = $"{profileId}/{def.Id}";
        var tracker = _trackers.GetOrAdd(key, _ => CreateTracker(opt));

        if (tracker is { } t && !TrackerIsInited(key))
        {
            using var initBytes = Cv2.ImDecode(Convert.FromBase64String(opt.InitFramePng), ImreadModes.Color);
            if (initBytes.Empty())
            {
                throw new OperationException("DETECTION_TRACKER_INIT_DECODE_FAILED", new() { ["id"] = def.Id });
            }
            t.Init(initBytes, new Rect(opt.InitX, opt.InitY, opt.InitW, opt.InitH));
            _trackerInited[key] = true;
        }

        var bbox = new Rect();
        var ok = tracker!.Update(frame.Image, ref bbox);

        if (!ok && opt.ReacquireOnLost)
        {
            tracker.Dispose();
            _trackers.TryRemove(key, out _);
            _trackerInited.TryRemove(key, out _);
        }

        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = ok,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = ok ? ToBox(bbox.X, bbox.Y, bbox.Width, bbox.Height) : null,
        };
    }

    private bool TrackerIsInited(string key) =>
        _trackerInited.TryGetValue(key, out var b) && b;

    private static Tracker CreateTracker(TrackerOptions opt) => opt.Algorithm switch
    {
        TrackerAlgorithm.Csrt => TrackerCSRT.Create(),
        TrackerAlgorithm.Mil  => TrackerMIL.Create(),
        _                     => TrackerKCF.Create(),
    };

    // ============================================================
    //  Pattern — ORB descriptor match
    // ============================================================

    private DetectionResult RunPattern(DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.Pattern ?? throw MissingOpts("pattern");
        if (string.IsNullOrEmpty(opt.Descriptors) || opt.KeypointCount <= 0)
        {
            throw new OperationException("DETECTION_PATTERN_NOT_TRAINED", new() { ["id"] = def.Id });
        }

        using var trainDescriptors = DeserializeDescriptors(opt.Descriptors, opt.KeypointCount);

        var match = _vision.MatchPattern(frame, trainDescriptors,
            new PatternMatchOptions(
                Roi: roi,
                LoweRatio: opt.LoweRatio,
                MinConfidence: opt.MinConfidence,
                MaxRuntimeKeypoints: opt.MaxRuntimeKeypoints,
                TemplateWidth: opt.TemplateWidth,
                TemplateHeight: opt.TemplateHeight));

        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = match is not null,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = match is null ? null : ToBox(match.X, match.Y, match.Width, match.Height),
            confidence = match?.Confidence,
        };
    }

    /// <summary>
    /// Decode a base64-encoded BRISK descriptor blob back into an 8U N×64 Mat. Trainer
    /// encoded it row-major; this just lays the bytes back into a Mat with the same shape.
    /// Width is inferred from <c>bytes.Length / rows</c> — supports legacy ORB (32-byte) and
    /// current BRISK (64-byte) blobs without a schema bump.
    /// </summary>
    private static Mat DeserializeDescriptors(string base64, int rows)
    {
        var bytes = Convert.FromBase64String(base64);
        if (rows <= 0 || bytes.Length % rows != 0)
        {
            throw new OperationException("DETECTION_PATTERN_DESCRIPTORS_CORRUPT",
                new() { ["expected"] = "rows-aligned", ["actual"] = bytes.Length.ToString() });
        }
        var width = bytes.Length / rows;
        if (width != 32 && width != 64)
        {
            throw new OperationException("DETECTION_PATTERN_DESCRIPTORS_CORRUPT",
                new() { ["expected"] = "32 or 64 byte descriptors", ["actual"] = width.ToString() });
        }
        var mat = new Mat(rows, width, MatType.CV_8UC1);
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, mat.Data, bytes.Length);
        return mat;
    }

    // ============================================================
    //  Text — Tesseract OCR (phase C — stub)
    // ============================================================

    private DetectionResult RunText(DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.Text ?? throw MissingOpts("text");
        if (roi is null || roi.Width <= 0 || roi.Height <= 0)
        {
            throw new OperationException("DETECTION_ROI_REQUIRED", new() { ["id"] = def.Id });
        }

        var (text, confidence) = _vision.OcrRoi(frame, roi, opt);

        var matched = !string.IsNullOrEmpty(text)
            && confidence >= opt.MinConfidence
            && (string.IsNullOrEmpty(opt.MatchRegex) || Regex.IsMatch(text, opt.MatchRegex));

        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = matched,
            durationMs = sw.Elapsed.TotalMilliseconds,
            text = matched ? text : null,
            confidence = confidence / 100.0,
            match = ToBox(roi.X, roi.Y, roi.Width, roi.Height),
        };
    }

    // ============================================================
    //  Bar — HP / MP / cooldown fill ratio
    // ============================================================

    private DetectionResult RunBar(DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.Bar ?? throw MissingOpts("bar");

        // Locate the bar's bbox: either from a referenced Pattern detection's last match,
        // or from the detection's own ROI directly.
        Rect bbox;
        if (!string.IsNullOrEmpty(opt.AnchorPatternId)
            && _lastResults.TryGetValue(opt.AnchorPatternId, out var anchor)
            && anchor.match is { } m)
        {
            bbox = new Rect(m.x, m.y, m.w, m.h);
        }
        else if (roi is not null && roi.Width > 0 && roi.Height > 0)
        {
            bbox = new Rect(roi.X, roi.Y, roi.Width, roi.Height);
        }
        else
        {
            throw new OperationException("DETECTION_BAR_NEEDS_ROI_OR_ANCHOR", new() { ["id"] = def.Id });
        }

        var fillColor = new ColorSample(opt.FillColor.R, opt.FillColor.G, opt.FillColor.B);
        var horizontal = opt.Direction == FillDirection.LeftToRight || opt.Direction == FillDirection.RightToLeft;

        // Sample a thin strip across the densest fill row/column inside the bar (drops icon
        // endcaps via the inset percentages), then measure linear fill ratio along it.
        int stripX, stripY, stripW, stripH;
        if (horizontal)
        {
            var insetA = (int)(bbox.Width * opt.InsetLeftPct);
            var insetB = (int)(bbox.Width * opt.InsetRightPct);
            stripX = bbox.X + insetA;
            stripW = Math.Max(1, bbox.Width - insetA - insetB);

            var brightestY = bbox.Y;
            var brightestPct = -1.0;
            for (var dy = 0; dy < bbox.Height; dy++)
            {
                var y = bbox.Y + dy;
                var pct = _vision.PercentBar(frame, new RegionOfInterest(stripX, y, stripW, 1),
                    fillColor, opt.Tolerance, opt.ColorSpace);
                if (pct > brightestPct) { brightestPct = pct; brightestY = y; }
            }
            stripY = Math.Max(0, brightestY - 2);
            stripH = Math.Min(5, frame.Height - stripY);
        }
        else
        {
            var insetA = (int)(bbox.Height * opt.InsetLeftPct);
            var insetB = (int)(bbox.Height * opt.InsetRightPct);
            stripY = bbox.Y + insetA;
            stripH = Math.Max(1, bbox.Height - insetA - insetB);

            var brightestX = bbox.X;
            var brightestPct = -1.0;
            for (var dx = 0; dx < bbox.Width; dx++)
            {
                var x = bbox.X + dx;
                var pct = _vision.PercentBar(frame, new RegionOfInterest(x, stripY, 1, stripH),
                    fillColor, opt.Tolerance, opt.ColorSpace);
                if (pct > brightestPct) { brightestPct = pct; brightestX = x; }
            }
            stripX = Math.Max(0, brightestX - 2);
            stripW = Math.Min(5, frame.Width - stripX);
        }

        var strip = new RegionOfInterest(stripX, stripY, stripW, stripH);
        var fill = _vision.LinearFillRatio(frame, strip, fillColor, opt.Tolerance,
            opt.ColorSpace, opt.Direction, opt.LineThreshold);

        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = true, value = fill,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = ToBox(bbox.X, bbox.Y, bbox.Width, bbox.Height),
            strip = ToBox(strip.X, strip.Y, strip.Width, strip.Height),
        };
    }

    // ============================================================
    //  ROI resolution
    // ============================================================

    private RegionOfInterest? ResolveRoi(DetectionRoi? roi, CaptureFrame frame)
    {
        if (roi is null) return null;

        if (!string.IsNullOrEmpty(roi.FromDetectionId))
        {
            if (!_lastResults.TryGetValue(roi.FromDetectionId, out var src) || src.match is null)
            {
                return null;
            }
            var insetL = Math.Max(0, roi.X);
            var insetT = Math.Max(0, roi.Y);
            var insetR = Math.Max(0, roi.W);
            var insetB = Math.Max(0, roi.H);
            return new RegionOfInterest(
                src.match.x + insetL,
                src.match.y + insetT,
                Math.Max(0, src.match.w - insetL - insetR),
                Math.Max(0, src.match.h - insetT - insetB));
        }

        if (roi.Anchor is { } anchor)
        {
            var (ox, oy) = AnchorOffset(anchor, frame.Width, frame.Height, roi.W, roi.H);
            return new RegionOfInterest(ox + roi.X, oy + roi.Y, roi.W, roi.H);
        }

        return new RegionOfInterest(roi.X, roi.Y, roi.W, roi.H);
    }

    private static (int x, int y) AnchorOffset(AnchorOrigin anchor, int frameW, int frameH, int roiW, int roiH)
    {
        int x = anchor switch
        {
            AnchorOrigin.TopLeft or AnchorOrigin.MidLeft or AnchorOrigin.BottomLeft => 0,
            AnchorOrigin.TopCenter or AnchorOrigin.Center or AnchorOrigin.BottomCenter => (frameW - roiW) / 2,
            AnchorOrigin.TopRight or AnchorOrigin.MidRight or AnchorOrigin.BottomRight => frameW - roiW,
            _ => 0,
        };
        int y = anchor switch
        {
            AnchorOrigin.TopLeft or AnchorOrigin.TopCenter or AnchorOrigin.TopRight => 0,
            AnchorOrigin.MidLeft or AnchorOrigin.Center or AnchorOrigin.MidRight => (frameH - roiH) / 2,
            AnchorOrigin.BottomLeft or AnchorOrigin.BottomCenter or AnchorOrigin.BottomRight => frameH - roiH,
            _ => 0,
        };
        return (x, y);
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private static ResultBox ToBox(int x, int y, int w, int h) =>
        new() { x = x, y = y, w = w, h = h, cx = x + w / 2, cy = y + h / 2 };

    private static OperationException MissingOpts(string kind) =>
        new("DETECTION_OPTIONS_MISSING", new() { ["kind"] = kind });
}
