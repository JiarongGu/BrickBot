using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using BrickBot.Modules.Capture.Models;

namespace BrickBot.Modules.Capture.Services;

public sealed class WindowFinder : IWindowFinder
{
    /// <summary>Cached own-process id; we never want to show BrickBot's own windows in the picker.</summary>
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    public IReadOnlyList<WindowInfo> ListVisibleWindows()
    {
        var results = new List<WindowInfo>();
        // Cache process icon per-pid for this enumeration pass — many windows share a pid (e.g. multi-window apps),
        // and Icon.ExtractAssociatedIcon + PNG encode is the expensive bit of building a WindowInfo.
        var iconCache = new Dictionary<uint, string?>();
        Native.EnumWindows((hWnd, _) =>
        {
            if (!Native.IsWindowVisible(hWnd)) return true;
            if (IsCloaked(hWnd)) return true;             // Drop UWP shell hosts (ApplicationFrameWindow stubs that render nothing).
            if (IsOwnProcess(hWnd)) return true;          // Drop BrickBot's own windows so the user can't accidentally screenshot itself.

            var length = Native.GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            Native.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            var info = BuildInfo(hWnd, title, iconCache);
            if (info is not null && info.Width > 0 && info.Height > 0) results.Add(info);
            return true;
        }, nint.Zero);
        return results;
    }

    public WindowInfo? FindByTitle(string titleSubstring)
    {
        if (string.IsNullOrWhiteSpace(titleSubstring)) return null;
        return ListVisibleWindows()
            .FirstOrDefault(w => w.Title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase));
    }

    public WindowInfo? GetByHandle(nint handle)
    {
        if (handle == nint.Zero || !Native.IsWindow(handle)) return null;
        var length = Native.GetWindowTextLength(handle);
        var sb = new StringBuilder(length + 1);
        Native.GetWindowText(handle, sb, sb.Capacity);
        return BuildInfo(handle, sb.ToString(), null);
    }

    private static WindowInfo? BuildInfo(nint hWnd, string title, Dictionary<uint, string?>? iconCache)
    {
        if (!Native.GetWindowRect(hWnd, out var rect)) return null;

        Native.GetWindowThreadProcessId(hWnd, out var pid);
        var processName = TryGetProcessName(pid);
        var className = TryGetClassName(hWnd);
        var iconBase64 = TryGetIconBase64(hWnd, pid, iconCache);
        return new WindowInfo(
            hWnd,
            title,
            processName,
            className,
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top,
            iconBase64);
    }

    private static bool IsCloaked(nint hWnd)
    {
        try
        {
            var cloaked = 0;
            var hr = Native.DwmGetWindowAttribute(hWnd, Native.DWMWA_CLOAKED, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOwnProcess(nint hWnd)
    {
        try
        {
            Native.GetWindowThreadProcessId(hWnd, out var pid);
            return pid == OwnProcessId;
        }
        catch
        {
            return false;
        }
    }

    private static string TryGetProcessName(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Resolve a window's icon as a base64 PNG. Resolution order:
    ///   1. <c>WM_GETICON</c> (ICON_BIG → ICON_SMALL2 → ICON_SMALL) — the window's own icon. Works
    ///      reliably even for elevated / system processes where <see cref="Process.MainModule"/>
    ///      access is denied.
    ///   2. <c>GCLP_HICON</c> / <c>GCLP_HICONSM</c> — the window class icon.
    ///   3. <see cref="Icon.ExtractAssociatedIcon"/> against the process exe — last resort.
    /// Returns null when every path fails. Caches per pid for an enumeration pass.
    /// </summary>
    private static string? TryGetIconBase64(nint hWnd, uint pid, Dictionary<uint, string?>? cache)
    {
        if (cache is not null && cache.TryGetValue(pid, out var cached)) return cached;

        var pngBase64 = TryGetWindowIcon(hWnd) ?? TryGetExeIcon(pid);
        if (cache is not null) cache[pid] = pngBase64;
        return pngBase64;
    }

    private static string? TryGetWindowIcon(nint hWnd)
    {
        try
        {
            // Order matters: ICON_BIG renders best at 18×18 in the dropdown row. Fall back to small.
            // Use SendMessageTimeout — a hung target process would otherwise freeze enumeration.
            nint iconHandle = SendIconQuery(hWnd, Native.ICON_BIG);
            if (iconHandle == nint.Zero) iconHandle = SendIconQuery(hWnd, Native.ICON_SMALL2);
            if (iconHandle == nint.Zero) iconHandle = SendIconQuery(hWnd, Native.ICON_SMALL);
            if (iconHandle == nint.Zero) iconHandle = Native.GetClassLongPtr(hWnd, Native.GCLP_HICON);
            if (iconHandle == nint.Zero) iconHandle = Native.GetClassLongPtr(hWnd, Native.GCLP_HICONSM);
            if (iconHandle == nint.Zero) return null;

            using var icon = Icon.FromHandle(iconHandle);
            return EncodeIconToPng(icon);
        }
        catch { return null; }
    }

    private static nint SendIconQuery(nint hWnd, nint iconType)
    {
        return Native.SendMessageTimeout(
            hWnd,
            Native.WM_GETICON,
            iconType,
            nint.Zero,
            Native.SMTO_ABORTIFHUNG | Native.SMTO_BLOCK,
            50,
            out var result) == nint.Zero ? nint.Zero : result;
    }

    private static string? TryGetExeIcon(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            var path = proc.MainModule?.FileName;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : EncodeIconToPng(icon);
        }
        catch { return null; }
    }

    private static string EncodeIconToPng(Icon icon)
    {
        using var bmp = icon.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string TryGetClassName(nint hWnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            var n = Native.GetClassName(hWnd, sb, sb.Capacity);
            return n > 0 ? sb.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static class Native
    {
        public const int DWMWA_CLOAKED = 14;
        public const uint WM_GETICON = 0x007F;
        public const nint ICON_SMALL = 0;
        public const nint ICON_BIG = 1;
        public const nint ICON_SMALL2 = 2;
        public const int GCLP_HICON = -14;
        public const int GCLP_HICONSM = -34;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const uint SMTO_BLOCK = 0x0001;

        public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(nint hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(nint hWnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(nint hWnd, int attr, out int value, int size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint SendMessageTimeout(
            nint hWnd, uint msg, nint wParam, nint lParam,
            uint flags, uint timeout, out nint result);

        // GetClassLongPtr only exists as a 64-bit export; the 32-bit alias is GetClassLong. We're
        // x64-only so use the Ptr variant directly.
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", CharSet = CharSet.Unicode)]
        public static extern nint GetClassLongPtr(nint hWnd, int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }
    }
}
