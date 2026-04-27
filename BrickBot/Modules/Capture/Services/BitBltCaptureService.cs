using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace BrickBot.Modules.Capture.Services;

/// <summary>
/// GDI BitBlt capture. Works for windowed/GDI games and most desktop apps.
/// Does NOT work for fullscreen exclusive DirectX — for those, swap in a WinRT
/// GraphicsCapture-based ICaptureService implementation.
/// </summary>
public sealed class BitBltCaptureService : ICaptureService
{
    private long _frameCounter;

    public CaptureFrame Grab(nint windowHandle)
    {
        if (windowHandle == nint.Zero || !Native.IsWindow(windowHandle))
        {
            throw new OperationException("CAPTURE_INVALID_WINDOW");
        }

        if (!Native.GetClientRect(windowHandle, out var rect))
        {
            throw new OperationException("CAPTURE_GET_RECT_FAILED");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new OperationException("CAPTURE_ZERO_SIZE");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            var hdc = g.GetHdc();
            try
            {
                var sourceDc = Native.GetDC(windowHandle);
                try
                {
                    if (!Native.BitBlt(hdc, 0, 0, width, height, sourceDc, 0, 0, Native.SRCCOPY))
                    {
                        throw new OperationException("CAPTURE_BITBLT_FAILED");
                    }
                }
                finally
                {
                    Native.ReleaseDC(windowHandle, sourceDc);
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        var mat = bitmap.ToMat();
        var bgr = new Mat();
        Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR);
        mat.Dispose();

        var frameNumber = Interlocked.Increment(ref _frameCounter);
        return new CaptureFrame(bgr, frameNumber, DateTimeOffset.UtcNow);
    }

    private static class Native
    {
        public const int SRCCOPY = 0x00CC0020;

        [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
        [DllImport("user32.dll")] public static extern nint GetDC(nint hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(nint hWnd, nint hdc);
        [DllImport("user32.dll")] public static extern bool GetClientRect(nint hWnd, out Rect rect);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int w, int h,
            nint hdcSrc, int xSrc, int ySrc, int rop);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }
    }
}
