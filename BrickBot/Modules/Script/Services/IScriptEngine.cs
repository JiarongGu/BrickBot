namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Executes a Run: bootstrap host API + stdlib (init.js, combat.js), then run the
/// compiled main script as a CommonJS module. Library scripts are no longer pre-loaded;
/// they're resolved lazily through <see cref="ScriptRunRequest.LibraryResolver"/> when
/// the main script (or another library) calls <c>require()</c>. The host module
/// <c>'brickbot'</c> is always available and exposes the <c>vision</c>/<c>input</c>/
/// <c>combat</c>/<c>ctx</c> globals as named exports.
/// </summary>
public interface IScriptEngine
{
    void Execute(ScriptRunRequest run, IScriptHost host, ScriptContext context);
}

/// <summary>
/// What the engine needs to execute a Run.
///   - <see cref="MainSource"/> is the compiled JavaScript of the user's main script.
///   - <see cref="LibraryResolver"/> is invoked once per unique <c>require(id)</c> call,
///     synchronously, returning the compiled JS for that library or null if missing.
/// </summary>
public sealed record ScriptRunRequest(
    string MainSource,
    Func<string, ScriptFile?> LibraryResolver);

public sealed record ScriptFile(string Name, string Source);
