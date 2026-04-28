namespace BrickBot.Modules.Input.Models;

/// <summary>
/// How <see cref="Services.IInputService"/> delivers input events. Selectable per-profile via
/// <c>ProfileConfiguration.Input.Mode</c>.
///
/// <list type="bullet">
///   <item><see cref="SendInput"/> (default) — Win32 <c>SendInput</c>; OS-level. Real cursor
///     moves, real keys; works everywhere but steals focus/cursor and can't drive a
///     background window.</item>
///   <item><see cref="PostMessage"/> — <c>PostMessage(hwnd, WM_KEYDOWN/UP, ...)</c> straight to
///     the target window. NO focus/cursor steal; works while another app is foreground. Some
///     games ignore plain WM_KEYDOWN (raw-input games like FPS titles) — try this first
///     before SendInput on action-game targets.</item>
///   <item><see cref="PostMessageWithPos"/> — same as PostMessage but with a brief
///     <c>SetWindowPos</c> dance to make the target's hit-test report "I am visible".
///     Useful for games that consult cursor-relative state inside their input handler.</item>
/// </list>
///
/// Serialized as camelCase string — frontend TypeScript type is
/// <c>'sendInput' | 'postMessage' | 'postMessageWithPos'</c>.
/// </summary>
public enum InputMode
{
    SendInput,
    PostMessage,
    PostMessageWithPos,
}
