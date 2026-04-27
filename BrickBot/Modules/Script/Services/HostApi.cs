using System.Collections.Concurrent;
using System.Diagnostics;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Input.Models;
using BrickBot.Modules.Input.Services;
using BrickBot.Modules.Runner.Services;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using OpenCvSharp;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Single object exposed to JS as <c>__host</c>. Holds primitive-typed methods so we never have
/// to convert Jint <c>JsValue</c>/<c>ObjectInstance</c> arguments — the JS-side stdlib
/// (init.js) wraps these into the ergonomic <c>vision</c>/<c>input</c>/<c>brickbot</c>
/// globals that user scripts call.
/// </summary>
public sealed class HostApi : IDisposable
{
    private readonly IVisionService _vision;
    private readonly ITemplateLoader _templates;
    private readonly IInputService _input;
    private readonly IRunLog _log;
    private readonly IScriptHost _host;
    private readonly IFrameBuffer _frameBuffer;
    private readonly IScriptDispatcher _dispatcher;
    /// <summary>Per-Run baseline cache for <c>vision.captureBaseline / vision.diff</c>.
    /// Mats are disposed when HostApi disposes (Run end).</summary>
    private readonly ConcurrentDictionary<string, Mat> _baselines = new(StringComparer.Ordinal);

    public HostApi(
        IVisionService vision,
        ITemplateLoader templates,
        IInputService input,
        IRunLog log,
        IScriptHost host,
        IFrameBuffer frameBuffer,
        IScriptDispatcher dispatcher)
    {
        _vision = vision;
        _templates = templates;
        _input = input;
        _log = log;
        _host = host;
        _frameBuffer = frameBuffer;
        _dispatcher = dispatcher;
    }

    // ---------------- Vision ----------------
    // Vision now reads from the FrameBuffer when a tick loop has pumped a recent frame.
    // Scripts that never call brickbot.runForever() fall through to a synchronous Grab so
    // legacy procedural mains keep working.

    public MatchResult? findTemplate(string templatePath, double minConfidence, double scale, bool grayscale, bool pyramid)
    {
        _host.EnsureNotCancelled();
        var template = _templates.Load(ResolveTemplate(templatePath));
        using var frame = AcquireFrame();
        var match = _vision.Find(frame, template, new FindOptions(minConfidence, null, scale, grayscale, pyramid));
        return match is null ? null : MatchResult.From(match);
    }

    public MatchResult? findTemplateRoi(string templatePath, double minConfidence, int x, int y, int w, int h, double scale, bool grayscale, bool pyramid)
    {
        _host.EnsureNotCancelled();
        var template = _templates.Load(ResolveTemplate(templatePath));
        using var frame = AcquireFrame();
        var roi = new RegionOfInterest(x, y, w, h);
        var match = _vision.Find(frame, template, new FindOptions(minConfidence, roi, scale, grayscale, pyramid));
        return match is null ? null : MatchResult.From(match);
    }

    public MatchResult? waitForTemplate(string templatePath, int timeoutMs, double minConfidence, double scale, bool grayscale, bool pyramid)
    {
        var template = _templates.Load(ResolveTemplate(templatePath));
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            _host.EnsureNotCancelled();
            using var frame = AcquireFrame();
            var match = _vision.Find(frame, template, new FindOptions(minConfidence, null, scale, grayscale, pyramid));
            if (match is not null) return MatchResult.From(match);
            Thread.Sleep(50);
        }
        return null;
    }

    /// <summary>Scale-invariant template match: tries a range of scales and returns the best.
    /// Robust against small UI scaling differences that defeat raw template matching.</summary>
    public MatchResult? findFeatures(string templatePath, double minConfidence,
        double scaleMin, double scaleMax, int scaleSteps,
        bool hasRoi, int rx, int ry, int rw, int rh)
    {
        _host.EnsureNotCancelled();
        var template = _templates.Load(ResolveTemplate(templatePath));
        using var frame = AcquireFrame();
        RegionOfInterest? roi = hasRoi ? new RegionOfInterest(rx, ry, rw, rh) : null;
        var match = _vision.FindFeatures(frame, template,
            new FeatureMatchOptions(minConfidence, roi, scaleMin, scaleMax, scaleSteps));
        return match is null ? null : MatchResult.From(match);
    }

    /// <summary>Find blobs of a color range. Returns an array of {x,y,w,h,area,cx,cy} sorted
    /// by area descending. Use for HP bars / cooldown overlays / debuff icons.</summary>
    public ColorBlobJs[] findColors(int rMin, int rMax, int gMin, int gMax, int bMin, int bMax,
        bool hasRoi, int rx, int ry, int rw, int rh, int minArea, int maxResults)
    {
        _host.EnsureNotCancelled();
        using var frame = AcquireFrame();
        RegionOfInterest? roi = hasRoi ? new RegionOfInterest(rx, ry, rw, rh) : null;
        var range = new ColorRange(rMin, rMax, gMin, gMax, bMin, bMax);
        var blobs = _vision.FindColors(frame, range, new FindColorsOptions(roi, minArea, maxResults));
        var result = new ColorBlobJs[blobs.Count];
        for (var i = 0; i < blobs.Count; i++) result[i] = ColorBlobJs.From(blobs[i]);
        return result;
    }

    public ColorTriple colorAt(int x, int y)
    {
        _host.EnsureNotCancelled();
        using var frame = AcquireFrame();
        var c = _vision.ColorAt(frame, x, y);
        return new ColorTriple(c.R, c.G, c.B);
    }

    /// <summary>HP/MP/cooldown bar fill ratio (0..1). Counts pixels within tolerance of the
    /// target color across the ROI. Use a tight ROI for speed (single horizontal strip).</summary>
    public double percentBar(int x, int y, int w, int h, int r, int g, int b, int tolerance)
    {
        _host.EnsureNotCancelled();
        using var frame = AcquireFrame();
        var roi = new RegionOfInterest(x, y, w, h);
        return _vision.PercentBar(frame, roi, new ColorSample(r, g, b), tolerance);
    }

    /// <summary>Snapshot the given ROI into the per-Run baseline cache under <paramref name="name"/>.
    /// Subsequent <c>diffBaseline</c> calls compare the current frame's ROI against this snapshot —
    /// good for detecting "did the skill icon change" / "did the buff appear".</summary>
    public void captureBaseline(string name, int x, int y, int w, int h)
    {
        _host.EnsureNotCancelled();
        using var frame = AcquireFrame();
        var rect = new Rect(
            Math.Clamp(x, 0, frame.Width),
            Math.Clamp(y, 0, frame.Height),
            Math.Clamp(w, 0, frame.Width - Math.Clamp(x, 0, frame.Width)),
            Math.Clamp(h, 0, frame.Height - Math.Clamp(y, 0, frame.Height)));
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var clone = new Mat(frame.Image, rect).Clone();
        if (_baselines.TryRemove(name, out var old)) old.Dispose();
        _baselines[name] = clone;
    }

    /// <summary>Mean absolute difference (0..1) between the named baseline and the same ROI in
    /// the current frame. Returns 1.0 if no baseline is registered under that name.</summary>
    public double diffBaseline(string name, int x, int y, int w, int h)
    {
        _host.EnsureNotCancelled();
        if (!_baselines.TryGetValue(name, out var baseline)) return 1.0;
        using var frame = AcquireFrame();
        var roi = new RegionOfInterest(x, y, w, h);
        return _vision.Diff(frame, baseline, roi);
    }

    public void clearBaselines()
    {
        foreach (var (_, mat) in _baselines) mat.Dispose();
        _baselines.Clear();
    }

    public void Dispose()
    {
        clearBaselines();
    }

    // ---------------- Input ----------------

    public void click(int x, int y, string button)
    {
        _host.EnsureNotCancelled();
        _input.Click(_host.WindowOriginX + x, _host.WindowOriginY + y, ParseButton(button));
    }

    public void moveTo(int x, int y)
    {
        _host.EnsureNotCancelled();
        _input.MoveTo(_host.WindowOriginX + x, _host.WindowOriginY + y);
    }

    public void drag(int x1, int y1, int x2, int y2, string button)
    {
        _host.EnsureNotCancelled();
        _input.Drag(
            _host.WindowOriginX + x1, _host.WindowOriginY + y1,
            _host.WindowOriginX + x2, _host.WindowOriginY + y2,
            ParseButton(button));
    }

    public void pressKey(int vk)
    {
        _host.EnsureNotCancelled();
        _input.PressKey(vk);
    }

    public void keyDown(int vk)
    {
        _host.EnsureNotCancelled();
        _input.KeyDown(vk);
    }

    public void keyUp(int vk)
    {
        _host.EnsureNotCancelled();
        _input.KeyUp(vk);
    }

    public void typeText(string text)
    {
        _host.EnsureNotCancelled();
        _input.TypeText(text);
    }

    // ---------------- Helpers ----------------

    public void log(string message) => _log.Info(message);

    public void waitMs(int ms)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(ms / 1000.0 * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            _host.EnsureNotCancelled();
            Thread.Sleep(Math.Min(20, ms));
        }
    }

    public bool isCancelled() => _host.Cancellation.IsCancellationRequested;

    public long now() => Environment.TickCount64;

    // ---------------- Engine event loop primitives ----------------
    // Called by the JS-side tick loop in StdLib. All run on the engine thread.

    /// <summary>Latest frame number published to the shared buffer (0 if none yet).</summary>
    public long latestFrameNumber() => _frameBuffer.LatestFrameNumber;

    /// <summary>Grab a fresh frame from the target window and publish it to the shared
    /// buffer so subsequent <c>vision.*</c> calls within the tick see the same image.
    /// Returns metadata for the captured frame.</summary>
    public PumpResult pumpFrame()
    {
        _host.EnsureNotCancelled();
        var frame = _host.GrabFrame();
        // Publish takes ownership; the buffer will dispose the previous frame.
        _frameBuffer.Publish(frame);
        return new PumpResult(frame.Width, frame.Height, frame.FrameNumber);
    }

    /// <summary>Push the current set of registered action names so the UI can list them.</summary>
    public void publishActions(string[] names)
    {
        _dispatcher.SetRegisteredActions(names);
    }

    /// <summary>Pull the next action that the UI (or another script) has scheduled to run.
    /// Returns null when nothing is queued.</summary>
    public string? tryDequeueAction() => _dispatcher.TryDequeueInvocation();

    // ---------------- Internals ----------------

    /// <summary>
    /// Returns a frame to use for vision ops in the current tick. Prefers the most-recently
    /// pumped frame so all vision calls inside a tick see the same image. Falls back to a
    /// fresh capture for scripts that don't run a tick loop.
    /// </summary>
    private CaptureFrame AcquireFrame()
    {
        var snapshot = _frameBuffer.Snapshot();
        return snapshot ?? _host.GrabFrame();
    }

    private string ResolveTemplate(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(_host.TemplateRoot, path);

    private static MouseButton ParseButton(string? name) => (name?.ToLowerInvariant()) switch
    {
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        _ => MouseButton.Left,
    };
}

/// <summary>JS-friendly match POCO. Jint surfaces fields/properties to the script.</summary>
public sealed class MatchResult
{
    public int x { get; init; }
    public int y { get; init; }
    public int w { get; init; }
    public int h { get; init; }
    public int cx { get; init; }
    public int cy { get; init; }
    public double confidence { get; init; }

    public static MatchResult From(VisionMatch m) => new()
    {
        x = m.X, y = m.Y, w = m.Width, h = m.Height,
        cx = m.CenterX, cy = m.CenterY, confidence = m.Confidence,
    };
}

public sealed class ColorTriple
{
    public int r { get; }
    public int g { get; }
    public int b { get; }
    public ColorTriple(int r, int g, int b) { this.r = r; this.g = g; this.b = b; }
}

/// <summary>JS-friendly POCO for FindColors blobs — Jint surfaces the public properties.</summary>
public sealed class ColorBlobJs
{
    public int x { get; init; }
    public int y { get; init; }
    public int w { get; init; }
    public int h { get; init; }
    public int area { get; init; }
    public int cx { get; init; }
    public int cy { get; init; }

    public static ColorBlobJs From(ColorBlob b) => new()
    {
        x = b.X, y = b.Y, w = b.Width, h = b.Height,
        area = b.Area, cx = b.CenterX, cy = b.CenterY,
    };
}

/// <summary>Returned by <see cref="HostApi.pumpFrame"/>; surfaced into JS frame events.</summary>
public sealed class PumpResult
{
    public int width { get; }
    public int height { get; }
    public long frameNumber { get; }
    public PumpResult(int width, int height, long frameNumber)
    {
        this.width = width;
        this.height = height;
        this.frameNumber = frameNumber;
    }
}
