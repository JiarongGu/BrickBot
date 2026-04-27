using System.Runtime.InteropServices;
using BrickBot.Modules.Input.Models;

namespace BrickBot.Modules.Input.Services;

/// <summary>
/// Win32 SendInput-based input simulation. Coordinates are absolute screen pixels.
/// Translation from window-relative coords is the script host's job.
/// </summary>
public sealed class SendInputService : IInputService
{
    public void MoveTo(int screenX, int screenY)
    {
        Native.SetCursorPos(screenX, screenY);
    }

    public void Click(int screenX, int screenY, MouseButton button = MouseButton.Left)
    {
        MoveTo(screenX, screenY);
        var (down, up) = MouseFlags(button);
        SendMouse(down);
        Thread.Sleep(20);
        SendMouse(up);
    }

    public void Drag(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, int holdMs = 50)
    {
        MoveTo(fromX, fromY);
        var (down, up) = MouseFlags(button);
        SendMouse(down);
        Thread.Sleep(holdMs);
        MoveTo(toX, toY);
        Thread.Sleep(holdMs);
        SendMouse(up);
    }

    public void PressKey(int virtualKey)
    {
        SendKey(virtualKey, Native.KEYEVENTF_KEYDOWN);
        Thread.Sleep(20);
        SendKey(virtualKey, Native.KEYEVENTF_KEYUP);
    }

    public void KeyDown(int virtualKey) => SendKey(virtualKey, Native.KEYEVENTF_KEYDOWN);
    public void KeyUp(int virtualKey) => SendKey(virtualKey, Native.KEYEVENTF_KEYUP);

    public void TypeText(string text)
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
    }

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

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
