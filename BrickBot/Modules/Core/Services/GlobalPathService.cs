using BrickBot.Modules.Core.Models;

namespace BrickBot.Modules.Core.Services;

/// <summary>
/// Single source of truth for absolute paths to application directories.
/// All non-profile paths (data, settings, languages, logs) and profile-scoped helpers live here.
/// </summary>
public interface IGlobalPathService
{
    string BaseDataPath { get; }
    string ProfilesDirectory { get; }
    string GlobalSettingsDirectory { get; }
    string GlobalSettingsFilePath { get; }
    string ProfilesConfigPath { get; }
    string LanguagesDirectory { get; }
    string FrontendPath { get; }
    string FrontendIndexPath { get; }
    string LogsDirectory { get; }
    string TempDirectory { get; }

    /// <summary>Idempotent: creates all standard global directories if missing.</summary>
    void EnsureDirectoriesExist();

    /// <summary>Profile root: data/profiles/{profileId}/</summary>
    string GetProfileDirectoryPath(string profileId);

    /// <summary>Profile config file: data/profiles/{profileId}/config.json</summary>
    string GetProfileConfigPath(string profileId);

    /// <summary>Profile temp folder: data/profiles/{profileId}/temp/</summary>
    string GetProfileTempDirectory(string profileId);

    /// <summary>Profile templates: data/profiles/{profileId}/templates/</summary>
    string GetProfileTemplatesDirectory(string profileId);

    /// <summary>Profile scripts: data/profiles/{profileId}/scripts/</summary>
    string GetProfileScriptsDirectory(string profileId);

    /// <summary>Settings file by name (with .json extension).</summary>
    string GetGlobalSettingsFilePath(string settingsFileName);
}

public sealed class GlobalPathService : IGlobalPathService
{
    private readonly IAppEnvironment _environment;

    public GlobalPathService(IAppEnvironment environment)
    {
        _environment = environment;
        EnsureDirectoriesExist();
    }

    public string BaseDataPath => _environment.DataDirectory;
    public string ProfilesDirectory => Path.Combine(BaseDataPath, "profiles");
    public string GlobalSettingsDirectory => Path.Combine(BaseDataPath, "settings");
    public string GlobalSettingsFilePath => Path.Combine(GlobalSettingsDirectory, "global.json");
    public string ProfilesConfigPath => Path.Combine(GlobalSettingsDirectory, "profiles.json");
    public string LanguagesDirectory => Path.Combine(BaseDataPath, "languages");
    public string FrontendPath => Path.Combine(_environment.BaseDirectory, "wwwroot");
    public string FrontendIndexPath => Path.Combine(FrontendPath, "index.html");
    public string LogsDirectory => Path.Combine(BaseDataPath, "logs");
    public string TempDirectory => Path.Combine(BaseDataPath, "temp");

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(BaseDataPath);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(GlobalSettingsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(TempDirectory);
    }

    public string GetProfileDirectoryPath(string profileId) =>
        Path.Combine(ProfilesDirectory, profileId);

    public string GetProfileConfigPath(string profileId) =>
        Path.Combine(GetProfileDirectoryPath(profileId), "config.json");

    public string GetProfileTempDirectory(string profileId) =>
        Path.Combine(GetProfileDirectoryPath(profileId), "temp");

    public string GetProfileTemplatesDirectory(string profileId) =>
        Path.Combine(GetProfileDirectoryPath(profileId), "templates");

    public string GetProfileScriptsDirectory(string profileId) =>
        Path.Combine(GetProfileDirectoryPath(profileId), "scripts");

    public string GetGlobalSettingsFilePath(string settingsFileName) =>
        Path.Combine(GlobalSettingsDirectory, settingsFileName);
}
