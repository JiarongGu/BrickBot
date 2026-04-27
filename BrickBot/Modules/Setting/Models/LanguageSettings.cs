namespace BrickBot.Modules.Setting.Models;

/// <summary>
/// A single language pack loaded from data/languages/{code}.json.
/// </summary>
public sealed class LanguageSettings
{
    /// <summary>Language code: "en", "cn", etc.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable name: "English", "中文".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Flat key→string translations. Frontend uses dotted keys (e.g. "settings.theme.label").</summary>
    public Dictionary<string, string> Translations { get; set; } = new();
}
