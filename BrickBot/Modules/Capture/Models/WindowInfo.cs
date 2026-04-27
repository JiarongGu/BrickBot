namespace BrickBot.Modules.Capture.Models;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    int X,
    int Y,
    int Width,
    int Height);
