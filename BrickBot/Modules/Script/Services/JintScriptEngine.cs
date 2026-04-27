using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Detection.Services;
using BrickBot.Modules.Input.Services;
using BrickBot.Modules.Runner.Services;
using BrickBot.Modules.Template.Services;
using BrickBot.Modules.Vision.Services;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// JavaScript engine backed by Jint (pure C#, no native deps). One engine per Run.
/// Boot order:
///   1. Bind <c>__host</c> = <see cref="HostApi"/> and <c>__ctx</c> = <see cref="ScriptContext"/>.
///   2. Run <see cref="StdLib.InitScript"/> + <see cref="StdLib.CombatScript"/> — establish globals.
///   3. Install <c>require()</c>. <c>require('brickbot')</c> exposes the globals as named exports;
///      every other id resolves to a profile library via <see cref="ScriptRunRequest.LibraryResolver"/>.
///   4. Run the main script as a CommonJS module (wrapped in <c>function (module, exports, require)</c>)
///      so the TypeScript-emitted <c>require()</c> calls and <c>module.exports</c> work.
/// All steps run on the calling thread (Runner provides a dedicated background thread).
/// </summary>
public sealed class JintScriptEngine : IScriptEngine
{
    private readonly IVisionService _vision;
    private readonly ITemplateLoader _templates;
    private readonly IInputService _input;
    private readonly IRunLog _log;
    private readonly IFrameBuffer _frameBuffer;
    private readonly IScriptDispatcher _dispatcher;
    private readonly IDetectionFileService _detectionFiles;
    private readonly IDetectionRunner _detectionRunner;
    private readonly ITemplateFileService _templateFiles;

    public JintScriptEngine(
        IVisionService vision,
        ITemplateLoader templates,
        IInputService input,
        IRunLog log,
        IFrameBuffer frameBuffer,
        IScriptDispatcher dispatcher,
        IDetectionFileService detectionFiles,
        IDetectionRunner detectionRunner,
        ITemplateFileService templateFiles)
    {
        _vision = vision;
        _templates = templates;
        _input = input;
        _log = log;
        _frameBuffer = frameBuffer;
        _dispatcher = dispatcher;
        _detectionFiles = detectionFiles;
        _detectionRunner = detectionRunner;
        _templateFiles = templateFiles;
    }

    public void Execute(ScriptRunRequest run, IScriptHost host, ScriptContext context)
    {
        // Detections are per-Run state — runner reset clears effect baselines so a fresh
        // run starts with no stale baselines from the previous Run.
        _detectionRunner.Reset();
        using var hostApi = new HostApi(
            _vision, _templates, _input, _log, host, _frameBuffer, _dispatcher,
            _detectionFiles, _detectionRunner, _templateFiles);

        var engine = new Engine(options =>
        {
            options.AllowClr();
            // OperationException carries our error code → propagate it untouched so the
            // facade can return ErrorDetails. Everything else gets wrapped into a JS error
            // so user scripts see a normal exception.
            options.CatchClrExceptions(ex => ex is not OperationException && ex is not OperationCanceledException);
            options.Strict();
        });

        engine.SetValue("__host", hostApi);
        engine.SetValue("__ctx", context);

        var moduleCache = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        var loading = new Stack<string>();

        try
        {
            engine.Execute(StdLib.InitScript);
            engine.Execute(StdLib.CombatScript);

            JsValue Require(string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new OperationException("SCRIPT_MODULE_NOT_FOUND",
                        new() { ["module"] = id ?? "(empty)", ["from"] = CurrentlyLoading(loading) });
                }

                if (moduleCache.TryGetValue(id, out var cached)) return cached;

                if (id == "brickbot")
                {
                    var builtin = engine.Evaluate(
                        "({ vision: vision, input: input, combat: combat, ctx: ctx, " +
                        "brickbot: brickbot, detect: detect, log: log, wait: wait, " +
                        "isCancelled: isCancelled, now: now })");
                    moduleCache[id] = builtin;
                    return builtin;
                }

                var libName = NormalizeLibraryId(id);
                if (loading.Contains(libName))
                {
                    // Cyclic dep — return what's been exported so far rather than recurse.
                    return moduleCache.TryGetValue(libName, out var partial) ? partial : JsValue.Undefined;
                }

                var lib = run.LibraryResolver(libName);
                if (lib is null)
                {
                    throw new OperationException("SCRIPT_MODULE_NOT_FOUND",
                        new() { ["module"] = id, ["from"] = CurrentlyLoading(loading) });
                }

                _log.Info($"Loading library: {libName}");
                loading.Push(libName);
                try
                {
                    var exports = ExecuteAsModule(engine, lib.Source);
                    moduleCache[libName] = exports;
                    return exports;
                }
                finally
                {
                    loading.Pop();
                }
            }

            engine.SetValue("require", new Func<string, JsValue>(Require));

            ExecuteAsModule(engine, run.MainSource);
        }
        catch (OperationCanceledException) { throw; }
        catch (OperationException) { throw; }
        catch (JavaScriptException ex)
        {
            throw new OperationException("SCRIPT_RUNTIME_ERROR",
                new() { ["message"] = ex.Message }, ex.Message, ex);
        }
        catch (Exception ex) when (IsParseError(ex))
        {
            throw new OperationException("SCRIPT_SYNTAX_ERROR",
                new() { ["message"] = ex.Message }, ex.Message, ex);
        }
    }

    /// <summary>
    /// Runs <paramref name="source"/> wrapped as a CommonJS module so the TS compiler's
    /// emit (which references <c>module</c>, <c>exports</c>, <c>require</c>) executes
    /// correctly. Returns the resulting <c>module.exports</c> value.
    /// </summary>
    private static JsValue ExecuteAsModule(Engine engine, string source)
    {
        var moduleObj = engine.Evaluate("({ exports: {} })").AsObject();
        var initialExports = moduleObj.Get("exports");
        var requireFn = engine.GetValue("require");

        // Newline before the user's source isolates a leading // comment from the wrapper.
        var wrapper = engine.Evaluate("(function (module, exports, require) {\n" + source + "\n})");
        engine.Invoke(wrapper, moduleObj, initialExports, requireFn);

        // Re-read exports — user code may have done `module.exports = X` to replace it.
        return moduleObj.Get("exports");
    }

    private static string CurrentlyLoading(Stack<string> loading)
        => loading.Count == 0 ? "(main)" : loading.Peek();

    private static bool IsParseError(Exception ex)
    {
        var typeName = ex.GetType().FullName ?? string.Empty;
        return typeName.Contains("Esprima", StringComparison.Ordinal)
            || typeName.Contains("Acornima", StringComparison.Ordinal)
            || ex.GetType().Name.Contains("Parse", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLibraryId(string id)
    {
        var s = id.Trim();
        if (s.StartsWith("./", StringComparison.Ordinal)) s = s[2..];
        else if (s.StartsWith("../", StringComparison.Ordinal)) s = s[3..];

        var slash = s.LastIndexOfAny(['/', '\\']);
        if (slash >= 0) s = s[(slash + 1)..];

        if (s.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) s = s[..^3];
        else if (s.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) s = s[..^3];

        return s;
    }
}
