using BrickBot.Modules.Input.Models;

namespace BrickBot.Modules.Input.Services;

public interface IInputService
{
    /// <summary>Move cursor to absolute screen coordinates.</summary>
    void MoveTo(int screenX, int screenY);

    /// <summary>Click at absolute screen coordinates.</summary>
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
