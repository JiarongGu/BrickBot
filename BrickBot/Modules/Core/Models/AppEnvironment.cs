using BrickBot.Modules.Core.Helpers;

namespace BrickBot.Modules.Core.Models;

/// <summary>
/// Application environment configuration. Provides base directory, dev-mode flag, and log level.
/// </summary>
public interface IAppEnvironment
{
    /// <summary>Base directory where the application is running.</summary>
    string BaseDirectory { get; }

    /// <summary>Base path for app data (logs, settings, profiles). Defaults to BaseDirectory/data.</summary>
    string DataDirectory { get; }

    /// <summary>True when the app is running in development mode.</summary>
    bool IsDevelopment { get; }

    /// <summary>Configured minimum log level. Mutable so the Settings UI can change it at runtime.</summary>
    LogLevel MinimumLogLevel { get; set; }
}

/// <summary>
/// Production implementation of IAppEnvironment.
/// Detects development mode via ASPNETCORE_ENVIRONMENT or a `.dev` marker file in the base directory.
/// </summary>
public sealed class AppEnvironment : IAppEnvironment
{
    private AppEnvironment(string baseDirectory, bool isDevelopment, LogLevel minimumLogLevel)
    {
        BaseDirectory = baseDirectory;
        DataDirectory = Path.Combine(baseDirectory, "data");
        IsDevelopment = isDevelopment;
        MinimumLogLevel = minimumLogLevel;
    }

    public string BaseDirectory { get; }
    public string DataDirectory { get; }
    public bool IsDevelopment { get; }
    public LogLevel MinimumLogLevel { get; set; }

    /// <summary>
    /// Creates an AppEnvironment instance. In dev mode the default log level is Debug; in prod it's Info.
    /// Override via the BRICKBOT_LOG_LEVEL environment variable (one of Verbose/Debug/Info/Warn/Error/Off/All).
    /// </summary>
    public static AppEnvironment Create(string baseDirectory)
    {
        var isDevelopment = CheckIfDevelopment(baseDirectory);
        var defaultLevel = isDevelopment ? LogLevel.Debug : LogLevel.Info;
        var logLevel = ReadLogLevelOverride() ?? defaultLevel;
        return new AppEnvironment(baseDirectory, isDevelopment, logLevel);
    }

    private static bool CheckIfDevelopment(string baseDirectory)
    {
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
            || File.Exists(Path.Combine(baseDirectory, ".dev"));
    }

    private static LogLevel? ReadLogLevelOverride()
    {
        var raw = Environment.GetEnvironmentVariable("BRICKBOT_LOG_LEVEL");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var parsed) ? parsed : null;
    }
}
