using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Core.Utilities;
using BrickBot.Modules.Setting.Models;

namespace BrickBot.Modules.Setting.Services;

/// <summary>
/// Loads language packs from data/languages/{code}.json.
/// On first run the file doesn't exist on disk — the C# project copies en.json/cn.json
/// from BrickBot/Languages/ to data/languages/ via the csproj &lt;Content&gt; rule.
/// </summary>
public interface ILanguageService
{
    Task<LanguageSettings?> GetLanguageAsync(string languageCode);
    Task<List<string>> GetAvailableLanguagesAsync();
    Task<bool> LanguageExistsAsync(string languageCode);
    Task SaveLanguageAsync(LanguageSettings language);
}

public sealed class LanguageService : ILanguageService
{
    private readonly string _languagesDirectory;
    private readonly ILogHelper _logger;

    public LanguageService(IGlobalPathService globalPaths, ILogHelper logger)
    {
        _logger = logger;
        _languagesDirectory = globalPaths.LanguagesDirectory;
        Directory.CreateDirectory(_languagesDirectory);
    }

    public async Task<LanguageSettings?> GetLanguageAsync(string languageCode)
    {
        ValidateCode(languageCode);
        var filePath = GetLanguageFilePath(languageCode);
        if (!File.Exists(filePath))
        {
            _logger.Warn($"Language file not found: {languageCode}", "Language");
            return null;
        }

        try
        {
            return await JsonHelper.DeserializeFromFileAsync<LanguageSettings>(filePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load language {languageCode}: {ex.Message}", "Language", ex);
            throw;
        }
    }

    public Task<List<string>> GetAvailableLanguagesAsync()
    {
        try
        {
            var codes = Directory.GetFiles(_languagesDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Cast<string>()
                .ToList();
            return Task.FromResult(codes);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to list languages: {ex.Message}", "Language", ex);
            return Task.FromResult(new List<string>());
        }
    }

    public Task<bool> LanguageExistsAsync(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return Task.FromResult(false);
        if (languageCode.Contains("..") || languageCode.Contains('/') || languageCode.Contains('\\'))
            return Task.FromResult(false);
        return Task.FromResult(File.Exists(GetLanguageFilePath(languageCode)));
    }

    public async Task SaveLanguageAsync(LanguageSettings language)
    {
        if (language is null) throw new ArgumentNullException(nameof(language));
        ValidateCode(language.Code);

        var filePath = GetLanguageFilePath(language.Code);
        await JsonHelper.SerializeToFileAsync(filePath, language).ConfigureAwait(false);
        _logger.Info($"Saved language: {language.Code}", "Language");
    }

    private string GetLanguageFilePath(string languageCode) =>
        Path.Combine(_languagesDirectory, $"{languageCode}.json");

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Language code cannot be empty", nameof(code));
        if (code.Contains("..") || code.Contains('/') || code.Contains('\\'))
            throw new ArgumentException($"Invalid language code: {code}", nameof(code));
    }
}
