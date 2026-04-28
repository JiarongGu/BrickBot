using System.Runtime.InteropServices;
using BrickBot.Modules.Input.Models;

namespace BrickBot.Modules.Input.Services;

/// <summary>
/// Win32-backed input simulation. Three delivery modes selectable via <see cref="Mode"/>:
///
/// <list type="bullet">
///   <item><see cref="InputMode.SendInput"/> — original path: <c>SendInput</c> + <c>SetCursorPos</c>.
///     Coords are absolute screen pixels; OS-level so works against any focused window.</item>
///   <item><see cref="InputMode.PostMessage"/> — <c>PostMessage(target, WM_KEYDOWN/UP, ...)</c> +
///     <c>WM_LBUTTONDOWN/UP</c> with packed client-relative coords. Background-friendly: no focus
///     or cursor steal. Some games (raw-input / DirectInput) ignore WM_KEY* — fall back to SendInput
///     for those.</item>
///   <item><see cref="InputMode.PostMessageWithPos"/> — same as PostMessage, but with a temporary
///     <c>SetWindowPos(SWP_NOMOVE|SWP_NOSIZE|SWP_NOREDRAW)</c> immediately before the post so the
///     target's internal hit-tests report "I am visible at my real geometry". Workaround for games
///     that consult window state inside their input handler.</item>
/// </list>
///
/// The script-side Coordinate translation (window-relative → screen) still happens in HostApi.
/// Under PostMessage modes we convert screen coords back to client-relative via <c>ScreenToClient</c>
/// for the target HWND. Mode switches are picked up immediately — RunnerService writes the property
/// at run start; no re-injection needed.
/// </summary>
public sealed class SendInputService : IInputService
{
    public InputMode Mode { get; set; } = InputMode.SendInput;
    public nint TargetWindow { get; set; } = nint.Zero;

    public void MoveTo(int screenX, int screenY)
    {
        if (Mode == InputMode.SendInput) Native.SetCursorPos(screenX, screenY);
        // PostMessage modes can't move a real cursor — games that need it generally don't work
        // with PostMessage anyway, so silently no-op rather than fail.
    }

    public void Click(int screenX, int screenY, MouseButton button = MouseButton.Left)
    {
        if (Mode == InputMode.SendInput)
        {
            MoveTo(screenX, screenY);
            var (down, up) = MouseFlags(button);
            SendMouse(down);
            Thread.Sleep(20);
            SendMouse(up);
            return;
        }
        PostMouseClick(screenX, screenY, button);
    }

    public void Drag(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, int holdMs = 50)
    {
        if (Mode == InputMode.SendInput)
        {
            MoveTo(fromX, fromY);
            var (down, up) = MouseFlags(button);
            SendMouse(down);
            Thread.Sleep(holdMs);
            MoveTo(toX, toY);
            Thread.Sleep(holdMs);
            SendMouse(up);
            return;
        }
        // PostMessage drag: down at start, multiple WM_MOUSEMOVE waypoints, up at end.
        PostMouseDown(fromX, fromY, button);
        Thread.Sleep(holdMs);
        PostMouseMove(toX, toY);
        Thread.Sleep(holdMs);
        PostMouseUp(toX, toY, button);
    }

    public void PressKey(int virtualKey)
    {
        if (Mode == InputMode.SendInput)
        {
            SendKey(virtualKey, Native.KEYEVENTF_KEYDOWN);
            Thread.Sleep(20);
            SendKey(virtualKey, Native.KEYEVENTF_KEYUP);
            return;
        }
        PostKey(virtualKey, down: true);
        Thread.Sleep(20);
        PostKey(virtualKey, down: false);
    }

    public void KeyDown(int virtualKey)
    {
        if (Mode == InputMode.SendInput) { SendKey(virtualKey, Native.KEYEVENTF_KEYDOWN); return; }
        PostKey(virtualKey, down: true);
    }

    public void KeyUp(int virtualKey)
    {
        if (Mode == InputMode.SendInput) { SendKey(virtualKey, Native.KEYEVENTF_KEYUP); return; }
        PostKey(virtualKey, down: false);
    }

    public void TypeText(string text)
    {
        if (Mode == InputMode.SendInput)
        {
            foreach (var ch in text)
            {
                var input = new Native.INPUT
                {
                    type = Native.INPUT_KEYBOARD,
                    u = new Native.InputUnion
                    {
                        ki = new Native.KEYBDINPUT
                        {
                            wScan = ch,
                            dwFlags = Native.KEYEVENTF_UNICODE,
                        }
                    }
                };
                Native.SendInput(1, [input], Marshal.SizeOf<Native.INPUT>());

                input.u.ki.dwFlags |= Native.KEYEVENTF_KEYUP;
                Native.SendInput(1, [input], Marshal.SizeOf<Native.INPUT>());
            }
            return;
        }
        // PostMessage: WM_CHAR per character. Doesn't cover modifier-augmented input (Shift+A etc.)
        // — that needs explicit KeyDown(VK_SHIFT) + WM_CHAR + KeyUp(VK_SHIFT). For typing-text use
        // case (search boxes / chat) plain WM_CHAR is the right call.
        var hwnd = TargetWindow;
        if (hwnd == nint.Zero) return;
        WithMaybeWindowPos(hwnd, () =>
        {
            foreach (var ch in text)
            {
                Native.PostMessage(hwnd, Native.WM_CHAR, ch, nint.Zero);
            }
        });
    }

    // ============================================================
    //  PostMessage path — mouse + keyboard
    // ============================================================

    private void PostKey(int virtualKey, bool down)
    {
        var hwnd = TargetWindow;
        if (hwnd == nint.Zero) return;

        var scan = Native.MapVirtualKey((uint)virtualKey, Native.MAPVK_VK_TO_VSC);
        // lParam layout for WM_KEYDOWN/UP:
        //   bits 0-15:  repeat count (1)
        //   bits 16-23: scan code
        //   bit 24:     extended-key flag (we leave 0; arrows/insert/home/end need this set, but
        //               most games tolerate without it; revisit if specific keys misfire)
        //   bits 25-28: reserved
        //   bit 29:     context code (always 0 for KEYDOWN/UP)
        //   bit 30:     previous key state (0 = was up, 1 = was down)
        //   bit 31:     transition state (0 = key down, 1 = key up)
        nint lParam = (nint)(1u | (scan << 16) | (down ? 0u : (1u << 30) | (1u << 31)));
        var msg = down ? Native.WM_KEYDOWN : Native.WM_KEYUP;

        WithMaybeWindowPos(hwnd, () => Native.PostMessage(hwnd, msg, virtualKey, lParam));
    }

    private void PostMouseClick(int screenX, int screenY, MouseButton button)
    {
        PostMouseDown(screenX, screenY, button);
        Thread.Sleep(20);
        PostMouseUp(screenX, screenY, button);
    }

    private void PostMouseDown(int screenX, int screenY, MouseButton button)
    {
        var hwnd = TargetWindow;
        if (hwnd == nint.Zero) return;
        var (downMsg, _, wParamDown, _) = MouseMessages(button);
        var (cx, cy) = ScreenToClient(hwnd, screenX, screenY);
        var lp = MakeMouseLParam(cx, cy);
        WithMaybeWindowPos(hwnd, () => Native.PostMessage(hwnd, downMsg, wParamDown, lp));
    }

    private void PostMouseUp(int screenX, int screenY, MouseButton button)
    {
        var hwnd = TargetWindow;
        if (hwnd == nint.Zero) return;
        var (_, upMsg, _, wParamUp) = MouseMessages(button);
        var (cx, cy) = ScreenToClient(hwnd, screenX, screenY);
        var lp = MakeMouseLParam(cx, cy);
        WithMaybeWindowPos(hwnd, () => Native.PostMessage(hwnd, upMsg, wParamUp, lp));
    }

    private void PostMouseMove(int screenX, int screenY)
    {
        var hwnd = TargetWindow;
        if (hwnd == nint.Zero) return;
        var (cx, cy) = ScreenToClient(hwnd, screenX, screenY);
        var lp = MakeMouseLParam(cx, cy);
        WithMaybeWindowPos(hwnd, () => Native.PostMessage(hwnd, Native.WM_MOUSEMOVE, nint.Zero, lp));
    }

    /// <summary>If <see cref="Mode"/> is <see cref="InputMode.PostMessageWithPos"/>, briefly
    /// SetWindowPos(NOMOVE|NOSIZE) the target before invoking <paramref name="action"/>. That
    /// nudges DWM / the target's hit-test caches without actually changing geometry — workaround
    /// for games that consult window state inside their input handler.</summary>
    private void WithMaybeWindowPos(nint hwnd, Action action)
    {
        if (Mode == InputMode.PostMessageWithPos)
        {
            // SWP_NOACTIVATE keeps focus where it is; SWP_NOSENDCHANGING skips the NCCALCSIZE
            // round-trip; SWP_DEFERERASE avoids a redraw flicker.
            const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER
                | Native.SWP_NOACTIVATE | Native.SWP_NOSENDCHANGING | Native.SWP_DEFERERASE;
            Native.SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, flags);
        }
        action();
    }

    private static (uint downMsg, uint upMsg, nint wParamDown, nint wParamUp) MouseMessages(MouseButton button) => button switch
    {
        MouseButton.Right  => (Native.WM_RBUTTONDOWN, Native.WM_RBUTTONUP, (nint)Native.MK_RBUTTON, nint.Zero),
        MouseButton.Middle => (Native.WM_MBUTTONDOWN, Native.WM_MBUTTONUP, (nint)Native.MK_MBUTTON, nint.Zero),
        _                  => (Native.WM_LBUTTONDOWN, Native.WM_LBUTTONUP, (nint)Native.MK_LBUTTON, nint.Zero),
    };

    private static nint MakeMouseLParam(int x, int y)
    {
        // lParam packs (y << 16) | (x & 0xFFFF). Cast through ushort to handle negative coords.
        var packed = ((uint)(ushort)y << 16) | (uint)(ushort)x;
        return (nint)packed;
    }

    private static (int x, int y) ScreenToClient(nint hwnd, int screenX, int screenY)
    {
        var pt = new Native.POINT { x = screenX, y = screenY };
        Native.ScreenToClient(hwnd, ref pt);
        return (pt.x, pt.y);
    }

    // ============================================================
    //  SendInput path (original)
    // ============================================================

    private static (uint down, uint up) MouseFlags(MouseButton button) => button switch
    {
        MouseButton.Left => (Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_LEFTUP),
        MouseButton.Right => (Native.MOUSEEVENTF_RIGHTDOWN, Native.MOUSEEVENTF_RIGHTUP),
        MouseButton.Middle => (Native.MOUSEEVENTF_MIDDLEDOWN, Native.MOUSEEVENTF_MIDDLEUP),
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    private static void SendMouse(uint flags)
    {
        var input = new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = flags } }
        };
        Native.SendInput(1, [input], Marshal.SizeOf<Native.INPUT>());
    }

    private static void SendKey(int virtualKey, uint flags)
    {
        var input = new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    dwFlags = flags,
                }
            }
        };
        Native.SendInput(1, [input], Marshal.SizeOf<Native.INPUT>());
    }

    // ============================================================
    //  Native interop
    // ============================================================

    private static class Native
    {
        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        public const uint KEYEVENTF_KEYDOWN = 0x0000;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        // Window messages used by the PostMessage path
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const uint WM_CHAR = 0x0102;
        public const uint WM_MOUSEMOVE = 0x0200;
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_RBUTTONDOWN = 0x0204;
        public const uint WM_RBUTTONUP = 0x0205;
        public const uint WM_MBUTTONDOWN = 0x0207;
        public const uint WM_MBUTTONUP = 0x0208;

        public const uint MK_LBUTTON = 0x0001;
        public const uint MK_RBUTTON = 0x0002;
        public const uint MK_MBUTTON = 0x0010;

        public const uint MAPVK_VK_TO_VSC = 0;

        // SetWindowPos flags for the PostMessageWithPos trick
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOSENDCHANGING = 0x0400;
        public const uint SWP_DEFERERASE = 0x2000;

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public nint dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public nint dwExtraInfo;
        }
    }
}
