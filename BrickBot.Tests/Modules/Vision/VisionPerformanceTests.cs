using System.Diagnostics;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Vision.Models;
using BrickBot.Modules.Vision.Services;
using FluentAssertions;
using OpenCvSharp;

namespace BrickBot.Tests.Modules.Vision;

/// <summary>
/// Performance gates for the action-game reaction loop. Targets reflect what the runtime
/// budget can afford at 30+ FPS; asserted thresholds are 2× the target so a noisy CI host
/// or background load doesn't false-fail. If a test fails, the actual median is in the
/// failure message — paste it back so we can chase the regression.
///
/// Synthetic frames are deterministic (no Random) so per-run variance only comes from
/// host noise. When real game captures are available, drop the bytes into TestData/ and
/// swap the helper. Asserts stay the same.
/// </summary>
public sealed class VisionPerformanceTests : IDisposable
{
    private const int FrameWidth = 1920;
    private const int FrameHeight = 1080;
    private const int TemplateWidth = 200;
    private const int TemplateHeight = 100;
    private const int TemplateX = 1200;
    private const int TemplateY = 600;

    private const int WarmupIterations = 3;
    private const int MeasuredIterations = 10;

    private readonly VisionService _vision = new();
    private readonly Mat _frame;
    private readonly Mat _template;
    private readonly CaptureFrame _captureFrame;

    public VisionPerformanceTests()
    {
        _template = BuildTemplate();
        _frame = BuildFrameWithTemplate(_template);
        _captureFrame = new CaptureFrame(_frame.Clone(), 1, DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        _captureFrame.Dispose();
        _frame.Dispose();
        _template.Dispose();
    }

    // ---------- vision.find variants ----------

    [Fact]
    public void Find_FullResolutionBgr_HasUpperBound()
    {
        // Intentionally the SLOW path: full 1920×1080 BGR (3-channel) match against a 200×100
        // template with no scale, ROI, or grayscale. Around 150-200ms on developer hardware,
        // and the explicit "don't do this for action games" baseline. Threshold catches a
        // catastrophic regression (>2x) without flapping on normal CI noise.
        var medianMs = MeasureMedian(() =>
            _vision.Find(_captureFrame, _template, new FindOptions(MinConfidence: 0.85)));

        medianMs.Should().BeLessThan(800,
            $"raw full-res find must stay under 800ms — this is the SLOW path; use grayscale + roi for action games (median={medianMs}ms)");
    }

    [Fact]
    public void Find_GrayscaleAndHalfScale_BeatsBudget()
    {
        var medianMs = MeasureMedian(() =>
            _vision.Find(_captureFrame, _template, new FindOptions(
                MinConfidence: 0.80, Scale: 0.5, Grayscale: true)));

        medianMs.Should().BeLessThan(50,
            $"grayscale + 0.5x scale must stay under 50ms (median={medianMs}ms; target 20ms)");
    }

    [Fact]
    public void Find_GrayscaleWithRoi_BeatsBudget()
    {
        // Tight ROI around the planted template — typical real-world usage.
        var roi = new RegionOfInterest(
            TemplateX - 100, TemplateY - 100,
            TemplateWidth + 200, TemplateHeight + 200);

        var medianMs = MeasureMedian(() =>
            _vision.Find(_captureFrame, _template, new FindOptions(
                MinConfidence: 0.85, Roi: roi, Grayscale: true)));

        medianMs.Should().BeLessThan(30,
            $"grayscale + ROI must stay under 30ms (median={medianMs}ms; target 10ms)");
    }

    [Fact]
    public void Find_Pyramid_BeatsBudget()
    {
        var medianMs = MeasureMedian(() =>
            _vision.Find(_captureFrame, _template, new FindOptions(
                MinConfidence: 0.85, Pyramid: true)));

        medianMs.Should().BeLessThan(150,
            $"pyramid full-frame must stay under 150ms (median={medianMs}ms; typical 60-90ms on dev hardware, 2x margin for CI)");
    }

    // ---------- vision.findFeatures (multi-scale) ----------

    [Fact]
    public void FindFeatures_Default3Scales_BeatsBudget()
    {
        var roi = new RegionOfInterest(
            TemplateX - 150, TemplateY - 150,
            TemplateWidth + 300, TemplateHeight + 300);

        var medianMs = MeasureMedian(() =>
            _vision.FindFeatures(_captureFrame, _template, new FeatureMatchOptions(
                MinConfidence: 0.75, Roi: roi)));

        medianMs.Should().BeLessThan(200,
            $"default 3-scale findFeatures with ROI must stay under 200ms (median={medianMs}ms; target 80ms)");
    }

    // ---------- vision.findColors ----------

    [Fact]
    public void FindColors_FullFrame_BeatsBudget()
    {
        // Search for the red rectangle planted by the template builder.
        var range = ColorRange.AroundRgb(220, 30, 30, tolerance: 30);

        var medianMs = MeasureMedian(() =>
            _vision.FindColors(_captureFrame, range, new FindColorsOptions(MinArea: 50)));

        medianMs.Should().BeLessThan(40,
            $"findColors on 1080p must stay under 40ms (median={medianMs}ms; target 15ms)");
    }

    // ---------- vision.percentBar ----------

    [Fact]
    public void PercentBar_NarrowStrip_BeatsBudget()
    {
        // 200×10 strip near the planted template (which is mostly red).
        var roi = new RegionOfInterest(TemplateX, TemplateY, 200, 10);
        var color = new ColorSample(220, 30, 30);

        var medianMs = MeasureMedian(() =>
            _vision.PercentBar(_captureFrame, roi, color, tolerance: 30));

        medianMs.Should().BeLessThan(8,
            $"narrow percentBar must stay under 8ms (median={medianMs}ms; target 2ms)");
    }

    // ---------- vision.diff ----------

    [Fact]
    public void Diff_AgainstBaseline_BeatsBudget()
    {
        var roi = new RegionOfInterest(TemplateX, TemplateY, TemplateWidth, TemplateHeight);
        // Baseline = same region the template was planted into → should diff to ~0.
        using var baseline = new Mat(_frame, new Rect(TemplateX, TemplateY, TemplateWidth, TemplateHeight)).Clone();

        var medianMs = MeasureMedian(() =>
            _vision.Diff(_captureFrame, baseline, roi));

        medianMs.Should().BeLessThan(12,
            $"diff against same-size baseline must stay under 12ms (median={medianMs}ms; target 3ms)");
    }

    // ---------- capture conversion micro-benchmark ----------

    [Fact]
    public void CaptureConversion_Bgra1080pToBgr_BeatsBudget()
    {
        // Mirrors WinRtCaptureService.MapAndCopyToMat: a 4-channel 1080p Mat run through
        // Cv2.CvtColor(BGRA2BGR). This is the per-frame conversion cost on a real capture.
        using var bgra = new Mat(FrameHeight, FrameWidth, MatType.CV_8UC4, new Scalar(50, 80, 100, 255));

        var medianMs = MeasureMedian(() =>
        {
            using var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        });

        medianMs.Should().BeLessThan(30,
            $"BGRA→BGR conversion on 1080p must stay under 30ms (median={medianMs}ms; target 10ms)");
    }

    // ---------- correctness sanity checks ----------
    // If perf passes but matching is broken these surface the bug independently.

    [Fact]
    public void Find_WithPlantedTemplate_FindsExactLocation()
    {
        var match = _vision.Find(_captureFrame, _template,
            new FindOptions(MinConfidence: 0.95, Grayscale: true));

        match.Should().NotBeNull();
        match!.X.Should().Be(TemplateX);
        match.Y.Should().Be(TemplateY);
    }

    [Fact]
    public void FindColors_WithRedTemplate_LocatesIt()
    {
        var range = ColorRange.AroundRgb(220, 30, 30, tolerance: 30);
        var blobs = _vision.FindColors(_captureFrame, range, new FindColorsOptions(MinArea: 1000));

        blobs.Should().NotBeEmpty();
        // Largest blob is the planted template — verify it overlaps the planted location.
        var largest = blobs[0];
        largest.X.Should().BeInRange(TemplateX - 5, TemplateX + 5);
        largest.Y.Should().BeInRange(TemplateY - 5, TemplateY + 5);
    }

    // ---------- helpers ----------

    /// <summary>Runs <paramref name="action"/> WarmupIterations times to settle JIT, then
    /// MeasuredIterations times for timing. Returns the median elapsed (ms) — robust to
    /// the occasional GC pause that ruins a mean.</summary>
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

    /// <summary>Solid-color BGR template with a small distinctive feature so MatchTemplate
    /// has something to lock onto. Red base + green dot in the corner.</summary>
    private static Mat BuildTemplate()
    {
        var t = new Mat(TemplateHeight, TemplateWidth, MatType.CV_8UC3, new Scalar(30, 30, 220));
        Cv2.Rectangle(t, new Rect(10, 10, 30, 30), new Scalar(30, 200, 30), thickness: -1);
        Cv2.Rectangle(t, new Rect(0, 0, TemplateWidth - 1, TemplateHeight - 1),
            new Scalar(0, 0, 0), thickness: 2);
        return t;
    }

    /// <summary>1920×1080 frame filled with a gradient (so MatchTemplate has variance to
    /// work against), with the template painted at (TemplateX, TemplateY).</summary>
    private static Mat BuildFrameWithTemplate(Mat template)
    {
        var f = new Mat(FrameHeight, FrameWidth, MatType.CV_8UC3);
        // Cheap gradient: per-row varying blue + per-col varying green.
        for (var y = 0; y < FrameHeight; y++)
        {
            var row = f.Row(y);
            row.SetTo(new Scalar(y * 255 / FrameHeight, 80, 120));
            row.Dispose();
        }

        // Paste template at the planted location.
        var dest = new Mat(f, new Rect(TemplateX, TemplateY, template.Width, template.Height));
        template.CopyTo(dest);
        dest.Dispose();
        return f;
    }
}
