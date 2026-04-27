using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Models;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Core.Utilities;
using BrickBot.Modules.Setting.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BrickBot.Modules.Setting.Services;

/// <summary>
/// Loads, caches, and persists application-wide settings (theme, language, log level, window state)
/// to data/settings/global.json. Emits <see cref="SettingEvents.GLOBAL_SETTINGS_CHANGED"/> on every write.
/// </summary>
public interface IGlobalSettingService
{
    Task<GlobalSettings> GetSettingsAsync();
    Task UpdateSettingsAsync(GlobalSettings settings);
    Task UpdateSettingAsync(string key, string value);
    Task ResetSettingsAsync();
    Task<LogLevel> GetLogLevelAsync();

    /// <summary>
    /// Persists ONLY the Window section. Does NOT emit GLOBAL_SETTINGS_CHANGED — used at
    /// shutdown to avoid the event chain re-entering the UI thread while OnFormClosed waits.
    /// </summary>
    Task UpdateWindowSettingsAsync(WindowSettings window);
}

public sealed class GlobalSettingService : IGlobalSettingService
{
    private const string CacheKey = "GlobalSettings";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(30);

    private readonly string _settingsFilePath;
    private readonly IMemoryCache _cache;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IProfileEventBus _eventBus;
    private readonly ILogHelper _logger;

    public GlobalSettingService(
        IGlobalPathService globalPaths,
        IAppEnvironment appEnvironment,
        IMemoryCache cache,
        IProfileEventBus eventBus,
        ILogHelper logger)
    {
        _appEnvironment = appEnvironment;
        _cache = cache;
        _eventBus = eventBus;
        _logger = logger;

        _settingsFilePath = globalPaths.GlobalSettingsFilePath;
        globalPaths.EnsureDirectoriesExist();
    }

    public async Task<GlobalSettings> GetSettingsAsync()
    {
        return await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiry;

            GlobalSettings settings;
            if (File.Exists(_settingsFilePath))
            {
                settings = await JsonHelper.DeserializeFromFileAsync<GlobalSettings>(_settingsFilePath).ConfigureAwait(false)
                    ?? new GlobalSettings();
                _appEnvironment.MinimumLogLevel = ParseLogLevel(settings.LogLevel);
            }
            else
            {
                settings = new GlobalSettings();
                _appEnvironment.MinimumLogLevel = ParseLogLevel(settings.LogLevel);
                await SaveSettingsAsync(settings).ConfigureAwait(false);
            }
            return settings;
        }).ConfigureAwait(false) ?? new GlobalSettings();
    }

    public async Task<LogLevel> GetLogLevelAsync()
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        return ParseLogLevel(settings.LogLevel);
    }

    public async Task UpdateSettingsAsync(GlobalSettings settings)
    {
        settings.LastUpdated = DateTime.UtcNow;
        await SaveSettingsAsync(settings).ConfigureAwait(false);
        InvalidateCache();
        await _eventBus.EmitAsync(ModuleNames.SETTING, SettingEvents.GLOBAL_SETTINGS_CHANGED, settings).ConfigureAwait(false);
    }

    public async Task UpdateSettingAsync(string key, string value)
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);

        switch (key.ToLowerInvariant())
        {
            case "theme":
                settings.Theme = value.ToLowerInvariant();
                break;
            case "annotationlevel":
                settings.AnnotationLevel = value.ToLowerInvariant();
                break;
            case "loglevel":
                settings.LogLevel = value;
                _appEnvironment.MinimumLogLevel = ParseLogLevel(value);
                break;
            case "language":
                settings.Language = value.ToLowerInvariant();
                break;
            default:
                throw new ArgumentException($"Unknown setting key: {key}");
        }

        await UpdateSettingsAsync(settings).ConfigureAwait(false);
    }

    public async Task ResetSettingsAsync()
    {
        var defaults = new GlobalSettings();
        await SaveSettingsAsync(defaults).ConfigureAwait(false);
        InvalidateCache();
        await _eventBus.EmitAsync(ModuleNames.SETTING, SettingEvents.GLOBAL_SETTINGS_CHANGED, defaults).ConfigureAwait(false);
    }

    public async Task UpdateWindowSettingsAsync(WindowSettings window)
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        settings.Window = window;
        settings.LastUpdated = DateTime.UtcNow;
        await SaveSettingsAsync(settings).ConfigureAwait(false);
        InvalidateCache();
        // Intentionally no event emission — window geometry is a backend-only concern,
        // and emitting at shutdown re-enters the UI thread that's awaiting us synchronously.
    }

    private async Task SaveSettingsAsync(GlobalSettings settings)
    {
        await JsonHelper.SerializeToFileAsync(_settingsFilePath, settings).ConfigureAwait(false);
        _logger.Verbose($"Settings saved to {_settingsFilePath}", "GlobalSettings");
    }

    private void InvalidateCache() => _cache.Remove(CacheKey);

    private static LogLevel ParseLogLevel(string raw)
    {
        return Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var level) ? level : LogLevel.Off;
    }
}
