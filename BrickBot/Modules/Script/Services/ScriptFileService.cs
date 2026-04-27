using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;

namespace BrickBot.Modules.Script.Services;

public sealed class ScriptFileService : IScriptFileService
{
    private const string SourceExt = ".ts";
    private const string CompiledExt = ".js";

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

    public string ReadSource(string profileId, ScriptKind kind, string name)
    {
        var path = ResolvePath(profileId, kind, name, SourceExt, ensureExists: true);
        return File.ReadAllText(path);
    }

    public string Save(string profileId, ScriptKind kind, string name, string tsSource, string jsSource)
    {
        var tsPath = ResolvePath(profileId, kind, name, SourceExt, ensureExists: false);
        var jsPath = ResolvePath(profileId, kind, name, CompiledExt, ensureExists: false);
        var dir = Path.GetDirectoryName(tsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(tsPath, tsSource);
        File.WriteAllText(jsPath, jsSource);
        _logger.Info($"Saved script {kind.ToString().ToLowerInvariant()}/{name} for profile {profileId}", "Script");
        return tsPath;
    }

    public void Delete(string profileId, ScriptKind kind, string name)
    {
        var tsPath = ResolvePath(profileId, kind, name, SourceExt, ensureExists: false);
        var jsPath = ResolvePath(profileId, kind, name, CompiledExt, ensureExists: false);
        var deletedAny = false;
        if (File.Exists(tsPath)) { File.Delete(tsPath); deletedAny = true; }
        if (File.Exists(jsPath)) { File.Delete(jsPath); deletedAny = true; }
        if (deletedAny)
        {
            _logger.Info($"Deleted script {kind.ToString().ToLowerInvariant()}/{name} for profile {profileId}", "Script");
        }
    }

    public string LoadCompiledMain(string profileId, string mainName)
    {
        var jsPath = ResolvePath(profileId, ScriptKind.Main, mainName, CompiledExt, ensureExists: false);
        if (!File.Exists(jsPath))
        {
            throw new OperationException("SCRIPT_NOT_COMPILED",
                new() { ["kind"] = "main", ["name"] = mainName });
        }
        return File.ReadAllText(jsPath);
    }

    public ScriptFile? LoadCompiledLibrary(string profileId, string libraryName)
    {
        ValidateName(libraryName);
        var jsPath = ResolvePath(profileId, ScriptKind.Library, libraryName, CompiledExt, ensureExists: false);
        if (!File.Exists(jsPath)) return null;
        return new ScriptFile(libraryName, File.ReadAllText(jsPath));
    }

    public IReadOnlyList<string> ListCompiledLibraries(string profileId)
    {
        var dir = KindDirectory(profileId, ScriptKind.Library);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, $"*{CompiledExt}")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolvePath(string profileId, ScriptKind kind, string name, string ext, bool ensureExists)
    {
        ValidateName(name);
        var bare = StripKnownExtensions(name);
        var fileName = $"{bare}{ext}";
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

        return Directory.EnumerateFiles(dir, $"*{SourceExt}")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => new ScriptFileInfo(kind, Path.GetFileNameWithoutExtension(p), p))
            .ToList();
    }

    private static string StripKnownExtensions(string name)
    {
        if (name.EndsWith(SourceExt, StringComparison.OrdinalIgnoreCase)) return name[..^SourceExt.Length];
        if (name.EndsWith(CompiledExt, StringComparison.OrdinalIgnoreCase)) return name[..^CompiledExt.Length];
        return name;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new OperationException("SCRIPT_NAME_REQUIRED");

        var invalid = Path.GetInvalidFileNameChars();
        var bare = StripKnownExtensions(name);
        if (bare.Any(c => invalid.Contains(c)) ||
            bare.Contains("..") || bare.Contains('/') || bare.Contains('\\'))
        {
            throw new OperationException("SCRIPT_INVALID_NAME", new() { ["name"] = name });
        }
    }
}
