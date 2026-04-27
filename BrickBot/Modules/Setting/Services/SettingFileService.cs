using System.Text.Json;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;

namespace BrickBot.Modules.Setting.Services;

/// <summary>
/// Generic JSON-file CRUD for arbitrary settings files in data/settings/.
/// Frontend uses this to persist anything that doesn't fit GlobalSettings (UI layout, recent items, etc.).
/// Refuses to write/read "global" — that file is owned by <see cref="IGlobalSettingService"/>.
/// </summary>
public interface ISettingFileService
{
    /// <summary>Returns raw JSON content of {filename}.json, or null if missing.</summary>
    Task<string?> GetSettingsFileAsync(string filename);

    /// <summary>Writes {filename}.json. <paramref name="jsonContent"/> must parse as valid JSON.</summary>
    Task SaveSettingsFileAsync(string filename, string jsonContent);

    /// <summary>Deletes {filename}.json (no-op if missing).</summary>
    Task DeleteSettingsFileAsync(string filename);

    Task<bool> SettingsFileExistsAsync(string filename);

    /// <summary>Returns filenames (no extension) of every *.json in data/settings/.</summary>
    Task<string[]> ListSettingsFilesAsync();
}

public sealed class SettingFileService : ISettingFileService
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingFileService(IGlobalPathService globalPaths, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public async Task<string?> GetSettingsFileAsync(string filename)
    {
        ValidateFilename(filename);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var filePath = GetFilePath(filename);
            if (!File.Exists(filePath)) return null;

            var content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            try { JsonDocument.Parse(content); }
            catch (JsonException ex)
            {
                _logger.Error($"Invalid JSON in {filename}: {ex.Message}", "SettingFile", ex);
                throw new InvalidOperationException($"Settings file contains invalid JSON: {filename}");
            }
            return content;
        }
        finally { _lock.Release(); }
    }

    public async Task SaveSettingsFileAsync(string filename, string jsonContent)
    {
        ValidateFilename(filename);
        try { JsonDocument.Parse(jsonContent); }
        catch (JsonException ex) { throw new ArgumentException($"Invalid JSON content: {ex.Message}"); }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var filePath = GetFilePath(filename);
            var tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, jsonContent).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
            _logger.Info($"Settings file saved: {filename}", "SettingFile");
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteSettingsFileAsync(string filename)
    {
        ValidateFilename(filename);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var filePath = GetFilePath(filename);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.Info($"Settings file deleted: {filename}", "SettingFile");
            }
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> SettingsFileExistsAsync(string filename)
    {
        ValidateFilename(filename);
        await _lock.WaitAsync().ConfigureAwait(false);
        try { return File.Exists(GetFilePath(filename)); }
        finally { _lock.Release(); }
    }

    public async Task<string[]> ListSettingsFilesAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var files = Directory.GetFiles(_globalPaths.GlobalSettingsDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name) && !string.Equals(name, "global", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return files!;
        }
        finally { _lock.Release(); }
    }

    private string GetFilePath(string filename) =>
        _globalPaths.GetGlobalSettingsFilePath($"{filename}.json");

    private static void ValidateFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentException("Filename cannot be empty");

        var invalid = Path.GetInvalidFileNameChars();
        if (filename.Any(c => invalid.Contains(c)) ||
            filename.Contains("..") ||
            filename.Contains('/') ||
            filename.Contains('\\'))
        {
            throw new ArgumentException($"Invalid filename: {filename}");
        }

        if (filename.Equals("global", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("'global' is reserved — use IGlobalSettingService instead");
    }
}
