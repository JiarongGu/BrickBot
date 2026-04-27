namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Executes a Run: bootstrap host API + stdlib (init.js, combat.js), pre-load every
/// library script (so they can register globals / helper functions / monitors), then
/// run the selected main script. All scripts share one Jint engine, one thread, one
/// <see cref="ScriptContext"/>, and one <see cref="IScriptHost"/> instance.
/// </summary>
public interface IScriptEngine
{
    /// <summary>
    /// Run a top-level main script with the given libraries pre-loaded.
    /// The order is: init.js → combat.js → libraries (alphabetical) → main.
    /// Throws on syntax/runtime errors.
    /// </summary>
    void Execute(ScriptRunRequest run, IScriptHost host, ScriptContext context);
}

public sealed record ScriptRunRequest(
    string MainSource,
    IReadOnlyList<ScriptFile> Libraries);

public sealed record ScriptFile(string Name, string Source);
