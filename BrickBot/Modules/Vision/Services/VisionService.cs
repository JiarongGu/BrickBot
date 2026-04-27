using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Vision.Models;
using OpenCvSharp;

namespace BrickBot.Modules.Vision.Services;

public sealed class VisionService : IVisionService
{
    public VisionMatch? Find(CaptureFrame frame, Mat template, FindOptions options)
    {
        if (template.Empty()) throw new OperationException("VISION_TEMPLATE_EMPTY");
        if (frame.Image.Empty()) throw new OperationException("VISION_FRAME_EMPTY");

        Mat haystack = frame.Image;
        var roiOffsetX = 0;
        var roiOffsetY = 0;
        Mat? cropped = null;

        try
        {
            if (options.Roi is { } roi)
            {
                var rect = ClampRect(haystack, roi);
                if (rect.Width <= 0 || rect.Height <= 0) return null;
                cropped = new Mat(haystack, rect);
                haystack = cropped;
                roiOffsetX = rect.X;
                roiOffsetY = rect.Y;
            }

            if (template.Width > haystack.Width || template.Height > haystack.Height) return null;

            using var result = new Mat();
            Cv2.MatchTemplate(haystack, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal < options.MinConfidence) return null;

            return new VisionMatch(
                X: maxLoc.X + roiOffsetX,
                Y: maxLoc.Y + roiOffsetY,
                Width: template.Width,
                Height: template.Height,
                Confidence: maxVal);
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    public ColorSample ColorAt(CaptureFrame frame, int x, int y)
    {
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
        {
            throw new OperationException("VISION_COORD_OUT_OF_BOUNDS",
                new() { ["x"] = x.ToString(), ["y"] = y.ToString() });
        }

        var pixel = frame.Image.Get<Vec3b>(y, x);
        return new ColorSample(pixel.Item2, pixel.Item1, pixel.Item0); // BGR → RGB
    }

    private static Rect ClampRect(Mat haystack, RegionOfInterest roi)
    {
        var x = Math.Clamp(roi.X, 0, haystack.Width);
        var y = Math.Clamp(roi.Y, 0, haystack.Height);
        var w = Math.Clamp(roi.Width, 0, haystack.Width - x);
        var h = Math.Clamp(roi.Height, 0, haystack.Height - y);
        return new Rect(x, y, w, h);
    }
}
