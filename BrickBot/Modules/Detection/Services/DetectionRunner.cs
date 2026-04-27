using System.Collections.Concurrent;
using System.Diagnostics;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Detection.Models;
using BrickBot.Modules.Template.Services;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using OpenCvSharp;
using VisionFeatureMatchOptions = BrickBot.Modules.Vision.Models.FeatureMatchOptions;

namespace BrickBot.Modules.Detection.Services;

public sealed class DetectionRunner : IDetectionRunner, IDisposable
{
    private readonly IVisionService _vision;
    private readonly ITemplateLoader _templates;
    private readonly ITemplateFileService _templateFiles;

    /// <summary>Per-detection effect baselines, keyed by "{profileId}/{detectionId}". Disposed on Reset().</summary>
    private readonly ConcurrentDictionary<string, Mat> _effectBaselines = new(StringComparer.Ordinal);

    /// <summary>Last result per detection id within the current Run — drives cross-detection
    /// ROI references. Cleared on Reset(). Updated whenever <see cref="Run"/> finishes.</summary>
    private readonly ConcurrentDictionary<string, DetectionResult> _lastResults = new(StringComparer.Ordinal);

    public DetectionRunner(
        IVisionService vision,
        ITemplateLoader templates,
        ITemplateFileService templateFiles)
    {
        _vision = vision;
        _templates = templates;
        _templateFiles = templateFiles;
    }

    public DetectionResult Run(string profileId, DetectionDefinition def, CaptureFrame frame)
    {
        var sw = Stopwatch.StartNew();
        var roi = ResolveRoi(def.Roi, frame);
        var result = def.Kind switch
        {
            DetectionKind.Region => RunRegion(def, roi, sw),
            DetectionKind.Template => RunTemplate(profileId, def, frame, roi, sw),
            DetectionKind.ProgressBar => RunProgressBar(profileId, def, frame, roi, sw),
            DetectionKind.ColorPresence => RunColorPresence(def, frame, roi, sw),
            DetectionKind.Effect => RunEffect(profileId, def, frame, roi, sw),
            DetectionKind.FeatureMatch => RunFeatureMatch(profileId, def, frame, roi, sw),
            _ => throw new OperationException("DETECTION_KIND_UNSUPPORTED",
                new() { ["kind"] = def.Kind.ToString() }),
        };
        if (!string.IsNullOrEmpty(def.Id)) _lastResults[def.Id] = result;
        return result;
    }

    public void Reset()
    {
        foreach (var (_, mat) in _effectBaselines) mat.Dispose();
        _effectBaselines.Clear();
        _lastResults.Clear();
    }

    public void Dispose() => Reset();

    // ---------------- Per-kind runners ----------------

    private DetectionResult RunRegion(DetectionDefinition def, RegionOfInterest? roi, Stopwatch sw)
    {
        // Region kind: ROI is the result. Found = ROI resolved to non-empty area.
        if (roi is null || roi.Width <= 0 || roi.Height <= 0)
        {
            return new DetectionResult
            {
                id = def.Id, name = def.Name, kind = def.Kind,
                found = false,
                durationMs = sw.Elapsed.TotalMilliseconds,
            };
        }
        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = true,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = ToBox(roi.X, roi.Y, roi.Width, roi.Height),
        };
    }

    private DetectionResult RunTemplate(string profileId, DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.Template ?? throw MissingOpts("template");

        using var template = ResolveTemplateMat(profileId, opt.EmbeddedPng, opt.TemplateName);
        var match = _vision.Find(frame, template, new FindOptions(
            opt.MinConfidence, roi, opt.Scale, opt.Grayscale, opt.Pyramid, opt.Edge));

        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = match is not null,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = match is null ? null : ToBox(match.X, match.Y, match.Width, match.Height),
            confidence = match?.Confidence,
        };
    }

    private DetectionResult RunFeatureMatch(string profileId, DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.FeatureMatch ?? throw MissingOpts("featureMatch");

        using var template = ResolveTemplateMat(profileId, opt.EmbeddedPng, opt.TemplateName);
        var match = _vision.FindFeatures(frame, template, new VisionFeatureMatchOptions(
            opt.MinConfidence, roi, opt.ScaleMin, opt.ScaleMax, opt.ScaleSteps, opt.Edge));

        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = match is not null,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = match is null ? null : ToBox(match.X, match.Y, match.Width, match.Height),
            confidence = match?.Confidence,
        };
    }

    private DetectionResult RunProgressBar(string profileId, DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.ProgressBar ?? throw MissingOpts("progressBar");

        // Two ways to provide the bar's bbox:
        //   (a) Template match — opt.TemplateName + opt.* template options.
        //   (b) Caller supplies the bbox directly via def.Roi (anchored / fromDetection).
        // Picking (b) skips the template stage entirely so users can compose bar fill on top
        // of another detection's match (e.g. find HUD region first → measure fill within it).
        VisionMatch? bar;
        bool fromRoi = string.IsNullOrEmpty(opt.TemplateName) && string.IsNullOrEmpty(opt.EmbeddedPng);
        if (fromRoi)
        {
            if (roi is null || roi.Width <= 0 || roi.Height <= 0)
            {
                throw new OperationException("DETECTION_PROGRESSBAR_NEEDS_ROI_OR_TEMPLATE",
                    new() { ["id"] = def.Id });
            }
            bar = new VisionMatch(roi.X, roi.Y, roi.Width, roi.Height, 1.0);
        }
        else
        {
            using var template = ResolveTemplateMat(profileId, opt.EmbeddedPng, opt.TemplateName);
            bar = _vision.Find(frame, template, new FindOptions(
                opt.MinConfidence, roi, opt.Scale, opt.Grayscale, opt.Pyramid, opt.TemplateEdge));
            if (bar is null)
            {
                return new DetectionResult
                {
                    id = def.Id, name = def.Name, kind = def.Kind,
                    found = false, value = 0.0,
                    durationMs = sw.Elapsed.TotalMilliseconds,
                };
            }
        }

        // Measure fill via per-line directional scan inside the bar bbox (with insets).
        // For horizontal directions, inset is applied left/right; for vertical, top/bottom.
        var fillColor = new ColorSample(opt.FillColor.R, opt.FillColor.G, opt.FillColor.B);
        var horizontal = opt.Direction == FillDirection.LeftToRight || opt.Direction == FillDirection.RightToLeft;

        int stripX, stripY, stripW, stripH;
        if (horizontal)
        {
            var insetA = (int)(bar.Width * opt.InsetLeftPct);
            var insetB = (int)(bar.Width * opt.InsetRightPct);
            stripX = bar.X + insetA;
            stripW = Math.Max(1, bar.Width - insetA - insetB);

            // Find the brightest fill row inside the bbox; sample a 5px strip around it.
            // PercentBar is fine here because we only need a relative score across rows.
            var brightestY = bar.Y;
            var brightestPct = -1.0;
            for (var dy = 0; dy < bar.Height; dy++)
            {
                var y = bar.Y + dy;
                var pct = _vision.PercentBar(frame, new RegionOfInterest(stripX, y, stripW, 1),
                    fillColor, opt.Tolerance, opt.ColorSpace);
                if (pct > brightestPct) { brightestPct = pct; brightestY = y; }
            }
            stripY = Math.Max(0, brightestY - 2);
            stripH = Math.Min(5, frame.Height - stripY);
        }
        else
        {
            var insetA = (int)(bar.Height * opt.InsetLeftPct);
            var insetB = (int)(bar.Height * opt.InsetRightPct);
            stripY = bar.Y + insetA;
            stripH = Math.Max(1, bar.Height - insetA - insetB);

            // Find brightest fill column for vertical bars.
            var brightestX = bar.X;
            var brightestPct = -1.0;
            for (var dx = 0; dx < bar.Width; dx++)
            {
                var x = bar.X + dx;
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
            confidence = fromRoi ? null : bar.Confidence,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = ToBox(bar.X, bar.Y, bar.Width, bar.Height),
            strip = ToBox(strip.X, strip.Y, strip.Width, strip.Height),
        };
    }

    private DetectionResult RunColorPresence(DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.ColorPresence ?? throw MissingOpts("colorPresence");
        var color = opt.Color;
        var t = opt.Tolerance;
        var range = new ColorRange(
            Math.Max(0, color.R - t), Math.Min(255, color.R + t),
            Math.Max(0, color.G - t), Math.Min(255, color.G + t),
            Math.Max(0, color.B - t), Math.Min(255, color.B + t));

        var blobs = _vision.FindColors(frame, range,
            new FindColorsOptions(roi, opt.MinArea, opt.MaxResults, opt.ColorSpace));

        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = blobs.Count > 0,
            value = blobs.Count,
            durationMs = sw.Elapsed.TotalMilliseconds,
            blobs = blobs.Select(b => ToBox(b.X, b.Y, b.Width, b.Height)).ToArray(),
        };
    }

    private DetectionResult RunEffect(string profileId, DetectionDefinition def, CaptureFrame frame, RegionOfInterest? roi, Stopwatch sw)
    {
        var opt = def.Effect ?? throw MissingOpts("effect");
        if (roi is null || roi.Width <= 0 || roi.Height <= 0)
        {
            throw new OperationException("DETECTION_ROI_REQUIRED", new() { ["id"] = def.Id });
        }

        var key = $"{profileId}/{def.Id}";

        if (!_effectBaselines.TryGetValue(key, out var baseline))
        {
            if (!string.IsNullOrEmpty(opt.EmbeddedBaselinePng))
            {
                // Trainer-pinned baseline. Decode once + cache so subsequent runs reuse it.
                var bytes = Convert.FromBase64String(opt.EmbeddedBaselinePng);
                var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);
                if (decoded.Empty()) throw new OperationException("DETECTION_TEMPLATE_DECODE_FAILED");
                baseline = decoded;
                _effectBaselines[key] = baseline;
            }
            else if (opt.AutoBaseline)
            {
                baseline = SnapshotBaseline(frame, roi);
                _effectBaselines[key] = baseline;
                return new DetectionResult
                {
                    id = def.Id, name = def.Name, kind = def.Kind,
                    found = true, triggered = false, value = 0.0,
                    durationMs = sw.Elapsed.TotalMilliseconds,
                    match = ToBox(roi.X, roi.Y, roi.Width, roi.Height),
                };
            }
            else
            {
                return new DetectionResult
                {
                    id = def.Id, name = def.Name, kind = def.Kind,
                    found = false, triggered = false, value = 0.0,
                    durationMs = sw.Elapsed.TotalMilliseconds,
                };
            }
        }

        var diff = _vision.Diff(frame, baseline, roi, opt.Edge);
        return new DetectionResult
        {
            id = def.Id, name = def.Name, kind = def.Kind,
            found = true,
            triggered = diff >= opt.Threshold,
            value = diff,
            durationMs = sw.Elapsed.TotalMilliseconds,
            match = ToBox(roi.X, roi.Y, roi.Width, roi.Height),
        };
    }

    // ---------------- ROI resolution ----------------

    /// <summary>
    /// Resolve a <see cref="DetectionRoi"/> into an absolute <see cref="RegionOfInterest"/>.
    /// Three modes (priority order):
    ///   1. <c>FromDetectionId</c> set → use the referenced detection's last match as parent;
    ///      X/Y/W/H are interpreted as inset margins (left, top, right, bottom).
    ///   2. <c>Anchor</c> set → resolve X/Y as offsets from the anchor on the current frame;
    ///      W/H are absolute sizes.
    ///   3. Otherwise → X/Y/W/H is absolute window-relative pixels.
    /// Returns null when the ROI is missing or the dependency hasn't run yet.
    /// </summary>
    private RegionOfInterest? ResolveRoi(DetectionRoi? roi, CaptureFrame frame)
    {
        if (roi is null) return null;

        if (!string.IsNullOrEmpty(roi.FromDetectionId))
        {
            if (!_lastResults.TryGetValue(roi.FromDetectionId, out var src) || src.match is null)
            {
                // Dependency not resolved yet (or didn't match). Treat as missing ROI; downstream
                // kinds that need ROI will error with DETECTION_ROI_REQUIRED.
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

    /// <summary>
    /// Anchor → (originX, originY) in window-relative pixels. Origin is the anchor's natural
    /// corner / midpoint; ROI's X/Y are added on top so positive offsets always point inward.
    /// For a top-right anchor with X=-100 W=80, the ROI lands 100px from the right edge.
    /// </summary>
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

    // ---------------- Helpers ----------------

    private static Mat SnapshotBaseline(CaptureFrame frame, RegionOfInterest roi)
    {
        var rect = new Rect(
            Math.Clamp(roi.X, 0, frame.Width),
            Math.Clamp(roi.Y, 0, frame.Height),
            Math.Clamp(roi.Width, 0, frame.Width - Math.Clamp(roi.X, 0, frame.Width)),
            Math.Clamp(roi.Height, 0, frame.Height - Math.Clamp(roi.Y, 0, frame.Height)));
        return new Mat(frame.Image, rect).Clone();
    }

    private static ResultBox ToBox(int x, int y, int w, int h) =>
        new() { x = x, y = y, w = w, h = h, cx = x + w / 2, cy = y + h / 2 };

    private static OperationException MissingOpts(string kind) =>
        new("DETECTION_OPTIONS_MISSING", new() { ["kind"] = kind });

    private static OperationException MissingTemplate(string id) =>
        new("DETECTION_TEMPLATE_REQUIRED", new() { ["id"] = id });

    private Mat LoadTemplate(string profileId, string templateRef)
    {
        // templateRef may be either a Guid id (preferred — set by the editor) or a legacy
        // user-typed name. Try id first (fast, file-only check); fall back to name lookup
        // through the repository.
        var path = _templateFiles.GetPath(profileId, templateRef);
        if (File.Exists(path))
        {
            try { return _templates.Load(path); }
            catch (FileNotFoundException) { /* swallow → name lookup below */ }
        }

        path = _templateFiles.ResolvePathAsync(profileId, templateRef).GetAwaiter().GetResult()
            ?? throw new OperationException("DETECTION_TEMPLATE_NOT_FOUND",
                new() { ["template"] = templateRef });
        try { return _templates.Load(path); }
        catch (FileNotFoundException ex)
        {
            throw new OperationException("DETECTION_TEMPLATE_NOT_FOUND",
                new() { ["template"] = templateRef }, ex.Message, ex);
        }
    }

    /// <summary>
    /// Resolve a template Mat from a definition's options: prefer embedded PNG bytes (the
    /// detection is self-contained), fall back to the legacy templateRef lookup. Caller owns
    /// disposal of the returned Mat — embedded path returns a fresh decode each call.
    /// </summary>
    private Mat ResolveTemplateMat(string profileId, string? embeddedPng, string templateRef)
    {
        if (!string.IsNullOrEmpty(embeddedPng))
        {
            var bytes = Convert.FromBase64String(embeddedPng);
            var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (mat.Empty())
            {
                throw new OperationException("DETECTION_TEMPLATE_DECODE_FAILED");
            }
            return mat;
        }
        if (string.IsNullOrEmpty(templateRef))
        {
            throw new OperationException("DETECTION_TEMPLATE_REQUIRED");
        }
        return LoadTemplate(profileId, templateRef);
    }
}
