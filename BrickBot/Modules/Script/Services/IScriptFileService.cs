namespace BrickBot.Modules.Script.Services;

/// <summary>
/// CRUD over the per-profile scripts directory at <c>data/profiles/{id}/scripts/</c>.
/// Two well-known subfolders:
///   - <c>main/</c>   — top-level orchestrator scripts. Runner picks one to execute.
///   - <c>library/</c> — helpers, monitors, skill defs. Pulled in lazily via <c>require()</c>.
/// Each script is stored as a pair: the TypeScript source-of-truth (<c>{name}.ts</c>) plus the
/// frontend-compiled CommonJS output (<c>{name}.js</c>) that the runner actually executes.
/// File names are validated to prevent path traversal.
/// </summary>
public interface IScriptFileService
{
    /// <summary>List every .ts source file across both kinds for the given profile.</summary>
    IReadOnlyList<ScriptFileInfo> List(string profileId);

    /// <summary>Read TypeScript source. Throws SCRIPT_FILE_NOT_FOUND if missing.</summary>
    string ReadSource(string profileId, ScriptKind kind, string name);

    /// <summary>
    /// Persist both the TypeScript source and its frontend-compiled JavaScript output.
    /// The .js sidecar is what the runner executes; the .ts is the user's editable copy.
    /// Returns the .ts absolute path.
    /// </summary>
    string Save(string profileId, ScriptKind kind, string name, string tsSource, string jsSource);

    /// <summary>Remove both .ts and .js for the given script. No-op if neither exists.</summary>
    void Delete(string profileId, ScriptKind kind, string name);

    /// <summary>
    /// Returns the compiled JS source of a main script. Throws SCRIPT_NOT_COMPILED
    /// if the .js sidecar is missing (e.g. the user added a .ts manually without compiling).
    /// </summary>
    string LoadCompiledMain(string profileId, string mainName);

    /// <summary>
    /// Resolves a library reference for the engine's <c>require()</c>. Returns the compiled
    /// JS source paired with its name, or null if no such library exists.
    /// </summary>
    ScriptFile? LoadCompiledLibrary(string profileId, string libraryName);

    /// <summary>
    /// Returns names (without extension) of every library that has a compiled .js sidecar.
    /// Used by the runner to surface "X libraries unavailable" warnings on Start.
    /// </summary>
    IReadOnlyList<string> ListCompiledLibraries(string profileId);
}

public enum ScriptKind
{
    Main,
    Library,
}

public sealed record ScriptFileInfo(ScriptKind Kind, string Name, string Path);
