using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// CRUD over the per-profile scripts directory at <c>data/profiles/{id}/scripts/</c>.
/// Two well-known subfolders:
///   - <c>main/</c>   — top-level orchestrator scripts. Runner picks one to execute.
///   - <c>library/</c> — helpers, monitors, skill defs. Pre-loaded into the engine before main.
/// File names are validated to prevent path traversal.
/// </summary>
public interface IScriptFileService
{
    /// <summary>List every .js file across both kinds for the given profile.</summary>
    IReadOnlyList<ScriptFileInfo> List(string profileId);

    /// <summary>Read script source. Throws SCRIPT_FILE_NOT_FOUND if missing.</summary>
    string Read(string profileId, ScriptKind kind, string name);

    /// <summary>Create or overwrite a script file. Returns the resulting absolute path.</summary>
    string Save(string profileId, ScriptKind kind, string name, string source);

    /// <summary>Remove a script file. No-op if it doesn't exist.</summary>
    void Delete(string profileId, ScriptKind kind, string name);

    /// <summary>Returns sources of every library script, alphabetical by name.</summary>
    IReadOnlyList<ScriptFile> LoadLibraries(string profileId);

    /// <summary>Returns the source of a specific main script. Throws if missing.</summary>
    string LoadMain(string profileId, string mainName);
}

public enum ScriptKind
{
    Main,
    Library,
}

public sealed record ScriptFileInfo(ScriptKind Kind, string Name, string Path);

public sealed class ScriptFileService : IScriptFileService
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;

    public ScriptFileService(IGlobalPathService globalPaths, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public IReadOnlyList<ScriptFileInfo> List(string profileId)
    {
        var result = new List<ScriptFileInfo>();
        result.AddRange(EnumerateKind(profileId, ScriptKind.Main));
        result.AddRange(EnumerateKind(profileId, ScriptKind.Library));
        return result;
    }

    public string Read(string profileId, ScriptKind kind, string name)
    {
        var path = ResolvePath(profileId, kind, name, ensureExists: true);
        return File.ReadAllText(path);
    }

    public string Save(string profileId, ScriptKind kind, string name, string source)
    {
        var path = ResolvePath(profileId, kind, name, ensureExists: false);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, source);
        _logger.Info($"Saved script {kind.ToString().ToLowerInvariant()}/{name} for profile {profileId}", "Script");
        return path;
    }

    public void Delete(string profileId, ScriptKind kind, string name)
    {
        var path = ResolvePath(profileId, kind, name, ensureExists: false);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.Info($"Deleted script {kind.ToString().ToLowerInvariant()}/{name} for profile {profileId}", "Script");
        }
    }

    public IReadOnlyList<ScriptFile> LoadLibraries(string profileId)
    {
        var dir = KindDirectory(profileId, ScriptKind.Library);
        if (!Directory.Exists(dir)) return Array.Empty<ScriptFile>();

        return Directory.EnumerateFiles(dir, "*.js")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => new ScriptFile(Path.GetFileNameWithoutExtension(p), File.ReadAllText(p)))
            .ToList();
    }

    public string LoadMain(string profileId, string mainName)
    {
        var path = ResolvePath(profileId, ScriptKind.Main, mainName, ensureExists: true);
        return File.ReadAllText(path);
    }

    private string ResolvePath(string profileId, ScriptKind kind, string name, bool ensureExists)
    {
        ValidateName(name);
        var fileName = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.js";
        var path = Path.Combine(KindDirectory(profileId, kind), fileName);

        if (ensureExists && !File.Exists(path))
        {
            throw new OperationException("SCRIPT_FILE_NOT_FOUND",
                new() { ["kind"] = kind.ToString().ToLowerInvariant(), ["name"] = name });
        }
        return path;
    }

    private string KindDirectory(string profileId, ScriptKind kind) =>
        Path.Combine(_globalPaths.GetProfileScriptsDirectory(profileId),
            kind == ScriptKind.Main ? "main" : "library");

    private IEnumerable<ScriptFileInfo> EnumerateKind(string profileId, ScriptKind kind)
    {
        var dir = KindDirectory(profileId, kind);
        if (!Directory.Exists(dir)) return Array.Empty<ScriptFileInfo>();

        return Directory.EnumerateFiles(dir, "*.js")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => new ScriptFileInfo(kind, Path.GetFileNameWithoutExtension(p), p))
            .ToList();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new OperationException("SCRIPT_NAME_REQUIRED");

        var invalid = Path.GetInvalidFileNameChars();
        if (name.Any(c => invalid.Contains(c)) ||
            name.Contains("..") || name.Contains('/') || name.Contains('\\'))
        {
            throw new OperationException("SCRIPT_INVALID_NAME", new() { ["name"] = name });
        }
    }
}
