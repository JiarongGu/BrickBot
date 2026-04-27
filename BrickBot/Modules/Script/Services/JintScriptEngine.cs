using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Input.Services;
using BrickBot.Modules.Runner.Services;
using BrickBot.Modules.Vision.Services;
using Jint;
using Jint.Runtime;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// JavaScript engine backed by Jint (pure C#, no native deps). One engine per Run.
/// Boot order:
///   1. Bind <c>__host</c> = <see cref="HostApi"/> and <c>__ctx</c> = <see cref="ScriptContext"/>.
///   2. Run <see cref="StdLib.InitScript"/> — wraps both into the friendly <c>vision</c>/<c>input</c>/
///      <c>log</c>/<c>wait</c>/<c>isCancelled</c>/<c>now</c>/<c>ctx</c> globals.
///   3. Run <see cref="StdLib.CombatScript"/> — exposes behavior-tree primitives via <c>combat.*</c>.
///   4. Run each library script in <c>request.Libraries</c> (alphabetical order). These typically
///      define helpers like <c>updatePerception(ctx)</c>, skill rotations, monitor utilities.
///   5. Run the main script — the orchestrator that ties everything together.
/// All steps run on the calling thread (Runner provides a dedicated background thread).
/// </summary>
public sealed class JintScriptEngine : IScriptEngine
{
    private readonly IVisionService _vision;
    private readonly ITemplateLoader _templates;
    private readonly IInputService _input;
    private readonly IRunLog _log;

    public JintScriptEngine(
        IVisionService vision,
        ITemplateLoader templates,
        IInputService input,
        IRunLog log)
    {
        _vision = vision;
        _templates = templates;
        _input = input;
        _log = log;
    }

    public void Execute(ScriptRunRequest run, IScriptHost host, ScriptContext context)
    {
        var hostApi = new HostApi(_vision, _templates, _input, _log, host);

        var engine = new Engine(options =>
        {
            options.AllowClr();
            options.CatchClrExceptions(_ => true);
            options.Strict();
        });

        engine.SetValue("__host", hostApi);
        engine.SetValue("__ctx", context);

        try
        {
            engine.Execute(StdLib.InitScript);
            engine.Execute(StdLib.CombatScript);

            foreach (var lib in run.Libraries)
            {
                _log.Info($"Loading library: {lib.Name}");
                engine.Execute(lib.Source);
            }

            engine.Execute(run.MainSource);
        }
        catch (OperationCanceledException)
        {
            // Stop button — let the runner show "cancelled".
            throw;
        }
        catch (JavaScriptException ex)
        {
            // Runtime error thrown by the script (TypeError, ReferenceError, etc.).
            throw new OperationException("SCRIPT_RUNTIME_ERROR",
                new() { ["message"] = ex.Message }, ex.Message, ex);
        }
        catch (Exception ex) when (ex.GetType().FullName?.Contains("Esprima", StringComparison.Ordinal) == true
                                || ex.GetType().FullName?.Contains("Acornima", StringComparison.Ordinal) == true
                                || ex.GetType().Name.Contains("Parse", StringComparison.OrdinalIgnoreCase))
        {
            // Parse-time failure surfaced by Jint's parser (Esprima/Acornima depending on version).
            throw new OperationException("SCRIPT_SYNTAX_ERROR",
                new() { ["message"] = ex.Message }, ex.Message, ex);
        }
    }
}
