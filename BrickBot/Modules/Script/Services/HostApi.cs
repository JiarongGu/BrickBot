using System.Diagnostics;
using BrickBot.Modules.Input.Models;
using BrickBot.Modules.Input.Services;
using BrickBot.Modules.Runner.Services;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Single object exposed to JS as <c>__host</c>. Holds primitive-typed methods so we never have
/// to convert Jint <c>JsValue</c>/<c>ObjectInstance</c> arguments — the JS-side stdlib
/// (init.js) wraps these into the ergonomic <c>vision</c>/<c>input</c>/<c>wait</c>/<c>log</c>
/// globals that user scripts call.
/// </summary>
public sealed class HostApi
{
    private readonly IVisionService _vision;
    private readonly ITemplateLoader _templates;
    private readonly IInputService _input;
    private readonly IRunLog _log;
    private readonly IScriptHost _host;

    public HostApi(
        IVisionService vision,
        ITemplateLoader templates,
        IInputService input,
        IRunLog log,
        IScriptHost host)
    {
        _vision = vision;
        _templates = templates;
        _input = input;
        _log = log;
        _host = host;
    }

    // ---------------- Vision ----------------

    public MatchResult? findTemplate(string templatePath, double minConfidence)
    {
        _host.EnsureNotCancelled();
        var template = _templates.Load(ResolveTemplate(templatePath));
        using var frame = _host.GrabFrame();
        var match = _vision.Find(frame, template, new FindOptions(minConfidence, null));
        return match is null ? null : MatchResult.From(match);
    }

    public MatchResult? findTemplateRoi(string templatePath, double minConfidence, int x, int y, int w, int h)
    {
        _host.EnsureNotCancelled();
        var template = _templates.Load(ResolveTemplate(templatePath));
        using var frame = _host.GrabFrame();
        var roi = new RegionOfInterest(x, y, w, h);
        var match = _vision.Find(frame, template, new FindOptions(minConfidence, roi));
        return match is null ? null : MatchResult.From(match);
    }

    public MatchResult? waitForTemplate(string templatePath, int timeoutMs, double minConfidence)
    {
        var template = _templates.Load(ResolveTemplate(templatePath));
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            _host.EnsureNotCancelled();
            using var frame = _host.GrabFrame();
            var match = _vision.Find(frame, template, new FindOptions(minConfidence, null));
            if (match is not null) return MatchResult.From(match);
            Thread.Sleep(50);
        }
        return null;
    }

    public ColorTriple colorAt(int x, int y)
    {
        _host.EnsureNotCancelled();
        using var frame = _host.GrabFrame();
        var c = _vision.ColorAt(frame, x, y);
        return new ColorTriple(c.R, c.G, c.B);
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
        // Cooperative wait — checks cancellation every 20ms so Stop() responds quickly.
        var deadline = Stopwatch.GetTimestamp() + (long)(ms / 1000.0 * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            _host.EnsureNotCancelled();
            Thread.Sleep(Math.Min(20, ms));
        }
    }

    public bool isCancelled() => _host.Cancellation.IsCancellationRequested;

    public long now() => Environment.TickCount64;

    // ---------------- Internals ----------------

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
