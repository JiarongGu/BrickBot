namespace BrickBot.Modules.Profile.Models;

/// <summary>
/// Per-profile config persisted to data/profiles/{id}/config.json. BrickBot-domain shape:
/// describes the game target (window match), how to capture frames, and where the user's script lives.
/// </summary>
public sealed class ProfileConfiguration
{
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Rule used to find the target game window each run.</summary>
    public WindowMatchRule WindowMatch { get; set; } = new();

    /// <summary>Capture pipeline settings (mode, FPS, ROI default).</summary>
    public CaptureSettings Capture { get; set; } = new();

    /// <summary>Script settings: which JS file to run, autostart, etc.</summary>
    public ScriptSettings Script { get; set; } = new();

    /// <summary>UI hints persisted with the profile (panel sizes, last-used template, etc.).</summary>
    public Dictionary<string, string> UiHints { get; set; } = new();
}

/// <summary>How BrickBot finds the game window when this profile activates.</summary>
public sealed class WindowMatchRule
{
    /// <summary>Match strategy: "title", "titleClass", "process".</summary>
    public string Strategy { get; set; } = "title";

    /// <summary>Title regex / substring (interpretation depends on Strategy).</summary>
    public string? TitlePattern { get; set; }

    /// <summary>Optional Win32 class name filter.</summary>
    public string? ClassName { get; set; }

    /// <summary>Optional process name filter (e.g. "MyGame.exe").</summary>
    public string? ProcessName { get; set; }
}

/// <summary>Capture pipeline configuration.</summary>
public sealed class CaptureSettings
{
    /// <summary>Capture backend: "winRT" (default, hardware-accelerated) or "bitBlt" (compatibility).</summary>
    public string Mode { get; set; } = "winRT";

    /// <summary>Target frames per second (capture decoupled from script tick rate).</summary>
    public int TargetFps { get; set; } = 60;

    /// <summary>Optional default region of interest. null = full window.</summary>
    public RoiSettings? DefaultRoi { get; set; }
}

/// <summary>Region of interest in window-relative pixels.</summary>
public sealed class RoiSettings
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>Script wiring for this profile.</summary>
public sealed class ScriptSettings
{
    /// <summary>Script file relative to data/profiles/{id}/scripts/ (e.g. "main.js"). null = no script.</summary>
    public string? EntryFile { get; set; }

    /// <summary>Auto-start on profile switch.</summary>
    public bool AutoStart { get; set; }

    /// <summary>Tick rate (ms between script iterations) — script can override.</summary>
    public int TickIntervalMs { get; set; } = 50;
}
