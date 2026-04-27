namespace BrickBot.Modules.Capture.Models;

/// <summary>
/// Metadata about a capturable top-level window. <see cref="ClassName"/> + <see cref="ProcessName"/>
/// help users disambiguate when a process spawns multiple windows with similar titles. The
/// finder excludes cloaked UWP shell hosts and BrickBot's own windows so they don't pollute
/// the picker.
/// </summary>
public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    string ClassName,
    int X,
    int Y,
    int Width,
    int Height);
