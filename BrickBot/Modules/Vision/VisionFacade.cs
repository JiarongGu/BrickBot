using System.Diagnostics;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Template.Services;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BrickBot.Modules.Vision;

/// <summary>
/// Vision is invoked primarily from inside scripts (not directly from the frontend).
/// The frontend-facing endpoints support the Capture &amp; Templates panel's "Detect"
/// section so users can dial in detection params (template / color blobs / bar fill)
/// against a live captured frame before committing the call to a script.
/// </summary>
public sealed class VisionFacade : BaseFacade
{
    private readonly IVisionService _vision;
    private readonly ITemplateLoader _templateLoader;
    private readonly ITemplateFileService _templateFiles;
    private readonly PayloadHelper _payload;

    public VisionFacade(
        IVisionService vision,
        ITemplateLoader templateLoader,
        ITemplateFileService templateFiles,
        PayloadHelper payload,
        ILogger<VisionFacade> logger) : base(logger)
    {
        _vision = vision;
        _templateLoader = templateLoader;
        _templateFiles = templateFiles;
        _payload = payload;
    }

    protected override async Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "TEST_TEMPLATE" => await TestTemplateAsync(request).ConfigureAwait(false),
            "TEST_FIND_COLORS" => TestFindColors(request),
            "TEST_PERCENT_BAR" => TestPercentBar(request),
            "TEST_BAR_FROM_TEMPLATE" => await TestBarFromTemplateAsync(request).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown VISION message type: {request.Type}"),
        };
    }

    private async Task<object> TestTemplateAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var templateName = _payload.GetRequiredValue<string>(request.Payload, "templateName");
        var frameBase64 = _payload.GetRequiredValue<string>(request.Payload, "frameBase64");
        var minConfidence = _payload.GetOptionalValue<double?>(request.Payload, "minConfidence") ?? 0.85;
        var grayscale = _payload.GetOptionalValue<bool?>(request.Payload, "grayscale") ?? true;
        var scale = _payload.GetOptionalValue<double?>(request.Payload, "scale") ?? 1.0;

        using var frame = DecodeFrame(frameBase64);
        var templatePath = await _templateFiles.ResolvePathAsync(profileId, templateName).ConfigureAwait(false)
            ?? throw new OperationException("TEMPLATE_NOT_FOUND", new() { ["id"] = templateName });
        var template = _templateLoader.Load(templatePath);

        var sw = Stopwatch.StartNew();
        var match = _vision.Find(frame, template, new FindOptions(minConfidence, null, scale, grayscale));
        var durationMs = sw.ElapsedMilliseconds;

        return new
        {
            match = match is null ? null : new
            {
                x = match.X,
                y = match.Y,
                w = match.Width,
                h = match.Height,
                cx = match.CenterX,
                cy = match.CenterY,
                confidence = match.Confidence,
            },
            durationMs,
        };
    }

    private object TestFindColors(IpcRequest request)
    {
        var frameBase64 = _payload.GetRequiredValue<string>(request.Payload, "frameBase64");
        var rMin = _payload.GetRequiredValue<int>(request.Payload, "rMin");
        var rMax = _payload.GetRequiredValue<int>(request.Payload, "rMax");
        var gMin = _payload.GetRequiredValue<int>(request.Payload, "gMin");
        var gMax = _payload.GetRequiredValue<int>(request.Payload, "gMax");
        var bMin = _payload.GetRequiredValue<int>(request.Payload, "bMin");
        var bMax = _payload.GetRequiredValue<int>(request.Payload, "bMax");
        var minArea = _payload.GetOptionalValue<int?>(request.Payload, "minArea") ?? 25;
        var maxResults = _payload.GetOptionalValue<int?>(request.Payload, "maxResults") ?? 32;

        using var frame = DecodeFrame(frameBase64);
        var range = new ColorRange(rMin, rMax, gMin, gMax, bMin, bMax);
        var sw = Stopwatch.StartNew();
        var blobs = _vision.FindColors(frame, range, new FindColorsOptions(null, minArea, maxResults));
        var durationMs = sw.ElapsedMilliseconds;

        return new
        {
            blobs = blobs.Select(b => new { x = b.X, y = b.Y, w = b.Width, h = b.Height,
                area = b.Area, cx = b.CenterX, cy = b.CenterY }).ToList(),
            durationMs,
        };
    }

    private object TestPercentBar(IpcRequest request)
    {
        var frameBase64 = _payload.GetRequiredValue<string>(request.Payload, "frameBase64");
        var x = _payload.GetRequiredValue<int>(request.Payload, "x");
        var y = _payload.GetRequiredValue<int>(request.Payload, "y");
        var w = _payload.GetRequiredValue<int>(request.Payload, "w");
        var h = _payload.GetRequiredValue<int>(request.Payload, "h");
        var r = _payload.GetRequiredValue<int>(request.Payload, "r");
        var g = _payload.GetRequiredValue<int>(request.Payload, "g");
        var b = _payload.GetRequiredValue<int>(request.Payload, "b");
        var tolerance = _payload.GetOptionalValue<int?>(request.Payload, "tolerance") ?? 30;

        using var frame = DecodeFrame(frameBase64);
        var sw = Stopwatch.StartNew();
        var fill = _vision.PercentBar(frame, new RegionOfInterest(x, y, w, h),
            new ColorSample(r, g, b), tolerance);
        var durationMs = sw.ElapsedMilliseconds;

        return new { fill, durationMs };
    }

    /// <summary>
    /// Two-stage HP/MP/cooldown bar detection: template-find locates the bar's area, then
    /// auto-discovers the brightest row inside the bbox and percent-bars on a ±2 strip
    /// around it. Solves the "template includes padding above/below the actual fill" case
    /// without requiring per-game strip-Y tuning.
    /// </summary>
    private async Task<object> TestBarFromTemplateAsync(IpcRequest request)
    {
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var templateName = _payload.GetRequiredValue<string>(request.Payload, "templateName");
        var frameBase64 = _payload.GetRequiredValue<string>(request.Payload, "frameBase64");
        var minConfidence = _payload.GetOptionalValue<double?>(request.Payload, "minConfidence") ?? 0.80;
        var fillR = _payload.GetRequiredValue<int>(request.Payload, "r");
        var fillG = _payload.GetRequiredValue<int>(request.Payload, "g");
        var fillB = _payload.GetRequiredValue<int>(request.Payload, "b");
        var tolerance = _payload.GetOptionalValue<int?>(request.Payload, "tolerance") ?? 60;
        // Optional horizontal insets to skip side icons / endcaps when sampling.
        var insetLeftPct = _payload.GetOptionalValue<double?>(request.Payload, "insetLeftPct") ?? 0.30;
        var insetRightPct = _payload.GetOptionalValue<double?>(request.Payload, "insetRightPct") ?? 0.18;

        using var frame = DecodeFrame(frameBase64);
        var templatePath = await _templateFiles.ResolvePathAsync(profileId, templateName).ConfigureAwait(false)
            ?? throw new OperationException("TEMPLATE_NOT_FOUND", new() { ["id"] = templateName });
        var template = _templateLoader.Load(templatePath);

        var sw = Stopwatch.StartNew();

        // Stage 1: template-find with grayscale (fast, accurate).
        var bar = _vision.Find(frame, template,
            new FindOptions(minConfidence, null, 1.0, Grayscale: true));
        if (bar is null)
        {
            return new
            {
                bar = (object?)null,
                strip = (object?)null,
                fill = 0.0,
                durationMs = sw.ElapsedMilliseconds,
            };
        }

        // Stage 2: scan rows inside bbox for the brightest, sample ±2 around it.
        var fillColor = new ColorSample(fillR, fillG, fillB);
        var insetLeft = (int)(bar.Width * insetLeftPct);
        var insetRight = (int)(bar.Width * insetRightPct);
        var stripX = bar.X + insetLeft;
        var stripW = Math.Max(1, bar.Width - insetLeft - insetRight);

        int brightestY = bar.Y;
        double brightestPct = -1.0;
        for (var dy = 0; dy < bar.Height; dy++)
        {
            var y = bar.Y + dy;
            var pct = _vision.PercentBar(frame, new RegionOfInterest(stripX, y, stripW, 1),
                fillColor, tolerance);
            if (pct > brightestPct) { brightestPct = pct; brightestY = y; }
        }

        var stripY = Math.Max(0, brightestY - 2);
        var stripH = Math.Min(5, frame.Height - stripY);
        var stripRect = new RegionOfInterest(stripX, stripY, stripW, stripH);
        var fill = _vision.PercentBar(frame, stripRect, fillColor, tolerance);

        return new
        {
            bar = new { x = bar.X, y = bar.Y, w = bar.Width, h = bar.Height,
                cx = bar.CenterX, cy = bar.CenterY, confidence = bar.Confidence },
            strip = new { x = stripRect.X, y = stripRect.Y, w = stripRect.Width, h = stripRect.Height },
            fill,
            durationMs = sw.ElapsedMilliseconds,
        };
    }

    private CaptureFrame DecodeFrame(string frameBase64)
    {
        var bytes = Convert.FromBase64String(frameBase64);
        var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty())
        {
            throw new OperationException("VISION_FRAME_DECODE_FAILED",
                new() { ["bytes"] = bytes.Length.ToString() });
        }
        return new CaptureFrame(mat, 0, DateTimeOffset.UtcNow);
    }
}
