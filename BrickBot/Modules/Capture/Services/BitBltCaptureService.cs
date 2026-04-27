using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Core.Exceptions;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace BrickBot.Modules.Capture.Services;

/// <summary>
/// GDI window capture with two strategies:
///   1. <c>PrintWindow</c> with <c>PW_CLIENTONLY | PW_RENDERFULLCONTENT</c>. Routes through
///      DWM, captures the client area of GPU-composited apps and DirectX-windowed games.
///      Works even when the target window is backgrounded.
///   2. Screen-DC <c>BitBlt</c> fallback: copies the screen region currently occupied by
///      the target window's client area. Works for any visible foreground window
///      including hardware-accelerated content. Requires the window to be on-screen and
///      not occluded (the normal case for foreground game automation).
/// We intentionally DO NOT fall back to <c>BitBlt</c> from the window's own HDC: GPU-
/// composited windows have no GDI backing store, so that path returns stale desktop
/// wallpaper pixels — a known DWM redirection-bitmap leak.
/// For fullscreen-exclusive DirectX targets, eventually swap in a WinRT GraphicsCapture
/// implementation behind the same <see cref="ICaptureService"/> interface.
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

        // Strategy 1: PrintWindow with PW_CLIENTONLY | PW_RENDERFULLCONTENT. Asks the target
        // window to render its current content into our DC, routed through DWM so GPU-
        // composited / DirectX-windowed apps actually render instead of returning black.
        var captured = TryPrintWindow(windowHandle, bitmap);

        // Strategy 2: Screen-DC BitBlt. Reads the pixels currently displayed at the window's
        // client-area screen position. Catches the cases where PrintWindow returns "blank"
        // (e.g. apps that don't honor PW_RENDERFULLCONTENT). Bypasses DWM's per-window
        // redirection bitmap, which is what was leaking desktop wallpaper through window-DC
        // BitBlt.
        if (!captured || IsBlank(bitmap))
        {
            captured = TryScreenBitBlt(windowHandle, bitmap, width, height);
        }

        if (!captured)
        {
            throw new OperationException("CAPTURE_BITBLT_FAILED");
        }

        var mat = bitmap.ToMat();
        var bgr = new Mat();
        Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR);
        mat.Dispose();

        var frameNumber = Interlocked.Increment(ref _frameCounter);
        return new CaptureFrame(bgr, frameNumber, DateTimeOffset.UtcNow);
    }

    private static bool TryPrintWindow(nint windowHandle, Bitmap bitmap)
    {
        using var g = Graphics.FromImage(bitmap);
        var hdc = g.GetHdc();
        try
        {
            // PW_CLIENTONLY tells PrintWindow to copy only the client area (matching our
            // GetClientRect-sized bitmap). Without it, PrintWindow draws the full window
            // (titlebar + borders) into the client-sized DC, clipping the right/bottom
            // edges and offsetting the visible content — looks like "wrong window".
            // PW_RENDERFULLCONTENT routes through DWM so DirectComposition / GPU-composited
            // surfaces actually render instead of returning a black redirection bitmap.
            return Native.PrintWindow(windowHandle, hdc,
                Native.PW_CLIENTONLY | Native.PW_RENDERFULLCONTENT);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    private static bool TryScreenBitBlt(nint windowHandle, Bitmap bitmap, int width, int height)
    {
        // Translate the window's client (0,0) origin into screen coordinates so we can
        // BitBlt the right region of the desktop DC. ClientToScreen handles DPI + multi-
        // monitor layouts correctly under PerMonitorV2.
        var origin = new Native.POINT { X = 0, Y = 0 };
        if (!Native.ClientToScreen(windowHandle, ref origin)) return false;

        using var g = Graphics.FromImage(bitmap);
        var destDc = g.GetHdc();
        try
        {
            // hwnd = NULL → desktop DC. Captures whatever pixels are currently on screen,
            // including GPU-composited content. Caveat: the target window must be visible
            // and not occluded by other windows for this region.
            var screenDc = Native.GetDC(nint.Zero);
            if (screenDc == nint.Zero) return false;
            try
            {
                return Native.BitBlt(destDc, 0, 0, width, height,
                    screenDc, origin.X, origin.Y, Native.SRCCOPY);
            }
            finally
            {
                Native.ReleaseDC(nint.Zero, screenDc);
            }
        }
        finally
        {
            g.ReleaseHdc(destDc);
        }
    }

    /// <summary>
    /// Heuristic: bitmap is "blank" if a fixed 9-point sample (corners + edges + center)
    /// is all-zero. Cheaper than scanning every pixel and catches the all-black DWM-cached
    /// failure mode without false positives on legitimately dark frames (which usually
    /// have some non-zero pixel among the 9 samples).
    /// </summary>
    private static bool IsBlank(Bitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w < 2 || h < 2) return false;

        Span<(int x, int y)> points = stackalloc (int, int)[]
        {
            (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1),
            (w / 2, 0), (0, h / 2), (w - 1, h / 2), (w / 2, h - 1),
            (w / 2, h / 2),
        };

        foreach (var (x, y) in points)
        {
            var c = bitmap.GetPixel(x, y);
            if (c.R != 0 || c.G != 0 || c.B != 0) return false;
        }
        return true;
    }

    private static class Native
    {
        public const int SRCCOPY = 0x00CC0020;
        /// <summary>Capture only the client area, not the full window (titlebar + borders).</summary>
        public const uint PW_CLIENTONLY = 0x00000001;
        /// <summary>Tells PrintWindow to render the window's full content even when GPU-composited.</summary>
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
        [DllImport("user32.dll")] public static extern nint GetDC(nint hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(nint hWnd, nint hdc);
        [DllImport("user32.dll")] public static extern bool GetClientRect(nint hWnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(nint hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int w, int h,
            nint hdcSrc, int xSrc, int ySrc, int rop);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }
    }
}
