using System.Diagnostics;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using FluentAssertions;
using OpenCvSharp;
using Xunit.Abstractions;

namespace BrickBot.Tests.Modules.Vision;

/// <summary>
/// Tests run against real game captures dropped into TestData/. Files are NOT committed
/// (gitignored) — when missing, every test in this class returns silently (passes as a no-op)
/// so CI doesn't fail on machines without the samples.
///
/// Two flavors:
///   - Hard gates: assert detection works AND beats a perf threshold based on the actual
///     measured number on real game frames. These guard against regressions.
///   - Diagnostics: run a recipe matrix and emit timings/coords via ITestOutputHelper.
///     Useful when tuning a new game; read with `dotnet test --logger "console;v=detailed"`.
/// </summary>
public sealed class GameFrameDiagnosticTests : IDisposable
{
    private const int WarmupIterations = 3;
    private const int MeasuredIterations = 7;

    private readonly ITestOutputHelper _output;
    private readonly VisionService _vision = new();
    private readonly Mat? _frame;
    private readonly Mat? _hpTemplate;
    private readonly CaptureFrame? _captureFrame;
    private readonly bool _hasData;

    public GameFrameDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        var capturePath = TestDataPath("capture.png");
        var hpPath = TestDataPath("hp.png");

        if (!File.Exists(capturePath) || !File.Exists(hpPath)) return;

        _frame = Cv2.ImRead(capturePath, ImreadModes.Color);
        _hpTemplate = Cv2.ImRead(hpPath, ImreadModes.Color);
        if (_frame.Empty() || _hpTemplate.Empty()) return;

        _captureFrame = new CaptureFrame(_frame.Clone(), 1, DateTimeOffset.UtcNow);
        _hasData = true;

        _output.WriteLine($"capture.png  → {_frame.Width}×{_frame.Height} ({_frame.Channels()} ch)");
        _output.WriteLine($"hp.png       → {_hpTemplate.Width}×{_hpTemplate.Height} ({_hpTemplate.Channels()} ch)");
    }

    public void Dispose()
    {
        _captureFrame?.Dispose();
        _frame?.Dispose();
        _hpTemplate?.Dispose();
    }

    private static string TestDataPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    // ============ HARD GATES — guard against regressions ============

    /// <summary>
    /// The action-game recommended recipe: grayscale + 0.5× scale. Should locate the HP
    /// bar with high confidence in well under 50ms on real game frames.
    /// </summary>
    [Fact]
    public void HpBar_GrayscaleHalfScale_FastAndAccurate()
    {
        if (!_hasData) return;

        var opts = new FindOptions(MinConfidence: 0.85, Grayscale: true, Scale: 0.5);

        // Correctness
        var match = _vision.Find(_captureFrame!, _hpTemplate!, opts);
        match.Should().NotBeNull("HP bar must be found in real game frame");
        match!.Confidence.Should().BeGreaterThan(0.85);

        // Perf — measured median ~19ms on dev hardware; assert 60ms for CI margin.
        var medianMs = MeasureMedian(() => _vision.Find(_captureFrame!, _hpTemplate!, opts));
        _output.WriteLine($"HpBar_GrayscaleHalfScale → {medianMs:0.0} ms median");
        medianMs.Should().BeLessThan(60,
            $"action-game hot path must stay under 60ms (median={medianMs}ms; target ~20ms)");
    }

    /// <summary>
    /// Even more aggressive recipe — 0.25× scale. Sub-15ms when you can afford the
    /// confidence drop. Useful for poll-every-frame triggers.
    /// </summary>
    [Fact]
    public void HpBar_GrayscaleQuarterScale_VeryFast()
    {
        if (!_hasData) return;

        var opts = new FindOptions(MinConfidence: 0.80, Grayscale: true, Scale: 0.25);
        var match = _vision.Find(_captureFrame!, _hpTemplate!, opts);
        match.Should().NotBeNull();
        match!.Confidence.Should().BeGreaterThan(0.80);

        var medianMs = MeasureMedian(() => _vision.Find(_captureFrame!, _hpTemplate!, opts));
        _output.WriteLine($"HpBar_GrayscaleQuarterScale → {medianMs:0.0} ms median");
        medianMs.Should().BeLessThan(25,
            $"quarter-scale grayscale must stay under 25ms (median={medianMs}ms; target ~8ms)");
    }

    /// <summary>
    /// Grayscale + ROI is the BEST combo when you know roughly where the target lives.
    /// Expected to be the absolute fastest template-matching path.
    /// </summary>
    [Fact]
    public void HpBar_GrayscaleWithRoi_Fastest()
    {
        if (!_hasData) return;

        // ROI = bottom 25% of frame (where game UI typically sits).
        var roi = new RegionOfInterest(0, _frame!.Height * 3 / 4, _frame.Width, _frame.Height / 4);
        var opts = new FindOptions(MinConfidence: 0.85, Roi: roi, Grayscale: true);

        var match = _vision.Find(_captureFrame!, _hpTemplate!, opts);
        match.Should().NotBeNull();
        match!.Confidence.Should().BeGreaterThan(0.85);

        var medianMs = MeasureMedian(() => _vision.Find(_captureFrame!, _hpTemplate!, opts));
        _output.WriteLine($"HpBar_GrayscaleWithRoi → {medianMs:0.0} ms median");
        medianMs.Should().BeLessThan(40,
            $"bottom-quarter ROI + grayscale must stay under 40ms (median={medianMs}ms; target ~15ms)");
    }

    // ============ DIAGNOSTICS — read output, don't gate ============

    [Fact]
    public void Diagnose_TemplateMatching_AcrossRecipes()
    {
        if (!_hasData) return;

        var recipes = new (string Name, FindOptions Opts)[]
        {
            ("raw full-res",                      new FindOptions(MinConfidence: 0.5)),
            ("grayscale",                         new FindOptions(MinConfidence: 0.5, Grayscale: true)),
            ("grayscale + scale 0.5",             new FindOptions(MinConfidence: 0.5, Grayscale: true, Scale: 0.5)),
            ("grayscale + pyramid",               new FindOptions(MinConfidence: 0.5, Grayscale: true, Pyramid: true)),
            ("grayscale + scale 0.25",            new FindOptions(MinConfidence: 0.5, Grayscale: true, Scale: 0.25)),
        };

        foreach (var (name, opts) in recipes)
        {
            var (match, ms) = TimeMatch(() => _vision.Find(_captureFrame!, _hpTemplate!, opts));
            var hit = match is null
                ? "MISS"
                : $"@ ({match.X},{match.Y}) {match.Width}×{match.Height} conf={match.Confidence:0.000}";
            _output.WriteLine($"  {name,-30} → {ms,6:0.0} ms  {hit}");
        }
    }

    [Fact]
    public void Diagnose_FindFeatures_WideScaleRange()
    {
        if (!_hasData) return;

        var ranges = new (string Name, FeatureMatchOptions Opts)[]
        {
            ("default 0.9–1.1 / 3 steps",          new FeatureMatchOptions(MinConfidence: 0.5)),
            ("wide 0.5–1.5 / 5 steps",             new FeatureMatchOptions(MinConfidence: 0.5, ScaleMin: 0.5, ScaleMax: 1.5, ScaleSteps: 5)),
            ("very wide 0.25–2.0 / 8 steps",       new FeatureMatchOptions(MinConfidence: 0.45, ScaleMin: 0.25, ScaleMax: 2.0, ScaleSteps: 8)),
        };

        foreach (var (name, opts) in ranges)
        {
            var (match, ms) = TimeMatch(() => _vision.FindFeatures(_captureFrame!, _hpTemplate!, opts));
            var hit = match is null
                ? "MISS"
                : $"@ ({match.X},{match.Y}) {match.Width}×{match.Height} conf={match.Confidence:0.000}";
            _output.WriteLine($"  {name,-35} → {ms,6:0.0} ms  {hit}");
        }
    }

    /// <summary>
    /// Vertical column scan across the located bar — for each Y row inside the bar's
    /// bounding box, count bright pixels horizontally and report the % bright. Output
    /// reveals exactly which Y rows contain the actual fill (vs background, text overlay,
    /// or template padding). Use the row(s) with high % bright to set strip-Y for
    /// percentBar in a real script.
    /// </summary>
    [Fact]
    public void HpBar_RowByRowBrightnessScan()
    {
        if (!_hasData) return;

        var bar = _vision.Find(_captureFrame!, _hpTemplate!,
            new FindOptions(MinConfidence: 0.85, Grayscale: true, Scale: 0.5));
        bar.Should().NotBeNull();

        var insetLeft = bar!.Width * 30 / 100;
        var insetRight = bar.Width * 18 / 100;
        var stripX = bar.X + insetLeft;
        var stripW = bar.Width - insetLeft - insetRight;

        _output.WriteLine($"bar @({bar.X},{bar.Y}) {bar.Width}×{bar.Height}");
        _output.WriteLine($"scanning rows in horizontal range x=[{stripX},{stripX + stripW}]");
        _output.WriteLine("y    | R   G   B   (mid sample)  | %white loose | %bright (V>200)");

        var bright = new ColorSample(220, 220, 220);
        for (var dy = 0; dy < bar.Height; dy += 2)
        {
            var y = bar.Y + dy;
            var midC = _vision.ColorAt(_captureFrame!, stripX + stripW / 2, y);
            var rowRoi = new RegionOfInterest(stripX, y, stripW, 1);
            var pctWhite = _vision.PercentBar(_captureFrame!, rowRoi, bright, 60);
            // "Bright" via threshold: any pixel where each RGB channel > 200.
            var pctBright = _vision.PercentBar(_captureFrame!, rowRoi,
                new ColorSample(240, 240, 240), 80);
            _output.WriteLine($"{y,-4} | {midC.R,3} {midC.G,3} {midC.B,3}  | {pctWhite,11:P1} | {pctBright,15:P1}");
        }
    }

    /// <summary>
    /// End-to-end HP detection: locate bar template, auto-discover the bar's actual
    /// fill rows (template typically includes padding above/below), then percentBar on
    /// that strip. Tested approach: scan rows for the brightest one, use ±2 around it
    /// as the strip. Works without per-game tuning.
    /// </summary>
    [Fact]
    public void HpBar_FillPercentEndToEnd()
    {
        if (!_hasData) return;

        var bar = _vision.Find(_captureFrame!, _hpTemplate!,
            new FindOptions(MinConfidence: 0.85, Grayscale: true, Scale: 0.5));
        bar.Should().NotBeNull();

        // Skip left icon + right ornament so we sample only the fill region horizontally.
        var insetLeft = bar!.Width * 30 / 100;
        var insetRight = bar.Width * 18 / 100;
        var stripX = bar.X + insetLeft;
        var stripW = bar.Width - insetLeft - insetRight;

        // Auto-discover the row with maximum brightness across the bar's vertical extent.
        // The bar's actual fill is usually only a handful of rows inside the template's
        // bounding box (template often includes ornament/padding around the fill).
        var bright = new ColorSample(220, 220, 220);
        int bestY = bar.Y;
        double bestPct = 0;
        for (var dy = 0; dy < bar.Height; dy++)
        {
            var y = bar.Y + dy;
            var rowRoi = new RegionOfInterest(stripX, y, stripW, 1);
            var pct = _vision.PercentBar(_captureFrame!, rowRoi, bright, 60);
            if (pct > bestPct) { bestPct = pct; bestY = y; }
        }

        // Strip = ±2 rows around the brightest row.
        var stripRoi = new RegionOfInterest(stripX, Math.Max(0, bestY - 2), stripW, 5);
        var fill = _vision.PercentBar(_captureFrame!, stripRoi, bright, 60);

        _output.WriteLine($"bar @({bar.X},{bar.Y}) {bar.Width}×{bar.Height}");
        _output.WriteLine($"brightest row: y={bestY} (offset {bestY - bar.Y} into template), single-row %white={bestPct:P1}");
        _output.WriteLine($"strip @({stripRoi.X},{stripRoi.Y}) {stripRoi.Width}×{stripRoi.Height} → fill={fill:P1}");

        // Hard gate — the captured frame's bar visibly reads ~96% in the text overlay.
        // Pixel-counted fill should land somewhere in that ballpark; assert >40% to allow
        // for anti-aliasing + text-overlay subtraction without false-failing on real frames.
        fill.Should().BeGreaterThan(0.40, "located bar in test image is ~96% full per visible text");
    }

    [Fact]
    public void HpBar_ViaWhiteColorContours()
    {
        if (!_hasData) return;

        var whiteRange = new ColorRange(
            RMin: 200, RMax: 255,
            GMin: 200, GMax: 255,
            BMin: 200, BMax: 255);

        var (blobs, ms) = TimeArr(() =>
            _vision.FindColors(_captureFrame!, whiteRange, new FindColorsOptions(MinArea: 200, MaxResults: 16)));

        _output.WriteLine($"white-blob detection → {ms:0.0} ms, {blobs.Count} blobs");
        for (var i = 0; i < Math.Min(blobs.Count, 8); i++)
        {
            var b = blobs[i];
            _output.WriteLine($"  #{i}: ({b.X},{b.Y}) {b.Width}×{b.Height} area={b.Area}");
        }
    }

    // ---------- Helpers ----------

    private static double MeasureMedian(Action action)
    {
        for (var i = 0; i < WarmupIterations; i++) action();

        var samples = new double[MeasuredIterations];
        var sw = new Stopwatch();
        for (var i = 0; i < MeasuredIterations; i++)
        {
            sw.Restart();
            action();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        return samples[MeasuredIterations / 2];
    }

    private static (T Result, double Ms) TimeMatch<T>(Func<T> f) where T : class?
    {
        f();
        var sw = Stopwatch.StartNew();
        var result = f();
        sw.Stop();
        return (result, sw.Elapsed.TotalMilliseconds);
    }

    private static (IReadOnlyList<TItem> Result, double Ms) TimeArr<TItem>(Func<IReadOnlyList<TItem>> f)
    {
        f();
        var sw = Stopwatch.StartNew();
        var result = f();
        sw.Stop();
        return (result, sw.Elapsed.TotalMilliseconds);
    }

    private static (TValue Result, double Ms) TimeValue<TValue>(Func<TValue> f)
    {
        f();
        var sw = Stopwatch.StartNew();
        var result = f();
        sw.Stop();
        return (result, sw.Elapsed.TotalMilliseconds);
    }
}
