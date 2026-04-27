using OpenCvSharp;

namespace BrickBot.Modules.Capture.Models;

/// <summary>
/// A captured frame. Owns the underlying Mat — call Dispose to release.
/// FrameNumber is monotonic per capture session for change detection.
/// </summary>
public sealed class CaptureFrame : IDisposable
{
    public Mat Image { get; }
    public long FrameNumber { get; }
    public DateTimeOffset CapturedAt { get; }
    public int Width => Image.Width;
    public int Height => Image.Height;

    public CaptureFrame(Mat image, long frameNumber, DateTimeOffset capturedAt)
    {
        Image = image;
        FrameNumber = frameNumber;
        CapturedAt = capturedAt;
    }

    public void Dispose() => Image.Dispose();
}
