using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Setting.Models;
using BrickBot.Modules.Setting.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Setting;

/// <summary>
/// IPC entrypoint for the SETTING module.
/// Handles: GET_GLOBAL, UPDATE_GLOBAL, UPDATE_FIELD, RESET_GLOBAL, GET_FILE, SAVE_FILE,
/// DELETE_FILE, FILE_EXISTS, LIST_FILES, GET_LANGUAGE, GET_AVAILABLE_LANGUAGES,
/// LANGUAGE_EXISTS, RESET_WINDOW_STATE.
/// </summary>
public sealed class SettingFacade : BaseFacade
{
    private readonly IGlobalSettingService _globalSettings;
    private readonly ISettingFileService _fileService;
    private readonly ILanguageService _languageService;
    private readonly IWindowStateService _windowStateService;
    private readonly IProfileEventBus _eventBus;
    private readonly PayloadHelper _payloadHelper;

    public SettingFacade(
        IGlobalSettingService globalSettings,
        ISettingFileService fileService,
        ILanguageService languageService,
        IWindowStateService windowStateService,
        IProfileEventBus eventBus,
        PayloadHelper payloadHelper,
        ILogger<SettingFacade> logger) : base(logger)
    {
        _globalSettings = globalSettings;
        _fileService = fileService;
        _languageService = languageService;
        _windowStateService = windowStateService;
        _eventBus = eventBus;
        _payloadHelper = payloadHelper;
    }

    protected override async Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            // Global settings
            "GET_GLOBAL" => await _globalSettings.GetSettingsAsync().ConfigureAwait(false),
            "UPDATE_GLOBAL" => await UpdateGlobalAsync(request).ConfigureAwait(false),
            "UPDATE_FIELD" => await UpdateFieldAsync(request).ConfigureAwait(false),
            "RESET_GLOBAL" => await ResetGlobalAsync().ConfigureAwait(false),

            // Settings files
            "GET_FILE" => await GetFileAsync(request).ConfigureAwait(false),
            "SAVE_FILE" => await SaveFileAsync(request).ConfigureAwait(false),
            "DELETE_FILE" => await DeleteFileAsync(request).ConfigureAwait(false),
            "FILE_EXISTS" => new { exists = await _fileService.SettingsFileExistsAsync(_payloadHelper.GetRequiredValue<string>(request.Payload, "filename")).ConfigureAwait(false) },
            "LIST_FILES" => new { files = await _fileService.ListSettingsFilesAsync().ConfigureAwait(false) },

            // Language
            "GET_LANGUAGE" => await GetLanguageAsync(request).ConfigureAwait(false),
            "GET_AVAILABLE_LANGUAGES" => new { languages = await _languageService.GetAvailableLanguagesAsync().ConfigureAwait(false) },
            "LANGUAGE_EXISTS" => new { exists = await _languageService.LanguageExistsAsync(_payloadHelper.GetRequiredValue<string>(request.Payload, "languageCode")).ConfigureAwait(false) },

            // Window state
            "RESET_WINDOW_STATE" => await ResetWindowStateAsync().ConfigureAwait(false),

            _ => throw new InvalidOperationException($"Unknown SETTING message type: {request.Type}"),
        };
    }

    private async Task<object> UpdateGlobalAsync(IpcRequest request)
    {
        var theme = _payloadHelper.GetOptionalValue<string>(request.Payload, "theme");
        var annotationLevel = _payloadHelper.GetOptionalValue<string>(request.Payload, "annotationLevel");
        var logLevel = _payloadHelper.GetOptionalValue<string>(request.Payload, "logLevel");
        var language = _payloadHelper.GetOptionalValue<string>(request.Payload, "language");

        var settings = await _globalSettings.GetSettingsAsync().ConfigureAwait(false);
        if (theme != null) settings.Theme = theme;
        if (annotationLevel != null) settings.AnnotationLevel = annotationLevel;
        if (logLevel != null) settings.LogLevel = logLevel;
        if (language != null) settings.Language = language;

        await _globalSettings.UpdateSettingsAsync(settings).ConfigureAwait(false);
        return new { success = true, settings };
    }

    private async Task<object> UpdateFieldAsync(IpcRequest request)
    {
        var key = _payloadHelper.GetRequiredValue<string>(request.Payload, "key");
        var value = _payloadHelper.GetRequiredValue<string>(request.Payload, "value");
        await _globalSettings.UpdateSettingAsync(key, value).ConfigureAwait(false);
        return new { success = true, key, value };
    }

    private async Task<object> ResetGlobalAsync()
    {
        await _globalSettings.ResetSettingsAsync().ConfigureAwait(false);
        var settings = await _globalSettings.GetSettingsAsync().ConfigureAwait(false);
        return new { success = true, settings };
    }

    private async Task<object> GetFileAsync(IpcRequest request)
    {
        var filename = _payloadHelper.GetRequiredValue<string>(request.Payload, "filename");
        var content = await _fileService.GetSettingsFileAsync(filename).ConfigureAwait(false);
        return content is null
            ? new { success = false, content = (string?)null }
            : new { success = true, content = (string?)content };
    }

    private async Task<object> SaveFileAsync(IpcRequest request)
    {
        var filename = _payloadHelper.GetRequiredValue<string>(request.Payload, "filename");
        var content = _payloadHelper.GetRequiredValue<string>(request.Payload, "content");
        await _fileService.SaveSettingsFileAsync(filename, content).ConfigureAwait(false);
        return new { success = true, filename };
    }

    private async Task<object> DeleteFileAsync(IpcRequest request)
    {
        var filename = _payloadHelper.GetRequiredValue<string>(request.Payload, "filename");
        await _fileService.DeleteSettingsFileAsync(filename).ConfigureAwait(false);
        return new { success = true, filename };
    }

    private async Task<object> GetLanguageAsync(IpcRequest request)
    {
        var code = _payloadHelper.GetRequiredValue<string>(request.Payload, "languageCode");
        var language = await _languageService.GetLanguageAsync(code).ConfigureAwait(false);
        return language is null
            ? new { success = false, language = (LanguageSettings?)null }
            : new { success = true, language = (LanguageSettings?)language };
    }

    private async Task<object> ResetWindowStateAsync()
    {
        var settings = await _globalSettings.GetSettingsAsync().ConfigureAwait(false);
        settings.Window.X = null;
        settings.Window.Y = null;
        settings.Window.Width = null;
        settings.Window.Height = null;
        settings.Window.Maximized = false;
        await _globalSettings.UpdateSettingsAsync(settings).ConfigureAwait(false);

        var (width, height, _, _, _) = await _windowStateService.LoadWindowStateAsync().ConfigureAwait(false);
        await _eventBus.EmitAsync(ModuleNames.SETTING, SettingEvents.WINDOW_STATE_RESET, new
        {
            width,
            height,
            maximized = false,
        }).ConfigureAwait(false);

        return new { success = true };
    }
}
