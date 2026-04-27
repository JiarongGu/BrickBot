using BrickBot.Modules.Capture.Models;

namespace BrickBot.Modules.Capture.Services;

/// <summary>
/// Single-writer, multi-reader buffer for the latest captured frame.
/// Vision modules read from here without blocking the capture loop.
/// </summary>
public interface IFrameBuffer
{
    /// <summary>Replace the current frame. Buffer takes ownership.</summary>
    void Publish(CaptureFrame frame);

    /// <summary>Get a clone of the latest frame, or null if nothing has been published.</summary>
    CaptureFrame? Snapshot();

    long LatestFrameNumber { get; }
}

public sealed class FrameBuffer : IFrameBuffer, IDisposable
{
    private readonly object _lock = new();
    private CaptureFrame? _latest;

    public long LatestFrameNumber => _latest?.FrameNumber ?? 0;

    public void Publish(CaptureFrame frame)
    {
        CaptureFrame? old;
        lock (_lock)
        {
            old = _latest;
            _latest = frame;
        }
        old?.Dispose();
    }

    public CaptureFrame? Snapshot()
    {
        lock (_lock)
        {
            if (_latest is null) return null;
            return new CaptureFrame(_latest.Image.Clone(), _latest.FrameNumber, _latest.CapturedAt);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _latest?.Dispose();
            _latest = null;
        }
    }
}
