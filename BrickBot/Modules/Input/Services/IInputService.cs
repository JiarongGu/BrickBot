using BrickBot.Modules.Input.Models;

namespace BrickBot.Modules.Input.Services;

public interface IInputService
{
    /// <summary>Delivery mode for keyboard + mouse events. Default <see cref="InputMode.SendInput"/>.
    /// Set by <c>RunnerService</c> at run start from the active profile's config; existing
    /// scripts pick up the new mode without changes.</summary>
    InputMode Mode { get; set; }

    /// <summary>Target window for <see cref="InputMode.PostMessage"/> /
    /// <see cref="InputMode.PostMessageWithPos"/>. Ignored for <see cref="InputMode.SendInput"/>.
    /// Set by RunnerService at run start.</summary>
    nint TargetWindow { get; set; }

    /// <summary>Move cursor to absolute screen coordinates. Ignored under PostMessage modes
    /// (those route mouse events directly to the target window).</summary>
    void MoveTo(int screenX, int screenY);

    /// <summary>Click at absolute screen coordinates. Under PostMessage modes the coords are
    /// converted to client-relative for <c>WM_LBUTTONDOWN</c>.</summary>
    void Click(int screenX, int screenY, MouseButton button = MouseButton.Left);

    /// <summary>Drag from one screen point to another.</summary>
    void Drag(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, int holdMs = 50);

    /// <summary>Press + release a virtual key.</summary>
    void PressKey(int virtualKey);

    /// <summary>Hold a virtual key down (caller responsible for releasing).</summary>
    void KeyDown(int virtualKey);

    /// <summary>Release a virtual key.</summary>
    void KeyUp(int virtualKey);

    /// <summary>Type ASCII text via individual key events.</summary>
    void TypeText(string text);
}
