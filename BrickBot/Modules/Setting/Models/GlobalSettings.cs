namespace BrickBot.Modules.Setting.Models;

/// <summary>
/// Global app-wide settings persisted to data/settings/global.json.
/// Cross-profile values only — anything per-profile lives in <c>ProfileConfiguration</c>.
/// </summary>
public sealed class GlobalSettings
{
    /// <summary>Theme mode: "light", "dark", or "auto".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Language code: "en", "cn", etc.</summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Backend file-log level: "All", "Verbose", "Debug", "Info", "Warn", "Error", "Off".
    /// Parsed case-insensitively into <see cref="Modules.Core.Helpers.LogLevel"/>.
    /// </summary>
    public string LogLevel { get; set; } = "Info";

    /// <summary>UI annotation/tooltip level: "all", "more", "less", "off".</summary>
    public string AnnotationLevel { get; set; } = "all";

    /// <summary>UTC timestamp of the last UpdateSettings call.</summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>Window position, size, maximized state.</summary>
    public WindowSettings Window { get; set; } = new();
}

/// <summary>Persisted main-window placement.</summary>
public sealed class WindowSettings
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool Maximized { get; set; }
}
