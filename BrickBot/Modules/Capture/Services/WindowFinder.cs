using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BrickBot.Modules.Capture.Models;

namespace BrickBot.Modules.Capture.Services;

public sealed class WindowFinder : IWindowFinder
{
    public IReadOnlyList<WindowInfo> ListVisibleWindows()
    {
        var results = new List<WindowInfo>();
        Native.EnumWindows((hWnd, _) =>
        {
            if (!Native.IsWindowVisible(hWnd)) return true;

            var length = Native.GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            Native.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            var info = BuildInfo(hWnd, title);
            if (info is not null) results.Add(info);
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
        return BuildInfo(handle, sb.ToString());
    }

    private static WindowInfo? BuildInfo(nint hWnd, string title)
    {
        if (!Native.GetWindowRect(hWnd, out var rect)) return null;

        var processName = TryGetProcessName(hWnd);
        return new WindowInfo(
            hWnd,
            title,
            processName,
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top);
    }

    private static string TryGetProcessName(nint hWnd)
    {
        try
        {
            Native.GetWindowThreadProcessId(hWnd, out var pid);
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static class Native
    {
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

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(nint hWnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }
    }
}
