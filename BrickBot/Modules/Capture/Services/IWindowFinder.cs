using BrickBot.Modules.Capture.Models;

namespace BrickBot.Modules.Capture.Services;

public interface IWindowFinder
{
    IReadOnlyList<WindowInfo> ListVisibleWindows();
    WindowInfo? FindByTitle(string titleSubstring);
    WindowInfo? GetByHandle(nint handle);
}
