using System.Collections.Concurrent;
using BrickBot.Modules.Core.Models;

namespace BrickBot.Modules.Core.Helpers;

/// <summary>
/// Log levels for categorizing log messages. Frontend mirrors this enum exactly.
/// </summary>
public enum LogLevel
{
    /// <summary>Verbose - extremely detailed (high-frequency events: mouse moves, IPC traffic).</summary>
    Verbose = 0,
    /// <summary>Debug - verbose diagnostic information.</summary>
    Debug = 1,
    /// <summary>Info - general informational messages.</summary>
    Info = 2,
    /// <summary>Warn - warning messages.</summary>
    Warn = 3,
    /// <summary>Error - error messages.</summary>
    Error = 4,
    /// <summary>All - filtering pseudo-value: log everything.</summary>
    All = -1,
    /// <summary>Off - filtering pseudo-value: log nothing.</summary>
    Off = -2,
}

/// <summary>
/// Centralized logging interface. Writes batched async log lines to a file under data\logs
/// and (in dev mode) colored output to the console.
/// </summary>
public interface ILogHelper
{
    LogLevel MinimumLevel { get; set; }
    void Verbose(string message, string? source = null);
    void Debug(string message, string? source = null);
    void Info(string message, string? source = null);
    void Warn(string message, string? source = null);
    void Error(string message, string? source = null, Exception? exception = null);
    void Log(LogLevel level, string message, string? source = null, Exception? exception = null);
    Task FlushAsync();
}

/// <summary>
/// Centralized logging service. Thread-safe with batched (every 100ms) async file writes.
/// </summary>
public sealed class LogHelper : ILogHelper, IDisposable
{
    private readonly IAppEnvironment _appEnvironment;
    private readonly string _logsBaseDirectory;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<(string logFile, string logEntry)> _logQueue = new();
    private readonly global::System.Timers.Timer _batchTimer;
    private readonly object _batchLock = new();
    private const int BatchIntervalMs = 100;
    private bool _disposed;

    public LogLevel MinimumLevel
    {
        get => _appEnvironment.MinimumLogLevel;
        set => _appEnvironment.MinimumLogLevel = value;
    }

    public LogHelper(IAppEnvironment appEnvironment)
    {
        _appEnvironment = appEnvironment;
        _logsBaseDirectory = Path.Combine(appEnvironment.DataDirectory, "logs");
        Directory.CreateDirectory(_logsBaseDirectory);

        _batchTimer = new global::System.Timers.Timer(BatchIntervalMs) { AutoReset = true };
        _batchTimer.Elapsed += (_, _) => FlushLogBatch();
        _batchTimer.Start();
    }

    /// <summary>Convenience factory for the bootstrap phase before DI is up.</summary>
    public static LogHelper Create(IAppEnvironment environment) => new(environment);

    public void Verbose(string message, string? source = null) => Log(LogLevel.Verbose, message, source);
    public void Debug(string message, string? source = null) => Log(LogLevel.Debug, message, source);
    public void Info(string message, string? source = null) => Log(LogLevel.Info, message, source);
    public void Warn(string message, string? source = null) => Log(LogLevel.Warn, message, source);
    public void Error(string message, string? source = null, Exception? exception = null) => Log(LogLevel.Error, message, source, exception);

    public void Log(LogLevel level, string message, string? source = null, Exception? exception = null)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logSource = source ?? "App";
        var levelStr = level.ToString().ToUpper().PadRight(7);
        var logEntry = $"[{timestamp}] [{levelStr}] [{logSource}] {message}";

        if (exception != null)
        {
            logEntry += $"\n  Exception: {exception.GetType().Name}: {exception.Message}";
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                logEntry += $"\n  StackTrace: {exception.StackTrace}";
            }
        }

        // Console: dev only, respects log level
        if (_appEnvironment.IsDevelopment && ShouldLog(level))
        {
            WriteToConsole(level, logEntry);
        }

        // File: respects log level in both dev and prod
        if (ShouldLog(level))
        {
            QueueLogEntry(logEntry);
        }
    }

    private bool ShouldLog(LogLevel level)
    {
        if (_appEnvironment.MinimumLogLevel == LogLevel.Off) return false;
        if (_appEnvironment.MinimumLogLevel == LogLevel.All) return true;
        return level >= _appEnvironment.MinimumLogLevel;
    }

    public async Task FlushAsync()
    {
        FlushLogBatch();
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try { } finally { _writeLock.Release(); }
    }

    private static void WriteToConsole(LogLevel level, string logEntry)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = level switch
            {
                LogLevel.Verbose => ConsoleColor.DarkGray,
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warn => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => ConsoleColor.White,
            };
            Console.WriteLine(logEntry);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private void QueueLogEntry(string logEntry)
    {
        if (_disposed) return;
        var logFile = Path.Combine(_logsBaseDirectory, $"{DateTime.UtcNow:yyyy-MM-dd}.log");
        _logQueue.Enqueue((logFile, logEntry));
    }

    private void FlushLogBatch()
    {
        if (_disposed || _logQueue.IsEmpty) return;

        lock (_batchLock)
        {
            if (_logQueue.IsEmpty) return;

            try
            {
                var batch = new List<(string logFile, string logEntry)>();
                while (_logQueue.TryDequeue(out var entry)) batch.Add(entry);
                if (batch.Count == 0) return;

                var grouped = batch
                    .GroupBy(x => x.logFile)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.logEntry).ToList());

                foreach (var (logFile, entries) in grouped)
                {
                    try
                    {
                        var combined = string.Join(Environment.NewLine, entries) + Environment.NewLine;
                        File.AppendAllText(logFile, combined);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LogHelper] Failed to write {entries.Count} entries to {logFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LogHelper] Error in FlushLogBatch: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _batchTimer.Stop();
        FlushLogBatch();
        _batchTimer.Dispose();
        _writeLock.Dispose();
    }
}
