using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Vision.Models;
using OpenCvSharp;

namespace BrickBot.Modules.Vision.Services;

public interface IVisionService
{
    /// <summary>Find the best match of `template` inside `frame`. Returns null if confidence below threshold.</summary>
    VisionMatch? Find(CaptureFrame frame, Mat template, FindOptions options);

    /// <summary>Sample the BGR color at the given coordinate inside the frame.</summary>
    ColorSample ColorAt(CaptureFrame frame, int x, int y);
}
